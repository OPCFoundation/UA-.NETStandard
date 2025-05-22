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
    /// The issued token type.
    /// </summary>
    public enum IssuedTokenType
    {
        /// <summary>
        /// Web services security (WSS) token.
        /// </summary>
        GenericWSS,

        /// <summary>
        /// Security Assertions Markup Language (SAML) token.
        /// </summary>
        SAML,

        /// <summary>
        /// JSON web token.
        /// </summary>
        JWT,

        /// <summary>
        /// Kerberos token.
        /// </summary>
        KerberosBinary
    }

    /// <summary>
    /// The IssuedIdentityToken class.
    /// </summary>
    public partial class IssuedIdentityToken
    {
        /// <summary>
        /// The type of issued token.
        /// </summary>
        public IssuedTokenType IssuedTokenType { get; set; }

        /// <summary>
        /// The decrypted password associated with the token.
        /// </summary>
        /// <remarks>
        /// Internally always creates a deep copy on get and set, so that the user
        /// can clear the token data after using or setting it.
        /// </remarks>
        [IgnoreDataMember]
        public byte[] DecryptedTokenData
        {
            get
            {
                if (m_decryptedTokenData != null)
                {
                    byte[] result = new byte[m_decryptedTokenData.Length];
                    Array.Copy(m_decryptedTokenData, result, m_decryptedTokenData.Length);
                    return result;
                }
                return null;
            }
            set
            {
                if (value != null)
                {
                    m_decryptedTokenData = new byte[value.Length];
                    Array.Copy(value, m_decryptedTokenData, value.Length);
                }
                else
                {
                    m_decryptedTokenData = null;
                }
            }
        }

        /// <summary>
        /// Encrypts the DecryptedTokenData using the EncryptionAlgorithm and places the result in Password
        /// </summary>
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
            // handle no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri) ||
                securityPolicyUri == SecurityPolicies.None)
            {
                m_tokenData = m_decryptedTokenData;
                m_encryptionAlgorithm = string.Empty;
                return;
            }

            byte[] dataToEncrypt = Utils.Append(m_decryptedTokenData, receiverNonce);

            ILogger logger = context.Telemetry.CreateLogger<IssuedIdentityToken>();
            EncryptedData encryptedData = SecurityPolicies.Encrypt(
                receiverCertificate,
                securityPolicyUri,
                dataToEncrypt,
                logger);

            Array.Clear(dataToEncrypt, 0, dataToEncrypt.Length);

            m_tokenData = encryptedData.Data;
            m_encryptionAlgorithm = encryptedData.Algorithm;
        }

        /// <summary>
        /// Decrypts the Password using the EncryptionAlgorithm and places the result in DecryptedPassword
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
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
            // handle no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri) ||
                securityPolicyUri == SecurityPolicies.None)
            {
                m_decryptedTokenData = m_tokenData;
                return;
            }

            var encryptedData = new EncryptedData
            {
                Data = m_tokenData,
                Algorithm = m_encryptionAlgorithm
            };

            ILogger logger = context.Telemetry.CreateLogger<IssuedIdentityToken>();
            byte[] decryptedTokenData = SecurityPolicies.Decrypt(
                certificate,
                securityPolicyUri,
                encryptedData,
                logger);

            // verify the sender's nonce.
            int startOfNonce = decryptedTokenData.Length;

            if (receiverNonce != null)
            {
                startOfNonce -= receiverNonce.Data.Length;

                for (int ii = 0; ii < receiverNonce.Data.Length; ii++)
                {
                    if (receiverNonce.Data[ii] != decryptedTokenData[ii + startOfNonce])
                    {
                        throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected);
                    }
                }
            }

            // copy results.
            m_decryptedTokenData = new byte[startOfNonce];
            Array.Copy(decryptedTokenData, m_decryptedTokenData, startOfNonce);
            Array.Clear(decryptedTokenData, 0, decryptedTokenData.Length);
        }

        /// <summary>
        /// Creates a signature with the token.
        /// </summary>
        public override SignatureData Sign(
            byte[] dataToSign,
            string securityPolicyUri,
            ITelemetryContext telemetry)
        {
            return null;
        }

        /// <summary>
        /// Verifies a signature created with the token.
        /// </summary>
        public override bool Verify(
            byte[] dataToVerify,
            SignatureData signatureData,
            string securityPolicyUri,
            ITelemetryContext telemetry)
        {
            return true;
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing && m_decryptedTokenData != null)
            {
                Array.Clear(m_decryptedTokenData, 0, m_decryptedTokenData.Length);
                m_decryptedTokenData = null;
            }
            base.Dispose(disposing);
        }

        private byte[] m_decryptedTokenData;
    }
}
