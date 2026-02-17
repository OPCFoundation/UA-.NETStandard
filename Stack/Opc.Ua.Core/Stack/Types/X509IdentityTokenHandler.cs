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
using System.Security.Cryptography.X509Certificates;

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
        }

        /// <summary>
        /// Create a identity token from X509 certificate
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="certificate"/> is <c>null</c>.
        /// </exception>
        public X509IdentityTokenHandler(X509Certificate2 certificate)
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
            m_token = new X509IdentityToken
            {
                CertificateData = certificate.RawData.ToByteString()
            };
        }

        /// <summary>
        /// The certificate associated with the token.
        /// </summary>
        public X509Certificate2 Certificate
        {
            get
            {
                if (m_certificate == null && !m_token.CertificateData.IsEmpty)
                {
                    m_certificate = CertificateFactory.Create(m_token.CertificateData);
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
        public void Encrypt(
            X509Certificate2 receiverCertificate,
            byte[] receiverNonce,
            string securityPolicyUri,
            IServiceMessageContext context,
            Nonce receiverEphemeralKey = null,
            X509Certificate2 senderCertificate = null,
            X509Certificate2Collection senderIssuerCertificates = null,
            bool doNotEncodeSenderCertificate = false)
        {
        }

        /// <inheritdoc/>
        public void Decrypt(
            X509Certificate2 certificate,
            Nonce receiverNonce,
            string securityPolicyUri,
            IServiceMessageContext context,
            Nonce ephemeralKey = null,
            X509Certificate2 senderCertificate = null,
            X509Certificate2Collection senderIssuerCertificates = null,
            CertificateValidator validator = null)
        {
        }

        /// <inheritdoc/>
        public SignatureData Sign(
            byte[] dataToSign,
            string securityPolicyUri)
        {
            X509Certificate2 certificate = Certificate ??
                CertificateFactory.Create(m_token.CertificateData);

            SignatureData signatureData = SecurityPolicies.Sign(
                certificate,
                securityPolicyUri,
                dataToSign);

            m_token.CertificateData = certificate.RawData.ToByteString();

            return signatureData;
        }

        /// <inheritdoc/>
        public bool Verify(
            byte[] dataToVerify,
            SignatureData signatureData,
            string securityPolicyUri)
        {
            try
            {
                X509Certificate2 certificate = Certificate ??
                    CertificateFactory.Create(m_token.CertificateData);

                bool valid = SecurityPolicies.Verify(
                    certificate,
                    securityPolicyUri,
                    dataToVerify,
                    signatureData);

                m_token.CertificateData = certificate.RawData.ToByteString();

                return valid;
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
            // TODOL Utils.SilentDispose(m_certificate);
            m_certificate = null;
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return new X509IdentityTokenHandler(Utils.Clone(m_token))
            {
                // TODO: m_certificate = m_certificate
            };
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
        private X509Certificate2 m_certificate;
    }
}
