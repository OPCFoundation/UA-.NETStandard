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
            Volatile.Write(ref m_started, 0);
            Interlocked.Exchange(ref m_startTicks, kNotStarted);
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
