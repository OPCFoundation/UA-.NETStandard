// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WotBinderRegistryServiceCollectionExtensions"/>:
    /// DI registration, singleton semantics and idempotency.
    /// </summary>
    [TestFixture]
    public sealed class WotBinderRegistryDiTests
    {
        [Test]
        public void EnsureWotBinderRegistryRegistersIWotBinderRegistry()
        {
            var services = new ServiceCollection();
            services.EnsureWotBinderRegistry();
            var provider = services.BuildServiceProvider();

            var registry = provider.GetService<IWotBinderRegistry>();

            Assert.That(registry, Is.Not.Null);
        }

        [Test]
        public void EnsureWotBinderRegistryRegistersIWotBindingChannelFactory()
        {
            var services = new ServiceCollection();
            services.EnsureWotBinderRegistry();
            var provider = services.BuildServiceProvider();

            var factory = provider.GetService<IWotBindingChannelFactory>();

            Assert.That(factory, Is.Not.Null);
        }

        [Test]
        public void EnsureWotBinderRegistryBothInterfacesResolveSameSingletonInstance()
        {
            var services = new ServiceCollection();
            services.EnsureWotBinderRegistry();
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<IWotBinderRegistry>();
            var factory = provider.GetRequiredService<IWotBindingChannelFactory>();

            Assert.That(object.ReferenceEquals(registry, factory), Is.True);
        }

        [Test]
        public void EnsureWotBinderRegistryIsIdempotent()
        {
            var services = new ServiceCollection();
            services.EnsureWotBinderRegistry();
            services.EnsureWotBinderRegistry();
            services.EnsureWotBinderRegistry();
            var provider = services.BuildServiceProvider();

            // There should be exactly one WotProtocolBinderRegistry singleton,
            // both interfaces resolve to the same instance.
            var registry = provider.GetRequiredService<IWotBinderRegistry>();
            var factory = provider.GetRequiredService<IWotBindingChannelFactory>();

            Assert.That(object.ReferenceEquals(registry, factory), Is.True);
        }

        [Test]
        public void EnsureWotBinderRegistryNullServicesThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => WotBinderRegistryServiceCollectionExtensions.EnsureWotBinderRegistry(null!));
        }

        [Test]
        public void EnsureWotBinderRegistryConcreteTypeIsAlsoResolvable()
        {
            var services = new ServiceCollection();
            services.EnsureWotBinderRegistry();
            var provider = services.BuildServiceProvider();

            var concrete = provider.GetService<WotProtocolBinderRegistry>();

            Assert.That(concrete, Is.Not.Null);
        }

        [Test]
        public void EnsureWotBinderRegistryWithNoBinders_RegistryHasNoBinders()
        {
            var services = new ServiceCollection();
            services.EnsureWotBinderRegistry();
            var provider = services.BuildServiceProvider();

            var concrete = provider.GetRequiredService<WotProtocolBinderRegistry>();

            Assert.That(concrete.Binders, Is.Empty);
        }
    }
}
