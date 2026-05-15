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

extern alias calcsample;

using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.Server;
using TUnit.Core.Interfaces;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT smoke tests that verify the source-generated
    /// <c>Calc.CalcNodeManagerFactory</c> emitted by the
    /// <c>[NodeManager]</c> attribute on
    /// <see cref="Calc.CalcNodeManager"/> (in the MinimalCalcServer
    /// sample) loads the calculator address space, registers its
    /// namespace, and dispatches each of the three typed fluent
    /// <c>OnCall(...)</c> overloads — sync int+int→int, async
    /// double+double→double, and sync string+string→string — wired in
    /// <c>CalcNodeManager.Configure.cs</c>. Together these tests cover
    /// the generator's typed input-unpack (Variant.TryGetValue&lt;T&gt;),
    /// output-box (Variant.From&lt;T&gt;), and async dispatch paths
    /// against the AOT-compiled binary.
    /// </summary>
    [ClassDataSource<CalculatorAotFixture>(Shared = SharedType.PerTestSession)]
    public class CalculatorNodeManagerAotTests(CalculatorAotFixture fixture)
    {
        private const string kCalcNamespaceUri =
            "http://opcfoundation.org/UA/Calc/";

        [Test]
        public async Task CalcNamespaceIsRegistered()
        {
            DataValue nsArray = await fixture.Session.ReadValueAsync(
                VariableIds.Server_NamespaceArray,
                CancellationToken.None).ConfigureAwait(false);

            await Assert.That(StatusCode.IsGood(nsArray.StatusCode)).IsTrue();
            string[] uris = nsArray.GetValue<string[]>(null);
            await Assert.That(uris).IsNotNull();
            await Assert.That(uris).Contains(kCalcNamespaceUri);
        }

        [Test]
        public async Task AddMethodReturnsSum()
        {
            // Wired via Configure(ICalcNodeManagerBuilder) using
            // builder.Calculator.Add.OnCall((int a, int b) => a + b) —
            // exercises the typed sync OnCall overload end-to-end with
            // primitive value-type inputs and output.
            NodeId calculator = await ResolveCalculatorNodeAsync()
                .ConfigureAwait(false);
            NodeId addMethod = await ResolveCalculatorNodeAsync("Add")
                .ConfigureAwait(false);

            ArrayOf<Variant> outputs = await fixture.Session.CallAsync(
                calculator,
                addMethod,
                CancellationToken.None,
                new Variant(2),
                new Variant(3)).ConfigureAwait(false);

            await Assert.That(outputs.Count).IsEqualTo(1);
            Variant single = outputs.ToList()[0];
            await Assert.That(single.TypeInfo.BuiltInType)
                .IsEqualTo(BuiltInType.Int32);
            await Assert.That(single.TryGetValue(out int sum)).IsTrue();
            await Assert.That(sum).IsEqualTo(5);
        }

        [Test]
        public async Task MultiplyMethodReturnsProductAsync()
        {
            // Wired via Configure(ICalcNodeManagerBuilder) using
            // builder.Calculator.Multiply.OnCall(async (double, double,
            // CancellationToken) => ...) — exercises the typed async
            // OnCall overload end-to-end through
            // AsyncCustomNodeManager.CallAsync.
            NodeId calculator = await ResolveCalculatorNodeAsync()
                .ConfigureAwait(false);
            NodeId multiplyMethod = await ResolveCalculatorNodeAsync("Multiply")
                .ConfigureAwait(false);

            ArrayOf<Variant> outputs = await fixture.Session.CallAsync(
                calculator,
                multiplyMethod,
                CancellationToken.None,
                new Variant(0.5),
                new Variant(4.0)).ConfigureAwait(false);

            await Assert.That(outputs.Count).IsEqualTo(1);
            Variant single = outputs.ToList()[0];
            await Assert.That(single.TypeInfo.BuiltInType)
                .IsEqualTo(BuiltInType.Double);
            await Assert.That(single.TryGetValue(out double product)).IsTrue();
            await Assert.That(product).IsEqualTo(2.0);
        }

        [Test]
        public async Task ConcatMethodReturnsConcatenation()
        {
            // Wired via Configure(ICalcNodeManagerBuilder) using
            // builder.Calculator.Concat.OnCall((string l, string r) =>
            // ...) — exercises typed reference-type marshalling on both
            // input arguments and the return value.
            NodeId calculator = await ResolveCalculatorNodeAsync()
                .ConfigureAwait(false);
            NodeId concatMethod = await ResolveCalculatorNodeAsync("Concat")
                .ConfigureAwait(false);

            ArrayOf<Variant> outputs = await fixture.Session.CallAsync(
                calculator,
                concatMethod,
                CancellationToken.None,
                new Variant("foo"),
                new Variant("bar")).ConfigureAwait(false);

            await Assert.That(outputs.Count).IsEqualTo(1);
            Variant single = outputs.ToList()[0];
            await Assert.That(single.TypeInfo.BuiltInType)
                .IsEqualTo(BuiltInType.String);
            await Assert.That(single.TryGetValue(out string concatenated)).IsTrue();
            await Assert.That(concatenated).IsEqualTo("foobar");
        }

        /// <summary>
        /// Walks the calculator instance tree starting from the
        /// well-known <c>Calculator</c> root (in the calc namespace)
        /// using TranslateBrowsePathsToNodeIds so the tests do not
        /// hard-code any generated NodeId.
        /// </summary>
        private async Task<NodeId> ResolveCalculatorNodeAsync(
            params string[] tail)
        {
            ushort nsIndex = (ushort)fixture.Session.NamespaceUris
                .GetIndex(kCalcNamespaceUri);
            await Assert.That(nsIndex).IsGreaterThan((ushort)0);

            var elements = new List<RelativePathElement>
            {
                new()
                {
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsInverse = false,
                    IncludeSubtypes = true,
                    TargetName = new QualifiedName("Calculator", nsIndex)
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
    /// hosting the source-generated <c>CalcNodeManagerFactory</c> and
    /// connects an anonymous client session to it.
    /// </summary>
    public sealed class CalculatorAotFixture : IAsyncInitializer, IAsyncDisposable
    {
        public AotServerFixture<CalculatorTestServer> ServerFixture { get; private set; }
        public Client.ISession Session { get; private set; }
        public string ServerUrl { get; private set; }
        public ITelemetryContext Telemetry { get; private set; }

        public async Task InitializeAsync()
        {
            Telemetry = DefaultTelemetry.Create(builder =>
                builder.SetMinimumLevel(LogLevel.Warning));

            ServerFixture = new AotServerFixture<CalculatorTestServer>(
                t => new CalculatorTestServer(t), Telemetry)
            {
                AutoAccept = true,
                SecurityNone = true
            };
            await ServerFixture.LoadConfigurationAsync(
                Path.Combine(Directory.GetCurrentDirectory(), "calc-pki"))
                .ConfigureAwait(false);
            await ServerFixture.StartAsync().ConfigureAwait(false);

            ServerUrl = $"opc.tcp://localhost:{ServerFixture.Port}/" +
                nameof(CalculatorTestServer);

            m_pkiRoot = Path.Combine(
                Path.GetTempPath(), "OpcUaAotTests", "calc-client-pki");

            m_clientConfiguration = new ApplicationConfiguration(Telemetry)
            {
                ApplicationName = "CalculatorAotTestClient",
                ApplicationUri = "urn:localhost:OPCFoundation:CalculatorAotTestClient",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(m_pkiRoot, "own"),
                        SubjectName = "CN=CalculatorAotTestClient, O=OPC Foundation"
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
            m_clientConfiguration.CertificateManager ??= CertificateManagerFactory.Create(
                m_clientConfiguration.SecurityConfiguration, Telemetry);
            m_clientConfiguration.CertificateManager.AcceptError = static (cert, err) => true;

            EndpointDescription endpointDescription =
                await CoreClientUtils.SelectEndpointAsync(
                    m_clientConfiguration, ServerUrl, useSecurity: false,
                    Telemetry, CancellationToken.None).ConfigureAwait(false);
            var configuredEndpoint = new ConfiguredEndpoint(
                null, endpointDescription,
                EndpointConfiguration.Create(m_clientConfiguration));

            var sessionFactory = new DefaultSessionFactory(Telemetry);
#pragma warning disable CA2000 // Dispose objects before losing scope
            Session = await sessionFactory.CreateAsync(
                m_clientConfiguration,
                configuredEndpoint,
                updateBeforeConnect: false,
                sessionName: "CalculatorAotTest",
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
                await Session.DisposeAsync().ConfigureAwait(false);
                Session = null;
            }
            if (m_clientConfiguration?.CertificateManager is IDisposable disposableManager)
            {
                disposableManager.Dispose();
                m_clientConfiguration.CertificateManager = null;
            }
            if (ServerFixture != null)
            {
                await ServerFixture.StopAsync().ConfigureAwait(false);
                ServerFixture = null;
            }
            GC.SuppressFinalize(this);
        }

        private ApplicationConfiguration m_clientConfiguration;
        private string m_pkiRoot;
    }

    /// <summary>
    /// Public <see cref="StandardServer"/> subclass that registers the
    /// source-generated <see cref="Calc.CalcNodeManagerFactory"/>.
    /// Mirrors the implicit hosting that <c>AddNodeManager</c> sets up
    /// in MinimalCalcServer's <c>Program.cs</c> but is exposed as
    /// <c>public</c> so <see cref="AotServerFixture{T}"/> can host it.
    /// </summary>
    public sealed class CalculatorTestServer : StandardServer
    {
        public CalculatorTestServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        protected override void OnServerStarting(ApplicationConfiguration configuration)
        {
            base.OnServerStarting(configuration);
            AddNodeManager(new calcsample::Calc.CalcNodeManagerFactory());
        }
    }
}
