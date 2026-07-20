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
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.OpenUsd.Client;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// End-to-end validation of the draft OPC UA — OpenUSD Bindings companion
    /// specification against the PumpDeviceIntegrationServer. Starts the pump
    /// server via the generic host, connects a real client session, discovers the
    /// OpenUsdRepresentation AddIn + live bindings on Pump #1, subscribes to the
    /// bound source Variables, and drives an <see cref="OpenUsdConnector"/> that
    /// converts values and writes them into a <see cref="MockUsdSink"/> — the
    /// CI-friendly stand-in for a USD/Omniverse sink.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    [Category("OpenUsd")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class PumpOpenUsdE2eTests
    {
        private ITelemetryContext m_telemetry = null!;
        private IHost? m_host;
        private ISession? m_session;
        private ApplicationConfiguration m_clientConfig = null!;

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

            int port = GetFreeTcpPort();
            string serverUrl = $"opc.tcp://localhost:{port}/PumpDeviceIntegrationServer";

            HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder();
            hostBuilder.Logging.ClearProviders();
            hostBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
            hostBuilder.Services
                .AddOpcUa()
                .AddServer(o =>
                {
                    o.ApplicationName = "PumpOpenUsdE2eServer";
                    o.ApplicationUri = "urn:localhost:OPCFoundation:PumpOpenUsdE2eServer";
                    o.AutoAcceptUntrustedCertificates = true;
                    o.EndpointUrls.Add(serverUrl);
                })
                .AddNodeManager<global::Pumps.PumpNodeManagerFactory>();
            m_host = hostBuilder.Build();
            await m_host.StartAsync().ConfigureAwait(false);

            string pkiRoot = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "PumpOpenUsdE2e", System.IO.Path.GetRandomFileName());
            m_clientConfig = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "PumpOpenUsdE2eClient",
                ApplicationUri = "urn:localhost:OPCFoundation:PumpOpenUsdE2eClient",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = System.IO.Path.Combine(pkiRoot, "own"),
                        SubjectName = "CN=PumpOpenUsdE2eClient, O=OPC Foundation"
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
            Assert.That(endpointDescription, Is.Not.Null, "Server endpoint did not become available.");

            var endpoint = new ConfiguredEndpoint(
                null, endpointDescription!, EndpointConfiguration.Create(m_clientConfig));
            var sessionFactory = new DefaultSessionFactory(m_telemetry);
            m_session = await sessionFactory.CreateAsync(
                m_clientConfig, endpoint, updateBeforeConnect: false,
                sessionName: "PumpOpenUsdE2e", sessionTimeout: 60000,
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

        [Test]
        public void OpenUsdCompanionModelIsDeployedAndServed()
        {
            // The running server advertises the OpenUSD namespace ...
            int ns = m_session!.NamespaceUris.GetIndex("http://opcfoundation.org/UA/OpenUSD/");
            Assert.That(ns, Is.GreaterThan(0), "OpenUSD namespace not advertised by the server.");

            // ... and serves the companion type nodes (proves the NodeSet loaded).
            var repType = new NodeId(1003u, (ushort)ns);
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink());
            string bn = connector.ReadBrowseNameAsync(repType, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.That(bn, Is.EqualTo("OpenUsdRepresentationType"));
        }

        [Test]
        public async Task OpenUsdFacilityIsBrowsableFromServerObjectAsync()
        {
            // F1 regression: the well-known OpenUSD facility must be a browsable
            // component of the Server Object (i=2253), so a spec-conformant connector
            // can Browse Server -> OpenUSD -> Representations without hard-coding NodeIds.
            var browseServer = new BrowseDescription
            {
                NodeId = Opc.Ua.ObjectIds.Server,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };
            BrowseResponse serverChildren = await m_session!.BrowseAsync(
                null!, null!, 0, new BrowseDescription[] { browseServer }, CancellationToken.None)
                .ConfigureAwait(false);

            ReferenceDescription? openUsd = null;
            ArrayOf<ReferenceDescription> refs = serverChildren.Results[0].References;
            for (int i = 0; i < refs.Count; i++)
            {
                if (refs[i].BrowseName.Name == "OpenUSD")
                {
                    openUsd = refs[i];
                    break;
                }
            }
            Assert.That(openUsd, Is.Not.Null, "OpenUSD facility is not browsable from the Server Object.");

            // ... and Representations is reachable one hop below it.
            NodeId openUsdId = ExpandedNodeId.ToNodeId(openUsd!.NodeId, m_session!.NamespaceUris);
            var browseRoot = new BrowseDescription
            {
                NodeId = openUsdId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };
            BrowseResponse rootChildren = await m_session!.BrowseAsync(
                null!, null!, 0, new BrowseDescription[] { browseRoot }, CancellationToken.None)
                .ConfigureAwait(false);
            bool hasRepresentations = false;
            ArrayOf<ReferenceDescription> rootRefs = rootChildren.Results[0].References;
            for (int i = 0; i < rootRefs.Count; i++)
            {
                if (rootRefs[i].BrowseName.Name == "Representations")
                {
                    hasRepresentations = true;
                    break;
                }
            }
            Assert.That(hasRepresentations, Is.True,
                "Representations registry is not reachable from Server/OpenUSD.");
        }

        [Test]
        public async Task RepresentationAndBindingsAreDiscoverableAsync()
        {
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink());
            OpenUsdConnector.RepresentationInfo? rep = await PumpRepAsync(connector).ConfigureAwait(false);

            Assert.That(rep, Is.Not.Null, "OpenUsdRepresentation not discovered on Pump #1.");
            Assert.That(rep!.PrimPath, Is.EqualTo("/Plant/Pumps/P101"));
            Assert.That(rep.StageNodeId, Is.Not.Null);
            Assert.That(rep.RootLayerIdentifier, Is.EqualTo("asset-repo/Plant.usd"));
            // 0.1 telemetry (3) + 0.2 alarm (1) + 0.2 command (1) = 5 bindings.
            Assert.That(rep.Bindings, Has.Count.EqualTo(5));
        }

        [Test]
        public async Task SemanticIdAndSignalRoleAreSurfacedAsync()
        {
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink());
            OpenUsdConnector.RepresentationInfo? rep = await PumpRepAsync(connector).ConfigureAwait(false);
            Assert.That(rep, Is.Not.Null);

            OpenUsdConnector.BindingInfo? massFlow = null;
            OpenUsdConnector.BindingInfo? command = null;
            foreach (OpenUsdConnector.BindingInfo b in rep!.Bindings)
            {
                if (b.SourceSemanticId == "0173-1#02-AAO677#002")
                {
                    massFlow = b;
                }
                if (b.Intent == OpenUsdIntentProfile.UsdToUaCommand)
                {
                    command = b;
                }
            }

            Assert.Multiple(() =>
            {
                // Semantic-ID source: the flow binding carries a portable IRDI.
                Assert.That(massFlow, Is.Not.Null, "MassFlow binding has no SourceSemanticId.");
                Assert.That(massFlow!.SignalRole, Is.EqualTo(OpenUsdSignalRole.Observable));
                // Controllable/command: a UsdToUaCommand binding is declared and marked Controllable.
                Assert.That(command, Is.Not.Null, "No UsdToUaCommand binding discovered.");
                Assert.That(command!.SignalRole, Is.EqualTo(OpenUsdSignalRole.Controllable));
                Assert.That(command.CommandTargetNodeId, Is.Not.Null);
            });
        }

        [Test]
        public async Task StageRootLayerDigestVerifiesAsync()
        {
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink());
            OpenUsdConnector.RepresentationInfo? rep = await PumpRepAsync(connector).ConfigureAwait(false);
            Assert.That(rep, Is.Not.Null);

            Assert.Multiple(() =>
            {
                // Twin-BOM integrity: the stage advertises a content digest ...
                Assert.That(rep!.DigestAlgorithm, Is.EqualTo(OpenUsdDigestAlgorithm.Sha256));
                Assert.That(rep.RootLayerDigest, Is.Not.Null.And.Length.EqualTo(32));
                // ... and it verifies against the resolved root-layer identity.
                Assert.That(connector.VerifyStageDigest(rep!), Is.True,
                    "RootLayerDigest failed verification.");
            });
        }

        [Test]
        public async Task AlarmBindingDrivesUsdVisibilityAsync()
        {
            var sink = new MockUsdSink();
            var connector = new OpenUsdConnector(m_session!, sink);
            await connector.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.Delay(2000, CancellationToken.None).ConfigureAwait(false);
            await connector.StopAsync().ConfigureAwait(false);

            // The UaAlarmToUsd binding subscribes the alarm-active aspect and authors
            // the status-light visibility token (initially "invisible" until an alarm).
            Assert.That(sink.WasWritten("/Plant/Pumps/P101/StatusLight", "visibility"), Is.True,
                "Alarm binding did not author StatusLight visibility.");
        }

        [Test]
        public void CommandBindingIsFailClosedByDefault()
        {
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink());
            // Opt-in: with commands disabled (the default), actuation is refused.
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await connector.IssueCommandAsync(10.0, CancellationToken.None).ConfigureAwait(false));
        }

        [Test]
        public async Task CommandBindingWritesServerVariableWhenEnabledAsync()
        {
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink(), enableCommands: true);
            OpenUsdConnector.RepresentationInfo? rep = await PumpRepAsync(connector).ConfigureAwait(false);
            NodeId? target = null;
            foreach (OpenUsdConnector.BindingInfo b in rep!.Bindings)
            {
                if (b.Intent == OpenUsdIntentProfile.UsdToUaCommand)
                {
                    target = b.CommandTargetNodeId;
                }
            }
            Assert.That(target, Is.Not.Null, "Command target NodeId missing.");

            const double setpoint = 42.5;
            bool ok = await connector.IssueCommandAsync(setpoint, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ok, Is.True, "Command write did not succeed.");

            var toRead = new ReadValueId[]
            {
                new ReadValueId { NodeId = target!.Value, AttributeId = Attributes.Value }
            };
            ReadResponse rr = await m_session!.ReadAsync(
                null!, 0, TimestampsToReturn.Neither, toRead, CancellationToken.None)
                .ConfigureAwait(false);
            double actual = System.Convert.ToDouble(
                rr.Results[0].WrappedValue.AsBoxedObject(),
                System.Globalization.CultureInfo.InvariantCulture);
            Assert.That(actual, Is.EqualTo(setpoint).Within(1e-9),
                "Server SpeedSetpoint was not updated by the command binding.");
        }

        [Test]
        public async Task HistoryReplayDegradesGracefullyOnNonHistorizingSourceAsync()
        {
            // The demo pump does not historize, so history replay finds no
            // UaHistoryToUsd binding and returns 0 without throwing. This validates
            // the connector's Part 11 code path and documents the requirement that a
            // history binding needs a historizing source (spec finding).
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink());
            int authored = await connector.ReplayHistoryAsync(
                DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(authored, Is.Zero);
        }

        [Test]
        public async Task LiveValuesFlowThroughConnectorToUsdSinkAsync()
        {
            var sink = new MockUsdSink();
            var connector = new OpenUsdConnector(m_session!, sink);

            await connector.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.Delay(4000, CancellationToken.None).ConfigureAwait(false);
            await connector.StopAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(sink.WasWritten("/Plant/Pumps/P101/Impeller", "xformOp:rotateZ"), Is.True,
                    "Rotation binding produced no value.");
                Assert.That(sink.WasWritten("/Plant/Pumps/P101/Body", "primvars:displayColor"), Is.True,
                    "DisplayColor binding produced no value.");
                Assert.That(sink.WasWritten("/Plant/Pumps/P101/StatusLight/Mat/Surface", "inputs:emissiveColor"), Is.True,
                    "EmissiveColor binding produced no value.");
                Assert.That(sink.TotalWrites, Is.GreaterThan(0));
            });
        }

        [Test]
        public async Task PumpComponentsComposeChildPrimsAsync()
        {
            // 1:1 composition (§5.12): the pump is composed of an Impeller and a Bearing,
            // each mapped to a child prim (arc=Child).
            var sink = new MockUsdSink();
            var connector = new OpenUsdConnector(m_session!, sink);
            await connector.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(sink.WasPrimComposed("/Plant/Pumps/P101/Impeller"), Is.True,
                        "Impeller component prim not composed.");
                    Assert.That(sink.WasPrimComposed("/Plant/Pumps/P101/Bearing"), Is.True,
                        "Bearing component prim not composed.");
                });
            }
            finally
            {
                await connector.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ProductionLineAggregatesPumpsAsync()
        {
            // 1..n composition (§5.12): a ProductionLine aggregates its pumps as
            // instanceable reference prims under a Pumps scope.
            var sink = new MockUsdSink();
            var connector = new OpenUsdConnector(m_session!, sink);
            await connector.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(sink.WasPrimComposed("/Plant/Line1/Pumps/P_201"), Is.True,
                        "Aggregated pump P-201 prim not composed.");
                    Assert.That(sink.WasPrimComposed("/Plant/Line1/Pumps/P_202"), Is.True,
                        "Aggregated pump P-202 prim not composed.");
                    Assert.That(sink.IsPrimActive("/Plant/Line1/Pumps/P_201"), Is.True);
                });
            }
            finally
            {
                await connector.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task CrossServerComponentIsComposedAsync()
        {
            // Cross-server composition (§5.14): the Line declares a component on another
            // server; the connector composes its reference prim (federation drives its
            // bindings only when a remote session factory is supplied — not here).
            var sink = new MockUsdSink();
            var connector = new OpenUsdConnector(m_session!, sink);
            await connector.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.That(sink.WasPrimComposed("/Plant/Line1/RemotePump"), Is.True,
                    "Cross-server component prim not composed.");
            }
            finally
            {
                await connector.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Ignore("Blocked by OPCFoundation/UA-.NETStandard#4061: a runtime-added child is "
            + "intermittently missing from a client Browse after CreateNodeAsync/AddNodeAsync + "
            + "GeneralModelChange, so the connector does not reliably observe the dynamically added "
            + "pump. Server-side state is correct; the browse is intermittently served stale. "
            + "Re-enable once the upstream browse-consistency issue is fixed.")]
        public async Task DynamicPumpIsComposedThenDeactivatedAsync()
        {
            // Dynamic composition (§5.13): the server periodically adds then removes a
            // pump, emitting model-change events; the connector reconciles the prim
            // (composed active on add, active=false on remove).
            var sink = new MockUsdSink();
            var connector = new OpenUsdConnector(m_session!, sink);
            await connector.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                const string dyn = "/Plant/Line1/Pumps/P_203";
                bool appeared = await PollAsync(
                    () => sink.WasPrimComposed(dyn) && sink.IsPrimActive(dyn), TimeSpan.FromSeconds(20))
                    .ConfigureAwait(false);
                Assert.That(appeared, Is.True, "Dynamically added pump prim was not composed.");

                bool deactivated = await PollAsync(
                    () => sink.WasPrimComposed(dyn) && !sink.IsPrimActive(dyn), TimeSpan.FromSeconds(20))
                    .ConfigureAwait(false);
                Assert.That(deactivated, Is.True, "Dynamically removed pump prim was not deactivated.");
            }
            finally
            {
                await connector.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ComponentBindingsAreDiscoverableAsync()
        {
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink());
            System.Collections.Generic.List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None).ConfigureAwait(false);

            OpenUsdConnector.RepresentationInfo? pump = reps.Find(r => r.PrimPath == "/Plant/Pumps/P101");
            OpenUsdConnector.RepresentationInfo? line = reps.Find(r => r.PrimPath == "/Plant/Line1");

            Assert.Multiple(() =>
            {
                Assert.That(reps, Has.Count.GreaterThanOrEqualTo(2), "Expected pump + line representations.");
                Assert.That(pump, Is.Not.Null);
                Assert.That(pump!.Components, Has.Count.EqualTo(2), "Pump should have 2 (1:1) component bindings.");
                Assert.That(line, Is.Not.Null);
                // Line: a Many aggregation + a cross-server component.
                Assert.That(line!.Components.Exists(c => c.Cardinality == OpenUsdCardinality.Many), Is.True);
                Assert.That(line.Components.Exists(c => !string.IsNullOrEmpty(c.ComponentEndpointUrl)), Is.True);
            });
        }

        private static async Task<OpenUsdConnector.RepresentationInfo?> PumpRepAsync(OpenUsdConnector connector)
        {
            System.Collections.Generic.List<OpenUsdConnector.RepresentationInfo> all =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None).ConfigureAwait(false);
            return all.Find(r => r.PrimPath == "/Plant/Pumps/P101");
        }

        private static async Task<bool> PollAsync(Func<bool> condition, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }
                await Task.Delay(250).ConfigureAwait(false);
            }
            return condition();
        }

        [Test]
        public async Task ServedAssetsAreFetchedVerifiedAndCachedAsync()
        {
            // §5.15 asset content delivery: the server serves its USD layer closure
            // (Plant.usda RootLayer + pump.usda + remote-pump.usda) via Part 5 FileType;
            // the connector streams, verifies each digest, and caches them locally.
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink());
            string cacheDir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "PumpAssetCache", System.IO.Path.GetRandomFileName());
            try
            {
                System.Collections.Generic.List<OpenUsdConnector.FetchedAsset> assets =
                    await connector.FetchServedAssetsAsync(cacheDir, CancellationToken.None).ConfigureAwait(false);

                OpenUsdConnector.FetchedAsset? root = assets.Find(a => a.Kind == OpenUsdAssetKind.RootLayer);
                Assert.Multiple(() =>
                {
                    Assert.That(assets, Has.Count.EqualTo(3), "Expected 3 served layers.");
                    Assert.That(assets.Exists(a => a.Identifier == "Plant.usda"
                        && a.Kind == OpenUsdAssetKind.RootLayer), Is.True, "Plant.usda RootLayer not served.");
                    Assert.That(assets.Exists(a => a.Identifier == "pump.usda"), Is.True, "pump.usda not served.");
                    Assert.That(assets.Exists(a => a.Identifier == "remote-pump.usda"), Is.True,
                        "remote-pump.usda not served.");
                    Assert.That(assets.TrueForAll(a => a.DigestVerified), Is.True,
                        "A served layer failed digest verification.");
                    Assert.That(assets.TrueForAll(a =>
                        System.IO.File.Exists(a.LocalPath) && new System.IO.FileInfo(a.LocalPath).Length > 0),
                        Is.True, "A served layer was not cached to disk.");
                    Assert.That(root, Is.Not.Null, "No RootLayer served.");
                    Assert.That(System.IO.File.ReadAllText(root!.LocalPath).TrimStart(),
                        Does.StartWith("#usda"), "Cached root layer is not valid USD text.");
                });
            }
            finally
            {
                if (System.IO.Directory.Exists(cacheDir))
                {
                    System.IO.Directory.Delete(cacheDir, recursive: true);
                }
            }
        }
    }
}
