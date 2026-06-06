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
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Subscription services
    /// </summary>
    public interface ISubscription : IAsyncDisposable
    {
        /// <summary>
        /// Created subscription
        /// </summary>
        bool Created { get; }

        /// <summary>
        /// The current publishing interval on the server
        /// </summary>
        TimeSpan CurrentPublishingInterval { get; }

        /// <summary>
        /// The current priority of the subscription
        /// </summary>
        byte CurrentPriority { get; }

        /// <summary>
        /// The current lifetime count on the server
        /// </summary>
        uint CurrentLifetimeCount { get; }

        /// <summary>
        /// The current keep alive count on the server
        /// </summary>
        uint CurrentKeepAliveCount { get; }

        /// <summary>
        /// The current publishing enabled state
        /// </summary>
        bool CurrentPublishingEnabled { get; }

        /// <summary>
        /// Current max notifications per publish
        /// </summary>
        uint CurrentMaxNotificationsPerPublish { get; }

        /// <summary>
        /// Monitored items
        /// </summary>
        IMonitoredItemCollection MonitoredItems { get; }

        /// <summary>
        /// Number of missing notification messages detected by the
        /// gap-walking sequence-number tracker for this subscription.
        /// Each missing slot triggers a republish attempt — see
        /// <see cref="RepublishMessageCount"/>.
        /// </summary>
        long MissingMessageCount { get; }

        /// <summary>
        /// Number of republish requests issued for this subscription
        /// (counts every attempt, regardless of whether the server still
        /// holds the message in its retransmission queue).
        /// </summary>
        long RepublishMessageCount { get; }

        /// <summary>
        /// Tells the server to refresh all conditions being
        /// monitored by the subscription.
        /// </summary>
        /// <param name="ct"></param>
        ValueTask ConditionRefreshAsync(
            CancellationToken ct = default);

        /// <summary>
        /// Mark this subscription as durable on the server (OPC UA Part 4
        /// §5.13.9 <c>SetSubscriptionDurable</c>). A durable subscription
        /// retains its monitored item state and message queue across
        /// session disconnects for the requested
        /// <paramref name="lifetime"/>, so a later
        /// transfer-on-load can take over without losing buffered
        /// notifications.
        /// </summary>
        /// <param name="lifetime">Requested lifetime as a
        /// <see cref="TimeSpan"/>. The server may revise downwards.
        /// Whole hours are sent on the wire (the
        /// <c>SetSubscriptionDurable</c> service uses an hour granularity);
        /// sub-hour components round up to the next whole hour.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The server-revised lifetime as a
        /// <see cref="TimeSpan"/> (whole-hour precision).</returns>
        /// <exception cref="ServiceResultException">Raised when the
        /// subscription is not yet created on the server, or when the
        /// server rejects the call (e.g. it has monitored items already
        /// — per spec <c>SetSubscriptionDurable</c> must be called
        /// before any items are added).</exception>
        ValueTask<TimeSpan> SetAsDurableAsync(
            TimeSpan lifetime,
            CancellationToken ct = default);

        /// <summary>
        /// Configure triggering relationships between monitored items
        /// in this subscription (OPC UA Part 4 §5.13.5 SetTriggering).
        /// Adds and/or removes triggering links from the given
        /// <paramref name="triggeringItem"/> to the supplied
        /// <paramref name="linksToAdd"/> / <paramref name="linksToRemove"/>
        /// triggered items. The call is queued into the subscription's
        /// batched apply pipeline: multiple imperative calls and
        /// declarative options changes that touch the same triggering
        /// item are coalesced into a single SetTriggering RPC per
        /// triggering item.
        /// </summary>
        /// <param name="triggeringItem">
        /// The triggering item. Must belong to this subscription's
        /// <see cref="MonitoredItems"/> collection.
        /// </param>
        /// <param name="linksToAdd">
        /// Items to be linked as triggered items of
        /// <paramref name="triggeringItem"/>. Each item must belong to
        /// this subscription.
        /// </param>
        /// <param name="linksToRemove">
        /// Items whose triggering link to
        /// <paramref name="triggeringItem"/> should be removed.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// A <see cref="SetTriggeringResult"/> with per-link statuses
        /// once the queued operation has been applied (the
        /// <see cref="ValueTask"/> completes when the batched RPC
        /// returns). Per-link entries are returned in the same order as
        /// the input lists. Per Part 4 §5.13.5.4 the only spec-specific
        /// per-link status code is <c>Bad_MonitoredItemIdInvalid</c>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="triggeringItem"/> or any entry of
        /// <paramref name="linksToAdd"/> / <paramref name="linksToRemove"/>
        /// is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="triggeringItem"/> or any entry of
        /// <paramref name="linksToAdd"/> / <paramref name="linksToRemove"/>
        /// is not a current member of this subscription's
        /// <see cref="MonitoredItems"/> collection (per spec §5.13.5.1,
        /// triggering and triggered items must belong to the same
        /// Subscription; validation uses reference identity).
        /// </exception>
        ValueTask<SetTriggeringResult> SetTriggeringAsync(
            IMonitoredItem triggeringItem,
            IReadOnlyCollection<IMonitoredItem>? linksToAdd = null,
            IReadOnlyCollection<IMonitoredItem>? linksToRemove = null,
            CancellationToken ct = default);
    }
}
