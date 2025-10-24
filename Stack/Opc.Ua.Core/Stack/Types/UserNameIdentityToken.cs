/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
#if ECC_SUPPORT
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
#else
                throw new NotSupportedException("Platform does not support ECC curves");
#endif
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
#if ECC_SUPPORT
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
#else
                throw new NotSupportedException("Platform does not support ECC curves");
#endif
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
