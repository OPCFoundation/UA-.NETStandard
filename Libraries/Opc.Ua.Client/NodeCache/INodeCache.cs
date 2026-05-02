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

#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Client-side cache of the server's address space. Combines the
    /// <see cref="IAsyncNodeTable"/> / <see cref="IAsyncTypeTable"/>
    /// surface (ExpandedNodeId-keyed, nullable, may fetch from the
    /// server) with NodeId-keyed direct accessors for callers that
    /// already hold a local <see cref="NodeId"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The interface is intentionally minimal. Two complementary
    /// families of lookups exist:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <term>Inherited <c>Find*</c></term>
    ///     <description>
    ///       From <see cref="IAsyncNodeTable"/> /
    ///       <see cref="IAsyncTypeTable"/>: ExpandedNodeId-keyed,
    ///       returns <c>null</c> on miss, may fetch from the server
    ///       and updates the cache. Suitable for callers that have not
    ///       yet resolved the namespace index.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term><c>Get*</c></term>
    ///     <description>
    ///       Defined here: NodeId-keyed, throws when the node cannot
    ///       be resolved, expected hot-path for traversals where the
    ///       caller already has a local <see cref="NodeId"/>. The
    ///       cache-fast-path returns synchronously.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// Convenience helpers (display text, browse path traversal,
    /// built-in type resolution, ExpandedNodeId variants of
    /// <c>Get*</c>) live as static extension methods on
    /// <see cref="NodeCacheExtensions"/>.
    /// </para>
    /// <para>
    /// All asynchronous operations return <see cref="ValueTask"/> /
    /// <see cref="ValueTask{T}"/>. <see cref="Clear"/> is intentionally
    /// synchronous because it is a pure local-state mutation with no
    /// I/O.
    /// </para>
    /// </remarks>
    public interface INodeCache : IAsyncNodeTable, IAsyncTypeTable
    {
        /// <summary>
        /// Removes all cached nodes, references and values. Pure
        /// local-state mutation — does not contact the server.
        /// </summary>
        void Clear();

        /// <summary>
        /// Force-fetches a node from the server and updates the cache.
        /// Returns <c>null</c> when the node does not exist or cannot
        /// be read.
        /// </summary>
        ValueTask<Node?> FetchNodeAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default);

        /// <summary>
        /// Force-fetches a node collection from the server and updates
        /// the cache.
        /// </summary>
        ValueTask<ArrayOf<Node?>> FetchNodesAsync(
            ArrayOf<ExpandedNodeId> nodeIds,
            CancellationToken ct = default);

        /// <summary>
        /// Get a node from the cache by local <see cref="NodeId"/>.
        /// Fetches from the server if not cached. Throws when the node
        /// cannot be resolved.
        /// </summary>
        ValueTask<INode> GetNodeAsync(
            NodeId nodeId,
            CancellationToken ct = default);

        /// <summary>
        /// Get a node collection from the cache by local
        /// <see cref="NodeId"/>s. Fetches missing entries from the
        /// server.
        /// </summary>
        ValueTask<ArrayOf<INode>> GetNodesAsync(
            ArrayOf<NodeId> nodeIds,
            CancellationToken ct = default);

        /// <summary>
        /// Get the value of a node from the cache. Fetches if not
        /// cached.
        /// </summary>
        ValueTask<DataValue> GetValueAsync(
            NodeId nodeId,
            CancellationToken ct = default);

        /// <summary>
        /// Get values for a node collection.
        /// </summary>
        ValueTask<ArrayOf<DataValue>> GetValuesAsync(
            ArrayOf<NodeId> nodeIds,
            CancellationToken ct = default);

        /// <summary>
        /// Get the references for a node by reference type.
        /// </summary>
        ValueTask<ArrayOf<INode>> GetReferencesAsync(
            NodeId nodeId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes = true,
            CancellationToken ct = default);

        /// <summary>
        /// Get the references for a node collection by reference type
        /// collection.
        /// </summary>
        ValueTask<ArrayOf<INode>> GetReferencesAsync(
            ArrayOf<NodeId> nodeIds,
            ArrayOf<NodeId> referenceTypeIds,
            bool isInverse,
            bool includeSubtypes = true,
            CancellationToken ct = default);

        /// <summary>
        /// Pre-load the type hierarchy for the supplied type ids so
        /// subsequent <see cref="IAsyncTypeTable.IsTypeOfAsync(NodeId,NodeId,CancellationToken)"/>
        /// / <see cref="IAsyncTypeTable.FindSubTypesAsync"/> calls are
        /// served from the cache.
        /// </summary>
        ValueTask LoadTypeHierarchyAsync(
            ArrayOf<NodeId> typeIds,
            CancellationToken ct = default);
    }
}
