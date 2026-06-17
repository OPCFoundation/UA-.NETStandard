/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Client identity provider for X.509 user-certificate activation.
    /// </summary>
    /// <remarks>
    /// The user certificate is loaded lazily through
    /// <see cref="ICertificateProvider.GetPrivateKeyCertificateAsync"/>
    /// once and its
    /// <see cref="CertificateKeyAlgorithm"/> cached so subsequent
    /// <see cref="CanSatisfyAsync"/> calls can pick the user-token
    /// policy whose
    /// <see cref="UserTokenPolicy.SecurityPolicyUri"/> matches the
    /// public-key algorithm of the certificate (RSA / NistP256 /
    /// NistP384 / Brainpool / Curve25519 / Curve448).
    /// </remarks>
    public sealed class X509ClientIdentityProvider : IClientIdentityProvider
    {
        /// <summary>
        /// Creates an X.509 client identity provider.
        /// </summary>
        public X509ClientIdentityProvider(
            CertificateIdentifier certificateId,
            ICertificatePasswordProvider passwordProvider,
            ICertificateProvider certificateProvider)
        {
            m_certificateId = certificateId ?? throw new ArgumentNullException(nameof(certificateId));
            m_passwordProvider = passwordProvider ?? throw new ArgumentNullException(nameof(passwordProvider));
            m_certificateProvider = certificateProvider ?? throw new ArgumentNullException(nameof(certificateProvider));
        }

        /// <inheritdoc/>
        public IReadOnlyList<UserTokenType> SupportedTokenTypes { get; }
            = [UserTokenType.Certificate];

        /// <inheritdoc/>
        public IReadOnlyList<string> SupportedIssuedTokenProfileUris { get; }
            = [];

        /// <inheritdoc/>
        public DateTime ExpiresAt => DateTime.MaxValue;

        /// <inheritdoc/>
        public async ValueTask<CanSatisfyResult> CanSatisfyAsync(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            if (policy == null)
            {
                return CanSatisfyResult.No("Policy is null.");
            }
            if (policy.TokenType != UserTokenType.Certificate)
            {
                return CanSatisfyResult.No(
                    $"TokenTypeNotSupported (provider handles Certificate, policy is {policy.TokenType}).");
            }

            CertificateKeyAlgorithm? certAlgorithm = await ResolveCertificateAlgorithmAsync(ct)
                .ConfigureAwait(false);
            if (certAlgorithm == null)
            {
                string subject = m_certificateId.SubjectName ?? "<unknown subject>";
                string thumbprint = m_certificateId.Thumbprint ?? "<unknown thumbprint>";
                return CanSatisfyResult.No(
                    $"CertificateLoadFailed (could not resolve user certificate Subject='{subject}', Thumbprint='{thumbprint}').");
            }

            string effectivePolicyUri = !string.IsNullOrEmpty(policy.SecurityPolicyUri)
                ? policy.SecurityPolicyUri!
                : context.EndpointDescription?.SecurityPolicyUri ?? SecurityPolicies.None;

            if (string.Equals(effectivePolicyUri, SecurityPolicies.None, StringComparison.Ordinal))
            {
                return CanSatisfyResult.Yes;
            }

            SecurityPolicyInfo? info = SecurityPolicies.GetInfo(effectivePolicyUri);
            if (info == null)
            {
                return CanSatisfyResult.No(
                    $"UnknownOrUnsupportedSecurityPolicy (policy '{effectivePolicyUri}' is not recognized or not supported on this platform).");
            }

            CertificateKeyAlgorithm policyAlgorithm = info.CertificateKeyAlgorithm;

            if (policyAlgorithm == CertificateKeyAlgorithm.None)
            {
                return CanSatisfyResult.Yes;
            }

            if (policyAlgorithm != certAlgorithm.Value)
            {
                return CanSatisfyResult.No(
                    $"CertificateAlgorithmMismatch (cert algorithm {certAlgorithm.Value}, policy '{effectivePolicyUri}' expects {policyAlgorithm}).");
            }

            return CanSatisfyResult.Yes;
        }

        /// <inheritdoc/>
        public async ValueTask<IUserIdentity> GetIdentityAsync(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            CanSatisfyResult satisfied = await CanSatisfyAsync(policy, context, ct)
                .ConfigureAwait(false);
            if (!satisfied.CanSatisfy)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenRejected,
                    "X.509 identity provider cannot satisfy the supplied user token policy: {0}",
                    satisfied.RejectionReason ?? string.Empty);
            }

            UserIdentity identity = await UserIdentity
                .CreateAsync(m_certificateId, m_passwordProvider, m_certificateProvider, ct)
                .ConfigureAwait(false);
            identity.PolicyId = policy.PolicyId ?? string.Empty;
            return identity;
        }

        /// <summary>
        /// Resolves and caches the
        /// <see cref="CertificateKeyAlgorithm"/> of the user
        /// certificate. Returns <see langword="null"/> if the
        /// certificate cannot be loaded; load failures are NOT
        /// cached, so transient errors (store offline, cert not yet
        /// provisioned, rotated identifier) are retried on the next
        /// call.
        /// </summary>
        private async ValueTask<CertificateKeyAlgorithm?> ResolveCertificateAlgorithmAsync(
            CancellationToken ct)
        {
            CachedAlgorithm? cached = Volatile.Read(ref m_cached);
            if (cached != null)
            {
                return cached.Algorithm;
            }

            Certificate? certificate = await m_certificateProvider
                .GetPrivateKeyCertificateAsync(m_certificateId, m_passwordProvider, null, ct)
                .ConfigureAwait(false);
            if (certificate == null)
            {
                return null;
            }

            try
            {
                CertificateKeyAlgorithm algorithm = ResolveAlgorithm(certificate);
                Interlocked.CompareExchange(
                    ref m_cached,
                    new CachedAlgorithm(algorithm),
                    null);
                return algorithm;
            }
            finally
            {
                certificate.Dispose();
            }
        }

        /// <summary>
        /// Inspects the public key of <paramref name="certificate"/>
        /// and maps it to a <see cref="CertificateKeyAlgorithm"/>.
        /// Falls back to <see cref="CertificateKeyAlgorithm.None"/>
        /// for unrecognized keys (e.g. ed25519/ed448 represented in
        /// platform-specific encodings).
        /// </summary>
        private static CertificateKeyAlgorithm ResolveAlgorithm(Certificate certificate)
        {
            string keyAlgorithm = certificate.GetKeyAlgorithm();
            if (keyAlgorithm == Oids.Rsa)
            {
                return CertificateKeyAlgorithm.RSA;
            }

            if (keyAlgorithm == Oids.ECPublicKey)
            {
                PublicKey encodedPublicKey = certificate.PublicKey;
                if (encodedPublicKey.EncodedParameters?.RawData is byte[] rawData)
                {
                    switch (BitConverter.ToString(rawData))
                    {
                        case CryptoUtils.NistP256KeyParameters:
                            return CertificateKeyAlgorithm.NistP256;
                        case CryptoUtils.NistP384KeyParameters:
                            return CertificateKeyAlgorithm.NistP384;
                        case CryptoUtils.BrainpoolP256r1KeyParameters:
                            return CertificateKeyAlgorithm.BrainpoolP256r1;
                        case CryptoUtils.BrainpoolP384r1KeyParameters:
                            return CertificateKeyAlgorithm.BrainpoolP384r1;
                    }
                }
            }

            return CertificateKeyAlgorithm.None;
        }

        private readonly CertificateIdentifier m_certificateId;
        private readonly ICertificatePasswordProvider m_passwordProvider;
        private readonly ICertificateProvider m_certificateProvider;
        private CachedAlgorithm? m_cached;

        private sealed class CachedAlgorithm
        {
            public CachedAlgorithm(CertificateKeyAlgorithm algorithm)
            {
                Algorithm = algorithm;
            }

            public CertificateKeyAlgorithm Algorithm { get; }
        }
    }
}
