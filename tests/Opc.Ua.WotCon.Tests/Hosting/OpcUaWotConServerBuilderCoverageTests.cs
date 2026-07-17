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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Hosting;
using Opc.Ua.WotCon.Server.ThingDescriptions;
using Opc.Ua.WotCon.Tests.Providers;

namespace Opc.Ua.WotCon.Tests.Hosting
{
    /// <summary>
    /// Additional coverage for the <c>OpcUaWotConServerBuilderExtensions</c>
    /// dependency-injection surface: configuration-bound overloads, asset /
    /// discovery provider registration and one-shot transport overloads.
    /// Does not start the hosted server.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Category("Builder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class OpcUaWotConServerBuilderCoverageTests
    {
        [Test]
        public void AddWotConServerWithConfigurationBindsOptions()
        {
            var configData = new Dictionary<string, string?>
            {
                ["OpcUa:WotCon:Server:AssetNamespaceUri"] = "http://test/wot/assets",
                ["OpcUa:WotCon:Server:MaxThingDescriptionSize"] = "2048"
            };

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            var services = new ServiceCollection();
            services.AddOpcUa().AddWotConServer(configuration);

            using ServiceProvider sp = services.BuildServiceProvider();
            WotConnectivityServerOptions options =
                sp.GetRequiredService<IOptions<WotConnectivityServerOptions>>().Value;

            Assert.That(options.AssetNamespaceUri, Is.EqualTo("http://test/wot/assets"));
            Assert.That(options.MaxThingDescriptionSize, Is.EqualTo(2048));
        }

        [Test]
        public void AddWotConServerWithConfigurationSectionBindsOptions()
        {
            var configData = new Dictionary<string, string?>
            {
                ["OpcUa:WotCon:Server:AssetNamespaceUri"] = "http://test/wot/section"
            };

            IConfigurationSection section = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build()
                .GetSection(OpcUaWotConServerBuilderExtensions.DefaultConfigurationSection);

            var services = new ServiceCollection();
            services.AddOpcUa().AddWotConServer(section);

            using ServiceProvider sp = services.BuildServiceProvider();
            WotConnectivityServerOptions options =
                sp.GetRequiredService<IOptions<WotConnectivityServerOptions>>().Value;

            Assert.That(options.AssetNamespaceUri, Is.EqualTo("http://test/wot/section"));
        }

        [Test]
        public void AddWotConServerConfigurationOverloadsThrowForNullArgs()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => builder.AddWotConServer((IConfiguration)null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => builder.AddWotConServer((IConfigurationSection)null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaWotConServerBuilderExtensions.AddWotConServer(
                    null!, new ConfigurationBuilder().Build().GetSection("OpcUa")),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWotConnectivityServerThrowsForNullArgs()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => OpcUaWotConServerBuilderExtensions.AddWotConnectivityServer(
                    null!, _ => { }, _ => { }),
                Throws.ArgumentNullException);
            Assert.That(
                () => builder.AddWotConnectivityServer(null!, _ => { }),
                Throws.ArgumentNullException);
            Assert.That(
                () => builder.AddWotConnectivityServer(_ => { }, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddAssetProviderGenericRegistersFactory()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddWotConServer(_ => { })
                .AddAssetProvider<TestAssetProviderFactory>();

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(
                sp.GetRequiredService<IWotAssetProviderFactory>(),
                Is.InstanceOf<TestAssetProviderFactory>());
            Assert.That(sp.GetRequiredService<TestAssetProviderFactory>(), Is.Not.Null);
        }

        [Test]
        public void AddAssetProviderFactoryDelegateRegistersFactory()
        {
            var factory = new TestAssetProviderFactory();
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddWotConServer(_ => { })
                .AddAssetProvider(_ => factory);

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<IWotAssetProviderFactory>(), Is.SameAs(factory));
        }

        [Test]
        public void AddAssetProviderThrowsForNullFactory()
        {
            var services = new ServiceCollection();
            IWotConServerBuilder builder = services.AddOpcUa().AddWotConServer(_ => { });

            Assert.That(
                () => builder.AddAssetProvider(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddDiscoveryProviderRegistersProvider()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddWotConServer(_ => { })
                .AddDiscoveryProvider<SimulatedWotDiscoveryProvider>();

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(
                sp.GetRequiredService<IWotAssetDiscoveryProvider>(),
                Is.InstanceOf<SimulatedWotDiscoveryProvider>());
        }

        [Test]
        public void WotConHttpsAndWssOneShotOverloadsReturnSameBuilder()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            IOpcUaBuilder opcUa = services.AddOpcUa();
            opcUa.AddServer(o =>
            {
                o.ApplicationName = "Test";
                o.ApplicationUri = "urn:test";
                o.ProductUri = "urn:test:product";
            });
            IWotConServerBuilder builder = opcUa.AddWotConServer(_ => { });

            Assert.That(builder.AddHttpsTransport(_ => { }), Is.SameAs(builder));
            Assert.That(builder.AddWssTransport(_ => { }), Is.SameAs(builder));
        }

        [Test]
        public void WotConTransportForwardersThrowForNullBuilder()
        {
            Assert.That(
                () => OpcUaWotConServerBuilderExtensions.AddOpcTcpTransport(null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaWotConServerBuilderExtensions.AddHttpsTransport(null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaWotConServerBuilderExtensions.AddHttpsTransport(null!, _ => { }),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaWotConServerBuilderExtensions.AddWssTransport(null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaWotConServerBuilderExtensions.AddWssTransport(null!, _ => { }),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaWotConServerBuilderExtensions.AddReverseConnect(null!, _ => { }),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WotConReverseConnectThrowsForNullConfigure()
        {
            var services = new ServiceCollection();
            IWotConServerBuilder builder = services.AddOpcUa().AddWotConServer(_ => { });

            Assert.That(
                () => builder.AddReverseConnect(null!),
                Throws.ArgumentNullException);
        }

        private sealed class TestAssetProviderFactory : IWotAssetProviderFactory
        {
            public IReadOnlyCollection<string> SupportedBindings { get; } = ["sim"];

            public bool CanHandle(ThingDescription thingDescription)
            {
                return false;
            }

            public ValueTask<IWotAssetProvider> ConnectAsync(
                ThingDescription thingDescription,
                CancellationToken ct)
            {
                throw new NotSupportedException();
            }
        }
    }
}
