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
using System.Collections.Generic;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// A contiguous, inclusive run of FrameSequenceNumbers a sender
    /// discarded before transmission (Part 6 errata 5.10).
    /// </summary>
    /// <remarks>
    /// Per frame deadlines make a non contiguous discard set the normal
    /// case rather than the exception, so a sender emits one GAP frame
    /// per contiguous run and never widens a range across a surviving
    /// frame.
    /// </remarks>
    public readonly record struct DataChannelGapRun
    {
        /// <summary>
        /// Creates a run.
        /// </summary>
        /// <param name="first">The first discarded number, inclusive.</param>
        /// <param name="last">The last discarded number, inclusive.</param>
        public DataChannelGapRun(uint first, uint last)
        {
            First = first;
            Last = last;
        }

        /// <summary>
        /// The first discarded number, inclusive.
        /// </summary>
        public uint First { get; }

        /// <summary>
        /// The last discarded number, inclusive.
        /// </summary>
        public uint Last { get; }
    }

    /// <summary>
    /// The send side of one data channel direction: the per channel
    /// FrameSequenceNumber counter, the payload queue, the control frame
    /// queue and the deadline expiry that produces GAP runs.
    /// </summary>
    /// <remarks>
    /// A DATA frame is assigned its FrameSequenceNumber when it is
    /// enqueued and DATA frames leave in ascending order. Assignment at
    /// enqueue is what allows a GAP frame to name a frame that was never
    /// transmitted; ascending transmission is what keeps the receiver's
    /// arithmetic monotonic despite the holes expiry leaves behind.
    /// Control frames take a number from the same counter but are exempt
    /// from flow control and from the scheduler's deficit, so they are
    /// held in their own queue and drained first.
    /// </remarks>
    internal sealed class DataChannelSendQueue
    {
        /// <summary>
        /// Creates a send queue.
        /// </summary>
        /// <param name="bufferManager">The pool payload buffers are
        /// rented from.</param>
        public DataChannelSendQueue(BufferManager bufferManager)
        {
            m_bufferManager = bufferManager;
        }

        /// <summary>
        /// The deficit counter the scheduler maintains for this channel.
        /// </summary>
        public long Deficit { get; set; }

        /// <summary>
        /// True when a control frame is waiting. Control frames are
        /// exempt from credit and from the deficit.
        /// </summary>
        public bool HasControlFrames => m_control.Count > 0;

        /// <summary>
        /// True when a DATA frame is waiting.
        /// </summary>
        public bool HasPayload => m_payload.Count > 0;

        /// <summary>
        /// The number of DATA frames waiting.
        /// </summary>
        public int PayloadCount => m_payload.Count;

        /// <summary>
        /// The FrameSequenceNumber the next frame will be assigned.
        /// </summary>
        public uint NextSequenceNumber => m_nextSequenceNumber;

        /// <summary>
        /// Takes the next FrameSequenceNumber. Control frames consume one
        /// from the same counter as DATA frames.
        /// </summary>
        public uint TakeSequenceNumber()
        {
            uint value = m_nextSequenceNumber;
            m_nextSequenceNumber = DataChannelSequence.Next(m_nextSequenceNumber);
            return value;
        }

        /// <summary>
        /// Enqueues payload, copying it into a pooled buffer so the
        /// caller may reuse its own.
        /// </summary>
        /// <param name="payload">The payload bytes.</param>
        /// <param name="flags">The flags, excluding DeadlinePresent which
        /// is derived from <paramref name="deadline"/>.</param>
        /// <param name="deadline">The expiry instant in 100 nanosecond
        /// intervals since 1601-01-01 UTC, or zero for none.</param>
        /// <returns>The FrameSequenceNumber assigned.</returns>
        public uint Enqueue(
            ReadOnlySpan<byte> payload,
            DataChannelFrameFlags flags,
            long deadline)
        {
            uint sequenceNumber = TakeSequenceNumber();

            byte[]? buffer = null;

            if (payload.Length > 0)
            {
                buffer = m_bufferManager.TakeBuffer(payload.Length, nameof(DataChannelSendQueue));
                payload.CopyTo(buffer.AsSpan(0, payload.Length));
            }

            m_payload.Enqueue(new QueuedFrame(sequenceNumber, flags, deadline, buffer, payload.Length));
            return sequenceNumber;
        }

        /// <summary>
        /// Enqueues a control frame, which is exempt from flow control
        /// and from the scheduler's deficit.
        /// </summary>
        /// <param name="frame">The frame, already carrying its
        /// FrameSequenceNumber.</param>
        public void EnqueueControl(in DataChannelFrame frame)
        {
            m_control.Enqueue(frame);
        }

        /// <summary>
        /// Takes the next control frame.
        /// </summary>
        /// <param name="frame">The frame.</param>
        public bool TryDequeueControl(out DataChannelFrame frame)
        {
            if (m_control.Count == 0)
            {
                frame = default;
                return false;
            }

            frame = m_control.Dequeue();
            return true;
        }

        /// <summary>
        /// The payload length of the frame at the head of the queue, used
        /// by the credit and deficit tests before it is dequeued.
        /// </summary>
        /// <param name="payloadLength">The length.</param>
        public bool TryPeekPayloadLength(out int payloadLength)
        {
            if (m_payload.Count == 0)
            {
                payloadLength = 0;
                return false;
            }

            payloadLength = m_payload.Peek().Length;
            return true;
        }

        /// <summary>
        /// Takes the DATA frame at the head of the queue.
        /// </summary>
        /// <param name="channelId">The channel the frame belongs to.</param>
        /// <param name="frame">The frame. Its payload points into a
        /// pooled buffer that <see cref="ReleaseFrame"/> returns.</param>
        /// <param name="buffer">The buffer to return once the frame has
        /// been handed to the transport.</param>
        public bool TryDequeuePayload(
            uint channelId,
            out DataChannelFrame frame,
            out byte[]? buffer)
        {
            if (m_payload.Count == 0)
            {
                frame = default;
                buffer = null;
                return false;
            }

            QueuedFrame queued = m_payload.Dequeue();
            buffer = queued.Buffer;

            frame = DataChannelFrame.Data(
                channelId,
                queued.SequenceNumber,
                queued.Flags,
                queued.Buffer != null
                    ? new ReadOnlyMemory<byte>(queued.Buffer, 0, queued.Length)
                    : ReadOnlyMemory<byte>.Empty,
                queued.Deadline);

            return true;
        }

        /// <summary>
        /// Returns a payload buffer to the pool.
        /// </summary>
        /// <param name="buffer">The buffer, which may be null.</param>
        public void ReleaseFrame(byte[]? buffer)
        {
            if (buffer != null)
            {
                m_bufferManager.ReturnBuffer(buffer, nameof(DataChannelSendQueue));
            }
        }

        /// <summary>
        /// Discards queued frames that carry Droppable and whose deadline
        /// has passed, and reports the contiguous runs of numbers that
        /// were discarded.
        /// </summary>
        /// <param name="nowTicks">The sender's clock in 100 nanosecond
        /// intervals since 1601-01-01 UTC.</param>
        /// <param name="runs">One entry per contiguous run. A run never
        /// spans a surviving frame, because widening a range across one
        /// would declare that frame lost and then deliver it.</param>
        /// <returns>The number of frames discarded.</returns>
        public int ExpireDroppable(long nowTicks, List<DataChannelGapRun> runs)
        {
            if (m_payload.Count == 0)
            {
                return 0;
            }

            bool anyExpired = false;

            foreach (QueuedFrame candidate in m_payload)
            {
                if (IsExpired(candidate, nowTicks))
                {
                    anyExpired = true;
                    break;
                }
            }

            if (!anyExpired)
            {
                return 0;
            }

            int discarded = 0;
            bool inRun = false;
            uint runFirst = 0;
            uint runLast = 0;

            int count = m_payload.Count;

            for (int ii = 0; ii < count; ii++)
            {
                QueuedFrame queued = m_payload.Dequeue();

                if (IsExpired(queued, nowTicks))
                {
                    ReleaseFrame(queued.Buffer);
                    discarded++;

                    if (inRun && DataChannelSequence.Next(runLast) == queued.SequenceNumber)
                    {
                        runLast = queued.SequenceNumber;
                    }
                    else
                    {
                        if (inRun)
                        {
                            runs.Add(new DataChannelGapRun(runFirst, runLast));
                        }

                        inRun = true;
                        runFirst = queued.SequenceNumber;
                        runLast = queued.SequenceNumber;
                    }

                    continue;
                }

                if (inRun)
                {
                    runs.Add(new DataChannelGapRun(runFirst, runLast));
                    inRun = false;
                }

                m_payload.Enqueue(queued);
            }

            if (inRun)
            {
                runs.Add(new DataChannelGapRun(runFirst, runLast));
            }

            return discarded;
        }

        /// <summary>
        /// Discards every queued frame, used when a channel is reset or
        /// closed with deleteQueued.
        /// </summary>
        public void Clear()
        {
            while (m_payload.Count > 0)
            {
                ReleaseFrame(m_payload.Dequeue().Buffer);
            }

            m_control.Clear();
            Deficit = 0;
        }

        private static bool IsExpired(in QueuedFrame frame, long nowTicks)
        {
            return (frame.Flags & DataChannelFrameFlags.Droppable) != 0 &&
                frame.Deadline != 0 &&
                frame.Deadline <= nowTicks;
        }

        private readonly struct QueuedFrame
        {
            public QueuedFrame(
                uint sequenceNumber,
                DataChannelFrameFlags flags,
                long deadline,
                byte[]? buffer,
                int length)
            {
                SequenceNumber = sequenceNumber;
                Flags = deadline != 0 ? flags | DataChannelFrameFlags.DeadlinePresent : flags;
                Deadline = deadline;
                Buffer = buffer;
                Length = length;
            }

            public uint SequenceNumber { get; }

            public DataChannelFrameFlags Flags { get; }

            public long Deadline { get; }

            public byte[]? Buffer { get; }

            public int Length { get; }
        }

        private readonly Queue<QueuedFrame> m_payload = new();
        private readonly Queue<DataChannelFrame> m_control = new();
        private readonly BufferManager m_bufferManager;
        private uint m_nextSequenceNumber = DataChannelConstants.FirstFrameSequenceNumber;
    }
}
