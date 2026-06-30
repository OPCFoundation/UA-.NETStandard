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

#nullable enable

using System;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Bindings;
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
                () => OpcUaWebApiClientBuilderExtensions.AddWebApiTransportChannel(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWebApiTransportChannelWithConfigureThrowsForNullBuilder()
        {
            Assert.That(
                () => OpcUaWebApiClientBuilderExtensions.AddWebApiTransportChannel(
                    null!,
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
