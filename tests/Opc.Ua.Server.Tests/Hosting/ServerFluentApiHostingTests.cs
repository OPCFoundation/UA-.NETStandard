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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Hosting
{
    /// <summary>
    /// Verifies fluent server registration, hosted startup, injected services, and lifecycle hook binding.
    /// </summary>
    [TestFixture]
    [Category("Hosting")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class ServerFluentApiHostingTests
    {
        /// <summary>
        /// Verifies that custom server registration creates the configured server type.
        /// </summary>
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

        /// <summary>
        /// Verifies that hosted startup starts the configured custom server.
        /// </summary>
        [Test]
        public async Task AddServerWithCustomServerStartsCustomServerAsync()
        {
            ObservedHostedServer.StartedType = null;
            await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                services => services.AddOpcUa().AddServer<ObservedHostedServer>(
                    o => ConfigureHostedOptions(o, "CustomHostedServer"))).ConfigureAwait(false);

            Assert.That(
                await WaitForAsync(
                    () => ObservedHostedServer.StartedType == typeof(ObservedHostedServer),
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                Is.True);
        }

        /// <summary>
        /// Verifies that application configuration is shared consistently between client and server setup.
        /// </summary>
        [Test]
        public async Task ConfigureApplicationBuildsSharedClientAndServerConfigurationAsync()
        {
            // Short root on purpose. A ClientAndServer application provisions
            // ECC certificates too, and those file names carry the curve
            // ("... [BrainpoolP256r1] [<thumbprint>].pfx"), which is long
            // enough that a root named after this test method pushes the PFX
            // past MAX_PATH. .NET Framework cannot open such a path at all.
            string pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "cfgapp",
                Guid.NewGuid().ToString("N"));
            using var certificateManager = new CertificateManager(
                NUnitTelemetryContext.Create(isServer: true));
            ObservedHostedServer.StartedApplicationName = null;
            ObservedHostedServer.StartedApplicationType = null;
            ObservedHostedServer.StartedCertificateManager = null;

            try
            {
                await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                    services =>
                    {
                        IOpcUaBuilder builder = services.AddOpcUa()
                            .ConfigureApplication(options =>
                            {
                                options.ApplicationName = "CombinedHostedApplication";
                                options.ApplicationUri =
                                    "urn:localhost:CombinedHostedApplication";
                                options.ProductUri =
                                    "uri:opcfoundation.org:CombinedHostedApplication";
                                options.PkiRoot = pkiRoot;
                                options.AutoAcceptUntrustedCertificates = true;
                            });
                        builder.AddClient(_ => { });
                        builder
                            .AddServer<ObservedHostedServer>(options =>
                            {
                                options.EndpointUrls.Add(
                                    "opc.tcp://localhost:0/CombinedHostedApplication");
                                options.IncludeUnsecurePolicyNone = true;
                            })
                            .AddCertificateManager(certificateManager);
                    }).ConfigureAwait(false);

                Assert.That(
                    await WaitForAsync(
                        () => ObservedHostedServer.StartedApplicationType != null,
                        TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                    Is.True);
                Assert.That(
                    ObservedHostedServer.StartedApplicationName,
                    Is.EqualTo("CombinedHostedApplication"));
                Assert.That(
                    ObservedHostedServer.StartedApplicationType,
                    Is.EqualTo(ApplicationType.ClientAndServer));
                Assert.That(
                    ObservedHostedServer.StartedCertificateManager,
                    Is.SameAs(certificateManager));
            }
            finally
            {
                if (Directory.Exists(pkiRoot))
                {
                    Directory.Delete(pkiRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies that default server registration uses a dependency-injection-aware factory.
        /// </summary>
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

        /// <summary>
        /// Verifies that the server factory can be resolved and replaced through dependency injection.
        /// </summary>
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

        /// <summary>
        /// Verifies that durable-subscription registration supplies the standard server's construction hooks.
        /// </summary>
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

        /// <summary>
        /// Verifies that session and subscription manager registrations supply the standard server's construction
        /// hooks.
        /// </summary>
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

        /// <summary>
        /// Verifies that alias-name registration is applied before node managers start.
        /// </summary>
        [Test]
        public async Task AddAliasNamesRegistersBeforeNodeManagerStartupAsync()
        {
            Mock<IAliasNameStore> aliasStore = new(MockBehavior.Strict);
            aliasStore.SetupGet(s => s.RootCategories).Returns([]);
            using ServiceProvider sp = CreateServerBuilder()
                .AddAliasNameStore(aliasStore.Object)
                .Services.BuildServiceProvider();
            using StandardServer server = CreateServer(sp);
            var aliasRegistry = new AliasNameStoreRegistry();
            Mock<IServerInternal> serverInternal = new(MockBehavior.Strict);
            serverInternal.As<IAliasNameStoreRegistryProvider>()
                .SetupGet(s => s.AliasNameStoreRegistry)
                .Returns(aliasRegistry);

            await new OpcUaServerAliasNameStartupTask(sp)
                .OnServerStartingAsync(serverInternal.Object).ConfigureAwait(false);

            Assert.That(aliasRegistry.Stores, Does.Contain(aliasStore.Object));
        }

        /// <summary>
        /// Verifies that the hosted historian is available before node-manager startup completes.
        /// </summary>
        [Test]
        public async Task HostedHistorianIsAvailableBeforeNodeManagerStartupAsync()
        {
            EarlyHistorianCaptureServer.Reset();
            var historian = new Mock<IHistorianProvider>();
            historian.As<IHistorianDataProvider>();
            historian
                .Setup(provider => provider.GetCapabilitiesAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    new HistorianNodeCapabilities
                    {
                        ReadRawData = true
                    }));

            await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                services =>
                {
                    services.AddSingleton(historian.Object);
                    services.AddOpcUa()
                        .AddServer<EarlyHistorianCaptureServer>(
                            options => ConfigureHostedOptions(options, "EarlyHistorian"))
                        .AddNodeManager<HistorizedNodeManagerFactory>()
                        .AddHistorian<IHistorianProvider>();
                }).ConfigureAwait(false);

            Assert.That(
                await WaitForAsync(
                    () => EarlyHistorianCaptureServer.NodeManagerStarted,
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                Is.True);
            Assert.That(
                EarlyHistorianCaptureServer.ResolvedProvider,
                Is.SameAs(historian.Object));
            Assert.That(HistorizedNodeManager.Variable, Is.Not.Null);
            Assert.That(HistorizedNodeManager.Variable!.Historizing, Is.True);
            Assert.That(
                HistorizedNodeManager.Variable.AccessLevel & AccessLevels.HistoryRead,
                Is.EqualTo(AccessLevels.HistoryRead));
            Assert.That(
                HistorizedNodeManager.Variable.UserAccessLevel & AccessLevels.HistoryRead,
                Is.EqualTo(AccessLevels.HistoryRead));
            Assert.That(
                EarlyHistorianCaptureServer.HistoryCapabilities?
                    .AccessHistoryDataCapability?.Value,
                Is.True);
        }

        /// <summary>
        /// Verifies that a transient pre-startup task executes only once.
        /// </summary>
        [Test]
        public async Task TransientPreStartupTaskRunsOnceAsync()
        {
            TransientPreStartupTask.Reset();
            await using HostedServerFixture fixture =
                await HostedServerFixture.StartAsync(
                    services =>
                    {
                        services.AddTransient<IServerPreStartupTask>(
                            _ => new TransientPreStartupTask());
                        services.AddOpcUa()
                            .AddServer(options =>
                                ConfigureHostedOptions(
                                    options,
                                    "TransientPreStartup"));
                    }).ConfigureAwait(false);

            Assert.That(
                await WaitForAsync(
                    () => TransientPreStartupTask.InvocationCount == 1,
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                Is.True);
        }

        /// <summary>
        /// Verifies that registered alias stores are copied into the server before node managers start.
        /// </summary>
        [Test]
        public async Task AddAliasNameStoreRegistryCopiesStoresBeforeNodeManagerStartupAsync()
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

            await new OpcUaServerAliasNameStartupTask(sp)
                .OnServerStartingAsync(serverInternal.Object).ConfigureAwait(false);

            Assert.That(targetRegistry.Stores, Does.Contain(aliasStore.Object));
        }

        /// <summary>
        /// Verifies that incompatible custom server construction hooks produce a clear exception.
        /// </summary>
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

        /// <summary>
        /// Verifies that a custom dependency-injection server applies durable-subscription hooks.
        /// </summary>
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

        /// <summary>
        /// Verifies that a dependency-injection server stages historian services without a hosted service.
        /// </summary>
        [Test]
        public void DependencyInjectionServerStagesHistorianWithoutHostedService()
        {
            var historian = new Mock<IHistorianProvider>();
            using ServiceProvider sp = CreateServerBuilder()
                .AddHistorian(historian.Object)
                .Services.BuildServiceProvider();

            using StandardServer server = CreateServer(sp);
            var registrations = (System.Collections.IEnumerable)
                typeof(StandardServer)
                    .GetField(
                        "m_historianProviders",
                        BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(server)!;

            Assert.That(
                registrations.Cast<object>().Count(),
                Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that fluent role configuration seeds the default role manager.
        /// </summary>
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

        /// <summary>
        /// Verifies that a configured roles section seeds the configured role manager.
        /// </summary>
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

        /// <summary>
        /// Verifies that the user-management creation hook binds the model to the server.
        /// </summary>
        [Test]
        public async Task CreateUserManagementSeamBindsTheModelToTheServerAsync()
        {
            // The user management model is bound through the CreateUserManagement factory
            // seam, not by reaching for IServerInternal.SetUserManagement from a node
            // manager override. Every other hosted-server test in this fixture starts
            // without one, which covers the default null path.
            UserManagementCaptureServer.Reset();
            var userManagement = new Mock<UserManagement.IUserManagement>();
            userManagement.Setup(u => u.SnapshotUsers()).Returns([]);
            userManagement.Setup(u => u.PasswordLength).Returns(new Range(256, 1));
            userManagement.Setup(u => u.PasswordOptions).Returns(PasswordOptionsMask.None);
            userManagement.Setup(u => u.PasswordRestrictions).Returns((LocalizedText?)null);
            UserManagementCaptureServer.Supplied = userManagement.Object;

            try
            {
                await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                    services => services.AddOpcUa()
                        .AddServer<UserManagementCaptureServer>(
                            options => ConfigureHostedOptions(options, "UserManagementCaptureServer")))
                    .ConfigureAwait(false);

                Assert.That(
                    await WaitForAsync(
                        () => UserManagementCaptureServer.NodeManagerStarted,
                        TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                    Is.True,
                    "the server must reach OnNodeManagerStarted");

                Assert.That(
                    UserManagementCaptureServer.BoundUserManagement,
                    Is.SameAs(userManagement.Object));
            }
            finally
            {
                UserManagementCaptureServer.Reset();
            }
        }

        /// <summary>
        /// Verifies that role configuration binds the RoleSet to the dependency-injected role manager.
        /// </summary>
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
                    }))).ConfigureAwait(false);

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

        /// <summary>
        /// Verifies that a configured custom role is browsable beneath the RoleSet.
        /// </summary>
        [Test]
        public async Task ConfigureRolesCustomRoleIsBrowsableUnderTheRoleSetAsync()
        {
            // Part 18 §4.2: the RoleSet describes every role the server
            // supports, so a role that only exists in the configuration still
            // needs a node — otherwise it grants access but cannot be browsed,
            // read or reconfigured by a client.
            RoleCaptureServer.Reset();
            await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                services => services.AddOpcUa()
                    .AddServer<RoleCaptureServer>(
                        options => ConfigureHostedOptions(options, "CustomRoleServer"))
                    .ConfigureRoles(options => options.Roles.Add(new RoleDefinitionOptions
                    {
                        Name = "Maintenance",
                        Identities =
                        {
                            new RoleIdentityMappingOptions
                            {
                                CriteriaType = IdentityCriteriaType.UserName,
                                Criteria = "maintainer"
                            }
                        }
                    }))).ConfigureAwait(false);

            Assert.That(
                await WaitForAsync(
                    () => RoleCaptureServer.BoundServer != null,
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                Is.True);

            IServerInternal server = RoleCaptureServer.BoundServer!;
            IRoleManager roleManager = fixture.Services.GetRequiredService<IRoleManager>();
            NodeId roleId = roleManager.RoleIds.Single(
                id => string.Equals(roleManager.GetRole(id)?.BrowseName, "Maintenance",
                    StringComparison.Ordinal));

            RoleState? roleNode = server.DiagnosticsNodeManager
                .FindPredefinedNode<RoleState>(roleId);
            Assert.That(roleNode, Is.Not.Null,
                "A role created from ConfigureRoles must have a node under the RoleSet.");
            Assert.That(roleNode!.BrowseName.Name, Is.EqualTo("Maintenance"));
            Assert.That(HasUserNameRule(roleNode.Identities!.Value, "maintainer"), Is.True,
                "The node must expose the configured identity mapping.");
        }

        /// <summary>
        /// Verifies that an injected role manager is bound to the server's RoleSet.
        /// </summary>
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
                    .AddRoleManager(roleManager)).ConfigureAwait(false);

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

        /// <summary>
        /// Verifies that the hosted service assigns server hooks and executes startup tasks.
        /// </summary>
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
                }).ConfigureAwait(false);

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

        /// <summary>
        /// Verifies that role-manager registration replaces the default role manager.
        /// </summary>
        [Test]
        public void AddRoleManagerReplacesDefaultRoleManager()
        {
            using var roleManager = new RoleManager();
            using ServiceProvider sp = CreateServerBuilder()
                .AddRoleManager(roleManager)
                .Services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<IRoleManager>(), Is.SameAs(roleManager));
        }

        /// <summary>
        /// Verifies that node-manager registration installs a fluent node-manager factory.
        /// </summary>
        [Test]
        public void AddNodeManagerRegistersFluentNodeManagerFactory()
        {
            using ServiceProvider sp = CreateServerBuilder()
                .AddNodeManager("urn:tests:fluent", _ => { })
                .Services.BuildServiceProvider();

            OpcUaServerNodeManagerRegistration registration = sp
                .GetServices<OpcUaServerNodeManagerRegistration>()
                .Single();

            Assert.That(registration.AsyncFactory, Is.Not.Null);
            Assert.That(registration.AsyncFactory!.NamespacesUris.Count, Is.EqualTo(1));
            Assert.That(registration.AsyncFactory.NamespacesUris[0], Is.EqualTo("urn:tests:fluent"));
        }

        /// <summary>
        /// Verifies that fluent reverse-connect and operation-limit settings configure server options.
        /// </summary>
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

        /// <summary>
        /// Verifies that reverse-connect and operation-limit settings bind from application configuration.
        /// </summary>
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

        /// <summary>
        /// Verifies that server transport forwarding preserves the builder and registers transport bindings.
        /// </summary>
        [Test]
        public void ServerTransportForwardersReturnSameBuilderAndRegisterBindings()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();

            IOpcUaServerBuilder opcTcp = builder.AddOpcTcpTransport();
            IOpcUaServerBuilder https = builder.AddHttpsTransport();
            IOpcUaServerBuilder wss = builder.AddWssTransport();
#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS
            IOpcUaServerBuilder kestrel = builder.AddKestrelOpcTcpTransport();
            IOpcUaServerBuilder webApi = builder.AddWebApiTransport();
#endif

            using ServiceProvider sp = builder.Services.BuildServiceProvider();
            Assert.That(opcTcp, Is.SameAs(builder));
            Assert.That(https, Is.SameAs(builder));
            Assert.That(wss, Is.SameAs(builder));
#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS
            Assert.That(kestrel, Is.SameAs(builder));
            Assert.That(webApi, Is.SameAs(builder));
#endif
            Assert.That(sp.GetServices<ITransportBindingConfigurator>(), Is.Not.Empty);
        }

        /// <summary>
        /// Verifies that server transport forwarding rejects a null builder.
        /// </summary>
        [Test]
        public void ServerTransportForwardersThrowForNullBuilder()
        {
            IOpcUaServerBuilder builder = null!;

            Assert.Throws<ArgumentNullException>(() => builder.AddOpcTcpTransport());
            Assert.Throws<ArgumentNullException>(() => builder.AddHttpsTransport());
            Assert.Throws<ArgumentNullException>(() => builder.AddWssTransport());
#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS
            Assert.Throws<ArgumentNullException>(() => builder.AddKestrelOpcTcpTransport());
            Assert.Throws<ArgumentNullException>(() => builder.AddWebApiTransport());
#endif
        }

        /// <summary>
        /// Verifies that one-shot server presets register their expected services.
        /// </summary>
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

        /// <summary>
        /// Verifies that file-system registration installs both the provider and node-manager factory.
        /// </summary>
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

        /// <summary>
        /// Verifies that secret-store and certificate-manager registrations replace existing services.
        /// </summary>
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

        /// <summary>
        /// Verifies that alias-name-store registration adds the service.
        /// </summary>
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

        /// <summary>
        /// Verifies that a nonanonymous policy without a matching authenticator produces a warning.
        /// </summary>
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
                addDefaultLogging: false).ConfigureAwait(false);

            Assert.That(
                await WaitForAsync(
                    () => loggerProvider.Messages.Any(
                        message => message.Contains("without a matching identity authenticator", StringComparison.Ordinal)),
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                Is.True);
        }

        /// <summary>
        /// Verifies that the new fluent server APIs reject null arguments.
        /// </summary>
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
            Assert.That(() => builder.AddFileSystem(null!),
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
            Assert.That(() => OpcUaServerBuilderExtensions
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

        /// <summary>
        /// Provides a distinct standard server type for custom server factory and hosting scenarios.
        /// </summary>
        public sealed class CustomServer : StandardServer
        {
            /// <summary>
            /// Creates the custom standard server with injected telemetry and time.
            /// </summary>
            public CustomServer(ITelemetryContext telemetry, TimeProvider timeProvider)
                : base(telemetry, timeProvider)
            {
            }
        }

        /// <summary>
        /// Provides a custom dependency-injection server for constructor-hook scenarios.
        /// </summary>
        public sealed class CustomDependencyInjectionServer : DependencyInjectionStandardServer
        {
            /// <summary>
            /// Creates the custom server with its service provider, telemetry, and time source.
            /// </summary>
            public CustomDependencyInjectionServer(
                IServiceProvider services,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
                : base(services, telemetry, timeProvider)
            {
            }
        }

        /// <summary>
        /// Captures the role manager, Observer role, and server bound during node-manager startup.
        /// </summary>
        public sealed class RoleCaptureServer : DependencyInjectionStandardServer
        {
            /// <summary>
            /// Creates the role-capturing server with injected services, telemetry, and time.
            /// </summary>
            public RoleCaptureServer(
                IServiceProvider services,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
                : base(services, telemetry, timeProvider)
            {
            }

            /// <summary>
            /// Gets the role manager captured when node managers started.
            /// </summary>
            public static IRoleManager? BoundRoleManager { get; private set; }

            /// <summary>
            /// Gets the standard Observer role captured from the server's diagnostics model.
            /// </summary>
            public static RoleState? BoundObserverRole { get; private set; }

            /// <summary>
            /// Gets the server instance captured when node managers started.
            /// </summary>
            public static IServerInternal? BoundServer { get; private set; }

            /// <summary>
            /// Clears the captured role manager, Observer role, and server before the next scenario.
            /// </summary>
            public static void Reset()
            {
                BoundRoleManager = null;
                BoundObserverRole = null;
                BoundServer = null;
            }

            protected override void OnNodeManagerStarted(IServerInternal server)
            {
                BoundServer = server;
                BoundRoleManager = server.RoleManager;
                BoundObserverRole = server.DiagnosticsNodeManager.FindPredefinedNode<RoleState>(
                    ObjectIds.WellKnownRole_Observer);
                base.OnNodeManagerStarted(server);
            }
        }

        /// <summary>
        /// Supplies a configurable user-management implementation and captures its binding during startup.
        /// </summary>
        public sealed class UserManagementCaptureServer : DependencyInjectionStandardServer
        {
            /// <summary>
            /// Creates the user-management capture server with injected services, telemetry, and time.
            /// </summary>
            public UserManagementCaptureServer(
                IServiceProvider services,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
                : base(services, telemetry, timeProvider)
            {
            }

            /// <summary>
            /// Gets or sets the user-management implementation returned by the server's creation hook.
            /// </summary>
            public static UserManagement.IUserManagement? Supplied { get; set; }

            /// <summary>
            /// Gets the user-management implementation bound to the server during node-manager startup.
            /// </summary>
            public static UserManagement.IUserManagement? BoundUserManagement { get; private set; }

            /// <summary>
            /// Gets whether the node-manager-started callback has run.
            /// </summary>
            public static bool NodeManagerStarted { get; private set; }

            /// <summary>
            /// Clears supplied and captured user-management state and resets the startup indicator.
            /// </summary>
            public static void Reset()
            {
                Supplied = null;
                BoundUserManagement = null;
                NodeManagerStarted = false;
            }

            protected override UserManagement.IUserManagement? CreateUserManagement(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                return Supplied;
            }

            protected override void OnNodeManagerStarted(IServerInternal server)
            {
                BoundUserManagement = server.UserManagement;
                NodeManagerStarted = true;
                base.OnNodeManagerStarted(server);
            }
        }

        /// <summary>
        /// Captures the concrete server type and application configuration observed during hosted startup.
        /// </summary>
        public sealed class ObservedHostedServer : StandardServer
        {
            /// <summary>
            /// Creates the observed hosted server with injected telemetry and time.
            /// </summary>
            public ObservedHostedServer(ITelemetryContext telemetry, TimeProvider timeProvider)
                : base(telemetry, timeProvider)
            {
            }

            /// <summary>
            /// Gets or sets the concrete server type captured after startup.
            /// </summary>
            public static Type? StartedType { get; set; }

            /// <summary>
            /// Gets or sets the application name captured from startup configuration.
            /// </summary>
            public static string? StartedApplicationName { get; set; }

            /// <summary>
            /// Gets or sets the application type captured from startup configuration.
            /// </summary>
            public static ApplicationType? StartedApplicationType { get; set; }

            /// <summary>
            /// Gets or sets the certificate manager captured from startup configuration.
            /// </summary>
            public static ICertificateManager? StartedCertificateManager { get; set; }

            protected override void OnServerStarting(
                ApplicationConfiguration configuration)
            {
                StartedApplicationName = configuration.ApplicationName;
                StartedApplicationType = configuration.ApplicationType;
                StartedCertificateManager = configuration.CertificateManager;
                base.OnServerStarting(configuration);
            }

            protected override void OnServerStarted(IServerInternal server)
            {
                StartedType = GetType();
                base.OnServerStarted(server);
            }
        }

        /// <summary>
        /// Publishes the constructed dependency-injection server so startup-hook scenarios can inspect it.
        /// </summary>
        public sealed class StartupHookCaptureServer : DependencyInjectionStandardServer
        {
            /// <summary>
            /// Creates the startup-hook capture server and records its instance.
            /// </summary>
            public StartupHookCaptureServer(
                IServiceProvider services,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
                : base(services, telemetry, timeProvider)
            {
                Volatile.Write(ref s_startedServer, this);
            }

            /// <summary>
            /// Gets the most recently constructed startup-hook capture server.
            /// </summary>
            public static StartupHookCaptureServer? StartedServer => Volatile.Read(ref s_startedServer);

            /// <summary>
            /// Clears the recorded startup-hook server instance.
            /// </summary>
            public static void Reset()
            {
                Volatile.Write(ref s_startedServer, null);
            }

            private static StartupHookCaptureServer? s_startedServer;
        }

        /// <summary>
        /// Captures historian availability and capability nodes at the node-manager-started lifecycle hook.
        /// </summary>
        public sealed class EarlyHistorianCaptureServer : StandardServer
        {
            /// <summary>
            /// Creates the early historian capture server with injected telemetry and time.
            /// </summary>
            public EarlyHistorianCaptureServer(
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
                : base(telemetry, timeProvider)
            {
            }

            /// <summary>
            /// Gets whether the node-manager-started callback has run.
            /// </summary>
            public static bool NodeManagerStarted { get; private set; }

            /// <summary>
            /// Gets the historian provider resolved for the fixture variable during startup.
            /// </summary>
            public static IHistorianProvider? ResolvedProvider { get; private set; }

            /// <summary>
            /// Gets the historical server capabilities node observed during startup.
            /// </summary>
            public static HistoryServerCapabilitiesState? HistoryCapabilities { get; private set; }

            /// <summary>
            /// Clears captured historian state and the fixture's historized variable.
            /// </summary>
            public static void Reset()
            {
                NodeManagerStarted = false;
                ResolvedProvider = null;
                HistoryCapabilities = null;
                HistorizedNodeManager.Reset();
            }

            protected override void OnNodeManagerStarted(IServerInternal server)
            {
                ResolvedProvider = ((IHistorianRegistryProvider)server)
                    .HistorianRegistry.Resolve(
                        HistorizedNodeManager.Variable?.NodeId ?? NodeId.Null);
                HistoryCapabilities = server.DiagnosticsNodeManager
                    .FindPredefinedNode<HistoryServerCapabilitiesState>(
                        ObjectIds.HistoryServerCapabilities);
                NodeManagerStarted = true;
                base.OnNodeManagerStarted(server);
            }
        }

        /// <summary>
        /// Creates the node manager containing the fixture's historized variable.
        /// </summary>
        public sealed class HistorizedNodeManagerFactory : IAsyncNodeManagerFactory
        {
            /// <summary>
            /// Identifies the namespace containing the hosted historian fixture's nodes.
            /// </summary>
            public const string NamespaceUri = "urn:tests:hosted-historian";

            /// <summary>
            /// Gets the namespace URI advertised by the historian node-manager factory.
            /// </summary>
            public ArrayOf<string> NamespacesUris { get; } = [NamespaceUri];

            /// <summary>
            /// Creates a historian fixture node manager for the supplied server and configuration.
            /// </summary>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<IAsyncNodeManager>(
                    new HistorizedNodeManager(server, configuration));
            }
        }

        /// <summary>
        /// Registers a historizing double variable used to observe historian wiring during hosted startup.
        /// </summary>
        public sealed class HistorizedNodeManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Creates the historian fixture node manager in its dedicated namespace.
            /// </summary>
            public HistorizedNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
                : base(
                    server,
                    configuration,
                    NullLogger.Instance,
                    HistorizedNodeManagerFactory.NamespaceUri)
            {
            }

            /// <summary>
            /// Gets the historizing variable created for the current hosting scenario.
            /// </summary>
            public static BaseDataVariableState? Variable { get; private set; }

            /// <summary>
            /// Clears the recorded historizing variable before another scenario.
            /// </summary>
            public static void Reset()
            {
                Variable = null;
            }

            /// <summary>
            /// Creates and registers the historizing double variable with current-read and history-read access.
            /// </summary>
            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                var variable = new BaseDataVariableState(null);
                variable.CreateAsPredefinedNode(SystemContext, cancellationToken);
                variable.NodeId = new NodeId("Historized", NamespaceIndex);
                variable.BrowseName = new QualifiedName("Historized", NamespaceIndex);
                variable.DisplayName = new LocalizedText("Historized");
                variable.DataType = DataTypeIds.Double;
                variable.ValueRank = ValueRanks.Scalar;
                variable.AccessLevel =
                    AccessLevels.CurrentRead | AccessLevels.HistoryRead;
                variable.UserAccessLevel =
                    AccessLevels.CurrentRead | AccessLevels.HistoryRead;
                variable.Historizing = true;
                Variable = variable;

                await AddPredefinedNodeAsync(
                    SystemContext,
                    variable,
                    cancellationToken).ConfigureAwait(false);
            }
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
                IHostedService hostedService = provider
                    .GetServices<IHostedService>()
                    .Single(static service => service is OpcUaServerHostedService);
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

            public IServerContext? ObservedServer { get; private set; }

            public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_invocationCount);
                ObservedServer = server;
                return default;
            }

            private int m_invocationCount;
        }

        private sealed class TransientPreStartupTask :
            IServerPreStartupTask
        {
            public static int InvocationCount =>
                Volatile.Read(ref s_invocationCount);

            public static void Reset()
            {
                Volatile.Write(ref s_invocationCount, 0);
            }

            public ValueTask OnServerStartingAsync(
                IServerContext server,
                CancellationToken cancellationToken = default)
            {
                _ = server;
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref s_invocationCount);
                return default;
            }

            private static int s_invocationCount;
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
