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
    /// Monitored item service set
    /// <see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.13"/>
    /// </summary>
    internal interface IMonitoredItemServiceSet
    {
        /// <summary>
        /// Set monitoring
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="subscriptionId"></param>
        /// <param name="monitoringMode"></param>
        /// <param name="monitoredItemIds"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<SetMonitoringModeResponse> SetMonitoringModeAsync(RequestHeader? requestHeader,
            uint subscriptionId, MonitoringMode monitoringMode, UInt32Collection monitoredItemIds,
            CancellationToken ct = default);

        /// <summary>
        /// Create monitored items services
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="subscriptionId"></param>
        /// <param name="timestampsToReturn"></param>
        /// <param name="itemsToCreate"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(RequestHeader? requestHeader,
            uint subscriptionId, TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate, CancellationToken ct = default);

        /// <summary>
        /// Modify monitored item service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="subscriptionId"></param>
        /// <param name="timestampsToReturn"></param>
        /// <param name="itemsToModify"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(RequestHeader? requestHeader,
            uint subscriptionId, TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify, CancellationToken ct = default);

        /// <summary>
        /// Delete monitored items service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="subscriptionId"></param>
        /// <param name="monitoredItemIds"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(RequestHeader? requestHeader,
            uint subscriptionId, UInt32Collection monitoredItemIds, CancellationToken ct = default);
    }
}
