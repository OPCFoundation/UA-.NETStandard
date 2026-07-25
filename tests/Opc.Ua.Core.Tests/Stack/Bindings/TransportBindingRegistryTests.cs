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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Bindings.Tests
{
    /// <summary>
    /// Behavioural tests for <see cref="DefaultTransportBindingRegistry"/>
    /// and the <c>Add*Transport()</c> DI extension methods that compose
    /// into it.
    /// </summary>
    [TestFixture]
    [Category("Bindings")]
    public sealed class TransportBindingRegistryTests
    {
        private ITelemetryContext m_telemetry = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void RegisterListenerFactoryStoresFactoryByScheme()
        {
            var registry = new DefaultTransportBindingRegistry();
            var factory = new FakeListenerFactory(Utils.UriSchemeOpcTcp);

            registry.RegisterListenerFactory(factory);

            Assert.That(registry.HasListenerFactory(Utils.UriSchemeOpcTcp), Is.True);
            Assert.That(registry.GetListenerFactory(Utils.UriSchemeOpcTcp), Is.SameAs(factory));
        }

        [Test]
        public void RegisterChannelFactoryStoresFactoryByScheme()
        {
            var registry = new DefaultTransportBindingRegistry();
            var factory = new FakeChannelFactory(Utils.UriSchemeOpcTcp);

            registry.RegisterChannelFactory(factory);

            Assert.That(registry.HasChannelFactory(Utils.UriSchemeOpcTcp), Is.True);
            Assert.That(registry.GetChannelFactory(Utils.UriSchemeOpcTcp), Is.SameAs(factory));
        }

        [Test]
        public void RegisterListenerFactoryReplacesPreviousRegistration()
        {
            var registry = new DefaultTransportBindingRegistry();
            var first = new FakeListenerFactory(Utils.UriSchemeOpcTcp);
            var second = new FakeListenerFactory(Utils.UriSchemeOpcTcp);

            registry.RegisterListenerFactory(first);
            registry.RegisterListenerFactory(second);

            Assert.That(registry.GetListenerFactory(Utils.UriSchemeOpcTcp), Is.SameAs(second),
                "Last writer wins per URI scheme.");
        }

        [Test]
        public void RegisterChannelFactoryReplacesPreviousRegistration()
        {
            var registry = new DefaultTransportBindingRegistry();
            var first = new FakeChannelFactory(Utils.UriSchemeOpcTcp);
            var second = new FakeChannelFactory(Utils.UriSchemeOpcTcp);

            registry.RegisterChannelFactory(first);
            registry.RegisterChannelFactory(second);

            Assert.That(registry.GetChannelFactory(Utils.UriSchemeOpcTcp), Is.SameAs(second),
                "Last writer wins per URI scheme.");
        }

        [Test]
        public void GetListenerFactoryReturnsNullForUnknownScheme()
        {
            var registry = new DefaultTransportBindingRegistry();

            Assert.That(registry.GetListenerFactory("opc.unknown"), Is.Null);
            Assert.That(registry.HasListenerFactory("opc.unknown"), Is.False);
        }

        [Test]
        public void GetChannelFactoryReturnsNullForUnknownScheme()
        {
            var registry = new DefaultTransportBindingRegistry();

            Assert.That(registry.GetChannelFactory("opc.unknown"), Is.Null);
            Assert.That(registry.HasChannelFactory("opc.unknown"), Is.False);
        }

        [Test]
        public void CreateListenerReturnsNullForUnknownScheme()
        {
            var registry = new DefaultTransportBindingRegistry();

            Assert.That(registry.CreateListener("opc.unknown", m_telemetry), Is.Null);
        }

        [Test]
        public void CreateChannelReturnsNullForUnknownScheme()
        {
            var registry = new DefaultTransportBindingRegistry();

            Assert.That(registry.CreateChannel("opc.unknown", m_telemetry), Is.Null);
        }

        [Test]
        public void WithDefaultTcpSeedsRawSocketTcpFactories()
        {
            var registry =
                DefaultTransportBindingRegistry.WithDefaultTcp();

            Assert.That(registry.HasListenerFactory(Utils.UriSchemeOpcTcp), Is.True);
            Assert.That(registry.HasChannelFactory(Utils.UriSchemeOpcTcp), Is.True);
            Assert.That(registry.GetListenerFactory(Utils.UriSchemeOpcTcp),
                Is.InstanceOf<TcpTransportListenerFactory>());
            Assert.That(registry.GetChannelFactory(Utils.UriSchemeOpcTcp),
                Is.InstanceOf<TcpTransportChannelFactory>());
        }

        [Test]
        public void RegisterListenerFactoryThrowsOnNull()
        {
            var registry = new DefaultTransportBindingRegistry();

            Assert.That(
                () => registry.RegisterListenerFactory(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void RegisterChannelFactoryThrowsOnNull()
        {
            var registry = new DefaultTransportBindingRegistry();

            Assert.That(
                () => registry.RegisterChannelFactory(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddOpcTcpTransportRegistersTcpFactories()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddOpcTcpTransport();
            using ServiceProvider provider = services.BuildServiceProvider();

            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry.HasListenerFactory(Utils.UriSchemeOpcTcp), Is.True);
            Assert.That(registry.HasChannelFactory(Utils.UriSchemeOpcTcp), Is.True);
            Assert.That(registry.GetListenerFactory(Utils.UriSchemeOpcTcp),
                Is.InstanceOf<TcpTransportListenerFactory>());
        }

        [Test]
        public void AddTransportBindingRegistryIsIdempotent()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddOpcTcpTransport();
            services.AddTransportBindingRegistry();
            services.AddTransportBindingRegistry();
            using ServiceProvider provider = services.BuildServiceProvider();

            // The registry is registered exactly once (singleton); resolving
            // it twice must return the same instance.
            ITransportBindingRegistry registry1 = provider.GetRequiredService<ITransportBindingRegistry>();
            ITransportBindingRegistry registry2 = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry2, Is.SameAs(registry1));
        }

        [Test]
        public void AddCustomTransportRegistersBothFactoriesByScheme()
        {
            var services = new ServiceCollection();
            services
                .AddOpcUa()
                .AddCustomTransport<FakeListenerFactory, FakeChannelFactory>();
            using ServiceProvider provider = services.BuildServiceProvider();

            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry.HasListenerFactory(FakeListenerFactory.FakeScheme), Is.True);
            Assert.That(registry.HasChannelFactory(FakeListenerFactory.FakeScheme), Is.True);
            Assert.That(registry.GetListenerFactory(FakeListenerFactory.FakeScheme),
                Is.InstanceOf<FakeListenerFactory>());
            Assert.That(registry.GetChannelFactory(FakeListenerFactory.FakeScheme),
                Is.InstanceOf<FakeChannelFactory>());
        }

        [Test]
        public void ParallelServiceProvidersHaveIndependentRegistries()
        {
            // Two test fixtures that each build their own host must not race
            // on a shared mutable state.
            var servicesA = new ServiceCollection();
            servicesA.AddOpcUa().AddOpcTcpTransport();
            using ServiceProvider providerA = servicesA.BuildServiceProvider();

            var servicesB = new ServiceCollection();
            servicesB.AddOpcUa().AddCustomTransport<FakeListenerFactory, FakeChannelFactory>();
            using ServiceProvider providerB = servicesB.BuildServiceProvider();

            ITransportBindingRegistry registryA = providerA.GetRequiredService<ITransportBindingRegistry>();
            ITransportBindingRegistry registryB = providerB.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registryB, Is.Not.SameAs(registryA),
                "Each ServiceProvider owns its own registry instance.");
            Assert.That(registryA.HasListenerFactory(Utils.UriSchemeOpcTcp), Is.True);
            Assert.That(registryA.HasListenerFactory(FakeListenerFactory.FakeScheme), Is.False,
                "Provider A did not register the fake transport.");
            Assert.That(registryB.HasListenerFactory(FakeListenerFactory.FakeScheme), Is.True);
            // opc.tcp is now seeded into every registry by AddOpcUa(); the two
            // registries remain independent instances (the fake transport is
            // only present in provider B).
            Assert.That(registryB.HasListenerFactory(Utils.UriSchemeOpcTcp), Is.True);
        }

        [Test]
        public void ConfiguratorsApplyInRegistrationOrderLastWriterWins()
        {
            // First registration installs the TCP factory; second
            // registration overrides it with a different one.
            var services = new ServiceCollection();
            services
                .AddOpcUa()
                .AddOpcTcpTransport();
            services.AddSingleton<ITransportBindingConfigurator>(
                new TransportBindingConfigurator(registry =>
                    registry.RegisterListenerFactory(new FakeListenerFactory(Utils.UriSchemeOpcTcp))));
            using ServiceProvider provider = services.BuildServiceProvider();

            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry.GetListenerFactory(Utils.UriSchemeOpcTcp),
                Is.InstanceOf<FakeListenerFactory>(),
                "The second configurator must override the first for the same URI scheme.");
        }

        [Test]
        public void AddOpcUaRegistersMandatoryOpcTcpTransport()
        {
            // AddOpcUa() alone must seed the mandatory raw-socket opc.tcp
            // listener + channel factories - no AddOpcTcpTransport() required.
            var services = new ServiceCollection();
            services.AddOpcUa();
            using ServiceProvider provider = services.BuildServiceProvider();

            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry.HasListenerFactory(Utils.UriSchemeOpcTcp), Is.True);
            Assert.That(registry.HasChannelFactory(Utils.UriSchemeOpcTcp), Is.True);
            // Assert the contract (a factory serving the opc.tcp scheme) rather
            // than a concrete implementation type, so swapping the default
            // raw-socket factory does not break this test.
            ITransportChannelFactory? channelFactory =
                registry.GetChannelFactory(Utils.UriSchemeOpcTcp);
            Assert.That(channelFactory, Is.Not.Null);
            Assert.That(channelFactory!.UriScheme, Is.EqualTo(Utils.UriSchemeOpcTcp));
            Assert.That(registry, Is.InstanceOf<ITransportChannelBindings>(),
                "The seeded registry must satisfy ITransportChannelBindings for the client channel manager.");
        }

        [Test]
        public void DefaultOpcTcpTransportIsOverrideableWithoutAddOpcTcpTransport()
        {
            // A downstream configurator (e.g. AddKestrelOpcTcpTransport) must be
            // able to override the seeded default opc.tcp listener even though
            // AddOpcTcpTransport() was never called.
            var services = new ServiceCollection();
            services.AddOpcUa();
            services.AddSingleton<ITransportBindingConfigurator>(
                new TransportBindingConfigurator(registry =>
                    registry.RegisterListenerFactory(new FakeListenerFactory(Utils.UriSchemeOpcTcp))));
            using ServiceProvider provider = services.BuildServiceProvider();

            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry.GetListenerFactory(Utils.UriSchemeOpcTcp),
                Is.InstanceOf<FakeListenerFactory>(),
                "The configurator must override the seeded default opc.tcp listener.");
        }

        [Test]
        public void ImplementerCanUnregisterDefaultTransport()
        {
            // An implementer can drop the seeded default opc.tcp transport with
            // a plain ITransportBindingConfigurator - no fluent API required.
            var services = new ServiceCollection();
            services.AddOpcUa();
            services.AddSingleton<ITransportBindingConfigurator>(
                new TransportBindingConfigurator(registry =>
                {
                    registry.RemoveListenerFactory(Utils.UriSchemeOpcTcp);
                    registry.RemoveChannelFactory(Utils.UriSchemeOpcTcp);
                }));
            using ServiceProvider provider = services.BuildServiceProvider();

            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(registry.HasListenerFactory(Utils.UriSchemeOpcTcp), Is.False);
            Assert.That(registry.HasChannelFactory(Utils.UriSchemeOpcTcp), Is.False);
        }

        [Test]
        public void RemoveListenerFactoryRemovesRegisteredFactory()
        {
            DefaultTransportBindingRegistry registry = DefaultTransportBindingRegistry.WithDefaultTcp();

            Assert.That(registry.RemoveListenerFactory(Utils.UriSchemeOpcTcp), Is.True);
            Assert.That(registry.HasListenerFactory(Utils.UriSchemeOpcTcp), Is.False);
            Assert.That(registry.RemoveListenerFactory(Utils.UriSchemeOpcTcp), Is.False,
                "Removing an absent listener factory returns false.");
        }

        [Test]
        public void RemoveChannelFactoryRemovesRegisteredFactory()
        {
            DefaultTransportBindingRegistry registry = DefaultTransportBindingRegistry.WithDefaultTcp();

            Assert.That(registry.RemoveChannelFactory(Utils.UriSchemeOpcTcp), Is.True);
            Assert.That(registry.HasChannelFactory(Utils.UriSchemeOpcTcp), Is.False);
            Assert.That(registry.RemoveChannelFactory(Utils.UriSchemeOpcTcp), Is.False,
                "Removing an absent channel factory returns false.");
        }

        [Test]
        public void RemoveListenerFactoryThrowsOnNullScheme()
        {
            var registry = new DefaultTransportBindingRegistry();
            Assert.That(
                () => registry.RemoveListenerFactory(null!),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("uriScheme"));
        }

        [Test]
        public void RemoveChannelFactoryThrowsOnNullScheme()
        {
            var registry = new DefaultTransportBindingRegistry();
            Assert.That(
                () => registry.RemoveChannelFactory(null!),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("uriScheme"));
        }

        [Test]
        public void GetListenerFactoryThrowsOnNullScheme()
        {
            var registry = new DefaultTransportBindingRegistry();
            Assert.That(
                () => registry.GetListenerFactory(null!),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("uriScheme"));
        }

        [Test]
        public void GetChannelFactoryThrowsOnNullScheme()
        {
            var registry = new DefaultTransportBindingRegistry();
            Assert.That(
                () => registry.GetChannelFactory(null!),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("uriScheme"));
        }

        [Test]
        public void HasListenerFactoryThrowsOnNullScheme()
        {
            var registry = new DefaultTransportBindingRegistry();
            Assert.That(
                () => registry.HasListenerFactory(null!),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("uriScheme"));
        }

        [Test]
        public void HasChannelFactoryThrowsOnNullScheme()
        {
            var registry = new DefaultTransportBindingRegistry();
            Assert.That(
                () => registry.HasChannelFactory(null!),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("uriScheme"));
        }

        [Test]
        public void CreateListenerThrowsOnNullScheme()
        {
            var registry = new DefaultTransportBindingRegistry();
            Assert.That(
                () => registry.CreateListener(null!, m_telemetry),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("uriScheme"));
        }

        [Test]
        public void CreateListenerThrowsOnNullTelemetry()
        {
            var registry = new DefaultTransportBindingRegistry();
            Assert.That(
                () => registry.CreateListener(Utils.UriSchemeOpcTcp, null!),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("telemetry"));
        }

        [Test]
        public void CreateChannelThrowsOnNullScheme()
        {
            var registry = new DefaultTransportBindingRegistry();
            Assert.That(
                () => registry.CreateChannel(null!, m_telemetry),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("uriScheme"));
        }

        [Test]
        public void CreateChannelThrowsOnNullTelemetry()
        {
            var registry = new DefaultTransportBindingRegistry();
            Assert.That(
                () => registry.CreateChannel(Utils.UriSchemeOpcTcp, null!),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("telemetry"));
        }

        [Test]
        public void ITransportChannelBindingsCreateForwardsToCreateChannel()
        {
            // Cast to the explicit interface implementation.
            ITransportChannelBindings bindings = new DefaultTransportBindingRegistry();

            // Unknown scheme → null (same behaviour as CreateChannel).
            Assert.That(bindings.Create("opc.unknown", m_telemetry), Is.Null);
        }

        [Test]
        public async Task CreateListenerReturnsInstanceForKnownSchemeAsync()
        {
            var registry =
                DefaultTransportBindingRegistry.WithDefaultTcp();

            await using ITransportListener? listener =
                registry.CreateListener(Utils.UriSchemeOpcTcp, m_telemetry);

            Assert.That(listener, Is.Not.Null);
        }

        [Test]
        public void CreateChannelReturnsInstanceForKnownScheme()
        {
            var registry =
                DefaultTransportBindingRegistry.WithDefaultTcp();

            using ITransportChannel? channel =
                registry.CreateChannel(Utils.UriSchemeOpcTcp, m_telemetry);

            Assert.That(channel, Is.Not.Null);
        }

        private sealed class FakeListenerFactory : ITransportListenerFactory
        {
            public const string FakeScheme = "opc.fake";

            public FakeListenerFactory()
                : this(FakeScheme)
            {
            }

            public FakeListenerFactory(string uriScheme)
            {
                UriScheme = uriScheme;
            }

            public string UriScheme { get; }

            public ITransportListener Create(ITelemetryContext telemetry)
            {
                throw new NotSupportedException("Fake factory does not create listeners.");
            }

            public ValueTask<List<EndpointDescription>> CreateServiceHostAsync(
                ServerBase serverBase,
                IDictionary<string, ServiceHost> hosts,
                ApplicationConfiguration configuration,
                ArrayOf<string> baseAddresses,
                ApplicationDescription serverDescription,
                ArrayOf<ServerSecurityPolicy> securityPolicies,
                ICertificateRegistry serverCertificates,
                ICertificateValidatorEx clientCertificateValidator,
                CancellationToken ct = default)
            {
                return new ValueTask<List<EndpointDescription>>([]);
            }
        }

        private sealed class FakeChannelFactory : ITransportChannelFactory
        {
            public FakeChannelFactory()
                : this(FakeListenerFactory.FakeScheme)
            {
            }

            public FakeChannelFactory(string uriScheme)
            {
                UriScheme = uriScheme;
            }

            public string UriScheme { get; }

            public ITransportChannel Create(ITelemetryContext telemetry)
            {
                throw new NotSupportedException("Fake factory does not create channels.");
            }
        }
    }
}
