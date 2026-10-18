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
using ConsoleDataChannelStreaming;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.DataChannels;

namespace Quickstarts.ConsoleDataChannelAudio
{
    /// <summary>
    /// Streams finite opaque PCM messages over real authenticated sockets, with playback or explicit WAV output.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Returns success only after exact byte verification, sink finalization and complete carrier cleanup.
        /// </summary>
        public static async Task<int> Main(string[] args)
        {
            try
            {
                AudioOptions options = AudioOptions.Parse(args);
                if (options.Connection.ShowHelp)
                {
                    AudioOptions.PrintUsage();
                    return 0;
                }
                using ILoggerFactory logs = LoggerFactory.Create(builder => builder.AddConsole()
                    .SetMinimumLevel(LogLevel.Error));
                var telemetry = new SampleTelemetry(logs);
                using var deadline = new CancellationTokenSource(
                    TimeSpan.FromSeconds(options.Connection.TimeoutSeconds));
                ConsoleCancelEventHandler handler = (_, eventArgs) =>
                {
                    eventArgs.Cancel = true;
                    deadline.Cancel();
                };
                Console.CancelKeyPress += handler;
                try
                {
                    SampleResult result = await RunAsync(options, telemetry, deadline.Token).ConfigureAwait(false);
                    Console.WriteLine("SUMMARY " + JsonSerializer.Serialize(
                        result, SampleResultJsonContext.Default.SampleResult));
                    return 0;
                }
                finally
                {
                    Console.CancelKeyPress -= handler;
                }
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("FAILED: cancelled or finite deadline exceeded; no complete audio result.");
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
                EntryPointNotFoundException or BadImageFormatException or NAudio.MmException)
            {
                Console.Error.WriteLine($"FAILED: {exception}");
                Console.Error.WriteLine(exception.StackTrace);
                return 1;
            }
        }

        /// <summary>
        /// Reuses the streaming sample's common flow logic; the only audio-specific concerns are PCM and the sink.
        /// </summary>
        private static async Task<SampleResult> RunAsync(
            AudioOptions options,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken)
        {
            IAudioSink sink = AudioSink.Create(options.WavePath);
            await using var sinkLifetime = sink.ConfigureAwait(false);
            var source = new AudioStreamingSource(options.FrameMilliseconds);
            Console.WriteLine($"OPC UA opaque PCM: {AudioFormat.SampleRate} Hz, 16-bit little-endian, mono; " +
                $"{options.DurationMilliseconds} ms requested; {sink.Description}");
            StreamingHarness harness = await StreamingHarness.CreateAsync(options.Connection, telemetry,
                SampleIdentityPair.ModelNamespace + ":pcm-s16le-44100-mono",
                AudioFormat.ContentType, cancellationToken).ConfigureAwait(false);
            SampleResult result;
            try
            {
                result = await MessageWorkload.RunAsync(harness, options.Connection, source.GetMessage,
                    (_, pcm, token) => sink.WriteAsync(pcm, token), cancellationToken).ConfigureAwait(false);
                await sink.CompleteAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
            return result with
            {
                ResourcesReleased = harness.ResourcesReleased,
                WaveFile = options.WavePath ?? string.Empty
            };
        }
    }
}
