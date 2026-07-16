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

            CachedCertInfo? certInfo = await ResolveCertificateInfoAsync(ct)
                .ConfigureAwait(false);
            if (certInfo == null)
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

            if (policyAlgorithm != certInfo.Algorithm)
            {
                return CanSatisfyResult.No(
                    $"CertificateAlgorithmMismatch (cert algorithm {certInfo.Algorithm}, policy '{effectivePolicyUri}' expects {policyAlgorithm}).");
            }

            // RSA key-length gate: a user cert must fall within the
            // [MinAsymmetricKeyLength..MaxAsymmetricKeyLength] window
            // declared by the policy, otherwise the server will reject
            // the token at ActivateSession time.
            if (policyAlgorithm == CertificateKeyAlgorithm.RSA &&
                info.MinAsymmetricKeyLength > 0)
            {
                int bits = certInfo.RsaKeySize;
                if (bits < info.MinAsymmetricKeyLength ||
                    (info.MaxAsymmetricKeyLength > 0 && bits > info.MaxAsymmetricKeyLength))
                {
                    return CanSatisfyResult.No(
                        $"CertificateKeyLengthMismatch (user cert is {bits}-bit RSA, policy '{effectivePolicyUri}' requires [{info.MinAsymmetricKeyLength}..{info.MaxAsymmetricKeyLength}]).");
                }
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
        /// <see cref="CertificateKeyAlgorithm"/> and RSA key length of
        /// the user certificate. Returns <see langword="null"/> if the
        /// certificate cannot be loaded; load failures are NOT cached,
        /// so transient errors (store offline, cert not yet
        /// provisioned, rotated identifier) are retried on the next
        /// call.
        /// </summary>
        private async ValueTask<CachedCertInfo?> ResolveCertificateInfoAsync(
            CancellationToken ct)
        {
            CachedCertInfo? cached = Volatile.Read(ref m_cached);
            if (cached != null)
            {
                return cached;
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
                CertificateKeyAlgorithm algorithm = CryptoUtils.GetCertificateKeyAlgorithm(certificate);
                int rsaKeySize = algorithm == CertificateKeyAlgorithm.RSA
                    ? CryptoUtils.GetRsaPublicKeySize(certificate)
                    : 0;
                var info = new CachedCertInfo(algorithm, rsaKeySize);
                Interlocked.CompareExchange(ref m_cached, info, null);
                return info;
            }
            finally
            {
                certificate.Dispose();
            }
        }

        private readonly CertificateIdentifier m_certificateId;
        private readonly ICertificatePasswordProvider m_passwordProvider;
        private readonly ICertificateProvider m_certificateProvider;
        private CachedCertInfo? m_cached;

        private sealed class CachedCertInfo
        {
            public CachedCertInfo(CertificateKeyAlgorithm algorithm, int rsaKeySize)
            {
                Algorithm = algorithm;
                RsaKeySize = rsaKeySize;
            }

            public CertificateKeyAlgorithm Algorithm { get; }

            public int RsaKeySize { get; }
        }
    }
}
