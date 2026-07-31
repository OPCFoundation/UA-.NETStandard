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

using Opc.Ua.XRegistry;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Splits the single combined WoT Connectivity 1.1 model into the disjoint
    /// static-node slices owned by the legacy connectivity surface and the
    /// additive registry surface.
    /// </summary>
    internal static class WotConModelPartition
    {
        /// <summary>
        /// First NodeId of the additive registry block.
        /// </summary>
        public const uint FirstRegistryNodeId = 64000;

        /// <summary>
        /// Ensures the xRegistry namespace is present so the combined model's
        /// registry nodes can be instantiated before the registry slice is removed.
        /// </summary>
        public static void EnsureXRegistryNamespace(ISystemContext context)
        {
            context.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
        }

        /// <summary>
        /// Removes the additive registry nodes, retaining only the incorporated
        /// OPC 10100-1 v1.02 surface for the legacy asset manager to own.
        /// </summary>
        public static NodeStateCollection RetainLegacyNodes(
            NodeStateCollection nodes,
            ISystemContext context)
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

        private static ushort ModelNamespaceIndex(ISystemContext context)
        {
            return (ushort)context.NamespaceUris.GetIndex(Namespaces.WotCon);
        }

        private static bool IsRegistryNode(NodeState node, ushort modelNs)
        {
            return node.NodeId.NamespaceIndex == modelNs &&
                node.NodeId.TryGetValue(out uint id) &&
                id >= FirstRegistryNodeId;
        }
    }
}
