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

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Hosting
{
    [TestFixture]
    [Category("Hosting")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class ServerFluentApiHostingTests
    {
        [Test]
        public void AddServerWithCustomServerCreatesConfiguredServerType()
        {
            using ServiceProvider sp = CreateServerBuilder<CustomServer>().Services.BuildServiceProvider();

            StandardServer server = sp.GetRequiredService<IOpcUaServerFactory>()
                .CreateServer(NUnitTelemetryContext.Create(isServer: true), TimeProvider.System);

            using (server)
            {
                Assert.That(server, Is.TypeOf<CustomServer>());
            }
        }

        [Test]
        public async Task AddServerWithCustomServerStartsCustomServerAsync()
        {
            ObservedHostedServer.StartedType = null;
            await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                services => services.AddOpcUa().AddServer<ObservedHostedServer>(
                    o => ConfigureHostedOptions(o, "CustomHostedServer")));

            Assert.That(
                await WaitForAsync(
                    () => ObservedHostedServer.StartedType == typeof(ObservedHostedServer),
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                Is.True);
        }

        [Test]
        public void AddServerUsesDependencyInjectionAwareDefaultFactory()
        {
            using ServiceProvider sp = CreateServerBuilder().Services.BuildServiceProvider();

            StandardServer server = sp.GetRequiredService<IOpcUaServerFactory>()
                .CreateServer(NUnitTelemetryContext.Create(isServer: true), TimeProvider.System);

            using (server)
            {
                Assert.That(server.GetType().Name, Does.Contain("DependencyInjection"));
            }
        }

        [Test]
        public void OpcUaServerFactoryIsResolvableAndOverridable()
        {
            var factory = new StubServerFactory();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IOpcUaServerFactory>(factory);
            services.AddOpcUa().AddServer(o =>
            {
                o.ApplicationName = "OverriddenFactory";
                o.ApplicationUri = "urn:localhost:OverriddenFactory";
                o.ProductUri = "urn:localhost:OverriddenFactory:product";
            });
            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<IOpcUaServerFactory>(), Is.SameAs(factory));
            using StandardServer server = sp.GetRequiredService<IOpcUaServerFactory>()
                .CreateServer(NUnitTelemetryContext.Create(isServer: true), TimeProvider.System);
            Assert.That(server, Is.TypeOf<CustomServer>());
        }

        [Test]
        public void AddDurableSubscriptionsFeedsStandardServerHooks()
        {
            Mock<ISubscriptionStore> store = new(MockBehavior.Strict);
            Mock<IMonitoredItemQueueFactory> queueFactory = new(MockBehavior.Strict);
            using ServiceProvider sp = CreateServerBuilder()
                .AddDurableSubscriptions(store.Object, queueFactory.Object)
                .Services.BuildServiceProvider();

            using StandardServer server = CreateServer(sp);

            Assert.That(InvokeProtected(server, "CreateSubscriptionStore"), Is.SameAs(store.Object));
            Assert.That(InvokeProtected(server, "CreateMonitoredItemQueueFactory"), Is.SameAs(queueFactory.Object));
        }

        [Test]
        public void AddSessionAndSubscriptionManagersFeedStandardServerHooks()
        {
            Mock<ISessionManager> sessionManager = new(MockBehavior.Strict);
            Mock<ISubscriptionManager> subscriptionManager = new(MockBehavior.Strict);
            using ServiceProvider sp = CreateServerBuilder()
                .AddSessionManager((_, _, _) => sessionManager.Object)
                .AddSubscriptionManager((_, _, _) => subscriptionManager.Object)
                .Services.BuildServiceProvider();

            using StandardServer server = CreateServer(sp);

            Assert.That(InvokeProtected(server, "CreateSessionManager"), Is.SameAs(sessionManager.Object));
            Assert.That(InvokeProtected(server, "CreateSubscriptionManager"), Is.SameAs(subscriptionManager.Object));
        }

        [Test]
        public void AddHistorianAndAliasNamesRegisterAtNodeManagerStarted()
        {
            Mock<IHistorianProvider> historian = new(MockBehavior.Strict);
            Mock<IAliasNameStore> aliasStore = new(MockBehavior.Strict);
            aliasStore.SetupGet(s => s.RootCategories).Returns([]);
            using ServiceProvider sp = CreateServerBuilder()
                .AddHistorian(historian.Object)
                .AddAliasNameStore(aliasStore.Object)
                .Services.BuildServiceProvider();
            using StandardServer server = CreateServer(sp);
            var historianRegistry = new HistorianProviderRegistry(new NamespaceTable());
            var aliasRegistry = new AliasNameStoreRegistry();
            Mock<IServerInternal> serverInternal = new(MockBehavior.Strict);
            serverInternal.As<IHistorianRegistryProvider>()
                .SetupGet(s => s.HistorianRegistry)
                .Returns(historianRegistry);
            serverInternal.As<IAliasNameStoreRegistryProvider>()
                .SetupGet(s => s.AliasNameStoreRegistry)
                .Returns(aliasRegistry);

            InvokeProtected(server, "OnNodeManagerStarted", serverInternal.Object);

            Assert.That(historianRegistry.Providers, Does.Contain(historian.Object));
            Assert.That(aliasRegistry.Stores, Does.Contain(aliasStore.Object));
        }

        [Test]
        public void AddAliasNameStoreRegistryCopiesStoresAtNodeManagerStarted()
        {
            Mock<IAliasNameStore> aliasStore = new(MockBehavior.Strict);
            aliasStore.SetupGet(s => s.RootCategories).Returns([]);
            var sourceRegistry = new AliasNameStoreRegistry();
            sourceRegistry.Register(aliasStore.Object);
            using ServiceProvider sp = CreateServerBuilder()
                .AddAliasNameStoreRegistry(sourceRegistry)
                .Services.BuildServiceProvider();
            using StandardServer server = CreateServer(sp);
            var targetRegistry = new AliasNameStoreRegistry();
            Mock<IServerInternal> serverInternal = new(MockBehavior.Strict);
            serverInternal.As<IHistorianRegistryProvider>()
                .SetupGet(s => s.HistorianRegistry)
                .Returns(new HistorianProviderRegistry(new NamespaceTable()));
            serverInternal.As<IAliasNameStoreRegistryProvider>()
                .SetupGet(s => s.AliasNameStoreRegistry)
                .Returns(targetRegistry);

            InvokeProtected(server, "OnNodeManagerStarted", serverInternal.Object);

            Assert.That(targetRegistry.Stores, Does.Contain(aliasStore.Object));
        }

        [Test]
        public void CustomServerWithConstructorHooksThrowsClearException()
        {
            Mock<ISubscriptionStore> store = new(MockBehavior.Strict);
            Mock<IMonitoredItemQueueFactory> queueFactory = new(MockBehavior.Strict);
            using ServiceProvider sp = CreateServerBuilder<CustomServer>()
                .AddDurableSubscriptions(store.Object, queueFactory.Object)
                .Services.BuildServiceProvider();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => CreateServer(sp))!;

            Assert.That(ex.Message, Does.Contain(nameof(DependencyInjectionStandardServer)));
            Assert.That(ex.Message, Does.Contain(nameof(OpcUaServerBuilderExtensions.AddDurableSubscriptions)));
        }

        [Test]
        public void CustomDependencyInjectionServerAppliesDurableSubscriptionHooks()
        {
            Mock<ISubscriptionStore> store = new(MockBehavior.Strict);
            Mock<IMonitoredItemQueueFactory> queueFactory = new(MockBehavior.Strict);
            using ServiceProvider sp = CreateServerBuilder<CustomDependencyInjectionServer>()
                .AddDurableSubscriptions(store.Object, queueFactory.Object)
                .Services.BuildServiceProvider();

            using StandardServer server = CreateServer(sp);

            Assert.That(InvokeProtected(server, "CreateSubscriptionStore"), Is.SameAs(store.Object));
            Assert.That(InvokeProtected(server, "CreateMonitoredItemQueueFactory"), Is.SameAs(queueFactory.Object));
        }

        [Test]
        public void ConfigureRolesSeedsDefaultRoleManager()
        {
            using ServiceProvider sp = CreateServerBuilder()
                .ConfigureRoles(options => options.Roles.Add(new RoleDefinitionOptions
                {
                    Name = BrowseNames.WellKnownRole_Observer,
                    Identities =
                    {
                        new RoleIdentityMappingOptions
                        {
                            CriteriaType = IdentityCriteriaType.UserName,
                            Criteria = "operator"
                        }
                    }
                }))
                .Services.BuildServiceProvider();

            IRoleManager roleManager = sp.GetRequiredService<IRoleManager>();
            RoleEntry entry = roleManager.GetRole(ObjectIds.WellKnownRole_Observer)!;

            Assert.That(entry.Identities, Has.Exactly(1).Matches<IdentityMappingRuleType>(rule =>
                rule.CriteriaType == IdentityCriteriaType.UserName && rule.Criteria == "operator"));
        }

        [Test]
        public void AddServerConfigurationWithRolesSectionSeedsConfiguredRoleManager()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Server:ApplicationName"] = "ConfiguredRolesServer",
                    ["Server:ApplicationUri"] = "urn:localhost:ConfiguredRolesServer",
                    ["Server:Roles:Roles:0:Name"] = BrowseNames.WellKnownRole_Observer,
                    ["Server:Roles:Roles:0:Identities:0:CriteriaType"] =
                        nameof(IdentityCriteriaType.UserName),
                    ["Server:Roles:Roles:0:Identities:0:Criteria"] = "operator"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();

            using ServiceProvider sp = services.AddOpcUa()
                .AddServer(configuration.GetSection("Server"))
                .Services.BuildServiceProvider();

            IRoleManager roleManager = sp.GetRequiredService<IRoleManager>();
            RoleEntry entry = roleManager.GetRole(ObjectIds.WellKnownRole_Observer)!;

            Assert.That(entry.Identities, Has.Exactly(1).Matches<IdentityMappingRuleType>(rule =>
                rule.CriteriaType == IdentityCriteriaType.UserName && rule.Criteria == "operator"));
        }

        [Test]
        public async Task ConfigureRolesBindsRoleSetToDependencyInjectedRoleManagerAsync()
        {
            RoleCaptureServer.Reset();
            await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                services => services.AddOpcUa()
                    .AddServer<RoleCaptureServer>(options => ConfigureHostedOptions(options, "RoleCaptureServer"))
                    .ConfigureRoles(options => options.Roles.Add(new RoleDefinitionOptions
                    {
                        Name = BrowseNames.WellKnownRole_Observer,
                        Identities =
                        {
                            new RoleIdentityMappingOptions
                            {
                                CriteriaType = IdentityCriteriaType.UserName,
                                Criteria = "operator"
                            }
                        }
                    })));

            Assert.That(
                await WaitForAsync(
                    () => RoleCaptureServer.BoundObserverRole?.Identities?.Value is not null,
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                Is.True);

            IRoleManager roleManager = fixture.Services.GetRequiredService<IRoleManager>();

            Assert.That(RoleCaptureServer.BoundRoleManager, Is.SameAs(roleManager));
            Assert.That(HasUserNameRule(RoleCaptureServer.BoundObserverRole!.Identities!.Value, "operator"), Is.True);
            Assert.That(ServiceResult.IsGood(
                roleManager.AddIdentity(ObjectIds.WellKnownRole_Observer, new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = "address-space-sync"
                })), Is.True);
            Assert.That(HasUserNameRule(RoleCaptureServer.BoundObserverRole.Identities.Value, "address-space-sync"),
                Is.True);
        }

        [Test]
        public async Task AddRoleManagerBindsRoleSetToInjectedRoleManagerAsync()
        {
            RoleCaptureServer.Reset();
            using var roleManager = new RoleManager();
            Assert.That(ServiceResult.IsGood(
                roleManager.AddIdentity(ObjectIds.WellKnownRole_Observer, new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = "injected"
                })), Is.True);

            await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                services => services.AddOpcUa()
                    .AddServer<RoleCaptureServer>(options => ConfigureHostedOptions(options, "InjectedRoleServer"))
                    .AddRoleManager(roleManager));

            Assert.That(
                await WaitForAsync(
                    () => RoleCaptureServer.BoundObserverRole?.Identities?.Value is not null,
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                Is.True);
            Assert.That(RoleCaptureServer.BoundRoleManager, Is.SameAs(roleManager));
            Assert.That(
                fixture.Services.GetRequiredService<IRoleManager>(),
                Is.SameAs(roleManager));
            Assert.That(HasUserNameRule(RoleCaptureServer.BoundObserverRole!.Identities!.Value, "injected"),
                Is.True);
        }

        [Test]
        public async Task HostedServiceAssignsServerHooksAndRunsStartupTasksAsync()
        {
            StartupHookCaptureServer.Reset();
            var sessionManagerFactory = new Mock<ISessionManagerFactory>();
            sessionManagerFactory
                .Setup(factory => factory.Create(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<TimeProvider>(),
                    It.IsAny<Func<string, Certificate?>>()))
                .Returns(
                    (IServerInternal server, ApplicationConfiguration configuration, TimeProvider timeProvider, Func<string, Certificate?> _) =>
                        new SessionManager(server, configuration, timeProvider));
            var redundantServerSetProvider = new Mock<IRedundantServerSetProvider>();
            redundantServerSetProvider.Setup(p => p.GetRedundantServerSet()).Returns([]);
            var getEndpointsDirector = new Mock<IGetEndpointsDirector>();
            var subscriptionStore = new Mock<ISubscriptionStore>();
            var monitoredItemQueueFactory = new Mock<IMonitoredItemQueueFactory>();
            var recordingTask = new RecordingStartupTask();

            await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                services =>
                {
                    services.AddOpcUa()
                        .AddServer<StartupHookCaptureServer>(options => ConfigureHostedOptions(options, "StartupHooks"));
                    services.AddSingleton(sessionManagerFactory.Object);
                    services.AddSingleton(redundantServerSetProvider.Object);
                    services.AddSingleton(getEndpointsDirector.Object);
                    services.AddSingleton(subscriptionStore.Object);
                    services.AddSingleton(monitoredItemQueueFactory.Object);
                    services.AddSingleton<IServerStartupTask>(recordingTask);
                });

            Assert.That(
                await WaitForAsync(
                    () => recordingTask.InvocationCount == 1 && StartupHookCaptureServer.StartedServer != null,
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                Is.True);

            Assert.That(StartupHookCaptureServer.StartedServer, Is.Not.Null);
            Assert.That(StartupHookCaptureServer.StartedServer!.SessionManagerFactory, Is.SameAs(sessionManagerFactory.Object));
            Assert.That(StartupHookCaptureServer.StartedServer.RedundantServerSetProvider, Is.SameAs(redundantServerSetProvider.Object));
            Assert.That(StartupHookCaptureServer.StartedServer.GetEndpointsDirector, Is.SameAs(getEndpointsDirector.Object));
            Assert.That(StartupHookCaptureServer.StartedServer.SubscriptionStore, Is.SameAs(subscriptionStore.Object));
            Assert.That(StartupHookCaptureServer.StartedServer.MonitoredItemQueueFactory, Is.SameAs(monitoredItemQueueFactory.Object));
            Assert.That(recordingTask.ObservedServer, Is.SameAs(StartupHookCaptureServer.StartedServer.CurrentInstance));
        }

        [Test]
        public void AddRoleManagerReplacesDefaultRoleManager()
        {
            using var roleManager = new RoleManager();
            using ServiceProvider sp = CreateServerBuilder()
                .AddRoleManager(roleManager)
                .Services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<IRoleManager>(), Is.SameAs(roleManager));
        }

        [Test]
        public void AddNodeManagerRegistersFluentNodeManagerFactory()
        {
            using ServiceProvider sp = CreateServerBuilder()
                .AddNodeManager("urn:tests:fluent", builder => builder.Node("ReferenceServer"))
                .Services.BuildServiceProvider();

            OpcUaServerNodeManagerRegistration registration = sp
                .GetServices<OpcUaServerNodeManagerRegistration>()
                .Single();

            Assert.That(registration.AsyncFactory, Is.Not.Null);
            Assert.That(registration.AsyncFactory!.NamespacesUris.Count, Is.EqualTo(1));
            Assert.That(registration.AsyncFactory.NamespacesUris[0], Is.EqualTo("urn:tests:fluent"));
        }

        [Test]
        public void ReverseConnectAndOperationLimitsConfigureServerOptions()
        {
            using ServiceProvider sp = CreateServerBuilder()
                .AddReverseConnect(options =>
                {
                    options.ConnectIntervalMs = 1234;
                    options.Clients.Add(new ServerReverseConnectClientOptions
                    {
                        EndpointUrl = "opc.tcp://client.example.com:4841"
                    });
                })
                .ConfigureOperationLimits(options => options.MaxNodesPerRead = 42)
                .Services.BuildServiceProvider();

            OpcUaServerOptions options = sp.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;

            Assert.That(options.ReverseConnect, Is.Not.Null);
            Assert.That(options.ReverseConnect!.ConnectIntervalMs, Is.EqualTo(1234));
            Assert.That(options.ReverseConnect.Clients[0].EndpointUrl, Is.EqualTo("opc.tcp://client.example.com:4841"));
            Assert.That(options.OperationLimits, Is.Not.Null);
            Assert.That(options.OperationLimits!.MaxNodesPerRead, Is.EqualTo(42));
        }

        [Test]
        public void ReverseConnectAndOperationLimitsBindFromConfiguration()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Reverse:ConnectIntervalMs"] = "2345",
                    ["Reverse:Clients:0:EndpointUrl"] = "opc.tcp://client.example.com:4842",
                    ["Limits:MaxNodesPerBrowse"] = "7"
                })
                .Build();

            using ServiceProvider sp = CreateServerBuilder()
                .AddReverseConnect(configuration.GetSection("Reverse"))
                .ConfigureOperationLimits(configuration.GetSection("Limits"))
                .Services.BuildServiceProvider();

            OpcUaServerOptions options = sp.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;

            Assert.That(options.ReverseConnect!.ConnectIntervalMs, Is.EqualTo(2345));
            Assert.That(options.ReverseConnect.Clients[0].EndpointUrl, Is.EqualTo("opc.tcp://client.example.com:4842"));
            Assert.That(options.OperationLimits!.MaxNodesPerBrowse, Is.EqualTo(7));
        }

        [Test]
        public void ServerTransportForwardersReturnSameBuilderAndRegisterBindings()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();

            IOpcUaServerBuilder returned = builder.AddOpcTcpTransport();

            using ServiceProvider sp = builder.Services.BuildServiceProvider();
            Assert.That(returned, Is.SameAs(builder));
            Assert.That(sp.GetServices<ITransportBindingConfigurator>(), Is.Not.Empty);
        }

        [Test]
        public void OneShotServerPresetsRegisterExpectedServices()
        {
            Mock<IHistorianProvider> historian = new(MockBehavior.Strict);
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "HistorianFileStore");
            using ServiceProvider reference = new ServiceCollection()
                .AddLogging()
                .AddOpcUa()
                .AddReferenceServer()
                .Services.BuildServiceProvider();
            using ServiceProvider secure = new ServiceCollection()
                .AddLogging()
                .AddOpcUa()
                .AddSecureServer(options =>
                {
                    options.ApplicationName = "SecurePreset";
                    options.ApplicationUri = "urn:localhost:SecurePreset";
                    options.ProductUri = "urn:localhost:SecurePreset:product";
                })
                .AddHistorianFileStore(historian.Object, root, "History")
                .Services.BuildServiceProvider();

            Assert.That(reference.GetServices<OpcUaServerNodeManagerRegistration>(), Is.Not.Empty);
            Assert.That(reference.GetRequiredService<IRoleManager>(), Is.Not.Null);
            Assert.That(secure.GetRequiredService<IRoleManager>(), Is.Not.Null);
            Assert.That(secure.GetRequiredService<IHistorianProvider>(), Is.SameAs(historian.Object));
            Assert.That(secure.GetRequiredService<IFileSystemProvider>(), Is.TypeOf<PhysicalFileSystemProvider>());
        }

        [Test]
        public void AddFileSystemRegistersProviderAndNodeManagerFactory()
        {
            Mock<IFileSystemProvider> provider = new(MockBehavior.Strict);
            provider.SetupGet(p => p.MountName).Returns("Files");

            using ServiceProvider sp = CreateServerBuilder()
                .AddFileSystem(provider.Object)
                .Services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<IFileSystemProvider>(), Is.SameAs(provider.Object));
            Assert.That(sp.GetRequiredService<FileSystemNodeManagerFactory>(), Is.Not.Null);
            Assert.That(sp.GetServices<OpcUaServerNodeManagerRegistration>(), Has.Exactly(1).Items);
        }

        [Test]
        public void AddSecretStoreAndCertificateManagerRegisterReplacements()
        {
            Mock<ISecretStore> secretStore = new(MockBehavior.Strict);
            Mock<ICertificateManager> certificateManager = new(MockBehavior.Strict);

            using ServiceProvider sp = CreateServerBuilder()
                .AddSecretStore(secretStore.Object)
                .AddCertificateManager(certificateManager.Object)
                .Services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<ISecretStore>(), Is.SameAs(secretStore.Object));
            Assert.That(sp.GetRequiredService<ICertificateManager>(), Is.SameAs(certificateManager.Object));
        }

        [Test]
        public void AddAliasNameStoreRegistersService()
        {
            Mock<IAliasNameStore> aliasStore = new(MockBehavior.Strict);
            aliasStore.SetupGet(s => s.RootCategories).Returns([]);

            using ServiceProvider sp = CreateServerBuilder()
                .AddAliasNameStore(aliasStore.Object)
                .Services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<IAliasNameStore>(), Is.SameAs(aliasStore.Object));
        }

        [Test]
        public async Task NonAnonymousPolicyWithoutMatchingAuthenticatorLogsWarningAsync()
        {
            var loggerProvider = new CapturingLoggerProvider();
            await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                services =>
                {
                    services.AddLogging(builder => builder.AddProvider(loggerProvider));
                    services.AddOpcUa().AddServer(o =>
                    {
                        ConfigureHostedOptions(o, "MissingAuthenticatorServer");
                        o.UserTokenPolicies.Clear();
                        o.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.UserName });
                        o.Identity.Defaults.EnableAnonymous = false;
                        o.Identity.Defaults.EnableUserNamePassword = false;
                        o.Identity.Defaults.EnableX509 = false;
                        o.Identity.Defaults.EnableJwt = false;
                    });
                },
                addDefaultLogging: false);

            Assert.That(
                await WaitForAsync(
                    () => loggerProvider.Messages.Any(
                        message => message.Contains("without a matching identity authenticator", StringComparison.Ordinal)),
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                Is.True);
        }

        [Test]
        public void NewServerFluentApiRejectsNullArguments()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();
            Mock<ISubscriptionStore> store = new(MockBehavior.Strict);
            Mock<IMonitoredItemQueueFactory> queueFactory = new(MockBehavior.Strict);
            Mock<IHistorianProvider> historian = new(MockBehavior.Strict);
            Mock<IFileSystemProvider> fileSystem = new(MockBehavior.Strict);
            Mock<ISecretStore> secretStore = new(MockBehavior.Strict);
            Mock<ICertificateManager> certificateManager = new(MockBehavior.Strict);
            Mock<IAliasNameStore> aliasStore = new(MockBehavior.Strict);
            Mock<IAliasNameStoreRegistry> aliasRegistry = new(MockBehavior.Strict);

            Assert.That(() => builder.AddDurableSubscriptions(null!, queueFactory.Object),
                Throws.ArgumentNullException);
            Assert.That(() => builder.AddDurableSubscriptions(store.Object, null!),
                Throws.ArgumentNullException);
            Assert.That(() => builder.AddSessionManager(null!),
                Throws.ArgumentNullException);
            Assert.That(() => builder.AddSubscriptionManager(null!),
                Throws.ArgumentNullException);
            Assert.That(() => builder.AddHistorian(null!),
                Throws.ArgumentNullException);
            Assert.That(() => builder.AddFileSystem((IFileSystemProvider)null!),
                Throws.ArgumentNullException);
            Assert.That(() => builder.AddSecretStore(null!),
                Throws.ArgumentNullException);
            Assert.That(() => builder.AddCertificateManager(null!),
                Throws.ArgumentNullException);
            Assert.That(() => builder.AddAliasNameStore(null!),
                Throws.ArgumentNullException);
            Assert.That(() => builder.AddAliasNameStoreRegistry(null!),
                Throws.ArgumentNullException);

            Assert.That(() => ((IOpcUaServerBuilder)null!).AddDurableSubscriptions(store.Object, queueFactory.Object),
                Throws.ArgumentNullException);
            Assert.That(() => ((IOpcUaServerBuilder)null!).AddSessionManager((_, _, _) => Mock.Of<ISessionManager>()),
                Throws.ArgumentNullException);
            Assert.That(() => ((IOpcUaServerBuilder)null!).AddSubscriptionManager(
                    (_, _, _) => Mock.Of<ISubscriptionManager>()),
                Throws.ArgumentNullException);
            Assert.That(() => ((IOpcUaServerBuilder)null!).AddHistorian(historian.Object),
                Throws.ArgumentNullException);
            Assert.That(() => ((IOpcUaServerBuilder)null!).AddFileSystem(fileSystem.Object),
                Throws.ArgumentNullException);
            Assert.That(() => ((IOpcUaServerBuilder)null!).AddSecretStore(secretStore.Object),
                Throws.ArgumentNullException);
            Assert.That(() => ((IOpcUaServerBuilder)null!).AddCertificateManager(certificateManager.Object),
                Throws.ArgumentNullException);
            Assert.That(() => ((IOpcUaServerBuilder)null!).AddAliasNameStore(aliasStore.Object),
                Throws.ArgumentNullException);
            Assert.That(() => ((IOpcUaServerBuilder)null!).AddAliasNameStoreRegistry(aliasRegistry.Object),
                Throws.ArgumentNullException);
            Assert.That(() => Microsoft.Extensions.DependencyInjection.OpcUaServerBuilderExtensions
                    .AddServer<CustomServer>(null!, _ => { }),
                Throws.ArgumentNullException);
            Assert.That(() => new ServiceCollection().AddOpcUa().AddServer<CustomServer>(null!),
                Throws.ArgumentNullException);
        }

        private static IOpcUaServerBuilder CreateServerBuilder()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            return services.AddOpcUa().AddServer(o =>
            {
                o.ApplicationName = "FluentApiTest";
                o.ApplicationUri = "urn:localhost:FluentApiTest";
                o.ProductUri = "urn:localhost:FluentApiTest:product";
            });
        }

        private static IOpcUaServerBuilder CreateServerBuilder<TServer>()
            where TServer : StandardServer
        {
            var services = new ServiceCollection();
            services.AddLogging();
            return services.AddOpcUa().AddServer<TServer>(o =>
            {
                o.ApplicationName = "FluentApiTest";
                o.ApplicationUri = "urn:localhost:FluentApiTest";
                o.ProductUri = "urn:localhost:FluentApiTest:product";
            });
        }

        private static StandardServer CreateServer(IServiceProvider services)
        {
            return services.GetRequiredService<IOpcUaServerFactory>()
                .CreateServer(NUnitTelemetryContext.Create(isServer: true), TimeProvider.System);
        }

        private static void ConfigureHostedOptions(OpcUaServerOptions options, string applicationName)
        {
            string testRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(ServerFluentApiHostingTests),
                applicationName,
                Guid.NewGuid().ToString("N"));
            options.ApplicationName = applicationName;
            options.ApplicationUri = "urn:localhost:" + applicationName;
            options.ProductUri = "urn:localhost:" + applicationName + ":product";
            options.PkiRoot = Path.Combine(testRoot, "pki");
            options.AutoAcceptUntrustedCertificates = true;
            options.IncludeUnsecurePolicyNone = true;
            options.EndpointUrls.Clear();
            options.EndpointUrls.Add(
                "opc.tcp://localhost:" +
                GetAvailablePort().ToString(CultureInfo.InvariantCulture) +
                "/" +
                applicationName);
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            return condition();
        }

        private static bool HasUserNameRule(ArrayOf<IdentityMappingRuleType> rules, string criteria)
        {
            foreach (IdentityMappingRuleType rule in rules)
            {
                if (rule.CriteriaType == IdentityCriteriaType.UserName &&
                    string.Equals(rule.Criteria, criteria, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static object? InvokeProtected(
            StandardServer server,
            string methodName,
            IServerInternal? serverInternal = null)
        {
            MethodInfo method = typeof(StandardServer).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new InvalidOperationException("Protected method not found.");
            return method.Invoke(
                server,
                methodName == "OnNodeManagerStarted"
                    ? [serverInternal]
                    : [serverInternal ?? Mock.Of<IServerInternal>(), new ApplicationConfiguration()]);
        }

        public sealed class CustomServer : StandardServer
        {
            public CustomServer(ITelemetryContext telemetry, TimeProvider timeProvider)
                : base(telemetry, timeProvider)
            {
            }
        }

        public sealed class CustomDependencyInjectionServer : DependencyInjectionStandardServer
        {
            public CustomDependencyInjectionServer(
                IServiceProvider services,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
                : base(services, telemetry, timeProvider)
            {
            }
        }

        public sealed class RoleCaptureServer : DependencyInjectionStandardServer
        {
            public RoleCaptureServer(
                IServiceProvider services,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
                : base(services, telemetry, timeProvider)
            {
            }

            public static IRoleManager? BoundRoleManager { get; private set; }

            public static RoleState? BoundObserverRole { get; private set; }

            public static void Reset()
            {
                BoundRoleManager = null;
                BoundObserverRole = null;
            }

            protected override void OnNodeManagerStarted(IServerInternal server)
            {
                BoundRoleManager = server.RoleManager;
                BoundObserverRole = server.DiagnosticsNodeManager.FindPredefinedNode<RoleState>(
                    ObjectIds.WellKnownRole_Observer);
                base.OnNodeManagerStarted(server);
            }
        }

        public sealed class ObservedHostedServer : StandardServer
        {
            public ObservedHostedServer(ITelemetryContext telemetry, TimeProvider timeProvider)
                : base(telemetry, timeProvider)
            {
            }

            public static Type? StartedType { get; set; }

            protected override void OnServerStarted(IServerInternal server)
            {
                StartedType = GetType();
                base.OnServerStarted(server);
            }
        }

        public sealed class StartupHookCaptureServer : DependencyInjectionStandardServer
        {
            public StartupHookCaptureServer(
                IServiceProvider services,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
                : base(services, telemetry, timeProvider)
            {
                Volatile.Write(ref s_startedServer, this);
            }

            public static StartupHookCaptureServer? StartedServer => Volatile.Read(ref s_startedServer);

            public static void Reset()
            {
                Volatile.Write(ref s_startedServer, null);
            }

            private static StartupHookCaptureServer? s_startedServer;
        }

        private sealed class StubServerFactory : IOpcUaServerFactory
        {
            public StandardServer CreateServer(ITelemetryContext telemetry, TimeProvider timeProvider)
            {
                return new CustomServer(telemetry, timeProvider);
            }
        }

        private sealed class HostedServerFixture : IAsyncDisposable
        {
            private HostedServerFixture(ServiceProvider provider, IHostedService hostedService)
            {
                m_provider = provider;
                m_hostedService = hostedService;
            }

            public static async ValueTask<HostedServerFixture> StartAsync(
                Action<IServiceCollection> configureServices,
                bool addDefaultLogging = true)
            {
                var services = new ServiceCollection();
                if (addDefaultLogging)
                {
                    services.AddLogging();
                }
                configureServices(services);
                ServiceProvider provider = services.BuildServiceProvider();
                IHostedService hostedService = provider.GetServices<IHostedService>().Single();
                try
                {
                    await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);
                    return new HostedServerFixture(provider, hostedService);
                }
                catch
                {
                    await provider.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            public IServiceProvider Services => m_provider;

            public async ValueTask DisposeAsync()
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await m_hostedService.StopAsync(cts.Token).ConfigureAwait(false);
                }
                finally
                {
                    await m_provider.DisposeAsync().ConfigureAwait(false);
                }
            }

            private readonly ServiceProvider m_provider;
            private readonly IHostedService m_hostedService;
        }

        private sealed class RecordingStartupTask : IServerStartupTask
        {
            public int InvocationCount => Volatile.Read(ref m_invocationCount);

            public IServerInternal? ObservedServer { get; private set; }

            public ValueTask OnServerStartedAsync(IServerInternal server, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_invocationCount);
                ObservedServer = server;
                return default;
            }

            private int m_invocationCount;
        }

        private sealed class CapturingLoggerProvider : ILoggerProvider
        {
            public ConcurrentBag<string> Messages { get; } = [];

            public ILogger CreateLogger(string categoryName)
            {
                return new CapturingLogger(Messages);
            }

            public void Dispose()
            {
            }
        }

        private sealed class CapturingLogger : ILogger
        {
            public CapturingLogger(ConcurrentBag<string> messages)
            {
                m_messages = messages;
            }

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                return NoopDisposable.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                {
                    m_messages.Add(formatter(state, exception));
                }
            }

            private readonly ConcurrentBag<string> m_messages;
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static NoopDisposable Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
