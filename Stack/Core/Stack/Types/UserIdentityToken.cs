/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Text;
using System.ServiceModel.Security;
using System.Security.Cryptography.X509Certificates;

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
        public virtual void Encrypt(X509Certificate2 certificate, byte[] receiverNonce, string securityPolicyUri)
        {
        }
                
        /// <summary>
        /// Decrypts the token (implemented by the subclass).
        /// </summary>
        public virtual void Decrypt(X509Certificate2 certificate, byte[] receiverNonce, string securityPolicyUri)
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
            get { return m_decryptedPassword;  }
            set { m_decryptedPassword = value; }
        }
        #endregion
        
        #region Public Methods
        /// <summary>
        /// Encrypts the DecryptedPassword using the EncryptionAlgorithm and places the result in Password
        /// </summary>
        public override void Encrypt(X509Certificate2 certificate, byte[] senderNonce, string securityPolicyUri)
        {
            if (m_decryptedPassword == null)
            {
                m_password = null;
                return;
            }

            // handle no encryption.
            if (String.IsNullOrEmpty(securityPolicyUri) || securityPolicyUri == SecurityPolicies.None)
            {
                m_password = new UTF8Encoding().GetBytes(m_decryptedPassword);
                m_encryptionAlgorithm = null;
                return;
            }
            
            // encrypt the password.
            byte[] dataToEncrypt = Utils.Append(new UTF8Encoding().GetBytes(m_decryptedPassword), senderNonce);

            EncryptedData encryptedData = SecurityPolicies.Encrypt(
                certificate,
                securityPolicyUri,
                dataToEncrypt);
                        
            m_password = encryptedData.Data;
            m_encryptionAlgorithm = encryptedData.Algorithm; 
        }
                
        /// <summary>
        /// Decrypts the Password using the EncryptionAlgorithm and places the result in DecryptedPassword
        /// </summary>
        public override void Decrypt(X509Certificate2 certificate, byte[] senderNonce, string securityPolicyUri)
        {
            // handle no encryption.
            if (String.IsNullOrEmpty(securityPolicyUri) || securityPolicyUri == SecurityPolicies.None)
            {
                m_decryptedPassword = new UTF8Encoding().GetString(m_password, 0, m_password.Length);
                return;
            }
            
            // decrypt.
            EncryptedData encryptedData = new EncryptedData();

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

            if (senderNonce != null)
            {
                 startOfNonce -= senderNonce.Length;

                for (int ii = 0; ii < senderNonce.Length; ii++)
                {
                    if (senderNonce[ii] != decryptedPassword[ii+startOfNonce])
                    {
                        throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                    }
                }
            }            
                     
            // convert to UTF-8.
            m_decryptedPassword = new UTF8Encoding().GetString(decryptedPassword, 0, startOfNonce);
        }
        #endregion

        #region Private Fields
        private string m_decryptedPassword;
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
            get { return m_certificate;  }
            set { m_certificate = value; }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Creates a signature with the token.
        /// </summary>
        public override SignatureData Sign(byte[] dataToSign, string securityPolicyUri)
        {
            X509Certificate2 certificate = m_certificate;
            
            if (certificate == null)
            {   
                certificate = CertificateFactory.Create(m_certificateData, true);
            }
            
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
            X509Certificate2 certificate = m_certificate;
            
            if (certificate == null)
            {   
                certificate = CertificateFactory.Create(m_certificateData, true);
            }          
            
            bool valid = SecurityPolicies.Verify(
                certificate, 
                securityPolicyUri, 
                dataToVerify,
                signatureData);

            m_certificateData = certificate.RawData;

            return valid;
        }
        #endregion

        #region Private Fields
        private X509Certificate2 m_certificate;
        #endregion
    }
        
	/// <summary>
	/// The IssuedIdentityToken class.
	/// </summary>
	public partial class IssuedIdentityToken
	{
        #region Public Properties
        /// <summary>
        /// The decrypted password associated with the token.
        /// </summary>
        public byte[] DecryptedTokenData
        {
            get { return m_decryptedTokenData;  }
            set { m_decryptedTokenData = value; }
        }
        #endregion
        
        #region Public Methods
        /// <summary>
        /// Encrypts the DecryptedTokenData using the EncryptionAlgorithm and places the result in Password
        /// </summary>
        public override void Encrypt(X509Certificate2 certificate, byte[] senderNonce, string securityPolicyUri)
        {
            byte[] dataToEncrypt = Utils.Append(m_decryptedTokenData, senderNonce);

            EncryptedData encryptedData = SecurityPolicies.Encrypt(
                certificate,
                securityPolicyUri,
                dataToEncrypt);
                        
            m_tokenData = encryptedData.Data;
            m_encryptionAlgorithm = encryptedData.Algorithm;
        }
                
        /// <summary>
        /// Decrypts the Password using the EncryptionAlgorithm and places the result in DecryptedPassword
        /// </summary>
        public override void Decrypt(X509Certificate2 certificate, byte[] senderNonce, string securityPolicyUri)
        {
            EncryptedData encryptedData = new EncryptedData();

            encryptedData.Data = m_tokenData;
            encryptedData.Algorithm = m_encryptionAlgorithm;

            byte[] decryptedTokenData = SecurityPolicies.Decrypt(
                certificate, 
                securityPolicyUri, 
                encryptedData);

            // verify the sender's nonce.
            int startOfNonce = decryptedTokenData.Length;

            if (senderNonce != null)
            {
                startOfNonce -= senderNonce.Length;

                for (int ii = 0; ii < senderNonce.Length; ii++)
                {
                    if (senderNonce[ii] != decryptedTokenData[ii+startOfNonce])
                    {
                        throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
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
