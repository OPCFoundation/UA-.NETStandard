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
            var store = new InMemoryNodeStateStore(m_keyValueStore, server.MessageContext, m_protector);
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
        }

        private async ValueTask StopSynchronizersAsync()
        {
            AddressSpaceSynchronizer[] synchronizers;
            lock (m_lock)
            {
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
    }
}
