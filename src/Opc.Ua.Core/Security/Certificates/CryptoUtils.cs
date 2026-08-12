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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
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

#nullable enable

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
            SecurityPolicyInfo? info = SecurityPolicyRegistry.Default.GetInfo(securityPolicyUri);

            if (info != null)
            {
                return info.CertificateKeyFamily == CertificateKeyFamily.ECC;
            }

            return false;
        }

        /// <summary>
        /// Returns the NodeId for the certificate type for the specified certificate.
        /// </summary>
        public static NodeId GetEccCertificateTypeId(Certificate certificate)
        {
            string keyAlgorithm = certificate.GetKeyAlgorithm();
            if (keyAlgorithm != Oids.ECPublicKey)
            {
                return NodeId.Null;
            }

            PublicKey encodedPublicKey = certificate.PublicKey;

            if (encodedPublicKey.EncodedParameters is null)
            {
                return NodeId.Null;
            }

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
        /// Maps the public key of <paramref name="certificate"/> to a
        /// <see cref="CertificateKeyAlgorithm"/>. Returns
        /// <see cref="CertificateKeyAlgorithm.None"/> when the certificate
        /// is <see langword="null"/> or the algorithm/curve cannot be
        /// recognized.
        /// </summary>
        public static CertificateKeyAlgorithm GetCertificateKeyAlgorithm(Certificate? certificate)
        {
            if (certificate == null)
            {
                return CertificateKeyAlgorithm.None;
            }

            string keyAlgorithm = certificate.GetKeyAlgorithm();
            if (keyAlgorithm == Oids.Rsa)
            {
                return CertificateKeyAlgorithm.RSA;
            }

            if (keyAlgorithm == Oids.ECPublicKey)
            {
                PublicKey encodedPublicKey = certificate.PublicKey;
                if (encodedPublicKey.EncodedParameters?.RawData is byte[] rawData)
                {
                    switch (BitConverter.ToString(rawData))
                    {
                        case NistP256KeyParameters:
                            return CertificateKeyAlgorithm.NistP256;
                        case NistP384KeyParameters:
                            return CertificateKeyAlgorithm.NistP384;
                        case BrainpoolP256r1KeyParameters:
                            return CertificateKeyAlgorithm.BrainpoolP256r1;
                        case BrainpoolP384r1KeyParameters:
                            return CertificateKeyAlgorithm.BrainpoolP384r1;
                    }
                }
            }

            return CertificateKeyAlgorithm.None;
        }

        /// <summary>
        /// Returns the key length in bits of the RSA public key in
        /// <paramref name="certificate"/>; returns 0 when the certificate
        /// is <see langword="null"/> or not an RSA certificate.
        /// </summary>
        public static int GetRsaPublicKeySize(Certificate? certificate)
        {
            if (certificate == null || certificate.GetKeyAlgorithm() != Oids.Rsa)
            {
                return 0;
            }

            using RSA? rsa = certificate.GetRSAPublicKey();
            return rsa?.KeySize ?? 0;
        }

        /// <summary>
        /// returns an ECCCurve if there is a matching supported curve for the provided
        /// certificate type id. if no supported ECC curve is found null is returned.
        /// </summary>
        /// <param name="certificateType">the  application certificatate type node id</param>
        /// <returns>the ECCCurve, null if certificatate type id has no matching supported ECC curve</returns>
        public static ECCurve? GetCurveFromCertificateTypeId(NodeId certificateType)
        {
            return SecurityPolicyRegistry.Default.GetCurveFromCertificateTypeId(certificateType);
        }

        /// <summary>
        /// Returns the signature algorithm for the specified certificate.
        /// </summary>
        public static string GetECDsaQualifier(Certificate certificate)
        {
            if (X509Utils.IsECDsaSignature(certificate))
            {
                const string signatureQualifier = "ECDsa";
                PublicKey encodedPublicKey = certificate.PublicKey;

                if (encodedPublicKey.EncodedParameters is null)
                {
                    return string.Empty;
                }

                // New values can be determined by running the dotted-decimal OID value
                // through BitConverter.ToString(CryptoConfig.EncodeOID(dottedDecimal));

                switch (BitConverter.ToString(encodedPublicKey.EncodedParameters!.RawData!))
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
        public static ECDsa? GetPublicKey(Certificate certificate)
        {
            return GetPublicKey(certificate, out string[]? _);
        }

        /// <summary>
        /// Returns the public key for the specified certificate and outputs the security policy uris.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="NotImplementedException"></exception>
        public static ECDsa? GetPublicKey(
            Certificate certificate,
            out string[]? securityPolicyUris)
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
                if (extension.Oid?.Value == "2.5.29.15")
                {
                    var kuExt = (X509KeyUsageExtension)extension;

                    if ((kuExt.KeyUsages & kSufficientFlags) == 0)
                    {
                        return null;
                    }
                }
            }

            PublicKey encodedPublicKey = certificate.PublicKey;

            if (encodedPublicKey.EncodedParameters is null)
            {
                return null;
            }

            string keyParameters = BitConverter.ToString(
                encodedPublicKey.EncodedParameters!.RawData!);
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
        public static int GetSignatureLength(Certificate signingCertificate)
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
        /// Computes a signature.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="securityPolicyUri"/> cannot be resolved to a
        /// known security policy.
        /// </exception>
        public static byte[]? Sign(
            ArraySegment<byte> dataToSign,
            Certificate signingCertificate,
            string securityPolicyUri)
        {
            SecurityPolicyInfo info = SecurityPolicyRegistry.Default.GetInfo(securityPolicyUri)
                ?? throw new ArgumentException(
                    $"Cannot resolve SecurityPolicy '{securityPolicyUri}'.",
                    nameof(securityPolicyUri));
            return Sign(dataToSign, signingCertificate, info.AsymmetricSignatureAlgorithm);
        }

        /// <summary>
        /// Computes a signature.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        public static byte[]? Sign(
            ArraySegment<byte> dataToSign,
            Certificate signingCertificate,
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

            byte[] arrayToSign = dataToSign.Array
                ?? throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Data to sign must not be empty.");

            using (senderPrivateKey)
            {
                return senderPrivateKey.SignData(
                    arrayToSign,
                    dataToSign.Offset,
                    dataToSign.Count,
                    hashAlgorithm);
            }
        }

        /// <summary>
        /// Computes a signature without occupying the calling thread when the
        /// private key is served over a network.
        /// </summary>
        /// <param name="dataToSign">The data to sign.</param>
        /// <param name="signingCertificate">
        /// The certificate whose private key signs.
        /// </param>
        /// <param name="algorithm">The signature algorithm to apply.</param>
        /// <param name="ct">Cancels the operation.</param>
        /// <returns>
        /// The signature, or <see langword="null"/> when the algorithm is
        /// <see cref="AsymmetricSignatureAlgorithm.None"/>.
        /// </returns>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        /// <remarks>
        /// The returned task completes synchronously unless the private key
        /// declares <see cref="IAsyncRsaKey"/> or <see cref="IAsyncEcdsaKey"/>.
        /// A software key therefore behaves exactly as it does through
        /// <see cref="Sign(ArraySegment{byte}, Certificate, AsymmetricSignatureAlgorithm)"/>,
        /// including the order in which everything around the call happens.
        /// </remarks>
        /// <exception cref="ArgumentNullException"></exception>
        public static ValueTask<byte[]?> SignAsync(
            ArraySegment<byte> dataToSign,
            Certificate signingCertificate,
            AsymmetricSignatureAlgorithm algorithm,
            CancellationToken ct = default)
        {
            if (algorithm == AsymmetricSignatureAlgorithm.None)
            {
                // Checked before the certificate, because a channel with no
                // security signs nothing and carries no certificate to check.
                return new ValueTask<byte[]?>((byte[]?)null);
            }

            if (signingCertificate is null)
            {
                throw new ArgumentNullException(nameof(signingCertificate));
            }

            if (TryGetAsymmetricSignatureParameters(
                    algorithm, out HashAlgorithmName hashAlgorithm, out RSASignaturePadding? padding))
            {
                if (padding != null)
                {
                    RSA? rsa = signingCertificate.GetRSAPrivateKey();

                    if (rsa is IAsyncRsaKey asyncRsa)
                    {
                        return SignWithAsyncRsaAsync(
                            rsa, asyncRsa, dataToSign, hashAlgorithm, padding, ct);
                    }

                    rsa?.Dispose();
                }
                else
                {
                    ECDsa? ecdsa = signingCertificate.GetECDsaPrivateKey();

                    if (ecdsa is IAsyncEcdsaKey asyncEcdsa)
                    {
                        return SignWithAsyncEcdsaAsync(
                            ecdsa, asyncEcdsa, dataToSign, hashAlgorithm, ct);
                    }

                    ecdsa?.Dispose();
                }
            }

            // No asynchronous path is available, so the operation is performed
            // inline and the task is already complete. Nothing about the caller's
            // sequencing changes.
            return new ValueTask<byte[]?>(Sign(dataToSign, signingCertificate, algorithm));
        }

        private static async ValueTask<byte[]?> SignWithAsyncRsaAsync(
            RSA owned,
            IAsyncRsaKey key,
            ArraySegment<byte> dataToSign,
            HashAlgorithmName hashAlgorithm,
            RSASignaturePadding padding,
            CancellationToken ct)
        {
            using (owned)
            {
                byte[] hash = ComputeHash(dataToSign, hashAlgorithm);
                return await key.SignHashAsync(hash, hashAlgorithm, padding, ct)
                    .ConfigureAwait(false);
            }
        }

        private static async ValueTask<byte[]?> SignWithAsyncEcdsaAsync(
            ECDsa owned,
            IAsyncEcdsaKey key,
            ArraySegment<byte> dataToSign,
            HashAlgorithmName hashAlgorithm,
            CancellationToken ct)
        {
            using (owned)
            {
                byte[] hash = ComputeHash(dataToSign, hashAlgorithm);
                return await key.SignHashAsync(hash, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Maps a signature algorithm onto the hash it uses and, for the RSA
        /// algorithms, the padding.
        /// </summary>
        /// <returns>
        /// <c>false</c> when the algorithm signs nothing, in which case there is
        /// no asynchronous path to look for.
        /// </returns>
        private static bool TryGetAsymmetricSignatureParameters(
            AsymmetricSignatureAlgorithm algorithm,
            out HashAlgorithmName hashAlgorithm,
            out RSASignaturePadding? padding)
        {
            switch (algorithm)
            {
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha1:
                    hashAlgorithm = HashAlgorithmName.SHA1;
                    padding = RSASignaturePadding.Pkcs1;
                    return true;
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha256:
                    hashAlgorithm = HashAlgorithmName.SHA256;
                    padding = RSASignaturePadding.Pkcs1;
                    return true;
                case AsymmetricSignatureAlgorithm.RsaPssSha256:
                    hashAlgorithm = HashAlgorithmName.SHA256;
                    padding = RSASignaturePadding.Pss;
                    return true;
                case AsymmetricSignatureAlgorithm.EcdsaSha256:
                    hashAlgorithm = HashAlgorithmName.SHA256;
                    padding = null;
                    return true;
                case AsymmetricSignatureAlgorithm.EcdsaSha384:
                    hashAlgorithm = HashAlgorithmName.SHA384;
                    padding = null;
                    return true;
                default:
                    hashAlgorithm = default;
                    padding = null;
                    return false;
            }
        }

        private static byte[] ComputeHash(
            ArraySegment<byte> data,
            HashAlgorithmName hashAlgorithm)
        {
            byte[] array = data.Array
                ?? throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument, "Data to hash must not be empty.");

#if NET5_0_OR_GREATER
            if (hashAlgorithm == HashAlgorithmName.SHA256)
            {
                return SHA256.HashData(array.AsSpan(data.Offset, data.Count));
            }

            if (hashAlgorithm == HashAlgorithmName.SHA384)
            {
                return SHA384.HashData(array.AsSpan(data.Offset, data.Count));
            }

#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
            return SHA1.HashData(array.AsSpan(data.Offset, data.Count));
#pragma warning restore CA5350
#else
            using HashAlgorithm hash = hashAlgorithm == HashAlgorithmName.SHA256
                ? SHA256.Create()
                : hashAlgorithm == HashAlgorithmName.SHA384
                    ? SHA384.Create()
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
                    : SHA1.Create();
#pragma warning restore CA5350

            return hash.ComputeHash(array, data.Offset, data.Count);
#endif
        }

        /// <summary>
        /// Verifies a signature.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static bool Verify(
            ArraySegment<byte> dataToVerify,
            byte[] signature,
            Certificate signingCertificate,
            string securityPolicyUri)
        {
            SecurityPolicyInfo info = SecurityPolicyRegistry.Default.GetInfo(securityPolicyUri)
                ?? throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    $"Unknown security policy: {securityPolicyUri}");

            return Verify(
                dataToVerify,
                signature,
                signingCertificate,
                info.AsymmetricSignatureAlgorithm);
        }

        /// <summary>
        /// Verifies a signature.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static bool Verify(
            ArraySegment<byte> dataToVerify,
            byte[] signature,
            Certificate signingCertificate,
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

            using ECDsa ecdsa = GetPublicKey(signingCertificate)
                ?? throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Missing ECC public key for signature verification.");

            byte[] arrayToVerify = dataToVerify.Array
                ?? throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Data to verify must not be empty.");

            return ecdsa.VerifyData(
                arrayToVerify,
                dataToVerify.Offset,
                dataToVerify.Count,
                signature,
                hashAlgorithm);
        }

        /// <summary>
        /// Adds padding to a buffer. Input: buffer with unencrypted data starting at 0;
        /// plaintext data starting at offset; no padding.
        /// </summary>
        /// <param name="data">buffer with unencrypted data starting at 0; plaintext data
        /// starting at offset; no padding.</param>
        /// <param name="blockSize"></param>
        /// <param name="trailingBytes">Additional bytes that will be appended after
        /// padding (e.g., HMAC) and must be considered for block alignment.</param>
        /// <returns>Output: buffer with unencrypted data starting at 0; plaintext data
        /// starting at offset; padding added.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        private static ArraySegment<byte> AddPadding(ArraySegment<byte> data, int blockSize, int trailingBytes = 0)
        {
            byte[] dataArray = data.Array ?? throw new ArgumentNullException(nameof(data), "Data array must not be null.");

            int paddingByteSize = blockSize > byte.MaxValue ? 2 : 1;
            int paddingSize = blockSize - ((data.Count + paddingByteSize + trailingBytes) % blockSize);
            paddingSize %= blockSize;

            int endOfData = data.Offset + data.Count;
            int endOfPaddedData = data.Offset + data.Count + paddingSize + paddingByteSize;

            for (int ii = endOfData; ii < endOfPaddedData - paddingByteSize && ii < dataArray.Length; ii++)
            {
                dataArray[ii] = (byte)(paddingSize & 0xFF);
            }

            dataArray[endOfData + paddingSize] = (byte)(paddingSize & 0xFF);

            if (blockSize > byte.MaxValue)
            {
                dataArray[endOfData + paddingSize + 1] = (byte)((paddingSize & 0xFF) >> 8);
            }

            return new ArraySegment<byte>(dataArray, data.Offset, data.Count + paddingSize + paddingByteSize);
        }

        /// <summary>
        /// Removes padding from a buffer. Input: buffer with unencrypted data starting at 0;
        /// plaintext including padding starting at offset; signature removed.
        /// </summary>
        /// <param name="data">Input: buffer with unencrypted data starting at 0; plaintext
        /// including padding starting at offset; signature removed.</param>
        /// <param name="blockSize"></param>
        /// <returns>Output: buffer with unencrypted data starting at 0; plaintext starting
        /// at offset; padding excluded.</returns>
        /// <exception cref="CryptographicException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        private static ArraySegment<byte> RemovePadding(ArraySegment<byte> data, int blockSize)
        {
            byte[] dataArray = data.Array ??
                throw new ArgumentNullException(nameof(data), "Data array must not be null.");

            int paddingSize = dataArray[data.Offset + data.Count - 1];
            int paddingByteSize = 1;

            if (blockSize > byte.MaxValue)
            {
                paddingSize <<= 8;
                paddingSize += dataArray[data.Offset + data.Count - 2];
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

                notvalid |= dataArray[start + ii] ^ (paddingSize & 0xFF);
            }

            if (notvalid != 0)
            {
                throw new CryptographicException("Invalid padding.");
            }

            return new ArraySegment<byte>(dataArray, 0, data.Offset + data.Count - paddingSize - paddingByteSize);
        }

        /// <summary>
        /// Encrypts the buffer using the algorithm specified by the security policy.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        /// <exception cref="CryptographicException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        /// <remarks>
        /// This overload preserves the signature that shipped before a symmetric
        /// crypto provider could be supplied, so assemblies compiled against it
        /// keep working without a recompile. It performs the operations with the
        /// platform, which is what the other overload does when no provider is
        /// resolved.
        /// </remarks>
        public static ArraySegment<byte> SymmetricEncryptAndSign(
            ArraySegment<byte> data,
            SecurityPolicyInfo securityPolicy,
            byte[] encryptingKey,
            byte[] iv,
            byte[]? signingKey = null,
            HMAC? hmac = null,
            bool signOnly = false,
            uint tokenId = 0,
            uint lastSequenceNumber = 0)
        {
            return SymmetricEncryptAndSign(
                data,
                securityPolicy,
                encryptingKey,
                iv,
                signingKey,
                hmac,
                signOnly,
                tokenId,
                lastSequenceNumber,
                null);
        }

        /// <summary>
        /// Encrypts the buffer using the algorithm specified by the security
        /// policy, optionally through a symmetric crypto provider.
        /// </summary>
        /// <param name="data">The buffer to encrypt and sign, in place.</param>
        /// <param name="securityPolicy">
        /// The security policy whose algorithms are applied.
        /// </param>
        /// <param name="encryptingKey">The symmetric encryption key.</param>
        /// <param name="iv">The initialization vector.</param>
        /// <param name="signingKey">
        /// The signing key, or <see langword="null"/> when the buffer is unsigned.
        /// </param>
        /// <param name="hmac">
        /// An HMAC to reuse for signing. The channel keeps one per token, which
        /// avoids allocating one per chunk. Ignored when
        /// <paramref name="provider"/> is supplied.
        /// </param>
        /// <param name="signOnly">
        /// <see langword="true"/> when the buffer is signed but not encrypted.
        /// </param>
        /// <param name="tokenId">
        /// The channel token id, used by the AEAD algorithms to derive the nonce.
        /// </param>
        /// <param name="lastSequenceNumber">
        /// The sequence number, used by the AEAD algorithms to derive the nonce.
        /// </param>
        /// <param name="provider">
        /// The symmetric crypto provider to perform the operations, or
        /// <see langword="null"/> to use the platform directly. Resolve it once
        /// where the channel token is computed; this is the per message path and
        /// must not consult a registry.
        /// </param>
        /// <exception cref="NotSupportedException"></exception>
        /// <exception cref="CryptographicException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public static ArraySegment<byte> SymmetricEncryptAndSign(
            ArraySegment<byte> data,
            SecurityPolicyInfo securityPolicy,
            byte[] encryptingKey,
            byte[] iv,
            byte[]? signingKey,
            HMAC? hmac,
            bool signOnly,
            uint tokenId,
            uint lastSequenceNumber,
            ISymmetricCryptoProvider? provider)
        {
            SymmetricEncryptionAlgorithm algorithm = securityPolicy.SymmetricEncryptionAlgorithm;

            if (algorithm == SymmetricEncryptionAlgorithm.None)
            {
                return data;
            }

            if (algorithm is SymmetricEncryptionAlgorithm.Aes128Gcm or SymmetricEncryptionAlgorithm.Aes256Gcm)
            {
#if NET8_0_OR_GREATER
                return EncryptWithAesGcm(
                    data, algorithm, encryptingKey, iv, signOnly, tokenId, lastSequenceNumber, provider);
#else
                throw new NotSupportedException("AES-GCM requires .NET 8 or greater.");
#endif
            }

            if (algorithm == SymmetricEncryptionAlgorithm.ChaCha20Poly1305)
            {
#if NET8_0_OR_GREATER
                return EncryptWithChaCha20Poly1305(
                    data,
                    algorithm,
                    encryptingKey,
                    iv,
                    signOnly,
                    tokenId,
                    lastSequenceNumber,
                    provider);
#else
                throw new NotSupportedException("ChaCha20Poly1305 requires .NET 8 or greater.");
#endif
            }

            SymmetricSignatureAlgorithm signatureAlgorithm =
                securityPolicy.SymmetricSignatureAlgorithm;
            ISymmetricCryptoProvider? signer =
                provider != null && provider.Supports(signatureAlgorithm) ? provider : null;
            ISymmetricCryptoProvider? cipher =
                provider != null && provider.Supports(algorithm) ? provider : null;

            int hashLength = 0;

            if (signingKey != null)
            {
                if (signer != null)
                {
                    hashLength = signer.GetSignatureLength(signatureAlgorithm);
                }
                else if (hmac != null)
                {
                    hashLength = hmac.HashSize / 8;
                }
                else
                {
                    throw new CryptographicException("Missing HMAC for symmetric signing.");
                }
            }

            if (!signOnly)
            {
                data = AddPadding(data, iv.Length, hashLength);
            }

            // The buffer originates from BufferManager so the backing array is non-null.
            byte[] dataArray = data.GetArray();

            if (signingKey != null)
            {
                if (signer != null)
                {
                    signer.Sign(
                        signatureAlgorithm,
                        signingKey,
                        dataArray.AsSpan(0, data.Offset + data.Count),
                        dataArray.AsSpan(data.Offset + data.Count, hashLength));
                }
                else
                {
#if NET6_0_OR_GREATER
                    // Write the signature straight into the space reserved for it
                    // instead of allocating a hash array and copying it across.
                    if (!hmac!.TryComputeHash(
                            dataArray.AsSpan(0, data.Offset + data.Count),
                            dataArray.AsSpan(data.Offset + data.Count, hashLength),
                            out int written) ||
                        written != hashLength)
                    {
                        throw new CryptographicException(
                            "Could not compute the symmetric signature.");
                    }
#else
                    byte[] hash = hmac!.ComputeHash(dataArray, 0, data.Offset + data.Count);

                    Buffer.BlockCopy(
                        hash,
                        0,
                        dataArray,
                        data.Offset + data.Count,
                        hash.Length);
#endif
                }

                data = new ArraySegment<byte>(
                    dataArray,
                    data.Offset,
                    data.Count + hashLength);
            }

            if (!signOnly)
            {
                if (cipher != null)
                {
                    cipher.Encrypt(
                        algorithm,
                        encryptingKey,
                        iv,
                        dataArray.AsSpan(data.Offset, data.Count),
                        dataArray.AsSpan(data.Offset, data.Count));
                }
                else
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
                        dataArray,
                        data.Offset,
                        data.Count,
                        dataArray,
                        data.Offset);
                }
            }

            return new ArraySegment<byte>(dataArray, 0, data.Offset + data.Count);
        }

#if NET8_0_OR_GREATER
        private static byte[] ApplyAeadMask(uint tokenId, uint lastSequenceNumber, byte[] iv)
        {
            byte[] copy = new byte[iv.Length];
            Buffer.BlockCopy(iv, 0, copy, 0, iv.Length);

            copy[0] ^= (byte)(tokenId & 0x000000FF);
            copy[1] ^= (byte)((tokenId & 0x0000FF00) >> 8);
            copy[2] ^= (byte)((tokenId & 0x00FF0000) >> 16);
            copy[3] ^= (byte)((tokenId & 0xFF000000) >> 24);
            copy[4] ^= (byte)(lastSequenceNumber & 0x000000FF);
            copy[5] ^= (byte)((lastSequenceNumber & 0x0000FF00) >> 8);
            copy[6] ^= (byte)((lastSequenceNumber & 0x00FF0000) >> 16);
            copy[7] ^= (byte)((lastSequenceNumber & 0xFF000000) >> 24);

            return copy;
        }

        private const int kChaChaPolyIvLength = 12;
        private const int kChaChaPolyTagLength = 16;

        private static ArraySegment<byte> EncryptWithChaCha20Poly1305(
            ArraySegment<byte> data,
            SymmetricEncryptionAlgorithm algorithm,
            byte[] encryptingKey,
            byte[] iv,
            bool signOnly,
            uint tokenId,
            uint lastSequenceNumber,
            ISymmetricCryptoProvider? provider)
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
            // Buffer originates from BufferManager so the backing array is non-null.
            byte[] dataArray = data.GetArray();

            var extraData = new ReadOnlySpan<byte>(
                dataArray,
                0,
                signOnly ? data.Offset + data.Count : data.Offset);

            iv = ApplyAeadMask(tokenId, lastSequenceNumber, iv);

            if (provider != null && provider.Supports(algorithm))
            {
                provider.EncryptAuthenticated(
                    algorithm,
                    encryptingKey,
                    iv,
                    signOnly ? ReadOnlySpan<byte>.Empty : data.AsSpan(),
                    ciphertext,
                    tag,
                    extraData);
            }
            else
            {
                using var chacha = new ChaCha20Poly1305(encryptingKey);

                chacha.Encrypt(
                    iv,
                    signOnly ? Array.Empty<byte>() : data,
                    ciphertext,
                    tag,
                    extraData);
            }

            // Return layout: [associated data | ciphertext | tag]
            if (!signOnly)
            {
                Buffer.BlockCopy(ciphertext, 0, dataArray, data.Offset, ciphertext.Length);
            }

            Buffer.BlockCopy(tag, 0, dataArray, data.Offset + data.Count, tag.Length);

            return new ArraySegment<byte>(
                dataArray,
                0,
                data.Offset + data.Count + kChaChaPolyTagLength);
        }

        private static ArraySegment<byte> DecryptWithChaCha20Poly1305(
           ArraySegment<byte> data,
           SymmetricEncryptionAlgorithm algorithm,
           byte[] encryptingKey,
           byte[] iv,
           bool signOnly,
           uint tokenId,
           uint lastSequenceNumber,
           ISymmetricCryptoProvider? provider)
        {
            if (encryptingKey == null || encryptingKey.Length != 32)
            {
                throw new ArgumentException(
                    "ChaCha20-Poly1305 requires a 256-bit (32-byte) key.",
                    nameof(encryptingKey));
            }

            if (iv == null || iv.Length != kChaChaPolyIvLength)
            {
                throw new ArgumentException(
                    "ChaCha20-Poly1305 requires a 96-bit (12-byte) nonce.",
                    nameof(iv));
            }

            if (data.Count < kChaChaPolyTagLength) // Must at least contain tag
            {
                throw new ArgumentException(
                    "Ciphertext too short.",
                    nameof(data));
            }

            byte[] plaintext = new byte[data.Count - kChaChaPolyTagLength];
            // Buffer originates from BufferManager so the backing array is non-null.
            byte[] dataArray = data.GetArray();

            var encryptedData = new ArraySegment<byte>(
                dataArray,
                data.Offset,
                signOnly ? 0 : data.Count - kChaChaPolyTagLength);

            var tag = new ArraySegment<byte>(
                dataArray,
                data.Offset + data.Count - kChaChaPolyTagLength,
                kChaChaPolyTagLength);

            var extraData = new ReadOnlySpan<byte>(
                dataArray,
                0,
                signOnly ? data.Offset + data.Count - kChaChaPolyTagLength : data.Offset);

            iv = ApplyAeadMask(tokenId, lastSequenceNumber, iv);

            if (provider != null && provider.Supports(algorithm))
            {
                if (!provider.DecryptAuthenticated(
                        algorithm,
                        encryptingKey,
                        iv,
                        encryptedData.AsSpan(),
                        tag.AsSpan(),
                        signOnly ? Span<byte>.Empty : plaintext,
                        extraData))
                {
                    throw new CryptographicException(
                        "The ChaCha20-Poly1305 authentication tag did not verify.");
                }
            }
            else
            {
                using var chacha = new ChaCha20Poly1305(encryptingKey);

                chacha.Decrypt(
                    iv,
                    encryptedData,
                    tag,
                    signOnly ? [] : plaintext,
                    extraData);
            }

            // Return layout: [associated data | plaintext]
            if (!signOnly)
            {
                Buffer.BlockCopy(plaintext, 0, dataArray, data.Offset, encryptedData.Count);
            }

            return new ArraySegment<byte>(dataArray, 0, data.Offset + data.Count - kChaChaPolyTagLength);
        }

        private const int kAesGcmIvLength = 12;
        private const int kAesGcmTagLength = 16;

        private static ArraySegment<byte> EncryptWithAesGcm(
            ArraySegment<byte> data,
            SymmetricEncryptionAlgorithm algorithm,
            byte[] encryptingKey,
            byte[] iv,
            bool signOnly,
            uint tokenId,
            uint lastSequenceNumber,
            ISymmetricCryptoProvider? provider)
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
            // Buffer originates from BufferManager so the backing array is non-null.
            byte[] dataArray = data.GetArray();

            var extraData = new ReadOnlySpan<byte>(
                dataArray,
                0,
                signOnly ? data.Offset + data.Count : data.Offset);

            iv = ApplyAeadMask(tokenId, lastSequenceNumber, iv);

            if (provider != null && provider.Supports(algorithm))
            {
                provider.EncryptAuthenticated(
                    algorithm,
                    encryptingKey,
                    iv,
                    signOnly ? ReadOnlySpan<byte>.Empty : data.AsSpan(),
                    ciphertext,
                    tag,
                    extraData);
            }
            else
            {
                using var aesGcm = new AesGcm(encryptingKey, kAesGcmTagLength);

                aesGcm.Encrypt(
                    iv,
                    signOnly ? Array.Empty<byte>() : data,
                    ciphertext,
                    tag,
                    extraData);
            }

            // Return layout: [associated data | ciphertext | tag]
            if (!signOnly)
            {
                Buffer.BlockCopy(ciphertext, 0, dataArray, data.Offset, ciphertext.Length);
            }

            Buffer.BlockCopy(tag, 0, dataArray, data.Offset + data.Count, tag.Length);

            return new ArraySegment<byte>(
                dataArray,
                0,
                data.Offset + data.Count + kAesGcmTagLength);
        }

        private static ArraySegment<byte> DecryptWithAesGcm(
            ArraySegment<byte> data,
            SymmetricEncryptionAlgorithm algorithm,
            byte[] encryptingKey,
            byte[] iv,
            bool signOnly,
            uint tokenId,
            uint lastSequenceNumber,
            ISymmetricCryptoProvider? provider)
        {
            if (encryptingKey == null)
            {
                throw new ArgumentNullException(nameof(encryptingKey));
            }

            if (iv == null || iv.Length != kAesGcmIvLength)
            {
                throw new ArgumentException(
                    "AES-GCM requires a 96-bit (12-byte) IV/nonce.",
                    nameof(iv));
            }

            if (data.Count < kAesGcmTagLength) // Must at least contain tag
            {
                throw new ArgumentException(
                    "Ciphertext too short.",
                    nameof(data));
            }

            byte[] plaintext = new byte[data.Count - kAesGcmTagLength];
            // Buffer originates from BufferManager so the backing array is non-null.
            byte[] dataArray = data.GetArray();

            var encryptedData = new ArraySegment<byte>(
                dataArray,
                data.Offset,
                signOnly ? 0 : data.Count - kAesGcmTagLength);

            var tag = new ArraySegment<byte>(
                dataArray,
                data.Offset + data.Count - kAesGcmTagLength,
                kAesGcmTagLength);

            var extraData = new ReadOnlySpan<byte>(
                dataArray,
                0,
                signOnly ? data.Offset + data.Count - kAesGcmTagLength : data.Offset);

            iv = ApplyAeadMask(tokenId, lastSequenceNumber, iv);

            if (provider != null && provider.Supports(algorithm))
            {
                if (!provider.DecryptAuthenticated(
                        algorithm,
                        encryptingKey,
                        iv,
                        encryptedData.AsSpan(),
                        tag.AsSpan(),
                        signOnly ? Span<byte>.Empty : plaintext,
                        extraData))
                {
                    throw new CryptographicException(
                        "The AES-GCM authentication tag did not verify.");
                }
            }
            else
            {
                using var aesGcm = new AesGcm(encryptingKey, kAesGcmTagLength);

                aesGcm.Decrypt(
                    iv,
                    encryptedData,
                    tag,
                    signOnly ? [] : plaintext,
                    extraData);
            }

            // Return layout: [associated data | plaintext]
            if (!signOnly)
            {
                Buffer.BlockCopy(plaintext, 0, dataArray, data.Offset, encryptedData.Count);
            }

            return new ArraySegment<byte>(dataArray, 0, data.Offset + data.Count - kAesGcmTagLength);
        }
#endif

#if NET6_0_OR_GREATER
        /// <summary>
        /// The largest symmetric signature any supported policy produces, SHA-512.
        /// </summary>
        private const int kMaxSymmetricHashLength = 64;
#endif

        /// <summary>
        /// Decrypts the buffer using the algorithm specified by the security policy.
        /// </summary>
        /// <param name="data">
        /// The buffer to decrypt and verify, decrypted in place.
        /// </param>
        /// <param name="securityPolicy">
        /// The security policy whose algorithms are applied.
        /// </param>
        /// <param name="encryptingKey">
        /// The symmetric decryption key.
        /// </param>
        /// <param name="iv">
        /// The initialization vector.
        /// </param>
        /// <param name="signingKey">
        /// The signing key, or <see langword="null"/> when the buffer is unsigned.
        /// </param>
        /// <param name="signOnly">
        /// <see langword="true"/> when the buffer is signed but not encrypted.
        /// </param>
        /// <param name="tokenId">
        /// The channel token id, used by the AEAD algorithms to derive the nonce.
        /// </param>
        /// <param name="lastSequenceNumber">
        /// The sequence number, used by the AEAD algorithms to derive the nonce.
        /// </param>
        /// <exception cref="CryptographicException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException">
        /// The signature HMAC could not be created.
        /// </exception>
        /// <remarks>
        /// This overload preserves the signature that shipped before an HMAC
        /// could be supplied, so assemblies compiled against it keep working
        /// without a recompile. It creates and disposes an HMAC per call; pass
        /// one to the other overload to avoid that.
        /// </remarks>
        public static ArraySegment<byte> SymmetricDecryptAndVerify(
           ArraySegment<byte> data,
           SecurityPolicyInfo securityPolicy,
           byte[] encryptingKey,
           byte[] iv,
           byte[]? signingKey = null,
           bool signOnly = false,
           uint tokenId = 0,
           uint lastSequenceNumber = 0)
        {
            return SymmetricDecryptAndVerify(
                data,
                securityPolicy,
                encryptingKey,
                iv,
                signingKey,
                signOnly,
                tokenId,
                lastSequenceNumber,
                null);
        }

        /// <summary>
        /// Decrypts the buffer using the algorithm specified by the security
        /// policy, reusing a caller supplied HMAC.
        /// </summary>
        /// <param name="data">
        /// The buffer to decrypt and verify, decrypted in place.
        /// </param>
        /// <param name="securityPolicy">
        /// The security policy whose algorithms are applied.
        /// </param>
        /// <param name="encryptingKey">
        /// The symmetric decryption key.
        /// </param>
        /// <param name="iv">
        /// The initialization vector.
        /// </param>
        /// <param name="signingKey">
        /// The signing key, or <see langword="null"/> when the buffer is unsigned.
        /// </param>
        /// <param name="signOnly">
        /// <see langword="true"/> when the buffer is signed but not encrypted.
        /// </param>
        /// <param name="tokenId">
        /// The channel token id, used by the AEAD algorithms to derive the nonce.
        /// </param>
        /// <param name="lastSequenceNumber">
        /// The sequence number, used by the AEAD algorithms to derive the nonce.
        /// </param>
        /// <param name="hmac">
        /// An HMAC to reuse for signature verification. When <see langword="null"/>
        /// one is created from the signing key and disposed before returning. The
        /// channel keeps one per token, which avoids allocating one per chunk.
        /// </param>
        /// <exception cref="CryptographicException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException">
        /// The signature HMAC could not be created.
        /// </exception>
        /// <remarks>
        /// This overload preserves the signature that shipped before a symmetric
        /// crypto provider could be supplied, so assemblies compiled against it
        /// keep working without a recompile.
        /// </remarks>
        public static ArraySegment<byte> SymmetricDecryptAndVerify(
           ArraySegment<byte> data,
           SecurityPolicyInfo securityPolicy,
           byte[] encryptingKey,
           byte[] iv,
           byte[]? signingKey,
           bool signOnly,
           uint tokenId,
           uint lastSequenceNumber,
           HMAC? hmac)
        {
            return SymmetricDecryptAndVerify(
                data,
                securityPolicy,
                encryptingKey,
                iv,
                signingKey,
                signOnly,
                tokenId,
                lastSequenceNumber,
                hmac,
                null);
        }

        /// <summary>
        /// Decrypts the buffer using the algorithm specified by the security
        /// policy, reusing a caller supplied HMAC and optionally performing the
        /// operations through a symmetric crypto provider.
        /// </summary>
        /// <param name="data">
        /// The buffer to decrypt and verify, decrypted in place.
        /// </param>
        /// <param name="securityPolicy">
        /// The security policy whose algorithms are applied.
        /// </param>
        /// <param name="encryptingKey">
        /// The symmetric decryption key.
        /// </param>
        /// <param name="iv">
        /// The initialization vector.
        /// </param>
        /// <param name="signingKey">
        /// The signing key, or <see langword="null"/> when the buffer is unsigned.
        /// </param>
        /// <param name="signOnly">
        /// <see langword="true"/> when the buffer is signed but not encrypted.
        /// </param>
        /// <param name="tokenId">
        /// The channel token id, used by the AEAD algorithms to derive the nonce.
        /// </param>
        /// <param name="lastSequenceNumber">
        /// The sequence number, used by the AEAD algorithms to derive the nonce.
        /// </param>
        /// <param name="hmac">
        /// An HMAC to reuse for signature verification. When <see langword="null"/>
        /// one is created from the signing key and disposed before returning. The
        /// channel keeps one per token, which avoids allocating one per chunk.
        /// Ignored when <paramref name="provider"/> is supplied.
        /// </param>
        /// <param name="provider">
        /// The symmetric crypto provider to perform the operations, or
        /// <see langword="null"/> to use the platform directly. Resolve it once
        /// where the channel token is computed; this is the per message path and
        /// must not consult a registry.
        /// </param>
        /// <exception cref="CryptographicException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException">
        /// The signature HMAC could not be created.
        /// </exception>
        /// <remarks>
        /// Every parameter is required so that a call using the defaults of the
        /// shorter overload stays unambiguous.
        /// </remarks>
        public static ArraySegment<byte> SymmetricDecryptAndVerify(
           ArraySegment<byte> data,
           SecurityPolicyInfo securityPolicy,
           byte[] encryptingKey,
           byte[] iv,
           byte[]? signingKey,
           bool signOnly,
           uint tokenId,
           uint lastSequenceNumber,
           HMAC? hmac,
           ISymmetricCryptoProvider? provider)
        {
            SymmetricEncryptionAlgorithm algorithm = securityPolicy.SymmetricEncryptionAlgorithm;

            if (algorithm == SymmetricEncryptionAlgorithm.None)
            {
                return data;
            }

            if (algorithm is SymmetricEncryptionAlgorithm.Aes128Gcm or SymmetricEncryptionAlgorithm.Aes256Gcm)
            {
#if NET8_0_OR_GREATER
                return DecryptWithAesGcm(
                    data, algorithm, encryptingKey, iv, signOnly, tokenId, lastSequenceNumber, provider);
#else
                throw new NotSupportedException("AES-GCM requires .NET 8 or greater.");
#endif
            }

            if (algorithm == SymmetricEncryptionAlgorithm.ChaCha20Poly1305)
            {
#if NET8_0_OR_GREATER
                return DecryptWithChaCha20Poly1305(
                    data,
                    algorithm,
                    encryptingKey,
                    iv,
                    signOnly,
                    tokenId,
                    lastSequenceNumber,
                    provider);
#else
                throw new NotSupportedException("ChaCha20Poly1305 requires .NET 8 or greater.");
#endif
            }

            SymmetricSignatureAlgorithm signatureAlgorithm =
                securityPolicy.SymmetricSignatureAlgorithm;
            ISymmetricCryptoProvider? verifier =
                provider != null && provider.Supports(signatureAlgorithm) ? provider : null;
            ISymmetricCryptoProvider? cipher =
                provider != null && provider.Supports(algorithm) ? provider : null;

            // The buffer originates from BufferManager so the backing array is non-null.
            byte[] dataArray = data.GetArray();

            if (!signOnly)
            {
                if (cipher != null)
                {
                    cipher.Decrypt(
                        algorithm,
                        encryptingKey,
                        iv,
                        dataArray.AsSpan(data.Offset, data.Count),
                        dataArray.AsSpan(data.Offset, data.Count));
                }
                else
                {
                    using var aes = Aes.Create();

                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.None;
                    aes.Key = encryptingKey;
                    aes.IV = iv;

                    using ICryptoTransform decryptor = aes.CreateDecryptor();

                    decryptor.TransformBlock(
                        dataArray,
                        data.Offset,
                        data.Count,
                        dataArray,
                        data.Offset);
                }
            }

            int isNotValid = 0;

            if (signingKey != null && verifier != null)
            {
                int hashLength = verifier.GetSignatureLength(signatureAlgorithm);
                int signedLength = data.Offset + data.Count - hashLength;

                if (!verifier.Verify(
                        signatureAlgorithm,
                        signingKey,
                        dataArray.AsSpan(0, signedLength),
                        dataArray.AsSpan(signedLength, hashLength)))
                {
                    isNotValid = 1;
                }

                data = new ArraySegment<byte>(
                    dataArray,
                    data.Offset,
                    data.Count - hashLength);
            }
            else if (signingKey != null)
            {
                // Only create and own an HMAC when the caller did not supply one.
                HMAC? ownedHmac = hmac != null
                    ? null
                    : securityPolicy.CreateSignatureHmac(signingKey) ??
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "Could not create signature HMAC.");

                try
                {
                    HMAC signer = hmac ?? ownedHmac!;
                    int hashLength = signer.HashSize / 8;
                    int signedLength = data.Offset + data.Count - hashLength;

#if NET6_0_OR_GREATER
                    if (hashLength > kMaxSymmetricHashLength)
                    {
                        throw new CryptographicException(
                            $"A symmetric signature of {hashLength} bytes is longer than any " +
                            "supported security policy produces.");
                    }

                    Span<byte> hash = stackalloc byte[kMaxSymmetricHashLength];
                    hash = hash[..hashLength];

                    if (!signer.TryComputeHash(
                            dataArray.AsSpan(0, signedLength),
                            hash,
                            out int written) ||
                        written != hashLength)
                    {
                        throw new CryptographicException(
                            "Could not compute the symmetric signature.");
                    }
#else
                    byte[] hash = signer.ComputeHash(dataArray, 0, signedLength);
#endif

                    for (int ii = 0; ii < hashLength; ii++)
                    {
                        isNotValid |= dataArray[signedLength + ii] != hash[ii] ? 1 : 0;
                    }

                    data = new ArraySegment<byte>(
                        dataArray,
                        data.Offset,
                        data.Count - hashLength);
                }
                finally
                {
                    ownedHmac?.Dispose();
                }
            }

            if (!signOnly)
            {
                data = RemovePadding(data, iv.Length);
            }

            if (isNotValid != 0)
            {
                throw new CryptographicException("Invalid signature.");
            }

            return new ArraySegment<byte>(dataArray, 0, data.Offset + data.Count);
        }

        /// <summary>
        /// Zeros a buffer so that sensitive key material does not linger in memory.
        /// </summary>
        /// <param name="buffer">
        /// The buffer to overwrite with zeros.
        /// </param>
        public static void ZeroMemory(Span<byte> buffer)
        {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            CryptographicOperations.ZeroMemory(buffer);
#else
            buffer.Clear();
#endif
        }

        /// <summary>
        /// Compares two buffers in constant time when their lengths match, avoiding
        /// timing side channels during authentication tag and signature checks.
        /// </summary>
        /// <param name="left">
        /// The first buffer to compare.
        /// </param>
        /// <param name="right">
        /// The second buffer to compare.
        /// </param>
        /// <returns>
        /// <c>true</c> when both buffers have the same length and content; otherwise <c>false</c>.
        /// </returns>
        public static bool FixedTimeEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            return CryptographicOperations.FixedTimeEquals(left, right);
#else
            if (left.Length != right.Length)
            {
                return false;
            }

            int different = 0;
            for (int ii = 0; ii < left.Length; ii++)
            {
                different |= left[ii] ^ right[ii];
            }

            return different == 0;
#endif
        }
    }
}
