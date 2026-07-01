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

// ===========================================================================
// Adapter that binds the external Raft engine `RaftCs` (shipped alongside the
// Crdt libraries) to the in-repo IRaftConsensus seam. Opc.Ua.Redundancy
// references the `RaftCs` and `RaftCs.Transport` packages, so this is the
// default IRaftConsensus implementation.
//
// Usage:
//   - Single-node / in-process: DefaultRaftConsensus.CreateSingleNode().
//   - Multi-pod: construct a RaftNode with durable storage
//     (RaftCs.Storage.File) and a networked transport
//     (RaftCs.Transport.NanoMsg, sharing the NanoMsg substrate with the CRDT
//     gossip layer), then `new DefaultRaftConsensus(node)`. Wire either through
//     RedundancyConsistencyOptions.RaftConsensusFactory (server) or the client
//     AddRaftClientSharedStore/AddRedundantClientSharedStore factories.
// ===========================================================================

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Raft;
using Raft.Configuration;
using Raft.Storage;
using Raft.Transport;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: binds an external <c>RaftCs</c> <see cref="RaftNode"/> replica to the
    /// <see cref="IRaftConsensus"/> seam. Proposals map to <see cref="RaftNode.ProposeAsync"/>, the committed apply
    /// stream is <see cref="RaftNode.Committed"/>, and leadership is observed from <see cref="RaftNode.IsLeader"/>.
    /// </summary>
    public sealed class DefaultRaftConsensus : IRaftConsensus
    {
        /// <summary>
        /// Creates an adapter over a <c>RaftCs</c> replica.
        /// </summary>
        /// <param name="node">The Raft replica (constructed with its storage and transport).</param>
        /// <param name="ownsNode">When <c>true</c>, the node is disposed with this adapter.</param>
        /// <param name="leadershipPollInterval">
        /// How often the adapter samples <see cref="RaftNode.IsLeader"/> to raise
        /// <see cref="LeadershipChanged"/> (the node exposes state, not an event).
        /// </param>
        /// <param name="ownedHost">
        /// An optional resource (for example the in-memory network the transport belongs to) disposed after the node.
        /// </param>
        /// <param name="readyTimeout">
        /// How long <see cref="StartAsync"/> waits for the cluster to elect an initial leader before returning (so the
        /// first proposals are not dropped during the initial election). Defaults to 10 seconds.
        /// </param>
        public DefaultRaftConsensus(
            RaftNode node,
            bool ownsNode = true,
            TimeSpan leadershipPollInterval = default,
            IAsyncDisposable? ownedHost = null,
            TimeSpan readyTimeout = default)
        {
            m_node = node ?? throw new ArgumentNullException(nameof(node));
            m_ownsNode = ownsNode;
            m_ownedHost = ownedHost;
            m_pollInterval = leadershipPollInterval <= TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(50)
                : leadershipPollInterval;
            m_readyTimeout = readyTimeout <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(10)
                : readyTimeout;
        }

        /// <summary>
        /// Creates an adapter over a single-node <c>RaftCs</c> replica (in-memory storage and transport). The node
        /// elects itself leader, so it provides a real, self-contained linearizable backend for single-process
        /// deployments and tests; use <see cref="CreateCluster(ulong, ArrayOf{ulong}, IRaftTransport, RaftNodeOptions,
        /// Action{RaftConfig}, TimeSpan)"/> for a multi-node cluster.
        /// </summary>
        /// <param name="nodeId">This replica's unique, non-zero id.</param>
        /// <param name="readyTimeout">How long to wait for self-election on start (defaults to 10 seconds).</param>
        public static DefaultRaftConsensus CreateSingleNode(ulong nodeId = 1, TimeSpan readyTimeout = default)
        {
            if (nodeId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeId), "Raft node id must be non-zero.");
            }

            // CA2000: ownership of the network/transport/node transfers to the
            // returned adapter (ownsNode + ownedResources), which disposes them.
#pragma warning disable CA2000
            var network = new InMemoryNetwork();
            IRaftTransport transport = network.CreateNode(nodeId);
            var storage = new MemoryStorage(new ConfState([nodeId]));
            return CreateCluster(
                nodeId,
                transport,
                storage,
                new RaftNodeOptions { TickInterval = TimeSpan.FromMilliseconds(5) },
                config => config.ElectionTick = 3,
                readyTimeout,
                ownedResources: network);
#pragma warning restore CA2000
        }

        /// <summary>
        /// Creates an adapter over a multi-node <c>RaftCs</c> replica with in-memory (volatile) storage. The
        /// membership is the static set <paramref name="memberIds"/>; the caller supplies the network transport that
        /// links the members. For durable storage (crash-safe WAL) use the overload that takes an
        /// <see cref="IRaftWritableStorage"/>.
        /// </summary>
        /// <param name="nodeId">This replica's unique, non-zero id (must be one of <paramref name="memberIds"/>).</param>
        /// <param name="memberIds">The static cluster membership (voter ids).</param>
        /// <param name="transport">This replica's transport bound to <paramref name="nodeId"/>.</param>
        /// <param name="options">Optional driver options (tick interval, apply cap).</param>
        /// <param name="configure">Optional callback to tune the <see cref="RaftConfig"/> (its <c>Id</c> is set).</param>
        /// <param name="readyTimeout">How long <see cref="StartAsync"/> waits for an initial leader.</param>
        public static DefaultRaftConsensus CreateCluster(
            ulong nodeId,
            ArrayOf<ulong> memberIds,
            IRaftTransport transport,
            RaftNodeOptions? options = null,
            Action<RaftConfig>? configure = null,
            TimeSpan readyTimeout = default)
        {
            if (memberIds.IsEmpty)
            {
                throw new ArgumentException("At least one member id is required.", nameof(memberIds));
            }

            // CA2000: the storage is volatile (MemoryStorage is not disposable);
            // the node/transport ownership transfers to the returned adapter.
#pragma warning disable CA2000
            var storage = new MemoryStorage(new ConfState(ToArray(memberIds)));
            return CreateCluster(nodeId, transport, storage, options, configure, readyTimeout);
#pragma warning restore CA2000
        }

        /// <summary>
        /// Creates an adapter over a multi-node <c>RaftCs</c> replica with caller-supplied durable storage (for
        /// example a <c>FileRaftStorage</c> WAL). The membership is read from the storage's initial
        /// <c>ConfState</c>.
        /// </summary>
        /// <param name="nodeId">This replica's unique, non-zero id.</param>
        /// <param name="transport">This replica's transport bound to <paramref name="nodeId"/>.</param>
        /// <param name="storage">The durable (or in-memory) Raft log/state storage carrying the membership.</param>
        /// <param name="options">Optional driver options (tick interval, apply cap).</param>
        /// <param name="configure">Optional callback to tune the <see cref="RaftConfig"/> (its <c>Id</c> is set).</param>
        /// <param name="readyTimeout">How long <see cref="StartAsync"/> waits for an initial leader.</param>
        /// <param name="ownedResources">
        /// An optional resource (for example the in-memory network, or the storage when it is disposable) disposed
        /// after the node.
        /// </param>
        public static DefaultRaftConsensus CreateCluster(
            ulong nodeId,
            IRaftTransport transport,
            IRaftWritableStorage storage,
            RaftNodeOptions? options = null,
            Action<RaftConfig>? configure = null,
            TimeSpan readyTimeout = default,
            IAsyncDisposable? ownedResources = null)
        {
            if (nodeId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeId), "Raft node id must be non-zero.");
            }
            if (transport == null)
            {
                throw new ArgumentNullException(nameof(transport));
            }
            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
            }

            var config = new RaftConfig { Id = nodeId };
            configure?.Invoke(config);

            // CA2000: the node (which disposes the transport) and ownedResources
            // transfer to the returned adapter, which disposes them.
#pragma warning disable CA2000
            var node = new RaftNode(config, storage, transport, options);
            return new DefaultRaftConsensus(node, ownsNode: true, readyTimeout: readyTimeout, ownedHost: ownedResources);
#pragma warning restore CA2000
        }

        private static ulong[] ToArray(ArrayOf<ulong> ids)
        {
            var result = new ulong[ids.Count];
            for (int ii = 0; ii < ids.Count; ii++)
            {
                result[ii] = ids[ii];
            }
            return result;
        }

        /// <inheritdoc/>
        public bool IsLeader => m_node.IsLeader;

        /// <inheritdoc/>
        public event Action<bool>? LeadershipChanged;

        /// <inheritdoc/>
        public ChannelReader<ReadOnlyMemory<byte>> Committed => m_node.Committed;

        /// <inheritdoc/>
        public async ValueTask StartAsync(CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref m_started, 1) == 0)
            {
                await m_node.StartAsync(ct).ConfigureAwait(false);
                m_leadershipLoop = Task.Run(() => WatchLeadershipAsync(m_cts.Token), m_cts.Token);
                await WaitForInitialLeaderAsync(ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public ValueTask ProposeAsync(ReadOnlyMemory<byte> command, CancellationToken ct = default)
        {
            // RaftNode.ProposeAsync is accepted on the leader; on a follower the
            // node forwards the proposal to the leader it recognizes
            // (RaftConfig.DisableProposalForwarding is false). The opaque command
            // bytes — including this store's originator id and request id — are
            // preserved, so the originating replica still correlates its own
            // committed command back to the caller.
            return m_node.ProposeAsync(command, ct);
        }

        /// <inheritdoc/>
        public ValueTask CampaignAsync(CancellationToken ct = default)
        {
            return m_node.CampaignAsync(ct);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            m_cts.Cancel();
            if (m_leadershipLoop != null)
            {
                try
                {
                    await m_leadershipLoop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // expected on shutdown
                }
            }

            if (m_ownsNode)
            {
                await m_node.DisposeAsync().ConfigureAwait(false);
            }

            if (m_ownedHost != null)
            {
                await m_ownedHost.DisposeAsync().ConfigureAwait(false);
            }

            m_cts.Dispose();
        }

        private async Task WaitForInitialLeaderAsync(CancellationToken ct)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(m_readyTimeout);
            try
            {
                while (m_node.LeaderId == 0)
                {
                    await Task.Delay(10, timeoutCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timed out waiting for the initial election; proceed. Proposals
                // are forwarded once a leader is elected, and each proposal is
                // still bounded by its own cancellation token.
            }
        }

        private async Task WatchLeadershipAsync(CancellationToken ct)
        {
            bool last = false;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    bool now = m_node.IsLeader;
                    if (now != last)
                    {
                        last = now;
                        LeadershipChanged?.Invoke(now);
                    }
                    await Task.Delay(m_pollInterval, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
        }

        private readonly RaftNode m_node;
        private readonly bool m_ownsNode;
        private readonly IAsyncDisposable? m_ownedHost;
        private readonly TimeSpan m_pollInterval;
        private readonly TimeSpan m_readyTimeout;
        private readonly CancellationTokenSource m_cts = new();
        private Task? m_leadershipLoop;
        private int m_started;
        private int m_disposed;
    }
}
