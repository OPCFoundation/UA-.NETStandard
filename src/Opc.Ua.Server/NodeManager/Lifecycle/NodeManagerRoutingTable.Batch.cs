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
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Opc.Ua.Server
{
    internal sealed partial class NodeManagerRoutingTable
    {
        internal RoutingSnapshot Revision => Volatile.Read(ref m_snapshot);

        internal RoutingSnapshot CapturedRevision => ReadSnapshot;

        internal TypeTable? TypeTree => m_preparedTypes.Value ?? ReadSnapshot.TypeTree;

        internal EncodeableFactory? Factory => m_preparedFactory.Value ?? ReadSnapshot.Factory;

        private RoutingSnapshot ReadSnapshot => m_readSnapshot.Value ?? Volatile.Read(ref m_snapshot);

        internal ReadScope Capture(RoutingSnapshot? snapshot = null)
        {
            RoutingSnapshot? previous = m_readSnapshot.Value;
            m_readSnapshot.Value = snapshot ?? previous ?? Volatile.Read(ref m_snapshot);
            return new ReadScope(this, previous);
        }

        internal ReadScope UseLiveRouting()
        {
            RoutingSnapshot? previous = m_readSnapshot.Value;
            m_readSnapshot.Value = null;
            return new ReadScope(this, previous);
        }

        internal IDisposable UseTypeImage(TypeTable typeTree, EncodeableFactory factory)
        {
            TypeTable? previous = m_preparedTypes.Value;
            EncodeableFactory? previousFactory = m_preparedFactory.Value;
            m_preparedTypes.Value = typeTree;
            m_preparedFactory.Value = factory;
            return new TypeScope(this, previous, previousFactory);
        }

        internal PreparedRoutes PrepareBatch(
            ArrayOf<PreparedNodeManager> candidates,
            ArrayOf<IAsyncNodeManager> removed,
            RoutingSnapshot expectedRevision,
            Func<IAsyncNodeManager, IEnumerable<int>> resolveNamespaces,
            TypeTable typeTree,
            EncodeableFactory factory,
            IReadOnlyDictionary<NodeState, NodeState.ReferenceSnapshot>? references = null)
        {
            lock (m_lock)
            {
                RoutingSnapshot current = m_snapshot;
                if (!ReferenceEquals(current, expectedRevision))
                {
                    throw new InvalidOperationException("Routing changed after the batch was prepared.");
                }

                HashSet<IAsyncNodeManager> removals = [.. removed];
                HashSet<IAsyncNodeManager> publishing = [];
                var replacements = new Dictionary<IAsyncNodeManager, IAsyncNodeManager>();
                foreach (PreparedNodeManager candidate in candidates)
                {
                    publishing.Add(candidate.NodeManager);
                    if (candidate.ReplacedNodeManager is { } replaced)
                    {
                        removals.Add(replaced);
                        replacements.Add(replaced, candidate.NodeManager);
                    }
                    if (current.NodeManagers.Contains(candidate.NodeManager))
                    {
                        throw new InvalidOperationException("A batch candidate is already registered.");
                    }
                }
                foreach (IAsyncNodeManager removedManager in removals)
                {
                    if (Array.IndexOf(current.NodeManagers, removedManager) < 2)
                    {
                        throw new InvalidOperationException("A batch retirement is not lifecycle-owned.");
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
                }
                foreach (PreparedNodeManager candidate in candidates)
                {
                    foreach (int index in resolveNamespaces(candidate.NodeManager).Distinct())
                    {
                        routes.TryGetValue(index, out IReadOnlyList<IAsyncNodeManager>? existing);
                        routes[index] = existing is null
                            ? [candidate.NodeManager]
                            : [.. existing.Where(manager => !AreSameManager(manager, candidate.NodeManager)),
                                candidate.NodeManager];
                    }
                }
                foreach (int index in routes.Keys.ToArray())
                {
                    routes[index] = [.. routes[index].OrderBy(manager => managers.IndexOf(manager))];
                }
                var next = new RoutingSnapshot(
                    [.. managers],
                    routes,
                    [
                        .. current.HiddenNodeManagers.Where(manager =>
                            !removals.Contains(manager) &&
                            !publishing.Any(candidate => AreSameManager(candidate, manager)))
                    ],
                    typeTree,
                    factory,
                    references ?? current.References);
                return new PreparedRoutes(this, current, next);
            }
        }

        internal NodeState.ReferenceUpdate PrepareReferences(
            IAsyncNodeManager owner,
            NodeState node,
            ArrayOf<IReference> additions,
            ArrayOf<IReference> removals,
            Dictionary<NodeState, NodeState.ReferenceSnapshot> next)
        {
            lock (m_lock)
            {
                if (m_referenceOwners.TryGetValue(node, out IAsyncNodeManager? existing) &&
                    !AreSameManager(existing, owner))
                {
                    throw new NotSupportedException("A reference image cannot have multiple NodeManager owners.");
                }
                NodeState.ReferenceUpdate update = node.PrepareReferences(SelectReferences, additions, removals);
                m_referenceOwners[node] = owner;
                next[node] = update.Next;
                return update;
            }
        }

        internal void ReleaseReferences(IAsyncNodeManager owner)
        {
            lock (m_lock)
            {
                NodeState[] nodes =
                [
                    .. m_referenceOwners.Where(entry => AreSameManager(entry.Value, owner)).Select(entry => entry.Key)
                ];
                if (nodes.Length == 0)
                {
                    return;
                }
                Dictionary<NodeState, NodeState.ReferenceSnapshot> references =
                    m_snapshot.References.ToDictionary(entry => entry.Key, entry => entry.Value);
                bool changed = false;
                foreach (NodeState node in nodes)
                {
                    references.TryGetValue(node, out NodeState.ReferenceSnapshot? image);
                    node.ReleaseReferenceView(SelectReferences, image);
                    changed |= references.Remove(node);
                    m_referenceOwners.Remove(node);
                }
                if (changed)
                {
                    m_snapshot = new RoutingSnapshot(
                        m_snapshot.NodeManagers, m_snapshot.NamespaceManagers, m_snapshot.HiddenNodeManagers,
                        m_snapshot.TypeTree, m_snapshot.Factory, references);
                }
            }
        }

        private NodeState.ReferenceSnapshot? SelectReferences(NodeState node)
        {
            return ReadSnapshot.References.TryGetValue(node, out NodeState.ReferenceSnapshot? image) ? image : null;
        }

        private readonly AsyncLocal<RoutingSnapshot?> m_readSnapshot = new();
        private readonly AsyncLocal<TypeTable?> m_preparedTypes = new();
        private readonly AsyncLocal<EncodeableFactory?> m_preparedFactory = new();
        private readonly Dictionary<NodeState, IAsyncNodeManager> m_referenceOwners = [];

        private sealed class TypeScope(
            NodeManagerRoutingTable owner,
            TypeTable? previous,
            EncodeableFactory? previousFactory) : IDisposable
        {
            public void Dispose()
            {
                NodeManagerRoutingTable? current = Interlocked.Exchange(ref m_owner, null);
                if (current is not null)
                {
                    current.m_preparedTypes.Value = previous;
                    current.m_preparedFactory.Value = previousFactory;
                }
            }

            private NodeManagerRoutingTable? m_owner = owner;
        }

        internal sealed class ReadScope : IDisposable
        {
            internal ReadScope(NodeManagerRoutingTable owner, RoutingSnapshot? previous)
            {
                m_owner = owner;
                m_previous = previous;
            }

            public void Dispose()
            {
                NodeManagerRoutingTable? owner = Interlocked.Exchange(ref m_owner, null);
                if (owner is not null)
                {
                    owner.m_readSnapshot.Value = m_previous;
                }
            }

            private NodeManagerRoutingTable? m_owner;
            private readonly RoutingSnapshot? m_previous;
        }

        internal sealed class PreparedRoutes
        {
            internal PreparedRoutes(
                NodeManagerRoutingTable owner,
                RoutingSnapshot previous,
                RoutingSnapshot next)
            {
                m_owner = owner;
                m_previous = previous;
                m_next = next;
            }

            public void Validate()
            {
                if (!ReferenceEquals(Volatile.Read(ref m_owner.m_snapshot), m_previous))
                {
                    throw new InvalidOperationException("Routing changed during batch preparation.");
                }
            }

            public void Publish()
            {
                Volatile.Write(ref m_owner.m_snapshot, m_next);
            }

            private readonly NodeManagerRoutingTable m_owner;
            private readonly RoutingSnapshot m_previous;
            private readonly RoutingSnapshot m_next;
        }
    }
}
