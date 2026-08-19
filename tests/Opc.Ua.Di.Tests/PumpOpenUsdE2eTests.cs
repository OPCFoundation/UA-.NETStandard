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
using System.Linq;
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
        /// <summary>
        /// Pumps the fixture's server materialises, which is the
        /// PumpDeviceIntegrationOptions default.
        /// </summary>
        private const int ExpectedPumpCount = 2;

        private ISession? m_session;
        private ISession? m_privilegedSession;
        private ApplicationConfiguration m_clientConfig = null!;

        /// <summary>
        /// §9: the sample withholds the command target's write right by default, so the
        /// positive command test must present a real (non-anonymous) credential. This
        /// authenticator accepts a single well-known operator credential; the Part 18
        /// role manager then maps it to the AuthenticatedUser role.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812",
            Justification = "Instantiated by the server's dependency injection via AddIdentityAuthenticator.")]
        internal sealed class OperatorAuthenticator : Opc.Ua.Identity.IUserTokenAuthenticator
        {
            internal const string UserName = "usd-operator";
            internal const string Password = "usd-operator-secret";

            public UserTokenType TokenType => UserTokenType.UserName;

            public string? IssuedTokenProfileUri => null;

            public ValueTask<Opc.Ua.Identity.AuthenticationResult> AuthenticateAsync(
                Opc.Ua.Identity.AuthenticationContext context,
                CancellationToken ct = default)
            {
                if (context.TokenHandler is not UserNameIdentityTokenHandler handler)
                {
                    return new ValueTask<Opc.Ua.Identity.AuthenticationResult>(
                        Opc.Ua.Identity.AuthenticationResult.NotHandled);
                }
                byte[]? password = handler.DecryptedPassword;
                if (handler.UserName != UserName || password == null ||
                    System.Text.Encoding.UTF8.GetString(password) != Password)
                {
                    return new ValueTask<Opc.Ua.Identity.AuthenticationResult>(
                        Opc.Ua.Identity.AuthenticationResult.Reject(
                            new ServiceResult(StatusCodes.BadUserAccessDenied)));
                }
                return new ValueTask<Opc.Ua.Identity.AuthenticationResult>(
                    Opc.Ua.Identity.AuthenticationResult.Accept(new UserIdentity(handler)));
            }
        }

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
                    // §9: the pump withholds the command target's write right from
                    // anonymous sessions, so the endpoint must also offer a real
                    // credential a connector can present.
                    o.UserTokenPolicies.Add(new Opc.Ua.Server.Hosting.OpcUaUserTokenPolicy
                    {
                        TokenType = UserTokenType.Anonymous
                    });
                    o.UserTokenPolicies.Add(new Opc.Ua.Server.Hosting.OpcUaUserTokenPolicy
                    {
                        TokenType = UserTokenType.UserName
                    });
                })
                .AddIdentityAuthenticator<OperatorAuthenticator>()
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
            // A second, authenticated session: §9 requires a connector to hold the
            // authorization the command target demands, and the sample withholds it
            // from anonymous sessions.
            m_privilegedSession = await sessionFactory.CreateAsync(
                m_clientConfig, endpoint, updateBeforeConnect: false,
                sessionName: "PumpOpenUsdE2ePrivileged", sessionTimeout: 60000,
                identity: new UserIdentity(
                    OperatorAuthenticator.UserName,
                    System.Text.Encoding.UTF8.GetBytes(OperatorAuthenticator.Password)),
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
            if (m_privilegedSession != null)
            {
                await m_privilegedSession.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                await m_privilegedSession.DisposeAsync().ConfigureAwait(false);
                m_privilegedSession = null;
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
            // Resolved from the generated symbolic constant (an ExpandedNodeId carrying
            // the namespace URI) rather than a numeric literal, so a model renumbering
            // can never silently invalidate this assertion.
            NodeId repType = ExpandedNodeId.ToNodeId(
                Opc.Ua.OpenUsd.ObjectTypeIds.OpenUsdRepresentationType, m_session!.NamespaceUris);
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

            Assert.That(rep, Is.Not.Null, "OpenUsdRepresentation not discovered on Pump_1.");
            Assert.That(rep!.PrimPath, Is.EqualTo("/Plant/Pumps/Pump_1"));
            Assert.That(rep.StageNodeId.IsNull, Is.False);
            Assert.That(rep.RootLayerIdentifier, Is.EqualTo("asset-repo/Plant.usd"));
            // Layout (1) + telemetry (10) + supervision alarms (3) + command (1).
            Assert.That(rep.Bindings, Has.Count.EqualTo(15));
        }

        [Test]
        public async Task RepresentationsAreMountedWithHasAddInAsync()
        {
            // The representation is an AddIn (§5.2), so every represented Object must
            // reference it with HasAddIn, not plain HasComponent. Both browse the same
            // way - HasAddIn is a subtype of HasComponent - so a wrong reference type
            // is invisible to every functional test and only shows up against a
            // conformance checker. Hence this assertion.
            //
            // Every representation is checked, not just the pump's: an earlier version
            // of this test looked at Pump #1 alone and would have waved through the
            // plant-aggregation representation that arrived later on a separate branch
            // still carrying HasComponent.
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink());
            System.Collections.Generic.List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            Assert.That(reps, Is.Not.Empty, "No representations discovered.");

            foreach (OpenUsdConnector.RepresentationInfo rep in reps)
            {
                var browseOwner = new BrowseDescription
                {
                    NodeId = rep.NodeId,
                    BrowseDirection = BrowseDirection.Inverse,
                    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasAddIn,
                    IncludeSubtypes = false,
                    NodeClassMask = (uint)NodeClass.Object,
                    ResultMask = (uint)BrowseResultMask.All
                };
                BrowseResponse owners = await m_session!.BrowseAsync(
                    null!, null!, 0, new BrowseDescription[] { browseOwner }, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(
                    owners.Results[0].References,
                    Has.Count.EqualTo(1),
                    $"{rep.PrimPath} is not mounted on its represented Object with HasAddIn.");
            }
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
                Assert.That(command.CommandTargetNodeId.IsNull, Is.False);
            });
        }

        [Test]
        public async Task StageRootLayerDigestVerifiesAsync()
        {
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink());
            OpenUsdConnector.RepresentationInfo? rep = await PumpRepAsync(connector).ConfigureAwait(false);
            Assert.That(rep, Is.Not.Null);

            // §5.2: the digest is computed over the resolved root-layer *content*, not
            // over the identifier string, and a connector shall refuse to open a layer
            // whose digest does not match.
            Assert.That(rep!.DigestAlgorithm, Is.EqualTo(OpenUsdDigestAlgorithm.Sha256));
            Assert.That(rep!.RootLayerDigest.IsNull, Is.False);
            Assert.That(rep!.RootLayerDigest.Length, Is.EqualTo(32));
            bool verified = await connector
                .VerifyStageDigestAsync(rep!, CancellationToken.None).ConfigureAwait(false);
            Assert.That(verified, Is.True, "RootLayerDigest failed verification.");
        }

        [Test]
        public async Task StageRootLayerDigestRefusesCorruptedContentAsync()
        {
            // §5.2 negative: a connector shall refuse to open a layer whose digest does
            // not match the resolved content. Flipping a single byte of the served root
            // layer must be detected — which is only possible because the digest covers
            // the content and not the (unchanged) identifier string.
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink());
            OpenUsdConnector.RepresentationInfo? rep = await PumpRepAsync(connector).ConfigureAwait(false);
            Assert.That(rep, Is.Not.Null);

            byte[] corrupted = System.Text.Encoding.UTF8.GetBytes("#usda 1.0\n(tampered)\n");
            Assert.That(OpenUsdConnector.VerifyStageDigest(rep!, corrupted), Is.False,
                "Connector accepted a root layer whose content does not match the digest.");
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
            // the alarm-ring visibility token (initially "invisible" until an alarm).
            Assert.That(sink.WasWritten("/Plant/Pumps/Pump_1/AlarmRing", "visibility"), Is.True,
                "Alarm binding did not author AlarmRing visibility.");
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
            var connector = new OpenUsdConnector(
                m_privilegedSession!, new MockUsdSink(), enableCommands: true);
            // Every pump declares a command binding, and the connector issues the
            // command to the first one it discovers, so the assertion has to read
            // that same target rather than assuming a particular pump.
            System.Collections.Generic.List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            NodeId target = NodeId.Null;
            foreach (OpenUsdConnector.RepresentationInfo r in reps)
            {
                foreach (OpenUsdConnector.BindingInfo b in r.Bindings)
                {
                    if (target.IsNull &&
                        b.Intent == OpenUsdIntentProfile.UsdToUaCommand &&
                        !b.CommandTargetNodeId.IsNull)
                    {
                        target = b.CommandTargetNodeId;
                    }
                }
            }
            Assert.That(target.IsNull, Is.False, "Command target NodeId missing.");

            const double setpoint = 42.5;
            bool ok = await connector.IssueCommandAsync(setpoint, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ok, Is.True, "Command write did not succeed.");

            var toRead = new ReadValueId[]
            {
                new ReadValueId { NodeId = target, AttributeId = Attributes.Value }
            };
            ReadResponse rr = await m_privilegedSession!.ReadAsync(
                null!, 0, TimestampsToReturn.Neither, toRead, CancellationToken.None)
                .ConfigureAwait(false);
            double actual = System.Convert.ToDouble(
                rr.Results[0].WrappedValue.AsBoxedObject(),
                System.Globalization.CultureInfo.InvariantCulture);
            Assert.That(actual, Is.EqualTo(setpoint).Within(1e-9),
                "Server SpeedSetpoint was not updated by the command binding.");
        }

        [Test]
        public async Task CommandIsRefusedForUnauthorizedSessionAsync()
        {
            // §5.10/§9: the connector shall hold the write authorization the target
            // requires, which the Server withholds by default. The anonymous session
            // does not hold it, so the connector refuses *before* issuing the Write —
            // it must not rely on the Server's error, and the value must not change.
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink(), enableCommands: true);
            OpenUsdConnector.RepresentationInfo? rep = await PumpRepAsync(connector).ConfigureAwait(false);
            NodeId target = NodeId.Null;
            foreach (OpenUsdConnector.BindingInfo b in rep!.Bindings)
            {
                if (b.Intent == OpenUsdIntentProfile.UsdToUaCommand)
                {
                    target = b.CommandTargetNodeId;
                }
            }
            Assert.That(target.IsNull, Is.False, "Command target NodeId missing.");

            var toRead = new ReadValueId[]
            {
                new ReadValueId { NodeId = target, AttributeId = Attributes.Value }
            };
            ReadResponse before = await m_session!.ReadAsync(
                null!, 0, TimestampsToReturn.Neither, toRead, CancellationToken.None)
                .ConfigureAwait(false);
            double previous = System.Convert.ToDouble(
                before.Results[0].WrappedValue.AsBoxedObject(),
                System.Globalization.CultureInfo.InvariantCulture);

            bool ok = await connector.IssueCommandAsync(previous + 7.0, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ok, Is.False, "Command was issued without the required authorization.");

            ReadResponse after = await m_session!.ReadAsync(
                null!, 0, TimestampsToReturn.Neither, toRead, CancellationToken.None)
                .ConfigureAwait(false);
            double actual = System.Convert.ToDouble(
                after.Results[0].WrappedValue.AsBoxedObject(),
                System.Globalization.CultureInfo.InvariantCulture);
            Assert.That(actual, Is.EqualTo(previous).Within(1e-9),
                "Unauthorized command changed the server value.");
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

            // Poll for the expected sink writes instead of waiting a fixed delay:
            // on a slow/loaded CI agent the first sampled values can take longer
            // than a fixed sleep, so StopAsync would run before any value flowed.
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline &&
                !(sink.WasWritten("/Plant/Pumps/Pump_1/Impeller", "xformOp:rotateZ") &&
                    sink.WasWritten("/Plant/Pumps/Pump_1/Body", "primvars:displayColor") &&
                    sink.WasWritten("/Plant/Pumps/Pump_1/Discharge/Gauge/Needle", "xformOp:rotateZ")))
            {
                await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
            }

            await connector.StopAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(sink.WasWritten("/Plant/Pumps/Pump_1/Impeller", "xformOp:rotateZ"), Is.True,
                    "Rotation binding produced no value.");
                Assert.That(sink.WasWritten("/Plant/Pumps/Pump_1/Body", "primvars:displayColor"), Is.True,
                    "DisplayColor binding produced no value.");
                Assert.That(sink.WasWritten("/Plant/Pumps/Pump_1/Discharge/Gauge/Needle", "xformOp:rotateZ"), Is.True,
                    "Discharge pressure gauge produced no value.");
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
                    Assert.That(sink.WasPrimComposed("/Plant/Pumps/Pump_1/Impeller"), Is.True,
                        "Impeller component prim not composed.");
                    Assert.That(sink.WasPrimComposed("/Plant/Pumps/Pump_1/Bearing"), Is.True,
                        "Bearing component prim not composed.");
                });
            }
            finally
            {
                await connector.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task OnlySimulatedPumpsAreComposedAsync()
        {
            // The rendered hall holds exactly the pumps the connected server
            // simulates. The ProductionLine aggregation and the cross-server
            // component are address-space topology, not machines anyone drives,
            // so composing them put pumps in the twin that no client could
            // account for - and a federated pump from another server is not this
            // server's to show at all.
            var sink = new MockUsdSink();
            var connector = new OpenUsdConnector(m_session!, sink);
            await connector.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.Multiple(() =>
                {
                    for (int i = 1; i <= ExpectedPumpCount; i++)
                    {
                        string prim = $"/Plant/Pumps/Pump_{i}";
                        Assert.That(sink.WasPrimComposed(prim), Is.True, prim);
                        Assert.That(sink.IsPrimActive(prim), Is.True, prim);
                    }
                    Assert.That(sink.WasPrimComposed($"/Plant/Pumps/Pump_{ExpectedPumpCount + 1}"),
                        Is.False, "A pump the server does not simulate was composed.");
                    Assert.That(sink.WasPrimComposed("/Plant/Line1/Pumps/P_201"), Is.False,
                        "An aggregated line pump was composed into the twin.");
                    Assert.That(sink.WasPrimComposed("/Plant/Line1/RemotePump"), Is.False,
                        "The cross-server pump was composed into the twin.");
                });
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

            OpenUsdConnector.RepresentationInfo? pump = reps.Find(r => r.PrimPath == "/Plant/Pumps/Pump_1");
            OpenUsdConnector.RepresentationInfo? plant = reps.Find(r => r.PrimPath == "/Plant");

            Assert.Multiple(() =>
            {
                Assert.That(reps, Has.Count.GreaterThanOrEqualTo(2), "Expected plant + pump representations.");
                Assert.That(pump, Is.Not.Null);
                Assert.That(pump!.Components, Has.Count.EqualTo(2), "Pump should have 2 (1:1) component bindings.");
                Assert.That(plant, Is.Not.Null, "The plant aggregation representation was not discovered.");
                // The plant composes one referenced pump prim per configured pump.
                Assert.That(plant!.Components.Exists(c => c.Cardinality == OpenUsdCardinality.Many), Is.True);
            });
        }

        /// <summary>
        /// Every configured pump has to be a twin in its own right: its own prim,
        /// discoverable through the registry, and driven by its own simulation.
        /// The sample previously bound every pump to one hard-coded prim path and
        /// registered only the first representation, so a server started with
        /// <c>--pumps N</c> rendered a single machine.
        /// </summary>
        [Test]
        public async Task EveryConfiguredPumpIsAnIndependentTwinAsync()
        {
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink());
            System.Collections.Generic.List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None)
                    .ConfigureAwait(false);

            System.Collections.Generic.List<OpenUsdConnector.RepresentationInfo> pumps =
                reps.FindAll(r => r.PrimPath != null &&
                    r.PrimPath.StartsWith("/Plant/Pumps/", StringComparison.Ordinal) &&
                    !r.PrimPath.EndsWith("/Impeller", StringComparison.Ordinal) &&
                    !r.PrimPath.EndsWith("/Bearing", StringComparison.Ordinal));

            Assert.Multiple(() =>
            {
                Assert.That(pumps, Has.Count.EqualTo(ExpectedPumpCount),
                    "Every configured pump must publish a discoverable representation.");
                Assert.That(
                    pumps.ConvertAll(r => r.PrimPath).Distinct().Count(),
                    Is.EqualTo(pumps.Count),
                    "Two pumps must never share a prim.");
                foreach (OpenUsdConnector.RepresentationInfo pump in pumps)
                {
                    string primPath = pump.PrimPath!;
                    Assert.That(pump.Bindings, Is.Not.Empty, primPath);
                    Assert.That(
                        pump.Bindings.TrueForAll(b =>
                            b.PrimPath != null &&
                            b.PrimPath.StartsWith(primPath, StringComparison.Ordinal)),
                        Is.True,
                        primPath + " has a binding that targets another pump's prim.");
                }
            });

            // The shaft angle is integrated per pump from its own phase-shifted
            // duty point, so two pumps can never report the same angle.
            System.Collections.Generic.List<double> angles = [];
            foreach (OpenUsdConnector.RepresentationInfo pump in pumps)
            {
                OpenUsdConnector.BindingInfo? shaft = pump.Bindings.Find(
                    b => b.PropertyName == "xformOp:rotateZ" &&
                        b.PrimPath != null &&
                        b.PrimPath.EndsWith("/Impeller", StringComparison.Ordinal));
                Assert.That(shaft, Is.Not.Null, pump.PrimPath + " has no shaft binding.");
                DataValue value = await m_session!.ReadValueAsync(
                    shaft!.SourceNodeId, CancellationToken.None).ConfigureAwait(false);
                Assert.That(value.WrappedValue.TryGetValue(out double angle), Is.True);
                angles.Add(angle);
            }

            Assert.That(angles.Distinct().Count(), Is.EqualTo(angles.Count),
                "Every pump must integrate its own shaft angle.");
        }

        /// <summary>
        /// Asserts that every prim a transform binding targets declares the op the
        /// connector actually authors in its <c>xformOpOrder</c>.
        /// A Translation, Rotation or Scale render target resolves to a single
        /// <c>xformOp:transform</c> matrix; every other <c>xformOp:</c> property is
        /// authored under its own name. USD only evaluates ops named in
        /// <c>xformOpOrder</c>, and the list is uniform, so a connector cannot add
        /// itself to it from a stronger layer. When the asset named the wrong op the
        /// value was silently discarded, which parked every composed pump on the
        /// origin - the hall rendered N stacked machines and looked like it held one.
        /// </summary>
        [Test]
        public async Task TransformBindingsTargetDeclaredXformOpsAsync()
        {
            var connector = new OpenUsdConnector(m_session!, new MockUsdSink());
            string cacheDir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "PumpXformOps", System.IO.Path.GetRandomFileName());
            try
            {
                System.Collections.Generic.List<OpenUsdConnector.FetchedAsset> assets =
                    await connector.FetchServedAssetsAsync(cacheDir, CancellationToken.None).ConfigureAwait(false);
                OpenUsdConnector.FetchedAsset? component = assets.Find(a => a.Identifier == "pump.usda");
                Assert.That(component, Is.Not.Null, "pump.usda was not served.");
                string layer = System.IO.File.ReadAllText(component!.LocalPath);

                OpenUsdConnector.RepresentationInfo? pump = await PumpRepAsync(connector).ConfigureAwait(false);
                Assert.That(pump, Is.Not.Null);

                System.Collections.Generic.List<OpenUsdConnector.BindingInfo> transforms =
                    pump!.Bindings.FindAll(b => b.PropertyName != null &&
                        b.PropertyName.StartsWith("xformOp:", StringComparison.Ordinal));
                Assert.That(transforms, Is.Not.Empty, "The pump publishes no transform bindings.");

                Assert.Multiple(() =>
                {
                    foreach (OpenUsdConnector.BindingInfo binding in transforms)
                    {
                        // Bindings address the composed stage (/Plant/Pumps/Pump_1/...);
                        // the asset authors the same prims under its own root (/Pump/...).
                        string assetPath = "/Pump" + binding.PrimPath![pump.PrimPath!.Length..];
                        string authored = AuthoredOpFor(binding.PropertyName!);
                        Assert.That(
                            DeclaredXformOps(layer, assetPath),
                            Does.Contain(authored),
                            $"{assetPath} is bound to {binding.PropertyName}, which a connector authors as " +
                            $"{authored}, but it does not list {authored} in xformOpOrder - so USD discards " +
                            "every value written to it.");
                    }
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

        /// <summary>
        /// Returns the xform op a connector authors for a bound op name. Translation,
        /// rotation and scale are accumulated into one matrix so that the op order
        /// never has to be rewritten from a stronger layer; anything else, such as the
        /// scalar <c>xformOp:rotateZ</c> a shaft uses, is authored under its own name.
        /// </summary>
        private static string AuthoredOpFor(string propertyName)
        {
            return propertyName is "xformOp:translate" or "xformOp:rotateXYZ" or "xformOp:scale"
                ? "xformOp:transform"
                : propertyName;
        }

        /// <summary>
        /// Reads the <c>xformOpOrder</c> declared on a prim in a USD text layer.
        /// </summary>
        private static System.Collections.Generic.List<string> DeclaredXformOps(
            string layer, string primPath)
        {
            var names = new System.Collections.Generic.List<string>();
            var openedAt = new System.Collections.Generic.List<int>();
            var ops = new System.Collections.Generic.List<string>();
            string? pending = null;
            int depth = 0;
            bool collecting = false;

            foreach (string line in layer.Split('\n'))
            {
                string text = line.Trim();
                bool inTarget = names.Count > 0 &&
                    string.Equals("/" + string.Join("/", names), primPath, StringComparison.Ordinal);

                if (text.StartsWith("def ", StringComparison.Ordinal) ||
                    text.StartsWith("over ", StringComparison.Ordinal) ||
                    text.StartsWith("class ", StringComparison.Ordinal))
                {
                    pending = QuotedTokens(text).FirstOrDefault();
                }

                // The op list may wrap over several lines, and the declaration itself
                // contains a bracket pair ("uniform token[]"), so only the text after
                // the assignment decides whether the list is already closed.
                if (inTarget && text.Contains("xformOpOrder", StringComparison.Ordinal))
                {
                    int assign = text.IndexOf('=', StringComparison.Ordinal);
                    string tail = assign >= 0 ? text[(assign + 1)..] : text;
                    ops.AddRange(QuotedTokens(tail));
                    collecting = !tail.Contains(']', StringComparison.Ordinal);
                }
                else if (collecting)
                {
                    ops.AddRange(QuotedTokens(text));
                    collecting = !text.Contains(']', StringComparison.Ordinal);
                }

                foreach (char c in line)
                {
                    if (c == '{')
                    {
                        depth++;
                        if (pending != null)
                        {
                            names.Add(pending);
                            openedAt.Add(depth);
                            pending = null;
                        }
                    }
                    else if (c == '}')
                    {
                        if (openedAt.Count > 0 && openedAt[^1] == depth)
                        {
                            names.RemoveAt(names.Count - 1);
                            openedAt.RemoveAt(openedAt.Count - 1);
                        }
                        depth--;
                    }
                }
            }

            return ops;
        }

        /// <summary>
        /// Splits the double-quoted tokens out of a line of USD text.
        /// </summary>
        private static System.Collections.Generic.IEnumerable<string> QuotedTokens(string line)
        {
            string[] parts = line.Split('"');
            for (int i = 1; i < parts.Length; i += 2)
            {
                yield return parts[i];
            }
        }

        private static async Task<OpenUsdConnector.RepresentationInfo?> PumpRepAsync(OpenUsdConnector connector)
        {
            System.Collections.Generic.List<OpenUsdConnector.RepresentationInfo> all =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None).ConfigureAwait(false);
            return all.Find(r => r.PrimPath == "/Plant/Pumps/Pump_1");
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
            // (Plant.usda RootLayer + pump.usda) via Part 5 FileType;
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
                    Assert.That(assets, Has.Count.EqualTo(2), "Expected 2 served layers.");
                    Assert.That(assets.Exists(a => a.Identifier == "Plant.usda"
                        && a.Kind == OpenUsdAssetKind.RootLayer), Is.True, "Plant.usda RootLayer not served.");
                    Assert.That(assets.Exists(a => a.Identifier == "pump.usda"), Is.True, "pump.usda not served.");
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
