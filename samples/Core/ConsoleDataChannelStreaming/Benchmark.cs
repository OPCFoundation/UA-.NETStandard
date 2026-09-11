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
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace ConsoleDataChannelStreaming
{
    /// <summary>
    /// Measures what a data channel sustains while the Session carries a
    /// competing Publish load.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The question this answers is whether Service traffic and a saturated
    /// data channel starve each other. Over inline framing they share one
    /// SecureChannel and one SequenceNumber space, so the credit window is
    /// what keeps a media stream from starving Publish; over
    /// <c>opc.quic</c> the channel owns its own stream and QUIC applies the
    /// flow control, so the two should barely interact. Running this over
    /// both transports is how that claim gets a number rather than an
    /// assertion.
    /// </para>
    /// <para>
    /// The Client and the Server are in one process talking over loopback,
    /// so these figures are bound by CPU and cryptography, not by a network
    /// interface. They are useful for comparing the four cases against each
    /// other. They are not a statement about what the stack does over a
    /// real link, and the output says so.
    /// </para>
    /// </remarks>
    internal static class Benchmark
    {
        public static async Task RunAsync(SampleOptions options, CancellationToken ct)
        {
            int[] publishingIntervals = [0, 10, 100, 1000];
            int repeat = Math.Max(options.Repeat, 1);

            Console.WriteLine(
                $"benchmark: {options.Transport} transport, {options.FrameCount} x " +
                $"{options.FrameSize} byte frames, {repeat} measured runs per case " +
                "after one discarded warm-up");
            Console.WriteLine();

            var results = new List<CaseResult>();

            // One Server, one Session and one data channel for the whole
            // matrix, with only the subscription changing between cases.
            // That keeps the comparison honest - every case runs on the same
            // channel with the same negotiated credit - and avoids standing
            // up several Servers in one process, which the data channel
            // Services do not expect.
            StreamingHarness harness = await StreamingHarness
                .CreateAsync(options with { RunMode = SampleRunMode.Server }, ct)
                .ConfigureAwait(false);

            try
            {
                var accumulators = new Dictionary<int, CaseAccumulator>();
                foreach (int publishingInterval in publishingIntervals)
                {
                    accumulators[publishingInterval] = new CaseAccumulator();
                }

                // Cases are round-robined inside each repetition rather than
                // run to completion one after another. Run sequentially, a
                // process that keeps warming up over the matrix hands every
                // later case an advantage the earlier ones never had, and the
                // result is a clean monotonic trend that is entirely an
                // artefact of the order. Round-robin spreads that drift
                // across all four cases instead of loading it onto the last.
                for (int pass = 0; pass <= repeat; pass++)
                {
                    bool isWarmup = pass == 0;

                    foreach (int publishingInterval in publishingIntervals)
                    {
                        if (!isWarmup)
                        {
                            Console.WriteLine(
                                publishingInterval == 0
                                    ? $"  pass {pass}: no subscription ..."
                                    : $"  pass {pass}: {options.MonitoredItems} items at " +
                                      $"{publishingInterval} ms ...");
                        }

                        await harness
                            .StartPublishLoadAsync(publishingInterval, options.MonitoredItems, ct)
                            .ConfigureAwait(false);

                        CaseAccumulator accumulator = accumulators[publishingInterval];

                        // The idle Publish rate is a property of the
                        // subscription, not of the pass, so it is measured
                        // once on the warm-up pass. Without this control
                        // there is no way to tell a Publish path starved by
                        // the data channel from one that was never going to
                        // run faster, and the two lead to opposite
                        // conclusions.
                        if (isWarmup && publishingInterval != 0)
                        {
                            accumulator.IdlePublishRate =
                                await MeasureIdlePublishRateAsync(harness, ct).ConfigureAwait(false);
                        }

                        DataChannelDiagnosticsDataType before = harness.Source.GetDiagnostics();
                        long notificationsBefore = harness.PublishNotifications;

                        Program.StreamingRun run = await Program
                            .StreamAsync(harness, options, verbose: false, closeChannel: false, ct)
                            .ConfigureAwait(false);

                        if (isWarmup)
                        {
                            continue;
                        }

                        DataChannelDiagnosticsDataType after = harness.Source.GetDiagnostics();
                        double seconds = run.Elapsed.TotalSeconds > 0
                            ? run.Elapsed.TotalSeconds
                            : double.Epsilon;

                        accumulator.Throughputs.Add(
                            (after.BytesSent - before.BytesSent) * 8 / 1_000_000.0 / seconds);
                        accumulator.CreditStalls += (long)(after.CreditStalls - before.CreditStalls);
                        accumulator.Discarded += (long)(after.FramesDiscarded - before.FramesDiscarded);
                        accumulator.FramesReceived += run.FramesReceived;
                        accumulator.Notifications += harness.PublishNotifications - notificationsBefore;
                        accumulator.LoadSeconds += seconds;
                        accumulator.RevisedInterval = harness.RevisedPublishingInterval;
                    }
                }

                foreach (int publishingInterval in publishingIntervals)
                {
                    CaseAccumulator accumulator = accumulators[publishingInterval];

                    results.Add(new CaseResult(
                        publishingInterval,
                        accumulator.RevisedInterval,
                        publishingInterval == 0 ? 0 : options.MonitoredItems,
                        Median(accumulator.Throughputs),
                        accumulator.Throughputs.Count > 0 ? accumulator.Throughputs.Min() : 0,
                        accumulator.Throughputs.Count > 0 ? accumulator.Throughputs.Max() : 0,
                        accumulator.FramesReceived / repeat,
                        accumulator.CreditStalls,
                        accumulator.Discarded,
                        accumulator.Notifications,
                        accumulator.LoadSeconds,
                        accumulator.IdlePublishRate));
                }

                await harness.StopPublishLoadAsync(ct).ConfigureAwait(false);
                await harness.CloseDataChannelAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }

            Console.WriteLine();
            Report(options, results);
        }

        private sealed class CaseAccumulator
        {
            public List<double> Throughputs { get; } = [];
            public long CreditStalls { get; set; }
            public long Discarded { get; set; }
            public int FramesReceived { get; set; }
            public long Notifications { get; set; }
            public double LoadSeconds { get; set; }
            public double IdlePublishRate { get; set; }
            public double RevisedInterval { get; set; }
        }

        /// <summary>
        /// Counts notifications over a fixed window with the data channel
        /// idle, giving the Publish rate the subscription reaches when it is
        /// not competing with anything.
        /// </summary>
        private static async Task<double> MeasureIdlePublishRateAsync(
            StreamingHarness harness,
            CancellationToken ct)
        {
            var window = TimeSpan.FromSeconds(3);

            long before = harness.PublishNotifications;
            long started = Stopwatch.GetTimestamp();

            await Task.Delay(window, ct).ConfigureAwait(false);

            double seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
            return seconds > 0 ? (harness.PublishNotifications - before) / seconds : 0;
        }

        private static void Report(SampleOptions options, List<CaseResult> results)
        {
            Console.WriteLine("data channel throughput vs competing Publish load");
            Console.WriteLine("=================================================");
            Console.WriteLine();
            Console.WriteLine(
                "  publish   revised   items   notif/s idle   notif/s loaded   " +
                "Mbit/s (median)   spread          stalls");
            Console.WriteLine(
                "  --------  --------  ------  -------------  ---------------  " +
                "----------------  --------------  -------");

            double baseline = results.Count > 0 ? results[0].MedianMbits : 0;

            foreach (CaseResult result in results)
            {
                string publish = result.PublishingInterval == 0
                    ? "none"
                    : result.PublishingInterval.ToString(CultureInfo.InvariantCulture) + " ms";

                string revised = result.PublishingInterval == 0
                    ? "-"
                    : result.RevisedPublishingInterval.ToString("F0", CultureInfo.InvariantCulture) + " ms";

                double notificationsPerSecond = result.LoadSeconds > 0
                    ? result.Notifications / result.LoadSeconds
                    : 0;

                string idle = result.PublishingInterval == 0
                    ? "-"
                    : result.IdlePublishRate.ToString("F0", CultureInfo.InvariantCulture);

                string loaded = result.PublishingInterval == 0
                    ? "-"
                    : notificationsPerSecond.ToString("F0", CultureInfo.InvariantCulture);

                string relative = baseline > 0 && result.PublishingInterval != 0
                    ? $" ({result.MedianMbits / baseline * 100:F0}% of baseline)"
                    : string.Empty;

                Console.WriteLine(
                    $"  {publish,-8}  {revised,-8}  {result.MonitoredItems,6}  " +
                    $"{idle,13}  {loaded,15}  {result.MedianMbits,16:F1}  " +
                    $"{result.MinMbits,6:F1}-{result.MaxMbits,-7:F1}  " +
                    $"{result.CreditStalls,7}{relative}");
            }

            Console.WriteLine();
            WriteResolution(results);
            WriteStarvation(results);

            Console.WriteLine();
            WriteValidity(options, results);

            Console.WriteLine(
                "  Client and Server run in one process over loopback, so these are");
            Console.WriteLine(
                "  CPU and cryptography bound figures for comparing the four cases");
            Console.WriteLine(
                "  against each other, not a statement about throughput over a real");
            Console.WriteLine(
                "  link. Write enqueues rather than blocking, so what is measured is");
            Console.WriteLine(
                "  the rate the pipeline drains at: first write to last frame received.");
            Console.WriteLine();

            if (results.Any(r => r.CreditStalls > 0))
            {
                Console.WriteLine(
                    "  Credit stalls are the mechanism, not a fault: over inline framing");
                Console.WriteLine(
                    "  a stalled data channel is what leaves the SecureChannel free for");
                Console.WriteLine(
                    "  Publish, which is the property the credit window exists to give.");
                Console.WriteLine();
            }
        }

        /// <summary>
        /// States whether the differences between the cases are large enough
        /// to mean anything.
        /// </summary>
        /// <remarks>
        /// A median is a number whatever the data does, and four medians
        /// printed in a column invite a reader to rank them. When the run to
        /// run spread within one case covers the distance between all of
        /// them, that ranking is noise wearing the costume of a result. This
        /// is the last and most important guard in the benchmark: it is the
        /// difference between reporting a measurement and reporting a
        /// coincidence.
        /// </remarks>
        private static void WriteResolution(List<CaseResult> results)
        {
            if (results.Count < 2)
            {
                return;
            }

            CaseResult baseline = results[0];
            var overlapping = new List<CaseResult>();

            foreach (CaseResult result in results.Skip(1))
            {
                bool disjoint =
                    result.MaxMbits < baseline.MinMbits ||
                    result.MinMbits > baseline.MaxMbits;

                if (!disjoint)
                {
                    overlapping.Add(result);
                }
            }

            if (overlapping.Count == 0)
            {
                Console.WriteLine(
                    "  Every loaded case is separated from the baseline by more than the");
                Console.WriteLine(
                    "  run to run spread, so the differences above are resolvable.");
                Console.WriteLine();
                return;
            }

            Console.WriteLine(
                "  NOT RESOLVABLE: the run to run spread of " +
                string.Join(
                    ", ",
                    overlapping.Select(r => r.PublishingInterval + " ms")) +
                " overlaps the");
            Console.WriteLine(
                $"  baseline's own spread ({baseline.MinMbits:F0}-{baseline.MaxMbits:F0} Mbit/s). The medians can be");
            Console.WriteLine(
                "  ranked but the ranking is not supported by the data: on this machine and");
            Console.WriteLine(
                "  at this load the difference between those cases is smaller than the noise.");
            Console.WriteLine(
                "  Treat them as equal, or reduce the variance before drawing a conclusion.");
            Console.WriteLine();
        }


        /// <remarks>
        /// The data channel throughput column answers only half the question.
        /// The other half is what the stream costs the Service path it shares
        /// the SecureChannel with, and that is the direction the asymmetry
        /// usually runs: a saturated stream barely notices Publish, while
        /// Publish notices the stream a great deal. Reporting only the
        /// throughput column would leave a reader concluding the two do not
        /// interact.
        /// </remarks>
        private static void WriteStarvation(List<CaseResult> results)
        {
            var starved = new List<(CaseResult Result, double Retained)>();

            foreach (CaseResult result in results)
            {
                if (result.PublishingInterval == 0 ||
                    result.IdlePublishRate <= 0 ||
                    result.LoadSeconds <= 0)
                {
                    continue;
                }

                double loaded = result.Notifications / result.LoadSeconds;
                double retained = loaded / result.IdlePublishRate;

                if (retained < 0.8)
                {
                    starved.Add((result, retained));
                }
            }

            if (starved.Count == 0)
            {
                Console.WriteLine(
                    "  The Publish path kept its idle rate under load in every case, so the");
                Console.WriteLine(
                    "  data channel did not starve Service traffic on this run.");
                Console.WriteLine();
                return;
            }

            Console.WriteLine("  Publish throughput retained while the data channel was saturated:");

            foreach ((CaseResult result, double retained) in starved)
            {
                Console.WriteLine(
                    $"    {result.PublishingInterval,5} ms : {retained * 100,5:F0}% of its idle rate " +
                    $"({result.IdlePublishRate:F0} -> {result.Notifications / result.LoadSeconds:F0} notif/s)");
            }

            Console.WriteLine();
        }


        /// <summary>
        /// Checks that the competing load was actually applied.
        /// </summary>
        /// <remarks>
        /// This is the check that decides whether the table above means
        /// anything. If the monitored items never reported, every subscribed
        /// case is really a second copy of the baseline, the numbers agree
        /// with each other beautifully, and the conclusion drawn from them is
        /// entirely false. Saying so loudly is cheaper than believing it.
        /// </remarks>
        private static void WriteValidity(SampleOptions options, List<CaseResult> results)
        {
            var starved = new List<CaseResult>();
            var tooShort = new List<CaseResult>();
            var revised = new List<CaseResult>();
            int repeat = Math.Max(options.Repeat, 1);

            foreach (CaseResult result in results)
            {
                if (result.PublishingInterval == 0 || result.LoadSeconds <= 0)
                {
                    continue;
                }

                double interval = result.RevisedPublishingInterval > 0
                    ? result.RevisedPublishingInterval
                    : result.PublishingInterval;

                if (Math.Abs(interval - result.PublishingInterval) > 0.5)
                {
                    revised.Add(result);
                }

                // The load is judged against the rate this subscription
                // actually reaches with the channel idle, not against
                // items/interval. The two are far apart, and the reason is
                // worth knowing: a Client keeps only a couple of Publish
                // requests outstanding per subscription, so the notification
                // rate is bounded by how often it asks rather than by how
                // often the Server has something to say. Judging against the
                // theoretical rate would condemn every row as invalid when
                // the load is in fact exactly as large as it can be.
                if (result.IdlePublishRate <= 1)
                {
                    starved.Add(result);
                }

                // A run that finishes inside a couple of publishing cycles
                // cannot show contention no matter what the number says:
                // most of the window simply had no Publish traffic in it.
                if (result.LoadSeconds / repeat * 1000.0 < interval * 5)
                {
                    tooShort.Add(result);
                }
            }

            if (revised.Count > 0)
            {
                Console.WriteLine(
                    "  NOTE: the Server revised the publishing interval for " +
                    string.Join(
                        ", ",
                        revised.Select(r =>
                            $"{r.PublishingInterval} ms -> {r.RevisedPublishingInterval:F0} ms")) +
                    ".");
                Console.WriteLine(
                    "  Rows that were revised to the same value are the same experiment run");
                Console.WriteLine(
                    "  twice, however different the requested column looks. Lower the");
                Console.WriteLine(
                    "  Server's MinPublishingInterval to separate them.");
                Console.WriteLine();
            }

            if (starved.Count > 0)
            {
                Console.WriteLine(
                    "  WARNING: no Publish traffic reached the Client at all for " +
                    $"{string.Join(", ", starved.Select(r => r.PublishingInterval + " ms"))}, even");
                Console.WriteLine(
                    "  with the data channel idle. Those rows are a second baseline rather");
                Console.WriteLine(
                    $"  than a loaded case: the {options.MonitoredItems} monitored items are not reporting,");
                Console.WriteLine(
                    "  and nothing below them should be read as a measurement of contention.");
                Console.WriteLine();
            }

            if (tooShort.Count > 0)
            {
                Console.WriteLine(
                    "  WARNING: the measured run is shorter than five publishing cycles for " +
                    $"{string.Join(", ", tooShort.Select(r => r.PublishingInterval + " ms"))}.");
                Console.WriteLine(
                    "  Most of the window had no Publish traffic in it, so those rows");
                Console.WriteLine(
                    "  understate the load rather than measuring it. Raise --frames until");
                Console.WriteLine(
                    "  a run lasts several seconds.");
                Console.WriteLine();
            }
        }

        private static double Median(List<double> values)
        {
            if (values.Count == 0)
            {
                return 0;
            }

            double[] ordered = [.. values.Order()];
            int middle = ordered.Length / 2;

            return ordered.Length % 2 == 1
                ? ordered[middle]
                : (ordered[middle - 1] + ordered[middle]) / 2;
        }

        private sealed record CaseResult(
            int PublishingInterval,
            double RevisedPublishingInterval,
            int MonitoredItems,
            double MedianMbits,
            double MinMbits,
            double MaxMbits,
            int FramesReceived,
            long CreditStalls,
            long Discarded,
            long Notifications,
            double LoadSeconds,
            double IdlePublishRate);
    }
}
