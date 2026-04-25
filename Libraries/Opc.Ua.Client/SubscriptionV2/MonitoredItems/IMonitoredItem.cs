#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    using System;

    /// <summary>
    /// The current monitored item inside a subscription
    /// </summary>
    public interface IMonitoredItem
    {
        /// <summary>
        /// Name of the item in the subscription
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Order of the item in the subscription
        /// </summary>
        uint Order { get; }

        /// <summary>
        /// The identifier assigned by the server.
        /// </summary>
        uint ServerId { get; }

        /// <summary>
        /// Whether the item has been created on the server.
        /// </summary>
        bool Created { get; }

        /// <summary>
        /// The last error result associated with the item
        /// </summary>
        ServiceResult Error { get; }

        /// <summary>
        /// The filter result of the last change applied.
        /// </summary>
        MonitoringFilterResult? FilterResult { get; }

        /// <summary>
        /// The monitoring mode.
        /// </summary>
        MonitoringMode CurrentMonitoringMode { get; }

        /// <summary>
        /// The sampling interval.
        /// </summary>
        TimeSpan CurrentSamplingInterval { get; }

        /// <summary>
        /// The length of the queue used to buffer values.
        /// </summary>
        uint CurrentQueueSize { get; }

        /// <summary>
        /// The identifier assigned by the client.
        /// </summary>
        uint ClientHandle { get; }
    }
}
#endif
