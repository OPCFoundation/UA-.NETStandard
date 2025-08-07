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
using System.Text;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// The UserIdentityToken class.
    /// </summary>
    public partial class UserIdentityToken
    {
        #region Public Methods

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
        public virtual SignatureData Sign(byte[] dataToSign, string securityPolicyUri)
        {
            return new SignatureData();
        }

        /// <summary>
        /// Verifies a signature created with the token (implemented by the subclass).
        /// </summary>
        public virtual bool Verify(byte[] dataToVerify, SignatureData signatureData, string securityPolicyUri)
        {
            return true;
        }
        #endregion
    }

    /// <summary>
    /// The UserIdentityToken class.
    /// </summary>
    public partial class UserNameIdentityToken
    {
        #region Public Properties
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

        #endregion

        #region Public Methods
        /// <summary>
        /// Encrypts the DecryptedPassword using the EncryptionAlgorithm and places the result in Password
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
            if (m_decryptedPassword == null)
            {
                m_password = null;
                return;
            }

            // handle no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri) || securityPolicyUri == SecurityPolicies.None)
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
                var secret = new EncryptedSecret();

                secret.ReceiverCertificate = receiverCertificate;
                secret.SecurityPolicyUri = securityPolicyUri;
                secret.ReceiverNonce = receiverEphemeralKey;
                secret.SenderCertificate = senderCertificate;
                secret.SenderIssuerCertificates = senderIssuerCertificates;
                secret.DoNotEncodeSenderCertificate = doNotEncodeSenderCertificate;

                // check if the complete chain is included in the sender issuers.
                if (senderIssuerCertificates != null && senderIssuerCertificates.Count > 0 && senderIssuerCertificates[0].Thumbprint == senderCertificate.Thumbprint)
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
            if (string.IsNullOrEmpty(securityPolicyUri) || securityPolicyUri == SecurityPolicies.None)
            {
                m_decryptedPassword = m_password;
                return;
            }

            // handle RSA encryption.
            if (!EccUtils.IsEccPolicy(securityPolicyUri))
            {
                var encryptedData = new EncryptedData();
                encryptedData.Data = m_password;
                encryptedData.Algorithm = m_encryptionAlgorithm;

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
                var secret = new EncryptedSecret();

                secret.SenderCertificate = senderCertificate;
                secret.SenderIssuerCertificates = senderIssuerCertificates;
                secret.Validator = validator;
                secret.ReceiverCertificate = certificate;
                secret.ReceiverNonce = ephemeralKey;
                secret.SecurityPolicyUri = securityPolicyUri;

                m_decryptedPassword = secret.Decrypt(DateTime.UtcNow.AddHours(-1), receiverNonce.Data, m_password, 0, m_password.Length);
#else
                throw new NotSupportedException("Platform does not support ECC curves");
#endif
            }
        }

        #endregion

        #region Private Fields
        private byte[] m_decryptedPassword;
        #endregion
    }

    /// <summary>
    /// The X509IdentityToken class.
    /// </summary>
    public partial class X509IdentityToken
    {
        #region Public Properties
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
            set { m_certificate = value; }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Creates a signature with the token.
        /// </summary>
        public override SignatureData Sign(byte[] dataToSign, string securityPolicyUri)
        {
            X509Certificate2 certificate = m_certificate ?? CertificateFactory.Create(m_certificateData, true);

            SignatureData signatureData = SecurityPolicies.Sign(
                certificate,
                securityPolicyUri,
                dataToSign);

            m_certificateData = certificate.RawData;

            return signatureData;
        }

        /// <summary>
        /// Verifies a signature created with the token.
        /// </summary>
        public override bool Verify(byte[] dataToVerify, SignatureData signatureData, string securityPolicyUri)
        {
            try
            {
                X509Certificate2 certificate = m_certificate ?? CertificateFactory.Create(m_certificateData, true);

                bool valid = SecurityPolicies.Verify(
                    certificate,
                    securityPolicyUri,
                    dataToVerify,
                    signatureData);

                m_certificateData = certificate.RawData;

                return valid;
            }
            catch (Exception e)
            {
                throw ServiceResultException.Create(StatusCodes.BadIdentityTokenInvalid, e, "Could not verify user signature!");
            }
        }
        #endregion

        #region Private Fields
        private X509Certificate2 m_certificate;
        #endregion
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
        #region Public Properties
        /// <summary>
        /// The type of issued token.
        /// </summary>
        public IssuedTokenType IssuedTokenType
        {
            get;
            set;
        }

        /// <summary>
        /// The decrypted password associated with the token.
        /// </summary>
        public byte[] DecryptedTokenData
        {
            get { return m_decryptedTokenData; }
            set { m_decryptedTokenData = value; }
        }
        #endregion

        #region Public Methods
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
            if (string.IsNullOrEmpty(securityPolicyUri) || securityPolicyUri == SecurityPolicies.None)
            {
                m_tokenData = m_decryptedTokenData;
                m_encryptionAlgorithm = string.Empty;
                return;
            }

            byte[] dataToEncrypt = Utils.Append(m_decryptedTokenData, receiverNonce);

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
            if (string.IsNullOrEmpty(securityPolicyUri) || securityPolicyUri == SecurityPolicies.None)
            {
                m_decryptedTokenData = m_tokenData;
                return;
            }

            var encryptedData = new EncryptedData();

            encryptedData.Data = m_tokenData;
            encryptedData.Algorithm = m_encryptionAlgorithm;

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
            m_decryptedTokenData = new byte[startOfNonce];
            Array.Copy(decryptedTokenData, m_decryptedTokenData, startOfNonce);
        }

        /// <summary>
        /// Creates a signature with the token.
        /// </summary>
        public override SignatureData Sign(byte[] dataToSign, string securityPolicyUri)
        {
            return null;
        }

        /// <summary>
        /// Verifies a signature created with the token.
        /// </summary>
        public override bool Verify(byte[] dataToVerify, SignatureData signatureData, string securityPolicyUri)
        {
            return true;
        }
        #endregion

        #region Private Fields
        private byte[] m_decryptedTokenData;
        #endregion
    }
}
