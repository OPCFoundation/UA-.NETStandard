// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Globalization;
using System.IO;
using Opc.Ua;

namespace CranesHoistsSample.Client
{
    /// <summary>
    /// Bounds reads and observation and requires explicit selection before any configuration request.
    /// </summary>
    internal sealed record CraneClientOptions
    {
        /// <summary>
        /// Gets the discovery endpoint.
        /// </summary>
        public string Endpoint { get; private init; } = "opc.tcp://localhost:62557/CranesHoistsServer";

        /// <summary>
        /// Gets the optional exact system browse name.
        /// </summary>
        public string? SystemName { get; private init; }

        /// <summary>
        /// Gets the exact axis browse name used only for an explicitly requested limit write.
        /// </summary>
        public string AxisName { get; private init; } = "Lift";

        /// <summary>
        /// Gets the maximum duration of each observation; zero skips observation.
        /// </summary>
        public int Seconds { get; private init; } = 5;

        /// <summary>
        /// Gets the independent notification cap for each observation.
        /// </summary>
        public int Count { get; private init; } = 5;

        /// <summary>
        /// Gets whether to observe standard events on the Server notifier as well as selected data values.
        /// </summary>
        public bool Events { get; private init; }

        /// <summary>
        /// Gets explicit consent to request a plus-direction speed-limit setpoint, not to move an axis.
        /// </summary>
        public double? RequestedLimit { get; private init; }

        /// <summary>
        /// Gets whether to verify the fresh sample provider's one-request capacity refusal.
        /// </summary>
        public bool ProveRefusal { get; private init; }

        /// <summary>
        /// Gets whether unknown certificates may be accepted for a loopback-only demonstration.
        /// </summary>
        public bool DemoTrust { get; private init; }

        /// <summary>
        /// Gets the private certificate-store root.
        /// </summary>
        public string PkiRoot { get; private init; } =
            Path.Combine("artifacts", "craneshoists-pki", "client");

        /// <summary>
        /// Rejects unbounded arguments, ambiguous write targets and remote demo-trust endpoints.
        /// </summary>
        /// <exception cref="ArgumentException">An option, target or explicit consent is invalid.</exception>
        internal static CraneClientOptions Parse(ArrayOf<string> arguments)
        {
            var options = new CraneClientOptions();
            for (int i = 0; i < arguments.Count; i++)
            {
                options = arguments[i] switch
                {
                    "--endpoint" => options with { Endpoint = SampleArguments.ReadValue(arguments, ref i) },
                    "--system" => options with { SystemName = SampleArguments.ReadValue(arguments, ref i) },
                    "--axis" => options with { AxisName = SampleArguments.ReadValue(arguments, ref i) },
                    "--seconds" => options with { Seconds = SampleArguments.ReadInteger(arguments, ref i, 0, 20) },
                    "--count" => options with { Count = SampleArguments.ReadInteger(arguments, ref i, 1, 100) },
                    "--pki" => options with { PkiRoot = SampleArguments.ReadValue(arguments, ref i) },
                    "--request-limit" => options with { RequestedLimit = ReadLimit(arguments, ref i) },
                    "--prove-refusal" => options with { ProveRefusal = true },
                    "--events" => options with { Events = true },
                    "--demo-trust" => options with { DemoTrust = true },
                    _ => throw new ArgumentException($"Unknown option '{arguments[i]}'.", nameof(arguments))
                };
            }
            if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out Uri? endpoint) ||
                endpoint.Scheme != "opc.tcp" ||
                endpoint.Port < 1)
            {
                throw new ArgumentException(
                    "--endpoint must be an absolute opc.tcp URL with a port.", nameof(arguments));
            }
            if (options.DemoTrust && !endpoint.IsLoopback)
            {
                throw new ArgumentException("--demo-trust is restricted to loopback endpoints.", nameof(arguments));
            }
            if (options.RequestedLimit.HasValue && options.SystemName == null)
            {
                throw new ArgumentException("--request-limit requires an explicit --system.", nameof(arguments));
            }
            if (options.ProveRefusal && (!options.RequestedLimit.HasValue || !options.DemoTrust))
            {
                throw new ArgumentException(
                    "--prove-refusal requires --request-limit and --demo-trust with a fresh sample server.",
                    nameof(arguments));
            }
            return options;
        }

        /// <summary>
        /// Documents all implemented flags without touching a session or certificate store.
        /// </summary>
        internal static void PrintHelp()
        {
            Console.WriteLine("""
                CranesHoistsClient --endpoint <opc.tcp URL> --system <browse-name> --axis <browse-name>
                                  --seconds <0..20> --count <1..100> --pki <directory>
                                  [--events] [--demo-trust] [--request-limit <0..100>]
                                  [--prove-refusal] [--help]
                Defaults: localhost:62557/CranesHoistsServer, all systems, Lift axis, 5 seconds, 5 notifications.
                Read-only unless --request-limit is given with an explicit --system; no motion commands.
                --events observes the Server notifier (all sources), not an invented crane event type.
                --seconds 0 skips observations. Each observation also has a 128 KiB decoded-payload bound.
                --demo-trust accepts unknown certificates on LOOPBACK ONLY; SignAndEncrypt remains required.
                --prove-refusal sends a second explicit request to a fresh sample's one-request provider.
                All work has a 90-second deadline. Ctrl+C cancels observation and awaits session/host cleanup.
                PKI is retained for trust administration; use separate server/client directories.
                """);
        }

        /// <summary>
        /// Consumes a finite standard percentage, including the valid zero and one-hundred boundaries.
        /// </summary>
        /// <exception cref="ArgumentException">The percentage is nonfinite or outside the valid range.</exception>
        private static double ReadLimit(ArrayOf<string> arguments, ref int index)
        {
            string value = SampleArguments.ReadValue(arguments, ref index);
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
                double.IsNaN(number) ||
                double.IsInfinity(number) ||
                number is < 0 or > 100)
            {
                throw new ArgumentException("--request-limit requires a finite percentage in [0, 100].",
                    nameof(arguments));
            }
            return number;
        }
    }
}
