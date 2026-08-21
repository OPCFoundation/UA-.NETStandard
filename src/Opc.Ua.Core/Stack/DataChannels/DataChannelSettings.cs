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
    /// The parameters actually in force on one data channel, together
    /// with the timeouts that bound its lifecycle.
    /// </summary>
    /// <remarks>
    /// This is the revised view: every value has already passed through
    /// the negotiation rules of Part 4 errata 5.1.1, so no member here
    /// carries the "no preference" sentinel.
    /// </remarks>
    public sealed record DataChannelSettings
    {
        /// <summary>
        /// The direction payload flows in. Not revisable.
        /// </summary>
        public DataChannelDirection Direction { get; init; }
            = DataChannelDirection.SourceToSink;

        /// <summary>
        /// The delivery guarantee. Not revisable.
        /// </summary>
        public DataChannelDeliveryMode DeliveryMode { get; init; }
            = DataChannelDeliveryMode.ReliableOrdered;

        /// <summary>
        /// The IANA media type of the payload. The data channel layer
        /// never interprets it.
        /// </summary>
        public string ContentType { get; init; } = "application/octet-stream";

        /// <summary>
        /// The largest frame payload in bytes.
        /// </summary>
        public uint MaxFrameSize { get; init; } = 4096;

        /// <summary>
        /// The flow control credit, in payload bytes, granted to the peer
        /// at open. It is never smaller than
        /// <see cref="MaxFrameSize"/>, because a window smaller than one
        /// frame is an immediate deadlock.
        /// </summary>
        public uint InitialCredit { get; init; } = 65536;

        /// <summary>
        /// The scheduling priority, zero lowest to seven highest. It
        /// determines share, never exclusivity.
        /// </summary>
        public byte Priority { get; init; }

        /// <summary>
        /// PartiallyReliable only: attempts before a frame is abandoned.
        /// </summary>
        public ushort MaxRetransmits { get; init; }

        /// <summary>
        /// PartiallyReliable and Unreliable only: how long a frame may
        /// wait in the send queue before it is discarded, in
        /// milliseconds. Zero disables deadline expiry, and a sender
        /// shall not set Droppable when it is zero.
        /// </summary>
        public double FrameDeadline { get; init; }

        /// <summary>
        /// How long the channel may stay in Opening, in milliseconds.
        /// </summary>
        public int OpenTimeout { get; init; } = DataChannelConstants.DefaultOpenTimeout;

        /// <summary>
        /// How long a peer's own drain of a direction may take before the
        /// channel faults, in milliseconds. It does not bound the wait
        /// for the peer's reverse END.
        /// </summary>
        public int DrainTimeout { get; init; } = DataChannelConstants.DefaultDrainTimeout;

        /// <summary>
        /// How long an Open channel may carry no DATA before the server
        /// may reset it, in milliseconds. Zero disables the timeout.
        /// </summary>
        public int IdleTimeout { get; init; }

        /// <summary>
        /// The absolute bound on retained GAP runs per direction.
        /// </summary>
        public int MaxGapRuns { get; init; } = DataChannelConstants.DefaultMaxGapRuns;

        /// <summary>
        /// True when the mode permits a frame to be discarded before
        /// transmission, which is what makes Droppable and GAP legal.
        /// </summary>
        public bool AllowsDiscard
            => DeliveryMode is DataChannelDeliveryMode.PartiallyReliable
                or DataChannelDeliveryMode.Unreliable;

        /// <summary>
        /// True when this peer may send DATA given the role it plays.
        /// </summary>
        /// <param name="isSource">True when this peer is the data channel
        /// source, which is normally the server.</param>
        public bool CanSendData(bool isSource)
        {
            switch (Direction)
            {
                case DataChannelDirection.SourceToSink:
                    return isSource;
                case DataChannelDirection.SinkToSource:
                    return !isSource;
                default:
                    return true;
            }
        }

        /// <summary>
        /// The absolute deadline of a frame enqueued now, in 100
        /// nanosecond intervals since 1601-01-01 UTC, or zero when the
        /// channel has no deadline.
        /// </summary>
        /// <param name="timeProvider">The clock to read.</param>
        public long ComputeDeadline(TimeProvider timeProvider)
        {
            if (FrameDeadline <= 0)
            {
                return 0;
            }

            long now = timeProvider.GetUtcNow().UtcDateTime.ToFileTimeUtc();
            return now + (long)(FrameDeadline * DataChannelConstants.DeadlineTicksPerMillisecond);
        }

        /// <summary>
        /// Builds a settings snapshot from the revised parameters a
        /// Service returned.
        /// </summary>
        /// <param name="parameters">The revised parameters.</param>
        public static DataChannelSettings FromParameters(
            DataChannelParametersDataType parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            return new DataChannelSettings
            {
                Direction = parameters.Direction,
                DeliveryMode = parameters.DeliveryMode,
                ContentType = string.IsNullOrEmpty(parameters.ContentType)
                    ? "application/octet-stream"
                    : parameters.ContentType,
                MaxFrameSize = parameters.MaxFrameSize,
                InitialCredit = parameters.InitialCredit,
                Priority = parameters.Priority,
                MaxRetransmits = parameters.MaxRetransmits,
                FrameDeadline = parameters.FrameDeadline
            };
        }

        /// <summary>
        /// Projects the settings back onto the wire structure.
        /// </summary>
        public DataChannelParametersDataType ToParameters()
        {
            return new DataChannelParametersDataType
            {
                Direction = Direction,
                DeliveryMode = DeliveryMode,
                ContentType = ContentType,
                MaxFrameSize = MaxFrameSize,
                InitialCredit = InitialCredit,
                Priority = Priority,
                MaxRetransmits = MaxRetransmits,
                FrameDeadline = FrameDeadline
            };
        }
    }
}
