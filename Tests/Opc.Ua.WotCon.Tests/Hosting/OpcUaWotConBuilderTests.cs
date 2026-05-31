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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Server.Hosting;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Hosting;
using Opc.Ua.WotCon.Tests.Providers;

namespace Opc.Ua.WotCon.Tests.Hosting
{
    /// <summary>
    /// Unit tests for the WoT Connectivity dependency-injection surface:
    /// <c>IOpcUaBuilder.AddWotConServer(...)</c> and
    /// <c>IOpcUaBuilder.AddWotConClient(...)</c>.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    public sealed class OpcUaWotConBuilderTests
    {
        [Test]
        public void AddWotConServerThrowsForNullArgs()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(() => OpcUaWotConServerBuilderExtensions
                .AddWotConServer(null!, _ => { }),
                Throws.ArgumentNullException);

            Assert.That(() => builder.AddWotConServer(
                (Action<WotConnectivityServerOptions>)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWotConServerRegistersFactoryAndNodeManager()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddLogging();

            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddServer(o =>
            {
                o.ApplicationName = "Test";
                o.ApplicationUri = "urn:test";
                o.ProductUri = "urn:test:product";
            });

            builder.AddWotConServer(o =>
            {
                o.AssetNamespaceUri =
                    WotConnectivityServerOptions.DefaultAssetNamespaceUri;
            });

            using ServiceProvider sp = services.BuildServiceProvider();

            WotConnectivityNodeManagerFactory factory =
                sp.GetRequiredService<WotConnectivityNodeManagerFactory>();
            Assert.That(factory, Is.Not.Null);

            OpcUaServerNodeManagerRegistration[] regs = sp
                .GetServices<OpcUaServerNodeManagerRegistration>()
                .ToArray();
            Assert.That(regs, Has.Length.GreaterThanOrEqualTo(1));
            Assert.That(
                regs.Any(r => ReferenceEquals(r.SyncFactory, factory)),
                Is.True,
                "Expected at least one OpcUaServerNodeManagerRegistration wrapping the WotCon factory.");
        }

        [Test]
        public void AddWotConServerThrowsOnDuplicateRegistration()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            builder.AddServer(o =>
            {
                o.ApplicationName = "Test";
                o.ApplicationUri = "urn:test";
                o.ProductUri = "urn:test:product";
            });

            builder.AddWotConServer(_ => { });

            Assert.That(() => builder.AddWotConServer(_ => { }),
                Throws.InvalidOperationException);
        }

        [Test]
        public void AddWotConClientThrowsForNullArgs()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(() => OpcUaWotConClientBuilderExtensions
                .AddWotConClient(null!, configure: null),
                Throws.ArgumentNullException);

            Assert.That(() => builder.AddWotConClient(
                (Microsoft.Extensions.Configuration.IConfiguration)null!),
                Throws.ArgumentNullException);

            Assert.That(() => builder.AddWotConClient(
                (Microsoft.Extensions.Configuration.IConfigurationSection)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWotConClientRegistersClientFactory()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            builder.AddWotConClient();

            using ServiceProvider sp = services.BuildServiceProvider();

            Func<CancellationToken, Task<WotConnectivityClient>>? factory =
                sp.GetService<Func<CancellationToken, Task<WotConnectivityClient>>>();
            Assert.That(factory, Is.Not.Null);
        }

        [Test]
        public void AddWotConClientReturnsBuilder()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddWotConClient(o =>
            {
                o.LazyConnect = false;
            });

            Assert.That(returned, Is.SameAs(builder));
        }

        // ----------------------------------------------------------------
        // AddWotConServer — IConfiguration / IConfigurationSection overloads
        // ----------------------------------------------------------------

        [Test]
        public void AddWotConServerFromIConfigurationThrowsForNullConfiguration()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => builder.AddWotConServer((IConfiguration)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWotConServerFromIConfigurationSectionThrowsForNullSection()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            // Null builder
            Assert.That(
                () => OpcUaWotConServerBuilderExtensions
                    .AddWotConServer(null!, (IConfigurationSection)new Mock<IConfigurationSection>().Object),
                Throws.ArgumentNullException);

            // Null section
            Assert.That(
                () => builder.AddWotConServer((IConfigurationSection)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWotConServerFromIConfigurationRegistersFactory()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddLogging();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddServer(o =>
            {
                o.ApplicationName = "Test";
                o.ApplicationUri = "urn:test";
                o.ProductUri = "urn:test:product";
            });

            IConfiguration config = new ConfigurationBuilder().Build();
            builder.AddWotConServer(config);

            using ServiceProvider sp = services.BuildServiceProvider();
            WotConnectivityNodeManagerFactory factory =
                sp.GetRequiredService<WotConnectivityNodeManagerFactory>();
            Assert.That(factory, Is.Not.Null);
        }

        [Test]
        public void AddWotConServerFromIConfigurationSectionRegistersFactory()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddLogging();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddServer(o =>
            {
                o.ApplicationName = "Test";
                o.ApplicationUri = "urn:test";
                o.ProductUri = "urn:test:product";
            });

            IConfigurationSection section = new ConfigurationBuilder().Build()
                .GetSection("OpcUa:WotCon:Server");
            builder.AddWotConServer(section);

            using ServiceProvider sp = services.BuildServiceProvider();
            WotConnectivityNodeManagerFactory factory =
                sp.GetRequiredService<WotConnectivityNodeManagerFactory>();
            Assert.That(factory, Is.Not.Null);
        }

        // ----------------------------------------------------------------
        // WotConServerBuilder chaining methods
        // ----------------------------------------------------------------

        [Test]
        public void WotConServerBuilderAddAssetProviderGenericRegistersFactory()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            IWotConServerBuilder wotBuilder = builder.AddWotConServer(_ => { });

            wotBuilder.AddAssetProvider<SimulatedWotAssetProviderFactory>();

            using ServiceProvider sp = services.BuildServiceProvider();
            IEnumerable<IWotAssetProviderFactory> factories =
                sp.GetServices<IWotAssetProviderFactory>();
            Assert.That(
                factories.OfType<SimulatedWotAssetProviderFactory>().Any(),
                Is.True);
        }

        [Test]
        public void WotConServerBuilderAddAssetProviderFuncRegistersFactory()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            IWotConServerBuilder wotBuilder = builder.AddWotConServer(_ => { });

            var canned = new SimulatedWotAssetProviderFactory();
            wotBuilder.AddAssetProvider(_ => canned);

            using ServiceProvider sp = services.BuildServiceProvider();
            IEnumerable<IWotAssetProviderFactory> factories =
                sp.GetServices<IWotAssetProviderFactory>();
            Assert.That(
                factories.Any(f => ReferenceEquals(f, canned)),
                Is.True);
        }

        [Test]
        public void WotConServerBuilderAddAssetProviderFuncThrowsForNull()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            IWotConServerBuilder wotBuilder = builder.AddWotConServer(_ => { });

            Assert.That(
                () => wotBuilder.AddAssetProvider(
                    (Func<IServiceProvider, IWotAssetProviderFactory>)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WotConServerBuilderAddDiscoveryProviderRegistersProvider()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            IWotConServerBuilder wotBuilder = builder.AddWotConServer(_ => { });

            wotBuilder.AddDiscoveryProvider<SimulatedWotDiscoveryProvider>();

            using ServiceProvider sp = services.BuildServiceProvider();
            IWotAssetDiscoveryProvider? discovery =
                sp.GetService<IWotAssetDiscoveryProvider>();
            Assert.That(discovery, Is.InstanceOf<SimulatedWotDiscoveryProvider>());
        }

        // ----------------------------------------------------------------
        // AddWotConClient — IConfiguration / IConfigurationSection overloads
        // ----------------------------------------------------------------

        [Test]
        public void AddWotConClientFromIConfigurationRegistersClientFactory()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IConfiguration config = new ConfigurationBuilder().Build();
            builder.AddWotConClient(config);

            using ServiceProvider sp = services.BuildServiceProvider();
            Func<CancellationToken, Task<WotConnectivityClient>>? factory =
                sp.GetService<Func<CancellationToken, Task<WotConnectivityClient>>>();
            Assert.That(factory, Is.Not.Null);
        }

        [Test]
        public void AddWotConClientFromIConfigurationSectionRegistersClientFactory()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IConfigurationSection section = new ConfigurationBuilder().Build()
                .GetSection("OpcUa:WotCon:Client");
            builder.AddWotConClient(section);

            using ServiceProvider sp = services.BuildServiceProvider();
            Func<CancellationToken, Task<WotConnectivityClient>>? factory =
                sp.GetService<Func<CancellationToken, Task<WotConnectivityClient>>>();
            Assert.That(factory, Is.Not.Null);
        }
        [Test]
        public void AddWotConClientFromIConfigurationSectionThrowsForNullBuilder()
        {
            IConfigurationSection section = new ConfigurationBuilder().Build()
                .GetSection("OpcUa:WotCon:Client");
            Assert.That(
                () => OpcUaWotConClientBuilderExtensions.AddWotConClient(null!, section),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WotConClientOptionsDefaultsToLazyConnect()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddWotConClient();

            using ServiceProvider sp = services.BuildServiceProvider();
            IOptions<Opc.Ua.WotCon.Client.Hosting.WotConClientOptions> opts =
                sp.GetRequiredService<IOptions<Opc.Ua.WotCon.Client.Hosting.WotConClientOptions>>();

            Assert.That(opts.Value.LazyConnect, Is.True);
        }
    }
}
