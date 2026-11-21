// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Scheduling
{
    /// <summary>
    /// Publishing / receive cadence parameters for one WriterGroup or
    /// ReaderGroup. Maps the four PubSub timing knobs from Part 14
    /// configuration onto a value type the scheduler can consume
    /// directly.
    /// </summary>
    /// <remarks>
    /// Implements the periodic publishing model described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.4.1">
    /// Part 14 §6.4.1 Periodic publishing</see>. Used by
    /// <see cref="IPubSubScheduler"/> to align local clocks with the
    /// publishing schedule advertised by remote publishers.
    /// </remarks>
    public readonly record struct PubSubSchedule
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubSchedule"/>.
        /// </summary>
        /// <param name="period">
        /// Interval between successive publishes (WriterGroup) or
        /// expected interval between receives (ReaderGroup).
        /// </param>
        /// <param name="keepAliveTime">
        /// Idle period after which a KeepAlive NetworkMessage must
        /// be emitted (publisher) or expected (subscriber).
        /// </param>
        /// <param name="publishingOffset">
        /// Wall-clock offset within <paramref name="period"/> at which
        /// the publisher emits the NetworkMessage. Zero aligns with
        /// the period boundary.
        /// </param>
        /// <param name="receiveOffset">
        /// Wall-clock offset within <paramref name="period"/> at which
        /// the subscriber expects to receive. Used by deterministic
        /// schedulers; zero means accept any time within the period.
        /// </param>
        public PubSubSchedule(
            TimeSpan period,
            TimeSpan keepAliveTime,
            TimeSpan publishingOffset,
            TimeSpan receiveOffset)
        {
            Period = period;
            KeepAliveTime = keepAliveTime;
            PublishingOffset = publishingOffset;
            ReceiveOffset = receiveOffset;
        }

        /// <summary>
        /// Publishing / receive period.
        /// </summary>
        public TimeSpan Period { get; init; }

        /// <summary>
        /// KeepAlive emit / expect interval.
        /// </summary>
        public TimeSpan KeepAliveTime { get; init; }

        /// <summary>
        /// Offset within <see cref="Period"/> at which the publisher
        /// emits the NetworkMessage.
        /// </summary>
        public TimeSpan PublishingOffset { get; init; }

        /// <summary>
        /// Offset within <see cref="Period"/> at which the subscriber
        /// expects to receive.
        /// </summary>
        public TimeSpan ReceiveOffset { get; init; }
    }
}
