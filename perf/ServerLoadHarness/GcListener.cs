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
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;
using System.Runtime;

namespace Opc.Ua.Perf.ServerLoadHarness
{
    /// <summary>
    /// Listens to the garbage collection events of its own process (runtime provider, GC
    /// keyword, informational level: a handful of events per collection, no allocation
    /// sampling) and summarizes a measurement window: collections by generation and
    /// trigger, pause and suspension percentiles, bytes promoted per gen0/gen1
    /// collection, the heap count DATAS chose and the gen0 budget.
    /// </summary>
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
                m_heaps.Clear();
                m_budgets.Clear();
                m_current = null;
                m_suspendStart = default;
                // events are dispatched later: the induced collection before the mark is not part of the window
                m_markTime = DateTime.UtcNow;
                m_recording = true;
            }
        }

        /// <summary>
        /// Stops recording and returns the summary line of the window.
        /// </summary>
        public string Report()
        {
            List<Collection> all;
            List<long> heaps;
            List<long> budgets;
            lock (m_lock)
            {
                m_recording = false;
                all = m_collections.Where(c => c.PauseMs >= 0).ToList();
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
            if (all.Count > 0)
            {
                double[] pauses = [.. all.Select(c => c.PauseMs).Order()];
                double[] suspensions = [.. all.Select(c => c.SuspendMs).Order()];
                text.Append(CultureInfo.InvariantCulture,
                    $" pause p50={Pct(pauses, 0.5):F2} p99={Pct(pauses, 0.99):F2} max={pauses[^1]:F2} sum={pauses.Sum():F0}ms");
                text.Append(CultureInfo.InvariantCulture,
                    $" suspend p99={Pct(suspensions, 0.99):F2} max={suspensions[^1]:F2}ms");
                AppendPromoted(text, all, 0);
                AppendPromoted(text, all, 1);
                double pinned = all.Average(c => (double)c.Pinned);
                text.Append(CultureInfo.InvariantCulture, $" pinned~{pinned:F0}");
                long gen2 = all[^1].Gen2Size;
                long loh = all[^1].LohSize;
                text.Append(CultureInfo.InvariantCulture,
                    $" gen2MB={gen2 / 1048576.0:F1} lohMB={loh / 1048576.0:F1}");
                // the largest heap left after a collection in the window
                text.Append(CultureInfo.InvariantCulture,
                    $" peakAfterGcMB={all.Max(c => c.HeapSize) / 1048576.0:F1}");
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
                if (!m_recording || e.TimeStamp.ToUniversalTime() < m_markTime)
                {
                    return;
                }
                if (name.StartsWith("GCSuspendEEBegin", StringComparison.Ordinal))
                {
                    m_suspendStart = e.TimeStamp;
                }
                else if (name.StartsWith("GCSuspendEEEnd", StringComparison.Ordinal))
                {
                    m_suspendEnd = e.TimeStamp;
                }
                else if (name.StartsWith("GCStart", StringComparison.Ordinal))
                {
                    m_current = new Collection
                    {
                        Depth = ToInt(e, "Depth"),
                        Reason = ToInt(e, "Reason"),
                        Type = ToInt(e, "Type"),
                        SuspendMs = m_suspendStart == default || m_suspendEnd < m_suspendStart
                            ? 0
                            : (m_suspendEnd - m_suspendStart).TotalMilliseconds,
                        Start = m_suspendStart
                    };
                    m_collections.Add(m_current);
                }
                else if (name.StartsWith("GCHeapStats", StringComparison.Ordinal) && m_current != null)
                {
                    m_current.Promoted0 = ToLong(e, "TotalPromotedSize0");
                    m_current.Promoted1 = ToLong(e, "TotalPromotedSize1");
                    m_current.Gen2Size = ToLong(e, "GenerationSize2");
                    m_current.LohSize = ToLong(e, "GenerationSize3");
                    m_current.Pinned = ToLong(e, "PinnedObjectCount");
                    m_current.HeapSize = ToLong(e, "GenerationSize0") + ToLong(e, "GenerationSize1") +
                        m_current.Gen2Size + m_current.LohSize + ToLong(e, "GenerationSize4");
                }
                else if (name.StartsWith("GCGlobalHeapHistory", StringComparison.Ordinal))
                {
                    m_heaps.Add(ToLong(e, "NumHeaps"));
                    m_budgets.Add(ToLong(e, "FinalYoungestDesired"));
                }
                else if (name.StartsWith("GCRestartEEEnd", StringComparison.Ordinal))
                {
                    // a background gen2 restarts the threads more than once: the first
                    // restart after a suspension ends the pause of the collection it started
                    if (m_current != null && m_current.PauseMs < 0 && m_suspendStart != default)
                    {
                        m_current.PauseMs = (e.TimeStamp - m_suspendStart).TotalMilliseconds;
                    }
                    else
                    {
                        // a foreground gen0/gen1 inside a background gen2 has its own suspension
                        Collection? open = m_collections.LastOrDefault(c => c.PauseMs < 0);
                        if (open != null && m_suspendStart != default)
                        {
                            open.PauseMs = (e.TimeStamp - m_suspendStart).TotalMilliseconds;
                        }
                    }
                    m_suspendStart = default;
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
            public DateTime Start;
            public double SuspendMs;
            public double PauseMs = -1;
            public long Promoted0;
            public long Promoted1;
            public long Gen2Size;
            public long LohSize;
            public long Pinned;
            public long HeapSize;
        }

        private readonly object m_lock = new();
        private readonly List<Collection> m_collections = [];
        private readonly List<long> m_heaps = [];
        private readonly List<long> m_budgets = [];
        private Collection? m_current;
        private DateTime m_suspendStart;
        private DateTime m_suspendEnd;
        private DateTime m_markTime;
        private bool m_recording;
    }
}
