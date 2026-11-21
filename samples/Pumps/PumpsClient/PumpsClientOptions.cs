// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Globalization;

namespace PumpsSample.Client
{
    /// <summary>
    /// Command-line options of the sample client.
    /// </summary>
    internal sealed record PumpsClientOptions
    {
        /// <summary>
        /// The endpoint to connect to.
        /// </summary>
        public string ServerUrl { get; init; } = "opc.tcp://localhost:62547/PumpsServer";

        /// <summary>
        /// Accepts any server certificate. Demo only.
        /// </summary>
        public bool Insecure { get; init; }

        /// <summary>
        /// How long to stream live values, in seconds. 0 skips it.
        /// </summary>
        public int WatchSeconds { get; init; } = 20;

        /// <summary>
        /// Reads the explicitly configured raw, aggregate and event history.
        /// </summary>
        public bool History { get; init; }

        /// <summary>
        /// Refreshes retained conditions and watches their live transitions without acknowledging them.
        /// </summary>
        public bool Conditions { get; init; }

        public static PumpsClientOptions Parse(string[] args)
        {
            var options = new PumpsClientOptions();
            for (int ii = 0; ii < args.Length; ii++)
            {
                string argument = args[ii];
                switch (argument)
                {
                    case "--insecure":
                        options = options with { Insecure = true };
                        break;
                    case "--history":
                        options = options with { History = true };
                        break;
                    case "--conditions":
                        options = options with { Conditions = true };
                        break;
                    case "--url" when ii + 1 < args.Length:
                        options = options with { ServerUrl = args[++ii] };
                        break;
                    case "--seconds" when ii + 1 < args.Length:
                        options = options with
                        {
                            WatchSeconds = int.Parse(args[++ii], CultureInfo.InvariantCulture)
                        };
                        break;
                    default:
                        if (argument.StartsWith("--", StringComparison.Ordinal))
                        {
                            throw new ArgumentException($"Unknown option '{argument}'.", nameof(args));
                        }
                        options = options with { ServerUrl = argument };
                        break;
                }
            }
            return options;
        }
    }
}
