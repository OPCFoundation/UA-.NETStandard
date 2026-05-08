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
    public sealed class X509IdentityTokenHandler : IUserIdentityTokenHandler
    {
        /// <summary>
        /// Create a new X509IdentityTokenHandler
        /// </summary>
        public X509IdentityTokenHandler(X509IdentityToken token)
        {
            m_token = token;

            if (!m_token.CertificateData.IsEmpty)
            {
                m_certificate = Certificate.FromRawData(m_token.CertificateData);
            }

            m_ownsCertificate = true;
        }

        /// <summary>
        /// Create a identity token from X509 certificate
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="certificate"/> is <c>null</c>.
        /// </exception>
        public X509IdentityTokenHandler(Certificate certificate)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            if (!certificate.HasPrivateKey)
            {
                throw new ServiceResultException(
                    "Cannot create User Identity with Certificate that does not have a private key");
            }

            Certificate = certificate;
            m_ownsCertificate = true;
            m_token = new X509IdentityToken
            {
                CertificateData = certificate.RawData.ToByteString()
            };
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
        /// <para>
        /// Use this overload when the user identity outlives a single
        /// signing operation (typical OPC UA client session) — the
        /// handler stays a POCO and lifetime questions move to the
        /// provider's cache. The provider's
        /// <see cref="ICertificateProvider.TryGetPrivateKeyCertificate"/>
        /// fast-path is consulted on every signing call so a warm cache
        /// completes synchronously.
        /// </para>
        /// <para>
        /// The wire-format <see cref="X509IdentityToken.CertificateData"/>
        /// payload (public-key DER) is loaded eagerly during
        /// construction so it is ready for the
        /// <c>ActivateSession</c> request without a registry round-trip.
        /// </para>
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
            m_ownsCertificate = false;

            // Pre-load the public-key bytes for the wire payload. This
            // is the only blocking step at construction; signing itself
            // is async.
            using Certificate resolved = certificateProvider
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
        /// Private constructor for <see cref="Clone"/>. The cloned handler
        /// shares the certificate reference but does not own it, so it will
        /// not dispose the certificate. This is necessary because the
        /// certificate's private key may reside in protected storage and
        /// cannot be deep-copied.
        /// </summary>
        private X509IdentityTokenHandler(
            X509IdentityToken token,
            Certificate certificate,
            CertificateIdentifier identifier,
            ICertificatePasswordProvider passwordProvider,
            ICertificateProvider provider)
        {
            m_token = token;
            m_certificate = certificate;
            m_ownsCertificate = false;
            m_identifier = identifier;
            m_passwordProvider = passwordProvider;
            m_provider = provider;
        }

        /// <summary>
        /// The certificate associated with the token.
        /// </summary>
        public Certificate Certificate
        {
            get
            {
                if (m_certificate == null && !m_token.CertificateData.IsEmpty)
                {
                    m_certificate = Certificate.FromRawData(m_token.CertificateData);
                }
                return m_certificate;
            }
            set => m_certificate = value;
        }

        /// <inheritdoc/>
        public UserIdentityToken Token => m_token;

        /// <inheritdoc/>
        public string DisplayName => Certificate.Subject;

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
            Nonce receiverEphemeralKey = null,
            Certificate senderCertificate = null,
            CertificateCollection senderIssuerCertificates = null,
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
            Nonce ephemeralKey = null,
            Certificate senderCertificate = null,
            CertificateCollection senderIssuerCertificates = null,
            ICertificateValidatorEx validator = null,
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
            SecurityPolicyInfo info = SecurityPolicies.GetInfo(securityPolicyUri);

            // Fast path: if the handler was constructed with a
            // CertificateIdentifier + provider, resolve via the cache.
            // The provider AddRef's; we own the returned reference.
            if (m_provider != null && m_identifier != null)
            {
                Certificate cached = m_provider.TryGetPrivateKeyCertificate(m_identifier.Thumbprint);
                if (cached != null)
                {
                    using (cached)
                    {
                        return SecurityPolicies.CreateSignatureData(info, cached, dataToSign);
                    }
                }

                using Certificate loaded = await m_provider
                    .GetPrivateKeyCertificateAsync(m_identifier, m_passwordProvider, applicationUri: null, ct)
                    .ConfigureAwait(false);
                if (loaded == null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadIdentityTokenInvalid,
                        "Cannot resolve private-key certificate for X509 identity token.");
                }

                return SecurityPolicies.CreateSignatureData(info, loaded, dataToSign);
            }

            // Legacy path: handler holds (or can reconstruct) the
            // certificate directly.
            await Task.CompletedTask.ConfigureAwait(false);

            Certificate ownedCert = null;
            Certificate certificate = Certificate ??
                (ownedCert = Certificate.FromRawData(m_token.CertificateData));

            try
            {
                return SecurityPolicies.CreateSignatureData(
                    info,
                    certificate,
                    dataToSign);
            }
            finally
            {
                ownedCert?.Dispose();
            }
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
                SecurityPolicyInfo info = SecurityPolicies.GetInfo(securityPolicyUri);
                Certificate ownedCert = null;
                Certificate certificate = Certificate ??
                    (ownedCert = Certificate.FromRawData(m_token.CertificateData));

                try
                {
                    return SecurityPolicies.VerifySignatureData(
                        signatureData,
                        info,
                        certificate,
                        dataToVerify);
                }
                finally
                {
                    ownedCert?.Dispose();
                }
            }
            catch (Exception e)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenInvalid,
                    e,
                    "Could not verify user signature!");
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_ownsCertificate && m_certificate != null)
            {
                m_certificate.Dispose();
            }
            m_certificate = null;
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return new X509IdentityTokenHandler(
                CoreUtils.Clone(m_token),
                m_certificate,
                m_identifier,
                m_passwordProvider,
                m_provider);
        }

        /// <inheritdoc/>
        public bool Equals(IUserIdentityTokenHandler other)
        {
            if (other is not X509IdentityTokenHandler tokenHandler)
            {
                return false;
            }
            return Utils.IsEqual(m_token.CertificateData, tokenHandler.m_token.CertificateData);
        }

        private readonly X509IdentityToken m_token;
        private readonly bool m_ownsCertificate;
        private readonly CertificateIdentifier m_identifier;
        private readonly ICertificatePasswordProvider m_passwordProvider;
        private readonly ICertificateProvider m_provider;
        private Certificate m_certificate;
    }
}
