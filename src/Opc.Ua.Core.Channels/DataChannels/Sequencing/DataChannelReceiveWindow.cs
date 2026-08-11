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

using System.Collections.Generic;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// What a receiver does with a DATA frame it has just decoded
    /// (Part 6 errata 5.2.1).
    /// </summary>
    public enum DataChannelReceiveOutcome
    {
        /// <summary>
        /// The frame is in sequence. Deliver it and advance
        /// HighestReceived.
        /// </summary>
        Deliver,

        /// <summary>
        /// The frame is ahead by more than one. The intervening range is
        /// lost: report the gap to the application, deliver this frame
        /// and advance HighestReceived.
        /// </summary>
        DeliverWithGap,

        /// <summary>
        /// The number falls inside a run already named by a GAP frame.
        /// Discard without delivering.
        /// </summary>
        DiscardGapped,

        /// <summary>
        /// A duplicate or a datagram retransmission. Discard silently and
        /// do not report a gap.
        /// </summary>
        DiscardDuplicate,

        /// <summary>
        /// The number is behind HighestReceived by more than the replay
        /// window. Reset the channel with Bad_DataChannelClosed.
        /// </summary>
        Reset
    }

    /// <summary>
    /// The per channel, per direction receive state a data channel
    /// maintains: HighestReceived, the replay window and the bounded set
    /// of retained GAP runs (Part 6 errata 5.2.1).
    /// </summary>
    /// <remarks>
    /// Only DATA frames reach <see cref="Accept"/>. Control frames carry
    /// a FrameSequenceNumber for audit but never advance HighestReceived:
    /// were a GAP to advance it, the GAP announcing an expiry would push
    /// HighestReceived past a lower numbered frame that survived and is
    /// still to be transmitted, and the receiver would then discard as a
    /// duplicate precisely the frame the per run rule exists to protect.
    /// </remarks>
    public sealed class DataChannelReceiveWindow
    {
        /// <summary>
        /// Creates a receive window.
        /// </summary>
        /// <param name="replayWindow">How far below HighestReceived a
        /// frame may arrive and still be read as a duplicate rather than
        /// a protocol error. Values below the specified minimum are
        /// raised to it.</param>
        /// <param name="maxGapRuns">The absolute bound on retained GAP
        /// runs. Adding beyond it discards the oldest.</param>
        public DataChannelReceiveWindow(
            int replayWindow = DataChannelConstants.MinReplayWindow,
            int maxGapRuns = DataChannelConstants.DefaultMaxGapRuns)
        {
            m_replayWindow = replayWindow < DataChannelConstants.MinReplayWindow
                ? DataChannelConstants.MinReplayWindow
                : replayWindow;
            m_maxGapRuns = maxGapRuns < 1 ? 1 : maxGapRuns;
        }

        /// <summary>
        /// True once a DATA frame or a GAP frame has established
        /// <see cref="HighestReceived"/>.
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// The highest FrameSequenceNumber accepted in this direction, in
        /// the arithmetic of Part 6 errata 5.2.1.
        /// </summary>
        public uint HighestReceived { get; private set; }

        /// <summary>
        /// The number of GAP runs currently retained.
        /// </summary>
        public int RetainedGapRuns => m_gapRuns.Count;

        /// <summary>
        /// The number of runs evicted because the absolute bound was
        /// reached. A non zero value means a peer is producing GAP runs
        /// faster than they can retire.
        /// </summary>
        public long EvictedGapRuns { get; private set; }

        /// <summary>
        /// Classifies a DATA frame and updates the window.
        /// </summary>
        /// <param name="frameSequenceNumber">The number the frame
        /// carries.</param>
        /// <param name="missingFrom">The first number of the lost range
        /// when the outcome is
        /// <see cref="DataChannelReceiveOutcome.DeliverWithGap"/>.</param>
        /// <param name="missingTo">The last number of the lost
        /// range.</param>
        public DataChannelReceiveOutcome Accept(
            uint frameSequenceNumber,
            out uint missingFrom,
            out uint missingTo)
        {
            missingFrom = 0;
            missingTo = 0;

            if (IsGapped(frameSequenceNumber))
            {
                return DataChannelReceiveOutcome.DiscardGapped;
            }

            if (!IsInitialized)
            {
                IsInitialized = true;
                HighestReceived = frameSequenceNumber;
                return DataChannelReceiveOutcome.Deliver;
            }

            if (DataChannelSequence.IsAfter(frameSequenceNumber, HighestReceived))
            {
                uint distance = DataChannelSequence.Distance(
                    HighestReceived,
                    frameSequenceNumber);

                if (distance == 1)
                {
                    HighestReceived = frameSequenceNumber;
                    RetireGapRuns();
                    return DataChannelReceiveOutcome.Deliver;
                }

                missingFrom = DataChannelSequence.Next(HighestReceived);
                missingTo = DataChannelSequence.Previous(frameSequenceNumber);
                HighestReceived = frameSequenceNumber;
                RetireGapRuns();
                return DataChannelReceiveOutcome.DeliverWithGap;
            }

            uint behind = DataChannelSequence.Distance(frameSequenceNumber, HighestReceived);

            return behind <= (uint)m_replayWindow
                ? DataChannelReceiveOutcome.DiscardDuplicate
                : DataChannelReceiveOutcome.Reset;
        }

        /// <summary>
        /// Records a run named by a GAP frame so later frames inside it
        /// are discarded without delivery.
        /// </summary>
        /// <param name="firstDiscarded">The first number of the run,
        /// inclusive.</param>
        /// <param name="lastDiscarded">The last number of the run,
        /// inclusive.</param>
        /// <remarks>
        /// A GAP may legitimately name a run at or after HighestReceived
        /// and may legitimately precede the first DATA frame on the
        /// channel: control frames are drained ahead of credit gated DATA
        /// and, over opc.quic, control frames travel on the stream while
        /// DATA travels by datagram with no ordering between them. Where
        /// no DATA has yet been accepted, HighestReceived is initialized
        /// to the number below the run rather than the frame rejected.
        /// </remarks>
        public void RecordGap(uint firstDiscarded, uint lastDiscarded)
        {
            if (!IsInitialized)
            {
                IsInitialized = true;
                HighestReceived = DataChannelSequence.Previous(firstDiscarded);
            }

            if (m_gapRuns.Count >= m_maxGapRuns)
            {
                m_gapRuns.RemoveAt(0);
                EvictedGapRuns++;
            }

            m_gapRuns.Add(new GapRun(firstDiscarded, lastDiscarded));
        }

        /// <summary>
        /// True when a number falls inside a retained GAP run.
        /// </summary>
        /// <param name="frameSequenceNumber">The number under test.</param>
        public bool IsGapped(uint frameSequenceNumber)
        {
            for (int ii = 0; ii < m_gapRuns.Count; ii++)
            {
                GapRun run = m_gapRuns[ii];

                if (DataChannelSequence.Distance(run.First, frameSequenceNumber) <=
                    DataChannelSequence.Distance(run.First, run.Last))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Drops runs that no frame can legitimately fall into any more,
        /// because DATA is transmitted in ascending order and
        /// HighestReceived has moved past the run by more than the replay
        /// window.
        /// </summary>
        private void RetireGapRuns()
        {
            for (int ii = m_gapRuns.Count - 1; ii >= 0; ii--)
            {
                GapRun run = m_gapRuns[ii];

                if (DataChannelSequence.IsAfter(HighestReceived, run.Last) &&
                    DataChannelSequence.Distance(run.Last, HighestReceived) >
                        (uint)m_replayWindow)
                {
                    m_gapRuns.RemoveAt(ii);
                }
            }
        }

        private readonly struct GapRun
        {
            public GapRun(uint first, uint last)
            {
                First = first;
                Last = last;
            }

            public uint First { get; }

            public uint Last { get; }
        }

        private readonly List<GapRun> m_gapRuns = [];
        private readonly int m_replayWindow;
        private readonly int m_maxGapRuns;
    }
}
