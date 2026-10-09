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
            "PollGCWorker|70_0",
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
                Console.WriteLine("usage: TraceAnalyzer cpu|alloc|contention <trace> [top] [focus]");
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
                default:
                    Allocations(log, top, focus);
                    break;
            }
            return 0;
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
                total++;
                Add(exclusive, leaf, 1);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                bool gotOurs = false;
                bool focusHit = false;
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
                    if (focus != null && !focusHit && n.Contains(focus, StringComparison.Ordinal))
                    {
                        focusHit = true;
                        Add(callers, f.Caller != null ? Short(Name(f.Caller)) : "<root>", 1);
                    }
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
