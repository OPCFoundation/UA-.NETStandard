// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Scheduling
{
    /// <summary>
    /// Schedules periodic publish or receive callbacks driven by a
    /// <see cref="PubSubSchedule"/>. Implementations bridge a
    /// <c>PeriodicTimer</c>, an OS RT timer
    /// or a deterministic test clock onto a single async surface.
    /// </summary>
    /// <remarks>
    /// Implements the periodic scheduling abstraction required by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.4.1">
    /// Part 14 §6.4.1 Periodic publishing</see>. A default
    /// implementation is provided.
    /// </remarks>
    public interface IPubSubScheduler
    {
        /// <summary>
        /// Registers <paramref name="action"/> to be invoked once
        /// per <see cref="PubSubSchedule.Period"/> at the configured
        /// <see cref="PubSubSchedule.PublishingOffset"/> (or
        /// <see cref="PubSubSchedule.ReceiveOffset"/>) of every
        /// period boundary.
        /// </summary>
        /// <param name="schedule">Schedule parameters.</param>
        /// <param name="action">
        /// Async callback invoked on each tick. Long-running work
        /// must respect the supplied <see cref="CancellationToken"/>.
        /// </param>
        /// <param name="cancellationToken">
        /// Token cancelling the registration attempt itself.
        /// </param>
        /// <returns>
        /// A handle whose <see cref="IAsyncDisposable.DisposeAsync"/>
        /// cancels the schedule and waits for the in-flight callback
        /// to drain.
        /// </returns>
        ValueTask<IAsyncDisposable> ScheduleAsync(
            PubSubSchedule schedule,
            Func<CancellationToken, ValueTask> action,
            CancellationToken cancellationToken = default);
    }
}
