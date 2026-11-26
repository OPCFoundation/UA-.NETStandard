// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client.WebApi;

namespace Opc.Ua.Bindings.Https.WebApi.Tests.DependencyInjection
{
    /// <summary>
    /// DI registration tests for <see cref="OpcUaWebApiClientBuilderExtensions"/>:
    /// <c>AddWebApiTransportChannel()</c>.
    /// </summary>
    [TestFixture]
    [Category("DIExtensionsBatch1")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class OpcUaWebApiClientBuilderExtensionsTests
    {
        [Test]
        public void AddWebApiTransportChannelThrowsForNullBuilder()
        {
            Assert.That(
                () => ((IOpcUaBuilder)null!).AddWebApiTransportChannel(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWebApiTransportChannelWithConfigureThrowsForNullBuilder()
        {
            Assert.That(
                () => ((IOpcUaBuilder)null!).AddWebApiTransportChannel(
                    configure: null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWebApiTransportChannelRegistersWebApiClientOptions()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWebApiTransportChannel();

            using ServiceProvider provider = services.BuildServiceProvider();
            WebApiClientOptions options = provider.GetRequiredService<WebApiClientOptions>();

            Assert.That(options, Is.Not.Null);
        }

        [Test]
        public void AddWebApiTransportChannelOptionsHaveDefaultEncoding()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWebApiTransportChannel();

            using ServiceProvider provider = services.BuildServiceProvider();
            WebApiClientOptions options = provider.GetRequiredService<WebApiClientOptions>();

            Assert.That(options.Encoding, Is.EqualTo(WebApiEncoding.Compact));
        }

        [Test]
        public void AddWebApiTransportChannelWithConfigureRunsCallback()
        {
            var services = new ServiceCollection();
            WebApiEncoding? captured = null;

            services.AddOpcUa().AddWebApiTransportChannel(o =>
            {
                captured = o.Encoding;
                o.Encoding = WebApiEncoding.Verbose;
            });

            using ServiceProvider provider = services.BuildServiceProvider();
            WebApiClientOptions options = provider.GetRequiredService<WebApiClientOptions>();

            Assert.That(captured, Is.EqualTo(WebApiEncoding.Compact));
            Assert.That(options.Encoding, Is.EqualTo(WebApiEncoding.Verbose));
        }

        [Test]
        public void AddWebApiTransportChannelRegistersTransportBindingConfigurator()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWebApiTransportChannel();

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetService<ITransportBindingConfigurator>(),
                Is.Not.Null);
        }

        [Test]
        public void AddWebApiTransportChannelRegistersWebApiChannelFactories()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWebApiTransportChannel();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(
                registry.HasChannelFactory(Utils.UriSchemeOpcHttpsWebApi),
                Is.True,
                "WebApi HTTPS channel factory should be registered.");
            Assert.That(
                registry.HasChannelFactory(Utils.UriSchemeOpcWssOpenApi),
                Is.True,
                "WebApi WSS channel factory should be registered.");
        }

        [Test]
        public void AddWebApiTransportChannelRegistersWebApiTransportChannelFactory()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWebApiTransportChannel();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(
                registry.GetChannelFactory(Utils.UriSchemeOpcHttpsWebApi),
                Is.InstanceOf<WebApiTransportChannelFactory>());
        }

        [Test]
        public void AddWebApiTransportChannelRegistersWebApiWssTransportChannelFactory()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWebApiTransportChannel();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(
                registry.GetChannelFactory(Utils.UriSchemeOpcWssOpenApi),
                Is.InstanceOf<WebApiWssTransportChannelFactory>());
        }

        [Test]
        public void AddWebApiTransportChannelIsIdempotent()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddWebApiTransportChannel()
                .AddWebApiTransportChannel();

            Assert.That(() =>
            {
                using ServiceProvider provider = services.BuildServiceProvider();
                _ = provider.GetRequiredService<WebApiClientOptions>();
            }, Throws.Nothing);
        }

        [Test]
        public void AddWebApiTransportChannelSecondCallDoesNotOverrideOptions()
        {
            // TryAddSingleton semantics: first registration wins.
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddWebApiTransportChannel(o => o.Encoding = WebApiEncoding.Verbose)
                .AddWebApiTransportChannel(o => o.Encoding = WebApiEncoding.Compact);

            using ServiceProvider provider = services.BuildServiceProvider();
            WebApiClientOptions options = provider.GetRequiredService<WebApiClientOptions>();

            Assert.That(options.Encoding, Is.EqualTo(WebApiEncoding.Verbose));
        }

        [Test]
        public void AddWebApiTransportChannelReturnsSameBuilder()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddWebApiTransportChannel();

            Assert.That(returned, Is.SameAs(builder));
        }
    }
}
