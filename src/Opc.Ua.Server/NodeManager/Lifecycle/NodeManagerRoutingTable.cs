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
    internal sealed class NodeManagerRoutingTable : IReadOnlyList<IAsyncNodeManager>
    {
        public int Count => Volatile.Read(ref m_snapshot).NodeManagers.Length;

        public IAsyncNodeManager this[int index]
            => Volatile.Read(ref m_snapshot).NodeManagers[index];

        public IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> NamespaceManagers
            => Volatile.Read(ref m_snapshot).VisibleNamespaceManagers;

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
                if (Array.IndexOf(snapshot.NodeManagers, nodeManager) >= 0)
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
                        !ReferenceEquals(manager, nodeManager))
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

        public void Replace(
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
                int managerIndex = Array.IndexOf(snapshot.NodeManagers, current);
                if (managerIndex < 2)
                {
                    throw new InvalidOperationException(
                        "Only lifecycle-managed NodeManagers can be replaced.");
                }
                if (Array.IndexOf(snapshot.NodeManagers, replacement) >= 0)
                {
                    throw new InvalidOperationException(
                        "The replacement NodeManager is already registered.");
                }

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
                        !ReferenceEquals(manager, current) &&
                        !ReferenceEquals(manager, replacement))
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
            }
        }

        public void Remove(IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            lock (m_lock)
            {
                RoutingSnapshot snapshot = m_snapshot;
                int managerIndex = Array.IndexOf(snapshot.NodeManagers, nodeManager);
                if (managerIndex < 2)
                {
                    throw new InvalidOperationException(
                        "Only lifecycle-managed NodeManagers can be removed.");
                }

                List<IAsyncNodeManager> managers = [.. snapshot.NodeManagers];
                managers.RemoveAt(managerIndex);
                Dictionary<int, IReadOnlyList<IAsyncNodeManager>> routes =
                    CopyRoutes(snapshot.NamespaceManagers);

                foreach (int namespaceIndex in routes.Keys.ToArray())
                {
                    var updated = routes[namespaceIndex].ToList();
                    updated.RemoveAll(manager => ReferenceEquals(manager, nodeManager));
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
                            !ReferenceEquals(manager, nodeManager))
                    ]);
            }
        }

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
                if (existing?.Any(manager => ReferenceEquals(manager, nodeManager)) == true)
                {
                    return;
                }
                routes[namespaceIndex] = existing is null
                    ? [nodeManager]
                    : [.. existing, nodeManager];
                IAsyncNodeManager[] hiddenNodeManagers =
                [
                    .. snapshot.HiddenNodeManagers.Where(manager =>
                        !ReferenceEquals(manager, nodeManager))
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
                    snapshot.NodeManagers,
                    routes,
                    hiddenNodeManagers);
            }
        }

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
                        ? ReferenceEquals(manager, asyncNodeManager)
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

        public bool IsVisible(IAsyncNodeManager nodeManager)
        {
            RoutingSnapshot snapshot = Volatile.Read(ref m_snapshot);
            return snapshot.NodeManagers.Any(manager =>
                ReferenceEquals(manager, nodeManager)) &&
                !snapshot.HiddenNodeManagers.Any(manager =>
                    ReferenceEquals(manager, nodeManager));
        }

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
                if (!snapshot.NodeManagers.Any(manager =>
                    ReferenceEquals(manager, nodeManager)))
                {
                    throw new InvalidOperationException(
                        "The NodeManager is not registered.");
                }

                IAsyncNodeManager[] hiddenNodeManagers =
                [
                    .. snapshot.HiddenNodeManagers.Where(manager =>
                        !ReferenceEquals(manager, nodeManager))
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
                    snapshot.NodeManagers,
                    snapshot.NamespaceManagers,
                    hiddenNodeManagers);
            }
        }

        public void Clear()
        {
            lock (m_lock)
            {
                m_snapshot = RoutingSnapshot.Empty;
            }
        }

        public IEnumerator<IAsyncNodeManager> GetEnumerator()
        {
            IAsyncNodeManager[] nodeManagers =
                Volatile.Read(ref m_snapshot).VisibleNodeManagers;
            return ((IEnumerable<IAsyncNodeManager>)nodeManagers).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private static int IndexOf(
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

        private static Dictionary<int, IReadOnlyList<IAsyncNodeManager>> CopyRoutes(
            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> routes)
        {
            return routes.ToDictionary(entry => entry.Key, entry => entry.Value);
        }

        private readonly Lock m_lock = new();
        private RoutingSnapshot m_snapshot = RoutingSnapshot.Empty;

        private sealed class RoutingSnapshot
        {
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

            public static RoutingSnapshot Empty { get; } = new(
                [],
                new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>(),
                []);

            public IAsyncNodeManager[] NodeManagers { get; }

            public IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> NamespaceManagers { get; }

            public IAsyncNodeManager[] HiddenNodeManagers { get; }

            public IAsyncNodeManager[] VisibleNodeManagers { get; }

            public IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>>
                VisibleNamespaceManagers
            { get; }

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
    }
}
