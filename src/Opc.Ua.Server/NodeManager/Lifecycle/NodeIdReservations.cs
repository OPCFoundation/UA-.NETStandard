// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Tracks complete sets of NodeIds by namespace URI and exact generation on one server.
    /// </summary>
    internal sealed class NodeIdReservations
    {
        /// <summary>
        /// Reserves all requested identifiers or leaves the table unchanged.
        /// A prepared replacement can overlap its slot's current generation, not another candidate.
        /// </summary>
        internal Reservation Reserve(
            NodeManagerGeneration generation,
            NamespaceTable namespaces,
            ArrayOf<NodeId> nodeIds,
            Dictionary<NodeId, NodeState>? nodes = null)
        {
            IAsyncNodeManager resource = NodeManagerGenerationRegistry.GetIdentity(generation.NodeManager);
            var claims = new Dictionary<(string NamespaceUri, NodeId Identifier), Claim>();
            foreach (NodeId nodeId in nodeIds)
            {
                var key = GetKey(namespaces, nodeId);
                NodeState? node = null;
                nodes?.TryGetValue(nodeId, out node);
                if (claims.ContainsKey(key))
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdExists);
                }
                claims.Add(key, new Claim(generation, resource, node));
            }

            var reservation = new Reservation(this, generation, [.. claims.Keys]);
            lock (m_lock)
            {
                generation.AttachNodeIdReservations(this);
                NodeManagerGenerationState state = generation.Gate.State;
                if (state is not (NodeManagerGenerationState.Prepared or NodeManagerGenerationState.Live))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                NodeManagerGeneration? predecessor = state == NodeManagerGenerationState.Prepared
                    ? generation.Slot.Current
                    : null;
                foreach (var key in claims.Keys)
                {
                    if (!m_claims.TryGetValue(key, out List<Claim>? owners))
                    {
                        continue;
                    }
                    foreach (Claim owner in owners)
                    {
                        if (!ReferenceEquals(owner.Generation, predecessor))
                        {
                            throw new ServiceResultException(StatusCodes.BadNodeIdExists);
                        }
                    }
                }
                foreach (var entry in claims)
                {
                    if (!m_claims.TryGetValue(entry.Key, out List<Claim>? owners))
                    {
                        owners = [];
                        m_claims.Add(entry.Key, owners);
                    }
                    owners.Add(entry.Value);
                }
            }
            return reservation;
        }

        /// <summary>
        /// Releases only the specified generation's claims, after its physical cleanup is verified.
        /// </summary>
        internal void Release(NodeManagerGeneration generation)
        {
            lock (m_lock)
            {
                if (generation.Gate.State is not (NodeManagerGenerationState.Retiring or
                    NodeManagerGenerationState.Disposed or NodeManagerGenerationState.Aborted))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                var empty = new List<(string NamespaceUri, NodeId Identifier)>();
                foreach (var entry in m_claims)
                {
                    entry.Value.RemoveAll(owner => ReferenceEquals(owner.Generation, generation));
                    if (entry.Value.Count == 0)
                    {
                        empty.Add(entry.Key);
                    }
                }
                foreach (var key in empty)
                {
                    m_claims.Remove(key);
                }
            }
        }

        /// <summary>
        /// Serializes indexing against claim admission; claimed IDs may index only the reserved instance.
        /// </summary>
        internal void Index(
            IAsyncNodeManager manager,
            NamespaceTable namespaces,
            PredefinedNodeDictionary dictionary,
            NodeState node,
            NodeId serviceReservation)
        {
            string? uri = namespaces.GetString(node.NodeId.NamespaceIndex);
            IAsyncNodeManager resource = NodeManagerGenerationRegistry.GetIdentity(manager);
            lock (m_lock)
            {
                bool allowReplacement = serviceReservation.IsNull;
                if (uri is not null && m_claims.TryGetValue(
                    (uri, node.NodeId.WithNamespaceIndex(0)), out List<Claim>? owners))
                {
                    if (!owners.Exists(claim => ReferenceEquals(claim.Node, node) &&
                        ReferenceEquals(claim.Resource, resource) &&
                        claim.Generation.Gate.State is
                            (NodeManagerGenerationState.Prepared or NodeManagerGenerationState.Live)))
                    {
                        throw new ServiceResultException(StatusCodes.BadNodeIdExists);
                    }
                    allowReplacement = false;
                }
                if (allowReplacement)
                {
                    dictionary.AddOrUpdate(node.NodeId, node, (_, _) => node);
                }
                else if (!dictionary.TryAdd(node.NodeId, node) &&
                    (serviceReservation == node.NodeId ||
                        !dictionary.TryGetValue(node.NodeId, out NodeState? existing) ||
                        !ReferenceEquals(existing, node)))
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdExists);
                }
            }
        }

        /// <summary>
        /// Releases an explicitly deleted node only while its exact generation still serves.
        /// </summary>
        internal void ReleaseDeletedNode(
            IAsyncNodeManager manager,
            NamespaceTable namespaces,
            PredefinedNodeDictionary dictionary,
            NodeState node)
        {
            string? uri = namespaces.GetString(node.NodeId.NamespaceIndex);
            if (uri is null)
            {
                return;
            }
            var key = (uri, node.NodeId.WithNamespaceIndex(0));
            IAsyncNodeManager resource = NodeManagerGenerationRegistry.GetIdentity(manager);
            lock (m_lock)
            {
                if (dictionary.TryGetValue(node.NodeId, out NodeState? indexed) &&
                    ReferenceEquals(indexed, node))
                {
                    return;
                }
                if (m_claims.TryGetValue(key, out List<Claim>? owners))
                {
                    owners.RemoveAll(claim =>
                        ReferenceEquals(claim.Node, node) &&
                        ReferenceEquals(claim.Resource, resource) &&
                        claim.Generation.Gate.State == NodeManagerGenerationState.Live &&
                        ReferenceEquals(claim.Generation.Slot.Current, claim.Generation));
                    if (owners.Count == 0)
                    {
                        m_claims.Remove(key);
                    }
                }
            }
        }

        private void ReleaseReservation(
            NodeManagerGeneration generation,
            HashSet<(string NamespaceUri, NodeId Identifier)> keys)
        {
            lock (m_lock)
            {
                foreach (var key in keys)
                {
                    if (m_claims.TryGetValue(key, out List<Claim>? owners))
                    {
                        owners.RemoveAll(owner => ReferenceEquals(owner.Generation, generation));
                        if (owners.Count == 0)
                        {
                            m_claims.Remove(key);
                        }
                    }
                }
            }
        }

        private static (string NamespaceUri, NodeId Identifier) GetKey(NamespaceTable namespaces, NodeId nodeId)
        {
            string? uri = namespaces.GetString(nodeId.NamespaceIndex);
            if (nodeId.IsNull || string.IsNullOrEmpty(uri))
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid);
            }
            return (uri, nodeId.WithNamespaceIndex(0));
        }

        private readonly Lock m_lock = new();
        private sealed record Claim(NodeManagerGeneration Generation, IAsyncNodeManager Resource, NodeState? Node);

        private readonly Dictionary<(string NamespaceUri, NodeId Identifier), List<Claim>>
            m_claims = [];

        /// <summary>
        /// Identifies one reservation, so failed composition does not release a generation's other nodes.
        /// </summary>
        internal sealed class Reservation(
            NodeIdReservations table,
            NodeManagerGeneration generation,
            HashSet<(string NamespaceUri, NodeId Identifier)> keys)
        {
            /// <summary>
            /// Releases this set after rollback succeeds. Retained nodes keep their claims until cleanup.
            /// </summary>
            internal void ReleaseAfterRollback()
            {
                if (Interlocked.Exchange(ref m_released, 1) == 0)
                {
                    table.ReleaseReservation(generation, keys);
                }
            }

            private int m_released;
        }
    }
}
