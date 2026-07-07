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
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
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
    }
}
