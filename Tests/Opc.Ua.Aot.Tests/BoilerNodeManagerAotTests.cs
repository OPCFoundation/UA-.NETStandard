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

extern alias boilersample;

using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using TUnit.Core.Interfaces;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT smoke tests that verify the source-generated
    /// <c>Boiler.BoilerNodeManagerFactory</c> emitted by the
    /// <c>[NodeManager]</c> attribute on <see cref="global::Boiler.BoilerNodeManager"/>
    /// (in the MinimalBoilerServer sample) actually loads the boiler
    /// address space, registers its namespace, and dispatches the
    /// fluent <c>OnRead</c> callback wired in
    /// <c>BoilerNodeManager.Configure.cs</c>. This protects the
    /// end-to-end pipeline (generator + fluent builder + factory)
    /// against AOT regressions.
    /// </summary>
    [ClassDataSource<BoilerAotFixture>(Shared = SharedType.PerTestSession)]
    public class BoilerNodeManagerAotTests(BoilerAotFixture fixture)
    {
        private const string kBoilerNamespaceUri =
            "http://opcfoundation.org/UA/Boiler/";

        [Test]
        public async Task BoilerNamespaceIsRegistered()
        {
            DataValue nsArray = await fixture.Session.ReadValueAsync(
                VariableIds.Server_NamespaceArray,
                CancellationToken.None).ConfigureAwait(false);

            await Assert.That(StatusCode.IsGood(nsArray.StatusCode)).IsTrue();
            string[] uris = nsArray.GetValue<string[]>(null);
            await Assert.That(uris).IsNotNull();
            await Assert.That(uris).Contains(kBoilerNamespaceUri);
        }

        [Test]
        public async Task DrumLevelOnReadCallbackProducesValueInRange()
        {
            NodeId drumLevel = await ResolveBoilerVariableAsync(
                "DrumX001", "LIX001", "Output").ConfigureAwait(false);

            DataValue dv = await fixture.Session.ReadValueAsync(
                drumLevel, CancellationToken.None).ConfigureAwait(false);

            await Assert.That(StatusCode.IsGood(dv.StatusCode)).IsTrue();
            double value = dv.GetValue<double>(double.NaN);
            // Configure-wired OnRead returns 50 + 10*sin(t*0.05).
            await Assert.That(value).IsBetween(40.0 - 1e-9, 60.0 + 1e-9);
        }

        [Test]
        public async Task PipeFlowOnReadCallbackProducesValueInRange()
        {
            NodeId pipeFlow = await ResolveBoilerVariableAsync(
                "PipeX001", "FTX001", "Output").ConfigureAwait(false);

            DataValue dv = await fixture.Session.ReadValueAsync(
                pipeFlow, CancellationToken.None).ConfigureAwait(false);

            await Assert.That(StatusCode.IsGood(dv.StatusCode)).IsTrue();
            double value = dv.GetValue<double>(double.NaN);
            // Configure-wired OnRead returns 100 + 25*cos(t*0.07).
            await Assert.That(value).IsBetween(75.0 - 1e-9, 125.0 + 1e-9);
        }

        /// <summary>
        /// Walks the boiler instance tree starting from the well-known
        /// <c>Boilers/Boiler #1</c> root (in the boiler namespace) using
        /// TranslateBrowsePathsToNodeIds so the test does not hard-code
        /// any generated NodeId.
        /// </summary>
        private async Task<NodeId> ResolveBoilerVariableAsync(
            params string[] tail)
        {
            ushort nsIndex = (ushort)fixture.Session.NamespaceUris
                .GetIndex(kBoilerNamespaceUri);
            await Assert.That(nsIndex).IsGreaterThan((ushort)0);

            var elements = new List<RelativePathElement>
            {
                new()
                {
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsInverse = false,
                    IncludeSubtypes = true,
                    TargetName = new QualifiedName("Boilers", nsIndex)
                },
                new()
                {
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsInverse = false,
                    IncludeSubtypes = true,
                    TargetName = new QualifiedName("Boiler #1", nsIndex)
                }
            };
            foreach (string segment in tail)
            {
                elements.Add(new RelativePathElement
                {
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsInverse = false,
                    IncludeSubtypes = true,
                    TargetName = new QualifiedName(segment, nsIndex)
                });
            }

            var browsePaths = new List<BrowsePath>
            {
                new()
                {
                    StartingNode = ObjectIds.ObjectsFolder,
                    RelativePath = new RelativePath
                    {
                        Elements = elements.ToArrayOf()
                    }
                }
            };

            TranslateBrowsePathsToNodeIdsResponse response =
                await fixture.Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, browsePaths.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            await Assert.That(response.Results.Count).IsEqualTo(1);
            BrowsePathResult result = response.Results.ToList()[0];
            await Assert.That(StatusCode.IsGood(result.StatusCode)).IsTrue();
            await Assert.That(result.Targets.Count).IsGreaterThan(0);

            return ExpandedNodeId.ToNodeId(
                result.Targets.ToList()[0].TargetId,
                fixture.Session.NamespaceUris);
        }
    }

    /// <summary>
    /// Per-test-session fixture that boots a NativeAOT-friendly server
    /// hosting the source-generated <c>BoilerNodeManagerFactory</c> and
    /// connects an anonymous client session to it.
    /// </summary>
    public sealed class BoilerAotFixture : IAsyncInitializer, IAsyncDisposable
    {
        public AotServerFixture<BoilerTestServer> ServerFixture { get; private set; } = null!;
        public Opc.Ua.Client.ISession Session { get; private set; } = null!;
        public string ServerUrl { get; private set; } = null!;
        public ITelemetryContext Telemetry { get; private set; } = null!;
        private ApplicationConfiguration m_clientConfiguration = null!;
        private string m_pkiRoot = null!;

        public async Task InitializeAsync()
        {
            Telemetry = DefaultTelemetry.Create(builder =>
                builder.SetMinimumLevel(LogLevel.Warning));

            ServerFixture = new AotServerFixture<BoilerTestServer>(
                t => new BoilerTestServer(t), Telemetry)
            {
                AutoAccept = true,
                SecurityNone = true
            };
            await ServerFixture.LoadConfigurationAsync(
                Path.Combine(Directory.GetCurrentDirectory(), "boiler-pki"))
                .ConfigureAwait(false);
            await ServerFixture.StartAsync().ConfigureAwait(false);

            ServerUrl = $"opc.tcp://localhost:{ServerFixture.Port}/" +
                nameof(BoilerTestServer);

            m_pkiRoot = Path.Combine(
                Path.GetTempPath(), "OpcUaAotTests", "boiler-client-pki");

            m_clientConfiguration = new ApplicationConfiguration(Telemetry)
            {
                ApplicationName = "BoilerAotTestClient",
                ApplicationUri = "urn:localhost:OPCFoundation:BoilerAotTestClient",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(m_pkiRoot, "own"),
                        SubjectName = "CN=BoilerAotTestClient, O=OPC Foundation"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(m_pkiRoot, "issuer")
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(m_pkiRoot, "trusted")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(m_pkiRoot, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = true
                },
                TransportQuotas = new TransportQuotas
                {
                    MaxMessageSize = 4 * 1024 * 1024
                },
                ClientConfiguration = new ClientConfiguration(),
                ServerConfiguration = new ServerConfiguration()
            };
            await m_clientConfiguration.ValidateAsync(
                ApplicationType.Client).ConfigureAwait(false);
            m_clientConfiguration.CertificateValidator
                .CertificateValidation += (s, e) => e.Accept = true;

            EndpointDescription endpointDescription =
                await CoreClientUtils.SelectEndpointAsync(
                    m_clientConfiguration, ServerUrl, useSecurity: false,
                    Telemetry, CancellationToken.None).ConfigureAwait(false);
            var configuredEndpoint = new ConfiguredEndpoint(
                null, endpointDescription,
                EndpointConfiguration.Create(m_clientConfiguration));

            var sessionFactory = new ClassicSessionFactory(Telemetry);
#pragma warning disable CA2000 // Dispose objects before losing scope
            Session = await sessionFactory.CreateAsync(
                m_clientConfiguration,
                configuredEndpoint,
                updateBeforeConnect: false,
                sessionName: "BoilerAotTest",
                sessionTimeout: 60000,
                identity: new UserIdentity(new AnonymousIdentityToken()),
                preferredLocales: default,
                ct: CancellationToken.None).ConfigureAwait(false);
#pragma warning restore CA2000 // Dispose objects before losing scope
        }

        public async ValueTask DisposeAsync()
        {
            if (Session != null)
            {
                await Session.CloseAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                Session.Dispose();
                Session = null!;
            }
            if (ServerFixture != null)
            {
                await ServerFixture.StopAsync().ConfigureAwait(false);
                ServerFixture = null!;
            }
        }
    }

    /// <summary>
    /// Public <see cref="StandardServer"/> subclass that registers the
    /// source-generated <see cref="global::Boiler.BoilerNodeManagerFactory"/>.
    /// Mirrors the internal <c>BoilerStandardServer</c> in
    /// MinimalBoilerServer's <c>Program.cs</c> but is exposed as
    /// <c>public</c> so <see cref="AotServerFixture{T}"/> can host it.
    /// </summary>
    public sealed class BoilerTestServer : StandardServer
    {
        public BoilerTestServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        protected override void OnServerStarting(ApplicationConfiguration configuration)
        {
            base.OnServerStarting(configuration);
            AddNodeManager(new boilersample::Boiler.BoilerNodeManagerFactory());
        }
    }
}
