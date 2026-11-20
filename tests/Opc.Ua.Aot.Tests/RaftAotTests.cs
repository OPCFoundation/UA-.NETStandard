// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.Redundancy;

// CA2007: AOT tests run without a SynchronizationContext.
#pragma warning disable CA2007

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests that exercise the Raft (strong-consistency) building blocks over a real
    /// <see cref="DefaultRaftConsensus"/> replica, ensuring the external RaftCs engine is reachable and functional under
    /// NativeAOT.
    /// </summary>
    public class RaftAotTests
    {
        [Test]
        public async Task RaftCsStoreCompareAndSwapUnderAotAsync()
        {
            await using var consensus = DefaultRaftConsensus.CreateSingleNode();
            await using var store = new RaftSharedKeyValueStore(consensus, ownsConsensus: false);

            var value = new ByteString(new byte[] { 7, 8, 9 });
            bool created = await store.CompareAndSwapAsync("raft/aot", default, value).ConfigureAwait(false);
            (bool found, ByteString stored) = await store.TryGetAsync("raft/aot").ConfigureAwait(false);

            await Assert.That(created).IsTrue();
            await Assert.That(found).IsTrue();
            await Assert.That(stored.ToArray().Length).IsEqualTo(3);
        }

        [Test]
        public async Task RaftLeaderElectionUnderAotAsync()
        {
            await using var consensus = DefaultRaftConsensus.CreateSingleNode();
            await using var election = new RaftLeaderElection(consensus);

            await consensus.StartAsync().ConfigureAwait(false);

            await Assert.That(consensus.IsLeader).IsTrue();
            await Assert.That(election.IsLeader).IsTrue();
        }
    }
}
