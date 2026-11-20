/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

#if NET8_0_OR_GREATER

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua;

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
        public void AddKestrelOpcTcpTransportReturnsSameSpecializedBuilder()
        {
            var builder = new TestTransportBuilder(new ServiceCollection());

            TestTransportBuilder returned = builder.AddKestrelOpcTcpTransport();

            Assert.That(returned, Is.SameAs(builder));
        }

        [Test]
        public void SpecializedTransportBuilderThrowsWhenNull()
        {
            TestTransportBuilder builder = null!;

            Assert.That(() => builder.AddKestrelOpcTcpTransport(), Throws.ArgumentNullException);
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
