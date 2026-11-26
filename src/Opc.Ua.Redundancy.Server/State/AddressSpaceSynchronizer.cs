// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            m_logger = logger;
            m_ownsNode = ownsNode ?? (static _ => true);
            m_partitionId = partitionId;
            m_identity = identity;
            m_onChanged = OnLocalNodeChanged;
            m_onNodeAdded = OnLocalNodeAdded;
            m_onNodeRemoved = OnLocalNodeRemoved;
            m_writerRequested = isWriter?.Invoke() ?? true;
            m_requiresCheckpoint = !m_writerRequested;
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
            OnLeadershipChanged(m_election.IsLeader);
        }

        /// <inheritdoc/>
        public bool IsWriter => Role == ReplicationRole.Writer;

        /// <summary>
        /// Gets whether the local address space is verified and can be advertised as available.
        /// An elected writer is not ready until it has completed its address-space handoff.
        /// </summary>
        internal bool IsReady => Role != ReplicationRole.Unavailable;

        /// <summary>
        /// Gets a single snapshot of readiness and the effective replication role.
        /// </summary>
        internal (bool IsReady, bool IsWriter) ReplicationStatus
        {
            get
            {
                ReplicationRole role = Role;
                return (role != ReplicationRole.Unavailable, role == ReplicationRole.Writer);
            }
        }

        /// <summary>
        /// Indicates whether the election can bind mutations to a committed writer grant.
        /// </summary>
        internal static bool SupportsWriterFence(ILeaderElection election) =>
            election is ISharedStoreWriteFenceSource;

        /// <summary>
        /// Checks whether this synchronizer owns the given node's publication.
        /// </summary>
        internal bool OwnsNode(NodeId nodeId) => m_ownsNode(nodeId);

        /// <summary>
        /// Writes a value-cache update through the same grant and generation used by replication.
        /// </summary>
        internal async ValueTask PublishManagedValueAsync(NodeId nodeId, DataValue value, CancellationToken ct)
        {
            Task? epoch = GetWriterEpoch();
            if (epoch == null || !OwnsNode(nodeId))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotWritable, "The owning address-space writer is not verified.");
            }
            m_identity?.ValidateValue(m_addressSpace.Context, value);
            await m_applyGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await WriteOutboundAsync(new QueuedOutbound(OutboundOp.ForValue(nodeId, value), epoch), ct)
                    .ConfigureAwait(false);
                MaybeTriggerSnapshotPublish(epoch);
            }
            catch
            {
                OnWriterMutationFailed(epoch);
                throw;
            }
            finally
            {
                m_applyGate.Release();
            }
        }

        /// <summary>
        /// Raised for tests after an inbound change is processed without rejection.
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
            CancellationTokenSource hydration;
            lock (m_lock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(AddressSpaceSynchronizer));
                }
                hydration = CancellationTokenSource.CreateLinkedTokenSource(ct, m_cts.Token);
            }
            using var hydrationCleanup = hydration;
            ct = hydration.Token;
            await m_applyGate.WaitAsync(ct).ConfigureAwait(false);
            Task? seedEpoch;
            lock (m_lock)
            {
                seedEpoch = CanInitializeWriter ? m_roleChanged.Task : null;
            }
            try
            {
                if (seedEpoch != null && HasAuthoritativeReads &&
                    !await TryBindWriterFenceAsync(seedEpoch, ct).ConfigureAwait(false))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotWritable, "The address-space writer grant is not available.");
                }
                if (seedEpoch != null && RequiresCommitFence &&
                    await ((InMemoryNodeStateStore)m_store).HasPendingPublicationsAsync(ct).ConfigureAwait(false))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "An earlier address-space publication is unfinished.");
                }
                await SeedOrHydrateCoreAsync(seedEpoch, ct).ConfigureAwait(false);
            }
            catch
            {
                lock (m_lock)
                {
                    if (seedEpoch != null && !m_disposed && ReferenceEquals(seedEpoch, m_roleChanged.Task))
                    {
                        RequireCheckpointLocked();
                    }
                }
                throw;
            }
            finally
            {
                m_applyGate.Release();
            }
        }

        private async ValueTask SeedOrHydrateCoreAsync(Task? seedEpoch, CancellationToken ct)
        {
            bool supportsPartitionMarker = m_partitionId != null &&
                m_store is INodeStatePartitionStore;
            bool partitionInitialized = await IsPartitionInitializedAsync(ct).ConfigureAwait(false);
            bool authoritativeReads = HasAuthoritativeReads;
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
                        else if (seedEpoch != null && authoritativeReads)
                        {
                            await SeedLocalNodesAsync(seedEpoch, ct).ConfigureAwait(false);
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
            sawStoredState |= await ScanStoredNodesAsync(hydratedRootIds, false, ct).ConfigureAwait(false);

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
                await ApplyStoredValuesAsync(ct).ConfigureAwait(false);
                await RemoveMissingLocalRootsAsync(hydratedRootIds, ct).ConfigureAwait(false);
                return;
            }

            if (seedEpoch != null)
            {
                await SeedLocalNodesAsync(seedEpoch, ct).ConfigureAwait(false);
            }
            else if (hydratedRootIds.Count > 0)
            {
                await ApplyStoredValuesAsync(ct).ConfigureAwait(false);
            }
        }

        private async ValueTask<bool> ScanStoredNodesAsync(
            HashSet<NodeId> hydratedRootIds,
            bool validateCheckpoint,
            CancellationToken ct)
        {
            bool sawStoredState = false;
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
                    if (validateCheckpoint)
                    {
                        await ApplyCheckpointNodeAsync(stored, sequence, ct).ConfigureAwait(false);
                    }
                    else if (IsTopologyApplicable(stored.NodeId, sequence))
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
                    if (validateCheckpoint)
                    {
                        await ApplyCheckpointNodeAsync(stored, 0, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await TryApplyUpsertAsync(stored.NodeId, stored.Payload, 0, ct).ConfigureAwait(false);
                        RecordTopology(stored.NodeId, 0, exists: true);
                    }
                    UpdateHydratedRootIds(hydratedRootIds, stored.NodeId);
                }
            }
            return sawStoredState;
        }

        private async ValueTask ApplyCheckpointNodeAsync(IStoredNode stored, ulong sequence, CancellationToken ct)
        {
            NodeId nodeId = stored.NodeId;
            if (m_topologyState.TryGetValue(nodeId, out TopologyState state) && sequence < state.Sequence)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The address-space checkpoint moved backwards.");
            }
            try
            {
                NodeState source = DecodeStoredNode(nodeId, stored.Payload);
                if (IsTopologyApplicable(nodeId, sequence) || !state.Exists)
                {
                    await ApplyUpsertAsync(nodeId, source, sequence, ct).ConfigureAwait(false);
                    RecordTopology(nodeId, sequence, exists: true);
                }
            }
            catch (ServiceResultException ex)
            {
                SetRole(ReplicationRole.Unavailable);
                m_logger?.DistributedAddressSpaceInboundApplyFailed(ex, nodeId);
                await WithdrawUnreconstructableTreeAsync(nodeId, sequence, ct).ConfigureAwait(false);
                throw;
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

        private async ValueTask SeedLocalNodesAsync(Task epoch, CancellationToken ct)
        {
            using InMemoryNodeStateStore? writer = OpenFencedWriter(epoch);
            INodeStateStore writeStore = writer ?? m_store;
            foreach (NodeState node in m_addressSpace.Nodes)
            {
                if (!m_ownsNode(node.NodeId))
                {
                    continue;
                }
                ValidateOwnedTree(node);
                ByteString payload = SerializeLocalTree(node);
                VerifySeedEpoch(epoch);
                await writeStore
                    .UpsertNodeAsync(
                        new StoredNode(node.NodeId, payload),
                        ct)
                    .ConfigureAwait(false);
                await WriteVariableValuesAsync(writeStore, node, epoch, ct).ConfigureAwait(false);
            }

            // Publish an initial snapshot so a standby that joins next hydrates
            // from it (and the seed's delta-log entries are trimmed).
            if (SupportsSnapshots && writeStore is INodeStateSnapshotStore seedSnapshotStore)
            {
                VerifySeedEpoch(epoch);
                await seedSnapshotStore.WriteSnapshotAsync(ct).ConfigureAwait(false);
            }
            VerifySeedEpoch(epoch);
            await MarkPartitionInitializedAsync(writeStore, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void Start() => StartCore(hydrating: false);

        private void StartCore(bool hydrating)
        {
            lock (m_lock)
            {
                if (m_started || m_disposed)
                {
                    return;
                }
                m_started = true;
                m_outbound = Channel.CreateUnbounded<QueuedOutbound>(
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
                if (!hydrating)
                {
                    FinishHydrationLocked();
                }
                m_outboundTask = Task.Run(() => DrainOutboundAsync(m_cts.Token));
                // Register the first standby watch before returning, but never scan under the startup lock.
                TaskCompletionSource<bool> started = NewSignal();
                m_inboundTask = SuperviseInboundAsync(started.Task, m_cts.Token);
                started.TrySetResult(true);
            }
        }

        /// <summary>
        /// Starts replication, but defers live apply and readiness until initial hydration completes.
        /// </summary>
        internal void StartBeforeHydration() => StartCore(hydrating: true);

        /// <summary>
        /// Hands over initial hydration to live replication and waits for its first checkpoint outcome.
        /// </summary>
        internal async ValueTask CompleteHydrationAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                FinishHydrationLocked();
            }
            await m_initialCheckpoint.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private void FinishHydrationLocked()
        {
            if (m_hydrated.TrySetResult(true) && CanInitializeWriter && HasAuthoritativeReads &&
                (!RequiresCommitFence || m_writeView != null))
            {
                SetRole(ReplicationRole.Writer);
                m_initialCheckpoint.TrySetResult(true);
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
                SetRole(ReplicationRole.Unavailable);
                m_initialCheckpoint.TrySetResult(false);
            }

            m_cts.Cancel();
            m_outbound?.Writer.TryComplete();

            try
            {
                Task outbound = AwaitQuietlyAsync(m_outboundTask);
                await Task.WhenAll(
                    outbound,
                    AwaitQuietlyAsync(m_inboundTask),
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
                await m_applyGate.WaitAsync().ConfigureAwait(false);
                m_applyGate.Dispose();
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
            Task? epoch = IsApplyingInbound ? null : GetWriterEpoch();
            if (epoch == null)
            {
                return;
            }
            NodeState root = GetReplicationRoot(node);
            Enqueue(OutboundOp.ForUpsert(
                root.NodeId,
                SerializeLocalTree(root)), epoch);
            EnqueueVariableValues(root, epoch);
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
            Task? epoch = IsApplyingInbound ? null : GetWriterEpoch();
            if (epoch == null)
            {
                return;
            }
            Enqueue(OutboundOp.ForDelete(nodeId), epoch);
        }

        private void OnLocalNodeChanged(ISystemContext context, NodeState node, NodeStateChangeMasks changes)
        {
            if (!m_ownsNode(node.NodeId) || IsApplyingInbound)
            {
                return;
            }
            Task? epoch;
            lock (m_lock)
            {
                if (m_disposed ||
                    !m_attached.TryGetValue(node.NodeId, out TrackedNode? tracked) ||
                    !ReferenceEquals(tracked.Node, node))
                {
                    return;
                }
                epoch = IsWriter ? m_roleChanged.Task : null;
            }
            ValidateOwnedTree(node);
            // Deletes are driven by ILocalAddressSpace.NodeRemoved.
            if ((changes & NodeStateChangeMasks.Deleted) != 0)
            {
                return;
            }
            if (epoch == null)
            {
                return;
            }

            if (node is BaseVariableState variable && (changes & NodeStateChangeMasks.Value) != 0)
            {
                Enqueue(OutboundOp.ForValue(
                    node.NodeId,
                    new DataValue(variable.Value, variable.StatusCode, variable.Timestamp)), epoch);
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
                    SerializeLocalTree(replicationRoot)), epoch);
                EnqueueVariableValues(replicationRoot, epoch);
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

        private Task<bool>? GetWriterEpoch()
        {
            lock (m_lock)
            {
                return IsWriter ? m_roleChanged.Task : null;
            }
        }

        private void VerifySeedEpoch(Task epoch)
        {
            lock (m_lock)
            {
                if (!CanInitializeWriter || !ReferenceEquals(m_roleChanged.Task, epoch))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "Writer ownership changed during address-space seeding.");
                }
            }
        }

        private async ValueTask<bool> TryBindWriterFenceAsync(Task epoch, CancellationToken ct)
        {
            if (!RequiresCommitFence)
            {
                return true;
            }
            FencedAddressSpaceWriteView? current;
            lock (m_lock)
            {
                if (!ReferenceEquals(m_roleChanged.Task, epoch) || !m_writerRequested || m_disposed)
                {
                    return false;
                }
                current = m_writeView;
            }
            if (current != null)
            {
                await current.VerifyGrantAsync(ct).ConfigureAwait(false);
                return true;
            }
            ISharedKeyValueStore storage = ((InMemoryNodeStateStore)m_store).Storage;
            if (!InMemoryNodeStateStore.HasGuardedWrites(storage) ||
                storage is not IDeadlineGuardedSharedKeyValueStore guarded ||
                storage is not ISharedKeyValueStoreConsistency consistency ||
                m_election is not ISharedStoreWriteFenceSource source ||
                !source.TryCaptureWriteFence(
                    storage, out string guardKey, out Func<SharedStoreWriteGrant>? readBoundGrant) ||
                !consistency.IsLinearizable(guardKey) ||
                consistency.IsProcessLocal(guardKey))
            {
                return false;
            }
            var view = new FencedAddressSpaceWriteView(guarded, consistency, guardKey, readBoundGrant);
            await view.VerifyGrantAsync(ct).ConfigureAwait(false);
            lock (m_lock)
            {
                if (!ReferenceEquals(m_roleChanged.Task, epoch) || !m_writerRequested || m_disposed)
                {
                    return false;
                }
                m_writeView = view;
                return true;
            }
        }

        private InMemoryNodeStateStore? OpenFencedWriter(Task epoch)
        {
            if (!RequiresCommitFence)
            {
                return null;
            }
            lock (m_lock)
            {
                if (!ReferenceEquals(m_roleChanged.Task, epoch) || m_writeView == null || m_disposed)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotWritable, "The address-space writer grant is not available.");
                }
                return ((InMemoryNodeStateStore)m_store).CreateWriter(m_writeView);
            }
        }

        private void OnWriterMutationFailed(Task epoch)
        {
            if (!RequiresCommitFence)
            {
                return;
            }
            lock (m_lock)
            {
                if (ReferenceEquals(m_roleChanged.Task, epoch) && !m_disposed)
                {
                    RequireCheckpointLocked();
                }
            }
        }

        private void Enqueue(OutboundOp op, Task epoch)
        {
            if (op.Kind == OutboundOpKind.Value)
            {
                m_identity?.ValidateValue(m_addressSpace.Context, op.Value);
            }
            m_outbound?.Writer.TryWrite(new QueuedOutbound(op, epoch));
        }

        private ByteString SerializeLocalTree(NodeState node)
        {
            m_identity?.ValidateReplicatedTree(m_addressSpace.Context, node);
            return NodeStateSerializer.Serialize(m_addressSpace.Context, node);
        }

        private void EnqueueVariableValues(NodeState node, Task epoch)
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
                        variable.Timestamp)), epoch);
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(m_addressSpace.Context, children);
            foreach (BaseInstanceState child in children)
            {
                EnqueueVariableValues(child, epoch);
            }
        }

        private async ValueTask WriteVariableValuesAsync(
            INodeStateStore writeStore,
            NodeState node,
            Task epoch,
            CancellationToken cancellationToken)
        {
            if (node is BaseVariableState variable &&
                !node.NodeId.IsNull &&
                m_ownsNode(node.NodeId))
            {
                var value = new DataValue(variable.Value, variable.StatusCode, variable.Timestamp);
                m_identity?.ValidateValue(m_addressSpace.Context, value);
                VerifySeedEpoch(epoch);
                await writeStore
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
                await WriteVariableValuesAsync(writeStore, child, epoch, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private async Task DrainOutboundAsync(CancellationToken ct)
        {
            try
            {
                await foreach (QueuedOutbound queued in m_outbound!.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    OutboundOp op = queued.Operation;
                    try
                    {
                        // A demoted writer's accepted store operation must finish before the next checkpoint.
                        await m_applyGate.WaitAsync(ct).ConfigureAwait(false);
                        try
                        {
                            if (!ReferenceEquals(GetWriterEpoch(), queued.Epoch))
                            {
                                continue;
                            }

                            await WriteOutboundAsync(queued, ct).ConfigureAwait(false);
                            MaybeTriggerSnapshotPublish(queued.Epoch);
                        }
                        finally
                        {
                            m_applyGate.Release();
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        OnWriterMutationFailed(queued.Epoch);
                        m_logger?.DistributedAddressSpaceOutboundWriteFailed(ex, op.NodeId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
        }

        private async ValueTask WriteOutboundAsync(QueuedOutbound queued, CancellationToken ct)
        {
            using InMemoryNodeStateStore? writer = OpenFencedWriter(queued.Epoch);
            INodeStateStore writeStore = writer ?? m_store;
            OutboundOp op = queued.Operation;
            switch (op.Kind)
            {
                case OutboundOpKind.Value:
                    await writeStore.WriteValueAsync(op.NodeId, op.Value, ct).ConfigureAwait(false);
                    break;
                case OutboundOpKind.Upsert:
                    await writeStore.UpsertNodeAsync(new StoredNode(op.NodeId, op.Payload), ct).ConfigureAwait(false);
                    break;
                case OutboundOpKind.Delete:
                    await writeStore.DeleteNodeAsync(op.NodeId, ct).ConfigureAwait(false);
                    break;
            }
        }

        private void MaybeTriggerSnapshotPublish(Task epoch)
        {
            if (!ReferenceEquals(GetWriterEpoch(), epoch) ||
                !SupportsSnapshots || m_store is not INodeStateSnapshotStore snapshotStore)
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
            m_snapshotTask = PublishSnapshotAsync(snapshotStore, epoch);
        }

        private async Task PublishSnapshotAsync(INodeStateSnapshotStore snapshotStore, Task epoch)
        {
            try
            {
                await m_applyGate.WaitAsync(m_cts.Token).ConfigureAwait(false);
                try
                {
                    if (ReferenceEquals(GetWriterEpoch(), epoch))
                    {
                        using InMemoryNodeStateStore? writer = OpenFencedWriter(epoch);
                        INodeStateSnapshotStore writeStore = writer ?? snapshotStore;
                        await writeStore.WriteSnapshotAsync(m_cts.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_applyGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
            catch (Exception ex)
            {
                OnWriterMutationFailed(epoch);
                m_logger?.DistributedAddressSpaceSnapshotPublishFailed(ex);
            }
            finally
            {
                Interlocked.Exchange(ref m_snapshotInFlight, 0);
            }
        }

        private async Task SuperviseInboundAsync(Task started, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    Task? waitForStandby;
                    lock (m_lock)
                    {
                        waitForStandby = IsWriter
                            ? m_roleChanged.Task
                            : CanInitializeWriter && HasAuthoritativeReads && !m_hydrated.Task.IsCompleted
                                ? Task.WhenAny(m_roleChanged.Task, m_hydrated.Task)
                                : null;
                    }
                    if (waitForStandby != null)
                    {
                        await waitForStandby.WaitAsync(ct).ConfigureAwait(false);
                        continue;
                    }

                    try
                    {
                        Task? promotionEpoch = await ConsumeInboundAsync(started, ct).ConfigureAwait(false);
                        lock (m_lock)
                        {
                            if (!m_disposed && m_writerRequested &&
                                ReferenceEquals(promotionEpoch, m_roleChanged.Task))
                            {
                                SetRole(ReplicationRole.Writer);
                                m_inboundRetryDelay = InitialRetryDelay;
                                m_initialCheckpoint.TrySetResult(true);
                            }
                        }
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        SetRole(ReplicationRole.Unavailable);
                        m_logger?.DistributedAddressSpaceInboundWatchFailed(ex);
                        m_initialCheckpoint.TrySetResult(false);
                        await Task.Delay(m_inboundRetryDelay, ct).ConfigureAwait(false);
                        m_inboundRetryDelay = TimeSpan.FromMilliseconds(Math.Min(
                            m_inboundRetryDelay.TotalMilliseconds * 2, MaxRetryDelay.TotalMilliseconds));
                    }
                }
            }
            catch (Exception ex) when (ct.IsCancellationRequested)
            {
                if (ex is not OperationCanceledException)
                {
                    m_logger?.DistributedAddressSpaceInboundWatchFailed(ex);
                }
            }
        }

        private async Task<Task?> ConsumeInboundAsync(Task started, CancellationToken ct)
        {
            using var watchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            CancellationToken watchToken = watchCts.Token;
            if (m_store is INodeStateSubscriptionPreparer preparer)
            {
                preparer.PrepareSubscription();
            }
            IAsyncEnumerator<NodeStateChange> watch = m_store
                .SubscribeChangesAsync(watchToken)
                .GetAsyncEnumerator(watchToken);
            await using var watchCleanup = watch.ConfigureAwait(false);
            Task<bool>? move = null;
            Task? promotionEpoch = null;
            try
            {
                move = watch.MoveNextAsync().AsTask();
                await started.ConfigureAwait(false);
                await m_hydrated.Task.WaitAsync(watchToken).ConfigureAwait(false);
                Task? checkpointEpoch = null;
                bool verified = false;
                Task? retryCheckpoint = null;

                while (true)
                {
                    watchToken.ThrowIfCancellationRequested();
                    Task roleEpoch;
                    bool writerRequested;
                    lock (m_lock)
                    {
                        roleEpoch = m_roleChanged.Task;
                        writerRequested = m_writerRequested;
                    }
                    if (!ReferenceEquals(checkpointEpoch, roleEpoch) || retryCheckpoint?.IsCompleted == true)
                    {
                        // The watch must overlap the scan to cover writes while the checkpoint is read.
                        // An already failed/completed watch cannot establish a checkpoint.
                        if (move.IsCompleted && !await move.ConfigureAwait(false))
                        {
                            throw new InvalidOperationException("The address-space watch ended.");
                        }
                        await m_applyGate.WaitAsync(watchToken).ConfigureAwait(false);
                        try
                        {
                            verified = (!writerRequested ||
                                await TryBindWriterFenceAsync(roleEpoch, watchToken).ConfigureAwait(false)) &&
                                await VerifyStandbyCheckpointAsync(watchToken).ConfigureAwait(false);
                            if (verified && writerRequested && RequiresCommitFence &&
                                await ((InMemoryNodeStateStore)m_store)
                                    .HasPendingPublicationsAsync(watchToken).ConfigureAwait(false))
                            {
                                // The guard cannot roll back sibling keys from a partially published older write.
                                verified = false;
                            }
                        }
                        finally
                        {
                            m_applyGate.Release();
                        }
                        checkpointEpoch = roleEpoch;
                        retryCheckpoint = !verified && HasAuthoritativeReads
                            ? Task.Delay(MaxRetryDelay, watchToken)
                            : null;
                    }

                    if (move.IsCompleted)
                    {
                        Task<bool> next = move;
                        move = null;
                        if (!await next.ConfigureAwait(false))
                        {
                            throw new InvalidOperationException("The address-space watch ended.");
                        }
                        await ApplyWatchedChangeAsync(watch.Current, watchToken).ConfigureAwait(false);
                        move = watch.MoveNextAsync().AsTask();
                        continue;
                    }

                    lock (m_lock)
                    {
                        if (m_disposed)
                        {
                            break;
                        }
                        if (move.IsCompleted || !ReferenceEquals(roleEpoch, m_roleChanged.Task))
                        {
                            continue;
                        }
                        if (verified && m_writerRequested)
                        {
                            promotionEpoch = roleEpoch;
                            break;
                        }
                        if (verified)
                        {
                            SetRole(ReplicationRole.Standby);
                            m_inboundRetryDelay = InitialRetryDelay;
                        }
                        m_initialCheckpoint.TrySetResult(verified);
                    }
                    if (retryCheckpoint == null)
                    {
                        await Task.WhenAny(move, roleEpoch).WaitAsync(watchToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await Task.WhenAny(move, roleEpoch, retryCheckpoint).WaitAsync(watchToken).ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                SetRole(ReplicationRole.Unavailable);
                throw;
            }
            finally
            {
                if (move?.IsCompleted == true)
                {
                    promotionEpoch = null;
                }
                watchCts.Cancel();
                if (move != null)
                {
                    try
                    {
                        if (await move.ConfigureAwait(false))
                        {
                            // A change became visible while retiring this watch. Reverify before writing.
                            promotionEpoch = null;
                        }
                    }
                    catch (OperationCanceledException) when (watchToken.IsCancellationRequested)
                    {
                        // Expected when interrupting the pending MoveNext.
                    }
                }
            }
            return promotionEpoch;
        }

        private async ValueTask<bool> VerifyStandbyCheckpointAsync(CancellationToken ct)
        {
            bool authoritative = HasAuthoritativeReads;
            bool hasMarker = m_partitionId != null && m_store is INodeStatePartitionStore;
            bool initialized = await IsPartitionInitializedAsync(ct).ConfigureAwait(false);
            var roots = new HashSet<NodeId>();
            bool hasNodes = await ScanStoredNodesAsync(roots, authoritative, ct).ConfigureAwait(false);
            if (m_store is INodeStateSnapshotStore snapshotStore)
            {
                await foreach (NodeStateChange change in snapshotStore.ReadDeltaLogAsync(0, ct).ConfigureAwait(false))
                {
                    if (!authoritative || (change.Kind == NodeStateChangeKind.Delete && (!hasMarker || initialized)))
                    {
                        await ApplyInboundAsync(change, ct).ConfigureAwait(false);
                        UpdateHydratedRootIds(roots, change.NodeId);
                    }
                }
            }
            await ApplyStoredValuesAsync(ct).ConfigureAwait(false);
            if (!authoritative || (hasMarker && !initialized))
            {
                return false;
            }
            if (initialized || hasNodes || m_checkpointEstablished || m_topologyState.Count > 0)
            {
                await RemoveMissingLocalRootsAsync(roots, ct).ConfigureAwait(false);
            }
            else
            {
                foreach (NodeState node in m_addressSpace.Nodes)
                {
                    if (m_ownsNode(node.NodeId))
                    {
                        return false;
                    }
                }
            }
            m_checkpointEstablished = true;
            return true;
        }

        private async ValueTask ApplyWatchedChangeAsync(NodeStateChange change, CancellationToken ct)
        {
            if (!m_ownsNode(change.NodeId))
            {
                return;
            }
            await m_applyGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await ApplyInboundAsync(change, ct).ConfigureAwait(false);
            }
            finally
            {
                m_applyGate.Release();
            }
            InboundApplied?.Invoke(change);
        }

        private void OnLeadershipChanged(bool isLeader)
        {
            lock (m_lock)
            {
                if (m_disposed || m_writerRequested == isLeader)
                {
                    return;
                }
                m_writerRequested = isLeader;
                RequireCheckpointLocked();
            }
        }

        private void RequireCheckpointLocked()
        {
            m_requiresCheckpoint = true;
            m_writeView = null;
            SetRole(ReplicationRole.Unavailable);
            TaskCompletionSource<bool> previous = m_roleChanged;
            m_roleChanged = NewSignal();
            previous.TrySetResult(true);
        }

        private async ValueTask ApplyInboundAsync(NodeStateChange change, CancellationToken cancellationToken)
        {
            if (!m_ownsNode(change.NodeId))
            {
                return;
            }
            m_identity?.ReserveId(change.NodeId);
            switch (change.Kind)
            {
                case NodeStateChangeKind.Upsert:
                    if (change.Node == null || change.Node.NodeId != change.NodeId)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError, "An address-space upsert is missing its matching node.");
                    }
                    if (IsTopologyApplicable(change.NodeId, change.Sequence))
                    {
                        try
                        {
                            await TryApplyUpsertAsync(
                                change.NodeId,
                                change.Node.Payload,
                                change.Sequence,
                                cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (ServiceResultException ex)
                        {
                            SetRole(ReplicationRole.Unavailable);
                            m_logger?.DistributedAddressSpaceInboundApplyFailed(ex, change.NodeId);
                            await WithdrawUnreconstructableTreeAsync(
                                change.NodeId, change.Sequence, cancellationToken).ConfigureAwait(false);
                            throw;
                        }
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
                default:
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError, "The address-space change has an invalid kind.");
            }
            ObserveStoreSequence(change.Sequence);
        }

        /// <summary>
        /// Withdraws stale live nodes when a newer topology cannot reconstruct their required policy.
        /// Independent value updates remain buffered until a valid policy tree is registered again.
        /// </summary>
        private async ValueTask WithdrawUnreconstructableTreeAsync(
            NodeId rootId,
            ulong sequence,
            CancellationToken cancellationToken)
        {
            var nodeIds = new HashSet<NodeId> { rootId };
            lock (m_lock)
            {
                nodeIds.UnionWith(GetTrackedSubtree(rootId));
            }
            if (m_addressSpace.TryGetNode(rootId, out NodeState? root))
            {
                nodeIds.UnionWith(GetDescendantNodeIds(m_addressSpace.Context, root));
            }
            using (EnterInboundApply())
            {
                foreach (NodeId nodeId in nodeIds)
                {
                    if (m_ownsNode(nodeId))
                    {
                        await m_addressSpace.RemoveNodeAsync(nodeId, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            lock (m_lock)
            {
                foreach (NodeId nodeId in nodeIds)
                {
                    if (sequence > 0)
                    {
                        RecordTopology(nodeId, sequence, exists: false);
                    }
                    else
                    {
                        m_topologyState.Remove(nodeId);
                    }
                    if (m_pendingValues.TryGetValue(nodeId, out PendingValue pending) &&
                        pending.Sequence <= sequence)
                    {
                        m_pendingValues.Remove(nodeId);
                    }
                    DetachTrackedNode(nodeId);
                }
            }
        }

        private bool IsTopologyApplicable(NodeId nodeId, ulong sequence)
        {
            return !m_topologyState.TryGetValue(nodeId, out TopologyState state) ||
                sequence > state.Sequence || (sequence == 0 && state.Sequence == 0);
        }

        private bool IsValueApplicable(NodeId nodeId, ulong sequence)
        {
            return !m_valueSequence.TryGetValue(nodeId, out ulong last) ||
                sequence > last || (sequence == 0 && last == 0);
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

        private ValueTask TryApplyUpsertAsync(
            NodeId nodeId,
            ByteString payload,
            ulong sequence,
            CancellationToken cancellationToken)
        {
            return ApplyUpsertAsync(nodeId, DecodeStoredNode(nodeId, payload), sequence, cancellationToken);
        }

        private async ValueTask ApplyUpsertAsync(
            NodeId nodeId,
            NodeState source,
            ulong sequence,
            CancellationToken cancellationToken)
        {
            NodeState node = await PrepareUpsertAsync(
                nodeId,
                source,
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

        private ValueTask MarkPartitionInitializedAsync(INodeStateStore writeStore, CancellationToken cancellationToken)
        {
            return m_partitionId != null &&
                writeStore is INodeStatePartitionStore partitionStore
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

        private bool HasAuthoritativeReads =>
            m_store is not INodeStateStoreReadConsistency consistency || consistency.HasAuthoritativeReads;

        private bool RequiresCommitFence => m_store is InMemoryNodeStateStore store &&
            !InMemoryNodeStateStore.HasProcessLocalWrites(store.Storage);

        private ReplicationRole Role => (ReplicationRole)Volatile.Read(ref m_role);

        private bool CanInitializeWriter => m_writerRequested && !m_requiresCheckpoint && !m_disposed;

        private void SetRole(ReplicationRole role) => Volatile.Write(ref m_role, (int)role);

        private static TaskCompletionSource<bool> NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private enum ReplicationRole
        {
            Unavailable,
            Standby,
            Writer
        }

        private readonly record struct QueuedOutbound(OutboundOp Operation, Task Epoch);

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
        private readonly AsyncLocal<int> m_inboundApplyDepth = new();
        private readonly SemaphoreSlim m_applyGate = new(1, 1);
        private readonly TaskCompletionSource<bool> m_hydrated = NewSignal();
        private readonly TaskCompletionSource<bool> m_initialCheckpoint = NewSignal();
        private TaskCompletionSource<bool> m_roleChanged = NewSignal();
        private FencedAddressSpaceWriteView? m_writeView;
        private Channel<QueuedOutbound>? m_outbound;
        private Task? m_outboundTask;
        private Task? m_inboundTask;
        private Task? m_snapshotTask;
        private long m_writesSinceSnapshot;
        private int m_snapshotInFlight;
        private int m_role;
        private bool m_writerRequested;
        private bool m_requiresCheckpoint;
        private bool m_checkpointEstablished;
        private bool m_started;
        private bool m_disposed;

        private TimeSpan m_inboundRetryDelay = InitialRetryDelay;
        private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(2);
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

        /// <summary>
        /// Reports an unavailable standby watch or failed recovery checkpoint.
        /// </summary>
        [LoggerMessage(EventId = RedundancyServerEventIds.AddressSpaceSynchronizer + 4, Level = LogLevel.Warning,
            Message = "Distributed address-space standby replication was interrupted.")]
        public static partial void DistributedAddressSpaceInboundWatchFailed(
            this ILogger logger,
            Exception exception);
    }
}
