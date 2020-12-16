/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
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
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Opc.Ua.Bindings
{
    public partial class UaSCUaBinaryChannel
    {
        #region Token Handling Members
        /// <summary>
        /// Returns the current security token.
        /// </summary>
        protected internal ChannelToken CurrentToken => m_currentToken;

        /// <summary>
        /// Returns the current security token.
        /// </summary>
        protected ChannelToken PreviousToken => m_previousToken;

        /// <summary>
        /// Returns the renewed but not yet activated token.
        /// </summary>
        protected ChannelToken RenewedToken => m_renewedToken;

        /// <summary>
        /// Creates a new token.
        /// </summary>
        protected ChannelToken CreateToken()
        {
            ChannelToken token = new ChannelToken();

            token.ChannelId = m_channelId;
            token.TokenId = 0;
            token.CreatedAt = DateTime.UtcNow;
            token.Lifetime = (int)Quotas.SecurityTokenLifetime;

            Utils.Trace("Token #{0} created. CreatedAt = {1:HH:mm:ss.fff} . Lifetime = {2}", token.TokenId, token.CreatedAt, token.Lifetime);

            return token;
        }

        /// <summary>
        /// Activates a new token.
        /// </summary>
        protected void ActivateToken(ChannelToken token)
        {
            // compute the keys for the token.
            ComputeKeys(token);

            m_previousToken = m_currentToken;
            m_currentToken = token;
            m_renewedToken = null;

            Utils.Trace("Token #{0} activated. CreatedAt = {1:HH:mm:ss.fff} . Lifetime = {2}", token.TokenId, token.CreatedAt, token.Lifetime);
        }

        /// <summary>
        /// Sets the renewed token
        /// </summary>
        protected void SetRenewedToken(ChannelToken token)
        {
            m_renewedToken = token;

            Utils.Trace("RenewedToken #{0} set. CreatedAt = {1:HH:mm:ss.fff} . Lifetime = {2}", token.TokenId, token.CreatedAt, token.Lifetime);
        }

        /// <summary>
        /// Discards the tokens.
        /// </summary>
        protected void DiscardTokens()
        {
            m_previousToken = null;
            m_currentToken = null;
        }
        #endregion

        #region Symmetric Cryptography Functions
        /// <summary>
        /// The byte length of the MAC (a.k.a signature) attached to each message.
        /// </summary>
        private int SymmetricSignatureSize => m_hmacHashSize;

        /// <summary>
        /// The byte length the encryption blocks.
        /// </summary>
        private int EncryptionBlockSize => m_encryptionBlockSize;

        /// <summary>
        /// Calculates the symmetric key sizes based on the current security policy.
        /// </summary>
        protected void CalculateSymmetricKeySizes()
        {
            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.Basic128Rsa15:
                {
                    m_hmacHashSize = 20;
                    m_signatureKeySize = 16;
                    m_encryptionKeySize = 16;
                    m_encryptionBlockSize = 16;
                    break;
                }

                case SecurityPolicies.Basic256:
                {
                    m_hmacHashSize = 20;
                    m_signatureKeySize = 24;
                    m_encryptionKeySize = 32;
                    m_encryptionBlockSize = 16;
                    break;
                }

                case SecurityPolicies.Basic256Sha256:
                {
                    m_hmacHashSize = 32;
                    m_signatureKeySize = 32;
                    m_encryptionKeySize = 32;
                    m_encryptionBlockSize = 16;
                    break;
                }

                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                {
                    m_hmacHashSize = 32;
                    m_signatureKeySize = 32;
                    m_encryptionKeySize = 16;
                    m_encryptionBlockSize = 16;
                    break;
                }

                case SecurityPolicies.Aes256_Sha256_RsaPss:
                {
                    m_hmacHashSize = 32;
                    m_signatureKeySize = 32;
                    m_encryptionKeySize = 32;
                    m_encryptionBlockSize = 16;
                    break;
                }

                default:
                case SecurityPolicies.None:
                {
                    m_hmacHashSize = 0;
                    m_signatureKeySize = 0;
                    m_encryptionKeySize = 0;
                    m_encryptionBlockSize = 1;
                    break;
                }
            }
        }

        /// <summary>
        /// Computes the keys for a token.
        /// </summary>
        protected void ComputeKeys(ChannelToken token)
        {
            if (SecurityMode == MessageSecurityMode.None)
            {
                return;
            }

            if (SecurityPolicyUri == SecurityPolicies.Basic256Sha256 ||
                SecurityPolicyUri == SecurityPolicies.Aes128_Sha256_RsaOaep ||
                SecurityPolicyUri == SecurityPolicies.Aes256_Sha256_RsaPss)
            {
                token.ClientSigningKey = Utils.PSHA256(token.ServerNonce, null, token.ClientNonce, 0, m_signatureKeySize);
                token.ClientEncryptingKey = Utils.PSHA256(token.ServerNonce, null, token.ClientNonce, m_signatureKeySize, m_encryptionKeySize);
                token.ClientInitializationVector = Utils.PSHA256(token.ServerNonce, null, token.ClientNonce, m_signatureKeySize + m_encryptionKeySize, m_encryptionBlockSize);
                token.ServerSigningKey = Utils.PSHA256(token.ClientNonce, null, token.ServerNonce, 0, m_signatureKeySize);
                token.ServerEncryptingKey = Utils.PSHA256(token.ClientNonce, null, token.ServerNonce, m_signatureKeySize, m_encryptionKeySize);
                token.ServerInitializationVector = Utils.PSHA256(token.ClientNonce, null, token.ServerNonce, m_signatureKeySize + m_encryptionKeySize, m_encryptionBlockSize);
            }
            else
            {
                token.ClientSigningKey = Utils.PSHA1(token.ServerNonce, null, token.ClientNonce, 0, m_signatureKeySize);
                token.ClientEncryptingKey = Utils.PSHA1(token.ServerNonce, null, token.ClientNonce, m_signatureKeySize, m_encryptionKeySize);
                token.ClientInitializationVector = Utils.PSHA1(token.ServerNonce, null, token.ClientNonce, m_signatureKeySize + m_encryptionKeySize, m_encryptionBlockSize);
                token.ServerSigningKey = Utils.PSHA1(token.ClientNonce, null, token.ServerNonce, 0, m_signatureKeySize);
                token.ServerEncryptingKey = Utils.PSHA1(token.ClientNonce, null, token.ServerNonce, m_signatureKeySize, m_encryptionKeySize);
                token.ServerInitializationVector = Utils.PSHA1(token.ClientNonce, null, token.ServerNonce, m_signatureKeySize + m_encryptionKeySize, m_encryptionBlockSize);
            }

            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                {
                    // create encryptors.
                    SymmetricAlgorithm AesCbcEncryptorProvider = Aes.Create();
                    AesCbcEncryptorProvider.Mode = CipherMode.CBC;
                    AesCbcEncryptorProvider.Padding = PaddingMode.None;
                    AesCbcEncryptorProvider.Key = token.ClientEncryptingKey;
                    AesCbcEncryptorProvider.IV = token.ClientInitializationVector;
                    token.ClientEncryptor = AesCbcEncryptorProvider;

                    SymmetricAlgorithm AesCbcDecryptorProvider = Aes.Create();
                    AesCbcDecryptorProvider.Mode = CipherMode.CBC;
                    AesCbcDecryptorProvider.Padding = PaddingMode.None;
                    AesCbcDecryptorProvider.Key = token.ServerEncryptingKey;
                    AesCbcDecryptorProvider.IV = token.ServerInitializationVector;
                    token.ServerEncryptor = AesCbcDecryptorProvider;

                    // create HMACs.
                    if (SecurityPolicyUri == SecurityPolicies.Basic256Sha256 ||
                        SecurityPolicyUri == SecurityPolicies.Aes128_Sha256_RsaOaep ||
                        SecurityPolicyUri == SecurityPolicies.Aes256_Sha256_RsaPss)
                    {
                        // SHA256
                        token.ServerHmac = new HMACSHA256(token.ServerSigningKey);
                        token.ClientHmac = new HMACSHA256(token.ClientSigningKey);
                    }
                    else
                    {   // SHA1
                        token.ServerHmac = new HMACSHA1(token.ServerSigningKey);
                        token.ClientHmac = new HMACSHA1(token.ClientSigningKey);
                    }
                    break;
                }

                default:
                case SecurityPolicies.None:
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Secures the message using the security token.
        /// </summary>
        protected BufferCollection WriteSymmetricMessage(
            uint messageType,
            uint requestId,
            ChannelToken token,
            object messageBody,
            bool isRequest,
            out bool limitsExceeded)
        {
            limitsExceeded = false;
            bool success = false;
            BufferCollection chunksToProcess = null;

            try
            {
                // calculate chunk sizes.
                int maxCipherTextSize = SendBufferSize - TcpMessageLimits.SymmetricHeaderSize;
                int maxCipherBlocks = maxCipherTextSize / EncryptionBlockSize;
                int maxPlainTextSize = maxCipherBlocks * EncryptionBlockSize;
                int maxPayloadSize = maxPlainTextSize - SymmetricSignatureSize - 1 - TcpMessageLimits.SequenceHeaderSize;
                int headerSize = TcpMessageLimits.SymmetricHeaderSize + TcpMessageLimits.SequenceHeaderSize;

                // write the body to stream.
                ArraySegmentStream ostrm = new ArraySegmentStream(
                    BufferManager,
                    SendBufferSize,
                    headerSize,
                    maxPayloadSize);

                // check for encodeable body.
                IEncodeable encodeable = messageBody as IEncodeable;

                if (encodeable != null)
                {
                    // debug code used to verify that message aborts are handled correctly.
                    // int maxMessageSize = Quotas.MessageContext.MaxMessageSize;
                    // Quotas.MessageContext.MaxMessageSize = Int32.MaxValue;

                    BinaryEncoder.EncodeMessage(encodeable, ostrm, Quotas.MessageContext);

                    // Quotas.MessageContext.MaxMessageSize = maxMessageSize;
                }

                // check for raw bytes.
                ArraySegment<byte>? rawBytes = messageBody as ArraySegment<byte>?;

                if (rawBytes != null)
                {
                    BinaryEncoder encoder = new BinaryEncoder(ostrm, Quotas.MessageContext);
                    encoder.WriteRawBytes(rawBytes.Value.Array, rawBytes.Value.Offset, rawBytes.Value.Count);
                    encoder.Close();
                }

                chunksToProcess = ostrm.GetBuffers("WriteSymmetricMessage");

                // ensure there is at least one chunk.
                if (chunksToProcess.Count == 0)
                {
                    byte[] buffer = BufferManager.TakeBuffer(SendBufferSize, "WriteSymmetricMessage");
                    chunksToProcess.Add(new ArraySegment<byte>(buffer, 0, 0));
                }

                BufferCollection chunksToSend = new BufferCollection(chunksToProcess.Capacity);

                int messageSize = 0;

                for (int ii = 0; ii < chunksToProcess.Count; ii++)
                {
                    ArraySegment<byte> chunkToProcess = chunksToProcess[ii];

                    // nothing more to do if limits exceeded.
                    if (limitsExceeded)
                    {
                        BufferManager.ReturnBuffer(chunkToProcess.Array, "WriteSymmetricMessage");
                        continue;
                    }

                    MemoryStream strm = new MemoryStream(chunkToProcess.Array, 0, SendBufferSize);
                    BinaryEncoder encoder = new BinaryEncoder(strm, Quotas.MessageContext);

                    // check if the message needs to be aborted.
                    if (MessageLimitsExceeded(isRequest, messageSize + chunkToProcess.Count - headerSize, ii + 1))
                    {
                        encoder.WriteUInt32(null, messageType | TcpMessageType.Abort);

                        // replace the body in the chunk with an error message.
                        BinaryEncoder errorEncoder = new BinaryEncoder(
                            chunkToProcess.Array,
                            chunkToProcess.Offset,
                            chunkToProcess.Count,
                            Quotas.MessageContext);

                        WriteErrorMessageBody(errorEncoder, (isRequest) ? StatusCodes.BadRequestTooLarge : StatusCodes.BadResponseTooLarge);

                        int size = errorEncoder.Close();
                        chunkToProcess = new ArraySegment<byte>(chunkToProcess.Array, chunkToProcess.Offset, size);

                        limitsExceeded = true;
                    }

                    // check if the message is complete.
                    else if (ii == chunksToProcess.Count - 1)
                    {
                        encoder.WriteUInt32(null, messageType | TcpMessageType.Final);
                    }

                    // more chunks to follow.
                    else
                    {
                        encoder.WriteUInt32(null, messageType | TcpMessageType.Intermediate);
                    }

                    int count = 0;

                    count += TcpMessageLimits.SequenceHeaderSize;
                    count += chunkToProcess.Count;
                    count += SymmetricSignatureSize;

                    // calculate the padding.
                    int padding = 0;

                    if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
                    {
                        // reserve one byte for the padding size.
                        count++;

                        if (count % EncryptionBlockSize != 0)
                        {
                            padding = EncryptionBlockSize - (count % EncryptionBlockSize);
                        }

                        count += padding;
                    }

                    count += TcpMessageLimits.SymmetricHeaderSize;

                    encoder.WriteUInt32(null, (uint)count);
                    encoder.WriteUInt32(null, ChannelId);
                    encoder.WriteUInt32(null, token.TokenId);

                    uint sequenceNumber = GetNewSequenceNumber();
                    encoder.WriteUInt32(null, sequenceNumber);

                    encoder.WriteUInt32(null, requestId);

                    // skip body.
                    strm.Seek(chunkToProcess.Count, SeekOrigin.Current);

                    // update message size count.
                    messageSize += chunkToProcess.Count;

                    // write padding.
                    if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
                    {
                        for (int jj = 0; jj <= padding; jj++)
                        {
                            encoder.WriteByte(null, (byte)padding);
                        }
                    }

                    if (SecurityMode != MessageSecurityMode.None)
                    {
                        // calculate and write signature.
                        byte[] signature = Sign(token, new ArraySegment<byte>(chunkToProcess.Array, 0, encoder.Position), isRequest);

                        if (signature != null)
                        {
                            encoder.WriteRawBytes(signature, 0, signature.Length);
                        }
                    }

                    if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
                    {
                        // encrypt the data.
                        ArraySegment<byte> dataToEncrypt = new ArraySegment<byte>(chunkToProcess.Array, TcpMessageLimits.SymmetricHeaderSize, encoder.Position - TcpMessageLimits.SymmetricHeaderSize);
                        Encrypt(token, dataToEncrypt, isRequest);
                    }

                    // add the header into chunk.
                    chunksToSend.Add(new ArraySegment<byte>(chunkToProcess.Array, 0, encoder.Position));
                }

                // ensure the buffers don't get cleaned up on exit.
                success = true;
                return chunksToSend;
            }
            finally
            {
                if (!success)
                {
                    if (chunksToProcess != null)
                    {
                        chunksToProcess.Release(BufferManager, "WriteSymmetricMessage");
                    }
                }
            }
        }

        /// <summary>
        /// Decrypts and verifies a message chunk.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", MessageId = "messageType"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", MessageId = "messageSize")]
        protected ArraySegment<byte> ReadSymmetricMessage(
            ArraySegment<byte> buffer,
            bool isRequest,
            out ChannelToken token,
            out uint requestId,
            out uint sequenceNumber)
        {
            BinaryDecoder decoder = new BinaryDecoder(buffer.Array, buffer.Offset, buffer.Count, Quotas.MessageContext);

            uint messageType = decoder.ReadUInt32(null);
            uint messageSize = decoder.ReadUInt32(null);
            uint channelId = decoder.ReadUInt32(null);
            uint tokenId = decoder.ReadUInt32(null);

            // ensure the channel is valid.
            if (channelId != ChannelId)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "SecureChannelId is not known. ChanneId={0}, CurrentChannelId={1}",
                    channelId,
                    ChannelId);
            }

            // check for a message secured with the new token.
            if (RenewedToken != null && RenewedToken.TokenId == tokenId)
            {
                ActivateToken(RenewedToken);
            }

            // check if activation of the new token should be forced.
            if (RenewedToken != null && CurrentToken.ActivationRequired)
            {
                ActivateToken(RenewedToken);

                Utils.Trace("Token #{0} activated forced.", CurrentToken.TokenId);
            }

            // check for valid token.
            ChannelToken currentToken = CurrentToken;

            if (currentToken == null)
            {
                throw new ServiceResultException(StatusCodes.BadSecureChannelClosed);
            }

            // find the token.
            if (currentToken.TokenId != tokenId && PreviousToken != null && PreviousToken.TokenId != tokenId)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "TokenId is not known. ChanneId={0}, TokenId={1}, CurrentTokenId={2}, PreviousTokenId={3}",
                    channelId,
                    tokenId,
                    currentToken.TokenId,
                    (PreviousToken != null) ? (int)PreviousToken.TokenId : -1);
            }

            token = currentToken;

            // check for a message secured with the token before it expired.
            if (PreviousToken != null && PreviousToken.TokenId == tokenId)
            {
                token = PreviousToken;
            }

            // check if token has expired.
            if (token.Expired)
            {
                throw ServiceResultException.Create(StatusCodes.BadTcpSecureChannelUnknown, "Token #{0} has expired. Lifetime={1:HH:mm:ss.fff}", token.TokenId, token.CreatedAt);
            }

            int headerSize = decoder.Position;

            if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
            {
                // decrypt the message.
                Decrypt(token, new ArraySegment<byte>(buffer.Array, buffer.Offset + headerSize, buffer.Count - headerSize), isRequest);
            }

            if (SecurityMode != MessageSecurityMode.None)
            {
                // extract signature.
                byte[] signature = new byte[SymmetricSignatureSize];

                for (int ii = 0; ii < SymmetricSignatureSize; ii++)
                {
                    signature[ii] = buffer.Array[buffer.Offset + buffer.Count - SymmetricSignatureSize + ii];
                }

                // verify the signature.
                if (!Verify(token, signature, new ArraySegment<byte>(buffer.Array, buffer.Offset, buffer.Count - SymmetricSignatureSize), isRequest))
                {
                    Utils.Trace("Could not verify signature on message.");
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Could not verify the signature on the message.");
                }
            }

            int paddingCount = 0;

            if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
            {
                // verify padding.
                int paddingStart = buffer.Offset + buffer.Count - SymmetricSignatureSize - 1;
                paddingCount = buffer.Array[paddingStart];

                for (int ii = paddingStart - paddingCount; ii < paddingStart; ii++)
                {
                    if (buffer.Array[ii] != paddingCount)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Could not verify the padding in the message.");
                    }
                }

                // add byte for size.
                paddingCount++;
            }

            // extract request id and sequence number.
            sequenceNumber = decoder.ReadUInt32(null);
            requestId = decoder.ReadUInt32(null);

            // return an the data contained in the message.
            int startOfBody = buffer.Offset + TcpMessageLimits.SymmetricHeaderSize + TcpMessageLimits.SequenceHeaderSize;
            int sizeOfBody = buffer.Count - TcpMessageLimits.SymmetricHeaderSize - TcpMessageLimits.SequenceHeaderSize - paddingCount - SymmetricSignatureSize;

            return new ArraySegment<byte>(buffer.Array, startOfBody, sizeOfBody);
        }

        /// <summary>
        /// Returns the symmetric signature for the data.
        /// </summary>
        protected byte[] Sign(ChannelToken token, ArraySegment<byte> dataToSign, bool useClientKeys)
        {
            switch (SecurityPolicyUri)
            {
                default:
                case SecurityPolicies.None:
                {
                    return null;
                }

                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                {
                    return SymmetricSign(token, dataToSign, useClientKeys);
                }
            }
        }

        /// <summary>
        /// Returns the symmetric signature for the data.
        /// </summary>
        protected bool Verify(
            ChannelToken token,
            byte[] signature,
            ArraySegment<byte> dataToVerify,
            bool useClientKeys)
        {
            // verify signature.
            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.None:
                {
                    return true;
                }

                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                {
                    return SymmetricVerify(token, signature, dataToVerify, useClientKeys);
                }

                default:
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Decrypts the data in a buffer using symmetric encryption.
        /// </summary>
        protected void Encrypt(ChannelToken token, ArraySegment<byte> dataToEncrypt, bool useClientKeys)
        {
            switch (SecurityPolicyUri)
            {
                default:
                case SecurityPolicies.None:
                {
                    break;
                }

                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                {
                    SymmetricEncrypt(token, dataToEncrypt, useClientKeys);
                    break;
                }
            }
        }

        /// <summary>
        /// Decrypts the data in a buffer using symmetric encryption.
        /// </summary>
        protected void Decrypt(ChannelToken token, ArraySegment<byte> dataToDecrypt, bool useClientKeys)
        {
            switch (SecurityPolicyUri)
            {
                default:
                case SecurityPolicies.None:
                {
                    break;
                }

                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                {
                    SymmetricDecrypt(token, dataToDecrypt, useClientKeys);
                    break;
                }
            }
        }


        /// <summary>
        /// Signs the message using SHA1 HMAC
        /// </summary>
        private static byte[] SymmetricSign(ChannelToken token, ArraySegment<byte> dataToSign, bool useClientKeys)
        {
            // get HMAC object.
            HMAC hmac = (useClientKeys) ? token.ClientHmac : token.ServerHmac;

            // compute hash.
            MemoryStream istrm = new MemoryStream(dataToSign.Array, dataToSign.Offset, dataToSign.Count, false);
            byte[] signature = hmac.ComputeHash(istrm);
            istrm.Dispose();

            // return signature.
            return signature;
        }

        /// <summary>
        /// Verifies a HMAC for a message.
        /// </summary>
        private static bool SymmetricVerify(
            ChannelToken token,
            byte[] signature,
            ArraySegment<byte> dataToVerify,
            bool useClientKeys)
        {
            // get HMAC object.
            HMAC hmac = (useClientKeys) ? token.ClientHmac : token.ServerHmac;

            // compute hash.
            MemoryStream istrm = new MemoryStream(dataToVerify.Array, dataToVerify.Offset, dataToVerify.Count, false);
            byte[] computedSignature = hmac.ComputeHash(istrm);
            istrm.Dispose();

            // compare signatures.
            for (int ii = 0; ii < signature.Length; ii++)
            {
                if (computedSignature[ii] != signature[ii])
                {
                    string messageType = new UTF8Encoding().GetString(dataToVerify.Array, dataToVerify.Offset, 4);
                    int messageLength = BitConverter.ToInt32(dataToVerify.Array, dataToVerify.Offset + 4);
                    string expectedSignature = Utils.ToHexString(computedSignature);
                    string actualSignature = Utils.ToHexString(signature);

                    var message = new StringBuilder();
                    message.AppendLine("Could not validate signature.");
                    message.AppendLine("ChannelId={0}, TokenId={1}, MessageType={2}, Length={3}");
                    message.AppendLine("ExpectedSignature={4}");
                    message.AppendLine("ActualSignature={5}");
                    Utils.Trace(message.ToString(), token.ChannelId, token.TokenId,
                        messageType, messageLength, expectedSignature, actualSignature);

                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Encrypts a message using a symmetric algorithm.
        /// </summary>
        private static void SymmetricEncrypt(
            ChannelToken token,
            ArraySegment<byte> dataToEncrypt,
            bool useClientKeys)
        {
            SymmetricAlgorithm encryptingKey = (useClientKeys) ? token.ClientEncryptor : token.ServerEncryptor;

            if (encryptingKey == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Token missing symmetric key object.");
            }

            using (ICryptoTransform encryptor = encryptingKey.CreateEncryptor())
            {
                byte[] blockToEncrypt = dataToEncrypt.Array;

                int start = dataToEncrypt.Offset;
                int count = dataToEncrypt.Count;

                if (count % encryptor.InputBlockSize != 0)
                {
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Input data is not an even number of encryption blocks.");
                }

                encryptor.TransformBlock(blockToEncrypt, start, count, blockToEncrypt, start);
            }
        }

        /// <summary>
        /// Decrypts a message using a symmetric algorithm.
        /// </summary>
        private static void SymmetricDecrypt(
            ChannelToken token,
            ArraySegment<byte> dataToDecrypt,
            bool useClientKeys)
        {
            // get the decrypting key.
            SymmetricAlgorithm decryptingKey = (useClientKeys) ? token.ClientEncryptor : token.ServerEncryptor;

            if (decryptingKey == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Token missing symmetric key object.");
            }

            using (ICryptoTransform decryptor = decryptingKey.CreateDecryptor())
            {
                byte[] blockToDecrypt = dataToDecrypt.Array;

                int start = dataToDecrypt.Offset;
                int count = dataToDecrypt.Count;

                if (count % decryptor.InputBlockSize != 0)
                {
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Input data is not an even number of encryption blocks.");
                }

                decryptor.TransformBlock(blockToDecrypt, start, count, blockToDecrypt, start);
            }
        }
        #endregion

        #region Private Fields
        private ChannelToken m_currentToken;
        private ChannelToken m_previousToken;
        private ChannelToken m_renewedToken;
        private int m_hmacHashSize;
        private int m_signatureKeySize;
        private int m_encryptionKeySize;
        private int m_encryptionBlockSize;
        #endregion
    }
}
