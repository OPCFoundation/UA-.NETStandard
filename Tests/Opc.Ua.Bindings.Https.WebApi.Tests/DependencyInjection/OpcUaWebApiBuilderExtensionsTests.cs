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

#if NET8_0_OR_GREATER

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.WebApi;

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
            HttpsServiceHost factory = (HttpsServiceHost)registry.GetListenerFactory(Utils.UriSchemeHttps)!;

            Assert.That(
                factory.StartupContributors,
                Has.Some.InstanceOf<WebApiHttpsStartupContributor>());
        }
    }
}

#endif // NET8_0_OR_GREATER
