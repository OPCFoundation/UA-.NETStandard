#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions
{
    using Opc.Ua.Client.Subscriptions.MonitoredItems;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// Data Value change notification
    /// </summary>
    /// <param name="MonitoredItem"></param>
    /// <param name="Value"></param>
    /// <param name="DiagnosticInfo"></param>
    public record struct DataValueChange(IMonitoredItem? MonitoredItem,
        DataValue Value, DiagnosticInfo? DiagnosticInfo);

    /// <summary>
    /// Event notification
    /// </summary>
    /// <param name="MonitoredItem"></param>
    /// <param name="Fields"></param>
    public record struct EventNotification(IMonitoredItem? MonitoredItem,
        ArrayOf<Variant> Fields);

    /// <summary>
    /// Notification handler
    /// </summary>
    public interface ISubscriptionNotificationHandler
    {
        /// <summary>
        /// Process data change notification
        /// </summary>
        /// <param name="subscription"></param>
        /// <param name="sequenceNumber"></param>
        /// <param name="publishTime"></param>
        /// <param name="notification"></param>
        /// <param name="publishStateMask"></param>
        /// <param name="stringTable"></param>
        /// <returns></returns>
        ValueTask OnDataChangeNotificationAsync(ISubscription subscription,
            uint sequenceNumber, DateTime publishTime,
            ReadOnlyMemory<DataValueChange> notification,
            PublishState publishStateMask, IReadOnlyList<string> stringTable);

        /// <summary>
        /// Process event notification
        /// </summary>
        /// <param name="subscription"></param>
        /// <param name="sequenceNumber"></param>
        /// <param name="publishTime"></param>
        /// <param name="notification"></param>
        /// <param name="publishStateMask"></param>
        /// <param name="stringTable"></param>
        /// <returns></returns>
        ValueTask OnEventDataNotificationAsync(ISubscription subscription,
            uint sequenceNumber, DateTime publishTime,
            ReadOnlyMemory<EventNotification> notification,
            PublishState publishStateMask, IReadOnlyList<string> stringTable);

        /// <summary>
        /// Process keep alive notification
        /// </summary>
        /// <param name="subscription"></param>
        /// <param name="sequenceNumber"></param>
        /// <param name="publishTime"></param>
        /// <param name="publishStateMask"></param>
        /// <returns></returns>
        ValueTask OnKeepAliveNotificationAsync(ISubscription subscription,
            uint sequenceNumber, DateTime publishTime,
            PublishState publishStateMask);
    }
}
#endif
