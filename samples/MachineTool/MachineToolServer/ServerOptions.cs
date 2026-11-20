/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using Opc.Ua;
using Opc.Ua.MachineTool;

namespace MachineToolSample.Server
{
    /// <summary>
    /// Selects the model closure, independent instances and explicit simulation permissions.
    /// </summary>
    internal sealed record ServerOptions
    {
        /// <summary>
        /// Gets the selected namespace closure, without forcing a CNC into WMTP-only hosting.
        /// </summary>
        public MachineToolParts Parts { get; private init; } = MachineToolParts.MachineTool;

        /// <summary>
        /// Gets the loopback port; zero selects a currently available port.
        /// </summary>
        public int Port { get; private init; } = 62558;

        /// <summary>
        /// Gets the acquisition window after actual OPC UA startup.
        /// </summary>
        public int Seconds { get; private init; } = 60;

        /// <summary>
        /// Gets the independent instance count per selected family.
        /// </summary>
        public int Instances { get; private init; } = 1;

        /// <summary>
        /// Gets whether optional monitoring, jobs/files and peripheral features are selected.
        /// </summary>
        public bool Advanced { get; private init; }

        /// <summary>
        /// Gets explicit server consent to accept simulated state-changing requests from local anonymous sessions.
        /// </summary>
        public bool AllowCommands { get; private init; }

        /// <summary>
        /// Gets whether simulated peripheral providers refuse every state-changing request.
        /// </summary>
        public bool RefuseCommands { get; private init; }

        /// <summary>
        /// Gets the explicitly selected machine on which an operator simulation may execute the declared job.
        /// </summary>
        public string? SimulateJob { get; private init; }

        /// <summary>
        /// Gets whether sensors retain their first acquisition so the real age policy becomes observable.
        /// </summary>
        public bool FreezeMeasurements { get; private init; }

        /// <summary>
        /// Gets the optional whole-application cancellation timer, including startup.
        /// </summary>
        public int? CancelAfterMilliseconds { get; private init; }

        /// <summary>
        /// Gets the explicit loopback-only certificate trust exception.
        /// </summary>
        public bool DemoTrust { get; private init; }

        /// <summary>
        /// Gets the application-owned certificate-store root.
        /// </summary>
        public string PkiRoot { get; private init; } = Path.Combine("artifacts", "machinetool-pki", "server");

        /// <summary>
        /// Validates all selections before opening a host, listener or certificate store.
        /// </summary>
        internal static ServerOptions Parse(ArrayOf<string> arguments)
        {
            var options = new ServerOptions();
            for (int i = 0; i < arguments.Count; i++)
            {
                options = arguments[i] switch
                {
                    "--mode" => options with { Parts = ReadParts(arguments, ref i) },
                    "--port" => options with { Port = SampleArguments.ReadInteger(arguments, ref i, 0, 65535) },
                    "--seconds" => options with { Seconds = SampleArguments.ReadInteger(arguments, ref i, 1, 600) },
                    "--instances" => options with { Instances = SampleArguments.ReadInteger(arguments, ref i, 1, 4) },
                    "--pki" => options with { PkiRoot = SampleArguments.ReadValue(arguments, ref i) },
                    "--advanced" => options with { Advanced = true },
                    "--allow-commands" => options with { AllowCommands = true },
                    "--refuse-commands" => options with { RefuseCommands = true },
                    "--simulate-job" => options with { SimulateJob = SampleArguments.ReadValue(arguments, ref i) },
                    "--freeze-measurements" => options with { FreezeMeasurements = true },
                    "--demo-trust" => options with { DemoTrust = true },
                    "--cancel-after-ms" => options with
                    {
                        CancelAfterMilliseconds = SampleArguments.ReadInteger(arguments, ref i, 1, 60000)
                    },
                    _ => throw new ArgumentException($"Unknown option '{arguments[i]}'.", nameof(arguments))
                };
            }
            if (options.AllowCommands && (!options.Advanced || !options.DemoTrust))
            {
                throw new ArgumentException(
                    "--allow-commands requires --advanced and --demo-trust.", nameof(arguments));
            }
            if (options.RefuseCommands && !options.AllowCommands)
            {
                throw new ArgumentException("--refuse-commands requires --allow-commands.", nameof(arguments));
            }
            if (options.FreezeMeasurements &&
                (!options.Advanced || (options.Parts & MachineToolParts.Wmtp) == 0))
            {
                throw new ArgumentException("--freeze-measurements requires advanced WMTP.", nameof(arguments));
            }
            if (options.SimulateJob != null)
            {
                bool selected = false;
                for (int i = 1; i <= options.Instances; i++)
                {
                    selected |= options.SimulateJob == FormattableString.Invariant($"Machine{i}");
                }
                if (!options.AllowCommands || (options.Parts & MachineToolParts.MachineTool) == 0 || !selected)
                {
                    throw new ArgumentException(
                        "--simulate-job requires --allow-commands and an existing Machine1..Machine4 target.",
                        nameof(arguments));
                }
            }
            return options;
        }

        /// <summary>
        /// Describes the selected simulation and its bounds.
        /// </summary>
        internal static void PrintHelp()
        {
            Console.WriteLine("""
                MachineToolServer [--mode machine|wmtp|mixed] [--advanced] [--instances 1..4]
                                  [--port 0..65535] [--seconds 1..600] [--pki directory]
                                  [--demo-trust] [--allow-commands] [--refuse-commands]
                                  [--simulate-job Machine1] [--freeze-measurements]
                                  [--cancel-after-ms 1..60000]
                Defaults: mandatory-only MachineTool, one instance, port 62558, 60 seconds.
                Binds 127.0.0.1 only; READY follows actual OPC UA startup, not Generic Host startup.
                --advanced selects monitoring/jobs/files and all eight WMTP sensor kinds/records.
                --allow-commands requires --advanced --demo-trust; authorization otherwise denies changes.
                --simulate-job explicitly models one operator's execution/part observations, never hardware.
                Each peripheral accepts one configuration/calibration/time/transfer of each kind;
                later requests of the same kind are refused.
                --demo-trust accepts unknown certificates ONLY FOR LOCAL SIMULATION; encryption stays required.
                File contents are application-owned, read-only, bounded and not executable CNC programs.
                Ctrl+C or the optional cancellation timer joins acquisition and disposes resources.
                PKI is retained for certificate administration; never share it between the applications.
                """);
        }

        /// <summary>
        /// Maps the explicit hosting mode to the existing model selection.
        /// </summary>
        private static MachineToolParts ReadParts(ArrayOf<string> arguments, ref int index)
        {
            return SampleArguments.ReadValue(arguments, ref index) switch
            {
                "machine" => MachineToolParts.MachineTool,
                "wmtp" => MachineToolParts.Wmtp,
                "mixed" => MachineToolParts.All,
                _ => throw new ArgumentException("--mode must be machine, wmtp or mixed.", nameof(arguments))
            };
        }
    }
}
