/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
    /// A client side cache of the server's type model.
    /// </summary>
    public interface INodeCache : IAsyncNodeTable, IAsyncTypeTable
    {
        /// <summary>
        /// Loads the UA defined types into the cache.
        /// </summary>
        /// <param name="context">The context.</param>
        void LoadUaDefinedTypes(ISystemContext context);

        /// <summary>
        /// Removes all nodes from the cache.
        /// </summary>
        void Clear();

        /// <summary>
        /// Finds a set of nodes in the nodeset,
        /// fetches missing nodes from server.
        /// </summary>
        /// <param name="nodeIds">The node identifier collection.</param>
        /// <param name="ct">Cancelation token to cancel operation with</param>
        Task<IList<INode>> FindAsync(
            IList<ExpandedNodeId> nodeIds,
            CancellationToken ct = default);

        /// <summary>
        /// Fetches a node from the server and updates the cache.
        /// </summary>
        /// <param name="nodeId">Node id to fetch.</param>
        /// <param name="ct">Cancelation token to cancel operation with</param>
        Task<Node> FetchNodeAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default);

        /// <summary>
        /// Fetches a node collection from the server and updates the cache.
        /// </summary>
        /// <param name="nodeIds">The node identifier collection.</param>
        /// <param name="ct">Cancelation token to cancel operation with</param>
        Task<IList<Node>> FetchNodesAsync(
            IList<ExpandedNodeId> nodeIds,
            CancellationToken ct = default);

        /// <summary>
        /// Adds the supertypes of the node to the cache.
        /// </summary>
        /// <param name="nodeId">Node id to fetch.</param>
        /// <param name="ct">Cancelation token to cancel operation with</param>
        Task FetchSuperTypesAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default);

        /// <summary>
        /// Returns the references of the specified node that
        /// meet the criteria specified.
        /// </summary>
        Task<IList<INode>> FindReferencesAsync(
            ExpandedNodeId nodeId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            CancellationToken ct = default);

        /// <summary>
        /// Returns the references of the specified nodes that
        /// meet the criteria specified.
        /// </summary>
        Task<IList<INode>> FindReferencesAsync(
            IList<ExpandedNodeId> nodeIds,
            IList<NodeId> referenceTypeIds,
            bool isInverse,
            bool includeSubtypes,
            CancellationToken ct = default);

        /// <summary>
        /// Returns a display name for a node.
        /// </summary>
        ValueTask<string> GetDisplayTextAsync(
            INode node,
            CancellationToken ct = default);

        /// <summary>
        /// Returns a display name for a node.
        /// </summary>
        ValueTask<string> GetDisplayTextAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default);

        /// <summary>
        /// Returns a display name for the target of a reference.
        /// </summary>
        ValueTask<string> GetDisplayTextAsync(
            ReferenceDescription reference,
            CancellationToken ct = default);

        /// <summary>
        /// Builds the relative path from a type to a node.
        /// </summary>
        NodeId BuildBrowsePath(
            ILocalNode node,
            IList<QualifiedName> browsePath);
    }
}
