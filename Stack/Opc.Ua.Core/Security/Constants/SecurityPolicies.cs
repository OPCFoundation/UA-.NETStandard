/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Defines constants for key security policies.
    /// </summary>
    public static class SecurityPolicies
    {
        #region Public Constants
        /// <summary>
        /// The base URI for all policy URIs.
        /// </summary>
        public const string BaseUri = "http://opcfoundation.org/UA/SecurityPolicy#";

        /// <summary>
        /// The URI for a policy that uses no security.
        /// </summary>
        public const string None = BaseUri + "None";

        /// <summary>
        /// The URI for the Basic128Rsa15 security policy.
        /// </summary>
        public const string Basic128Rsa15 = BaseUri + "Basic128Rsa15";

        /// <summary>
        /// The URI for the Basic256 security policy.
        /// </summary>
        public const string Basic256 = BaseUri + "Basic256";

        /// <summary>
        /// The URI for the Https security policy.
        /// </summary>
        public const string Https = BaseUri + "Https";

        /// <summary>
        /// The URI for the Basic256Sha256 security policy.
        /// </summary>
        public const string Basic256Sha256 = BaseUri + "Basic256Sha256";
        #endregion

        #region Static Methods
        /// <summary>
        /// Returns the uri associated with the display name.
        /// </summary>
        public static string GetUri(string displayName)
        {
            FieldInfo[] fields = typeof(SecurityPolicies).GetFields(BindingFlags.Public | BindingFlags.Static);

            foreach (FieldInfo field in fields)
            {
                if (field.Name == displayName)
                {
                    return (string)field.GetValue(typeof(SecurityPolicies));
                }
            }

            return null;
        }

        /// <summary>
        /// Returns a display name for a security policy uri.
        /// </summary>
        public static string GetDisplayName(string policyUri)
        {
            FieldInfo[] fields = typeof(SecurityPolicies).GetFields(BindingFlags.Public | BindingFlags.Static);

            foreach (FieldInfo field in fields)
            {
                if (policyUri == (string)field.GetValue(typeof(SecurityPolicies)))
                {
                    return field.Name;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the display names for all security policy uris.
        /// </summary>
        public static string[] GetDisplayNames()
        {
            FieldInfo[] fields = typeof(SecurityPolicies).GetFields(BindingFlags.Public | BindingFlags.Static);

            int ii = 0;

            string[] names = new string[fields.Length];

            foreach (FieldInfo field in fields)
            {
                names[ii++] = field.Name;
            }

            return names;
        }
        
        /// <summary>
        /// Encrypts the text using the SecurityPolicyUri and returns the result.
        /// </summary>
        public static EncryptedData Encrypt(X509Certificate2 certificate, string securityPolicyUri, byte[] plainText)
        {
            EncryptedData encryptedData = new EncryptedData();

            encryptedData.Algorithm = null;
            encryptedData.Data = plainText;

            // check if nothing to do.
            if (plainText == null)
            {
                return encryptedData;
            }

            // nothing more to do if no encryption.
            if (String.IsNullOrEmpty(securityPolicyUri))
            {
                return encryptedData;
            }

            // encrypt data.
            switch (securityPolicyUri)
            {
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                    {
                        encryptedData.Algorithm = SecurityAlgorithms.RsaOaep;
                        encryptedData.Data = RsaUtils.Encrypt(plainText, certificate, true);
                        break;
                    }

                case SecurityPolicies.Basic128Rsa15:
                    {
                    encryptedData.Algorithm = SecurityAlgorithms.Rsa15;
                        encryptedData.Data = RsaUtils.Encrypt(plainText, certificate, false);
                        break;
                    }

                case SecurityPolicies.None:
                    {
                        break;
                    }

                default:
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadSecurityPolicyRejected,
                            "Unsupported security policy: {0}",
                            securityPolicyUri);
                    }
            }

            return encryptedData;
        }

        /// <summary>
        /// Decrypts the CipherText using the SecurityPolicyUri and returns the PlainTetx.
        /// </summary>
        public static byte[] Decrypt(X509Certificate2 certificate, string securityPolicyUri, EncryptedData dataToDecrypt)
        {
            // check if nothing to do.
            if (dataToDecrypt == null)
            {
                return null;
            }

            // nothing more to do if no encryption.
            if (String.IsNullOrEmpty(securityPolicyUri))
            {
                return dataToDecrypt.Data;
            }

            // decrypt data.
            switch (securityPolicyUri)
            {
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                    {
                        if (dataToDecrypt.Algorithm == SecurityAlgorithms.RsaOaep)
                        {
                            return RsaUtils.Decrypt(new ArraySegment<byte>(dataToDecrypt.Data), certificate, true);
                        }

                        break;
                    }

                case SecurityPolicies.Basic128Rsa15:
                    {
                    if (dataToDecrypt.Algorithm == SecurityAlgorithms.Rsa15)
                        {
                            return RsaUtils.Decrypt(new ArraySegment<byte>(dataToDecrypt.Data), certificate, false);
                        }

                        break;
                    }

                case SecurityPolicies.None:
                    {
                        if (String.IsNullOrEmpty(dataToDecrypt.Algorithm))
                        {
                            return dataToDecrypt.Data;
                        }

                        break;
                    }

                default:
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadSecurityPolicyRejected,
                            "Unsupported security policy: {0}",
                            securityPolicyUri);
                    }
            }

            throw ServiceResultException.Create(
                StatusCodes.BadIdentityTokenInvalid, 
                "Unexpected encryption algorithm : {0}",
                dataToDecrypt.Algorithm);
        }

        /// <summary>
        /// Signs the data using the SecurityPolicyUri and returns the signature.
        /// </summary>
        public static SignatureData Sign(X509Certificate2 certificate, string securityPolicyUri, byte[] dataToSign)
        {
            SignatureData signatureData = new SignatureData();

            // check if nothing to do.
            if (dataToSign == null)
            {
                return signatureData;
            }

            // nothing more to do if no encryption.
            if (String.IsNullOrEmpty(securityPolicyUri))
            {
                return signatureData;
            }

            // sign data.
            switch (securityPolicyUri)
            {
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic128Rsa15:
                    {
                        signatureData.Algorithm = SecurityAlgorithms.RsaSha1;
                        signatureData.Signature = RsaUtils.RsaPkcs15Sha1_Sign(new ArraySegment<byte>(dataToSign), certificate);
                        break;
                    }

                case SecurityPolicies.Basic256Sha256:
                    {
                        signatureData.Algorithm = SecurityAlgorithms.RsaSha256;
                        signatureData.Signature = RsaUtils.RsaPkcs15Sha256_Sign(new ArraySegment<byte>(dataToSign), certificate);
                        break;
                    }

                case SecurityPolicies.None:
                    {
                        signatureData.Algorithm = null;
                        signatureData.Signature = null;
                        break;
                    }

                default:
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadSecurityPolicyRejected,
                            "Unsupported security policy: {0}",
                            securityPolicyUri);
                    }
            }

            return signatureData;
        }

        /// <summary>
        /// Verifies the signature using the SecurityPolicyUri and return true if valid.
        /// </summary>
        public static bool Verify(X509Certificate2 certificate, string securityPolicyUri, byte[] dataToVerify, SignatureData signature)
        {
            // check if nothing to do.
            if (signature == null)
            {
                return true;
            }

            // nothing more to do if no encryption.
            if (String.IsNullOrEmpty(securityPolicyUri))
            {
                return true;
            }

            // decrypt data.
            switch (securityPolicyUri)
            {
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic128Rsa15:
                    {
                        if (signature.Algorithm == SecurityAlgorithms.RsaSha1)
                        {
                            return RsaUtils.RsaPkcs15Sha1_Verify(new ArraySegment<byte>(dataToVerify), signature.Signature, certificate);
                        }

                        break;
                    }

                case SecurityPolicies.Basic256Sha256:
                    {
                        if (signature.Algorithm == SecurityAlgorithms.RsaSha256)
                        {
                            return RsaUtils.RsaPkcs15Sha256_Verify(new ArraySegment<byte>(dataToVerify), signature.Signature, certificate);
                        }

                        break;
                    }

                // always accept signatures if security is not used.
                case SecurityPolicies.None:
                    {
                        return true;
                    }

                default:
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadSecurityPolicyRejected,
                            "Unsupported security policy: {0}",
                            securityPolicyUri);
                    }
            }

            throw ServiceResultException.Create(
                StatusCodes.BadSecurityChecksFailed,
                "Unexpected signature algorithm : {0}",
                signature.Algorithm);
        }
        #endregion
    }
}
