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
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.Redundancy;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Tests for <see cref="PubSubRedundancyServiceCollectionExtensions.AddPubSubRedundancy"/>.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [Category("Redundancy")]
    public sealed class PubSubRedundancyServiceCollectionExtensionsTests
    {
        [Test]
        public async Task LeaderElectionModeWiresElectionCoordinatorAndSharedStoresAsync()
        {
            await using ServiceProvider provider = BuildProvider(
                new InMemorySharedKeyValueStore(),
                PubSubRedundancyElection.LeaderElection,
                registerElection: true);

            Assert.That(
                provider.GetRequiredService<IPubSubActivationCoordinator>(),
                Is.TypeOf<LeaderElectionActivationCoordinator>());
            Assert.That(
                provider.GetRequiredService<IPubSubRuntimeStateStore>(),
                Is.TypeOf<SharedStorePubSubRuntimeStateStore>());
            Assert.That(
                provider.GetRequiredService<IPubSubSecurityKeyStore>(),
                Is.TypeOf<SharedStorePubSubSecurityKeyStore>());
        }

        [Test]
        public async Task LeaseStoreModeWiresLeaseCoordinatorAndSharedLeaseStoreAsync()
        {
            await using ServiceProvider provider = BuildProvider(
                new InMemorySharedKeyValueStore(),
                PubSubRedundancyElection.LeaseStore,
                registerElection: false);

            Assert.That(
                provider.GetRequiredService<IPubSubLeaseStore>(),
                Is.TypeOf<SharedStorePubSubLeaseStore>());
            Assert.That(
                provider.GetRequiredService<IPubSubActivationCoordinator>(),
                Is.TypeOf<LeaseActivationCoordinator>());
        }

        [Test]
        public async Task HotLeaderElectionModeWiresCheckpointStoreAndSharedStoresAsync()
        {
            await using ServiceProvider provider = BuildProvider(
                new InMemorySharedKeyValueStore(),
                PubSubRedundancyElection.LeaderElection,
                registerElection: true,
                PubSubRedundancyMode.Hot);

            Assert.That(
                provider.GetRequiredService<IPubSubActivationCoordinator>(),
                Is.TypeOf<LeaderElectionActivationCoordinator>());
            Assert.That(
                provider.GetRequiredService<IPubSubRuntimeStateStore>(),
                Is.TypeOf<SharedStorePubSubRuntimeStateStore>());
            Assert.That(
                provider.GetRequiredService<IPubSubWriterCheckpointStore>(),
                Is.TypeOf<SharedStorePubSubWriterCheckpointStore>());
        }

        [Test]
        public async Task NetworkedStoreWithoutProtectorFailsClosedForSecurityKeyStoreAsync()
        {
            var networkedStore = new Mock<ISharedKeyValueStore>(MockBehavior.Loose).Object;
            await using ServiceProvider provider = BuildProvider(
                networkedStore,
                PubSubRedundancyElection.LeaderElection,
                registerElection: true);

            Assert.That(
                () => provider.GetRequiredService<IPubSubSecurityKeyStore>(),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task NetworkedStoreWithProtectorResolvesSecurityKeyStoreAsync()
        {
            var networkedStore = new Mock<ISharedKeyValueStore>(MockBehavior.Loose).Object;
            ServiceCollection services = CreateServices(networkedStore, registerElection: true);
            services.AddSingleton<IRecordProtector>(new AesCbcHmacRecordProtector(new byte[32]));
            services.AddPubSubRedundancy(o => o.Election = PubSubRedundancyElection.LeaderElection);

            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetRequiredService<IPubSubSecurityKeyStore>(),
                Is.TypeOf<SharedStorePubSubSecurityKeyStore>());
        }

        [Test]
        public void AddPubSubRedundancyRejectsNullServices()
        {
            Assert.That(
                () => PubSubRedundancyServiceCollectionExtensions.AddPubSubRedundancy(null!),
                Throws.ArgumentNullException);
        }

        private static ServiceCollection CreateServices(ISharedKeyValueStore store, bool registerElection)
        {
            var services = new ServiceCollection();
            services.AddSingleton(store);
            services.AddSingleton<IServiceMessageContext>(
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
            if (registerElection)
            {
                services.AddSingleton<ILeaderElection>(new StaticLeaderElection(true));
            }

            return services;
        }

        private static ServiceProvider BuildProvider(
            ISharedKeyValueStore store,
            PubSubRedundancyElection election,
            bool registerElection,
            PubSubRedundancyMode mode = PubSubRedundancyMode.Warm)
        {
            ServiceCollection services = CreateServices(store, registerElection);
            services.AddPubSubRedundancy(o =>
            {
                o.Election = election;
                o.Mode = mode;
            });
            return services.BuildServiceProvider();
        }
    }
}
