// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for distributed address-space fluent options.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class DistributedAddressSpaceOptionsTests
    {
        [Test]
        public void DefaultsMatchStaticSingleLeaderDeployment()
        {
            var options = new DistributedAddressSpaceOptions();

            Assert.That(options.KeyValueStoreFactory, Is.Null);
            Assert.That(options.UseLeaderElection, Is.False);
            Assert.That(options.LeaseKey, Is.EqualTo("addressspace/leader"));
            Assert.That(options.NodeId, Is.Empty);
            Assert.That(options.LeaseDuration, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(options.RenewInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(options.RedundancyMode, Is.EqualTo(RedundancySupport.Warm));
            Assert.That((object?)options.ServiceLevelLoadMetric, Is.Null);
            Assert.That((object?)options.HealthServiceLevel, Is.Null);
        }

        [Test]
        public async Task SharedElectionDoesNotUseAddressSpaceLocalFallbackAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(_ => { }).UseDistributedAddressSpace(options =>
            {
                options.UseLeaderElection = true;
                options.NodeId = "A";
                options.LeaseMemberIds = ["A", "B"];
            });
            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetRequiredService<ISharedKeyValueStore>(), Is.InstanceOf<InMemorySharedKeyValueStore>());
            Assert.That(() => provider.GetRequiredService<ILeaderElection>(), Throws.InvalidOperationException);
        }

        /// <summary>
        /// Explicit selection uses the protected publication lease even when an election was already registered.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SharedElectionUsesProtectedLeaseWithExplicitMembersAsync(bool preRegisteredElection)
        {
            var services = new ServiceCollection();
            services.AddSingleton<SimulatedSharedKeyValueStore>();
            services.AddSingleton<IRecordProtector>(_ =>
            {
                using var random = RandomNumberGenerator.Create();
                var keyBytes = new byte[32];
                random.GetBytes(keyBytes);
                return new AesCbcHmacRecordProtector(keyBytes);
            });
            services.AddSharedRecordFreshness(
                sp => sp.GetRequiredService<SimulatedSharedKeyValueStore>(), Guid.NewGuid(), Guid.NewGuid());
            if (preRegisteredElection)
            {
                services.AddSingleton<ILeaderElection>(_ => new StaticLeaderElection(false));
            }
            services.AddOpcUa().AddServer(_ => { }).UseDistributedAddressSpace(options =>
            {
                options.UseLeaderElection = true;
                options.NodeId = "A";
                options.LeaseMemberIds = ["A", "B"];
            });
            await using ServiceProvider provider = services.BuildServiceProvider();

            await provider.GetRequiredService<FreshnessProtectedKeyValueStore>().EnrollAsync();
            ILeaderElection election = provider.GetRequiredService<ILeaderElection>();
            Assert.That(election, Is.InstanceOf<SharedStoreLeaseElection>());
            Assert.That(await election.TryAcquireOrRenewAsync(), Is.True);
        }

        [Test]
        public async Task UseDistributedAddressSpaceRegistersDistributedServicesAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(_ => { })
                .UseDistributedAddressSpace(options =>
                {
                    options.RedundancyMode = RedundancySupport.Hot;
                    options.ServiceLevelLoadMetric = () => 2;
                    options.HealthServiceLevel = () => 240;
                });
            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetRequiredService<ISharedKeyValueStore>(),
                Is.InstanceOf<InMemorySharedKeyValueStore>());
            ILeaderElection election = provider.GetRequiredService<ILeaderElection>();
            Assert.That(election, Is.InstanceOf<StaticLeaderElection>());
            Assert.That(election.IsLeader, Is.True);

            IServiceLevelProvider serviceLevelProvider = provider.GetRequiredService<IServiceLevelProvider>();
            Assert.That(serviceLevelProvider, Is.InstanceOf<LeaderServiceLevelProvider>());
            Assert.That(serviceLevelProvider.GetServiceLevel(), Is.EqualTo(ServiceLevels.NoData));
            Assert.That(
                provider.GetServices<IServerStartupTask>(),
                Has.Some.InstanceOf<ServiceLevelStartupTask>());

            using DiagnosticsServerHarness harness = await DiagnosticsServerHarness.CreateAsync().ConfigureAwait(false);
            harness.Server.Setup(server => server.FindNodeManagers<ILocalAddressSpaceSource>()).Returns([]);
            await provider.GetRequiredService<DistributedAddressSpaceStartupTask>()
                .OnServerStartedAsync(harness.Server.Object).ConfigureAwait(false);
            Assert.That(serviceLevelProvider.GetServiceLevel(), Is.EqualTo(238));
        }
    }
}
