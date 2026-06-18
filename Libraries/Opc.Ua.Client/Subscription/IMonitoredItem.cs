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

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    /// <summary>
    /// The current monitored item inside a subscription
    /// </summary>
    public interface IMonitoredItem
    {
        /// <summary>
        /// Name of the item in the subscription
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Order of the item in the subscription
        /// </summary>
        uint Order { get; }

        /// <summary>
        /// The identifier assigned by the server.
        /// </summary>
        uint ServerId { get; }

        /// <summary>
        /// Whether the item has been created on the server.
        /// </summary>
        bool Created { get; }

        /// <summary>
        /// The last error result associated with the item
        /// </summary>
        ServiceResult Error { get; }

        /// <summary>
        /// The filter result of the last change applied.
        /// </summary>
        MonitoringFilterResult? FilterResult { get; }

        /// <summary>
        /// The monitoring mode.
        /// </summary>
        MonitoringMode CurrentMonitoringMode { get; }

        /// <summary>
        /// The sampling interval.
        /// </summary>
        TimeSpan CurrentSamplingInterval { get; }

        /// <summary>
        /// The length of the queue used to buffer values.
        /// </summary>
        uint CurrentQueueSize { get; }

        /// <summary>
        /// The identifier assigned by the client.
        /// </summary>
        uint ClientHandle { get; }

        /// <summary>
        /// Items that trigger this item (OPC UA Part 4 §5.13.1.6 N:M
        /// triggering relationships). Returns a snapshot — the
        /// underlying collection may change between successive reads.
        /// An empty enumerable means no triggering relationship.
        /// </summary>
        /// <remarks>
        /// Resolution is on-demand against the owning subscription's
        /// monitored-item collection by stable name; siblings that are
        /// not currently present (e.g. removed) are silently skipped.
        /// This projects the runtime desired-state set
        /// (<c>DesiredTriggeredByNames</c>) — which is updated
        /// immediately by imperative <c>SetTriggeringAsync</c> calls
        /// and by options changes — and therefore reflects intent
        /// even before the server has applied a SetTriggering RPC.
        /// </remarks>
        IEnumerable<IMonitoredItem> TriggeringItems { get; }

        /// <summary>
        /// Items that this item triggers (the reverse view of
        /// <see cref="TriggeringItems"/>). Resolved on demand by
        /// walking the owning subscription's monitored items and
        /// returning those whose runtime desired triggering set
        /// contains <see cref="Name"/>.
        /// </summary>
        IEnumerable<IMonitoredItem> TriggeredItems { get; }

        /// <summary>
        /// Issue an OPC UA Part 9 §5.5.7 ConditionRefresh2 method call
        /// for this monitored item. The server responds by re-sending
        /// the current state of every condition this item is monitoring
        /// (bracketed by RefreshStartEvent and RefreshEndEvent), so the
        /// client can rebuild a complete view after disconnect or
        /// subscription transfer without missing currently-active
        /// alarms.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ServiceResultException">Raised with
        /// <c>BadMonitoredItemIdInvalid</c> if this item has not been
        /// created on the server yet, or with the server-returned
        /// status if the method call fails.</exception>
        System.Threading.Tasks.ValueTask ConditionRefreshAsync(
            System.Threading.CancellationToken ct = default);
    }
}
