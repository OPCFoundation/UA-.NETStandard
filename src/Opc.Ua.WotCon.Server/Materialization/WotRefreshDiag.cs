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

using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// [DEBUG-G10-RF1] Temporary diagnostics-only timing collector for one coordinator refresh.
    /// Records numeric phase timings and counts only. Remove together with every
    /// [DEBUG-G10-RF1] marker before the final repair.
    /// </summary>
    internal sealed class WotRefreshDiag
    {
        /// <summary>
        /// Starts timing a refresh and emits the queue marker when the mutex is already held.
        /// </summary>
        public WotRefreshDiag(ILogger? logger, int mutexAvailable, int kind)
        {
            m_logger = logger;
            m_kind = kind;
            m_id = Interlocked.Increment(ref s_nextId);
            m_entry = Stopwatch.GetTimestamp();
            m_waiting = Interlocked.Increment(ref s_waiting);
            if (mutexAvailable == 0)
            {
                m_logger?.RefreshQueued(m_id, m_kind, m_waiting);
            }
        }

        /// <summary>
        /// Marks the mutex as acquired.
        /// </summary>
        public void Acquired()
        {
            Interlocked.Decrement(ref s_waiting);
            m_acquired = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Marks the end of the closure build phase.
        /// </summary>
        public void Built(int enabled, int closures)
        {
            m_built = Stopwatch.GetTimestamp();
            m_enabled = enabled;
            m_closures = closures;
        }

        /// <summary>
        /// Marks the end of the native partition phase.
        /// </summary>
        public void Native()
        {
            m_native = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Marks the end of the retirement reconcile phase.
        /// </summary>
        public void Retired()
        {
            m_retired = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Marks the end of the closure loop.
        /// </summary>
        public void Looped(int results)
        {
            m_looped = Stopwatch.GetTimestamp();
            m_results = results;
        }

        /// <summary>
        /// Marks the end of the apply phase.
        /// </summary>
        public void Applied()
        {
            m_applied = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Marks the refresh as returning a result.
        /// </summary>
        public void Done(bool rejected)
        {
            m_outcome = rejected ? 2 : 1;
        }

        /// <summary>
        /// Emits the single summary line.
        /// </summary>
        public void Finish(bool cancellationRequested)
        {
            long end = Stopwatch.GetTimestamp();
            if (m_acquired == 0)
            {
                Interlocked.Decrement(ref s_waiting);
            }
            string outcome = m_outcome switch
            {
                1 => "completed",
                2 => "rejected",
                _ => cancellationRequested ? "cancelled" : "faulted"
            };
            long acquired = m_acquired == 0 ? end : m_acquired;
            long built = m_built == 0 ? 0 : Ms(acquired, m_built);
            m_logger?.RefreshSummary(
                m_id,
                m_kind,
                outcome,
                Ms(m_entry, acquired),
                built,
                m_native == 0 ? 0 : Ms(m_built, m_native),
                m_retired == 0 ? 0 : Ms(m_native, m_retired),
                m_looped == 0 ? 0 : Ms(m_retired, m_looped),
                m_applied == 0 ? 0 : Ms(m_looped, m_applied),
                Ms(m_entry, end),
                m_enabled,
                m_closures,
                m_results);
        }

        private static long Ms(long from, long to)
        {
            return (to - from) * 1000 / Stopwatch.Frequency;
        }

        private static int s_nextId;
        private static int s_waiting;

        private readonly ILogger? m_logger;
        private readonly int m_id;
        private readonly int m_kind;
        private readonly int m_waiting;
        private readonly long m_entry;
        private long m_acquired;
        private long m_built;
        private long m_native;
        private long m_retired;
        private long m_looped;
        private long m_applied;
        private int m_enabled;
        private int m_closures;
        private int m_results;
        private int m_outcome;
    }

    /// <summary>
    /// [DEBUG-G10-RF1] Holds temporary source-generated refresh timing log messages.
    /// </summary>
    internal static partial class WotRefreshDiagLog
    {
        /// <summary>
        /// Logs that a refresh is queued behind another coordinator operation.
        /// </summary>
        [LoggerMessage(EventId = WotConServerEventIds.WotRefreshDiag + 0, Level = LogLevel.Information,
            Message = "[DEBUG-G10-RF1] refresh queued id={RefreshId} kind={Kind} waiting={Waiting}")]
        public static partial void RefreshQueued(this ILogger logger, int refreshId, int kind, int waiting);

        /// <summary>
        /// Logs the aggregate timing of one refresh.
        /// </summary>
        [LoggerMessage(EventId = WotConServerEventIds.WotRefreshDiag + 1, Level = LogLevel.Information,
            Message = "[DEBUG-G10-RF1] refresh id={RefreshId} kind={Kind} outcome={Outcome} waitMs={WaitMs} " +
                "buildMs={BuildMs} nativeMs={NativeMs} retireMs={RetireMs} loopMs={LoopMs} " +
                "applyMs={ApplyMs} totalMs={TotalMs} enabled={Enabled} closures={Closures} results={Results}")]
        public static partial void RefreshSummary(
            this ILogger logger,
            int refreshId,
            int kind,
            string outcome,
            long waitMs,
            long buildMs,
            long nativeMs,
            long retireMs,
            long loopMs,
            long applyMs,
            long totalMs,
            int enabled,
            int closures,
            int results);
    }
}
