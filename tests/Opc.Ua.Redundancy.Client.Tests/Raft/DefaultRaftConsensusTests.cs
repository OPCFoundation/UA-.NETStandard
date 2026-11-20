/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

// IDE0230: byte-array literals below are opaque binary test vectors, not text; a
// UTF-8 "..."u8 literal would misrepresent their intent, so keep the explicit byte arrays.
#pragma warning disable IDE0230 // Use UTF-8 string literal

// CA2000: system-under-test disposables are created per test and released at teardown;
//   there is no cross-test resource leak. Suppressed file-level for the suite.
#pragma warning disable CA2000 // Dispose objects before losing scope

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Raft;
using Raft.Transport;

namespace Opc.Ua.Client.Redundancy.Tests
{
    /// <summary>
    /// Coverage for <see cref="DefaultRaftConsensus"/>: the adapter that binds an external <c>RaftCs</c> replica to the
    /// <see cref="IRaftConsensus"/> seam (single-node self-election, factory validation, and the no-quorum path).
    /// </summary>
    [TestFixture]
    [Category("ClientRedundancy")]
    public sealed class DefaultRaftConsensusTests
    {
        private static readonly ArrayOf<ulong> ThreeMemberIds = new(new ulong[] { 1, 2, 3 }.AsMemory());

        [Test]
        public async Task SingleNodeElectsItselfLeaderAsync()
        {
            await using var consensus = DefaultRaftConsensus.CreateSingleNode();

            await consensus.StartAsync().ConfigureAwait(false);

            Assert.That(consensus.IsLeader, Is.True, "a single-voter RaftCs replica elects itself leader");
        }

        [Test]
        public async Task StartIsIdempotentAndStaysLeaderAsync()
        {
            await using var consensus = DefaultRaftConsensus.CreateSingleNode();

            await consensus.StartAsync().ConfigureAwait(false);
            await consensus.StartAsync().ConfigureAwait(false);

            Assert.That(consensus.IsLeader, Is.True);
        }

        [Test]
        public async Task StoreCompareAndSwapIsLinearizableAsync()
        {
            await using var store = new RaftSharedKeyValueStore(
                DefaultRaftConsensus.CreateSingleNode(), ownsConsensus: true);

            bool created = await store.CompareAndSwapAsync("k", default, ByteString.From(new byte[] { 1 }))
                .ConfigureAwait(false);
            bool createdAgain = await store.CompareAndSwapAsync("k", default, ByteString.From(new byte[] { 2 }))
                .ConfigureAwait(false);
            (bool found, ByteString value) = await store.TryGetAsync("k").ConfigureAwait(false);

            Assert.That(created, Is.True);
            Assert.That(createdAgain, Is.False, "the second create-if-absent loses once the key is committed");
            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(new byte[] { 1 }));
        }

        [Test]
        public async Task SetThenGetRoundTripsThroughCommittedLogAsync()
        {
            await using var store = new RaftSharedKeyValueStore(
                DefaultRaftConsensus.CreateSingleNode(), ownsConsensus: true);
            var payload = ByteString.From(new byte[] { 4, 5, 6 });

            await store.SetAsync("session/a", payload).ConfigureAwait(false);
            (bool found, ByteString value) = await store.TryGetAsync("session/a").ConfigureAwait(false);

            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(payload.ToArray()));
        }

        [Test]
        public async Task LeadershipChangedRaisedWhenSingleNodeBecomesLeaderAsync()
        {
            await using var consensus = DefaultRaftConsensus.CreateSingleNode();
            var becameLeader = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            consensus.LeadershipChanged += isLeader =>
            {
                if (isLeader)
                {
                    becameLeader.TrySetResult(true);
                }
            };

            await consensus.StartAsync().ConfigureAwait(false);

            Task winner = await Task.WhenAny(becameLeader.Task, Task.Delay(TimeSpan.FromSeconds(5)))
                .ConfigureAwait(false);
            Assert.That(winner, Is.SameAs(becameLeader.Task),
                "the leadership watcher must raise LeadershipChanged(true) once the node is leader");
        }

        [Test]
        public async Task CampaignKeepsSingleNodeLeaderAsync()
        {
            await using var consensus = DefaultRaftConsensus.CreateSingleNode();
            await consensus.StartAsync().ConfigureAwait(false);

            await consensus.CampaignAsync().ConfigureAwait(false);

            Assert.That(consensus.IsLeader, Is.True);
        }

        [Test]
        public void CreateSingleNodeWithZeroNodeIdThrows()
        {
            Assert.That(
                () => DefaultRaftConsensus.CreateSingleNode(0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void CreateClusterWithEmptyMembersThrows()
        {
            Assert.That(
                () => DefaultRaftConsensus.CreateCluster(1, default(ArrayOf<ulong>), null!),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task CreateClusterValidatesArgumentsAsync()
        {
            await using var network = new InMemoryNetwork();
            IRaftTransport transport = network.CreateNode(1);

            Assert.That(
                () => DefaultRaftConsensus.CreateCluster(0, transport, null!),
                Throws.TypeOf<ArgumentOutOfRangeException>(), "node id must be non-zero");
            Assert.That(
                () => DefaultRaftConsensus.CreateCluster(1, (IRaftTransport)null!, null!),
                Throws.TypeOf<ArgumentNullException>(), "transport must not be null");
            Assert.That(
                () => DefaultRaftConsensus.CreateCluster(1, transport, null!),
                Throws.TypeOf<ArgumentNullException>(), "storage must not be null");
        }

        [Test]
        public async Task LoneNodeWithoutQuorumTimesOutAndIsNotLeaderAsync()
        {
            // A three-voter cluster with only member 1 alive can never reach a
            // quorum, so StartAsync returns after the (short) ready timeout and
            // the node is deterministically not the leader. Every member's
            // transport endpoint is created so the lone node's outbound sends do
            // not fault against a missing peer.
            await using var network = new InMemoryNetwork();
            _ = network.CreateNode(2);
            _ = network.CreateNode(3);

            await using var node = DefaultRaftConsensus.CreateCluster(
                1,
                ThreeMemberIds,
                network.CreateNode(1),
                new RaftNodeOptions { TickInterval = TimeSpan.FromMilliseconds(10) },
                config =>
                {
                    config.ElectionTick = 5;
                    config.HeartbeatTick = 1;
                    config.PreVote = true;
                },
                readyTimeout: TimeSpan.FromMilliseconds(500));

            await node.StartAsync().ConfigureAwait(false);

            Assert.That(node.IsLeader, Is.False, "a lone node cannot win an election without a quorum");
        }

        [Test]
        public async Task DisposeIsIdempotentAsync()
        {
            var consensus = DefaultRaftConsensus.CreateSingleNode();
            await consensus.StartAsync().ConfigureAwait(false);

            await consensus.DisposeAsync().ConfigureAwait(false);
            Assert.That(async () => await consensus.DisposeAsync().ConfigureAwait(false), Throws.Nothing);
        }
    }
}
