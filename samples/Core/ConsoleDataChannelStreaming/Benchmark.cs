/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace ConsoleDataChannelStreaming
{
    /// <summary>
    /// Measures received payload throughput with and without concurrent ordinary Read/Publish traffic.
    /// These loopback observations are not a scheduler latency bound or specification-conformance claim.
    /// </summary>
    internal static class Benchmark
    {
        /// <summary>
        /// Runs a finite round-robin comparison and rejects an unobserved competing load.
        /// </summary>
        public static async Task RunAsync(
            SampleOptions options,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken)
        {
            Console.WriteLine("Measured loopback payload throughput; certificate/startup time excluded.");
            Console.WriteLine("No universal Service-latency or transport-conformance claim is made.");
            for (int repetition = 0; repetition < options.Repeat; repetition++)
            {
                foreach (bool load in new[] { false, true })
                {
                    SampleResult result = await Program.RunOnceAsync(
                        options with { Benchmark = false, ServiceLoad = load }, telemetry, cancellationToken)
                        .ConfigureAwait(false);
                    if (load && (result.ReadResponsesDuringStreaming == 0 ||
                        result.PublishResponsesDuringStreaming == 0 || result.NotificationsDuringStreaming == 0))
                    {
                        throw new InvalidOperationException(
                            "No complete competing Read/Publish cycle occurred while streaming. " +
                            "Increase --count or --interval-ms; no benchmark result is claimed.");
                    }
                    Console.WriteLine($"BENCHMARK repetition={repetition + 1} services={load} " +
                        JsonSerializer.Serialize(result, SampleResultJsonContext.Default.SampleResult));
                }
            }
        }
    }
}
