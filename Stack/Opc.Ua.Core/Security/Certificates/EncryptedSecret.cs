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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;
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
        private static readonly TimeSpan s_rsaEncryptedSecretMaxClockSkew = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan s_rsaEncryptedSecretMaxTokenAge = TimeSpan.FromHours(1);

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
            SecurityPolicy = SecurityPolicies.GetInfo(securityPolicyUri);
            Context = context;

            if (SecurityPolicy == null)
            {
                throw new ArgumentException($"Cannot resolve SecurityPolicy '{securityPolicyUri}'.", nameof(securityPolicyUri));
            }
        }

        /// <summary>
        /// Creates an <see cref="EncryptedSecret"/> instance for RSAEncryptedSecret encryption/decryption.
        /// </summary>
        public static EncryptedSecret CreateForRsa(
            IServiceMessageContext context,
            string securityPolicyUri,
            X509Certificate2 receiverCertificate,
            Nonce receiverNonce = null)
        {
            return new EncryptedSecret(
                context: context,
                securityPolicyUri: securityPolicyUri,
                senderIssuerCertificates: null,
                receiverCertificate: receiverCertificate,
                receiverNonce: receiverNonce,
                senderCertificate: null,
                senderNonce: null);
        }

        /// <summary>
        /// Creates an <see cref="EncryptedSecret"/> instance for ECC encrypted secret encryption/decryption.
        /// </summary>
        public static EncryptedSecret CreateForEcc(
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
            return new EncryptedSecret(
                context: context,
                securityPolicyUri: securityPolicyUri,
                senderIssuerCertificates: senderIssuerCertificates,
                receiverCertificate: receiverCertificate,
                receiverNonce: receiverNonce,
                senderCertificate: senderCertificate,
                senderNonce: senderNonce,
                validator: validator,
                doNotEncodeSenderCertificate: doNotEncodeSenderCertificate);
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
        /// Gets or sets the security policy.
        /// </summary>
        public SecurityPolicyInfo SecurityPolicy { get; private set; }

        /// <summary>
        /// Service message context to use
        /// </summary>
        public IServiceMessageContext Context { get; }

        private static readonly byte[] s_secretLabel = System.Text.Encoding.UTF8.GetBytes("opcua-secret");

        /// <summary>
        /// Creates the encrypting key and initialization vector (IV) for Elliptic Curve Cryptography (ECC) encryption or decryption.
        /// </summary>
        private static void CreateKeysForEcc(
            SecurityPolicyInfo securityPolicy,
            Nonce localNonce,
            Nonce remoteNonce,
            bool forDecryption,
            out byte[] encryptingKey,
            out byte[] iv)
        {
            int encryptingKeySize = securityPolicy.SymmetricEncryptionKeyLength;
            int blockSize = securityPolicy.InitializationVectorLength;

            encryptingKey = new byte[encryptingKeySize];
            iv = new byte[blockSize];

            byte[] secret = localNonce.GenerateSecret(remoteNonce, null);
            byte[] keyLength = BitConverter.GetBytes((ushort)(encryptingKeySize + blockSize));

            byte[] salt = Utils.Append(
                keyLength,
                s_secretLabel,
                forDecryption ? remoteNonce.Data : localNonce.Data,
                forDecryption ? localNonce.Data : remoteNonce.Data);

            byte[] keyData = localNonce.DeriveKeyData(
                secret,
                salt,
                securityPolicy.KeyDerivationAlgorithm,
                encryptingKeySize + blockSize);

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
            if (SecurityPolicy.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.None)
            {
                return EncryptRsa(secret, nonce);
            }

            byte[] message = null;
            int lengthPosition = 0;

            int signatureLength = CryptoUtils.GetSignatureLength(SenderCertificate);

            using var encoder = new BinaryEncoder(Context);

            // write header.
            encoder.WriteNodeId(null, DataTypeIds.EccEncryptedSecret);
            encoder.WriteByte(null, (byte)ExtensionObjectEncoding.Binary);

            lengthPosition = encoder.Position;
            encoder.WriteUInt32(null, 0);

            encoder.WriteString(null, SecurityPolicy.Uri);

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

            if (ReceiverNonce?.Data == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadArgumentsMissing,
                    $"The receiver did not provide an ephemeral key.");
            }

            byte[] senderNonce = SenderNonce.Data;
            byte[] receiverNonce = ReceiverNonce.Data;

            encoder.WriteUInt16(null, (ushort)(senderNonce.Length + receiverNonce.Length + 8));
            int senderNonceStart = encoder.Position;
            encoder.WriteByteString(null, senderNonce);
            int senderNonceEnd = encoder.Position;
            encoder.WriteByteString(null, receiverNonce);
            int receiverNonceEnd = encoder.Position;

            // create keys.
            CreateKeysForEcc(
                SecurityPolicy,
                SenderNonce,
                ReceiverNonce,
                false,
                out byte[] encryptingKey,
                out byte[] iv);

            // reserves space for padding and tag that is added by SymmetricEncryptAndSign.
            int startOfSecret = encoder.Position;
            encoder.WriteByteString(null, nonce);
            encoder.WriteByteString(null, secret);

            int paddingCount = 0;
            int tagLength = 0;

            switch (SecurityPolicy.SymmetricEncryptionAlgorithm)
            {
                case SymmetricEncryptionAlgorithm.Aes128Cbc:
                case SymmetricEncryptionAlgorithm.Aes256Cbc:
                    paddingCount = GetPaddingCount(SecurityPolicy.InitializationVectorLength, secret.Length, encoder.Position - startOfSecret);
                    tagLength = 0;
                    break;
                case SymmetricEncryptionAlgorithm.Aes128Gcm:
                case SymmetricEncryptionAlgorithm.Aes256Gcm:
                case SymmetricEncryptionAlgorithm.ChaCha20Poly1305:
                    paddingCount = GetPaddingCount(16, secret.Length, encoder.Position - startOfSecret);
                    tagLength = SecurityPolicy.SymmetricSignatureLength;
                    break;
            }

            for (int ii = 0; ii < paddingCount; ii++)
            {
                encoder.WriteByte(null, (byte)paddingCount);
            }

            encoder.WriteByte(null, (byte)paddingCount);
            encoder.WriteByte(null, 0);

            int endOfSecret = encoder.Position;

            // reserve space for the outer padding that SymmetricEncryptAndSign will add (CBC only).
            int outerPaddingSize = 0;
            if (SecurityPolicy.SymmetricEncryptionAlgorithm is SymmetricEncryptionAlgorithm.Aes128Cbc or SymmetricEncryptionAlgorithm.Aes256Cbc)
            {
                int blockSize = SecurityPolicy.InitializationVectorLength;
                int paddingByteSize = blockSize > byte.MaxValue ? 2 : 1;
                int paddingSize = blockSize - ((endOfSecret - startOfSecret + paddingByteSize) % blockSize);
                paddingSize %= blockSize;
                outerPaddingSize = paddingSize + paddingByteSize;

                for (int ii = 0; ii < outerPaddingSize; ii++)
                {
                    encoder.WriteByte(null, 0xCD);
                }
            }

            // save space for tag.
            for (int ii = 0; ii < tagLength; ii++)
            {
                encoder.WriteByte(null, 0xAB);
            }

            // save space for signature.
            for (int ii = 0; ii < signatureLength; ii++)
            {
                encoder.WriteByte(null, 0xDE);
            }

            message = encoder.CloseAndReturnBuffer();

            int length = message.Length - lengthPosition - 4;

            message[lengthPosition++] = (byte)(length & 0xFF);
            message[lengthPosition++] = (byte)((length & 0xFF00) >> 8);
            message[lengthPosition++] = (byte)((length & 0xFF0000) >> 16);
            message[lengthPosition++] = (byte)((length & 0xFF000000) >> 24);

            _ = CryptoUtils.SymmetricEncryptAndSign(
                new ArraySegment<byte>(message, startOfSecret, endOfSecret - startOfSecret),
                SecurityPolicy,
                encryptingKey,
                iv);

            var dataToSign = new ArraySegment<byte>(message, 0, message.Length - signatureLength);

            byte[] signature = CryptoUtils.Sign(
                dataToSign,
                SenderCertificate,
                SecurityPolicy.AsymmetricSignatureAlgorithm);

            Buffer.BlockCopy(
                signature,
                0,
                message,
                endOfSecret + outerPaddingSize + tagLength,
                signatureLength);

            return message;
        }

        /// <summary>
        /// Encrypts a secret using RSAEncryptedSecret format.
        /// </summary>
        public byte[] EncryptRsa(byte[] secret, byte[] nonce)
        {
            if (SecurityPolicy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityPolicyRejected);
            }

            if (ReceiverCertificate == null)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }

            byte[] signingKey = null;
            byte[] encryptingKey = null;
            byte[] iv = null;
            byte[] keyData = null;
            byte[] encryptedPayload = null;

            try
            {
                signingKey = Nonce.CreateRandomNonceData(SecurityPolicy.DerivedSignatureKeyLength, false);
                encryptingKey = Nonce.CreateRandomNonceData(SecurityPolicy.SymmetricEncryptionKeyLength, false);
                iv = Nonce.CreateRandomNonceData(SecurityPolicy.InitializationVectorLength, false);
                keyData = Utils.Append(signingKey, encryptingKey, iv);

                ILogger logger = Context.Telemetry.CreateLogger<EncryptedSecret>();
                byte[] encryptedKeyData = SecurityPolicies.Encrypt(
                    ReceiverCertificate,
                    SecurityPolicy.Uri,
                    keyData,
                    logger).Data;

                using var payloadEncoder = new BinaryEncoder(Context);
                payloadEncoder.WriteByteString(null, nonce ?? []);
                payloadEncoder.WriteByteString(null, secret);
                byte[] payload = payloadEncoder.CloseAndReturnBuffer();

                int blockSize = SecurityPolicy.InitializationVectorLength;
                int paddingByteSize = blockSize > byte.MaxValue ? 2 : 1;
                int paddingSize = blockSize - ((payload.Length + paddingByteSize) % blockSize);
                paddingSize %= blockSize;

                encryptedPayload = new byte[payload.Length + paddingSize + paddingByteSize];
                Buffer.BlockCopy(payload, 0, encryptedPayload, 0, payload.Length);

                for (int ii = payload.Length; ii < payload.Length + paddingSize; ii++)
                {
                    encryptedPayload[ii] = (byte)(paddingSize & 0xFF);
                }

                encryptedPayload[payload.Length + paddingSize] = (byte)(paddingSize & 0xFF);
                if (paddingByteSize > 1)
                {
                    encryptedPayload[payload.Length + paddingSize + 1] = (byte)((paddingSize >> 8) & 0xFF);
                }

#pragma warning disable CA5401 // Symmetric encryption uses non-default initialization vector
                using Aes aes = Aes.Create();
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = encryptingKey;
                aes.IV = iv;
#pragma warning restore CA5401
                using ICryptoTransform encryptor = aes.CreateEncryptor();
                int bytesEncrypted = encryptor.TransformBlock(
                    encryptedPayload,
                    0,
                    encryptedPayload.Length,
                    encryptedPayload,
                    0);
                if (bytesEncrypted != encryptedPayload.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingError);
                }

                ZeroMemory(payload);

                using var encoder = new BinaryEncoder(Context);
                encoder.WriteNodeId(null, DataTypeIds.RsaEncryptedSecret);
                encoder.WriteByte(null, (byte)ExtensionObjectEncoding.Binary);
                int lengthPosition = encoder.Position;
                encoder.WriteUInt32(null, 0);
                encoder.WriteString(null, SecurityPolicy.Uri);
#pragma warning disable CA5350 // SHA1 is required by OPC UA RsaEncryptedSecret certificate hash field.
                encoder.WriteByteString(null, ComputeSha1Hash(ReceiverCertificate.RawData));
#pragma warning restore CA5350
                encoder.WriteDateTime(null, DateTime.UtcNow);
                encoder.WriteUInt16(null, (ushort)encryptedKeyData.Length);

                for (int ii = 0; ii < encryptedKeyData.Length; ii++)
                {
                    encoder.WriteByte(null, encryptedKeyData[ii]);
                }

                for (int ii = 0; ii < encryptedPayload.Length; ii++)
                {
                    encoder.WriteByte(null, encryptedPayload[ii]);
                }

                for (int ii = 0; ii < SecurityPolicy.SymmetricSignatureLength; ii++)
                {
                    encoder.WriteByte(null, 0);
                }

                byte[] encodedSecret = encoder.CloseAndReturnBuffer();
                int extensionObjectLength = encodedSecret.Length - lengthPosition - 4;
                encodedSecret[lengthPosition++] = (byte)(extensionObjectLength & 0xFF);
                encodedSecret[lengthPosition++] = (byte)((extensionObjectLength >> 8) & 0xFF);
                encodedSecret[lengthPosition++] = (byte)((extensionObjectLength >> 16) & 0xFF);
                encodedSecret[lengthPosition] = (byte)((extensionObjectLength >> 24) & 0xFF);

                int signatureStart = encodedSecret.Length - SecurityPolicy.SymmetricSignatureLength;
                if (SecurityPolicy.SymmetricSignatureLength > 0)
                {
                    using HMAC hmac = SecurityPolicy.CreateSignatureHmac(signingKey) ??
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "The security policy does not support symmetric signatures required for RSAEncryptedSecret creation.");
                    byte[] signature = hmac.ComputeHash(encodedSecret, 0, signatureStart);
                    Buffer.BlockCopy(
                        signature,
                        0,
                        encodedSecret,
                        signatureStart,
                        Math.Min(signature.Length, SecurityPolicy.SymmetricSignatureLength));
                }

                return encodedSecret;
            }
            finally
            {
                if (signingKey != null)
                {
                    ZeroMemory(signingKey);
                }

                if (encryptingKey != null)
                {
                    ZeroMemory(encryptingKey);
                }

                if (iv != null)
                {
                    ZeroMemory(iv);
                }

                if (keyData != null)
                {
                    ZeroMemory(keyData);
                }

                if (encryptedPayload != null)
                {
                    ZeroMemory(encryptedPayload);
                }
            }
        }

        /// <summary>
        /// Tries to decrypt an RSAEncryptedSecret payload.
        /// </summary>
        public bool TryDecryptRsa(byte[] encodedSecret, byte[] expectedNonce, out byte[] secret)
        {
            secret = null;

            if (SecurityPolicy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None)
            {
                return false;
            }

            if (ReceiverCertificate == null || encodedSecret == null || encodedSecret.Length < 8)
            {
                return false;
            }

            using var decoder = new BinaryDecoder(encodedSecret, Context);
            NodeId typeId = decoder.ReadNodeId(null);

            if (typeId != DataTypeIds.RsaEncryptedSecret)
            {
                return false;
            }

            var encoding = (ExtensionObjectEncoding)decoder.ReadByte(null);
            if (encoding != ExtensionObjectEncoding.Binary)
            {
                throw new ServiceResultException(StatusCodes.BadDataEncodingUnsupported);
            }

            int endOfSecret = checked((int)decoder.ReadUInt32(null) + decoder.Position);
            if (endOfSecret > encodedSecret.Length || endOfSecret <= decoder.Position)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }

            string encryptedSecretPolicyUri = decoder.ReadString(null);
            if (!string.Equals(encryptedSecretPolicyUri, SecurityPolicy.Uri, StringComparison.Ordinal))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unexpected encrypted secret security policy: {0}",
                    encryptedSecretPolicyUri);
            }

            ByteString certificateHash = decoder.ReadByteString(null);
            if (certificateHash.Length > 0)
            {
#pragma warning disable CA5350 // SHA1 is required by OPC UA RsaEncryptedSecret certificate hash field.
                byte[] actualCertificateHash = ComputeSha1Hash(ReceiverCertificate.RawData);
#pragma warning restore CA5350
                if (!Utils.IsEqual(certificateHash.ToArray(), actualCertificateHash))
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
                }
            }

            DateTime signingTime = (DateTime)decoder.ReadDateTime(null);
            DateTime now = DateTime.UtcNow;
            // Accept tokens from the recent past to account for transit/processing delays while
            // only allowing a small future clock skew to prevent replay with future-dated tokens.
            if (signingTime < now - s_rsaEncryptedSecretMaxTokenAge || signingTime > now + s_rsaEncryptedSecretMaxClockSkew)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidTimestamp);
            }

            ushort keyDataLength = decoder.ReadUInt16(null);
            if (keyDataLength == 0 || decoder.Position + keyDataLength > endOfSecret)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }

            int keyDataStart = decoder.Position;
            _ = decoder.BaseStream.Seek(keyDataLength, SeekOrigin.Current);

            int signatureLength = SecurityPolicy.SymmetricSignatureLength;
            int signatureStart = endOfSecret - signatureLength;
            if (signatureStart <= decoder.Position)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }

            byte[] keyData = null;
            byte[] signingKey = null;
            byte[] encryptingKey = null;
            byte[] iv = null;
            byte[] encryptedPayload = null;
            byte[] payload = null;

            try
            {
                ILogger logger = Context.Telemetry.CreateLogger<EncryptedSecret>();
                keyData = RsaUtils.Decrypt(
                    new ArraySegment<byte>(encodedSecret, keyDataStart, keyDataLength),
                    ReceiverCertificate,
                    SecurityPolicy.AsymmetricEncryptionAlgorithm switch
                    {
                        AsymmetricEncryptionAlgorithm.RsaOaepSha1 => RsaUtils.Padding.OaepSHA1,
                        AsymmetricEncryptionAlgorithm.RsaPkcs15Sha1 => RsaUtils.Padding.Pkcs1,
                        _ => RsaUtils.Padding.OaepSHA256
                    },
                    logger);

                int expectedKeyDataLength =
                    SecurityPolicy.DerivedSignatureKeyLength +
                    SecurityPolicy.SymmetricEncryptionKeyLength +
                    SecurityPolicy.InitializationVectorLength;

                if (keyData.Length < expectedKeyDataLength)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError);
                }

                signingKey = new byte[SecurityPolicy.DerivedSignatureKeyLength];
                encryptingKey = new byte[SecurityPolicy.SymmetricEncryptionKeyLength];
                iv = new byte[SecurityPolicy.InitializationVectorLength];
                int keyMaterialOffset = signingKey.Length + encryptingKey.Length;
                if (keyMaterialOffset + iv.Length > keyData.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError);
                }

                Buffer.BlockCopy(keyData, 0, signingKey, 0, signingKey.Length);
                Buffer.BlockCopy(keyData, signingKey.Length, encryptingKey, 0, encryptingKey.Length);
                Buffer.BlockCopy(keyData, keyMaterialOffset, iv, 0, iv.Length);

                if (signatureLength > 0)
                {
                    using HMAC hmac = SecurityPolicy.CreateSignatureHmac(signingKey) ??
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "The security policy does not support symmetric signatures required for RSAEncryptedSecret validation.");

                    byte[] expectedSignature = hmac.ComputeHash(encodedSecret, 0, signatureStart);
                    int notValid = expectedSignature.Length == signatureLength ? 0 : 1;

                    for (int ii = 0; ii < signatureLength; ii++)
                    {
                        byte expectedByte = ii < expectedSignature.Length ? expectedSignature[ii] : (byte)0;
                        notValid |= expectedByte ^ encodedSecret[signatureStart + ii];
                    }

                    if (notValid != 0)
                    {
                        throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                    }
                }

                int encryptedPayloadStart = decoder.Position;
                int encryptedPayloadLength = signatureStart - encryptedPayloadStart;
                encryptedPayload = new byte[encryptedPayloadLength];
                Buffer.BlockCopy(encodedSecret, encryptedPayloadStart, encryptedPayload, 0, encryptedPayloadLength);
                ArraySegment<byte> plainText = CryptoUtils.SymmetricDecryptAndVerify(
                    new ArraySegment<byte>(encryptedPayload),
                    SecurityPolicy,
                    encryptingKey,
                    iv);
                payload = new byte[plainText.Count];
                Buffer.BlockCopy(plainText.Array, plainText.Offset, payload, 0, payload.Length);

                using var payloadDecoder = new BinaryDecoder(payload, Context);

                ByteString actualNonce = payloadDecoder.ReadByteString(null);
                if (expectedNonce != null && !Utils.IsEqual(actualNonce.ToArray(), expectedNonce))
                {
                    throw new ServiceResultException(StatusCodes.BadNonceInvalid);
                }

                secret = payloadDecoder.ReadByteString(null).ToArray();
                return true;
            }
            finally
            {
                if (keyData != null)
                {
                    ZeroMemory(keyData);
                }

                if (signingKey != null)
                {
                    ZeroMemory(signingKey);
                }

                if (encryptingKey != null)
                {
                    ZeroMemory(encryptingKey);
                }

                if (iv != null)
                {
                    ZeroMemory(iv);
                }

                if (encryptedPayload != null)
                {
                    ZeroMemory(encryptedPayload);
                }

                if (payload != null)
                {
                    ZeroMemory(payload);
                }
            }
        }

        /// <summary>
        /// Tries to decrypt the encrypted secret and returns the plain secret.
        /// </summary>
        public bool TryDecrypt(byte[] encryptedSecret, byte[] expectedNonce, out byte[] secret)
        {
            secret = null;

            if (encryptedSecret == null)
            {
                return false;
            }

            if (SecurityPolicy.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.None)
            {
                return TryDecryptRsa(encryptedSecret, expectedNonce, out secret);
            }

            try
            {
                secret = Decrypt(
                    DateTime.UtcNow.AddHours(-1),
                    expectedNonce,
                    encryptedSecret,
                    0,
                    encryptedSecret.Length,
                    Context.Telemetry);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private int GetPaddingCount(int blockSize, int secretLength, int dataLength)
        {
            dataLength += 2; // add padding size

            int paddingCount =
                dataLength % blockSize == 0
                ? 0
                : blockSize - dataLength % blockSize;

            if (paddingCount + secretLength < blockSize)
            {
                paddingCount += blockSize;
            }

            return paddingCount;
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

            int length = (int)decoder.ReadUInt32(null) + decoder.Position;

            SecurityPolicy = SecurityPolicies.GetInfo(decoder.ReadString(null));

            if (SecurityPolicy.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.None)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityPolicyRejected);
            }

            // extract the send certificate and any chain.
            ByteString senderCertificate = decoder.ReadByteString(null);

            if (senderCertificate.Length == 0)
            {
                if (SenderCertificate == null)
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
                }
            }
            else
            {
                X509Certificate2Collection senderCertificateChain = Utils.ParseCertificateChainBlob(
                    senderCertificate.ToArray(),
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
            DateTime signingTime = (DateTime)decoder.ReadDateTime(null);

            if (signingTime < earliestTime)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidTimestamp);
            }

            // extract the key data length.
            ushort headerLength = decoder.ReadUInt16(null);

            if (headerLength == 0 || headerLength > length)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }

            // read the key data.
            int senderNonceStart = decoder.Position;
            ByteString senderPublicKey = decoder.ReadByteString(null);
            int senderNonceEnd = decoder.Position;
            ByteString receiverPublicKey = decoder.ReadByteString(null);
            int receiverNonceEnd = decoder.Position;

            if (headerLength != senderPublicKey.Length + receiverPublicKey.Length + 8)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "Unexpected key data length");
            }

            int startOfEncryption = decoder.Position;

            SenderNonce = Nonce.CreateNonce(SecurityPolicy, senderPublicKey.ToArray());

            if (!Utils.IsEqual(receiverPublicKey.ToArray(), ReceiverNonce.Data))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "Unexpected receiver nonce.");
            }

            // check the signature.
            int signatureLength = CryptoUtils.GetSignatureLength(SenderCertificate);

            if (signatureLength >= length)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }

            byte[] signature = new byte[signatureLength];

            Buffer.BlockCopy(
                dataToDecrypt.Array,
                dataToDecrypt.Offset + dataToDecrypt.Count - signatureLength,
                signature,
                0,
                signatureLength);

            var dataToSign = new ArraySegment<byte>(
                dataToDecrypt.Array,
                dataToDecrypt.Offset,
                dataToDecrypt.Count - signatureLength);

            if (!CryptoUtils.Verify(dataToSign, signature, SenderCertificate, SecurityPolicy.AsymmetricSignatureAlgorithm))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "Could not verify signature.");
            }

            // extract the encrypted data.
            return new ArraySegment<byte>(
                dataToDecrypt.Array,
                dataToDecrypt.Offset + startOfEncryption,
                dataToDecrypt.Count - startOfEncryption - signatureLength);
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
                SecurityPolicy,
                ReceiverNonce,
                SenderNonce,
                true,
                out byte[] encryptingKey,
                out byte[] iv);

            ArraySegment<byte> plainText = CryptoUtils.SymmetricDecryptAndVerify(
                dataToDecrypt,
                SecurityPolicy,
                encryptingKey,
                iv);

            using var decoder = new BinaryDecoder(
                plainText.Array,
                plainText.Offset + dataToDecrypt.Offset,
                plainText.Count - dataToDecrypt.Offset,
                Context);

            ByteString actualNonce = decoder.ReadByteString(null);

            if (expectedNonce != null && expectedNonce.Length > 0)
            {
                int notvalid = expectedNonce.Length == actualNonce.Length ? 0 : 1;

                for (int ii = 0; ii < expectedNonce.Length && ii < actualNonce.Length; ii++)
                {
                    notvalid |= expectedNonce[ii] ^ actualNonce.Span[ii];
                }

                if (notvalid != 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNonceInvalid);
                }
            }

            ByteString key = decoder.ReadByteString(null);
            var paddingCount = decoder.ReadByte(null);

            int error = 0;

            for (int ii = 0; ii < paddingCount; ii++)
            {
                var padding = decoder.ReadByte(null);
                error |= (padding & ~paddingCount);
            }

            var highByte = decoder.ReadByte(null);

            if (error != 0 || highByte != 0)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }

            return key.ToArray();
        }

        /// <summary>
        /// Computes the SHA-1 hash required by the OPC UA RSAEncryptedSecret certificate hash field.
        /// </summary>
        private static byte[] ComputeSha1Hash(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            using SHA1 sha1 = SHA1.Create();
            return sha1.ComputeHash(data);
        }

        private static void ZeroMemory(byte[] buffer)
        {
            if (buffer == null)
            {
                return;
            }
#if NET8_0_OR_GREATER
            CryptographicOperations.ZeroMemory(buffer);
#else
            Array.Clear(buffer, 0, buffer.Length);
#endif
        }
    }
}
