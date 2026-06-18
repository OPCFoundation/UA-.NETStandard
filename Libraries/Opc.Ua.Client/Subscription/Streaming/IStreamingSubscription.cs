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
using System.Threading;

namespace Opc.Ua.Client.Subscriptions.Streaming
{
    /// <summary>
    /// Lazy-created, shared subscription that exposes data change
    /// and event notifications as <see cref="IAsyncEnumerable{T}"/>
    /// streams.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The underlying OPC UA subscription is created on first use
    /// (via the configured <see cref="ISubscriptionManager"/>) and
    /// monitored items are added/removed as consumers
    /// subscribe/dispose enumerators.
    /// </para>
    /// <para>
    /// Each call to <c>SubscribeXxxAsync</c> returns an
    /// <see cref="IAsyncEnumerable{T}"/> backed by a bounded channel.
    /// Disposing the enumerator removes the monitored item.
    /// </para>
    /// <para>
    /// State machine helpers like
    /// <see cref="StreamingSubscriptionExtensions.TakeUntilAsync{T}"/>
    /// make this API natural for short-lived subscribe-and-wait
    /// scenarios (e.g., wait for an alarm to acknowledge).
    /// </para>
    /// </remarks>
    public interface IStreamingSubscription : IAsyncDisposable
    {
        /// <summary>
        /// Subscribes to data changes on a single node and returns the
        /// notifications as an async stream. Disposing the returned
        /// enumerator removes the monitored item.
        /// </summary>
        IAsyncEnumerable<DataValueChange> SubscribeDataChangesAsync(
            NodeId nodeId,
            MonitoredItems.MonitoredItemOptions? options = null,
            CancellationToken ct = default);

        /// <summary>
        /// Subscribes to data changes on multiple nodes and returns
        /// a merged async stream.
        /// </summary>
        IAsyncEnumerable<DataValueChange> SubscribeDataChangesAsync(
            IReadOnlyList<NodeId> nodeIds,
            MonitoredItems.MonitoredItemOptions? options = null,
            CancellationToken ct = default);

        /// <summary>
        /// Subscribes to events from a notifier and returns the
        /// notifications as an async stream.
        /// </summary>
        IAsyncEnumerable<EventNotification> SubscribeEventsAsync(
            NodeId notifierId,
            EventFilter filter,
            MonitoredItems.MonitoredItemOptions? options = null,
            CancellationToken ct = default);
    }
}
