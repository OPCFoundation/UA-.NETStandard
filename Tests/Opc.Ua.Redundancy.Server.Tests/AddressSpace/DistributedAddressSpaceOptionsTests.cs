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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

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
