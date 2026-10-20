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


// Trace analyzer for perf/ServerLoadHarness traces. Not part of the product; see perf/README.md.
//   TraceAnalyzer cpu        <trace> [top] [focus] : on-CPU samples (blocked leaf frames dropped)
//   TraceAnalyzer alloc      <trace> [top] [type]  : GCAllocationTick bytes by type, first Opc.Ua frame, stacks
//   TraceAnalyzer contention <trace> [top]         : ContentionStart events by Opc.Ua stack

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace Opc.Ua.Perf.TraceAnalyzer
{
    /// <summary>
    /// Aggregates EventPipe traces recorded by the server load harness.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Leaf frames of threads that wait rather than run. The sampled thread-time
        /// profile samples blocked threads too, so these are dropped to approximate CPU.
        /// </summary>
        private static readonly string[] s_blockedLeafFrames =
        [
            "LowLevelLifoSemaphore.Wait",
            "WaitForSignal",
            "WorkerThreadStart",
            "IOCompletionPoller.Poll",
            "WaitOneNoCheck",
            "Thread.Sleep",
            "WaitMultipleIgnoringSyncContext",
            "Monitor.Wait",
            "!?"
        ];

        /// <summary>
        /// Entry point.
        /// </summary>
        public static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("usage: TraceAnalyzer cpu|alloc|contention|gc|samples|suspend|threads <trace> [top|thresholdMs] [focus]");
                return 1;
            }

            string mode = args[0];
            int top = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 40;
            string? focus = args.Length > 3 ? args[3] : null;

            string etlx = TraceLog.CreateFromEventPipeDataFile(
                args[1], null, new TraceLogOptions { ContinueOnError = true });
            using var log = new TraceLog(etlx);

            switch (mode)
            {
                case "contention":
                    Contention(log, top);
                    break;
                case "cpu":
                    Cpu(log, top, focus);
                    break;
                case "gc":
                    Collections(log);
                    break;
                case "samples":
                    Samples(log, top);
                    break;
                case "suspend":
                    Suspensions(log, top);
                    break;
                case "threads":
                    Threads(log, top);
                    break;
                default:
                    Allocations(log, top, focus);
                    break;
            }
            return 0;
        }

        /// <summary>
        /// All thread-time samples by type (managed code or external/native, which includes
        /// threads blocked in the kernel) and the leaf and first Opc.Ua frame of the external
        /// ones. The cpu mode keeps only managed samples; this shows what it leaves out.
        /// </summary>
        private static void Samples(TraceLog log, int top)
        {
            var types = new Dictionary<string, double>(StringComparer.Ordinal);
            var externalLeaves = new Dictionary<string, double>(StringComparer.Ordinal);
            var externalOurs = new Dictionary<string, double>(StringComparer.Ordinal);
            var managedLeaves = new Dictionary<string, double>(StringComparer.Ordinal);
            var managedOurs = new Dictionary<string, double>(StringComparer.Ordinal);
            double all = 0;
            double external = 0;
            double managed = 0;
            foreach (TraceEvent e in log.Events)
            {
                if (e.ProviderName != "Microsoft-DotNETCore-SampleProfiler")
                {
                    continue;
                }
                all++;
                object? type = e.PayloadByName("Type");
                int kind = type == null ? -1 : Convert.ToInt32(type, CultureInfo.InvariantCulture);
                Add(types, kind == 2 ? "managed" : kind == 1 ? "external" : "other " + kind, 1);
                TraceCallStack? stack = e.CallStack();
                if (stack == null || (kind != 1 && kind != 2))
                {
                    continue;
                }
                Dictionary<string, double> leaves = kind == 1 ? externalLeaves : managedLeaves;
                Dictionary<string, double> ours = kind == 1 ? externalOurs : managedOurs;
                if (kind == 1)
                {
                    external++;
                }
                else
                {
                    managed++;
                }
                Add(leaves, Short(Name(stack)), 1);
                for (TraceCallStack? f = stack; f != null; f = f.Caller)
                {
                    string n = Short(Name(f));
                    if (IsOurs(n))
                    {
                        Add(ours, n, 1);
                        break;
                    }
                }
            }
            Dump("sample types", types, all, top);
            Dump("external samples by leaf", externalLeaves, external, top);
            Dump("external samples by first Opc.Ua frame", externalOurs, external, top);
            Dump("managed samples by leaf", managedLeaves, managed, top);
            Dump("managed samples by first Opc.Ua frame", managedOurs, managed, top);
        }

        /// <summary>
        /// On-CPU samples per thread: the busy share of each thread (its on-CPU samples over all
        /// of its samples) and its top first Opc.Ua frames. This is a sample ratio, not
        /// wall-clock CPU utilization. A thread near 100% busy while other cores are idle
        /// points to a possible serialized stage; confirm with call stacks and workload parallelism.
        /// </summary>
        private static void Threads(TraceLog log, int top)
        {
            var all = new Dictionary<int, double>();
            var busy = new Dictionary<int, double>();
            var frames = new Dictionary<int, Dictionary<string, double>>();
            foreach (TraceEvent e in log.Events)
            {
                if (e.ProviderName != "Microsoft-DotNETCore-SampleProfiler")
                {
                    continue;
                }
                int thread = e.ThreadID;
                all[thread] = all.GetValueOrDefault(thread) + 1;
                object? type = e.PayloadByName("Type");
                if (type != null && Convert.ToInt32(type, CultureInfo.InvariantCulture) != 2)
                {
                    continue;
                }
                TraceCallStack? stack = e.CallStack();
                if (stack == null)
                {
                    continue;
                }
                string leaf = Short(Name(stack));
                if (s_blockedLeafFrames.Any(b => leaf.EndsWith(b, StringComparison.Ordinal)))
                {
                    continue;
                }
                busy[thread] = busy.GetValueOrDefault(thread) + 1;
                if (!frames.TryGetValue(thread, out Dictionary<string, double>? ours))
                {
                    frames[thread] = ours = new Dictionary<string, double>(StringComparer.Ordinal);
                }
                // the outermost Opc.Ua frame names the thread's role (timer loop, worker, receive loop)
                string? outer = null;
                string? inner = null;
                for (TraceCallStack? f = stack; f != null; f = f.Caller)
                {
                    string n = Short(Name(f));
                    if (IsOurs(n))
                    {
                        inner ??= n;
                        outer = n;
                    }
                }
                Add(ours, (outer ?? "-") + "  >  " + (inner ?? leaf), 1);
            }
            double totalBusy = busy.Values.Sum();
            Console.WriteLine(FormattableString.Invariant($"threads={all.Count} on-cpu samples={totalBusy}"));
            foreach (KeyValuePair<int, double> t in busy.OrderByDescending(b => b.Value).Take(top))
            {
                Console.WriteLine(FormattableString.Invariant(
                    $"thread {t.Key,6}: busy {100.0 * t.Value / all[t.Key],5:F1}%  share {100.0 * t.Value / totalBusy,5:F1}%"));
                foreach (KeyValuePair<string, double> f in frames[t.Key].OrderByDescending(k => k.Value).Take(3))
                {
                    Console.WriteLine(FormattableString.Invariant($"    {100.0 * f.Value / t.Value,5:F1}%  {f.Key}"));
                }
            }
        }

        /// <summary>
        /// Garbage collections by generation and trigger reason, with pause times
        /// (a trace recorded with <c>--trace-profile gc</c>).
        /// </summary>
        private static void Collections(TraceLog log)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
            var pauses = new List<double>();
            var suspensions = new List<double>();
            // time and bytes of the phases of a blocking collection that does not overlap a
            // background gen2: marking per heap (each heap's time since its previous mark event,
            // summed over heaps), plan to end and end to restart as elapsed time
            var phaseMs = new Dictionary<string, double>(StringComparer.Ordinal);
            var markedBytes = new Dictionary<string, double>(StringComparer.Ordinal);
            var heapLastMark = new Dictionary<int, double>();
            double suspendStart = 0;
            double collectionStart = 0;
            double collectionEnd = 0;
            long phasedCount = -1;
            long backgroundCount = -1;
            int phased = 0;
            int unphased = 0;
            foreach (TraceEvent e in log.Events)
            {
                if (e is GCStartTraceData start)
                {
                    string key = FormattableString.Invariant($"gen{start.Depth} {start.Type} {start.Reason}");
                    counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                    if (start.Type == GCType.BackgroundGC || backgroundCount >= 0)
                    {
                        // a background gen2, or a foreground collection inside one
                        if (start.Type == GCType.BackgroundGC)
                        {
                            backgroundCount = start.Count;
                        }
                        phasedCount = -1;
                        unphased++;
                        continue;
                    }
                    if (suspendStart > 0)
                    {
                        Add(phaseMs, "0 suspend+start", e.TimeStampRelativeMSec - suspendStart);
                    }
                    phasedCount = start.Count;
                    collectionStart = e.TimeStampRelativeMSec;
                    collectionEnd = 0;
                    heapLastMark.Clear();
                    phased++;
                }
                else if (e.EventName == "GC/MarkWithType" && phasedCount >= 0 && collectionEnd == 0)
                {
                    int type = Convert.ToInt32(e.PayloadByName("Type") ?? -1, CultureInfo.InvariantCulture);
                    int heap = Convert.ToInt32(e.PayloadByName("HeapNum") ?? 0, CultureInfo.InvariantCulture);
                    string name = "1 mark " + (type is >= 0 and < 10 ? s_markTypes[type] : type.ToString(CultureInfo.InvariantCulture));
                    double previous = heapLastMark.TryGetValue(heap, out double last) ? last : collectionStart;
                    Add(phaseMs, name, e.TimeStampRelativeMSec - previous);
                    Add(markedBytes, name, Convert.ToDouble(e.PayloadByName("Promoted") ?? 0, CultureInfo.InvariantCulture));
                    heapLastMark[heap] = e.TimeStampRelativeMSec;
                }
                else if (e is GCEndTraceData end)
                {
                    if (end.Count == backgroundCount)
                    {
                        backgroundCount = -1;
                    }
                    else if (end.Count == phasedCount)
                    {
                        // plan, relocate, compact or sweep after the last heap finished marking
                        double lastMark = heapLastMark.Count > 0 ? heapLastMark.Values.Max() : collectionStart;
                        Add(phaseMs, "2 plan+relocate+compact", e.TimeStampRelativeMSec - lastMark);
                        collectionEnd = e.TimeStampRelativeMSec;
                    }
                }
                else if (e is GCSuspendEETraceData)
                {
                    suspendStart = e.TimeStampRelativeMSec;
                }
                else if (e.EventName == "GC/PerHeapHistory" && e.PayloadByName("CondemnReasons0") is object r0)
                {
                    // why the generation was condemned: the generation each tuning input asked for,
                    // and the conditions that raised it (gc_condemn_reason_gen / _condition)
                    string key = "condemn " + CondemnReasons(
                        Convert.ToInt64(r0, CultureInfo.InvariantCulture),
                        Convert.ToInt64(e.PayloadByName("CondemnReasons1") ?? 0, CultureInfo.InvariantCulture));
                    reasons[key] = reasons.TryGetValue(key, out int n) ? n + 1 : 1;
                }
                else if (e.EventName == "GC/SuspendEEStop" && suspendStart > 0)
                {
                    // time to bring every managed thread to a safe point
                    suspensions.Add(e.TimeStampRelativeMSec - suspendStart);
                }
                else if (e.EventName == "GC/RestartEEStop" && suspendStart > 0)
                {
                    pauses.Add(e.TimeStampRelativeMSec - suspendStart);
                    if (phasedCount >= 0 && collectionEnd > 0)
                    {
                        Add(phaseMs, "3 end+restart", e.TimeStampRelativeMSec - collectionEnd);
                        phasedCount = -1;
                    }
                    suspendStart = 0;
                }
            }
            int total = counts.Values.Sum();
            Console.WriteLine(FormattableString.Invariant($"collections={total}"));
            foreach (KeyValuePair<string, int> entry in counts.OrderByDescending(c => c.Value))
            {
                Console.WriteLine(FormattableString.Invariant($" {entry.Value,7}  {entry.Key}"));
            }
            foreach (KeyValuePair<string, int> entry in reasons.OrderByDescending(c => c.Value))
            {
                Console.WriteLine(FormattableString.Invariant($" {entry.Value,7}  {entry.Key}"));
            }
            if (phased > 0)
            {
                Console.WriteLine(FormattableString.Invariant(
                    $"phases (average per blocking collection outside background GCs, {phased} of {phased + unphased}; mark phases summed over heaps):"));
                foreach (KeyValuePair<string, double> entry in phaseMs.OrderBy(c => c.Key, StringComparer.Ordinal))
                {
                    string bytes = markedBytes.TryGetValue(entry.Key, out double b)
                        ? FormattableString.Invariant($"  {b / phased / 1024,9:F1} KB marked")
                        : string.Empty;
                    Console.WriteLine(FormattableString.Invariant($" {entry.Value / phased,8:F3} ms  {entry.Key}{bytes}"));
                }
            }
            if (suspensions.Count > 0)
            {
                suspensions.Sort();
                Console.WriteLine(FormattableString.Invariant(
                    $"suspension n={suspensions.Count} total={suspensions.Sum():F1}ms p50={suspensions[suspensions.Count / 2]:F2}ms ") +
                    FormattableString.Invariant(
                    $"p99={suspensions[Math.Min(suspensions.Count - 1, (int)(suspensions.Count * 0.99))]:F2}ms max={suspensions[^1]:F2}ms"));
            }
            if (pauses.Count > 0)
            {
                pauses.Sort();
                Console.WriteLine(FormattableString.Invariant(
                    $"pauses n={pauses.Count} total={pauses.Sum():F1}ms p50={pauses[pauses.Count / 2]:F2}ms ") +
                    FormattableString.Invariant(
                    $"p99={pauses[Math.Min(pauses.Count - 1, (int)(pauses.Count * 0.99))]:F2}ms max={pauses[^1]:F2}ms"));
            }
        }

        private static readonly string[] s_markTypes =
        [
            "stack", "finalizeQueue", "handles", "older(cards)", "sizedRef", "overflow",
            "dependentHandles", "newFinalizeQueue", "steal", "bgcRoots"
        ];

        /// <summary>
        /// Decodes the condemn reasons of a GCPerHeapHistory event: CondemnReasons0 packs the
        /// generation each input asked for in 2-bit fields (initial, final per heap, allocation
        /// budget, time tuning, ...), CondemnReasons1 is a set of condition flags.
        /// </summary>
        private static string CondemnReasons(long gens, long conditions)
        {
            string[] genNames = ["initial", "perHeap", "budget", "timeTuning"];
            string[] flagNames =
            [
                "inducedFull", "expandFull", "highMem", "veryHighMem", "lowEphemeral", "lowCard",
                "ephHighFrag", "maxHighFrag", "maxHighFragE", "maxHighFragM", "maxHighFragVm",
                "maxGen1", "beforeOom", "gen2TooSmall", "inducedNoForce", "beforeBgc",
                "almostMaxAlloc"
            ];
            var text = new StringBuilder();
            for (int i = 0; i < genNames.Length; i++)
            {
                long gen = (gens >> (2 * i)) & 3;
                if (gen != 0)
                {
                    text.Append(CultureInfo.InvariantCulture, $"{genNames[i]}=gen{gen} ");
                }
            }
            for (int i = 0; i < 32; i++)
            {
                if ((conditions & (1L << i)) != 0)
                {
                    text.Append('+').Append(i < flagNames.Length ? flagNames[i] : "c" + i.ToString(CultureInfo.InvariantCulture)).Append(' ');
                }
            }
            return text.Length == 0 ? "gen0" : text.ToString().TrimEnd();
        }

        /// <summary>
        /// Event counts (including the heap counts DATAS chose and the GC handle kinds), and for
        /// each suspension longer than <paramref name="thresholdMs"/> the events recorded between
        /// SuspendEE start and stop, by thread. Use allocation-event ordering and scheduling
        /// evidence to investigate the cause of a suspension delay.
        /// </summary>
        private static void Suspensions(TraceLog log, int thresholdMs)
        {
            var names = new Dictionary<string, double>(StringComparer.Ordinal);
            var window = new List<(double Time, string Text)>();
            double start = 0;
            int startThread = 0;
            string reason = string.Empty;
            foreach (TraceEvent e in log.Events)
            {
                Add(names, e.ProviderName + "/" + e.EventName, 1);
                if (e.EventName == "GC/GlobalHeapHistory")
                {
                    Add(names, FormattableString.Invariant(
                        $"HEAPS {e.PayloadByName("NumHeaps")} gen0budget~{e.PayloadByName("FinalYoungestDesired")}"), 1);
                }
                if (e.EventName == "GC/SetGCHandle")
                {
                    Add(names, "HANDLEKIND " + e.PayloadByName("Kind"), 1);
                }
                if (e is GCSuspendEETraceData s)
                {
                    start = e.TimeStampRelativeMSec;
                    startThread = e.ThreadID;
                    reason = s.Reason.ToString();
                    window.Clear();
                    continue;
                }
                if (start > 0 && e.EventName == "GC/SuspendEEStop")
                {
                    double d = e.TimeStampRelativeMSec - start;
                    if (d >= thresholdMs)
                    {
                        Console.WriteLine(FormattableString.Invariant(
                            $"suspension at {start:F1}ms took {d:F2}ms reason={reason} suspender={startThread} stopThread={e.ThreadID}"));
                        foreach ((double time, string text) in window)
                        {
                            Console.WriteLine(FormattableString.Invariant($"   +{time - start,8:F3} {text}"));
                        }
                    }
                    start = 0;
                    continue;
                }
                if (start > 0 && window.Count < 200)
                {
                    window.Add((e.TimeStampRelativeMSec, FormattableString.Invariant(
                        $"t={e.ThreadID,6} {e.ProviderName}/{e.EventName} {(e.CallStack() is { } cs ? Short(Name(cs)) : string.Empty)}")));
                }
            }
            Dump("events", names, names.Values.Sum(), 60);
            double handles = names.GetValueOrDefault("Microsoft-Windows-DotNETRuntime/GC/SetGCHandle");
            Console.WriteLine(FormattableString.Invariant(
                $"SetGCHandle={handles} trace ms={log.SessionDuration.TotalMilliseconds:F0}"));
        }

        private static void Contention(TraceLog log, int top)
        {
            var byStack = new Dictionary<string, double>(StringComparer.Ordinal);
            double total = 0;
            foreach (TraceEvent e in log.Events)
            {
                if (e.EventName?.StartsWith("Contention/Start", StringComparison.Ordinal) != true)
                {
                    continue;
                }
                total++;
                var chain = new List<string>();
                for (TraceCallStack? f = e.CallStack(); f != null && chain.Count < 7; f = f.Caller)
                {
                    string n = Short(Name(f));
                    if (IsOurs(n) || chain.Count == 0)
                    {
                        chain.Add(n);
                    }
                }
                Add(byStack, string.Join("\n      ", chain), 1);
            }
            Console.WriteLine(FormattableString.Invariant($"contention events: {total}"));
            Dump("stacks", byStack, total, top);
        }

        private static void Cpu(TraceLog log, int top, string? focus)
        {
            var exclusive = new Dictionary<string, double>(StringComparer.Ordinal);
            var inclusive = new Dictionary<string, double>(StringComparer.Ordinal);
            var firstOurs = new Dictionary<string, double>(StringComparer.Ordinal);
            var callers = new Dictionary<string, double>(StringComparer.Ordinal);
            var ourCallers = new Dictionary<string, double>(StringComparer.Ordinal);
            var callees = new Dictionary<string, double>(StringComparer.Ordinal);
            double total = 0;
            int all = 0;
            foreach (TraceEvent e in log.Events)
            {
                if (e.ProviderName != "Microsoft-DotNETCore-SampleProfiler")
                {
                    continue;
                }
                all++;
                object? type = e.PayloadByName("Type");
                if (type != null && Convert.ToInt32(type, CultureInfo.InvariantCulture) != 2)
                {
                    // not running managed code
                    continue;
                }
                TraceCallStack? stack = e.CallStack();
                if (stack == null)
                {
                    continue;
                }
                string leaf = Short(Name(stack));
                if (s_blockedLeafFrames.Any(b => leaf.EndsWith(b, StringComparison.Ordinal)))
                {
                    continue;
                }
                if (leaf.EndsWith("PollGCWorker|70_0", StringComparison.Ordinal) && stack.Caller != null)
                {
                    // The sampler suspends running threads at their next GC poll, so a running
                    // thread is often sampled there: attribute the sample to the caller. Threads
                    // that wait for a real collection to finish land here too (see the gc mode).
                    stack = stack.Caller;
                    leaf = Short(Name(stack));
                }
                total++;
                Add(exclusive, leaf, 1);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                bool gotOurs = false;
                bool focusHit = false;
                bool ourCallerHit = false;
                string below = "<self>";
                for (TraceCallStack? f = stack; f != null; f = f.Caller)
                {
                    string n = Short(Name(f));
                    if (seen.Add(n))
                    {
                        Add(inclusive, n, 1);
                    }
                    if (!gotOurs && IsOurs(n))
                    {
                        gotOurs = true;
                        Add(firstOurs, n, 1);
                    }
                    if (focusHit && !ourCallerHit && IsOurs(n))
                    {
                        // the first stack frame of the stack above the focus, past framework wrappers
                        ourCallerHit = true;
                        Add(ourCallers, n, 1);
                    }
                    if (focus != null && !focusHit && n.Contains(focus, StringComparison.Ordinal))
                    {
                        focusHit = true;
                        Add(callers, f.Caller != null ? Short(Name(f.Caller)) : "<root>", 1);
                        Add(callees, below, 1);
                    }
                    below = n;
                }
            }
            Console.WriteLine(FormattableString.Invariant($"samples: on-cpu={total} all={all}"));
            Dump("exclusive", exclusive, total, top);
            Dump("first Opc.Ua frame", firstOurs, total, top);
            Dump(
                "inclusive (Opc.Ua only)",
                inclusive.Where(k => IsOurs(k.Key)).ToDictionary(k => k.Key, k => k.Value, StringComparer.Ordinal),
                total,
                top);
            if (focus != null)
            {
                Dump("callers of " + focus, callers, total, top);
                Dump("first Opc.Ua frame above " + focus, ourCallers, total, top);
                Dump("direct callees of " + focus, callees, total, top);
            }
        }

        private static void Allocations(TraceLog log, int top, string? focus)
        {
            var byType = new Dictionary<string, double>(StringComparer.Ordinal);
            var byFrame = new Dictionary<string, double>(StringComparer.Ordinal);
            var byStack = new Dictionary<string, double>(StringComparer.Ordinal);
            double total = 0;
            foreach (TraceEvent e in log.Events)
            {
                if (e is not GCAllocationTickTraceData tick)
                {
                    continue;
                }
                double amount = tick.AllocationAmount64;
                total += amount;
                string type = tick.TypeName ?? "?";
                Add(byType, type, amount);
                string frame = "?";
                var chain = new List<string>();
                for (TraceCallStack? f = e.CallStack(); f != null; f = f.Caller)
                {
                    string n = Short(Name(f));
                    if (!IsOurs(n))
                    {
                        continue;
                    }
                    if (frame == "?")
                    {
                        frame = n;
                    }
                    if (chain.Count < 6 || (focus != null && chain.Count < 12))
                    {
                        chain.Add(n);
                    }
                }
                Add(byFrame, type + " <- " + frame, amount);
                if (focus == null || type.Contains(focus, StringComparison.Ordinal))
                {
                    Add(byStack, type + "\n      " + string.Join("\n      ", chain), amount);
                }
            }
            Console.WriteLine(FormattableString.Invariant($"sampled alloc total: {total / 1048576:F1} MB"));
            Dump("by type", byType, total, top);
            Dump("by type <- first Opc.Ua frame", byFrame, total, top);
            Dump("stacks", byStack, total, top / 2);
        }

        private static void Add(Dictionary<string, double> totals, string key, double amount)
        {
            totals[key] = totals.GetValueOrDefault(key) + amount;
        }

        private static void Dump(string title, Dictionary<string, double> totals, double total, int count)
        {
            Console.WriteLine("--- " + title);
            foreach (KeyValuePair<string, double> kv in totals.OrderByDescending(k => k.Value).Take(count))
            {
                Console.WriteLine(FormattableString.Invariant($"{100.0 * kv.Value / total,6:F2}%  {kv.Key}"));
            }
        }

        private static string Name(TraceCallStack frame)
        {
            string name = frame.CodeAddress.FullMethodName;
            return string.IsNullOrEmpty(name) ? frame.CodeAddress.ModuleName + "!?" : name;
        }

        private static string Short(string method)
        {
            int p = method.IndexOf('(', StringComparison.Ordinal);
            return p > 0 ? method[..p] : method;
        }

        private static bool IsOurs(string method)
        {
            return method.StartsWith("Opc.Ua", StringComparison.Ordinal) ||
                method.StartsWith("Quickstarts", StringComparison.Ordinal);
        }
    }
}
