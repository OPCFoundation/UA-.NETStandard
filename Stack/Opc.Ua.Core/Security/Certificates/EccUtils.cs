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
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
        #region Public constants


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

        #endregion

        #region Private constants


        private const string NistP256KeyParameters = "06-08-2A-86-48-CE-3D-03-01-07";
        private const string NistP384KeyParameters = "06-05-2B-81-04-00-22";
        private const string BrainpoolP256r1KeyParameters = "06-09-2B-24-03-03-02-08-01-01-07";
        private const string BrainpoolP384r1KeyParameters = "06-09-2B-24-03-03-02-08-01-01-0B";

        #endregion



        /// <summary>
        /// Returns true if the certificate is an ECC certificate.
        /// </summary>
        /// <param name="securityPolicyUri"></param>
        /// <returns></returns>
        public static bool IsEccPolicy(string securityPolicyUri)
        {
            if (securityPolicyUri != null)
            {
                switch (securityPolicyUri)
                {
                    case SecurityPolicies.ECC_nistP256:
                    case SecurityPolicies.ECC_nistP384:
                    case SecurityPolicies.ECC_brainpoolP256r1:
                    case SecurityPolicies.ECC_brainpoolP384r1:
                    case SecurityPolicies.ECC_curve25519:
                    case SecurityPolicies.ECC_curve448:
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the NodeId for the certificate type for the specified certificate.
        /// </summary>
        /// <param name="certificate"></param>
        /// <returns></returns>
        public static NodeId GetEccCertificateTypeId(X509Certificate2 certificate)
        {
            var keyAlgorithm = certificate.GetKeyAlgorithm();
            if (keyAlgorithm != Oids.ECPublicKey)
            {
                return NodeId.Null;
            }

            PublicKey encodedPublicKey = certificate.PublicKey;
            string keyParameters = BitConverter.ToString(encodedPublicKey.EncodedParameters.RawData);
            switch (keyParameters)
            {
                // nistP256
                case NistP256KeyParameters: return ObjectTypeIds.EccNistP256ApplicationCertificateType;
                // nistP384
                case NistP384KeyParameters: return ObjectTypeIds.EccNistP384ApplicationCertificateType;
                // brainpoolP256r1
                case BrainpoolP256r1KeyParameters: return ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType;
                // brainpoolP384r1
                case BrainpoolP384r1KeyParameters: return ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType;
                default: return NodeId.Null;
            }
        }

        /// <summary>
        /// Returns the signature algorithm for the specified certificate.
        /// </summary>
        /// <param name="certificate"></param>
        /// <returns></returns>
        public static string GetECDsaQualifier(X509Certificate2 certificate)
        {
            if (X509Utils.IsECDsaSignature(certificate))
            {
                string signatureQualifier = "ECDsa";
                PublicKey encodedPublicKey = certificate.PublicKey;
                string keyParameters = BitConverter.ToString(encodedPublicKey.EncodedParameters.RawData);

                // New values can be determined by running the dotted-decimal OID value
                // through BitConverter.ToString(CryptoConfig.EncodeOID(dottedDecimal));

                switch (keyParameters)
                {
                    case NistP256KeyParameters:
                    {
                        signatureQualifier = NistP256;
                        break;
                    }

                    case NistP384KeyParameters:
                    {
                        signatureQualifier = NistP384;
                        break;
                    }

                    case BrainpoolP256r1KeyParameters:
                    {
                        signatureQualifier = BrainpoolP256r1;
                        break;
                    }

                    case BrainpoolP384r1KeyParameters:
                    {
                        signatureQualifier = BrainpoolP384r1;
                        break;
                    }
                }
                return signatureQualifier;
            }
            return string.Empty;
        }

#if ECC_SUPPORT
        /// <summary>
        /// Returns the public key for the specified certificate.
        /// </summary>
        /// <param name="certificate"></param>
        /// <returns></returns>
        public static ECDsa GetPublicKey(X509Certificate2 certificate)
        {
            string[] securityPolicyUris;
            return GetPublicKey(certificate, out securityPolicyUris);
        }

        /// <summary>
        /// Returns the public key for the specified certificate and ouputs the security policy uris.
        /// </summary>
        /// <param name="certificate"></param>
        /// <param name="securityPolicyUris"></param>
        /// <returns></returns>
        public static ECDsa GetPublicKey(X509Certificate2 certificate, out string[] securityPolicyUris)
        {
            securityPolicyUris = null;

            var keyAlgorithm = certificate.GetKeyAlgorithm();

            if (certificate == null || keyAlgorithm != Oids.ECPublicKey)
            {
                return null;
            }

            const X509KeyUsageFlags SufficientFlags =
                X509KeyUsageFlags.KeyAgreement |
                X509KeyUsageFlags.DigitalSignature |
                X509KeyUsageFlags.NonRepudiation |
                X509KeyUsageFlags.CrlSign |
                X509KeyUsageFlags.KeyCertSign;

            foreach (X509Extension extension in certificate.Extensions)
            {
                if (extension.Oid.Value == "2.5.29.15")
                {
                    X509KeyUsageExtension kuExt = (X509KeyUsageExtension)extension;

                    if ((kuExt.KeyUsages & SufficientFlags) == 0)
                    {
                        return null;
                    }
                }
            }

            PublicKey encodedPublicKey = certificate.PublicKey;
            string keyParameters = BitConverter.ToString(encodedPublicKey.EncodedParameters.RawData);
            byte[] keyValue = encodedPublicKey.EncodedKeyValue.RawData;

            ECParameters ecParameters = default(ECParameters);

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
                {
                    ecParameters.Curve = ECCurve.NamedCurves.nistP256;
                    securityPolicyUris = new string[] { SecurityPolicies.ECC_nistP256 };
                    break;
                }

                case NistP384KeyParameters:
                {
                    ecParameters.Curve = ECCurve.NamedCurves.nistP384;
                    securityPolicyUris = new string[] { SecurityPolicies.ECC_nistP384, SecurityPolicies.ECC_nistP256 };
                    break;
                }

                case BrainpoolP256r1KeyParameters:
                {
                    ecParameters.Curve = ECCurve.NamedCurves.brainpoolP256r1;
                    securityPolicyUris = new string[] { SecurityPolicies.ECC_brainpoolP256r1 };
                    break;
                }

                case BrainpoolP384r1KeyParameters:
                {
                    ecParameters.Curve = ECCurve.NamedCurves.brainpoolP384r1;
                    securityPolicyUris = new string[] { SecurityPolicies.ECC_brainpoolP384r1, SecurityPolicies.ECC_brainpoolP256r1 };
                    break;
                }

                default:
                {
                    throw new NotImplementedException(keyParameters);
                }
            }

            return ECDsa.Create(ecParameters);
        }

        /// <summary>
        /// Returns the length of a ECDsa signature of a digest.
        /// </summary>
        public static int GetSignatureLength(X509Certificate2 signingCertificate)
        {
            if (signingCertificate == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No public key for certificate.");
            }
            using (var publicKey = GetPublicKey(signingCertificate))
            {
                if (publicKey == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No public key for certificate.");
                }

                return publicKey.KeySize / 4;
            }

            throw new NotImplementedException();
        }

        /// <summary>
        /// Returns the hash algorithm for the specified security policy.
        /// </summary>
        /// <param name="securityPolicyUri"></param>
        /// <returns></returns>
        public static HashAlgorithmName GetSignatureAlgorithmName(string securityPolicyUri)
        {
            if (securityPolicyUri == null)
            {
                throw new ArgumentNullException(nameof(securityPolicyUri));
            }

            switch (securityPolicyUri)
            {
                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_brainpoolP256r1:
                {
                    return HashAlgorithmName.SHA256;
                }

                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP384r1:
                {
                    return HashAlgorithmName.SHA384;
                }

                case SecurityPolicies.None:
                case SecurityPolicies.ECC_curve25519:
                case SecurityPolicies.ECC_curve448:
                default:
                {
                    return HashAlgorithmName.SHA256;
                }
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
            var algorithm = GetSignatureAlgorithmName(securityPolicyUri);
            return Sign(dataToSign, signingCertificate, algorithm);
        }

        /// <summary>
        /// Computes an ECDSA signature.
        /// </summary>
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
#if DEBUG
                var verifier = new Ed25519Signer();

                verifier.Init(false, signingCertificate.BcCertificate.GetPublicKey());
                verifier.BlockUpdate(dataToSign.Array, dataToSign.Offset, dataToSign.Count);

                if (!verifier.VerifySignature(signature))
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed, "Could not verify signature.");
                }
#endif
                return signature;
            }

            if (publicKey is Ed448PublicKeyParameters)
            {
                var signer = new Ed448Signer(new byte[32]);

                signer.Init(true, signingCertificate.BcPrivateKey);
                signer.BlockUpdate(dataToSign.Array, dataToSign.Offset, dataToSign.Count);
                byte[] signature = signer.GenerateSignature();
#if DEBUG
                var verifier = new Ed448Signer(new byte[32]);

                verifier.Init(false, signingCertificate.BcCertificate.GetPublicKey());
                verifier.BlockUpdate(dataToSign.Array, dataToSign.Offset, dataToSign.Count);

                if (!verifier.VerifySignature(signature))
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed, "Could not verify signature.");
                }
#endif
                return signature;
            }
#endif
            var senderPrivateKey = signingCertificate.GetECDsaPrivateKey() as ECDsa;

            if (senderPrivateKey == null)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Missing private key needed for create a signature.");
            }

            using (senderPrivateKey)
            {
                var signature = senderPrivateKey.SignData(dataToSign.Array, dataToSign.Offset, dataToSign.Count, algorithm);

#if DEBUGxxx
                using (ECDsa ecdsa = EccUtils.GetPublicKey(new X509Certificate2(signingCertificate.RawData)))
                {
                    if (!ecdsa.VerifyData(dataToSign.Array, dataToSign.Offset, dataToSign.Count, signature, algorithm))
                    {
                        throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed, "Could not verify signature.");
                    }
                }
#endif

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
            return Verify(dataToVerify, signature, signingCertificate, GetSignatureAlgorithmName(securityPolicyUri));
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
            using (ECDsa ecdsa = EccUtils.GetPublicKey(signingCertificate))
            {
                return ecdsa.VerifyData(dataToVerify.Array, dataToVerify.Offset, dataToVerify.Count, signature, algorithm);
            }
        }
    }

    /// <summary>
    /// Utility class for encrypting and decrypting secrets using Elliptic Curve Cryptography (ECC).
    /// </summary>
    public class EncryptedSecret
    {
        /// <summary>
        /// Gets or sets the X.509 certificate of the sender.
        /// </summary>
        public X509Certificate2 SenderCertificate { get; set; }

        /// <summary>
        /// Gets or sets the collection of X.509 certificates of the sender's issuer.
        /// </summary>
        public X509Certificate2Collection SenderIssuerCertificates { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the sender's certificate should not be encoded.
        /// </summary>
        public bool DoNotEncodeSenderCertificate { get; set; }

        /// <summary>
        /// Gets or sets the nonce of the sender.
        /// </summary>
        public Nonce SenderNonce { get; set; }

        /// <summary>
        /// Gets or sets the nonce of the receiver.
        /// </summary>
        public Nonce ReceiverNonce { get; set; }

        /// <summary>
        /// Gets or sets the X.509 certificate of the receiver.
        /// </summary>
        public X509Certificate2 ReceiverCertificate { get; set; }

        /// <summary>
        /// Gets or sets the certificate validator.
        /// </summary>
        public CertificateValidator Validator { get; set; }

        /// <summary>
        /// Gets or sets the security policy URI.
        /// </summary>
        public string SecurityPolicyUri { get; set; }

        /// <summary>
        /// Encrypts a secret using the specified nonce, encrypting key, and initialization vector (IV).
        /// </summary>
        /// <param name="secret">The secret to encrypt.</param>
        /// <param name="nonce">The nonce to use for encryption.</param>
        /// <param name="encryptingKey">The key to use for encryption.</param>
        /// <param name="iv">The initialization vector to use for encryption.</param>
        /// <returns>The encrypted secret.</returns>
        private static byte[] EncryptSecret(
            byte[] secret,
            byte[] nonce,
            byte[] encryptingKey,
            byte[] iv)
        {
#if CURVE25519
            bool useAuthenticatedEncryption = false;
            if (SenderCertificate.BcCertificate.GetPublicKey() is Ed25519PublicKeyParameters ||
                SenderCertificate.BcCertificate.GetPublicKey() is Ed448PublicKeyParameters)
            {
                useAuthenticatedEncryption = true;
            }
#endif
            byte[] dataToEncrypt = null;

            using (var encoder = new BinaryEncoder(ServiceMessageContext.GlobalContext))
            {
                encoder.WriteByteString(null, nonce);
                encoder.WriteByteString(null, secret);

                // add padding.
                int paddingSize = (iv.Length - ((encoder.Position + 2) % iv.Length));
                paddingSize %= iv.Length;

                if (secret.Length + paddingSize < iv.Length)
                {
                    paddingSize += iv.Length;
                }

                for (int ii = 0; ii < paddingSize; ii++)
                {
                    encoder.WriteByte(null, (byte)(paddingSize & 0xFF));
                }

                encoder.WriteUInt16(null, (ushort)paddingSize);

                dataToEncrypt = encoder.CloseAndReturnBuffer();
            }
#if CURVE25519
            if (useAuthenticatedEncryption)
            {
                return EncryptWithChaCha20Poly1305(encryptingKey, iv, dataToEncrypt);
            }
#endif
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = encryptingKey;
                aes.IV = iv;

                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    if (dataToEncrypt.Length % encryptor.InputBlockSize != 0)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Input data is not an even number of encryption blocks.");
                    }

                    encryptor.TransformBlock(dataToEncrypt, 0, dataToEncrypt.Length, dataToEncrypt, 0);
                }
            }

            return dataToEncrypt;
        }

#if CURVE25519
        /// <summary>
        /// Encrypts the given data using the ChaCha20Poly1305 algorithm with the provided key and initialization vector (IV).
        /// </summary>
        /// <param name="encryptingKey">The key used for encryption.</param>
        /// <param name="iv">The initialization vector used for encryption.</param>
        /// <param name="dataToEncrypt">The data to be encrypted.</param>
        /// <returns>The encrypted data.</returns>
        private static byte[] EncryptWithChaCha20Poly1305(
            byte[] encryptingKey,
            byte[] iv,
            byte[] dataToEncrypt)
        {
            Utils.Trace($"EncryptKey={Utils.ToHexString(encryptingKey)}");
            Utils.Trace($"EncryptIV={Utils.ToHexString(iv)}");

            int signatureLength = 16;

            AeadParameters parameters = new AeadParameters(
                new KeyParameter(encryptingKey),
                signatureLength * 8,
                iv,
                null);

            ChaCha20Poly1305 encryptor = new ChaCha20Poly1305();
            encryptor.Init(true, parameters);

            byte[] ciphertext = new byte[encryptor.GetOutputSize(dataToEncrypt.Length)];
            int length = encryptor.ProcessBytes(dataToEncrypt, 0, dataToEncrypt.Length, ciphertext, 0);
            length += encryptor.DoFinal(ciphertext, length);

            if (ciphertext.Length != length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    $"CipherText not the expected size. [{ciphertext.Length} != {length}]");
            }

            return ciphertext;
        }

        /// <summary>
        /// Decrypts the given data using the ChaCha20Poly1305 algorithm with the provided key and initialization vector (IV).
        /// </summary>
        /// <param name="encryptingKey">The key used for encryption.</param>
        /// <param name="iv">The initialization vector used for encryption.</param>
        /// <param name="dataToDecrypt">The data to be decrypted.</param>
        /// <param name="offset">The offset in the data to start decrypting from.</param>
        /// <param name="count">The number of bytes to decrypt.</param>
        /// <returns>An <see cref="ArraySegment{T}"/> containing the decrypted data.</returns>
        /// <exception cref="ServiceResultException">Thrown if the plaintext is not the expected size or too short, or if the nonce is invalid.</exception>
        private ArraySegment<byte> DecryptWithChaCha20Poly1305(
            byte[] encryptingKey,
            byte[] iv,
            byte[] dataToDecrypt,
            int offset,
            int count)
        {
            Utils.Trace($"EncryptKey={Utils.ToHexString(encryptingKey)}");
            Utils.Trace($"EncryptIV={Utils.ToHexString(iv)}");

            int signatureLength = 16;

            AeadParameters parameters = new AeadParameters(
                new KeyParameter(encryptingKey),
                signatureLength * 8,
                iv,
                null);

            ChaCha20Poly1305 decryptor = new ChaCha20Poly1305();
            decryptor.Init(false, parameters);

            byte[] plaintext = new byte[decryptor.GetOutputSize(count)];
            int length = decryptor.ProcessBytes(dataToDecrypt, offset, count, plaintext, 0);
            length += decryptor.DoFinal(plaintext, length);

            if (plaintext.Length != length || plaintext.Length < iv.Length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    $"PlainText not the expected size or too short. [{count} != {length}]");
            }

            ushort paddingSize = plaintext[length-1];
            paddingSize <<= 8;
            paddingSize += plaintext[length - 2];

            int notvalid = (paddingSize < length) ? 0 : 1;
            int start = length - paddingSize - 2;

            for (int ii = 0; ii < length - 2 && ii < paddingSize; ii++)
            {
                if (start < 0 || start + ii >= plaintext.Length)
                {
                    notvalid |= 1;
                    continue;
                }

                notvalid |= plaintext[start + ii] ^ (paddingSize & 0xFF);
            }
            
            if (notvalid != 0)
            {
                throw new ServiceResultException(StatusCodes.BadNonceInvalid);
            }

            return new ArraySegment<byte>(plaintext, 0, start);
        }
#endif


        /// <summary>
        /// Decrypts the specified data using the provided encrypting key and initialization vector (IV).
        /// </summary>
        /// <param name="dataToDecrypt">The data to decrypt.</param>
        /// <param name="offset">The offset in the data to start decrypting from.</param>
        /// <param name="count">The number of bytes to decrypt.</param>
        /// <param name="encryptingKey">The key to use for decryption.</param>
        /// <param name="iv">The initialization vector to use for decryption.</param>
        /// <returns>The decrypted data.</returns>
        /// <exception cref="ServiceResultException">Thrown if the input data is not an even number of encryption blocks or if the nonce is invalid.</exception>
        private static ArraySegment<byte> DecryptSecret(
            byte[] dataToDecrypt,
            int offset,
            int count,
            byte[] encryptingKey,
            byte[] iv)
        {
#if CURVE25519
            bool useAuthenticatedEncryption = false;
            if (SenderCertificate.BcCertificate.GetPublicKey() is Ed25519PublicKeyParameters ||
                SenderCertificate.BcCertificate.GetPublicKey() is Ed448PublicKeyParameters)
            {
                useAuthenticatedEncryption = true;
            }
            if (useAuthenticatedEncryption)
            {
                return DecryptWithChaCha20Poly1305(encryptingKey, iv, dataToDecrypt, offset, count);
            }
#endif
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = encryptingKey;
                aes.IV = iv;

                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                {
                    if (count % decryptor.InputBlockSize != 0)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Input data is not an even number of encryption blocks.");
                    }

                    decryptor.TransformBlock(dataToDecrypt, offset, count, dataToDecrypt, offset);
                }
            }

            ushort paddingSize = dataToDecrypt[offset + count - 1];
            paddingSize <<= 8;
            paddingSize += dataToDecrypt[offset + count - 2];

            int notvalid = (paddingSize < count) ? 0 : 1;
            int start = offset + count - paddingSize - 2;

            for (int ii = 0; ii < count - 2 && ii < paddingSize; ii++)
            {
                if (start < 0 || start + ii >= dataToDecrypt.Length)
                {
                    notvalid |= 1;
                    continue;
                }

                notvalid |= dataToDecrypt[start + ii] ^ (paddingSize & 0xFF);
            }

            if (notvalid != 0)
            {
                throw new ServiceResultException(StatusCodes.BadNonceInvalid);
            }

            return new ArraySegment<byte>(dataToDecrypt, offset, count - paddingSize);
        }


        private static readonly byte[] s_Label = new UTF8Encoding().GetBytes("opcua-secret");


        /// <summary>
        /// Creates the encrypting key and initialization vector (IV) for Elliptic Curve Cryptography (ECC) encryption or decryption.
        /// </summary>
        /// <param name="securityPolicyUri">The security policy URI.</param>
        /// <param name="senderNonce">The sender nonce.</param>
        /// <param name="receiverNonce">The receiver nonce.</param>
        /// <param name="forDecryption">if set to <c>true</c>, creates the keys for decryption; otherwise, creates the keys for encryption.</param>
        /// <param name="encryptingKey">The encrypting key.</param>
        /// <param name="iv">The initialization vector (IV).</param>
        private static void CreateKeysForEcc(
            string securityPolicyUri,
            Nonce senderNonce,
            Nonce receiverNonce,
            bool forDecryption,
            out byte[] encryptingKey,
            out byte[] iv)
        {
            int encryptingKeySize = 32;
            int blockSize = 16;
            HashAlgorithmName algorithmName = HashAlgorithmName.SHA256;

            switch (securityPolicyUri)
            {
                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_brainpoolP256r1:
                {
                    encryptingKeySize = 16;
                    break;
                }

                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP384r1:
                {
                    encryptingKeySize = 32;
                    algorithmName = HashAlgorithmName.SHA384;
                    break;
                }

                case SecurityPolicies.ECC_curve25519:
                case SecurityPolicies.ECC_curve448:
                {
                    encryptingKeySize = 32;
                    blockSize = 12;
                    algorithmName = HashAlgorithmName.SHA256;
                    break;
                }
            }

            encryptingKey = new byte[encryptingKeySize];
            iv = new byte[blockSize];

            var keyLength = BitConverter.GetBytes((ushort)(encryptingKeySize + blockSize));
            var salt = Utils.Append(keyLength, s_Label, senderNonce.Data, receiverNonce.Data);

            byte[] keyData = null;

            if (forDecryption)
            {
                keyData = receiverNonce.DeriveKey(senderNonce, salt, algorithmName, encryptingKeySize + blockSize);
            }
            else
            {
                keyData = senderNonce.DeriveKey(receiverNonce, salt, algorithmName, encryptingKeySize + blockSize);
            }

            Buffer.BlockCopy(keyData, 0, encryptingKey, 0, encryptingKey.Length);
            Buffer.BlockCopy(keyData, encryptingKeySize, iv, 0, iv.Length);
        }

        /// <summary>
        /// Encrypts a secret using the specified nonce.
        /// </summary>
        /// <param name="secret">The secret to encrypt.</param>
        /// <param name="nonce">The nonce to use for encryption.</param>
        /// <returns>The encrypted secret.</returns>
        public byte[] Encrypt(byte[] secret, byte[] nonce)
        {
            byte[] encryptingKey = null;
            byte[] iv = null;
            byte[] message = null;
            int lengthPosition = 0;

            var signatureLength = EccUtils.GetSignatureLength(SenderCertificate);

            using (BinaryEncoder encoder = new BinaryEncoder(ServiceMessageContext.GlobalContext))
            {
                // write header.
                encoder.WriteNodeId(null, DataTypeIds.EccEncryptedSecret);
                encoder.WriteByte(null, (byte)ExtensionObjectEncoding.Binary);

                lengthPosition = encoder.Position;
                encoder.WriteUInt32(null, 0);

                encoder.WriteString(null, SecurityPolicyUri);

                byte[] senderCertificate = null;

                if (!DoNotEncodeSenderCertificate)
                {
                    senderCertificate = SenderCertificate.RawData;

                    if (SenderIssuerCertificates != null && SenderIssuerCertificates.Count > 0)
                    {
                        int blobSize = senderCertificate.Length;

                        foreach (var issuer in SenderIssuerCertificates)
                        {
                            blobSize += issuer.RawData.Length;
                        }

                        var blob = new byte[blobSize];
                        Buffer.BlockCopy(senderCertificate, 0, blob, 0, senderCertificate.Length);

                        int pos = senderCertificate.Length;

                        foreach (var issuer in SenderIssuerCertificates)
                        {
                            var data = issuer.RawData;
                            Buffer.BlockCopy(data, 0, blob, pos, data.Length);
                            pos += data.Length;
                        }

                        senderCertificate = blob;
                    }
                }

                encoder.WriteByteString(null, senderCertificate);
                encoder.WriteDateTime(null, DateTime.UtcNow);

                var senderNonce = SenderNonce.Data;
                var receiverNonce = ReceiverNonce.Data;

                encoder.WriteUInt16(null, (ushort)(senderNonce.Length + receiverNonce.Length + 8));
                encoder.WriteByteString(null, senderNonce);
                encoder.WriteByteString(null, receiverNonce);

                // create keys.
                if (EccUtils.IsEccPolicy(SecurityPolicyUri))
                {
                    CreateKeysForEcc(SecurityPolicyUri, SenderNonce, ReceiverNonce, false, out encryptingKey, out iv);
                }

                // encrypt  secret,
                var encryptedData = EncryptSecret(secret, nonce, encryptingKey, iv);

                // append encrypted secret.
                for (int ii = 0; ii < encryptedData.Length; ii++)
                {
                    encoder.WriteByte(null, encryptedData[ii]);
                }

                // save space for signature.
                for (int ii = 0; ii < signatureLength; ii++)
                {
                    encoder.WriteByte(null, 0);
                }

                message = encoder.CloseAndReturnBuffer();
            }

            var length = message.Length - lengthPosition - 4;

            message[lengthPosition++] = (byte)((length & 0xFF));
            message[lengthPosition++] = (byte)((length & 0xFF00) >> 8);
            message[lengthPosition++] = (byte)((length & 0xFF0000) >> 16);
            message[lengthPosition++] = (byte)((length & 0xFF000000) >> 24);

            // get the algorithm used for the signature.
            HashAlgorithmName signatureAlgorithm = HashAlgorithmName.SHA256;

            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP384r1:
                {
                    signatureAlgorithm = HashAlgorithmName.SHA384;
                    break;
                }
            }

            ArraySegment<byte> dataToSign = new ArraySegment<byte>(message, 0, message.Length - signatureLength);
            var signature = EccUtils.Sign(dataToSign, SenderCertificate, signatureAlgorithm);
            Buffer.BlockCopy(signature, 0, message, message.Length - signatureLength, signatureLength);
            return message;
        }

        /// <summary>
        /// Verifies the header for an ECC encrypted message and returns the encrypted data.
        /// </summary>
        /// <param name="dataToDecrypt">The data to decrypt.</param>
        /// <param name="earliestTime">The earliest time allowed for the message signing time.</param>
        /// <returns>The encrypted data.</returns>
        private ArraySegment<byte> VerifyHeaderForEcc(
            ArraySegment<byte> dataToDecrypt,
            DateTime earliestTime)
        {
            using (BinaryDecoder decoder = new BinaryDecoder(dataToDecrypt.Array, dataToDecrypt.Offset, dataToDecrypt.Count, ServiceMessageContext.GlobalContext))
            {
                var typeId = decoder.ReadNodeId(null);

                if (typeId != DataTypeIds.EccEncryptedSecret)
                {
                    throw new ServiceResultException(StatusCodes.BadDataTypeIdUnknown);
                }

                var encoding = (ExtensionObjectEncoding)decoder.ReadByte(null);

                if (encoding != ExtensionObjectEncoding.Binary)
                {
                    throw new ServiceResultException(StatusCodes.BadDataEncodingUnsupported);
                }

                var length = decoder.ReadUInt32(null);

                // get the start of data.
                int startOfData = decoder.Position + dataToDecrypt.Offset;

                SecurityPolicyUri = decoder.ReadString(null);

                if (!EccUtils.IsEccPolicy(SecurityPolicyUri))
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityPolicyRejected);
                }

                // get the algorithm used for the signature.
                HashAlgorithmName signatureAlgorithm = HashAlgorithmName.SHA256;

                switch (SecurityPolicyUri)
                {
                    case SecurityPolicies.ECC_nistP384:
                    case SecurityPolicies.ECC_brainpoolP384r1:
                    {
                        signatureAlgorithm = HashAlgorithmName.SHA384;
                        break;
                    }
                }

                // extract the send certificate and any chain.
                var senderCertificate = decoder.ReadByteString(null);

                if (senderCertificate == null || senderCertificate.Length == 0)
                {
                    if (SenderCertificate == null)
                    {
                        throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
                    }
                }
                else
                {
                    var senderCertificateChain = Utils.ParseCertificateChainBlob(senderCertificate);

                    SenderCertificate = senderCertificateChain[0];
                    SenderIssuerCertificates = new X509Certificate2Collection();

                    for (int ii = 1; ii < senderCertificateChain.Count; ii++)
                    {
                        SenderIssuerCertificates.Add(senderCertificateChain[ii]);
                    }

                    // validate the sender.
                    if (Validator != null)
                    {
                        Validator.Validate(senderCertificateChain);
                    }
                }

                // extract the send certificate and any chain.
                var signingTime = decoder.ReadDateTime(null);

                if (signingTime < earliestTime)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidTimestamp);
                }

                // extract the policy header.
                var headerLength = decoder.ReadUInt16(null);

                if (headerLength == 0 || headerLength > length)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError);
                }

                // read the policy header.
                var senderPublicKey = decoder.ReadByteString(null);
                var receiverPublicKey = decoder.ReadByteString(null);

                if (headerLength != senderPublicKey.Length + receiverPublicKey.Length + 8)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError, "Unexpected policy header length");
                }

                var startOfEncryption = decoder.Position;

                SenderNonce = Nonce.CreateNonce(SecurityPolicyUri, senderPublicKey);

                if (!Utils.IsEqual(receiverPublicKey, ReceiverNonce.Data))
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError, "Unexpected receiver nonce.");
                }

                // check the signature.
                int signatureLength = EccUtils.GetSignatureLength(SenderCertificate);

                if (signatureLength >= length)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError);
                }

                byte[] signature = new byte[signatureLength];
                Buffer.BlockCopy(dataToDecrypt.Array, startOfData + (int)length - signatureLength, signature, 0, signatureLength);

                ArraySegment<byte> dataToSign = new ArraySegment<byte>(dataToDecrypt.Array, 0, startOfData + (int)length - signatureLength);

                if (!EccUtils.Verify(dataToSign, signature, SenderCertificate, signatureAlgorithm))
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed, "Could not verify signature.");
                }

                // extract the encrypted data.
                return new ArraySegment<byte>(dataToDecrypt.Array, startOfEncryption, (int)length - (startOfEncryption - startOfData + signatureLength));
            }
        }

        /// <summary>
        /// Decrypts the specified data using the ECC algorithm.
        /// </summary>
        /// <param name="earliestTime">The earliest time allowed for the message.</param>
        /// <param name="expectedNonce">The expected nonce value.</param>
        /// <param name="data">The data to decrypt.</param>
        /// <param name="offset">The offset of the data to decrypt.</param>
        /// <param name="count">The number of bytes to decrypt.</param>
        /// <returns>The decrypted data.</returns>
        public byte[] Decrypt(DateTime earliestTime, byte[] expectedNonce, byte[] data, int offset, int count)
        {
            byte[] encryptingKey = null;
            byte[] iv = null;
            byte[] secret = null;

            var dataToDecrypt = VerifyHeaderForEcc(new ArraySegment<byte>(data, offset, count), earliestTime);

            CreateKeysForEcc(SecurityPolicyUri, SenderNonce, ReceiverNonce, true, out encryptingKey, out iv);

            var plainText = DecryptSecret(dataToDecrypt.Array, dataToDecrypt.Offset, dataToDecrypt.Count, encryptingKey, iv);

            using (BinaryDecoder decoder = new BinaryDecoder(plainText.Array, plainText.Offset, plainText.Count, ServiceMessageContext.GlobalContext))
            {
                var actualNonce = decoder.ReadByteString(null);

                if (expectedNonce != null && expectedNonce.Length > 0)
                {
                    int notvalid = (expectedNonce.Length == actualNonce.Length) ? 0 : 1;

                    for (int ii = 0; ii < expectedNonce.Length && ii < actualNonce.Length; ii++)
                    {
                        notvalid |= expectedNonce[ii] ^ actualNonce[ii];
                    }

                    if (notvalid != 0)
                    {
                        throw new ServiceResultException(StatusCodes.BadNonceInvalid);
                    }
                }

                secret = decoder.ReadByteString(null);
            }

            return secret;
        }
#else
        /// <summary>
        /// Verifies a ECDsa signature.
        /// </summary>
        public static bool Verify(
            ArraySegment<byte> dataToVerify,
            byte[] signature,
            X509Certificate2 signingCertificate,
            string securityPolicyUri)
        {
            return Verify(dataToVerify, signature, signingCertificate, GetSignatureAlgorithmName(securityPolicyUri));
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
            using (ECDsa ecdsa = EccUtils.GetPublicKey(signingCertificate))
            {
                return ecdsa.VerifyData(dataToVerify.Array, dataToVerify.Offset, dataToVerify.Count, signature, algorithm);
            }
        }

        /// <summary>
        /// Returns the public key for the specified certificate and ouputs the security policy uris.
        /// </summary>
        /// <param name="certificate"></param>
        /// <param name="securityPolicyUris"></param>
        /// <returns></returns>
        public static ECDsa GetPublicKey(X509Certificate2 certificate, out string[] securityPolicyUris)
        {
            securityPolicyUris = null;

            var keyAlgorithm = certificate.GetKeyAlgorithm();

            if (certificate == null || keyAlgorithm != Oids.ECPublicKey)
            {
                return null;
            }

            const X509KeyUsageFlags SufficientFlags =
                X509KeyUsageFlags.KeyAgreement |
                X509KeyUsageFlags.DigitalSignature |
                X509KeyUsageFlags.NonRepudiation |
                X509KeyUsageFlags.CrlSign |
                X509KeyUsageFlags.KeyCertSign;

            foreach (X509Extension extension in certificate.Extensions)
            {
                if (extension.Oid.Value == "2.5.29.15")
                {
                    X509KeyUsageExtension kuExt = (X509KeyUsageExtension)extension;

                    if ((kuExt.KeyUsages & SufficientFlags) == 0)
                    {
                        return null;
                    }
                }
            }

            PublicKey encodedPublicKey = certificate.PublicKey;
            string keyParameters = BitConverter.ToString(encodedPublicKey.EncodedParameters.RawData);
            byte[] keyValue = encodedPublicKey.EncodedKeyValue.RawData;

            ECParameters ecParameters = default(ECParameters);

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
                {
                    ecParameters.Curve = ECCurve.NamedCurves.nistP256;
                    securityPolicyUris = new string[] { SecurityPolicies.ECC_nistP256 };
                    break;
                }

                case NistP384KeyParameters:
                {
                    ecParameters.Curve = ECCurve.NamedCurves.nistP384;
                    securityPolicyUris = new string[] { SecurityPolicies.ECC_nistP384, SecurityPolicies.ECC_nistP256 };
                    break;
                }

                case BrainpoolP256r1KeyParameters:
                {
                    ecParameters.Curve = ECCurve.NamedCurves.brainpoolP256r1;
                    securityPolicyUris = new string[] { SecurityPolicies.ECC_brainpoolP256r1 };
                    break;
                }

                case BrainpoolP384r1KeyParameters:
                {
                    ecParameters.Curve = ECCurve.NamedCurves.brainpoolP384r1;
                    securityPolicyUris = new string[] { SecurityPolicies.ECC_brainpoolP384r1, SecurityPolicies.ECC_brainpoolP256r1 };
                    break;
                }

                default:
                {
                    throw new NotImplementedException(keyParameters);
                }
            }

            return ECDsa.Create(ecParameters);
        }

        /// <summary>
        /// Returns the public key for the specified certificate.
        /// </summary>
        /// <param name="certificate"></param>
        /// <returns></returns>
        public static ECDsa GetPublicKey(X509Certificate2 certificate)
        {
            string[] securityPolicyUris;
            return GetPublicKey(certificate, out securityPolicyUris);
        }

            /// <summary>
        /// Returns the hash algorithm for the specified security policy.
        /// </summary>
        /// <param name="securityPolicyUri"></param>
        /// <returns></returns>
        public static HashAlgorithmName GetSignatureAlgorithmName(string securityPolicyUri)
        {
            if (securityPolicyUri == null)
            {
                throw new ArgumentNullException(nameof(securityPolicyUri));
            }

            switch (securityPolicyUri)
            {
                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_brainpoolP256r1:
                {
                    return HashAlgorithmName.SHA256;
                }

                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP384r1:
                {
                    return HashAlgorithmName.SHA384;
                }

                case SecurityPolicies.None:
                case SecurityPolicies.ECC_curve25519:
                case SecurityPolicies.ECC_curve448:
                default:
                {
                    return HashAlgorithmName.SHA256;
                }
            }
        }
    
#endif
    }
}
