// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.Hosting;

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
            Assert.That(options.NodeId, Is.EqualTo(Environment.MachineName));
            Assert.That(options.LeaseDuration, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(options.RenewInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(options.RedundancyMode, Is.EqualTo(RedundancySupport.Warm));
            Assert.That((object?)options.ServiceLevelLoadMetric, Is.Null);
            Assert.That((object?)options.HealthServiceLevel, Is.Null);
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
            Assert.That(serviceLevelProvider.GetServiceLevel(), Is.EqualTo((byte)(ServiceLevels.Maximum - 2)));
            Assert.That(
                provider.GetServices<IServerStartupTask>(),
                Has.Some.InstanceOf<ServiceLevelStartupTask>());
        }
    }
}
