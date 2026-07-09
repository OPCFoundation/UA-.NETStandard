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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: default <see cref="IAddressSpaceSynchronizer"/>. See the interface for
    /// the writer/reader role model.
    /// </summary>
    public sealed class AddressSpaceSynchronizer : IAddressSpaceSynchronizer
    {
        /// <summary>
        /// Creates a synchronizer between a local graph and a shared store.
        /// </summary>
        /// <param name="store">The shared node state store.</param>
        /// <param name="addressSpace">The local node graph.</param>
        /// <param name="isWriter">
        /// Predicate that reports whether this replica is the writer
        /// (leader). Defaults to always-writer (single instance).
        /// </param>
        /// <param name="logger">Optional logger for replication errors.</param>
        public AddressSpaceSynchronizer(
            INodeStateStore store,
            ILocalAddressSpace addressSpace,
            Func<bool>? isWriter = null,
            ILogger? logger = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
            m_isWriter = isWriter ?? (static () => true);
            m_logger = logger;
            m_onChanged = OnLocalNodeChanged;
            m_onNodeAdded = OnLocalNodeAdded;
            m_onNodeRemoved = OnLocalNodeRemoved;
            Volatile.Write(ref m_isWriterState, m_isWriter() ? 1 : 0);
        }

        /// <summary>
        /// Creates a synchronizer between a local graph and a shared store
        /// that follows a leader election's writer/reader transitions.
        /// </summary>
        /// <param name="store">The shared node state store.</param>
        /// <param name="addressSpace">The local node graph.</param>
        /// <param name="election">The leader election controlling writer role.</param>
        /// <param name="logger">Optional logger for replication errors.</param>
        public AddressSpaceSynchronizer(
            INodeStateStore store,
            ILocalAddressSpace addressSpace,
            ILeaderElection election,
            ILogger? logger = null)
            : this(store, addressSpace, () => election?.IsLeader ?? false, logger)
        {
            m_election = election ?? throw new ArgumentNullException(nameof(election));
            m_onLeadershipChanged = OnLeadershipChanged;
            m_election.LeadershipChanged += m_onLeadershipChanged;
            Volatile.Write(ref m_isWriterState, m_election.IsLeader ? 1 : 0);
        }

        /// <inheritdoc/>
        public bool IsWriter => Volatile.Read(ref m_isWriterState) != 0;

        /// <summary>
        /// Raised (for tests) after each inbound change is applied.
        /// </summary>
        internal event Action<NodeStateChange>? InboundApplied;

        /// <summary>
        /// Gets the number of node instances currently tracked for
        /// <see cref="NodeState.StateChanged"/> notifications.
        /// </summary>
        internal int TrackedNodeCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_attached.Count;
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask SeedOrHydrateAsync(CancellationToken ct = default)
        {
            // Fast path: hydrate from a published snapshot plus the bounded delta
            // log of changes after it, instead of streaming and applying every
            // node one at a time. Falls back to the streamed path below when the
            // store has no snapshot capability or none has been published yet.
            if (m_store is INodeStateSnapshotStore snapshotStore)
            {
                NodeStateSnapshot? snapshot = await snapshotStore
                    .TryReadSnapshotAsync(ct)
                    .ConfigureAwait(false);
                if (snapshot != null)
                {
                    await HydrateFromSnapshotAsync(snapshotStore, snapshot, ct).ConfigureAwait(false);
                    return;
                }
            }

            bool any = false;
            await foreach (IStoredNode stored in m_store.EnumerateAsync(ct).ConfigureAwait(false))
            {
                any = true;
                await TryApplyUpsertAsync(stored.NodeId, stored.Payload, ct).ConfigureAwait(false);
            }

            if (any)
            {
                // Apply the latest value for every hydrated variable; value
                // keys can be newer than the node payload they were carved
                // from. Stream the value keyspace in one pass instead of
                // issuing a read per variable so hydrating a large address
                // space costs a bounded number of round trips.
                await foreach ((NodeId nodeId, DataValue value) in m_store
                    .EnumerateValuesAsync(ct)
                    .ConfigureAwait(false))
                {
                    ApplyValue(nodeId, value);
                }
                return;
            }

            if (IsWriter)
            {
                foreach (NodeState node in m_addressSpace.Nodes)
                {
                    await m_store
                        .UpsertNodeAsync(
                            new StoredNode(node.NodeId, NodeStateSerializer.Serialize(m_addressSpace.Context, node)),
                            ct)
                        .ConfigureAwait(false);
                    if (node is BaseVariableState variable)
                    {
                        await m_store
                            .WriteValueAsync(
                                node.NodeId,
                                new DataValue(variable.Value, variable.StatusCode, variable.Timestamp),
                                ct)
                            .ConfigureAwait(false);
                    }
                }

                // Publish an initial snapshot so a standby that joins next hydrates
                // from it (and the seed's delta-log entries are trimmed).
                if (m_store is INodeStateSnapshotStore seedSnapshotStore)
                {
                    await seedSnapshotStore.WriteSnapshotAsync(ct).ConfigureAwait(false);
                }
            }
        }

        private async ValueTask HydrateFromSnapshotAsync(
            INodeStateSnapshotStore snapshotStore,
            NodeStateSnapshot snapshot,
            CancellationToken ct)
        {
            // Materialize the snapshot node topology in one bulk pass (no per-node
            // NodeAdded event or await), then apply the snapshot values. Every
            // entry seeds the per-key applied-sequence guard.
            var nodes = new List<NodeState>();
            var values = new List<(NodeId NodeId, ulong Sequence, DataValue Value)>();
            await foreach (NodeStateChange entry in snapshot.Entries.ConfigureAwait(false))
            {
                if (entry.Kind == NodeStateChangeKind.Upsert && entry.Node != null)
                {
                    nodes.Add(NodeStateSerializer.Deserialize(m_addressSpace.Context, entry.Node.Payload));
                    m_nodeSequence[entry.NodeId] = entry.Sequence;
                }
                else if (entry.Kind == NodeStateChangeKind.Value)
                {
                    values.Add((entry.NodeId, entry.Sequence, entry.Value));
                }
            }

            await m_addressSpace.AddOrUpdateRangeAsync(nodes, ct).ConfigureAwait(false);

            foreach ((NodeId nodeId, ulong sequence, DataValue value) in values)
            {
                ApplyValue(nodeId, value);
                m_valueSequence[nodeId] = sequence;
            }

            snapshotStore.ObserveSequence(snapshot.Sequence);

            // Replay the changes that occurred after the snapshot. The guard makes
            // this idempotent, so any overlap with the live feed started in Start()
            // cannot apply a stale change over a newer one.
            await foreach (NodeStateChange change in snapshotStore
                .ReadDeltaLogAsync(snapshot.Sequence, ct)
                .ConfigureAwait(false))
            {
                await ApplyInboundAsync(change, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public void Start()
        {
            lock (m_lock)
            {
                if (m_started)
                {
                    return;
                }
                m_started = true;
                m_outbound = Channel.CreateUnbounded<OutboundOp>(
                    new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
                m_addressSpace.NodeAdded += m_onNodeAdded;
                m_addressSpace.NodeRemoved += m_onNodeRemoved;
                foreach (NodeState node in m_addressSpace.Nodes)
                {
                    AttachStateChanged(node);
                }
                m_outboundTask = Task.Run(() => DrainOutboundAsync(m_cts.Token));
                if (!IsWriter)
                {
                    StartInboundLocked();
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
            }

            m_cts.Cancel();
            m_outbound?.Writer.TryComplete();
            StopInbound();

            await AwaitQuietlyAsync(m_outboundTask).ConfigureAwait(false);
            await AwaitQuietlyAsync(m_inboundCleanupTask).ConfigureAwait(false);
            await AwaitQuietlyAsync(m_snapshotTask).ConfigureAwait(false);

            m_addressSpace.NodeAdded -= m_onNodeAdded;
            m_addressSpace.NodeRemoved -= m_onNodeRemoved;
            if (m_election != null && m_onLeadershipChanged != null)
            {
                m_election.LeadershipChanged -= m_onLeadershipChanged;
            }
            DetachAll();
            m_cts.Dispose();
        }

        private void OnLocalNodeAdded(NodeState node)
        {
            AttachStateChanged(node);
            if (!IsWriter)
            {
                return;
            }
            Enqueue(OutboundOp.ForUpsert(
                node.NodeId,
                NodeStateSerializer.Serialize(m_addressSpace.Context, node)));
        }

        private void OnLocalNodeRemoved(NodeId nodeId)
        {
            DetachStateChanged(nodeId);
            if (!IsWriter)
            {
                return;
            }
            Enqueue(OutboundOp.ForDelete(nodeId));
        }

        private void OnLocalNodeChanged(ISystemContext context, NodeState node, NodeStateChangeMasks changes)
        {
            // Deletes are driven by ILocalAddressSpace.NodeRemoved.
            if ((changes & NodeStateChangeMasks.Deleted) != 0)
            {
                return;
            }
            if (!IsWriter)
            {
                return;
            }

            if (node is BaseVariableState variable && (changes & NodeStateChangeMasks.Value) != 0)
            {
                Enqueue(OutboundOp.ForValue(
                    node.NodeId,
                    new DataValue(variable.Value, variable.StatusCode, variable.Timestamp)));
            }

            if ((changes &
                (NodeStateChangeMasks.NonValue |
                    NodeStateChangeMasks.Children |
                    NodeStateChangeMasks.References)) != 0)
            {
                Enqueue(OutboundOp.ForUpsert(
                    node.NodeId,
                    NodeStateSerializer.Serialize(m_addressSpace.Context, node)));
            }
        }

        private void Enqueue(OutboundOp op)
        {
            m_outbound?.Writer.TryWrite(op);
        }

        private async Task DrainOutboundAsync(CancellationToken ct)
        {
            try
            {
                await foreach (OutboundOp op in m_outbound!.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    try
                    {
                        if (!IsWriter)
                        {
                            continue;
                        }

                        switch (op.Kind)
                        {
                            case OutboundOpKind.Value:
                                await m_store.WriteValueAsync(op.NodeId, op.Value, ct).ConfigureAwait(false);
                                break;
                            case OutboundOpKind.Upsert:
                                await m_store
                                    .UpsertNodeAsync(new StoredNode(op.NodeId, op.Payload), ct)
                                    .ConfigureAwait(false);
                                break;
                            case OutboundOpKind.Delete:
                                await m_store.DeleteNodeAsync(op.NodeId, ct).ConfigureAwait(false);
                                break;
                        }

                        MaybeTriggerSnapshotPublish();
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        m_logger?.LogError(ex, "Distributed address-space outbound write failed for {NodeId}.", op.NodeId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
        }

        private void MaybeTriggerSnapshotPublish()
        {
            if (m_store is not INodeStateSnapshotStore snapshotStore)
            {
                return;
            }
            if (Interlocked.Increment(ref m_writesSinceSnapshot) < SnapshotWriteThreshold)
            {
                return;
            }
            // Publish at most one snapshot at a time; writes keep accumulating and
            // the next publish captures them. The scan-and-write runs off the
            // outbound hot path.
            if (Interlocked.CompareExchange(ref m_snapshotInFlight, 1, 0) != 0)
            {
                return;
            }
            Interlocked.Exchange(ref m_writesSinceSnapshot, 0);
            m_snapshotTask = PublishSnapshotAsync(snapshotStore);
        }

        private async Task PublishSnapshotAsync(INodeStateSnapshotStore snapshotStore)
        {
            try
            {
                await snapshotStore.WriteSnapshotAsync(m_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
            catch (Exception ex)
            {
                m_logger?.LogError(ex, "Distributed address-space snapshot publish failed.");
            }
            finally
            {
                Interlocked.Exchange(ref m_snapshotInFlight, 0);
            }
        }

        private async Task ApplyInboundLoopAsync(
            IAsyncEnumerator<NodeStateChange> inboundEnumerator,
            ValueTask<bool> firstMove,
            CancellationToken ct)
        {
            try
            {
                bool hasValue = await firstMove.ConfigureAwait(false);
                while (hasValue)
                {
                    NodeStateChange change = inboundEnumerator.Current;
                    try
                    {
                        await ApplyInboundAsync(change, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_logger?.LogError(ex, "Distributed address-space inbound apply failed for {NodeId}.", change.NodeId);
                    }

                    InboundApplied?.Invoke(change);
                    hasValue = await inboundEnumerator.MoveNextAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
        }

        private void OnLeadershipChanged(bool isLeader)
        {
            Volatile.Write(ref m_isWriterState, isLeader ? 1 : 0);

            lock (m_lock)
            {
                if (!m_started || m_disposed)
                {
                    return;
                }

                if (isLeader)
                {
                    StopInboundLocked();
                }
                else
                {
                    StartInboundLocked();
                }
            }
        }

        private async ValueTask ApplyInboundAsync(NodeStateChange change, CancellationToken cancellationToken)
        {
            switch (change.Kind)
            {
                case NodeStateChangeKind.Upsert:
                    if (change.Node != null &&
                        change.Sequence >= NextApplicable(m_nodeSequence, change.NodeId))
                    {
                        await TryApplyUpsertAsync(change.NodeId, change.Node.Payload, cancellationToken)
                            .ConfigureAwait(false);
                        m_nodeSequence[change.NodeId] = change.Sequence;
                    }
                    break;
                case NodeStateChangeKind.Delete:
                    await m_addressSpace.RemoveNodeAsync(change.NodeId, cancellationToken).ConfigureAwait(false);
                    m_nodeSequence[change.NodeId] = change.Sequence;
                    m_valueSequence[change.NodeId] = change.Sequence;
                    break;
                case NodeStateChangeKind.Value:
                    if (change.Sequence >= NextApplicable(m_valueSequence, change.NodeId))
                    {
                        ApplyValue(change.NodeId, change.Value);
                        m_valueSequence[change.NodeId] = change.Sequence;
                    }
                    break;
            }
        }

        private static ulong NextApplicable(NodeIdDictionary<ulong> applied, NodeId nodeId)
        {
            // Returns the smallest sequence that may still be applied for this
            // key: one past the last applied sequence, or 0 when nothing has been
            // applied yet (so the first change — and the unsequenced streamed
            // fallback — always applies, while a replayed or duplicate change at
            // or below the last applied sequence is skipped).
            return applied.TryGetValue(nodeId, out ulong last) ? last + 1 : 0;
        }

        private async ValueTask TryApplyUpsertAsync(NodeId nodeId, ByteString payload, CancellationToken cancellationToken)
        {
            NodeState node = NodeStateSerializer.Deserialize(m_addressSpace.Context, payload);
            await m_addressSpace.AddOrUpdateNodeAsync(node, cancellationToken).ConfigureAwait(false);
        }

        private void ApplyValue(NodeId nodeId, DataValue value)
        {
            if (m_addressSpace.TryGetNode(nodeId, out NodeState? node) && node is BaseVariableState variable)
            {
                variable.Value = value.WrappedValue;
                variable.StatusCode = value.StatusCode;
                variable.Timestamp = value.SourceTimestamp;
                variable.ClearChangeMasks(m_addressSpace.Context, false);
            }
        }

        private void AttachStateChanged(NodeState node)
        {
            lock (m_lock)
            {
                if (m_attached.TryGetValue(node.NodeId, out NodeState? existing))
                {
                    if (ReferenceEquals(existing, node))
                    {
                        return;
                    }

                    existing.StateChanged -= m_onChanged;
                }

                m_attached[node.NodeId] = node;
                node.StateChanged += m_onChanged;
            }
        }

        private void DetachStateChanged(NodeId nodeId)
        {
            lock (m_lock)
            {
                if (m_attached.TryRemove(nodeId, out NodeState? node))
                {
                    node.StateChanged -= m_onChanged;
                }
            }
        }

        private void DetachAll()
        {
            lock (m_lock)
            {
                foreach (NodeState node in m_attached.Values)
                {
                    node.StateChanged -= m_onChanged;
                }
                m_attached.Clear();
            }
        }

        private void StartInboundLocked()
        {
            if (m_inboundTask != null)
            {
                return;
            }

            // Register the change-feed watcher synchronously (the first
            // MoveNextAsync runs the iterator prefix that adds the watcher) so
            // no change published after Start() returns is missed, then consume
            // it on a background task.
            var inboundCts = CancellationTokenSource.CreateLinkedTokenSource(m_cts.Token);
            IAsyncEnumerator<NodeStateChange> inboundEnumerator = m_store
                .SubscribeChangesAsync(inboundCts.Token)
                .GetAsyncEnumerator();
            ValueTask<bool> firstMove = inboundEnumerator.MoveNextAsync();
            m_inboundCts = inboundCts;
            m_inboundEnumerator = inboundEnumerator;
            m_inboundTask = Task.Run(() => ApplyInboundLoopAsync(inboundEnumerator, firstMove, inboundCts.Token));
        }

        private void StopInbound()
        {
            lock (m_lock)
            {
                StopInboundLocked();
            }
        }

        private void StopInboundLocked()
        {
            if (m_inboundEnumerator == null || m_inboundTask == null || m_inboundCts == null)
            {
                return;
            }

            IAsyncEnumerator<NodeStateChange> enumerator = m_inboundEnumerator;
            Task inboundTask = m_inboundTask;
            CancellationTokenSource inboundCts = m_inboundCts;
            m_inboundEnumerator = null;
            m_inboundTask = null;
            m_inboundCts = null;
            inboundCts.Cancel();

            Task cleanup = CleanupInboundAsync(enumerator, inboundTask, inboundCts);
            m_inboundCleanupTask = m_inboundCleanupTask == null
                ? cleanup
                : Task.WhenAll(m_inboundCleanupTask, cleanup);
        }

        private static async Task CleanupInboundAsync(
            IAsyncEnumerator<NodeStateChange> inboundEnumerator,
            Task inboundTask,
            CancellationTokenSource inboundCts)
        {
            try
            {
                await AwaitQuietlyAsync(inboundTask).ConfigureAwait(false);
            }
            finally
            {
                await inboundEnumerator.DisposeAsync().ConfigureAwait(false);
                inboundCts.Dispose();
            }
        }

        private static async Task AwaitQuietlyAsync(Task? task)
        {
            if (task == null)
            {
                return;
            }
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
        }

        private enum OutboundOpKind
        {
            Value,
            Upsert,
            Delete
        }

        private readonly struct OutboundOp
        {
            private OutboundOp(OutboundOpKind kind, NodeId nodeId, DataValue value, ByteString payload)
            {
                Kind = kind;
                NodeId = nodeId;
                Value = value;
                Payload = payload;
            }

            /// <summary>
            /// Gets the kind of operation to replicate.
            /// </summary>
            public OutboundOpKind Kind { get; }

            /// <summary>
            /// Gets the identifier of the node the operation applies to.
            /// </summary>
            public NodeId NodeId { get; }

            /// <summary>
            /// Gets the data value carried by a <see cref="OutboundOpKind.Value"/> operation.
            /// </summary>
            public DataValue Value { get; }

            /// <summary>
            /// Gets the encoded node payload carried by a <see cref="OutboundOpKind.Upsert"/> operation.
            /// </summary>
            public ByteString Payload { get; }

            /// <summary>
            /// Creates an operation that replicates a changed data value for a node.
            /// </summary>
            /// <param name="nodeId">The node whose value changed.</param>
            /// <param name="value">The new data value.</param>
            /// <returns>The outbound operation.</returns>
            public static OutboundOp ForValue(NodeId nodeId, DataValue value)
            {
                return new OutboundOp(OutboundOpKind.Value, nodeId, value, default);
            }

            /// <summary>
            /// Creates an operation that replicates the addition or update of a node.
            /// </summary>
            /// <param name="nodeId">The node being added or updated.</param>
            /// <param name="payload">The encoded node state.</param>
            /// <returns>The outbound operation.</returns>
            public static OutboundOp ForUpsert(NodeId nodeId, ByteString payload)
            {
                return new OutboundOp(OutboundOpKind.Upsert, nodeId, DataValue.Null, payload);
            }

            /// <summary>
            /// Creates an operation that replicates the removal of a node.
            /// </summary>
            /// <param name="nodeId">The node being removed.</param>
            /// <returns>The outbound operation.</returns>
            public static OutboundOp ForDelete(NodeId nodeId)
            {
                return new OutboundOp(OutboundOpKind.Delete, nodeId, DataValue.Null, default);
            }
        }

        private readonly INodeStateStore m_store;
        private readonly ILocalAddressSpace m_addressSpace;
        private readonly Func<bool> m_isWriter;
        private readonly ILeaderElection? m_election;
        private readonly ILogger? m_logger;
        private readonly NodeStateChangedHandler m_onChanged;
        private readonly Action<NodeState> m_onNodeAdded;
        private readonly Action<NodeId> m_onNodeRemoved;
        private readonly Action<bool>? m_onLeadershipChanged;
        private readonly CancellationTokenSource m_cts = new();
        private readonly Lock m_lock = new();
        private readonly NodeIdDictionary<NodeState> m_attached = [];
        private readonly NodeIdDictionary<ulong> m_nodeSequence = [];
        private readonly NodeIdDictionary<ulong> m_valueSequence = [];
        private Channel<OutboundOp>? m_outbound;
        private CancellationTokenSource? m_inboundCts;
        private IAsyncEnumerator<NodeStateChange>? m_inboundEnumerator;
        private Task? m_outboundTask;
        private Task? m_inboundTask;
        private Task? m_inboundCleanupTask;
        private Task? m_snapshotTask;
        private long m_writesSinceSnapshot;
        private int m_snapshotInFlight;
        private int m_isWriterState;
        private bool m_started;
        private bool m_disposed;

        private const int SnapshotWriteThreshold = 1024;
    }
}
