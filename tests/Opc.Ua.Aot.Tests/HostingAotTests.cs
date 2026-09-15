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

using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Client;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT smoke tests for the unified <c>services.AddOpcUa()</c> DI surface
    /// exposed by Opc.Ua.Core, Opc.Ua.Server and Opc.Ua.Client.
    /// </summary>
    /// <remarks>
    /// These tests must only exercise the <c>Action&lt;TOptions&gt;</c>
    /// overloads of <see cref="OpcUaServerBuilderExtensions"/> and
    /// <see cref="OpcUaClientBuilderExtensions"/>. The
    /// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
    /// overloads are decorated with <c>RequiresUnreferencedCode</c> /
    /// <c>RequiresDynamicCode</c> and are not AOT safe; if a future
    /// refactor causes any of those overloads to leak into this test
    /// the AOT publish will produce IL2026/IL3050 warnings attributable
    /// to this file.
    /// </remarks>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class HostingAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task AddOpcUaCanRegisterRootServicesWithoutAotWarningAsync()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            await Assert.That(builder).IsNotNull();
            await Assert.That(builder.Services).IsSameReferenceAs(services);

            using ServiceProvider sp = services.BuildServiceProvider();

            ITelemetryContext telemetry = sp.GetService<ITelemetryContext>();
            await Assert.That(telemetry).IsNotNull();
        }

        [Test]
        public async Task AddServerActionOverloadIsAotSafeAsync()
        {
            const string expectedName = "AotSmokeServer";
            const string expectedUri = "urn:localhost:OPCFoundation:AotSmokeServer";

            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o =>
                {
                    o.ApplicationName = expectedName;
                    o.ApplicationUri = expectedUri;
                    o.AutoAcceptUntrustedCertificates = true;
                });

            using ServiceProvider sp = services.BuildServiceProvider();

            IOptions<OpcUaServerOptions> options =
                sp.GetService<IOptions<OpcUaServerOptions>>();
            await Assert.That(options).IsNotNull();

            OpcUaServerOptions resolved = options.Value;
            await Assert.That(resolved).IsNotNull();
            await Assert.That(resolved.ApplicationName).IsEqualTo(expectedName);
            await Assert.That(resolved.ApplicationUri).IsEqualTo(expectedUri);
            await Assert.That(resolved.AutoAcceptUntrustedCertificates).IsTrue();

            INodeManagerLifecycle lifecycle =
                sp.GetService<INodeManagerLifecycle>();
            await Assert.That(lifecycle).IsNotNull();

            // The hosted service is registered via the AOT-safe
            // AddHostedService<T>() overload. Resolve it via the
            // ServiceDescriptor collection rather than constructing it,
            // because the production constructor requires an
            // IApplicationInstanceFactory which is contributed by a
            // separate Opc.Ua.Configuration registration that callers
            // wire up explicitly.
            int hostedCount = services.Count(s =>
                s.ServiceType == typeof(IHostedService) &&
                s.ImplementationType?.Name == "OpcUaServerHostedService");
            await Assert.That(hostedCount).IsEqualTo(1);
        }

        [Test]
        public async Task AddClientActionOverloadIsAotSafeAsync()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                b => b.SetMinimumLevel(LogLevel.Warning));
            ApplicationConfiguration configuration = CreateMinimalClientConfiguration(telemetry);

            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddClient(o => o.Configuration = configuration);

            using ServiceProvider sp = services.BuildServiceProvider();

            OpcUaClientOptions options = sp.GetService<OpcUaClientOptions>();
            await Assert.That(options).IsNotNull();
            await Assert.That(options.Configuration).IsSameReferenceAs(configuration);

            ISessionFactory sessionFactory = sp.GetService<ISessionFactory>();
            await Assert.That(sessionFactory).IsNotNull();

            ManagedSessionFactory managedFactory = sp.GetService<ManagedSessionFactory>();
            await Assert.That(managedFactory).IsNotNull();

            Func<CancellationToken, Task<ManagedSession>> sessionAccessor =
                sp.GetService<Func<CancellationToken, Task<ManagedSession>>>();
            await Assert.That(sessionAccessor).IsNotNull();

            ReverseConnectManager reverseConnectManager =
                sp.GetService<ReverseConnectManager>();
            await Assert.That(reverseConnectManager).IsNotNull();

            IReverseConnectConfigurationProvider reverseConnectProvider =
                sp.GetService<IReverseConnectConfigurationProvider>();
            await Assert.That(reverseConnectProvider).IsNotNull();

            int reverseConnectHostedCount = services.Count(s =>
                s.ServiceType == typeof(IHostedService) &&
                s.ImplementationType?.Name == "ReverseConnectManagerHostedService");
            await Assert.That(reverseConnectHostedCount).IsEqualTo(1);
        }

        [Test]
        public async Task CombinedServerAndClientRegistrationIsAotSafeAsync()
        {
            const string serverName = "AotSmokeCombinedServer";

            ITelemetryContext telemetry = DefaultTelemetry.Create(
                b => b.SetMinimumLevel(LogLevel.Warning));
            ApplicationConfiguration clientConfiguration =
                CreateMinimalClientConfiguration(telemetry);

            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o =>
                {
                    o.ApplicationName = serverName;
                    o.AutoAcceptUntrustedCertificates = true;
                })
                .Services.AddOpcUa()
                .AddClient(o => o.Configuration = clientConfiguration);

            using ServiceProvider sp = services.BuildServiceProvider();

            OpcUaServerOptions serverOptions =
                sp.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
            await Assert.That(serverOptions.ApplicationName).IsEqualTo(serverName);

            OpcUaClientOptions clientOptions = sp.GetService<OpcUaClientOptions>();
            await Assert.That(clientOptions).IsNotNull();
            await Assert.That(clientOptions.Configuration)
                .IsSameReferenceAs(clientConfiguration);

            int hostedCount = services.Count(s =>
                s.ServiceType == typeof(IHostedService) &&
                s.ImplementationType?.Name == "OpcUaServerHostedService");
            await Assert.That(hostedCount).IsEqualTo(1);

            Func<CancellationToken, Task<ManagedSession>> sessionAccessor =
                sp.GetService<Func<CancellationToken, Task<ManagedSession>>>();
            await Assert.That(sessionAccessor).IsNotNull();
        }

        [Test]
        public async Task AddRuntimeNodeSetStreamRegistrationIsAotSafeAsync()
        {
            string namespaceUri = fixture.ServerFixture.Server.CurrentInstance
                .NamespaceUris.GetString(1);
            string nodeSetXml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">" +
                "<NamespaceUris><Uri>" + namespaceUri + "</Uri></NamespaceUris>" +
                "<Models><Model ModelUri=\"" + namespaceUri + "\" /></Models>" +
                "<UAObject NodeId=\"ns=1;i=1\" BrowseName=\"1:Root\">" +
                "<DisplayName>Root</DisplayName></UAObject>" +
                "</UANodeSet>";

            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o =>
                {
                    o.ApplicationName = "AotRuntimeNodeSetServer";
                    o.AutoAcceptUntrustedCertificates = true;
                })
                .AddRuntimeNodeSet(options =>
                {
                    options.Sources =
                    [
                        RuntimeNodeSetSource.FromStream(
                            "AOT runtime NodeSet",
                            _ => new ValueTask<Stream>(
                                new MemoryStream(Encoding.UTF8.GetBytes(nodeSetXml))),
                            [namespaceUri])
                    ];
                });

            using ServiceProvider sp = services.BuildServiceProvider();
            IAsyncNodeManagerFactory factory = sp.GetService<IAsyncNodeManagerFactory>();

            await Assert.That(factory).IsNotNull();
            await Assert.That(factory.NamespacesUris.Count).IsEqualTo(1);
            await Assert.That(factory.NamespacesUris[0]).IsEqualTo(namespaceUri);

            IAsyncNodeManager manager = await factory.CreateAsync(
                fixture.ServerFixture.Server.CurrentInstance,
                fixture.ServerFixture.Config,
                CancellationToken.None).ConfigureAwait(false);

            try
            {
                await manager.CreateAddressSpaceAsync(
                    new Dictionary<NodeId, IList<IReference>>(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                (manager as IDisposable)?.Dispose();
            }
        }

        private static ApplicationConfiguration CreateMinimalClientConfiguration(
            ITelemetryContext telemetry)
        {
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "AotSmokeClient",
                ApplicationUri = "urn:localhost:OPCFoundation:AotSmokeClient",
                ApplicationType = ApplicationType.Client,
                ClientConfiguration = new ClientConfiguration()
            };
        }

        [Test]
        public async Task HostedCompositionRunsGenericAndDelegateExtensionsInAotAsync()
        {
            var observations = new AotCompositionObservations();
            var token = new UserNameIdentityTokenHandler(
                "aot-composition-user", Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N")));
            var expectedIdentity = new UserIdentity(token);
            var usernameAuthenticator = new UserNamePasswordAuthenticator((handler, ct) =>
            {
                observations.AuthenticatedToken = handler;
                observations.AuthenticationCalls++;
                return new ValueTask<IUserIdentity>(expectedIdentity);
            });
            using var aliases = new Server.AliasNames.InMemoryAliasNameStore(
                [new Server.AliasNames.AliasNameCategoryDescriptor(
                    ObjectIds.TagVariables, QualifiedName.From(BrowseNames.TagVariables),
                    Server.AliasNames.AliasNameCapabilities.All)]);
            using var aliasRegistry = new Server.AliasNames.AliasNameStoreRegistry();
            aliases.Seed(ObjectIds.TagVariables, "AotBuildName",
                VariableIds.Server_ServerStatus_BuildInfo_ProductName, null, ReferenceTypeIds.AliasFor);
            var host = new AotCompositionHost(fixture.Telemetry, observations, builder =>
            {
                builder.ConfigureServerProperties(properties =>
                {
                    properties.ProductName = "AOT Composed Product";
                    properties.ManufacturerName = "AOT Composition";
                    properties.SoftwareVersion = "4.5.6";
                });
                builder.ConfigureResources(resources =>
                {
                    observations.DefaultStatusText = resources.Translate(
                        ["en-US"], new ServiceResult(StatusCodes.BadNodeIdUnknown)).LocalizedText.Text;
                    resources.Add("AotComposition.Greeting", "en-US", "Hello from AOT");
                });
                builder.ConfigureResources((services, resources) =>
                {
                    observations.ResourceDependency = services.GetRequiredService<AotCompositionObservations>();
                    resources.Add("AotComposition.Greeting", "de-DE", "Hallo aus AOT");
                    resources.Add("AotComposition.Fallback", "de-DE", "Hallo {0}");
                });
                builder.AddIdentityAuthenticator<AnonymousAuthenticator>();
                builder.AddIdentityAuthenticator(usernameAuthenticator);
                builder.AddIdentityAuthenticator((services, validator) =>
                {
                    observations.AuthenticatorDependency = services
                        .GetRequiredService<AotCompositionObservations>();
                    observations.CertificateValidator = validator;
                    observations.AuthenticatorFactoryCalls++;
                    return new AnonymousAuthenticator();
                });
                builder.AddNodeManager<AotCompositionNodeManagerFactory>();
                builder.AddNodeManager(new AotValueNodeManagerFactory("urn:aot:composition:instance", 23));
                builder.AddNodeManagers((services, configuration) =>
                {
                    observations.ConfigurationDependency = services
                        .GetRequiredService<AotCompositionObservations>();
                    observations.Configuration = configuration;
                    observations.ConfigurationFactoryCalls++;
                    return [new AotValueNodeManagerFactory("urn:aot:composition:deferred", 37)];
                });
                builder.AddNodeManagers((_, _) =>
                {
                    observations.EmptyFactoryCalls++;
                    return [];
                });
                builder.AddAliasNameStoreRegistry(aliasRegistry);
                builder.Services.AddSingleton<IServerPreStartupTask>(
                    new AotAliasInitializationTask(aliasRegistry, aliases, observations));
                builder.ConfigureAliasNames(options => options.MaterializeAliasNodes = true);
                builder.AddStartupTask<AotCompositionStartupTask>();
                builder.AddStartupTask<AotCompositionStartupTask>();
                builder.AddStartupTask(async (services, context, ct) =>
                {
                    observations.DelegateDependency = services.GetRequiredService<AotCompositionObservations>();
                    observations.DelegateContext = context;
                    observations.DelegateCancellationCanBeCanceled = ct.CanBeCanceled;
                    HistoryServerCapabilitiesState history = await context
                        .FindNodeManagers<IDiagnosticsNodeManager>().Single()
                        .GetDefaultHistoryCapabilitiesAsync(ct).ConfigureAwait(false);
                    observations.HistoryLimitAtDelegate = history.MaxReturnDataValues.Value;
                    observations.Events.Add("delegate");
                });
            });
            await using var cleanup = host.ConfigureAwait(false);

            await Assert.That(observations.FactoryConstructions).IsEqualTo(0);
            await Assert.That(observations.AuthenticatorFactoryCalls).IsEqualTo(0);
            await Assert.That(aliasRegistry.Stores.Count).IsEqualTo(0);
            await host.StartAsync().ConfigureAwait(false);

            await Assert.That(host.Context.CurrentState).IsEqualTo(ServerState.Running);
            BuildInfo info = ReadAotCompositionValue(host.Context, VariableIds.Server_ServerStatus_BuildInfo)
                .WrappedValue.GetStructure<BuildInfo>();
            await Assert.That(info.ProductName).IsEqualTo("AOT Composed Product");
            await Assert.That(info.ProductUri).IsEqualTo("urn:aot:composition:product");
            await Assert.That(info.ManufacturerName).IsEqualTo("AOT Composition");
            await Assert.That(info.SoftwareVersion).IsEqualTo("4.5.6");
            await Assert.That(info.BuildNumber).IsEqualTo(Utils.GetAssemblyBuildNumber());
            await Assert.That(observations.Configuration.ApplicationName).IsEqualTo("AotComposedServer");
            await Assert.That(observations.FactoryConstructions).IsEqualTo(1);
            await Assert.That(observations.FactorySawConfiguration).IsTrue();
            await Assert.That(observations.ConfigurationFactoryCalls).IsEqualTo(1);
            await Assert.That(observations.EmptyFactoryCalls).IsEqualTo(1);
            await Assert.That(observations.ConfigurationDependency).IsSameReferenceAs(observations);
            await Assert.That(ReadAotCompositionMarker(host.Context, "urn:aot:composition:generic")).IsEqualTo(19);
            await Assert.That(ReadAotCompositionMarker(host.Context, "urn:aot:composition:instance")).IsEqualTo(23);
            await Assert.That(ReadAotCompositionMarker(host.Context, "urn:aot:composition:deferred")).IsEqualTo(37);
            await Assert.That(observations.StartupCalls).IsEqualTo(1);
            await Assert.That(observations.AliasInitializationCalls).IsEqualTo(1);
            await Assert.That(observations.Events.SequenceEqual(["generic", "delegate"])).IsTrue();
            await Assert.That(observations.DelegateDependency).IsSameReferenceAs(observations);
            await Assert.That(observations.DelegateContext).IsSameReferenceAs(host.Context);
            await Assert.That(observations.DelegateCancellationCanBeCanceled).IsTrue();
            await Assert.That(observations.HistoryLimitAtDelegate).IsEqualTo(67u);
            await Assert.That(observations.ResourceDependency).IsSameReferenceAs(observations);
            await Assert.That(observations.DefaultStatusText).IsEqualTo("BadNodeIdUnknown");
            LocalizedText greeting = observations.Server.ResourceManager.Translate(
                ["de-DE"], "AotComposition.Greeting", "fallback");
            await Assert.That(greeting.Text).IsEqualTo("Hallo aus AOT");
            await Assert.That(greeting.Locale).IsEqualTo("de-DE");
            await Assert.That(observations.Server.ResourceManager.Translate(
                ["en-US"], "AotComposition.Greeting", "fallback").Text).IsEqualTo("Hello from AOT");
            var fallback = new LocalizedText("AotComposition.Fallback", "en-US", "Hello {0}", "AOT");
            LocalizedText german = observations.Server.ResourceManager.Translate(["de-DE"], fallback);
            LocalizedText english = observations.Server.ResourceManager.Translate(["en-US"], german);
            await Assert.That(german.Locale).IsEqualTo("de-DE");
            await Assert.That(german.Text).IsEqualTo("Hallo AOT");
            await Assert.That(german.TranslationInfo).IsEqualTo(fallback.TranslationInfo);
            await Assert.That(english.Locale).IsEqualTo("en-US");
            await Assert.That(english.Text).IsEqualTo("Hello AOT");
            await Assert.That(observations.AuthenticatorFactoryCalls).IsEqualTo(1);
            await Assert.That(observations.AuthenticatorDependency).IsSameReferenceAs(observations);
            await Assert.That(observations.CertificateValidator)
                .IsSameReferenceAs(observations.Configuration.CertificateManager);

            var authenticationContext = new Identity.AuthenticationContext(
                token, new UserTokenPolicy { TokenType = UserTokenType.UserName },
                new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                host.Context.MessageContext);
            Identity.AuthenticationResult identity = await observations.Server.IdentityRegistry
                .AuthenticateAsync(authenticationContext, CancellationToken.None).ConfigureAwait(false);
            await Assert.That(identity.Outcome).IsEqualTo(Identity.AuthenticationOutcome.Accepted);
            await Assert.That(identity.Identity).IsSameReferenceAs(expectedIdentity);
            await Assert.That(observations.AuthenticatedToken).IsSameReferenceAs(token);
            await Assert.That(observations.AuthenticationCalls).IsEqualTo(1);
            await Assert.That(observations.Configuration.ServerConfiguration.UserTokenPolicies
                .ToArray().Select(policy => policy.TokenType).SequenceEqual([UserTokenType.Anonymous])).IsTrue();

            AliasNameCategoryState category = host.Context
                .FindPredefinedNode<AliasNameCategoryState>(ObjectIds.TagVariables);
            await Assert.That(category.FindAliasVerbose).IsNotNull();
            await Assert.That(category.AddAliasesToCategory).IsNotNull();
            var argumentErrors = new List<ServiceResult>();
            var output = new List<Variant>();
            ServiceResult result = await category.FindAlias.CallAsync(
                host.Context.DefaultSystemContext, category.NodeId,
                [new Variant("Aot%"), new Variant(NodeId.Null)],
                argumentErrors, output, CancellationToken.None).ConfigureAwait(false);
            await Assert.That(ServiceResult.IsGood(result)).IsTrue();
            await Assert.That(output.Count).IsEqualTo(1);
            await Assert.That(output[0].TryGetStructure(out ArrayOf<AliasNameDataType> found)).IsTrue();
            await Assert.That(found.Count).IsEqualTo(1);
            await Assert.That(found[0].AliasName.Name).IsEqualTo("AotBuildName");

            await host.StopAsync().ConfigureAwait(false);
            await Assert.That(observations.GenericManager.Disposed).IsTrue();
            await Assert.That(host.Listener.CloseCount > 0).IsTrue();
        }

        [Test]
        public async Task HostedStartupDelegateFailureCleansUpInAotAsync()
        {
            var observations = new AotCompositionObservations();
            var expected = new InvalidOperationException("aot-composition-startup-failure");
            int laterCalls = 0;
            var host = new AotCompositionHost(fixture.Telemetry, observations, builder =>
            {
                builder.AddNodeManager<AotCompositionNodeManagerFactory>();
                builder.AddStartupTask((_, context, _) =>
                {
                    observations.DelegateContext = context;
                    observations.MarkerAtFailure = ReadAotCompositionMarker(
                        context, "urn:aot:composition:generic");
                    throw expected;
                });
                builder.AddStartupTask((_, _, _) =>
                {
                    laterCalls++;
                    return default;
                });
            });
            await using var cleanup = host.ConfigureAwait(false);
            Exception actual = null;
            try
            {
                await host.StartAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                actual = exception;
            }

            await Assert.That(actual).IsSameReferenceAs(expected);
            await Assert.That(host.ExecuteTask.IsFaulted).IsTrue();
            await Assert.That(observations.MarkerAtFailure).IsEqualTo(19);
            await Assert.That(laterCalls).IsEqualTo(0);
            await Assert.That(observations.GenericManager.Disposed).IsTrue();
            await Assert.That(host.Listener.CloseCount > 0).IsTrue();
        }

        private static DataValue ReadAotCompositionValue(IServerContext context, NodeId nodeId)
        {
            BaseVariableState node = context.FindPredefinedNode<BaseVariableState>(nodeId)
                ?? throw new InvalidOperationException("The composed server did not publish the expected variable.");
            return ReadAotCompositionValue(context, node);
        }

        private static DataValue ReadAotCompositionValue(IServerContext context, BaseVariableState node)
        {
            var value = new DataValue();
            ServiceResult result = node.ReadAttribute(
                context.DefaultSystemContext, Attributes.Value, NumericRange.Null, QualifiedName.Null, ref value);
            if (ServiceResult.IsBad(result) || StatusCode.IsBad(value.StatusCode))
            {
                throw new InvalidOperationException("The composed server could not read its published variable.");
            }
            return value;
        }

        private static int ReadAotCompositionMarker(IServerContext context, string namespaceUri)
        {
            int namespaceIndex = context.DefaultSystemContext.NamespaceUris.GetIndex(namespaceUri);
            if (namespaceIndex <= 0)
            {
                throw new InvalidOperationException("The composed node manager namespace was not registered.");
            }
            NodeId nodeId = new("Marker", checked((ushort)namespaceIndex));
            BaseVariableState node = context.FindNodeManagers<AotValueNodeManager>()
                .Select(manager => manager.FindPredefinedNode<BaseVariableState>(nodeId))
                .Single(value => value != null);
            return ReadAotCompositionValue(context, node).WrappedValue.GetInt32();
        }

        public sealed class AotCompositionObservations
        {
            public bool ConfigurationReady { get; set; }
            public bool FactorySawConfiguration { get; set; }
            public int FactoryConstructions { get; set; }
            public int ConfigurationFactoryCalls { get; set; }
            public int EmptyFactoryCalls { get; set; }
            public int AuthenticatorFactoryCalls { get; set; }
            public int AuthenticationCalls { get; set; }
            public int StartupCalls { get; set; }
            public int AliasInitializationCalls { get; set; }
            public int MarkerAtFailure { get; set; }
            public uint HistoryLimitAtDelegate { get; set; }
            public bool DelegateCancellationCanBeCanceled { get; set; }
            public string DefaultStatusText { get; set; }
            public IServerInternal Server { get; set; }
            public IServerContext DelegateContext { get; set; }
            public ApplicationConfiguration Configuration { get; set; }
            public ICertificateValidatorEx CertificateValidator { get; set; }
            public UserNameIdentityTokenHandler AuthenticatedToken { get; set; }
            public AotCompositionObservations ConfigurationDependency { get; set; }
            public AotCompositionObservations AuthenticatorDependency { get; set; }
            public AotCompositionObservations ResourceDependency { get; set; }
            public AotCompositionObservations DelegateDependency { get; set; }
            public AotValueNodeManager GenericManager { get; set; }
            public List<string> Events { get; } = [];
        }

        private sealed class AotAliasInitializationTask(
            Server.AliasNames.IAliasNameStoreRegistry registry,
            Server.AliasNames.IAliasNameStore store,
            AotCompositionObservations observations) : IServerPreStartupTask
        {
            public ValueTask OnServerStartingAsync(
                IServerContext server,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                registry.Register(store);
                observations.AliasInitializationCalls++;
                return default;
            }
        }

        public sealed class AotCompositionStartupTask(AotCompositionObservations observations) : IServerStartupTask
        {
            public async ValueTask OnServerStartedAsync(
                IServerContext server,
                CancellationToken cancellationToken = default)
            {
                observations.StartupCalls++;
                HistoryServerCapabilitiesState history = await server
                    .FindNodeManagers<IDiagnosticsNodeManager>().Single()
                    .GetDefaultHistoryCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
                history.MaxReturnDataValues.Value = 67;
                history.MaxReturnEventValues.Value = 13;
                observations.Events.Add("generic");
            }
        }

        public sealed class AotCompositionNodeManagerFactory : IAsyncNodeManagerFactory
        {
            public AotCompositionNodeManagerFactory(AotCompositionObservations observations)
            {
                m_observations = observations;
                observations.FactoryConstructions++;
                observations.FactorySawConfiguration = observations.ConfigurationReady;
            }

            public ArrayOf<string> NamespacesUris => ["urn:aot:composition:generic"];

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_observations.Server = server;
                m_observations.GenericManager = new AotValueNodeManager(
                    server, configuration, NamespacesUris[0], 19);
                return new ValueTask<IAsyncNodeManager>(m_observations.GenericManager);
            }

            private readonly AotCompositionObservations m_observations;
        }

        private sealed class AotValueNodeManagerFactory(string namespaceUri, int marker) : IAsyncNodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris => [namespaceUri];
            public AotValueNodeManager Manager { get; private set; }

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Manager = new AotValueNodeManager(server, configuration, namespaceUri, marker);
                return new ValueTask<IAsyncNodeManager>(Manager);
            }
        }

        public sealed class AotValueNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            string namespaceUri,
            int marker) : AsyncCustomNodeManager(server, configuration, namespaceUri)
        {
            public bool Disposed { get; private set; }

            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                var variable = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("Marker", NamespaceIndex),
                    BrowseName = new QualifiedName("Marker", NamespaceIndex),
                    DisplayName = LocalizedText.From("AOT Marker"),
                    TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    Value = marker
                };
                await AddPredefinedNodeAsync(SystemContext, variable, cancellationToken).ConfigureAwait(false);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Disposed = true;
                }
                base.Dispose(disposing);
            }
        }

        private sealed class AotCompositionHost : IAsyncDisposable
        {
            public AotCompositionHost(
                ITelemetryContext telemetry,
                AotCompositionObservations observations,
                Action<IOpcUaServerBuilder> compose)
            {
                m_root = Path.Combine(Path.GetTempPath(), "uaca", Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(m_root);
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddSingleton(telemetry);
                services.AddSingleton(observations);
                var bindings = new Bindings.DefaultTransportBindingRegistry();
                bindings.RegisterListenerFactory(new AotCompositionTransport(Listener));
                services.AddSingleton<Bindings.ITransportBindingRegistry>(bindings);
                IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(options =>
                {
                    options.ApplicationName = "AotComposedServer";
                    options.ApplicationUri = "urn:localhost:AotComposedServer";
                    options.ProductUri = "urn:aot:composition:product";
                    options.PkiRoot = m_root;
                    options.IncludeSignAndEncryptPolicies = false;
                    options.IncludeUnsecurePolicyNone = true;
                    options.AutoAcceptUntrustedCertificates = true;
                    options.EndpointUrls.Add("opc.tcp://localhost:0/AotComposedServer");
                    options.ConfigureBuilder = configuration =>
                    {
                        configuration.SetShutdownDelay(0);
                        observations.ConfigurationReady = true;
                    };
                });
                compose(builder);
                builder.AddStartupTask((_, context, _) =>
                {
                    Context = context;
                    m_ready.TrySetResult(true);
                    return default;
                });
                m_provider = services.BuildServiceProvider();
                _ = HostedService;
            }

            public IServerContext Context { get; private set; }
            public AotCompositionListener Listener { get; } = new();
            public Task ExecuteTask => HostedService.ExecuteTask;
            private BackgroundService HostedService => m_provider.GetServices<IHostedService>()
                .OfType<BackgroundService>().Single();

            public async Task StartAsync()
            {
                await HostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);
                Task completed = await Task.WhenAny(m_ready.Task, ExecuteTask)
                    .WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
                if (completed != m_ready.Task)
                {
                    throw new InvalidOperationException("The AOT composition host stopped before startup completed.");
                }
            }

            public async Task StopAsync()
            {
                if (m_stopped)
                {
                    return;
                }
                m_stopped = true;
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await HostedService.StopAsync(cancellation.Token).ConfigureAwait(false);
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await StopAsync().ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        await m_provider.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        if (Directory.Exists(m_root))
                        {
                            Directory.Delete(m_root, recursive: true);
                        }
                    }
                }
            }

            private readonly string m_root;
            private readonly ServiceProvider m_provider;
            private readonly TaskCompletionSource<bool> m_ready =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private bool m_stopped;
        }

        private sealed class AotCompositionTransport(AotCompositionListener listener) : Bindings.TcpServiceHost
        {
            public override string UriScheme => Utils.UriSchemeOpcTcp;

            public override ITransportListener Create(ITelemetryContext telemetry)
            {
                return listener;
            }
        }

        private sealed class AotCompositionListener : ITransportListener
        {
            public string ListenerId => "aot-composition";
            public string UriScheme => Utils.UriSchemeOpcTcp;
            public int CloseCount { get; private set; }

            public event ConnectionWaitingHandlerAsync ConnectionWaiting
            {
                add { }
                remove { }
            }

            public event EventHandler<ConnectionStatusEventArgs> ConnectionStatusChanged
            {
                add { }
                remove { }
            }

            public ValueTask OpenAsync(
                Uri baseAddress,
                TransportListenerSettings settings,
                ITransportListenerCallback callback,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return default;
            }

            public ValueTask CloseAsync(CancellationToken ct = default)
            {
                CloseCount++;
                return default;
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }

            public void CertificateUpdate(ICertificateValidatorEx validator, ICertificateRegistry serverCertificates)
            {
            }

            public void CreateReverseConnection(Uri url, int timeout)
            {
                throw new InvalidOperationException("The AOT composition fixture must not create network connections.");
            }

            public void UpdateChannelLastActiveTime(string globalChannelId)
            {
            }
        }
    }
}
