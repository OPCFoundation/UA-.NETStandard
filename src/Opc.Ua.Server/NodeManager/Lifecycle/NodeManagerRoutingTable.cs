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
            => Volatile.Read(ref m_snapshot).NamespaceManagers;

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
                    snapshot.NamespaceManagers);
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
                        entry => (IReadOnlyList<IAsyncNodeManager>)[.. entry.Value]));
            }
        }

        public void Add(
            IAsyncNodeManager nodeManager,
            IEnumerable<int> namespaceIndexes)
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
                    routes[namespaceIndex] = existing is null
                        ? [nodeManager]
                        : [.. existing, nodeManager];
                }

                m_snapshot = new RoutingSnapshot(
                    [.. snapshot.NodeManagers, nodeManager],
                    routes);
            }
        }

        public void Replace(
            IAsyncNodeManager current,
            IAsyncNodeManager replacement,
            IEnumerable<int> replacementNamespaceIndexes)
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
                foreach (int namespaceIndex in routes.Keys.ToArray())
                {
                    IReadOnlyList<IAsyncNodeManager> existing = routes[namespaceIndex];
                    int routeIndex = IndexOf(existing, current);
                    if (routeIndex < 0)
                    {
                        continue;
                    }

                    var updated = existing.ToList();
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

                m_snapshot = new RoutingSnapshot(managers, routes);
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

                m_snapshot = new RoutingSnapshot([.. managers], routes);
            }
        }

        public void RegisterNamespace(int namespaceIndex, IAsyncNodeManager nodeManager)
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
                m_snapshot = new RoutingSnapshot(snapshot.NodeManagers, routes);
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
                        : ReferenceEquals(manager.SyncNodeManager, nodeManager));
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
                m_snapshot = new RoutingSnapshot(snapshot.NodeManagers, routes);
                return true;
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
            return ((IEnumerable<IAsyncNodeManager>)
                Volatile.Read(ref m_snapshot).NodeManagers).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private static int IndexOf(
            IReadOnlyList<IAsyncNodeManager> managers,
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
                IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> namespaceManagers)
            {
                NodeManagers = nodeManagers;
                NamespaceManagers = namespaceManagers;
            }

            public static RoutingSnapshot Empty { get; } = new(
                [],
                new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>());

            public IAsyncNodeManager[] NodeManagers { get; }

            public IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> NamespaceManagers { get; }
        }
    }
}
