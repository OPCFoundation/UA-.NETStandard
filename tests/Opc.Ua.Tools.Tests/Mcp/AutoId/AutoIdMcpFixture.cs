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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.AutoID.Server;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Tools.Tests.Mcp.AutoId
{
    /// <summary>
    /// A real server with simulated RFID, RTLS and verifier readers and a real managed session, so the MCP
    /// tools are exercised against actual opc.tcp traffic rather than a mocked session.
    /// </summary>
    internal sealed class AutoIdMcpFixture : IAsyncDisposable
    {
        internal const string RfidName = "Gate1";
        internal const string RtlsName = "Yard1";
        internal const string VerifierName = "Verifier1";

        /// <summary>
        /// Gets the simulated RFID reader hardware.
        /// </summary>
        internal InMemoryRfidReaderProvider Rfid { get; } = new(TimeSpan.FromMilliseconds(5));

        /// <summary>
        /// Gets the simulated locating system.
        /// </summary>
        internal InMemoryRtlsProvider Rtls { get; } = new(cycleInterval: TimeSpan.FromMilliseconds(5));

        /// <summary>
        /// Gets the simulated verifier.
        /// </summary>
        internal InMemoryOpticalVerifierProvider Verifier { get; } = new(TimeSpan.FromMilliseconds(5));

        /// <summary>
        /// Gets the managed session the tools borrow.
        /// </summary>
        internal ManagedSession Session { get; private set; } = null!;

        /// <summary>
        /// Gets the node manager that owns the readers.
        /// </summary>
        internal AutoIdNodeManager Manager => m_factory.Manager!;

        /// <summary>
        /// Starts the server, creates the readers and connects the session.
        /// </summary>
        internal static async Task<AutoIdMcpFixture> StartAsync()
        {
            var fixture = new AutoIdMcpFixture();
            try
            {
                await fixture.StartCoreAsync().ConfigureAwait(false);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private async Task StartCoreAsync()
        {
            m_server = new ServerFixture<StandardServer>(telemetry =>
            {
                var server = new StandardServer(telemetry);
                server.AddNodeManager(m_factory);
                return server;
            })
            {
                AutoAccept = true,
                SecurityNone = true
            };
            m_root = System.IO.Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "autoid-mcp",
                Guid.NewGuid().ToString("N")[..8]);
            await m_server.LoadConfigurationAsync(System.IO.Path.Combine(m_root, "server")).ConfigureAwait(false);
            await m_server.StartAsync().ConfigureAwait(false);

            AutoIdRuntime runtime = Manager.Runtime;
            ushort ns = Manager.InstanceNamespaceIndex;
            await runtime.CreateReaderAsync(new QualifiedName(RfidName, ns), Rfid).ConfigureAwait(false);
            await runtime.CreateReaderAsync(new QualifiedName(RtlsName, ns), Rtls).ConfigureAwait(false);
            await runtime.CreateReaderAsync(new QualifiedName(VerifierName, ns), Verifier).ConfigureAwait(false);

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_clientFixture = new ClientFixture(telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(
                System.IO.Path.Combine(m_root, "client"), "AutoIdMcp").ConfigureAwait(false);
            ApplicationConfiguration configuration = m_clientFixture.Config;
            string url = m_server.Config.ServerConfiguration!.BaseAddresses[0];
            EndpointDescription? endpoint = await CoreClientUtils.SelectEndpointAsync(
                configuration, url, useSecurity: false, discoverTimeout: 15000, telemetry).ConfigureAwait(false);
            Session = await new ManagedSessionBuilder(configuration, telemetry)
                .UseEndpoint(new ConfiguredEndpoint(null, endpoint!, EndpointConfiguration.Create(configuration)))
                .WithSessionName("AutoIdMcp")
                .ConnectAsync().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Session != null)
            {
                await Session.DisposeAsync().ConfigureAwait(false);
            }
            m_clientFixture?.Dispose();
            if (m_server != null)
            {
                await m_server.StopAsync().ConfigureAwait(false);
            }
            Rfid.Dispose();
            Rtls.Dispose();
            Verifier.Dispose();
            if (m_root != null && System.IO.Directory.Exists(m_root))
            {
                System.IO.Directory.Delete(m_root, recursive: true);
            }
        }

        /// <summary>
        /// Creates the AutoID node manager and remembers it, so the test can reach its runtime.
        /// </summary>
        private sealed class CapturingFactory : IAsyncNodeManagerFactory
        {
            internal AutoIdNodeManager? Manager { get; private set; }

            public ArrayOf<string> NamespacesUris => m_inner.NamespacesUris;

            public async ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                IAsyncNodeManager manager = await m_inner.CreateAsync(server, configuration, cancellationToken)
                    .ConfigureAwait(false);
                Manager = (AutoIdNodeManager)manager;
                return manager;
            }

            private readonly AutoIdNodeManagerFactory m_inner = new();
        }

        private readonly CapturingFactory m_factory = new();
        private ServerFixture<StandardServer>? m_server;
        private ClientFixture? m_clientFixture;
        private string? m_root;
    }
}
#endif
