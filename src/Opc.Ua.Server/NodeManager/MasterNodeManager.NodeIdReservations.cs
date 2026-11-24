// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    public partial class MasterNodeManager
    {
        /// <summary>
        /// Reserves a materialized instance's identifiers before its owner initializes external state.
        /// </summary>
        internal async ValueTask<NodeIdReservations.Reservation> ReserveManagedNodeIdsAsync(
            AsyncCustomNodeManager owner,
            ArrayOf<NodeId> nodeIds,
            Dictionary<NodeId, NodeState> nodes,
            CancellationToken cancellationToken)
        {
            NamespaceTable namespaces = owner.SystemContext.NamespaceUris;
            IAsyncNodeManager? facade = (owner.Server as ChangePreparer.StagingServer)?.RegisteredFacade;
            NodeManagerGeneration generation = facade is not null &&
                NodeManagerGenerationRegistry.TryGet(facade, out NodeManagerGeneration? staged) &&
                staged is { Gate.State: NodeManagerGenerationState.Prepared }
                    ? staged
                    : GetSourceGeneration(owner.Server, owner);
            NodeManagerGeneration? predecessor = generation.Gate.State == NodeManagerGenerationState.Prepared
                ? generation.Slot.Current
                : null;
            NodeIdReservations.Reservation reservation =
                m_nodeIdReservations.Reserve(generation, namespaces, nodeIds, nodes);
            bool accepted = false;
            try
            {
                NodeManagerRoutingTable.RoutingSnapshot routing = m_nodeManagers.Revision;
                int pin;
                while (!routing.TryPin(out pin))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    routing = m_nodeManagers.Revision;
                }
                try
                {
                    for (int index = 0; index < nodeIds.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        NodeId id = nodeIds[index];
                        int servingIndex = Server.NamespaceUris.GetIndex(namespaces.GetString(id.NamespaceIndex)!);
                        if (servingIndex < 0)
                        {
                            continue;
                        }
                        NodeId servingId = id.WithNamespaceIndex((ushort)servingIndex);
                        for (int ownerIndex = 0; ownerIndex < routing.NodeManagers.Length; ownerIndex++)
                        {
                            if (ReferenceEquals(routing.Generations[ownerIndex], predecessor))
                            {
                                continue;
                            }
                            object handle = await routing.NodeManagers[ownerIndex].GetManagerHandleAsync(
                                servingId, cancellationToken).ConfigureAwait(false);
                            if (handle is not null)
                            {
                                throw new ServiceResultException(StatusCodes.BadNodeIdExists);
                            }
                        }
                    }
                }
                finally
                {
                    routing.Epoch.Exit(pin);
                }
                accepted = true;
                return reservation;
            }
            finally
            {
                if (!accepted)
                {
                    reservation.ReleaseAfterRollback();
                }
            }
        }

        /// <summary>
        /// Indexes an in-memory node without displacing a foreign reservation.
        /// </summary>
        internal void IndexManagedNode(
            IAsyncNodeManager owner,
            NamespaceTable namespaces,
            PredefinedNodeDictionary dictionary,
            NodeState node,
            NodeId serviceReservation = default)
        {
            m_nodeIdReservations.Index(owner, namespaces, dictionary, node, serviceReservation);
        }

        /// <summary>
        /// Releases claims for the exact instances explicitly removed from a live address space.
        /// </summary>
        internal void ReleaseDeletedManagedNodes(
            AsyncCustomNodeManager owner,
            NamespaceTable namespaces,
            PredefinedNodeDictionary dictionary,
            List<NodeState> nodes)
        {
            foreach (NodeState node in nodes)
            {
                m_nodeIdReservations.ReleaseDeletedNode(owner, namespaces, dictionary, node);
            }
        }

        /// <summary>The shared claim table for this server's managed composition paths.</summary>
        private readonly NodeIdReservations m_nodeIdReservations = new();
    }
}
