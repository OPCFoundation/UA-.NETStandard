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
using System.Linq;

namespace Opc.Ua.Server.Alarms
{
    /// <summary>
    /// Tracks alarm activations over a sliding window to compute
    /// alarm rates compatible with the Part 9 <c>AlarmMetricsType</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call <see cref="RecordActivation()"/> on each new alarm activation
    /// (transition to active state). The window discards activations
    /// older than the configured <see cref="WindowDuration"/>.
    /// </para>
    /// <para>
    /// Reading <see cref="CurrentAlarmRate"/> reports the number of
    /// activations within the window; <see cref="MaximumAlarmRate"/>
    /// reports the all-time maximum since the metric was reset.
    /// </para>
    /// </remarks>
    public sealed class AlarmRateTracker
    {
        private readonly object m_lock = new();
        private readonly Queue<DateTime> m_activations = new();
        private readonly TimeProvider m_timeProvider;
        private long m_maximumRate;

        /// <summary>
        /// The sliding window duration used to compute the current rate.
        /// </summary>
        public TimeSpan WindowDuration { get; }

        /// <summary>
        /// Initializes a new tracker with the given window duration.
        /// </summary>
        /// <param name="windowDuration">Window duration; default 1 minute.</param>
        public AlarmRateTracker(TimeSpan? windowDuration = null)
            : this(windowDuration, timeProvider: null)
        {
        }

        /// <summary>
        /// Initializes a new tracker with the given window duration and an
        /// explicit <see cref="TimeProvider"/>.
        /// </summary>
        /// <param name="windowDuration">Window duration; default 1 minute.</param>
        /// <param name="timeProvider">
        /// Optional <see cref="TimeProvider"/> used to time-stamp activations
        /// and to trim the sliding window. Falls back to
        /// <see cref="TimeProvider.System"/> when <c>null</c>.
        /// </param>
        public AlarmRateTracker(TimeSpan? windowDuration, TimeProvider? timeProvider)
        {
            WindowDuration = windowDuration ?? TimeSpan.FromMinutes(1);
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Records an alarm activation at the current time.
        /// </summary>
        public void RecordActivation()
        {
            RecordActivation(m_timeProvider.GetUtcNow().UtcDateTime);
        }

        /// <summary>
        /// Records an alarm activation at the specified time.
        /// </summary>
        public void RecordActivation(DateTime time)
        {
            lock (m_lock)
            {
                m_activations.Enqueue(time);
                TrimOldEntries(time);

                if (m_activations.Count > m_maximumRate)
                {
                    m_maximumRate = m_activations.Count;
                }
            }
        }

        /// <summary>
        /// The number of activations in the current window.
        /// </summary>
        public long CurrentAlarmRate
        {
            get
            {
                lock (m_lock)
                {
                    TrimOldEntries(m_timeProvider.GetUtcNow().UtcDateTime);
                    return m_activations.Count;
                }
            }
        }

        /// <summary>
        /// The maximum observed activation count within any window
        /// since the tracker was created or reset.
        /// </summary>
        public long MaximumAlarmRate
        {
            get
            {
                lock (m_lock)
                {
                    return m_maximumRate;
                }
            }
        }

        /// <summary>
        /// Resets all counters and clears the activation history.
        /// </summary>
        public void Reset()
        {
            lock (m_lock)
            {
                m_activations.Clear();
                m_maximumRate = 0;
            }
        }

        /// <summary>
        /// Returns the activation timestamps currently inside the window.
        /// </summary>
        public IReadOnlyList<DateTime> GetActivationsInWindow()
        {
            lock (m_lock)
            {
                TrimOldEntries(m_timeProvider.GetUtcNow().UtcDateTime);
                return m_activations.ToArray();
            }
        }

        private void TrimOldEntries(DateTime now)
        {
            DateTime cutoff = now - WindowDuration;
            while (m_activations.Count > 0 && m_activations.Peek() < cutoff)
            {
                m_activations.Dequeue();
            }
        }
    }
}
