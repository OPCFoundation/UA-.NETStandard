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

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// <para>
    /// Data value change observed on a monitored item during the V2
    /// publish dispatch.
    /// </para>
    /// <para>
    /// Lifetime note (pooled notifications): when the V2 subscription
    /// manager has <see cref="ISubscriptionManager.PoolNotifications"/>
    /// enabled, the underlying <c>MonitoredItemNotification</c> that
    /// supplied this struct's fields is recycled back to its
    /// activator pool after the handler returns. This struct's
    /// references (<see cref="Value"/>, <see cref="DiagnosticInfo"/>)
    /// project the inner <c>DataValue</c> / <c>DiagnosticInfo</c>
    /// instances directly — those are not themselves pooled (out of
    /// scope), so a handler may safely copy this struct by value and
    /// continue using <see cref="Value"/> after the call. Pool-aware
    /// projection audit: confirmed safe — no reference to a pooled
    /// instance is surfaced.
    /// </para>
    /// </summary>
    /// <param name="MonitoredItem"></param>
    /// <param name="Value"></param>
    /// <param name="DiagnosticInfo"></param>
    public record struct DataValueChange(IMonitoredItem? MonitoredItem,
        DataValue Value, DiagnosticInfo? DiagnosticInfo);

    /// <summary>
    /// <para>
    /// Event notification observed during the V2 publish dispatch.
    /// </para>
    /// <para>
    /// Lifetime note (pooled notifications): when the V2 subscription
    /// manager has <see cref="ISubscriptionManager.PoolNotifications"/>
    /// enabled, the underlying <c>EventFieldList</c> is recycled back
    /// to its activator pool after the handler returns. The
    /// <see cref="Fields"/> property is captured by value as an
    /// <see cref="ArrayOf{T}"/> wrapping the original event-fields
    /// array. Arrays are not pooled in this design (out of scope), so
    /// the captured backing array survives the recycle. Pool-aware
    /// projection audit: confirmed safe — no reference to a pooled
    /// instance is surfaced.
    /// </para>
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
