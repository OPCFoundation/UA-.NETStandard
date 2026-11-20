// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;

namespace LadsSample.Client
{
    /// <summary>
    /// Command line options of the client.
    /// </summary>
    internal sealed record ClientOptions
    {
        public string Endpoint { get; init; } = "opc.tcp://localhost:62565/LadsServer";

        public string? PkiRoot { get; init; }

        public bool AutoAccept { get; init; } = true;

        /// <summary>
        /// Parses <c>[--url endpoint] [--pki-root path] [--autoaccept true|false]</c>.
        /// </summary>
        public static ClientOptions Parse(string[] args)
        {
            var options = new ClientOptions();
            for (int ii = 0; ii < args.Length; ii++)
            {
                string argument = args[ii];
                switch (argument)
                {
                    case "--url" when ii + 1 < args.Length:
                        options = options with { Endpoint = args[++ii] };
                        break;
                    case "--pki-root" when ii + 1 < args.Length && Path.IsPathFullyQualified(args[ii + 1]):
                        options = options with { PkiRoot = args[++ii] };
                        break;
                    case "--autoaccept" when ii + 1 < args.Length && bool.TryParse(args[ii + 1], out bool accept):
                        ii++;
                        options = options with { AutoAccept = accept };
                        break;
                    default:
                        throw new ArgumentException($"Invalid option '{argument}'.", nameof(args));
                }
            }
            return options;
        }
    }
}
