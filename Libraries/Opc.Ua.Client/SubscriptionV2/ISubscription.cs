#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions
{
    using Opc.Ua.Client.Subscriptions.MonitoredItems;
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Subscription services
    /// </summary>
    public interface ISubscription : IAsyncDisposable
    {
        /// <summary>
        /// Created subscription
        /// </summary>
        bool Created { get; }

        /// <summary>
        /// The current publishing interval on the server
        /// </summary>
        TimeSpan CurrentPublishingInterval { get; }

        /// <summary>
        /// The current priority of the subscription
        /// </summary>
        byte CurrentPriority { get; }

        /// <summary>
        /// The current lifetime count on the server
        /// </summary>
        uint CurrentLifetimeCount { get; }

        /// <summary>
        /// The current keep alive count on the server
        /// </summary>
        uint CurrentKeepAliveCount { get; }

        /// <summary>
        /// The current publishing enabled state
        /// </summary>
        bool CurrentPublishingEnabled { get; }

        /// <summary>
        /// Current max notifications per publish
        /// </summary>
        uint CurrentMaxNotificationsPerPublish { get; }

        /// <summary>
        /// Monitored items
        /// </summary>
        IMonitoredItemCollection MonitoredItems { get; }

        /// <summary>
        /// Tells the server to refresh all conditions being
        /// monitored by the subscription.
        /// </summary>
        /// <param name="ct"></param>
        ValueTask ConditionRefreshAsync(
            CancellationToken ct = default);
    }
}
#endif
