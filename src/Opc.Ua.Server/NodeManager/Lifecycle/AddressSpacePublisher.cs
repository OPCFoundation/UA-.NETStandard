/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Installs a fully prepared address-space publication without running asynchronous or application code.
    /// </summary>
    internal sealed class AddressSpacePublisher
    {
        /// <summary>
        /// Captures reserved publication inputs and resolves candidate handles before the durable decision.
        /// </summary>
        internal AddressSpacePublisher(
            NodeManagerRoutingTable.PreparedRoutes routes,
            StringTable.Publication namespaces,
            StringTable.Publication serverUris,
            TypeTable.Publication types,
            EncodeableFactory.Publication registrations,
            TypeTable typeTree,
            EncodeableFactory factory,
            ArrayOf<PreparedNodeManager> candidates,
            IReadOnlyList<NodeState.ReferenceUpdate> referenceUpdates,
            Action<bool> viewPublished)
        {
            m_routes = routes;
            m_namespaces = namespaces;
            m_serverUris = serverUris;
            m_types = types;
            m_registrations = registrations;
            m_typeTree = typeTree;
            m_factory = factory;
            m_viewPublished = viewPublished;
            m_stagingServers = new ChangePreparer.StagingServer?[candidates.Count];
            for (int index = 0; index < candidates.Count; index++)
            {
                m_stagingServers[index] = ChangePreparer.GetServer(candidates[index].NodeManager);
            }
            m_referenceUpdates = new NodeState.ReferenceUpdate[referenceUpdates.Count];
            for (int index = 0; index < referenceUpdates.Count; index++)
            {
                m_referenceUpdates[index] = referenceUpdates[index];
            }
        }

        /// <summary>
        /// Installs captured storage and ordered references around the sole routing-view commit point.
        /// This operation performs no candidate lookup, allocation, or application callback before the swap.
        /// </summary>
        internal void Publish()
        {
            m_namespaces.Complete();
            m_serverUris.Complete();
            m_types.Complete(m_typeTree);
            m_registrations.Complete(m_factory);
            foreach (ChangePreparer.StagingServer? server in m_stagingServers)
            {
                server?.Bind();
            }
            foreach (NodeState.ReferenceUpdate update in m_referenceUpdates)
            {
                update.Remove();
            }

            bool historianRegistrationsChanged = m_routes.Publish();
            try
            {
                m_viewPublished(historianRegistrationsChanged);
            }
            finally
            {
                foreach (NodeState.ReferenceUpdate update in m_referenceUpdates)
                {
                    update.Complete();
                }
            }
        }

        /// <summary>Reserved inputs with sole ownership retained by the lifecycle transaction.</summary>
        private readonly NodeManagerRoutingTable.PreparedRoutes m_routes;
        private readonly StringTable.Publication m_namespaces;
        private readonly StringTable.Publication m_serverUris;
        private readonly TypeTable.Publication m_types;
        private readonly EncodeableFactory.Publication m_registrations;
        private readonly TypeTable m_typeTree;
        private readonly EncodeableFactory m_factory;
        private readonly ChangePreparer.StagingServer?[] m_stagingServers;
        private readonly NodeState.ReferenceUpdate[] m_referenceUpdates;
        private readonly Action<bool> m_viewPublished;
    }
}
