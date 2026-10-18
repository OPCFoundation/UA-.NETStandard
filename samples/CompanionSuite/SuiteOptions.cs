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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Opc.Ua;

namespace CompanionSuite
{
    /// <summary>
    /// Selects one bounded execution mode.
    /// </summary>
    internal enum SuiteMode
    {
        /// <summary>Serve simulated observations for a finite window.</summary>
        Server,

        /// <summary>Read the sample endpoint over a real managed session.</summary>
        Client,

        /// <summary>Start the server and exercise the real TCP client, then stop both.</summary>
        SelfCheck
    }

    /// <summary>
    /// Explicit resource bounds and opt-in policies, parsed before host construction.
    /// </summary>
    internal sealed record SuiteOptions
    {
        /// <summary>
        /// Gets the execution mode; serving requires no interactive input.
        /// </summary>
        internal SuiteMode Mode { get; private init; }

        /// <summary>
        /// Gets the endpoint; port zero requests a free port for server/self-check.
        /// </summary>
        internal string Endpoint { get; private init; } = "opc.tcp://localhost:48485/CompanionSuite";

        /// <summary>
        /// Gets the explicitly selected port, retaining port zero on the legacy CLR.
        /// </summary>
        internal int EndpointPort { get; private init; } = 48485;

        /// <summary>
        /// Gets the finite server window.
        /// </summary>
        internal int Seconds { get; private init; } = 20;

        /// <summary>
        /// Gets the number of independent instances per family, from one through two.
        /// </summary>
        internal int Instances { get; private init; } = 1;

        /// <summary>
        /// Gets the explicit local certificate-trust exception.
        /// </summary>
        internal bool DemoTrust { get; private init; }

        /// <summary>
        /// Gets whether the client explicitly exercises the standard simulated vision job methods.
        /// </summary>
        internal bool Exercise { get; private init; }

        /// <summary>
        /// Gets the certificate-store root; it is not a file-provider mount.
        /// </summary>
        internal string PkiRoot { get; private init; } = Path.GetFullPath(".companion-suite-pki");

        /// <summary>
        /// Rejects conflicting modes, unknown arguments, invalid bounds and nonloopback demo trust.
        /// </summary>
        /// <exception cref="ArgumentException">An option is missing, duplicated or invalid.</exception>
        internal static SuiteOptions Parse(ArrayOf<string> arguments)
        {
            var result = new SuiteOptions();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool modeSelected = false;
            for (int i = 0; i < arguments.Count; i++)
            {
                string option = arguments[i];
                if (!seen.Add(option))
                {
                    throw new ArgumentException("Repeated option: " + option);
                }
                switch (option)
                {
                    case "--server":
                    case "--client":
                    case "--selfcheck":
                        if (modeSelected)
                        {
                            throw new ArgumentException("Select exactly one execution mode.");
                        }
                        modeSelected = true;
                        result = result with
                        {
                            Mode = option == "--server" ? SuiteMode.Server :
                                option == "--client" ? SuiteMode.Client : SuiteMode.SelfCheck
                        };
                        break;
                    case "--endpoint":
                        result = result with { Endpoint = Next(arguments, ref i, option) };
                        break;
                    case "--pki-root":
                        result = result with { PkiRoot = Path.GetFullPath(Next(arguments, ref i, option)) };
                        break;
                    case "--seconds":
                        result = result with { Seconds = Number(Next(arguments, ref i, option), 1, 300) };
                        break;
                    case "--instances":
                        result = result with { Instances = Number(Next(arguments, ref i, option), 1, 2) };
                        break;
                    case "--demo-trust":
                        result = result with { DemoTrust = true };
                        break;
                    case "--exercise":
                        result = result with { Exercise = true };
                        break;
                    default:
                        throw new ArgumentException("Unknown option: " + option);
                }
            }
            if (!Uri.TryCreate(result.Endpoint, UriKind.Absolute, out Uri? endpoint) ||
                endpoint.Scheme != "opc.tcp" ||
                !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) ||
                !string.IsNullOrEmpty(endpoint.Fragment))
            {
                throw new ArgumentException(
                    "Supply an absolute opc.tcp endpoint with a valid port and no credentials, query or fragment.");
            }
            int port = ReadPort(endpoint);
            if (port < 0 || port > 65535 || (result.Mode == SuiteMode.Client && port == 0))
            {
                throw new ArgumentException("Supply an explicit port; zero is allowed only for server/selfcheck.");
            }
            result = result with { EndpointPort = port };
            bool loopback = string.Equals(endpoint.DnsSafeHost, "localhost", StringComparison.OrdinalIgnoreCase) ||
                (IPAddress.TryParse(endpoint.DnsSafeHost, out IPAddress? address) && IPAddress.IsLoopback(address));
            if (result.DemoTrust && !loopback)
            {
                throw new ArgumentException("--demo-trust is restricted to explicit loopback endpoints.");
            }
            if (result.Exercise && result.Mode == SuiteMode.Server)
            {
                throw new ArgumentException("--exercise belongs to --client or --selfcheck, not the passive server.");
            }
            return result;
        }

        /// <summary>
        /// Resolves the exact listener URL used by server registration and the same-process TCP client.
        /// </summary>
        internal string CreateListenerEndpoint()
        {
            var requested = new Uri(Endpoint);
            var endpoint = new UriBuilder(requested) { Port = EndpointPort };
            IPAddress? address;
            if (string.Equals(requested.DnsSafeHost, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                address = IPAddress.Loopback;
                endpoint.Host = address.ToString();
            }
            else if (!IPAddress.TryParse(requested.DnsSafeHost, out address))
            {
                address = null;
            }
            if (DemoTrust && (address == null || !IPAddress.IsLoopback(address)))
            {
                throw new ArgumentException("A demo-trust listener requires a numeric loopback address.");
            }
            if (endpoint.Port == 0)
            {
                IPAddress probeAddress = address ?? IPAddress.Loopback;
                using var socket = new Socket(probeAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socket.Bind(new IPEndPoint(probeAddress, 0));
                endpoint.Port = ((IPEndPoint)socket.LocalEndPoint!).Port;
            }
            return endpoint.Uri.AbsoluteUri;
        }

        /// <summary>
        /// Shows bounded, noninteractive commands without generating trust automatically.
        /// </summary>
        internal static void PrintHelp()
        {
            Console.WriteLine(
                "CompanionSuite [--server|--client|--selfcheck] [--endpoint opc.tcp://localhost:48485/CompanionSuite]");
            Console.WriteLine("  --instances 1..2  --seconds 1..300  --pki-root PATH");
            Console.WriteLine("  --demo-trust     Explicit loopback-only trust for disposable local demonstrations.");
            Console.WriteLine("  --exercise       Opt in to one standard simulated MachineVision job per system.");
            Console.WriteLine("  --verify-endpoint-config  Check listener URL selection without starting a server.");
            Console.WriteLine(
                "Defaults: finite server, one instance/family, 20 seconds, SignAndEncrypt, strict trust.");
        }

        /// <summary>
        /// Distinguishes explicit port zero from an omitted port on runtimes that normalize both to minus one.
        /// </summary>
        private static int ReadPort(Uri endpoint)
        {
            if (endpoint.Port >= 0)
            {
                return endpoint.Port;
            }
#if NETFRAMEWORK
            // The .NET Framework custom-scheme parser loses :0. A known-scheme parse preserves that input;
            // this URI is used only for parsing, never for discovery, hosting, or a conformance declaration.
            return Uri.TryCreate("http" + endpoint.OriginalString.Substring(endpoint.Scheme.Length),
                UriKind.Absolute, out Uri? parsed) && parsed.Port == 0 ? 0 : -1;
#else
            return -1;
#endif
        }

        /// <summary>
        /// Consumes an option value without accepting the next option as its value.
        /// </summary>
        private static string Next(ArrayOf<string> arguments, ref int position, string option)
        {
            if (++position >= arguments.Count || arguments[position].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Missing value for " + option);
            }
            return arguments[position];
        }

        /// <summary>
        /// Parses an invariant finite integer bound.
        /// </summary>
        private static int Number(string text, int minimum, int maximum)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ||
                value < minimum || value > maximum)
            {
                throw new ArgumentException($"Expected a number between {minimum} and {maximum}.");
            }
            return value;
        }
    }
}
