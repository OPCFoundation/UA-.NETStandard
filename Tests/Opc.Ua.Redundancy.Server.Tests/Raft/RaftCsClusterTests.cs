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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Raft;
using Raft.Transport;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Integration tests for a real multi-node <see cref="RaftCsConsensus"/> cluster (RaftCs over an in-process
    /// network): election, replication, follower forwarding, failover, and quorum-loss behaviour.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class RaftCsClusterTests
    {
        private static readonly ArrayOf<ulong> MemberIds = new(new ulong[] { 1, 2, 3 }.AsMemory());

        [Test]
        public async Task ThreeNodeClusterConvergesViaFollowerWriteAsync()
        {
            await using var network = new InMemoryNetwork();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            RaftCsConsensus[] nodes = CreateNodes(network);
            RaftSharedKeyValueStore[] stores = nodes
                .Select(n => new RaftSharedKeyValueStore(n, ownsConsensus: false, commitTimeout: TimeSpan.FromSeconds(15)))
                .ToArray();

            try
            {
                // Nodes must start concurrently: each waits for an initial leader,
                // which cannot be elected until a quorum is up.
                await Task.WhenAll(nodes.Select(n => n.StartAsync(cts.Token).AsTask()));

                int leader = await WaitForLeaderAsync(nodes, cts.Token);
                int follower = (leader + 1) % nodes.Length;

                // A write on a follower's store is forwarded to the leader,
                // committed, and replicated to every replica.
                ByteString payload = ByteString.From(new byte[] { 42 });
                await stores[follower].SetAsync("shared", payload, cts.Token);

                foreach (RaftSharedKeyValueStore store in stores)
                {
                    ByteString observed = await WaitForValueAsync(store, "shared", cts.Token);
                    Assert.That(observed.ToArray(), Is.EqualTo(payload.ToArray()));
                }
            }
            finally
            {
                await DisposeAllAsync(stores, nodes);
            }
        }

        [Test]
        public async Task LeaderFailoverKeepsClusterWritableAsync()
        {
            await using var network = new InMemoryNetwork();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            RaftCsConsensus[] nodes = CreateNodes(network);
            RaftSharedKeyValueStore[] stores = nodes
                .Select(n => new RaftSharedKeyValueStore(n, ownsConsensus: false, commitTimeout: TimeSpan.FromSeconds(15)))
                .ToArray();

            try
            {
                await Task.WhenAll(nodes.Select(n => n.StartAsync(cts.Token).AsTask()));
                int leader = await WaitForLeaderAsync(nodes, cts.Token);

                // Take the leader down; the remaining two must re-elect and stay
                // writable (quorum 2 of the original 3).
                await stores[leader].DisposeAsync();
                await nodes[leader].DisposeAsync();

                int[] survivors = Enumerable.Range(0, nodes.Length).Where(i => i != leader).ToArray();
                await WaitForLeaderAsync(survivors.Select(i => nodes[i]).ToArray(), cts.Token);

                ByteString payload = ByteString.From(new byte[] { 7 });
                await stores[survivors[0]].SetAsync("after-failover", payload, cts.Token);
                ByteString observed = await WaitForValueAsync(stores[survivors[1]], "after-failover", cts.Token);
                Assert.That(observed.ToArray(), Is.EqualTo(payload.ToArray()));
            }
            finally
            {
                await DisposeAllAsync(stores, nodes);
            }
        }

        [Test]
        public async Task ProposalTimesOutWhenQuorumLostAsync()
        {
            await using var network = new InMemoryNetwork();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            RaftCsConsensus[] nodes = CreateNodes(network);
            // Short commit timeout so the quorum-loss failure is quick.
            RaftSharedKeyValueStore[] stores = nodes
                .Select(n => new RaftSharedKeyValueStore(n, ownsConsensus: false, commitTimeout: TimeSpan.FromSeconds(1)))
                .ToArray();

            try
            {
                await Task.WhenAll(nodes.Select(n => n.StartAsync(cts.Token).AsTask()));
                int leader = await WaitForLeaderAsync(nodes, cts.Token);

                // Remove two of three replicas: the survivor can no longer reach a
                // quorum, so a proposal fails via the commit timeout instead of
                // hanging.
                int[] toKill = Enumerable.Range(0, nodes.Length).Where(i => i != leader).ToArray();
                foreach (int i in toKill)
                {
                    await stores[i].DisposeAsync();
                    await nodes[i].DisposeAsync();
                }

                Assert.That(
                    async () => await stores[leader].SetAsync("no-quorum", ByteString.From(new byte[] { 1 }), cts.Token),
                    Throws.TypeOf<TimeoutException>());
            }
            finally
            {
                await DisposeAllAsync(stores, nodes);
            }
        }

        private static RaftCsConsensus[] CreateNodes(InMemoryNetwork network)
        {
            var nodes = new RaftCsConsensus[3];
            for (int ii = 0; ii < nodes.Length; ii++)
            {
                ulong id = (ulong)(ii + 1);
                nodes[ii] = RaftCsConsensus.CreateCluster(
                    id,
                    MemberIds,
                    network.CreateNode(id),
                    new RaftNodeOptions { TickInterval = TimeSpan.FromMilliseconds(10) },
                    config =>
                    {
                        config.ElectionTick = 10;
                        config.HeartbeatTick = 1;
                        config.PreVote = true;
                        // Distinct fixed timeouts make the lowest-id live node win,
                        // keeping the test deterministic.
                        config.RandomizedElectionTimeout = (int)(id * 6);
                    },
                    readyTimeout: TimeSpan.FromSeconds(25));
            }
            return nodes;
        }

        private static async Task<int> WaitForLeaderAsync(RaftCsConsensus[] nodes, CancellationToken ct)
        {
            while (true)
            {
                for (int ii = 0; ii < nodes.Length; ii++)
                {
                    if (nodes[ii].IsLeader)
                    {
                        return ii;
                    }
                }
                await Task.Delay(20, ct);
            }
        }

        private static async Task<ByteString> WaitForValueAsync(
            RaftSharedKeyValueStore store,
            string key,
            CancellationToken ct)
        {
            while (true)
            {
                (bool found, ByteString value) = await store.TryGetAsync(key, ct);
                if (found)
                {
                    return value;
                }
                await Task.Delay(10, ct);
            }
        }

        private static async Task DisposeAllAsync(
            IReadOnlyList<RaftSharedKeyValueStore> stores,
            IReadOnlyList<RaftCsConsensus> nodes)
        {
            foreach (RaftSharedKeyValueStore store in stores)
            {
                try
                {
                    await store.DisposeAsync();
                }
                catch (ObjectDisposedException)
                {
                    // already disposed by the test body
                }
            }
            foreach (RaftCsConsensus node in nodes)
            {
                await node.DisposeAsync();
            }
        }
    }
}
