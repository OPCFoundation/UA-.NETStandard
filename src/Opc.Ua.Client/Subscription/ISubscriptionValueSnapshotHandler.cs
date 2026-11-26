// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Receives current values read when an opted-in subscription cannot use ResendData.
    /// Implement alongside <see cref="ISubscriptionNotificationHandler"/>.
    /// </summary>
    public interface ISubscriptionValueSnapshotHandler
    {
        /// <summary>
        /// Processes a recovery snapshot for a monitored item on the logical subscription.
        /// The value is not a Publish notification and has no sequence number or acknowledgement.
        /// </summary>
        ValueTask OnValueSnapshotAsync(
            ISubscription subscription,
            IMonitoredItem monitoredItem,
            DataValue value,
            CancellationToken ct);
    }
}
