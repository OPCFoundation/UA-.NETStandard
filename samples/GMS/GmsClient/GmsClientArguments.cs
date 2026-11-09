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

namespace GmsSample.Client
{
    /// <summary>
    /// Bounds passive reads and requires both an exact target and consent for result acknowledgement.
    /// </summary>
    internal sealed record GmsClientArguments
    {
        /// <summary>
        /// Gets the discovery URL.
        /// </summary>
        public string Endpoint { get; private init; } = "opc.tcp://127.0.0.1:62558/GmsServer";

        /// <summary>
        /// Gets an optional exact system browse name, required before acknowledgement.
        /// </summary>
        public string? SystemName { get; private init; }

        /// <summary>
        /// Gets the maximum window per observation; zero skips subscriptions.
        /// </summary>
        public int Seconds { get; private init; } = 5;

        /// <summary>
        /// Gets the notification bound; result queries additionally cap their page at sixteen identifiers.
        /// </summary>
        public int Count { get; private init; } = 8;

        /// <summary>
        /// Gets the per-file byte bound.
        /// </summary>
        public int MaximumFileBytes { get; private init; } = 32 * 1024;

        /// <summary>
        /// Gets whether optional files should be read into a bounded in-memory ByteString.
        /// </summary>
        public bool Download { get; private init; }

        /// <summary>
        /// Gets whether to observe events, retained environmental conditions and intermediate characteristics.
        /// </summary>
        public bool Events { get; private init; }

        /// <summary>
        /// Gets the explicit characteristic placeholder name in the selected instance's namespace.
        /// </summary>
        public string? Characteristic { get; private init; }

        /// <summary>
        /// Gets the exact result identifier to acknowledge; null leaves all result state untouched.
        /// </summary>
        public string? AcknowledgeResult { get; private init; }

        /// <summary>
        /// Gets explicit consent to change the targeted result's acknowledgement state.
        /// </summary>
        public bool ConfirmAcknowledgement { get; private init; }

        /// <summary>
        /// Gets whether bounded local-demo error and cancellation cleanup checks are requested.
        /// </summary>
        public bool VerifyCleanup { get; private init; }

        /// <summary>
        /// Gets explicit loopback-only consent to accept an unknown server certificate.
        /// </summary>
        public bool DemoTrust { get; private init; }

        /// <summary>
        /// Gets the retained certificate-store directory.
        /// </summary>
        public string PkiRoot { get; private init; } = Path.Combine("artifacts", "gms-pki", "client");

        /// <summary>
        /// Rejects unsafe trust exceptions, unbounded observations and ambiguous state-changing targets.
        /// </summary>
        /// <exception cref="ArgumentException">An option, endpoint or acknowledgement consent is invalid.</exception>
        internal static GmsClientArguments Parse(ArrayOf<string> arguments)
        {
            var options = new GmsClientArguments();
            for (int i = 0; i < arguments.Count; i++)
            {
                options = arguments[i] switch
                {
                    "--endpoint" => options with { Endpoint = SampleArguments.ReadValue(arguments, ref i) },
                    "--system" => options with { SystemName = SampleArguments.ReadValue(arguments, ref i) },
                    "--seconds" => options with { Seconds = SampleArguments.ReadInteger(arguments, ref i, 0, 15) },
                    "--count" => options with { Count = SampleArguments.ReadInteger(arguments, ref i, 1, 64) },
                    "--max-file-bytes" => options with
                    {
                        MaximumFileBytes = SampleArguments.ReadInteger(arguments, ref i, 1, 1024 * 1024)
                    },
                    "--pki" => options with { PkiRoot = SampleArguments.ReadValue(arguments, ref i) },
                    "--characteristic" => options with { Characteristic = SampleArguments.ReadValue(arguments, ref i) },
                    "--acknowledge" => options with { AcknowledgeResult = SampleArguments.ReadValue(arguments, ref i) },
                    "--confirm-acknowledgement" => options with { ConfirmAcknowledgement = true },
                    "--events" => options with { Events = true },
                    "--download" => options with { Download = true },
                    "--verify-cleanup" => options with { VerifyCleanup = true },
                    "--demo-trust" => options with { DemoTrust = true },
                    _ => throw new ArgumentException($"Unknown option '{arguments[i]}'.", nameof(arguments))
                };
            }
            if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out Uri? endpoint) ||
                endpoint.Scheme != "opc.tcp" ||
                endpoint.Port < 1 ||
                !string.IsNullOrEmpty(endpoint.UserInfo))
            {
                throw new ArgumentException("--endpoint requires an opc.tcp URL with a port and no credentials.",
                    nameof(arguments));
            }
            if (options.DemoTrust && !endpoint.IsLoopback)
            {
                throw new ArgumentException("--demo-trust is restricted to loopback endpoints.", nameof(arguments));
            }
            if (options.AcknowledgeResult != null &&
                (options.SystemName == null || !options.ConfirmAcknowledgement))
            {
                throw new ArgumentException(
                    "--acknowledge requires an explicit --system and --confirm-acknowledgement.", nameof(arguments));
            }
            if (options.ConfirmAcknowledgement && options.AcknowledgeResult == null)
            {
                throw new ArgumentException("--confirm-acknowledgement requires --acknowledge <result-id>.",
                    nameof(arguments));
            }
            if (options.Characteristic != null && (!options.Events || options.Seconds == 0))
            {
                throw new ArgumentException("--characteristic requires --events and a nonzero --seconds.",
                    nameof(arguments));
            }
            if (options.VerifyCleanup &&
                (!options.DemoTrust || !options.Download || options.SystemName == null || options.Seconds == 0))
            {
                throw new ArgumentException(
                    "--verify-cleanup requires --demo-trust, --download, --system and nonzero --seconds.",
                    nameof(arguments));
            }
            return options;
        }

        /// <summary>
        /// Prints all implemented flags without connecting or changing trust.
        /// </summary>
        internal static void PrintHelp()
        {
            Console.WriteLine("""
                GmsClient [--endpoint <opc.tcp URL>] [--system <browse-name>] [--pki <directory>]
                          [--seconds <0..15>] [--count <1..64>] [--events] [--characteristic <name>]
                          [--download] [--max-file-bytes <1..1048576>] [--demo-trust]
                          [--acknowledge <result-id> --confirm-acknowledgement] [--verify-cleanup] [--help]
                Defaults: 127.0.0.1:62558/GmsServer, all systems, 5 seconds, 8 records, 32768 file bytes.
                Passive by default. Acknowledgement requires an explicit system, result and confirmation.
                No measurement/job/calibration commands, correction writes or implicit condition acknowledgement.
                --events reads retained sensor warnings and live events; --characteristic Bore selects the
                sample's native intermediate-result placeholder in the system's application namespace.
                --download skips absent file transfer explicitly; failures are never reported as empty successes.
                --verify-cleanup demonstrates missing-result/file-limit refusal and cancellation, then reuses
                the sample's single metadata/file-handle capacity. It requires a loopback demo and a system.
                SignAndEncrypt and domain validation remain enabled with --demo-trust; remote use rejects it.
                Two selected systems maximum, 128 KiB metadata/read bounds, 1 MiB data/event collection bounds.
                Query pages contain at most 16 IDs. Use --count 32 to observe beyond the initial advanced inventory.
                All work has a 120-second deadline. Ctrl+C cancels and awaits subscription/session/host cleanup.
                """);
        }
    }
}
