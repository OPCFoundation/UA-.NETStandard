// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Extension methods for <see cref="INodeCache"/> to provide the
    /// convenience methods needed by the type system, bridging to the
    /// <see cref="IAsyncNodeTable"/> and <see cref="INodeCache"/> methods.
    /// </summary>
    internal static class NodeCacheExtensions
    {
        /// <summary>
        /// Get a node by its <see cref="NodeId"/>.
        /// </summary>
        public static async ValueTask<INode> GetNodeAsync(
            this INodeCache cache, NodeId nodeId,
            CancellationToken ct = default)
        {
            return await ((IAsyncNodeTable)cache).FindAsync(
                new ExpandedNodeId(nodeId), ct).ConfigureAwait(false)
                ?? throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown);
        }

        /// <summary>
        /// Get references for a single node.
        /// </summary>
        public static async ValueTask<ArrayOf<INode>> GetReferencesAsync(
            this INodeCache cache,
            NodeId nodeId, NodeId referenceTypeId,
            bool isInverse, bool includeSubtypes,
            CancellationToken ct = default)
        {
            return await cache.FindReferencesAsync(
                new ExpandedNodeId(nodeId), referenceTypeId,
                isInverse, includeSubtypes, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Get references for multiple nodes.
        /// </summary>
        public static async ValueTask<ArrayOf<INode>> GetReferencesAsync(
            this INodeCache cache,
            IReadOnlyList<NodeId> nodeIds,
            ArrayOf<NodeId> referenceTypeIds,
            bool isInverse, bool includeSubtypes,
            CancellationToken ct = default)
        {
            ArrayOf<ExpandedNodeId> expanded = nodeIds
                .Select(static id => new ExpandedNodeId(id)).ToArray();
            return await cache.FindReferencesAsync(
                expanded, referenceTypeIds,
                isInverse, includeSubtypes, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Read the value attribute of a single variable node.
        /// Falls back to fetching the full node when the cache
        /// does not provide a dedicated value-read method.
        /// </summary>
        public static async ValueTask<DataValue> GetValueAsync(
            this INodeCache cache, NodeId nodeId,
            CancellationToken ct = default)
        {
            var node = await cache.FetchNodeAsync(
                new ExpandedNodeId(nodeId), ct)
                .ConfigureAwait(false);
            if (node is VariableNode v)
            {
                return new DataValue(v.Value, StatusCodes.Good);
            }
            return new DataValue(StatusCodes.BadNodeClassInvalid);
        }

        /// <summary>
        /// Read the value attribute of multiple variable nodes.
        /// Falls back to fetching the full nodes when the cache
        /// does not provide a dedicated value-read method.
        /// </summary>
        public static async ValueTask<ArrayOf<DataValue>> GetValuesAsync(
            this INodeCache cache,
            IReadOnlyList<NodeId> nodeIds,
            CancellationToken ct = default)
        {
            ArrayOf<ExpandedNodeId> expanded = nodeIds
                .Select(static id => new ExpandedNodeId(id)).ToArray();
            var nodes = await cache.FetchNodesAsync(expanded, ct)
                .ConfigureAwait(false);
            return nodes.ConvertAll(static n =>
                n is VariableNode v
                    ? new DataValue(v.Value, StatusCodes.Good)
                    : new DataValue(StatusCodes.BadNodeClassInvalid));
        }
    }
}
