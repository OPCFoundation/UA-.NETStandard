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
    /// One unit of payload delivered to the application, together with
    /// the loss information the data channel layer detected around it.
    /// </summary>
    /// <remarks>
    /// The payload occupies a pooled buffer. Disposing the message
    /// returns that buffer and, as Part 6 errata 5.8.2 requires, is what
    /// releases flow control credit back to the sender: an application
    /// that never disposes its messages stalls the channel it is reading.
    /// </remarks>
    public sealed class DataChannelMessage : IDisposable
    {
        /// <summary>
        /// Creates a message.
        /// </summary>
        /// <param name="owner">The channel that delivered it.</param>
        /// <param name="buffer">The pooled buffer holding the payload.</param>
        /// <param name="length">The payload length.</param>
        /// <param name="flags">The flags the frame carried.</param>
        /// <param name="frameSequenceNumber">The number the frame
        /// carried.</param>
        /// <param name="status">Good, or Uncertain_DataDiscarded when
        /// frames were lost immediately before this one.</param>
        /// <param name="gapFrom">The first lost number, when a gap
        /// preceded this frame.</param>
        /// <param name="gapTo">The last lost number.</param>
        internal DataChannelMessage(
            DataChannel owner,
            byte[]? buffer,
            int length,
            DataChannelFrameFlags flags,
            uint frameSequenceNumber,
            StatusCode status,
            uint gapFrom,
            uint gapTo)
        {
            m_owner = owner;
            m_buffer = buffer;
            Length = length;
            Flags = flags;
            FrameSequenceNumber = frameSequenceNumber;
            Status = status;
            GapFrom = gapFrom;
            GapTo = gapTo;
        }

        /// <summary>
        /// The payload bytes.
        /// </summary>
        public ByteString Payload
            => m_buffer != null && !m_disposed
                ? new ByteString(new ReadOnlyMemory<byte>(m_buffer, 0, Length))
                : ByteString.Empty;

        /// <summary>
        /// The payload length in bytes.
        /// </summary>
        public int Length { get; }

        /// <summary>
        /// The flags the frame carried.
        /// </summary>
        public DataChannelFrameFlags Flags { get; }

        /// <summary>
        /// The FrameSequenceNumber the frame carried.
        /// </summary>
        public uint FrameSequenceNumber { get; }

        /// <summary>
        /// Good, or Uncertain_DataDiscarded when the delivered stream is
        /// incomplete because frames were discarded or lost.
        /// </summary>
        public StatusCode Status { get; }

        /// <summary>
        /// The first FrameSequenceNumber lost immediately before this
        /// frame, or zero when none were.
        /// </summary>
        public uint GapFrom { get; }

        /// <summary>
        /// The last FrameSequenceNumber lost immediately before this
        /// frame, or zero when none were.
        /// </summary>
        public uint GapTo { get; }

        /// <summary>
        /// True when this frame begins a logical application message.
        /// </summary>
        public bool IsMessageStart
            => (Flags & DataChannelFrameFlags.MessageStart) != 0;

        /// <summary>
        /// True when this frame ends a logical application message.
        /// </summary>
        public bool IsMessageEnd
            => (Flags & DataChannelFrameFlags.MessageEnd) != 0;

        /// <summary>
        /// True when the sender marked this frame as a synchronization
        /// point, for example a video key frame. A receiver that has just
        /// recovered from a gap can resume here without understanding the
        /// payload.
        /// </summary>
        public bool IsMarker
            => (Flags & DataChannelFrameFlags.Marker) != 0;

        /// <summary>
        /// Returns the payload buffer to the pool and releases the flow
        /// control credit it occupied.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            m_owner.ReleaseMessage(m_buffer, Length);
            m_buffer = null;
        }

        private readonly DataChannel m_owner;
        private byte[]? m_buffer;
        private bool m_disposed;
    }
}
