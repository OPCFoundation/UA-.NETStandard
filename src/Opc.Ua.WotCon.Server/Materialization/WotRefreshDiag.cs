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
        /// Starts the aggregate part timing of one closure of this refresh.
        /// </summary>
        public WotClosureDiag BeginClosure()
        {
            return new WotClosureDiag(m_logger, m_id, m_kind);
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
    /// [DEBUG-G10-RF1] Temporary diagnostics-only aggregate part timing for one closure.
    /// Parts are disjoint raw-tick accumulators converted to milliseconds once at the end;
    /// time not inside any part is reported as unattributed. Numeric values only.
    /// </summary>
    internal sealed class WotClosureDiag
    {
        /// <summary>
        /// Creates the closure collector.
        /// </summary>
        public WotClosureDiag(ILogger? logger, int refreshId, int kind)
        {
            m_logger = logger;
            m_id = refreshId;
            m_kind = kind;
            m_entry = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Reads the timestamp, or zero when diagnostics are disabled.
        /// </summary>
        public static long Now(WotClosureDiag? diag)
        {
            return diag is null ? 0 : Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Adds the ticks elapsed since the start timestamp to a part.
        /// </summary>
        public static void Add(WotClosureDiag? diag, int part, long start)
        {
            if (diag is not null)
            {
                diag.m_ticks[part] += Stopwatch.GetTimestamp() - start;
            }
        }

        /// <summary>
        /// Records the member loop boundaries and counts.
        /// </summary>
        public static void LoopStart(WotClosureDiag? diag, int members)
        {
            if (diag is not null)
            {
                diag.m_members = members;
                diag.m_loopStart = Stopwatch.GetTimestamp();
            }
        }

        /// <summary>
        /// Records the normal end of the member loop.
        /// </summary>
        public static void LoopEnd(
            WotClosureDiag? diag,
            int converted,
            int projections,
            int plans)
        {
            if (diag is not null)
            {
                diag.m_loopEnd = Stopwatch.GetTimestamp();
                diag.m_converted = converted;
                diag.m_projections = projections;
                diag.m_plans = plans;
            }
        }

        /// <summary>
        /// Records the number of coalesced sources.
        /// </summary>
        public static void Sources(WotClosureDiag? diag, int sources)
        {
            if (diag is not null)
            {
                diag.m_sources = sources;
            }
        }

        /// <summary>
        /// Marks the closure as returning normally.
        /// </summary>
        public void Done()
        {
            m_completed = true;
        }

        /// <summary>
        /// Emits the single parts-summary line.
        /// </summary>
        public void Finish(bool cancellationRequested)
        {
            long end = Stopwatch.GetTimestamp();
            long total = end - m_entry;
            long parts = 0;
            for (int i = 0; i < m_ticks.Length; i++)
            {
                parts += m_ticks[i];
            }
            bool looped = m_loopEnd != 0;
            long loopTicks = looped ? m_loopEnd - m_loopStart : 0;
            long loopParts = m_ticks[Read] + m_ticks[Parse] + m_ticks[Convert] + m_ticks[Contrib] +
                m_ticks[DeclAdd] + m_ticks[PlanReq];
            string outcome = m_completed ? "completed" : cancellationRequested ? "cancelled" : "faulted";
            m_logger?.ClosureParts(
                m_id,
                m_kind,
                outcome,
                m_members,
                m_converted,
                m_projections,
                m_sources,
                m_plans,
                Ms(m_ticks[Read]),
                Ms(m_ticks[Parse]),
                Ms(m_ticks[Convert]),
                Ms(m_ticks[Contrib]),
                Ms(m_ticks[DeclAdd]),
                Ms(m_ticks[PlanReq]),
                looped ? Ms(loopTicks) : -1,
                looped ? Ms(loopTicks - loopParts) : -1,
                Ms(m_ticks[Coalesce]),
                Ms(m_ticks[Resolve]),
                Ms(m_ticks[Prepare]),
                Ms(m_ticks[HostAdd]),
                Ms(m_ticks[Views]),
                Ms(m_ticks[Deactivate]),
                Ms(total - parts),
                Ms(total));
        }

        private static long Ms(long ticks)
        {
            return ticks * 1000 / Stopwatch.Frequency;
        }

        /// <summary>
        /// Part index of reading the member content.
        /// </summary>
        public const int Read = 0;

        /// <summary>
        /// Part index of the projection-document check (full parse).
        /// </summary>
        public const int Parse = 1;

        /// <summary>
        /// Part index of the document conversion.
        /// </summary>
        public const int Convert = 2;

        /// <summary>
        /// Part index of the NodeSet contributors.
        /// </summary>
        public const int Contrib = 3;

        /// <summary>
        /// Part index of adding the NodeSet to the declaration context.
        /// </summary>
        public const int DeclAdd = 4;

        /// <summary>
        /// Part index of building the binding plan request.
        /// </summary>
        public const int PlanReq = 5;

        /// <summary>
        /// Part index of coalescing the projection sources.
        /// </summary>
        public const int Coalesce = 6;

        /// <summary>
        /// Part index of resolving dependency models.
        /// </summary>
        public const int Resolve = 7;

        /// <summary>
        /// Part index of the declaration check and binding plan preparation.
        /// </summary>
        public const int Prepare = 8;

        /// <summary>
        /// Part index of the host add or reload.
        /// </summary>
        public const int HostAdd = 9;

        /// <summary>
        /// Part index of the projection view materialization.
        /// </summary>
        public const int Views = 10;

        /// <summary>
        /// Part index of deactivating the previous plans and views.
        /// </summary>
        public const int Deactivate = 11;

        private readonly ILogger? m_logger;
        private readonly int m_id;
        private readonly int m_kind;
        private readonly long m_entry;
        private readonly long[] m_ticks = new long[12];
        private long m_loopStart;
        private long m_loopEnd;
        private int m_members;
        private int m_converted;
        private int m_projections;
        private int m_sources;
        private int m_plans;
        private bool m_completed;
    }

    /// <summary>
    /// [DEBUG-G10-RF1] Holds temporary source-generated refresh timing log messages.
    /// </summary>
    internal static partial class WotRefreshDiagLog
    {
        /// <summary>
        /// Logs that a refresh is queued behind another coordinator operation.
        /// </summary>
        [LoggerMessage(EventId = WotConServerEventIds.WotRefreshDiag + 0, Level = LogLevel.Warning,
            Message = "[DEBUG-G10-RF1] refresh queued id={RefreshId} kind={Kind} waiting={Waiting}")]
        public static partial void RefreshQueued(this ILogger logger, int refreshId, int kind, int waiting);

        /// <summary>
        /// Logs the aggregate timing of one refresh.
        /// </summary>
        [LoggerMessage(EventId = WotConServerEventIds.WotRefreshDiag + 1, Level = LogLevel.Warning,
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

        /// <summary>
        /// Logs the aggregate part timing of one closure.
        /// </summary>
        [LoggerMessage(EventId = WotConServerEventIds.WotRefreshDiag + 2, Level = LogLevel.Warning,
            Message = "[DEBUG-G10-RF1] closure parts id={RefreshId} kind={Kind} outcome={Outcome} " +
                "members={Members} converted={Converted} projections={Projections} sources={Sources} " +
                "plans={Plans} readMs={ReadMs} parseMs={ParseMs} convertMs={ConvertMs} " +
                "contribMs={ContribMs} declAddMs={DeclAddMs} planReqMs={PlanReqMs} memberLoopMs={MemberLoopMs} " +
                "loopResidualMs={LoopResidualMs} coalesceMs={CoalesceMs} resolveMs={ResolveMs} " +
                "prepareMs={PrepareMs} hostAddMs={HostAddMs} viewsMs={ViewsMs} deactivateMs={DeactivateMs} " +
                "unattributedMs={UnattributedMs} closureMs={ClosureMs}")]
        public static partial void ClosureParts(
            this ILogger logger,
            int refreshId,
            int kind,
            string outcome,
            int members,
            int converted,
            int projections,
            int sources,
            int plans,
            long readMs,
            long parseMs,
            long convertMs,
            long contribMs,
            long declAddMs,
            long planReqMs,
            long memberLoopMs,
            long loopResidualMs,
            long coalesceMs,
            long resolveMs,
            long prepareMs,
            long hostAddMs,
            long viewsMs,
            long deactivateMs,
            long unattributedMs,
            long closureMs);
    }
}
