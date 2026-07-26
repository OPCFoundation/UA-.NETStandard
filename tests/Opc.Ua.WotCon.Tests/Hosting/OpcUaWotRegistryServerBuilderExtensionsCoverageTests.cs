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

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Hosting
{
    /// <summary>
    /// Exercises every overload of
    /// <c>IOpcUaBuilder.AddWotRegistryServer(...)</c> defined in
    /// <c>OpcUaWotRegistryServerBuilderExtensions</c>: null-guard validation,
    /// options binding, and the set of singleton services that must be
    /// resolvable from the built <see cref="ServiceProvider"/> without a
    /// running OPC UA server.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Category("Builder")]
    [Parallelizable]
    public sealed class OpcUaWotRegistryServerBuilderExtensionsCoverageTests
    {
        [Test]
        public void AddWotRegistryServerWithNoConfigureRegistersServices()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            builder.AddWotRegistryServer();

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<IWotRegistryService>(), Is.Not.Null);
            Assert.That(sp.GetRequiredService<WotRegistryServerOptions>(), Is.Not.Null);
        }

        [Test]
        public void AddWotRegistryServerWithConfigureActionAppliesOptions()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            builder.AddWotRegistryServer(o => o.StorageFolder = "custom-folder");

            using ServiceProvider sp = services.BuildServiceProvider();

            WotRegistryServerOptions options =
                sp.GetRequiredService<IOptions<WotRegistryServerOptions>>().Value;
            Assert.That(options.StorageFolder, Is.EqualTo("custom-folder"));
        }

        [Test]
        public void AddWotRegistryServerWithNullConfigureActionDoesNotThrow()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => builder.AddWotRegistryServer(configure: null),
                Throws.Nothing);
        }

        [Test]
        public void AddWotRegistryServerWithNullBuilderThrowsArgumentNull()
        {
            Assert.That(
                () => OpcUaWotRegistryServerBuilderExtensions.AddWotRegistryServer(
                    null!, configure: null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWotRegistryServerWithConfigurationBindsOptions()
        {
            var configData = new Dictionary<string, string?>
            {
                [$"{OpcUaWotRegistryServerBuilderExtensions.DefaultConfigurationSection}:StorageFolder"] =
                    "my-registry-data"
            };

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            builder.AddWotRegistryServer(configuration);

            using ServiceProvider sp = services.BuildServiceProvider();

            WotRegistryServerOptions options =
                sp.GetRequiredService<IOptions<WotRegistryServerOptions>>().Value;
            Assert.That(options.StorageFolder, Is.EqualTo("my-registry-data"));
        }

        [Test]
        public void AddWotRegistryServerWithNullConfigurationThrowsArgumentNull()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => builder.AddWotRegistryServer((IConfiguration)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWotRegistryServerWithConfigurationSectionBindsOptions()
        {
            var configData = new Dictionary<string, string?>
            {
                [$"{OpcUaWotRegistryServerBuilderExtensions.DefaultConfigurationSection}:StorageFolder"] =
                    "section-folder"
            };

            IConfigurationSection section = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build()
                .GetSection(OpcUaWotRegistryServerBuilderExtensions.DefaultConfigurationSection);

            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            builder.AddWotRegistryServer(section);

            using ServiceProvider sp = services.BuildServiceProvider();

            WotRegistryServerOptions options =
                sp.GetRequiredService<IOptions<WotRegistryServerOptions>>().Value;
            Assert.That(options.StorageFolder, Is.EqualTo("section-folder"));
        }

        [Test]
        public void AddWotRegistryServerWithNullSectionThrowsArgumentNull()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => builder.AddWotRegistryServer((IConfigurationSection)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWotRegistryServerWithNullBuilderAndSectionThrowsArgumentNull()
        {
            IConfigurationSection section = new ConfigurationBuilder()
                .Build()
                .GetSection("OpcUa");

            Assert.That(
                () => OpcUaWotRegistryServerBuilderExtensions.AddWotRegistryServer(
                    null!, section),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWotRegistryServerReturnsTheSameBuilder()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddWotRegistryServer();

            Assert.That(returned, Is.SameAs(builder));
        }

        [Test]
        public void AddWotRegistryServerRegistersIWotRegistryServiceAsSingleton()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddWotRegistryServer();

            using ServiceProvider sp = services.BuildServiceProvider();

            IWotRegistryService first = sp.GetRequiredService<IWotRegistryService>();
            IWotRegistryService second = sp.GetRequiredService<IWotRegistryService>();

            Assert.That(first, Is.SameAs(second),
                "IWotRegistryService must be registered as a singleton.");
        }

        [Test]
        public void AddWotRegistryServerRegistersWotMaterializationCoordinator()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddWotRegistryServer();

            Assert.That(
                services.Any(s => s.ServiceType == typeof(WotMaterializationCoordinator)),
                Is.True,
                "WotMaterializationCoordinator must be registered in the service collection.");
        }

        [Test]
        public void AddWotRegistryServerRegistersWotRegistryNodeManagerFactory()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddWotRegistryServer();

            Assert.That(
                services.Any(s => s.ServiceType == typeof(WotRegistryNodeManagerFactory)),
                Is.True,
                "WotRegistryNodeManagerFactory must be registered in the service collection.");
        }

        [Test]
        public void AddWotRegistryServerRegistersOpcUaServerNodeManagerRegistration()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddWotRegistryServer();

            Assert.That(
                services.Any(s => s.ServiceType == typeof(OpcUaServerNodeManagerRegistration)),
                Is.True,
                "OpcUaServerNodeManagerRegistration must be registered in the service collection.");
        }

        [Test]
        public void AddWotRegistryServerRegistersBinderRegistryInterfaces()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddWotRegistryServer();

            using ServiceProvider sp = services.BuildServiceProvider();

            IWotBinderRegistry registry = sp.GetRequiredService<IWotBinderRegistry>();
            IWotBindingChannelFactory channelFactory =
                sp.GetRequiredService<IWotBindingChannelFactory>();
            WotProtocolBinderRegistry concrete =
                sp.GetRequiredService<WotProtocolBinderRegistry>();

            Assert.That(registry, Is.SameAs(concrete));
            Assert.That(channelFactory, Is.SameAs(concrete));
        }

        [Test]
        public void AddWotRegistryServerRegistersTargetVariableResolver()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddWotRegistryServer();

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(
                sp.GetRequiredService<IWotTargetVariableResolver>(),
                Is.Not.Null);
        }

        [Test]
        public void AddWotRegistryServerCalledTwiceDoesNotDoubleRegisterSingleton()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            builder.AddWotRegistryServer(o => o.StorageFolder = "first");
            builder.AddWotRegistryServer(o => o.StorageFolder = "second");

            using ServiceProvider sp = services.BuildServiceProvider();

            IWotRegistryService first = sp.GetRequiredService<IWotRegistryService>();
            IWotRegistryService second = sp.GetRequiredService<IWotRegistryService>();

            Assert.That(first, Is.SameAs(second),
                "Calling AddWotRegistryServer twice must still yield a single singleton.");
        }

        [Test]
        public void AddWotRegistryServerUsesInMemoryStoreWhenNoFolderConfigured()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddWotRegistryServer(o => o.StorageFolder = null);

            using ServiceProvider sp = services.BuildServiceProvider();

            IWotRegistryService svc = sp.GetRequiredService<IWotRegistryService>();
            Assert.That(svc, Is.InstanceOf<WotRegistryService>());
        }

        [Test]
        public void AddWotRegistryServerDefaultConfigSectionIsConstant()
        {
            Assert.That(
                OpcUaWotRegistryServerBuilderExtensions.DefaultConfigurationSection,
                Is.EqualTo("OpcUa:WotConRegistry:Server"));
        }
    }
}
