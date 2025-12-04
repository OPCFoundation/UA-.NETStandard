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
using System.Runtime.Serialization;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// The UserIdentityToken class.
    /// </summary>
    public partial class UserNameIdentityToken
    {
        /// <summary>
        /// The decrypted password associated with the token.
        /// </summary>
        [IgnoreDataMember]
        public byte[] DecryptedPassword { get; set; }

        /// <summary>
        /// Encrypts the DecryptedPassword using the EncryptionAlgorithm and places the result in Password
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public override void Encrypt(
            X509Certificate2 receiverCertificate,
            byte[] receiverNonce,
            string securityPolicyUri,
            IServiceMessageContext context,
            Nonce receiverEphemeralKey = null,
            X509Certificate2 senderCertificate = null,
            X509Certificate2Collection senderIssuerCertificates = null,
            bool doNotEncodeSenderCertificate = false)
        {
            if (DecryptedPassword == null)
            {
                m_password = null;
                return;
            }

            // handle no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri) ||
                securityPolicyUri == SecurityPolicies.None)
            {
                m_password = DecryptedPassword;
                m_encryptionAlgorithm = null;
                return;
            }

            // handle RSA encryption.
            if (!EccUtils.IsEccPolicy(securityPolicyUri))
            {
                byte[] dataToEncrypt = Utils.Append(DecryptedPassword, receiverNonce);

                ILogger logger = context.Telemetry.CreateLogger<UserNameIdentityToken>();
                EncryptedData encryptedData = SecurityPolicies.Encrypt(
                    receiverCertificate,
                    securityPolicyUri,
                    dataToEncrypt,
                    logger);

                m_password = encryptedData.Data;
                m_encryptionAlgorithm = encryptedData.Algorithm;
                Array.Clear(dataToEncrypt, 0, dataToEncrypt.Length);
            }
            // handle ECC encryption.
            else
            {
                // check if the complete chain is included in the sender issuers.
                if (senderIssuerCertificates != null &&
                    senderIssuerCertificates.Count > 0 &&
                    senderIssuerCertificates[0].Thumbprint == senderCertificate.Thumbprint)
                {
                    var issuers = new X509Certificate2Collection();

                    for (int ii = 1; ii < senderIssuerCertificates.Count; ii++)
                    {
                        issuers.Add(senderIssuerCertificates[ii]);
                    }

                    senderIssuerCertificates = issuers;
                }

                var secret = new EncryptedSecret(
                    context,
                    securityPolicyUri,
                    senderIssuerCertificates,
                    receiverCertificate,
                    receiverEphemeralKey,
                    senderCertificate,
                    Nonce.CreateNonce(securityPolicyUri),
                    null,
                    doNotEncodeSenderCertificate);

                m_password = secret.Encrypt(DecryptedPassword, receiverNonce);
                m_encryptionAlgorithm = null;
            }
        }

        /// <summary>
        /// Decrypts the Password using the EncryptionAlgorithm and places the result in DecryptedPassword
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        public override void Decrypt(
            X509Certificate2 certificate,
            Nonce receiverNonce,
            string securityPolicyUri,
            IServiceMessageContext context,
            Nonce ephemeralKey = null,
            X509Certificate2 senderCertificate = null,
            X509Certificate2Collection senderIssuerCertificates = null,
            CertificateValidator validator = null)
        {
            //zero out existing password
            if (DecryptedPassword != null)
            {
                Array.Clear(DecryptedPassword, 0, DecryptedPassword.Length);
            }

            // handle no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri) ||
                securityPolicyUri == SecurityPolicies.None)
            {
                DecryptedPassword = new byte[m_password.Length];
                Array.Copy(m_password, DecryptedPassword, m_password.Length);
                return;
            }

            // handle RSA encryption.
            if (!EccUtils.IsEccPolicy(securityPolicyUri))
            {
                var encryptedData = new EncryptedData
                {
                    Data = m_password,
                    Algorithm = m_encryptionAlgorithm
                };

                ILogger logger = context.Telemetry.CreateLogger<UserNameIdentityToken>();
                byte[] decryptedPassword = SecurityPolicies.Decrypt(
                    certificate,
                    securityPolicyUri,
                    encryptedData,
                    logger);

                if (decryptedPassword == null)
                {
                    DecryptedPassword = null;
                    return;
                }

                // verify the sender's nonce.
                int startOfNonce = decryptedPassword.Length;
                if (receiverNonce != null)
                {
                    startOfNonce -= receiverNonce.Data.Length;

                    int result = 0;
                    for (int ii = 0; ii < receiverNonce.Data.Length; ii++)
                    {
                        result |= receiverNonce.Data[ii] ^ decryptedPassword[ii + startOfNonce];
                    }

                    if (result != 0)
                    {
                        throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected);
                    }
                }

                // copy result to m_decrypted password field
                DecryptedPassword = new byte[startOfNonce];
                Array.Copy(decryptedPassword, DecryptedPassword, startOfNonce);
                Array.Clear(decryptedPassword, 0, decryptedPassword.Length);
            }
            // handle ECC encryption.
            else
            {
                var secret = new EncryptedSecret(
                    context,
                    securityPolicyUri,
                    senderIssuerCertificates,
                    certificate,
                    ephemeralKey,
                    senderCertificate,
                    null,
                    validator);

                DecryptedPassword = secret.Decrypt(
                    DateTime.UtcNow.AddHours(-1),
                    receiverNonce.Data,
                    m_password,
                    0,
                    m_password.Length,
                    context.Telemetry);
            }
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (DecryptedPassword != null)
                {
                    Array.Clear(DecryptedPassword, 0, DecryptedPassword.Length);
                    DecryptedPassword = null;
                }
                if (m_password != null)
                {
                    Array.Clear(m_password, 0, m_password.Length);
                    m_password = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
