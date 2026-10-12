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
using System.Runtime.CompilerServices;
using System.Threading;

namespace Opc.Ua.Server
{
    internal sealed partial class NodeManagerRoutingTable
    {
        /// <summary>
        /// Gets the current published routing revision.
        /// </summary>
        internal RoutingSnapshot Revision => Volatile.Read(ref m_snapshot);

        /// <summary>
        /// Gets the routing revision captured by the current read scope, or the live revision.
        /// </summary>
        internal RoutingSnapshot CapturedRevision => ReadSnapshot;

        /// <summary>
        /// Gets the type tree of the prepared image in use, or the one captured with the routing revision.
        /// </summary>
        internal TypeTable? TypeTree => m_preparedTypes.Value ?? ReadSnapshot.TypeTree;

        /// <summary>
        /// Gets the encodeable factory of the prepared image in use, or the one captured with the routing revision.
        /// </summary>
        internal EncodeableFactory? Factory => m_preparedFactory.Value ?? ReadSnapshot.Factory;

        /// <summary>
        /// Gets the snapshot captured by the current read scope, or the live snapshot.
        /// </summary>
        private RoutingSnapshot ReadSnapshot => m_readSnapshot.Value ?? Volatile.Read(ref m_snapshot);

        /// <summary>
        /// Gets the read image published for a node manager in the active snapshot.
        /// </summary>
        /// <param name="owner">The node manager that owns the image.</param>
        /// <returns>The read image, or null if the owner has none.</returns>
        internal INodeManagerReadImage? GetReadImage(IAsyncNodeManager owner)
        {
            return ReadSnapshot.ReadImages.TryGetValue(owner, out INodeManagerReadImage? image) ? image : null;
        }

        /// <summary>
        /// Captures a routing snapshot for the current asynchronous flow until the scope is disposed.
        /// </summary>
        /// <param name="snapshot">The snapshot to capture, or null to capture the active or live one.</param>
        /// <returns>The scope that restores the previous snapshot when disposed.</returns>
        internal ReadScope Capture(RoutingSnapshot? snapshot = null)
        {
            RoutingSnapshot? previous = m_readSnapshot.Value;
            m_readSnapshot.Value = snapshot ?? previous ?? Volatile.Read(ref m_snapshot);
            return new ReadScope(this, previous);
        }

        /// <summary>
        /// Reads the live routing in the current asynchronous flow until the scope is disposed.
        /// </summary>
        /// <returns>The scope that restores the previous snapshot when disposed.</returns>
        internal ReadScope UseLiveRouting()
        {
            RoutingSnapshot? previous = m_readSnapshot.Value;
            m_readSnapshot.Value = null;
            return new ReadScope(this, previous);
        }

        /// <summary>
        /// Uses privately prepared type tree and factory images in the current asynchronous flow until disposed.
        /// </summary>
        /// <param name="typeTree">The prepared type tree.</param>
        /// <param name="factory">The prepared encodeable factory.</param>
        /// <returns>The scope that restores the previous images when disposed.</returns>
        internal IDisposable UseTypeImage(TypeTable typeTree, EncodeableFactory factory)
        {
            TypeTable? previous = m_preparedTypes.Value;
            EncodeableFactory? previousFactory = m_preparedFactory.Value;
            m_preparedTypes.Value = typeTree;
            m_preparedFactory.Value = factory;
            return new TypeScope(this, previous, previousFactory);
        }

        /// <summary>
        /// Builds the routing image that results from adding, replacing and removing node managers.
        /// </summary>
        /// <param name="candidates">The prepared node managers to add or replace.</param>
        /// <param name="removed">The node managers to retire.</param>
        /// <param name="expectedRevision">The revision the batch was prepared against.</param>
        /// <param name="resolveNamespaces">Resolves the namespace indexes served by a node manager.</param>
        /// <param name="typeTree">The type tree to publish.</param>
        /// <param name="factory">The encodeable factory to publish.</param>
        /// <param name="references">The reference images to publish, or null to keep the current ones.</param>
        /// <param name="readImages">The read images to publish.</param>
        /// <returns>The prepared routes that can be reserved and published.</returns>
        internal PreparedRoutes PrepareBatch(
            ArrayOf<PreparedNodeManager> candidates,
            ArrayOf<IAsyncNodeManager> removed,
            RoutingSnapshot expectedRevision,
            Func<IAsyncNodeManager, IEnumerable<int>> resolveNamespaces,
            TypeTable typeTree,
            EncodeableFactory factory,
            IReadOnlyDictionary<NodeState, NodeState.ReferenceSnapshot>? references = null,
            ArrayOf<INodeManagerReadImage> readImages = default)
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
                Dictionary<IAsyncNodeManager, INodeManagerReadImage> images =
                    current.ReadImages.ToDictionary(
                        entry => entry.Key, entry => entry.Value, ReadImageOwnerComparer.Instance);
                var imageOwners = new HashSet<IAsyncNodeManager>(ReadImageOwnerComparer.Instance);
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
                            !publishing.Any(candidate => AreSameManager(candidate, manager)))
                    ],
                    typeTree,
                    factory,
                    references ?? current.References,
                    images);
                return new PreparedRoutes(this, current, next);
            }
        }

        /// <summary>
        /// Prepares the reference image of a node that receives external references from a node manager.
        /// </summary>
        /// <param name="owner">The node manager that owns the references.</param>
        /// <param name="node">The node that receives the references.</param>
        /// <param name="additions">The references to add.</param>
        /// <param name="removals">The references to remove.</param>
        /// <param name="next">Receives the prepared reference snapshot of the node.</param>
        /// <returns>The reference update to reserve, complete or dispose.</returns>
        internal NodeState.ReferenceUpdate PrepareReferences(
            IAsyncNodeManager owner,
            NodeState node,
            ArrayOf<IReference> additions,
            ArrayOf<IReference> removals,
            Dictionary<NodeState, NodeState.ReferenceSnapshot> next)
        {
            lock (m_lock)
            {
                EnsureMutable();
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

        /// <summary>
        /// Releases the reference images of all nodes whose references are owned by a node manager.
        /// </summary>
        /// <param name="owner">The node manager that owned the references.</param>
        internal void ReleaseReferences(IAsyncNodeManager owner)
        {
            lock (m_lock)
            {
                EnsureMutable();
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
                        m_snapshot.TypeTree, m_snapshot.Factory, references, m_snapshot.ReadImages);
                }
            }
        }

        /// <summary>
        /// Selects the reference image of a node in the active snapshot.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>The reference snapshot, or null if there is none.</returns>
        private NodeState.ReferenceSnapshot? SelectReferences(NodeState node)
        {
            return ReadSnapshot.References.TryGetValue(node, out NodeState.ReferenceSnapshot? image) ? image : null;
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

        private readonly AsyncLocal<RoutingSnapshot?> m_readSnapshot = new();
        private readonly AsyncLocal<TypeTable?> m_preparedTypes = new();
        private readonly AsyncLocal<EncodeableFactory?> m_preparedFactory = new();
        private readonly Dictionary<NodeState, IAsyncNodeManager> m_referenceOwners = [];
        private PreparedRoutes? m_reservedRoutes;

        /// <summary>
        /// Compares node manager read image owners by reference.
        /// </summary>
        private sealed class ReadImageOwnerComparer : IEqualityComparer<IAsyncNodeManager>
        {
            /// <summary>
            /// Gets the singleton comparer.
            /// </summary>
            public static ReadImageOwnerComparer Instance { get; } = new();

            /// <summary>
            /// Determines whether two node managers are the same instance.
            /// </summary>
            /// <param name="left">The first node manager.</param>
            /// <param name="right">The second node manager.</param>
            /// <returns>True if both are the same instance.</returns>
            public bool Equals(IAsyncNodeManager? left, IAsyncNodeManager? right)
            {
                return ReferenceEquals(left, right);
            }

            /// <summary>
            /// Gets the reference-based hash code of a node manager.
            /// </summary>
            /// <param name="owner">The node manager.</param>
            /// <returns>The hash code.</returns>
            public int GetHashCode(IAsyncNodeManager owner)
            {
                return RuntimeHelpers.GetHashCode(owner);
            }
        }

        /// <summary>
        /// Restores the previous prepared type tree and factory when disposed.
        /// </summary>
        /// <param name="owner">The routing table.</param>
        /// <param name="previous">The previously prepared type tree.</param>
        /// <param name="previousFactory">The previously prepared factory.</param>
        private sealed class TypeScope(
            NodeManagerRoutingTable owner,
            TypeTable? previous,
            EncodeableFactory? previousFactory) : IDisposable
        {
            /// <inheritdoc/>
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

        /// <summary>
        /// Restores the previously captured routing snapshot when disposed.
        /// </summary>
        internal sealed class ReadScope : IDisposable
        {
            /// <summary>
            /// Creates a read scope.
            /// </summary>
            /// <param name="owner">The routing table.</param>
            /// <param name="previous">The snapshot to restore on disposal.</param>
            internal ReadScope(NodeManagerRoutingTable owner, RoutingSnapshot? previous)
            {
                m_owner = owner;
                m_previous = previous;
            }

            /// <inheritdoc/>
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

        /// <summary>
        /// Routing image prepared for a batch that can be reserved and then published.
        /// </summary>
        internal sealed class PreparedRoutes : IDisposable
        {
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
            }

            /// <summary>
            /// Reserves the routing table for this publication so that no other routing change can interleave.
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
                    m_owner.m_reservedRoutes = this;
                }
            }

            /// <summary>
            /// Publishes the prepared snapshot. Repeated calls have no effect.
            /// </summary>
            public void Publish()
            {
                lock (m_owner.m_lock)
                {
                    if (m_published)
                    {
                        return;
                    }
                    if (m_disposed || !ReferenceEquals(m_owner.m_reservedRoutes, this))
                    {
                        throw new InvalidOperationException("The routing publication has not been reserved.");
                    }
                    Volatile.Write(ref m_owner.m_snapshot, m_next);
                    m_published = true;
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                lock (m_owner.m_lock)
                {
                    if (ReferenceEquals(m_owner.m_reservedRoutes, this))
                    {
                        m_owner.m_reservedRoutes = null;
                    }
                    m_disposed = true;
                }
            }

            private readonly NodeManagerRoutingTable m_owner;
            private readonly RoutingSnapshot m_previous;
            private readonly RoutingSnapshot m_next;
            private bool m_published;
            private bool m_disposed;
        }
    }
}
