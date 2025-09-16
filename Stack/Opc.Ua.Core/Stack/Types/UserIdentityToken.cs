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
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// The UserIdentityToken class.
    /// </summary>
    public partial class UserIdentityToken
    {
        /// <summary>
        /// Encrypts the token (implemented by the subclass).
        /// </summary>
        public virtual void Encrypt(
            X509Certificate2 receiverCertificate,
            byte[] receiverNonce,
            string securityPolicyUri,
            Nonce receiverEphemeralKey = null,
            X509Certificate2 senderCertificate = null,
            X509Certificate2Collection senderIssuerCertificates = null,
            bool doNotEncodeSenderCertificate = false)
        {
        }

        /// <summary>
        /// Decrypts the token (implemented by the subclass).
        /// </summary>
        public virtual void Decrypt(
            X509Certificate2 certificate,
            Nonce receiverNonce,
            string securityPolicyUri,
            Nonce ephemeralKey = null,
            X509Certificate2 senderCertificate = null,
            X509Certificate2Collection senderIssuerCertificates = null,
            CertificateValidator validator = null)
        {
        }

        /// <summary>
        /// Creates a signature with the token (implemented by the subclass).
        /// </summary>
        public virtual SignatureData Sign(
            string securityPolicyUri,
            byte[] serverCertificate,
            byte[] serverChannelCertificate,
            byte[] clientCertificate,
            byte[] clientChannelCertificate,
            byte[] serverNonce,
            byte[] clientNonce)
        {
            return new SignatureData();
        }

        /// <summary>
        /// Verifies a signature created with the token (implemented by the subclass).
        /// </summary>
        public virtual bool Verify(
            SignatureData signatureData,
            string securityPolicyUri,
            byte[] serverCertificate,
            byte[] serverChannelCertificate,
            byte[] clientCertificate,
            byte[] clientChannelCertificate,
            byte[] serverNonce,
            byte[] clientNonce)
        {
            return true;
        }
    }

    /// <summary>
    /// The UserIdentityToken class.
    /// </summary>
    public partial class UserNameIdentityToken
    {
        /// <summary>
        /// The decrypted password associated with the token.
        /// </summary>
        public string DecryptedPassword
        {
            get
            {
                if (m_decryptedPassword != null)
                {
                    return Encoding.UTF8.GetString(m_decryptedPassword);
                }
                return null;
            }
            set
            {
                //zero out existing password
                if (m_decryptedPassword != null)
                {
                    Array.Clear(m_decryptedPassword, 0, m_decryptedPassword.Length);
                }

                if (value == null)
                {
                    m_decryptedPassword = null;
                    return;
                }

                m_decryptedPassword = Encoding.UTF8.GetBytes(value);
            }
        }

        /// <summary>
        /// Encrypts the DecryptedPassword using the EncryptionAlgorithm and places the result in Password
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public override void Encrypt(
            X509Certificate2 receiverCertificate,
            byte[] receiverNonce,
            string securityPolicyUri,
            Nonce receiverEphemeralKey = null,
            X509Certificate2 senderCertificate = null,
            X509Certificate2Collection senderIssuerCertificates = null,
            bool doNotEncodeSenderCertificate = false)
        {
            if (m_decryptedPassword == null)
            {
                m_password = null;
                return;
            }

            // handle no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri) ||
                securityPolicyUri == SecurityPolicies.None)
            {
                m_password = m_decryptedPassword;
                m_encryptionAlgorithm = null;
                return;
            }

            // handle RSA encryption.
            if (!EccUtils.IsEccPolicy(securityPolicyUri))
            {
                byte[] dataToEncrypt = Utils.Append(m_decryptedPassword, receiverNonce);

                EncryptedData encryptedData = SecurityPolicies.Encrypt(
                    receiverCertificate,
                    securityPolicyUri,
                    dataToEncrypt);

                m_password = encryptedData.Data;
                m_encryptionAlgorithm = encryptedData.Algorithm;
            }
            // handle ECC encryption.
            else
            {
#if ECC_SUPPORT
                var secret = new EncryptedSecret
                {
                    ReceiverCertificate = receiverCertificate,
                    SecurityPolicyUri = securityPolicyUri,
                    ReceiverNonce = receiverEphemeralKey,
                    SenderCertificate = senderCertificate,
                    SenderIssuerCertificates = senderIssuerCertificates,
                    DoNotEncodeSenderCertificate = doNotEncodeSenderCertificate
                };

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

                secret.SenderIssuerCertificates = senderIssuerCertificates;
                secret.SenderNonce = Nonce.CreateNonce(securityPolicyUri);

                m_password = secret.Encrypt(m_decryptedPassword, receiverNonce);
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
            Nonce ephemeralKey = null,
            X509Certificate2 senderCertificate = null,
            X509Certificate2Collection senderIssuerCertificates = null,
            CertificateValidator validator = null)
        {
            //zero out existing password
            if (m_decryptedPassword != null)
            {
                Array.Clear(m_decryptedPassword, 0, m_decryptedPassword.Length);
            }

            // handle no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri) ||
                securityPolicyUri == SecurityPolicies.None)
            {
                m_decryptedPassword = m_password;
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

                byte[] decryptedPassword = SecurityPolicies.Decrypt(
                    certificate,
                    securityPolicyUri,
                    encryptedData);

                if (decryptedPassword == null)
                {
                    m_decryptedPassword = null;
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

                //copy result to m_decrypted password field
                m_decryptedPassword = new byte[startOfNonce];
                Array.Copy(decryptedPassword, 0, m_decryptedPassword, 0, startOfNonce);
            }
            // handle ECC encryption.
            else
            {
#if ECC_SUPPORT
                var secret = new EncryptedSecret
                {
                    SenderCertificate = senderCertificate,
                    SenderIssuerCertificates = senderIssuerCertificates,
                    Validator = validator,
                    ReceiverCertificate = certificate,
                    ReceiverNonce = ephemeralKey,
                    SecurityPolicyUri = securityPolicyUri
                };

                m_decryptedPassword = secret.Decrypt(
                    DateTime.UtcNow.AddHours(-1),
                    receiverNonce.Data,
                    m_password,
                    0,
                    m_password.Length);
#else
                throw new NotSupportedException("Platform does not support ECC curves");
#endif
            }
        }

        private byte[] m_decryptedPassword;
    }

    /// <summary>
    /// The X509IdentityToken class.
    /// </summary>
    public partial class X509IdentityToken
    {
        /// <summary>
        /// The certificate associated with the token.
        /// </summary>
        public X509Certificate2 Certificate
        {
            get
            {
                if (m_certificate == null && m_certificateData != null)
                {
                    return CertificateFactory.Create(m_certificateData, true);
                }
                return m_certificate;
            }
            set => m_certificate = value;
        }

        /// <summary>
        /// Creates a signature with the token.
        /// </summary>
        public override SignatureData Sign(
            string securityPolicyUri,
            byte[] serverCertificate,
            byte[] serverChannelCertificate,
            byte[] clientCertificate,
            byte[] clientChannelCertificate,
            byte[] serverNonce,
            byte[] clientNonce)
        {
            X509Certificate2 certificate = m_certificate ??
                CertificateFactory.Create(m_certificateData, true);

            // nothing more to do if no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                return new SignatureData();
            }

            // get the info object.
            var info = SecurityPolicies.GetInfo(securityPolicyUri);

            // unsupported policy.
            if (info == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unsupported security policy: {0}",
                    securityPolicyUri);
            }
  
            // create the data to sign.
            byte[] dataToSign = (info.SecureChannelEnhancements)
                ? Utils.Append(
                    serverCertificate ?? Array.Empty<byte>(),
                    serverChannelCertificate ?? Array.Empty<byte>(),
                    clientCertificate ?? Array.Empty<byte>(),
                    clientChannelCertificate ?? Array.Empty<byte>(),
                    serverNonce ?? Array.Empty<byte>(),
                    clientNonce ?? Array.Empty<byte>())
                : Utils.Append(
                    serverCertificate ?? Array.Empty<byte>(),
                    serverNonce);

            SignatureData signatureData = SecurityPolicies.CreateSignatureData(
                info,
                certificate,
                dataToSign);

            m_certificateData = certificate.RawData;

            return signatureData;
        }

        /// <summary>
        /// Verifies a signature created with the token.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public override bool Verify(
            SignatureData signatureData,
            string securityPolicyUri,
            byte[] serverCertificate,
            byte[] serverChannelCertificate,
            byte[] clientCertificate,
            byte[] clientChannelCertificate,
            byte[] serverNonce,
            byte[] clientNonce)
        {
            try
            {
                X509Certificate2 certificate = m_certificate ??
                    CertificateFactory.Create(m_certificateData, true);

                // nothing more to do if no encryption.
                if (string.IsNullOrEmpty(securityPolicyUri))
                {
                    return true;
                }

                // get the info object.
                var info = SecurityPolicies.GetInfo(securityPolicyUri);

                // unsupported policy.
                if (info == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityPolicyRejected,
                        "Unsupported security policy: {0}",
                        securityPolicyUri);
                }

                // create the data to sign.
                byte[] dataToVerify = (info.SecureChannelEnhancements)
                    ? Utils.Append(
                        serverCertificate ?? Array.Empty<byte>(),
                        serverChannelCertificate ?? Array.Empty<byte>(),
                        clientCertificate ?? Array.Empty<byte>(),
                        clientChannelCertificate ?? Array.Empty<byte>(),
                        serverNonce ?? Array.Empty<byte>(),
                        clientNonce ?? Array.Empty<byte>())
                    :
                     Utils.Append(
                        serverCertificate ?? Array.Empty<byte>(),
                        serverNonce);

                bool valid = SecurityPolicies.VerifySignatureData(
                    signatureData,
                    info,
                    certificate,
                    dataToVerify);

                m_certificateData = certificate.RawData;

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

        private X509Certificate2 m_certificate;
    }

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
        public byte[] DecryptedTokenData { get; set; }

        /// <summary>
        /// Encrypts the DecryptedTokenData using the EncryptionAlgorithm and places the result in Password
        /// </summary>
        public override void Encrypt(
            X509Certificate2 receiverCertificate,
            byte[] receiverNonce,
            string securityPolicyUri,
            Nonce receiverEphemeralKey = null,
            X509Certificate2 senderCertificate = null,
            X509Certificate2Collection senderIssuerCertificates = null,
            bool doNotEncodeSenderCertificate = false)
        {
            // handle no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri) ||
                securityPolicyUri == SecurityPolicies.None)
            {
                m_tokenData = DecryptedTokenData;
                m_encryptionAlgorithm = string.Empty;
                return;
            }

            byte[] dataToEncrypt = Utils.Append(DecryptedTokenData, receiverNonce);

            EncryptedData encryptedData = SecurityPolicies.Encrypt(
                receiverCertificate,
                securityPolicyUri,
                dataToEncrypt);

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
            Nonce ephemeralKey = null,
            X509Certificate2 senderCertificate = null,
            X509Certificate2Collection senderIssuerCertificates = null,
            CertificateValidator validator = null)
        {
            // handle no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri) ||
                securityPolicyUri == SecurityPolicies.None)
            {
                DecryptedTokenData = m_tokenData;
                return;
            }

            var encryptedData = new EncryptedData
            {
                Data = m_tokenData,
                Algorithm = m_encryptionAlgorithm
            };

            byte[] decryptedTokenData = SecurityPolicies.Decrypt(
                certificate,
                securityPolicyUri,
                encryptedData);

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
            DecryptedTokenData = new byte[startOfNonce];
            Array.Copy(decryptedTokenData, DecryptedTokenData, startOfNonce);
        }

        /// <summary>
        /// Creates a signature with the token.
        /// </summary>
        public override SignatureData Sign(
            string securityPolicyUri,
            byte[] serverCertificate,
            byte[] serverChannelCertificate,
            byte[] clientCertificate,
            byte[] clientChannelCertificate,
            byte[] serverNonce,
            byte[] clientNonce)
        {
            return null;
        }

        /// <summary>
        /// Verifies a signature created with the token.
        /// </summary>
        public override bool Verify(
            SignatureData signatureData,
            string securityPolicyUri,
            byte[] serverCertificate,
            byte[] serverChannelCertificate,
            byte[] clientCertificate,
            byte[] clientChannelCertificate,
            byte[] serverNonce,
            byte[] clientNonce)
        {
            return true;
        }
    }
}
