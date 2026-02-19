/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Server
{
    /// <summary>
    /// A node manager which implements the ICoreNodeManager interface using the CustomNodeManager2 base class.
    /// Every Server has one instance of this NodeManager.
    /// It manages the built-in OPC UA nodes and provides core functionality.
    /// </summary>
    public class CoreNodeManager2 : CustomNodeManager2, ICoreNodeManager
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public CoreNodeManager2(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : base(server, configuration, useSamplingGroups: true)
        {
        }

        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public CoreNodeManager2(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ushort dynamicNamespaceIndex)
            : base(server, configuration, useSamplingGroups: true, server.NamespaceUris.GetString(dynamicNamespaceIndex))
        {
        }

        /// <inheritdoc/>
        public void ImportNodes(
            ISystemContext context,
            IEnumerable<NodeState> predefinedNodes)
        {
            ImportNodes(context, predefinedNodes, false);
        }

        /// <inheritdoc/>
        public void ImportNodes(
            ISystemContext context,
            IEnumerable<NodeState> predefinedNodes,
            bool isInternal)
        {
            foreach (NodeState node in predefinedNodes)
            {
                AddPredefinedNode(context, node);

                if (!isInternal)
                {
                    lock (Server.DiagnosticsLock)
                    {
                        UpdateDiagnostics(context, node);
                    }
                }
            }
        }

        /// <summary>
        /// Updates the diagnostics node manager with the nodes that were imported.
        /// </summary>
        private void UpdateDiagnostics(ISystemContext context, NodeState node)
        {
            if (node.NodeId.NamespaceIndex == 0)
            {
                NodeState diagNode = Server.DiagnosticsNodeManager.FindPredefinedNode<NodeState>(node.NodeId);

                if (diagNode != null)
                {
                    var references = new List<IReference>();
                    node.GetReferences(context, references);

                    foreach (IReference reference in references)
                    {
                        AddReferenceToDiagnostics(context, diagNode, reference);
                    }
                }
            }

            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);

            foreach (BaseInstanceState child in children)
            {
                UpdateDiagnostics(context, child);
            }
        }

        /// <summary>
        /// Adds a reference to the diagnostics node.
        /// </summary>
        private static void AddReferenceToDiagnostics(ISystemContext context, NodeState diagNode, IReference reference)
        {
            INodeBrowser browser = diagNode.CreateBrowser(
                context,
                null,
                reference.ReferenceTypeId,
                true,
                reference.IsInverse ? BrowseDirection.Inverse : BrowseDirection.Forward,
                default,
                null,
                true);

            for (IReference existing = browser.Next(); existing != null; existing = browser.Next())
            {
                if (existing.TargetId == reference.TargetId)
                {
                    return;
                }
            }

            diagNode.AddReference(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
        }
    }
}
