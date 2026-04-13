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
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#else
using System.Linq;
using System.Collections.ObjectModel;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// Defines constants for key security policies.
    /// </summary>
    public static class SecurityPolicies
    {
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

        private static bool IsPlatformSupportedName(string name)
        {
            // all RSA
            if (name.Equals(nameof(None), StringComparison.Ordinal) ||
                name.Equals(nameof(Basic256), StringComparison.Ordinal) ||
                name.Equals(nameof(Basic128Rsa15), StringComparison.Ordinal) ||
                name.Equals(nameof(Basic256Sha256), StringComparison.Ordinal) ||
                name.Equals(nameof(Aes128_Sha256_RsaOaep), StringComparison.Ordinal))
            {
                return true;
            }

            if (name.Equals(nameof(Aes256_Sha256_RsaPss), StringComparison.Ordinal) &&
                RsaUtils.IsSupportingRSAPssSign.Value)
            {
                return true;
            }
            if (name.Equals(nameof(ECC_nistP256), StringComparison.Ordinal))
            {
                return Utils.IsSupportedCertificateType(
                    ObjectTypeIds.EccNistP256ApplicationCertificateType);
            }
            if (name.Equals(nameof(ECC_nistP384), StringComparison.Ordinal))
            {
                return Utils.IsSupportedCertificateType(
                    ObjectTypeIds.EccNistP384ApplicationCertificateType);
            }
            if (name.Equals(nameof(ECC_brainpoolP256r1), StringComparison.Ordinal))
            {
                return Utils.IsSupportedCertificateType(
                    ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType);
            }
            if (name.Equals(nameof(ECC_brainpoolP384r1), StringComparison.Ordinal))
            {
                return Utils.IsSupportedCertificateType(
                    ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType);
            }

            if (name.Equals(nameof(ECC_curve25519), StringComparison.Ordinal) ||
                name.Equals(nameof(ECC_curve448), StringComparison.Ordinal))
            {
#if CURVE25519
                return true;
#endif
            }
            return false;
        }

        /// <summary>
        /// Returns the uri associated with the display name. This includes http and all
        /// other supported platform security policies.
        /// </summary>
        public static string GetUri(string displayName)
        {
            if (s_securityPolicyNameToUri.Value.TryGetValue(displayName, out string policyUri) &&
                IsPlatformSupportedName(displayName))
            {
                return policyUri;
            }

            return null;
        }

        /// <summary>
        /// Returns a display name for a security policy uri. This includes http and all
        /// other supported platform security policies.
        /// </summary>
        public static string GetDisplayName(string policyUri)
        {
            if (s_securityPolicyUriToName.Value.TryGetValue(policyUri, out string displayName) &&
                IsPlatformSupportedName(displayName))
            {
                return displayName;
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
            return s_securityPolicyUriToName.Value.ContainsKey(policyUri);
        }

        /// <summary>
        /// Returns the display names for all security policy uris including https.
        /// </summary>
        public static string[] GetDisplayNames()
        {
            var names = new List<string>(s_securityPolicyUriToName.Value.Count);

            foreach (string displayName in s_securityPolicyUriToName.Value.Values)
            {
                if (IsPlatformSupportedName(displayName))
                {
                    names.Add(displayName);
                }
            }

            return [.. names];
        }

        /// <summary>
        /// Returns the deprecated RSA security policy uri.
        /// </summary>
        public static string[] GetDefaultDeprecatedUris()
        {
            string[] defaultNames = [nameof(Basic128Rsa15), nameof(Basic256)];
            var defaultUris = new List<string>();
            foreach (string name in defaultNames)
            {
                string uri = GetUri(name);
                if (uri != null)
                {
                    defaultUris.Add(uri);
                }
            }
            return [.. defaultUris];
        }

        /// <summary>
        /// Returns the default RSA security policy uri.
        /// </summary>
        public static string[] GetDefaultUris()
        {
            string[] defaultNames =
            [
                nameof(Basic256Sha256),
                nameof(Aes128_Sha256_RsaOaep),
                nameof(Aes256_Sha256_RsaPss)
            ];
            var defaultUris = new List<string>();
            foreach (string name in defaultNames)
            {
                string uri = GetUri(name);
                if (uri != null)
                {
                    defaultUris.Add(uri);
                }
            }
            return [.. defaultUris];
        }

        /// <summary>
        /// Returns the default ECC security policy uri.
        /// </summary>
        public static string[] GetDefaultEccUris()
        {
            string[] defaultNames =
            [
                nameof(ECC_nistP256),
                nameof(ECC_nistP384),
                nameof(ECC_brainpoolP256r1),
                nameof(ECC_brainpoolP384r1)
            ];
            var defaultUris = new List<string>();
            foreach (string name in defaultNames)
            {
                string uri = GetUri(name);
                if (uri != null)
                {
                    defaultUris.Add(uri);
                }
            }
            return [.. defaultUris];
        }

        /// <summary>
        /// Encrypts the text using the SecurityPolicyUri and returns the result.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static EncryptedData Encrypt(
            X509Certificate2 certificate,
            string securityPolicyUri,
            ReadOnlySpan<byte> plainText,
            ILogger logger)
        {
            var encryptedData = new EncryptedData
            {
                Algorithm = null,
                Data = plainText.IsEmpty ? null : plainText.ToArray()
            };

            // check if nothing to do.
            if (plainText.IsEmpty)
            {
                return encryptedData;
            }

            // nothing more to do if no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                return encryptedData;
            }

            // encrypt data.
            switch (securityPolicyUri)
            {
                case Basic256:
                case Basic256Sha256:
                case Aes128_Sha256_RsaOaep:
                    encryptedData.Algorithm = SecurityAlgorithms.RsaOaep;
                    encryptedData.Data = RsaUtils.Encrypt(
                        plainText,
                        certificate,
                        RsaUtils.Padding.OaepSHA1,
                        logger);
                    break;
                case Basic128Rsa15:
                    encryptedData.Algorithm = SecurityAlgorithms.Rsa15;
                    encryptedData.Data = RsaUtils.Encrypt(
                        plainText,
                        certificate,
                        RsaUtils.Padding.Pkcs1,
                        logger);
                    break;
                case Aes256_Sha256_RsaPss:
                    encryptedData.Algorithm = SecurityAlgorithms.RsaOaepSha256;
                    encryptedData.Data = RsaUtils.Encrypt(
                        plainText,
                        certificate,
                        RsaUtils.Padding.OaepSHA256,
                        logger);
                    break;
                case ECC_nistP256:
                case ECC_nistP384:
                case ECC_brainpoolP256r1:
                case ECC_brainpoolP384r1:
                    return encryptedData;
                case None:
                    break;
                default:
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityPolicyRejected,
                        "Unsupported security policy: {0}",
                        securityPolicyUri);
            }

            return encryptedData;
        }

        /// <summary>
        /// Decrypts the CipherText using the SecurityPolicyUri and returns the PlainText.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static byte[] Decrypt(
            X509Certificate2 certificate,
            string securityPolicyUri,
            EncryptedData dataToDecrypt,
            ILogger logger)
        {
            // check if nothing to do.
            if (dataToDecrypt == null)
            {
                return null;
            }

            // nothing more to do if no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                return dataToDecrypt.Data;
            }

            // decrypt data.
            switch (securityPolicyUri)
            {
                case Basic256:
                case Basic256Sha256:
                case Aes128_Sha256_RsaOaep:
                    if (dataToDecrypt.Algorithm == SecurityAlgorithms.RsaOaep)
                    {
                        return RsaUtils.Decrypt(
                            new ArraySegment<byte>(dataToDecrypt.Data),
                            certificate,
                            RsaUtils.Padding.OaepSHA1,
                            logger);
                    }
                    break;
                case Basic128Rsa15:
                    if (dataToDecrypt.Algorithm == SecurityAlgorithms.Rsa15)
                    {
                        return RsaUtils.Decrypt(
                            new ArraySegment<byte>(dataToDecrypt.Data),
                            certificate,
                            RsaUtils.Padding.Pkcs1,
                            logger);
                    }
                    break;
                case Aes256_Sha256_RsaPss:
                    if (dataToDecrypt.Algorithm == SecurityAlgorithms.RsaOaepSha256)
                    {
                        return RsaUtils.Decrypt(
                            new ArraySegment<byte>(dataToDecrypt.Data),
                            certificate,
                            RsaUtils.Padding.OaepSHA256,
                            logger);
                    }
                    break;
                case ECC_nistP256:
                case ECC_nistP384:
                case ECC_brainpoolP256r1:
                case ECC_brainpoolP384r1:
                case None:
                    if (string.IsNullOrEmpty(dataToDecrypt.Algorithm))
                    {
                        return dataToDecrypt.Data;
                    }
                    break;
                default:
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityPolicyRejected,
                        "Unsupported security policy: {0}",
                        securityPolicyUri);
            }

            throw ServiceResultException.Create(
                StatusCodes.BadIdentityTokenInvalid,
                "Unexpected encryption algorithm : {0}",
                dataToDecrypt.Algorithm);
        }

        /// <summary>
        /// Signs the data using the SecurityPolicyUri and returns the signature.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static SignatureData Sign(
            X509Certificate2 certificate,
            string securityPolicyUri,
            byte[] dataToSign)
        {
            var signatureData = new SignatureData();

            // check if nothing to do.
            if (dataToSign == null)
            {
                return signatureData;
            }

            // nothing more to do if no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                return signatureData;
            }

            // sign data.
            switch (securityPolicyUri)
            {
                case Basic256:
                case Basic128Rsa15:
                    signatureData.Algorithm = SecurityAlgorithms.RsaSha1;
                    signatureData.Signature = RsaUtils.Rsa_Sign(
                        new ArraySegment<byte>(dataToSign),
                        certificate,
                        HashAlgorithmName.SHA1,
                        RSASignaturePadding.Pkcs1).ToByteString();
                    break;
                case Aes128_Sha256_RsaOaep:
                case Basic256Sha256:
                    signatureData.Algorithm = SecurityAlgorithms.RsaSha256;
                    signatureData.Signature = RsaUtils.Rsa_Sign(
                        new ArraySegment<byte>(dataToSign),
                        certificate,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pkcs1).ToByteString();
                    break;
                case Aes256_Sha256_RsaPss:
                    signatureData.Algorithm = SecurityAlgorithms.RsaPssSha256;
                    signatureData.Signature = RsaUtils.Rsa_Sign(
                        new ArraySegment<byte>(dataToSign),
                        certificate,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pss).ToByteString();
                    break;
                case ECC_nistP256:
                case ECC_brainpoolP256r1:
                    signatureData.Algorithm = null;
                    signatureData.Signature = EccUtils.Sign(
                        new ArraySegment<byte>(dataToSign),
                        certificate,
                        HashAlgorithmName.SHA256).ToByteString();
                    break;
                case ECC_nistP384:
                case ECC_brainpoolP384r1:
                    signatureData.Algorithm = null;
                    signatureData.Signature = EccUtils.Sign(
                        new ArraySegment<byte>(dataToSign),
                        certificate,
                        HashAlgorithmName.SHA384).ToByteString();
                    break;
                case None:
                    signatureData.Algorithm = null;
                    signatureData.Signature = default;
                    break;
                default:
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityPolicyRejected,
                        "Unsupported security policy: {0}",
                        securityPolicyUri);
            }

            return signatureData;
        }

        /// <summary>
        /// Verifies the signature using the SecurityPolicyUri and return true if valid.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static bool Verify(
            X509Certificate2 certificate,
            string securityPolicyUri,
            byte[] dataToVerify,
            SignatureData signature)
        {
            // check if nothing to do.
            if (signature == null)
            {
                return true;
            }

            // nothing more to do if no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                return true;
            }

            // decrypt data.
            switch (securityPolicyUri)
            {
                case Basic256:
                case Basic128Rsa15:
                    if (signature.Algorithm == SecurityAlgorithms.RsaSha1)
                    {
                        return RsaUtils.Rsa_Verify(
                            new ArraySegment<byte>(dataToVerify),
                            signature.Signature.ToArray(),
                            certificate,
                            HashAlgorithmName.SHA1,
                            RSASignaturePadding.Pkcs1);
                    }
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        "Unexpected signature algorithm for Basic256/Basic128Rsa15: {0}\n" +
                        "Expected signature algorithm: {1}",
                        signature.Algorithm,
                        SecurityAlgorithms.RsaSha1);
                case Aes128_Sha256_RsaOaep:
                case Basic256Sha256:
                    if (signature.Algorithm == SecurityAlgorithms.RsaSha256)
                    {
                        return RsaUtils.Rsa_Verify(
                            new ArraySegment<byte>(dataToVerify),
                            signature.Signature.ToArray(),
                            certificate,
                            HashAlgorithmName.SHA256,
                            RSASignaturePadding.Pkcs1);
                    }
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        "Unexpected signature algorithm for Basic256Sha256/Aes128_Sha256_RsaOaep: {0}\n" +
                        "Expected signature algorithm: {1}",
                        signature.Algorithm,
                        SecurityAlgorithms.RsaSha256);
                case Aes256_Sha256_RsaPss:
                    if (signature.Algorithm == SecurityAlgorithms.RsaPssSha256)
                    {
                        return RsaUtils.Rsa_Verify(
                            new ArraySegment<byte>(dataToVerify),
                            signature.Signature.ToArray(),
                            certificate,
                            HashAlgorithmName.SHA256,
                            RSASignaturePadding.Pss);
                    }
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        "Unexpected signature algorithm for Aes256_Sha256_RsaPss: {0}\n" +
                        "Expected signature algorithm : {1}",
                        signature.Algorithm,
                        SecurityAlgorithms.RsaPssSha256);
                case ECC_nistP256:
                case ECC_brainpoolP256r1:
                    return EccUtils.Verify(
                        new ArraySegment<byte>(dataToVerify),
                        signature.Signature.ToArray(),
                        certificate,
                        HashAlgorithmName.SHA256);
                case ECC_nistP384:
                case ECC_brainpoolP384r1:
                    return EccUtils.Verify(
                        new ArraySegment<byte>(dataToVerify),
                        signature.Signature.ToArray(),
                        certificate,
                        HashAlgorithmName.SHA384);
                // always accept signatures if security is not used.
                case None:
                    return true;
                default:
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityPolicyRejected,
                        "Unsupported security policy: {0}",
                        securityPolicyUri);
            }
        }

        /// <summary>
        /// Creates a dictionary of uris to name excluding base uri
        /// </summary>
        private static readonly Lazy<IReadOnlyDictionary<string, string>> s_securityPolicyUriToName =
            new(() =>
            {
#if NET8_0_OR_GREATER
                return s_securityPolicyNameToUri.Value.ToFrozenDictionary(k => k.Value, k => k.Key);
#else
                return new ReadOnlyDictionary<string, string>(
                    s_securityPolicyNameToUri.Value.ToDictionary(k => k.Value, k => k.Key));
#endif
            });

        /// <summary>
        /// Creates a dictionary for names to uri excluding base uri
        /// </summary>
        private static readonly Lazy<IReadOnlyDictionary<string, string>> s_securityPolicyNameToUri =
            new(() =>
            {
                FieldInfo[] fields = typeof(SecurityPolicies).GetFields(
                    BindingFlags.Public | BindingFlags.Static);

                var keyValuePairs = new Dictionary<string, string>();
                foreach (FieldInfo field in fields)
                {
                    string policyUri = (string)field.GetValue(typeof(SecurityPolicies));
                    if (field.Name == nameof(BaseUri) ||
                        field.Name == nameof(Https) ||
                        !policyUri.StartsWith(BaseUri, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    keyValuePairs.Add(field.Name, policyUri);
                }
#if NET8_0_OR_GREATER
                return keyValuePairs.ToFrozenDictionary();
#else
                return new ReadOnlyDictionary<string, string>(keyValuePairs);
#endif
            });
    }
}
