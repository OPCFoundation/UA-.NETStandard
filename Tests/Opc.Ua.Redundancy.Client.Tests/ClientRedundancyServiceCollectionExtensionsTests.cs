/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

// CA2000: system-under-test disposables are created per test and released at teardown
//   or via the resolving container; there is no cross-test resource leak.
#pragma warning disable CA2000 // Dispose objects before losing scope

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Redundancy.Tests
{
    /// <summary>
    /// Coverage for the client-redundancy dependency-injection registration helpers, focusing on the
    /// argument guards, the CRDT store registration, and the hosted-service lifecycle wiring.
    /// </summary>
    [TestFixture]
    [Category("ClientRedundancy")]
    public sealed class ClientRedundancyServiceCollectionExtensionsTests
    {
        private ITelemetryContext m_telemetry = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public async Task AddCrdtClientSharedStoreRegistersReplicatedStoreAsync()
        {
            await using var network = new InMemoryNetwork();
            var services = new ServiceCollection();
            services.AddCrdtClientSharedStore(ReplicaId.New(), _ => network.CreateTransport());

            await using ServiceProvider provider = services.BuildServiceProvider();
            ISharedKeyValueStore store = provider.GetRequiredService<ISharedKeyValueStore>();

            Assert.That(store, Is.InstanceOf<ReplicatedSharedKeyValueStore>());
        }

        [Test]
        public void AddCrdtClientSharedStoreThrowsWhenServicesNull()
        {
            Assert.That(
                () => ClientRedundancyServiceCollectionExtensions.AddCrdtClientSharedStore(
                    null!,
                    ReplicaId.New(),
                    _ => null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddCrdtClientSharedStoreThrowsWhenTransportFactoryNull()
        {
            var services = new ServiceCollection();

            Assert.That(
                () => services.AddCrdtClientSharedStore(ReplicaId.New(), null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddRaftClientSharedStoreThrowsWhenServicesNull()
        {
            Assert.That(
                () => ClientRedundancyServiceCollectionExtensions.AddRaftClientSharedStore(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddRedundantClientSharedStoreThrowsWhenServicesNull()
        {
            Assert.That(
                () => ClientRedundancyServiceCollectionExtensions.AddRedundantClientSharedStore(
                    null!,
                    RedundancyConsistencyMode.Strong,
                    ReplicaId.New()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddRedundantClientSessionThrowsWhenServicesNull()
        {
            Assert.That(
                () => ClientRedundancyServiceCollectionExtensions.AddRedundantClientSession(
                    null!,
                    _ => { }),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddRedundantClientSessionThrowsWhenConfigureNull()
        {
            var services = new ServiceCollection();

            Assert.That(
                () => services.AddRedundantClientSession(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddRedundantClientSessionThrowsWhenCreateSessionMissing()
        {
            var services = new ServiceCollection();

            Assert.That(
                () => services.AddRedundantClientSession(options => options.NodeId = "replica-a"),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task RedundantClientSessionHostedServiceStartsAndStopsSessionAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton(m_telemetry);
            services.AddSingleton<ISharedKeyValueStore>(_ => new InMemorySharedKeyValueStore());
            services.AddSingleton<ILeaderElection>(new StaticLeaderElection(false));
            services.AddRedundantClientSession(options =>
            {
                options.NodeId = "replica-a";
                options.CreateSessionAsync = _ => default;
            });

            await using ServiceProvider provider = services.BuildServiceProvider();
            IHostedService hosted = provider.GetServices<IHostedService>().Single();
            RedundantClientSession session = provider.GetRequiredService<RedundantClientSession>();

            await hosted.StartAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(session.Disposed, Is.False);

            await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(session.Disposed, Is.True);
        }
    }
}
