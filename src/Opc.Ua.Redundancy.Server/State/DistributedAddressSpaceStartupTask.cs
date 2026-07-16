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
    /// manager that opts in via <see cref="ILocalAddressSpaceSource"/> (i.e.
    /// every <c>CustomNodeManager2</c>-derived manager). Built-in
    /// infrastructure managers (Core / Diagnostics / Configuration) do not opt
    /// in and are never replicated.
    /// </summary>
    public sealed class DistributedAddressSpaceStartupTask : IServerStartupTask, IAsyncDisposable
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
        {
            m_keyValueStore = keyValueStore ?? throw new ArgumentNullException(nameof(keyValueStore));
            m_election = election ?? throw new ArgumentNullException(nameof(election));
            m_protector = protector ?? NullRecordProtector.Instance;
        }

        /// <summary>
        /// The registry of <see cref="INodeStateStore"/> instances built by
        /// this task; populated once the server has started, <c>null</c>
        /// before then.
        /// </summary>
        public INodeStateStoreRegistry? NodeStateStoreRegistry { get; private set; }

        /// <inheritdoc/>
        public async ValueTask OnServerStartedAsync(IServerInternal server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            ILogger logger = server.Telemetry.CreateLogger<DistributedAddressSpaceStartupTask>();

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
            var registry = new NodeStateStoreRegistry(server.NamespaceUris);
            registry.RegisterDefault(store);
            m_registry = registry;
            NodeStateStoreRegistry = registry;

            // Settle leadership before seeding so the writer seeds the store and
            // standbys hydrate from it, then keep renewing in the background.
            await m_election.TryAcquireOrRenewAsync(cancellationToken).ConfigureAwait(false);
            m_election.Start();

            foreach (INodeManager nodeManager in server.NodeManager.NodeManagers)
            {
                if (nodeManager is not ILocalAddressSpaceSource source)
                {
                    continue;
                }

                ILocalAddressSpace addressSpace = source.CreateLocalAddressSpace();
                var synchronizer = new AddressSpaceSynchronizer(
                    store, addressSpace, m_election, logger);
                await synchronizer.SeedOrHydrateAsync(cancellationToken).ConfigureAwait(false);
                synchronizer.Start();
                lock (m_lock)
                {
                    m_synchronizers.Add(synchronizer);
                }
            }
        }

        /// <summary>
        /// Stops every synchronizer and the leader election.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            AddressSpaceSynchronizer[] synchronizers;
            lock (m_lock)
            {
                synchronizers = [.. m_synchronizers];
                m_synchronizers.Clear();
            }

            foreach (AddressSpaceSynchronizer synchronizer in synchronizers)
            {
                await synchronizer.DisposeAsync().ConfigureAwait(false);
            }
            await m_election.DisposeAsync().ConfigureAwait(false);
            m_registry?.Dispose();
        }

        private readonly ISharedKeyValueStore m_keyValueStore;
        private readonly ILeaderElection m_election;
        private readonly IRecordProtector m_protector;
        private readonly Lock m_lock = new();
        private readonly List<AddressSpaceSynchronizer> m_synchronizers = [];
        private NodeStateStoreRegistry? m_registry;
    }
}
