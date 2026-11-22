// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.PubSub.Redundancy;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.DependencyInjection
{
    /// <summary>
    /// Unit tests for <see cref="PubSubRedundancyBuilderExtensions"/> — the
    /// PubSub high-availability (redundancy) activation-coordinator and
    /// lease-store DI surface.
    /// </summary>
    [TestFixture]
    [TestSpec("9.1.6", Summary = "PubSub redundancy activation-coordinator / lease-store DI")]
    public class PubSubRedundancyBuilderExtensionsTests
    {
        private static (IPubSubBuilder Builder, ServiceCollection Services) CreatePubSubBuilder()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IPubSubBuilder captured = null!;
            services.AddOpcUa().AddPubSub(pubsub => captured = pubsub);
            return (captured, services);
        }

        [Test]
        public void WithActivationCoordinatorGenericRegistersTypedCoordinator()
        {
            (IPubSubBuilder builder, ServiceCollection services) = CreatePubSubBuilder();

            IPubSubBuilder returned = builder.WithActivationCoordinator<StubActivationCoordinator>();

            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            Assert.That(returned, Is.SameAs(builder));
            Assert.That(
                serviceProvider.GetRequiredService<IPubSubActivationCoordinator>(),
                Is.InstanceOf<StubActivationCoordinator>());
        }

        [Test]
        public void WithLeaseStoreGenericRegistersTypedStore()
        {
            (IPubSubBuilder builder, ServiceCollection services) = CreatePubSubBuilder();

            IPubSubBuilder returned = builder.WithLeaseStore<InMemoryPubSubLeaseStore>();

            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            Assert.That(returned, Is.SameAs(builder));
            Assert.That(
                serviceProvider.GetRequiredService<IPubSubLeaseStore>(),
                Is.InstanceOf<InMemoryPubSubLeaseStore>());
        }

        [Test]
        public async Task WithLeaseActivationRegistersLeaseCoordinatorAndDefaultStoreAsync()
        {
            (IPubSubBuilder builder, ServiceCollection services) = CreatePubSubBuilder();

            IPubSubBuilder returned = builder.WithLeaseActivation(options => options.OwnerId = "node-1");

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();

            Assert.Multiple(() =>
            {
                Assert.That(returned, Is.SameAs(builder));
                Assert.That(
                    serviceProvider.GetRequiredService<IPubSubActivationCoordinator>(),
                    Is.InstanceOf<LeaseActivationCoordinator>());
                Assert.That(
                    serviceProvider.GetRequiredService<IPubSubLeaseStore>(),
                    Is.InstanceOf<InMemoryPubSubLeaseStore>());
            });
        }

        [Test]
        public async Task WithLeaseActivationHonorsPreregisteredLeaseStoreAsync()
        {
            (IPubSubBuilder builder, ServiceCollection services) = CreatePubSubBuilder();
            var store = new InMemoryPubSubLeaseStore();

            builder.WithLeaseStore(store).WithLeaseActivation();

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();

            Assert.That(serviceProvider.GetRequiredService<IPubSubLeaseStore>(), Is.SameAs(store));
        }

#pragma warning disable CA1812  // Class is used as generic argument
        private sealed class StubActivationCoordinator : IPubSubActivationCoordinator
        {
            public event EventHandler<PubSubRoleChangedEventArgs>? RoleChanged
            {
                add { }
                remove { }
            }

            public ValueTask StartAsync(CancellationToken cancellationToken = default)
            {
                return default;
            }

            public ValueTask StopAsync(CancellationToken cancellationToken = default)
            {
                return default;
            }

            public ValueTask<PubSubComponentRole> GetRoleAsync(
                string componentId,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<PubSubComponentRole>(PubSubComponentRole.Active);
            }
        }
#pragma warning restore CA1812
    }
}
