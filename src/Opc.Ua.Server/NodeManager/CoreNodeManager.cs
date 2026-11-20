/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A node manager which implements the ICoreNodeManager interface using the AsyncCustomNodeManager base class.
    /// Every Server has one instance of this NodeManager.
    /// It manages the built-in OPC UA nodes and provides core functionality.
    /// </summary>
    public class CoreNodeManager : AsyncCustomNodeManager, ICoreNodeManager
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public CoreNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : base(server, configuration, useSamplingGroups: true)
        {
        }

        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public CoreNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ushort dynamicNamespaceIndex)
            : base(server, configuration, useSamplingGroups: true, server.NamespaceUris.GetString(dynamicNamespaceIndex)!)
        {
        }

        /// <inheritdoc/>
        public ValueTask ImportNodesAsync(
             ISystemContext context,
             IEnumerable<NodeState> predefinedNodes,
             CancellationToken cancellationToken = default)
        {
            return ImportNodesAsync(context, predefinedNodes, false, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask ImportNodesAsync(
            ISystemContext context,
            IEnumerable<NodeState> predefinedNodes,
            bool isInternal,
            CancellationToken cancellationToken = default)
        {
            foreach (NodeState node in predefinedNodes)
            {
                await AddPredefinedNodeAsync(context, node, cancellationToken)
                    .ConfigureAwait(false);

                if (!isInternal)
                {
                    Server.UpdateServerDiagnostics(diagnostics =>
                    {
                        UpdateDiagnostics(context, node);
                    });
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

            for (IReference existing = browser.Next()!; existing != null; existing = browser.Next()!)
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
