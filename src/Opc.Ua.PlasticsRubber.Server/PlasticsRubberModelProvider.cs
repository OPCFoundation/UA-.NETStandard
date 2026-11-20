// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Linq;
using Opc.Ua.Di;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Registers the common model once, ahead of device-family providers in one Machinery owner.
    /// </summary>
    public sealed class PlasticsRubberModelProvider : IMachineryModelProvider
    {
        /// <inheritdoc/>
        public int Order => 150;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => [Namespaces.PlasticsRubber];

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
            NodeId commonType = ExpandedNodeId.ToNodeId(ObjectTypeIds.StartDeviceType, context.NamespaceUris);
            if (!nodes.Any(node => node.NodeId == commonType))
            {
                nodes.AddOpcUaPlasticsRubber(context);
            }
            context.NodeStateFactory.AddOpcUaDi().AddOpcUaPlasticsRubber();
            context.EncodeableFactory.Builder.AddOpcUaPlasticsRubber().Commit();
        }
    }
}
