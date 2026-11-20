/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

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
    /// The default implementation allocates capacity for at least one data change per
    /// monitored item, or ten events when no event queue size is specified.
    /// Multi-node streams share that capacity rather than reserving slots per item.
    /// Explicit queue sizes are multiplied by the number of monitored items;
    /// <see cref="MonitoredItems.MonitoredItemOptions.DiscardOldest"/> selects the drop policy.
    /// <see cref="StreamingSubscription.DroppedNotificationCount"/> reports local buffer overflow.
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
        /// <remarks>
        /// All nodes share one bounded local queue. With DiscardOldest enabled, a busy node can evict
        /// a quiet node's only queued value; the stream does not guarantee retention of each node's
        /// latest value. Overflow increments <see cref="StreamingSubscription.DroppedNotificationCount"/>
        /// rather than failing the stream. Use separate single-node streams when per-node retention is required.
        /// </remarks>
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

    /// <summary>
    /// Internal event-subscription hook for consumers that need to
    /// await server-side monitored item creation before proceeding.
    /// </summary>
    internal interface IStreamingSubscriptionReadiness
    {
        /// <summary>
        /// Subscribes to events and invokes <paramref name="onMonitoredItemReady"/>
        /// after the monitored item has been added locally and before
        /// notifications are yielded.
        /// </summary>
        IAsyncEnumerable<EventNotification> SubscribeEventsAsync(
            NodeId notifierId,
            EventFilter filter,
            MonitoredItems.MonitoredItemOptions? options,
            Func<IMonitoredItem, CancellationToken, ValueTask> onMonitoredItemReady,
            CancellationToken ct = default);
    }
}
