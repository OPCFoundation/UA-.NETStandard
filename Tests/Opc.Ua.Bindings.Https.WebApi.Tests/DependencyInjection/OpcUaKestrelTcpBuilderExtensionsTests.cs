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

#if NET8_0_OR_GREATER

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Bindings;

namespace Opc.Ua.Bindings.Https.WebApi.Tests.DependencyInjection
{
    /// <summary>
    /// DI registration tests for <see cref="OpcUaKestrelTcpBuilderExtensions"/>:
    /// <c>AddKestrelOpcTcpTransport()</c>.
    /// </summary>
    [TestFixture]
    [Category("DIExtensionsBatch1")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class OpcUaKestrelTcpBuilderExtensionsTests
    {
        [Test]
        public void AddKestrelOpcTcpTransportThrowsForNullBuilder()
        {
            Assert.That(
                () => OpcUaKestrelTcpBuilderExtensions.AddKestrelOpcTcpTransport(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddKestrelOpcTcpTransportRegistersKestrelTcpListenerFactory()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddKestrelOpcTcpTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry.HasListenerFactory(Utils.UriSchemeOpcTcp), Is.True);
            Assert.That(
                registry.GetListenerFactory(Utils.UriSchemeOpcTcp),
                Is.InstanceOf<KestrelTcpTransportListenerFactory>());
        }

        [Test]
        public void AddKestrelOpcTcpTransportReturnsSameBuilder()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddKestrelOpcTcpTransport();

            Assert.That(returned, Is.SameAs(builder));
        }

        [Test]
        public void AddKestrelOpcTcpTransportOverridesRawSocketListener()
        {
            // When called after AddOpcTcpTransport, Kestrel listener replaces the raw-socket one
            // (last-writer-wins per URI scheme).
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddOpcTcpTransport()
                .AddKestrelOpcTcpTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry.HasListenerFactory(Utils.UriSchemeOpcTcp), Is.True);
            Assert.That(
                registry.GetListenerFactory(Utils.UriSchemeOpcTcp),
                Is.InstanceOf<KestrelTcpTransportListenerFactory>());
        }

        [Test]
        public void AddKestrelOpcTcpTransportBeforeRawSocketStillWins()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddKestrelOpcTcpTransport()
                .AddOpcTcpTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(
                registry.GetListenerFactory(Utils.UriSchemeOpcTcp),
                Is.InstanceOf<KestrelTcpTransportListenerFactory>());
            Assert.That(registry.HasChannelFactory(Utils.UriSchemeOpcTcp), Is.True);
        }

        [Test]
        public void AddOpcTcpTransportStillRegistersRawSocketWhenNoKestrelOverride()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddOpcTcpTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(
                registry.GetListenerFactory(Utils.UriSchemeOpcTcp),
                Is.InstanceOf<TcpTransportListenerFactory>());
        }

        [Test]
        public void AddKestrelOpcTcpTransportIsIdempotent()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddKestrelOpcTcpTransport()
                .AddKestrelOpcTcpTransport();

            Assert.That(() =>
            {
                using ServiceProvider provider = services.BuildServiceProvider();
                _ = provider.GetRequiredService<ITransportBindingRegistry>();
            }, Throws.Nothing);
        }
    }
}

#endif // NET8_0_OR_GREATER
