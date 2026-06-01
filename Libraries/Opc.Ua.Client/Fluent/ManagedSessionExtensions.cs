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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Convenience extensions on <see cref="ManagedSession"/> for working
    /// with the new options-based subscription API. These wrap plain
    /// <see cref="Subscriptions.SubscriptionOptions"/> /
    /// <see cref="Subscriptions.MonitoredItems.MonitoredItemOptions"/> snapshots into
    /// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>
    /// adapters so callers don't need to set up a DI options pipeline for
    /// one-off use.
    /// </summary>
    public static class ManagedSessionExtensions
    {
        /// <summary>
        /// Add a new subscription to the session using the supplied options
        /// snapshot. The subscription is registered with
        /// <see cref="ManagedSession.SubscriptionManager"/> and starts
        /// asynchronously.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <c>null</c>.</exception>
        public static Subscriptions.ISubscription AddSubscription(
            this ManagedSession session,
            Subscriptions.ISubscriptionNotificationHandler handler,
            Subscriptions.SubscriptionOptions options)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            var monitor = new OptionsMonitor<Subscriptions.SubscriptionOptions>(options);
            return session.SubscriptionManager.Add(handler, monitor);
        }

        /// <summary>
        /// Add a new subscription to the session, configuring options via
        /// a callback over a fresh <see cref="Subscriptions.SubscriptionOptions"/>
        /// record.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <c>null</c>.</exception>
        public static Subscriptions.ISubscription AddSubscription(
            this ManagedSession session,
            Subscriptions.ISubscriptionNotificationHandler handler,
            Func<Subscriptions.SubscriptionOptions, Subscriptions.SubscriptionOptions> configure)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return session.AddSubscription(handler, configure(new Subscriptions.SubscriptionOptions()));
        }

        /// <summary>
        /// Add a monitored item to the subscription using the supplied
        /// options snapshot.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="subscription"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public static bool TryAddMonitoredItem(
            this Subscriptions.ISubscription subscription,
            string name,
            Subscriptions.MonitoredItems.MonitoredItemOptions options,
            out Subscriptions.MonitoredItems.IMonitoredItem? monitoredItem)
        {
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name is required", nameof(name));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            var monitor = new OptionsMonitor<Subscriptions.MonitoredItems.MonitoredItemOptions>(options);
            return subscription.MonitoredItems.TryAdd(name, monitor, out monitoredItem);
        }

        /// <summary>
        /// Add a monitored item to the subscription, configuring options
        /// via a callback over a fresh
        /// <see cref="Subscriptions.MonitoredItems.MonitoredItemOptions"/>
        /// record initialized with the supplied node id.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="subscription"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public static bool TryAddMonitoredItem(
            this Subscriptions.ISubscription subscription,
            string name,
            NodeId nodeId,
            Func<Subscriptions.MonitoredItems.MonitoredItemOptions, Subscriptions.MonitoredItems.MonitoredItemOptions> configure,
            out Subscriptions.MonitoredItems.IMonitoredItem? monitoredItem)
        {
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name is required", nameof(name));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return subscription.TryAddMonitoredItem(
                name,
                configure(new Subscriptions.MonitoredItems.MonitoredItemOptions { StartNodeId = nodeId }),
                out monitoredItem);
        }

        /// <summary>
        /// Persist every subscription managed by <paramref name="session"/>
        /// (or a caller-supplied subset) to <paramref name="destination"/>
        /// in OPC UA binary encoding. The format starts with the
        /// session's namespace and server URI tables so the snapshot is
        /// portable across sessions whose tables index URIs in different
        /// positions.
        /// </summary>
        /// <param name="session">Session whose
        /// <see cref="ManagedSession.SubscriptionManager"/> is being
        /// snapshotted.</param>
        /// <param name="destination">Writable destination stream.</param>
        /// <param name="subscriptions">Optional subset of subscriptions
        /// to include. When <c>null</c> every subscription currently
        /// managed by <paramref name="session"/> is included.</param>
        /// <param name="ct">Cancellation token.</param>
        public static ValueTask SaveSubscriptionsAsync(
            this ManagedSession session,
            Stream destination,
            IEnumerable<ISubscription>? subscriptions = null,
            CancellationToken ct = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            return session.SubscriptionManager.SaveAsync(
                destination, session.MessageContext, subscriptions, ct);
        }

        /// <summary>
        /// Restore subscriptions previously persisted by
        /// <see cref="SaveSubscriptionsAsync"/>. Each restored subscription is
        /// re-registered with
        /// <see cref="ManagedSession.SubscriptionManager"/>.
        /// </summary>
        /// <param name="session">Session that owns the V2 subscription
        /// manager and supplies the active message context.</param>
        /// <param name="source">Readable source stream produced by
        /// <see cref="SaveSubscriptionsAsync"/>.</param>
        /// <param name="handlerFactory">Factory invoked once per
        /// restored subscription to construct the application's
        /// <see cref="ISubscriptionNotificationHandler"/>. The factory
        /// receives the per-subscription stable name captured in the
        /// snapshot.</param>
        /// <param name="transferSubscriptions">When <c>true</c> the
        /// restored subscriptions take over the original server-side
        /// state via <c>TransferSubscriptions</c>; if that fails for any
        /// subscription the V2 manager falls back to recreate.</param>
        /// <param name="ct">Cancellation token.</param>
        public static ValueTask<IReadOnlyList<ISubscription>> LoadSubscriptionsAsync(
            this ManagedSession session, Stream source,
            Func<string, ISubscriptionNotificationHandler> handlerFactory,
            bool transferSubscriptions = false, CancellationToken ct = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            return session.SubscriptionManager.LoadAsync(source,
                session.MessageContext, handlerFactory,
                transferSubscriptions, ct);
        }

        /// <summary>
        /// Capture an in-memory snapshot of every subscription managed
        /// by <paramref name="session"/>. The returned list of
        /// <see cref="SubscriptionStateSnapshot"/>s can be persisted by
        /// the caller in any format and later passed to
        /// <see cref="RestoreSubscriptionsAsync"/>.
        /// </summary>
        public static IReadOnlyList<SubscriptionStateSnapshot> SnapshotSubscriptions(
            this ManagedSession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            var result = new List<SubscriptionStateSnapshot>();
            foreach (ISubscription s in session.SubscriptionManager.Items)
            {
                if (s is Subscriptions.Subscription concrete)
                {
                    result.Add(concrete.Snapshot());
                }
            }
            return result;
        }

        /// <summary>
        /// Restore a list of <see cref="SubscriptionStateSnapshot"/>s
        /// previously captured by <see cref="SnapshotSubscriptions"/>.
        /// </summary>
        /// <param name="session">Session that owns the V2 subscription
        /// manager.</param>
        /// <param name="states">Snapshots to restore.</param>
        /// <param name="handlerFactory">Factory invoked once per
        /// snapshot to construct the application's notification
        /// handler. The factory receives the snapshot itself so callers
        /// can route by options or per-item metadata.</param>
        /// <param name="transferSubscriptions">When <c>true</c> the
        /// restored subscriptions take over the original server-side
        /// state via <c>TransferSubscriptions</c>; if that fails for
        /// any subscription the V2 manager falls back to recreate.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async ValueTask<IReadOnlyList<ISubscription>> RestoreSubscriptionsAsync(
            this ManagedSession session,
            IReadOnlyList<SubscriptionStateSnapshot> states,
            Func<SubscriptionStateSnapshot, ISubscriptionNotificationHandler> handlerFactory,
            bool transferSubscriptions = false,
            CancellationToken ct = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (states == null)
            {
                throw new ArgumentNullException(nameof(states));
            }
            if (handlerFactory == null)
            {
                throw new ArgumentNullException(nameof(handlerFactory));
            }
            var result = new List<ISubscription>(states.Count);
            foreach (SubscriptionStateSnapshot state in states)
            {
                result.Add(await session.SubscriptionManager.RestoreAsync(
                    handlerFactory(state), state, transferSubscriptions, ct)
                    .ConfigureAwait(false));
            }
            return result;
        }
    }
}
