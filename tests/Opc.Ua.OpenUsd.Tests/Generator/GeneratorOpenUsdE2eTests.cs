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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.OpenUsd.Client;

namespace Opc.Ua.OpenUsd.Tests.Generator
{
    /// <summary>
    /// Hosts the generator server and asks a real client what it can see.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This fixture exists because of a defect no other test could catch. The
    /// plant-level aggregation was created as a child of <c>DeviceSet</c> after that
    /// subtree had already been registered, so it existed only as a C# object
    /// hanging off a registered parent: every unit test that inspected the node
    /// objects passed, and the server answered reads on everything else, but no
    /// client could resolve or browse the aggregation. A connector therefore never
    /// composed any generator geometry, and the sample rendered live values on prims
    /// with nothing behind them - an empty powerhouse reporting 250 kW - for its
    /// entire existence.
    /// </para>
    /// <para>
    /// The only way to catch that class of defect is to ask a client, over the wire,
    /// what the address space actually exposes.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("Generators")]
    [NonParallelizable]
    public sealed class GeneratorOpenUsdE2eTests
    {
        private const int GeneratorCount = 2;

        private IHost? m_host;
        private ApplicationConfiguration? m_clientConfig;
        private ISession? m_session;
        private ITelemetryContext? m_telemetry;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = DefaultTelemetry.Create(b => b.SetMinimumLevel(LogLevel.Warning));

            int port = GetFreeTcpPort();
            string serverUrl = $"opc.tcp://localhost:{port}/GeneratorE2eServer";

            HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder();
            hostBuilder.Logging.ClearProviders();
            hostBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
            hostBuilder.Services.Configure<global::Generators.GeneratorDeviceIntegrationOptions>(
                options =>
                {
                    options.GeneratorCount = GeneratorCount;

                    // A deterministic plant: the fault rotation is for someone
                    // watching a viewport, and a set that trips mid-test would make
                    // the assertions below depend on timing.
                    options.InjectFaults = false;
                });
            hostBuilder.Services
                .AddOpcUa()
                .AddServer(o =>
                {
                    o.ApplicationName = "GeneratorE2eServer";
                    o.ApplicationUri = "urn:localhost:OPCFoundation:GeneratorE2eServer";
                    o.AutoAcceptUntrustedCertificates = true;
                    o.EndpointUrls.Add(serverUrl);
                })
                .ConfigureDevicesFor<global::Generators.GeneratorNodeManager>(_ =>
                {
                    // No topology work needed here; registering the hook is what puts
                    // the post-setup runner in the container, which is what lets the
                    // node-manager factory receive the configured options. Without it
                    // GeneratorCount is silently ignored and the set count below would
                    // not be what this fixture asked for.
                })
                .AddNodeManager<global::Generators.GeneratorNodeManagerFactory>();

            m_host = hostBuilder.Build();
            await m_host.StartAsync().ConfigureAwait(false);

            m_clientConfig = CreateClientConfiguration();
            await m_clientConfig.ValidateAsync(ApplicationType.Client).ConfigureAwait(false);

            var appInstance = new Opc.Ua.Configuration.ApplicationInstance(m_clientConfig, m_telemetry);
            await appInstance.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false);
            m_clientConfig.CertificateManager ??= CertificateManagerFactory.Create(
                m_clientConfig.SecurityConfiguration, m_telemetry);
            m_clientConfig.CertificateManager.AcceptError = static (cert, err) => true;

            EndpointDescription? endpointDescription = null;
            for (int attempt = 0; attempt < 40 && endpointDescription == null; attempt++)
            {
                try
                {
                    endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                        m_clientConfig, serverUrl, useSecurity: false, m_telemetry,
                        CancellationToken.None).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // The endpoint opens asynchronously after the host starts.
                catch (Exception)
#pragma warning restore CA1031
                {
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
            Assert.That(endpointDescription, Is.Not.Null, "Server endpoint did not become available.");

            var endpoint = new ConfiguredEndpoint(
                null, endpointDescription!, EndpointConfiguration.Create(m_clientConfig));
            var sessionFactory = new DefaultSessionFactory(m_telemetry);
            m_session = await sessionFactory.CreateAsync(
                m_clientConfig, endpoint, updateBeforeConnect: false,
                sessionName: "GeneratorE2e", sessionTimeout: 60000,
                identity: new UserIdentity(new AnonymousIdentityToken()),
                preferredLocales: default, ct: CancellationToken.None).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_session != null)
            {
                await m_session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                await m_session.DisposeAsync().ConfigureAwait(false);
                m_session = null;
            }
            if (m_clientConfig?.CertificateManager is IDisposable manager)
            {
                manager.Dispose();
            }
            if (m_host != null)
            {
                await m_host.StopAsync().ConfigureAwait(false);
                m_host.Dispose();
                m_host = null;
            }
        }

        /// <summary>
        /// A client discovers the plant aggregation, not just the per-set twins.
        /// </summary>
        /// <remarks>
        /// The aggregation is what carries the component binding that composes
        /// generator geometry. When it was unregistered, discovery returned only the
        /// per-set twins - which is why live values arrived while nothing rendered.
        /// </remarks>
        [Test]
        public async Task AClientDiscoversThePlantAggregationAsync()
        {
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink(), enableCommands: false);
            await using (connector.ConfigureAwait(false))
            {
                List<OpenUsdConnector.RepresentationInfo> reps = await connector
                    .DiscoverAllRepresentationsAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                OpenUsdConnector.RepresentationInfo? aggregation = reps.Find(
                    r => string.Equals(r.PrimPath, "/Powerhouse/Generators", StringComparison.Ordinal));

                Assert.That(
                    aggregation,
                    Is.Not.Null,
                    "No client-visible representation at /Powerhouse/Generators. The plant " +
                    "aggregation exists as a node object but was never registered, so nothing " +
                    "composes generator geometry.");
                Assert.That(
                    aggregation!.Components,
                    Is.Not.Empty,
                    "The aggregation carries no component binding, so it composes nothing.");
            }
        }

        /// <summary>
        /// The aggregation binding names the asset a connector has to reference.
        /// </summary>
        [Test]
        public async Task TheAggregationNamesTheGeneratorAssetAsync()
        {
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink(), enableCommands: false);
            await using (connector.ConfigureAwait(false))
            {
                List<OpenUsdConnector.RepresentationInfo> reps = await connector
                    .DiscoverAllRepresentationsAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                OpenUsdConnector.RepresentationInfo? aggregation = reps.Find(
                    r => string.Equals(r.PrimPath, "/Powerhouse/Generators", StringComparison.Ordinal));
                Assert.That(aggregation, Is.Not.Null);

                OpenUsdConnector.ComponentInfo binding = aggregation!.Components[0];

                Assert.Multiple(() =>
                {
                    Assert.That(binding.Enabled, Is.True);
                    Assert.That(
                        binding.ComponentAssetReference,
                        Does.Contain("generator.usda"),
                        "The binding must name the asset the composed prims reference.");
                    Assert.That(
                        binding.TargetPrimPath,
                        Is.EqualTo("/Powerhouse/Generators"));
                });
            }
        }

        /// <summary>
        /// Every configured set is discoverable in its own right.
        /// </summary>
        /// <remarks>
        /// Guards the sibling failure: registering the aggregation but losing the
        /// per-set twins would render geometry that never moves.
        /// </remarks>
        [Test]
        public async Task EverySetIsDiscoverableAsync()
        {
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink(), enableCommands: false);
            await using (connector.ConfigureAwait(false))
            {
                List<OpenUsdConnector.RepresentationInfo> reps = await connector
                    .DiscoverAllRepresentationsAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                var setPaths = new List<string>();
                foreach (OpenUsdConnector.RepresentationInfo rep in reps)
                {
                    if (rep.PrimPath != null &&
                        rep.PrimPath.StartsWith("/Powerhouse/Generators/", StringComparison.Ordinal))
                    {
                        setPaths.Add(rep.PrimPath);
                    }
                }

                Assert.That(
                    setPaths,
                    Has.Count.EqualTo(GeneratorCount),
                    "Each configured set publishes its own twin representation.");
            }
        }

        [Test]
        public async Task AllRepresentationsAreMountedAsAddInsAsync()
        {
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink(), enableCommands: false);
            await using (connector.ConfigureAwait(false))
            {
                List<OpenUsdConnector.RepresentationInfo> representations = await connector
                    .DiscoverAllRepresentationsAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                await OpenUsdRepresentationMountAssert.AllAreAddInsAsync(
                    m_session!,
                    representations,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        private ApplicationConfiguration CreateClientConfiguration()
        {
            string pkiRoot = Path.Combine(
                Path.GetTempPath(), "GeneratorOpenUsdE2e", Path.GetRandomFileName());
            return new ApplicationConfiguration(m_telemetry!)
            {
                ApplicationName = "GeneratorE2eClient",
                ApplicationUri = "urn:localhost:OPCFoundation:GeneratorE2eClient",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "own"),
                        SubjectName = "CN=GeneratorE2eClient, O=OPC Foundation"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "issuer")
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "trusted")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = true
                },
                TransportQuotas = new TransportQuotas { MaxMessageSize = 4 * 1024 * 1024 },
                ClientConfiguration = new ClientConfiguration(),
                ServerConfiguration = new ServerConfiguration()
            };
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
