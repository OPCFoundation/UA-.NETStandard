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

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua;

namespace Opc.Ua.Bindings.Https.WebApi.Tests.DependencyInjection
{
    /// <summary>
    /// DI registration tests for <see cref="OpcUaHttpsBuilderExtensions"/>:
    /// <c>AddHttpsTransport()</c> and <c>AddWssTransport()</c>.
    /// </summary>
    [TestFixture]
    [Category("DIExtensionsBatch1")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class OpcUaHttpsBuilderExtensionsTests
    {
        [Test]
        public void AddHttpsTransportThrowsForNullBuilder()
        {
            Assert.That(
                () => OpcUaHttpsBuilderExtensions.AddHttpsTransport(null!),
                Throws.ArgumentNullException);
        }

#if NET8_0_OR_GREATER
        [Test]
        public void AddHttpsRateLimiterThrowsForNullBuilder()
        {
            Assert.That(
                () => OpcUaHttpsBuilderExtensions.AddHttpsRateLimiter(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddHttpsRateLimiterWithConfigureThrowsForNullBuilder()
        {
            Assert.That(
                () => OpcUaHttpsBuilderExtensions.AddHttpsRateLimiter(null!, _ => { }),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddHttpsRateLimiterWithConfigureThrowsForNullConfigure()
        {
            var services = new ServiceCollection();

            Assert.That(
                () => services.AddOpcUa().AddHttpsRateLimiter(null!),
                Throws.ArgumentNullException);
        }
#endif

        [Test]
        public void AddHttpsTransportRegistersHttpsListenerFactory()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddHttpsTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry.HasListenerFactory(Utils.UriSchemeHttps), Is.True);
            Assert.That(
                registry.GetListenerFactory(Utils.UriSchemeHttps),
                Is.InstanceOf<HttpsTransportListenerFactory>());
        }

        [Test]
        public void AddHttpsTransportRegistersOpcHttpsListenerFactory()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddHttpsTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry.HasListenerFactory(Utils.UriSchemeOpcHttps), Is.True);
            Assert.That(
                registry.GetListenerFactory(Utils.UriSchemeOpcHttps),
                Is.InstanceOf<OpcHttpsTransportListenerFactory>());
        }

        [Test]
        public void AddHttpsTransportRegistersHttpsChannelFactory()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddHttpsTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry.HasChannelFactory(Utils.UriSchemeHttps), Is.True);
            Assert.That(
                registry.GetChannelFactory(Utils.UriSchemeHttps),
                Is.InstanceOf<HttpsTransportChannelFactory>());
        }

        [Test]
        public void AddHttpsTransportRegistersOpcHttpsChannelFactory()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddHttpsTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry.HasChannelFactory(Utils.UriSchemeOpcHttps), Is.True);
            Assert.That(
                registry.GetChannelFactory(Utils.UriSchemeOpcHttps),
                Is.InstanceOf<OpcHttpsTransportChannelFactory>());
        }

        [Test]
        public void AddHttpsTransportReturnsSameBuilder()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddHttpsTransport();

            Assert.That(returned, Is.SameAs(builder));
        }

#if NET8_0_OR_GREATER
        [Test]
        public void AddHttpsRateLimiterAddsContributorToHttpsListenerFactory()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddHttpsTransport()
                .AddHttpsRateLimiter();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();
            HttpsServiceHost factory = (HttpsServiceHost)registry.GetListenerFactory(Utils.UriSchemeHttps)!;

            Assert.That(
                factory.StartupContributors,
                Has.Exactly(1).InstanceOf<HttpsRateLimiterStartupContributor>());
        }

        [Test]
        public void AddHttpsRateLimiterAddsConfiguredContributorToWssListenerFactory()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddWssTransport()
                .AddHttpsRateLimiter(options => options.RejectionStatusCode = 429);

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();
            HttpsServiceHost factory = (HttpsServiceHost)registry.GetListenerFactory(Utils.UriSchemeWss)!;

            Assert.That(
                factory.StartupContributors,
                Has.Exactly(1).InstanceOf<HttpsRateLimiterStartupContributor>());
        }
#endif

        [Test]
        public void AddHttpsTransportIsIdempotent()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddHttpsTransport()
                .AddHttpsTransport();

            Assert.That(() =>
            {
                using ServiceProvider provider = services.BuildServiceProvider();
                _ = provider.GetRequiredService<ITransportBindingRegistry>();
            }, Throws.Nothing);
        }

        [Test]
        public void AddWssTransportThrowsForNullBuilder()
        {
            Assert.That(
                () => OpcUaHttpsBuilderExtensions.AddWssTransport(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWssTransportRegistersWssListenerFactory()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWssTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry.HasListenerFactory(Utils.UriSchemeWss), Is.True);
            Assert.That(
                registry.GetListenerFactory(Utils.UriSchemeWss),
                Is.InstanceOf<WssTransportListenerFactory>());
        }

        [Test]
        public void AddWssTransportRegistersOpcWssListenerFactory()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWssTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry.HasListenerFactory(Utils.UriSchemeOpcWss), Is.True);
            Assert.That(
                registry.GetListenerFactory(Utils.UriSchemeOpcWss),
                Is.InstanceOf<OpcWssTransportListenerFactory>());
        }

        [Test]
        public void AddWssTransportRegistersWssChannelFactory()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWssTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry.HasChannelFactory(Utils.UriSchemeWss), Is.True);
            Assert.That(
                registry.GetChannelFactory(Utils.UriSchemeWss),
                Is.InstanceOf<WssTransportChannelFactory>());
        }

        [Test]
        public void AddWssTransportRegistersOpcWssChannelFactory()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWssTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry.HasChannelFactory(Utils.UriSchemeOpcWss), Is.True);
            Assert.That(
                registry.GetChannelFactory(Utils.UriSchemeOpcWss),
                Is.InstanceOf<OpcWssTransportChannelFactory>());
        }

        [Test]
        public void AddWssTransportWithOptionsReturnsSameBuilder()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            bool configured = false;

            IOpcUaBuilder returned = builder.AddWssTransport(options =>
            {
                configured = true;
                options.IncludeWebApi = false;
            });

            Assert.That(returned, Is.SameAs(builder));
            Assert.That(configured, Is.True);
        }

        [Test]
        public void AddWssTransportReturnsSameBuilder()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddWssTransport();

            Assert.That(returned, Is.SameAs(builder));
        }

        [Test]
        public void AddWssTransportIsIdempotent()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddWssTransport()
                .AddWssTransport();

            Assert.That(() =>
            {
                using ServiceProvider provider = services.BuildServiceProvider();
                _ = provider.GetRequiredService<ITransportBindingRegistry>();
            }, Throws.Nothing);
        }

        [Test]
        public void HttpsAndWssTransportsCompose()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddHttpsTransport()
                .AddWssTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry.HasListenerFactory(Utils.UriSchemeHttps), Is.True);
            Assert.That(registry.HasListenerFactory(Utils.UriSchemeWss), Is.True);
            Assert.That(registry.HasChannelFactory(Utils.UriSchemeHttps), Is.True);
            Assert.That(registry.HasChannelFactory(Utils.UriSchemeWss), Is.True);
        }
    }
}
