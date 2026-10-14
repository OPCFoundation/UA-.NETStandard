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

namespace Opc.Ua
{
    /// <summary>
    /// Stops running the requests of one connection on the thread that receives
    /// them, for a while, once they hold that thread longer than a budget.
    /// </summary>
    /// <remarks>
    /// A request run inline holds the receive loop of its connection: nothing more
    /// is read from it until the request completes or waits. The transport makes
    /// the valve of the connection it dispatches for <see cref="Current"/>, the
    /// request queue asks it before running a request inline and reports how long
    /// the request held the thread.
    /// <para>
    /// The time is wall-clock time, so a garbage collection or the operating
    /// system descheduling the thread adds to it. A single overrun therefore does
    /// not count; two in a row, or one on the first request after a pause, pause
    /// inline execution for <see cref="kFirstPause"/>, doubling up to
    /// <see cref="kMaxPauseDoublings"/> times while the overruns persist. A request
    /// within the budget after a pause resets the doubling.
    /// </para>
    /// <para>
    /// Not thread safe: a connection dispatches one received message at a time,
    /// and the valve is only used while it does.
    /// </para>
    /// </remarks>
    internal sealed class InlineExecutionValve
    {
        /// <summary>
        /// Creates the valve of one connection.
        /// </summary>
        /// <param name="timeProvider">The clock the connection uses.</param>
        public InlineExecutionValve(TimeProvider timeProvider)
        {
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        /// <summary>
        /// The valve of the connection whose received message the current thread
        /// dispatches, if any.
        /// </summary>
        public static InlineExecutionValve? Current => t_current;

        /// <summary>
        /// Makes <paramref name="valve"/> the <see cref="Current"/> valve of the
        /// current thread.
        /// </summary>
        /// <returns>The value to pass to <see cref="Exit"/>.</returns>
        public static InlineExecutionValve? Enter(InlineExecutionValve? valve)
        {
            InlineExecutionValve? previous = t_current;
            t_current = valve;
            return previous;
        }

        /// <summary>
        /// Ends what <see cref="Enter"/> began on the current thread.
        /// </summary>
        /// <param name="previous">The value <see cref="Enter"/> returned.</param>
        public static void Exit(InlineExecutionValve? previous)
        {
            t_current = previous;
        }

        /// <summary>
        /// Whether inline execution is paused on the connection now.
        /// </summary>
        public bool IsPaused => m_paused && m_timeProvider.GetTimestamp() < m_pausedUntil;

        /// <summary>
        /// Asks whether a request may run inline.
        /// </summary>
        /// <param name="started">The time to pass to <see cref="Complete"/>.</param>
        /// <returns>False while inline execution is paused.</returns>
        public bool TryBegin(out long started)
        {
            started = m_timeProvider.GetTimestamp();
            if (!m_paused)
            {
                return true;
            }
            if (started < m_pausedUntil)
            {
                return false;
            }
            m_paused = false;
            m_probing = true;
            return true;
        }

        /// <summary>
        /// Reports that a request <see cref="TryBegin"/> let run inline returned
        /// the thread.
        /// </summary>
        /// <param name="started">The time <see cref="TryBegin"/> returned.</param>
        /// <param name="budget">How long the request may hold the thread.</param>
        /// <param name="held">How long the request held the thread.</param>
        /// <returns>
        /// How long inline execution is now paused, or <see cref="TimeSpan.Zero"/>.
        /// </returns>
        public TimeSpan Complete(long started, TimeSpan budget, out TimeSpan held)
        {
            long now = m_timeProvider.GetTimestamp();
            held = m_timeProvider.GetElapsedTime(started, now);
            if (held <= budget)
            {
                m_overran = false;
                if (m_probing)
                {
                    m_probing = false;
                    m_pauses = 0;
                }
                return TimeSpan.Zero;
            }
            if (!m_overran && !m_probing)
            {
                m_overran = true;
                return TimeSpan.Zero;
            }

            m_overran = false;
            m_probing = false;
            var pause = TimeSpan.FromTicks(kFirstPause.Ticks << m_pauses);
            if (m_pauses < kMaxPauseDoublings)
            {
                m_pauses++;
            }
            m_paused = true;
            m_pausedUntil = now + (long)(pause.TotalSeconds * m_timeProvider.TimestampFrequency);
            return pause;
        }

        /// <summary>
        /// How long the first pause lasts.
        /// </summary>
        internal static readonly TimeSpan kFirstPause = TimeSpan.FromSeconds(1);

        /// <summary>
        /// How often the pause doubles at most, to about a minute.
        /// </summary>
        internal const int kMaxPauseDoublings = 6;

        [ThreadStatic]
        private static InlineExecutionValve? t_current;

        private readonly TimeProvider m_timeProvider;

        /// <summary>
        /// The last request run inline exceeded the budget.
        /// </summary>
        private bool m_overran;

        /// <summary>
        /// Inline execution is paused until <see cref="m_pausedUntil"/>.
        /// </summary>
        private bool m_paused;

        /// <summary>
        /// The next request run inline is the first after a pause.
        /// </summary>
        private bool m_probing;

        /// <summary>
        /// The pauses in a row, which the next pause doubles for.
        /// </summary>
        private int m_pauses;

        private long m_pausedUntil;
    }
}
