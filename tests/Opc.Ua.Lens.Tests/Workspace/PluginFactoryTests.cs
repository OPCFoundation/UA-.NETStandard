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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Capabilities;
using UaLens.Connection;
using UaLens.Telemetry;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.Workspace;

[TestFixture]
public sealed class PluginFactoryTests
{
    [TestCase("Subscription")]
    [TestCase("GdsPush")]
    [TestCase("GdsManagement")]
    [TestCase("GdsDiscovery")]
    [TestCase("Performance")]
    [TestCase("EventView")]
    [TestCase("Historian")]
    [TestCase("FileSystem")]
    [TestCase("CertificateManager")]
    [TestCase("RoleManagement")]
    [TestCase("UserManagement")]
    [TestCase("SubscriptionBench")]
    public async Task EveryExistingFactoryRemainsDirectlyConstructibleOffline(string kindName)
    {
        PluginKind kind = Enum.Parse<PluginKind>(kindName);
        var telemetry = new AppTelemetryContext(new LogRingBuffer(32));
        var connection = new ConnectionService(telemetry);
        await using (connection.ConfigureAwait(false))
        {
            var workspace = new Mock<IPluginWorkspace>();
            var host = new PluginHost(
                workspace.Object, connection, new BrowserViewModel(telemetry, connection), telemetry);
            await using (host.ConfigureAwait(false))
            {
                IPlugin plugin = PluginRegistry.For(kind).Factory(host);
                await using (plugin.ConfigureAwait(false))
                {
                    Assert.That(plugin.Kind, Is.EqualTo(kind));
                    Assert.That(plugin.Title, Is.Not.Empty);
                    Assert.That(host.Session, Is.Null);
                    Assert.That(connection.IsConnected, Is.False);
                    Assert.That(host.Factory, Is.SameAs(PluginFactory.Default));
                    Assert.That(host.Capabilities, Is.TypeOf<CapabilityService>());
                }
            }
        }
    }

    [Test]
    public async Task TypedFactoryRegistrationReachesTheCachedHostAndCreatesFreshWorkspaceOwnedDocuments()
    {
        var services = CreateServices();
        var factory = new RecordingFactory();
        services.AddSingleton(factory);
        services.AddUaLensPluginFactory<RecordingFactory>(
            PluginKind.EventView, static (typed, host) => typed.Create(host));
        services.AddUaLens();
        services.AddUaLens();
        ServiceProvider provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            MainViewModel model = provider.GetRequiredService<MainViewModel>();
            PluginHost host = provider.GetRequiredService<PluginHost>();

            IPlugin first = await model.OpenToolAsync(PluginKind.EventView).ConfigureAwait(false);
            IPlugin second = await model.OpenToolAsync(PluginKind.EventView).ConfigureAwait(false);
            await model.Workspace.CloseAsync(first).ConfigureAwait(false);

            Assert.That(model.CreatePluginHost(), Is.SameAs(host));
            Assert.That(factory.LastHost, Is.SameAs(host));
            Assert.That(host.Capabilities, Is.SameAs(provider.GetRequiredService<ICapabilityService>()));
            Assert.That(host.Factory, Is.SameAs(provider.GetRequiredService<IPluginFactory>()));
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(factory.CreatedCount, Is.EqualTo(2));
            Assert.That(factory.ConnectionNotifications, Is.EqualTo(2));
            Assert.That(factory.DisposedCount, Is.EqualTo(1));
            Assert.That(model.ActiveDocument, Is.SameAs(second));
            Assert.That(model.Connection.IsConnected, Is.False);
            Assert.That(services.Count(entry => entry.ServiceType == typeof(IPluginFactory)), Is.EqualTo(1));
            Assert.That(services.Count(entry => entry.ServiceType == typeof(ICapabilityService)), Is.EqualTo(1));

            await model.Workspace.CloseAsync(second).ConfigureAwait(false);
            Assert.That(factory.DisposedCount, Is.EqualTo(2));
        }
        Assert.That(factory.DisposedCount, Is.EqualTo(2));
    }

    [Test]
    public async Task InjectedFactoriesLeaveUnmodifiedKindsOnTheirDirectFallback()
    {
        var services = CreateServices();
        var factory = new RecordingFactory();
        services.AddSingleton(factory);
        services.AddUaLensPluginFactory<RecordingFactory>(
            PluginKind.EventView, static (typed, host) => typed.Create(host));
        services.AddUaLens();
        ServiceProvider provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            MainViewModel model = provider.GetRequiredService<MainViewModel>();
            IPlugin monitor = await model.OpenToolAsync(PluginKind.Subscription).ConfigureAwait(false);

            Assert.That(monitor, Is.TypeOf<SubscriptionViewModel>());
            Assert.That(((SubscriptionViewModel)monitor).Adapter, Is.Null);
            Assert.That(factory.CreatedCount, Is.Zero);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CompositionAllowsExplicitFactoryAndCapabilityReplacement(bool replaceAfterDefaults)
    {
        var services = CreateServices();
        var recording = new RecordingFactory();
        var factory = new Mock<IPluginFactory>();
        var capability = new Mock<ICapabilityService>();
        var probe = new Mock<ICapabilityProbe>();
        factory.Setup(value => value.Create(
            PluginKind.EventView, It.IsAny<PluginHost>(), It.IsAny<Func<PluginHost, IPlugin>>()))
            .Returns((PluginKind _, PluginHost host, Func<PluginHost, IPlugin> _) => recording.Create(host));
        if (replaceAfterDefaults)
        {
            services.AddUaLens();
        }
        services.Replace(ServiceDescriptor.Singleton(factory.Object));
        services.Replace(ServiceDescriptor.Singleton(capability.Object));
        services.Replace(ServiceDescriptor.Singleton(probe.Object));
        services.AddUaLens();
        ServiceProvider provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            MainViewModel model = provider.GetRequiredService<MainViewModel>();
            IPlugin document = await model.OpenToolAsync(PluginKind.EventView).ConfigureAwait(false);

            Assert.That(document.Kind, Is.EqualTo(PluginKind.EventView));
            Assert.That(model.CreatePluginHost().Factory, Is.SameAs(factory.Object));
            Assert.That(model.CreatePluginHost().Capabilities, Is.SameAs(capability.Object));
            Assert.That(provider.GetRequiredService<ICapabilityProbe>(), Is.SameAs(probe.Object));
            factory.Verify(value => value.Create(
                PluginKind.EventView, model.CreatePluginHost(), It.IsAny<Func<PluginHost, IPlugin>>()), Times.Once);
            probe.VerifyNoOtherCalls();
        }
        Assert.That(recording.DisposedCount, Is.EqualTo(1));
    }

    [Test]
    public async Task CancelledInitializationDisposesTheInjectedDocumentBeforeReturning()
    {
        var services = CreateServices();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new RecordingFactory
        {
            ConnectionWork = async token =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
            }
        };
        services.AddSingleton(factory);
        services.AddUaLensPluginFactory<RecordingFactory>(
            PluginKind.EventView, static (typed, host) => typed.Create(host));
        services.AddUaLens();
        ServiceProvider provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            MainViewModel model = provider.GetRequiredService<MainViewModel>();
            using var cancellation = new CancellationTokenSource();
            Task<IPlugin> opening = model.OpenToolAsync(PluginKind.EventView, cancellationToken: cancellation.Token);
            Task first = await Task.WhenAny(entered.Task, opening).WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            if (first == opening)
            {
                await opening.ConfigureAwait(false);
            }
            Assert.That(entered.Task.IsCompletedSuccessfully, Is.True, "Document initialization must start.");

            await cancellation.CancelAsync().ConfigureAwait(false);
            await Assert.ThatAsync(() => opening, Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);

            Assert.That(factory.CreatedCount, Is.EqualTo(1));
            Assert.That(factory.DisposedCount, Is.EqualTo(1));
            Assert.That(model.Tabs, Is.Empty);
            Assert.That(model.ActiveDocument, Is.Null);
        }
        Assert.That(factory.DisposedCount, Is.EqualTo(1));
    }

    [Test]
    public void DuplicateKindFactoriesFailCompositionInsteadOfSilentlySelectingOne()
    {
        var factory = new RecordingFactory();
        Assert.That(() => new PluginFactory(
            [
                new PluginFactoryRegistration(PluginKind.EventView, factory.Create),
                new PluginFactoryRegistration(PluginKind.EventView, factory.Create)
            ]), Throws.TypeOf<ArgumentException>().With.Message.Contains("already registered"));
        Assert.That(factory.CreatedCount, Is.Zero);
    }

    [Test]
    public async Task DirectHostDisposesOnlyCapabilitiesItOwns()
    {
        var telemetry = new AppTelemetryContext(new LogRingBuffer(32));
        var connection = new ConnectionService(telemetry);
        await using (connection.ConfigureAwait(false))
        {
            var workspace = new Mock<IPluginWorkspace>();
            var external = new Mock<ICapabilityService>();
            var host = new PluginHost(workspace.Object, connection,
                new BrowserViewModel(telemetry, connection), telemetry, external.Object, null);

            await host.DisposeAsync().ConfigureAwait(false);

            Assert.That(host.Capabilities, Is.SameAs(external.Object));
            external.Verify(value => value.DisposeAsync(), Times.Never);

            var owned = new PluginHost(workspace.Object, connection,
                new BrowserViewModel(telemetry, connection), telemetry);
            await owned.DisposeAsync().ConfigureAwait(false);
            Assert.That(() => owned.Capabilities.GetCached(
                new CapabilityRequest(ObjectIds.Server, CapabilityOperation.Browse)),
                Throws.TypeOf<ObjectDisposedException>());
        }
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceDispatcher>(_ => InlineWorkspaceDispatcher.Instance);
        return services;
    }

    private sealed class RecordingFactory
    {
        public int CreatedCount { get; private set; }
        public int ConnectionNotifications { get; private set; }
        public int DisposedCount { get; private set; }
        public PluginHost? LastHost { get; private set; }
        public Func<CancellationToken, Task> ConnectionWork { get; init; } = _ => Task.CompletedTask;

        public IPlugin Create(PluginHost host)
        {
            ArgumentNullException.ThrowIfNull(host);
            CreatedCount++;
            LastHost = host;
            var document = new Mock<IPlugin>();
            Mock<IWorkspaceDocument> lifecycle = document.As<IWorkspaceDocument>();
            document.SetupGet(value => value.Kind).Returns(PluginKind.EventView);
            document.SetupProperty(value => value.Title, "Injected document");
            lifecycle.Setup(value => value.OnConnectionStateChangedAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) =>
                {
                    ConnectionNotifications++;
                    return ConnectionWork(token);
                });
            document.Setup(value => value.DisposeAsync()).Returns(() =>
            {
                DisposedCount++;
                return ValueTask.CompletedTask;
            });
            return document.Object;
        }
    }
}
