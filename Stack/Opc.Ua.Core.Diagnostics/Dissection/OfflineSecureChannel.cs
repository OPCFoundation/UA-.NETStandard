/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.KeyLog;

namespace Opc.Ua.Pcap.Dissection
{
    /// <summary>
    /// Offline decoder that reuses the stack's own
    /// <see cref="UaSCUaBinaryChannel"/> to decrypt and verify captured
    /// OPC UA UA-SC chunks using the channel key material recorded in a
    /// keylog. Because the actual decrypt path is the stack's own
    /// <see cref="UaSCUaBinaryChannel.ReadSymmetricMessage"/>, every
    /// security profile the stack supports works automatically with no
    /// per-profile code in the diagnostics library.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One instance corresponds to one secure channel (one
    /// <see cref="ChannelKeyMaterial.ChannelId"/>). Token renewals are
    /// supported by calling <see cref="LoadKeyMaterial"/> for each new
    /// token in the order they were observed. Sequence-number state is
    /// tracked per direction since client- and server-sent messages use
    /// disjoint counters.
    /// </para>
    /// <para>
    /// The decoder is not thread-safe; callers should walk a single
    /// channel's traffic sequentially.
    /// </para>
    /// </remarks>
    public sealed class OfflineSecureChannel : IDisposable
    {
        private readonly ChannelDecoderShim m_clientToServer;
        private readonly ChannelDecoderShim m_serverToClient;
        private readonly Dictionary<uint, ChannelKeyMaterial> m_tokens;
#pragma warning disable IDE0052 // Kept for diagnostics context; decode paths currently throw typed exceptions directly.
        private readonly ILogger m_logger;
#pragma warning restore IDE0052
        private uint m_currentTokenId;

        /// <summary>
        /// Constructs an offline decoder for the channel described by
        /// <paramref name="firstToken"/>. Additional tokens (renewals) can
        /// be added with <see cref="LoadKeyMaterial"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="firstToken"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="PcapDiagnosticsException">
        /// The token's security policy URI is not recognised by the
        /// stack.
        /// </exception>
        public OfflineSecureChannel(ChannelKeyMaterial firstToken)
            : this(firstToken, NullLoggerFactory.Instance)
        {
        }

        /// <summary>
        /// Constructs an offline decoder with explicit logger factory.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// Either argument is <c>null</c>.
        /// </exception>
        /// <exception cref="PcapDiagnosticsException">
        /// The token's security policy URI is not recognised by the
        /// stack.
        /// </exception>
        public OfflineSecureChannel(
            ChannelKeyMaterial firstToken,
            ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(firstToken);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            m_logger = loggerFactory.CreateLogger<OfflineSecureChannel>();
            ChannelId = firstToken.ChannelId;
            m_currentTokenId = firstToken.TokenId;
            m_tokens = [];

            NoopTelemetryContext telemetry = NoopTelemetryContext.Instance;
            m_clientToServer = new ChannelDecoderShim(firstToken, telemetry);
            m_serverToClient = new ChannelDecoderShim(firstToken, telemetry);

            LoadKeyMaterial(firstToken);
        }

        /// <summary>
        /// The secure-channel id this decoder handles.
        /// </summary>
        public uint ChannelId { get; }

        /// <summary>
        /// Adds (or updates) the key material for a token observed on
        /// this channel. Subsequent decodes whose
        /// <c>TokenId</c> matches <paramref name="material"/> use the
        /// supplied keys.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="material"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="PcapDiagnosticsException">
        /// The token belongs to a different channel than the one this
        /// decoder was constructed for.
        /// </exception>
        public void LoadKeyMaterial(ChannelKeyMaterial material)
        {
            ArgumentNullException.ThrowIfNull(material);
            if (material.ChannelId != ChannelId)
            {
                throw new PcapDiagnosticsException(
                    "ChannelKeyMaterial.ChannelId " +
                    $"(0x{material.ChannelId:X8}) does not match this " +
                    $"OfflineSecureChannel (0x{ChannelId:X8}).");
            }
            m_tokens[material.TokenId] = material;
            if (material.TokenId > m_currentTokenId)
            {
                m_currentTokenId = material.TokenId;
            }
        }

        /// <summary>
        /// Decodes a single OPC UA UA-SC chunk. The chunk's encoded
        /// <c>tokenId</c> selects which previously-loaded key material to
        /// use. AsymmetricMessage chunks (OpenSecureChannel) are returned
        /// undecoded with <see cref="OfflineDecodedChunk.Body"/> set to
        /// the raw payload after the asymmetric security header - the
        /// caller is expected to look up the matching token via the
        /// keylog.
        /// </summary>
        /// <param name="chunkBytes">
        /// The full OPC UA chunk including the 8-byte message header.
        /// </param>
        /// <param name="fromClient">
        /// <c>true</c> when the chunk was sent by the client; <c>false</c>
        /// when sent by the server. The direction determines which key
        /// set (client or server) is used to decrypt.
        /// </param>
        /// <exception cref="PcapDiagnosticsException">
        /// The chunk's channel id does not match the channel this decoder
        /// was constructed for; or the chunk is shorter than the minimum
        /// OPC UA framing length; or the chunk's token id has not been
        /// loaded.
        /// </exception>
        public OfflineDecodedChunk ReadChunk(
            ReadOnlySpan<byte> chunkBytes,
            bool fromClient)
        {
            if (chunkBytes.Length < TcpMessageLimits.SymmetricHeaderSize +
                TcpMessageLimits.SequenceHeaderSize)
            {
                throw new PcapDiagnosticsException(
                    $"Chunk too short ({chunkBytes.Length} bytes; minimum " +
                    $"is {TcpMessageLimits.SymmetricHeaderSize + TcpMessageLimits.SequenceHeaderSize}).");
            }

            uint messageType = BinaryPrimitives.ReadUInt32LittleEndian(chunkBytes);
            uint chunkType = messageType & TcpMessageType.ChunkTypeMask;
            uint baseType = messageType & TcpMessageType.MessageTypeMask;

            if (baseType == TcpMessageType.Open)
            {
                return ReadAsymmetricChunkUnverified(chunkBytes);
            }

            uint channelId = BinaryPrimitives.ReadUInt32LittleEndian(chunkBytes[8..]);
            if (channelId != ChannelId)
            {
                throw new PcapDiagnosticsException(
                    $"Chunk channel id (0x{channelId:X8}) does not match " +
                    $"this OfflineSecureChannel (0x{ChannelId:X8}).");
            }
            uint tokenId = BinaryPrimitives.ReadUInt32LittleEndian(chunkBytes[12..]);
            if (!m_tokens.TryGetValue(tokenId, out ChannelKeyMaterial? material))
            {
                throw new PcapDiagnosticsException(
                    $"No key material loaded for token id 0x{tokenId:X8} on " +
                    $"channel 0x{channelId:X8}. Did you forget to load the keylog?");
            }

            ChannelDecoderShim shim = fromClient ? m_clientToServer : m_serverToClient;
            shim.UpdateForToken(material);

            byte[] mutable = new byte[chunkBytes.Length];
            chunkBytes.CopyTo(mutable);
            var segment = new ArraySegment<byte>(mutable);

            ArraySegment<byte> body = shim.Decode(
                segment,
                isRequest: fromClient,
                out uint requestId,
                out uint sequenceNumber);

            ReadOnlyMemory<byte> bodyMemory = body.Array is null
                ? ReadOnlyMemory<byte>.Empty
                : new ReadOnlyMemory<byte>(body.Array, body.Offset, body.Count);

            return new OfflineDecodedChunk(
                messageType: messageType,
                channelId: channelId,
                tokenId: tokenId,
                sequenceNumber: sequenceNumber,
                requestId: requestId,
                isFinal: chunkType == TcpMessageType.Final,
                isAbort: chunkType == TcpMessageType.Abort,
                body: bodyMemory);
        }

        /// <summary>
        /// Disposes the underlying decoder shims and zeroes the in-memory
        /// key material as best-effort.
        /// </summary>
        public void Dispose()
        {
            m_clientToServer.Dispose();
            m_serverToClient.Dispose();
            m_tokens.Clear();
        }

        private static OfflineDecodedChunk ReadAsymmetricChunkUnverified(
            ReadOnlySpan<byte> chunkBytes)
        {
            // Asymmetric chunks are OpenSecureChannel; their bodies are
            // encrypted with the receiver's RSA / ECC private key which is
            // not part of the keylog. We surface a partial decode so the
            // caller can extract metadata (channel id, security policy URI,
            // sequence header) without claiming we decrypted the body.
            uint messageType = BinaryPrimitives.ReadUInt32LittleEndian(chunkBytes);
            uint chunkType = messageType & TcpMessageType.ChunkTypeMask;
            uint channelId = chunkBytes.Length >= 12
                ? BinaryPrimitives.ReadUInt32LittleEndian(chunkBytes[8..])
                : 0;
            ReadOnlyMemory<byte> body = chunkBytes.Length > TcpMessageLimits.SymmetricHeaderSize
                ? new ReadOnlyMemory<byte>(chunkBytes[TcpMessageLimits.SymmetricHeaderSize..].ToArray())
                : ReadOnlyMemory<byte>.Empty;
            return new OfflineDecodedChunk(
                messageType: messageType,
                channelId: channelId,
                tokenId: 0,
                sequenceNumber: 0,
                requestId: 0,
                isFinal: chunkType == TcpMessageType.Final,
                isAbort: chunkType == TcpMessageType.Abort,
                body: body);
        }

        /// <summary>
        /// Thin <see cref="UaSCUaBinaryChannel"/> subclass whose only
        /// purpose is to give us access to the protected
        /// <see cref="UaSCUaBinaryChannel.ReadSymmetricMessage"/> entry
        /// point and the protected <see cref="UaSCUaBinaryChannel.ChannelId"/>
        /// setter. All other behaviour is left to the base.
        /// </summary>
        private sealed class ChannelDecoderShim : UaSCUaBinaryChannel
        {
            private uint m_loadedTokenId;

            public ChannelDecoderShim(
                ChannelKeyMaterial firstToken,
                ITelemetryContext telemetry)
                : base(
                    contextId: "offline",
                    bufferManager: new BufferManager(
                        "offline",
                        TcpMessageLimits.DefaultMaxBufferSize,
                        telemetry),
                    quotas: new ChannelQuotas(ServiceMessageContext.CreateEmpty(telemetry)),
                    serverCertificates: null,
                    endpoints: null,
                    securityMode: firstToken.SecurityMode,
                    securityPolicyUri: firstToken.SecurityPolicyUri,
                    telemetry: telemetry)
            {
                ChannelId = firstToken.ChannelId;
                UpdateForToken(firstToken);
            }

            public void UpdateForToken(ChannelKeyMaterial material)
            {
                if (material.TokenId == m_loadedTokenId && CurrentToken != null)
                {
                    return;
                }

                // Build the ChannelToken in-place from the snapshot. Use
                // a very large lifetime so the live IsExpired check
                // tolerates traces captured days ago.
                // CA2000: ownership is transferred to the channel via IDiagnosticsChannelMutation below.
                // TODO: add an ownership-transfer helper when ChannelToken lifetime management is refactored.
#pragma warning disable CA2000
                var token = new ChannelToken
                {
                    ChannelId = material.ChannelId,
                    TokenId = material.TokenId,
                    CreatedAt = material.CreatedAt,
                    Lifetime = int.MaxValue,
                    SecurityPolicy = SecurityPolicies.GetInfo(material.SecurityPolicyUri),
                    ClientNonce = material.ClientNonce,
                    ServerNonce = material.ServerNonce,
                    ClientSigningKey = material.ClientSigningKey,
                    ClientEncryptingKey = material.ClientEncryptingKey,
                    ClientInitializationVector = material.ClientInitializationVector,
                    ServerSigningKey = material.ServerSigningKey,
                    ServerEncryptingKey = material.ServerEncryptingKey,
                    ServerInitializationVector = material.ServerInitializationVector
                };
#pragma warning restore CA2000

                if (token.SecurityPolicy is null)
                {
                    throw new PcapDiagnosticsException(
                        $"Unsupported SecurityPolicyUri '{material.SecurityPolicyUri}'.");
                }

                ChannelToken? previous = CurrentToken;
                ((IDiagnosticsChannelMutation)this).LoadTokensForOfflineDecode(token, previous);
                OfflineResetRemoteSequenceNumber(0);
                m_loadedTokenId = material.TokenId;
            }

            public ArraySegment<byte> Decode(
                ArraySegment<byte> buffer,
                bool isRequest,
                out uint requestId,
                out uint sequenceNumber)
            {
                return ReadSymmetricMessage(
                    buffer,
                    isRequest,
                    out _,
                    out requestId,
                    out sequenceNumber);
            }
        }

        /// <summary>
        /// Minimal <see cref="ITelemetryContext"/> implementation that
        /// hands out <see cref="NullLogger.Instance"/> for everything.
        /// Used so the offline channel never emits diagnostic logs.
        /// </summary>
        private sealed class NoopTelemetryContext : ITelemetryContext
        {
            public static NoopTelemetryContext Instance { get; } = new();

            private static readonly System.Diagnostics.ActivitySource s_activitySource
                = new("Opc.Ua.Core.Diagnostics.OfflineSecureChannel");

            private NoopTelemetryContext()
            {
            }

            public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;

            public System.Diagnostics.ActivitySource ActivitySource => s_activitySource;

            public System.Diagnostics.Metrics.Meter CreateMeter()
            {
                return new System.Diagnostics.Metrics.Meter(
                    "Opc.Ua.Core.Diagnostics.OfflineSecureChannel");
            }
        }
    }
}
