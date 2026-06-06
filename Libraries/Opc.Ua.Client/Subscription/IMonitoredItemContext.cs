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

using System.Collections.Generic;
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
        /// Resolve a sibling monitored item by client handle. Used in
        /// the dispatch path where notifications arrive keyed by
        /// client handle.
        /// </summary>
        /// <param name="clientHandle">Client-assigned handle of the
        /// item to resolve.</param>
        /// <param name="item">The resolved item, or <c>null</c> if no
        /// item with that handle is currently registered.</param>
        bool TryGetMonitoredItemByClientHandle(
            uint clientHandle,
            [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out IMonitoredItem? item);

        /// <summary>
        /// Resolve a sibling monitored item by stable name. Used by
        /// <see cref="IMonitoredItem.TriggeringItems"/> /
        /// <see cref="IMonitoredItem.TriggeredItems"/> to expose
        /// triggering relationships as concrete item references rather
        /// than raw names, and by the engine to resolve triggering
        /// operations queued before the named item exists in the
        /// subscription.
        /// </summary>
        /// <param name="name">Manager-unique monitored-item name.</param>
        /// <param name="item">The resolved item, or <c>null</c> if no
        /// item with that name is currently registered.</param>
        bool TryGetMonitoredItemByName(
            string name,
            [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out IMonitoredItem? item);

        /// <summary>
        /// Enumerate every monitored item currently registered with
        /// the owning subscription. Used by
        /// <see cref="IMonitoredItem.TriggeredItems"/> to find the
        /// siblings whose runtime desired triggering set names this
        /// item. The returned enumerable is a snapshot — siblings
        /// may be added or removed between successive reads.
        /// </summary>
        IEnumerable<IMonitoredItem> Items { get; }

        /// <summary>
        /// Enqueue a per-edge triggering delta originating from an
        /// item-level event (options change with a different
        /// <c>TriggeredByNames</c>, or a <c>Reset</c> that needs to
        /// replay the entire desired set after a recreate). The
        /// triggering items are resolved by name against the owning
        /// subscription's collection; entries that don't currently
        /// resolve are still enqueued and Phase 4 of
        /// <c>ApplyChangesAsync</c> retries them on subsequent passes
        /// (with a bounded retry budget) until the named item appears
        /// or the budget is exhausted.
        /// </summary>
        /// <param name="triggeredItem">The triggered item.</param>
        /// <param name="addedTriggeringNames">
        /// Names to add the triggered item under (i.e. the triggering
        /// items that should now report through this triggered item).
        /// </param>
        /// <param name="removedTriggeringNames">
        /// Names from which the triggered item is removed.
        /// </param>
        void EnqueueTriggeringDelta(
            IMonitoredItem triggeredItem,
            IReadOnlyList<string> addedTriggeringNames,
            IReadOnlyList<string> removedTriggeringNames);
    }
}
