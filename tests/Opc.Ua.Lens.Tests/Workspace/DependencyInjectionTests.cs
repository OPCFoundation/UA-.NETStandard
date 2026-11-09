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

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Identity;
using UaLens.Capabilities;
using UaLens.Connection;
using UaLens.Plugins.Continuity;
using UaLens.Storage;
using UaLens.Themes;
using UaLens.Tests.Diagnose;
using UaLens.Tests.Observe;
using UaLens.ViewModels;
using UaLens.Views;
using UaLens.Workspace;

namespace UaLens.Tests.Workspace;

[TestFixture]
public sealed class DependencyInjectionTests
{
    [Test]
    public void AppearancePreferencesAreSharedAcrossScopesWithoutCreatingTheDesktop()
    {
        IServiceCollection services = new ServiceCollection().AddUaLens();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope first = provider.CreateScope();
        using IServiceScope second = provider.CreateScope();

        AppearancePreferences original = first.ServiceProvider.GetRequiredService<AppearancePreferences>();
        AppearancePreferences shared = second.ServiceProvider.GetRequiredService<AppearancePreferences>();

        Assert.That(shared, Is.SameAs(original));
    }

    [Test]
    public void RegisteringDefaultsPreservesAnExplicitAppearanceStoreAndDoesNotDuplicateIt()
    {
        var appearance = new AppearancePreferences("isolated-appearance.json");
        var services = new ServiceCollection();
        services.AddSingleton(appearance);
        services.AddUaLens();
        services.AddUaLens();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<AppearancePreferences>(), Is.SameAs(appearance));
        Assert.That(
            services.Count(descriptor => descriptor.ServiceType == typeof(AppearancePreferences)), Is.EqualTo(1));
    }

    [Test]
    public void InspectorPreferencesAreSharedAcrossScopes()
    {
        IServiceCollection services = new ServiceCollection().AddUaLens();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope first = provider.CreateScope();
        using IServiceScope second = provider.CreateScope();

        InspectorPreferences original = first.ServiceProvider.GetRequiredService<InspectorPreferences>();
        InspectorPreferences shared = second.ServiceProvider.GetRequiredService<InspectorPreferences>();

        Assert.That(shared, Is.SameAs(original));
    }

    [TestCase(nameof(PluginKind.Alarms))]
    [TestCase(nameof(PluginKind.Models))]
    [TestCase(nameof(PluginKind.Continuity))]
    [TestCase(nameof(PluginKind.PubSub))]
    [TestCase(nameof(PluginKind.Companions))]
    [TestCase(nameof(PluginKind.Pumps))]
    [TestCase(nameof(PluginKind.Weighing))]
    [TestCase(nameof(PluginKind.AssetManagement))]
    [TestCase(nameof(PluginKind.PackML))]
    [TestCase(nameof(PluginKind.Positioning))]
    [TestCase(nameof(PluginKind.GdsManagement))]
    [TestCase(nameof(PluginKind.GdsPush))]
    [TestCase(nameof(PluginKind.SubscriptionBench))]
    [TestCase(nameof(PluginKind.Historian))]
    [TestCase(nameof(PluginKind.EventView))]
    [TestCase(nameof(PluginKind.Subscription))]
    public async Task ShowcaseFactoriesAreIdempotentlyRegisteredAndCreateFreshOfflineDocumentsAsync(string kindName)
    {
        PluginKind kind = Enum.Parse<PluginKind>(kindName);
        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceDispatcher>(_ => InlineWorkspaceDispatcher.Instance);
        services.AddUaLens();
        services.AddUaLens();
        ServiceProvider provider = services.BuildServiceProvider();
        await using var disposal = provider.ConfigureAwait(false);
        IPluginFactory factory = provider.GetRequiredService<IPluginFactory>();
        var context = new ObserveTestHost();
        await using (context.ConfigureAwait(false))
        {
            IPlugin first = factory.Create(kind, context.Host,
                _ => throw new InvalidOperationException("The injected factory was not registered."));
            await using (first.ConfigureAwait(false))
            {
                IPlugin second = factory.Create(kind, context.Host,
                    _ => throw new InvalidOperationException("The injected factory was not registered."));
                await using (second.ConfigureAwait(false))
                {
                    Assert.That(first.Kind, Is.EqualTo(kind));
                    Assert.That(second.Kind, Is.EqualTo(kind));
                    Assert.That(second, Is.Not.SameAs(first));
                    Assert.That(context.Connection.IsConnected, Is.False);
                }
            }
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ContinuityCompositionUsesTheConfiguredBackendWithoutStartingOnRestore(bool registerAfterDefaults)
    {
        var backend = new Mock<IContinuityBackend>(MockBehavior.Strict);
        backend.Setup(value => value.CheckSetup(It.IsAny<ContinuityConfiguration>()))
            .Returns(new ContinuitySetup(ContinuityAvailability.Supported, "Explicit owned continuity setup"));
        backend.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var replacement = new Mock<IContinuityBackendFactory>(MockBehavior.Strict);
        replacement.Setup(value => value.Create(It.IsAny<ContinuityTimeline>())).Returns(backend.Object);
        var sessions = new Mock<IContinuitySessionFactory>(MockBehavior.Strict);
        var services = new ServiceCollection();
        if (registerAfterDefaults)
        {
            services.AddUaLens();
        }
        services.AddSingleton(replacement.Object);
        services.AddSingleton(sessions.Object);
        services.AddUaLens();
        services.AddUaLens();
        ServiceProvider provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            IPluginFactory factory = provider.GetRequiredService<IPluginFactory>();
            var context = new ObserveTestHost();
            await using (context.ConfigureAwait(false))
            {
                var plugin = (ContinuityPlugin)factory.Create(PluginKind.Continuity, context.Host,
                    _ => throw new AssertionException("The registered continuity factory must be used."));
                await using (plugin.ConfigureAwait(false))
                {
                    plugin.SelectedScenario = plugin.Scenarios.Single(
                        choice => choice.Scenario == ContinuityScenario.ConfiguredFailover);
                    await plugin.RestoreStateAsync(plugin.CaptureState()).ConfigureAwait(false);

                    Assert.That(plugin.SetupText, Is.EqualTo("Ready to attempt: Explicit owned continuity setup"));
                    Assert.That(plugin.CanStart, Is.True);
                    Assert.That(context.Connection.IsConnected, Is.False);
                    replacement.Verify(value => value.Create(It.IsAny<ContinuityTimeline>()), Times.Once);
                    backend.Verify(value => value.CheckSetup(It.Is<ContinuityConfiguration>(
                        configuration => configuration.Scenario == ContinuityScenario.ConfiguredFailover)),
                        Times.AtLeastOnce);
                    backend.Verify(value => value.StartAsync(
                        It.IsAny<ContinuityConfiguration>(), It.IsAny<CancellationToken>()), Times.Never);
                    sessions.VerifyNoOtherCalls();
                }
            }
        }
        backend.Verify(value => value.DisposeAsync(), Times.Once);
    }

    [TestCase(nameof(ContinuityScenario.GracefulDurableRestore))]
    [TestCase(nameof(ContinuityScenario.ConfiguredFailover))]
    public async Task ContinuityCompositionPassesConfiguredSessionsToTheLiveHostWithoutOpeningThem(string scenarioName)
    {
        ContinuityScenario scenario = Enum.Parse<ContinuityScenario>(scenarioName);
        var context = new ContinuityStackTestContext("primary");
        EndpointDescription endpoint = context.Session.Object.Endpoint;
        endpoint.UserIdentityTokens = [new UserTokenPolicy(UserTokenType.Anonymous) { PolicyId = "anonymous" }];
        var certificates = new Mock<ICertificateManager>();
        certificates.As<IAsyncDisposable>().Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var configuration = new ApplicationConfiguration(context.Telemetry)
        {
            CertificateManager = certificates.Object,
            SecurityConfiguration = new SecurityConfiguration()
        };
        var owner = new Mock<IConnectionSession>();
        owner.SetupGet(value => value.Session).Returns(context.Session.Object);
        owner.SetupGet(value => value.State).Returns(new ConnectionSessionState(ConnectionPhase.Connected));
        owner.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var transport = new Mock<IConnectionBackend>();
        transport.Setup(value => value.CreateConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(configuration);
        transport.Setup(value => value.DiscoverAsync(
            configuration, endpoint.EndpointUrl!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ArrayOf<EndpointDescription>(new[] { endpoint }));
        transport.Setup(value => value.ConnectAsync(
            configuration, endpoint, It.IsAny<ConnectionProfile>(),
            It.IsAny<IClientIdentityProvider>(), It.IsAny<CancellationToken>())).ReturnsAsync(owner.Object);
        var credentials = new ProfileCredentialProvider();
        var connection = new ConnectionService(context.Telemetry, null, transport.Object, credentials);
        await using (connection.ConfigureAwait(false))
        {
            ConnectionProfile profile = ConnectionProfile.Create(
                endpoint, endpoint.UserIdentityTokens[0], SubscriptionEngineKind.ChannelV2);
            IClientIdentityProvider identity = await credentials.GetAsync(profile, CancellationToken.None)
                .ConfigureAwait(false);
            await connection.ConnectAsync(profile, identity).ConfigureAwait(false);
            var sessions = new Mock<IContinuitySessionFactory>(MockBehavior.Strict);
            sessions.Setup(value => value.CheckSetup(scenario))
                .Returns(new ContinuitySetup(ContinuityAvailability.Supported, "Owned configured session provider"));
            var services = new ServiceCollection();
            services.AddSingleton(sessions.Object);
            services.AddUaLens();
            ServiceProvider provider = services.BuildServiceProvider();
            await using (provider.ConfigureAwait(false))
            {
                IPluginFactory factory = provider.GetRequiredService<IPluginFactory>();
                var host = new PluginHost(new Mock<IPluginWorkspace>().Object, connection,
                    new BrowserViewModel(context.Telemetry, connection), context.Telemetry,
                    new Mock<ICapabilityService>().Object, factory);
                await using (host.ConfigureAwait(false))
                {
                    var plugin = (ContinuityPlugin)factory.Create(PluginKind.Continuity, host,
                        _ => throw new AssertionException("The configured factory must be used."));
                    await using (plugin.ConfigureAwait(false))
                    {
                        plugin.SelectedScenario = plugin.Scenarios.Single(choice => choice.Scenario == scenario);
                        await plugin.RestoreStateAsync(plugin.CaptureState()).ConfigureAwait(false);

                        Assert.That(plugin.SetupText,
                            Is.EqualTo("Ready to attempt: Owned configured session provider"));
                        Assert.That(plugin.CanStart, Is.True);
                        Assert.That(connection.CurrentSession, Is.SameAs(context.Session.Object));
                        sessions.Verify(value => value.CheckSetup(scenario), Times.AtLeastOnce);
                        sessions.VerifyNoOtherCalls();
                        context.Manager.Verify(value => value.Add(
                            It.IsAny<Opc.Ua.Client.Subscriptions.ISubscriptionNotificationHandler>(),
                            It.IsAny<Microsoft.Extensions.Options.IOptionsMonitor<
                                Opc.Ua.Client.Subscriptions.SubscriptionOptions>>()), Times.Never);
                    }
                }
            }
            context.Session.Verify(value => value.DisposeAsync(), Times.Never);
        }
        owner.Verify(value => value.DisposeAsync(), Times.Once);
    }

    [Test]
    public async Task CertificateOperationsUseTheInjectedStoreWithoutCreatingDesktopOrRealStores()
    {
        var store = new Mock<ICertificateStoreAccess>(MockBehavior.Strict);
        store.Setup(value => value.ListAsync(CertStoreKind.Trusted, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArrayOf<System.Security.Cryptography.X509Certificates.X509Certificate2>.Empty);
        var services = new ServiceCollection();
        services.AddSingleton(store.Object);
        services.AddUaLens();
        using ServiceProvider provider = services.BuildServiceProvider();
        Func<ApplicationConfiguration, CertificateStoreOperations> factory =
            provider.GetRequiredService<Func<ApplicationConfiguration, CertificateStoreOperations>>();

        CertificateStoreOperations operations = factory(new ApplicationConfiguration());
        await operations.ReloadAsync(CertStoreKind.Trusted).ConfigureAwait(false);

        Assert.That(operations.LoadingStatus, Is.EqualTo("Trusted: 0 certificate(s)."));
        store.Verify(value => value.ListAsync(CertStoreKind.Trusted, It.IsAny<CancellationToken>()), Times.Once);
        store.VerifyNoOtherCalls();
    }

    [Test]
    public async Task WriteOperationFactoryRemainsReplaceableAndIdempotent()
    {
        var session = new Mock<ISession>(MockBehavior.Strict);
        var operation = new WriteValueOperation(new NodeId("value", 0), session.Object);
        await using (operation.ConfigureAwait(false))
        {
            WriteValueOperationFactory replacement = (_, _) => operation;
            var services = new ServiceCollection();
            services.AddSingleton(replacement);
            services.AddUaLens();
            services.AddUaLens();
            using ServiceProvider provider = services.BuildServiceProvider();

            WriteValueOperationFactory factory = provider.GetRequiredService<WriteValueOperationFactory>();
            Assert.That(factory(new NodeId("value", 0), session.Object), Is.SameAs(operation));
            Assert.That(services.Count(value => value.ServiceType == typeof(WriteValueOperationFactory)), Is.EqualTo(1));
            session.VerifyNoOtherCalls();
        }
    }
}
