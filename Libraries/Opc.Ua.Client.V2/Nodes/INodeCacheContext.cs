// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes
{
    using Opc.Ua.Client.Sessions;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Interface between node cache and session
    /// </summary>
    internal interface INodeCacheContext
    {
        /// <summary>
        /// Gets the table of namespace uris known to the server.
        /// </summary>
        NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object
        /// collection.
        /// </summary>
        /// <remarks>
        /// If the nodeclass for the nodes in nodeIdCollection is already known
        /// and passed as nodeClass, reads only values of required attributes.
        /// Otherwise NodeClass.Unspecified should be used.
        /// </remarks>
        /// <param name="header"></param>
        /// <param name="nodeIds">The nodeId collection to read.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The node collection and associated errors.</returns>
        ValueTask<ResultSet<Node>> FetchNodesAsync(RequestHeader? header,
            IReadOnlyList<NodeId> nodeIds, CancellationToken ct = default);

        /// <summary>
        /// Reads the values for the node attributes and returns a node object.
        /// </summary>
        /// <remarks>
        /// If the nodeclass is known, only the supported attribute values are
        /// read.
        /// </remarks>
        /// <param name="header"></param>
        /// <param name="nodeId">The nodeId.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        ValueTask<Node> FetchNodeAsync(RequestHeader? header, NodeId nodeId,
            CancellationToken ct = default);

        /// <summary>
        /// Fetches all references for the specified node.
        /// </summary>
        /// <param name="header"></param>
        /// <param name="nodeId">The node id.</param>
        /// <param name="ct"></param>
        ValueTask<ReferenceDescriptionCollection> FetchReferencesAsync(
            RequestHeader? header, NodeId nodeId, CancellationToken ct = default);

        /// <summary>
        /// Fetches all references for the specified nodes.
        /// </summary>
        /// <param name="header"></param>
        /// <param name="nodeIds">The node id collection.</param>
        /// <param name="ct"></param>
        /// <returns>A list of reference collections and the errors reported by the
        /// server.</returns>
        ValueTask<ResultSet<ReferenceDescriptionCollection>> FetchReferencesAsync(
            RequestHeader? header, IReadOnlyList<NodeId> nodeIds,
            CancellationToken ct = default);

        /// <summary>
        /// Reads the value for a node.
        /// </summary>
        /// <param name="header"></param>
        /// <param name="nodeId">The node Id.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        ValueTask<DataValue> FetchValueAsync(RequestHeader? header, NodeId nodeId,
            CancellationToken ct = default);

        /// <summary>
        /// Reads the values for a node collection. Returns diagnostic errors.
        /// </summary>
        /// <param name="header"></param>
        /// <param name="nodeIds">The node Id.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        ValueTask<ResultSet<DataValue>> FetchValuesAsync(RequestHeader? header,
            IReadOnlyList<NodeId> nodeIds, CancellationToken ct = default);
    }
}
