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
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using UaLens.Themes;
using UaLens.Tests.Observe;
using UaLens.ViewModels;

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
    public async Task ShowcaseFactoriesAreIdempotentlyRegisteredAndCreateFreshOfflineDocumentsAsync(string kindName)
    {
        PluginKind kind = Enum.Parse<PluginKind>(kindName);
        var services = new ServiceCollection();
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
}
