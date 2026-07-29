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

            DataChannel source = harness.Source;
            DataChannel sink = harness.Sink;

            Task consumer = Task.Run(
                async () => await ConsumeAsync(sink, options, ct).ConfigureAwait(false),
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

            await WaitForSentAsync(source, options.FrameCount, ct).ConfigureAwait(false);
            await harness.CloseDataChannelAsync(ct).ConfigureAwait(false);

            await consumer.ConfigureAwait(false);
            stopwatch.Stop();

            Report(source, sink, harness, options, stopwatch.Elapsed);

            await harness.DisposeAsync().ConfigureAwait(false);
        }

        private static async Task ConsumeAsync(
            DataChannel sink,
            SampleOptions options,
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

                    Console.WriteLine(
                        $"  gap: frames {message.GapFrom}..{message.GapTo} never arrived; " +
                        $"resuming{(message.IsMarker ? " at a marker" : string.Empty)}");
                }
            }

            Console.WriteLine($"consumed {received} frames, {gaps} reported gaps");
        }

        private static async Task WaitForSentAsync(
            DataChannel source,
            int frameCount,
            CancellationToken ct)
        {
            while (source.GetDiagnostics().FramesSent < (uint)frameCount &&
                !ct.IsCancellationRequested)
            {
                await Task.Delay(10, ct).ConfigureAwait(false);
            }
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
