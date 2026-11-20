// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

// CA2000: system-under-test disposables are created per test and released at teardown;
//   there is no cross-test resource leak. Suppressed file-level for the suite.
#pragma warning disable CA2000 // Dispose objects before losing scope

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Tests that exercise the in-repo Raft seams over a real <see cref="DefaultRaftConsensus"/> (a single-node RaftCs
    /// replica with real election, log replication, and commit), proving the adapter binding to the external engine.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class RaftCsConsensusTests
    {
        [Test]
        public async Task SingleNodeElectsItselfLeaderAsync()
        {
            await using var consensus = DefaultRaftConsensus.CreateSingleNode();
            await using var election = new RaftLeaderElection(consensus);

            await consensus.StartAsync().ConfigureAwait(false);

            Assert.That(consensus.IsLeader, Is.True, "a single-voter RaftCs replica elects itself leader");
            Assert.That(election.IsLeader, Is.True);
        }

        [Test]
        public async Task StoreCompareAndSwapIsLinearizableAsync()
        {
            await using var store = new RaftSharedKeyValueStore(
                DefaultRaftConsensus.CreateSingleNode(), ownsConsensus: true);

            bool created = await store.CompareAndSwapAsync("k", default, ByteString.From(new byte[] { 1 })).ConfigureAwait(false);
            bool createdAgain = await store.CompareAndSwapAsync("k", default, ByteString.From(new byte[] { 2 })).ConfigureAwait(false);
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
        public async Task NonceRegistryEnforcesExactlyOnceAsync()
        {
            await using var store = new RaftSharedKeyValueStore(
                DefaultRaftConsensus.CreateSingleNode(), ownsConsensus: true);
            var registry = new SharedSingleUseNonceRegistry(store);
            var nonce = ByteString.From(new byte[] { 1, 2, 3, 4 });

            bool first = await registry.TryConsumeAsync(nonce).ConfigureAwait(false);
            bool second = await registry.TryConsumeAsync(nonce).ConfigureAwait(false);

            Assert.That(first, Is.True);
            Assert.That(second, Is.False, "a replayed nonce is rejected by the RaftCs-backed registry");
        }
    }
}
