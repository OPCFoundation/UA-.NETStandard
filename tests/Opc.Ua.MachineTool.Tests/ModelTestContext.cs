// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Creates isolated model contexts with nontrivial namespace indexes.
    /// </summary>
    internal static class ModelTestContext
    {
        /// <summary>
        /// Creates a core message context and registers only the selected encodeable closure.
        /// </summary>
        public static ServiceMessageContext CreateMessageContext(MachineToolParts parts)
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.NamespaceUris.GetIndexOrAppend("urn:machinetool-tests:application");
            context.NamespaceUris.GetIndexOrAppend("urn:machinetool-tests:unrelated");
            foreach (string uri in MachineToolModels.GetNamespaceUris(parts))
            {
                context.NamespaceUris.GetIndexOrAppend(uri);
            }
            context.Factory.Builder.AddMachineToolTypeSystem(parts).Commit();
            return context;
        }

        /// <summary>
        /// Creates a node-state context using the same namespace table and encodeable factory.
        /// </summary>
        public static SystemContext CreateSystemContext(MachineToolParts parts)
        {
            ServiceMessageContext message = CreateMessageContext(parts);
            return new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = message.NamespaceUris,
                EncodeableFactory = message.Factory
            };
        }

        /// <summary>
        /// Indexes materialized model nodes, including nested instance declarations.
        /// </summary>
        public static Dictionary<NodeId, NodeState> IndexNodes(ISystemContext context, NodeStateCollection roots)
        {
            var nodes = new Dictionary<NodeId, NodeState>();
            foreach (NodeState root in roots)
            {
                AddNode(context, root, nodes);
            }
            return nodes;
        }

        private static void AddNode(
            ISystemContext context,
            NodeState node,
            Dictionary<NodeId, NodeState> nodes)
        {
            if (!nodes.TryAdd(node.NodeId, node))
            {
                return;
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                AddNode(context, child, nodes);
            }
        }
    }
}
