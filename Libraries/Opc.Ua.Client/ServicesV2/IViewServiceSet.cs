#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Services
{
    using Opc.Ua;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// View service set
    /// <see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.9"/>
    /// </summary>
    public interface IViewServiceSet
    {
        /// <summary>
        /// Browse service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="view"></param>
        /// <param name="requestedMaxReferencesPerNode"></param>
        /// <param name="nodesToBrowse"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<BrowseResponse> BrowseAsync(RequestHeader? requestHeader,
            ViewDescription? view, uint requestedMaxReferencesPerNode,
            ArrayOf<BrowseDescription> nodesToBrowse, CancellationToken ct = default);

        /// <summary>
        /// Browse next service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="releaseContinuationPoints"></param>
        /// <param name="continuationPoints"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<BrowseNextResponse> BrowseNextAsync(RequestHeader? requestHeader,
            bool releaseContinuationPoints, ArrayOf<ByteString> continuationPoints,
            CancellationToken ct = default);

        /// <summary>
        /// Translate browse path service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="browsePaths"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            RequestHeader? requestHeader, ArrayOf<BrowsePath> browsePaths,
            CancellationToken ct = default);

        /// <summary>
        /// Register nodes service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="nodesToRegister"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<RegisterNodesResponse> RegisterNodesAsync(RequestHeader? requestHeader,
            ArrayOf<NodeId> nodesToRegister, CancellationToken ct = default);

        /// <summary>
        /// Unregister nodes service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="nodesToUnregister"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<UnregisterNodesResponse> UnregisterNodesAsync(RequestHeader? requestHeader,
            ArrayOf<NodeId> nodesToUnregister, CancellationToken ct = default);
    }
}
#endif
