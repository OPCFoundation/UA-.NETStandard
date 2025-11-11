// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Interface between node cache and a session
    /// </summary>
    public interface INodeCacheContext
    {
        /// <summary>
        /// Gets the table of namespace uris known to the server.
        /// </summary>
        NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// Get the table with the server uris known to the server.
        /// </summary>
        StringTable ServerUris { get; }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object
        /// collection.
        /// </summary>
        /// <remarks>
        /// If the nodeclass for the nodes in nodeIdCollection is already known
        /// and passed as nodeClass, reads only values of required attributes.
        /// Otherwise NodeClass.Unspecified should be used.
        /// </remarks>
        /// <param name="requestHeader">Request header to use</param>
        /// <param name="nodeIds">The nodeId collection to read.</param>
        /// <param name="skipOptionalAttributes">If optional attributes should
        /// not be read.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The node collection and associated errors.</returns>
        ValueTask<ResultSet<Node>> FetchNodesAsync(
            RequestHeader? requestHeader,
            IReadOnlyList<NodeId> nodeIds,
            bool skipOptionalAttributes = false,
            CancellationToken ct = default);

        /// <summary>
        /// Reads the values for the node attributes and returns a node object
        /// collection.
        /// </summary>
        /// <remarks>
        /// If the nodeclass for the nodes in nodeIdCollection is already known
        /// and passed as nodeClass, reads only values of required attributes.
        /// Otherwise NodeClass.Unspecified should be used.
        /// </remarks>
        /// <param name="requestHeader">Request header to use</param>
        /// <param name="nodeIds">The nodeId collection to read.</param>
        /// <param name="nodeClass">The nodeClass of all nodes in the collection.
        /// Set to <c>NodeClass.Unspecified</c> if the nodeclass is unknown.</param>
        /// <param name="skipOptionalAttributes">Set to <c>true</c> if optional
        /// attributes should omitted.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The node collection and associated errors.</returns>
        ValueTask<ResultSet<Node>> FetchNodesAsync(
            RequestHeader? requestHeader,
            IReadOnlyList<NodeId> nodeIds,
            NodeClass nodeClass,
            bool skipOptionalAttributes = false,
            CancellationToken ct = default);

        /// <summary>
        /// Reads the values for the node attributes and returns a node object.
        /// </summary>
        /// <remarks>
        /// If the nodeclass is known, only the supported attribute values are
        /// read.
        /// </remarks>
        /// <param name="requestHeader">Request header to use</param>
        /// <param name="nodeId">The nodeId.</param>
        /// <param name="nodeClass">The nodeclass of the node to read.</param>
        /// <param name="skipOptionalAttributes">Skip reading optional attributes.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        ValueTask<Node> FetchNodeAsync(
            RequestHeader? requestHeader,
            NodeId nodeId,
            NodeClass nodeClass = NodeClass.Unspecified,
            bool skipOptionalAttributes = false,
            CancellationToken ct = default);

        /// <summary>
        /// Fetches all references for the specified node.
        /// </summary>
        /// <param name="requestHeader">Request header to use</param>
        /// <param name="nodeId">The node id.</param>
        /// <param name="ct"></param>
        ValueTask<ReferenceDescriptionCollection> FetchReferencesAsync(
            RequestHeader? requestHeader,
            NodeId nodeId,
            CancellationToken ct = default);

        /// <summary>
        /// Fetches all references for the specified nodes.
        /// </summary>
        /// <param name="requestHeader">Request header to use</param>
        /// <param name="nodeIds">The node id collection.</param>
        /// <param name="ct"></param>
        /// <returns>A list of reference collections and the errors reported by the
        /// server.</returns>
        ValueTask<ResultSet<ReferenceDescriptionCollection>> FetchReferencesAsync(
            RequestHeader? requestHeader,
            IReadOnlyList<NodeId> nodeIds,
            CancellationToken ct = default);

        /// <summary>
        /// Reads the value for a node.
        /// </summary>
        /// <param name="requestHeader">Request header to use</param>
        /// <param name="nodeId">The node Id.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        ValueTask<DataValue> FetchValueAsync(
            RequestHeader? requestHeader,
            NodeId nodeId,
            CancellationToken ct = default);

        /// <summary>
        /// Reads the values for a node collection. Returns diagnostic errors.
        /// </summary>
        /// <param name="requestHeader">Request header to use</param>
        /// <param name="nodeIds">The node Id.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        ValueTask<ResultSet<DataValue>> FetchValuesAsync(
            RequestHeader? requestHeader,
            IReadOnlyList<NodeId> nodeIds,
            CancellationToken ct = default);
    }
}
