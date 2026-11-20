/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

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
        public async Task ColdModeKeepsProcessLocalRuntimeStateWhileWarmModeSharesItAsync()
        {
            await using ServiceProvider coldProvider = BuildProvider(
                new InMemorySharedKeyValueStore(),
                PubSubRedundancyElection.LeaderElection,
                registerElection: true,
                PubSubRedundancyMode.Cold);
            await using ServiceProvider warmProvider = BuildProvider(
                new InMemorySharedKeyValueStore(),
                PubSubRedundancyElection.LeaderElection,
                registerElection: true,
                PubSubRedundancyMode.Warm);

            Assert.Multiple(() =>
            {
                Assert.That(
                    coldProvider.GetRequiredService<IPubSubRuntimeStateStore>(),
                    Is.TypeOf<InMemoryPubSubRuntimeStateStore>());
                Assert.That(
                    coldProvider.GetRequiredService<IPubSubWriterCheckpointStore>(),
                    Is.TypeOf<NullPubSubWriterCheckpointStore>());
                Assert.That(
                    warmProvider.GetRequiredService<IPubSubRuntimeStateStore>(),
                    Is.TypeOf<SharedStorePubSubRuntimeStateStore>());
                Assert.That(
                    warmProvider.GetRequiredService<IPubSubWriterCheckpointStore>(),
                    Is.TypeOf<NullPubSubWriterCheckpointStore>());
            });
        }

        [Test]
        public async Task NetworkedStoreWithoutProtectorFailsClosedForSecurityKeyStoreAsync()
        {
            ISharedKeyValueStore networkedStore = new Mock<ISharedKeyValueStore>(MockBehavior.Loose).Object;
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
            ISharedKeyValueStore networkedStore = new Mock<ISharedKeyValueStore>(MockBehavior.Loose).Object;
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
