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

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="RedundancyConsistencyBuilderExtensions"/>: consistency-mode DI selection.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class RedundancyConsistencyBuilderExtensionsTests
    {
        [Test]
        public async Task StrongModeRegistersRaftStoreAndElectionAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(_ => { })
                .UseRedundancyConsistency(RedundancyConsistencyMode.Strong);
            await using ServiceProvider provider = services.BuildServiceProvider();

            ISharedKeyValueStore store = provider.GetRequiredService<ISharedKeyValueStore>();
            ILeaderElection election = provider.GetRequiredService<ILeaderElection>();

            Assert.That(store, Is.InstanceOf<RaftSharedKeyValueStore>());
            Assert.That(election, Is.InstanceOf<RaftLeaderElection>());

            bool created = await store.CompareAndSwapAsync("k", default, ByteString.From(new byte[] { 1 }));
            Assert.That(created, Is.True, "the strong store provides a linearizable compare-and-swap");
            Assert.That(election.IsLeader, Is.True, "the single-node default replica is the leader once used");
        }

        [Test]
        public async Task EventualModeRegistersHybridStoreAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(_ => { })
                .UseRedundancyConsistency();
            await using ServiceProvider provider = services.BuildServiceProvider();

            ISharedKeyValueStore store = provider.GetRequiredService<ISharedKeyValueStore>();
            Assert.That(store, Is.InstanceOf<HybridSharedKeyValueStore>());

            // Strong-prefix keys get linearizable CAS; bulk keys are stored too.
            bool created = await store.CompareAndSwapAsync("nonce/x", default, ByteString.From(new byte[] { 1 }));
            await store.SetAsync("node/1", ByteString.From(new byte[] { 2 }));
            (bool found, _) = await store.TryGetAsync("node/1");

            Assert.That(created, Is.True);
            Assert.That(found, Is.True);
        }

        [Test]
        public async Task UseRaftLeaderElectionFalseSkipsElectionRegistrationAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(_ => { })
                .UseRedundancyConsistency(options =>
                {
                    options.Mode = RedundancyConsistencyMode.Strong;
                    options.UseRaftLeaderElection = false;
                });
            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetService<ILeaderElection>(), Is.Null);
            Assert.That(provider.GetRequiredService<ISharedKeyValueStore>(),
                Is.InstanceOf<RaftSharedKeyValueStore>());
        }

        [Test]
        public async Task ComposesBeforeUseDistributedAddressSpaceAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(_ => { })
                .UseRedundancyConsistency(RedundancyConsistencyMode.Strong)
                .UseDistributedAddressSpace();
            await using ServiceProvider provider = services.BuildServiceProvider();

            // UseDistributedAddressSpace uses TryAddSingleton, so the strong
            // consistency registration wins.
            Assert.That(provider.GetRequiredService<ISharedKeyValueStore>(),
                Is.InstanceOf<RaftSharedKeyValueStore>());
            Assert.That(provider.GetRequiredService<ILeaderElection>(),
                Is.InstanceOf<RaftLeaderElection>());
        }
    }
}
