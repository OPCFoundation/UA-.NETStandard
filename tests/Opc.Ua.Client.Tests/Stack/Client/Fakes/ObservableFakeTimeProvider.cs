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
 *
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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;

namespace Opc.Ua.Client.Tests.Stack.Client.Fakes
{
    /// <summary>
    /// Exposes fake-time timer creation and successful schedule changes so tests can wait for timer registration
    /// before advancing the clock.
    /// </summary>
    /// <remarks>
    /// Observations are not retained. Register each wait before starting the operation whose timer should satisfy it.
    /// </remarks>
    internal sealed class ObservableFakeTimeProvider : FakeTimeProvider
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Reports the new timer's schedule and wraps it to report subsequent successful schedule changes.
        /// </remarks>
        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ITimer timer = base.CreateTimer(callback, state, dueTime, period);
            NotifyTimer(false, dueTime, period);
            return new ObservedTimer(this, timer);
        }

        /// <summary>
        /// Waits for a subsequently created timer with the specified due time, regardless of its period.
        /// </summary>
        /// <param name="dueTime">The exact due time that must be supplied when creating the timer.</param>
        public Task WaitForTimerCreatedAsync(TimeSpan dueTime)
        {
            return WaitForTimerAsync(false, dueTime, dueTime, null);
        }

        /// <summary>
        /// Waits for a subsequently created timer with an infinite period and a due time in the inclusive range.
        /// Returns the due time supplied when that timer was created.
        /// </summary>
        /// <param name="minimumDueTime">The inclusive lower bound for the timer's due time.</param>
        /// <param name="maximumDueTime">The inclusive upper bound for the timer's due time.</param>
        /// <exception cref="ArgumentOutOfRangeException">The maximum due time is less than the minimum.</exception>
        public Task<TimeSpan> WaitForTimerCreatedAsync(TimeSpan minimumDueTime, TimeSpan maximumDueTime)
        {
            return WaitForTimerAsync(false, minimumDueTime, maximumDueTime, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Waits for a subsequent successful timer schedule change matching both the due time and period.
        /// </summary>
        /// <param name="dueTime">The exact due time supplied to the timer's change operation.</param>
        /// <param name="period">The exact period supplied to the timer's change operation.</param>
        public Task WaitForTimerChangedAsync(TimeSpan dueTime, TimeSpan period)
        {
            return WaitForTimerAsync(true, dueTime, dueTime, period);
        }

        private Task<TimeSpan> WaitForTimerAsync(
            bool changed,
            TimeSpan minimumDueTime,
            TimeSpan maximumDueTime,
            TimeSpan? period)
        {
            if (maximumDueTime < minimumDueTime)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDueTime));
            }
            var completion = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (m_lock)
            {
                m_waiters.Add((changed, minimumDueTime, maximumDueTime, period, completion));
            }
            return completion.Task;
        }

        private void NotifyTimer(bool changed, TimeSpan dueTime, TimeSpan period)
        {
            lock (m_lock)
            {
                for (int index = m_waiters.Count - 1; index >= 0; index--)
                {
                    var waiter = m_waiters[index];
                    if (waiter.Changed == changed &&
                        waiter.MinimumDueTime <= dueTime &&
                        waiter.MaximumDueTime >= dueTime &&
                        (!waiter.Period.HasValue || waiter.Period.Value == period))
                    {
                        waiter.Completion.TrySetResult(dueTime);
                        m_waiters.RemoveAt(index);
                    }
                }
            }
        }

        /// <summary>
        /// Forwards timer operations and reports successful schedule changes to the observing clock.
        /// </summary>
        /// <param name="owner">The clock whose pending waits receive schedule-change observations.</param>
        /// <param name="timer">The underlying fake-time timer.</param>
        private sealed class ObservedTimer(ObservableFakeTimeProvider owner, ITimer timer) : ITimer
        {
            /// <inheritdoc/>
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                bool changed = timer.Change(dueTime, period);
                if (changed)
                {
                    owner.NotifyTimer(true, dueTime, period);
                }
                return changed;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                timer.Dispose();
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                return timer.DisposeAsync();
            }
        }

        private readonly Lock m_lock = new();
        private readonly List<(
            bool Changed,
            TimeSpan MinimumDueTime,
            TimeSpan MaximumDueTime,
            TimeSpan? Period,
            TaskCompletionSource<TimeSpan> Completion)> m_waiters = [];
    }
}
