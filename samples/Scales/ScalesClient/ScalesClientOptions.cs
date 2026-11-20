// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Globalization;

namespace ScalesSample.Client
{
    /// <summary>
    /// Command-line options of the sample client.
    /// </summary>
    internal sealed record ScalesClientOptions
    {
        /// <summary>
        /// The endpoint to connect to.
        /// </summary>
        public string ServerUrl { get; init; } = "opc.tcp://localhost:62548/ScalesServer";

        /// <summary>
        /// Accepts any server certificate. Demo only.
        /// </summary>
        public bool Insecure { get; init; }

        /// <summary>
        /// How long to stream live values, in seconds. 0 skips it.
        /// </summary>
        public int WatchSeconds { get; init; } = 20;

        public static ScalesClientOptions Parse(string[] args)
        {
            var options = new ScalesClientOptions();
            for (int ii = 0; ii < args.Length; ii++)
            {
                string argument = args[ii];
                switch (argument)
                {
                    case "--insecure":
                        options = options with { Insecure = true };
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
