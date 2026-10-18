/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.IO;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.DataChannels;

namespace ConsoleDataChannelStreaming
{
    /// <summary>
    /// Runs bounded complete-message streaming over a real explicitly selected authenticated carrier.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Returns success only after exact payload verification and deterministic resource release.
        /// </summary>
        public static async Task<int> Main(string[] args)
        {
            try
            {
                SampleOptions options = SampleOptions.Parse(args);
                if (options.ShowHelp)
                {
                    SampleOptions.PrintUsage();
                    return 0;
                }
                using ILoggerFactory logs = LoggerFactory.Create(builder => builder.AddConsole()
                    .SetMinimumLevel(LogLevel.Error));
                var telemetry = new SampleTelemetry(logs);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
                void Cancel(ConsoleCancelEventArgs eventArgs)
                {
                    eventArgs.Cancel = true;
                    deadline.Cancel();
                }
                ConsoleCancelEventHandler handler = (_, eventArgs) => Cancel(eventArgs);
                Console.CancelKeyPress += handler;
                try
                {
                    Console.WriteLine("OPC UA complete-message streaming (experimental rewritten draft)");
                    if (options.Benchmark)
                    {
                        await Benchmark.RunAsync(options, telemetry, deadline.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        SampleResult result = await RunOnceAsync(options, telemetry, deadline.Token)
                            .ConfigureAwait(false);
                        Console.WriteLine("SUMMARY " + JsonSerializer.Serialize(
                            result, SampleResultJsonContext.Default.SampleResult));
                    }
                    return 0;
                }
                finally
                {
                    Console.CancelKeyPress -= handler;
                }
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("FAILED: cancelled or finite deadline exceeded; no success result.");
                return 130;
            }
            catch (DataChannelException exception)
            {
                Console.Error.WriteLine($"FAILED: data channel result {exception.Result}: {exception}");
                return 1;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                ServiceResultException or IOException or InvalidDataException or NotSupportedException or
                AuthenticationException or CryptographicException or TimeoutException or DllNotFoundException or
                EntryPointNotFoundException or BadImageFormatException)
            {
                Console.Error.WriteLine($"FAILED: {exception}");
                Console.Error.WriteLine(exception.StackTrace);
                return 1;
            }
        }

        /// <summary>
        /// Measures only the actual data run, then closes flows, sessions, sockets and owned PKI before reporting.
        /// </summary>
        internal static async Task<SampleResult> RunOnceAsync(
            SampleOptions options,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken)
        {
            StreamingHarness harness = await StreamingHarness.CreateAsync(options, telemetry,
                SampleIdentityPair.ModelNamespace + ":bytes", "application/octet-stream", cancellationToken)
                .ConfigureAwait(false);
            SampleResult result;
            try
            {
                result = await MessageWorkload.RunAsync(harness, options,
                    index => MessageWorkload.SyntheticMessage(index, options.Size),
                    MessageWorkload.DecodeSyntheticAsync, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
            return result with { ResourcesReleased = harness.ResourcesReleased };
        }
    }
}
