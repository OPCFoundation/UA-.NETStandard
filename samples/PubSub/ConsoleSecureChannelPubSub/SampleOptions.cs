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
using System.Globalization;
using System.IO;
using Opc.Ua.DataChannels;

namespace Quickstarts.ConsoleSecureChannelPubSub
{
    /// <summary>
    /// Finite, explicit selections shared by both process roles.
    /// </summary>
    internal sealed record SampleOptions
    {
        /// <summary>
        /// Gets the process role: demo, setup, server, or client.
        /// </summary>
        internal string Mode { get; init; } = "demo";

        /// <summary>
        /// Gets whether Services and PubSub share the authenticated UASC connection.
        /// </summary>
        internal bool Shared { get; init; }

        /// <summary>
        /// Gets whether the ordinary Session grants each flow and revision.
        /// </summary>
        internal bool SessionAuthorization { get; init; }

        /// <summary>
        /// Gets whether the transport initiator publishes rather than subscribes.
        /// </summary>
        internal bool Reverse { get; init; }

        /// <summary>
        /// Gets the exact number of samples produced by each writer.
        /// </summary>
        internal int Count { get; init; } = 8;

        /// <summary>
        /// Gets the bound for establishment, transmission and cleanup.
        /// </summary>
        internal int TimeoutSeconds { get; init; } = 45;

        /// <summary>
        /// Gets the requested TCP port, with zero selecting a free loopback port.
        /// </summary>
        internal int Port { get; init; }

        /// <summary>
        /// Gets the explicitly owned certificate-store root.
        /// </summary>
        internal string? Pki { get; init; }

        /// <summary>
        /// Gets the atomic server readiness file used by separate processes.
        /// </summary>
        internal string? ReadyFile { get; init; }

        /// <summary>
        /// Gets the optional machine-readable evidence file.
        /// </summary>
        internal string? ResultFile { get; init; }

        /// <summary>
        /// Gets whether to print command help without starting any resources.
        /// </summary>
        internal bool Help { get; init; }

        /// <summary>
        /// Gets the requested carrier, independent from encoding.
        /// </summary>
        internal DataChannelCarrierKind Carrier =>
            Shared ? DataChannelCarrierKind.SharedUasc : DataChannelCarrierKind.DedicatedUasc;

        /// <summary>
        /// Parses only supported options; misspellings and unsupported prerequisites fail closed.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static SampleOptions Parse(string[] args)
        {
            var options = new SampleOptions();
            int index = 0;
            if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal))
            {
                options = options with { Mode = args[index++] };
            }
            while (index < args.Length)
            {
                string option = args[index++];
                switch (option)
                {
                    case "--help":
                        options = options with { Help = true };
                        break;
                    case "--shared":
                        options = options with { Shared = true };
                        break;
                    case "--reverse":
                        options = options with { Reverse = true };
                        break;
                    case "--authorization":
                        string authorization = Value(args, ref index, option);
                        options = options with
                        {
                            SessionAuthorization = authorization switch
                            {
                                "configured" => false,
                                "session" => true,
                                _ => throw new ArgumentException("--authorization must be configured or session.")
                            }
                        };
                        break;
                    case "--count":
                        options = options with { Count = Integer(args, ref index, option, 4, 1000) };
                        break;
                    case "--timeout":
                        options = options with { TimeoutSeconds = Integer(args, ref index, option, 5, 300) };
                        break;
                    case "--port":
                        options = options with { Port = Integer(args, ref index, option, 0, 65535) };
                        break;
                    case "--pki":
                        options = options with { Pki = Path.GetFullPath(Value(args, ref index, option)) };
                        break;
                    case "--ready":
                        options = options with { ReadyFile = Path.GetFullPath(Value(args, ref index, option)) };
                        break;
                    case "--result":
                        options = options with { ResultFile = Path.GetFullPath(Value(args, ref index, option)) };
                        break;
                    default:
                        throw new ArgumentException($"Unknown option: {option}. Use --help.");
                }
            }
            if (options.Mode is not ("demo" or "setup" or "server" or "client"))
            {
                throw new ArgumentException("Mode must be demo, setup, server, or client.");
            }
            if (!options.Help && options.Mode != "demo" && options.Pki == null)
            {
                throw new ArgumentException("Separate processes require --pki and the setup command first.");
            }
            if (!options.Help && options.Mode is "server" or "client" && options.ReadyFile == null)
            {
                throw new ArgumentException("Separate server/client modes require the same --ready file.");
            }
            return options;
        }

        /// <summary>
        /// Returns one mandatory option argument.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static string Value(string[] args, ref int index, string option)
        {
            return index < args.Length
                ? args[index++]
                : throw new ArgumentException($"{option} requires a value.");
        }

        /// <summary>
        /// Validates a bounded decimal option.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static int Integer(string[] args, ref int index, string option, int minimum, int maximum)
        {
            string value = Value(args, ref index, option);
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) &&
                parsed >= minimum &&
                parsed <= maximum
                    ? parsed
                    : throw new ArgumentException($"{option} must be between {minimum} and {maximum}.");
        }
    }
}
