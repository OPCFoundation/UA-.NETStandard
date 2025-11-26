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
            int encryptingKeySize = securityPolicy.SymmetricEncryptionKeyLength;
            int blockSize = securityPolicy.InitializationVectorLength;

            encryptingKey = new byte[encryptingKeySize];
            iv = new byte[blockSize];

            byte[] secret = localNonce.GenerateSecret(remoteNonce, null);
            byte[] keyLength = BitConverter.GetBytes((ushort)(encryptingKeySize + blockSize));

            byte[] salt = (forDecryption) ?
                Utils.Append(keyLength, s_secretLabel, remoteNonce.Data, localNonce.Data) :
                Utils.Append(keyLength, s_secretLabel, localNonce.Data, remoteNonce.Data);

            System.Console.WriteLine(
                $"LOCAL={Utils.ToHexString(localNonce.Data).Substring(0, 8)} " +
                $"REMOTE={Utils.ToHexString(remoteNonce.Data).Substring(0, 8)} " +
                $"SALT={Utils.ToHexString(salt).Substring(0, 8)} ");

            byte[] keyData;

            if (forDecryption)
            {
                keyData = remoteNonce.DeriveKey(
                    secret,
                    salt,
                    securityPolicy.KeyDerivationAlgorithm,
                    encryptingKeySize + blockSize);
            }
            else
            {
                keyData = localNonce.DeriveKey(
                    secret,
                    salt,
                    securityPolicy.KeyDerivationAlgorithm,
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

            int signatureLength = CryptoUtils.GetSignatureLength(SenderCertificate);

            using (var encoder = new BinaryEncoder(Context))
            {
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

                byte[] senderNonce = SenderNonce.Data;
                byte[] receiverNonce = ReceiverNonce.Data;

                encoder.WriteUInt16(null, (ushort)(senderNonce.Length + receiverNonce.Length + 8));
                encoder.WriteByteString(null, senderNonce);
                encoder.WriteByteString(null, receiverNonce);

                // create keys.
                CreateKeysForEcc(
                    SecurityPolicy,
                    SenderNonce,
                    ReceiverNonce,
                    false,
                    out encryptingKey,
                    out iv);

                // reserves space for padding and tag that is added by SymmetricEncryptAndSign.
                var dataToEncrypt = new byte[4096];
                using var stream = new MemoryStream(dataToEncrypt);
                using var secretEncoder = new BinaryEncoder(stream, Context, false);

                secretEncoder.WriteByteString(null, nonce);
                secretEncoder.WriteByteString(null, secret);

                var encryptedData = CryptoUtils.SymmetricEncryptAndSign(
                    new ArraySegment<byte>(dataToEncrypt, 0, secretEncoder.Position),
                    SecurityPolicy,
                    encryptingKey,
                    iv);

                // append encrypted secret.
                for (int ii = encryptedData.Offset; ii < encryptedData.Offset + encryptedData.Count; ii++)
                {
                    encoder.WriteByte(null, encryptedData.Array[ii]);
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

            var dataToSign = new ArraySegment<byte>(message, 0, message.Length - signatureLength);
            byte[] signature = CryptoUtils.Sign(dataToSign, SenderCertificate, SecurityPolicy.AsymmetricSignatureAlgorithm);

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

            SecurityPolicy = SecurityPolicies.GetInfo(decoder.ReadString(null));

            if (SecurityPolicy.CertificateKeyFamily != CertificateKeyFamily.ECC)
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
                startOfData + (int)length - signatureLength,
                signature,
                0,
                signatureLength);

            var dataToSign = new ArraySegment<byte>(
                dataToDecrypt.Array,
                0,
                startOfData + (int)length - signatureLength);

            if (!CryptoUtils.Verify(dataToSign, signature, SenderCertificate, SecurityPolicy.AsymmetricSignatureAlgorithm))
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
                SecurityPolicy,
                ReceiverNonce,
                SenderNonce,
                true,
                out byte[] encryptingKey,
                out byte[] iv);

            byte[] bytes = new byte[dataToDecrypt.Count];
            Buffer.BlockCopy(dataToDecrypt.Array, dataToDecrypt.Offset, bytes, 0, dataToDecrypt.Count);

            ArraySegment<byte> plainText = CryptoUtils.SymmetricDecryptAndVerify(
                new ArraySegment<byte>(bytes),
                SecurityPolicy,
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
