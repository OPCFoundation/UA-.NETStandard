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
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
        /// The URI for the Aes128_Sha256_RsaOaep security policy.
        /// </summary>
        public const string Aes128_Sha256_RsaOaep = BaseUri + "Aes128_Sha256_RsaOaep";

        /// <summary>
        /// The URI for the Basic256Sha256 security policy.
        /// </summary>
        public const string Basic256Sha256 = BaseUri + "Basic256Sha256";

        /// <summary>
        /// The URI for the Aes256_Sha256_RsaPss security policy.
        /// </summary>
        public const string Aes256_Sha256_RsaPss = BaseUri + "Aes256_Sha256_RsaPss";

        /// <summary>
        /// The URI for the ECC_nistP256 security policy.
        /// </summary>
        public const string ECC_nistP256 = BaseUri + "ECC_nistP256";

        /// <summary>
        /// The URI for the ECC_nistP384 security policy.
        /// </summary>
        public const string ECC_nistP384 = BaseUri + "ECC_nistP384";

        /// <summary>
        /// The URI for the ECC_brainpoolP256r1 security policy.
        /// </summary>
        public const string ECC_brainpoolP256r1 = BaseUri + "ECC_brainpoolP256r1";

        /// <summary>
        /// The URI for the ECC_brainpoolP384r1 security policy.
        /// </summary>
        public const string ECC_brainpoolP384r1 = BaseUri + "ECC_brainpoolP384r1";

        /// <summary>
        /// The URI for the ECC_curve25519 security policy.
        /// </summary>
        public const string ECC_curve25519 = BaseUri + "ECC_curve25519";

        /// <summary>
        /// The URI for the ECC_curve448 security policy.
        /// </summary>
        public const string ECC_curve448 = BaseUri + "ECC_curve448";

        /// <summary>
        /// The URI for the Https security policy.
        /// </summary>
        public const string Https = BaseUri + "Https";
        #endregion

        #region Static Methods
        private static bool IsPlatformSupportedName(string name)
        {
            // all RSA
            if (name.Equals(nameof(None)) ||
                name.Equals(nameof(Basic256)) ||
                name.Equals(nameof(Basic128Rsa15)) ||
                name.Equals(nameof(Basic256Sha256)) ||
                name.Equals(nameof(Aes128_Sha256_RsaOaep)))
            {
                return true;
            }

            if (name.Equals(nameof(Aes256_Sha256_RsaPss)) &&
                RsaUtils.IsSupportingRSAPssSign.Value)
            {
                return true;
            }

#if ECC_SUPPORT
            // ECC policy
            if (name.Equals(nameof(ECC_nistP256)) ||
                name.Equals(nameof(ECC_nistP384)))
            {
                return true;
            }

            if (name.Equals(nameof(ECC_brainpoolP256r1)) ||
                name.Equals(nameof(ECC_brainpoolP384r1)))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return false;
                }
                return true;
            }

            // ECC policy
            if (name.Equals(nameof(ECC_curve25519)) ||
                name.Equals(nameof(ECC_curve448)))
            {
#if CURVE25519
                return true;
#endif
            }
#endif
            return false;
        }

        /// <summary>
        /// Returns the uri associated with the display name.
        /// </summary>
        public static string GetUri(string displayName)
        {
            FieldInfo[] fields = typeof(SecurityPolicies).GetFields(BindingFlags.Public | BindingFlags.Static);
            foreach (FieldInfo field in fields)
            {
                if (field.Name == displayName && IsPlatformSupportedName(field.Name))
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
                if (policyUri == (string)field.GetValue(typeof(SecurityPolicies)) &&
                    IsPlatformSupportedName(field.Name))
                {
                    return field.Name;
                }
            }

            return null;
        }

        /// <summary>
        /// If a security policy is known and spelled according to the spec.
        /// </summary>
        /// <remarks>
        /// This functions returns only information if a security policy Uri is
        /// valid and existing according to the spec.
        /// It does not provide the information if the policy is supported
        /// by the application or by the platform.
        /// </remarks>
        public static bool IsValidSecurityPolicyUri(string policyUri)
        {
            FieldInfo[] fields = typeof(SecurityPolicies).GetFields(BindingFlags.Public | BindingFlags.Static);

            foreach (FieldInfo field in fields)
            {
                if (policyUri == (string)field.GetValue(typeof(SecurityPolicies)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the display names for all security policy uris.
        /// </summary>
        public static string[] GetDisplayNames()
        {
            FieldInfo[] fields = typeof(SecurityPolicies).GetFields(BindingFlags.Public | BindingFlags.Static);
            var names = new List<string>();

            // skip base Uri, ignore Https
            for (int ii = 1; ii < fields.Length - 1; ii++)
            {
                if (IsPlatformSupportedName(fields[ii].Name))
                {
                    names.Add(fields[ii].Name);
                }
            }

            return names.ToArray();
        }

        /// <summary>
        /// Returns the deprecated RSA security policy uri.
        /// </summary>
        public static string[] GetDefaultDeprecatedUris()
        {
            string[] defaultNames = {
                nameof(Basic128Rsa15),
                nameof(Basic256)
            };
            var defaultUris = new List<string>();
            foreach (var name in defaultNames)
            {
                var uri = GetUri(name);
                if (uri != null)
                {
                    defaultUris.Add(uri);
                }
            }
            return defaultUris.ToArray();
        }

        /// <summary>
        /// Returns the default RSA security policy uri.
        /// </summary>
        public static string[] GetDefaultUris()
        {
            string[] defaultNames = {
                nameof(Basic256Sha256),
                nameof(Aes128_Sha256_RsaOaep),
                nameof(Aes256_Sha256_RsaPss) };
            var defaultUris = new List<string>();
            foreach (var name in defaultNames)
            {
                var uri = GetUri(name);
                if (uri != null)
                {
                    defaultUris.Add(uri);
                }
            }
            return defaultUris.ToArray();
        }

        /// <summary>
        /// Returns the default ECC security policy uri.
        /// </summary>
        public static string[] GetDefaultEccUris()
        {
            string[] defaultNames = {
                nameof(ECC_nistP256),
                nameof(ECC_nistP384),
                nameof(ECC_brainpoolP256r1),
                nameof(ECC_brainpoolP384r1)
                };
            var defaultUris = new List<string>();
            foreach (var name in defaultNames)
            {
                var uri = GetUri(name);
                if (uri != null)
                {
                    defaultUris.Add(uri);
                }
            }
            return defaultUris.ToArray();
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
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                {
                    encryptedData.Algorithm = SecurityAlgorithms.RsaOaep;
                    encryptedData.Data = RsaUtils.Encrypt(plainText, certificate, RsaUtils.Padding.OaepSHA1);
                    break;
                }

                case SecurityPolicies.Basic128Rsa15:
                {
                    encryptedData.Algorithm = SecurityAlgorithms.Rsa15;
                    encryptedData.Data = RsaUtils.Encrypt(plainText, certificate, RsaUtils.Padding.Pkcs1);
                    break;
                }

                case SecurityPolicies.Aes256_Sha256_RsaPss:
                {
                    encryptedData.Algorithm = SecurityAlgorithms.RsaOaepSha256;
                    encryptedData.Data = RsaUtils.Encrypt(plainText, certificate, RsaUtils.Padding.OaepSHA256);
                    break;
                }

                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP256r1:
                case SecurityPolicies.ECC_brainpoolP384r1:
                {
                    return encryptedData;
                }

                case SecurityPolicies.None:
                {
                    break;
                }

                case SecurityPolicies.ECC_curve25519:
                case SecurityPolicies.ECC_curve448:
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
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                {
                    if (dataToDecrypt.Algorithm == SecurityAlgorithms.RsaOaep)
                    {
                        return RsaUtils.Decrypt(new ArraySegment<byte>(dataToDecrypt.Data), certificate, RsaUtils.Padding.OaepSHA1);
                    }
                    break;
                }

                case SecurityPolicies.Basic128Rsa15:
                {
                    if (dataToDecrypt.Algorithm == SecurityAlgorithms.Rsa15)
                    {
                        return RsaUtils.Decrypt(new ArraySegment<byte>(dataToDecrypt.Data), certificate, RsaUtils.Padding.Pkcs1);
                    }
                    break;
                }

                case SecurityPolicies.Aes256_Sha256_RsaPss:
                {
                    if (dataToDecrypt.Algorithm == SecurityAlgorithms.RsaOaepSha256)
                    {
                        return RsaUtils.Decrypt(new ArraySegment<byte>(dataToDecrypt.Data), certificate, RsaUtils.Padding.OaepSHA256);
                    }
                    break;
                }

                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP256r1:
                case SecurityPolicies.ECC_brainpoolP384r1:
                case SecurityPolicies.None:
                {
                    if (String.IsNullOrEmpty(dataToDecrypt.Algorithm))
                    {
                        return dataToDecrypt.Data;
                    }
                    break;
                }

                case SecurityPolicies.ECC_curve25519:
                case SecurityPolicies.ECC_curve448:
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
                    signatureData.Signature = RsaUtils.Rsa_Sign(new ArraySegment<byte>(dataToSign), certificate, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
                    break;
                }

                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Basic256Sha256:
                {
                    signatureData.Algorithm = SecurityAlgorithms.RsaSha256;
                    signatureData.Signature = RsaUtils.Rsa_Sign(new ArraySegment<byte>(dataToSign), certificate, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    break;
                }

                case SecurityPolicies.Aes256_Sha256_RsaPss:
                {
                    signatureData.Algorithm = SecurityAlgorithms.RsaPssSha256;
                    signatureData.Signature = RsaUtils.Rsa_Sign(new ArraySegment<byte>(dataToSign), certificate, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
                    break;
                }

#if ECC_SUPPORT
                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_brainpoolP256r1:
                {
                    signatureData.Algorithm = null;
                    signatureData.Signature = EccUtils.Sign(new ArraySegment<byte>(dataToSign), certificate, HashAlgorithmName.SHA256);
                    break;
                }

                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP384r1:
                {
                    signatureData.Algorithm = null;
                    signatureData.Signature = EccUtils.Sign(new ArraySegment<byte>(dataToSign), certificate, HashAlgorithmName.SHA384);
                    break;
                }
#endif

                case SecurityPolicies.None:
                {
                    signatureData.Algorithm = null;
                    signatureData.Signature = null;
                    break;
                }

                case SecurityPolicies.ECC_curve25519:
                case SecurityPolicies.ECC_curve448:
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
                        return RsaUtils.Rsa_Verify(new ArraySegment<byte>(dataToVerify), signature.Signature, certificate, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
                    }
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        "Unexpected signature algorithm for Basic256/Basic128Rsa15: {0}\n" +
                        "Expected signature algorithm: {1}",
                        signature.Algorithm,
                        SecurityAlgorithms.RsaSha1);
                }

                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Basic256Sha256:
                {
                    if (signature.Algorithm == SecurityAlgorithms.RsaSha256)
                    {
                        return RsaUtils.Rsa_Verify(new ArraySegment<byte>(dataToVerify), signature.Signature, certificate, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    }
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        "Unexpected signature algorithm for Basic256Sha256/Aes128_Sha256_RsaOaep: {0}\n" +
                        "Expected signature algorithm: {1}",
                        signature.Algorithm,
                        SecurityAlgorithms.RsaSha256);
                }

                case SecurityPolicies.Aes256_Sha256_RsaPss:
                {
                    if (signature.Algorithm == SecurityAlgorithms.RsaPssSha256)
                    {
                        return RsaUtils.Rsa_Verify(new ArraySegment<byte>(dataToVerify), signature.Signature, certificate, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
                    }
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        "Unexpected signature algorithm for Aes256_Sha256_RsaPss: {0}\n" +
                        "Expected signature algorithm : {1}",
                        signature.Algorithm,
                        SecurityAlgorithms.RsaPssSha256);
                }
#if ECC_SUPPORT
                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_brainpoolP256r1:
                {
                    return EccUtils.Verify(new ArraySegment<byte>(dataToVerify), signature.Signature, certificate, HashAlgorithmName.SHA256);
                }

                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP384r1:
                {
                    return EccUtils.Verify(new ArraySegment<byte>(dataToVerify), signature.Signature, certificate, HashAlgorithmName.SHA384);
                }
#endif
                // always accept signatures if security is not used.
                case SecurityPolicies.None:
                {
                    return true;
                }

                case SecurityPolicies.ECC_curve25519:
                case SecurityPolicies.ECC_curve448:
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
                "Unexpected security policy Uri: {0}",
                securityPolicyUri);
        }
        #endregion
    }
}
