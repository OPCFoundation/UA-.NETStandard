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
            : this(store, addressSpace, isWriter, logger, null, null)
        {
        }

        internal AddressSpaceSynchronizer(
            INodeStateStore store,
            ILocalAddressSpace addressSpace,
            Func<bool>? isWriter,
            ILogger? logger,
            Func<NodeId, bool>? ownsNode,
            string? partitionId,
            ReplicaNodeIdFactory? identity = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
            m_isWriter = isWriter ?? (static () => true);
            m_logger = logger;
            m_ownsNode = ownsNode ?? (static _ => true);
            m_partitionId = partitionId;
            m_identity = identity;
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
            : this(store, addressSpace, election, logger, null, null)
        {
        }

        internal AddressSpaceSynchronizer(
            INodeStateStore store,
            ILocalAddressSpace addressSpace,
            ILeaderElection election,
            ILogger? logger,
            Func<NodeId, bool>? ownsNode,
            string? partitionId,
            ReplicaNodeIdFactory? identity = null)
            : this(
                store,
                addressSpace,
                () => election?.IsLeader ?? false,
                logger,
                ownsNode,
                partitionId,
                identity)
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
            bool supportsPartitionMarker = m_partitionId != null &&
                m_store is INodeStatePartitionStore;
            bool partitionInitialized = await IsPartitionInitializedAsync(ct).ConfigureAwait(false);
            bool authoritativeReads = m_store is not INodeStateStoreReadConsistency readConsistency ||
                readConsistency.HasAuthoritativeReads;
            bool authoritative = authoritativeReads && (!supportsPartitionMarker || partitionInitialized);
            ulong fallbackDeltaSequence = 0;
            bool sawStoredState = false;

            // Fast path: hydrate from a published snapshot plus the bounded delta
            // log of changes after it, instead of streaming and applying every
            // node one at a time. Falls back to the streamed path below when the
            // store has no snapshot capability or none has been published yet.
            if (SupportsSnapshots && m_store is INodeStateSnapshotStore snapshotStore)
            {
                NodeStateSnapshot? snapshot = await snapshotStore
                    .TryReadSnapshotAsync(ct)
                    .ConfigureAwait(false);
                if (snapshot != null)
                {
                    sawStoredState = true;
                    fallbackDeltaSequence = snapshot.Sequence;
                    snapshotStore.ObserveSequence(snapshot.Sequence);
                    try
                    {
                        HashSet<NodeId> hydratedRoots = await HydrateFromSnapshotAsync(
                            snapshotStore,
                            snapshot,
                            authoritative,
                            ct).ConfigureAwait(false);
                        if (authoritative)
                        {
                            await RemoveMissingLocalRootsAsync(hydratedRoots, ct).ConfigureAwait(false);
                        }
                        else if (IsWriter && authoritativeReads)
                        {
                            await SeedLocalNodesAsync(ct).ConfigureAwait(false);
                        }
                        return;
                    }
                    catch (ServiceResultException exception)
                        when (exception.StatusCode == StatusCodes.BadDecodingError)
                    {
                        // A missing or corrupt snapshot chunk is not authoritative.
                        // Fall back to the complete node/value keyspaces below.
                    }
                }
            }

            var hydratedRootIds = new HashSet<NodeId>();
            if (m_store is ISequencedNodeStateStore sequencedStore)
            {
                await foreach ((IStoredNode stored, ulong sequence) in sequencedStore
                    .EnumerateNodesWithSequenceAsync(ct)
                    .ConfigureAwait(false))
                {
                    ObserveStoreSequence(sequence);
                    if (!m_ownsNode(stored.NodeId))
                    {
                        continue;
                    }
                    sawStoredState = true;
                    if (IsTopologyApplicable(stored.NodeId, sequence))
                    {
                        await TryApplyUpsertAsync(
                            stored.NodeId,
                            stored.Payload,
                            sequence,
                            ct).ConfigureAwait(false);
                        RecordTopology(stored.NodeId, sequence, exists: true);
                    }
                    UpdateHydratedRootIds(hydratedRootIds, stored.NodeId);
                }
            }
            else
            {
                await foreach (IStoredNode stored in m_store.EnumerateAsync(ct).ConfigureAwait(false))
                {
                    if (!m_ownsNode(stored.NodeId))
                    {
                        continue;
                    }
                    sawStoredState = true;
                    await TryApplyUpsertAsync(
                        stored.NodeId,
                        stored.Payload,
                        0,
                        ct).ConfigureAwait(false);
                    RecordTopology(stored.NodeId, 0, exists: true);
                    UpdateHydratedRootIds(hydratedRootIds, stored.NodeId);
                }
            }

            if (m_store is INodeStateSnapshotStore fallbackSnapshotStore)
            {
                await foreach (NodeStateChange change in fallbackSnapshotStore
                    .ReadDeltaLogAsync(fallbackDeltaSequence, ct)
                    .ConfigureAwait(false))
                {
                    sawStoredState = true;
                    if (authoritative ||
                        !authoritativeReads ||
                        change.Kind != NodeStateChangeKind.Delete)
                    {
                        await ApplyInboundAsync(change, ct).ConfigureAwait(false);
                        UpdateHydratedRootIds(hydratedRootIds, change.NodeId);
                    }
                }
            }
            if (!sawStoredState &&
                m_store is INodeStateStoreStateProbe stateProbe)
            {
                sawStoredState = await stateProbe
                    .HasStoredStateAsync(m_ownsNode, ct)
                    .ConfigureAwait(false);
            }

            if (!authoritativeReads)
            {
                // An eventual replica view can supply updates, but absence is not proof of deletion or a new store.
                await ApplyStoredValuesAsync(ct).ConfigureAwait(false);
                m_logger?.DistributedAddressSpaceEventualHydration();
                return;
            }

            if (authoritative &&
                (supportsPartitionMarker || sawStoredState))
            {
                // Apply the latest value for every hydrated variable; value
                // keys can be newer than the node payload they were carved
                // from. Stream the value keyspace in one pass instead of
                // issuing a read per variable so hydrating a large address
                // space costs a bounded number of round trips.
                await ApplyStoredValuesAsync(ct).ConfigureAwait(false);
                await RemoveMissingLocalRootsAsync(hydratedRootIds, ct).ConfigureAwait(false);
                return;
            }

            if (IsWriter)
            {
                await SeedLocalNodesAsync(ct).ConfigureAwait(false);
            }
            else if (hydratedRootIds.Count > 0)
            {
                await ApplyStoredValuesAsync(ct).ConfigureAwait(false);
            }
        }

        private async ValueTask ApplyStoredValuesAsync(CancellationToken ct)
        {
            if (m_store is ISequencedNodeStateStore sequencedStore)
            {
                await foreach ((NodeId nodeId, DataValue value, ulong sequence) in
                    sequencedStore
                        .EnumerateValuesWithSequenceAsync(ct)
                        .ConfigureAwait(false))
                {
                    ObserveStoreSequence(sequence);
                    if (m_ownsNode(nodeId) &&
                        IsValueApplicable(nodeId, sequence))
                    {
                        ApplyValueChange(nodeId, value, sequence);
                    }
                }
            }
            else
            {
                await foreach ((NodeId nodeId, DataValue value) in m_store
                    .EnumerateValuesAsync(ct)
                    .ConfigureAwait(false))
                {
                    if (m_ownsNode(nodeId))
                    {
                        ApplyValue(nodeId, value);
                    }
                }
            }
        }

        private async ValueTask<HashSet<NodeId>> HydrateFromSnapshotAsync(
            INodeStateSnapshotStore snapshotStore,
            NodeStateSnapshot snapshot,
            bool authoritative,
            CancellationToken ct)
        {
            snapshotStore.ObserveSequence(snapshot.Sequence);
            // Materialize the snapshot node topology in one bulk pass (no per-node
            // NodeAdded event or await), then apply the snapshot values. Every
            // entry seeds the per-key applied-sequence guard.
            var stagedNodes = new List<(NodeId NodeId, NodeState Source, ulong Sequence)>();
            var rootNodeIds = new HashSet<NodeId>();
            var values = new List<(NodeId NodeId, ulong Sequence, DataValue Value)>();
            await foreach (NodeStateChange entry in snapshot.Entries.ConfigureAwait(false))
            {
                if (!m_ownsNode(entry.NodeId))
                {
                    continue;
                }
                if (entry.Kind == NodeStateChangeKind.Upsert && entry.Node != null)
                {
                    stagedNodes.Add((
                        entry.NodeId,
                        DecodeStoredNode(entry.NodeId, entry.Node.Payload),
                        entry.Sequence));
                }
                else if (entry.Kind == NodeStateChangeKind.Value)
                {
                    values.Add((entry.NodeId, entry.Sequence, entry.Value));
                }
            }

            var nodes = new List<NodeState>(stagedNodes.Count);
            var appliedNodeSequences = new List<(NodeId NodeId, ulong Sequence)>();
            foreach ((NodeId nodeId, NodeState source, ulong sequence) in stagedNodes)
            {
                if (IsTopologyApplicable(nodeId, sequence))
                {
                    nodes.Add(await PrepareUpsertAsync(
                        nodeId,
                        source,
                        sequence,
                        ct).ConfigureAwait(false));
                    appliedNodeSequences.Add((nodeId, sequence));
                }
            }
            using (EnterInboundApply())
            {
                await m_addressSpace.AddOrUpdateRangeAsync(nodes, ct).ConfigureAwait(false);
            }
            foreach ((NodeId nodeId, ulong sequence) in appliedNodeSequences)
            {
                RecordTopology(nodeId, sequence, exists: true);
            }
            foreach ((NodeId nodeId, _, _) in stagedNodes)
            {
                UpdateHydratedRootIds(rootNodeIds, nodeId);
            }
            foreach (NodeState node in nodes)
            {
                if (m_addressSpace.TryGetNode(node.NodeId, out NodeState? liveNode))
                {
                    AttachStateChangedTree(liveNode);
                    ApplyPendingValues(liveNode);
                }
            }

            foreach ((NodeId nodeId, ulong sequence, DataValue value) in values)
            {
                if (IsValueApplicable(nodeId, sequence))
                {
                    ApplyValueChange(nodeId, value, sequence);
                }
            }

            // Replay the changes that occurred after the snapshot. The guard makes
            // this idempotent, so any overlap with the live feed started in Start()
            // cannot apply a stale change over a newer one.
            await foreach (NodeStateChange change in snapshotStore
                .ReadDeltaLogAsync(snapshot.Sequence, ct)
                .ConfigureAwait(false))
            {
                if (authoritative ||
                    change.Kind != NodeStateChangeKind.Delete)
                {
                    await ApplyInboundAsync(change, ct).ConfigureAwait(false);
                    UpdateHydratedRootIds(rootNodeIds, change.NodeId);
                }
            }
            return rootNodeIds;
        }

        private async ValueTask SeedLocalNodesAsync(CancellationToken ct)
        {
            foreach (NodeState node in m_addressSpace.Nodes)
            {
                if (!m_ownsNode(node.NodeId))
                {
                    continue;
                }
                ValidateOwnedTree(node);
                ByteString payload = SerializeLocalTree(node);
                await m_store
                    .UpsertNodeAsync(
                        new StoredNode(node.NodeId, payload),
                        ct)
                    .ConfigureAwait(false);
                await WriteVariableValuesAsync(node, ct).ConfigureAwait(false);
            }

            // Publish an initial snapshot so a standby that joins next hydrates
            // from it (and the seed's delta-log entries are trimmed).
            if (SupportsSnapshots && m_store is INodeStateSnapshotStore seedSnapshotStore)
            {
                await seedSnapshotStore.WriteSnapshotAsync(ct).ConfigureAwait(false);
            }
            await MarkPartitionInitializedAsync(ct).ConfigureAwait(false);
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
                    if (m_ownsNode(node.NodeId))
                    {
                        AttachStateChangedTree(node);
                    }
                }
                m_outboundTask = Task.Run(() => DrainOutboundAsync(m_cts.Token));
                if (!IsWriter)
                {
                    StartInboundLocked();
                }
            }
        }

        internal void StartBeforeHydration()
        {
            lock (m_lock)
            {
                m_bufferInbound = true;
            }
            Start();
        }

        internal async ValueTask CompleteHydrationAsync(
            CancellationToken cancellationToken = default)
        {
            while (true)
            {
                NodeStateChange[] buffered;
                lock (m_lock)
                {
                    if (m_bufferedInbound.Count == 0)
                    {
                        m_bufferInbound = false;
                        return;
                    }
                    buffered = [.. m_bufferedInbound];
                    m_bufferedInbound.Clear();
                }

                foreach (NodeStateChange change in buffered)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ApplyInboundAsync(change, cancellationToken).ConfigureAwait(false);
                    InboundApplied?.Invoke(change);
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

            try
            {
                Task outbound = AwaitQuietlyAsync(m_outboundTask);
                await Task.WhenAll(
                    outbound,
                    AwaitQuietlyAsync(m_inboundCleanupTask),
                    AwaitFinalSnapshotAsync(outbound)).ConfigureAwait(false);
            }
            finally
            {
                m_addressSpace.NodeAdded -= m_onNodeAdded;
                m_addressSpace.NodeRemoved -= m_onNodeRemoved;
                if (m_election != null && m_onLeadershipChanged != null)
                {
                    m_election.LeadershipChanged -= m_onLeadershipChanged;
                }
                DetachAll();
                m_cts.Dispose();
            }
        }

        private async Task AwaitFinalSnapshotAsync(Task outbound)
        {
            try
            {
                await outbound.ConfigureAwait(false);
            }
            finally
            {
                // The producer may assign its final snapshot task while it is shutting down.
                await AwaitQuietlyAsync(m_snapshotTask).ConfigureAwait(false);
            }
        }

        private void OnLocalNodeAdded(NodeState node)
        {
            if (!m_ownsNode(node.NodeId))
            {
                return;
            }
            m_identity?.ValidateRegistration(m_addressSpace.Context, node);
            ValidateOwnedTree(node);
            AttachStateChangedTree(node);
            if (!IsApplyingInbound)
            {
                ApplyPendingValues(node);
            }
            if (IsApplyingInbound || !IsWriter)
            {
                return;
            }
            NodeState root = GetReplicationRoot(node);
            Enqueue(OutboundOp.ForUpsert(
                root.NodeId,
                SerializeLocalTree(root)));
            EnqueueVariableValues(root);
        }

        private void OnLocalNodeRemoved(NodeId nodeId)
        {
            if (!m_ownsNode(nodeId))
            {
                return;
            }
            if (m_addressSpace.TryGetNode(nodeId, out NodeState? replacement))
            {
                AttachStateChangedTree(replacement);
                return;
            }
            DetachStateChanged(nodeId);
            if (IsApplyingInbound || !IsWriter)
            {
                return;
            }
            Enqueue(OutboundOp.ForDelete(nodeId));
        }

        private void OnLocalNodeChanged(ISystemContext context, NodeState node, NodeStateChangeMasks changes)
        {
            if (!m_ownsNode(node.NodeId) || IsApplyingInbound)
            {
                return;
            }
            lock (m_lock)
            {
                if (m_disposed ||
                    !m_attached.TryGetValue(node.NodeId, out TrackedNode? tracked) ||
                    !ReferenceEquals(tracked.Node, node))
                {
                    return;
                }
            }
            ValidateOwnedTree(node);
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
                NodeState replicationRoot = GetReplicationRoot(node);
                AttachStateChangedTree(replicationRoot);
                Enqueue(OutboundOp.ForUpsert(
                    replicationRoot.NodeId,
                    SerializeLocalTree(replicationRoot)));
                EnqueueVariableValues(replicationRoot);
            }
        }

        private NodeState GetReplicationRoot(NodeState node)
        {
            NodeState root = node;
            while (root is BaseInstanceState instance &&
                instance.Parent != null &&
                !instance.Parent.NodeId.IsNull &&
                m_ownsNode(instance.Parent.NodeId))
            {
                root = instance.Parent;
            }
            return root;
        }

        private void Enqueue(OutboundOp op)
        {
            if (op.Kind == OutboundOpKind.Value)
            {
                m_identity?.ValidateValue(m_addressSpace.Context, op.Value);
            }
            m_outbound?.Writer.TryWrite(op);
        }

        private ByteString SerializeLocalTree(NodeState node)
        {
            m_identity?.ValidateReplicatedTree(m_addressSpace.Context, node);
            return NodeStateSerializer.Serialize(m_addressSpace.Context, node);
        }

        private void EnqueueVariableValues(NodeState node)
        {
            if (node is BaseVariableState variable &&
                !node.NodeId.IsNull &&
                m_ownsNode(node.NodeId))
            {
                Enqueue(OutboundOp.ForValue(
                    node.NodeId,
                    new DataValue(
                        variable.Value,
                        variable.StatusCode,
                        variable.Timestamp)));
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(m_addressSpace.Context, children);
            foreach (BaseInstanceState child in children)
            {
                EnqueueVariableValues(child);
            }
        }

        private async ValueTask WriteVariableValuesAsync(
            NodeState node,
            CancellationToken cancellationToken)
        {
            if (node is BaseVariableState variable &&
                !node.NodeId.IsNull &&
                m_ownsNode(node.NodeId))
            {
                var value = new DataValue(variable.Value, variable.StatusCode, variable.Timestamp);
                m_identity?.ValidateValue(m_addressSpace.Context, value);
                await m_store
                    .WriteValueAsync(
                        node.NodeId,
                        value,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(m_addressSpace.Context, children);
            foreach (BaseInstanceState child in children)
            {
                await WriteVariableValuesAsync(child, cancellationToken)
                    .ConfigureAwait(false);
            }
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
                        m_logger?.DistributedAddressSpaceOutboundWriteFailed(ex, op.NodeId);
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
            if (!SupportsSnapshots || m_store is not INodeStateSnapshotStore snapshotStore)
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
                m_logger?.DistributedAddressSpaceSnapshotPublishFailed(ex);
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
                    bool buffered = false;
                    lock (m_lock)
                    {
                        if (m_bufferInbound)
                        {
                            m_bufferedInbound.Enqueue(change);
                            buffered = true;
                        }
                    }
                    if (buffered)
                    {
                        hasValue = await inboundEnumerator.MoveNextAsync().ConfigureAwait(false);
                        continue;
                    }

                    if (m_ownsNode(change.NodeId))
                    {
                        try
                        {
                            await ApplyInboundAsync(change, ct).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            m_logger?.DistributedAddressSpaceInboundApplyFailed(ex, change.NodeId);
                        }

                        InboundApplied?.Invoke(change);
                    }
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
            if (!m_ownsNode(change.NodeId))
            {
                return;
            }
            m_identity?.ReserveId(change.NodeId);
            if (change.Sequence > 0 &&
                m_store is INodeStateSnapshotStore snapshotStore)
            {
                snapshotStore.ObserveSequence(change.Sequence);
            }
            switch (change.Kind)
            {
                case NodeStateChangeKind.Upsert:
                    if (change.Node != null &&
                        IsTopologyApplicable(change.NodeId, change.Sequence))
                    {
                        await TryApplyUpsertAsync(
                            change.NodeId,
                            change.Node.Payload,
                            change.Sequence,
                            cancellationToken)
                            .ConfigureAwait(false);
                        RecordTopology(change.NodeId, change.Sequence, exists: true);
                    }
                    break;
                case NodeStateChangeKind.Delete:
                    if (change.Sequence > 0 &&
                        !IsTopologyApplicable(change.NodeId, change.Sequence))
                    {
                        break;
                    }
                    using (EnterInboundApply())
                    {
                        await m_addressSpace
                            .RemoveNodeAsync(change.NodeId, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    RecordTopology(change.NodeId, change.Sequence, exists: false);
                    lock (m_lock)
                    {
                        if (change.Sequence > 0 && IsValueApplicable(change.NodeId, change.Sequence))
                        {
                            m_valueSequence[change.NodeId] = change.Sequence;
                        }
                        if (m_pendingValues.TryGetValue(change.NodeId, out PendingValue pending) &&
                            pending.Sequence <= change.Sequence)
                        {
                            m_pendingValues.Remove(change.NodeId);
                        }
                    }
                    break;
                case NodeStateChangeKind.Value:
                    if (IsValueApplicable(change.NodeId, change.Sequence))
                    {
                        ApplyValueChange(change.NodeId, change.Value, change.Sequence);
                    }
                    break;
            }
        }

        private bool IsTopologyApplicable(NodeId nodeId, ulong sequence)
        {
            return !m_topologyState.TryGetValue(nodeId, out TopologyState state) || sequence > state.Sequence;
        }

        private bool IsValueApplicable(NodeId nodeId, ulong sequence)
        {
            return !m_valueSequence.TryGetValue(nodeId, out ulong last) || sequence > last;
        }

        private void RecordTopology(NodeId nodeId, ulong sequence, bool exists)
        {
            ulong deletedThrough = 0;
            if (m_topologyState.TryGetValue(nodeId, out TopologyState previous))
            {
                sequence = Math.Max(sequence, previous.Sequence);
                deletedThrough = previous.DeletedThrough;
            }
            if (!exists)
            {
                deletedThrough = Math.Max(deletedThrough, sequence);
            }
            m_topologyState[nodeId] = new TopologyState(sequence, exists, deletedThrough);
        }

        private void UpdateHydratedRootIds(HashSet<NodeId> rootIds, NodeId nodeId)
        {
            if (!m_ownsNode(nodeId) || !m_topologyState.TryGetValue(nodeId, out TopologyState state))
            {
                return;
            }
            if (state.Exists)
            {
                rootIds.Add(nodeId);
            }
            else
            {
                rootIds.Remove(nodeId);
            }
        }

        private bool IsApplyingInbound => m_inboundApplyDepth.Value > 0;

        private InboundApplyScope EnterInboundApply()
        {
            return new InboundApplyScope(m_inboundApplyDepth);
        }

        private async ValueTask TryApplyUpsertAsync(
            NodeId nodeId,
            ByteString payload,
            ulong sequence,
            CancellationToken cancellationToken)
        {
            NodeState node = await PrepareUpsertAsync(
                nodeId,
                payload,
                sequence,
                cancellationToken)
                .ConfigureAwait(false);
            using (EnterInboundApply())
            {
                await m_addressSpace
                    .AddOrUpdateNodeAsync(node, cancellationToken)
                    .ConfigureAwait(false);
            }
            ApplyPendingValues(node);
        }

        private ValueTask<NodeState> PrepareUpsertAsync(
            NodeId nodeId,
            ByteString payload,
            ulong sequence,
            CancellationToken cancellationToken)
        {
            return PrepareUpsertAsync(
                nodeId,
                DecodeStoredNode(nodeId, payload),
                sequence,
                cancellationToken);
        }

        private NodeState DecodeStoredNode(
            NodeId nodeId,
            ByteString payload)
        {
            NodeState source = NodeStateSerializer.Deserialize(
                m_addressSpace.Context,
                payload);
            m_identity?.AuthorizeReplicatedTree(m_addressSpace.Context, source);
            if (source.NodeId != nodeId || !m_ownsNode(source.NodeId))
            {
                throw new InvalidOperationException(
                    $"Stored node {nodeId} contains payload root {source.NodeId} outside its ownership partition.");
            }
            ValidateOwnedTree(source);
            return source;
        }

        private async ValueTask<NodeState> PrepareUpsertAsync(
            NodeId nodeId,
            NodeState source,
            ulong sequence,
            CancellationToken cancellationToken)
        {
            using InboundApplyScope inbound = EnterInboundApply();
            m_addressSpace.TryGetNode(nodeId, out NodeState? existingNode);
            Dictionary<NodeId, DataValue>? preservedValues = existingNode == null
                ? null
                : CaptureNewerVariableValues(existingNode, sequence);
            HashSet<NodeId>? previousDescendants = existingNode == null
                ? null
                : GetDescendantNodeIds(m_addressSpace.Context, existingNode);
            NodeState node = existingNode == null
                ? source
                : NodeStateSerializer.UpdateExisting(
                    m_addressSpace.Context,
                    existingNode,
                    source);
            m_identity?.AuthorizeReplicatedTree(m_addressSpace.Context, node);
            if (preservedValues is { Count: > 0 })
            {
                RestoreVariableValues(node, preservedValues);
            }
            if (previousDescendants != null)
            {
                previousDescendants.ExceptWith(
                    GetDescendantNodeIds(m_addressSpace.Context, node));
                foreach (NodeId removedNodeId in previousDescendants)
                {
                    if (m_ownsNode(removedNodeId))
                    {
                        await m_addressSpace
                            .RemoveNodeAsync(removedNodeId, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
            return node;
        }

        private Dictionary<NodeId, DataValue> CaptureNewerVariableValues(
            NodeState node,
            ulong topologySequence)
        {
            var values = new Dictionary<NodeId, DataValue>();
            CaptureNewerVariableValues(node, topologySequence, values);
            return values;
        }

        private void CaptureNewerVariableValues(
            NodeState node,
            ulong topologySequence,
            Dictionary<NodeId, DataValue> values)
        {
            if (node is BaseVariableState variable &&
                m_valueSequence.TryGetValue(node.NodeId, out ulong valueSequence) &&
                (topologySequence == 0 || valueSequence > topologySequence))
            {
                values[node.NodeId] = new DataValue(
                    variable.Value,
                    variable.StatusCode,
                    variable.Timestamp);
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(m_addressSpace.Context, children);
            foreach (BaseInstanceState child in children)
            {
                CaptureNewerVariableValues(child, topologySequence, values);
            }
        }

        private void RestoreVariableValues(
            NodeState node,
            Dictionary<NodeId, DataValue> values)
        {
            if (node is BaseVariableState variable &&
                values.TryGetValue(node.NodeId, out DataValue value))
            {
                variable.Value = value.WrappedValue;
                variable.StatusCode = value.StatusCode;
                variable.Timestamp = value.SourceTimestamp;
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(m_addressSpace.Context, children);
            foreach (BaseInstanceState child in children)
            {
                RestoreVariableValues(child, values);
            }
        }

        private void ObserveStoreSequence(ulong sequence)
        {
            if (sequence > 0 &&
                m_store is INodeStateSnapshotStore snapshotStore)
            {
                snapshotStore.ObserveSequence(sequence);
            }
        }

        private async ValueTask RemoveMissingLocalRootsAsync(
            HashSet<NodeId> hydratedRootIds,
            CancellationToken cancellationToken)
        {
            foreach (NodeState localNode in new List<NodeState>(m_addressSpace.Nodes))
            {
                if (m_ownsNode(localNode.NodeId) &&
                    !hydratedRootIds.Contains(localNode.NodeId))
                {
                    using (EnterInboundApply())
                    {
                        await m_addressSpace
                            .RemoveNodeAsync(localNode.NodeId, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    RecordTopology(localNode.NodeId, 0, exists: false);
                }
            }
        }

        private void ValidateOwnedTree(NodeState node)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(m_addressSpace.Context, children);
            foreach (BaseInstanceState child in children)
            {
                if (!child.NodeId.IsNull && !m_ownsNode(child.NodeId))
                {
                    throw new InvalidOperationException(
                        $"Node {node.NodeId} contains child {child.NodeId} outside its ownership partition.");
                }
                ValidateOwnedTree(child);
            }
        }

        private ValueTask<bool> IsPartitionInitializedAsync(CancellationToken cancellationToken)
        {
            return m_partitionId != null &&
                m_store is INodeStatePartitionStore partitionStore
                    ? partitionStore.IsPartitionInitializedAsync(
                        m_partitionId,
                        cancellationToken)
                    : new ValueTask<bool>(false);
        }

        private ValueTask MarkPartitionInitializedAsync(CancellationToken cancellationToken)
        {
            return m_partitionId != null &&
                m_store is INodeStatePartitionStore partitionStore
                    ? partitionStore.MarkPartitionInitializedAsync(
                        m_partitionId,
                        cancellationToken)
                    : default;
        }

        private static HashSet<NodeId> GetDescendantNodeIds(
            ISystemContext context,
            NodeState node)
        {
            var nodeIds = new HashSet<NodeId>();
            CollectDescendantNodeIds(context, node, nodeIds);
            return nodeIds;
        }

        private static void CollectDescendantNodeIds(
            ISystemContext context,
            NodeState node,
            HashSet<NodeId> nodeIds)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (!child.NodeId.IsNull)
                {
                    nodeIds.Add(child.NodeId);
                }
                CollectDescendantNodeIds(context, child, nodeIds);
            }
        }

        private void ApplyValueChange(NodeId nodeId, in DataValue value, ulong sequence)
        {
            m_identity?.ValidateValue(m_addressSpace.Context, value);
            lock (m_lock)
            {
                if (m_disposed ||
                    !IsValueApplicable(nodeId, sequence) ||
                    (m_pendingValues.TryGetValue(nodeId, out PendingValue pending) && pending.Sequence > sequence))
                {
                    return;
                }
                if (ApplyValue(nodeId, value))
                {
                    m_valueSequence[nodeId] = sequence;
                    m_pendingValues.Remove(nodeId);
                }
                else
                {
                    m_pendingValues[nodeId] = new PendingValue(sequence, value.Copy());
                }
            }
        }

        private void ApplyPendingValues(NodeState node, ulong deletedThrough = 0)
        {
            if (m_topologyState.TryGetValue(node.NodeId, out TopologyState topology))
            {
                deletedThrough = Math.Max(deletedThrough, topology.DeletedThrough);
            }
            lock (m_lock)
            {
                // A parent tombstone also ends its descendants' previous incarnation.
                if (deletedThrough > 0 && IsValueApplicable(node.NodeId, deletedThrough))
                {
                    m_valueSequence[node.NodeId] = deletedThrough;
                }
                if (m_pendingValues.TryGetValue(node.NodeId, out PendingValue pending))
                {
                    if (pending.Sequence <= deletedThrough && deletedThrough > 0)
                    {
                        m_pendingValues.Remove(node.NodeId);
                    }
                    else if (IsValueApplicable(node.NodeId, pending.Sequence))
                    {
                        ApplyValueChange(node.NodeId, pending.Value, pending.Sequence);
                    }
                }
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(m_addressSpace.Context, children);
            foreach (BaseInstanceState child in children)
            {
                ApplyPendingValues(child, deletedThrough);
            }
        }

        private bool ApplyValue(NodeId nodeId, in DataValue value)
        {
            if (m_addressSpace.TryGetNode(nodeId, out NodeState? node) && node is BaseVariableState variable)
            {
                using InboundApplyScope scope = EnterInboundApply();
                variable.Value = value.WrappedValue;
                variable.StatusCode = value.StatusCode;
                variable.Timestamp = value.SourceTimestamp;
                variable.ClearChangeMasks(m_addressSpace.Context, false);
                return true;
            }
            return false;
        }

        private void AttachStateChangedTree(NodeState node)
        {
            var current = new Dictionary<NodeId, TrackedNode>();
            NodeId parentId = (node as BaseInstanceState)?.Parent?.NodeId ?? NodeId.Null;
            CollectAttachments(node, parentId, current);
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }

                foreach (NodeId previousId in GetTrackedSubtree(node.NodeId))
                {
                    if (!current.ContainsKey(previousId))
                    {
                        DetachTrackedNode(previousId);
                    }
                }

                foreach (KeyValuePair<NodeId, TrackedNode> entry in current)
                {
                    bool sameInstance = false;
                    if (m_attached.TryGetValue(entry.Key, out TrackedNode? existing))
                    {
                        sameInstance = ReferenceEquals(existing.Node, entry.Value.Node);
                        if (!sameInstance)
                        {
                            existing.Node.StateChanged -= m_onChanged;
                        }
                        if (m_attached.TryGetValue(existing.ParentId, out TrackedNode? previousParent))
                        {
                            previousParent.Children.Remove(entry.Key);
                        }
                    }
                    m_attached[entry.Key] = entry.Value;
                    if (!sameInstance)
                    {
                        entry.Value.Node.StateChanged += m_onChanged;
                    }
                }
                foreach (KeyValuePair<NodeId, TrackedNode> entry in current)
                {
                    if (m_attached.TryGetValue(entry.Value.ParentId, out TrackedNode? parent))
                    {
                        parent.Children.Add(entry.Key);
                    }
                }
            }
        }

        private void CollectAttachments(NodeState node, NodeId parentId, Dictionary<NodeId, TrackedNode> attachments)
        {
            if (!node.NodeId.IsNull && m_ownsNode(node.NodeId))
            {
                attachments.Add(node.NodeId, new TrackedNode(node, parentId));
                parentId = node.NodeId;
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(m_addressSpace.Context, children);
            foreach (BaseInstanceState child in children)
            {
                CollectAttachments(child, parentId, attachments);
            }
        }

        private void DetachStateChanged(NodeId nodeId)
        {
            lock (m_lock)
            {
                // NodeRemoved is raised after the real node manager has cleared the runtime child links.
                foreach (NodeId trackedId in GetTrackedSubtree(nodeId))
                {
                    DetachTrackedNode(trackedId);
                }
            }
        }

        private List<NodeId> GetTrackedSubtree(NodeId nodeId)
        {
            var result = new List<NodeId>();
            var pending = new Stack<NodeId>();
            var visited = new HashSet<NodeId>();
            pending.Push(nodeId);
            while (pending.Count > 0)
            {
                NodeId current = pending.Pop();
                if (visited.Add(current) && m_attached.TryGetValue(current, out TrackedNode? tracked))
                {
                    result.Add(current);
                    foreach (NodeId childId in tracked.Children)
                    {
                        pending.Push(childId);
                    }
                }
            }
            return result;
        }

        private void DetachTrackedNode(NodeId nodeId)
        {
            if (m_attached.TryRemove(nodeId, out TrackedNode? tracked))
            {
                tracked.Node.StateChanged -= m_onChanged;
                if (m_attached.TryGetValue(tracked.ParentId, out TrackedNode? parent))
                {
                    parent.Children.Remove(nodeId);
                }
            }
        }

        private void DetachAll()
        {
            lock (m_lock)
            {
                foreach (TrackedNode tracked in m_attached.Values)
                {
                    tracked.Node.StateChanged -= m_onChanged;
                }
                m_attached.Clear();
                m_pendingValues.Clear();
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
            if (m_store is INodeStateSubscriptionPreparer preparer)
            {
                preparer.PrepareSubscription();
            }
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

        private bool SupportsSnapshots =>
            m_store is not INodeStateStoreReadConsistency consistency || consistency.SupportsSnapshots;

        private readonly record struct TopologyState(ulong Sequence, bool Exists, ulong DeletedThrough);

        private readonly record struct PendingValue(ulong Sequence, DataValue Value);

        private sealed class TrackedNode
        {
            public TrackedNode(NodeState node, NodeId parentId)
            {
                Node = node;
                ParentId = parentId;
            }

            public NodeState Node { get; }

            public NodeId ParentId { get; }

            public HashSet<NodeId> Children { get; } = [];
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

        private readonly struct InboundApplyScope : IDisposable
        {
            public InboundApplyScope(AsyncLocal<int> depth)
            {
                m_depth = depth;
                m_previous = depth.Value;
                depth.Value = m_previous + 1;
            }

            public void Dispose()
            {
                m_depth.Value = m_previous;
            }

            private readonly AsyncLocal<int> m_depth;
            private readonly int m_previous;
        }

        private readonly INodeStateStore m_store;
        private readonly ILocalAddressSpace m_addressSpace;
        private readonly Func<bool> m_isWriter;
        private readonly Func<NodeId, bool> m_ownsNode;
        private readonly string? m_partitionId;
        private readonly ReplicaNodeIdFactory? m_identity;
        private readonly ILeaderElection? m_election;
        private readonly ILogger? m_logger;
        private readonly NodeStateChangedHandler m_onChanged;
        private readonly Action<NodeState> m_onNodeAdded;
        private readonly Action<NodeId> m_onNodeRemoved;
        private readonly Action<bool>? m_onLeadershipChanged;
        private readonly CancellationTokenSource m_cts = new();
        private readonly Lock m_lock = new();
        private readonly NodeIdDictionary<TrackedNode> m_attached = [];
        private readonly NodeIdDictionary<TopologyState> m_topologyState = [];
        private readonly NodeIdDictionary<ulong> m_valueSequence = [];
        private readonly NodeIdDictionary<PendingValue> m_pendingValues = [];
        private readonly Queue<NodeStateChange> m_bufferedInbound = new();
        private readonly AsyncLocal<int> m_inboundApplyDepth = new();
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
        private bool m_bufferInbound;
        private bool m_started;
        private bool m_disposed;

        private const int SnapshotWriteThreshold = 1024;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="AddressSpaceSynchronizer"/>.
    /// </summary>
    internal static partial class AddressSpaceSynchronizerLog
    {
        /// <summary>
        /// Reports a failed outbound state publication.
        /// </summary>
        [LoggerMessage(EventId = RedundancyServerEventIds.AddressSpaceSynchronizer + 0, Level = LogLevel.Error,
            Message = "Distributed address-space outbound write failed for {NodeId}.")]
        public static partial void DistributedAddressSpaceOutboundWriteFailed(
            this ILogger logger,
            Exception exception,
            NodeId nodeId);

        /// <summary>
        /// Reports failure to publish a validated snapshot.
        /// </summary>
        [LoggerMessage(EventId = RedundancyServerEventIds.AddressSpaceSynchronizer + 1, Level = LogLevel.Error,
            Message = "Distributed address-space snapshot publish failed.")]
        public static partial void DistributedAddressSpaceSnapshotPublishFailed(
            this ILogger logger,
            Exception exception);

        /// <summary>
        /// Reports failure to apply an incoming node change.
        /// </summary>
        [LoggerMessage(EventId = RedundancyServerEventIds.AddressSpaceSynchronizer + 2, Level = LogLevel.Error,
            Message = "Distributed address-space inbound apply failed for {NodeId}.")]
        public static partial void DistributedAddressSpaceInboundApplyFailed(
            this ILogger logger,
            Exception exception,
            NodeId nodeId);

        /// <summary>
        /// Reports the non-destructive hydration semantics of an eventually consistent state view.
        /// </summary>
        [LoggerMessage(EventId = RedundancyServerEventIds.AddressSpaceSynchronizer + 3, Level = LogLevel.Information,
            Message = "Distributed address-space hydration used an eventual view; " +
                "absence-based cleanup, initial seeding, and snapshot compaction are unavailable.")]
        public static partial void DistributedAddressSpaceEventualHydration(this ILogger logger);
    }
}
