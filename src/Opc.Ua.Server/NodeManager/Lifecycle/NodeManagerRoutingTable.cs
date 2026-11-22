// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Opc.Ua.Server.NodeManagerGenerationRegistry;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Routes Nodes to the NodeManagers that own them and lets NodeManagers be added, replaced,
    /// and removed while the server is running.
    /// <para>
    /// Routing reads do not take the writer lock. Every mutation builds a complete new
    /// <c>RoutingSnapshot</c> under the lock and publishes it with a single volatile write, so a
    /// service call in flight observes either the state before or the state after a lifecycle operation.
    /// Resolving an unregistered source takes the writer lock to coordinate its legacy generation with publication.
    /// </para>
    /// <para>
    /// A NodeManager can be registered but hidden. Hidden NodeManagers are resolvable by the
    /// lifecycle operation that is preparing them, yet excluded from enumeration and from the
    /// namespace routes Clients see, which is how a NodeManager is staged before it is committed.
    /// </para>
    /// </summary>
    internal sealed class NodeManagerRoutingTable : IReadOnlyList<IAsyncNodeManager>
    {
        /// <summary>
        /// Gets the number of visible NodeManagers.
        /// </summary>
        public int Count => ReadSnapshot.VisibleNodeManagers.Length;

        /// <summary>
        /// Gets the visible NodeManager at the given position.
        /// </summary>
        /// <param name="index">The position of the NodeManager.</param>
        public IAsyncNodeManager this[int index]
            => ReadSnapshot.VisibleNodeManagers[index];

        /// <summary>
        /// Gets the NodeManagers that serve each namespace index, excluding hidden ones.
        /// </summary>
        public IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> NamespaceManagers
            => ReadSnapshot.VisibleNamespaceManagers;

        /// <summary>
        /// Resolves a manager to the generation carried by the current routing view.
        /// </summary>
        /// <param name="nodeManager">The manager present in the current view.</param>
        /// <returns>The exact generation identity.</returns>
        internal NodeManagerGeneration GetGeneration(IAsyncNodeManager nodeManager)
            => ReadSnapshot.GetGeneration(nodeManager);

        /// <summary>
        /// Resolves an unregistered source against publication before creating its legacy generation.
        /// </summary>
        /// <exception cref="InvalidOperationException">The source has no unique registered owner.</exception>
        internal NodeManagerGeneration ResolveUnregisteredSource(
            IAsyncNodeManager nodeManager,
            RoutingSnapshot? requestSnapshot)
        {
            lock (m_lock)
            {
                RoutingSnapshot routing = requestSnapshot ?? m_snapshot;
                if (routing.TryGetSourceGeneration(nodeManager, out NodeManagerGeneration? generation) ||
                    routing.TryGetSourceGeneration(nodeManager, out generation, includeHidden: true))
                {
                    return generation!;
                }
                if (routing.ContainsSourceResource(nodeManager))
                {
                    throw new InvalidOperationException(
                        "The monitored source does not have a unique registered owner.");
                }
                return GetOrCreateSource(nodeManager);
            }
        }

        /// <summary>
        /// Gets the visible NodeManagers in dispatch order and the namespace routes from one
        /// snapshot, so a service call cannot see the routes of one lifecycle generation and
        /// the dispatch order of another.
        /// </summary>
        /// <param name="snapshot">An explicit operation view, or null for live routes.</param>
        public (IAsyncNodeManager[] NodeManagers,
            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> NamespaceManagers) GetDispatchView(
                RoutingSnapshot? snapshot = null)
        {
            snapshot ??= ReadSnapshot;
            return (snapshot.VisibleNodeManagers, snapshot.VisibleNamespaceManagers);
        }

        /// <summary>
        /// Closes the prior view, transitions its removed generations, and publishes the next immutable view.
        /// </summary>
        /// <param name="next">The view to publish.</param>
        private bool PublishSnapshot(RoutingSnapshot next)
        {
            RoutingSnapshot previous = m_snapshot;
            bool historianRegistrationsRetired = false;
            foreach (NodeManagerGeneration generation in previous.Generations)
            {
                if (!next.Generations.Contains(generation))
                {
                    historianRegistrationsRetired |=
                        ChangePreparer.RetireHistorianRegistrations(generation.NodeManager);
                    generation.Gate.Retire();
                    generation.Slot.Unpublish(generation);
                }
            }
            foreach (NodeManagerGeneration generation in next.Generations)
            {
                bool wasHidden = ContainsManager(previous.HiddenNodeManagers, generation.NodeManager);
                bool isHidden = ContainsManager(next.HiddenNodeManagers, generation.NodeManager);
                if (generation.Gate.State == NodeManagerGenerationState.Prepared &&
                    !isHidden &&
                    (!previous.Generations.Contains(generation) || wasHidden))
                {
                    generation.Gate.Activate();
                }
                if (generation.Gate.State == NodeManagerGenerationState.Live)
                {
                    generation.Slot.Publish(generation);
                }
            }
            next.SetRevision(Interlocked.Increment(ref m_revision));
            Volatile.Write(ref m_snapshot, next);
            previous.Epoch.Close();
            return historianRegistrationsRetired;
        }

        /// <summary>
        /// Validates generation transitions while the routing image is still private.
        /// </summary>
        /// <param name="previous">The serving image.</param>
        /// <param name="next">The prepared image.</param>
        private static void ValidatePublicationTransitions(
            RoutingSnapshot previous,
            RoutingSnapshot next)
        {
            foreach (NodeManagerGeneration generation in previous.Generations)
            {
                if (!next.Generations.Contains(generation) &&
                    generation.Gate.State == NodeManagerGenerationState.Preparing)
                {
                    throw new InvalidOperationException(
                        "A generation in the serving image cannot still be preparing.");
                }
            }

            foreach (NodeManagerGeneration generation in next.Generations)
            {
                NodeManagerGenerationState state = generation.Gate.State;
                bool wasHidden = ContainsManager(previous.HiddenNodeManagers, generation.NodeManager);
                bool isHidden = ContainsManager(next.HiddenNodeManagers, generation.NodeManager);
                bool willActivate = state == NodeManagerGenerationState.Prepared &&
                    !isHidden &&
                    (!previous.Generations.Contains(generation) || wasHidden);
                if (state != NodeManagerGenerationState.Live &&
                    !(state == NodeManagerGenerationState.Prepared && (willActivate || isHidden)))
                {
                    throw new InvalidOperationException(
                        "The prepared routing image contains a generation that cannot be published.");
                }
            }
        }

        /// <summary>
        /// Compares manager identities without invoking manager-defined equality during publication.
        /// </summary>
        private static bool ContainsManager(IAsyncNodeManager[] managers, IAsyncNodeManager candidate)
        {
            foreach (IAsyncNodeManager manager in managers)
            {
                if (ReferenceEquals(manager, candidate))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Adds a NodeManager during server startup, before the namespace routes are built.
        /// </summary>
        /// <param name="nodeManager">The NodeManager to add.</param>
        /// <exception cref="ArgumentNullException"><paramref name="nodeManager"/> is <c>null</c>.</exception>
        public void AddInitial(IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            lock (m_lock)
            {
                EnsureMutable();
                RoutingSnapshot snapshot = m_snapshot;
                _ = NodeManagerGenerationRegistry.GetOrCreate(nodeManager);
                PublishSnapshot(new RoutingSnapshot(
                    [.. snapshot.NodeManagers, nodeManager],
                    snapshot.NamespaceManagers,
                    snapshot.HiddenNodeManagers,
                    snapshot.TypeTree,
                    snapshot.Factory,
                    snapshot.ReadImages));
            }
        }

        /// <summary>
        /// Publishes the namespace routes that were built during server startup.
        /// </summary>
        /// <param name="namespaceManagers">The NodeManagers that serve each namespace index.</param>
        /// <param name="typeTree">The initial type image owned by the routing snapshot.</param>
        /// <param name="factory">The initial factory image owned by the routing snapshot.</param>
        /// <exception cref="ArgumentNullException"><paramref name="namespaceManagers"/> is <c>null</c>.</exception>
        public void Initialize(
            IReadOnlyDictionary<int, List<IAsyncNodeManager>> namespaceManagers,
            TypeTable? typeTree = null,
            EncodeableFactory? factory = null)
        {
            if (namespaceManagers is null)
            {
                throw new ArgumentNullException(nameof(namespaceManagers));
            }

            lock (m_lock)
            {
                EnsureMutable();
                PublishSnapshot(new RoutingSnapshot(
                    m_snapshot.NodeManagers,
                    namespaceManagers.ToDictionary(
                        entry => entry.Key,
                        entry => (IReadOnlyList<IAsyncNodeManager>)[.. entry.Value]),
                    m_snapshot.HiddenNodeManagers,
                    typeTree ?? m_snapshot.TypeTree,
                    factory ?? m_snapshot.Factory,
                    m_snapshot.ReadImages));
            }
        }

        /// <summary>
        /// Registers a NodeManager at runtime and routes the given namespace indexes to it.
        /// </summary>
        /// <param name="nodeManager">The NodeManager to register.</param>
        /// <param name="namespaceIndexes">The namespace indexes the NodeManager serves.</param>
        /// <param name="visible">
        /// <c>false</c> to keep the NodeManager hidden from Clients until it is committed.
        /// </param>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The NodeManager is already registered.</exception>
        public void Add(
            IAsyncNodeManager nodeManager,
            IEnumerable<int> namespaceIndexes,
            bool visible = true)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }
            if (namespaceIndexes is null)
            {
                throw new ArgumentNullException(nameof(namespaceIndexes));
            }

            lock (m_lock)
            {
                EnsureMutable();
                RoutingSnapshot snapshot = m_snapshot;
                if (IndexOf(snapshot.NodeManagers, nodeManager) >= 0)
                {
                    throw new InvalidOperationException("The NodeManager is already registered.");
                }
                _ = NodeManagerGenerationRegistry.CreateAddition(nodeManager);

                Dictionary<int, IReadOnlyList<IAsyncNodeManager>> routes =
                    CopyRoutes(snapshot.NamespaceManagers);
                foreach (int namespaceIndex in namespaceIndexes.Distinct())
                {
                    routes.TryGetValue(
                        namespaceIndex,
                        out IReadOnlyList<IAsyncNodeManager>? existing);
                    if (existing is null)
                    {
                        routes[namespaceIndex] = [nodeManager];
                    }
                    else if (!existing.Any(manager =>
                        HasSameGenerationIdentity(manager, nodeManager)))
                    {
                        routes[namespaceIndex] = [.. existing, nodeManager];
                    }
                }

                IAsyncNodeManager[] hiddenNodeManagers =
                [
                    .. snapshot.HiddenNodeManagers.Where(manager =>
                        !HasSameGenerationIdentity(manager, nodeManager))
                ];
                if (!visible)
                {
                    hiddenNodeManagers =
                    [
                        .. hiddenNodeManagers,
                        nodeManager
                    ];
                }

                PublishSnapshot(new RoutingSnapshot(
                    [.. snapshot.NodeManagers, nodeManager],
                    routes,
                    hiddenNodeManagers,
                    snapshot.TypeTree,
                    snapshot.Factory,
                    snapshot.ReadImages));
            }
        }

        /// <summary>
        /// Swaps a registered NodeManager for its replacement in place, so the replacement keeps
        /// the routing position, and therefore the dispatch order, of the NodeManager it replaces.
        /// </summary>
        /// <param name="current">The NodeManager to replace.</param>
        /// <param name="replacement">The replacement NodeManager.</param>
        /// <param name="replacementNamespaceIndexes">
        /// The namespace indexes the replacement serves.
        /// </param>
        /// <param name="replacementVisible">
        /// <c>false</c> to keep the replacement hidden from Clients until it is committed.
        /// </param>
        /// <returns>The routing position occupied by the current NodeManager.</returns>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="current"/> is not registered.
        /// </exception>
        public NodeManagerRoutingPosition Replace(
            IAsyncNodeManager current,
            IAsyncNodeManager replacement,
            IEnumerable<int> replacementNamespaceIndexes,
            bool replacementVisible = true)
        {
            if (current is null)
            {
                throw new ArgumentNullException(nameof(current));
            }
            if (replacement is null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }
            if (replacementNamespaceIndexes is null)
            {
                throw new ArgumentNullException(nameof(replacementNamespaceIndexes));
            }

            lock (m_lock)
            {
                EnsureMutable();
                RoutingSnapshot snapshot = m_snapshot;
                int managerIndex = IndexOf(snapshot.NodeManagers, current);
                if (managerIndex < 0)
                {
                    throw new InvalidOperationException(
                        "The NodeManager being replaced is not registered.");
                }
                if (IndexOf(snapshot.NodeManagers, replacement) >= 0)
                {
                    throw new InvalidOperationException(
                        "The replacement NodeManager is already registered.");
                }
                NodeManagerGenerationRegistry.CreateReplacement(
                    snapshot.GetGeneration(current),
                    replacement);

                NodeManagerRoutingPosition currentPosition = CreatePosition(
                    snapshot,
                    managerIndex,
                    snapshot.NodeManagers[managerIndex]);
                int[] replacementNamespaces = [.. replacementNamespaceIndexes.Distinct()];
                var replacementNamespaceSet = new HashSet<int>(replacementNamespaces);
                IAsyncNodeManager[] managers = [.. snapshot.NodeManagers];
                managers[managerIndex] = replacement;

                Dictionary<int, IReadOnlyList<IAsyncNodeManager>> routes =
                    CopyRoutes(snapshot.NamespaceManagers);
                foreach (KeyValuePair<int, IReadOnlyList<IAsyncNodeManager>> route in routes)
                {
                    if (route.Value.Any(manager =>
                        HasSameGenerationIdentity(manager, replacement)))
                    {
                        replacementNamespaceSet.Add(route.Key);
                    }
                }
                foreach (int namespaceIndex in routes.Keys.ToArray())
                {
                    IReadOnlyList<IAsyncNodeManager> existing = routes[namespaceIndex];
                    var updated = existing
                        .Where(manager => !HasSameGenerationIdentity(
                            manager,
                            replacement))
                        .ToList();
                    int routeIndex = IndexOf(updated, current);
                    if (routeIndex < 0)
                    {
                        if (updated.Count == 0)
                        {
                            routes.Remove(namespaceIndex);
                        }
                        else if (updated.Count != existing.Count)
                        {
                            routes[namespaceIndex] = [.. updated];
                        }
                        continue;
                    }

                    if (replacementNamespaceSet.Remove(namespaceIndex))
                    {
                        updated[routeIndex] = replacement;
                    }
                    else
                    {
                        updated.RemoveAt(routeIndex);
                    }

                    if (updated.Count == 0)
                    {
                        routes.Remove(namespaceIndex);
                    }
                    else
                    {
                        routes[namespaceIndex] = [.. updated];
                    }
                }

                foreach (int namespaceIndex in replacementNamespaceSet)
                {
                    routes.TryGetValue(
                        namespaceIndex,
                        out IReadOnlyList<IAsyncNodeManager>? existing);
                    routes[namespaceIndex] = existing is null
                        ? [replacement]
                        : [.. existing, replacement];
                }

                IAsyncNodeManager[] hiddenNodeManagers =
                [
                    .. snapshot.HiddenNodeManagers.Where(manager =>
                        !HasSameGenerationIdentity(manager, current) &&
                        !HasSameGenerationIdentity(manager, replacement))
                ];
                if (!replacementVisible)
                {
                    hiddenNodeManagers =
                    [
                        .. hiddenNodeManagers,
                        replacement
                    ];
                }

                PublishSnapshot(new RoutingSnapshot(
                    managers,
                    routes,
                    hiddenNodeManagers,
                    snapshot.TypeTree,
                    snapshot.Factory,
                    snapshot.ReadImages));
                return currentPosition;
            }
        }

        /// <summary>
        /// Atomically removes a replacement and restores the previous NodeManager
        /// to its captured global and namespace-route positions.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The replacement is absent, the previous manager is already registered,
        /// or the exact retiring predecessor is unavailable, has not drained or can no longer be restored.
        /// </exception>
        public void RestoreReplacement(
            IAsyncNodeManager replacement,
            IAsyncNodeManager previous,
            NodeManagerRoutingPosition previousPosition,
            bool visible)
        {
            if (replacement is null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }
            if (previous is null)
            {
                throw new ArgumentNullException(nameof(previous));
            }
            if (previousPosition is null)
            {
                throw new ArgumentNullException(nameof(previousPosition));
            }

            lock (m_lock)
            {
                EnsureMutable();
                RoutingSnapshot snapshot = m_snapshot;
                int replacementIndex = IndexOf(
                    snapshot.NodeManagers,
                    replacement);
                if (replacementIndex < 0)
                {
                    throw new InvalidOperationException(
                        "The NodeManager being restored is not registered.");
                }
                if (IndexOf(snapshot.NodeManagers, previous) >= 0)
                {
                    throw new InvalidOperationException(
                        "The previous NodeManager is already registered.");
                }
                NodeManagerGeneration replacing = snapshot.GetGeneration(replacement);
                if (!TryGet(previous, out NodeManagerGeneration? restoring) ||
                    !ReferenceEquals(restoring!.Slot, replacing.Slot) ||
                    restoring.Number + 1 != replacing.Number ||
                    restoring.Gate.State != NodeManagerGenerationState.Retiring ||
                    !restoring.TryRestoreBeforeShutdown())
                {
                    throw new InvalidOperationException(
                        "The exact previous generation cannot be restored after cleanup or before its work drains.");
                }

                var managers = new List<IAsyncNodeManager>(
                    snapshot.NodeManagers);
                managers.RemoveAt(replacementIndex);
                managers.Insert(
                    ResolveInsertionIndex(
                        managers,
                        previousPosition.ManagerPosition),
                    previous);

                Dictionary<int, IReadOnlyList<IAsyncNodeManager>> routes =
                    CopyRoutes(snapshot.NamespaceManagers);
                foreach (int namespaceIndex in routes.Keys.ToArray())
                {
                    var updated = routes[namespaceIndex]
                        .Where(manager =>
                            !HasSameGenerationIdentity(manager, replacement))
                        .ToList();
                    if (updated.Count == 0)
                    {
                        routes.Remove(namespaceIndex);
                    }
                    else
                    {
                        routes[namespaceIndex] = [.. updated];
                    }
                }
                foreach (KeyValuePair<int, NodeManagerInsertionPoint> routePosition
                    in previousPosition.NamespaceRoutePositions)
                {
                    routes.TryGetValue(
                        routePosition.Key,
                        out IReadOnlyList<IAsyncNodeManager>? existing);
                    var updated = existing?.Where(manager =>
                        !HasSameGenerationIdentity(manager, previous)).ToList() ??
                        [];
                    updated.Insert(
                        ResolveInsertionIndex(
                            updated,
                            routePosition.Value),
                        previous);
                    routes[routePosition.Key] = [.. updated];
                }

                IAsyncNodeManager[] hiddenNodeManagers =
                [
                    .. snapshot.HiddenNodeManagers.Where(manager =>
                        !HasSameGenerationIdentity(manager, replacement) &&
                        !HasSameGenerationIdentity(manager, previous))
                ];
                if (!visible)
                {
                    hiddenNodeManagers =
                    [
                        .. hiddenNodeManagers,
                        previous
                    ];
                }

                PublishSnapshot(new RoutingSnapshot(
                    [.. managers],
                    routes,
                    hiddenNodeManagers,
                    snapshot.TypeTree,
                    snapshot.Factory,
                    snapshot.ReadImages));
            }
        }

        /// <summary>
        /// Removes a NodeManager and every namespace route that points at it.
        /// </summary>
        /// <param name="nodeManager">The NodeManager to remove.</param>
        public void Remove(IAsyncNodeManager nodeManager)
        {
            _ = RemoveAndCapturePosition(nodeManager);
        }

        /// <summary>
        /// Atomically captures and removes a lifecycle-managed NodeManager.
        /// </summary>
        public NodeManagerRoutingPosition RemoveAndCapturePosition(
            IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            lock (m_lock)
            {
                EnsureMutable();
                RoutingSnapshot snapshot = m_snapshot;
                int managerIndex = IndexOf(snapshot.NodeManagers, nodeManager);
                if (managerIndex < 0)
                {
                    throw new InvalidOperationException(
                        "The NodeManager being removed is not registered.");
                }

                List<IAsyncNodeManager> managers = [.. snapshot.NodeManagers];
                IAsyncNodeManager registeredManager = managers[managerIndex];
                NodeManagerRoutingPosition position = CreatePosition(
                    snapshot,
                    managerIndex,
                    registeredManager);
                managers.RemoveAt(managerIndex);
                Dictionary<int, IReadOnlyList<IAsyncNodeManager>> routes =
                    CopyRoutes(snapshot.NamespaceManagers);

                foreach (int namespaceIndex in routes.Keys.ToArray())
                {
                    var updated = routes[namespaceIndex].ToList();
                    updated.RemoveAll(manager =>
                        HasSameGenerationIdentity(manager, registeredManager));
                    if (updated.Count == 0)
                    {
                        routes.Remove(namespaceIndex);
                    }
                    else
                    {
                        routes[namespaceIndex] = [.. updated];
                    }
                }

                PublishSnapshot(new RoutingSnapshot(
                    [.. managers],
                    routes,
                    [
                        .. snapshot.HiddenNodeManagers.Where(manager =>
                            !HasSameGenerationIdentity(manager, registeredManager))
                    ],
                    snapshot.TypeTree,
                    snapshot.Factory,
                    snapshot.ReadImages));
                return position;
            }
        }

        /// <summary>
        /// Captures the manager's position in the global list and every namespace route.
        /// </summary>
        public NodeManagerRoutingPosition CapturePosition(
            IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            RoutingSnapshot snapshot = Volatile.Read(ref m_snapshot);
            int managerIndex = IndexOf(snapshot.NodeManagers, nodeManager);
            if (managerIndex < 0)
            {
                throw new InvalidOperationException(
                    "The NodeManager whose position was requested is not registered.");
            }

            return CreatePosition(
                snapshot,
                managerIndex,
                snapshot.NodeManagers[managerIndex]);
        }

        /// <summary>
        /// Restores a previously removed NodeManager to its captured routing positions.
        /// The exact generation must have drained, its slot must be unused, and physical cleanup must not have started.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The manager is already registered, or its exact retiring generation cannot be restored
        /// into an unused slot after draining and before physical cleanup.
        /// </exception>
        public void Restore(
            IAsyncNodeManager nodeManager,
            NodeManagerRoutingPosition position,
            bool? visible = null)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }
            if (position is null)
            {
                throw new ArgumentNullException(nameof(position));
            }

            lock (m_lock)
            {
                EnsureMutable();
                RoutingSnapshot snapshot = m_snapshot;
                if (IndexOf(snapshot.NodeManagers, nodeManager) >= 0)
                {
                    throw new InvalidOperationException(
                        "The NodeManager is already registered.");
                }
                if (!TryGet(nodeManager, out NodeManagerGeneration? restoring) ||
                    restoring!.Gate.State != NodeManagerGenerationState.Retiring ||
                    restoring.Slot.Current is not null ||
                    !restoring.TryRestoreBeforeShutdown())
                {
                    throw new InvalidOperationException(
                        "The exact removed generation cannot be restored after cleanup or before its work drains.");
                }

                var managers = new List<IAsyncNodeManager>(snapshot.NodeManagers);
                managers.Insert(
                    ResolveInsertionIndex(
                        managers,
                        position.ManagerPosition),
                    nodeManager);

                Dictionary<int, IReadOnlyList<IAsyncNodeManager>> routes =
                    CopyRoutes(snapshot.NamespaceManagers);
                foreach (KeyValuePair<int, NodeManagerInsertionPoint> routePosition
                    in position.NamespaceRoutePositions)
                {
                    routes.TryGetValue(
                        routePosition.Key,
                        out IReadOnlyList<IAsyncNodeManager>? existing);
                    var updated = existing?.Where(manager =>
                        !HasSameGenerationIdentity(manager, nodeManager)).ToList() ??
                        [];
                    updated.Insert(
                        ResolveInsertionIndex(
                            updated,
                            routePosition.Value),
                        nodeManager);
                    routes[routePosition.Key] = [.. updated];
                }

                IAsyncNodeManager[] hiddenNodeManagers =
                [
                    .. snapshot.HiddenNodeManagers.Where(manager =>
                        !HasSameGenerationIdentity(manager, nodeManager))
                ];
                if (!(visible ?? position.WasVisible))
                {
                    hiddenNodeManagers =
                    [
                        .. hiddenNodeManagers,
                        nodeManager
                    ];
                }

                PublishSnapshot(new RoutingSnapshot(
                    [.. managers],
                    routes,
                    hiddenNodeManagers,
                    snapshot.TypeTree,
                    snapshot.Factory,
                    snapshot.ReadImages));
            }
        }

        /// <summary>
        /// Routes an additional namespace index to an already registered NodeManager.
        /// </summary>
        /// <param name="namespaceIndex">The namespace index to route.</param>
        /// <param name="nodeManager">The NodeManager that serves the namespace.</param>
        /// <param name="visible">
        /// <c>false</c> to keep the NodeManager hidden from Clients until it is committed.
        /// </param>
        public void RegisterNamespace(
            int namespaceIndex,
            IAsyncNodeManager nodeManager,
            bool visible = true)
        {
            lock (m_lock)
            {
                EnsureMutable();
                RoutingSnapshot? next = PrepareNamespaceRegistration(m_snapshot, namespaceIndex, nodeManager, visible);
                if (next is not null)
                {
                    PublishSnapshot(next);
                }
            }
        }

        /// <summary>
        /// Stops routing a namespace index to a NodeManager. The NodeManager may be identified by
        /// its async form or by the synchronous NodeManager an adapter wraps.
        /// </summary>
        /// <param name="namespaceIndex">The namespace index to stop routing.</param>
        /// <param name="asyncNodeManager">The async NodeManager, if known.</param>
        /// <param name="nodeManager">The synchronous NodeManager, if known.</param>
        /// <returns><c>true</c> if a route was removed.</returns>
        public bool UnregisterNamespace(
            int namespaceIndex,
            IAsyncNodeManager? asyncNodeManager,
#pragma warning disable CS0618, UA0031 // Unregistration still accepts the legacy node manager identity.
            INodeManager? nodeManager)
#pragma warning restore CS0618, UA0031
        {
            lock (m_lock)
            {
                EnsureMutable();
                RoutingSnapshot? next = PrepareNamespaceUnregistration(
                    m_snapshot, namespaceIndex, asyncNodeManager, nodeManager);
                if (next is null)
                {
                    return false;
                }
                PublishSnapshot(next);
                return true;
            }
        }

        /// <summary>
        /// Prepares an alias addition without modifying its source routing image.
        /// </summary>
        private static RoutingSnapshot? PrepareNamespaceRegistration(
            RoutingSnapshot snapshot,
            int namespaceIndex,
            IAsyncNodeManager nodeManager,
            bool visible)
        {
            snapshot.NamespaceManagers.TryGetValue(namespaceIndex, out IReadOnlyList<IAsyncNodeManager>? existing);
            int managerIndex = IndexOf(snapshot.NodeManagers, nodeManager);
            IAsyncNodeManager registeredManager = managerIndex >= 0 ? snapshot.NodeManagers[managerIndex] : nodeManager;
            if (existing?.Any(manager => HasSameGenerationIdentity(manager, registeredManager)) == true)
            {
                return null;
            }
            Dictionary<int, IReadOnlyList<IAsyncNodeManager>> routes = CopyRoutes(snapshot.NamespaceManagers);
            routes[namespaceIndex] = existing is null ? [registeredManager] : [.. existing, registeredManager];
            IAsyncNodeManager[] hiddenNodeManagers =
            [
                .. snapshot.HiddenNodeManagers.Where(manager => !HasSameGenerationIdentity(manager, registeredManager))
            ];
            if (!visible)
            {
                hiddenNodeManagers = [.. hiddenNodeManagers, registeredManager];
            }
            return new RoutingSnapshot(
                snapshot.NodeManagers, routes, hiddenNodeManagers,
                snapshot.TypeTree, snapshot.Factory, snapshot.ReadImages);
        }

        /// <summary>
        /// Prepares an alias removal without modifying its source routing image.
        /// </summary>
#pragma warning disable CS0618, UA0031 // Preserve identity-based unregistration for legacy synchronous callers.
        // TODO: Remove the synchronous identity parameter when that overload is removed.
        private static RoutingSnapshot? PrepareNamespaceUnregistration(
            RoutingSnapshot snapshot,
            int namespaceIndex,
            IAsyncNodeManager? asyncNodeManager,
            INodeManager? nodeManager)
#pragma warning restore CS0618, UA0031
        {
            if (!snapshot.NamespaceManagers.TryGetValue(
                namespaceIndex, out IReadOnlyList<IAsyncNodeManager>? existing))
            {
                return null;
            }
            var updated = existing.ToList();
            int removed = updated.RemoveAll(manager =>
                asyncNodeManager is not null
                    ? HasSameGenerationIdentity(manager, asyncNodeManager)
                    : manager.SyncNodeManager is { } syncNodeManager && ReferenceEquals(syncNodeManager, nodeManager));
            if (removed == 0)
            {
                return null;
            }
            Dictionary<int, IReadOnlyList<IAsyncNodeManager>> routes = CopyRoutes(snapshot.NamespaceManagers);
            if (updated.Count == 0)
            {
                routes.Remove(namespaceIndex);
            }
            else
            {
                routes[namespaceIndex] = [.. updated];
            }
            return new RoutingSnapshot(
                snapshot.NodeManagers, routes, snapshot.HiddenNodeManagers,
                snapshot.TypeTree, snapshot.Factory, snapshot.ReadImages);
        }

        /// <summary>
        /// Removes every namespace route that points at a NodeManager, while keeping the
        /// NodeManager itself registered.
        /// </summary>
        /// <param name="nodeManager">The NodeManager to unroute.</param>
        public void RemoveNamespaceManager(IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            lock (m_lock)
            {
                EnsureMutable();
                RoutingSnapshot snapshot = m_snapshot;
                Dictionary<int, IReadOnlyList<IAsyncNodeManager>> routes =
                    CopyRoutes(snapshot.NamespaceManagers);
                foreach (int namespaceIndex in routes.Keys.ToArray())
                {
                    var updated = routes[namespaceIndex].ToList();
                    updated.RemoveAll(manager =>
                        HasSameGenerationIdentity(manager, nodeManager));
                    if (updated.Count == 0)
                    {
                        routes.Remove(namespaceIndex);
                    }
                    else
                    {
                        routes[namespaceIndex] = [.. updated];
                    }
                }

                PublishSnapshot(new RoutingSnapshot(
                    snapshot.NodeManagers,
                    routes,
                    [
                        .. snapshot.HiddenNodeManagers.Where(manager =>
                            !HasSameGenerationIdentity(manager, nodeManager))
                    ],
                    snapshot.TypeTree,
                    snapshot.Factory,
                    snapshot.ReadImages));
            }
        }

        /// <summary>
        /// Gets whether a NodeManager is registered and reachable by Clients.
        /// </summary>
        /// <param name="nodeManager">The NodeManager to test.</param>
        /// <returns><c>true</c> if the NodeManager is registered and not hidden.</returns>
        public bool IsVisible(IAsyncNodeManager nodeManager)
        {
            RoutingSnapshot snapshot = Volatile.Read(ref m_snapshot);
            return snapshot.NodeManagers.Any(manager =>
                HasSameGenerationIdentity(manager, nodeManager)) &&
                !snapshot.HiddenNodeManagers.Any(manager =>
                    HasSameGenerationIdentity(manager, nodeManager));
        }

        /// <summary>
        /// Returns whether the given NodeManager is still registered (visible or hidden).
        /// A shadow-retired generation removed from the routing table returns <c>false</c>,
        /// which callers use to detect that a monitored item is owned by a retired
        /// generation rather than a live routing-table manager.
        /// </summary>
        public bool Contains(IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                return false;
            }

            RoutingSnapshot snapshot = Volatile.Read(ref m_snapshot);
            return snapshot.NodeManagers.Any(manager =>
                HasSameGenerationIdentity(manager, nodeManager));
        }

        /// <summary>
        /// Shows or hides a registered NodeManager. Committing a lifecycle operation shows the
        /// NodeManager, and starting to remove one hides it without unregistering it.
        /// </summary>
        /// <param name="nodeManager">The NodeManager to show or hide.</param>
        /// <param name="visible"><c>true</c> to make the NodeManager reachable by Clients.</param>
        /// <exception cref="ArgumentNullException"><paramref name="nodeManager"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The NodeManager is not registered.</exception>
        public void SetVisible(
            IAsyncNodeManager nodeManager,
            bool visible)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            lock (m_lock)
            {
                EnsureMutable();
                RoutingSnapshot snapshot = m_snapshot;
                int managerIndex = IndexOf(snapshot.NodeManagers, nodeManager);
                if (managerIndex < 0)
                {
                    throw new InvalidOperationException(
                        "The NodeManager is not registered.");
                }
                IAsyncNodeManager registeredManager =
                    snapshot.NodeManagers[managerIndex];

                IAsyncNodeManager[] hiddenNodeManagers =
                [
                    .. snapshot.HiddenNodeManagers.Where(manager =>
                        !HasSameGenerationIdentity(manager, registeredManager))
                ];
                if (!visible)
                {
                    hiddenNodeManagers =
                    [
                        .. hiddenNodeManagers,
                        registeredManager
                    ];
                }

                PublishSnapshot(new RoutingSnapshot(
                    snapshot.NodeManagers,
                    snapshot.NamespaceManagers,
                    hiddenNodeManagers,
                    snapshot.TypeTree,
                    snapshot.Factory,
                    snapshot.ReadImages));
            }
        }

        /// <summary>
        /// Removes every NodeManager and route, which happens when the server shuts down.
        /// </summary>
        public void Clear()
        {
            lock (m_lock)
            {
                EnsureMutable();
                PublishSnapshot(RoutingSnapshot.Empty);
            }
        }

        /// <summary>
        /// Returns an enumerator over the NodeManagers that are reachable by Clients. The
        /// enumerator walks a snapshot, so it is unaffected by concurrent lifecycle operations.
        /// </summary>
        public IEnumerator<IAsyncNodeManager> GetEnumerator()
        {
            IAsyncNodeManager[] nodeManagers =
                ReadSnapshot.VisibleNodeManagers;
            return ((IEnumerable<IAsyncNodeManager>)nodeManagers).GetEnumerator();
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Returns the position of a NodeManager by async or wrapped synchronous identity,
        /// or -1 when it is absent.
        /// </summary>
        private static int IndexOf(
            IReadOnlyList<IAsyncNodeManager> managers,
            IAsyncNodeManager manager)
        {
            for (int ii = 0; ii < managers.Count; ii++)
            {
                if (HasSameGenerationIdentity(managers[ii], manager))
                {
                    return ii;
                }
            }
            return -1;
        }

        /// <summary>
        /// Returns the position of the exact registered manager, or -1 when it is absent.
        /// </summary>
        private static int IndexOfExact(
            List<IAsyncNodeManager> managers,
            IAsyncNodeManager manager)
        {
            for (int ii = 0; ii < managers.Count; ii++)
            {
                if (ReferenceEquals(managers[ii], manager))
                {
                    return ii;
                }
            }
            return -1;
        }

        /// <summary>
        /// Copies the namespace routes so a mutation can be applied without touching the snapshot
        /// that readers are currently using.
        /// </summary>
        private static Dictionary<int, IReadOnlyList<IAsyncNodeManager>> CopyRoutes(
            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> routes)
        {
            return routes.ToDictionary(entry => entry.Key, entry => entry.Value);
        }

        private static NodeManagerRoutingPosition CreatePosition(
            RoutingSnapshot snapshot,
            int managerIndex,
            IAsyncNodeManager registeredManager)
        {
            var routePositions =
                new Dictionary<int, NodeManagerInsertionPoint>();
            foreach (KeyValuePair<int, IReadOnlyList<IAsyncNodeManager>> route
                in snapshot.NamespaceManagers)
            {
                int routeIndex = IndexOf(route.Value, registeredManager);
                if (routeIndex >= 0)
                {
                    routePositions.Add(
                        route.Key,
                        CreateInsertionPoint(route.Value, routeIndex));
                }
            }

            return new NodeManagerRoutingPosition(
                CreateInsertionPoint(snapshot.NodeManagers, managerIndex),
                routePositions,
                !snapshot.HiddenNodeManagers.Any(manager =>
                    HasSameGenerationIdentity(manager, registeredManager)));
        }

        private static NodeManagerInsertionPoint CreateInsertionPoint(
            IReadOnlyList<IAsyncNodeManager> managers,
            int index)
        {
            return new NodeManagerInsertionPoint(
                index,
                index > 0 ? managers[index - 1] : null,
                index + 1 < managers.Count ? managers[index + 1] : null);
        }

        private static int ResolveInsertionIndex(
            List<IAsyncNodeManager> managers,
            NodeManagerInsertionPoint position)
        {
            if (position.Next is not null)
            {
                int nextIndex = IndexOf(managers, position.Next);
                if (nextIndex >= 0)
                {
                    return nextIndex;
                }
            }
            if (position.Previous is not null)
            {
                int previousIndex = IndexOf(managers, position.Previous);
                if (previousIndex >= 0)
                {
                    return previousIndex + 1;
                }
            }
            return Math.Min(position.Index, managers.Count);
        }

        private readonly Lock m_lock = new();
        private RoutingSnapshot m_snapshot = RoutingSnapshot.Empty;
        private long m_revision;

        /// <summary>
        /// An immutable view of the routing table. Every mutation publishes a new instance, which
        /// is what allows readers to work without locking.
        /// </summary>
        internal sealed class RoutingSnapshot
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="RoutingSnapshot"/> class and
            /// precomputes the views that exclude hidden NodeManagers.
            /// </summary>
            /// <param name="nodeManagers">All registered NodeManagers, in dispatch order.</param>
            /// <param name="namespaceManagers">The NodeManagers serving each namespace index.</param>
            /// <param name="hiddenNodeManagers">The NodeManagers not yet reachable by Clients.</param>
            /// <param name="typeTree">The type image published with these routes, if supplied.</param>
            /// <param name="factory">The factory image published with these routes, if supplied.</param>
            /// <param name="readImages">Immutable application state belonging to the registered owners.</param>
            public RoutingSnapshot(
                IAsyncNodeManager[] nodeManagers,
                IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> namespaceManagers,
                IAsyncNodeManager[] hiddenNodeManagers,
                TypeTable? typeTree = null,
                EncodeableFactory? factory = null,
                IReadOnlyDictionary<IAsyncNodeManager, INodeManagerReadImage>? readImages = null)
            {
                NodeManagers = nodeManagers;
                NamespaceManagers = namespaceManagers;
                HiddenNodeManagers = hiddenNodeManagers;
                TypeTree = typeTree?.CaptureImmutableSnapshot();
                Factory = factory?.CaptureSnapshot(out _, out _);
                IEqualityComparer<IAsyncNodeManager> ownerComparer = RefEqualityComparer.Default;
                ReadImages = readImages is null
                    ? new Dictionary<IAsyncNodeManager, INodeManagerReadImage>(ownerComparer)
                    : readImages.Where(entry => Array.Exists(nodeManagers, owner => ReferenceEquals(owner, entry.Key)))
                        .ToDictionary(entry => entry.Key, entry => entry.Value, ownerComparer);
                Generations = [.. nodeManagers.Select(NodeManagerGenerationRegistry.GetOrCreate)];
                m_ambiguousVisibleResources = FindAmbiguousSourceResources(
                    nodeManagers, Generations, hiddenNodeManagers, includeHidden: false);
                m_ambiguousResources = hiddenNodeManagers.Length == 0
                    ? m_ambiguousVisibleResources
                    : FindAmbiguousSourceResources(nodeManagers, Generations, hiddenNodeManagers, includeHidden: true);
                m_pendingReadiness = [.. Generations.Where(generation => generation.ReadinessPending)];
                VisibleNodeManagers =
                [
                    .. nodeManagers.Where(manager =>
                        !hiddenNodeManagers.Any(hidden =>
                            ReferenceEquals(hidden, manager)))
                ];
                var overrides = new Dictionary<NodeId, IAsyncNodeManager>();
                foreach (IAsyncNodeManager manager in VisibleNodeManagers)
                {
                    if (NodeManagerGenerationRegistry.GetIdentity(manager) is not INodeManagerNodeOverrides owner)
                    {
                        continue;
                    }
                    foreach (NodeId nodeId in owner.OverriddenNodeIds)
                    {
                        if (nodeId.IsNull || !overrides.TryAdd(nodeId, manager))
                        {
                            throw new InvalidOperationException(
                                "Node overrides must have non-null, unique identifiers in a routing view.");
                        }
                    }
                }
                NodeOverrides = overrides;
                Epoch = new AddressSpaceEpoch();
                Epoch.Attach(this);
                foreach (NodeManagerGeneration generation in Generations)
                {
                    generation.RegisterViewEpoch(Epoch);
                }
                VisibleNamespaceManagers =
                    CreateVisibleNamespaceManagers(
                        namespaceManagers,
                        hiddenNodeManagers);
                Epoch = new AddressSpaceEpoch();
                bool registered = false;
                try
                {
                    foreach (NodeManagerGeneration generation in Generations)
                    {
                        generation.RegisterViewEpoch(Epoch);
                    }
                    registered = true;
                }
                finally
                {
                    if (!registered)
                    {
                        Epoch.Close();
                    }
                }
            }

            /// <summary>
            /// Gets the snapshot of a routing table without any NodeManager.
            /// </summary>
            public static RoutingSnapshot Empty
                => new(
                    [],
                    new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>(),
                    []);

            /// <summary>
            /// Gets the generations routed by this immutable view in dispatch order.
            /// </summary>
            public NodeManagerGeneration[] Generations { get; }

            /// <summary>
            /// Gets the monotonic revision assigned when this view is published.
            /// </summary>
            public long Revision { get; private set; }

            /// <summary>
            /// Assigns the revision before this view becomes visible to readers.
            /// </summary>
            /// <param name="revision">The next routing revision.</param>
            internal void SetRevision(long revision)
            {
                Revision = revision;
            }

            /// <summary>
            /// Gets the striped request epoch owned by this immutable view.
            /// </summary>
            public AddressSpaceEpoch Epoch { get; }

            /// <summary>
            /// Finds the exact selected generation that still requires readiness admission.
            /// </summary>
            /// <param name="nodeManager">The owner selected by these namespace routes.</param>
            /// <returns>The pending generation, or null when no readiness check is needed.</returns>
            internal NodeManagerGeneration? FindPendingReadiness(IAsyncNodeManager nodeManager)
            {
                foreach (NodeManagerGeneration generation in m_pendingReadiness)
                {
                    if (ReferenceEquals(generation.NodeManager, nodeManager) && generation.ReadinessPending)
                    {
                        return generation;
                    }
                }
                return null;
            }

            /// <summary>
            /// Resolves a manager to the exact generation carried by this view.
            /// </summary>
            /// <param name="nodeManager">The manager in this view.</param>
            /// <returns>The generation identity.</returns>
            internal NodeManagerGeneration GetGeneration(IAsyncNodeManager nodeManager)
            {
                if (TryGetGeneration(nodeManager, out NodeManagerGeneration? generation))
                {
                    return generation!;
                }
                throw new InvalidOperationException("The manager is not present in this routing view.");
            }

            /// <summary>
            /// Finds the registered owner for an exact manager or a transparent stack adapter.
            /// </summary>
            /// <param name="nodeManager">The manager or standard adapter to resolve.</param>
            /// <returns>The exact registered owner, or null when it is absent.</returns>
            /// <exception cref="InvalidOperationException">
            /// A standard adapter matches more than one registered owner by generation identity.
            /// </exception>
            internal IAsyncNodeManager? FindRegisteredOwner(IAsyncNodeManager nodeManager)
            {
                foreach (IAsyncNodeManager owner in NodeManagers)
                {
                    if (ReferenceEquals(owner, nodeManager))
                    {
                        return owner;
                    }
                }

                IAsyncNodeManager? match = null;
                foreach (IAsyncNodeManager owner in NodeManagers)
                {
                    if ((!IsStandardAdapter(owner) && !IsStandardAdapter(nodeManager)) ||
                        !HasSameGenerationIdentity(owner, nodeManager))
                    {
                        continue;
                    }
                    if (match is not null)
                    {
                        throw new InvalidOperationException(
                            "A standard adapter matches more than one registered NodeManager.");
                    }
                    match = owner;
                }
                return match;
            }

            /// <summary>
            /// Attempts to resolve a manager to its exact generation in this view.
            /// </summary>
            /// <param name="nodeManager">The manager in the view.</param>
            /// <param name="generation">The matching generation, when present.</param>
            /// <returns><c>true</c> when the manager is present in the view.</returns>
            internal bool TryGetGeneration(
                IAsyncNodeManager nodeManager,
                out NodeManagerGeneration? generation)
            {
                for (int ii = 0; ii < NodeManagers.Length; ii++)
                {
                    if (ReferenceEquals(NodeManagers[ii], nodeManager))
                    {
                        generation = Generations[ii];
                        return true;
                    }
                }
                if (TryGet(nodeManager, out NodeManagerGeneration? identity))
                {
                    for (int ii = 0; ii < Generations.Length; ii++)
                    {
                        if (ReferenceEquals(Generations[ii], identity))
                        {
                            generation = identity;
                            return true;
                        }
                    }
                }
                generation = null;
                return false;
            }

            /// <summary>
            /// Resolves an unbound event source through a visible facade over the same resource.
            /// </summary>
            /// <param name="nodeManager">The event source.</param>
            /// <param name="generation">The exact published generation, when present.</param>
            /// <param name="includeHidden">Whether a staged owner may be resolved for binding.</param>
            /// <returns><c>true</c> if the source has an unambiguous owner in this view.</returns>
            internal bool TryGetSourceGeneration(
                IAsyncNodeManager nodeManager,
                out NodeManagerGeneration? generation,
                bool includeHidden = false)
            {
                generation = null;
                HashSet<IAsyncNodeManager> ambiguous =
                    includeHidden ? m_ambiguousResources : m_ambiguousVisibleResources;
                if (ambiguous.Contains(nodeManager))
                {
                    return false;
                }
                if (IsStandardAdapter(nodeManager))
                {
                    IAsyncNodeManager resource = GetIdentity(nodeManager);
                    if (ambiguous.Contains(resource) && HasSameGenerationIdentity(nodeManager, resource))
                    {
                        return false;
                    }
                }
                for (int ii = 0; ii < NodeManagers.Length; ii++)
                {
                    IAsyncNodeManager owner = NodeManagers[ii];
                    if (ReferenceEquals(owner, nodeManager))
                    {
                        if (includeHidden || !ContainsManager(HiddenNodeManagers, owner))
                        {
                            generation = Generations[ii];
                            return true;
                        }
                        return false;
                    }
                }
                if (IsStandardAdapter(nodeManager))
                {
                    for (int ii = 0; ii < NodeManagers.Length; ii++)
                    {
                        IAsyncNodeManager owner = NodeManagers[ii];
                        if ((includeHidden || !ContainsManager(HiddenNodeManagers, owner)) &&
                            HasSameGenerationIdentity(owner, nodeManager))
                        {
                            if (generation is not null && !ReferenceEquals(generation, Generations[ii]))
                            {
                                generation = null;
                                return false;
                            }
                            generation = Generations[ii];
                        }
                    }
                    if (generation is not null)
                    {
                        return true;
                    }
                }
                IAsyncNodeManager sourceResource = GetIdentity(nodeManager);
                if (!HasSameGenerationIdentity(nodeManager, sourceResource))
                {
                    return false;
                }
                IAsyncNodeManager? matchedOwner = null;
                for (int ii = 0; ii < NodeManagers.Length; ii++)
                {
                    IAsyncNodeManager owner = NodeManagers[ii];
                    if (!ReferenceEquals(GetIdentity(owner), sourceResource))
                    {
                        continue;
                    }
                    if (!includeHidden && ContainsManager(HiddenNodeManagers, owner))
                    {
                        continue;
                    }
                    if (generation is not null &&
                        (!ReferenceEquals(generation, Generations[ii]) ||
                            matchedOwner is null ||
                            !HasSameGenerationIdentity(matchedOwner, owner)))
                    {
                        generation = null;
                        return false;
                    }
                    generation = Generations[ii];
                    matchedOwner = owner;
                }
                return generation is not null;
            }

            /// <summary>
            /// Checks whether the exact generation or a standard alias remains visible in this view.
            /// </summary>
            /// <param name="generation">The generation already bound to the operation.</param>
            /// <returns>Whether a visible owner carries that exact generation.</returns>
            internal bool ContainsVisibleGenerationOwner(NodeManagerGeneration generation)
            {
                for (int ii = 0; ii < NodeManagers.Length; ii++)
                {
                    IAsyncNodeManager owner = NodeManagers[ii];
                    if (!ContainsManager(HiddenNodeManagers, owner) &&
                        ReferenceEquals(Generations[ii], generation) &&
                        (ReferenceEquals(owner, generation.NodeManager) ||
                            ((IsStandardAdapter(owner) || IsStandardAdapter(generation.NodeManager)) &&
                                HasSameGenerationIdentity(owner, generation.NodeManager))))
                    {
                        return true;
                    }
                }
                return false;
            }

            /// <summary>
            /// Checks whether any visible or hidden owner uses the specified physical resource.
            /// </summary>
            internal bool ContainsSourceResource(IAsyncNodeManager nodeManager)
            {
                IAsyncNodeManager resource = GetIdentity(nodeManager);
                foreach (IAsyncNodeManager owner in NodeManagers)
                {
                    if (ReferenceEquals(GetIdentity(owner), resource))
                    {
                        return true;
                    }
                }
                return false;
            }

            /// <summary>
            /// Attempts to acquire a request pin while this view remains published.
            /// </summary>
            /// <param name="slot">The epoch counter slot on success.</param>
            /// <returns><c>true</c> if this view admitted the request.</returns>
            internal bool TryPin(out int slot)
            {
                return Epoch.TryPin(out slot);
            }

            /// <summary>
            /// Gets the type image owned by this routing generation.
            /// </summary>
            public TypeTable? TypeTree { get; }

            /// <summary>
            /// Gets the factory image owned by this routing generation.
            /// </summary>
            public EncodeableFactory? Factory { get; }

            /// <summary>
            /// Gets the in-memory reference images owned by this routing generation.
            /// </summary>
            /// <summary>
            /// Gets the immutable application state owned by this routing generation.
            /// </summary>
            public IReadOnlyDictionary<IAsyncNodeManager, INodeManagerReadImage> ReadImages { get; }

            /// <summary>
            /// Gets all registered NodeManagers, in dispatch order, including hidden ones.
            /// </summary>
            public IAsyncNodeManager[] NodeManagers { get; }

            /// <summary>
            /// Gets the NodeManagers serving each namespace index, including hidden ones.
            /// </summary>
            public IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> NamespaceManagers { get; }

            /// <summary>
            /// Gets the registered NodeManagers that are not reachable by Clients.
            /// </summary>
            public IAsyncNodeManager[] HiddenNodeManagers { get; }

            /// <summary>
            /// Gets the NodeManagers that are reachable by Clients, in dispatch order.
            /// </summary>
            public IAsyncNodeManager[] VisibleNodeManagers { get; }

            /// <summary>
            /// Gets the exact override owners captured by this routing view.
            /// </summary>
            internal IReadOnlyDictionary<NodeId, IAsyncNodeManager> NodeOverrides { get; }

            /// <summary>
            /// Gets the references frozen for a previously published routing view.
            /// </summary>
            internal bool TryGetReferences(NodeState node, out ArrayOf<IReference> references)
            {
                return Volatile.Read(ref m_referenceImages).TryGetValue(node, out references);
            }

            /// <summary>
            /// Captures the reference images currently installed on this view.
            /// </summary>
            internal Dictionary<NodeState, ArrayOf<IReference>> CopyReferenceImages()
            {
                return new Dictionary<NodeState, ArrayOf<IReference>>(Volatile.Read(ref m_referenceImages));
            }

            /// <summary>
            /// Installs prepared reference images before the routing view is replaced.
            /// </summary>
            internal void InstallReferenceImages(Dictionary<NodeState, ArrayOf<IReference>> images)
            {
                Volatile.Write(ref m_referenceImages, images);
            }

            /// <summary>
            /// Gets the NodeManagers serving each namespace index, excluding hidden ones.
            /// </summary>
            public IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>>
                VisibleNamespaceManagers
            { get; }

            /// <summary>
            /// Builds the namespace routes that exclude hidden NodeManagers, dropping namespaces
            /// left without any visible NodeManager.
            /// </summary>
            private static Dictionary<int, IReadOnlyList<IAsyncNodeManager>>
                CreateVisibleNamespaceManagers(
                    IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> routes,
                    IAsyncNodeManager[] hiddenNodeManagers)
            {
                var visibleRoutes =
                    new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>();
                foreach (KeyValuePair<int, IReadOnlyList<IAsyncNodeManager>> route in routes)
                {
                    IAsyncNodeManager[] visibleManagers =
                    [
                        .. route.Value.Where(manager =>
                            !hiddenNodeManagers.Any(hidden =>
                                ReferenceEquals(hidden, manager)))
                    ];
                    if (visibleManagers.Length > 0)
                    {
                        visibleRoutes[route.Key] = visibleManagers;
                    }
                }
                return visibleRoutes;
            }

            /// <summary>
            /// Finds resources represented by distinct logical owners or conflicting generation bindings.
            /// </summary>
            private static HashSet<IAsyncNodeManager> FindAmbiguousSourceResources(
                IAsyncNodeManager[] owners,
                NodeManagerGeneration[] generations,
                IAsyncNodeManager[] hidden,
                bool includeHidden)
            {
                var firstOwners = new Dictionary<
                    IAsyncNodeManager,
                    (IAsyncNodeManager Owner, NodeManagerGeneration Generation)>(RefEqualityComparer.Default);
                var ambiguous = new HashSet<IAsyncNodeManager>(RefEqualityComparer.Default);
                for (int ii = 0; ii < owners.Length; ii++)
                {
                    IAsyncNodeManager owner = owners[ii];
                    if (!includeHidden && ContainsManager(hidden, owner))
                    {
                        continue;
                    }
                    IAsyncNodeManager resource = GetIdentity(owner);
                    if (!firstOwners.TryGetValue(
                        resource, out (IAsyncNodeManager Owner, NodeManagerGeneration Generation) first))
                    {
                        firstOwners.Add(resource, (owner, generations[ii]));
                    }
                    else if (!ReferenceEquals(first.Generation, generations[ii]) ||
                        !HasSameGenerationIdentity(first.Owner, owner))
                    {
                        ambiguous.Add(resource);
                    }
                }
                return ambiguous;
            }

            /// <summary>The resources with more than one visible logical source.</summary>
            private readonly HashSet<IAsyncNodeManager> m_ambiguousVisibleResources;

            /// <summary>The resources with more than one source, including hidden owners.</summary>
            private readonly HashSet<IAsyncNodeManager> m_ambiguousResources;

            /// <summary>The generations whose readiness was pending when this view was built.</summary>
            private readonly NodeManagerGeneration[] m_pendingReadiness;

            /// <summary>The frozen reference images for this routing view.</summary>
            private Dictionary<NodeState, ArrayOf<IReference>> m_referenceImages = [];
        }

        /// <summary>
        /// Captures a lifecycle-managed NodeManager's ordering in the routing table.
        /// </summary>
        internal sealed class NodeManagerRoutingPosition
        {
            /// <summary>
            /// Initializes a captured routing position.
            /// </summary>
            /// <param name="managerPosition">The position in the global manager list.</param>
            /// <param name="namespaceRoutePositions">The position in each namespace route.</param>
            /// <param name="wasVisible">Whether the manager was visible to clients.</param>
            public NodeManagerRoutingPosition(
                NodeManagerInsertionPoint managerPosition,
                IReadOnlyDictionary<int, NodeManagerInsertionPoint> namespaceRoutePositions,
                bool wasVisible)
            {
                ManagerPosition = managerPosition;
                NamespaceRoutePositions = namespaceRoutePositions;
                WasVisible = wasVisible;
            }

            /// <summary>
            /// Gets the position in the global manager list.
            /// </summary>
            public NodeManagerInsertionPoint ManagerPosition { get; }

            /// <summary>
            /// Gets the position in each namespace route.
            /// </summary>
            public IReadOnlyDictionary<int, NodeManagerInsertionPoint>
                NamespaceRoutePositions
            { get; }

            /// <summary>
            /// Gets whether the manager was visible to clients.
            /// </summary>
            public bool WasVisible { get; }
        }

        /// <summary>
        /// Identifies an insertion point by its former index and neighboring managers.
        /// </summary>
        internal sealed class NodeManagerInsertionPoint
        {
            /// <summary>
            /// Initializes an insertion point.
            /// </summary>
            /// <param name="index">The former absolute index.</param>
            /// <param name="previous">The former predecessor, if any.</param>
            /// <param name="next">The former successor, if any.</param>
            public NodeManagerInsertionPoint(
                int index,
                IAsyncNodeManager? previous,
                IAsyncNodeManager? next)
            {
                Index = index;
                Previous = previous;
                Next = next;
            }

            /// <summary>
            /// Gets the former absolute index.
            /// </summary>
            public int Index { get; }

            /// <summary>
            /// Gets the manager that formerly preceded the removed manager.
            /// </summary>
            public IAsyncNodeManager? Previous { get; }

            /// <summary>
            /// Gets the manager that formerly followed the removed manager.
            /// </summary>
            public IAsyncNodeManager? Next { get; }
        }

        /// <summary>
        /// Gets the current published routing revision.
        /// </summary>
        internal RoutingSnapshot Revision => Volatile.Read(ref m_snapshot);

        /// <summary>
        /// Pins the current routing revision, retrying when publication closes the view concurrently.
        /// </summary>
        /// <returns>The immutable view, epoch, and matching counter slot.</returns>
        internal (RoutingSnapshot Snapshot, AddressSpaceEpoch Epoch, int Slot) PinCurrent()
        {
            var spinWait = new SpinWait();
            while (true)
            {
                RoutingSnapshot snapshot = Revision;
                if (!snapshot.TryPin(out int slot))
                {
                    spinWait.SpinOnce();
                    continue;
                }
                if (ReferenceEquals(snapshot, Revision))
                {
                    return (snapshot, snapshot.Epoch, slot);
                }
                snapshot.Epoch.Exit(slot);
                spinWait.SpinOnce();
            }
        }

        /// <summary>
        /// Gets the type registration image captured by this routing revision.
        /// </summary>
        internal TypeTable? TypeTree => Revision.TypeTree;

        /// <summary>
        /// Gets the encodeable registration image captured by this routing revision.
        /// </summary>
        internal EncodeableFactory? Factory => Revision.Factory;

        /// <summary>
        /// Gets live routes. Metadata image scopes never select dispatch routes.
        /// </summary>
        private RoutingSnapshot ReadSnapshot => Volatile.Read(ref m_snapshot);

        /// <summary>
        /// Gets the live published application state for a node manager.
        /// </summary>
        /// <param name="owner">The node manager that owns the image.</param>
        /// <returns>The read image, or null if the owner has none.</returns>
        internal INodeManagerReadImage? GetReadImage(IAsyncNodeManager owner)
        {
            return Revision.ReadImages.TryGetValue(owner, out INodeManagerReadImage? image) ? image : null;
        }

        /// <summary>
        /// Builds the routing image that results from adding, replacing and removing node managers.
        /// </summary>
        /// <param name="candidates">The prepared node managers to add or replace.</param>
        /// <param name="removed">The node managers to retire.</param>
        /// <param name="resolveNamespaces">Resolves the namespace indexes served by a node manager.</param>
        /// <param name="typeTree">The type tree to publish.</param>
        /// <param name="factory">The encodeable factory to publish.</param>
        /// <param name="readImages">The read images to publish.</param>
        /// <returns>The prepared routes that can be reserved and published.</returns>
        /// <exception cref="InvalidOperationException">
        /// A candidate has been consumed, has lost its identity or is already registered;
        /// a candidate or predecessor is repeated; a retirement is not registered; or a read image
        /// has no unique owner in the next view.
        /// </exception>
        internal PreparedRoutes PrepareBatch(
            ArrayOf<PreparedNodeManager> candidates,
            ArrayOf<IAsyncNodeManager> removed,
            Func<IAsyncNodeManager, IEnumerable<int>> resolveNamespaces,
            TypeTable typeTree,
            EncodeableFactory? factory,
            ArrayOf<INodeManagerReadImage> readImages = default)
        {
            lock (m_lock)
            {
                RoutingSnapshot current = m_snapshot;
                var removals = new HashSet<IAsyncNodeManager>(RefEqualityComparer.Default);
                for (int index = 0; index < removed.Count; index++)
                {
                    removals.Add(current.FindRegisteredOwner(removed[index]) ??
                        throw new InvalidOperationException("A batch retirement is not registered."));
                }
                var publishing = new HashSet<IAsyncNodeManager>(RefEqualityComparer.Default);
                var publishingIdentities = new HashSet<IAsyncNodeManager>(RefEqualityComparer.Default);
                var replacements = new Dictionary<IAsyncNodeManager, IAsyncNodeManager>(RefEqualityComparer.Default);
                foreach (PreparedNodeManager candidate in candidates)
                {
                    candidate.ValidateForPublication();
                    if (!TryGetCandidateIdentity(candidate))
                    {
                        throw new InvalidOperationException("A candidate no longer owns its assigned generation.");
                    }
                    if (!publishingIdentities.Add(GetGenerationIdentity(candidate.NodeManager)))
                    {
                        throw new InvalidOperationException("A batch candidate was supplied more than once.");
                    }
                    publishing.Add(candidate.NodeManager);
                    if (candidate.ReplacedNodeManager is { } replaced)
                    {
                        IAsyncNodeManager owner = current.FindRegisteredOwner(replaced) ??
                            throw new InvalidOperationException("A batch retirement is not registered.");
                        removals.Add(owner);
                        if (!replacements.TryAdd(owner, candidate.NodeManager))
                        {
                            throw new InvalidOperationException("A batch predecessor was supplied more than once.");
                        }
                    }
                    if (current.FindRegisteredOwner(candidate.NodeManager) is not null)
                    {
                        throw new InvalidOperationException("A batch candidate is already registered.");
                    }
                }

                var managers = new List<IAsyncNodeManager>();
                foreach (IAsyncNodeManager manager in current.NodeManagers)
                {
                    if (replacements.TryGetValue(manager, out IAsyncNodeManager? replacement))
                    {
                        managers.Add(replacement);
                    }
                    else if (!removals.Contains(manager))
                    {
                        managers.Add(manager);
                    }
                }
                foreach (PreparedNodeManager candidate in candidates)
                {
                    if (candidate.ReplacedNodeManager is null)
                    {
                        managers.Add(candidate.NodeManager);
                    }
                }

                var routes = new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>();
                var namespaceChanges = new HashSet<int>();
                foreach (KeyValuePair<int, IReadOnlyList<IAsyncNodeManager>> route in current.NamespaceManagers)
                {
                    IAsyncNodeManager[] retained =
                    [
                        .. route.Value.Where(manager => !removals.Contains(manager))
                    ];
                    if (retained.Length != 0)
                    {
                        routes.Add(route.Key, retained);
                    }
                    if (retained.Length != route.Value.Count)
                    {
                        namespaceChanges.Add(route.Key);
                    }
                }
                foreach (PreparedNodeManager candidate in candidates)
                {
                    foreach (int index in resolveNamespaces(candidate.NodeManager).Distinct())
                    {
                        namespaceChanges.Add(index);
                        routes.TryGetValue(index, out IReadOnlyList<IAsyncNodeManager>? existing);
                        routes[index] = existing is null
                            ? [candidate.NodeManager]
                            : [.. existing.Where(manager => !HasSameGenerationIdentity(manager, candidate.NodeManager)),
                                candidate.NodeManager];
                    }
                }
                // Unrelated alias order must not become an artificial publication conflict.
                foreach (int index in namespaceChanges)
                {
                    if (routes.TryGetValue(index, out IReadOnlyList<IAsyncNodeManager>? changed))
                    {
                        routes[index] = [.. changed.OrderBy(manager => IndexOfExact(managers, manager))];
                    }
                }
                IEqualityComparer<IAsyncNodeManager> ownerComparer = RefEqualityComparer.Default;
                Dictionary<IAsyncNodeManager, INodeManagerReadImage> images =
                    current.ReadImages.ToDictionary(
                        entry => entry.Key, entry => entry.Value, ownerComparer);
                var imageOwners = new HashSet<IAsyncNodeManager>(RefEqualityComparer.Default);
                foreach (INodeManagerReadImage image in readImages)
                {
                    if (image?.Owner is not { } owner || !managers.Any(manager => ReferenceEquals(manager, owner)))
                    {
                        throw new InvalidOperationException("A read image has no owner in the prepared routing image.");
                    }
                    if (!imageOwners.Add(owner))
                    {
                        throw new InvalidOperationException("A publication contains more than one image for an owner.");
                    }
                    images[owner] = image;
                }
                var next = new RoutingSnapshot(
                    [.. managers],
                    routes,
                    [
                        .. current.HiddenNodeManagers.Where(manager =>
                            !removals.Contains(manager) &&
                            !publishing.Any(candidate => HasSameGenerationIdentity(candidate, manager)))
                    ],
                    typeTree,
                    factory,
                    images);
                return new PreparedRoutes(this, current, next);

                bool TryGetCandidateIdentity(PreparedNodeManager candidate)
                {
                    return TryGet(
                        candidate.NodeManager, out NodeManagerGeneration? identity) &&
                        ReferenceEquals(identity, candidate.Generation) &&
                        (candidate.ReplacedNodeManager is not { } predecessor ||
                            ReferenceEquals(current.GetGeneration(predecessor).Slot, candidate.Generation.Slot));
                }
            }
        }

        /// <summary>
        /// Explains a failed admission by a real publication or a changed routing image, never a busy retry.
        /// </summary>
        internal Task GetPublicationChange(RoutingSnapshot previous)
        {
            lock (m_lock)
            {
                if (m_reservedRoutes is { } pending)
                {
                    return pending.WhenReleased;
                }
                if (!ReferenceEquals(m_snapshot, previous))
                {
                    return Task.CompletedTask;
                }
                throw new InvalidOperationException("An unchanged serving routing image cannot admit its owner.");
            }
        }

        /// <summary>
        /// Throws if the routing image is reserved by a pending publication.
        /// </summary>
        private void EnsureMutable()
        {
            if (m_reservedRoutes is not null)
            {
                throw new InvalidOperationException(
                    "The routing image belongs to a pending publication and cannot be changed.");
            }
        }

        private PreparedRoutes? m_reservedRoutes;

        /// <summary>
        /// Routing image prepared for a batch that can be reserved and then published.
        /// </summary>
        internal sealed class PreparedRoutes : IDisposable
        {
            /// <summary>
            /// Gets the routing view prepared for publication.
            /// </summary>
            internal RoutingSnapshot Next => m_next;

            /// <summary>
            /// Creates prepared routes.
            /// </summary>
            /// <param name="owner">The routing table.</param>
            /// <param name="previous">The snapshot the routes were prepared from.</param>
            /// <param name="next">The snapshot to publish.</param>
            internal PreparedRoutes(
                NodeManagerRoutingTable owner,
                RoutingSnapshot previous,
                RoutingSnapshot next)
            {
                m_owner = owner;
                m_previous = previous;
                m_next = next;
                m_reservedNamespaces =
                [
                    .. previous.NamespaceManagers.Keys.Concat(next.NamespaceManagers.Keys)
                        .Where(index =>
                            !previous.NamespaceManagers.TryGetValue(
                                index, out IReadOnlyList<IAsyncNodeManager>? before) ||
                            !next.NamespaceManagers.TryGetValue(
                                index, out IReadOnlyList<IAsyncNodeManager>? after) ||
                            !before.SequenceEqual(after, RefEqualityComparer.Default))
                ];
                m_changingManagers =
                [
                    .. previous.NodeManagers.Concat(next.NodeManagers).Where(manager =>
                        !ContainsManager(previous.NodeManagers, manager) ||
                        !ContainsManager(next.NodeManagers, manager) ||
                        ContainsManager(previous.HiddenNodeManagers, manager) !=
                            ContainsManager(next.HiddenNodeManagers, manager))
                ];
            }

            /// <summary>
            /// Gets completion of publication bookkeeping or abandonment.
            /// </summary>
            internal Task WhenReleased
            {
                get
                {
                    lock (m_owner.m_lock)
                    {
                        return m_disposed
                            ? Task.CompletedTask
                            : (m_released ??= new TaskCompletionSource<bool>(
                                TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                    }
                }
            }

            /// <summary>
            /// Reserves this routing transaction, permitting only explicit unrelated namespace reconciliation.
            /// </summary>
            public void Reserve()
            {
                lock (m_owner.m_lock)
                {
                    if (m_disposed || m_published || ReferenceEquals(m_owner.m_reservedRoutes, this))
                    {
                        throw new InvalidOperationException("The routing publication is no longer preparable.");
                    }
                    if (!ReferenceEquals(m_owner.m_snapshot, m_previous))
                    {
                        throw new InvalidOperationException("Routing changed during batch preparation.");
                    }
                    m_owner.EnsureMutable();
                    ValidatePublicationTransitions(m_previous, m_next);
                    m_owner.m_reservedRoutes = this;
                }
            }

            /// <summary>
            /// Checks whether an alias mutation touches a namespace or owner changed by this exact publication.
            /// </summary>
            internal bool ConflictsWithNamespace(int namespaceIndex, IAsyncNodeManager nodeManager)
            {
                lock (m_owner.m_lock)
                {
                    ValidateNamespaceMutation();
                    return m_reservedNamespaces.Contains(namespaceIndex) ||
                        m_changingManagers.Any(manager => HasSameGenerationIdentity(manager, nodeManager));
                }
            }

            /// <summary>
            /// Mirrors an allowed alias mutation into the serving image and this reserved successor only.
            /// </summary>
            /// <exception cref="InvalidOperationException">
            /// The reservation is no longer current, the namespace or manager conflicts with it,
            /// or the reserved successor routes are inconsistent.
            /// </exception>
            internal bool ChangeNamespace(
                int namespaceIndex,
                IAsyncNodeManager nodeManager,
                bool register,
                bool visible)
            {
                lock (m_owner.m_lock)
                {
                    if (ConflictsWithNamespace(namespaceIndex, nodeManager))
                    {
                        throw new InvalidOperationException("The namespace mutation conflicts with this publication.");
                    }
                    RoutingSnapshot? serving = register
                        ? PrepareNamespaceRegistration(m_previous, namespaceIndex, nodeManager, visible)
                        : PrepareNamespaceUnregistration(m_previous, namespaceIndex, nodeManager, null);
                    if (serving is null)
                    {
                        return false;
                    }
                    RoutingSnapshot successor;
                    try
                    {
                        successor = (register
                            ? PrepareNamespaceRegistration(m_next, namespaceIndex, nodeManager, visible)
                            : PrepareNamespaceUnregistration(m_next, namespaceIndex, nodeManager, null)) ??
                            throw new InvalidOperationException("The reserved namespace routes are inconsistent.");
                    }
                    catch
                    {
                        serving.Epoch.Close();
                        throw;
                    }
                    try
                    {
                        ValidatePublicationTransitions(m_previous, serving);
                        ValidatePublicationTransitions(m_previous, successor);
                    }
                    catch
                    {
                        serving.Epoch.Close();
                        successor.Epoch.Close();
                        throw;
                    }
                    RoutingSnapshot replaced = m_next;
                    m_owner.PublishSnapshot(serving);
                    m_previous = serving;
                    m_next = successor;
                    replaced.Epoch.Close();
                    return true;
                }
            }

            /// <summary>
            /// Attempts reservation without treating a competing publication or stale image as cleanup failure.
            /// </summary>
            internal bool TryReserve(out Task ready)
            {
                lock (m_owner.m_lock)
                {
                    if (m_disposed || m_published || ReferenceEquals(m_owner.m_reservedRoutes, this))
                    {
                        throw new InvalidOperationException("The routing publication is no longer preparable.");
                    }
                    if (m_owner.m_reservedRoutes is { } pending)
                    {
                        ready = pending.WhenReleased;
                        return false;
                    }
                    ready = Task.CompletedTask;
                    if (!ReferenceEquals(m_owner.m_snapshot, m_previous))
                    {
                        return false;
                    }
                    ValidatePublicationTransitions(m_previous, m_next);
                    m_owner.m_reservedRoutes = this;
                    return true;
                }
            }

            /// <summary>
            /// Publishes the prepared snapshot. Repeated calls have no effect.
            /// </summary>
            public bool Publish()
            {
                lock (m_owner.m_lock)
                {
                    if (m_published)
                    {
                        return m_historianRegistrationsChanged;
                    }
                    if (m_disposed || !ReferenceEquals(m_owner.m_reservedRoutes, this))
                    {
                        throw new InvalidOperationException("The routing publication has not been reserved.");
                    }
                    m_historianRegistrationsChanged = m_owner.PublishSnapshot(m_next);
                    m_published = true;
                    return m_historianRegistrationsChanged;
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                TaskCompletionSource<bool>? released = null;
                try
                {
                    lock (m_owner.m_lock)
                    {
                        if (m_disposed)
                        {
                            return;
                        }
                        try
                        {
                            if (!m_published)
                            {
                                m_next.Epoch.Close();
                            }
                        }
                        finally
                        {
                            if (ReferenceEquals(m_owner.m_reservedRoutes, this))
                            {
                                m_owner.m_reservedRoutes = null;
                            }
                            m_disposed = true;
                            released = m_released;
                        }
                    }
                }
                finally
                {
                    released?.TrySetResult(true);
                }
            }

            /// <summary>
            /// Prevents alias reconciliation from accepting stale work or another publication's reservation.
            /// </summary>
            /// <exception cref="InvalidOperationException">
            /// This publication is disposed, already published, not the current reservation,
            /// or based on a stale routing snapshot.
            /// </exception>
            private void ValidateNamespaceMutation()
            {
                if (m_disposed ||
                    m_published ||
                    !ReferenceEquals(m_owner.m_reservedRoutes, this) ||
                    !ReferenceEquals(m_owner.m_snapshot, m_previous))
                {
                    throw new InvalidOperationException("The routing publication has not been reserved.");
                }
            }

            private readonly NodeManagerRoutingTable m_owner;

            /// <summary>The serving image, advanced only by permitted mutations under this exact reservation.</summary>
            private RoutingSnapshot m_previous;

            /// <summary>
            /// The preallocated successor containing the batch and every completed permitted alias mutation.
            /// </summary>
            private RoutingSnapshot m_next;

            /// <summary>
            /// Namespace routes changed by the prepared transaction, never by permitted alias mutations.
            /// </summary>
            private readonly HashSet<int> m_reservedNamespaces;

            /// <summary>Owners whose lifetime or visibility changes in the prepared transaction.</summary>
            private readonly IAsyncNodeManager[] m_changingManagers;
            private bool m_published;
            private bool m_disposed;
            private bool m_historianRegistrationsChanged;

            /// <summary>Signals completion only when another cleanup actually encounters this reservation.</summary>
            private TaskCompletionSource<bool>? m_released;
        }
    }
}
