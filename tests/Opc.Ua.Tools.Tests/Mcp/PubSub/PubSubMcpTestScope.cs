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

#if NET10_0
using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Gives one legacy test a real caller policy and one exact loopback datagram destination.
    /// The shared namespace host remains default-deny.
    /// </summary>
    internal sealed class PubSubMcpTestScope : IAsyncDisposable
    {
        /// <summary>
        /// Creates an explicitly enabled local test host without starting a runtime or connecting a UA session.
        /// </summary>
        internal PubSubMcpTestScope(bool allowMethodBinding = false)
        {
            Port = PubSubMcpTestHelpers.ReserveEphemeralLoopbackPort();
            NetworkInterface loopback = NetworkInterface.GetAllNetworkInterfaces().First(network =>
                network.GetIPProperties().UnicastAddresses.Any(address => address.Address.Equals(IPAddress.Loopback)));
            ArrayOf<McpCapability> capabilities = allowMethodBinding
                ? [McpCapability.PubSubRuntime, McpCapability.PubSubBridge]
                : [McpCapability.PubSubRuntime];
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUaMcpCore(new OpcUaMcpOptions
            {
                Execution = new McpExecutionOptions
                {
                    EnabledCapabilities = capabilities,
                    CallerGrants =
                    [
                        new McpCallerGrant
                        {
                            Caller = s_caller, Capabilities = capabilities,
                            SessionNames = allowMethodBinding ? [McpTestEnvironment.SessionName] : []
                        }
                    ]
                }
            });
            ArrayOf<McpPubSubAdapterRule> adapters = [];
            if (allowMethodBinding)
            {
                OpcUaSessionManager.SessionInfo session =
                    McpTestEnvironment.SessionManager.GetSessionInfoOrThrow(McpTestEnvironment.SessionName);
                adapters =
                [
                    new McpPubSubAdapterRule
                    {
                        SessionName = session.Name, Endpoint = session.Endpoint.EndpointUrl!,
                        Operation = "call", NodeId = ObjectIds.Server.ToString(),
                        MethodId = MethodIds.Server_GetMonitoredItems.ToString()
                    }
                ];
            }
            services.AddOpcUaMcpPubSub(new McpPubSubOptions
            {
                AdapterRules = adapters,
                DatagramRules =
                [
                    new McpPubSubDatagramRule
                    {
                        Endpoint = $"opc.udp://127.0.0.1:{Port}", NetworkInterface = loopback.Name,
                        LocalAddress = "127.0.0.1", Operations = ["publish", "subscribe"],
                        Peers =
                        [
                            new McpPubSubPeerRule { Address = "127.0.0.1", FirstPort = 1, LastPort = 65535 }
                        ]
                    }
                ]
            });
            m_services = services.BuildServiceProvider();
        }

        /// <summary>
        /// Gets the one destination port authorized by this host.
        /// </summary>
        internal int Port { get; }

        /// <summary>
        /// Gets the isolated session registry, whose existing-session registrations remain borrowed.
        /// </summary>
        internal OpcUaSessionManager Sessions => m_services.GetRequiredService<OpcUaSessionManager>();

        /// <summary>
        /// Enters the exact test caller in the test method's execution context.
        /// </summary>
        internal IDisposable Enter()
        {
            return m_services.GetRequiredService<McpCallerContext>().EnterTrustedCaller(s_caller);
        }

        /// <summary>
        /// Creates an isolated legacy runtime governed by the real host policy and transport provider.
        /// </summary>
        internal PubSubRuntimeManager NewManager()
        {
            return new PubSubRuntimeManager(m_services, NullLogger<PubSubRuntimeManager>.Instance);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_services.DisposeAsync();
        }

        /// <summary>The exact trusted identity used only inside explicitly scoped legacy tests.</summary>
        private static readonly McpCallerIdentity s_caller = new("legacy-pubsub-test", "urn:test:local-host");

        /// <summary>The isolated host owns its policy, providers, and borrowed-session registry.</summary>
        private readonly ServiceProvider m_services;
    }
}
#endif
