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
