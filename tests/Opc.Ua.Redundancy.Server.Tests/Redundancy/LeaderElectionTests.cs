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
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for the leader-election implementations.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class LeaderElectionTests
    {
        private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(10);

        [Test]
        public async Task StaticLeaderElectionReportsFixedRoleAsync()
        {
            await using var leader = new StaticLeaderElection(true);
            await using var follower = new StaticLeaderElection(false);

            Assert.That(leader.IsLeader, Is.True);
            Assert.That(follower.IsLeader, Is.False);
        }

        [Test]
        public async Task FirstAcquirerBecomesLeaderAsync()
        {
            var time = new FakeTimeProvider();
            using var kv = new InMemorySharedKeyValueStore();
            await using SharedStoreLeaseElection election = NewElection(kv, "A", time);

            bool acquired = await election.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.True);
            Assert.That(election.IsLeader, Is.True);
        }

        [Test]
        public async Task SecondReplicaIsFollowerWhileLeaseHeldAsync()
        {
            var time = new FakeTimeProvider();
            using var kv = new InMemorySharedKeyValueStore();
            await using SharedStoreLeaseElection a = NewElection(kv, "A", time);
            await using SharedStoreLeaseElection b = NewElection(kv, "B", time);

            Assert.That(await a.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            Assert.That(await b.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.False);
            Assert.That(b.IsLeader, Is.False);
        }

        [Test]
        public async Task LeaderRenewsWithoutLosingLeadershipAsync()
        {
            var time = new FakeTimeProvider();
            using var kv = new InMemorySharedKeyValueStore();
            await using SharedStoreLeaseElection a = NewElection(kv, "A", time);
            await using SharedStoreLeaseElection b = NewElection(kv, "B", time);

            Assert.That(await a.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            time.Advance(TimeSpan.FromSeconds(10));
            Assert.That(await a.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True, "leader should renew");
            Assert.That(await b.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.False, "standby cannot take a live lease");
        }

        [Test]
        public async Task StandbyTakesOverAfterLeaseExpiresAsync()
        {
            var time = new FakeTimeProvider();
            using var kv = new InMemorySharedKeyValueStore();
            await using SharedStoreLeaseElection a = NewElection(kv, "A", time);
            await using SharedStoreLeaseElection b = NewElection(kv, "B", time);
            bool? demotedLeaderState = null;
            a.LeadershipChanged += value => demotedLeaderState = value;

            Assert.That(await a.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            time.Advance(LeaseDuration + TimeSpan.FromSeconds(1));

            Assert.That(await b.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True, "standby takes over the expired lease");
            Assert.That(await a.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.False, "old leader becomes follower");
            Assert.That(b.IsLeader, Is.True);
            Assert.That(a.IsLeader, Is.False);
            Assert.That(demotedLeaderState, Is.False);
        }

        [Test]
        public async Task ReleaseOnDisposeAllowsImmediateTakeoverAsync()
        {
            var time = new FakeTimeProvider();
            using var kv = new InMemorySharedKeyValueStore();

            SharedStoreLeaseElection a = NewElection(kv, "A", time);
            Assert.That(await a.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            await a.DisposeAsync().ConfigureAwait(false);

            await using SharedStoreLeaseElection b = NewElection(kv, "B", time);
            Assert.That(await b.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True, "released lease can be taken immediately");
        }

        [Test]
        public async Task LeadershipChangedFiresOnAcquireAsync()
        {
            var time = new FakeTimeProvider();
            using var kv = new InMemorySharedKeyValueStore();
            await using SharedStoreLeaseElection a = NewElection(kv, "A", time);
            bool? observed = null;
            a.LeadershipChanged += value => observed = value;

            await a.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(observed, Is.True);
        }

        private static SharedStoreLeaseElection NewElection(
            ISharedKeyValueStore store,
            string nodeId,
            TimeProvider time)
        {
            return new SharedStoreLeaseElection(store, "lease/asp", nodeId, LeaseDuration, RenewInterval, time);
        }
    }
}
