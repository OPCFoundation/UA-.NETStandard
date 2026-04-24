// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A client side cache of the server's address space and type
    /// system to allow more efficient use of the server and connectivity
    /// resources.
    /// </summary>
    public interface INodeCache
    {
        /// <summary>
        /// Finds a node on the server. While the underlying object is
        /// of type Node it is not fetching the reference table.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="ct"></param>
        ValueTask<INode> GetNodeAsync(NodeId nodeId,
            CancellationToken ct = default);

        /// <summary>
        /// Finds a set of nodes in the address space of the server.
        /// While the underlying objects returned are of type Node it
        /// is not fetching the reference table.
        /// </summary>
        /// <param name="nodeIds">The node identifier collection.</param>
        /// <param name="ct"></param>
        ValueTask<IReadOnlyList<INode>> GetNodesAsync(
            IReadOnlyList<NodeId> nodeIds, CancellationToken ct = default);

        /// <summary>
        /// Get the value of a variable node. If the node is not a variable
        /// the data value contains the error.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="ct"></param>
        ValueTask<DataValue> GetValueAsync(NodeId nodeId,
            CancellationToken ct = default);

        /// <summary>
        /// Get the values of the specified variable nodes. If the node is
        /// not a variable the data value contains the error.
        /// </summary>
        /// <param name="nodeIds">The node identifier collection.</param>
        /// <param name="ct"></param>
        ValueTask<IReadOnlyList<DataValue>> GetValuesAsync(
            IReadOnlyList<NodeId> nodeIds, CancellationToken ct = default);

        /// <summary>
        /// Find a node by traversing a provided browse path.
        /// </summary>
        /// <param name="nodeId"></param>
        /// <param name="browsePath"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<INode?> GetNodeWithBrowsePathAsync(NodeId nodeId,
            QualifiedNameCollection browsePath, CancellationToken ct = default);

        /// <summary>
        /// Returns the references of the specified node that meet
        /// the criteria specified. The node might not contain references.
        /// </summary>
        /// <param name="nodeId"></param>
        /// <param name="referenceTypeId"></param>
        /// <param name="isInverse"></param>
        /// <param name="includeSubtypes"></param>
        /// <param name="ct"></param>
        ValueTask<IReadOnlyList<INode>> GetReferencesAsync(NodeId nodeId,
            NodeId referenceTypeId, bool isInverse, bool includeSubtypes,
            CancellationToken ct = default);

        /// <summary>
        /// Returns the references of the specified nodes that meet
        /// the criteria specified. The node might not contain references.
        /// </summary>
        /// <param name="nodeIds"></param>
        /// <param name="referenceTypeIds"></param>
        /// <param name="isInverse"></param>
        /// <param name="includeSubtypes"></param>
        /// <param name="ct"></param>
        ValueTask<IReadOnlyList<INode>> GetReferencesAsync(
            IReadOnlyList<NodeId> nodeIds,
            IReadOnlyList<NodeId> referenceTypeIds, bool isInverse,
            bool includeSubtypes, CancellationToken ct = default);

        /// <summary>
        /// Load the type hierarchy of this type into the cache for
        /// efficiently calling <see cref="IsTypeOf(NodeId, NodeId)"/>
        /// </summary>
        /// <param name="typeIds"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask LoadTypeHierarchyAync(IReadOnlyList<NodeId> typeIds,
            CancellationToken ct = default);

        /// <summary>
        /// Determines whether a type is a subtype of another type.
        /// </summary>
        /// <param name="subTypeId">The subtype identifier.</param>
        /// <param name="superTypeId">The supertype identifier.</param>
        /// <returns><c>true</c> if <paramref name="superTypeId"/> is
        /// supertype of <paramref name="subTypeId"/>; otherwise
        /// <c>false</c>. </returns>
        bool IsTypeOf(NodeId subTypeId, NodeId superTypeId);

        /// <summary>
        /// Returns the immediate supertype for the type.
        /// </summary>
        /// <param name="typeId">The type identifier.</param>
        /// <param name="ct"></param>
        /// <returns>The immediate supertype idnetyfier for
        /// <paramref name="typeId"/></returns>
        ValueTask<NodeId> GetSuperTypeAsync(NodeId typeId,
            CancellationToken ct = default);

        /// <summary>
        /// Get built in type of the data type
        /// </summary>
        /// <param name="datatypeId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<BuiltInType> GetBuiltInTypeAsync(NodeId datatypeId,
            CancellationToken ct = default);

        /// <summary>
        /// Removes all nodes from the cache.
        /// </summary>
        void Clear();
    }
}
