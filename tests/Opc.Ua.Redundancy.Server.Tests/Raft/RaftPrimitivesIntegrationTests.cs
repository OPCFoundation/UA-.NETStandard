// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// IDE0230: byte-array literals below are opaque binary test vectors, not text; a
// UTF-8 "..."u8 literal would misrepresent their intent, so keep the explicit byte arrays.
#pragma warning disable IDE0230 // Use UTF-8 string literal

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Integration tests proving the linearizable primitives (single-use nonce registry, lease election) work over the
    /// Raft store, and that the consistency-mode DI registration auto-wires them.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class RaftPrimitivesIntegrationTests
    {
        [Test]
        public async Task NonceRegistryOverRaftEnforcesExactlyOnceUnderContentionAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(store);
            var nonce = ByteString.From(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            const int contenders = 24;

            IEnumerable<Task<bool>> races = Enumerable.Range(0, contenders)
                .Select(_ => registry.TryConsumeAsync(nonce).AsTask());
            bool[] results = await Task.WhenAll(races).ConfigureAwait(false);

            Assert.That(results.Count(consumed => consumed), Is.EqualTo(1),
                "a single-use nonce may be consumed exactly once across the replica set");
        }

        [Test]
        public async Task LeaseElectionOverRaftElectsSingleLeaderUnderContentionAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            await using var election1 = new SharedStoreLeaseElection(
                store, "lease/leader", "node-1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));
            await using var election2 = new SharedStoreLeaseElection(
                store, "lease/leader", "node-2", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));

            bool[] acquired = await Task.WhenAll(
                election1.TryAcquireOrRenewAsync().AsTask(),
                election2.TryAcquireOrRenewAsync().AsTask()).ConfigureAwait(false);

            Assert.That(acquired.Count(won => won), Is.EqualTo(1),
                "exactly one replica may hold the lease at a time");
            Assert.That(election1.IsLeader ^ election2.IsLeader, Is.True, "leadership is exclusive");
        }

        [Test]
        public async Task ConsistencyRegistrationAutoWiresNonceRegistryAsync()
        {
            // The CRDT session factory resolves ISharedKeyValueStore and builds a
            // SharedSingleUseNonceRegistry over it. With UseRedundancyConsistency
            // that store is Raft-backed, so the nonce "nonce/" CAS is linearizable
            // and no separate strongly-consistent backend (e.g. Redis) is needed.
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(_ => { })
                .UseRedundancyConsistency();
            await using ServiceProvider provider = services.BuildServiceProvider();

            ISharedKeyValueStore store = provider.GetRequiredService<ISharedKeyValueStore>();
            var registry = new SharedSingleUseNonceRegistry(store);
            var nonce = ByteString.From(new byte[] { 9, 9, 9, 9 });

            bool first = await registry.TryConsumeAsync(nonce).ConfigureAwait(false);
            bool second = await registry.TryConsumeAsync(nonce).ConfigureAwait(false);

            Assert.That(first, Is.True);
            Assert.That(second, Is.False, "a replayed nonce is rejected by the Raft-backed registry");
        }
    }
}
