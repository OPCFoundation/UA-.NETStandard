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

namespace Opc.Ua.Diagnostics.Pcap.Dissection
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
        /// <see cref="Opc.Ua.Bindings.TcpMessageType.MessageFinal"/>,
        /// <see cref="Opc.Ua.Bindings.TcpMessageType.Open"/>, etc.
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
            return MessageType == other.MessageType
                && ChannelId == other.ChannelId
                && TokenId == other.TokenId
                && SequenceNumber == other.SequenceNumber
                && RequestId == other.RequestId
                && IsFinal == other.IsFinal
                && IsAbort == other.IsAbort
                && Body.Span.SequenceEqual(other.Body.Span);
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
        public static bool operator ==(OfflineDecodedChunk left, OfflineDecodedChunk right) => left.Equals(right);

        /// <summary>
        /// Inequality comparison.
        /// </summary>
        public static bool operator !=(OfflineDecodedChunk left, OfflineDecodedChunk right) => !left.Equals(right);
    }
}
