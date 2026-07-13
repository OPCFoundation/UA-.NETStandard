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
    public partial class UaSCUaBinaryChannel : IDiagnosticsChannelMutation
    {
        /// <summary>
        /// Returns the current security token.
        /// </summary>
        protected internal ChannelToken? CurrentToken { get; private set; }

        /// <summary>
        /// Returns the current security token.
        /// </summary>
        protected internal ChannelToken? PreviousToken { get; private set; }

        /// <summary>
        /// Returns the renewed but not yet activated token.
        /// </summary>
        protected internal ChannelToken? RenewedToken { get; private set; }

        /// <summary>
        /// Replaces the current and previous tokens without re-deriving any
        /// key material. Used exclusively by the offline diagnostic
        /// decoder (Opc.Ua.Pcap) which reconstitutes
        /// <see cref="ChannelToken"/> instances directly from a keylog and
        /// must NOT trigger the live token-activation pipeline.
        /// </summary>
        /// <remarks>
        /// This entry point is intentionally <see langword="internal"/>;
        /// production code paths must continue to go through
        /// <see cref="ActivateToken"/>.
        /// </remarks>
        [Obsolete(
            "Use IDiagnosticsChannelMutation.LoadTokensForOfflineDecode via cast; " +
            "this method will be removed in the next major version.",
            error: false)]
        internal void OfflineLoadTokens(ChannelToken? current, ChannelToken? previous)
        {
            ((IDiagnosticsChannelMutation)this).LoadTokensForOfflineDecode(current, previous);
        }

        void IDiagnosticsChannelMutation.LoadTokensForOfflineDecode(ChannelToken? current, ChannelToken? previous)
        {
            EnsureDiagnosticsCallerIsAllowed();

            PreviousToken?.Dispose();
            PreviousToken = previous;
            CurrentToken = current;
            RenewedToken = null;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void EnsureDiagnosticsCallerIsAllowed()
        {
            const string coreAssemblyName = "Opc.Ua.Core";
            const string diagnosticsAssemblyName = "Opc.Ua.Core.Diagnostics";
            var stackTrace = new System.Diagnostics.StackTrace(skipFrames: 1, fNeedFileInfo: false);

            for (int ii = 0; ii < stackTrace.FrameCount; ii++)
            {
                System.Diagnostics.StackFrame? frame = stackTrace.GetFrame(ii);
                string? assemblyName = GetStackFrameAssemblyName(frame);
                if (assemblyName is null or coreAssemblyName)
                {
                    continue;
                }

                if (assemblyName == diagnosticsAssemblyName || assemblyName.EndsWith(".Tests", StringComparison.Ordinal))
                {
                    return;
                }

                throw new InvalidOperationException(
                    "LoadTokensForOfflineDecode may only be called from the Opc.Ua.Core.Diagnostics assembly.");
            }

            throw new InvalidOperationException(
                "LoadTokensForOfflineDecode may only be called from the Opc.Ua.Core.Diagnostics assembly.");
        }

        private static string? GetStackFrameAssemblyName(System.Diagnostics.StackFrame? frame)
        {
            if (frame == null)
            {
                return null;
            }

#if NET10_0_OR_GREATER
            var methodInfo =
                System.Diagnostics.DiagnosticMethodInfo.Create(frame);
            string? assemblyName = methodInfo?.DeclaringAssemblyName;
            int separatorIndex = assemblyName?.IndexOf(',', StringComparison.Ordinal) ?? -1;
            return separatorIndex > 0 ? assemblyName![..separatorIndex] : assemblyName;
#else
            Type? declaringType = frame.GetMethod()?.DeclaringType;
            return declaringType?.Assembly.GetName().Name;
#endif
        }

        /// <summary>
        /// Resets the offline decoder's per-direction sequence-number
        /// tracking so that subsequent calls to
        /// <see cref="ReadSymmetricMessage"/> start from a known baseline.
        /// </summary>
        /// <remarks>
        /// Intended for the offline diagnostic decoder. The live channels
        /// manage <c>m_remoteSequenceNumber</c> through
        /// <see cref="VerifySequenceNumber"/>.
        /// </remarks>
        internal void OfflineResetRemoteSequenceNumber(uint sequenceNumber)
        {
            ResetSequenceNumber(sequenceNumber);
        }

        /// <summary>
        /// Called when the token changes.
        /// </summary>
        /// <remarks>
        /// The base channel exposes this as an in-process callback rather
        /// than a multicast event so derived channels and transports can
        /// project token transitions to their own preferred event shape.
        /// <see cref="UaSCUaBinaryTransportChannel"/> bridges it to the
        /// public <see cref="ISecureChannel.OnTokenActivated"/> event on
        /// the client side; <see cref="TcpListenerChannel"/> bridges it to
        /// the public <c>OnTokenActivated</c> event on the server side.
        /// </remarks>
        protected internal Action<ChannelToken?, ChannelToken?>? TokenActivatedCallback { get; set; }

        /// <summary>
        /// Creates a new token.
        /// </summary>
        protected ChannelToken CreateToken()
        {
            var token = new ChannelToken
            {
                ChannelId = Id,
                TokenId = 0,
                CreatedAt = TimeProvider.GetUtcNow().UtcDateTime,
                CreatedAtTimestamp = TimeProvider.GetTimestamp(),
                Lifetime = Quotas.SecurityTokenLifetime
            };

            if (m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.UaSCChannelLog9(
                    Id,
                    token.CreatedAt,
                    token.CreatedAtTimestamp,
                    token.Lifetime);
            }

            return token;
        }

        /// <summary>
        /// Activates a new token.
        /// </summary>
        protected void ActivateToken(ChannelToken token)
        {
            // compute the keys for the token.
            ComputeKeys(token);

            PreviousToken?.Dispose();
            PreviousToken = CurrentToken;
            CurrentToken = token;
            RenewedToken = null;

            TokenActivatedCallback?.Invoke(token, PreviousToken);

            if (m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.UaSCChannelLog10(
                    Id,
                    token.TokenId,
                    token.CreatedAt,
                    token.CreatedAtTimestamp,
                    token.Lifetime);
            }
        }

        /// <summary>
        /// Sets the renewed token.
        /// </summary>
        protected void SetRenewedToken(ChannelToken token)
        {
            RenewedToken?.Dispose();
            RenewedToken = token;
            if (m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.UaSCChannelLog11(
                    Id,
                    token.TokenId,
                    token.CreatedAt,
                    token.CreatedAtTimestamp,
                    token.Lifetime);
            }
        }

        /// <summary>
        /// Discards the tokens.
        /// </summary>
        protected void DiscardTokens()
        {
            PreviousToken?.Dispose();
            PreviousToken = null;
            CurrentToken?.Dispose();
            CurrentToken = null;
            RenewedToken?.Dispose();
            RenewedToken = null;

            TokenActivatedCallback?.Invoke(null, null);
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
            SecurityPolicyInfo policy = SecurityPolicy!;
            using HMAC hmac = Utils.CreateHMAC(algorithmName, secret);

            int length = m_signatureKeySize + m_encryptionKeySize + EncryptionBlockSize;

            if (!isServer && policy.SecureChannelEnhancements)
            {
                length += hmac.HashSize / 8;
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
                token.ServerHmac = policy.CreateSignatureHmac(signingKey);
            }
            else
            {
                token.ClientSigningKey = signingKey;
                token.ClientEncryptingKey = encryptingKey;
                token.ClientInitializationVector = iv;
                token.ClientHmac = policy.CreateSignatureHmac(signingKey);
            }
        }

        private void DeriveKeysWithHKDF(
            ChannelToken token,
            byte[] salt,
            bool isServer,
            int length)
        {
            SecurityPolicyInfo tokenPolicy = token.SecurityPolicy!;
            byte[] keyData = m_localNonce!.DeriveKeyData(
                token.Secret!,
                salt,
                tokenPolicy.KeyDerivationAlgorithm,
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
                token.ServerHmac = tokenPolicy.CreateSignatureHmac(signingKey);
            }
            else
            {
                token.ClientSigningKey = signingKey;
                token.ClientEncryptingKey = encryptingKey;
                token.ClientInitializationVector = iv;
                token.ClientHmac = tokenPolicy.CreateSignatureHmac(signingKey);
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

            byte[]? serverSecret = token.ServerNonce;
            byte[]? clientSecret = token.ClientNonce;
            // SecurityPolicy was just assigned above and is non-null when SecurityMode != None.
            SecurityPolicyInfo tokenPolicy = token.SecurityPolicy!;

            switch (tokenPolicy.KeyDerivationAlgorithm)
            {
                case KeyDerivationAlgorithm.HKDFSha256:
                case KeyDerivationAlgorithm.HKDFSha384:
                    token.Secret = m_localNonce!.GenerateSecret(m_remoteNonce!, token.PreviousSecret);

                    byte[] clientSalt = Utils.Append(
                        BitConverter.GetBytes((ushort)tokenPolicy.ClientKeyDataLength),
                        s_hkdfClientLabel,
                        clientSecret,
                        serverSecret);

                    DeriveKeysWithHKDF(token, clientSalt, false, tokenPolicy.ClientKeyDataLength);

                    byte[] serverSalt = Utils.Append(
                        BitConverter.GetBytes((ushort)tokenPolicy.ServerKeyDataLength),
                        s_hkdfServerLabel,
                        serverSecret,
                        clientSecret);

                    DeriveKeysWithHKDF(token, serverSalt, true, tokenPolicy.ServerKeyDataLength);
                    break;
                default:
                    HashAlgorithmName algorithmName = tokenPolicy.GetKeyDerivationHashAlgorithmName();
                    DeriveKeysWithPSHA(algorithmName, serverSecret!, clientSecret!, token, false);
                    DeriveKeysWithPSHA(algorithmName, clientSecret!, serverSecret!, token, true);
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
            BufferCollection? chunksToProcess = null;

            try
            {
                // calculate chunk sizes.
                int maxCipherTextSize = SendBufferSize - TcpMessageLimits.SymmetricHeaderSize;
                int maxCipherBlocks = maxCipherTextSize / EncryptionBlockSize;
                int maxPlainTextSize = maxCipherBlocks * EncryptionBlockSize;

                int signatureSize = SymmetricSignatureSize;
                // token.SecurityPolicy is set in ComputeKeys before the channel sends messages.
                int paddingCountSize =
                    SecurityMode != MessageSecurityMode.SignAndEncrypt || token.SecurityPolicy!.NoSymmetricEncryptionPadding
                    ? 0
                    : (EncryptionBlockSize > byte.MaxValue ? 2 : 1);

                int maxPayloadSize =
                    maxPlainTextSize -
                    signatureSize -
                    TcpMessageLimits.SequenceHeaderSize -
                    paddingCountSize;

                const int headerSize = TcpMessageLimits.SymmetricHeaderSize +
                    TcpMessageLimits.SequenceHeaderSize;

                // write the body to stream.
                using var ostrm = new ArraySegmentStream(
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
                    // BufferManager-backed ArraySegment is always created with a non-null backing array.
                    encoder.WriteRawBytes(
                        rawBytes.Value.GetArray(),
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
                    // BufferManager-backed segment always has a non-null backing array.
                    byte[] chunkArray = chunkToProcess.GetArray();

                    // nothing more to do if limits exceeded.
                    if (limitsExceeded)
                    {
                        BufferManager.ReturnBuffer(chunkArray, "WriteSymmetricMessage");
                        continue;
                    }

#pragma warning disable CA2000 // Stream is disposed by the BinaryEncoder (leaveOpen: false)
                    var strm = new MemoryStream(chunkArray, 0, SendBufferSize);
                    using var encoder = new BinaryEncoder(strm, Quotas.MessageContext, false);
#pragma warning restore CA2000

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
                                chunkArray,
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
                                chunkArray,
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
                    count += signatureSize;

                    int padding = 0;

                    if (paddingCountSize > 0)
                    {
                        padding = EncryptionBlockSize - (count % EncryptionBlockSize);

                        if (padding == EncryptionBlockSize)
                        {
                            padding = 0;
                        }

                        if (padding > 0)
                        {
                            count += padding;
                        }
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

                    ArraySegment<byte> dataToSend;

                    if (SecurityMode != MessageSecurityMode.None)
                    {
                        dataToSend = new ArraySegment<byte>(
                            chunkArray,
                            TcpMessageLimits.SymmetricHeaderSize,
                            encoder.Position - TcpMessageLimits.SymmetricHeaderSize);

                        dataToSend = EncryptAndSign(token, dataToSend, isRequest);
                    }
                    else
                    {
                        dataToSend = new ArraySegment<byte>(
                            chunkArray,
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
            // The keys and security policy are populated in ComputeKeys before send/receive.
            return CryptoUtils.SymmetricEncryptAndSign(
                dataToEncrypt,
                token.SecurityPolicy!,
                (useClientKeys ? token.ClientEncryptingKey : token.ServerEncryptingKey)!,
                (useClientKeys ? token.ClientInitializationVector : token.ServerInitializationVector)!,
                useClientKeys ? token.ClientSigningKey : token.ServerSigningKey,
                useClientKeys ? token.ClientHmac : token.ServerHmac,
                SecurityMode == MessageSecurityMode.Sign,
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
            else if (RenewedToken != null &&
                CurrentToken != null &&
                CurrentToken.IsActivationRequired(TimeProvider))
            {
                ActivateToken(RenewedToken);
                m_logger.UaSCChannelLog12(Id, CurrentToken.TokenId);
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
            if (token.IsExpired(TimeProvider))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "Channel{0}: Token #{1} has expired. Lifetime={2:HH:mm:ss.fff}-{3}",
                    Id,
                    token.TokenId,
                    token.CreatedAt,
                    token.CreatedAtTimestamp);
            }

            int headerSize = decoder.Position;

            // ArraySegment.Array is non-null because the buffer comes from BufferManager.
            byte[] bufferArray = buffer.GetArray();
            var dataToProcess = new ArraySegment<byte>(
                bufferArray,
                buffer.Offset,
                buffer.Count);

            if (SecurityMode != MessageSecurityMode.None)
            {
                dataToProcess = new ArraySegment<byte>(
                    bufferArray,
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
                dataToProcess.GetArray(),
                dataToProcess.Offset + headerSize,
                dataToProcess.Count - headerSize);
        }

        private ArraySegment<byte> DecryptAndVerify(
            ChannelToken token,
            ArraySegment<byte> dataToDecrypt,
            bool useClientKeys)
        {
            // Keys/security policy are populated in ComputeKeys before send/receive.
            return CryptoUtils.SymmetricDecryptAndVerify(
                dataToDecrypt,
                token.SecurityPolicy!,
                (useClientKeys ? token.ClientEncryptingKey : token.ServerEncryptingKey)!,
                (useClientKeys ? token.ClientInitializationVector : token.ServerInitializationVector)!,
                useClientKeys ? token.ClientSigningKey : token.ServerSigningKey,
                SecurityMode == MessageSecurityMode.Sign,
                token.TokenId,
                m_remoteSequenceNumber);
        }

        private static readonly byte[] s_hkdfClientLabel = Encoding.UTF8.GetBytes("opcua-client");
        private static readonly byte[] s_hkdfServerLabel = Encoding.UTF8.GetBytes("opcua-server");
        private int m_signatureKeySize;
        private int m_encryptionKeySize;
    }
}
