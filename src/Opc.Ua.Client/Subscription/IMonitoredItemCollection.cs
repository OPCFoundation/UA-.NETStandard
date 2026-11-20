/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    /// <summary>
    /// Collection of managed monitored items
    /// </summary>
    public interface IMonitoredItemCollection
    {
        /// <summary>
        /// Monitored item count
        /// </summary>
        uint Count { get; }

        /// <summary>
        /// Monitored items
        /// </summary>
        IEnumerable<IMonitoredItem> Items { get; }

        /// <summary>
        /// Try get monitored item by client handle
        /// </summary>
        /// <param name="clientHandle"></param>
        /// <param name="monitoredItem"></param>
        /// <returns></returns>
        bool TryGetMonitoredItemByClientHandle(uint clientHandle,
            out IMonitoredItem? monitoredItem);

        /// <summary>
        /// Try get monitored item by name
        /// </summary>
        /// <param name="name"></param>
        /// <param name="monitoredItem"></param>
        /// <returns></returns>
        bool TryGetMonitoredItemByName(string name,
            out IMonitoredItem? monitoredItem);

        /// <summary>
        /// Try add monitored item
        /// </summary>
        /// <param name="name"></param>
        /// <param name="options"></param>
        /// <param name="monitoredItem"></param>
        /// <returns></returns>
        bool TryAdd(string name,
            IOptionsMonitor<MonitoredItemOptions> options,
            out IMonitoredItem? monitoredItem);

        /// <summary>
        /// Try remove monitored item
        /// </summary>
        /// <param name="clientHandle"></param>
        /// <returns></returns>
        bool TryRemove(uint clientHandle);

        /// <summary>
        /// Update the state of the subscription. This applies the
        /// state provided to the entire subscription, adding and
        /// removing items that are not in the state, as well
        /// as updating any state of items that are in the state.
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        IReadOnlyList<IMonitoredItem> Update(IReadOnlyList<(string Name,
            IOptionsMonitor<MonitoredItemOptions> Options)> state);
    }

    /// <summary>
    /// Optional monitored-item collection capability for requeueing one
    /// failed or otherwise unapplied item after its internal retry budget
    /// has been exhausted.
    /// </summary>
    public interface IMonitoredItemRetryCollection
    {
        /// <summary>
        /// Requeues the current desired state for one failed or unapplied
        /// monitored item.
        /// </summary>
        /// <param name="clientHandle">The monitored-item client handle.</param>
        /// <returns>
        /// <c>true</c> if the item was found and its desired state was requeued.
        /// </returns>
        bool TryRequeue(uint clientHandle);
    }
}
