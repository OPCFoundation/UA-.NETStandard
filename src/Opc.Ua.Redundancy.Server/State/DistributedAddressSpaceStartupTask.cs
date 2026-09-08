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
        public async ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

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
                var registrations = new List<AddressSpaceRegistration>();
                var namespaceClaims = new Dictionary<ushort, bool>();
                var partitionIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (ILocalAddressSpaceSource source in
                    server.FindNodeManagers<ILocalAddressSpaceSource>())
                {
                    if (source is ICoreNodeManager or IDiagnosticsNodeManager)
                    {
                        continue;
                    }

                    ILocalAddressSpace addressSpace = source.CreateLocalAddressSpace();
                    IEnumerable<string>? namespaceUris = source switch
                    {
                        IAsyncNodeManager asyncNodeManager => asyncNodeManager.NamespaceUris,
                        INodeManager nodeManager => nodeManager.NamespaceUris,
                        _ => null
                    };
                    var sortedNamespaceUris = namespaceUris == null
                        ? null
                        : new List<string>(namespaceUris);
                    sortedNamespaceUris?.Sort(StringComparer.Ordinal);
                    var namespaceIndexes = new HashSet<ushort>();
                    if (sortedNamespaceUris != null)
                    {
                        foreach (string namespaceUri in sortedNamespaceUris)
                        {
                            int namespaceIndex = server.MessageContext.NamespaceUris.GetIndex(namespaceUri);
                            if (namespaceIndex > 0 && namespaceIndex <= ushort.MaxValue)
                            {
                                namespaceIndexes.Add((ushort)namespaceIndex);
                            }
                        }
                    }

                    bool hasExplicitOwnership = source is ILocalAddressSpaceOwnership;
                    foreach (ushort namespaceIndex in namespaceIndexes)
                    {
                        if (namespaceClaims.TryGetValue(namespaceIndex, out bool existingExplicit) &&
                            (!hasExplicitOwnership || !existingExplicit))
                        {
                            throw new InvalidOperationException(
                                $"Namespace index {namespaceIndex} is claimed by multiple distributed node managers. " +
                                $"Every manager sharing a namespace must implement " +
                                $"{nameof(ILocalAddressSpaceOwnership)}.");
                        }
                        namespaceClaims[namespaceIndex] = hasExplicitOwnership;
                    }

                    Func<NodeId, bool> ownsNode;
                    string partitionId;
                    if (source is ILocalAddressSpaceOwnership ownership)
                    {
                        if (string.IsNullOrWhiteSpace(ownership.PartitionId))
                        {
                            throw new InvalidOperationException(
                                $"{source.GetType().FullName} returned an empty distributed address-space " +
                                "partition id.");
                        }
                        ValidateExplicitOwnership(addressSpace, ownership);
                        partitionId = ownership.PartitionId;
                        ownsNode = nodeId =>
                            nodeId.NamespaceIndex != 0 &&
                            ownership.OwnsNode(nodeId);
                    }
                    else
                    {
                        if (sortedNamespaceUris == null)
                        {
                            throw new InvalidOperationException(
                                $"{source.GetType().FullName} must expose owned NamespaceUris or implement " +
                                $"{nameof(ILocalAddressSpaceOwnership)}.");
                        }

                        if (namespaceIndexes.Count == 0)
                        {
                            continue;
                        }
                        partitionId = string.Join("|", sortedNamespaceUris);
                        ownsNode = nodeId => namespaceIndexes.Contains(nodeId.NamespaceIndex);
                    }
                    if (!partitionIds.Add(partitionId))
                    {
                        throw new InvalidOperationException(
                            $"Distributed address-space partition '{partitionId}' is registered more than once.");
                    }
                    registrations.Add(new AddressSpaceRegistration(
                        addressSpace,
                        ownsNode,
                        partitionId));
                }

                // Settle leadership only after every ownership descriptor has
                // been validated, then initialize all synchronizers.
                await m_election.TryAcquireOrRenewAsync(cancellationToken).ConfigureAwait(false);
                m_election.Start();

                foreach (AddressSpaceRegistration registration in registrations)
                {
                    var synchronizer = new AddressSpaceSynchronizer(
                        store,
                        registration.AddressSpace,
                        m_election,
                        logger,
                        registration.OwnsNode,
                        registration.PartitionId);
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
            catch
            {
                await DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private static void ValidateExplicitOwnership(
            ILocalAddressSpace addressSpace,
            ILocalAddressSpaceOwnership ownership)
        {
            foreach (NodeState node in addressSpace.Nodes)
            {
                ValidateExplicitOwnership(addressSpace.Context, node, ownership);
            }
        }

        private static void ValidateExplicitOwnership(
            ISystemContext context,
            NodeState node,
            ILocalAddressSpaceOwnership ownership)
        {
            if (node.NodeId.NamespaceIndex == 0 &&
                ownership.OwnsNode(node.NodeId))
            {
                throw new InvalidOperationException(
                    $"Distributed address-space partition '{ownership.PartitionId}' claims namespace-zero node " +
                    $"{node.NodeId}.");
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                ValidateExplicitOwnership(context, child, ownership);
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

        private sealed record AddressSpaceRegistration(
            ILocalAddressSpace AddressSpace,
            Func<NodeId, bool> OwnsNode,
            string PartitionId);

        private readonly ISharedKeyValueStore m_keyValueStore;
        private readonly ILeaderElection m_election;
        private readonly IRecordProtector m_protector;
        private readonly Lock m_lock = new();
        private readonly List<AddressSpaceSynchronizer> m_synchronizers = [];
        private NodeStateStoreRegistry? m_registry;
    }
}
