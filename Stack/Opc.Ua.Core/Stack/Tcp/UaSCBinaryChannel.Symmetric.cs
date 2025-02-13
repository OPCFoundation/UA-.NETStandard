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
using System.Text;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;

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
        /// Called when the token changes
        /// </summary>
        protected internal Action<ChannelToken, ChannelToken> OnTokenActivated { get; set; }

        /// <summary>
        /// Creates a new token.
        /// </summary>
        protected ChannelToken CreateToken()
        {
            ChannelToken token = new ChannelToken {
                ChannelId = m_channelId,
                TokenId = 0,
                CreatedAt = DateTime.UtcNow,
                CreatedAtTickCount = HiResClock.TickCount,
                Lifetime = Quotas.SecurityTokenLifetime
            };

            Utils.LogInfo("ChannelId {0}: New Token created. CreatedAt={1:HH:mm:ss.fff}-{2}. Lifetime={3}.",
                Id, token.CreatedAt, token.CreatedAtTickCount, token.Lifetime);

            return token;
        }

        /// <summary>
        /// Activates a new token.
        /// </summary>
        protected void ActivateToken(ChannelToken token)
        {
            // compute the keys for the token.
            ComputeKeys(token);

            Utils.SilentDispose(m_previousToken);
            m_previousToken = m_currentToken;
            m_currentToken = token;
            m_renewedToken = null;

            OnTokenActivated?.Invoke(token, m_previousToken);

            Utils.LogInfo("ChannelId {0}: Token #{1} activated. CreatedAt={2:HH:mm:ss.fff}-{3}. Lifetime={4}.",
                Id, token.TokenId, token.CreatedAt, token.CreatedAtTickCount, token.Lifetime);
        }

        /// <summary>
        /// Sets the renewed token.
        /// </summary>
        protected void SetRenewedToken(ChannelToken token)
        {
            Utils.SilentDispose(m_renewedToken);
            m_renewedToken = token;
            Utils.LogInfo("ChannelId {0}: Renewed Token #{1} set. CreatedAt={2:HH:mm:ss.fff}-{3}. Lifetime={4}.",
                Id, token.TokenId, token.CreatedAt, token.CreatedAtTickCount, token.Lifetime);
        }

        /// <summary>
        /// Discards the tokens.
        /// </summary>
        protected void DiscardTokens()
        {
            Utils.SilentDispose(m_previousToken);
            m_previousToken = null;
            Utils.SilentDispose(m_currentToken);
            m_currentToken = null;
            Utils.SilentDispose(m_renewedToken);
            m_renewedToken = null;

            OnTokenActivated?.Invoke(null, null);
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
        [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms",
            Justification = "SHA1 required for deprecated profiles")]
        protected void ComputeKeys(ChannelToken token)
        {
            if (SecurityMode == MessageSecurityMode.None)
            {
                return;
            }

            bool useSHA256 = SecurityPolicyUri != SecurityPolicies.Basic128Rsa15 && SecurityPolicyUri != SecurityPolicies.Basic256;

            if (useSHA256)
            {
                using (HMACSHA256 hmac = new HMACSHA256(token.ServerNonce))
                {
                    token.ClientSigningKey = Utils.PSHA256(hmac, null, token.ClientNonce, 0, m_signatureKeySize);
                    token.ClientEncryptingKey = Utils.PSHA256(hmac, null, token.ClientNonce, m_signatureKeySize, m_encryptionKeySize);
                    token.ClientInitializationVector = Utils.PSHA256(hmac, null, token.ClientNonce, m_signatureKeySize + m_encryptionKeySize, m_encryptionBlockSize);
                }
                using (HMACSHA256 hmac = new HMACSHA256(token.ClientNonce))
                {
                    token.ServerSigningKey = Utils.PSHA256(hmac, null, token.ServerNonce, 0, m_signatureKeySize);
                    token.ServerEncryptingKey = Utils.PSHA256(hmac, null, token.ServerNonce, m_signatureKeySize, m_encryptionKeySize);
                    token.ServerInitializationVector = Utils.PSHA256(hmac, null, token.ServerNonce, m_signatureKeySize + m_encryptionKeySize, m_encryptionBlockSize);
                }
            }
            else
            {
                using (HMACSHA1 hmac = new HMACSHA1(token.ServerNonce))
                {
                    token.ClientSigningKey = Utils.PSHA1(hmac, null, token.ClientNonce, 0, m_signatureKeySize);
                    token.ClientEncryptingKey = Utils.PSHA1(hmac, null, token.ClientNonce, m_signatureKeySize, m_encryptionKeySize);
                    token.ClientInitializationVector = Utils.PSHA1(hmac, null, token.ClientNonce, m_signatureKeySize + m_encryptionKeySize, m_encryptionBlockSize);
                }
                using (HMACSHA1 hmac = new HMACSHA1(token.ClientNonce))
                {
                    token.ServerSigningKey = Utils.PSHA1(hmac, null, token.ServerNonce, 0, m_signatureKeySize);
                    token.ServerEncryptingKey = Utils.PSHA1(hmac, null, token.ServerNonce, m_signatureKeySize, m_encryptionKeySize);
                    token.ServerInitializationVector = Utils.PSHA1(hmac, null, token.ServerNonce, m_signatureKeySize + m_encryptionKeySize, m_encryptionBlockSize);
                }
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
                    SymmetricAlgorithm aesCbcEncryptorProvider = Aes.Create();
                    aesCbcEncryptorProvider.Mode = CipherMode.CBC;
                    aesCbcEncryptorProvider.Padding = PaddingMode.None;
                    aesCbcEncryptorProvider.Key = token.ClientEncryptingKey;
                    aesCbcEncryptorProvider.IV = token.ClientInitializationVector;
                    token.ClientEncryptor = aesCbcEncryptorProvider;

                    SymmetricAlgorithm aesCbcDecryptorProvider = Aes.Create();
                    aesCbcDecryptorProvider.Mode = CipherMode.CBC;
                    aesCbcDecryptorProvider.Padding = PaddingMode.None;
                    aesCbcDecryptorProvider.Key = token.ServerEncryptingKey;
                    aesCbcDecryptorProvider.IV = token.ServerInitializationVector;
                    token.ServerEncryptor = aesCbcDecryptorProvider;

                    // create HMACs. Must be disposed after use.
                    if (useSHA256)
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
                if (messageBody is IEncodeable encodeable)
                {
                    // debug code used to verify that message aborts are handled correctly.
                    // int maxMessageSize = Quotas.MessageContext.MaxMessageSize;
                    // Quotas.MessageContext.MaxMessageSize = Int32.MaxValue;

                    BinaryEncoder.EncodeMessage(encodeable, ostrm, Quotas.MessageContext, true);

                    // Quotas.MessageContext.MaxMessageSize = maxMessageSize;
                }

                // check for raw bytes.
                ArraySegment<byte>? rawBytes = messageBody as ArraySegment<byte>?;

                if (rawBytes != null)
                {
                    using (BinaryEncoder encoder = new BinaryEncoder(ostrm, Quotas.MessageContext, true))
                    {
                        encoder.WriteRawBytes(rawBytes.Value.Array, rawBytes.Value.Offset, rawBytes.Value.Count);
                    }
                }

                chunksToProcess = ostrm.GetBuffers("WriteSymmetricMessage");

                // ensure there is at least one chunk.
                if (chunksToProcess.Count == 0)
                {
                    byte[] buffer = BufferManager.TakeBuffer(SendBufferSize, "WriteSymmetricMessage");
                    chunksToProcess.Add(new ArraySegment<byte>(buffer, 0, 0));
                }

                BufferCollection chunksToSend = new BufferCollection(chunksToProcess.Capacity);

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
                Span<byte> paddingBuffer = stackalloc byte[EncryptionBlockSize];
#endif
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
                    using (BinaryEncoder encoder = new BinaryEncoder(strm, Quotas.MessageContext, false))
                    {
                        // check if the message needs to be aborted.
                        if (MessageLimitsExceeded(isRequest, messageSize + chunkToProcess.Count - headerSize, ii + 1))
                        {
                            encoder.WriteUInt32(null, messageType | TcpMessageType.Abort);

                            // replace the body in the chunk with an error message.
                            using (BinaryEncoder errorEncoder = new BinaryEncoder(
                                chunkToProcess.Array,
                                chunkToProcess.Offset,
                                chunkToProcess.Count,
                                Quotas.MessageContext))
                            {
                                WriteErrorMessageBody(errorEncoder, (isRequest) ? StatusCodes.BadRequestTooLarge : StatusCodes.BadResponseTooLarge);
                                int size = errorEncoder.Close();
                                chunkToProcess = new ArraySegment<byte>(chunkToProcess.Array, chunkToProcess.Offset, size);
                            }

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

                            // use padding as helper to calc the real padding
                            padding = count % EncryptionBlockSize;
                            if (padding != 0)
                            {
                                padding = EncryptionBlockSize - padding;
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
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
                            if (padding > 1)
                            {
                                Span<byte> buffer = paddingBuffer.Slice(0, padding + 1);
                                buffer.Fill((byte)padding);
                                encoder.WriteRawBytes(buffer);
                            }
                            else
#endif
                            {
                                for (int jj = 0; jj <= padding; jj++)
                                {
                                    encoder.WriteByte(null, (byte)padding);
                                }
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
                }

                // ensure the buffers don't get cleaned up on exit.
                success = true;
                return chunksToSend;
            }
            finally
            {
                if (!success)
                {
                    chunksToProcess?.Release(BufferManager, "WriteSymmetricMessage");
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
            using (var decoder = new BinaryDecoder(buffer, Quotas.MessageContext))
            {
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
                else if (RenewedToken != null && CurrentToken.ActivationRequired)
                {
                    ActivateToken(RenewedToken);
                    Utils.LogInfo("ChannelId {0}: Token #{1} activated forced.", Id, CurrentToken.TokenId);
                }

                // check for valid token.
                ChannelToken currentToken = CurrentToken;

                if (currentToken == null)
                {
                    throw new ServiceResultException(StatusCodes.BadSecureChannelClosed);
                }

                // find the token.
                if (currentToken.TokenId != tokenId && (PreviousToken == null || PreviousToken.TokenId != tokenId))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTcpSecureChannelUnknown,
                        "Channel{0}: TokenId is not known. ChanneId={1}, TokenId={2}, CurrentTokenId={3}, PreviousTokenId={4}",
                        Id, channelId,
                        tokenId, currentToken.TokenId,
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
                    throw ServiceResultException.Create(StatusCodes.BadTcpSecureChannelUnknown,
                        "Channel{0}: Token #{1} has expired. Lifetime={2:HH:mm:ss.fff}-{3}",
                        Id, token.TokenId, token.CreatedAt, token.CreatedAtTickCount);
                }

                int headerSize = decoder.Position;

                if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
                {
                    // decrypt the message.
                    Decrypt(token, new ArraySegment<byte>(buffer.Array, buffer.Offset + headerSize, buffer.Count - headerSize), isRequest);
                }

                int paddingCount = 0;
                if (SecurityMode != MessageSecurityMode.None)
                {
                    int signatureStart = buffer.Offset + buffer.Count - SymmetricSignatureSize;

                    // extract signature.
                    byte[] signature = new byte[SymmetricSignatureSize];
                    Array.Copy(buffer.Array, signatureStart, signature, 0, signature.Length);

                    // verify the signature.
                    if (!Verify(token, signature, new ArraySegment<byte>(buffer.Array, buffer.Offset, buffer.Count - SymmetricSignatureSize), isRequest))
                    {
                        Utils.LogError("ChannelId {0}: Could not verify signature on message.", Id);
                        throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Could not verify the signature on the message.");
                    }

                    if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
                    {
                        // verify padding.
                        int paddingStart = signatureStart - 1;
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
                }

                // extract request id and sequence number.
                sequenceNumber = decoder.ReadUInt32(null);
                requestId = decoder.ReadUInt32(null);

                // return an the data contained in the message.
                int startOfBody = buffer.Offset + TcpMessageLimits.SymmetricHeaderSize + TcpMessageLimits.SequenceHeaderSize;
                int sizeOfBody = buffer.Count - TcpMessageLimits.SymmetricHeaderSize - TcpMessageLimits.SequenceHeaderSize - paddingCount - SymmetricSignatureSize;

                return new ArraySegment<byte>(buffer.Array, startOfBody, sizeOfBody);
            }
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

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        /// <summary>
        /// Signs the message using HMAC.
        /// </summary>
        private static byte[] SymmetricSign(ChannelToken token, ReadOnlySpan<byte> dataToSign, bool useClientKeys)
        {
            // get HMAC object.
            HMAC hmac = (useClientKeys) ? token.ClientHmac : token.ServerHmac;

            // compute hash.
            int hashSizeInBytes = hmac.HashSize >> 3;
            byte[] signature = new byte[hashSizeInBytes];
            bool result = hmac.TryComputeHash(dataToSign, signature, out int bytesWritten);

            // check result
            if (!result || bytesWritten != hashSizeInBytes)
            {
                ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "The computed hash doesn't match the expected size.");
            }

            // return signature.
            return signature;
        }
#else
        /// <summary>
        /// Signs the message using HMAC.
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
#endif

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        /// <summary>
        /// Verifies a HMAC for a message.
        /// </summary>
        private bool SymmetricVerify(
            ChannelToken token,
            ReadOnlySpan<byte> signature,
            ReadOnlySpan<byte> dataToVerify,
            bool useClientKeys)
        {
            // get HMAC object.
            HMAC hmac = (useClientKeys) ? token.ClientHmac : token.ServerHmac;

            // compute hash.
            int hashSizeInBytes = hmac.HashSize >> 3;
            Span<byte> computedSignature = stackalloc byte[hashSizeInBytes];
            bool result = hmac.TryComputeHash(dataToVerify, computedSignature, out int bytesWritten);
            Debug.Assert(bytesWritten == hashSizeInBytes);
            // compare signatures.
            if (!result || !computedSignature.SequenceEqual(signature))
            {
                string expectedSignature = Utils.ToHexString(computedSignature.ToArray());
                string messageType = Encoding.UTF8.GetString(dataToVerify.Slice(0, 4));
                int messageLength = BitConverter.ToInt32(dataToVerify.Slice(4));
                string actualSignature = Utils.ToHexString(signature);
#else
        /// <summary>
        /// Verifies a HMAC for a message.
        /// </summary>
        private bool SymmetricVerify(
            ChannelToken token,
            byte[] signature,
            ArraySegment<byte> dataToVerify,
            bool useClientKeys)
        {
            // get HMAC object.
            HMAC hmac = (useClientKeys) ? token.ClientHmac : token.ServerHmac;

            MemoryStream istrm = new MemoryStream(dataToVerify.Array, dataToVerify.Offset, dataToVerify.Count, false);
            byte[] computedSignature = hmac.ComputeHash(istrm);
            istrm.Dispose();
            // compare signatures.
            if (!Utils.IsEqual(computedSignature, signature))
            {
                string expectedSignature = Utils.ToHexString(computedSignature);
                string messageType = Encoding.UTF8.GetString(dataToVerify.Array, dataToVerify.Offset, 4);
                int messageLength = BitConverter.ToInt32(dataToVerify.Array, dataToVerify.Offset + 4);
                string actualSignature = Utils.ToHexString(signature);
#endif

                var message = new StringBuilder();
                message.AppendLine("Channel{0}: Could not validate signature.");
                message.AppendLine("ChannelId={1}, TokenId={2}, MessageType={3}, Length={4}");
                message.AppendLine("ExpectedSignature={5}");
                message.AppendLine("ActualSignature={6}");
                Utils.LogError(message.ToString(), Id, token.ChannelId, token.TokenId,
                    messageType, messageLength, expectedSignature, actualSignature);

                return false;
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
