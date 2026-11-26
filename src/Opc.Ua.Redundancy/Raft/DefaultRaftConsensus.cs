// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
    /// <see cref="IRaftSnapshotConsensus"/> seam. Proposals map to <see cref="RaftNode.ProposeAsync"/>, the committed
    /// apply stream is <see cref="RaftNode.Committed"/> (with log indices and snapshots,
    /// <see cref="RaftNode.CommittedItems"/>), compaction records a snapshot in the node's storage and discards the log
    /// behind it, and leadership is observed from <see cref="RaftNode.IsLeader"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="CompactAsync"/> needs storage that keeps snapshots (<see cref="IRaftSnapshotStorage"/>): the
    /// in-memory storage of the factory methods and <c>RaftCs.Storage.File</c> do.
    /// The factory methods validate the key/value store snapshot format before Raft persists it. An adapter over
    /// an externally constructed node cannot change that node's snapshot validation.
    /// </remarks>
    public sealed class DefaultRaftConsensus : IRaftSnapshotConsensus, IRaftConsensusLocality
    {
        /// <summary>
        /// Creates an adapter over a <c>RaftCs</c> replica whose transport locality is unknown.
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
            : this(node, ownsNode, leadershipPollInterval, ownedHost, readyTimeout, isProcessLocal: true)
        {
        }

        /// <summary>
        /// Creates an adapter over a <c>RaftCs</c> replica with a known transport locality.
        /// </summary>
        /// <param name="node">The Raft replica (constructed with its storage and transport).</param>
        /// <param name="ownsNode">When <c>true</c>, the node is disposed with this adapter.</param>
        /// <param name="leadershipPollInterval">How often the adapter samples leadership changes.</param>
        /// <param name="ownedHost">An optional resource disposed after the node.</param>
        /// <param name="readyTimeout">How long startup waits for the cluster to elect an initial leader.</param>
        /// <param name="isProcessLocal">
        /// Pass <c>false</c> only for a transport configured to share the same Raft cluster across processes.
        /// A single-voter replica is reported as process-local regardless.
        /// </param>
        public DefaultRaftConsensus(
            RaftNode node,
            bool ownsNode,
            TimeSpan leadershipPollInterval,
            IAsyncDisposable? ownedHost,
            TimeSpan readyTimeout,
            bool isProcessLocal)
        {
            m_node = node ?? throw new ArgumentNullException(nameof(node));
            IsProcessLocal = isProcessLocal || node.Configuration.Voters.Count <= 1;
            m_committedItems = new CommittedItemReader(node.CommittedItems);
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
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="nodeId"/></exception>
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
        /// <exception cref="ArgumentException"></exception>
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
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="nodeId"/></exception>
        /// <exception cref="ArgumentNullException"><paramref name="transport"/> is <c>null</c>.</exception>
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

            options ??= new RaftNodeOptions();
            Func<Snapshot, bool>? validateSnapshot = options.SnapshotValidator;
            var storeOptions = new RaftNodeOptions
            {
                TickInterval = options.TickInterval,
                MaxApplyBytes = options.MaxApplyBytes,
                MaxIncomingMessageBytes = options.MaxIncomingMessageBytes,
                StateObservationCapacity = options.StateObservationCapacity,
                SnapshotValidator = snapshot =>
                    RaftSharedKeyValueStore.IsValidEngineSnapshot(snapshot.Data.Span, snapshot.Metadata.Index) &&
                    (validateSnapshot?.Invoke(snapshot) ?? true)
            };

            // CA2000: the node (which disposes the transport) and ownedResources
            // transfer to the returned adapter, which disposes them.
#pragma warning disable CA2000
            var node = new RaftNode(config, storage, transport, storeOptions);
            return new DefaultRaftConsensus(
                node,
                ownsNode: true,
                leadershipPollInterval: default,
                ownedHost: ownedResources,
                readyTimeout: readyTimeout,
                isProcessLocal: transport is InMemoryTransport)
            {
                UsesOnlyInProcessMemory = transport is InMemoryTransport && storage is MemoryStorage
            };
#pragma warning restore CA2000
        }

        private static ulong[] ToArray(ArrayOf<ulong> ids)
        {
            ulong[] result = new ulong[ids.Count];
            for (int ii = 0; ii < ids.Count; ii++)
            {
                result[ii] = ids[ii];
            }
            return result;
        }

        /// <inheritdoc/>
        public bool IsLeader => IsAvailable && m_node.IsLeader;

        /// <inheritdoc/>
        public bool IsProcessLocal { get; }

        /// <summary>
        /// Gets whether both the transport and backing storage are known to be private process memory.
        /// </summary>
        internal bool UsesOnlyInProcessMemory { get; private init; }

        /// <inheritdoc/>
        public event Action<bool>? LeadershipChanged;

        /// <inheritdoc/>
        public ChannelReader<ReadOnlyMemory<byte>> Committed => m_node.Committed;

        /// <inheritdoc/>
        public ChannelReader<RaftCommittedItem> CommittedItems => m_committedItems;

        /// <summary>
        /// Gets whether the consensus adapter has not failed or stopped.
        /// </summary>
        internal bool IsAvailable => Volatile.Read(ref m_disposed) == 0 &&
            Volatile.Read(ref m_stateMachineFault) == null &&
            Volatile.Read(ref m_startTask) is not ({ IsFaulted: true } or { IsCanceled: true }) &&
            !m_committedItems.Completion.IsCompleted;

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">The node's storage does not keep snapshots.</exception>
        public async ValueTask CompactAsync(
            ulong appliedIndex,
            ByteString snapshot,
            CancellationToken ct = default)
        {
            await m_node.CreateSnapshotAsync(appliedIndex, snapshot.Memory, ct).ConfigureAwait(false);
            await m_node.CompactAsync(appliedIndex, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask StartAsync(CancellationToken ct = default)
        {
            lock (m_startLock)
            {
                ThrowIfUnavailable();
                ct.ThrowIfCancellationRequested();
                m_startTask ??= Task.Run(StartCoreAsync, CancellationToken.None);
                return new ValueTask(m_startTask.WaitAsync(ct));
            }
        }

        /// <inheritdoc/>
        public ValueTask ProposeAsync(ReadOnlyMemory<byte> command, CancellationToken ct = default)
        {
            ThrowIfUnavailable();
            if (command.Length > RaftSharedKeyValueStore.MaxCommandBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(command), "The Raft command exceeds its size limit.");
            }
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
            ThrowIfUnavailable();
            return m_node.CampaignAsync(ct);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Task? startup;
            lock (m_startLock)
            {
                if (m_disposed != 0)
                {
                    return;
                }
                Volatile.Write(ref m_disposed, 1);
                startup = m_startTask;
            }
            try
            {
                m_cts.Cancel();
                if (startup != null)
                {
                    await ObserveStartupAsync(startup).ConfigureAwait(false);
                }
                if (m_leadershipLoop != null)
                {
                    await m_leadershipLoop.ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    if (m_ownsNode)
                    {
                        await m_node.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        if (m_ownedHost != null)
                        {
                            await m_ownedHost.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        m_cts.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Prevents this adapter from advertising usable leadership after its local state machine has failed.
        /// </summary>
        internal void ReportStateMachineFault(Exception error)
        {
            Interlocked.CompareExchange(ref m_stateMachineFault, error, null);
        }

        private void ThrowIfUnavailable()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(DefaultRaftConsensus));
            }
            if (Volatile.Read(ref m_stateMachineFault) is Exception error)
            {
                throw new InvalidOperationException("The Raft key/value state machine has failed.", error);
            }
        }

        private async Task StartCoreAsync()
        {
            CancellationToken ct = m_cts.Token;
            await m_node.StartAsync(ct).ConfigureAwait(false);
            await WaitForInitialLeaderAsync(ct).ConfigureAwait(false);
            m_leadershipLoop = Task.Run(() => WatchLeadershipAsync(ct), CancellationToken.None);
        }

        private static async Task ObserveStartupAsync(Task startup)
        {
            try
            {
                await startup.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // StartAsync owns the outcome; disposal still releases all owned resources.
            }
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
                    bool now = IsLeader;
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

        /// <summary>
        /// The <see cref="RaftCommittedItem"/> view of <see cref="RaftNode.CommittedItems"/>.
        /// </summary>
        private sealed class CommittedItemReader : ChannelReader<RaftCommittedItem>
        {
            public CommittedItemReader(ChannelReader<CommittedItem> items)
            {
                m_items = items;
            }

            public override Task Completion => m_items.Completion;

            public override bool TryRead(out RaftCommittedItem item)
            {
                if (m_items.TryRead(out CommittedItem? next))
                {
                    item = new RaftCommittedItem(next.Kind == CommittedItemKind.Snapshot, next.Index, new ByteString(next.Data));
                    return true;
                }
                item = null!;
                return false;
            }

            public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
            {
                return m_items.WaitToReadAsync(cancellationToken);
            }

            private readonly ChannelReader<CommittedItem> m_items;
        }

        private readonly RaftNode m_node;
        private readonly CommittedItemReader m_committedItems;
        private readonly bool m_ownsNode;
        private readonly IAsyncDisposable? m_ownedHost;
        private readonly TimeSpan m_pollInterval;
        private readonly TimeSpan m_readyTimeout;
        private readonly Lock m_startLock = new();
        private readonly CancellationTokenSource m_cts = new();
        private Task? m_startTask;
        private Task? m_leadershipLoop;
        private Exception? m_stateMachineFault;
        private int m_disposed;
    }
}
