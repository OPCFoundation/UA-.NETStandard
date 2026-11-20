/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Linq;
using Opc.Ua.IA;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server;
using Opc.Ua.PlasticsRubber;
using Opc.Ua.PlasticsRubber.Server;

namespace Opc.Ua.GranulateDrying.Server
{
    /// <summary>
    /// Adds the licensed GDD model after the existing Machinery/DI owner's dependencies.
    /// Common types are loaded once even when other plastics/rubber families share the owner.
    /// </summary>
    public sealed class GranulateDryingModelProvider : IMachineryModelProvider
    {
        /// <inheritdoc/>
        public int Order => 350;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris =>
            [PlasticsRubber.Namespaces.PlasticsRubber, Namespaces.GranulateDrying];

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
            NodeId machines = ExpandedNodeId.ToNodeId(Machinery.ObjectIds.Machines, context.NamespaceUris);
            if (!nodes.Any(node => node.NodeId == machines))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Granulate drying requires MachineryParts.BuildingBlocks on the shared owner.");
            }
            new PlasticsRubberModelProvider().AddPredefinedNodes(nodes, context);
            NodeId type = ExpandedNodeId.ToNodeId(ObjectTypeIds.GranulateDryingDeviceType, context.NamespaceUris);
            if (!nodes.Any(node => node.NodeId == type))
            {
                nodes.AddOpcUaGranulateDrying(context);
            }
            context.NodeStateFactory.AddOpcUaIA().AddOpcUaMachinery().AddOpcUaGranulateDrying();
            context.EncodeableFactory.Builder.AddOpcUaGranulateDrying().Commit();
        }
    }
}
