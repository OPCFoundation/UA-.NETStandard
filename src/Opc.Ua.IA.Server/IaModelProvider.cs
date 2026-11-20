// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Linq;
using Opc.Ua.Di.Server;

namespace Opc.Ua.IA.Server
{
    /// <summary>
    /// Loads the single compiled IA model into any existing DI or companion owner.
    /// </summary>
    public sealed class IaModelProvider : IDiModelProvider
    {
        /// <inheritdoc/>
        public int Order => 50;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => [Namespaces.IA];

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
            NodeId type = ExpandedNodeId.ToNodeId(ObjectTypeIds.BasicStacklightType, context.NamespaceUris);
            if (!nodes.Any(node => node.NodeId == type))
            {
                nodes.AddOpcUaIA(context);
            }
            context.EncodeableFactory.Builder.AddOpcUaIA().Commit();
        }
    }
}
