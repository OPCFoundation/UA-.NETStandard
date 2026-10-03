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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// One decoded data channel frame: the twelve byte stream header, the
    /// optional Deadline, the fields the FrameType implies, and, for a
    /// DATA frame, the payload.
    /// </summary>
    /// <remarks>
    /// The payload is a window onto the buffer the frame was decoded
    /// from and is valid only while the caller owns that buffer. Nothing
    /// is copied.
    /// </remarks>
    public readonly record struct DataChannelFrame
    {
        /// <summary>
        /// Creates a frame. Callers use the named factory methods.
        /// </summary>
        private DataChannelFrame(
            uint channelId,
            DataChannelFrameType frameType,
            DataChannelFrameFlags flags,
            uint frameSequenceNumber,
            long deadline,
            uint value1,
            uint value2,
            ReadOnlyMemory<byte> payload)
        {
            ChannelId = channelId;
            FrameType = frameType;
            Flags = flags;
            FrameSequenceNumber = frameSequenceNumber;
            Deadline = deadline;
            m_value1 = value1;
            m_value2 = value2;
            Payload = payload;
        }

        /// <summary>
        /// The data channel this frame belongs to. Zero is the connection
        /// control channel.
        /// </summary>
        public uint ChannelId { get; }

        /// <summary>
        /// The frame type.
        /// </summary>
        public DataChannelFrameType FrameType { get; }

        /// <summary>
        /// The flags.
        /// </summary>
        public DataChannelFrameFlags Flags { get; }

        /// <summary>
        /// The per channel, per direction counter. Never zero.
        /// </summary>
        public uint FrameSequenceNumber { get; }

        /// <summary>
        /// The sender's clock at which a droppable frame expires,
        /// expressed in 100 nanosecond intervals since 1601-01-01 UTC.
        /// Zero when <see cref="HasDeadline"/> is false. It is never
        /// compared across the two ends.
        /// </summary>
        public long Deadline { get; }

        /// <summary>
        /// The payload. Empty for every frame type but DATA.
        /// </summary>
        public ReadOnlyMemory<byte> Payload { get; }

        /// <summary>
        /// True when the frame carries the Deadline field.
        /// </summary>
        public bool HasDeadline
            => (Flags & DataChannelFrameFlags.DeadlinePresent) != 0;

        /// <summary>
        /// True when the sender may discard this frame once its deadline
        /// passes.
        /// </summary>
        public bool IsDroppable
            => (Flags & DataChannelFrameFlags.Droppable) != 0;

        /// <summary>
        /// The channel level grant carried by a CREDIT frame, in payload
        /// bytes.
        /// </summary>
        public uint ChannelCredit => m_value1;

        /// <summary>
        /// The connection level grant carried by a CREDIT frame, in
        /// payload bytes.
        /// </summary>
        public uint ConnectionCredit => m_value2;

        /// <summary>
        /// The first FrameSequenceNumber of the run a GAP frame reports,
        /// inclusive.
        /// </summary>
        public uint FirstDiscarded => m_value1;

        /// <summary>
        /// The last FrameSequenceNumber of the run a GAP frame reports,
        /// inclusive.
        /// </summary>
        public uint LastDiscarded => m_value2;

        /// <summary>
        /// The StatusCode a RESET frame carries. Good is an orderly
        /// discard and close; a Bad value is an abort.
        /// </summary>
        public StatusCode Status => new(m_value1);

        /// <summary>
        /// The value a PING carries and a PONG copies back verbatim.
        /// </summary>
        public long Timestamp => (long)(((ulong)m_value2 << 32) | m_value1);

        /// <summary>
        /// The number of bytes this frame occupies in the secured body,
        /// excluding any transport framing around it.
        /// </summary>
        public int EncodedSize
            => DataChannelConstants.StreamHeaderSize +
               (HasDeadline ? DataChannelConstants.DeadlineSize : 0) +
               ExtraFieldSize(FrameType) +
               Payload.Length;

        /// <summary>
        /// The number of bytes the fields of a frame type occupy between
        /// the stream header and the payload.
        /// </summary>
        /// <param name="frameType">The frame type.</param>
        public static int ExtraFieldSize(DataChannelFrameType frameType)
        {
            switch (frameType)
            {
                case DataChannelFrameType.Credit:
                case DataChannelFrameType.Gap:
                    return 8;
                case DataChannelFrameType.Reset:
                    return 4;
                case DataChannelFrameType.Ping:
                case DataChannelFrameType.Pong:
                    return 8;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Creates a DATA frame.
        /// </summary>
        /// <param name="channelId">The data channel.</param>
        /// <param name="frameSequenceNumber">The per channel counter,
        /// assigned when the frame is enqueued.</param>
        /// <param name="flags">The flags.</param>
        /// <param name="payload">The payload.</param>
        /// <param name="deadline">The expiry instant, or zero.</param>
        public static DataChannelFrame Data(
            uint channelId,
            uint frameSequenceNumber,
            DataChannelFrameFlags flags,
            ReadOnlyMemory<byte> payload,
            long deadline = 0)
        {
            return new DataChannelFrame(
                channelId,
                DataChannelFrameType.Data,
                deadline != 0 ? flags | DataChannelFrameFlags.DeadlinePresent : flags,
                frameSequenceNumber,
                deadline,
                0,
                0,
                payload);
        }

        /// <summary>
        /// Creates a CREDIT frame.
        /// </summary>
        /// <param name="channelId">The data channel, or zero for the
        /// connection control channel.</param>
        /// <param name="frameSequenceNumber">The per channel counter.</param>
        /// <param name="channelCredit">The channel grant. Shall be zero on
        /// the connection control channel.</param>
        /// <param name="connectionCredit">The connection grant.</param>
        public static DataChannelFrame Credit(
            uint channelId,
            uint frameSequenceNumber,
            uint channelCredit,
            uint connectionCredit)
        {
            return new DataChannelFrame(
                channelId,
                DataChannelFrameType.Credit,
                DataChannelFrameFlags.None,
                frameSequenceNumber,
                0,
                channelCredit,
                connectionCredit,
                ReadOnlyMemory<byte>.Empty);
        }

        /// <summary>
        /// Creates a GAP frame naming one contiguous, inclusive run.
        /// </summary>
        /// <param name="channelId">The data channel.</param>
        /// <param name="frameSequenceNumber">The per channel counter.</param>
        /// <param name="firstDiscarded">The first discarded number.</param>
        /// <param name="lastDiscarded">The last discarded number.</param>
        public static DataChannelFrame Gap(
            uint channelId,
            uint frameSequenceNumber,
            uint firstDiscarded,
            uint lastDiscarded)
        {
            return new DataChannelFrame(
                channelId,
                DataChannelFrameType.Gap,
                DataChannelFrameFlags.None,
                frameSequenceNumber,
                0,
                firstDiscarded,
                lastDiscarded,
                ReadOnlyMemory<byte>.Empty);
        }

        /// <summary>
        /// Creates a RESET frame.
        /// </summary>
        /// <param name="channelId">The data channel.</param>
        /// <param name="frameSequenceNumber">The per channel counter.</param>
        /// <param name="status">Good for an orderly discard and close,
        /// a Bad value for an abort.</param>
        public static DataChannelFrame Reset(
            uint channelId,
            uint frameSequenceNumber,
            StatusCode status)
        {
            return new DataChannelFrame(
                channelId,
                DataChannelFrameType.Reset,
                DataChannelFrameFlags.None,
                frameSequenceNumber,
                0,
                status.Code,
                0,
                ReadOnlyMemory<byte>.Empty);
        }

        /// <summary>
        /// Creates an END frame, which half closes one direction.
        /// </summary>
        /// <param name="channelId">The data channel.</param>
        /// <param name="frameSequenceNumber">The per channel counter.</param>
        public static DataChannelFrame End(uint channelId, uint frameSequenceNumber)
        {
            return new DataChannelFrame(
                channelId,
                DataChannelFrameType.End,
                DataChannelFrameFlags.None,
                frameSequenceNumber,
                0,
                0,
                0,
                ReadOnlyMemory<byte>.Empty);
        }

        /// <summary>
        /// Creates a PING frame.
        /// </summary>
        /// <param name="channelId">The data channel, or zero to probe the
        /// connection.</param>
        /// <param name="frameSequenceNumber">The per channel counter.</param>
        /// <param name="timestamp">An opaque value the peer copies back.</param>
        public static DataChannelFrame Ping(
            uint channelId,
            uint frameSequenceNumber,
            long timestamp)
        {
            return Probe(channelId, DataChannelFrameType.Ping, frameSequenceNumber, timestamp);
        }

        /// <summary>
        /// Creates a PONG frame copying a PING timestamp verbatim.
        /// </summary>
        /// <param name="channelId">The data channel.</param>
        /// <param name="frameSequenceNumber">The per channel counter.</param>
        /// <param name="timestamp">The value copied from the PING.</param>
        public static DataChannelFrame Pong(
            uint channelId,
            uint frameSequenceNumber,
            long timestamp)
        {
            return Probe(channelId, DataChannelFrameType.Pong, frameSequenceNumber, timestamp);
        }

        /// <summary>
        /// Creates a frame with fields taken verbatim from the wire.
        /// Used by the decoder, which has already validated them.
        /// </summary>
        /// <param name="channelId">The data channel.</param>
        /// <param name="frameType">The frame type.</param>
        /// <param name="flags">The flags.</param>
        /// <param name="frameSequenceNumber">The per channel counter.</param>
        /// <param name="deadline">The deadline, or zero.</param>
        /// <param name="value1">The first type specific field.</param>
        /// <param name="value2">The second type specific field.</param>
        /// <param name="payload">The payload.</param>
        internal static DataChannelFrame Decoded(
            uint channelId,
            DataChannelFrameType frameType,
            DataChannelFrameFlags flags,
            uint frameSequenceNumber,
            long deadline,
            uint value1,
            uint value2,
            ReadOnlyMemory<byte> payload)
        {
            return new DataChannelFrame(
                channelId,
                frameType,
                flags,
                frameSequenceNumber,
                deadline,
                value1,
                value2,
                payload);
        }

        private static DataChannelFrame Probe(
            uint channelId,
            DataChannelFrameType frameType,
            uint frameSequenceNumber,
            long timestamp)
        {
            return new DataChannelFrame(
                channelId,
                frameType,
                DataChannelFrameFlags.None,
                frameSequenceNumber,
                0,
                (uint)((ulong)timestamp & 0xFFFFFFFFu),
                (uint)((ulong)timestamp >> 32),
                ReadOnlyMemory<byte>.Empty);
        }

        private readonly uint m_value1;
        private readonly uint m_value2;
    }
}
