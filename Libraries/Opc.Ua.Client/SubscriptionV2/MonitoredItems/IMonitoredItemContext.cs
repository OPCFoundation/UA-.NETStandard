#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    /// <summary>
    /// Non sdk interface that allows monitored items to signal
    /// state changes to the monitored item manager.
    /// </summary>
    internal interface IMonitoredItemContext
    {
        /// <summary>
        /// Notify item change results. This includes intermittent
        /// errors trying to apply the monitored item options.
        /// </summary>
        /// <param name="monitoredItem"></param>
        /// <param name="retryCount"></param>
        /// <param name="source"></param>
        /// <param name="serviceResult"></param>
        /// <param name="final"></param>
        /// <param name="filterResult"></param>
        bool NotifyItemChangeResult(MonitoredItem monitoredItem,
            int retryCount, MonitoredItemOptions source,
            ServiceResult serviceResult, bool final,
            MonitoringFilterResult? filterResult);

        /// <summary>
        /// Notify item change
        /// </summary>
        /// <param name="monitoredItem"></param>
        /// <param name="itemDisposed"></param>
        void NotifyItemChange(MonitoredItem monitoredItem,
            bool itemDisposed = false);
    }
}
#endif
