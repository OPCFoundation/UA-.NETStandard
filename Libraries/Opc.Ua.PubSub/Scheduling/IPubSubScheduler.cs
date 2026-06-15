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
    /// Part 14 §6.4.1 Periodic publishing</see>. The default
    /// implementation ships in Phase 5; Phase 1 only commits the
    /// contract.
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
