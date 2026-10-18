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
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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
            // compute the keys for the token, unless ReadSymmetricMessage already
            // did so to verify the first chunk secured with it.
            if (!ReferenceEquals(token, m_renewedTokenWithKeys))
            {
                ComputeKeys(token);
            }
            m_renewedTokenWithKeys = null;

            ChannelToken? previousToken;

            // the writer secures messages with the current token under the send
            // order lock; swapping it there keeps a token from being disposed while
            // a chunk is secured with it.
            using (SendOrder.Enter())
            {
                // a pending renewal superseded by another token (reconnect) is dropped.
                if (RenewedToken != null && !ReferenceEquals(RenewedToken, token))
                {
                    RenewedToken.Dispose();
                }

                PreviousToken?.Dispose();
                PreviousToken = CurrentToken;
                CurrentToken = token;
                RenewedToken = null;
                previousToken = PreviousToken;
            }

            TokenActivatedCallback?.Invoke(token, previousToken);

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
            // compute the keys (and the secret a further renewal chains from) now,
            // while the nonces of this renewal are installed: a second renewal
            // before any message secured with this token replaces them.
            ComputeKeys(token);

            // a renewal still pending here was answered before the client sent this one
            // on the same connection, so the client already secures its requests with it.
            // Make it current (the token before it stays accepted as the previous one)
            // instead of dropping it: OPC 10000-6 6.7.4 requires the server to accept it
            // until it expires or a message secured with the newest token arrives.
            if (RenewedToken != null)
            {
                ActivateToken(RenewedToken);
            }

            RenewedToken = token;
            m_renewedTokenWithKeys = token;
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
        /// Activates the pending renewed token once the current token is close to expiry.
        /// </summary>
        /// <remarks>
        /// A server keeps securing its messages with the current token until the client
        /// uses the renewed one, but only until the current token expires (OPC 10000-4
        /// 5.6.2.1). A client that sends nothing - one waiting on outstanding Publish
        /// requests - would otherwise receive responses secured with an expired token.
        /// </remarks>
        protected void ActivateRenewedTokenIfDue()
        {
            ChannelToken? renewedToken = RenewedToken;
            if (renewedToken != null &&
                CurrentToken != null &&
                CurrentToken.IsActivationRequired(TimeProvider))
            {
                ActivateToken(renewedToken);
                m_logger.UaSCChannelLog12(Id, CurrentToken.TokenId);
            }
        }

        /// <summary>
        /// Whether <see cref="ActivateRenewedTokenIfDue"/> would activate a token.
        /// </summary>
        /// <remarks>
        /// Read without the gate by a sender deciding whether it may skip it. The
        /// answer can be stale by the time the message is sealed, which only moves
        /// the activation to the next message or the next received chunk.
        /// </remarks>
        private protected bool IsRenewedTokenActivationDue =>
            RenewedToken != null &&
            CurrentToken is ChannelToken currentToken &&
            currentToken.IsActivationRequired(TimeProvider);

        /// <summary>
        /// Discards the tokens.
        /// </summary>
        protected void DiscardTokens()
        {
            // see ActivateToken.
            using (SendOrder.Enter())
            {
                PreviousToken?.Dispose();
                PreviousToken = null;
                CurrentToken?.Dispose();
                CurrentToken = null;
                RenewedToken?.Dispose();
                RenewedToken = null;
            }

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
            SecurityPolicyInfo info = SecurityPolicyRegistry.GetInfo(SecurityPolicyUri)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unsupported security policy: {0}",
                    SecurityPolicyUri);

            SymmetricSignatureSize = info.SymmetricSignatureLength;
            m_signatureKeySize = info.DerivedSignatureKeyLength;
            m_encryptionKeySize = info.SymmetricEncryptionKeyLength;
            EncryptionBlockSize = info.InitializationVectorLength != 0 ? info.InitializationVectorLength : 1;

            // Resolved once, here, and held for the lifetime of the channel. Both
            // stay null in the default configuration, which is what keeps the per
            // message path free of any interface dispatch.
            m_symmetricProvider = CryptoProviderFacets.ResolveSymmetric(
                Quotas.CryptoProviders, SecurityPolicyUri);
            m_keyDerivationProvider = CryptoProviderFacets.ResolveKeyDerivation(
                Quotas.CryptoProviders, SecurityPolicyUri);
        }

        private void DeriveKeysWithPSHA(
            HashAlgorithmName algorithmName,
            byte[] secret,
            byte[] seed,
            ChannelToken token,
            bool isServer)
        {
            SecurityPolicyInfo policy = SecurityPolicy!;

            int length = m_signatureKeySize + m_encryptionKeySize + EncryptionBlockSize;

            byte[] output;

            if (m_keyDerivationProvider != null &&
                m_keyDerivationProvider.Supports(policy.KeyDerivationAlgorithm))
            {
                if (!isServer && policy.SecureChannelEnhancements)
                {
                    length += GetHashLength(algorithmName);
                }

                output = new byte[length];
                CryptoProviderOutput.Stamp(output);
                m_keyDerivationProvider.DeriveKey(
                    policy.KeyDerivationAlgorithm, secret, seed, output);
                CryptoProviderOutput.Verify(
                    output, "key derivation", m_keyDerivationProvider);
            }
            else
            {
                using HMAC hmac = Utils.CreateHMAC(algorithmName, secret);

                if (!isServer && policy.SecureChannelEnhancements)
                {
                    length += hmac.HashSize / 8;
                }

                output = Utils.PSHA(hmac, null, seed, 0, length);
            }

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

        private static int GetHashLength(HashAlgorithmName algorithmName)
        {
            if (algorithmName == HashAlgorithmName.SHA384)
            {
                return 48;
            }

            if (algorithmName == HashAlgorithmName.SHA1)
            {
                return 20;
            }

            return 32;
        }

        private void DeriveKeysWithHKDF(
            ChannelToken token,
            byte[] salt,
            bool isServer,
            int length,
            bool signingKeyOnly)
        {
            SecurityPolicyInfo tokenPolicy = token.SecurityPolicy!;
            byte[] keyData;

            if (m_keyDerivationProvider != null &&
                m_keyDerivationProvider.Supports(tokenPolicy.KeyDerivationAlgorithm))
            {
                keyData = new byte[length];
                CryptoProviderOutput.Stamp(keyData);
                m_keyDerivationProvider.DeriveKey(
                    tokenPolicy.KeyDerivationAlgorithm, token.Secret!, salt, keyData);
                CryptoProviderOutput.Verify(
                    keyData, "key derivation", m_keyDerivationProvider);
            }
            else
            {
                keyData = m_localNonce!.DeriveKeyData(
                    token.Secret!,
                    salt,
                    tokenPolicy.KeyDerivationAlgorithm,
                    length);
            }

            // a Sign only channel without AuthenticatedEncryption derives no
            // encrypting key or IV; neither is used to sign.
            byte[] signingKey = new byte[m_signatureKeySize];
            byte[] encryptingKey = [];
            byte[] iv = [];

            Buffer.BlockCopy(keyData, 0, signingKey, 0, signingKey.Length);

            if (!signingKeyOnly)
            {
                encryptingKey = new byte[m_encryptionKeySize];
                iv = new byte[EncryptionBlockSize];
                Buffer.BlockCopy(keyData, m_signatureKeySize, encryptingKey, 0, encryptingKey.Length);
                Buffer.BlockCopy(keyData, m_signatureKeySize + m_encryptionKeySize, iv, 0, iv.Length);
            }

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
            token.SecurityPolicy = SecurityPolicyRegistry.GetInfo(SecurityPolicyUri);

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
                    // OPC 10000-6 6.8.1 Step 2: a renewal chains the IKM of the current
                    // keys into the new IKM only when SecureChannelEnhancements = TRUE.
                    token.Secret = m_localNonce!.GenerateSecret(
                        m_remoteNonce!,
                        tokenPolicy.SecureChannelEnhancements ? token.PreviousSecret : null);

                    // OPC 10000-6 6.8.1: when not using AuthenticatedEncryption with Sign
                    // only, the EncryptionKeyLength and InitializationVectorLength are 0 in
                    // the calculation of L, which is part of the salt.
                    bool signingKeyOnly =
                        SecurityMode == MessageSecurityMode.Sign &&
                        !tokenPolicy.NoSymmetricEncryptionPadding;
                    int clientKeyDataLength = signingKeyOnly
                        ? tokenPolicy.DerivedSignatureKeyLength
                        : tokenPolicy.ClientKeyDataLength;
                    int serverKeyDataLength = signingKeyOnly
                        ? tokenPolicy.DerivedSignatureKeyLength
                        : tokenPolicy.ServerKeyDataLength;

                    byte[] clientSalt = Utils.Append(
                        BitConverter.GetBytes((ushort)clientKeyDataLength),
                        s_hkdfClientLabel,
                        clientSecret,
                        serverSecret);

                    DeriveKeysWithHKDF(token, clientSalt, false, clientKeyDataLength, signingKeyOnly);

                    byte[] serverSalt = Utils.Append(
                        BitConverter.GetBytes((ushort)serverKeyDataLength),
                        s_hkdfServerLabel,
                        serverSecret,
                        clientSecret);

                    DeriveKeysWithHKDF(token, serverSalt, true, serverKeyDataLength, signingKeyOnly);
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
        /// <remarks>
        /// Encodes and seals in one step. The sequence numbers are taken under
        /// <see cref="SendOrder"/>, but the caller queues the chunks after it is
        /// released, so this is only correct on a channel that queues no unsealed
        /// messages (<see cref="BeginWriteSymmetricMessage"/>) and serialises its
        /// numbered writes otherwise, as the client channel does under its gate.
        /// </remarks>
        protected BufferCollection WriteSymmetricMessage(
            uint messageType,
            uint requestId,
            ChannelToken token,
            object messageBody,
            bool isRequest,
            out bool limitsExceeded)
        {
            EncodedSymmetricMessage encoded = EncodeSymmetricMessage(
                messageType,
                requestId,
                messageBody,
                isRequest,
                out limitsExceeded);

            using (SendOrder.Enter())
            {
                return SealSymmetricMessage(encoded, token);
            }
        }

        /// <summary>
        /// A symmetric message encoded into chunks whose headers, sequence numbers
        /// and security are not written yet.
        /// </summary>
        /// <remarks>
        /// Encoding needs nothing that is ordered per channel, so any thread can do
        /// it without the gate. Sealing takes the token and the sequence numbers and
        /// is done by the writer, in queue order (see <see cref="SendOrder"/>).
        /// </remarks>
        private protected readonly struct EncodedSymmetricMessage
        {
            public EncodedSymmetricMessage(
                uint messageType,
                uint requestId,
                bool isRequest,
                bool aborted,
                BufferCollection chunks)
            {
                MessageType = messageType;
                RequestId = requestId;
                IsRequest = isRequest;
                Aborted = aborted;
                Chunks = chunks;
            }

            /// <summary>
            /// The message type without the chunk type.
            /// </summary>
            public uint MessageType { get; }

            /// <summary>
            /// The request the message belongs to.
            /// </summary>
            public uint RequestId { get; }

            /// <summary>
            /// Whether the message is a request (client keys secure it).
            /// </summary>
            public bool IsRequest { get; }

            /// <summary>
            /// Whether the last chunk is an abort chunk replacing the rest of the
            /// message because it exceeded the limits.
            /// </summary>
            public bool Aborted { get; }

            /// <summary>
            /// The chunks, each holding its body after the reserved header space.
            /// Owned by the message until sealed.
            /// </summary>
            public BufferCollection Chunks { get; }
        }

        /// <summary>
        /// Encodes a message into chunks without securing them.
        /// </summary>
        /// <param name="messageType">The message type without the chunk type.</param>
        /// <param name="requestId">The request the message belongs to.</param>
        /// <param name="messageBody">An encodeable, or the raw bytes to send.</param>
        /// <param name="isRequest">Whether the message is a request.</param>
        /// <param name="limitsExceeded">Set when the message was cut off with an
        /// abort chunk because it exceeds the negotiated limits.</param>
        /// <returns>The encoded message. The caller owns its chunks.</returns>
        private protected EncodedSymmetricMessage EncodeSymmetricMessage(
            uint messageType,
            uint requestId,
            object messageBody,
            bool isRequest,
            out bool limitsExceeded)
        {
            limitsExceeded = false;
            bool success = false;
            BufferCollection? chunksToProcess = null;

            try
            {
                // SendBufferSize is normalized when the channel is created or negotiated.
                int sendBufferSize = SendBufferSize;

                // calculate chunk sizes.
                int maxCipherTextSize = sendBufferSize - TcpMessageLimits.SymmetricHeaderSize;
                int maxCipherBlocks = maxCipherTextSize / EncryptionBlockSize;
                int maxPlainTextSize = maxCipherBlocks * EncryptionBlockSize;

                int signatureSize = SymmetricSignatureSize;
                int paddingCountSize = GetPaddingCountSize();

                int maxPayloadSize =
                    maxPlainTextSize -
                    signatureSize -
                    TcpMessageLimits.SequenceHeaderSize -
                    paddingCountSize;

                const int headerSize = TcpMessageLimits.SymmetricHeaderSize +
                    TcpMessageLimits.SequenceHeaderSize;

                // write the body to chunks.
                chunksToProcess = EncodeMessageBody(messageBody, sendBufferSize, headerSize, maxPayloadSize);

                // ensure there is at least one chunk.
                if (chunksToProcess.Count == 0)
                {
                    byte[] buffer = BufferManager.TakeBuffer(
                        sendBufferSize,
                        "WriteSymmetricMessage");

                    chunksToProcess.Add(new ArraySegment<byte>(buffer, headerSize, 0));
                }

                int messageSize = 0;
                int keptCount = chunksToProcess.Count;

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

                    // check if the message needs to be aborted. The segment holds
                    // only the body (it starts after the headers), which is what
                    // the receiver counts against MaxMessageSize.
                    if (MessageLimitsExceeded(
                        isRequest,
                        messageSize + chunkToProcess.Count,
                        ii + 1))
                    {
                        // Replace the body in the chunk with an error message.
                        // The encoder is bounded by the room the chunk has for a
                        // body, not by what the chunk currently holds: an error
                        // body is at least eight bytes and the offending chunk
                        // can be shorter than that.
                        using (
                            var errorEncoder = new BinaryEncoder(
                                chunkArray,
                                chunkToProcess.Offset,
                                maxPayloadSize,
                                Quotas.MessageContext))
                        {
                            WriteErrorMessageBody(
                                errorEncoder,
                                isRequest
                                    ? StatusCodes.BadRequestTooLarge
                                    : StatusCodes.BadResponseTooLarge);
                            int size = errorEncoder.Close();
                            chunksToProcess[ii] = new ArraySegment<byte>(
                                chunkArray,
                                chunkToProcess.Offset,
                                size);
                        }

                        limitsExceeded = true;
                        keptCount = ii + 1;
                    }

                    // update message size count.
                    messageSize += chunksToProcess[ii].Count;
                }

                // the chunks after an abort chunk were returned above.
                if (keptCount < chunksToProcess.Count)
                {
                    chunksToProcess.RemoveRange(keptCount, chunksToProcess.Count - keptCount);
                }

                // A small message that will wait in the write queue (writes are already
                // queued) moves to a buffer of its own size: a deep pipeline of
                // responses that each hold a send-buffer-sized array exhausts the
                // buffer pool. A message that is sent right away skips the copy. The
                // room after the body covers the padding count, the padding and the
                // signature that sealing adds.
                if (chunksToProcess.Count == 1 && Volatile.Read(ref m_activeWriteRequests) > 0)
                {
                    ArraySegment<byte> body = chunksToProcess[0];
                    int required = headerSize + body.Count + paddingCountSize +
                        signatureSize + (2 * EncryptionBlockSize);
                    if (required <= sendBufferSize / kSmallMessageFraction)
                    {
                        byte[] large = body.GetArray();
                        byte[] small = BufferManager.TakeBuffer(required, "WriteSymmetricMessage");
                        Buffer.BlockCopy(large, body.Offset, small, headerSize, body.Count);
                        chunksToProcess[0] = new ArraySegment<byte>(small, headerSize, body.Count);
                        BufferManager.ReturnBuffer(large, "WriteSymmetricMessage");
                    }
                }

                var encoded = new EncodedSymmetricMessage(
                    messageType,
                    requestId,
                    isRequest,
                    limitsExceeded,
                    chunksToProcess);

                // ensure the buffers don't get cleaned up on exit.
                success = true;
                return encoded;
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
        /// Encodes the body of a symmetric message to the message stream of the thread.
        /// </summary>
        /// <returns>The chunks holding the body. The caller owns them.</returns>
        private BufferCollection EncodeMessageBody(
            object messageBody,
            int sendBufferSize,
            int headerSize,
            int maxPayloadSize)
        {
            var messageStream = MessageStream.Rent(
                BufferManager,
                sendBufferSize,
                headerSize,
                maxPayloadSize);
            ArraySegmentStream ostrm = messageStream.Stream;
            BufferCollection chunks;
            try
            {
                // check for encodeable body.
                if (messageBody is IEncodeable encodeable)
                {
                    using var encoder = new BinaryEncoder(messageStream.Writer, Quotas.MessageContext);
                    encoder.EncodeMessage(encodeable, encodeable.TypeId);
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

                chunks = ostrm.GetBuffers("WriteSymmetricMessage");
            }
            catch
            {
                // a body that failed to encode returns the buffers the stream holds, and the
                // stream is not used again.
                ostrm.ReleaseBuffers();
                throw;
            }

            // the stream is empty again: the next message of the thread can use it.
            messageStream.Return();
            return chunks;
        }

        /// <summary>
        /// The stream, and the writer over it, a thread encodes the bodies of symmetric
        /// messages to; every message gets an encoder of its own that borrows the writer.
        /// </summary>
        /// <remarks>
        /// Encoding is synchronous, so a thread encodes one message at a time; it takes the
        /// stream for a message and gives it back once the stream handed its buffers over
        /// (<see cref="ArraySegmentStream.GetBuffers"/>), so the encoded message references
        /// neither. A message encoded while the thread's stream is taken gets a new one.
        /// </remarks>
        private sealed class MessageStream
        {
            private MessageStream(BufferManager bufferManager, int bufferSize, int start, int count)
            {
                Stream = new ArraySegmentStream(bufferManager, bufferSize, start, count);
                Writer = new BinaryWriter(Stream, Encoding.UTF8, true);
            }

            public ArraySegmentStream Stream { get; }

            public BinaryWriter Writer { get; }

            public static MessageStream Rent(BufferManager bufferManager, int bufferSize, int start, int count)
            {
                MessageStream? stream = t_messageStream;
                if (stream == null)
                {
                    return new MessageStream(bufferManager, bufferSize, start, count);
                }
                t_messageStream = null;
                stream.Stream.Reset(bufferManager, bufferSize, start, count);
                return stream;
            }

            public void Return()
            {
                t_messageStream = this;
            }

            [ThreadStatic]
            private static MessageStream? t_messageStream;
        }

        /// <summary>
        /// Decodes a message in one buffer, without a stream over it.
        /// </summary>
        /// <typeparam name="T">The type of the message.</typeparam>
        private protected static T DecodeMessage<T>(ArraySegment<byte> buffer, IServiceMessageContext context)
            where T : IEncodeable
        {
            using var decoder = new BinaryDecoder(buffer, context);
            return decoder.DecodeMessage<T>();
        }

        /// <summary>
        /// Writes the headers and sequence numbers of an encoded message and
        /// secures its chunks with the token.
        /// </summary>
        /// <param name="encoded">The message. Its chunks are consumed: returned
        /// as the result, or released when sealing fails.</param>
        /// <param name="token">The token that secures the message.</param>
        /// <returns>The chunks to send.</returns>
        /// <remarks>
        /// The caller holds <see cref="SendOrder"/>, which is what keeps the
        /// sequence numbers in the order the chunks reach the wire and keeps the
        /// token from being disposed while it is in use.
        /// </remarks>
        private protected BufferCollection SealSymmetricMessage(
            EncodedSymmetricMessage encoded,
            ChannelToken token)
        {
            BufferCollection chunksToProcess = encoded.Chunks;
            bool success = false;

            try
            {
                int signatureSize = SymmetricSignatureSize;
                int paddingCountSize = GetPaddingCountSize();

                const int headerSize = TcpMessageLimits.SymmetricHeaderSize +
                    TcpMessageLimits.SequenceHeaderSize;

                var chunksToSend = new BufferCollection(chunksToProcess.Count);
                int lastChunk = chunksToProcess.Count - 1;

                for (int ii = 0; ii < chunksToProcess.Count; ii++)
                {
                    ArraySegment<byte> chunkToProcess = chunksToProcess[ii];
                    // BufferManager-backed segment always has a non-null backing array.
                    byte[] chunkArray = chunkToProcess.GetArray();

                    // The headers are fixed-size little-endian fields in front of the
                    // body, written directly rather than through an encoder per chunk.
                    uint chunkType;
                    if (ii < lastChunk)
                    {
                        chunkType = encoded.MessageType | TcpMessageType.Intermediate;
                    }
                    else if (encoded.Aborted)
                    {
                        chunkType = encoded.MessageType | TcpMessageType.Abort;
                    }
                    else
                    {
                        chunkType = encoded.MessageType | TcpMessageType.Final;
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

                    Span<byte> header = chunkArray.AsSpan(0, headerSize);
                    BinaryPrimitives.WriteUInt32LittleEndian(header, chunkType);
                    BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)count);
                    BinaryPrimitives.WriteUInt32LittleEndian(header[8..], ChannelId);
                    BinaryPrimitives.WriteUInt32LittleEndian(header[12..], token.TokenId);

                    uint sequenceNumber = GetNewSequenceNumber();
                    BinaryPrimitives.WriteUInt32LittleEndian(header[16..], sequenceNumber);
                    BinaryPrimitives.WriteUInt32LittleEndian(header[20..], encoded.RequestId);

                    // the headers are followed by the body.
                    int position = headerSize + chunkToProcess.Count;

                    ArraySegment<byte> dataToSend;

                    if (SecurityMode != MessageSecurityMode.None)
                    {
                        dataToSend = new ArraySegment<byte>(
                            chunkArray,
                            TcpMessageLimits.SymmetricHeaderSize,
                            position - TcpMessageLimits.SymmetricHeaderSize);

                        // the number was taken above under the same lock, so the
                        // last one sent is the one before this chunk's.
                        dataToSend = EncryptAndSign(
                            token,
                            dataToSend,
                            encoded.IsRequest,
                            LastSentSequenceNumber);
                    }
                    else
                    {
                        dataToSend = new ArraySegment<byte>(
                            chunkArray,
                            0,
                            position);
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
                    chunksToProcess.Release(BufferManager, "WriteSymmetricMessage");
                }
            }
        }

        /// <summary>
        /// The size of the padding count field of an encrypted chunk.
        /// </summary>
        private int GetPaddingCountSize()
        {
            return SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                SecurityPolicy!.NoSymmetricEncryptionPadding
                ? 0
                : (EncryptionBlockSize > byte.MaxValue ? 2 : 1);
        }

        private ArraySegment<byte> EncryptAndSign(
            ChannelToken token,
            ArraySegment<byte> dataToEncrypt,
            bool useClientKeys,
            uint lastSequenceNumber)
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
                lastSequenceNumber,
                m_symmetricProvider,
                useClientKeys ? token.ClientCiphers : token.ServerCiphers);
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
            // The headers are fixed-size little-endian fields, read directly
            // rather than through a decoder per chunk.
            _ = ReadHeaderField(buffer, 0); // message type
            _ = ReadHeaderField(buffer, 4); // message size
            uint channelId = ReadHeaderField(buffer, 8);
            uint tokenId = ReadHeaderField(buffer, 12);

            // ensure the channel is valid.
            if (channelId != ChannelId)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "SecureChannelId is not known. ChanneId={0}, CurrentChannelId={1}",
                    channelId,
                    ChannelId);
            }

            // check for a message secured with the new token. The token id in
            // the header is not authenticated, so the new token only becomes
            // current once the chunk has been verified with it (below).
            ChannelToken? renewedToken = RenewedToken;
            ChannelToken? tokenToActivate = null;
            if (renewedToken != null && renewedToken.TokenId == tokenId)
            {
                tokenToActivate = renewedToken;
            }

            // check if activation of the new token should be forced.
            else
            {
                ActivateRenewedTokenIfDue();
            }

            if (tokenToActivate != null)
            {
                // the keys are needed to verify the chunk; computed once per token.
                if (!ReferenceEquals(tokenToActivate, m_renewedTokenWithKeys))
                {
                    ComputeKeys(tokenToActivate);
                    m_renewedTokenWithKeys = tokenToActivate;
                }
                token = tokenToActivate;
            }
            else
            {
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
            }

            // check if token has expired. A client accepts responses secured with an expired
            // token for a grace period (OPC 10000-4 5.6.2.1): the server keeps securing its
            // responses with the old token until it sees the new one, so a response sent just
            // before expiry can arrive after it. Rejecting it shuts the channel down.
            if (token.IsExpired(TimeProvider, isRequest ? 0 : TcpMessageLimits.TokenExpiryGracePeriod))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "Channel{0}: Token #{1} has expired. Lifetime={2:HH:mm:ss.fff}-{3}",
                    Id,
                    token.TokenId,
                    token.CreatedAt,
                    token.CreatedAtTimestamp);
            }

            int headerSize = TcpMessageLimits.SymmetricHeaderSize;

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

            // the chunk was secured with the new token: it becomes current.
            if (tokenToActivate != null && ReferenceEquals(RenewedToken, tokenToActivate))
            {
                ActivateToken(tokenToActivate);
            }

            // extract request id and sequence number.
            sequenceNumber = ReadHeaderField(buffer, headerSize);
            requestId = ReadHeaderField(buffer, headerSize + 4);

            headerSize += TcpMessageLimits.SequenceHeaderSize;

            // return only the data contained in the message.
            return new ArraySegment<byte>(
                dataToProcess.GetArray(),
                dataToProcess.Offset + headerSize,
                dataToProcess.Count - headerSize);
        }

        /// <summary>
        /// Reads a 32-bit header field of a message chunk.
        /// </summary>
        /// <exception cref="ServiceResultException">The chunk ends before the
        /// field, with the decoding error a decoder reports.</exception>
        private uint ReadHeaderField(ArraySegment<byte> buffer, int offset)
        {
            if (buffer.Count - offset >= sizeof(uint))
            {
                return BinaryPrimitives.ReadUInt32LittleEndian(
                    buffer.AsSpan(offset, sizeof(uint)));
            }

            // Too short: the decoder raises the same error as when it read the
            // whole header.
            using var decoder = new BinaryDecoder(
                buffer.GetArray(),
                buffer.Offset + Math.Min(offset, buffer.Count),
                Math.Max(buffer.Count - offset, 0),
                Quotas.MessageContext);
            return decoder.ReadUInt32(null);
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
                m_remoteSequenceNumber,
                useClientKeys ? token.ClientHmac : token.ServerHmac,
                m_symmetricProvider,
                useClientKeys ? token.ClientCiphers : token.ServerCiphers);
        }

        private static readonly byte[] s_hkdfClientLabel = Encoding.UTF8.GetBytes("opcua-client");
        private static readonly byte[] s_hkdfServerLabel = Encoding.UTF8.GetBytes("opcua-server");

        /// <summary>
        /// The renewed token whose keys were computed to verify a chunk before it
        /// was activated.
        /// </summary>
        private ChannelToken? m_renewedTokenWithKeys;

        /// <summary>
        /// A single-chunk message that needs at most this fraction of the send buffer
        /// is moved to a buffer of its own size (see <see cref="EncodeSymmetricMessage"/>).
        /// </summary>
        private const int kSmallMessageFraction = 8;
        private int m_signatureKeySize;
        private int m_encryptionKeySize;
        private ISymmetricCryptoProvider? m_symmetricProvider;
        private IKeyDerivationProvider? m_keyDerivationProvider;
    }
}
