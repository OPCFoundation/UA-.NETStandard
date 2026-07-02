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

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a deterministic, in-process <see cref="IRaftConsensus"/> replica. Used
    /// standalone it forms a single-node "cluster" (always the leader, fully linearizable) for single-process
    /// deployments and tests; attached to an <see cref="InProcessRaftCluster"/> it shares one totally-ordered
    /// committed log with its peers so a multi-replica state machine converges without a network.
    /// </summary>
    /// <remarks>
    /// This is the offline / in-process backend behind <c>RaftSharedKeyValueStore</c> and
    /// <c>RaftLeaderElection</c>. The external multi-node Raft engine (<c>RaftCs</c>) is a drop-in replacement
    /// of the <see cref="IRaftConsensus"/> contract.
    /// </remarks>
    public sealed class InProcessRaftConsensus : IRaftConsensus
    {
        /// <summary>
        /// Creates a standalone single-node replica (its own private one-member cluster). The node is always the
        /// leader once started.
        /// </summary>
        /// <param name="nodeId">
        /// This replica's unique, non-zero identity (defaults to <c>1</c>).
        /// </param>
        public InProcessRaftConsensus(ulong nodeId = 1)
            : this(new InProcessRaftCluster(), nodeId)
        {
        }

        /// <summary>
        /// Creates a replica attached to a shared <paramref name="cluster"/>.
        /// </summary>
        /// <param name="cluster">The shared in-process cluster.</param>
        /// <param name="nodeId">
        /// This replica's unique, non-zero identity; the lowest live id is the leader.
        /// </param>
        public InProcessRaftConsensus(InProcessRaftCluster cluster, ulong nodeId)
        {
            m_cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));
            NodeId = nodeId;
            m_committed = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        }

        /// <summary>
        /// This replica's unique identity within the cluster.
        /// </summary>
        public ulong NodeId { get; }

        /// <inheritdoc/>
        public bool IsLeader => Volatile.Read(ref m_isLeader);

        /// <inheritdoc/>
        public event Action<bool>? LeadershipChanged;

        /// <inheritdoc/>
        public ChannelReader<ReadOnlyMemory<byte>> Committed => m_committed.Reader;

        /// <inheritdoc/>
        public ValueTask StartAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref m_started, 1) == 0)
            {
                m_cluster.Register(this);
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask ProposeAsync(ReadOnlyMemory<byte> command, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();

            // Copy into a stable, immutable buffer so every replica shares an
            // identical view independent of the caller's buffer lifetime.
            byte[] copy = command.ToArray();
            m_cluster.Propose(copy);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask CampaignAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();

            // Leadership is deterministic (lowest live id). Campaigning is a
            // no-op here; the real Raft backend forces an election campaign.
            return default;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return default;
            }

            m_cluster.Unregister(this);
            m_committed.Writer.TryComplete();
            return default;
        }

        internal void Deliver(ReadOnlyMemory<byte> command)
        {
            m_committed.Writer.TryWrite(command);
        }

        internal void SetLeadership(bool isLeader)
        {
            if (Volatile.Read(ref m_isLeader) == isLeader)
            {
                return;
            }
            Volatile.Write(ref m_isLeader, isLeader);
            LeadershipChanged?.Invoke(isLeader);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(InProcessRaftConsensus));
            }
        }

        private readonly InProcessRaftCluster m_cluster;
        private readonly Channel<ReadOnlyMemory<byte>> m_committed;
        private int m_started;
        private int m_disposed;
        private bool m_isLeader;
    }
}
