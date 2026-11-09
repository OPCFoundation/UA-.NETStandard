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
