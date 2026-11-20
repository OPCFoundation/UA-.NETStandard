/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Dissection
{
    /// <summary>
    /// The result of decoding a single OPC UA UA-SC chunk offline.
    /// </summary>
    public readonly struct OfflineDecodedChunk : IEquatable<OfflineDecodedChunk>
    {
        /// <summary>
        /// Constructs a new decoded chunk record.
        /// </summary>
        public OfflineDecodedChunk(
            uint messageType,
            uint channelId,
            uint tokenId,
            uint sequenceNumber,
            uint requestId,
            bool isFinal,
            bool isAbort,
            ReadOnlyMemory<byte> body)
        {
            MessageType = messageType;
            ChannelId = channelId;
            TokenId = tokenId;
            SequenceNumber = sequenceNumber;
            RequestId = requestId;
            IsFinal = isFinal;
            IsAbort = isAbort;
            Body = body;
        }

        /// <summary>
        /// The 32-bit message-type marker, e.g.
        /// <see cref="TcpMessageType.MessageFinal"/>,
        /// <see cref="TcpMessageType.Open"/>, etc.
        /// </summary>
        public uint MessageType { get; }

        /// <summary>
        /// The secure-channel id this chunk belongs to.
        /// </summary>
        public uint ChannelId { get; }

        /// <summary>
        /// The token id that secured this chunk.
        /// </summary>
        public uint TokenId { get; }

        /// <summary>
        /// The chunk's sequence number (from the OPC UA sequence header).
        /// </summary>
        public uint SequenceNumber { get; }

        /// <summary>
        /// The request id assigned by the client.
        /// </summary>
        public uint RequestId { get; }

        /// <summary>
        /// <c>true</c> if this is a final chunk for the message.
        /// </summary>
        public bool IsFinal { get; }

        /// <summary>
        /// <c>true</c> if this chunk was an abort signal.
        /// </summary>
        public bool IsAbort { get; }

        /// <summary>
        /// The decrypted body bytes of the chunk (no sequence header).
        /// </summary>
        public ReadOnlyMemory<byte> Body { get; }

        /// <inheritdoc/>
        public bool Equals(OfflineDecodedChunk other)
        {
            return MessageType == other.MessageType &&
                ChannelId == other.ChannelId &&
                TokenId == other.TokenId &&
                SequenceNumber == other.SequenceNumber &&
                RequestId == other.RequestId &&
                IsFinal == other.IsFinal &&
                IsAbort == other.IsAbort &&
                Body.Span.SequenceEqual(other.Body.Span);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is OfflineDecodedChunk other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(MessageType, ChannelId, TokenId, SequenceNumber, RequestId, IsFinal, IsAbort, Body.Length);
        }

        /// <summary>
        /// Equality comparison.
        /// </summary>
        public static bool operator ==(OfflineDecodedChunk left, OfflineDecodedChunk right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality comparison.
        /// </summary>
        public static bool operator !=(OfflineDecodedChunk left, OfflineDecodedChunk right)
        {
            return !left.Equals(right);
        }
    }
}
