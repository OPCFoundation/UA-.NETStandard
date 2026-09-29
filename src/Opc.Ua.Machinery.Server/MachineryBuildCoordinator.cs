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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server;

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// Per-node-manager registry of the NodeIds and root browse names that
    /// in-flight Machinery builds have reserved. Every build context created
    /// for the same <see cref="DiNodeManager"/> shares one instance, so
    /// concurrent builds never hand out the same identifier or sibling name.
    /// </summary>
    internal sealed class MachineryBuildCoordinator
    {
        /// <summary>
        /// Returns the coordinator attached to <paramref name="manager"/>,
        /// creating it on first use. It lives exactly as long as the manager.
        /// </summary>
        /// <param name="manager">The node manager to coordinate.</param>
        public static MachineryBuildCoordinator Get(DiNodeManager manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            return s_managerCoordinators.GetValue(
                manager,
                static _ => new MachineryBuildCoordinator());
        }

        /// <summary>
        /// Reserves a numeric NodeId in <paramref name="namespaceIndex"/> for
        /// <paramref name="node"/> that no other build holds and
        /// <paramref name="manager"/> has not indexed yet. A node that already
        /// holds a reservation in that namespace gets its NodeId back.
        /// </summary>
        /// <param name="manager">
        /// The node manager whose already-indexed nodes are skipped.
        /// </param>
        /// <param name="namespaceIndex">The namespace to allocate in.</param>
        /// <param name="node">The node the NodeId is reserved for.</param>
        /// <returns>The reserved NodeId.</returns>
        public NodeId ReserveNodeId(
            DiNodeManager manager,
            ushort namespaceIndex,
            NodeState node)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            lock (m_lock)
            {
                NamespaceNodeIdReservations reservations =
                    GetNodeIdReservations(namespaceIndex);
                if (node.NodeId.NamespaceIndex == namespaceIndex &&
                    reservations.Owners.TryGetValue(
                        node.NodeId,
                        out WeakReference<NodeState>? owner) &&
                    owner.TryGetTarget(out NodeState? reservedNode) &&
                    ReferenceEquals(reservedNode, node))
                {
                    return node.NodeId;
                }

                while (true)
                {
                    uint lastUsedNodeId = reservations.LastUsedNodeId;
                    uint identifier = Utils.IncrementIdentifier(ref lastUsedNodeId);
                    reservations.LastUsedNodeId = lastUsedNodeId;

                    var candidate = new NodeId(identifier, namespaceIndex);
                    if (reservations.NodeIds.Contains(candidate) ||
                        manager.FindPredefinedNode(candidate) != null)
                    {
                        continue;
                    }

                    reservations.NodeIds.Add(candidate);
                    reservations.Owners.Add(
                        candidate,
                        new WeakReference<NodeState>(node));
                    return candidate;
                }
            }
        }

        /// <summary>
        /// Releases the reservation of <paramref name="nodeId"/> if
        /// <paramref name="node"/> still owns it. Called once the node manager
        /// has indexed the node and the reservation is no longer needed.
        /// </summary>
        /// <param name="nodeId">The reserved NodeId.</param>
        /// <param name="node">The node that must own the reservation.</param>
        public void ReleaseNodeId(NodeId nodeId, NodeState node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            lock (m_lock)
            {
                if (m_nodeIdReservations.TryGetValue(
                        nodeId.NamespaceIndex,
                        out NamespaceNodeIdReservations? reservations) &&
                    reservations.Owners.TryGetValue(
                        nodeId,
                        out WeakReference<NodeState>? owner) &&
                    owner.TryGetTarget(out NodeState? reservedNode) &&
                    ReferenceEquals(reservedNode, node))
                {
                    reservations.NodeIds.Remove(nodeId);
                    reservations.Owners.Remove(nodeId);
                }
            }
        }

        /// <summary>
        /// Releases every NodeId reserved for <paramref name="node"/>, in any
        /// namespace. Called when a build ends, so a node that never reached
        /// the address space does not keep its identifier.
        /// </summary>
        /// <param name="node">The node whose reservations are released.</param>
        public void ReleaseNodeId(NodeState node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            lock (m_lock)
            {
                foreach (NamespaceNodeIdReservations reservations
                    in m_nodeIdReservations.Values)
                {
                    var nodeIds = new List<NodeId>();
                    foreach (KeyValuePair<
                        NodeId,
                        WeakReference<NodeState>> reservation in reservations.Owners)
                    {
                        if (reservation.Value.TryGetTarget(out NodeState? reservedNode) &&
                            ReferenceEquals(reservedNode, node))
                        {
                            nodeIds.Add(reservation.Key);
                        }
                    }

                    for (int ii = 0; ii < nodeIds.Count; ii++)
                    {
                        NodeId nodeId = nodeIds[ii];
                        reservations.NodeIds.Remove(nodeId);
                        reservations.Owners.Remove(nodeId);
                    }
                }
            }
        }

        /// <summary>
        /// Returns how many NodeIds are currently reserved in
        /// <paramref name="namespaceIndex"/> — zero once every build has
        /// finished, so a non-zero count after the fact points at a leak.
        /// </summary>
        /// <param name="namespaceIndex">The namespace to count.</param>
        public int GetReservedNodeIdCount(ushort namespaceIndex)
        {
            lock (m_lock)
            {
                return m_nodeIdReservations.TryGetValue(
                    namespaceIndex,
                    out NamespaceNodeIdReservations? reservations)
                    ? reservations.NodeIds.Count
                    : 0;
            }
        }

        /// <summary>
        /// Gets how many resources of registered machines the coordinator
        /// currently holds.
        /// </summary>
        public int ResourceCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_resources.Count;
                }
            }
        }

        /// <summary>
        /// Takes over the resources a registered machine build created — a
        /// result transfer manager, a job-management change pump — so they live
        /// as long as the machine, which lives as long as the manager.
        /// </summary>
        /// <param name="resources">The resources to take over.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="resources"/> is <see langword="null"/>.
        /// </exception>
        public void AdoptResources(IReadOnlyList<IAsyncDisposable> resources)
        {
            if (resources == null)
            {
                throw new ArgumentNullException(nameof(resources));
            }
            lock (m_lock)
            {
                for (int ii = 0; ii < resources.Count; ii++)
                {
                    m_resources.Add(resources[ii]);
                }
            }
        }

        /// <summary>
        /// Releases every adopted resource, newest first. The node manager
        /// calls this when it is disposed; a failure of one resource does not
        /// keep the others from being released.
        /// </summary>
        /// <exception cref="AggregateException">
        /// One or more resources failed to release.
        /// </exception>
        public async ValueTask DisposeResourcesAsync()
        {
            IAsyncDisposable[] resources;
            lock (m_lock)
            {
                resources = [.. m_resources];
                m_resources.Clear();
            }

            List<Exception>? errors = null;
            for (int ii = resources.Length - 1; ii >= 0; ii--)
            {
                try
                {
                    await resources[ii].DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    (errors ??= []).Add(ex);
                }
            }
            if (errors != null)
            {
                throw new AggregateException(
                    "Releasing the resources of the Machinery machines failed.",
                    errors);
            }
        }

        /// <summary>
        /// Reserves <paramref name="browseName"/> for a new child of
        /// <paramref name="parent"/>, refusing a name that an existing child or
        /// another in-flight build already uses.
        /// </summary>
        /// <param name="context">
        /// The system context the existing children are enumerated with.
        /// </param>
        /// <param name="parent">The node the machine is created below.</param>
        /// <param name="browseName">The browse name to reserve.</param>
        /// <returns>The reservation; disposing it releases the name.</returns>
        /// <exception cref="ServiceResultException">
        /// The name is already taken, or <paramref name="parent"/> has no
        /// NodeId.
        /// </exception>
        public IDisposable ReserveRootBrowseName(
            ISystemContext context,
            NodeState parent,
            QualifiedName browseName)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }
            if (parent.NodeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "A parent NodeId is required to reserve root BrowseName '{0}'.",
                    browseName);
            }

            NodeId parentNodeId = parent.NodeId;
            lock (m_lock)
            {
                var children = new List<BaseInstanceState>();
                parent.GetChildren(context, children);
                for (int ii = 0; ii < children.Count; ii++)
                {
                    if (children[ii].BrowseName == browseName)
                    {
                        throw CreateDuplicateBrowseNameException(parent, browseName);
                    }
                }

                if (!m_rootBrowseNameReservations.TryGetValue(
                        parentNodeId,
                        out HashSet<QualifiedName>? reservations))
                {
                    reservations = [];
                    m_rootBrowseNameReservations.Add(parentNodeId, reservations);
                }
                if (!reservations.Add(browseName))
                {
                    throw CreateDuplicateBrowseNameException(parent, browseName);
                }
            }

            return new RootBrowseNameReservation(this, parentNodeId, browseName);
        }

        private static ServiceResultException CreateDuplicateBrowseNameException(
            NodeState parent,
            QualifiedName browseName)
        {
            return ServiceResultException.Create(
                StatusCodes.BadBrowseNameDuplicated,
                "Parent '{0}' already contains or is creating a child named '{1}'.",
                parent.BrowseName,
                browseName);
        }

        private NamespaceNodeIdReservations GetNodeIdReservations(
            ushort namespaceIndex)
        {
            if (!m_nodeIdReservations.TryGetValue(
                    namespaceIndex,
                    out NamespaceNodeIdReservations? reservations))
            {
                reservations = new NamespaceNodeIdReservations();
                m_nodeIdReservations.Add(namespaceIndex, reservations);
            }
            return reservations;
        }

        private void ReleaseRootBrowseName(
            NodeId parentNodeId,
            QualifiedName browseName)
        {
            lock (m_lock)
            {
                if (m_rootBrowseNameReservations.TryGetValue(
                        parentNodeId,
                        out HashSet<QualifiedName>? reservations) &&
                    reservations.Remove(browseName) &&
                    reservations.Count == 0)
                {
                    m_rootBrowseNameReservations.Remove(parentNodeId);
                }
            }
        }

        private static readonly ConditionalWeakTable<
            DiNodeManager,
            MachineryBuildCoordinator> s_managerCoordinators = new();
        private readonly Dictionary<ushort, NamespaceNodeIdReservations>
            m_nodeIdReservations = [];
        private readonly Dictionary<NodeId, HashSet<QualifiedName>>
            m_rootBrowseNameReservations = [];
        private readonly List<IAsyncDisposable> m_resources = [];
        private readonly Lock m_lock = new();

        private sealed class NamespaceNodeIdReservations
        {
            public HashSet<NodeId> NodeIds { get; } = [];

            public Dictionary<NodeId, WeakReference<NodeState>> Owners { get; } = [];

            public uint LastUsedNodeId { get; set; }
        }

        private sealed class RootBrowseNameReservation : IDisposable
        {
            public RootBrowseNameReservation(
                MachineryBuildCoordinator coordinator,
                NodeId parentNodeId,
                QualifiedName browseName)
            {
                m_coordinator = coordinator;
                m_parentNodeId = parentNodeId;
                m_browseName = browseName;
            }

            public void Dispose()
            {
                MachineryBuildCoordinator? coordinator =
                    Interlocked.Exchange(ref m_coordinator, null);
                coordinator?.ReleaseRootBrowseName(m_parentNodeId, m_browseName);
            }

            private MachineryBuildCoordinator? m_coordinator;
            private readonly NodeId m_parentNodeId;
            private readonly QualifiedName m_browseName;
        }
    }
}
