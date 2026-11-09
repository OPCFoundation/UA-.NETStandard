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
