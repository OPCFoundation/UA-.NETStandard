/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#if NET8_0_OR_GREATER

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// End-to-end integration tests for the Kestrel-TCP reverse-connect
    /// listener mode. Mirrors the existing
    /// <c>Opc.Ua.Sessions.Tests.ReverseConnectTest</c> pattern but
    /// hosts the client-side reverse-connect listener inside Kestrel
    /// via <see cref="KestrelTcpTransportListener"/> with
    /// <c>settings.ReverseConnectListener = true</c>.
    /// </summary>
    [TestFixture]
    [Category("KestrelTcp")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class KestrelTcpReverseConnectIntegrationTests
    {
        private const int kMaxTimeout = 30_000;

        private ITelemetryContext m_telemetry = null!;
        private ServerFixture<ReferenceServer> m_serverFixture = null!;
        private ClientFixture m_clientFixture = null!;
        private ReferenceServer m_server = null!;
        private string m_pkiRoot = null!;
        private Uri m_serverUrl = null!;
        private ITransportListenerFactory? m_originalOpcTcpFactory;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();

            // start ref server with reverse connect using the DEFAULT
            // (raw-socket) opc.tcp factory - the server is responsible for
            // initiating outbound reverse connections and the raw-socket
            // TcpTransportListener supports that natively.
            m_serverFixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AutoAccept = true,
                SecurityNone = true,
                ReverseConnectTimeout = kMaxTimeout,
                TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security
            };
            m_server = await m_serverFixture.StartAsync(m_pkiRoot).ConfigureAwait(false);

            // NOW swap the opc.tcp listener binding to the Kestrel-TCP
            // factory so the client-side reverse-connect listener that
            // ReverseConnectHost.CreateListener instantiates next picks
            // up the Kestrel implementation. The server's listener was
            // already constructed above; this swap does not affect it.
            m_originalOpcTcpFactory = TransportBindings.Listeners.GetBinding(Utils.UriSchemeOpcTcp, m_telemetry);
            TransportBindings.Listeners.SetBinding(new KestrelTcpTransportListenerFactory());

            // create client and start the reverse-connect host (uses the
            // Kestrel-TCP factory now installed in TransportBindings.Listeners).
            m_clientFixture = new ClientFixture(telemetry: m_telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            await m_clientFixture.StartReverseConnectHostAsync().ConfigureAwait(false);

            m_serverUrl = new Uri(
                Utils.ReplaceLocalhost(
                    "opc.tcp://localhost:" +
                    m_serverFixture.Port.ToString(CultureInfo.InvariantCulture)));

            // direct the reference server to reverse-connect into the
            // client-side Kestrel-TCP listener.
            m_server.AddReverseConnection(
                new Uri(m_clientFixture.ReverseConnectUri),
                kMaxTimeout);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            m_clientFixture?.Dispose();
            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }

            // restore the default opc.tcp factory so other fixtures use it.
            if (m_originalOpcTcpFactory != null)
            {
                TransportBindings.Listeners.SetBinding(m_originalOpcTcpFactory);
            }

            try
            {
                if (m_pkiRoot != null && Directory.Exists(m_pkiRoot))
                {
                    Directory.Delete(m_pkiRoot, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }

        [Test]
        public async Task GetEndpointsViaKestrelReverseConnectAsync()
        {
            ApplicationConfiguration config = m_clientFixture.Config;
            ITransportWaitingConnection connection;
            using (var cts = new CancellationTokenSource(kMaxTimeout))
            {
                connection = await m_clientFixture
                    .ReverseConnectManager.WaitForConnectionAsync(
                        m_serverUrl,
                        null,
                        cts.Token)
                    .ConfigureAwait(false);
                Assert.That(connection, Is.Not.Null,
                    "Failed to receive a reverse connection from the server via the Kestrel-TCP listener.");
            }

            using (var cts = new CancellationTokenSource(kMaxTimeout))
            {
                var endpointConfiguration = EndpointConfiguration.Create();
                endpointConfiguration.OperationTimeout = kMaxTimeout;
                using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                    config,
                    connection,
                    endpointConfiguration,
                    ct: cts.Token).ConfigureAwait(false);
                ArrayOf<EndpointDescription> endpoints = await client
                    .GetEndpointsAsync(default, cts.Token)
                    .ConfigureAwait(false);
                Assert.That(endpoints.Count, Is.GreaterThan(0));
                await client.CloseAsync(cts.Token).ConfigureAwait(false);
            }
        }
    }
}

#endif // NET8_0_OR_GREATER
