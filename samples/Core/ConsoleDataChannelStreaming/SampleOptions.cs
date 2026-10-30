/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Globalization;
using System.IO;
using Opc.Ua;
using Opc.Ua.DataChannels;

namespace ConsoleDataChannelStreaming
{
    /// <summary>
    /// Explicit finite application choices, independent of carrier negotiation.
    /// </summary>
    internal sealed record SampleOptions
    {
        /// <summary>
        /// Gets the selected carrier; unsupported selections never fall back.
        /// </summary>
        public DataChannelCarrierKind Carrier { get; init; } = DataChannelCarrierKind.DedicatedUasc;

        /// <summary>
        /// Gets whether normal Session Calls additionally authorize the flow.
        /// </summary>
        public bool SessionAuthorization { get; init; }

        /// <summary>
        /// Gets whether the connecting application, rather than the listening application, sends payload.
        /// </summary>
        public bool ClientPublishes { get; init; }

        /// <summary>
        /// Gets the number of complete opaque messages.
        /// </summary>
        public int Count { get; init; } = 100;

        /// <summary>
        /// Gets the complete application message length.
        /// </summary>
        public int Size { get; init; } = 1200;

        /// <summary>
        /// Gets optional pacing, not a transport delivery guarantee.
        /// </summary>
        public int IntervalMilliseconds { get; init; }

        /// <summary>
        /// Gets the finite deadline, including certificate setup and shutdown.
        /// </summary>
        public int TimeoutSeconds { get; init; } = 60;

        /// <summary>
        /// Gets an optional cancellation demonstration deadline starting only after the flow is ready.
        /// </summary>
        public int CancelAfterMilliseconds { get; init; }

        /// <summary>
        /// Gets whether the opener commits a new priority halfway through the run.
        /// </summary>
        public bool Revise { get; init; }

        /// <summary>
        /// Gets whether to compare shared-channel runs with and without competing Services.
        /// </summary>
        public bool Benchmark { get; init; }

        /// <summary>
        /// Gets the bounded number of benchmark repetitions.
        /// </summary>
        public int Repeat { get; init; } = 1;

        /// <summary>
        /// Gets how many real monitored items generate Publish notifications.
        /// </summary>
        public int MonitoredItems { get; init; } = 4;

        /// <summary>
        /// Gets the requested publishing interval; the actual revised value is reported.
        /// </summary>
        public int PublishingInterval { get; init; } = 50;

        /// <summary>
        /// Gets whether ordinary Read and Publish traffic is enabled on a shared connection.
        /// </summary>
        public bool ServiceLoad { get; init; } = true;

        /// <summary>
        /// Gets a fresh, exclusively owned state directory, removed after the run.
        /// </summary>
        public string? StateDirectory { get; init; }

        /// <summary>
        /// Gets the explicit reviewed native QUIC asset.
        /// </summary>
        public string? QuicLibrary { get; init; }

        /// <summary>
        /// Gets the independently supplied expected QUIC asset SHA-256.
        /// </summary>
        public string? QuicSha256 { get; init; }

        /// <summary>
        /// Gets the explicit reviewed native HTTP/3 asset.
        /// </summary>
        public string? Http3Library { get; init; }

        /// <summary>
        /// Gets the independently supplied expected HTTP/3 asset SHA-256.
        /// </summary>
        public string? Http3Sha256 { get; init; }

        /// <summary>
        /// Gets whether only command-line help was requested.
        /// </summary>
        public bool ShowHelp { get; init; }

        /// <summary>
        /// Gets whether this carrier has an ordinary Service lane.
        /// </summary>
        public bool Shared => Carrier == DataChannelCarrierKind.SharedUasc;

        /// <summary>
        /// Parses strict choices rather than interpreting unknown input as a default transport.
        /// </summary>
        public static SampleOptions Parse(string[] arguments)
        {
            var options = new SampleOptions
            {
                QuicLibrary = Environment.GetEnvironmentVariable("OPCUA_QUIC_LIBRARY") ??
                    Environment.GetEnvironmentVariable("OPCUA_QUIC_NATIVE_PATH"),
                QuicSha256 = Environment.GetEnvironmentVariable("OPCUA_QUIC_SHA256") ??
                    Environment.GetEnvironmentVariable("OPCUA_QUIC_NATIVE_SHA256"),
                Http3Library = Environment.GetEnvironmentVariable("OPCUA_HTTP3_LIBRARY") ??
                    Environment.GetEnvironmentVariable("OPCUA_HTTP3_NATIVE_PATH"),
                Http3Sha256 = Environment.GetEnvironmentVariable("OPCUA_HTTP3_SHA256") ??
                    Environment.GetEnvironmentVariable("OPCUA_HTTP3_NATIVE_SHA256")
            };
            for (int index = 0; index < arguments.Length; index++)
            {
                string argument = arguments[index];
                options = argument switch
                {
                    "--help" or "-h" => options with { ShowHelp = true },
                    "--carrier" => options with { Carrier = ParseCarrier(Next(arguments, ref index)) },
                    "--authorization" => options with
                    {
                        SessionAuthorization = Choice(Next(arguments, ref index), "configured", "session")
                    },
                    "--publisher" => options with
                    {
                        ClientPublishes = Choice(Next(arguments, ref index), "server", "client")
                    },
                    "--mode" => options with
                    {
                        Benchmark = Choice(Next(arguments, ref index), "stream", "benchmark")
                    },
                    "--count" => options with { Count = Number(Next(arguments, ref index), 1, 1_000_000) },
                    "--size" => options with { Size = Number(Next(arguments, ref index), 4, 32768) },
                    "--interval-ms" => options with
                    {
                        IntervalMilliseconds = Number(Next(arguments, ref index), 0, 1000)
                    },
                    "--timeout" => options with { TimeoutSeconds = Number(Next(arguments, ref index), 1, 300) },
                    "--cancel-after-ms" => options with
                    {
                        CancelAfterMilliseconds = Number(Next(arguments, ref index), 1, 60000)
                    },
                    "--repeat" => options with { Repeat = Number(Next(arguments, ref index), 1, 10) },
                    "--monitored-items" => options with
                    {
                        MonitoredItems = Number(Next(arguments, ref index), 1, 1000)
                    },
                    "--publishing-interval" => options with
                    {
                        PublishingInterval = Number(Next(arguments, ref index), 10, 1000)
                    },
                    "--state" => options with { StateDirectory = Next(arguments, ref index) },
                    "--quic-library" => options with { QuicLibrary = Next(arguments, ref index) },
                    "--quic-sha256" => options with { QuicSha256 = Next(arguments, ref index) },
                    "--http3-library" => options with { Http3Library = Next(arguments, ref index) },
                    "--http3-sha256" => options with { Http3Sha256 = Next(arguments, ref index) },
                    "--revise" => options with { Revise = true },
                    _ => throw new ArgumentException($"Unknown argument '{argument}'. Use --help.")
                };
            }
            if (!options.ShowHelp)
            {
                options.Validate();
            }
            return options;
        }

        /// <summary>
        /// Validates application combinations before creating certificates or opening sockets.
        /// </summary>
        public void Validate()
        {
            if (Revise && Count < 2)
            {
                throw new ArgumentException("--revise requires at least two messages.");
            }
            if (Benchmark && !Shared)
            {
                throw new ArgumentException("The competing Read/Publish benchmark requires --carrier shared.");
            }
            if (Carrier is DataChannelCarrierKind.Quic or DataChannelCarrierKind.WebTransport)
            {
                ValidateAsset(QuicLibrary, QuicSha256, "QUIC");
            }
            if (Carrier == DataChannelCarrierKind.WebTransport)
            {
                ValidateAsset(Http3Library, Http3Sha256, "HTTP/3");
            }
        }

        /// <summary>
        /// Prints only supported finite modes.
        /// </summary>
        public static void PrintUsage()
        {
            Console.WriteLine(
                "ConsoleDataChannelStreaming (.NET 10)\n" +
                "  --carrier dedicated|shared|quic|webtransport (default dedicated)\n" +
                "  --authorization configured|session (default configured)\n" +
                "  --publisher server|client (default server; client always opens the flow)\n" +
                "  --count N --size N --interval-ms N --timeout SECONDS\n" +
                "  --cancel-after-ms N (cancel after traffic starts; expected nonzero exit)\n" +
                "  --revise (commit a live priority revision halfway through the messages)\n" +
                "  --mode stream|benchmark --repeat N --monitored-items N --publishing-interval MS\n" +
                "  --state NEW-DIRECTORY (owned PKI; removed at shutdown)\n" +
                "  --quic-library ABSOLUTE-PATH --quic-sha256 EXPECTED-HASH\n" +
                "  --http3-library ABSOLUTE-PATH --http3-sha256 EXPECTED-HASH\n" +
                "Native assets may also be selected with OPCUA_QUIC_LIBRARY/OPCUA_QUIC_SHA256 and\n" +
                "OPCUA_HTTP3_LIBRARY/OPCUA_HTTP3_SHA256. No fallback, implicit UASC, or browser mode.");
        }

        /// <summary>
        /// Rejects missing or unreviewed assets without substituting another carrier.
        /// </summary>
        private static void ValidateAsset(string? path, string? hash, string name)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path) ||
                !IsSha256(hash))
            {
                throw new ArgumentException(
                    $"{name} requires an explicit existing absolute library path and reviewed SHA-256; no fallback.");
            }
        }

        /// <summary>
        /// Validates the expected digest without accepting a locally computed replacement.
        /// </summary>
        private static bool IsSha256(string? value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }
            foreach (char character in value)
            {
                if (!char.IsAsciiHexDigit(character))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Reads a required argument value.
        /// </summary>
        private static string Next(string[] arguments, ref int index)
        {
            if (++index >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index]))
            {
                throw new ArgumentException("An option value is missing.");
            }
            return arguments[index];
        }

        /// <summary>
        /// Parses a finite integer range without coercion.
        /// </summary>
        private static int Number(string value, int minimum, int maximum)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ||
                number < minimum || number > maximum)
            {
                throw new ArgumentException($"'{value}' must be an integer in [{minimum}, {maximum}].");
            }
            return number;
        }

        /// <summary>
        /// Parses one of two explicit choices.
        /// </summary>
        private static bool Choice(string value, string first, string second)
        {
            return value == first ? false : value == second ? true :
                throw new ArgumentException($"'{value}' must be '{first}' or '{second}'.");
        }

        /// <summary>
        /// Keeps carrier selection independent of payload and authorization choices.
        /// </summary>
        private static DataChannelCarrierKind ParseCarrier(string value)
        {
            return value switch
            {
                "dedicated" => DataChannelCarrierKind.DedicatedUasc,
                "shared" => DataChannelCarrierKind.SharedUasc,
                "quic" => DataChannelCarrierKind.Quic,
                "webtransport" => DataChannelCarrierKind.WebTransport,
                _ => throw new ArgumentException($"Unknown carrier '{value}'. No transport fallback is available.")
            };
        }
    }
}
