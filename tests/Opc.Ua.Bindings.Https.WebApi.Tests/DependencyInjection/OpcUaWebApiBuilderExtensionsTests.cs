// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET8_0_OR_GREATER

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Bindings.WebApi;
using Opc.Ua.Schema;
using Opc.Ua.Schema.OpenApi;

namespace Opc.Ua.Bindings.Https.WebApi.Tests.DependencyInjection
{
    /// <summary>
    /// DI registration tests for <see cref="OpcUaWebApiBuilderExtensions"/>:
    /// <c>AddWebApiTransport()</c>.
    /// </summary>
    [TestFixture]
    [Category("DIExtensionsBatch1")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class OpcUaWebApiBuilderExtensionsTests
    {
        [Test]
        public void AddWebApiTransportThrowsForNullBuilder()
        {
            Assert.That(
                () => OpcUaWebApiBuilderExtensions.AddWebApiTransport(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWebApiTransportWithConfigureThrowsForNullBuilder()
        {
            Assert.That(
                () => OpcUaWebApiBuilderExtensions.AddWebApiTransport(null!, configure: null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWebApiTransportReturnsSameSpecializedBuilder()
        {
            var builder = new TestTransportBuilder(new ServiceCollection());

            TestTransportBuilder returned = builder.AddWebApiTransport();

            Assert.That(returned, Is.SameAs(builder));
        }

        [Test]
        public void AddWebApiTransportWithOptionsReturnsSameSpecializedBuilder()
        {
            var builder = new TestTransportBuilder(new ServiceCollection());

            TestTransportBuilder returned = builder.AddWebApiTransport(_ => { });

            Assert.That(returned, Is.SameAs(builder));
        }

        [Test]
        public void SpecializedTransportBuilderThrowsWhenNull()
        {
            TestTransportBuilder builder = null!;

            Assert.That(() => builder.AddWebApiTransport(), Throws.ArgumentNullException);
            Assert.That(() => builder.AddWebApiTransport(_ => { }), Throws.ArgumentNullException);
        }

        [Test]
        public void AddWebApiTransportRegistersWebApiServer()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWebApiTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            WebApiServer server = provider.GetRequiredService<WebApiServer>();

            Assert.That(server, Is.Not.Null);
        }

        [Test]
        public void AddWebApiTransportRegistersIWebApiServer()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWebApiTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            IWebApiServer server = provider.GetRequiredService<IWebApiServer>();

            Assert.That(server, Is.Not.Null);
        }

        [Test]
        public void AddWebApiTransportWebApiServerIsSameAsSingleton()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWebApiTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            WebApiServer concreteServer = provider.GetRequiredService<WebApiServer>();
            IWebApiServer interfaceServer = provider.GetRequiredService<IWebApiServer>();

            Assert.That(interfaceServer, Is.SameAs(concreteServer));
        }

        [Test]
        public void AddWebApiTransportRegistersTransportBindingConfigurator()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWebApiTransport();

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetService<ITransportBindingConfigurator>(),
                Is.Not.Null);
        }

        [Test]
        public void AddWebApiTransportRegistersOptionsWithDefaultValues()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWebApiTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            IOptions<WebApiTransportOptions> options =
                provider.GetRequiredService<IOptions<WebApiTransportOptions>>();

            Assert.That(options.Value.HostingMode,
                Is.EqualTo(WebApiHostingMode.SharedWithHttpsListener));
            Assert.That(options.Value.DefaultEncoding,
                Is.EqualTo(WebApiEncoding.Compact));
        }

        [Test]
        public void AddWebApiTransportRegistersTheOpenApiGeneratorAsASingleton()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWebApiTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(
                provider.GetRequiredService<WebApiOpenApiGenerator>(),
                Is.SameAs(provider.GetRequiredService<WebApiOpenApiGenerator>()));
        }

        [Test]
        public void AddWebApiTransportKeepsAnOpenApiGeneratorTheApplicationRegistered()
        {
            var services = new ServiceCollection();
            var generator = new WebApiOpenApiGenerator(new DataTypeDefinitionRegistry());
            services.AddSingleton(generator);

            services.AddOpcUa().AddWebApiTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<WebApiOpenApiGenerator>(), Is.SameAs(generator));
        }

        [Test]
        public void AddWebApiTransportWithConfigureRunsCallback()
        {
            var services = new ServiceCollection();
            WebApiEncoding? capturedEncoding = null;

            services.AddOpcUa().AddWebApiTransport(o =>
            {
                capturedEncoding = o.DefaultEncoding;
                o.DefaultEncoding = WebApiEncoding.Verbose;
            });

            using ServiceProvider provider = services.BuildServiceProvider();
            IOptions<WebApiTransportOptions> options =
                provider.GetRequiredService<IOptions<WebApiTransportOptions>>();

            // Accessing .Value triggers the configure action.
            WebApiTransportOptions value = options.Value;

            Assert.That(capturedEncoding, Is.EqualTo(WebApiEncoding.Compact));
            Assert.That(value.DefaultEncoding, Is.EqualTo(WebApiEncoding.Verbose));
        }

        [Test]
        public void AddWebApiTransportIsIdempotent()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddWebApiTransport()
                .AddWebApiTransport();

            Assert.That(() =>
            {
                using ServiceProvider provider = services.BuildServiceProvider();
                WebApiServer server1 = provider.GetRequiredService<WebApiServer>();
                WebApiServer server2 = provider.GetRequiredService<WebApiServer>();

                Assert.That(server2, Is.SameAs(server1));
            }, Throws.Nothing);
        }

        [Test]
        public void AddWebApiTransportReturnsSameBuilder()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddWebApiTransport();

            Assert.That(returned, Is.SameAs(builder));
        }

        [Test]
        public void AddWebApiTransportBeforeHttpsAttachesContributor()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddWebApiTransport()
                .AddHttpsTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            AssertHttpsFactoryHasWebApiContributor(registry);
        }

        [Test]
        public void AddWebApiTransportAfterHttpsAttachesContributor()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddHttpsTransport()
                .AddWebApiTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            AssertHttpsFactoryHasWebApiContributor(registry);
        }

        [Test]
        public void AddHttpsTransportOneShotRegistersHttpsWssAndWebApi()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddHttpsTransport(options => options.IncludeWebApi = true);

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry.HasListenerFactory(Utils.UriSchemeHttps), Is.True);
            Assert.That(registry.HasListenerFactory(Utils.UriSchemeWss), Is.True);
            Assert.That(provider.GetRequiredService<WebApiServer>(), Is.Not.Null);
            AssertHttpsFactoryHasWebApiContributor(registry);
        }

        private static void AssertHttpsFactoryHasWebApiContributor(ITransportBindingRegistry registry)
        {
            var factory = (HttpsServiceHost)registry.GetListenerFactory(Utils.UriSchemeHttps)!;

            Assert.That(
                factory.StartupContributors,
                Has.Some.InstanceOf<WebApiHttpsStartupContributor>());
        }
    }
}

#endif // NET8_0_OR_GREATER
