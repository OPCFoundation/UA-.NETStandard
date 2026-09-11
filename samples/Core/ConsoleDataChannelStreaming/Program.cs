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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Bindings;

namespace ConsoleDataChannelStreaming
{
    /// <summary>
    /// Streams a synthetic media-like byte stream over an OPC UA data
    /// channel and reports what the transport actually did with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point of the sample is that the <b>same</b> application code
    /// drives both framings. Only the transport differs: inline framing
    /// interleaves STR MessageChunks with Service traffic on one
    /// connection and carries its own credit-based flow control, while
    /// opc.quic binds the channel to its own QUIC stream and lets QUIC do
    /// the flow control. Nothing above the transport changes.
    /// </para>
    /// <para>
    /// Experimental: the OPC UA Data Channels errata is a working draft
    /// and every identifier it uses is provisional.
    /// </para>
    /// </remarks>
    internal static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var options = SampleOptions.Parse(args);

            if (options.ShowHelp)
            {
                SampleOptions.PrintUsage();
                return 0;
            }

            Console.WriteLine("OPC UA data channel streaming sample (experimental)");
            Console.WriteLine("===================================================");
            Console.WriteLine($"transport : {options.Transport}");
            Console.WriteLine($"run mode  : {options.RunMode}");
            Console.WriteLine($"frames    : {options.FrameCount} x {options.FrameSize} bytes");
            Console.WriteLine($"delivery  : {options.DeliveryMode}");
            Console.WriteLine();

            if (options.Transport == SampleTransport.Quic && !QuicTransport.IsSupported)
            {
                Console.Error.WriteLine(
                    "QUIC is unavailable on this platform (msquic is missing). " +
                    "Re-run with --transport tcp.");
                return 2;
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                if (options.RunMode == SampleRunMode.Benchmark)
                {
                    await Benchmark.RunAsync(options, cts.Token).ConfigureAwait(false);
                    return 0;
                }

                await RunAsync(options, cts.Token).ConfigureAwait(false);
                return 0;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("cancelled");
                return 0;
            }
            catch (ServiceResultException e)
            {
                Console.Error.WriteLine($"failed: {e.Message}");
                return 1;
            }
            catch (NotSupportedException e)
            {
                Console.Error.WriteLine($"pending: {e.Message}");
                return 3;
            }
        }

        private static async Task RunAsync(SampleOptions options, CancellationToken ct)
        {
            StreamingHarness harness = await StreamingHarness
                .CreateAsync(options, ct)
                .ConfigureAwait(false);

            StreamingRun run = await StreamAsync(harness, options, verbose: true, closeChannel: true, ct)
                .ConfigureAwait(false);

            Report(harness.Source, harness.Sink, harness, options, run.Elapsed);

            await harness.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Streams <see cref="SampleOptions.FrameCount"/> frames and measures
        /// how long the sink took to receive all of them.
        /// </summary>
        /// <remarks>
        /// <see cref="DataChannel.Write"/> enqueues rather than blocking, so
        /// the window deliberately runs from the first write to the last
        /// frame received: what is measured is the rate the pipeline drains
        /// at, not the rate the producer can call Write at.
        /// </remarks>
        internal static async Task<StreamingRun> StreamAsync(
            StreamingHarness harness,
            SampleOptions options,
            bool verbose,
            bool closeChannel,
            CancellationToken ct)
        {
            DataChannel source = harness.Source;
            DataChannel sink = harness.Sink;

            Task<int> consumer = Task.Run(
                () => ConsumeAsync(sink, options, verbose, ct),
                CancellationToken.None);

            var payload = new byte[options.FrameSize];
            var stopwatch = Stopwatch.StartNew();

            for (int ii = 0; ii < options.FrameCount && !ct.IsCancellationRequested; ii++)
            {
                // A marker every thirtieth frame stands in for a video key
                // frame: it is where a receiver that has just recovered
                // from a gap can resume without understanding the payload.
                DataChannelFrameFlags flags =
                    DataChannelFrameFlags.MessageStart |
                    DataChannelFrameFlags.MessageEnd;

                if (ii % 30 == 0)
                {
                    flags |= DataChannelFrameFlags.Marker;
                }

                if (options.DeliveryMode is DataChannelDeliveryMode.PartiallyReliable
                    or DataChannelDeliveryMode.Unreliable)
                {
                    flags |= DataChannelFrameFlags.Droppable;
                }

                BitConverter.GetBytes(ii).CopyTo(payload, 0);
                source.Write(payload, flags);
            }

            if (closeChannel)
            {
                await harness.CloseDataChannelAsync(ct).ConfigureAwait(false);
            }

            int received = await consumer.ConfigureAwait(false);
            stopwatch.Stop();

            return new StreamingRun(received, stopwatch.Elapsed);
        }

        /// <summary>
        /// What one measured streaming run produced.
        /// </summary>
        /// <param name="FramesReceived">Frames the sink actually saw.</param>
        /// <param name="Elapsed">First write to last frame received.</param>
        internal readonly record struct StreamingRun(int FramesReceived, TimeSpan Elapsed);

        private static async Task<int> ConsumeAsync(
            DataChannel sink,
            SampleOptions options,
            bool verbose,
            CancellationToken ct)
        {
            int received = 0;
            int gaps = 0;

            while (received < options.FrameCount && !ct.IsCancellationRequested)
            {
                using DataChannelMessage? message = await sink
                    .ReadAsync(ct)
                    .ConfigureAwait(false);

                if (message == null)
                {
                    break;
                }

                received++;

                if (StatusCode.IsUncertain(message.Status))
                {
                    gaps++;

                    if (verbose)
                    {
                        Console.WriteLine(
                            $"  gap: frames {message.GapFrom}..{message.GapTo} never arrived; " +
                            $"resuming{(message.IsMarker ? " at a marker" : string.Empty)}");
                    }
                }
            }

            if (verbose)
            {
                Console.WriteLine($"consumed {received} frames, {gaps} reported gaps");
            }

            return received;
        }

        private static void Report(
            DataChannel source,
            DataChannel sink,
            StreamingHarness harness,
            SampleOptions options,
            TimeSpan elapsed)
        {
            DataChannelDiagnosticsDataType sent = source.GetDiagnostics();
            DataChannelDiagnosticsDataType got = sink.GetDiagnostics();

            double megabits = sent.BytesSent * 8 / 1_000_000.0;
            double seconds = elapsed.TotalSeconds > 0 ? elapsed.TotalSeconds : double.Epsilon;

            Console.WriteLine();
            Console.WriteLine("result");
            Console.WriteLine("------");
            Console.WriteLine($"  framing          : {harness.FramingMode}");
            Console.WriteLine($"  channel id       : {harness.ChannelId}");
            Console.WriteLine($"  revised max frame: {harness.RevisedParameters.MaxFrameSize}");
            Console.WriteLine($"  revised credit   : {harness.RevisedParameters.InitialCredit}");
            Console.WriteLine($"  revised delivery : {harness.RevisedParameters.DeliveryMode}");
            Console.WriteLine($"  transport chan id: {harness.RevisedTransportChannelId}");
            Console.WriteLine($"  frames sent      : {sent.FramesSent}");
            Console.WriteLine($"  frames received  : {got.FramesReceived}");
            Console.WriteLine($"  bytes sent       : {sent.BytesSent}");
            Console.WriteLine($"  frames discarded : {sent.FramesDiscarded}");
            Console.WriteLine($"  credit stalls    : {sent.CreditStalls}");
            Console.WriteLine($"  elapsed          : {elapsed.TotalMilliseconds:F0} ms");
            Console.WriteLine($"  throughput       : {megabits / seconds:F1} Mbit/s");
            Console.WriteLine();

            if (harness.FramingMode == DataChannelFramingMode.Quic)
            {
                Console.WriteLine(
                    "  Over opc.quic the credit-stall counter stays at zero: QUIC applies its\n" +
                    "  own per-stream and per-connection flow control, so no CREDIT frame is\n" +
                    "  sent or expected. Duplicating the window in two layers gains nothing and\n" +
                    "  deadlocks when the two disagree.");
            }
            else
            {
                Console.WriteLine(
                    "  Over inline framing the credit window is this layer's own: a consumer\n" +
                    "  that cannot keep up stalls its channel and nothing else, which is what\n" +
                    "  keeps a saturated media stream from starving the Publish path.");
            }
        }
    }
}
