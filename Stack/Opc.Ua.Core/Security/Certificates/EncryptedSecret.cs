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

#if NET8_0_OR_GREATER
        private static readonly byte[] s_secretLabel = System.Text.Encoding.UTF8.GetBytes("opcua-secret");

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
            HashAlgorithmName algorithmName = securityPolicy.GetKeyDerivationHashAlgorithmName();

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
                    algorithmName,
                    encryptingKeySize + blockSize);
            }
            else
            {
                keyData = localNonce.DeriveKey(
                    secret,
                    salt,
                    algorithmName,
                    encryptingKeySize + blockSize);
            }

            Buffer.BlockCopy(keyData, 0, encryptingKey, 0, encryptingKey.Length);
            Buffer.BlockCopy(keyData, encryptingKeySize, iv, 0, iv.Length);
        }
#endif

        /// <summary>
        /// Encrypts a secret using the specified nonce.
        /// </summary>
        /// <param name="secret">The secret to encrypt.</param>
        /// <param name="nonce">The nonce to use for encryption.</param>
        /// <returns>The encrypted secret.</returns>
        public byte[] Encrypt(byte[] secret, byte[] nonce)
        {
#if NET8_0_OR_GREATER
            byte[] encryptingKey = null;
            byte[] iv = null;
            byte[] message = null;
            int lengthPosition = 0;

            int signatureLength = EccUtils.GetSignatureLength(SenderCertificate);

            using (var encoder = new BinaryEncoder(ServiceMessageContext.GlobalContext))
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
                        SecurityPolicies.GetInfo(SecurityPolicyUri),
                        SenderNonce,
                        ReceiverNonce,
                        false,
                        out encryptingKey,
                        out iv);
                }

                // encrypt  secret
                var info = SecurityPolicies.GetInfo(SecurityPolicyUri);

                // reserves space for padding and tag that is added by SymmetricEncryptAndSign.
                var dataToEncrypt = new byte[4096];
                using var stream = new MemoryStream(dataToEncrypt);
                using var secretEncoder = new BinaryEncoder(stream, ServiceMessageContext.GlobalContext, false);

                secretEncoder.WriteByteString(null, nonce);
                secretEncoder.WriteByteString(null, secret);

                var encryptedData = EccUtils.SymmetricEncryptAndSign(
                    new ArraySegment<byte>(dataToEncrypt, 0, secretEncoder.Position),
                    info,
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

            // get the algorithm used for the signature.
            HashAlgorithmName signatureAlgorithm = HashAlgorithmName.SHA256;

            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP384r1:
                    signatureAlgorithm = HashAlgorithmName.SHA384;
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
#else
            throw new NotSupportedException("ECC encryption requires .NET 8 or greater.");
#endif
        }

#if NET8_0_OR_GREATER
        private ArraySegment<byte> VerifyHeaderForEcc(
            ArraySegment<byte> dataToDecrypt,
            DateTime earliestTime)
        {
            using var decoder = new BinaryDecoder(
                dataToDecrypt.Array,
                dataToDecrypt.Offset,
                dataToDecrypt.Count,
                ServiceMessageContext.GlobalContext);
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
            HashAlgorithmName signatureAlgorithm = HashAlgorithmName.SHA256;

            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP384r1:
                    signatureAlgorithm = HashAlgorithmName.SHA384;
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
                    senderCertificate);

                SenderCertificate = senderCertificateChain[0];
                SenderIssuerCertificates = [];

                for (int ii = 1; ii < senderCertificateChain.Count; ii++)
                {
                    SenderIssuerCertificates.Add(senderCertificateChain[ii]);
                }

                // validate the sender.
                Validator?.Validate(senderCertificateChain);
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
            int encryptedDataSize = (int)length - (startOfEncryption - startOfData + signatureLength);
            var encryptedData = new byte[encryptedDataSize];
            Buffer.BlockCopy(dataToDecrypt.Array, startOfEncryption, encryptedData, 0, encryptedDataSize);

            return new ArraySegment<byte>(
                encryptedData,
                0,
                encryptedDataSize);
        }
#endif

        /// <summary>
        /// Decrypts the specified data using the ECC algorithm.
        /// </summary>
        /// <param name="earliestTime">The earliest time allowed for the message.</param>
        /// <param name="expectedNonce">The expected nonce value.</param>
        /// <param name="data">The data to decrypt.</param>
        /// <param name="offset">The offset of the data to decrypt.</param>
        /// <param name="count">The number of bytes to decrypt.</param>
        /// <returns>The decrypted data.</returns>
        /// <exception cref="ServiceResultException"></exception>
        public byte[] Decrypt(
            DateTime earliestTime,
            byte[] expectedNonce,
            byte[] data,
            int offset,
            int count)
        {
#if NET8_0_OR_GREATER
            ArraySegment<byte> dataToDecrypt = VerifyHeaderForEcc(
                new ArraySegment<byte>(data, offset, count),
                earliestTime);

            CreateKeysForEcc(
                SecurityPolicies.GetInfo(SecurityPolicyUri),
                ReceiverNonce,
                SenderNonce,
                true,
                out byte[] encryptingKey,
                out byte[] iv);

            ArraySegment<byte> plainText = EccUtils.SymmetricDecryptAndVerify(
                dataToDecrypt,
                SecurityPolicies.GetInfo(SecurityPolicyUri),
                encryptingKey,
                iv);

            using var decoder = new BinaryDecoder(
                plainText.Array,
                plainText.Offset,
                plainText.Count,
                ServiceMessageContext.GlobalContext);

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
#else
            throw new NotSupportedException("ECC decryption requires .NET 8 or greater.");
#endif
        }

    }
}
