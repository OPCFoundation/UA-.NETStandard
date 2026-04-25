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
    /// Node management service set
    /// <see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.8"/>
    /// </summary>
    public interface INodeManagementServiceSet
    {
        /// <summary>
        /// Add nodes service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="nodesToAdd"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<AddNodesResponse> AddNodesAsync(RequestHeader? requestHeader,
            ArrayOf<AddNodesItem> nodesToAdd, CancellationToken ct = default);

        /// <summary>
        /// Add references service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="referencesToAdd"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<AddReferencesResponse> AddReferencesAsync(RequestHeader? requestHeader,
            ArrayOf<AddReferencesItem> referencesToAdd, CancellationToken ct = default);

        /// <summary>
        /// Delete nodes service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="nodesToDelete"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<DeleteNodesResponse> DeleteNodesAsync(RequestHeader? requestHeader,
            ArrayOf<DeleteNodesItem> nodesToDelete, CancellationToken ct = default);

        /// <summary>
        /// Delete references service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="referencesToDelete"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<DeleteReferencesResponse> DeleteReferencesAsync(RequestHeader? requestHeader,
            ArrayOf<DeleteReferencesItem> referencesToDelete, CancellationToken ct = default);
    }
}
#endif
