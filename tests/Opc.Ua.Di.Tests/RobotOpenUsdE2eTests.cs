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
    /// specification against the MinimalRobotServer. Starts the robotics
    /// server via the generic host, connects a real client session, discovers the 15
    /// OpenUsdRepresentation AddIns (a MotionDeviceSystem "RobotCell" of two 6-axis
    /// robots composed recursively of their axes), subscribes to the bound Axis
    /// positions, and drives the SAME generic <see cref="OpenUsdConnector"/> used by
    /// the pump sample into a <see cref="MockUsdSink"/> — proving the connector needs
    /// no robot-specific code. Asserts per-axis articulation, nested composition
    /// (system → devices → axes), 1..n robot aggregation, an emergency-stop safety
    /// visual, an opt-in speed-override command, and dynamic tool composition.
    /// </summary>
    [TestFixture]
    [Category("Robotics")]
    [Category("OpenUsd")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class RobotOpenUsdE2eTests
    {
        private ITelemetryContext m_telemetry = null!;
        private IHost? m_host;
        private ISession? m_session;
        private ApplicationConfiguration m_clientConfig = null!;

        private const string CellPrim = "/Cell";
        private const string R1Prim = "/Cell/Robots/R1";
        private const string R2Prim = "/Cell/Robots/R2";
        private const string ToolPrim = "/Cell/Robots/R1/Base/J1/J2/J3/J4/J5/J6/Flange/Tool";

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
            string serverUrl = $"opc.tcp://localhost:{port}/MinimalRobotServer";

            HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder();
            hostBuilder.Logging.ClearProviders();
            hostBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
            hostBuilder.Services
                .AddOpcUa()
                .AddServer(o =>
                {
                    o.ApplicationName = "RobotOpenUsdE2eServer";
                    o.ApplicationUri = "urn:localhost:OPCFoundation:RobotOpenUsdE2eServer";
                    o.AutoAcceptUntrustedCertificates = true;
                    o.EndpointUrls.Add(serverUrl);
                })
                .AddNodeManager<global::Robotics.RoboticsNodeManagerFactory>();
            m_host = hostBuilder.Build();
            await m_host.StartAsync().ConfigureAwait(false);

            string pkiRoot = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "RobotOpenUsdE2e", System.IO.Path.GetRandomFileName());
            m_clientConfig = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "RobotOpenUsdE2eClient",
                ApplicationUri = "urn:localhost:OPCFoundation:RobotOpenUsdE2eClient",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = System.IO.Path.Combine(pkiRoot, "own"),
                        SubjectName = "CN=RobotOpenUsdE2eClient, O=OPC Foundation"
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

            EndpointDescription? endpointDescription = null;
            for (int attempt = 0; attempt < 90; attempt++)
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
                sessionName: "RobotOpenUsdE2e", sessionTimeout: 60000,
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

        private static async Task<bool> PollAsync(Func<bool> condition, TimeSpan timeout)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                if (condition())
                {
                    return true;
                }
                await Task.Delay(250).ConfigureAwait(false);
            }
            return condition();
        }

        private Task<List<OpenUsdConnector.RepresentationInfo>> AllRepsAsync(OpenUsdConnector connector)
            => connector.DiscoverAllRepresentationsAsync(CancellationToken.None);

        [Test]
        public void OpenUsdCompanionModelIsDeployedAndServed()
        {
            // The running server advertises the Robotics + OpenUSD namespaces (proving
            // the runtime-imported Robotics NodeSet and the source-generated OpenUSD
            // model both loaded) and serves the OpenUSD representation type node.
            int usdNs = m_session!.NamespaceUris.GetIndex("http://opcfoundation.org/UA/OpenUSD/");
            int roboNs = m_session!.NamespaceUris.GetIndex("http://opcfoundation.org/UA/Robotics/");
            Assert.Multiple(() =>
            {
                Assert.That(usdNs, Is.GreaterThan(0), "OpenUSD namespace not advertised.");
                Assert.That(roboNs, Is.GreaterThan(0), "Robotics namespace not advertised.");
            });

            var connector = new OpenUsdConnector(m_session!, new MockUsdSink());
            string bn = connector.ReadBrowseNameAsync(new NodeId(1003u, (ushort)usdNs), CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.That(bn, Is.EqualTo("OpenUsdRepresentationType"));
        }

        [Test]
        public async Task AllRepresentationsAreDiscoverableAsync()
        {
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink());
            List<OpenUsdConnector.RepresentationInfo> reps = await AllRepsAsync(connector).ConfigureAwait(false);

            OpenUsdConnector.RepresentationInfo? cell = reps.Find(r => r.PrimPath == CellPrim);
            OpenUsdConnector.RepresentationInfo? r1 = reps.Find(r => r.PrimPath == R1Prim);
            int axisReps = reps.FindAll(r => r.PrimPath != null && r.PrimPath.Contains("/Base/J", StringComparison.Ordinal)).Count;

            Assert.Multiple(() =>
            {
                // 1 system + 2 robots + 12 axes = 15 representations in the registry.
                Assert.That(reps, Has.Count.GreaterThanOrEqualTo(15), "Expected >= 15 representations.");
                Assert.That(cell, Is.Not.Null, "System (RobotCell) representation not discovered.");
                Assert.That(cell!.RootLayerIdentifier, Is.EqualTo("asset-repo/Cell.usd"));
                Assert.That(r1, Is.Not.Null, "Robot R1 representation not discovered.");
                Assert.That(axisReps, Is.EqualTo(12), "Expected 12 axis representations (6 per robot).");
                // The system carries the RobotsAggregation (Many) component binding.
                Assert.That(cell.Components.Exists(c => c.Cardinality == OpenUsdCardinality.Many), Is.True,
                    "System representation is missing the Many RobotsAggregation.");
                // Each robot carries the AxesAggregation (Many) component binding (nested).
                Assert.That(r1!.Components.Exists(c => c.Cardinality == OpenUsdCardinality.Many), Is.True,
                    "Robot representation is missing the Many AxesAggregation (nested composition).");
            });
        }

        [Test]
        public async Task AxisPositionsArticulateJointsAsync()
        {
            var sink = new MockUsdSink();
            var connector = new OpenUsdConnector(m_session!, sink);
            await connector.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.Delay(3000, CancellationToken.None).ConfigureAwait(false);
            await connector.StopAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                // Each Axis' ActualPosition drives the rotate op of its (nested) link
                // prim — the live kinematic articulation, on both robots.
                Assert.That(sink.WasWritten("/Cell/Robots/R1/Base/J1", "xformOp:rotateZ"), Is.True,
                    "R1 axis A1 did not articulate.");
                Assert.That(sink.WasWritten("/Cell/Robots/R1/Base/J1/J2/J3", "xformOp:rotateY"), Is.True,
                    "R1 axis A3 did not articulate.");
                Assert.That(sink.WasWritten("/Cell/Robots/R2/Base/J1", "xformOp:rotateZ"), Is.True,
                    "R2 axis A1 did not articulate.");
                Assert.That(sink.TotalWrites, Is.GreaterThan(0));
            });
        }

        [Test]
        public async Task RobotsAggregateAsReferencePrimsAsync()
        {
            // 1..n composition (§5.12): the cell aggregates its robots as reference prims
            // (Reference, not Instance, so each articulates independently).
            var sink = new MockUsdSink();
            var connector = new OpenUsdConnector(m_session!, sink);
            await connector.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                bool composed = await PollAsync(
                    () => sink.WasPrimComposed(R1Prim) && sink.WasPrimComposed(R2Prim),
                    TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(composed, Is.True, "Both robot prims should be composed.");
                    Assert.That(sink.IsPrimActive(R1Prim), Is.True, "R1 prim not active.");
                    Assert.That(sink.IsPrimActive(R2Prim), Is.True, "R2 prim not active.");
                });
            }
            finally
            {
                await connector.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task AxesComposeAsNestedChildMarkersAsync()
        {
            // Nested composition (§5.12): each robot is composed of its 6 axes (Many,
            // Child) — the connector composes an axis component prim per axis under the
            // robot scope.
            var sink = new MockUsdSink();
            var connector = new OpenUsdConnector(m_session!, sink);
            await connector.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                bool composed = await PollAsync(
                    () => sink.WasPrimComposed("/Cell/Robots/R1/A1") && sink.WasPrimComposed("/Cell/Robots/R1/A6"),
                    TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(composed, Is.True,
                    "Axis component prims (nested Many composition) were not composed.");
            }
            finally
            {
                await connector.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task EmergencyStopDrivesSafetyVisualsAsync()
        {
            var sink = new MockUsdSink();
            var connector = new OpenUsdConnector(m_session!, sink);
            await connector.StartAsync(CancellationToken.None).ConfigureAwait(false);
            // The emergency-stop pulses periodically; wait long enough to observe at
            // least one visibility authoring on the beacon and a robot warning halo.
            bool authored = await PollAsync(
                () => sink.WasWritten("/Cell/SafetyBeacon", "visibility")
                   && sink.WasWritten("/Cell/Robots/R1/Warning", "visibility"),
                TimeSpan.FromSeconds(35)).ConfigureAwait(false);
            await connector.StopAsync().ConfigureAwait(false);

            Assert.That(authored, Is.True,
                "Emergency-stop alarm bindings did not author the safety beacon + robot warning visibility.");
        }

        [Test]
        public async Task DynamicToolIsComposedAsync()
        {
            // Dynamic composition (§5.13): the server attaches a gripper tool on R1's
            // flange at runtime (emitting model-change events); the connector reconciles
            // the tool reference prim.
            var sink = new MockUsdSink();
            var connector = new OpenUsdConnector(m_session!, sink);
            await connector.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                bool appeared = await PollAsync(
                    () => sink.WasPrimComposed(ToolPrim) && sink.IsPrimActive(ToolPrim),
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Assert.That(appeared, Is.True, "Dynamically attached gripper tool prim was not composed.");
            }
            finally
            {
                await connector.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public void SpeedOverrideCommandIsFailClosedByDefault()
        {
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink());
            // Opt-in: with commands disabled (the default), actuation is refused.
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await connector.IssueCommandAsync(50.0, CancellationToken.None).ConfigureAwait(false));
        }

        [Test]
        public async Task SpeedOverrideCommandWritesServerVariableWhenEnabledAsync()
        {
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink(), enableCommands: true);
            List<OpenUsdConnector.RepresentationInfo> reps = await AllRepsAsync(connector).ConfigureAwait(false);
            NodeId? target = null;
            foreach (OpenUsdConnector.RepresentationInfo rep in reps)
            {
                foreach (OpenUsdConnector.BindingInfo b in rep.Bindings)
                {
                    if (b.Intent == OpenUsdIntentProfile.UsdToUaCommand)
                    {
                        target = b.CommandTargetNodeId;
                    }
                }
            }
            Assert.That(target, Is.Not.Null, "Command target NodeId missing.");

            const double setpoint = 63.5;
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
                "Server SpeedOverride was not updated by the command binding.");
        }

        [Test]
        public async Task StageRootLayerDigestVerifiesAsync()
        {
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink());
            List<OpenUsdConnector.RepresentationInfo> reps = await AllRepsAsync(connector).ConfigureAwait(false);
            OpenUsdConnector.RepresentationInfo? cell = reps.Find(r => r.PrimPath == CellPrim);
            Assert.That(cell, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(cell!.DigestAlgorithm, Is.EqualTo(OpenUsdDigestAlgorithm.Sha256));
                Assert.That(cell!.RootLayerDigest.IsNull, Is.False);
                Assert.That(cell!.RootLayerDigest.Length, Is.EqualTo(32));
                Assert.That(connector.VerifyStageDigest(cell!), Is.True,
                    "RootLayerDigest failed verification.");
            });
        }

        [Test]
        public async Task ServedAssetsAreFetchedVerifiedAndCachedAsync()
        {
            // §5.15 asset content delivery: the server serves its USD layer closure
            // (Cell.usda RootLayer + robot.usda + tool.usda) via Part 5 FileType; the
            // connector streams, verifies each digest, and caches them locally.
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink());
            string cacheDir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "RobotAssetCache", System.IO.Path.GetRandomFileName());
            try
            {
                List<OpenUsdConnector.FetchedAsset> assets =
                    await connector.FetchServedAssetsAsync(cacheDir, CancellationToken.None).ConfigureAwait(false);

                OpenUsdConnector.FetchedAsset? root = assets.Find(a => a.Kind == OpenUsdAssetKind.RootLayer);
                Assert.Multiple(() =>
                {
                    Assert.That(assets, Has.Count.EqualTo(3), "Expected 3 served layers.");
                    Assert.That(assets.Exists(a => a.Identifier == "Cell.usda"
                        && a.Kind == OpenUsdAssetKind.RootLayer), Is.True, "Cell.usda RootLayer not served.");
                    Assert.That(assets.Exists(a => a.Identifier == "robot.usda"), Is.True, "robot.usda not served.");
                    Assert.That(assets.Exists(a => a.Identifier == "tool.usda"), Is.True, "tool.usda not served.");
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
