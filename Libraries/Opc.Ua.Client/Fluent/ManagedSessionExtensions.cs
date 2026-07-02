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
        private static Subscriptions.ISubscriptionManager GetSubscriptionManager(
            this ManagedSession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (!session.TryGetSubscriptionManager(
                    out Subscriptions.ISubscriptionManager? manager))
            {
                throw new InvalidOperationException(
                    "The managed session does not expose a V2 subscription manager. " +
                    "The session is using the classic engine; recreate the ManagedSession " +
                    "with the V2 subscription engine factory.");
            }
            return manager;
        }

        /// <summary>
        /// Add a new subscription to the session using the supplied options
        /// snapshot. The subscription is registered with the session's V2
        /// subscription manager (see
        /// <see cref="ISession.TryGetSubscriptionManager"/>) and starts
        /// asynchronously.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <c>null</c>.</exception>
        public static ISubscription AddSubscription(
            this ManagedSession session,
            ISubscriptionNotificationHandler handler,
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
            return session.GetSubscriptionManager().Add(handler, monitor);
        }

        /// <summary>
        /// Add a new subscription to the session, configuring options via
        /// a callback over a fresh <see cref="Subscriptions.SubscriptionOptions"/>
        /// record.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <c>null</c>.</exception>
        public static ISubscription AddSubscription(
            this ManagedSession session,
            ISubscriptionNotificationHandler handler,
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
            this ISubscription subscription,
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
            this ISubscription subscription,
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
        /// <param name="session">Session whose V2 subscription manager (see
        /// <see cref="ISession.TryGetSubscriptionManager"/>) is being
        /// snapshotted.</param>
        /// <param name="destination">Writable destination stream.</param>
        /// <param name="subscriptions">Optional subset of subscriptions
        /// to include. When <c>null</c> every subscription currently
        /// managed by <paramref name="session"/> is included.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <c>null</c>.</exception>
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
            return session.GetSubscriptionManager().SaveAsync(
                destination, session.MessageContext, subscriptions, ct);
        }

        /// <summary>
        /// Restore subscriptions previously persisted by
        /// <see cref="SaveSubscriptionsAsync"/>. Each restored subscription is
        /// re-registered with the session's V2 subscription manager (see
        /// <see cref="ISession.TryGetSubscriptionManager"/>).
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
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <c>null</c>.</exception>
        public static ValueTask<IReadOnlyList<ISubscription>> LoadSubscriptionsAsync(
            this ManagedSession session, Stream source,
            Func<string, ISubscriptionNotificationHandler> handlerFactory,
            bool transferSubscriptions = false, CancellationToken ct = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            return session.GetSubscriptionManager().LoadAsync(source,
                session.MessageContext, handlerFactory,
                transferSubscriptions, ct);
        }

        /// <summary>
        /// Capture an in-memory snapshot of every subscription managed
        /// by <paramref name="session"/>. Multi-partition wrappers
        /// contribute one <see cref="SubscriptionStateSnapshot"/> per
        /// partition; the returned list can be persisted by the caller
        /// in any format and later passed to
        /// <see cref="RestoreSubscriptionsAsync"/>, which regroups
        /// snapshots by their <c>LogicalGroupId</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <c>null</c>.</exception>
        public static IReadOnlyList<SubscriptionStateSnapshot> SnapshotSubscriptions(
            this ManagedSession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            var result = new List<SubscriptionStateSnapshot>();
            foreach (ISubscription s in session.GetSubscriptionManager().Items)
            {
                if (s is LogicalSubscription logical)
                {
                    result.AddRange(logical.SnapshotAllPartitions());
                }
                else if (s is Subscriptions.Subscription concrete)
                {
                    // Fall-through for any direct Subscription usage
                    // (e.g. tests bypassing the manager).
                    result.Add(concrete.Snapshot());
                }
            }
            return result;
        }

        /// <summary>
        /// Restore a list of <see cref="SubscriptionStateSnapshot"/>s
        /// previously captured by <see cref="SnapshotSubscriptions"/>.
        /// Snapshots that share a non-null
        /// <c>LogicalGroupId</c> are regrouped into a single
        /// multi-partition <c>LogicalSubscription</c>; snapshots
        /// with <c>null</c> <c>LogicalGroupId</c> restore as
        /// standalone subscriptions (matching V1 snapshot files).
        /// Malformed groups throw
        /// <see cref="ServiceResultException"/> with
        /// <see cref="StatusCodes.BadDecodingError"/>.
        /// </summary>
        /// <param name="session">Session that owns the V2 subscription
        /// manager.</param>
        /// <param name="states">Snapshots to restore.</param>
        /// <param name="handlerFactory">Factory invoked once per
        /// logical subscription to construct the application's
        /// notification handler. For grouped restores the factory is
        /// passed the primary partition's snapshot.</param>
        /// <param name="transferSubscriptions">When <c>true</c> the
        /// restored subscriptions take over the original server-side
        /// state via <c>TransferSubscriptions</c>; if that fails for
        /// any subscription the V2 manager falls back to recreate.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <c>null</c>.</exception>
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
            var manager =
                (SubscriptionManager)session.GetSubscriptionManager();

            // Group by LogicalGroupId; null group = standalone. The
            // grouping logic mirrors the stream-based LoadAsync
            // path so callers see consistent multi-partition
            // restore behaviour regardless of where the snapshots
            // came from.
            var groups = new Dictionary<string, List<SubscriptionStateSnapshot>>(
                StringComparer.Ordinal);
            var standalone = new List<SubscriptionStateSnapshot>();
            foreach (SubscriptionStateSnapshot state in states)
            {
                if (string.IsNullOrEmpty(state.LogicalGroupId))
                {
                    standalone.Add(state);
                }
                else
                {
                    if (!groups.TryGetValue(state.LogicalGroupId,
                        out List<SubscriptionStateSnapshot>? bucket))
                    {
                        bucket = [];
                        groups[state.LogicalGroupId] = bucket;
                    }
                    bucket.Add(state);
                }
            }

            foreach (SubscriptionStateSnapshot state in standalone)
            {
                result.Add(await manager.RestoreAsync(
                    handlerFactory(state),
                    state,
                    transferSubscriptions,
                    ct).ConfigureAwait(false));
            }
            foreach (KeyValuePair<string, List<SubscriptionStateSnapshot>> entry in groups)
            {
                List<SubscriptionStateSnapshot> ordered = ValidateAndSortGroup(
                    entry.Key, entry.Value);
                result.Add(await manager.RestoreGroupAsync(
                    handlerFactory(ordered[0]),
                    ordered,
                    transferSubscriptions,
                    ct).ConfigureAwait(false));
            }
            return result;
        }

        internal static List<SubscriptionStateSnapshot> ValidateAndSortGroup(
            string groupId,
            List<SubscriptionStateSnapshot> bucket)
        {
            bucket.Sort(static (a, b) => a.PartitionIndex.CompareTo(b.PartitionIndex));
            for (int i = 0; i < bucket.Count; i++)
            {
                if (bucket[i].PartitionIndex != i)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Multi-partition snapshot group '{0}' has " +
                        "non-contiguous or duplicated PartitionIndex " +
                        "values (expected {1} at position {1}, got {2}).",
                        groupId, i, bucket[i].PartitionIndex);
                }
            }

            // Snapshot-trust guard: a multi-partition group must
            // share identical subscription-wide options across every
            // partition (the wrapper assumes the partitions are
            // siblings of a single logical subscription). Reject the
            // load if the partitions disagree on any setting that
            // affects publish behaviour, durability, or transfer
            // semantics so an attacker-controlled or corrupted
            // snapshot file cannot smuggle a non-durable partition
            // into a durable logical (or vice versa), nor cause the
            // wrapper to mix partitions with diverging publishing
            // cadences.
            if (bucket.Count > 1)
            {
                SubscriptionStateSnapshot primary = bucket[0];
                for (int i = 1; i < bucket.Count; i++)
                {
                    SubscriptionStateSnapshot secondary = bucket[i];
                    AssertSameOption(groupId, "PublishingIntervalMs",
                        primary.PublishingIntervalMs, secondary.PublishingIntervalMs);
                    AssertSameOption(groupId, "KeepAliveCount",
                        primary.KeepAliveCount, secondary.KeepAliveCount);
                    AssertSameOption(groupId, "LifetimeCount",
                        primary.LifetimeCount, secondary.LifetimeCount);
                    AssertSameOption(groupId, "MaxNotificationsPerPublish",
                        primary.MaxNotificationsPerPublish,
                        secondary.MaxNotificationsPerPublish);
                    AssertSameOption(groupId, "MinLifetimeIntervalMs",
                        primary.MinLifetimeIntervalMs,
                        secondary.MinLifetimeIntervalMs);
                    AssertSameOption(groupId, "Priority",
                        primary.Priority, secondary.Priority);
                    AssertSameOption(groupId, "PublishingEnabled",
                        primary.PublishingEnabled, secondary.PublishingEnabled);
                    AssertSameOption(groupId, "Disabled",
                        primary.Disabled, secondary.Disabled);
                    AssertSameOption(groupId, "SendInitialValuesOnTransfer",
                        primary.SendInitialValuesOnTransfer,
                        secondary.SendInitialValuesOnTransfer);
                }
            }

            return bucket;
        }

        private static void AssertSameOption<T>(
            string groupId, string optionName, T primary, T secondary)
            where T : IEquatable<T>
        {
            if (!EqualityComparer<T>.Default.Equals(primary, secondary))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Multi-partition snapshot group '{0}' has " +
                    "inconsistent option '{1}' across partitions " +
                    "(primary={2}, secondary={3}). All partitions " +
                    "in a logical subscription must agree on " +
                    "subscription-wide options.",
                    groupId, optionName, primary, secondary);
            }
        }

        /// <summary>
        /// Convenience overload of
        /// <see cref="ISubscription.SetTriggeringAsync"/>
        /// that resolves the triggering item and triggered items by
        /// stable name against the subscription's
        /// <see cref="Subscriptions.MonitoredItems.IMonitoredItemCollection"/>.
        /// Unknown names cause <see cref="ArgumentException"/>.
        /// </summary>
        /// <param name="subscription">Owning V2 subscription.</param>
        /// <param name="triggeringItemName">
        /// Stable name of the triggering item.
        /// </param>
        /// <param name="triggeredItemNames">
        /// Stable names of items to be triggered. All entries are
        /// added; pass an empty array to query an existing trigger
        /// without adding new links.
        /// </param>
        public static ValueTask<SetTriggeringResult> SetTriggeringAsync(
            this ISubscription subscription,
            string triggeringItemName,
            params string[] triggeredItemNames)
        {
            return subscription.SetTriggeringAsync(
                triggeringItemName,
                triggeredItemNames,
                null,
                default);
        }

        /// <summary>
        /// Convenience overload of
        /// <see cref="ISubscription.SetTriggeringAsync"/>
        /// that resolves the triggering item and triggered items by
        /// stable name against the subscription's
        /// <see cref="Subscriptions.MonitoredItems.IMonitoredItemCollection"/>.
        /// Unknown names cause <see cref="ArgumentException"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="subscription"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public static ValueTask<SetTriggeringResult> SetTriggeringAsync(
            this ISubscription subscription,
            string triggeringItemName,
            IReadOnlyCollection<string>? add = null,
            IReadOnlyCollection<string>? remove = null,
            CancellationToken ct = default)
        {
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }
            if (string.IsNullOrEmpty(triggeringItemName))
            {
                throw new ArgumentException(
                    "Triggering item name must not be null/empty.",
                    nameof(triggeringItemName));
            }
            Subscriptions.MonitoredItems.IMonitoredItemCollection items =
                subscription.MonitoredItems;
            if (!items.TryGetMonitoredItemByName(triggeringItemName,
                    out Subscriptions.MonitoredItems.IMonitoredItem? trig) ||
                trig == null)
            {
                throw new ArgumentException(
                    $"Triggering item '{triggeringItemName}' was not found " +
                    "in the subscription. Add it first via TryAddMonitoredItem.",
                    nameof(triggeringItemName));
            }
            List<Subscriptions.MonitoredItems.IMonitoredItem>? addItems = null;
            if (add != null)
            {
                addItems = new List<Subscriptions.MonitoredItems.IMonitoredItem>(add.Count);
                foreach (string name in add)
                {
                    if (!items.TryGetMonitoredItemByName(name,
                            out Subscriptions.MonitoredItems.IMonitoredItem? item) ||
                        item == null)
                    {
                        throw new ArgumentException(
                            $"Triggered item '{name}' was not found in the " +
                            "subscription. Add it first via TryAddMonitoredItem.",
                            nameof(add));
                    }
                    addItems.Add(item);
                }
            }
            List<Subscriptions.MonitoredItems.IMonitoredItem>? removeItems = null;
            if (remove != null)
            {
                removeItems = new List<Subscriptions.MonitoredItems.IMonitoredItem>(remove.Count);
                foreach (string name in remove)
                {
                    if (!items.TryGetMonitoredItemByName(name,
                            out Subscriptions.MonitoredItems.IMonitoredItem? item) ||
                        item == null)
                    {
                        throw new ArgumentException(
                            $"Triggered item '{name}' was not found in the " +
                            "subscription. Add it first via TryAddMonitoredItem.",
                            nameof(remove));
                    }
                    removeItems.Add(item);
                }
            }
            return subscription.SetTriggeringAsync(trig, addItems, removeItems, ct);
        }
    }
}
