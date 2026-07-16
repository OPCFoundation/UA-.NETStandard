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

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client.Subscriptions.Fakes
{
    /// <summary>
    /// Hand-rolled fake for <see cref="IMonitoredItemContext"/>. Records
    /// every invocation. Replaces <c>Mock&lt;IMonitoredItemContext&gt;</c>.
    /// </summary>
    internal sealed class FakeMonitoredItemContext : IMonitoredItemContext
    {
        /// <summary>
        /// Recorded calls to <see cref="NotifyItemChangeResult"/>.
        /// </summary>
        public List<NotifyItemChangeResultCall> NotifyItemChangeResultCalls { get; } = [];

        /// <summary>
        /// Recorded calls to <see cref="NotifyItemChange"/>.
        /// </summary>
        public List<NotifyItemChangeCall> NotifyItemChangeCalls { get; } = [];

        /// <summary>
        /// Optional value to return from <see cref="NotifyItemChangeResult"/>.
        /// Defaults to <c>true</c>.
        /// </summary>
        public bool NotifyItemChangeResultReturnValue { get; set; } = true;

        /// <summary>
        /// Optional override for <see cref="ToString"/>.
        /// </summary>
        public string? ToStringValue { get; set; }

        /// <summary>
        /// Optional override for ConditionRefreshAsync. When unset, the
        /// fake records the call and completes synchronously.
        /// </summary>
        public Func<uint, CancellationToken, ValueTask>? OnConditionRefreshAsync { get; set; }

        public List<ConditionRefreshCall> ConditionRefreshCalls { get; } = [];

        /// <summary>
        /// Lookup table for <see cref="TryGetMonitoredItemByClientHandle"/>.
        /// Tests populate this directly when they need the
        /// <see cref="IMonitoredItem.TriggeringItems"/> projection to
        /// resolve.
        /// </summary>
        public Dictionary<uint, IMonitoredItem> ItemsByClientHandle { get; } = [];

        /// <summary>
        /// Lookup table for <see cref="TryGetMonitoredItemByName"/>.
        /// Tests populate this directly when they need
        /// <see cref="IMonitoredItem.TriggeringItems"/> /
        /// <see cref="IMonitoredItem.TriggeredItems"/> to resolve names.
        /// </summary>
        public Dictionary<string, IMonitoredItem> ItemsByName { get; } = [];

        public ValueTask ConditionRefreshAsync(
            uint monitoredItemServerId,
            CancellationToken ct = default)
        {
            ConditionRefreshCalls.Add(new ConditionRefreshCall(monitoredItemServerId, ct));
            if (OnConditionRefreshAsync != null)
            {
                return OnConditionRefreshAsync(monitoredItemServerId, ct);
            }
            return default;
        }

        internal readonly record struct ConditionRefreshCall(
            uint MonitoredItemServerId, CancellationToken Ct);

        public bool NotifyItemChangeResult(
            MonitoredItems.MonitoredItem monitoredItem,
            int retryCount,
            MonitoredItems.MonitoredItemOptions source,
            ServiceResult serviceResult,
            bool final,
            MonitoringFilterResult? filterResult)
        {
            NotifyItemChangeResultCalls.Add(new NotifyItemChangeResultCall(
                monitoredItem, retryCount, source, serviceResult, final,
                filterResult));
            return NotifyItemChangeResultReturnValue;
        }

        public void NotifyItemChange(
            MonitoredItems.MonitoredItem monitoredItem,
            bool itemDisposed = false)
        {
            NotifyItemChangeCalls.Add(new NotifyItemChangeCall(monitoredItem,
                itemDisposed));
        }

        public bool TryGetMonitoredItemByClientHandle(
            uint clientHandle,
            [MaybeNullWhen(false)] out IMonitoredItem? item)
        {
            return ItemsByClientHandle.TryGetValue(clientHandle, out item);
        }

        public bool TryGetMonitoredItemByName(
            string name,
            [MaybeNullWhen(false)] out IMonitoredItem? item)
        {
            return ItemsByName.TryGetValue(name, out item);
        }

        /// <inheritdoc/>
        public IEnumerable<IMonitoredItem> Items => ItemsByClientHandle.Values;

        /// <summary>
        /// Recorded calls to <see cref="EnqueueTriggeringDelta"/>.
        /// Tests inspect this list to verify per-item triggering
        /// deltas were enqueued in the expected order.
        /// </summary>
        public List<EnqueueTriggeringDeltaCall> EnqueueTriggeringDeltaCalls { get; } = [];

        public void EnqueueTriggeringDelta(
            IMonitoredItem triggeredItem,
            IReadOnlyList<string> addedTriggeringNames,
            IReadOnlyList<string> removedTriggeringNames)
        {
            EnqueueTriggeringDeltaCalls.Add(new EnqueueTriggeringDeltaCall(
                triggeredItem, addedTriggeringNames, removedTriggeringNames));
        }

        internal readonly record struct EnqueueTriggeringDeltaCall(
            IMonitoredItem TriggeredItem,
            IReadOnlyList<string> AddedTriggeringNames,
            IReadOnlyList<string> RemovedTriggeringNames);

        public override string ToString()
        {
            return ToStringValue ?? base.ToString()!;
        }

        internal readonly record struct NotifyItemChangeResultCall(
            MonitoredItems.MonitoredItem MonitoredItem, int RetryCount,
            MonitoredItems.MonitoredItemOptions Source, ServiceResult ServiceResult,
            bool Final, MonitoringFilterResult? FilterResult);

        internal readonly record struct NotifyItemChangeCall(
            MonitoredItems.MonitoredItem MonitoredItem, bool ItemDisposed);
    }
}
