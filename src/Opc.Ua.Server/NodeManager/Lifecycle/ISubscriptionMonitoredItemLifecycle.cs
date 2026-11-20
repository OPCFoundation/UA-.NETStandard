// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server
{
    /// <summary>
    /// Checks exact subscription membership during generation handoff.
    /// </summary>
    internal interface ISubscriptionMonitoredItemLifecycle
    {
        /// <summary>
        /// Returns whether the monitored item is still owned by the subscription.
        /// </summary>
        bool ContainsMonitoredItem(IMonitoredItem monitoredItem);

        /// <summary>
        /// Returns whether the subscription has monitored items owned by the NodeManager.
        /// </summary>
        bool HasMonitoredItems(IAsyncNodeManager nodeManager);
    }
}
