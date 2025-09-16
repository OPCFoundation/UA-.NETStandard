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
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32.SafeHandles;
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
    public static class EccUtils
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
            if (securityPolicyUri != null)
            {
                return securityPolicyUri.Contains("#ECC_", StringComparison.Ordinal);
            }

            return false;
        }

        // Input : buffer with unencrypted data starting at 0; plaintext data starting at offset; no padding.
        // Output: buffer with unencrypted data starting at 0; plaintext data starting at offset; padding added.
        private static ArraySegment<byte> AddPadding(ArraySegment<byte> data, int blockSize)
        {
            int paddingByteSize = (blockSize > Byte.MaxValue) ? 2 : 1;
            int paddingSize = blockSize - ((data.Count + paddingByteSize) % blockSize);
            paddingSize %= blockSize;
            //System.Console.WriteLine($"PaddingOut {data.Offset} {data.Count} P={paddingSize}");

            int endOfData = data.Offset + data.Count;
            int endOfPaddedData = data.Offset + data.Count + paddingSize + paddingByteSize;

            for (int ii = endOfData; ii < endOfPaddedData - paddingByteSize && ii < data.Array.Length; ii++)
            {
                data.Array[ii] = (byte)(paddingSize & 0xFF);
            }

            data.Array[endOfData + paddingSize] = (byte)(paddingSize & 0xFF);

            if (blockSize > Byte.MaxValue)
            {
                data.Array[endOfData + paddingSize + 1] = (byte)((paddingSize & 0xFF) >> 8);
            }

            return new ArraySegment<byte>(data.Array, data.Offset, data.Count + paddingSize + paddingByteSize);
        }

        /// Input: buffer with unencrypted data starting at 0; plaintext including padding starting at offset; signature removed.
        /// Output: buffer with unencrypted data starting at 0; plaintext starting at offset; padding excluded.
        private static ArraySegment<byte> RemovePadding(ArraySegment<byte> data, int blockSize)
        {
            int paddingSize = data.Array[data.Offset + data.Count - 1];
            int paddingByteSize = 1;
            //System.Console.WriteLine($"PaddingIn {data.Offset} {data.Count} P={paddingSize}");

            if (blockSize > Byte.MaxValue)
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
        /// <param name="data">The data to encrypt.</param>
        /// <param name="securityPolicy">The security policy to use.</param>
        /// <param name="encryptingKey">The key to use for encryption.</param>
        /// <param name="iv">The initialization vector to use for encryption.</param>
        /// <param name="signingKey">The key to use for signing.</param>
        /// <param name="signOnly">If TRUE, the data is not encrypted.</param>
        /// <returns>The encrypted buffer.</returns>
        public static ArraySegment<byte> SymmetricEncryptAndSign(
            ArraySegment<byte> data,
            SecurityPolicyInfo securityPolicy,
            byte[] encryptingKey,
            byte[] iv,
            byte[] signingKey = null,
            bool signOnly = false)
        {
            var algorithm = securityPolicy.SymmetricEncryptionAlgorithm;

            if (algorithm == SymmetricEncryptionAlgorithm.None)
            {
                return data;
            }

            if (algorithm == SymmetricEncryptionAlgorithm.Aes128Gcm || algorithm == SymmetricEncryptionAlgorithm.Aes256Gcm)
            {
                #if NET8_0_OR_GREATER
                return EncryptWithAesGcm(encryptingKey, iv, signOnly, data);
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
                    !securityPolicy.SecureChannelEnhancements);
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
                using var hmac = securityPolicy.CreateSignatureHmac(signingKey);
                var hash = hmac.ComputeHash(data.Array, 0, data.Offset + data.Count);

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
                using var aes = Aes.Create();

                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = encryptingKey;
                aes.IV = iv;

                using ICryptoTransform encryptor = aes.CreateEncryptor();

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
        const int ChaChaPolyIvLength = 12;
        const int ChaChaPolyTagLength = 16;

        private static ArraySegment<byte> EncryptWithChaCha20Poly1305(
            ArraySegment<byte> data,
            byte[] encryptingKey,
            byte[] iv,
            bool signOnly,
            bool noPadding)
        {
            if (encryptingKey == null || encryptingKey.Length != 32)
            {
                throw new ArgumentException("ChaCha20-Poly1305 requires a 256-bit (32-byte) key.", nameof(encryptingKey));
            }

            if (iv == null || iv.Length != ChaChaPolyIvLength)
            {
                throw new ArgumentException("ChaCha20-Poly1305 requires a 96-bit (12-byte) nonce.", nameof(iv));
            }

            if (!noPadding && !signOnly)
            {
                data = AddPadding(data, iv.Length);
            }

            byte[] ciphertext = new byte[(signOnly) ? 0 : data.Count];
            byte[] tag = new byte[ChaChaPolyTagLength]; // AES-GCM uses 128-bit authentication tag

            var extraData = new ReadOnlySpan<byte>(
                data.Array,
                0,
                (signOnly) ? data.Offset + data.Count : data.Offset);

            using var chacha = new ChaCha20Poly1305(encryptingKey);

            chacha.Encrypt(
                iv,
                (signOnly) ? Array.Empty<byte>() : data,
                ciphertext,
                tag,
                extraData);

            //if (!signOnly)
            //{
            //    System.Console.WriteLine(
            //        $"ENC KEY={Utils.ToHexString(encryptingKey).Substring(0, 8)} " +
            //        $"IV={Utils.ToHexString(iv).Substring(0, 8)} " +
            //        $"TAG={Utils.ToHexString(tag).Substring(0, 8)}"
            //    );
            //}

            // Return layout: [associated data | ciphertext | tag]
            if (!signOnly)
            {
                Buffer.BlockCopy(ciphertext, 0, data.Array, data.Offset, ciphertext.Length);
            }

            Buffer.BlockCopy(tag, 0, data.Array, data.Offset + data.Count, tag.Length);

            return new ArraySegment<byte>(
                data.Array,
                0,
                data.Offset + data.Count + AesGcmTagLength);
        }
#endif

#if NET8_0_OR_GREATER
        private static ArraySegment<byte> DecryptWithChaCha20Poly1305(
           ArraySegment<byte> data,
           byte[] encryptingKey,
           byte[] iv,
           bool signOnly,
           bool noPadding)
        {
            if (encryptingKey == null || encryptingKey.Length != 32)
            {
                throw new ArgumentException("ChaCha20-Poly1305 requires a 256-bit (32-byte) key.", nameof(encryptingKey));
            }

            if (iv == null || iv.Length != ChaChaPolyIvLength)
            {
                throw new ArgumentException("ChaCha20-Poly1305 requires a 96-bit (12-byte) nonce.", nameof(iv));
            }

            if (data.Count < ChaChaPolyTagLength) // must at least contain tag
            {
                throw new ArgumentException("Ciphertext too short.", nameof(data.Count));
            }

            var plaintext = new byte[data.Count - ChaChaPolyTagLength];

            var encryptedData = new ArraySegment<byte>(
                data.Array,
                data.Offset,
                (signOnly) ? 0 : data.Count - ChaChaPolyTagLength);

            var tag = new ArraySegment<byte>(
                data.Array,
                data.Offset + data.Count - ChaChaPolyTagLength,
                ChaChaPolyTagLength);

            var extraData = new ReadOnlySpan<byte>(
                data.Array,
                0,
                (signOnly) ? data.Offset + data.Count - ChaChaPolyTagLength : data.Offset);

            using var chacha = new ChaCha20Poly1305(encryptingKey);

            //if (!signOnly)
            //{
            //    System.Console.WriteLine(
            //        $"DEC KEY={Utils.ToHexString(encryptingKey).Substring(0, 8)} " +
            //        $"IV={Utils.ToHexString(iv).Substring(0, 8)} " +
            //        $"TAG={Utils.ToHexString(tag).Substring(0, 8)}"
            //    );
            //}

            chacha.Decrypt(
                iv,
                encryptedData,
                tag,
                (signOnly) ? Array.Empty<byte>() : plaintext,
                extraData);

            // Return layout: [associated data | plaintext]
            if (!signOnly)
            {
                Buffer.BlockCopy(plaintext, 0, data.Array, data.Offset, encryptedData.Count);
            }

            if (!noPadding && !signOnly)
            {
                return RemovePadding(new ArraySegment<byte>(data.Array, data.Offset, data.Count - ChaChaPolyTagLength), iv.Length);
            }

            return new ArraySegment<byte>(data.Array, 0, data.Offset + data.Count - ChaChaPolyTagLength);
        }
#endif

#if NET8_0_OR_GREATER
        private const int AesGcmIvLength = 12;
        private const int AesGcmTagLength = 16;

        private static ArraySegment<byte> EncryptWithAesGcm(
            byte[] encryptingKey,
            byte[] iv,
            bool signOnly,
            ArraySegment<byte> data)
        {
            if (encryptingKey == null)
            {
                throw new ArgumentNullException(nameof(encryptingKey));
            }

            if (iv == null || iv.Length != AesGcmIvLength)
            {
                throw new ArgumentException("AES-GCM requires a 96-bit (12-byte) IV/nonce.", nameof(iv));
            }

            if (!signOnly)
            {
                data = AddPadding(data, iv.Length);
            }

            byte[] ciphertext = new byte[(signOnly) ? 0 : data.Count];
            byte[] tag = new byte[AesGcmTagLength]; // AES-GCM uses 128-bit authentication tag

            var extraData = new ReadOnlySpan<byte>(
                data.Array,
                0,
                (signOnly) ? data.Offset + data.Count : data.Offset);

            using var aesGcm = new AesGcm(encryptingKey, AesGcmTagLength);

            aesGcm.Encrypt(
                iv,
                (signOnly) ? Array.Empty<byte>() : data,
                ciphertext,
                tag,
                extraData);

            //System.Console.WriteLine($"Encrypt {data.Offset} {data.Count} {Utils.ToHexString(tag)}");

            // Return layout: [associated data | ciphertext | tag]
            if (!signOnly)
            {
                Buffer.BlockCopy(ciphertext, 0, data.Array, data.Offset, ciphertext.Length);
            }

            Buffer.BlockCopy(tag, 0, data.Array, data.Offset + data.Count, tag.Length);

            return new ArraySegment<byte>(
                data.Array,
                0,
                data.Offset + data.Count + AesGcmTagLength);
        }
#endif

#if NET8_0_OR_GREATER
        private static ArraySegment<byte> DecryptWithAesGcm(
            ArraySegment<byte> data,
            byte[] encryptingKey,
            byte[] iv,
            bool signOnly)
        {
            if (encryptingKey == null)
            {
                throw new ArgumentNullException(nameof(encryptingKey));
            }

            if (iv == null || iv.Length != AesGcmIvLength)
            {
                throw new ArgumentException("AES-GCM requires a 96-bit (12-byte) IV/nonce.", nameof(iv));
            }

            if (data.Count < AesGcmTagLength) // must at least contain tag
            {
                throw new ArgumentException("Ciphertext too short.", nameof(data.Count));
            }

            var plaintext = new byte[data.Count - AesGcmTagLength];

            var encryptedData = new ArraySegment<byte>(
                data.Array,
                data.Offset,
                (signOnly) ? 0 : data.Count - AesGcmTagLength);

            var tag = new ArraySegment<byte>(
                data.Array,
                data.Offset + data.Count - AesGcmTagLength,
                AesGcmTagLength);

            var extraData = new ReadOnlySpan<byte>(
                data.Array,
                0,
                (signOnly) ? data.Offset + data.Count - AesGcmTagLength : data.Offset);

            using var aesGcm = new AesGcm(encryptingKey, AesGcmTagLength);

            //System.Console.WriteLine($"Decrypt {data.Offset} {data.Count - AesGcmTagLength} {Utils.ToHexString(tag)}");

            aesGcm.Decrypt(
                iv,
                encryptedData,
                tag,
                (signOnly) ? Array.Empty<byte>() : plaintext,
                extraData);

            // Return layout: [associated data | plaintext]
            if (!signOnly)
            {
                Buffer.BlockCopy(plaintext, 0, data.Array, data.Offset, encryptedData.Count);
            }

            if (!signOnly)
            {
                return RemovePadding(new ArraySegment<byte>(data.Array, data.Offset, data.Count - AesGcmTagLength), iv.Length);
            }

            return new ArraySegment<byte>(data.Array, 0, data.Offset + data.Count - AesGcmTagLength);
        }
#endif

        /// <summary>
        /// Decrypts the buffer using the algorithm specified by the security policy.
        /// </summary>
        public static ArraySegment<byte> SymmetricDecryptAndVerify(
           ArraySegment<byte> data,
           SecurityPolicyInfo securityPolicy,
           byte[] encryptingKey,
           byte[] iv,
           byte[] signingKey = null,
           bool signOnly = false)
        {
            var algorithm = securityPolicy.SymmetricEncryptionAlgorithm;

            if (algorithm == SymmetricEncryptionAlgorithm.None)
            {
                return data;
            }

            if (algorithm == SymmetricEncryptionAlgorithm.Aes128Gcm || algorithm == SymmetricEncryptionAlgorithm.Aes256Gcm)
            {
                #if NET8_0_OR_GREATER
                return DecryptWithAesGcm(data, encryptingKey, iv, signOnly);
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
                    !securityPolicy.SecureChannelEnhancements);
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
                using var hmac = securityPolicy.CreateSignatureHmac(signingKey);
                var hash  = hmac.ComputeHash(data.Array, 0, data.Offset + data.Count - hmac.HashSize/8);

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
                }
                return signatureQualifier;
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
            using ECDsa publicKey =
                GetPublicKey(signingCertificate)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "No public key for certificate.");

            return publicKey.KeySize / 4;
        }

        /// <summary>
        /// Returns the hash algorithm for the specified security policy.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="securityPolicyUri"/> is <c>null</c>.</exception>
        public static HashAlgorithmName GetSignatureAlgorithmName(string securityPolicyUri)
        {
            if (securityPolicyUri == null)
            {
                throw new ArgumentNullException(nameof(securityPolicyUri));
            }

            switch (securityPolicyUri)
            {
                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_nistP256_AES:
                case SecurityPolicies.ECC_nistP256_ChaChaPoly:
                case SecurityPolicies.ECC_brainpoolP256r1:
                case SecurityPolicies.ECC_brainpoolP256r1_AES:
                case SecurityPolicies.ECC_brainpoolP256r1_ChaChaPoly:
                    return HashAlgorithmName.SHA256;
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_nistP384_AES:
                case SecurityPolicies.ECC_nistP384_ChaChaPoly:
                case SecurityPolicies.ECC_brainpoolP384r1:
                case SecurityPolicies.ECC_brainpoolP384r1_AES:
                case SecurityPolicies.ECC_brainpoolP384r1_ChaChaPoly:
                    return HashAlgorithmName.SHA384;
                default:
                    return HashAlgorithmName.SHA256;
            }
        }

        /// <summary>
        /// Computes an ECDSA signature.
        /// </summary>
        public static byte[] Sign(
            ArraySegment<byte> dataToSign,
            X509Certificate2 signingCertificate,
            string securityPolicyUri)
        {
            HashAlgorithmName algorithm = GetSignatureAlgorithmName(securityPolicyUri);
            return Sign(dataToSign, signingCertificate, algorithm);
        }

        /// <summary>
        /// Computes an ECDSA signature.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static byte[] Sign(
            ArraySegment<byte> dataToSign,
            X509Certificate2 signingCertificate,
            HashAlgorithmName algorithm)
        {
#if CURVE25519
            var publicKey = signingCertificate.BcCertificate.GetPublicKey();

            if (publicKey is Ed25519PublicKeyParameters)
            {
                var signer = new Ed25519Signer();

                signer.Init(true, signingCertificate.BcPrivateKey);
                signer.BlockUpdate(dataToSign.Array, dataToSign.Offset, dataToSign.Count);
                byte[] signature = signer.GenerateSignature();
                return signature;
            }

            if (publicKey is Ed448PublicKeyParameters)
            {
                var signer = new Ed448Signer(new byte[32]);

                signer.Init(true, signingCertificate.BcPrivateKey);
                signer.BlockUpdate(dataToSign.Array, dataToSign.Offset, dataToSign.Count);
                byte[] signature = signer.GenerateSignature();
                return signature;
            }
#endif
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
                    algorithm);

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
            return Verify(
                dataToVerify,
                signature,
                signingCertificate,
                GetSignatureAlgorithmName(securityPolicyUri));
        }

        /// <summary>
        /// Verifies a ECDsa signature.
        /// </summary>
        public static bool Verify(
            ArraySegment<byte> dataToVerify,
            byte[] signature,
            X509Certificate2 signingCertificate,
            HashAlgorithmName algorithm)
        {
#if CURVE25519
            var publicKey = signingCertificate.BcCertificate.GetPublicKey();

            if (publicKey is Ed25519PublicKeyParameters)
            {
                var verifier = new Ed25519Signer();

                verifier.Init(false, signingCertificate.BcCertificate.GetPublicKey());
                verifier.BlockUpdate(dataToVerify.Array, dataToVerify.Offset, dataToVerify.Count);

                if (!verifier.VerifySignature(signature))
                {
                    return false;
                }

                return true;
            }

            if (publicKey is Ed448PublicKeyParameters)
            {
                var verifier = new Ed448Signer(new byte[32]);

                verifier.Init(false, signingCertificate.BcCertificate.GetPublicKey());
                verifier.BlockUpdate(dataToVerify.Array, dataToVerify.Offset, dataToVerify.Count);

                if (!verifier.VerifySignature(signature))
                {
                    return false;
                }

                return true;
            }
#endif
            using ECDsa ecdsa = GetPublicKey(signingCertificate);

            return ecdsa.VerifyData(
                dataToVerify.Array,
                dataToVerify.Offset,
                dataToVerify.Count,
                signature,
                algorithm);
        }
    }

}
