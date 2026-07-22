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

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Splits the single combined WoT Connectivity 1.1 model
    /// (<c>http://opcfoundation.org/UA/WoT-Con/</c>) into the two disjoint
    /// static-node slices its NodeManagers own, so the deprecated 1.02 surface
    /// and the additive registry surface are never claimed twice when both the
    /// <see cref="WotConnectivityNodeManager"/> and the
    /// <see cref="WotRegistryNodeManager"/> operate on the same server.
    /// </summary>
    /// <remarks>
    /// The combined NodeSet incorporates the published OPC 10100-1 v1.02 model
    /// at its original NodeIds (<c>1..172</c>, marked deprecated) plus the
    /// additive registry nodes in the provisional <c>64000+</c> block.
    /// Ownership is therefore decided by NodeId: the legacy asset manager owns
    /// the incorporated 1.02 nodes and the registry manager owns the registry
    /// nodes (together with the xRegistry base nodes it also loads).
    /// </remarks>
    internal static class WotConModelPartition
    {
        /// <summary>
        /// First NodeId of the additive registry block. NodeIds below this
        /// value belong to the incorporated OPC 10100-1 v1.02 surface.
        /// </summary>
        public const uint FirstRegistryNodeId = 64000;

        /// <summary>
        /// Ensures the xRegistry namespace is present so the combined model's
        /// registry nodes (which reference xRegistry base types while being
        /// created) can be instantiated before the registry slice is removed.
        /// The legacy manager does not own the xRegistry namespace; it merely
        /// needs the URI registered so <see cref="NodeId.Create(uint, string,
        /// NamespaceTable)"/> resolves during predefined-node creation.
        /// </summary>
        public static void EnsureXRegistryNamespace(ISystemContext context)
        {
            context.NamespaceUris.GetIndexOrAppend(XRegistry.Namespaces.XRegistry);
        }

        /// <summary>
        /// Removes the additive registry nodes, retaining only the incorporated
        /// OPC 10100-1 v1.02 surface for the legacy asset manager to own.
        /// </summary>
        public static NodeStateCollection RetainLegacyNodes(
            NodeStateCollection nodes, ISystemContext context)
        {
            ushort modelNs = ModelNamespaceIndex(context);
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                if (IsRegistryNode(nodes[i], modelNs))
                {
                    nodes.RemoveAt(i);
                }
            }
            return nodes;
        }

        /// <summary>
        /// Removes the incorporated OPC 10100-1 v1.02 nodes, retaining only the
        /// additive registry nodes (and any xRegistry base nodes already in the
        /// collection) for the registry manager to own.
        /// </summary>
        public static NodeStateCollection RetainRegistryNodes(
            NodeStateCollection nodes, ISystemContext context)
        {
            ushort modelNs = ModelNamespaceIndex(context);
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                if (IsLegacyNode(nodes[i], modelNs))
                {
                    nodes.RemoveAt(i);
                }
            }
            return nodes;
        }

        private static ushort ModelNamespaceIndex(ISystemContext context)
            => (ushort)context.NamespaceUris.GetIndex(Namespaces.WotCon);

        private static bool IsRegistryNode(NodeState node, ushort modelNs)
            => node.NodeId.NamespaceIndex == modelNs &&
               node.NodeId.TryGetValue(out uint id) && id >= FirstRegistryNodeId;

        private static bool IsLegacyNode(NodeState node, ushort modelNs)
            => node.NodeId.NamespaceIndex == modelNs &&
               node.NodeId.TryGetValue(out uint id) && id < FirstRegistryNodeId;
    }
}
