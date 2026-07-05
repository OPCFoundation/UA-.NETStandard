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
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;

namespace Opc.Ua.PubSub.Tests.DependencyInjection
{
    /// <summary>
    /// Unit tests for
    /// <see cref="OpcUaPubSubBuilderExtensions"/>.
    /// </summary>
    [TestFixture]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class OpcUaPubSubBuilderExtensionsTests
    {
        [Test]
        public void AddPubSub_RegistersCoreServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddPubSub();
            ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetService<IDataSetMetaDataRegistry>(), Is.Not.Null);
            Assert.That(sp.GetService<IPubSubDiagnostics>(), Is.Not.Null);
            Assert.That(sp.GetService<IPubSubScheduler>(), Is.Not.Null);
        }

        [Test]
        public void AddPubSub_RegistersHostedService()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            services.AddLogging();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddPubSub();
            ServiceProvider sp = services.BuildServiceProvider();
            IEnumerable<IHostedService> hosted = sp.GetServices<IHostedService>();
            Assert.That(
                hosted.OfType<PubSubApplicationHostedService>(),
                Is.Not.Empty);
        }

        [Test]
        public void AddPubSub_ResolvesIPubSubApplication()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            services.AddLogging();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddPubSub();
            ServiceProvider sp = services.BuildServiceProvider();
            IPubSubApplication? app = sp.GetService<IPubSubApplication>();
            Assert.That(app, Is.Not.Null);
        }

        [Test]
        public void AddPubSubPublisher_RegistersServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            services.AddLogging();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddPubSubPublisher();
            ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetService<IPubSubApplication>(), Is.Not.Null);
        }

        [Test]
        public void AddPubSubSubscriber_RegistersServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            services.AddLogging();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddPubSubSubscriber();
            ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetService<IPubSubApplication>(), Is.Not.Null);
        }

        [Test]
        public void AddPubSub_NullBuilder_Throws()
        {
            IOpcUaBuilder? builder = null;
            Assert.That(
                () => builder!.AddPubSub(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddPubSubFluent_ResolvesIPubSubApplication()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            services.AddLogging();
            services.AddOpcUa().AddPubSub(pubsub => pubsub.AddPublisher());
            ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetService<IPubSubApplication>(), Is.Not.Null);
        }

        [Test]
        public void AddPubSubFluent_NullConfigure_Throws()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            Assert.That(
                () => builder.AddPubSub((Action<IPubSubBuilder>)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddPubSubFluent_NullBuilder_Throws()
        {
            IOpcUaBuilder? builder = null;
            Assert.That(
                () => builder!.AddPubSub(pubsub => pubsub.AddPublisher()),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddPubSubFluent_ConfigureApplication_IsApplied()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            services.AddLogging();
            bool configureApplicationInvoked = false;
            services.AddOpcUa().AddPubSub(pubsub =>
                pubsub.AddPublisher().ConfigureApplication(
                    app =>
                    {
                        configureApplicationInvoked = true;
                        app.WithApplicationId("urn:test:application");
                    }));
            ServiceProvider sp = services.BuildServiceProvider();
            _ = sp.GetRequiredService<IPubSubApplication>();
            Assert.That(configureApplicationInvoked, Is.True);
        }

        [Test]
        public void AddPubSubFluent_AddSecurityKeyProvider_RegistersProvider()
        {
            var keyProvider = new Mock<IPubSubSecurityKeyProvider>();
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            services.AddLogging();
            services.AddOpcUa().AddPubSub(pubsub =>
                pubsub.AddSubscriber().AddSecurityKeyProvider(keyProvider.Object));
            ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(
                sp.GetService<IPubSubSecurityKeyProvider>(),
                Is.SameAs(keyProvider.Object));
        }

        [Test]
        public void AddPubSubFluent_ExposesServicesAndOpcUaBuilder()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IServiceCollection? captured = null;
            IOpcUaBuilder root = services.AddOpcUa();
            root.AddPubSub(pubsub =>
            {
                captured = pubsub.Services;
                Assert.That(pubsub.OpcUaBuilder, Is.SameAs(root));
            });
            Assert.That(captured, Is.SameAs(services));
        }

        [Test]
        [Description("OPC 10000-14 §9.1.6: HA deployments can replace PubSub state providers.")]
        public void AddPubSubFluent_WithProviders_RegistersProviderInstances()
        {
            var configurationStore = new InMemoryPubSubConfigurationStore();
            var idAllocator = new InMemoryPubSubIdAllocator();
            var runtimeStateStore = new InMemoryPubSubRuntimeStateStore();
            var securityKeyStore = new InMemoryPubSubSecurityKeyStore();
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            services.AddLogging();

            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .WithConfigurationStore(configurationStore)
                .WithIdAllocator(idAllocator)
                .WithRuntimeStateStore(runtimeStateStore)
                .WithSecurityKeyStore(securityKeyStore));

            ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<IPubSubConfigurationStore>(), Is.SameAs(configurationStore));
            Assert.That(sp.GetRequiredService<IPubSubIdAllocator>(), Is.SameAs(idAllocator));
            Assert.That(sp.GetRequiredService<IPubSubRuntimeStateStore>(), Is.SameAs(runtimeStateStore));
            Assert.That(sp.GetRequiredService<IPubSubSecurityKeyStore>(), Is.SameAs(securityKeyStore));
        }

        [Test]
        public async Task AddPubSubFluentConfigureConfigurationBuildsAndAppliesConfigurationAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            services.AddLogging();

            services.AddOpcUa().AddPubSub(pubsub => pubsub.ConfigureConfiguration(configuration =>
                configuration
                    .Enabled(false)
                    .AddConnection("udp-connection", connection =>
                        connection.WithTransportProfile(Profiles.PubSubUdpUadpTransport))));

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            PubSubConfigurationDataType configuration = serviceProvider
                .GetRequiredService<IPubSubApplication>()
                .GetConfiguration();

            Assert.Multiple(() =>
            {
                Assert.That(configuration.Enabled, Is.False);
                Assert.That(configuration.Connections, Has.Count.EqualTo(1));
                Assert.That(configuration.Connections[0].Name, Is.EqualTo("udp-connection"));
                Assert.That(
                    configuration.Connections[0].TransportProfileUri,
                    Is.EqualTo(Profiles.PubSubUdpUadpTransport));
            });
        }

        [Test]
        public void AddPubSubFluentConfigureConfigurationNullBuilderThrows()
        {
            IPubSubBuilder? builder = null;

            Assert.That(
                () => builder!.ConfigureConfiguration(_ => { }),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddPubSubFluentConfigureConfigurationNullConfigureThrows()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IPubSubBuilder captured = null!;
            services.AddOpcUa().AddPubSub(pubsub => captured = pubsub);

            Assert.That(
                () => captured.ConfigureConfiguration(null!),
                Throws.ArgumentNullException);
        }
    }
}
