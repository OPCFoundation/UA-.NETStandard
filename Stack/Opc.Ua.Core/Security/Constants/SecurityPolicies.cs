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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;


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
        public const string RSA_DH_AES_GCM = BaseUri + "RSA_DH_AES_GCM";

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
        public const string ECC_nistP256_AES = ECC_nistP256 + "_AES";

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
        public const string ECC_nistP384_AES = ECC_nistP384 + "_AES";

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
        public const string ECC_brainpoolP256r1_AES = ECC_brainpoolP256r1 + "_AES";

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
        public const string ECC_brainpoolP384r1_AES = ECC_brainpoolP384r1 + "_AES";

        /// <summary>
        /// The URI for the ECC_brainpoolP384r1 security policy with ChaCha20Poly1305.
        /// </summary>
        public const string ECC_brainpoolP384r1_ChaChaPoly = ECC_brainpoolP384r1 + "_ChaChaPoly";

        /// <summary>
        /// The URI for the ECC_curve25519 security policy.
        /// </summary>
        public const string ECC_curve25519 = BaseUri + "ECC_curve25519";

        /// <summary>
        /// The URI for the ECC_curve25519 security policy with AES-GCM.
        /// </summary>
        public const string ECC_curve25519_AES = ECC_curve25519 + "_AES";

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
        public const string ECC_curve448_AES = ECC_curve448 + "_AES";

        /// <summary>
        /// The URI for the ECC_curve448 security policy with ChaCha20Poly1305.
        /// </summary>
        public const string ECC_curve448_ChaChaPoly = ECC_curve448 + "_ChaChaPoly";

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

#if ECC_SUPPORT
            // ECC policy
            if (name.Equals(nameof(ECC_nistP256), StringComparison.Ordinal) ||
                name.Equals(nameof(ECC_nistP256_AES), StringComparison.Ordinal) ||
                name.Equals(nameof(ECC_nistP256_ChaChaPoly), StringComparison.Ordinal))
            {
                return Utils.IsSupportedCertificateType(
                    ObjectTypeIds.EccNistP256ApplicationCertificateType);
            }
            if (name.Equals(nameof(ECC_nistP384), StringComparison.Ordinal) ||
                name.Equals(nameof(ECC_nistP384_AES), StringComparison.Ordinal) ||
                name.Equals(nameof(ECC_nistP384_ChaChaPoly), StringComparison.Ordinal))
            {
                return Utils.IsSupportedCertificateType(
                    ObjectTypeIds.EccNistP384ApplicationCertificateType);
            }
            if (name.Equals(nameof(ECC_brainpoolP256r1), StringComparison.Ordinal) ||
                name.Equals(nameof(ECC_brainpoolP256r1_AES), StringComparison.Ordinal) ||
                name.Equals(nameof(ECC_brainpoolP256r1_ChaChaPoly), StringComparison.Ordinal))
            {
                return Utils.IsSupportedCertificateType(
                    ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType);
            }
            if (name.Equals(nameof(ECC_brainpoolP384r1), StringComparison.Ordinal) ||
                name.Equals(nameof(ECC_brainpoolP384r1_AES), StringComparison.Ordinal) ||
                name.Equals(nameof(ECC_brainpoolP384r1_ChaChaPoly), StringComparison.Ordinal))
            {
                return Utils.IsSupportedCertificateType(
                    ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType);
            }

            // ECC policy
            if (name.Equals(nameof(ECC_curve25519), StringComparison.Ordinal) ||
                name.Equals(nameof(ECC_curve25519_AES), StringComparison.Ordinal) ||
                name.Equals(nameof(ECC_curve25519_ChaChaPoly), StringComparison.Ordinal) ||
                name.Equals(nameof(ECC_curve448), StringComparison.Ordinal) ||
                name.Equals(nameof(ECC_curve448_AES), StringComparison.Ordinal) ||
                name.Equals(nameof(ECC_curve448_ChaChaPoly), StringComparison.Ordinal))
            {
#if CURVE25519
                return true;
#endif
            }
#endif
            return false;
        }

        /// <summary>
        /// Returns the info object associated with the SecurityPolicyUri.
        /// </summary>
        public static SecurityPolicyInfo GetInfo(string securityPolicyUri)
        {
            if (s_securityPolicyUriToInfo.Value.TryGetValue(securityPolicyUri, out var info) && IsPlatformSupportedName(info.Name))
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
            byte[] plainText)
        {
            var encryptedData = new EncryptedData { Algorithm = null, Data = plainText };

            // check if nothing to do.
            if (plainText == null)
            {
                return encryptedData;
            }

            // nothing more to do if no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
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
                            RsaUtils.Padding.OaepSHA1);
                        break;
                    }

                    case AsymmetricEncryptionAlgorithm.RsaPkcs15Sha1:
                    {
                        encryptedData.Algorithm = SecurityAlgorithms.Rsa15;
                        encryptedData.Data = RsaUtils.Encrypt(
                            plainText,
                            certificate,
                            RsaUtils.Padding.Pkcs1);
                        break;
                    }

                    case AsymmetricEncryptionAlgorithm.RsaOaepSha256:
                    {
                        encryptedData.Algorithm = SecurityAlgorithms.RsaOaepSha256;
                        encryptedData.Data = RsaUtils.Encrypt(
                            plainText,
                            certificate,
                            RsaUtils.Padding.OaepSHA256);
                        break;
                    }
                }
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
            EncryptedData dataToDecrypt)
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
                                RsaUtils.Padding.OaepSHA1);
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
                                RsaUtils.Padding.Pkcs1);
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
                                RsaUtils.Padding.OaepSHA256);
                        }
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(dataToDecrypt.Algorithm))
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

            System.Console.WriteLine(
                $"CreateSignatureData\r\n" +
                $"secureChannelSecret: {ToFragment(secureChannelSecret)}\r\n" +
                $"remoteCertificate: {ToFragment(remoteCertificate)}\r\n" +
                $"remoteChannelCertificate: {ToFragment(remoteChannelCertificate)}\r\n" +
                $"localChannelCertificate: {ToFragment(localChannelCertificate)}\r\n" +
                $"remoteNonce: {ToFragment(remoteNonce)}\r\n" +
                $"localNonce: {ToFragment(localNonce)}"
            );

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
                    signatureData.Signature = RsaUtils.Rsa_Sign(
                        new ArraySegment<byte>(dataToSign),
                        localCertificate,
                        HashAlgorithmName.SHA1,
                        RSASignaturePadding.Pkcs1);
                    break;
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha256:
                    signatureData.Algorithm = SecurityAlgorithms.RsaSha256;
                    signatureData.Signature = RsaUtils.Rsa_Sign(
                        new ArraySegment<byte>(dataToSign),
                        localCertificate,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pkcs1);
                    break;
                case AsymmetricSignatureAlgorithm.RsaPssSha256:
                    signatureData.Algorithm = SecurityAlgorithms.RsaPssSha256;
                    signatureData.Signature = RsaUtils.Rsa_Sign(
                        new ArraySegment<byte>(dataToSign),
                        localCertificate,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pss);
                    break;
#if ECC_SUPPORT
                case AsymmetricSignatureAlgorithm.EcdsaSha256:
                    signatureData.Algorithm = null;
                    signatureData.Signature = EccUtils.Sign(
                        new ArraySegment<byte>(dataToSign),
                        localCertificate,
                        HashAlgorithmName.SHA256);
                    break;
                case AsymmetricSignatureAlgorithm.EcdsaSha384:
                    signatureData.Algorithm = null;
                    signatureData.Signature = EccUtils.Sign(
                        new ArraySegment<byte>(dataToSign),
                        localCertificate,
                        HashAlgorithmName.SHA384);
                    break;
#endif
                case AsymmetricSignatureAlgorithm.None:
                    signatureData.Algorithm = null;
                    signatureData.Signature = null;
                    break;
                default:
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityPolicyRejected,
                        "Unsupported security policy: {0}",
                        securityPolicy.Uri);
            }

            return signatureData;
        }

        static string ToFragment(byte[] input)
        {
            if (input != null)
            {
                if (input.Length < 8)
                {
                    return Utils.ToHexString(input);
                }

                return Utils.ToHexString(input).Substring(0, 16);
            }

            return "null";
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

            System.Console.WriteLine(
                $"VerifySignatureData\r\n" +
                $"secureChannelSecret: {ToFragment(secureChannelSecret)}\r\n" +
                $"localCertificate: {ToFragment(localCertificate)}\r\n" +
                $"localChannelCertificate: {ToFragment(localChannelCertificate)}\r\n" +
                $"remoteChannelCertificate: {ToFragment(remoteChannelCertificate)}\r\n" +
                $"localNonce: {ToFragment(localNonce)}\r\n" +
                $"remoteNonce: {ToFragment(remoteNonce)}"
            );

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
            SecurityPolicyInfo securityPolicy,
            X509Certificate2 remoteCertificate,
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
                            remoteCertificate,
                            HashAlgorithmName.SHA1,
                            RSASignaturePadding.Pkcs1);
                    }
                    break;
                }

                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha256:
                {
                    if (signature.Algorithm == SecurityAlgorithms.RsaSha256)
                    {
                        return RsaUtils.Rsa_Verify(
                            new ArraySegment<byte>(dataToVerify),
                            signature.Signature,
                            remoteCertificate,
                            HashAlgorithmName.SHA256,
                            RSASignaturePadding.Pkcs1);
                    }
                    break;
                }

                case AsymmetricSignatureAlgorithm.RsaPssSha256:
                {
                    if (signature.Algorithm == SecurityAlgorithms.RsaPssSha256)
                    {
                        return RsaUtils.Rsa_Verify(
                            new ArraySegment<byte>(dataToVerify),
                            signature.Signature,
                            remoteCertificate,
                            HashAlgorithmName.SHA256,
                            RSASignaturePadding.Pss);
                    }
                    break;
                }

#if ECC_SUPPORT
                case AsymmetricSignatureAlgorithm.EcdsaSha256:
                {
                    if (signature.Algorithm == null || signature.Algorithm == securityPolicy.Uri)
                    {
                        return EccUtils.Verify(
                            new ArraySegment<byte>(dataToVerify),
                            signature.Signature,
                            remoteCertificate,
                            HashAlgorithmName.SHA256);
                    }

                    break;
                }

                case AsymmetricSignatureAlgorithm.EcdsaSha384:
                {
                    if (signature.Algorithm == null || signature.Algorithm == securityPolicy.Uri)
                    {
                        return EccUtils.Verify(
                            new ArraySegment<byte>(dataToVerify),
                            signature.Signature,
                            remoteCertificate,
                            HashAlgorithmName.SHA384);
                    }

                    break;
                }
#endif
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
                        !policyUri.StartsWith(BaseUri))
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
        /// Creates a dictionary for uri to info objects.
        /// </summary>
        private static readonly Lazy<IReadOnlyDictionary<string, SecurityPolicyInfo>> s_securityPolicyUriToInfo =
            new(() =>
            {
                FieldInfo[] fields = typeof(SecurityPolicyInfo).GetFields(
                    BindingFlags.Public | BindingFlags.Static);

                var keyValuePairs = new Dictionary<string, SecurityPolicyInfo>();
                foreach (FieldInfo field in fields)
                {
                    SecurityPolicyInfo info = field.GetValue(typeof(SecurityPolicyInfo)) as SecurityPolicyInfo;

                    if (info == null)
                    {
                        continue;
                    }

                    keyValuePairs.Add(info.Uri, info);
                }
#if NET8_0_OR_GREATER
                return keyValuePairs.ToFrozenDictionary();
#else
                return new ReadOnlyDictionary<string, SecurityPolicyInfo>(keyValuePairs);
#endif
            });
    }
}
