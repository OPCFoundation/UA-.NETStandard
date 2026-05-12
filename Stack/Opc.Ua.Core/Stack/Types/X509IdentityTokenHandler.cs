/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// The X509IdentityTokenHandler class.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler holds no live <see cref="Certificate"/> reference.
    /// The wire payload (public-key DER) is carried in the underlying
    /// <see cref="X509IdentityToken.CertificateData"/>; the private-key
    /// signing certificate is resolved on demand by
    /// <see cref="ICertificateProvider"/> inside <see cref="SignAsync"/>.
    /// </para>
    /// <para>
    /// Server-side construction (from a wire-format
    /// <see cref="X509IdentityToken"/>) supports verification only —
    /// <see cref="SignAsync"/> requires a configured provider and
    /// <see cref="CertificateIdentifier"/>.
    /// </para>
    /// </remarks>
    public sealed class X509IdentityTokenHandler : IUserIdentityTokenHandler
    {
        /// <summary>
        /// Create a new X509IdentityTokenHandler from an inbound wire
        /// payload. The handler can verify signatures using the public
        /// key carried in <see cref="X509IdentityToken.CertificateData"/>
        /// but cannot sign (no private key available).
        /// </summary>
        public X509IdentityTokenHandler(X509IdentityToken token)
        {
            m_token = token ?? throw new ArgumentNullException(nameof(token));
        }

        /// <summary>
        /// Create an identity token handler from a
        /// <see cref="CertificateIdentifier"/> + cache-aware
        /// <see cref="ICertificateProvider"/> pair. The handler holds
        /// no live certificate reference; the certificate is resolved
        /// on demand inside <see cref="SignAsync"/> and disposed at the
        /// end of the call.
        /// </summary>
        /// <remarks>
        /// The wire-format <see cref="X509IdentityToken.CertificateData"/>
        /// payload (public-key DER) is loaded eagerly during
        /// construction so it is ready for the
        /// <c>ActivateSession</c> request without a registry round-trip.
        /// </remarks>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="ServiceResultException"/>
        public X509IdentityTokenHandler(
            CertificateIdentifier identifier,
            ICertificatePasswordProvider passwordProvider,
            ICertificateProvider certificateProvider)
        {
            m_identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));
            m_passwordProvider = passwordProvider ?? throw new ArgumentNullException(nameof(passwordProvider));
            m_provider = certificateProvider ?? throw new ArgumentNullException(nameof(certificateProvider));

            // Pre-load the public-key bytes for the wire payload. The
            // resolved Certificate is disposed immediately; the handler
            // never holds a live reference past this constructor.
            using Certificate? resolved = certificateProvider
                .GetPrivateKeyCertificateAsync(identifier, passwordProvider)
                .AsTask()
                .GetAwaiter()
                .GetResult();

            if (resolved == null || !resolved.HasPrivateKey)
            {
                throw new ServiceResultException(
                    StatusCodes.BadIdentityTokenInvalid,
                    "Cannot resolve a private-key certificate from the supplied CertificateIdentifier.");
            }

            m_token = new X509IdentityToken
            {
                CertificateData = resolved.RawData.ToByteString()
            };
        }

        /// <summary>
        /// Private constructor for <see cref="Clone"/>. Both the
        /// identifier-based and wire-payload paths copy the underlying
        /// <see cref="X509IdentityToken"/> and propagate the provider
        /// references; no live certificate reference is shared.
        /// </summary>
        private X509IdentityTokenHandler(
            X509IdentityToken token,
            CertificateIdentifier? identifier,
            ICertificatePasswordProvider? passwordProvider,
            ICertificateProvider? provider)
        {
            m_token = token;
            m_identifier = identifier;
            m_passwordProvider = passwordProvider;
            m_provider = provider;
        }

        /// <inheritdoc/>
        public UserIdentityToken Token => m_token;

        /// <inheritdoc/>
        public string DisplayName
        {
            get
            {
                using Certificate? cert = MaterialiseTokenCertificate();
                return cert?.Subject ?? string.Empty;
            }
        }

        /// <inheritdoc/>
        public UserTokenType TokenType => UserTokenType.Certificate;

        /// <inheritdoc/>
        public void UpdatePolicy(UserTokenPolicy userTokenPolicy)
        {
            m_token.PolicyId = userTokenPolicy.PolicyId;
        }

        /// <inheritdoc/>
        public ValueTask EncryptAsync(
            Certificate receiverCertificate,
            byte[] receiverNonce,
            string securityPolicyUri,
            IServiceMessageContext context,
            Nonce? receiverEphemeralKey = null,
            Certificate? senderCertificate = null,
            CertificateCollection? senderIssuerCertificates = null,
            bool doNotEncodeSenderCertificate = false,
            CancellationToken ct = default)
        {
            return default;
        }

        /// <inheritdoc/>
        public ValueTask DecryptAsync(
            Certificate certificate,
            Nonce receiverNonce,
            string securityPolicyUri,
            IServiceMessageContext context,
            Nonce? ephemeralKey = null,
            Certificate? senderCertificate = null,
            CertificateCollection? senderIssuerCertificates = null,
            ICertificateValidatorEx? validator = null,
            CancellationToken ct = default)
        {
            return default;
        }

        /// <inheritdoc/>
        public async ValueTask<SignatureData> SignAsync(
            byte[] dataToSign,
            string securityPolicyUri,
            CancellationToken ct = default)
        {
            SecurityPolicyInfo info = SecurityPolicies.GetInfo(securityPolicyUri) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unsupported security policy: {0}",
                    securityPolicyUri);

            if (m_provider == null || m_identifier == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadIdentityTokenInvalid,
                    "X509IdentityTokenHandler must be constructed with a CertificateIdentifier + ICertificateProvider to sign.");
            }

            // Fast path: synchronous cache hit. The provider AddRef's;
            // we own and dispose for the duration of the signing call.
            Certificate? cached = m_provider.TryGetPrivateKeyCertificate(m_identifier.Thumbprint!);
            if (cached != null)
            {
                using (cached)
                {
                    return SecurityPolicies.CreateSignatureData(info, cached, dataToSign);
                }
            }

            // Cold path: async load through the registry/store.
            using Certificate loaded = await m_provider
                .GetPrivateKeyCertificateAsync(m_identifier, m_passwordProvider!, applicationUri: null, ct)
                .ConfigureAwait(false) ??
                throw new ServiceResultException(
                    StatusCodes.BadIdentityTokenInvalid,
                    "Cannot resolve private-key certificate for X509 identity token.");

            return SecurityPolicies.CreateSignatureData(info, loaded, dataToSign);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> VerifyAsync(
            byte[] dataToVerify,
            SignatureData signatureData,
            string securityPolicyUri,
            CancellationToken ct = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            try
            {
                SecurityPolicyInfo info = SecurityPolicies.GetInfo(securityPolicyUri) ??
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityPolicyRejected,
                        "Unsupported security policy: {0}",
                        securityPolicyUri);
                using Certificate cert = MaterialiseTokenCertificate() ??
                    throw new ServiceResultException(
                        StatusCodes.BadIdentityTokenInvalid,
                        "X509IdentityToken has no certificate data to verify against.");
                return SecurityPolicies.VerifySignatureData(
                    signatureData,
                    info,
                    cert,
                    dataToVerify);
            }
            catch (Exception e) when (e is not ServiceResultException)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenInvalid,
                    e,
                    "Could not verify user signature!");
            }
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return new X509IdentityTokenHandler(
                CoreUtils.Clone(m_token)!,
                m_identifier,
                m_passwordProvider,
                m_provider);
        }

        /// <inheritdoc/>
        public bool Equals(IUserIdentityTokenHandler? other)
        {
            if (other is not X509IdentityTokenHandler tokenHandler)
            {
                return false;
            }
            return Utils.IsEqual(m_token.CertificateData, tokenHandler.m_token.CertificateData);
        }

        /// <summary>
        /// Materialises a public-key <see cref="Certificate"/> from the
        /// wire-format <see cref="X509IdentityToken.CertificateData"/>
        /// payload, or <c>null</c> when the payload is empty. The
        /// returned reference is owned by the caller (callers must
        /// dispose).
        /// </summary>
        private Certificate? MaterialiseTokenCertificate()
        {
            return m_token.CertificateData.IsEmpty
                ? null
                : Certificate.FromRawData(m_token.CertificateData);
        }

        private readonly X509IdentityToken m_token;
        private readonly CertificateIdentifier? m_identifier;
        private readonly ICertificatePasswordProvider? m_passwordProvider;
        private readonly ICertificateProvider? m_provider;
    }
}
