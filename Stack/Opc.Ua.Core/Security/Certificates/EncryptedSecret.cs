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
#if CURVE25519
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// Utility class for encrypting and decrypting secrets using Elliptic Curve Cryptography (ECC).
    /// </summary>
    public class EncryptedSecret
    {
        /// <summary>
        /// Create secret
        /// </summary>
        public EncryptedSecret(
            IServiceMessageContext context,
            string securityPolicyUri,
            X509Certificate2Collection senderIssuerCertificates,
            X509Certificate2 receiverCertificate,
            Nonce receiverNonce,
            X509Certificate2 senderCertificate,
            Nonce senderNonce,
            CertificateValidator validator = null,
            bool doNotEncodeSenderCertificate = false)
        {
            SenderCertificate = senderCertificate;
            SenderIssuerCertificates = senderIssuerCertificates;
            DoNotEncodeSenderCertificate = doNotEncodeSenderCertificate;
            SenderNonce = senderNonce;
            ReceiverNonce = receiverNonce;
            ReceiverCertificate = receiverCertificate;
            Validator = validator;
            SecurityPolicyUri = securityPolicyUri;
            Context = context;
        }

        /// <summary>
        /// Gets or sets the X.509 certificate of the sender.
        /// </summary>
        public X509Certificate2 SenderCertificate { get; private set; }

        /// <summary>
        /// Gets or sets the collection of X.509 certificates of the sender's issuer.
        /// </summary>
        public X509Certificate2Collection SenderIssuerCertificates { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating whether the sender's certificate should not be encoded.
        /// </summary>
        public bool DoNotEncodeSenderCertificate { get; }

        /// <summary>
        /// Gets or sets the nonce of the sender.
        /// </summary>
        public Nonce SenderNonce { get; private set; }

        /// <summary>
        /// Gets or sets the nonce of the receiver.
        /// </summary>
        public Nonce ReceiverNonce { get; }

        /// <summary>
        /// Gets or sets the X.509 certificate of the receiver.
        /// </summary>
        public X509Certificate2 ReceiverCertificate { get; }

        /// <summary>
        /// Gets or sets the certificate validator.
        /// </summary>
        public CertificateValidator Validator { get; }

        /// <summary>
        /// Gets or sets the security policy URI.
        /// </summary>
        public string SecurityPolicyUri { get; private set; }

        /// <summary>
        /// Service message context to use
        /// </summary>
        public IServiceMessageContext Context { get; }

        /// <summary>
        /// Encrypts a secret using the specified nonce, encrypting key, and initialization vector (IV).
        /// </summary>
        /// <param name="secret">The secret to encrypt.</param>
        /// <param name="nonce">The nonce to use for encryption.</param>
        /// <param name="encryptingKey">The key to use for encryption.</param>
        /// <param name="iv">The initialization vector to use for encryption.</param>
        /// <returns>The encrypted secret.</returns>
        /// <exception cref="ServiceResultException"></exception>
        private byte[] EncryptSecret(
            byte[] secret,
            byte[] nonce,
            byte[] encryptingKey,
            byte[] iv)
        {
#if CURVE25519
            bool useAuthenticatedEncryption = false;
            if (SenderCertificate.BcCertificate.GetPublicKey() is Ed25519PublicKeyParameters
                || SenderCertificate.BcCertificate.GetPublicKey() is Ed448PublicKeyParameters)
            {
                useAuthenticatedEncryption = true;
            }
#endif
            byte[] dataToEncrypt = null;

            using (var encoder = new BinaryEncoder(Context))
            {
                encoder.WriteByteString(null, nonce);
                encoder.WriteByteString(null, secret);

                // add padding.
                int paddingSize = iv.Length - ((encoder.Position + 2) % iv.Length);
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
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = encryptingKey;
                aes.IV = iv;

#pragma warning disable CA5401 // Symmetric encryption uses non-default initialization vector, which could be potentially repeatable
                using ICryptoTransform encryptor = aes.CreateEncryptor();
#pragma warning restore CA5401 // Symmetric encryption uses non-default initialization vector, which could be potentially repeatable
                if (dataToEncrypt.Length % encryptor.InputBlockSize != 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        "Input data is not an even number of encryption blocks.");
                }

                encryptor.TransformBlock(dataToEncrypt, 0, dataToEncrypt.Length, dataToEncrypt, 0);
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
        private static byte[] EncryptWithChaCha20Poly1305(byte[] encryptingKey, byte[] iv, byte[] dataToEncrypt)
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

            ushort paddingSize = plaintext[length - 1];
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
        private ArraySegment<byte> DecryptSecret(
            byte[] dataToDecrypt,
            int offset,
            int count,
            byte[] encryptingKey,
            byte[] iv)
        {
#if CURVE25519
            bool useAuthenticatedEncryption = false;
            if (SenderCertificate.BcCertificate.GetPublicKey() is Ed25519PublicKeyParameters
                || SenderCertificate.BcCertificate.GetPublicKey() is Ed448PublicKeyParameters)
            {
                useAuthenticatedEncryption = true;
            }
            if (useAuthenticatedEncryption)
            {
                return DecryptWithChaCha20Poly1305(encryptingKey, iv, dataToDecrypt, offset, count);
            }
#endif
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = encryptingKey;
                aes.IV = iv;

                using ICryptoTransform decryptor = aes.CreateDecryptor();
                if (count % decryptor.InputBlockSize != 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        "Input data is not an even number of encryption blocks.");
                }

                decryptor.TransformBlock(dataToDecrypt, offset, count, dataToDecrypt, offset);
            }

            ushort paddingSize = dataToDecrypt[offset + count - 1];
            paddingSize <<= 8;
            paddingSize += dataToDecrypt[offset + count - 2];

            int notvalid = paddingSize < count ? 0 : 1;
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

        private static readonly byte[] s_label = System.Text.Encoding.UTF8.GetBytes("opcua-secret");

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
            int encryptingKeySize;
            int blockSize;
            HashAlgorithmName algorithmName;

            switch (securityPolicyUri)
            {
                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_brainpoolP256r1:
                    blockSize = 16;
                    encryptingKeySize = 16;
                    algorithmName = HashAlgorithmName.SHA256;
                    break;
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP384r1:
                    encryptingKeySize = 32;
                    blockSize = 16;
                    algorithmName = HashAlgorithmName.SHA384;
                    break;
                case SecurityPolicies.ECC_curve25519:
                case SecurityPolicies.ECC_curve448:
                    encryptingKeySize = 32;
                    blockSize = 12;
                    algorithmName = HashAlgorithmName.SHA256;
                    break;
                default:
                    encryptingKeySize = 32;
                    blockSize = 16;
                    algorithmName = HashAlgorithmName.SHA256;
                    break;
            }

            encryptingKey = new byte[encryptingKeySize];
            iv = new byte[blockSize];

            byte[] keyLength = BitConverter.GetBytes((ushort)(encryptingKeySize + blockSize));
            byte[] salt = Utils.Append(keyLength, s_label, senderNonce.Data, receiverNonce.Data);

            byte[] keyData;
            if (forDecryption)
            {
                keyData = receiverNonce.DeriveKey(
                    senderNonce,
                    salt,
                    algorithmName,
                    encryptingKeySize + blockSize);
            }
            else
            {
                keyData = senderNonce.DeriveKey(
                    receiverNonce,
                    salt,
                    algorithmName,
                    encryptingKeySize + blockSize);
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

            int signatureLength = EccUtils.GetSignatureLength(SenderCertificate);

            using (var encoder = new BinaryEncoder(Context))
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

                        foreach (X509Certificate2 issuer in SenderIssuerCertificates)
                        {
                            blobSize += issuer.RawData.Length;
                        }

                        byte[] blob = new byte[blobSize];
                        Buffer.BlockCopy(senderCertificate, 0, blob, 0, senderCertificate.Length);

                        int pos = senderCertificate.Length;

                        foreach (X509Certificate2 issuer in SenderIssuerCertificates)
                        {
                            byte[] data = issuer.RawData;
                            Buffer.BlockCopy(data, 0, blob, pos, data.Length);
                            pos += data.Length;
                        }

                        senderCertificate = blob;
                    }
                }

                encoder.WriteByteString(null, senderCertificate);
                encoder.WriteDateTime(null, DateTime.UtcNow);

                byte[] senderNonce = SenderNonce.Data;
                byte[] receiverNonce = ReceiverNonce.Data;

                encoder.WriteUInt16(null, (ushort)(senderNonce.Length + receiverNonce.Length + 8));
                encoder.WriteByteString(null, senderNonce);
                encoder.WriteByteString(null, receiverNonce);

                // create keys.
                if (EccUtils.IsEccPolicy(SecurityPolicyUri))
                {
                    CreateKeysForEcc(
                        SecurityPolicyUri,
                        SenderNonce,
                        ReceiverNonce,
                        false,
                        out encryptingKey,
                        out iv);
                }

                // encrypt  secret,
                byte[] encryptedData = EncryptSecret(secret, nonce, encryptingKey, iv);

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

            int length = message.Length - lengthPosition - 4;

            message[lengthPosition++] = (byte)(length & 0xFF);
            message[lengthPosition++] = (byte)((length & 0xFF00) >> 8);
            message[lengthPosition++] = (byte)((length & 0xFF0000) >> 16);
            message[lengthPosition++] = (byte)((length & 0xFF000000) >> 24);

            // get the algorithm used for the signature.
            HashAlgorithmName signatureAlgorithm;
            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP384r1:
                    signatureAlgorithm = HashAlgorithmName.SHA384;
                    break;
                default:
                    signatureAlgorithm = HashAlgorithmName.SHA256;
                    break;
            }

            var dataToSign = new ArraySegment<byte>(message, 0, message.Length - signatureLength);
            byte[] signature = EccUtils.Sign(dataToSign, SenderCertificate, signatureAlgorithm);
            Buffer.BlockCopy(
                signature,
                0,
                message,
                message.Length - signatureLength,
                signatureLength);
            return message;
        }

        /// <summary>
        /// Verifies the header for an ECC encrypted message and returns the encrypted data.
        /// </summary>
        /// <param name="dataToDecrypt">The data to decrypt.</param>
        /// <param name="earliestTime">The earliest time allowed for the message signing time.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <returns>The encrypted data.</returns>
        /// <exception cref="ServiceResultException"></exception>
        private ArraySegment<byte> VerifyHeaderForEcc(
            ArraySegment<byte> dataToDecrypt,
            DateTime earliestTime,
            ITelemetryContext telemetry)
        {
            using var decoder = new BinaryDecoder(
                dataToDecrypt.Array,
                dataToDecrypt.Offset,
                dataToDecrypt.Count,
                Context);
            NodeId typeId = decoder.ReadNodeId(null);

            if (typeId != DataTypeIds.EccEncryptedSecret)
            {
                throw new ServiceResultException(StatusCodes.BadDataTypeIdUnknown);
            }

            var encoding = (ExtensionObjectEncoding)decoder.ReadByte(null);

            if (encoding != ExtensionObjectEncoding.Binary)
            {
                throw new ServiceResultException(StatusCodes.BadDataEncodingUnsupported);
            }

            uint length = decoder.ReadUInt32(null);

            // get the start of data.
            int startOfData = decoder.Position + dataToDecrypt.Offset;

            SecurityPolicyUri = decoder.ReadString(null);

            if (!EccUtils.IsEccPolicy(SecurityPolicyUri))
            {
                throw new ServiceResultException(StatusCodes.BadSecurityPolicyRejected);
            }

            // get the algorithm used for the signature.
            HashAlgorithmName signatureAlgorithm;

            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP384r1:
                    signatureAlgorithm = HashAlgorithmName.SHA384;
                    break;
                default:
                    signatureAlgorithm = HashAlgorithmName.SHA256;
                    break;
            }

            // extract the send certificate and any chain.
            byte[] senderCertificate = decoder.ReadByteString(null);

            if (senderCertificate == null || senderCertificate.Length == 0)
            {
                if (SenderCertificate == null)
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
                }
            }
            else
            {
                X509Certificate2Collection senderCertificateChain = Utils.ParseCertificateChainBlob(
                    senderCertificate,
                    telemetry);

                SenderCertificate = senderCertificateChain[0];
                SenderIssuerCertificates = [];

                for (int ii = 1; ii < senderCertificateChain.Count; ii++)
                {
                    SenderIssuerCertificates.Add(senderCertificateChain[ii]);
                }

                // validate the sender.
                Validator?.ValidateAsync(senderCertificateChain, default).GetAwaiter().GetResult();
            }

            // extract the send certificate and any chain.
            DateTime signingTime = decoder.ReadDateTime(null);

            if (signingTime < earliestTime)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidTimestamp);
            }

            // extract the policy header.
            ushort headerLength = decoder.ReadUInt16(null);

            if (headerLength == 0 || headerLength > length)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }

            // read the policy header.
            byte[] senderPublicKey = decoder.ReadByteString(null);
            byte[] receiverPublicKey = decoder.ReadByteString(null);

            if (headerLength != senderPublicKey.Length + receiverPublicKey.Length + 8)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "Unexpected policy header length");
            }

            int startOfEncryption = decoder.Position;

            SenderNonce = Nonce.CreateNonce(SecurityPolicyUri, senderPublicKey);

            if (!Utils.IsEqual(receiverPublicKey, ReceiverNonce.Data))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "Unexpected receiver nonce.");
            }

            // check the signature.
            int signatureLength = EccUtils.GetSignatureLength(SenderCertificate);

            if (signatureLength >= length)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }

            byte[] signature = new byte[signatureLength];
            Buffer.BlockCopy(
                dataToDecrypt.Array,
                startOfData + (int)length - signatureLength,
                signature,
                0,
                signatureLength);

            var dataToSign = new ArraySegment<byte>(
                dataToDecrypt.Array,
                0,
                startOfData + (int)length - signatureLength);

            if (!EccUtils.Verify(dataToSign, signature, SenderCertificate, signatureAlgorithm))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "Could not verify signature.");
            }

            // extract the encrypted data.
            return new ArraySegment<byte>(
                dataToDecrypt.Array,
                startOfEncryption,
                (int)length - (startOfEncryption - startOfData + signatureLength));
        }

        /// <summary>
        /// Decrypts the specified data using the ECC algorithm.
        /// </summary>
        /// <param name="earliestTime">The earliest time allowed for the message.</param>
        /// <param name="expectedNonce">The expected nonce value.</param>
        /// <param name="data">The data to decrypt.</param>
        /// <param name="offset">The offset of the data to decrypt.</param>
        /// <param name="count">The number of bytes to decrypt.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <returns>The decrypted data.</returns>
        /// <exception cref="ServiceResultException"></exception>
        public byte[] Decrypt(
            DateTime earliestTime,
            byte[] expectedNonce,
            byte[] data,
            int offset,
            int count,
            ITelemetryContext telemetry)
        {
            ArraySegment<byte> dataToDecrypt = VerifyHeaderForEcc(
                new ArraySegment<byte>(data, offset, count),
                earliestTime,
                telemetry);

            CreateKeysForEcc(
                SecurityPolicyUri,
                SenderNonce,
                ReceiverNonce,
                true,
                out byte[] encryptingKey,
                out byte[] iv);

            ArraySegment<byte> plainText = DecryptSecret(
                dataToDecrypt.Array,
                dataToDecrypt.Offset,
                dataToDecrypt.Count,
                encryptingKey,
                iv);

            using var decoder = new BinaryDecoder(
                plainText.Array,
                plainText.Offset,
                plainText.Count,
                Context);
            byte[] actualNonce = decoder.ReadByteString(null);

            if (expectedNonce != null && expectedNonce.Length > 0)
            {
                int notvalid = expectedNonce.Length == actualNonce.Length ? 0 : 1;

                for (int ii = 0; ii < expectedNonce.Length && ii < actualNonce.Length; ii++)
                {
                    notvalid |= expectedNonce[ii] ^ actualNonce[ii];
                }

                if (notvalid != 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNonceInvalid);
                }
            }

            return decoder.ReadByteString(null);
        }
    }
}