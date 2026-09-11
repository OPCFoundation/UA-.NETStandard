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
using UaLens.Connection;
using UaLens.Themes;
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

    [TestCase(nameof(PluginKind.Alarms))]
    [TestCase(nameof(PluginKind.Models))]
    [TestCase(nameof(PluginKind.Continuity))]
    [TestCase(nameof(PluginKind.PubSub))]
    [TestCase(nameof(PluginKind.Companions))]
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
        using ServiceProvider provider = services.BuildServiceProvider();
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
