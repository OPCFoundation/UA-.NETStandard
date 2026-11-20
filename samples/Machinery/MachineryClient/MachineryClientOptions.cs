/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Globalization;

namespace MachinerySample.Client
{
    /// <summary>
    /// Command-line options of the sample client.
    /// </summary>
    internal sealed record MachineryClientOptions
    {
        /// <summary>
        /// The endpoint to connect to.
        /// </summary>
        public string ServerUrl { get; init; } =
            "opc.tcp://localhost:62546/MachineryServer";

        /// <summary>
        /// Accepts any server certificate. Demo only.
        /// </summary>
        public bool Insecure { get; init; }

        /// <summary>
        /// Downloads the latest result through OPC 40001-101.
        /// </summary>
        public bool Download { get; init; } = true;

        /// <summary>
        /// How long to stream state transitions, in seconds.
        /// </summary>
        public int ObserveSeconds { get; init; } = 12;

        public static MachineryClientOptions Parse(string[] args)
        {
            var options = new MachineryClientOptions();
            for (int ii = 0; ii < args.Length; ii++)
            {
                string argument = args[ii];
                switch (argument)
                {
                    case "--insecure":
                        options = options with { Insecure = true };
                        break;
                    case "--no-download":
                        options = options with { Download = false };
                        break;
                    case "--url" when ii + 1 < args.Length:
                        options = options with { ServerUrl = args[++ii] };
                        break;
                    case "--seconds" when ii + 1 < args.Length:
                        options = options with
                        {
                            ObserveSeconds = int.Parse(
                                args[++ii],
                                CultureInfo.InvariantCulture)
                        };
                        break;
                    default:
                        if (argument.StartsWith("--", StringComparison.Ordinal))
                        {
                            throw new ArgumentException(
                                $"Unknown option '{argument}'.",
                                nameof(args));
                        }
                        options = options with { ServerUrl = argument };
                        break;
                }
            }
            return options;
        }
    }
}
