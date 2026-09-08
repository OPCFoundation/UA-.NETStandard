/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Routes Nodes to the NodeManagers that own them and lets NodeManagers be added, replaced,
    /// and removed while the server is running.
    /// <para>
    /// Readers never take the lock. Every mutation builds a complete new
    /// <c>RoutingSnapshot</c> under the lock and publishes it with a single volatile write, so a
    /// service call in flight always observes either the state before or the state after a
    /// lifecycle operation, never a half updated table.
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
        /// Gets the number of registered NodeManagers, including hidden ones.
        /// </summary>
        public int Count => Volatile.Read(ref m_snapshot).NodeManagers.Length;

        /// <summary>
        /// Gets the registered NodeManager at the given position, including hidden ones.
        /// </summary>
        /// <param name="index">The position of the NodeManager.</param>
        public IAsyncNodeManager this[int index]
            => Volatile.Read(ref m_snapshot).NodeManagers[index];

        /// <summary>
        /// Gets the NodeManagers that serve each namespace index, excluding hidden ones.
        /// </summary>
        public IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> NamespaceManagers
            => Volatile.Read(ref m_snapshot).VisibleNamespaceManagers;

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
                RoutingSnapshot snapshot = m_snapshot;
                m_snapshot = new RoutingSnapshot(
                    [.. snapshot.NodeManagers, nodeManager],
                    snapshot.NamespaceManagers,
                    snapshot.HiddenNodeManagers);
            }
        }

        /// <summary>
        /// Publishes the namespace routes that were built during server startup.
        /// </summary>
        /// <param name="namespaceManagers">The NodeManagers that serve each namespace index.</param>
        /// <exception cref="ArgumentNullException"><paramref name="namespaceManagers"/> is <c>null</c>.</exception>
        public void Initialize(
            IReadOnlyDictionary<int, List<IAsyncNodeManager>> namespaceManagers)
        {
            if (namespaceManagers is null)
            {
                throw new ArgumentNullException(nameof(namespaceManagers));
            }

            lock (m_lock)
            {
                m_snapshot = new RoutingSnapshot(
                    m_snapshot.NodeManagers,
                    namespaceManagers.ToDictionary(
                        entry => entry.Key,
                        entry => (IReadOnlyList<IAsyncNodeManager>)[.. entry.Value]),
                    m_snapshot.HiddenNodeManagers);
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
                RoutingSnapshot snapshot = m_snapshot;
                if (IndexOf(snapshot.NodeManagers, nodeManager) >= 0)
                {
                    throw new InvalidOperationException("The NodeManager is already registered.");
                }

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
                        AreSameManager(manager, nodeManager)))
                    {
                        routes[namespaceIndex] = [.. existing, nodeManager];
                    }
                }

                IAsyncNodeManager[] hiddenNodeManagers =
                [
                    .. snapshot.HiddenNodeManagers.Where(manager =>
                        !AreSameManager(manager, nodeManager))
                ];
                if (!visible)
                {
                    hiddenNodeManagers =
                    [
                        .. hiddenNodeManagers,
                        nodeManager
                    ];
                }

                m_snapshot = new RoutingSnapshot(
                    [.. snapshot.NodeManagers, nodeManager],
                    routes,
                    hiddenNodeManagers);
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
                RoutingSnapshot snapshot = m_snapshot;
                int managerIndex = IndexOf(snapshot.NodeManagers, current);
                if (managerIndex < 2)
                {
                    throw new InvalidOperationException(
                        "Only lifecycle-managed NodeManagers can be replaced.");
                }
                if (IndexOf(snapshot.NodeManagers, replacement) >= 0)
                {
                    throw new InvalidOperationException(
                        "The replacement NodeManager is already registered.");
                }

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
                        AreSameManager(manager, replacement)))
                    {
                        replacementNamespaceSet.Add(route.Key);
                    }
                }
                foreach (int namespaceIndex in routes.Keys.ToArray())
                {
                    IReadOnlyList<IAsyncNodeManager> existing = routes[namespaceIndex];
                    var updated = existing
                        .Where(manager => !AreSameManager(
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
                        !AreSameManager(manager, current) &&
                        !AreSameManager(manager, replacement))
                ];
                if (!replacementVisible)
                {
                    hiddenNodeManagers =
                    [
                        .. hiddenNodeManagers,
                        replacement
                    ];
                }

                m_snapshot = new RoutingSnapshot(
                    managers,
                    routes,
                    hiddenNodeManagers);
                return currentPosition;
            }
        }

        /// <summary>
        /// Atomically removes a replacement and restores the previous NodeManager
        /// to its captured global and namespace-route positions.
        /// </summary>
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
                RoutingSnapshot snapshot = m_snapshot;
                int replacementIndex = IndexOf(
                    snapshot.NodeManagers,
                    replacement);
                if (replacementIndex < 2)
                {
                    throw new InvalidOperationException(
                        "Only lifecycle-managed NodeManagers can be restored.");
                }
                if (IndexOf(snapshot.NodeManagers, previous) >= 0)
                {
                    throw new InvalidOperationException(
                        "The previous NodeManager is already registered.");
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
                            !AreSameManager(manager, replacement))
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
                        !AreSameManager(manager, previous)).ToList() ?? [];
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
                        !AreSameManager(manager, replacement) &&
                        !AreSameManager(manager, previous))
                ];
                if (!visible)
                {
                    hiddenNodeManagers =
                    [
                        .. hiddenNodeManagers,
                        previous
                    ];
                }

                m_snapshot = new RoutingSnapshot(
                    [.. managers],
                    routes,
                    hiddenNodeManagers);
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
                RoutingSnapshot snapshot = m_snapshot;
                int managerIndex = IndexOf(snapshot.NodeManagers, nodeManager);
                if (managerIndex < 2)
                {
                    throw new InvalidOperationException(
                        "Only lifecycle-managed NodeManagers can be removed.");
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
                        AreSameManager(manager, registeredManager));
                    if (updated.Count == 0)
                    {
                        routes.Remove(namespaceIndex);
                    }
                    else
                    {
                        routes[namespaceIndex] = [.. updated];
                    }
                }

                m_snapshot = new RoutingSnapshot(
                    [.. managers],
                    routes,
                    [
                        .. snapshot.HiddenNodeManagers.Where(manager =>
                            !AreSameManager(manager, registeredManager))
                    ]);
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
            if (managerIndex < 2)
            {
                throw new InvalidOperationException(
                    "Only lifecycle-managed NodeManagers have a restorable position.");
            }

            return CreatePosition(
                snapshot,
                managerIndex,
                snapshot.NodeManagers[managerIndex]);
        }

        /// <summary>
        /// Restores a previously removed NodeManager to its captured routing positions.
        /// </summary>
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
                RoutingSnapshot snapshot = m_snapshot;
                if (IndexOf(snapshot.NodeManagers, nodeManager) >= 0)
                {
                    throw new InvalidOperationException(
                        "The NodeManager is already registered.");
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
                        !AreSameManager(manager, nodeManager)).ToList() ?? [];
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
                        !AreSameManager(manager, nodeManager))
                ];
                if (!(visible ?? position.WasVisible))
                {
                    hiddenNodeManagers =
                    [
                        .. hiddenNodeManagers,
                        nodeManager
                    ];
                }

                m_snapshot = new RoutingSnapshot(
                    [.. managers],
                    routes,
                    hiddenNodeManagers);
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
                RoutingSnapshot snapshot = m_snapshot;
                Dictionary<int, IReadOnlyList<IAsyncNodeManager>> routes =
                    CopyRoutes(snapshot.NamespaceManagers);
                routes.TryGetValue(
                    namespaceIndex,
                    out IReadOnlyList<IAsyncNodeManager>? existing);
                int managerIndex = IndexOf(snapshot.NodeManagers, nodeManager);
                IAsyncNodeManager registeredManager = managerIndex >= 0
                    ? snapshot.NodeManagers[managerIndex]
                    : nodeManager;
                if (existing?.Any(manager =>
                    AreSameManager(manager, registeredManager)) == true)
                {
                    return;
                }
                routes[namespaceIndex] = existing is null
                    ? [registeredManager]
                    : [.. existing, registeredManager];
                IAsyncNodeManager[] hiddenNodeManagers =
                [
                    .. snapshot.HiddenNodeManagers.Where(manager =>
                        !AreSameManager(manager, registeredManager))
                ];
                if (!visible)
                {
                    hiddenNodeManagers =
                    [
                        .. hiddenNodeManagers,
                        registeredManager
                    ];
                }

                m_snapshot = new RoutingSnapshot(
                    snapshot.NodeManagers,
                    routes,
                    hiddenNodeManagers);
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
            INodeManager? nodeManager)
        {
            lock (m_lock)
            {
                RoutingSnapshot snapshot = m_snapshot;
                if (!snapshot.NamespaceManagers.TryGetValue(
                    namespaceIndex,
                    out IReadOnlyList<IAsyncNodeManager>? existing))
                {
                    return false;
                }

                var updated = existing.ToList();
                int removed = updated.RemoveAll(manager =>
                    asyncNodeManager is not null
                        ? AreSameManager(manager, asyncNodeManager)
                        : manager.SyncNodeManager is { } syncNodeManager &&
                            ReferenceEquals(syncNodeManager, nodeManager));
                if (removed == 0)
                {
                    return false;
                }

                Dictionary<int, IReadOnlyList<IAsyncNodeManager>> routes =
                    CopyRoutes(snapshot.NamespaceManagers);
                if (updated.Count == 0)
                {
                    routes.Remove(namespaceIndex);
                }
                else
                {
                    routes[namespaceIndex] = [.. updated];
                }
                m_snapshot = new RoutingSnapshot(
                    snapshot.NodeManagers,
                    routes,
                    snapshot.HiddenNodeManagers);
                return true;
            }
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
                RoutingSnapshot snapshot = m_snapshot;
                Dictionary<int, IReadOnlyList<IAsyncNodeManager>> routes =
                    CopyRoutes(snapshot.NamespaceManagers);
                foreach (int namespaceIndex in routes.Keys.ToArray())
                {
                    var updated = routes[namespaceIndex].ToList();
                    updated.RemoveAll(manager =>
                        AreSameManager(manager, nodeManager));
                    if (updated.Count == 0)
                    {
                        routes.Remove(namespaceIndex);
                    }
                    else
                    {
                        routes[namespaceIndex] = [.. updated];
                    }
                }

                m_snapshot = new RoutingSnapshot(
                    snapshot.NodeManagers,
                    routes,
                    [
                        .. snapshot.HiddenNodeManagers.Where(manager =>
                            !AreSameManager(manager, nodeManager))
                    ]);
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
                AreSameManager(manager, nodeManager)) &&
                !snapshot.HiddenNodeManagers.Any(manager =>
                    AreSameManager(manager, nodeManager));
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
                AreSameManager(manager, nodeManager));
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
                        !AreSameManager(manager, registeredManager))
                ];
                if (!visible)
                {
                    hiddenNodeManagers =
                    [
                        .. hiddenNodeManagers,
                        registeredManager
                    ];
                }

                m_snapshot = new RoutingSnapshot(
                    snapshot.NodeManagers,
                    snapshot.NamespaceManagers,
                    hiddenNodeManagers);
            }
        }

        /// <summary>
        /// Removes every NodeManager and route, which happens when the server shuts down.
        /// </summary>
        public void Clear()
        {
            lock (m_lock)
            {
                m_snapshot = RoutingSnapshot.Empty;
            }
        }

        /// <summary>
        /// Returns an enumerator over the NodeManagers that are reachable by Clients. The
        /// enumerator walks a snapshot, so it is unaffected by concurrent lifecycle operations.
        /// </summary>
        public IEnumerator<IAsyncNodeManager> GetEnumerator()
        {
            IAsyncNodeManager[] nodeManagers =
                Volatile.Read(ref m_snapshot).VisibleNodeManagers;
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
                if (AreSameManager(managers[ii], manager))
                {
                    return ii;
                }
            }
            return -1;
        }

        /// <summary>
        /// Gets whether two entries denote the same NodeManager. Two adapters that wrap the same
        /// synchronous NodeManager count as the same NodeManager.
        /// </summary>
        private static bool AreSameManager(
            IAsyncNodeManager left,
            IAsyncNodeManager right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }
            INodeManager? leftSyncNodeManager = left.SyncNodeManager;
            INodeManager? rightSyncNodeManager = right.SyncNodeManager;
            return leftSyncNodeManager is not null &&
                rightSyncNodeManager is not null &&
                ReferenceEquals(
                    leftSyncNodeManager,
                    rightSyncNodeManager);
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
                    AreSameManager(manager, registeredManager)));
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

        /// <summary>
        /// An immutable view of the routing table. Every mutation publishes a new instance, which
        /// is what allows readers to work without locking.
        /// </summary>
        private sealed class RoutingSnapshot
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="RoutingSnapshot"/> class and
            /// precomputes the views that exclude hidden NodeManagers.
            /// </summary>
            /// <param name="nodeManagers">All registered NodeManagers, in dispatch order.</param>
            /// <param name="namespaceManagers">The NodeManagers serving each namespace index.</param>
            /// <param name="hiddenNodeManagers">The NodeManagers not yet reachable by Clients.</param>
            public RoutingSnapshot(
                IAsyncNodeManager[] nodeManagers,
                IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> namespaceManagers,
                IAsyncNodeManager[] hiddenNodeManagers)
            {
                NodeManagers = nodeManagers;
                NamespaceManagers = namespaceManagers;
                HiddenNodeManagers = hiddenNodeManagers;
                VisibleNodeManagers =
                [
                    .. nodeManagers.Where(manager =>
                        !hiddenNodeManagers.Any(hidden =>
                            ReferenceEquals(hidden, manager)))
                ];
                VisibleNamespaceManagers =
                    CreateVisibleNamespaceManagers(
                        namespaceManagers,
                        hiddenNodeManagers);
            }

            /// <summary>
            /// Gets the snapshot of a routing table without any NodeManager.
            /// </summary>
            public static RoutingSnapshot Empty { get; } = new(
                [],
                new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>(),
                []);

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
    }
}
