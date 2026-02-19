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
            CryptoTrace.Start(ConsoleColor.Blue, $"EncryptedSecret {((forDecryption) ? "DECRYPT" : "ENCRYPT")}");
            CryptoTrace.WriteLine($"SecurityPolicy={securityPolicy.Name}");
            CryptoTrace.WriteLine($"LocalNonce={CryptoTrace.KeyToString(localNonce?.Data)}");
            CryptoTrace.WriteLine($"RemoteNonce={CryptoTrace.KeyToString(remoteNonce?.Data)}");

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

            Console.ForegroundColor = ConsoleColor.Blue;
            CryptoTrace.WriteLine($"EncryptingKey={CryptoTrace.KeyToString(encryptingKey)}");
            CryptoTrace.WriteLine($"IV={CryptoTrace.KeyToString(iv)}");
            CryptoTrace.Finish("EncryptedSecret");
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
                out encryptingKey,
                out iv);

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
                endOfSecret + tagLength,
                signatureLength);

            return message;
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

            // extract the key data length.
            ushort headerLength = decoder.ReadUInt16(null);

            if (headerLength == 0 || headerLength > length)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }

            // read the key data.
            int senderNonceStart = decoder.Position;
            byte[] senderPublicKey = decoder.ReadByteString(null);
            int senderNonceEnd = decoder.Position;
            byte[] receiverPublicKey = decoder.ReadByteString(null);
            int receiverNonceEnd = decoder.Position;

            if (headerLength != senderPublicKey.Length + receiverPublicKey.Length + 8)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "Unexpected key data length");
            }

            int startOfEncryption = decoder.Position;

            SenderNonce = Nonce.CreateNonce(SecurityPolicy, senderPublicKey);

            if (!Utils.IsEqual(receiverPublicKey, ReceiverNonce.Data))
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

            var key = decoder.ReadByteString(null);
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

            return key;
        }
    }
}
