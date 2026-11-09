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
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Perf.ServerLoadHarness
{
    /// <summary>
    /// Listens to the garbage collection events of its own process (runtime provider, GC
    /// keyword, informational level: a handful of events per collection, no allocation
    /// sampling) and summarizes a measurement window: collections by generation and
    /// trigger, pause and suspension percentiles, bytes promoted per gen0/gen1
    /// collection, the heap count DATAS chose and the gen0 budget.
    /// </summary>
    /// <remarks>
    /// Collections and pauses are tracked separately. A background gen2 overlaps foreground
    /// gen0/gen1 collections and stops the threads more than once, so a collection is
    /// identified by its count from start to end (its heap statistics follow its end), and
    /// every suspend/restart interval is a pause of its own.
    /// </remarks>
    internal sealed class GcListener : EventListener
    {
        /// <summary>
        /// Starts recording the window; earlier collections are dropped.
        /// </summary>
        public void Mark()
        {
            lock (m_lock)
            {
                m_collections.Clear();
                m_open.Clear();
                m_pauses.Clear();
                m_heaps.Clear();
                m_budgets.Clear();
                m_ended = null;
                m_suspendStart = default;
                m_suspendEnd = default;
                // events are dispatched later: the induced collection before the mark is not part of the window
                m_markTime = DateTime.UtcNow;
                m_endTime = DateTime.MaxValue;
                m_sawAfterEnd = false;
                m_recording = true;
            }
        }

        /// <summary>
        /// Ends the window: events stamped after now are not counted, events stamped before
        /// now still are when they arrive later.
        /// </summary>
        public void End()
        {
            lock (m_lock)
            {
                m_endTime = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Waits until the events of the window have been delivered and returns its summary line.
        /// </summary>
        /// <remarks>
        /// Events arrive in order but later than they happen. A gen0 collection induced after
        /// <see cref="End"/> produces events stamped after the window; once one arrives, every
        /// event of the window has arrived too (or the wait gives up after two seconds).
        /// </remarks>
        public async Task<string> ReportAsync()
        {
            GC.Collect(0, GCCollectionMode.Forced, blocking: true);
            var wait = Stopwatch.StartNew();
            while (!Volatile.Read(ref m_sawAfterEnd) && wait.ElapsedMilliseconds < 2000)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }
            return Report();
        }

        private string Report()
        {
            List<Collection> all;
            List<Pause> pauses;
            List<long> heaps;
            List<long> budgets;
            lock (m_lock)
            {
                m_recording = false;
                all = [.. m_collections];
                pauses = [.. m_pauses];
                heaps = [.. m_heaps];
                budgets = [.. m_budgets];
            }

            var text = new System.Text.StringBuilder();
            text.Append(CultureInfo.InvariantCulture,
                $"mode={(GCSettings.IsServerGC ? "server" : "workstation")}{(GCSettings.LatencyMode == GCLatencyMode.Batch ? string.Empty : "+concurrent")} n={all.Count}");
            foreach (IGrouping<string, Collection> g in all
                .GroupBy(c => FormattableString.Invariant($"gen{c.Depth}{(c.Type == 1 ? "bg" : string.Empty)}/{ReasonName(c.Reason)}"))
                .OrderByDescending(g => g.Count()))
            {
                text.Append(CultureInfo.InvariantCulture, $" {g.Key}={g.Count()}");
            }
            if (pauses.Count > 0)
            {
                double[] pauseMs = [.. pauses.Select(p => p.PauseMs).Order()];
                double[] suspendMs = [.. pauses.Select(p => p.SuspendMs).Order()];
                text.Append(CultureInfo.InvariantCulture,
                    $" pauses={pauseMs.Length} p50={Pct(pauseMs, 0.5):F2} p99={Pct(pauseMs, 0.99):F2} max={pauseMs[^1]:F2} sum={pauseMs.Sum():F0}ms");
                text.Append(CultureInfo.InvariantCulture,
                    $" suspend p99={Pct(suspendMs, 0.99):F2} max={suspendMs[^1]:F2}ms");
            }
            List<Collection> ended = all.Where(c => c.HasStats).ToList();
            if (ended.Count > 0)
            {
                AppendPromoted(text, ended, 0);
                AppendPromoted(text, ended, 1);
                double pinned = ended.Average(c => (double)c.Pinned);
                text.Append(CultureInfo.InvariantCulture, $" pinned~{pinned:F0}");
                // the generation sizes after the collection that ended last
                Collection last = ended.MaxBy(c => c.EndOrder)!;
                text.Append(CultureInfo.InvariantCulture,
                    $" gen2MB={last.Gen2Size / 1048576.0:F1} lohMB={last.LohSize / 1048576.0:F1}");
                // the largest heap left after a collection in the window
                text.Append(CultureInfo.InvariantCulture,
                    $" peakAfterGcMB={ended.Max(c => c.HeapSize) / 1048576.0:F1}");
            }
            GCMemoryInfo info = GC.GetGCMemoryInfo();
            text.Append(CultureInfo.InvariantCulture,
                $" committedMB={info.TotalCommittedBytes / 1048576.0:F1} limitMB={info.TotalAvailableMemoryBytes / 1048576}");
            if (heaps.Count > 0)
            {
                text.Append(CultureInfo.InvariantCulture, $" heaps={heaps.Min()}..{heaps.Max()}");
            }
            if (budgets.Count > 0)
            {
                text.Append(CultureInfo.InvariantCulture,
                    $" gen0budgetMB~{budgets.Average() / 1048576.0:F1}");
            }
            return text.ToString();
        }

        /// <inheritdoc/>
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "Microsoft-Windows-DotNETRuntime")
            {
                // GC keyword
                EnableEvents(eventSource, EventLevel.Informational, (EventKeywords)0x1);
            }
        }

        /// <inheritdoc/>
        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            string? name = e.EventName;
            if (name == null || e.Payload == null)
            {
                return;
            }
            lock (m_lock)
            {
                DateTime stamp = e.TimeStamp.ToUniversalTime();
                if (!m_recording || stamp < m_markTime)
                {
                    return;
                }
                if (stamp > m_endTime)
                {
                    // the delivery boundary of ReportAsync
                    m_sawAfterEnd = true;
                    return;
                }
                if (name.StartsWith("GCSuspendEEBegin", StringComparison.Ordinal))
                {
                    m_suspendStart = e.TimeStamp;
                    m_suspendEnd = default;
                }
                else if (name.StartsWith("GCSuspendEEEnd", StringComparison.Ordinal))
                {
                    m_suspendEnd = e.TimeStamp;
                }
                else if (name.StartsWith("GCRestartEEEnd", StringComparison.Ordinal))
                {
                    // every stop-the-world interval, including the extra ones of a background gen2
                    if (m_suspendStart != default)
                    {
                        m_pauses.Add(new Pause(
                            (e.TimeStamp - m_suspendStart).TotalMilliseconds,
                            m_suspendEnd < m_suspendStart ? 0 : (m_suspendEnd - m_suspendStart).TotalMilliseconds));
                    }
                    m_suspendStart = default;
                }
                else if (name.StartsWith("GCStart", StringComparison.Ordinal))
                {
                    var collection = new Collection
                    {
                        Depth = ToInt(e, "Depth"),
                        Reason = ToInt(e, "Reason"),
                        Type = ToInt(e, "Type")
                    };
                    m_collections.Add(collection);
                    m_open[ToLong(e, "Count")] = collection;
                }
                else if (name.StartsWith("GCEnd", StringComparison.Ordinal))
                {
                    // the heap statistics that follow describe the collection that ended
                    m_ended = m_open.Remove(ToLong(e, "Count"), out Collection? collection) ? collection : null;
                }
                else if (name.StartsWith("GCHeapStats", StringComparison.Ordinal) && m_ended != null)
                {
                    Collection c = m_ended;
                    c.HasStats = true;
                    c.EndOrder = ++m_endOrder;
                    c.Promoted0 = ToLong(e, "TotalPromotedSize0");
                    c.Promoted1 = ToLong(e, "TotalPromotedSize1");
                    c.Gen2Size = ToLong(e, "GenerationSize2");
                    c.LohSize = ToLong(e, "GenerationSize3");
                    c.Pinned = ToLong(e, "PinnedObjectCount");
                    c.HeapSize = ToLong(e, "GenerationSize0") + ToLong(e, "GenerationSize1") +
                        c.Gen2Size + c.LohSize + ToLong(e, "GenerationSize4");
                    m_ended = null;
                }
                else if (name.StartsWith("GCGlobalHeapHistory", StringComparison.Ordinal))
                {
                    m_heaps.Add(ToLong(e, "NumHeaps"));
                    m_budgets.Add(ToLong(e, "FinalYoungestDesired"));
                }
            }
        }

        private static void AppendPromoted(System.Text.StringBuilder text, List<Collection> all, int depth)
        {
            // every collection condemns gen0; gen1 is condemned by gen1 and gen2 collections
            List<Collection> gens = all.Where(c => c.Depth >= depth).ToList();
            if (gens.Count == 0)
            {
                return;
            }
            // the bytes that survived the generation (gen0 into gen1, gen1 into gen2)
            double promoted = gens.Average(c => (double)(depth == 0 ? c.Promoted0 : c.Promoted1));
            text.Append(CultureInfo.InvariantCulture, $" survivedKB/gen{depth}={promoted / 1024:F0}");
        }

        private static double Pct(double[] sorted, double q)
        {
            return sorted[Math.Min(sorted.Length - 1, (int)(q * sorted.Length))];
        }

        private static string ReasonName(int reason)
        {
            return reason switch
            {
                0 => "alloc",
                1 => "induced",
                2 => "lowmem",
                3 => "empty",
                4 => "loh",
                5 => "oosSoh",
                6 => "oosLoh",
                7 => "inducedNotForced",
                8 => "stress",
                9 => "lowmemHost",
                _ => reason.ToString(CultureInfo.InvariantCulture)
            };
        }

        private static int ToInt(EventWrittenEventArgs e, string name)
        {
            return (int)ToLong(e, name);
        }

        private static long ToLong(EventWrittenEventArgs e, string name)
        {
            int index = e.PayloadNames?.IndexOf(name) ?? -1;
            return index < 0 || e.Payload![index] == null
                ? 0
                : Convert.ToInt64(e.Payload[index], CultureInfo.InvariantCulture);
        }

        private sealed class Collection
        {
            public int Depth;
            public int Reason;
            public int Type;
            public bool HasStats;
            public long EndOrder;
            public long Promoted0;
            public long Promoted1;
            public long Gen2Size;
            public long LohSize;
            public long Pinned;
            public long HeapSize;
        }

        private readonly record struct Pause(double PauseMs, double SuspendMs);

        private readonly object m_lock = new();
        private readonly List<Collection> m_collections = [];
        private readonly Dictionary<long, Collection> m_open = [];
        private readonly List<Pause> m_pauses = [];
        private readonly List<long> m_heaps = [];
        private readonly List<long> m_budgets = [];
        private Collection? m_ended;
        private long m_endOrder;
        private DateTime m_suspendStart;
        private DateTime m_suspendEnd;
        private DateTime m_markTime;
        private DateTime m_endTime = DateTime.MaxValue;
        private bool m_sawAfterEnd;
        private bool m_recording;
    }
}
