// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.XRegistry
{
    /// <summary>
    /// Captures NodeIds whose Opaque identifier must not alias a caller-owned buffer.
    /// </summary>
    /// <remarks>
    /// Compiled into both the XRegistry and XRegistry.Client assemblies so the capture policy has one definition.
    /// </remarks>
    internal static class XRegistryNodeIdCopy
    {
        /// <summary>
        /// Returns a NodeId whose Opaque identifier is an owned copy; other identifier types are returned as is.
        /// </summary>
        /// <param name="nodeId">The NodeId to capture.</param>
        /// <returns>The captured NodeId.</returns>
        public static NodeId Own(NodeId nodeId)
        {
            return nodeId.TryGetValue(out ByteString identifier)
                ? new NodeId(identifier.Copy(), nodeId.NamespaceIndex)
                : nodeId;
        }

        /// <summary>
        /// Returns an ExpandedNodeId whose Opaque identifier is an owned copy, preserving the namespace
        /// index, namespace URI and server index; other identifier types are returned as is.
        /// </summary>
        /// <param name="nodeId">The ExpandedNodeId to capture.</param>
        /// <returns>The captured ExpandedNodeId.</returns>
        public static ExpandedNodeId Own(ExpandedNodeId nodeId)
        {
            return nodeId.TryGetValue(out ByteString identifier)
                ? new ExpandedNodeId(
                    new NodeId(identifier.Copy(), nodeId.NamespaceIndex),
                    nodeId.NamespaceUri,
                    nodeId.ServerIndex)
                : nodeId;
        }
    }
}
