// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Opc.Ua.Di.Server;

namespace Opc.Ua.Robotics.Server
{
    internal sealed class RoboticsBuildCoordinator
    {
        public static RoboticsBuildCoordinator Get(DiNodeManager manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            return s_managerCoordinators.GetValue(
                manager,
                static _ => new RoboticsBuildCoordinator());
        }

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
            RoboticsBuildCoordinator> s_managerCoordinators = new();
        private readonly Dictionary<ushort, NamespaceNodeIdReservations>
            m_nodeIdReservations = [];
        private readonly Dictionary<NodeId, HashSet<QualifiedName>>
            m_rootBrowseNameReservations = [];
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
                RoboticsBuildCoordinator coordinator,
                NodeId parentNodeId,
                QualifiedName browseName)
            {
                m_coordinator = coordinator;
                m_parentNodeId = parentNodeId;
                m_browseName = browseName;
            }

            public void Dispose()
            {
                RoboticsBuildCoordinator? coordinator =
                    Interlocked.Exchange(ref m_coordinator, null);
                coordinator?.ReleaseRootBrowseName(m_parentNodeId, m_browseName);
            }

            private RoboticsBuildCoordinator? m_coordinator;
            private readonly NodeId m_parentNodeId;
            private readonly QualifiedName m_browseName;
        }
    }
}
