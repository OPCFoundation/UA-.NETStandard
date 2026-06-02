/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    /// <summary>
    /// Non sdk interface that allows monitored items to signal
    /// state changes to the monitored item manager.
    /// </summary>
    internal interface IMonitoredItemContext
    {
        /// <summary>
        /// Issue an OPC UA Part 9 §5.5.7 <c>ConditionRefresh2</c>
        /// service call for the monitored item with the supplied
        /// server-side <paramref name="monitoredItemServerId"/>. The
        /// context already knows the subscription id and method service
        /// set, so callers only forward their own server-side handle.
        /// </summary>
        /// <param name="monitoredItemServerId">Server-assigned monitored
        /// item id (<see cref="IMonitoredItem.ServerId"/>). The item
        /// must have been created on the server.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask ConditionRefreshAsync(
            uint monitoredItemServerId,
            CancellationToken ct = default);

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
        bool NotifyItemChangeResult(
            MonitoredItem monitoredItem,
            int retryCount,
            MonitoredItemOptions source,
            ServiceResult serviceResult,
            bool final,
            MonitoringFilterResult? filterResult);

        /// <summary>
        /// Notify item change
        /// </summary>
        /// <param name="monitoredItem"></param>
        /// <param name="itemDisposed"></param>
        void NotifyItemChange(
            MonitoredItem monitoredItem,
            bool itemDisposed = false);

        /// <summary>
        /// Resolve a sibling monitored item by client handle. Used by
        /// <see cref="IMonitoredItem.TriggeringItem"/> to expose the
        /// triggering relationship as a concrete item reference rather
        /// than a raw handle, and by reverse lookups that find the set
        /// of items triggered by a given item.
        /// </summary>
        /// <param name="clientHandle">Client-assigned handle of the
        /// item to resolve.</param>
        /// <param name="item">The resolved item, or <c>null</c> if no
        /// item with that handle is currently registered.</param>
        bool TryGetMonitoredItemByClientHandle(
            uint clientHandle,
            [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out IMonitoredItem? item);
    }
}
