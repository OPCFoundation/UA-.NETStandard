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
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
#if CURVE25519
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Digests;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// Defines functions to implement ECC cryptography.
    /// </summary>
    public static class CryptoUtils
    {
        /// <summary>
        /// The name of the NIST P-256 curve.
        /// </summary>
        public const string NistP256 = nameof(NistP256);

        /// <summary>
        /// The name of the NIST P-384 curve.
        /// </summary>
        public const string NistP384 = nameof(NistP384);

        /// <summary>
        /// The name of the BrainpoolP256r1 curve.
        /// </summary>
        public const string BrainpoolP256r1 = nameof(BrainpoolP256r1);

        /// <summary>
        /// The name of the BrainpoolP384r1 curve.
        /// </summary>
        public const string BrainpoolP384r1 = nameof(BrainpoolP384r1);

        internal const string NistP256KeyParameters = "06-08-2A-86-48-CE-3D-03-01-07";
        internal const string NistP384KeyParameters = "06-05-2B-81-04-00-22";
        internal const string BrainpoolP256r1KeyParameters = "06-09-2B-24-03-03-02-08-01-01-07";
        internal const string BrainpoolP384r1KeyParameters = "06-09-2B-24-03-03-02-08-01-01-0B";

        /// <summary>
        /// Returns true if the certificate is an ECC certificate.
        /// </summary>
        public static bool IsEccPolicy(string securityPolicyUri)
        {
            var info = SecurityPolicies.GetInfo(securityPolicyUri);

            if (info != null)
            {
                return info.CertificateKeyFamily == CertificateKeyFamily.ECC;
            }

            return false;
        }

        /// <summary>
        /// Returns the NodeId for the certificate type for the specified certificate.
        /// </summary>
        public static NodeId GetEccCertificateTypeId(X509Certificate2 certificate)
        {
            string keyAlgorithm = certificate.GetKeyAlgorithm();
            if (keyAlgorithm != Oids.ECPublicKey)
            {
                return NodeId.Null;
            }

            PublicKey encodedPublicKey = certificate.PublicKey;
            switch (BitConverter.ToString(encodedPublicKey.EncodedParameters.RawData))
            {
                // nistP256
                case NistP256KeyParameters:
                    return ObjectTypeIds.EccNistP256ApplicationCertificateType;
                // nistP384
                case NistP384KeyParameters:
                    return ObjectTypeIds.EccNistP384ApplicationCertificateType;
                // brainpoolP256r1
                case BrainpoolP256r1KeyParameters:
                    return ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType;
                // brainpoolP384r1
                case BrainpoolP384r1KeyParameters:
                    return ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType;
                default:
                    return NodeId.Null;
            }
        }

        /// <summary>
        /// returns an ECCCurve if there is a matching supported curve for the provided
        /// certificate type id. if no supported ECC curve is found null is returned.
        /// </summary>
        /// <param name="certificateType">the  application certificatate type node id</param>
        /// <returns>the ECCCurve, null if certificatate type id has no matching supported ECC curve</returns>
        public static ECCurve? GetCurveFromCertificateTypeId(NodeId certificateType)
        {
            ECCurve? curve = null;

            if (certificateType == ObjectTypeIds.EccApplicationCertificateType ||
                certificateType == ObjectTypeIds.EccNistP256ApplicationCertificateType)
            {
                curve = ECCurve.NamedCurves.nistP256;
            }
            else if (certificateType == ObjectTypeIds.EccNistP384ApplicationCertificateType)
            {
                curve = ECCurve.NamedCurves.nistP384;
            }
            else if (certificateType == ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType)
            {
                curve = ECCurve.NamedCurves.brainpoolP256r1;
            }
            else if (certificateType == ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType)
            {
                curve = ECCurve.NamedCurves.brainpoolP384r1;
            }
#if CURVE25519
            else if (certificateType == ObjectTypeIds.EccCurve25519ApplicationCertificateType)
            {
                curve = default(ECCurve);
            }
            else if (certificateType == ObjectTypeIds.EccCurve448ApplicationCertificateType)
            {
                curve = default(ECCurve);
            }
#endif
            return curve;
        }

        /// <summary>
        /// Returns the signature algorithm for the specified certificate.
        /// </summary>
        public static string GetECDsaQualifier(X509Certificate2 certificate)
        {
            if (X509Utils.IsECDsaSignature(certificate))
            {
                const string signatureQualifier = "ECDsa";
                PublicKey encodedPublicKey = certificate.PublicKey;

                // New values can be determined by running the dotted-decimal OID value
                // through BitConverter.ToString(CryptoConfig.EncodeOID(dottedDecimal));

                switch (BitConverter.ToString(encodedPublicKey.EncodedParameters.RawData))
                {
                    case NistP256KeyParameters:
                        return NistP256;
                    case NistP384KeyParameters:
                        return NistP384;
                    case BrainpoolP256r1KeyParameters:
                        return BrainpoolP256r1;
                    case BrainpoolP384r1KeyParameters:
                        return BrainpoolP384r1;
                    default:
                        return signatureQualifier;
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// Returns the public key for the specified certificate.
        /// </summary>
        public static ECDsa GetPublicKey(X509Certificate2 certificate)
        {
            return GetPublicKey(certificate, out string[] _);
        }

        /// <summary>
        /// Returns the public key for the specified certificate and outputs the security policy uris.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="NotImplementedException"></exception>
        public static ECDsa GetPublicKey(
            X509Certificate2 certificate,
            out string[] securityPolicyUris)
        {
            securityPolicyUris = null;

            if (certificate == null)
            {
                return null;
            }

            string keyAlgorithm = certificate.GetKeyAlgorithm();

            if (keyAlgorithm != Oids.ECPublicKey)
            {
                return null;
            }

            const X509KeyUsageFlags kSufficientFlags =
                X509KeyUsageFlags.KeyAgreement |
                X509KeyUsageFlags.DigitalSignature |
                X509KeyUsageFlags.NonRepudiation |
                X509KeyUsageFlags.CrlSign |
                X509KeyUsageFlags.KeyCertSign;

            foreach (X509Extension extension in certificate.Extensions)
            {
                if (extension.Oid.Value == "2.5.29.15")
                {
                    var kuExt = (X509KeyUsageExtension)extension;

                    if ((kuExt.KeyUsages & kSufficientFlags) == 0)
                    {
                        return null;
                    }
                }
            }

            PublicKey encodedPublicKey = certificate.PublicKey;
            string keyParameters = BitConverter.ToString(
                encodedPublicKey.EncodedParameters.RawData);
            byte[] keyValue = encodedPublicKey.EncodedKeyValue.RawData;

            var ecParameters = default(ECParameters);

            if (keyValue[0] != 0x04)
            {
                throw new InvalidOperationException("Only uncompressed points are supported");
            }

            byte[] x = new byte[(keyValue.Length - 1) / 2];
            byte[] y = new byte[x.Length];

            Buffer.BlockCopy(keyValue, 1, x, 0, x.Length);
            Buffer.BlockCopy(keyValue, 1 + x.Length, y, 0, y.Length);

            ecParameters.Q.X = x;
            ecParameters.Q.Y = y;

            // New values can be determined by running the dotted-decimal OID value
            // through BitConverter.ToString(CryptoConfig.EncodeOID(dottedDecimal));

            switch (keyParameters)
            {
                case NistP256KeyParameters:
                    ecParameters.Curve = ECCurve.NamedCurves.nistP256;
                    securityPolicyUris = [SecurityPolicies.ECC_nistP256];
                    break;
                case NistP384KeyParameters:
                    ecParameters.Curve = ECCurve.NamedCurves.nistP384;
                    securityPolicyUris = [SecurityPolicies.ECC_nistP384, SecurityPolicies
                        .ECC_nistP256];
                    break;
                case BrainpoolP256r1KeyParameters:
                    ecParameters.Curve = ECCurve.NamedCurves.brainpoolP256r1;
                    securityPolicyUris = [SecurityPolicies.ECC_brainpoolP256r1];
                    break;
                case BrainpoolP384r1KeyParameters:
                    ecParameters.Curve = ECCurve.NamedCurves.brainpoolP384r1;
                    securityPolicyUris = [SecurityPolicies.ECC_brainpoolP384r1, SecurityPolicies
                        .ECC_brainpoolP256r1];
                    break;
                default:
                    throw new NotImplementedException(keyParameters);
            }

            return ECDsa.Create(ecParameters);
        }

        /// <summary>
        /// Returns the length of a ECDsa signature of a digest.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static int GetSignatureLength(X509Certificate2 signingCertificate)
        {
            if (signingCertificate == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "No public key for certificate.");
            }

            if (signingCertificate.GetRSAPublicKey() != null)
            {
                return RsaUtils.GetSignatureLength(signingCertificate);
            }

            using ECDsa publicKey =
                GetPublicKey(signingCertificate)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "No public key for certificate.");

            return publicKey.KeySize / 4;
        }

        /// <summary>
        /// Computes an ECDSA signature.
        /// </summary>
        public static byte[] Sign(
            ArraySegment<byte> dataToSign,
            X509Certificate2 signingCertificate,
            string securityPolicyUri)
        {
            var info = SecurityPolicies.GetInfo(securityPolicyUri);
            return Sign(dataToSign, signingCertificate, info.AsymmetricSignatureAlgorithm);
        }

        /// <summary>
        /// Computes an signature.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static byte[] Sign(
            ArraySegment<byte> dataToSign,
            X509Certificate2 signingCertificate,
            AsymmetricSignatureAlgorithm algorithm)
        {
            switch (algorithm)
            {
                case AsymmetricSignatureAlgorithm.None:
                    return null;
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha1:
                    return RsaUtils.Rsa_Sign(
                        dataToSign,
                        signingCertificate,
                        HashAlgorithmName.SHA1,
                        RSASignaturePadding.Pkcs1);
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha256:
                    return RsaUtils.Rsa_Sign(
                        dataToSign,
                        signingCertificate,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pkcs1);
                case AsymmetricSignatureAlgorithm.RsaPssSha256:
                    return RsaUtils.Rsa_Sign(
                        dataToSign,
                        signingCertificate,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pss);
                case AsymmetricSignatureAlgorithm.EcdsaSha256:
                case AsymmetricSignatureAlgorithm.EcdsaSha384:
                    break;
                default:
                    throw new ServiceResultException(StatusCodes.BadSecurityPolicyRejected);
            }

            // get the algorithm used for the signature.
            HashAlgorithmName hashAlgorithm;

            switch (algorithm)
            {
                case AsymmetricSignatureAlgorithm.EcdsaSha384:
                    hashAlgorithm = HashAlgorithmName.SHA384;
                    break;
                case AsymmetricSignatureAlgorithm.EcdsaSha256:
                    hashAlgorithm = HashAlgorithmName.SHA256;
                    break;
                default:
                    throw new NotSupportedException($"AsymmetricSignatureAlgorithm not supported: {algorithm}");
            }

            ECDsa senderPrivateKey =
                signingCertificate.GetECDsaPrivateKey()
                ?? throw new ServiceResultException(
                    StatusCodes.BadCertificateInvalid,
                    "Missing private key needed for create a signature.");

            using (senderPrivateKey)
            {
                byte[] signature = senderPrivateKey.SignData(
                    dataToSign.Array,
                    dataToSign.Offset,
                    dataToSign.Count,
                    hashAlgorithm);

                return signature;
            }
        }

        /// <summary>
        /// Verifies a ECDsa signature.
        /// </summary>
        public static bool Verify(
            ArraySegment<byte> dataToVerify,
            byte[] signature,
            X509Certificate2 signingCertificate,
            string securityPolicyUri)
        {
            var info = SecurityPolicies.GetInfo(securityPolicyUri);

            if (info == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    $"Unknown security policy: {securityPolicyUri}");
            }

            return Verify(
                dataToVerify,
                signature,
                signingCertificate,
                info.AsymmetricSignatureAlgorithm);
        }

        /// <summary>
        /// Verifies a ECDsa signature.
        /// </summary>
        public static bool Verify(
            ArraySegment<byte> dataToVerify,
            byte[] signature,
            X509Certificate2 signingCertificate,
            AsymmetricSignatureAlgorithm algorithm)
        {
            switch (algorithm)
            {
                case AsymmetricSignatureAlgorithm.None:
                    return true;
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha1:
                    return RsaUtils.Rsa_Verify(
                        dataToVerify,
                        signature,
                        signingCertificate,
                        HashAlgorithmName.SHA1,
                        RSASignaturePadding.Pkcs1);
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha256:
                    return RsaUtils.Rsa_Verify(
                        dataToVerify,
                        signature,
                        signingCertificate,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pkcs1);
                case AsymmetricSignatureAlgorithm.RsaPssSha256:
                    return RsaUtils.Rsa_Verify(
                        dataToVerify,
                        signature,
                        signingCertificate,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pss);
                case AsymmetricSignatureAlgorithm.EcdsaSha256:
                case AsymmetricSignatureAlgorithm.EcdsaSha384:
                    break;
                default:
                    return false;
            }

            // get the algorithm used for the signature.
            HashAlgorithmName hashAlgorithm;

            switch (algorithm)
            {
                case AsymmetricSignatureAlgorithm.EcdsaSha384:
                    hashAlgorithm = HashAlgorithmName.SHA384;
                    break;
                case AsymmetricSignatureAlgorithm.EcdsaSha256:
                    hashAlgorithm = HashAlgorithmName.SHA256;
                    break;
                default:
                    throw new NotSupportedException($"AsymmetricSignatureAlgorithm not supported: {algorithm}.");
            }

            using ECDsa ecdsa = GetPublicKey(signingCertificate);

            return ecdsa.VerifyData(
                dataToVerify.Array,
                dataToVerify.Offset,
                dataToVerify.Count,
                signature,
                hashAlgorithm);
        }

        /// <summary>
        /// Adds padding to a buffer. Input: buffer with unencrypted data starting at 0; plaintext data starting at offset; no padding.
        /// </summary>
        /// <param name="data">buffer with unencrypted data starting at 0; plaintext data starting at offset; no padding.</param>
        /// <param name="blockSize"></param>
        /// <returns>Output: buffer with unencrypted data starting at 0; plaintext data starting at offset; padding added.</returns>
        private static ArraySegment<byte> AddPadding(ArraySegment<byte> data, int blockSize)
        {
            int paddingByteSize = blockSize > byte.MaxValue ? 2 : 1;
            int paddingSize = blockSize - ((data.Count + paddingByteSize) % blockSize);
            paddingSize %= blockSize;

            int endOfData = data.Offset + data.Count;
            int endOfPaddedData = data.Offset + data.Count + paddingSize + paddingByteSize;

            for (int ii = endOfData; ii < endOfPaddedData - paddingByteSize && ii < data.Array.Length; ii++)
            {
                data.Array[ii] = (byte)(paddingSize & 0xFF);
            }

            data.Array[endOfData + paddingSize] = (byte)(paddingSize & 0xFF);

            if (blockSize > byte.MaxValue)
            {
                data.Array[endOfData + paddingSize + 1] = (byte)((paddingSize & 0xFF) >> 8);
            }

            return new ArraySegment<byte>(data.Array, data.Offset, data.Count + paddingSize + paddingByteSize);
        }

        /// <summary>
        /// Removes padding from a buffer. Input: buffer with unencrypted data starting at 0; plaintext including padding starting at offset; signature removed.
        /// </summary>
        /// <param name="data">Input: buffer with unencrypted data starting at 0; plaintext including padding starting at offset; signature removed.</param>
        /// <param name="blockSize"></param>
        /// <returns>Output: buffer with unencrypted data starting at 0; plaintext starting at offset; padding excluded.</returns>
        /// <exception cref="CryptographicException"></exception>
        private static ArraySegment<byte> RemovePadding(ArraySegment<byte> data, int blockSize)
        {
            int paddingSize = data.Array[data.Offset + data.Count - 1];
            int paddingByteSize = 1;

            if (blockSize > byte.MaxValue)
            {
                paddingSize <<= 8;
                paddingSize += data.Array[data.Offset + data.Count - 2];
                paddingByteSize = 2;
            }

            int notvalid = paddingSize < data.Count ? 0 : 1;
            int start = data.Offset + data.Count - paddingSize - paddingByteSize;

            for (int ii = data.Offset; ii < data.Count - paddingByteSize && ii < paddingSize; ii++)
            {
                if (start < 0 || start + ii >= data.Count)
                {
                    notvalid |= 1;
                    continue;
                }

                notvalid |= data.Array[start + ii] ^ (paddingSize & 0xFF);
            }

            if (notvalid != 0)
            {
                throw new CryptographicException("Invalid padding.");
            }

            return new ArraySegment<byte>(data.Array, 0, data.Offset + data.Count - paddingSize - paddingByteSize);
        }

        /// <summary>
        /// Encrypts the buffer using the algorithm specified by the security policy.
        /// </summary>
        public static ArraySegment<byte> SymmetricEncryptAndSign(
            ArraySegment<byte> data,
            SecurityPolicyInfo securityPolicy,
            byte[] encryptingKey,
            byte[] iv,
            byte[] signingKey = null,
            HMAC hmac = null,
            bool signOnly = false,
            uint tokenId = 0,
            uint lastSequenceNumber = 0)
        {
            SymmetricEncryptionAlgorithm algorithm = securityPolicy.SymmetricEncryptionAlgorithm;

            if (algorithm == SymmetricEncryptionAlgorithm.None)
            {
                return data;
            }

            if (algorithm is SymmetricEncryptionAlgorithm.Aes128Gcm or SymmetricEncryptionAlgorithm.Aes256Gcm)
            {
#if NET8_0_OR_GREATER
                return EncryptWithAesGcm(data, encryptingKey, iv, signOnly, tokenId, lastSequenceNumber);
#else
                throw new NotSupportedException("AES-GCM requires .NET 8 or greater.");
#endif
            }

            if (algorithm == SymmetricEncryptionAlgorithm.ChaCha20Poly1305)
            {
#if NET8_0_OR_GREATER
                return EncryptWithChaCha20Poly1305(
                    data,
                    encryptingKey,
                    iv,
                    signOnly,
                    tokenId,
                    lastSequenceNumber);
#else
                throw new NotSupportedException("ChaCha20Poly1305 requires .NET 8 or greater.");
#endif
            }

            if (!signOnly)
            {
                data = AddPadding(data, iv.Length);
            }

            if (signingKey != null)
            {
                byte[] hash = hmac.ComputeHash(data.Array, 0, data.Offset + data.Count);

                Buffer.BlockCopy(
                    hash,
                    0,
                    data.Array,
                    data.Offset + data.Count,
                    hash.Length);

                data = new ArraySegment<byte>(
                    data.Array,
                    data.Offset,
                    data.Count + hash.Length);
            }

            if (!signOnly)
            {
#pragma warning disable CA5401 // Symmetric encryption uses non-default initialization vector
                using var aes = Aes.Create();

                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = encryptingKey;
                aes.IV = iv;

                using ICryptoTransform encryptor = aes.CreateEncryptor();
#pragma warning restore CA5401

                encryptor.TransformBlock(
                    data.Array,
                    data.Offset,
                    data.Count,
                    data.Array,
                    data.Offset);
            }

            return new ArraySegment<byte>(data.Array, 0, data.Offset + data.Count);
        }

#if NET8_0_OR_GREATER
        private static byte[] ApplyAeadMask(uint tokenId, uint lastSequenceNumber, byte[] iv)
        {
            var copy = new byte[iv.Length];
            Buffer.BlockCopy(iv, 0, copy, 0, iv.Length);

            copy[0] ^= (byte)((tokenId & 0x000000FF));
            copy[1] ^= (byte)((tokenId & 0x0000FF00) >> 8);
            copy[2] ^= (byte)((tokenId & 0x00FF0000) >> 16);
            copy[3] ^= (byte)((tokenId & 0xFF000000) >> 24);
            copy[4] ^= (byte)((lastSequenceNumber & 0x000000FF));
            copy[5] ^= (byte)((lastSequenceNumber & 0x0000FF00) >> 8);
            copy[6] ^= (byte)((lastSequenceNumber & 0x00FF0000) >> 16);
            copy[7] ^= (byte)((lastSequenceNumber & 0xFF000000) >> 24);

            return copy;
        }

        private const int kChaChaPolyIvLength = 12;
        private const int kChaChaPolyTagLength = 16;

        private static ArraySegment<byte> EncryptWithChaCha20Poly1305(
            ArraySegment<byte> data,
            byte[] encryptingKey,
            byte[] iv,
            bool signOnly,
            uint tokenId,
            uint lastSequenceNumber)
        {
            if (encryptingKey == null || encryptingKey.Length != 32)
            {
                throw new ArgumentException("ChaCha20-Poly1305 requires a 256-bit (32-byte) key.", nameof(encryptingKey));
            }

            if (iv == null || iv.Length != kChaChaPolyIvLength)
            {
                throw new ArgumentException("ChaCha20-Poly1305 requires a 96-bit (12-byte) nonce.", nameof(iv));
            }

            byte[] ciphertext = new byte[signOnly ? 0 : data.Count];
            byte[] tag = new byte[kChaChaPolyTagLength]; // ChaCha20-Poly1305/AES-GCM uses 128-bit authentication tag

            var extraData = new ReadOnlySpan<byte>(
                data.Array,
                0,
                signOnly ? data.Offset + data.Count : data.Offset);

            using var chacha = new ChaCha20Poly1305(encryptingKey);

            iv = ApplyAeadMask(tokenId, lastSequenceNumber, iv);

            chacha.Encrypt(
                iv,
                signOnly ? Array.Empty<byte>() : data,
                ciphertext,
                tag,
                extraData);

            CryptoTrace.Start(ConsoleColor.DarkCyan, "EncryptWithChaCha20Poly1305");
            CryptoTrace.WriteLine($"Data Offset/Count={data.Offset}/{data.Count}");
            CryptoTrace.WriteLine($"TokenId/LastSequenceNumber={tokenId}/{lastSequenceNumber}");
            CryptoTrace.WriteLine($"EncryptingKey={CryptoTrace.KeyToString(encryptingKey)}");
            CryptoTrace.WriteLine($"IV={CryptoTrace.KeyToString(iv)}");
            CryptoTrace.WriteLine($"EncryptedData={CryptoTrace.KeyToString(ciphertext)}");
            CryptoTrace.WriteLine($"Tag={CryptoTrace.KeyToString(tag)}");
            CryptoTrace.WriteLine($"ExtraData={CryptoTrace.KeyToString(extraData.ToArray())}");
            CryptoTrace.Finish("EncryptWithChaCha20Poly1305");

            // Return layout: [associated data | ciphertext | tag]
            if (!signOnly)
            {
                Buffer.BlockCopy(ciphertext, 0, data.Array, data.Offset, ciphertext.Length);
            }

            Buffer.BlockCopy(tag, 0, data.Array, data.Offset + data.Count, tag.Length);

            return new ArraySegment<byte>(
                data.Array,
                0,
                data.Offset + data.Count + kChaChaPolyTagLength);
        }
#endif

#if NET8_0_OR_GREATER
        private static ArraySegment<byte> DecryptWithChaCha20Poly1305(
           ArraySegment<byte> data,
           byte[] encryptingKey,
           byte[] iv,
           bool signOnly,
           uint tokenId,
           uint lastSequenceNumber)
        {
            if (encryptingKey == null || encryptingKey.Length != 32)
            {
                throw new ArgumentException("ChaCha20-Poly1305 requires a 256-bit (32-byte) key.", nameof(encryptingKey));
            }

            if (iv == null || iv.Length != kChaChaPolyIvLength)
            {
                throw new ArgumentException("ChaCha20-Poly1305 requires a 96-bit (12-byte) nonce.", nameof(iv));
            }

            if (data.Count < kChaChaPolyTagLength) // Must at least contain tag
            {
                throw new ArgumentException("Ciphertext too short.", nameof(data));
            }

            byte[] plaintext = new byte[data.Count - kChaChaPolyTagLength];

            var encryptedData = new ArraySegment<byte>(
                data.Array,
                data.Offset,
                signOnly ? 0 : data.Count - kChaChaPolyTagLength);

            var tag = new ArraySegment<byte>(
                data.Array,
                data.Offset + data.Count - kChaChaPolyTagLength,
                kChaChaPolyTagLength);

            var extraData = new ReadOnlySpan<byte>(
                data.Array,
                0,
                signOnly ? data.Offset + data.Count - kChaChaPolyTagLength : data.Offset);

            using var chacha = new ChaCha20Poly1305(encryptingKey);

            iv = ApplyAeadMask(tokenId, lastSequenceNumber, iv);

            chacha.Decrypt(
                iv,
                encryptedData,
                tag,
                signOnly ? [] : plaintext,
                extraData);

            CryptoTrace.Start(ConsoleColor.DarkCyan, "DecryptWithChaCha20Poly1305");
            CryptoTrace.WriteLine($"Data Offset/Count={data.Offset}/{data.Count - kChaChaPolyTagLength}");
            CryptoTrace.WriteLine($"TokenId/LastSequenceNumber={tokenId}/{lastSequenceNumber}");
            CryptoTrace.WriteLine($"EncryptingKey={CryptoTrace.KeyToString(encryptingKey)}");
            CryptoTrace.WriteLine($"IV={CryptoTrace.KeyToString(iv)}");
            CryptoTrace.WriteLine($"EncryptedData={CryptoTrace.KeyToString(encryptedData)}");
            CryptoTrace.WriteLine($"Tag={CryptoTrace.KeyToString(tag)}");
            CryptoTrace.WriteLine($"ExtraData={CryptoTrace.KeyToString(extraData.ToArray())}");
            CryptoTrace.Finish("DecryptWithChaCha20Poly1305");

            // Return layout: [associated data | plaintext]
            if (!signOnly)
            {
                Buffer.BlockCopy(plaintext, 0, data.Array, data.Offset, encryptedData.Count);
            }

            return new ArraySegment<byte>(data.Array, 0, data.Offset + data.Count - kChaChaPolyTagLength);
        }
#endif

#if NET8_0_OR_GREATER
        private const int kAesGcmIvLength = 12;
        private const int kAesGcmTagLength = 16;

        private static ArraySegment<byte> EncryptWithAesGcm(
            ArraySegment<byte> data,
            byte[] encryptingKey,
            byte[] iv,
            bool signOnly,
            uint tokenId,
            uint lastSequenceNumber)
        {
            if (encryptingKey == null)
            {
                throw new ArgumentNullException(nameof(encryptingKey));
            }

            if (iv == null || iv.Length != kAesGcmIvLength)
            {
                throw new ArgumentException("AES-GCM requires a 96-bit (12-byte) IV/nonce.", nameof(iv));
            }

            byte[] ciphertext = new byte[signOnly ? 0 : data.Count];
            byte[] tag = new byte[kAesGcmTagLength]; // AES-GCM uses 128-bit authentication tag

            var extraData = new ReadOnlySpan<byte>(
                data.Array,
                0,
                signOnly ? data.Offset + data.Count : data.Offset);

            using var aesGcm = new AesGcm(encryptingKey, kAesGcmTagLength);

            iv = ApplyAeadMask(tokenId, lastSequenceNumber, iv);

            aesGcm.Encrypt(
                iv,
                signOnly ? Array.Empty<byte>() : data,
                ciphertext,
                tag,
                extraData);

            CryptoTrace.Start(ConsoleColor.DarkCyan, "EncryptWithAesGcm");
            CryptoTrace.WriteLine($"Data Offset/Count={data.Offset}/{data.Count}");
            CryptoTrace.WriteLine($"TokenId/LastSequenceNumber={tokenId}/{lastSequenceNumber}");
            CryptoTrace.WriteLine($"EncryptingKey={CryptoTrace.KeyToString(encryptingKey)}");
            CryptoTrace.WriteLine($"IV={CryptoTrace.KeyToString(iv)}");
            CryptoTrace.WriteLine($"EncryptedData={CryptoTrace.KeyToString(ciphertext)}");
            CryptoTrace.WriteLine($"Tag={CryptoTrace.KeyToString(tag)}");
            CryptoTrace.WriteLine($"ExtraData={CryptoTrace.KeyToString(extraData.ToArray())}");
            CryptoTrace.Finish("DecryptWithAesGcm");

            // Return layout: [associated data | ciphertext | tag]
            if (!signOnly)
            {
                Buffer.BlockCopy(ciphertext, 0, data.Array, data.Offset, ciphertext.Length);
            }

            Buffer.BlockCopy(tag, 0, data.Array, data.Offset + data.Count, tag.Length);

            return new ArraySegment<byte>(
                data.Array,
                0,
                data.Offset + data.Count + kAesGcmTagLength);
        }
#endif

#if NET8_0_OR_GREATER
        private static ArraySegment<byte> DecryptWithAesGcm(
            ArraySegment<byte> data,
            byte[] encryptingKey,
            byte[] iv,
            bool signOnly,
            uint tokenId,
            uint lastSequenceNumber)
        {
            if (encryptingKey == null)
            {
                throw new ArgumentNullException(nameof(encryptingKey));
            }

            if (iv == null || iv.Length != kAesGcmIvLength)
            {
                throw new ArgumentException("AES-GCM requires a 96-bit (12-byte) IV/nonce.", nameof(iv));
            }

            if (data.Count < kAesGcmTagLength) // Must at least contain tag
            {
                throw new ArgumentException("Ciphertext too short.", nameof(data));
            }

            byte[] plaintext = new byte[data.Count - kAesGcmTagLength];

            var encryptedData = new ArraySegment<byte>(
                data.Array,
                data.Offset,
                signOnly ? 0 : data.Count - kAesGcmTagLength);

            var tag = new ArraySegment<byte>(
                data.Array,
                data.Offset + data.Count - kAesGcmTagLength,
                kAesGcmTagLength);

            var extraData = new ReadOnlySpan<byte>(
                data.Array,
                0,
                signOnly ? data.Offset + data.Count - kAesGcmTagLength : data.Offset);

            using var aesGcm = new AesGcm(encryptingKey, kAesGcmTagLength);

            iv = ApplyAeadMask(tokenId, lastSequenceNumber, iv);

            CryptoTrace.Start(ConsoleColor.DarkCyan, "DecryptWithAesGcm");
            CryptoTrace.WriteLine($"Data Offset/Count={data.Offset}/{data.Count - kAesGcmTagLength}");
            CryptoTrace.WriteLine($"TokenId/LastSequenceNumber={tokenId}/{lastSequenceNumber}");
            CryptoTrace.WriteLine($"EncryptingKey={CryptoTrace.KeyToString(encryptingKey)}");
            CryptoTrace.WriteLine($"IV={CryptoTrace.KeyToString(iv)}");
            CryptoTrace.WriteLine($"EncryptedData={CryptoTrace.KeyToString(encryptedData)}");
            CryptoTrace.WriteLine($"Tag={CryptoTrace.KeyToString(tag)}");
            CryptoTrace.WriteLine($"ExtraData={CryptoTrace.KeyToString(extraData.ToArray())}");
            CryptoTrace.Finish("DecryptWithAesGcm");

            aesGcm.Decrypt(
                iv,
                encryptedData,
                tag,
                signOnly ? [] : plaintext,
                extraData);

            // Return layout: [associated data | plaintext]
            if (!signOnly)
            {
                Buffer.BlockCopy(plaintext, 0, data.Array, data.Offset, encryptedData.Count);
            }

            return new ArraySegment<byte>(data.Array, 0, data.Offset + data.Count - kAesGcmTagLength);
        }
#endif

            /// <summary>
            /// Decrypts the buffer using the algorithm specified by the security policy.
            /// </summary>
            /// <exception cref="CryptographicException"></exception>
            /// <exception cref="NotSupportedException"></exception>
        public static ArraySegment<byte> SymmetricDecryptAndVerify(
           ArraySegment<byte> data,
           SecurityPolicyInfo securityPolicy,
           byte[] encryptingKey,
           byte[] iv,
           byte[] signingKey = null,
           bool signOnly = false,
           uint tokenId = 0,
           uint lastSequenceNumber = 0)
        {
            SymmetricEncryptionAlgorithm algorithm = securityPolicy.SymmetricEncryptionAlgorithm;

            if (algorithm == SymmetricEncryptionAlgorithm.None)
            {
                return data;
            }

            if (algorithm is SymmetricEncryptionAlgorithm.Aes128Gcm or SymmetricEncryptionAlgorithm.Aes256Gcm)
            {
#if NET8_0_OR_GREATER
                return DecryptWithAesGcm(data, encryptingKey, iv, signOnly, tokenId, lastSequenceNumber);
#else
                throw new NotSupportedException("AES-GCM requires .NET 8 or greater.");
#endif
            }

            if (algorithm == SymmetricEncryptionAlgorithm.ChaCha20Poly1305)
            {
#if NET8_0_OR_GREATER
                return DecryptWithChaCha20Poly1305(
                    data,
                    encryptingKey,
                    iv,
                    signOnly,
                    tokenId,
                    lastSequenceNumber);
#else
                throw new NotSupportedException("ChaCha20Poly1305 requires .NET 8 or greater.");
#endif
            }

            if (!signOnly)
            {
                using var aes = Aes.Create();

                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = encryptingKey;
                aes.IV = iv;

                using ICryptoTransform decryptor = aes.CreateDecryptor();

                decryptor.TransformBlock(
                    data.Array,
                    data.Offset,
                    data.Count,
                    data.Array,
                    data.Offset);
            }

            int isNotValid = 0;

            if (signingKey != null)
            {
                using HMAC hmac = securityPolicy.CreateSignatureHmac(signingKey);
                byte[] hash = hmac.ComputeHash(data.Array, 0, data.Offset + data.Count - (hmac.HashSize / 8));
                for (int ii = 0; ii < hash.Length; ii++)
                {
                    int index = data.Offset + data.Count - hash.Length + ii;
                    isNotValid |= data.Array[index] != hash[ii] ? 1 : 0;
                }

                data = new ArraySegment<byte>(
                    data.Array,
                    data.Offset,
                    data.Count - hash.Length);
            }

            if (!signOnly)
            {
                data = RemovePadding(data, iv.Length);
            }

            if (isNotValid != 0)
            {
                throw new CryptographicException("Invalid signature.");
            }

            return new ArraySegment<byte>(data.Array, 0, data.Offset + data.Count);
        }
    }

#if X
    class FfdheDhWithRsaAuth
    {
        // ffdhe2048 prime from RFC 7919 (hex, without whitespace).  
        // (RFC 7919 Appendix A.3 — use this canonical modulus in production.)
        const string FFDHE2048_HEX = @"
            FFFFFFFF FFFFFFFF ADF85458 A2BB4A9A AFDC5620 273D3CF1
            D8B9C583 CE2D3695 A9E13641 146433FB CC939DCE 249B3EF9
            7D2FE363 630C75D8 F681B202 AEC4617A D3DF1ED5 D5FD6561
            2433F51F 5F066ED0 85636555 3DED1AF3 B557135E 7F57C935
            984F0C70 E0E68B77 E2A689DA F3EFE872 1DF158A1 36ADE735
            30ACCA4F 483A797A BC0AB182 B324FB61 D108A94B B2C8E3FB
            B96ADAB7 60D7F468 1D4F42A3 DE394DF4 AE56EDE7 6372BB19
            0B07A7C8 EE0A6D70 9E02FCE1 CDF7E2EC C03404CD 28342F61
            9172FE9C E98583FF 8E4F1232 EEF28183 C3FE3B1B 4C6FAD73
            3BB5FCBC 2EC22005 C58EF183 7D1683B2 C6F34A26 C1B2EFFA
            886B4238 61285C97 FFFFFFFF FFFFFFFF";

        // Generator for FFDHE groups is 2
        static readonly BigInteger G = new BigInteger(2);

        static void Main()
        {
            // Parse the RFC hex prime into a positive BigInteger
            BigInteger p = ParseHexBigInteger(FFDHE2048_HEX);

            // Recommended: use a short ephemeral exponent (e.g. 256-bit) for performance
            // while still achieving ~128-bit security for typical scenarios.
            // (RFC7919 and implementation guidance discuss exponent sizing; choose per your threat model.)
            int privateBitLength = 256;

            Console.WriteLine("Simulating DH exchange (ffdhe2048) with RSA signing...");

            // Simulate Alice and Bob
            var alice = CreateDhParticipant(p, privateBitLength);
            var bob = CreateDhParticipant(p, privateBitLength);

            // Each signs their public value with their own RSA key (DHE-RSA style authentication)
            byte[] alicePub = ToBigEndian(alice.PublicValue);
            byte[] bobPub = ToBigEndian(bob.PublicValue);

            byte[] aliceSig, bobSig;
            using (RSA rsaAlice = RSA.Create(2048))
            using (RSA rsaBob = RSA.Create(2048))
            {
                // In real use the RSA keys are persistent (server cert) and public keys/certificates exchanged
                aliceSig = rsaAlice.SignData(alicePub, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                bobSig = rsaBob.SignData(bobPub, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                // Each verifies the other's signature (in a real protocol they'd have the peer's cert/public key)
                bool verifyAlice = rsaBob.VerifyData(bobPub, bobSig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                bool verifyBob = rsaAlice.VerifyData(alicePub, aliceSig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                Console.WriteLine($"Alice verifies Bob's signature: {verifyAlice}");
                Console.WriteLine($"Bob verifies Alice's signature: {verifyBob}");
            }

            // Compute shared secrets
            BigInteger aliceShared = BigInteger.ModPow(bob.PublicValue, alice.PrivateValue, p);
            BigInteger bobShared = BigInteger.ModPow(alice.PublicValue, bob.PrivateValue, p);

            if (aliceShared != bobShared)
            {
                Console.WriteLine("Shared secrets do not match! Aborting.");
                return;
            }

            byte[] sharedBytes = ToBigEndian(aliceShared); // same as bobShared

            // Derive 32-byte key with HKDF-SHA256 (RFC 5869)
            byte[] salt = RandomNumberGenerator.GetBytes(32); // optional but recommended
            byte[] info = Encoding.UTF8.GetBytes("ffdhe2048-dhe-rsa-derived-key");
            byte[] aesKey = HkdfSha256(sharedBytes, salt, info, 32);
        }

        // Creates a participant with ephemeral private/public values
        static (BigInteger PrivateValue, BigInteger PublicValue) CreateDhParticipant(BigInteger p, int privateBitLen)
        {
            BigInteger priv = GenerateRandomBigInteger(privateBitLen);
            BigInteger pub = BigInteger.ModPow(G, priv, p);
            return (priv, pub);
        }

        // Generate an unsigned positive BigInteger of bitLength bits (big-endian)
        static BigInteger GenerateRandomBigInteger(int bitLength)
        {
            int byteCount = (bitLength + 7) / 8;
            byte[] be = RandomNumberGenerator.GetBytes(byteCount);

            // Ensure top bit set so the value has the requested bit length (avoid tiny exponents)
            int topBitIndex = (bitLength - 1) % 8;
            be[0] |= (byte)(1 << topBitIndex);

            // Convert big-endian to little-endian + sign byte for BigInteger ctor
            byte[] le = new byte[be.Length + 1]; // extra zero to force positive
            for (int i = 0; i < be.Length; i++)
                le[i] = be[be.Length - 1 - i];
            le[le.Length - 1] = 0;
            return new BigInteger(le);
        }

        // Convert BigInteger to big-endian unsigned byte[] (no leading zero)
        static byte[] ToBigEndian(BigInteger value)
        {
            if (value.Sign < 0)
                throw new ArgumentException("value must be non-negative");
            byte[] le = value.ToByteArray(); // little-endian two's complement
                                             // Trim any trailing zero that indicates sign if present
            int last = le.Length - 1;
            if (le[last] == 0)
            {
                Array.Resize(ref le, last);
                last--;
            }
            byte[] be = new byte[le.Length];
            for (int i = 0; i < le.Length; i++)
                be[i] = le[le.Length - 1 - i];
            return be;
        }

        // Parse hex into a positive BigInteger (handles odd-length and ensures positive)
        static BigInteger ParseHexBigInteger(string hex)
        {
            hex = hex.Trim().Replace(" ", "").Replace("\n", "").Replace("\r", "");
            // Ensure even length
            if ((hex.Length & 1) == 1)
                hex = "0" + hex;
            // Use Convert.FromHexString (available in .NET 5+) or implement equivalent
            byte[] be = Convert.FromHexString(hex);
            // If highest bit of first byte is set, BigInteger.Parse with HexNumber can treat it as negative;
            // we therefore construct positive BigInteger from big-endian bytes:
            byte[] le = new byte[be.Length + 1];
            for (int i = 0; i < be.Length; i++)
                le[i] = be[be.Length - 1 - i];
            le[le.Length - 1] = 0;
            return new BigInteger(le);
        }

        // Simple HKDF-SHA256 implementation (RFC 5869)
        static byte[] HkdfSha256(byte[] ikm, byte[] salt, byte[] info, int outLen)
        {
            // HKDF-Extract
            byte[] prk;
            using (var hmac = new HMACSHA256(salt ?? new byte[0]))
            {
                prk = hmac.ComputeHash(ikm);
            }

            // HKDF-Expand
            int hashLen = 32;
            int n = (outLen + hashLen - 1) / hashLen;
            byte[] okm = new byte[outLen];
            byte[] previous = Array.Empty<byte>();
            using (var hmac = new HMACSHA256(prk))
            {
                int offset = 0;
                for (byte i = 1; i <= n; i++)
                {
                    // T(i) = HMAC-PRK( T(i-1) | info | i )
                    hmac.Initialize();
                    hmac.TransformBlock(previous, 0, previous.Length, null, 0);
                    if (info != null && info.Length > 0)
                        hmac.TransformBlock(info, 0, info.Length, null, 0);
                    byte[] counter = new byte[] { i };
                    hmac.TransformFinalBlock(counter, 0, 1);
                    previous = hmac.Hash!;
                    int toCopy = Math.Min(hashLen, outLen - offset);
                    Array.Copy(previous, 0, okm, offset, toCopy);
                    offset += toCopy;
                }
            }
            return okm;
        }
    }
#endif

    /// <summary>
    /// A class to assist with tracing crypto operations.
    /// </summary>
    public static class CryptoTrace
    {
        /// <summary>
        /// Starts a trace block.
        /// </summary>
        public static void Start(ConsoleColor color, string format, params object[] args)
        {
#if DEBUG
            Console.ForegroundColor = color;
            Console.Write("============ ");
            Console.Write(format, args);
            Console.WriteLine(" ============");
#endif
        }

        /// <summary>
        /// Finishes a trace block.
        /// </summary>
        public static void Finish(string format, params object[] args)
        {
#if DEBUG
            Console.Write("============ ");
            Console.Write(format, args);
            Console.WriteLine(" Finished ============");
            Console.ForegroundColor = ConsoleColor.White;
#endif
        }

        /// <summary>
        /// Writes a trace message.
        /// </summary>
        public static void Write(string format, params object[] args)
        {
#if DEBUG
            Console.Write(format, args);
#endif
        }

        /// <summary>
        /// Writes a trace message.
        /// </summary>
        public static void WriteLine(string format, params object[] args)
        {
#if DEBUG
            Console.WriteLine(format, args);
#endif
        }

        /// <summary>
        /// Returns a debug string for a key.
        /// </summary>
        public static string KeyToString(ArraySegment<byte> key)
        {
#if DEBUG
            byte[] bytes = new byte[key.Count];
            Buffer.BlockCopy(key.Array ?? [], key.Offset, bytes, 0, key.Count);
            return KeyToString(bytes);
#else
            return String.Empty;
#endif
        }

        /// <summary>
        /// Returns a debug string for a key.
        /// </summary>
        public static string KeyToString(byte[] key)
        {
#if DEBUG
            if (key == null || key.Length == 0)
            {
                return "Len=0:---";
            }

            byte checksum = 0;

            foreach (var item in key)
            {
                checksum ^= item;
            }

            if (key.Length <= 16)
            {
                return "Len=" + key.Length.ToString(CultureInfo.InvariantCulture) +
                    ":" +
                    Utils.ToHexString(key) +
                    "=>XOR=" +
                    checksum.ToString(CultureInfo.InvariantCulture);
            }

            var text = Utils.ToHexString(key);
            return $"Len={key.Length}:{text.Substring(0, 8)}...{text.Substring(text.Length - 8, 8)}=>XOR={checksum}";
#else
            return String.Empty;
#endif
        }
    }
}
