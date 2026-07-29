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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Encodes and decodes the secured body of a data channel frame: the
    /// twelve byte stream header, the optional Deadline, the fields the
    /// FrameType implies and the payload (Part 6 errata 5.2 to 5.5).
    /// </summary>
    /// <remarks>
    /// All integers are little endian, matching the OPC UA Binary
    /// DataEncoding. The decoder never allocates and never reads outside
    /// the buffer it was given: every field is bounds checked against the
    /// length the frame's own FrameType and flags imply before it is
    /// read.
    /// </remarks>
    public static class DataChannelFrameCodec
    {
        /// <summary>
        /// The highest defined FrameType. Values above it are reserved.
        /// </summary>
        public const byte MaxFrameType = (byte)DataChannelFrameType.Pong;

        /// <summary>
        /// Decodes one frame from the secured body of a chunk.
        /// </summary>
        /// <param name="body">The secured body, starting at the stream
        /// header and ending at the message footer.</param>
        /// <param name="maxFrameSize">The largest body the peer is
        /// permitted to send, or zero to skip the check.</param>
        /// <param name="frame">The decoded frame. Its payload is a window
        /// onto <paramref name="body"/>.</param>
        /// <param name="error">Why the frame was rejected.</param>
        /// <returns>True when the frame is well formed.</returns>
        public static bool TryDecode(
            ReadOnlyMemory<byte> body,
            int maxFrameSize,
            out DataChannelFrame frame,
            out DataChannelFrameError error)
        {
            frame = default;

            ReadOnlySpan<byte> span = body.Span;

            if (span.Length < DataChannelConstants.StreamHeaderSize)
            {
                error = DataChannelFrameError.MalformedHeader;
                return false;
            }

            if (maxFrameSize > 0 && span.Length > maxFrameSize)
            {
                error = DataChannelFrameError.FrameTooLarge;
                return false;
            }

            uint channelId = BinaryPrimitives.ReadUInt32LittleEndian(span);
            byte frameType = span[4];
            byte flagBits = span[5];
            ushort reserved = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(6));
            uint frameSequenceNumber = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8));

            if (reserved != 0)
            {
                error = DataChannelFrameError.NonZeroHeaderPadding;
                return false;
            }

            if ((flagBits & DataChannelConstants.ReservedFlagMask) != 0)
            {
                error = DataChannelFrameError.UnknownFlagBits;
                return false;
            }

            if (frameSequenceNumber == 0)
            {
                error = DataChannelFrameError.ZeroFrameSequenceNumber;
                return false;
            }

            if (frameType > MaxFrameType)
            {
                frame = DataChannelFrame.Decoded(
                    channelId,
                    (DataChannelFrameType)frameType,
                    (DataChannelFrameFlags)flagBits,
                    frameSequenceNumber,
                    0,
                    0,
                    0,
                    ReadOnlyMemory<byte>.Empty);
                error = DataChannelFrameError.UnknownFrameType;
                return false;
            }

            var type = (DataChannelFrameType)frameType;
            var flags = (DataChannelFrameFlags)flagBits;

            // The connection control channel carries only CREDIT, PING and
            // PONG, and never payload (5.6). It cannot be reset, so a
            // violation closes the SecureChannel.
            if (channelId == DataChannelConstants.ConnectionControlChannelId &&
                type != DataChannelFrameType.Credit &&
                type != DataChannelFrameType.Ping &&
                type != DataChannelFrameType.Pong)
            {
                error = DataChannelFrameError.InvalidControlChannelFrame;
                return false;
            }

            int offset = DataChannelConstants.StreamHeaderSize;
            long deadline = 0;

            if ((flags & DataChannelFrameFlags.DeadlinePresent) != 0)
            {
                if (span.Length - offset < DataChannelConstants.DeadlineSize)
                {
                    error = DataChannelFrameError.FrameTooShort;
                    return false;
                }

                deadline = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(offset));
                offset += DataChannelConstants.DeadlineSize;
            }

            int extra = DataChannelFrame.ExtraFieldSize(type);

            if (span.Length - offset < extra)
            {
                error = DataChannelFrameError.FrameTooShort;
                return false;
            }

            uint value1 = 0;
            uint value2 = 0;

            if (extra >= 4)
            {
                value1 = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset));
            }

            if (extra >= 8)
            {
                value2 = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 4));
            }

            offset += extra;

            int payloadLength = span.Length - offset;

            if (type != DataChannelFrameType.Data && payloadLength > 0)
            {
                frame = DataChannelFrame.Decoded(
                    channelId,
                    type,
                    flags,
                    frameSequenceNumber,
                    deadline,
                    value1,
                    value2,
                    ReadOnlyMemory<byte>.Empty);
                error = DataChannelFrameError.PayloadOnNonDataFrame;
                return false;
            }

            frame = DataChannelFrame.Decoded(
                channelId,
                type,
                flags,
                frameSequenceNumber,
                deadline,
                value1,
                value2,
                payloadLength > 0 ? body.Slice(offset, payloadLength) : ReadOnlyMemory<byte>.Empty);

            error = DataChannelFrameError.None;
            return true;
        }

        /// <summary>
        /// Encodes one frame into the secured body of a chunk.
        /// </summary>
        /// <param name="destination">The buffer to write into. It shall be
        /// at least <see cref="DataChannelFrame.EncodedSize"/> bytes.</param>
        /// <param name="frame">The frame to encode.</param>
        /// <returns>The number of bytes written.</returns>
        /// <exception cref="ArgumentException">The destination is too
        /// small.</exception>
        public static int Encode(Span<byte> destination, in DataChannelFrame frame)
        {
            int size = frame.EncodedSize;

            if (destination.Length < size)
            {
                throw new ArgumentException(
                    "The destination is smaller than the encoded frame.",
                    nameof(destination));
            }

            BinaryPrimitives.WriteUInt32LittleEndian(destination, frame.ChannelId);
            destination[4] = (byte)frame.FrameType;
            destination[5] = (byte)frame.Flags;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(
                destination.Slice(8),
                frame.FrameSequenceNumber);

            int offset = DataChannelConstants.StreamHeaderSize;

            if (frame.HasDeadline)
            {
                BinaryPrimitives.WriteInt64LittleEndian(
                    destination.Slice(offset),
                    frame.Deadline);
                offset += DataChannelConstants.DeadlineSize;
            }

            switch (frame.FrameType)
            {
                case DataChannelFrameType.Credit:
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        destination.Slice(offset),
                        frame.ChannelCredit);
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        destination.Slice(offset + 4),
                        frame.ConnectionCredit);
                    offset += 8;
                    break;
                case DataChannelFrameType.Gap:
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        destination.Slice(offset),
                        frame.FirstDiscarded);
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        destination.Slice(offset + 4),
                        frame.LastDiscarded);
                    offset += 8;
                    break;
                case DataChannelFrameType.Reset:
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        destination.Slice(offset),
                        frame.Status.Code);
                    offset += 4;
                    break;
                case DataChannelFrameType.Ping:
                case DataChannelFrameType.Pong:
                    BinaryPrimitives.WriteInt64LittleEndian(
                        destination.Slice(offset),
                        frame.Timestamp);
                    offset += 8;
                    break;
                default:
                    break;
            }

            if (!frame.Payload.IsEmpty)
            {
                frame.Payload.Span.CopyTo(destination.Slice(offset));
                offset += frame.Payload.Length;
            }

            return offset;
        }

        /// <summary>
        /// Validates the reused chunk headers of an inline framed frame
        /// (Part 6 errata 5.1).
        /// </summary>
        /// <param name="messageType">The message type and chunk type of
        /// the STR chunk.</param>
        /// <param name="requestId">The RequestId from the sequence
        /// header.</param>
        /// <param name="error">Why the chunk was rejected.</param>
        /// <returns>True when the headers are acceptable.</returns>
        public static bool TryValidateChunkHeaders(
            uint messageType,
            uint requestId,
            out DataChannelFrameError error)
        {
            // A data channel frame is a single chunk, so it is never an
            // intermediate chunk and never a Message abort. Accepting 'A'
            // would let the 6.7.3 abort parser read a 32 bit string length
            // out of the attacker controlled stream header bytes.
            if (!TcpMessageType.IsFinal(messageType))
            {
                error = DataChannelFrameError.InvalidIsFinal;
                return false;
            }

            if (requestId != DataChannelConstants.FrameRequestId)
            {
                error = DataChannelFrameError.NonZeroRequestId;
                return false;
            }

            error = DataChannelFrameError.None;
            return true;
        }

        /// <summary>
        /// The largest payload a DATA frame may carry given the buffer the
        /// transport negotiated (Part 6 errata 5.5).
        /// </summary>
        /// <param name="mode">The framing mode.</param>
        /// <param name="transportBufferSize">The buffer size the peer
        /// declared in Hello or Acknowledge, or the QUIC stream or
        /// datagram limit.</param>
        /// <param name="footerSize">The bytes the security policy's
        /// padding and signature occupy, or zero for QUIC framing.</param>
        /// <param name="withDeadline">True to reserve the optional
        /// Deadline field.</param>
        public static int MaxPayload(
            DataChannelFramingMode mode,
            int transportBufferSize,
            int footerSize,
            bool withDeadline)
        {
            int overhead = mode == DataChannelFramingMode.Quic
                ? DataChannelConstants.QuicFrameOverhead
                : DataChannelConstants.InlineFrameOverhead;

            if (withDeadline)
            {
                overhead += DataChannelConstants.DeadlineSize;
            }

            int available = transportBufferSize - overhead - footerSize;
            return available > 0 ? available : 0;
        }
    }
}
