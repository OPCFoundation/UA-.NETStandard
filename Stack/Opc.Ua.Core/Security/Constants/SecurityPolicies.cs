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
        /// The URI for the RSA_DH_AES_GCM security policy.
        /// </summary>
        public const string RSA_DH_AesGcm = BaseUri + "RSA_DH_AesGcm";

        /// <summary>
        /// The URI for the RSA_DH_ChaChaPoly security policy.
        /// </summary>
        public const string RSA_DH_ChaChaPoly = BaseUri + "RSA_DH_ChaChaPoly";

        /// <summary>
        /// The URI for the ECC_nistP256 security policy.
        /// </summary>
        public const string ECC_nistP256 = BaseUri + "ECC_nistP256";

        /// <summary>
        /// The URI for the ECC_nistP256 security policy with AES-GCM.
        /// </summary>
        public const string ECC_nistP256_AesGcm = ECC_nistP256 + "_AesGcm";

        /// <summary>
        /// The URI for the ECC_nistP256 security policy with ChaCha20Poly1305.
        /// </summary>
        public const string ECC_nistP256_ChaChaPoly = ECC_nistP256 + "_ChaChaPoly";

        /// <summary>
        /// The URI for the ECC_nistP384 security policy.
        /// </summary>
        public const string ECC_nistP384 = BaseUri + "ECC_nistP384";

        /// <summary>
        /// The URI for the ECC_nistP384 security policy with AES-GCM.
        /// </summary>
        public const string ECC_nistP384_AesGcm = ECC_nistP384 + "_AesGcm";

        /// <summary>
        /// The URI for the ECC_nistP384 security policy with ChaCha20Poly1305.
        /// </summary>
        public const string ECC_nistP384_ChaChaPoly = ECC_nistP384 + "_ChaChaPoly";

        /// <summary>
        /// The URI for the ECC_brainpoolP256r1 security policy.
        /// </summary>
        public const string ECC_brainpoolP256r1 = BaseUri + "ECC_brainpoolP256r1";

        /// <summary>
        /// The URI for the ECC_brainpoolP256r1 security policy with AES-GCM.
        /// </summary>
        public const string ECC_brainpoolP256r1_AesGcm = ECC_brainpoolP256r1 + "_AesGcm";

        /// <summary>
        /// The URI for the ECC_brainpoolP256r1 security policy with ChaCha20Poly1305.
        /// </summary>
        public const string ECC_brainpoolP256r1_ChaChaPoly = ECC_brainpoolP256r1 + "_ChaChaPoly";

        /// <summary>
        /// The URI for the ECC_brainpoolP384r1 security policy.
        /// </summary>
        public const string ECC_brainpoolP384r1 = BaseUri + "ECC_brainpoolP384r1";

        /// <summary>
        /// The URI for the ECC_brainpoolP384r1 security policy with AES-GCM.
        /// </summary>
        public const string ECC_brainpoolP384r1_AesGcm = ECC_brainpoolP384r1 + "_AesGcm";

        /// <summary>
        /// The URI for the ECC_brainpoolP384r1 security policy with ChaCha20Poly1305.
        /// </summary>
        public const string ECC_brainpoolP384r1_ChaChaPoly = ECC_brainpoolP384r1 + "_ChaChaPoly";

        /// <summary>
        /// The URI for the ECC_curve25519 security policy.brainpoolP384r1_AesGcm
        /// </summary>
        public const string ECC_curve25519 = BaseUri + "ECC_curve25519";

        /// <summary>
        /// The URI for the ECC_curve25519 security policy with AES-GCM.
        /// </summary>
        public const string ECC_curve25519_AesGcm = ECC_curve25519 + "_AesGcm";

        /// <summary>
        /// The URI for the ECC_curve25519 security policy with ChaCha20Poly1305.
        /// </summary>
        public const string ECC_curve25519_ChaChaPoly = ECC_curve25519 + "_ChaChaPoly";

        /// <summary>
        /// The URI for the ECC_curve448 deprecated security policy.
        /// </summary>
        public const string ECC_curve448 = BaseUri + "ECC_curve448";

        /// <summary>
        /// The URI for the ECC_curve448 security policy with AES-GCM.
        /// </summary>
        public const string ECC_curve448_AesGcm = ECC_curve448 + "_AesGcm";

        /// <summary>
        /// The URI for the ECC_curve448 security policy with ChaCha20Poly1305.
        /// </summary>
        public const string ECC_curve448_ChaChaPoly = ECC_curve448 + "_ChaChaPoly";

        /// <summary>
        /// The URI for the Https security policy.
        /// </summary>
        public const string Https = BaseUri + "Https";

        private static bool SupportsAesGcmPolicy()
        {
#if NET8_0_OR_GREATER
            return AesGcm.IsSupported;
#else
            return false;
#endif
        }

        private static bool SupportsChaCha20Poly1305Policy()
        {
#if NET8_0_OR_GREATER
            return ChaCha20Poly1305.IsSupported;
#else
            return false;
#endif
        }

        private static bool IsPlatformSupportedName(string name)
        {
            // If name contains BaseUri trim the BaseUri part
            if (name.StartsWith(BaseUri, StringComparison.Ordinal))
            {
                name = name.Substring(BaseUri.Length);
            }

            // all RSA
            if (name.Equals(nameof(None), StringComparison.Ordinal) ||
                name.Equals(nameof(Basic256), StringComparison.Ordinal) ||
                name.Equals(nameof(Basic128Rsa15), StringComparison.Ordinal) ||
                name.Equals(nameof(Basic256Sha256), StringComparison.Ordinal) ||
                name.Equals(nameof(Aes128_Sha256_RsaOaep), StringComparison.Ordinal))
            {
                return true;
            }

            if (name.Equals(nameof(RSA_DH_AesGcm), StringComparison.Ordinal))
            {
                return SupportsAesGcmPolicy();
            }

            if (name.Equals(nameof(RSA_DH_ChaChaPoly), StringComparison.Ordinal))
            {
                return SupportsChaCha20Poly1305Policy();
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
            if (name.Equals(nameof(ECC_nistP256_AesGcm), StringComparison.Ordinal))
            {
                return SupportsAesGcmPolicy() &&
                    Utils.IsSupportedCertificateType(ObjectTypeIds.EccNistP256ApplicationCertificateType);
            }
            if (name.Equals(nameof(ECC_nistP256_ChaChaPoly), StringComparison.Ordinal))
            {
                return SupportsChaCha20Poly1305Policy() &&
                    Utils.IsSupportedCertificateType(ObjectTypeIds.EccNistP256ApplicationCertificateType);
            }
            if (name.Equals(nameof(ECC_nistP384), StringComparison.Ordinal))
            {
                return Utils.IsSupportedCertificateType(
                    ObjectTypeIds.EccNistP384ApplicationCertificateType);
            }
            if (name.Equals(nameof(ECC_nistP384_AesGcm), StringComparison.Ordinal))
            {
                return SupportsAesGcmPolicy() &&
                    Utils.IsSupportedCertificateType(ObjectTypeIds.EccNistP384ApplicationCertificateType);
            }
            if (name.Equals(nameof(ECC_nistP384_ChaChaPoly), StringComparison.Ordinal))
            {
                return SupportsChaCha20Poly1305Policy() &&
                    Utils.IsSupportedCertificateType(ObjectTypeIds.EccNistP384ApplicationCertificateType);
            }
            if (name.Equals(nameof(ECC_brainpoolP256r1), StringComparison.Ordinal))
            {
                return Utils.IsSupportedCertificateType(
                    ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType);
            }
            if (name.Equals(nameof(ECC_brainpoolP256r1_AesGcm), StringComparison.Ordinal))
            {
                return SupportsAesGcmPolicy() &&
                    Utils.IsSupportedCertificateType(ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType);
            }
            if (name.Equals(nameof(ECC_brainpoolP256r1_ChaChaPoly), StringComparison.Ordinal))
            {
                return SupportsChaCha20Poly1305Policy() &&
                    Utils.IsSupportedCertificateType(ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType);
            }
            if (name.Equals(nameof(ECC_brainpoolP384r1), StringComparison.Ordinal))
            {
                return Utils.IsSupportedCertificateType(
                    ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType);
            }
            if (name.Equals(nameof(ECC_brainpoolP384r1_AesGcm), StringComparison.Ordinal))
            {
                return SupportsAesGcmPolicy() &&
                    Utils.IsSupportedCertificateType(ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType);
            }
            if (name.Equals(nameof(ECC_brainpoolP384r1_ChaChaPoly), StringComparison.Ordinal))
            {
                return SupportsChaCha20Poly1305Policy() &&
                    Utils.IsSupportedCertificateType(ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType);
            }

            if (name.Equals(nameof(ECC_curve25519), StringComparison.Ordinal) ||
                name.Equals(nameof(ECC_curve448), StringComparison.Ordinal))
            {
#if CURVE25519
                return true;
#endif
            }
            if (name.Equals(nameof(ECC_curve25519_AesGcm), StringComparison.Ordinal) ||
                name.Equals(nameof(ECC_curve448_AesGcm), StringComparison.Ordinal))
            {
#if CURVE25519
                return SupportsAesGcmPolicy();
#endif
            }
            if (name.Equals(nameof(ECC_curve25519_ChaChaPoly), StringComparison.Ordinal) ||
                name.Equals(nameof(ECC_curve448_ChaChaPoly), StringComparison.Ordinal))
            {
#if CURVE25519
                return SupportsChaCha20Poly1305Policy();
#endif
            }
            return false;
        }

        /// <summary>
        /// Returns the info object associated with the SecurityPolicyUri.
        /// Supports both full URI and short name (without BaseUri prefix).
        /// </summary>
        public static SecurityPolicyInfo GetInfo(string securityPolicyUri)
        {
            if (String.IsNullOrEmpty(securityPolicyUri))
            {
                return SecurityPolicyInfo.None;
            }

            // Try full URI lookup first (e.g., "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256")
            if (s_securityPolicyUriToInfo.Value.TryGetValue(securityPolicyUri, out SecurityPolicyInfo info) &&
                IsPlatformSupportedName(info.Name))
            {
                return info;
            }

            // Try short name lookup (e.g., "Basic256Sha256")
            if (s_securityPolicyNameToInfo.Value.TryGetValue(securityPolicyUri, out info) &&
                IsPlatformSupportedName(info.Name))
            {
                return info;
            }

            return null;
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
            var encryptedData = new EncryptedData { Algorithm = null };

            // check if nothing to do.
            if (plainText.Length == 0 || String.IsNullOrEmpty(securityPolicyUri))
            {
                encryptedData.Data = plainText.ToArray();
                return encryptedData;
            }

            // get the info object.
            var info = GetInfo(securityPolicyUri);

            // unsupported policy.
            if (info == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unsupported security policy: {0}",
                    securityPolicyUri);
            }

            // check if asymmetric encryption is possible.
            if (info.AsymmetricEncryptionAlgorithm != AsymmetricEncryptionAlgorithm.None)
            {
                switch (info.AsymmetricEncryptionAlgorithm)
                {
                    case AsymmetricEncryptionAlgorithm.RsaOaepSha1:
                    {
                        encryptedData.Algorithm = SecurityAlgorithms.RsaOaep;
                        encryptedData.Data = RsaUtils.Encrypt(
                            plainText,
                            certificate,
                            RsaUtils.Padding.OaepSHA1,
                            logger);
                        break;
                    }

                    case AsymmetricEncryptionAlgorithm.RsaPkcs15Sha1:
                    {
                        encryptedData.Algorithm = SecurityAlgorithms.Rsa15;
                        encryptedData.Data = RsaUtils.Encrypt(
                            plainText,
                            certificate,
                            RsaUtils.Padding.Pkcs1,
                            logger);
                        break;
                    }

                    case AsymmetricEncryptionAlgorithm.RsaOaepSha256:
                    {
                        encryptedData.Algorithm = SecurityAlgorithms.RsaOaepSha256;
                        encryptedData.Data = RsaUtils.Encrypt(
                            plainText,
                            certificate,
                            RsaUtils.Padding.OaepSHA256,
                            logger);
                        break;
                    }
                }
            }
            else
            {
                // No asymmetric encryption is defined for this policy – return the plaintext.
                encryptedData.Data = plainText.ToArray();
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

            // get the info object.
            var info = GetInfo(securityPolicyUri);

            // unsupported policy.
            if (info == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unsupported security policy: {0}",
                    securityPolicyUri);
            }

            // check if asymmetric encryption is possible.
            if (info.AsymmetricEncryptionAlgorithm != AsymmetricEncryptionAlgorithm.None)
            {
                switch (info.AsymmetricEncryptionAlgorithm)
                {
                    case AsymmetricEncryptionAlgorithm.RsaOaepSha1:
                    {
                        if (dataToDecrypt.Algorithm == SecurityAlgorithms.RsaOaep)
                        {
                            return RsaUtils.Decrypt(
                                new ArraySegment<byte>(dataToDecrypt.Data),
                                certificate,
                                RsaUtils.Padding.OaepSHA1,
                                logger);
                        }
                        break;
                    }

                    case AsymmetricEncryptionAlgorithm.RsaPkcs15Sha1:
                    {
                        if (dataToDecrypt.Algorithm == SecurityAlgorithms.Rsa15)
                        {
                            return RsaUtils.Decrypt(
                                new ArraySegment<byte>(dataToDecrypt.Data),
                                certificate,
                                RsaUtils.Padding.Pkcs1,
                                logger);
                        }
                        break;
                    }

                    default:
                    case AsymmetricEncryptionAlgorithm.RsaOaepSha256:
                    {
                        if (dataToDecrypt.Algorithm == SecurityAlgorithms.RsaOaepSha256)
                        {
                            return RsaUtils.Decrypt(
                                new ArraySegment<byte>(dataToDecrypt.Data),
                                certificate,
                                RsaUtils.Padding.OaepSHA256,
                                logger);
                        }
                        break;
                    }
                }
            }

            if (String.IsNullOrEmpty(dataToDecrypt.Algorithm))
            {
                return dataToDecrypt.Data;
            }

            throw ServiceResultException.Create(
                StatusCodes.BadIdentityTokenInvalid,
                "Unexpected encryption algorithm : {0}",
                dataToDecrypt.Algorithm);
        }

        /// <summary>
        /// Creates a signature using the security enhancements if required by the SecurityPolicy.
        /// </summary>
        public static SignatureData CreateSignatureData(
            string securityPolicyUri,
            X509Certificate2 signingCertificate,
            byte[] secureChannelSecret,
            byte[] remoteCertificate,
            byte[] remoteChannelCertificate,
            byte[] localChannelCertificate,
            byte[] remoteNonce,
            byte[] localNonce)
        {
            var signatureData = new SignatureData();

            // nothing more to do if no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                return signatureData;
            }

            // get the info object.
            var info = GetInfo(securityPolicyUri);

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
                    secureChannelSecret ?? Array.Empty<byte>(),
                    remoteCertificate ?? Array.Empty<byte>(),
                    remoteChannelCertificate ?? Array.Empty<byte>(),
                    localChannelCertificate ?? Array.Empty<byte>(),
                    remoteNonce ?? Array.Empty<byte>(),
                    localNonce ?? Array.Empty<byte>())
                :
                  Utils.Append(
                    remoteCertificate ?? Array.Empty<byte>(),
                    remoteNonce);

            return CreateSignatureData(info, signingCertificate, dataToSign);
        }

        /// <summary>
        /// Creates a signature on the data provided using the SecurityPolicy.
        /// </summary>
        public static SignatureData CreateSignatureData(
           string securityPolicyUri,
           X509Certificate2 localCertificate,
           byte[] dataToSign)
        {
            var info = GetInfo(securityPolicyUri);
            return CreateSignatureData(info, localCertificate, dataToSign);
        }

        /// <summary>
        /// Creates a signature on the data provided using the SecurityPolicy.
        /// </summary>
        public static SignatureData CreateSignatureData(
           SecurityPolicyInfo securityPolicy,
           X509Certificate2 localCertificate,
           byte[] dataToSign)
        {
            var signatureData = new SignatureData();

            // sign data.
            switch (securityPolicy.AsymmetricSignatureAlgorithm)
            {
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha1:
                    signatureData.Algorithm = SecurityAlgorithms.RsaSha1;
                    break;
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha256:
                    signatureData.Algorithm = SecurityAlgorithms.RsaSha256;
                    break;
                case AsymmetricSignatureAlgorithm.RsaPssSha256:
                    signatureData.Algorithm = SecurityAlgorithms.RsaPssSha256;
                    break;
                case AsymmetricSignatureAlgorithm.EcdsaSha256:
                case AsymmetricSignatureAlgorithm.EcdsaSha384:
                    signatureData.Algorithm = null;
                    break;
                case AsymmetricSignatureAlgorithm.None:
                    signatureData.Algorithm = null;
                    signatureData.Signature = null;
                    return signatureData;
                    ;
                default:
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityPolicyRejected,
                        "Unsupported security policy: {0}",
                        securityPolicy.Uri);
            }

            if (securityPolicy.SecureChannelEnhancements)
            {
                signatureData.Signature = null;
            }

            signatureData.Signature = CryptoUtils.Sign(
                new ArraySegment<byte>(dataToSign),
                localCertificate,
                securityPolicy.AsymmetricSignatureAlgorithm);

            return signatureData;
        }

        /// <summary>
        /// Creates a signature using the security enhancements if required by the SecurityPolicy.
        /// </summary>
        public static bool VerifySignatureData(
            SignatureData signature,
            string securityPolicyUri,
            X509Certificate2 signingCertificate,
            byte[] secureChannelSecret,
            byte[] localCertificate,
            byte[] localChannelCertificate,
            byte[] remoteChannelCertificate,
            byte[] localNonce,
            byte[] remoteNonce)
        {
            var signatureData = new SignatureData();

            // nothing more to do if no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                return true;
            }

            // get the info object.
            var info = GetInfo(securityPolicyUri);

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
                    secureChannelSecret ?? Array.Empty<byte>(),
                    localCertificate ?? Array.Empty<byte>(),
                    localChannelCertificate ?? Array.Empty<byte>(),
                    remoteChannelCertificate ?? Array.Empty<byte>(),
                    localNonce ?? Array.Empty<byte>(),
                    remoteNonce ?? Array.Empty<byte>())
                :
                  Utils.Append(
                    localCertificate ?? Array.Empty<byte>(),
                    localNonce);

            return VerifySignatureData(signature, info, signingCertificate, dataToVerify);
        }
                
        /// <summary>
        /// Verifies the signature using the SecurityPolicyUri and return true if valid.
        /// </summary>
        public static bool VerifySignatureData(
            SignatureData signature,
            string securityPolicyUri,
            X509Certificate2 signingCertificate,
            byte[] dataToVerify)
        {
            var info = GetInfo(securityPolicyUri);
            return VerifySignatureData(signature, info, signingCertificate, dataToVerify);
        }

        /// <summary>
        /// Verifies the signature using the SecurityPolicyUri and return true if valid.
        /// </summary>
        public static bool VerifySignatureData(
            SignatureData signature,
            SecurityPolicyInfo securityPolicy,
            X509Certificate2 signingCertificate,
            byte[] dataToVerify)
        {
            // check if nothing to do.
            if (signature == null)
            {
                return true;
            }

            // sign data.
            switch (securityPolicy.AsymmetricSignatureAlgorithm)
            {
                // always accept signatures if security is not used.
                case AsymmetricSignatureAlgorithm.None:
                    return true;

                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha1:
                {
                    if (signature.Algorithm == SecurityAlgorithms.RsaSha1)
                    {
                        return RsaUtils.Rsa_Verify(
                            new ArraySegment<byte>(dataToVerify),
                            signature.Signature,
                            signingCertificate,
                            HashAlgorithmName.SHA1,
                            RSASignaturePadding.Pkcs1);
                    }
                    break;
                }

                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha256:
                {
                    if (String.IsNullOrEmpty(signature.Algorithm) || signature.Algorithm == SecurityAlgorithms.RsaSha256)
                    {
                        return RsaUtils.Rsa_Verify(
                            new ArraySegment<byte>(dataToVerify),
                            signature.Signature,
                            signingCertificate,
                            HashAlgorithmName.SHA256,
                            RSASignaturePadding.Pkcs1);
                    }
                    break;
                }

                case AsymmetricSignatureAlgorithm.RsaPssSha256:
                {
                    if (String.IsNullOrEmpty(signature.Algorithm) || signature.Algorithm == SecurityAlgorithms.RsaPssSha256)
                    {
                        return RsaUtils.Rsa_Verify(
                            new ArraySegment<byte>(dataToVerify),
                            signature.Signature,
                            signingCertificate,
                            HashAlgorithmName.SHA256,
                            RSASignaturePadding.Pss);
                    }
                    break;
                }

                case AsymmetricSignatureAlgorithm.EcdsaSha256:
                {
                    if (String.IsNullOrEmpty(signature.Algorithm) || signature.Algorithm == securityPolicy.Uri)
                    {
                        return CryptoUtils.Verify(
                            new ArraySegment<byte>(dataToVerify),
                            signature.Signature,
                            signingCertificate,
                            securityPolicy.AsymmetricSignatureAlgorithm);
                    }

                    break;
                }

                case AsymmetricSignatureAlgorithm.EcdsaSha384:
                {
                    if (String.IsNullOrEmpty(signature.Algorithm) || signature.Algorithm == securityPolicy.Uri)
                    {
                        return CryptoUtils.Verify(
                            new ArraySegment<byte>(dataToVerify),
                            signature.Signature,
                            signingCertificate,
                            securityPolicy.AsymmetricSignatureAlgorithm);
                    }

                    break;
                }
            }

            throw ServiceResultException.Create(
                StatusCodes.BadSecurityChecksFailed,
                "Unexpected SignatureData algorithm: {0}",
                signature.Algorithm);
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

        /// <summary>
        /// Creates a dictionary of uris to SecurityPolicyInfo excluding base uri
        /// </summary>
        private static readonly Lazy<IReadOnlyDictionary<string, SecurityPolicyInfo>> s_securityPolicyUriToInfo =
            new(() =>
            {
#if NET8_0_OR_GREATER
                return s_securityPolicyNameToInfo.Value.ToFrozenDictionary(k => k.Value.Uri, k => k.Value);
#else
                return new ReadOnlyDictionary<string, SecurityPolicyInfo>(
                    s_securityPolicyNameToInfo.Value.ToDictionary(k => k.Value.Uri, k => k.Value));
#endif
            });

        /// <summary>
        /// Creates a dictionary for names to SecurityPolicyInfo excluding base uri
        /// </summary>
        private static readonly Lazy<IReadOnlyDictionary<string, SecurityPolicyInfo>> s_securityPolicyNameToInfo =
            new(() =>
            {
                FieldInfo[] policyFields = typeof(SecurityPolicies).GetFields(
                    BindingFlags.Public | BindingFlags.Static);

                FieldInfo[] infoFields = typeof(SecurityPolicyInfo).GetFields(
                    BindingFlags.Public | BindingFlags.Static);

                var keyValuePairs = new Dictionary<string, SecurityPolicyInfo>();
                foreach (FieldInfo field in policyFields)
                {
                    string policyUri = (string)field.GetValue(typeof(SecurityPolicies));
                    if (field.Name == nameof(BaseUri) ||
                        field.Name == nameof(Https) ||
                        !policyUri.StartsWith(BaseUri, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // Find the corresponding SecurityPolicyInfo field by name
                    FieldInfo infoField = Array.Find(infoFields, f => f.Name == field.Name);
                    if (infoField != null && infoField.FieldType == typeof(SecurityPolicyInfo))
                    {
                        SecurityPolicyInfo info = (SecurityPolicyInfo)infoField.GetValue(null);
                        keyValuePairs.Add(field.Name, info);
                    }
                    else
                    {
                        // Fallback to creating a minimal instance for unknown policies
                        keyValuePairs.Add(field.Name, new SecurityPolicyInfo(policyUri, field.Name));
                    }
                }
#if NET8_0_OR_GREATER
                return keyValuePairs.ToFrozenDictionary();
#else
                return new ReadOnlyDictionary<string, SecurityPolicyInfo>(keyValuePairs);
#endif
            });
    }
}
