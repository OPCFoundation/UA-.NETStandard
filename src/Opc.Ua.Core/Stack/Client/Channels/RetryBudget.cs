// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// Default thread-safe retry budget based on a monotonic
    /// <see cref="TimeProvider"/> clock.
    /// </summary>
    public sealed class RetryBudget : IRetryBudget
    {
        /// <summary>
        /// Initializes a new retry budget.
        /// </summary>
        /// <param name="maxTotalTime">Maximum elapsed time. Use
        /// <see cref="Timeout.InfiniteTimeSpan"/> for an unlimited
        /// budget.</param>
        /// <param name="timeProvider">Optional time provider.</param>
        public RetryBudget(TimeSpan maxTotalTime, TimeProvider? timeProvider = null)
        {
            if (maxTotalTime < TimeSpan.Zero && maxTotalTime != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(maxTotalTime));
            }

            m_maxTotalTime = maxTotalTime == Timeout.InfiniteTimeSpan
                ? TimeSpan.MaxValue
                : maxTotalTime;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_startTicks = kNotStarted;
        }

        /// <inheritdoc/>
        public TimeSpan ElapsedSinceFirstAttempt
        {
            get
            {
                if (Volatile.Read(ref m_started) == 0)
                {
                    return TimeSpan.Zero;
                }

                long startTicks = Volatile.Read(ref m_startTicks);
                return startTicks == kNotStarted
                    ? TimeSpan.Zero
                    : m_timeProvider.GetElapsedTime(startTicks);
            }
        }

        /// <inheritdoc/>
        public bool IsExhausted
        {
            get
            {
                if (m_maxTotalTime == TimeSpan.MaxValue)
                {
                    return false;
                }

                if (Volatile.Read(ref m_started) == 0)
                {
                    return false;
                }

                long startTicks = Volatile.Read(ref m_startTicks);
                return startTicks != kNotStarted &&
                    m_timeProvider.GetElapsedTime(startTicks) >= m_maxTotalTime;
            }
        }

        /// <inheritdoc/>
        public bool TryConsume(out TimeSpan remaining)
        {
            long startTicks = EnsureStarted();
            if (m_maxTotalTime == TimeSpan.MaxValue)
            {
                remaining = TimeSpan.MaxValue;
                return true;
            }

            TimeSpan elapsed = m_timeProvider.GetElapsedTime(startTicks);
            if (elapsed >= m_maxTotalTime)
            {
                remaining = TimeSpan.Zero;
                return false;
            }

            remaining = m_maxTotalTime - elapsed;
            return true;
        }

        /// <inheritdoc/>
        public void Reset()
        {
            // Order matters: clear m_startTicks BEFORE m_started so a concurrent
            // EnsureStarted observer cannot see "started=1" with a stale tick value.
            // EnsureStarted writes m_started=1 only after the CAS on m_startTicks
            // succeeds, so resetting m_startTicks first preserves that invariant.
            Interlocked.Exchange(ref m_startTicks, kNotStarted);
            Volatile.Write(ref m_started, 0);
        }

        private long EnsureStarted()
        {
            long startTicks = Volatile.Read(ref m_startTicks);
            if (startTicks != kNotStarted)
            {
                return startTicks;
            }

            long nowTicks = m_timeProvider.GetTimestamp();
            long observed = Interlocked.CompareExchange(
                ref m_startTicks,
                nowTicks,
                kNotStarted);
            if (observed == kNotStarted)
            {
                Volatile.Write(ref m_started, 1);
                return nowTicks;
            }

            return observed;
        }

        private const long kNotStarted = long.MinValue;
        private readonly TimeSpan m_maxTotalTime;
        private readonly TimeProvider m_timeProvider;
        private long m_startTicks;
        private int m_started;
    }
}
