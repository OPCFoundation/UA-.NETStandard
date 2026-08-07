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

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.OpenUsd.Client;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// End-to-end validation of §5.14 cross-server composition: a supervisory
    /// <c>SiteCompositionServer</c> that owns no devices declares one cross-server
    /// component binding per subordinate, and a connector given a
    /// <see cref="OpenUsdConnectorOptions.RemoteSessionFactory"/> opens a session to
    /// each named server and drives its bindings into the same stage.
    /// </summary>
    /// <remarks>
    /// Two servers really run here — the site server and a pump server it points at —
    /// because the behaviour under test is what happens across a session boundary. The
    /// interesting cases are the failures: a subordinate that is down, and a connector
    /// that cannot be constructed for a session that was already opened. Both have to
    /// leave the rest of the scene standing, and the second must not leak the session.
    /// </remarks>
    [TestFixture]
    [Category("Pumps")]
    [Category("OpenUsd")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class SiteFederationE2eTests
    {
        private const string SitePrimPath = "/Site";
        private const string PumpHallPrimPath = "/Site/PumpHall";

        private ITelemetryContext m_telemetry = null!;
        private IHost? m_pumpHost;
        private IHost? m_siteHost;
        private ISession? m_siteSession;
        private ApplicationConfiguration m_clientConfig = null!;
        private string m_pumpServerUrl = null!;

        private static int GetFreeTcpPort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = DefaultTelemetry.Create(b => b.SetMinimumLevel(LogLevel.Warning));

            m_pumpServerUrl = $"opc.tcp://localhost:{GetFreeTcpPort()}/PumpDeviceIntegrationServer";
            string siteServerUrl = $"opc.tcp://localhost:{GetFreeTcpPort()}/SiteCompositionServer";

            m_pumpHost = BuildServerHost(
                "SiteFederationPumpServer",
                m_pumpServerUrl,
                builder => builder.AddNodeManager<global::Pumps.PumpNodeManagerFactory>());
            await m_pumpHost.StartAsync().ConfigureAwait(false);

            // The site server names the pump server but never proxies it: the endpoint
            // is all it publishes, and the connector talks to the owner directly.
            m_siteHost = BuildServerHost(
                "SiteFederationSiteServer",
                siteServerUrl,
                builder => builder.AddNodeManager<global::SiteComposition.SiteNodeManagerFactory>(),
                services => services.Configure<global::SiteComposition.SiteCompositionOptions>(
                    o => o.PumpServerEndpointUrl = m_pumpServerUrl));
            await m_siteHost.StartAsync().ConfigureAwait(false);

            await CreateClientConfigurationAsync().ConfigureAwait(false);
            m_siteSession = await ConnectAsync(siteServerUrl, "SiteFederationE2e").ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_siteSession != null)
            {
                await m_siteSession.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                await m_siteSession.DisposeAsync().ConfigureAwait(false);
                m_siteSession = null;
            }
            if (m_clientConfig?.CertificateManager is IDisposable manager)
            {
                manager.Dispose();
            }
            if (m_siteHost != null)
            {
                await m_siteHost.StopAsync().ConfigureAwait(false);
                m_siteHost.Dispose();
                m_siteHost = null;
            }
            if (m_pumpHost != null)
            {
                await m_pumpHost.StopAsync().ConfigureAwait(false);
                m_pumpHost.Dispose();
                m_pumpHost = null;
            }
        }

        [Test]
        public async Task FederationDrivesTheSubordinateServersMachinesIntoTheSiteStageAsync()
        {
            var sink = new MockUsdSink();
            int opened = 0;
            var options = new OpenUsdConnectorOptions
            {
                RemoteSessionFactory = (endpointUrl, ct) =>
                {
                    Interlocked.Increment(ref opened);
                    return ConnectAsync(endpointUrl, "SiteFederationRemote");
                }
            };
            var connector = new OpenUsdConnector(m_siteSession!, sink, options, m_telemetry);
            await connector.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(opened, Is.EqualTo(1),
                        "The connector must open one session per cross-server component.");
                    Assert.That(sink.WasPrimComposed(PumpHallPrimPath), Is.True,
                        "The cross-server placeholder prim was not composed.");
                    // The subordinate's own machines are composed by the remote
                    // connector into the same sink - that is what federation buys.
                    Assert.That(sink.WasPrimComposed("/Plant/Pumps/Pump_1"), Is.True,
                        "The subordinate server's machines were not federated into the stage.");
                });
            }
            finally
            {
                await connector.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task AnUnreachableSubordinateLeavesTheRestOfTheSceneStandingAsync()
        {
            // A subordinate is an independent process that can be down. Letting one
            // refuse a session abort StartAsync would take the whole stage down with it.
            var sink = new MockUsdSink();
            var options = new OpenUsdConnectorOptions
            {
                RemoteSessionFactory = (endpointUrl, ct) =>
                    throw new ServiceResultException(StatusCodes.BadNotConnected)
            };
            var connector = new OpenUsdConnector(m_siteSession!, sink, options, m_telemetry);

            await connector.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(sink.WasPrimComposed(PumpHallPrimPath), Is.True,
                        "The placeholder prim must still be composed when a subordinate is down.");
                    Assert.That(sink.WasPrimComposed("/Plant/Pumps/Pump_1"), Is.False,
                        "No machine can be federated from a server that refused the session.");
                });
            }
            finally
            {
                await connector.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task AFailedRemoteConnectorClosesTheSessionItAlreadyOpenedAsync()
        {
            // The session is owned by nobody between the factory returning it and the
            // remote connector being registered. If the constructor throws in that
            // window the channel and the server-side session would stay alive until the
            // server's session timeout expires, so the connector closes it explicitly.
            var sink = new MockUsdSink();
            var closed = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var broken = new Mock<ISession>();
            // The constructor reads NamespaceUris to resolve the OpenUSD namespace
            // index, so a session that has none fails construction - which is the
            // window this test exists to cover.
            broken.SetupGet(s => s.NamespaceUris).Returns((NamespaceTable)null!);
            broken
                .Setup(s => s.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken _) =>
                {
                    closed.TrySetResult(true);
                    return Task.FromResult<StatusCode>(StatusCodes.Good);
                });

            var options = new OpenUsdConnectorOptions
            {
                RemoteSessionFactory = (endpointUrl, ct) => Task.FromResult(broken.Object)
            };
            var connector = new OpenUsdConnector(m_siteSession!, sink, options, m_telemetry);

            await connector.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.That(
                    await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(10)))
                        .ConfigureAwait(false),
                    Is.SameAs(closed.Task),
                    "The remote session was not closed after the remote connector failed to construct.");
                Assert.That(sink.WasPrimComposed(PumpHallPrimPath), Is.True,
                    "The placeholder prim must still be composed.");
            }
            finally
            {
                await connector.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public void FederationPropagatesCancellationRatherThanSwallowingIt()
        {
            // The broad catch that isolates an unreachable subordinate must never
            // swallow a cancellation - a cancelled StartAsync has to surface as one.
            var sink = new MockUsdSink();
            using var cts = new CancellationTokenSource();
            var options = new OpenUsdConnectorOptions
            {
                RemoteSessionFactory = (endpointUrl, ct) =>
                {
                    cts.Cancel();
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult<ISession>(null!);
                }
            };
            var connector = new OpenUsdConnector(m_siteSession!, sink, options, m_telemetry);

            Assert.That(
                async () => await connector.StartAsync(cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task WithoutASessionFactoryTheSiteRendersItsShellOnlyAsync()
        {
            // Federation is opt-in: the endpoint the connector would dial comes from
            // the server being rendered rather than from the operator, which makes
            // honouring it a trust decision.
            var sink = new MockUsdSink();
            var connector = new OpenUsdConnector(m_siteSession!, sink);

            await connector.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(sink.WasPrimComposed(PumpHallPrimPath), Is.True,
                        "The site shell must render without federation.");
                    Assert.That(sink.WasPrimComposed("/Plant/Pumps/Pump_1"), Is.False,
                        "No subordinate machine may be composed without an explicit session factory.");
                });
            }
            finally
            {
                await connector.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TheSiteRepresentationDeclaresItsSubordinateEndpointAsync()
        {
            var connector = new OpenUsdConnector(m_siteSession!, new MockUsdSink());
            System.Collections.Generic.List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None)
                    .ConfigureAwait(false);

            OpenUsdConnector.RepresentationInfo? site = reps.Find(r => r.PrimPath == SitePrimPath);
            Assert.That(site, Is.Not.Null, "The site representation was not discovered.");
            OpenUsdConnector.ComponentInfo? pumpHall =
                site!.Components.Find(c => c.TargetPrimPath == PumpHallPrimPath);
            Assert.That(pumpHall, Is.Not.Null, "The PumpHall component binding was not discovered.");
            Assert.That(pumpHall!.ComponentEndpointUrl, Is.EqualTo(m_pumpServerUrl));
        }

        private static IHost BuildServerHost(
            string applicationName,
            string endpointUrl,
            Action<Opc.Ua.Server.Hosting.IOpcUaServerBuilder> configureServer,
            Action<IServiceCollection>? configureServices = null)
        {
            HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder();
            hostBuilder.Logging.ClearProviders();
            hostBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
            configureServices?.Invoke(hostBuilder.Services);
            configureServer(hostBuilder.Services
                .AddOpcUa()
                .AddServer(o =>
                {
                    o.ApplicationName = applicationName;
                    o.ApplicationUri = $"urn:localhost:OPCFoundation:{applicationName}";
                    o.AutoAcceptUntrustedCertificates = true;
                    o.EndpointUrls.Add(endpointUrl);
                    o.UserTokenPolicies.Add(new Opc.Ua.Server.Hosting.OpcUaUserTokenPolicy
                    {
                        TokenType = UserTokenType.Anonymous
                    });
                }));
            return hostBuilder.Build();
        }

        private async Task CreateClientConfigurationAsync()
        {
            string pkiRoot = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "SiteFederationE2e", System.IO.Path.GetRandomFileName());
            m_clientConfig = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "SiteFederationE2eClient",
                ApplicationUri = "urn:localhost:OPCFoundation:SiteFederationE2eClient",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = System.IO.Path.Combine(pkiRoot, "own"),
                        SubjectName = "CN=SiteFederationE2eClient, O=OPC Foundation"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = System.IO.Path.Combine(pkiRoot, "issuer")
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = System.IO.Path.Combine(pkiRoot, "trusted")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = System.IO.Path.Combine(pkiRoot, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = true
                },
                TransportQuotas = new TransportQuotas { MaxMessageSize = 4 * 1024 * 1024 },
                ClientConfiguration = new ClientConfiguration(),
                ServerConfiguration = new ServerConfiguration()
            };
            await m_clientConfig.ValidateAsync(ApplicationType.Client).ConfigureAwait(false);

            var appInstance = new Opc.Ua.Configuration.ApplicationInstance(m_clientConfig, m_telemetry);
            await appInstance.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false);

            m_clientConfig.CertificateManager ??= CertificateManagerFactory.Create(
                m_clientConfig.SecurityConfiguration, m_telemetry);
            m_clientConfig.CertificateManager.AcceptError = static (cert, err) => true;
        }

        private async Task<ISession> ConnectAsync(string serverUrl, string sessionName)
        {
            // The hosted endpoint opens asynchronously after the host starts.
            EndpointDescription? endpointDescription = null;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                try
                {
                    endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                        m_clientConfig, serverUrl, useSecurity: false, m_telemetry, CancellationToken.None)
                        .ConfigureAwait(false);
                    if (endpointDescription != null)
                    {
                        break;
                    }
                }
                catch (Exception)
                {
                    // not ready yet
                }
                await Task.Delay(500).ConfigureAwait(false);
            }
            Assert.That(endpointDescription, Is.Not.Null,
                $"Server endpoint {serverUrl} did not become available.");

            var endpoint = new ConfiguredEndpoint(
                null, endpointDescription!, EndpointConfiguration.Create(m_clientConfig));
            var sessionFactory = new DefaultSessionFactory(m_telemetry);
            return await sessionFactory.CreateAsync(
                m_clientConfig, endpoint, updateBeforeConnect: false,
                sessionName: sessionName, sessionTimeout: 60000,
                identity: new UserIdentity(new AnonymousIdentityToken()),
                preferredLocales: default, ct: CancellationToken.None).ConfigureAwait(false);
        }
    }
}
