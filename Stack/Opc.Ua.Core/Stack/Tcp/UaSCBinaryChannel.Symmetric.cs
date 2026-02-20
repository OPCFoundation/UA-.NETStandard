/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    public partial class UaSCUaBinaryChannel
    {
        /// <summary>
        /// Returns the current security token.
        /// </summary>
        protected internal ChannelToken CurrentToken { get; private set; }

        /// <summary>
        /// Returns the current security token.
        /// </summary>
        protected internal ChannelToken PreviousToken { get; private set; }

        /// <summary>
        /// Returns the renewed but not yet activated token.
        /// </summary>
        protected internal ChannelToken RenewedToken { get; private set; }

        /// <summary>
        /// Called when the token changes
        /// </summary>
        protected internal Action<ChannelToken, ChannelToken> OnTokenActivated { get; set; }

        /// <summary>
        /// Creates a new token.
        /// </summary>
        protected ChannelToken CreateToken()
        {
            var token = new ChannelToken
            {
                ChannelId = Id,
                TokenId = 0,
                CreatedAt = DateTime.UtcNow,
                CreatedAtTickCount = HiResClock.TickCount,
                Lifetime = Quotas.SecurityTokenLifetime
            };

            m_logger.LogInformation(
                "ChannelId {ChannelId}: New Token created. CreatedAt={CreatedAt:HH:mm:ss.fff}-{CreatedAtTickCount}. Lifetime={Lifetime}.",
                Id,
                token.CreatedAt,
                token.CreatedAtTickCount,
                token.Lifetime);

            return token;
        }

        /// <summary>
        /// Activates a new token.
        /// </summary>
        protected void ActivateToken(ChannelToken token)
        {
            // compute the keys for the token.
            ComputeKeys(token);

            Utils.SilentDispose(PreviousToken);
            PreviousToken = CurrentToken;
            CurrentToken = token;
            RenewedToken = null;

            OnTokenActivated?.Invoke(token, PreviousToken);

            m_logger.LogInformation(
                "ChannelId {Id}: Token #{TokenId} activated. CreatedAt={CreatedAt:HH:mm:ss.fff}-{CreatedAtTickCount}. Lifetime={Lifetime}.",
                Id,
                token.TokenId,
                token.CreatedAt,
                token.CreatedAtTickCount,
                token.Lifetime);
        }

        /// <summary>
        /// Sets the renewed token.
        /// </summary>
        protected void SetRenewedToken(ChannelToken token)
        {
            Utils.SilentDispose(RenewedToken);
            RenewedToken = token;
            m_logger.LogInformation(
                "ChannelId {Id}: Renewed Token #{TokenId} set. CreatedAt={CreatedAt:HH:mm:ss.fff}-{CreatedAtTickCount}. Lifetime={Lifetime}.",
                Id,
                token.TokenId,
                token.CreatedAt,
                token.CreatedAtTickCount,
                token.Lifetime);
        }

        /// <summary>
        /// Discards the tokens.
        /// </summary>
        protected void DiscardTokens()
        {
            Utils.SilentDispose(PreviousToken);
            PreviousToken = null;
            Utils.SilentDispose(CurrentToken);
            CurrentToken = null;
            Utils.SilentDispose(RenewedToken);
            RenewedToken = null;

            OnTokenActivated?.Invoke(null, null);
        }

        /// <summary>
        /// The byte length of the MAC (a.k.a signature) attached to each message.
        /// </summary>
        private int SymmetricSignatureSize { get; set; }

        /// <summary>
        /// The byte length the encryption blocks.
        /// </summary>
        private int EncryptionBlockSize { get; set; }

        /// <summary>
        /// Calculates the symmetric key sizes based on the current security policy.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected void CalculateSymmetricKeySizes()
        {
            SecurityPolicyInfo info = SecurityPolicies.GetInfo(SecurityPolicyUri)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unsupported security policy: {0}",
                    SecurityPolicyUri);

            SymmetricSignatureSize = info.SymmetricSignatureLength;
            m_signatureKeySize = info.DerivedSignatureKeyLength;
            m_encryptionKeySize = info.SymmetricEncryptionKeyLength;
            EncryptionBlockSize = info.InitializationVectorLength != 0 ? info.InitializationVectorLength : 1;
        }

        private void DeriveKeysWithPSHA(
            HashAlgorithmName algorithmName,
            byte[] secret,
            byte[] seed,
            ChannelToken token,
            bool isServer)
        {
            using HMAC hmac = Utils.CreateHMAC(algorithmName, secret);

            int length = m_signatureKeySize + m_encryptionKeySize + EncryptionBlockSize;

            if (!isServer && SecurityPolicy.SecureChannelEnhancements)
            {
                length += hmac.HashSize/8;
            }
      
            byte[] output = Utils.PSHA(hmac, null, seed, 0, length);

            byte[] signingKey = new byte[m_signatureKeySize];
            byte[] encryptingKey = new byte[m_encryptionKeySize];
            byte[] iv = new byte[EncryptionBlockSize];

            Buffer.BlockCopy(output, 0, signingKey, 0, signingKey.Length);
            Buffer.BlockCopy(output, m_signatureKeySize, encryptingKey, 0, encryptingKey.Length);
            Buffer.BlockCopy(output, m_signatureKeySize + m_encryptionKeySize, iv, 0, iv.Length);

            if (isServer)
            {
                token.ServerSigningKey = signingKey;
                token.ServerEncryptingKey = encryptingKey;
                token.ServerInitializationVector = iv;
                token.ServerHmac = SecurityPolicy.CreateSignatureHmac(signingKey);
            }
            else
            {
                token.ClientSigningKey = signingKey;
                token.ClientEncryptingKey = encryptingKey;
                token.ClientInitializationVector = iv;
                token.ClientHmac = SecurityPolicy.CreateSignatureHmac(signingKey);
            }
        }

        private void DeriveKeysWithHKDF(
            ChannelToken token,
            byte[] salt,
            bool isServer,
            int length)
        {
            CryptoTrace.WriteLine($"DeriveKeys for {((isServer) ? "SERVER" : "CLIENT")}");

            byte[] keyData = m_localNonce.DeriveKeyData(
                token.Secret,
                salt,
                token.SecurityPolicy.KeyDerivationAlgorithm,
                length);

            byte[] signingKey = new byte[m_signatureKeySize];
            byte[] encryptingKey = new byte[m_encryptionKeySize];
            byte[] iv = new byte[EncryptionBlockSize];

            Buffer.BlockCopy(keyData, 0, signingKey, 0, signingKey.Length);
            Buffer.BlockCopy(keyData, m_signatureKeySize, encryptingKey, 0, encryptingKey.Length);
            Buffer.BlockCopy(keyData, m_signatureKeySize + m_encryptionKeySize, iv, 0, iv.Length);

            if (isServer)
            {
                token.ServerSigningKey = signingKey;
                token.ServerEncryptingKey = encryptingKey;
                token.ServerInitializationVector = iv;
            }
            else
            {
                token.ClientSigningKey = signingKey;
                token.ClientEncryptingKey = encryptingKey;
                token.ClientInitializationVector = iv;
            }
        }

        /// <summary>
        /// Computes the keys for a token.
        /// </summary>
        protected void ComputeKeys(ChannelToken token)
        {
            token.SecurityPolicy = SecurityPolicies.GetInfo(SecurityPolicyUri);

            if (SecurityMode == MessageSecurityMode.None)
            {
                return;
            }

            byte[] serverSecret = token.ServerNonce;
            byte[] clientSecret = token.ClientNonce;

            switch (token.SecurityPolicy.KeyDerivationAlgorithm)
            {
                case KeyDerivationAlgorithm.HKDFSha256:
                case KeyDerivationAlgorithm.HKDFSha384:
                {
                    token.Secret = m_localNonce.GenerateSecret(m_remoteNonce, token.PreviousSecret);

                    byte[] clientSalt = Utils.Append(
                        BitConverter.GetBytes((ushort)token.SecurityPolicy.ClientKeyDataLength),
                        s_hkdfClientLabel,
                        clientSecret,
                        serverSecret);

                    DeriveKeysWithHKDF(token, clientSalt, false, token.SecurityPolicy.ClientKeyDataLength);

                    byte[] serverSalt = Utils.Append(
                        BitConverter.GetBytes((ushort)token.SecurityPolicy.ServerKeyDataLength),
                        s_hkdfServerLabel,
                        serverSecret,
                        clientSecret);

                    DeriveKeysWithHKDF(token, serverSalt, true, token.SecurityPolicy.ServerKeyDataLength);

                    CryptoTrace.Start(ConsoleColor.Green, $"ComputeKeys (TokenId={token.TokenId})");
                    CryptoTrace.WriteLine($"IKM={CryptoTrace.KeyToString(token.Secret)}");
                    CryptoTrace.WriteLine($"ServerNonce={CryptoTrace.KeyToString(serverSecret)}");
                    CryptoTrace.WriteLine($"ClientNonce={CryptoTrace.KeyToString(clientSecret)}");
                    CryptoTrace.WriteLine($"ServerSalt={CryptoTrace.KeyToString(serverSalt)}");
                    CryptoTrace.WriteLine($"ServerEncryptingKey={CryptoTrace.KeyToString(token.ServerEncryptingKey)}");
                    CryptoTrace.WriteLine($"ServerInitializationVector={CryptoTrace.KeyToString(token.ServerInitializationVector)}");
                    CryptoTrace.WriteLine($"ClientEncryptingKey={CryptoTrace.KeyToString(token.ClientEncryptingKey)}");
                    CryptoTrace.WriteLine($"ClientInitializationVector={CryptoTrace.KeyToString(token.ClientInitializationVector)}");
                    CryptoTrace.Finish("ComputeKeys");
                    break;
                }

                default:
                case KeyDerivationAlgorithm.PSha1:
                case KeyDerivationAlgorithm.PSha256:
                    HashAlgorithmName algorithmName = token.SecurityPolicy.GetKeyDerivationHashAlgorithmName();
                    DeriveKeysWithPSHA(algorithmName, serverSecret, clientSecret, token, false);
                    DeriveKeysWithPSHA(algorithmName, clientSecret, serverSecret, token, true);
                    break;
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

                int paddingCountSize =
                    (SecurityMode != MessageSecurityMode.SignAndEncrypt || token.SecurityPolicy.NoSymmetricEncryptionPadding)
                    ? 0
                    : 1;

                int maxPayloadSize =
                    maxPlainTextSize -
                    SymmetricSignatureSize -
                    TcpMessageLimits.SequenceHeaderSize -
                    paddingCountSize;

                const int headerSize = TcpMessageLimits.SymmetricHeaderSize +
                    TcpMessageLimits.SequenceHeaderSize;

                // write the body to stream.
                var ostrm = new ArraySegmentStream(
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
                var rawBytes = messageBody as ArraySegment<byte>?;

                if (rawBytes != null)
                {
                    using var encoder = new BinaryEncoder(ostrm, Quotas.MessageContext, true);
                    encoder.WriteRawBytes(
                        rawBytes.Value.Array,
                        rawBytes.Value.Offset,
                        rawBytes.Value.Count);
                }

                chunksToProcess = ostrm.GetBuffers("WriteSymmetricMessage");

                // ensure there is at least one chunk.
                if (chunksToProcess.Count == 0)
                {
                    byte[] buffer = BufferManager.TakeBuffer(
                        SendBufferSize,
                        "WriteSymmetricMessage");

                    chunksToProcess.Add(new ArraySegment<byte>(buffer, 0, 0));
                }

                var chunksToSend = new BufferCollection(chunksToProcess.Capacity);

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

                    var strm = new MemoryStream(chunkToProcess.Array, 0, SendBufferSize);
                    using var encoder = new BinaryEncoder(strm, Quotas.MessageContext, false);

                    // check if the message needs to be aborted.
                    if (MessageLimitsExceeded(
                        isRequest,
                        messageSize + chunkToProcess.Count - headerSize,
                        ii + 1))
                    {
                        encoder.WriteUInt32(null, messageType | TcpMessageType.Abort);

                        // replace the body in the chunk with an error message.
                        using (
                            var errorEncoder = new BinaryEncoder(
                                chunkToProcess.Array,
                                chunkToProcess.Offset,
                                chunkToProcess.Count,
                                Quotas.MessageContext))
                        {
                            WriteErrorMessageBody(
                                errorEncoder,
                                isRequest
                                    ? StatusCodes.BadRequestTooLarge
                                    : StatusCodes.BadResponseTooLarge);
                            int size = errorEncoder.Close();
                            chunkToProcess = new ArraySegment<byte>(
                                chunkToProcess.Array,
                                chunkToProcess.Offset,
                                size);
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
                    count += paddingCountSize;

                    int padding = 0;

                    if (paddingCountSize > 0)
                    {
                        padding = EncryptionBlockSize - (count % EncryptionBlockSize);

                        if (padding < EncryptionBlockSize)
                        {
                            count += padding;
                        }
                    }

                    count += TcpMessageLimits.SymmetricHeaderSize;
                    count += SymmetricSignatureSize;

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

                    ArraySegment<byte> dataToSend;

                    if (SecurityMode != MessageSecurityMode.None)
                    {
                        dataToSend = new ArraySegment<byte>(
                            chunkToProcess.Array,
                            TcpMessageLimits.SymmetricHeaderSize,
                            encoder.Position - TcpMessageLimits.SymmetricHeaderSize);

                        dataToSend = EncryptAndSign(token, dataToSend, isRequest);
                    }
                    else
                    {
                        dataToSend = new ArraySegment<byte>(
                            chunkToProcess.Array,
                            0,
                            encoder.Position);
                    }

                    // add the header into chunk.
                    chunksToSend.Add(dataToSend);
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
        private ArraySegment<byte> EncryptAndSign(
            ChannelToken token,
            ArraySegment<byte> dataToEncrypt,
            bool useClientKeys)
        {
            return CryptoUtils.SymmetricEncryptAndSign(
                dataToEncrypt,
                token.SecurityPolicy,
                useClientKeys ? token.ClientEncryptingKey : token.ServerEncryptingKey,
                useClientKeys ? token.ClientInitializationVector : token.ServerInitializationVector,
                useClientKeys ? token.ClientSigningKey : token.ServerSigningKey,
                useClientKeys ? token.ClientHmac : token.ServerHmac,
                this.SecurityMode == MessageSecurityMode.Sign,
                token.TokenId,
                (uint)(m_localSequenceNumber - 1)); // already incremented to create this message. need the last one sent.
        }

        /// <summary>
        /// Decrypts and verifies a message chunk.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected ArraySegment<byte> ReadSymmetricMessage(
            ArraySegment<byte> buffer,
            bool isRequest,
            out ChannelToken token,
            out uint requestId,
            out uint sequenceNumber)
        {
            using var decoder = new BinaryDecoder(buffer, Quotas.MessageContext);
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
                m_logger.LogInformation(
                    "ChannelId {Id}: Token #{TokenId} activated forced.",
                    Id,
                    CurrentToken.TokenId);
            }

            // check for valid token.
            ChannelToken currentToken =
                CurrentToken ??
                throw ServiceResultException.Create(
                    StatusCodes.BadSecureChannelClosed,
                    "Channel{0}: Token missing to read symmetric messagee.", Id);

            // find the token.
            if (currentToken.TokenId != tokenId &&
                (PreviousToken == null || PreviousToken.TokenId != tokenId))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "Channel{0}: TokenId is not known. ChanneId={1}, TokenId={2}, CurrentTokenId={3}, PreviousTokenId={4}",
                    Id,
                    channelId,
                    tokenId,
                    currentToken.TokenId,
                    PreviousToken != null ? (int)PreviousToken.TokenId : -1);
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
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "Channel{0}: Token #{1} has expired. Lifetime={2:HH:mm:ss.fff}-{3}",
                    Id,
                    token.TokenId,
                    token.CreatedAt,
                    token.CreatedAtTickCount);
            }

            int headerSize = decoder.Position;

            var dataToProcess = new ArraySegment<byte>(
                buffer.Array,
                buffer.Offset,
                buffer.Count);

            if (SecurityMode != MessageSecurityMode.None)
            {
                dataToProcess = new ArraySegment<byte>(
                    buffer.Array,
                    buffer.Offset + headerSize,
                    buffer.Count - headerSize);

                dataToProcess = DecryptAndVerify(
                    token,
                    dataToProcess,
                    isRequest);
            }

            // extract request id and sequence number.
            sequenceNumber = decoder.ReadUInt32(null);
            requestId = decoder.ReadUInt32(null);

            headerSize += TcpMessageLimits.SequenceHeaderSize;

            // return only the data contained in the message.
            return new ArraySegment<byte>(
                dataToProcess.Array,
                dataToProcess.Offset + headerSize,
                dataToProcess.Count - headerSize);
        }

        private ArraySegment<byte> DecryptAndVerify(
            ChannelToken token,
            ArraySegment<byte> dataToDecrypt,
            bool useClientKeys)
        {
            return CryptoUtils.SymmetricDecryptAndVerify(
                dataToDecrypt,
                token.SecurityPolicy,
                useClientKeys ? token.ClientEncryptingKey : token.ServerEncryptingKey,
                useClientKeys ? token.ClientInitializationVector : token.ServerInitializationVector,
                useClientKeys ? token.ClientSigningKey : token.ServerSigningKey,
                this.SecurityMode == MessageSecurityMode.Sign,
                token.TokenId,
                (uint)m_remoteSequenceNumber);
        }

        private static readonly byte[] s_hkdfClientLabel = Encoding.UTF8.GetBytes("opcua-client");
        private static readonly byte[] s_hkdfServerLabel = Encoding.UTF8.GetBytes("opcua-server");
        private int m_signatureKeySize;
        private int m_encryptionKeySize;
    }
}
