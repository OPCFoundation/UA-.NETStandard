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

using System.Collections.Generic;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using V2MonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItem;
using V2MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

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

        public bool NotifyItemChangeResult(V2MonitoredItem monitoredItem,
            int retryCount, V2MonitoredItemOptions source,
            ServiceResult serviceResult, bool final,
            MonitoringFilterResult? filterResult)
        {
            NotifyItemChangeResultCalls.Add(new NotifyItemChangeResultCall(
                monitoredItem, retryCount, source, serviceResult, final,
                filterResult));
            return NotifyItemChangeResultReturnValue;
        }

        public void NotifyItemChange(V2MonitoredItem monitoredItem,
            bool itemDisposed = false)
        {
            NotifyItemChangeCalls.Add(new NotifyItemChangeCall(monitoredItem,
                itemDisposed));
        }

        public override string ToString()
        {
            return ToStringValue ?? base.ToString()!;
        }

        internal readonly record struct NotifyItemChangeResultCall(
            V2MonitoredItem MonitoredItem, int RetryCount,
            V2MonitoredItemOptions Source, ServiceResult ServiceResult,
            bool Final, MonitoringFilterResult? FilterResult);

        internal readonly record struct NotifyItemChangeCall(
            V2MonitoredItem MonitoredItem, bool ItemDisposed);
    }
}
