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
using System.Net;
using System.Net.NetworkInformation;
using Opc.Ua;

namespace CompanionSuite
{
    /// <summary>
    /// Bounded checks of the actual listener-selection path; no application host or listener is started.
    /// </summary>
    internal static class EndpointConfigurationChecks
    {
        /// <summary>
        /// Verifies hostname normalization, explicit address families, automatic ports and strict-trust intent.
        /// </summary>
        internal static void Verify()
        {
            Check("DefaultDemoUsesNumericLoopback",
                ["--server", "--demo-trust"], "127.0.0.1", 48485, numericLoopback: true);
            Check("LocalhostSelfcheckUsesNumericLoopback",
                ["--selfcheck", "--demo-trust", "--endpoint", "opc.tcp://LOCALHOST:48486/Workcell"],
                "127.0.0.1", 48486, numericLoopback: true);
            Check("LocalhostAutomaticPortUsesNumericLoopback",
                ["--selfcheck", "--demo-trust", "--endpoint", "opc.tcp://localhost:0/Workcell"],
                "127.0.0.1", 0, numericLoopback: true);
            Check("ExplicitIpv4LoopbackIsPreserved",
                ["--server", "--demo-trust", "--endpoint", "opc.tcp://127.0.0.2:48486/Workcell"],
                "127.0.0.2", 48486, numericLoopback: true);
            Check("ExplicitIpv6LoopbackIsPreserved",
                ["--server", "--demo-trust", "--endpoint", "opc.tcp://[::1]:48486/Workcell"],
                "::1", 48486, numericLoopback: true);
            Check("Ipv6AutomaticPortIsPreserved",
                ["--selfcheck", "--demo-trust", "--endpoint", "opc.tcp://[::1]:0/Workcell"],
                "::1", 0, numericLoopback: true);
            Check("StrictTrustDnsIntentIsPreserved",
                ["--server", "--endpoint", "opc.tcp://workcell.example:48486/Workcell"],
                "workcell.example", 48486, numericLoopback: false);
            Check("StrictTrustIpv4IntentIsPreserved",
                ["--server", "--endpoint", "opc.tcp://192.0.2.10:48486/Workcell"],
                "192.0.2.10", 48486, numericLoopback: false);
            Check("StrictTrustIpv6IntentIsPreserved",
                ["--server", "--endpoint", "opc.tcp://[2001:db8::10]:48486/Workcell"],
                "2001:db8::10", 48486, numericLoopback: false);
            Reject("DemoWildcardIpv4IsRejected", "opc.tcp://0.0.0.0:48486/Workcell");
            Reject("DemoWildcardIpv6IsRejected", "opc.tcp://[::]:48486/Workcell");
            Reject("DemoNonloopbackDnsIsRejected", "opc.tcp://workcell.example:48486/Workcell");
            Console.WriteLine("ENDPOINT_CONFIGURATION_VERIFIED passed=12 listeners_started=0");
        }

        /// <summary>
        /// Checks the operating system's actual bound sockets before the encrypted self-check uses this endpoint.
        /// </summary>
        internal static void VerifyLoopbackListener(string url)
        {
            var endpoint = new Uri(url);
            if (!IPAddress.TryParse(endpoint.DnsSafeHost, out IPAddress? expected) || !IPAddress.IsLoopback(expected))
            {
                throw new InvalidOperationException("The demo listener URL does not name a numeric loopback address.");
            }
            bool found = false;
            int matches = 0;
            foreach (IPEndPoint listener in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
            {
                if (listener.Port != endpoint.Port)
                {
                    continue;
                }
                if (!IPAddress.IsLoopback(listener.Address))
                {
                    throw new InvalidOperationException(
                        "A wildcard or nonloopback listener exists on the selected demo port.");
                }
                matches++;
                found |= listener.Address.Equals(expected);
            }
            if (!found)
            {
                throw new InvalidOperationException("The selected loopback socket is not listening.");
            }
            Console.WriteLine(
                $"LISTENER_BINDING_VERIFIED address={expected} family={expected.AddressFamily} " +
                $"port={endpoint.Port} loopbackListeners={matches} wildcard=False");
        }

        /// <summary>
        /// Evaluates the same URL-producing method that feeds AddServer and the self-check client.
        /// </summary>
        private static void Check(
            string name, ArrayOf<string> arguments, string host, int port, bool numericLoopback)
        {
            SuiteOptions options = SuiteOptions.Parse(arguments);
            var endpoint = new Uri(options.CreateListenerEndpoint());
            if (endpoint.Scheme != "opc.tcp" ||
                !string.Equals(endpoint.DnsSafeHost, host, StringComparison.OrdinalIgnoreCase) ||
                (port == 0 ? endpoint.Port <= 0 : endpoint.Port != port) ||
                (numericLoopback && (!IPAddress.TryParse(endpoint.DnsSafeHost, out IPAddress? address) ||
                    !IPAddress.IsLoopback(address))))
            {
                string expectedPort = port == 0 ? "automatic" : port.ToString(CultureInfo.InvariantCulture);
                throw new InvalidOperationException(
                    $"{name}: expected host={host} port={expectedPort}; " +
                    $"actual={endpoint.AbsoluteUri}");
            }
            if (endpoint.AbsolutePath != new Uri(options.Endpoint).AbsolutePath)
            {
                throw new InvalidOperationException(name + ": the selected application path changed.");
            }
            Console.WriteLine($"PASS {name} {endpoint.AbsoluteUri}");
        }

        /// <summary>
        /// Requires refusal before a wildcard or remote trust-exception configuration can reach a listener.
        /// </summary>
        private static void Reject(string name, string endpoint)
        {
            try
            {
                _ = SuiteOptions.Parse(["--server", "--demo-trust", "--endpoint", endpoint]);
            }
            catch (ArgumentException)
            {
                Console.WriteLine("PASS " + name);
                return;
            }
            throw new InvalidOperationException(name + ": a nonloopback demo endpoint was accepted.");
        }
    }
}
