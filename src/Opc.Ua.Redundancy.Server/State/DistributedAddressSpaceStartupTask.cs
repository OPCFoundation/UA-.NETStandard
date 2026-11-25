// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: server startup task that wires the distributed address space once the
    /// server is running: it builds the <see cref="INodeStateStore"/> with the
    /// server's populated message context, registers it as the default in the
    /// server's <see cref="INodeStateStoreRegistry"/>, starts leader election,
    /// and attaches an <see cref="AddressSpaceSynchronizer"/> to every node
    /// manager that exposes non-standard owned namespaces through
    /// <see cref="ILocalAddressSpaceSource"/>. Replica-local namespace-zero
    /// infrastructure is never replicated. Built-in core, diagnostics, and
    /// configuration managers remain excluded even when they inherit the source interface.
    /// </summary>
    public sealed class DistributedAddressSpaceStartupTask : IServerStartupTask, IServerPreStartupTask, IAsyncDisposable
    {
        /// <summary>
        /// Creates the wiring task.
        /// </summary>
        /// <param name="keyValueStore">The shared key/value backend.</param>
        /// <param name="election">The leader election controlling writer role.</param>
        /// <param name="protector">
        /// Optional record protector applied to every payload written to the
        /// shared store; defaults to a no-op pass-through.
        /// </param>
        public DistributedAddressSpaceStartupTask(
            ISharedKeyValueStore keyValueStore,
            ILeaderElection election,
            IRecordProtector? protector = null)
            : this(keyValueStore, election, protector, null)
        {
        }

        /// <summary>
        /// Creates a wiring task that also validates the configured shared-store lease key.
        /// </summary>
        /// <param name="keyValueStore">The shared key/value backend.</param>
        /// <param name="election">The leader election controlling writer role.</param>
        /// <param name="protector">The record protector, or null for a no-op pass-through.</param>
        /// <param name="leaseKey">
        /// The configured shared-store lease key, when lease-based election is used.
        /// It is validated along with sequence coordination before startup mutates shared state.
        /// </param>
        public DistributedAddressSpaceStartupTask(
            ISharedKeyValueStore keyValueStore,
            ILeaderElection election,
            IRecordProtector? protector,
            string? leaseKey)
        {
            if (leaseKey != null && string.IsNullOrWhiteSpace(leaseKey))
            {
                throw new ArgumentException("A nonempty lease key is required.", nameof(leaseKey));
            }
            m_keyValueStore = keyValueStore ?? throw new ArgumentNullException(nameof(keyValueStore));
            m_election = election ?? throw new ArgumentNullException(nameof(election));
            m_protector = protector ?? NullRecordProtector.Instance;
            m_leaseKey = leaseKey;
        }

        /// <summary>
        /// The registry of <see cref="INodeStateStore"/> instances built by
        /// this task; populated once the server has started, <c>null</c>
        /// before then.
        /// </summary>
        public INodeStateStoreRegistry? NodeStateStoreRegistry { get; private set; }

        /// <inheritdoc/>
        public async ValueTask OnServerStartingAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            cancellationToken.ThrowIfCancellationRequested();
            InMemoryNodeStateStore.ValidateCoordinator(m_keyValueStore);
            if (m_leaseKey != null)
            {
                InMemoryNodeStateStore.ValidateCoordinator(m_keyValueStore, m_leaseKey);
            }
            if (m_keyValueStore is ISharedKeyValueStoreConsistency consistency &&
                !consistency.IsProcessLocal(InMemoryNodeStateStore.SequenceKey) &&
                server is not INodeIdFactoryProvider { NodeIdFactory: ReplicaNodeIdFactory })
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "Replica-set startup requires an explicit fixed namespace and NodeId identity policy.");
            }
            if (!InMemoryNodeStateStore.HasProcessLocalWrites(m_keyValueStore) &&
                (!InMemoryNodeStateStore.HasGuardedWrites(m_keyValueStore) ||
                    !AddressSpaceSynchronizer.SupportsWriterFence(m_election)))
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "Distributed address-space publication requires a store that checks the writer grant and its " +
                    "deadline in the same commit, and a " +
                    "writer election that exposes its committed grant on the same store.");
            }
            if (server is INodeIdFactoryProvider { NodeIdFactory: ReplicaNodeIdFactory identity })
            {
                identity.ValidateNamespaces(server.MessageContext.NamespaceUris);
                await ReplicaIdentityStore.VerifyAsync(
                    m_keyValueStore, m_protector, identity.Descriptor, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            await OnServerStartingAsync(server, cancellationToken).ConfigureAwait(false);
            var identity = (server as INodeIdFactoryProvider)?.NodeIdFactory as ReplicaNodeIdFactory;
            ILogger logger = server.DefaultSystemContext.Telemetry.CreateLogger<DistributedAddressSpaceStartupTask>();

            // Build the store with the server's populated message context so
            // NodeId namespace indices resolve correctly. Disposal ownership is
            // transferred to the registry below (NodeStateStoreRegistry.Dispose
            // disposes registered stores from this task's DisposeAsync).
#pragma warning disable CA2000
            var store = new InMemoryNodeStateStore(
                m_keyValueStore,
                server.MessageContext,
                m_protector,
                default,
                requireWriterGeneration: true,
                publishManagedValue: PublishManagedValueAsync);
#pragma warning restore CA2000

            // Own the node state store registry; nothing in the core server
            // surface holds it. The default store is the fallback for every
            // node that does not have a more specific binding.
            var registry = new NodeStateStoreRegistry(server.DefaultSystemContext.NamespaceUris);
            registry.RegisterDefault(store);
            m_registry = registry;
            NodeStateStoreRegistry = registry;

            try
            {
                List<AddressSpaceRegistration> registrations = AddressSpaceRegistration.Create(server, identity);

                // Settle leadership only after every ownership descriptor has
                // been validated, then initialize all synchronizers.
                await m_election.TryAcquireOrRenewAsync(cancellationToken).ConfigureAwait(false);
                m_election.Start();

                await StartSynchronizersAsync(registrations, store, identity, logger, cancellationToken)
                    .ConfigureAwait(false);
                m_identity = identity;
                identity?.SetAddressSpaceRebinder(server, async (current, token) =>
                {
                    List<AddressSpaceRegistration> next = AddressSpaceRegistration.Create(current, identity);
                    await StopSynchronizersAsync().ConfigureAwait(false);
                    await StartSynchronizersAsync(next, store, identity, logger, token).ConfigureAwait(false);
                });
                identity?.SetNodeManagerPreparer(async (current, next, previous, token) =>
                    {
                        foreach (AddressSpaceRegistration registration in
                            AddressSpaceRegistration.CreatePrepared(current, identity, next, previous))
                        {
                            var hydration = new AddressSpaceSynchronizer(
                                store,
                                registration.AddressSpace,
                                static () => false,
                                logger,
                                registration.OwnsNode,
                                registration.PartitionId,
                                identity);
                            lock (m_lock)
                            {
                                m_preparing.Add(hydration);
                            }
                            hydration.StartBeforeHydration();
                            await hydration.SeedOrHydrateAsync(token).ConfigureAwait(false);
                            await hydration.CompleteHydrationAsync(token).ConfigureAwait(false);
                        }
                    });
            }
            catch
            {
                await DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Stops every synchronizer and the leader election.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            m_identity?.ClearAddressSpaceRebinder();
            await StopSynchronizersAsync().ConfigureAwait(false);
            await m_election.DisposeAsync().ConfigureAwait(false);
            m_registry?.Dispose();
        }

        /// <summary>
        /// Checks that every local partition is ready in the elected role.
        /// </summary>
        internal bool IsRoleReady(bool isLeader)
        {
            lock (m_lock)
            {
                if (!m_replicationInitialized || m_election.IsLeader != isLeader)
                {
                    return false;
                }

                foreach (AddressSpaceSynchronizer synchronizer in m_synchronizers)
                {
                    (bool isReady, bool isWriter) = synchronizer.ReplicationStatus;
                    if (!isReady || isWriter != isLeader)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        private async ValueTask StartSynchronizersAsync(
            List<AddressSpaceRegistration> registrations,
            InMemoryNodeStateStore store,
            ReplicaNodeIdFactory? identity,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            foreach (AddressSpaceRegistration registration in registrations)
            {
                var synchronizer = new AddressSpaceSynchronizer(
                    store,
                    registration.AddressSpace,
                    m_election,
                    logger,
                    registration.OwnsNode,
                    registration.PartitionId,
                    identity);
                try
                {
                    synchronizer.StartBeforeHydration();
                    await synchronizer.SeedOrHydrateAsync(cancellationToken).ConfigureAwait(false);
                    await synchronizer.CompleteHydrationAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await synchronizer.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
                lock (m_lock)
                {
                    m_synchronizers.Add(synchronizer);
                }
            }
            lock (m_lock)
            {
                m_replicationInitialized = true;
            }
        }

        private async ValueTask PublishManagedValueAsync(NodeId nodeId, DataValue value, CancellationToken ct)
        {
            AddressSpaceSynchronizer? owner = null;
            lock (m_lock)
            {
                if (m_replicationInitialized)
                {
                    foreach (AddressSpaceSynchronizer synchronizer in m_synchronizers)
                    {
                        if (synchronizer.OwnsNode(nodeId))
                        {
                            owner = synchronizer;
                            break;
                        }
                    }
                }
            }
            if (owner == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotWritable, "No verified writer owns the distributed cache value.");
            }
            await owner.PublishManagedValueAsync(nodeId, value, ct).ConfigureAwait(false);
        }

        private async ValueTask StopSynchronizersAsync()
        {
            AddressSpaceSynchronizer[] synchronizers;
            lock (m_lock)
            {
                m_replicationInitialized = false;
                synchronizers = [.. m_synchronizers, .. m_preparing];
                m_synchronizers.Clear();
                m_preparing.Clear();
            }

            foreach (AddressSpaceSynchronizer synchronizer in synchronizers)
            {
                await synchronizer.DisposeAsync().ConfigureAwait(false);
            }
        }

        private readonly ISharedKeyValueStore m_keyValueStore;
        private readonly ILeaderElection m_election;
        private readonly IRecordProtector m_protector;
        private readonly string? m_leaseKey;
        private readonly Lock m_lock = new();
        private readonly List<AddressSpaceSynchronizer> m_synchronizers = [];
        private readonly List<AddressSpaceSynchronizer> m_preparing = [];
        private NodeStateStoreRegistry? m_registry;
        private ReplicaNodeIdFactory? m_identity;
        private bool m_replicationInitialized;
    }
}
