/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using Opc.Ua;

namespace CranesHoistsSample.Server
{
    /// <summary>
    /// Selects a bounded, explicitly simulated standalone crane demonstration.
    /// </summary>
    internal sealed record CraneServerOptions
    {
        /// <summary>
        /// Gets the listen port; zero selects an available local port before startup.
        /// </summary>
        public int Port { get; private init; } = 62557;

        /// <summary>
        /// Gets the maximum running window after startup.
        /// </summary>
        public int Seconds { get; private init; } = 60;

        /// <summary>
        /// Gets the number of independent crane/provider pairs.
        /// </summary>
        public int Instances { get; private init; } = 1;

        /// <summary>
        /// Gets whether the optional monitoring and limit-request capabilities are selected.
        /// </summary>
        public bool Advanced { get; private init; }

        /// <summary>
        /// Gets whether this local simulation explicitly authorizes anonymous limit requests.
        /// </summary>
        public bool AllowLimitRequests { get; private init; }

        /// <summary>
        /// Gets whether untrusted peer certificates may be accepted for a local demonstration.
        /// </summary>
        public bool DemoTrust { get; private init; }

        /// <summary>
        /// Gets the private certificate-store root, relative to the working directory by default.
        /// </summary>
        public string PkiRoot { get; private init; } =
            Path.Combine("artifacts", "craneshoists-pki", "server");

        /// <summary>
        /// Parses every option and rejects unsupported or unbounded settings.
        /// </summary>
        /// <exception cref="ArgumentException">An option is unknown, unbounded or missing required consent.</exception>
        internal static CraneServerOptions Parse(ArrayOf<string> arguments)
        {
            var options = new CraneServerOptions();
            for (int i = 0; i < arguments.Count; i++)
            {
                options = arguments[i] switch
                {
                    "--port" => options with { Port = SampleArguments.ReadInteger(arguments, ref i, 0, 65535) },
                    "--seconds" => options with { Seconds = SampleArguments.ReadInteger(arguments, ref i, 1, 600) },
                    "--instances" => options with { Instances = SampleArguments.ReadInteger(arguments, ref i, 1, 4) },
                    "--pki" => options with { PkiRoot = SampleArguments.ReadValue(arguments, ref i) },
                    "--advanced" => options with { Advanced = true },
                    "--allow-limit-requests" => options with { AllowLimitRequests = true },
                    "--demo-trust" => options with { DemoTrust = true },
                    _ => throw new ArgumentException($"Unknown option '{arguments[i]}'.", nameof(arguments))
                };
            }
            if (options.AllowLimitRequests && (!options.Advanced || !options.DemoTrust))
            {
                throw new ArgumentException(
                    "--allow-limit-requests requires --advanced and --demo-trust.", nameof(arguments));
            }
            return options;
        }

        /// <summary>
        /// Describes the implemented flags without starting an application or creating certificates.
        /// </summary>
        internal static void PrintHelp()
        {
            Console.WriteLine("""
                CranesHoistsServer --port <0..65535> --seconds <1..600> --instances <1..4>
                                  --pki <directory> [--advanced] [--demo-trust]
                                  [--allow-limit-requests] [--help]
                Defaults: port 62557, 60 seconds, one mandatory-only crane, certificate trust required.
                --port 0 selects a free local port; READY prints the actual endpoint.
                --advanced selects load, brakes, protections, events and standard limit-request properties.
                --demo-trust accepts unknown certificates for LOCAL TESTING ONLY; encryption stays enabled.
                --allow-limit-requests requires both flags above and authorizes anonymous demo sessions.
                Each simulated provider accepts at most ONE request; later requests are explicitly refused.
                Accepted requests NEVER change provider-reported active limits or move equipment.
                Ctrl+C cancels startup/acquisition and awaits cleanup. PKI is retained for trust administration.
                """);
        }
    }
}
