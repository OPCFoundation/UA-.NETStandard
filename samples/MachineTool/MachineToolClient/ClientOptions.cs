/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.IO;
using Opc.Ua;

namespace MachineToolSample.Client
{
    /// <summary>
    /// Bounds passive workflows and requires a target plus explicit consent before any state-changing request.
    /// </summary>
    internal sealed record ClientOptions
    {
        /// <summary>
        /// Gets the secure discovery endpoint.
        /// </summary>
        public string Endpoint { get; private init; } = "opc.tcp://127.0.0.1:62558/MachineToolServer";

        /// <summary>
        /// Gets an optional exact application browse name, mandatory for commands.
        /// </summary>
        public string? Target { get; private init; }

        /// <summary>
        /// Gets the single explicitly selected command; none means strictly passive.
        /// </summary>
        public string Command { get; private init; } = "none";

        /// <summary>
        /// Gets explicit consent to the selected simulation command.
        /// </summary>
        public bool Consent { get; private init; }

        /// <summary>
        /// Gets an explicitly selected sensor for configuration.
        /// </summary>
        public string? Sensor { get; private init; }

        /// <summary>
        /// Gets the requested sensor update interval.
        /// </summary>
        public int? TriggerMilliseconds { get; private init; }

        /// <summary>
        /// Gets the single explicitly selected work-record index to delete.
        /// </summary>
        public int? DeleteIndex { get; private init; }

        /// <summary>
        /// Gets the bounded count of generated nonsecret demonstration bytes.
        /// </summary>
        public int? TransferBytes { get; private init; }

        /// <summary>
        /// Gets whether a second explicit configuration should prove the fresh sample's capacity refusal.
        /// </summary>
        public bool ProveRefusal { get; private init; }

        /// <summary>
        /// Gets whether to read the bounded, read-only demonstration program through the existing file client.
        /// </summary>
        public bool ReadFiles { get; private init; }

        /// <summary>
        /// Gets the one-shot observation window; zero skips observation.
        /// </summary>
        public int Seconds { get; private init; } = 5;

        /// <summary>
        /// Gets the one-shot notification limit.
        /// </summary>
        public int Count { get; private init; } = 5;

        /// <summary>
        /// Gets the accepted discovery ceiling.
        /// </summary>
        public int MaximumDevices { get; private init; } = 8;

        /// <summary>
        /// Gets the optional whole-application cancellation timer.
        /// </summary>
        public int? CancelAfterMilliseconds { get; private init; }

        /// <summary>
        /// Gets whether unknown certificates may be accepted at a loopback endpoint only.
        /// </summary>
        public bool DemoTrust { get; private init; }

        /// <summary>
        /// Gets the application's separate certificate-store root.
        /// </summary>
        public string PkiRoot { get; private init; } = Path.Combine("artifacts", "machinetool-pki", "client");

        /// <summary>
        /// Rejects unbounded inputs, remote demo trust, implicit mutation and ambiguous command selection.
        /// </summary>
        internal static ClientOptions Parse(ArrayOf<string> arguments)
        {
            var options = new ClientOptions();
            for (int i = 0; i < arguments.Count; i++)
            {
                options = arguments[i] switch
                {
                    "--endpoint" => options with { Endpoint = SampleArguments.ReadValue(arguments, ref i) },
                    "--target" => options with { Target = SampleArguments.ReadValue(arguments, ref i) },
                    "--command" => options with { Command = SampleArguments.ReadValue(arguments, ref i) },
                    "--sensor" => options with { Sensor = SampleArguments.ReadValue(arguments, ref i) },
                    "--trigger-ms" => options with
                    {
                        TriggerMilliseconds = SampleArguments.ReadInteger(arguments, ref i, 1, 60000)
                    },
                    "--delete-index" => options with
                    {
                        DeleteIndex = SampleArguments.ReadInteger(arguments, ref i, 1, 3)
                    },
                    "--transfer-bytes" => options with
                    {
                        TransferBytes = SampleArguments.ReadInteger(arguments, ref i, 1, 1024)
                    },
                    "--seconds" => options with { Seconds = SampleArguments.ReadInteger(arguments, ref i, 0, 20) },
                    "--count" => options with { Count = SampleArguments.ReadInteger(arguments, ref i, 1, 100) },
                    "--maximum-devices" => options with
                    {
                        MaximumDevices = SampleArguments.ReadInteger(arguments, ref i, 1, 32)
                    },
                    "--cancel-after-ms" => options with
                    {
                        CancelAfterMilliseconds = SampleArguments.ReadInteger(arguments, ref i, 1, 60000)
                    },
                    "--pki" => options with { PkiRoot = SampleArguments.ReadValue(arguments, ref i) },
                    "--consent" => options with { Consent = true },
                    "--prove-refusal" => options with { ProveRefusal = true },
                    "--read-files" => options with { ReadFiles = true },
                    "--demo-trust" => options with { DemoTrust = true },
                    _ => throw new ArgumentException($"Unknown option '{arguments[i]}'.", nameof(arguments))
                };
            }
            if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out Uri? endpoint) ||
                endpoint.Scheme != "opc.tcp" || endpoint.Port < 1)
            {
                throw new ArgumentException(
                    "--endpoint must be an absolute opc.tcp URL with a port.", nameof(arguments));
            }
            if (options.DemoTrust && !endpoint.IsLoopback)
            {
                throw new ArgumentException("--demo-trust is restricted to loopback endpoints.", nameof(arguments));
            }
            if (options.Command is not ("none" or "configure" or "calibrate-on" or "calibrate-off" or
                "set-time" or "delete-work" or "abort-work" or "firmware" or "license" or "job-store-start"))
            {
                throw new ArgumentException("Unknown --command; see --help.", nameof(arguments));
            }
            if (options.Command != "none" && (!options.Consent || options.Target == null))
            {
                throw new ArgumentException(
                    "A command requires --consent and an explicit --target.", nameof(arguments));
            }
            if (options.Command == "configure" && (options.Sensor == null || !options.TriggerMilliseconds.HasValue))
            {
                throw new ArgumentException("configure requires --sensor and --trigger-ms.", nameof(arguments));
            }
            if (options.Command == "delete-work" && !options.DeleteIndex.HasValue)
            {
                throw new ArgumentException("delete-work requires --delete-index.", nameof(arguments));
            }
            if (options.Command is "firmware" or "license" && !options.TransferBytes.HasValue)
            {
                throw new ArgumentException("A transfer requires --transfer-bytes.", nameof(arguments));
            }
            if (options.ProveRefusal && (options.Command != "configure" || !options.DemoTrust))
            {
                throw new ArgumentException(
                    "--prove-refusal requires configure and --demo-trust on a fresh sample.", nameof(arguments));
            }
            if (options.ReadFiles && options.Target == null)
            {
                throw new ArgumentException("--read-files requires an explicit machine --target.", nameof(arguments));
            }
            return options;
        }

        /// <summary>
        /// Documents the passive default and every implemented opt-in.
        /// </summary>
        internal static void PrintHelp()
        {
            Console.WriteLine("""
                MachineToolClient [--endpoint opc.tcp-URL] [--target browse-name]
                                  [--seconds 0..20] [--count 1..100] [--maximum-devices 1..32]
                                  [--pki directory] [--demo-trust] [--read-files]
                                  [--cancel-after-ms 1..60000]
                Passive by default: discovery, actual capabilities, raw snapshots, nonconsuming records,
                and ONE bounded observation. No calibration, deletion, configuration, abort, transfer or job start.
                Commands REQUIRE --target AND --consent; the server independently authorizes each request:
                  --command configure --sensor Temperature --trigger-ms 2000 [--prove-refusal]
                  --command calibrate-on|calibrate-off
                  --command set-time                  (current UTC, explicit UTC zone)
                  --command delete-work --delete-index 1..3
                  --command abort-work                (explicit peripheral-scoped abort)
                  --command firmware|license --transfer-bytes 1..1024
                  --command job-store-start           (the documented DemoOrder/DemoWorkMaster)
                Transfers use generated nonsecret DEMO bytes, not installed firmware or activated licenses.
                Job acceptance is NOT execution; only the server's separate --simulate-job models execution.
                --demo-trust is LOOPBACK ONLY; SignAndEncrypt and SHA-256 remain required.
                Work has a 90-second deadline. Exit codes: 0 completed, 1 failure/refusal, 2 cancelled.
                """);
        }
    }
}
