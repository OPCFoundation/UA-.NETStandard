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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Cache implementation for Node cache
    /// </summary>
    public interface ILruNodeCache
    {
        /// <summary>
        /// The namespaces used in the server
        /// </summary>
        NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// Get node from cache
        /// </summary>
        ValueTask<INode> GetNodeAsync(NodeId nodeId, CancellationToken ct = default);

        /// <summary>
        /// Get nodes from cache
        /// </summary>
        ValueTask<ArrayOf<INode>> GetNodesAsync(
            ArrayOf<NodeId> nodeIds,
            CancellationToken ct = default);

        /// <summary>
        /// Get node using browse path
        /// </summary>
        ValueTask<INode?> GetNodeWithBrowsePathAsync(
            NodeId nodeId,
            QualifiedNameCollection browsePath,
            CancellationToken ct = default);

        /// <summary>
        /// Get list of references for a node
        /// </summary>
        ValueTask<ArrayOf<INode>> GetReferencesAsync(
            ArrayOf<NodeId> nodeIds,
            ArrayOf<NodeId> referenceTypeIds,
            bool isInverse,
            bool includeSubtypes = true,
            CancellationToken ct = default);

        /// <summary>
        /// Get references for a node
        /// </summary>
        ValueTask<ArrayOf<INode>> GetReferencesAsync(
            NodeId nodeId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes = true,
            CancellationToken ct = default);

        /// <summary>
        /// Get super type of a type node.
        /// </summary>
        ValueTask<NodeId> GetSuperTypeAsync(NodeId typeId, CancellationToken ct = default);

        /// <summary>
        /// Get value of a node from cache
        /// </summary>
        ValueTask<DataValue> GetValueAsync(NodeId nodeId, CancellationToken ct = default);

        /// <summary>
        /// Get values of nodes from cache
        /// </summary>
        ValueTask<ArrayOf<DataValue>> GetValuesAsync(
            ArrayOf<NodeId> nodeIds,
            CancellationToken ct = default);

        /// <summary>
        /// Get built in type for a datatype id.
        /// </summary>
        ValueTask<BuiltInType> GetBuiltInTypeAsync(
            NodeId datatypeId,
            CancellationToken ct = default);

        /// <summary>
        /// Load the type hierarchy for a list of type ids.
        /// </summary>
        ValueTask LoadTypeHierarchyAsync(
            ArrayOf<NodeId> typeIds,
            CancellationToken ct = default);

        /// <summary>
        /// Check if node is type of a type using the cache.
        /// Best to load the type hierarchy first.
        /// </summary>
        bool IsTypeOf(NodeId subTypeId, NodeId superTypeId);

        /// <summary>
        /// Clear the cache
        /// </summary>
        void Clear();
    }
}
