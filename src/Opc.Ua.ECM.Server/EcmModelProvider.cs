/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Di.Server;
using Opc.Ua.IA;

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// Loads ECM and its IA dependency into an existing DI owner without replacing loaded models.
    /// </summary>
    public sealed class EcmModelProvider : IDiModelProvider
    {
        /// <inheritdoc/>
        public int Order => 200;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => [IA.Namespaces.IA, Namespaces.ECM];

        /// <inheritdoc/>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (!Contains(nodes, Di.ObjectTypeIds.DeviceType, context))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, "Load DI before composing the ECM model.");
            }
            if (!Contains(nodes, IA.ObjectTypeIds.StacklightType, context))
            {
                nodes.AddOpcUaIA(context);
            }
            if (!Contains(nodes, ObjectTypeIds.EnergyStandbyManagementType, context))
            {
                nodes.AddOpcUaECM(context);
            }
            context.EncodeableFactory.Builder.AddOpcUaECM().Commit();
        }

        /// <summary>
        /// Resolves published identifiers using the owner's current namespace table.
        /// </summary>
        private static bool Contains(
            NodeStateCollection nodes,
            ExpandedNodeId type,
            ISystemContext context)
        {
            NodeId id = ExpandedNodeId.ToNodeId(type, context.NamespaceUris);
            foreach (NodeState node in nodes)
            {
                if (node.NodeId == id)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
