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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using UaLens.Diagnostics;
using V2MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using V2SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

namespace UaLens.Subscriptions
{
    /// <summary>
    /// Adapter on top of the V2 channel-based subscription engine
    /// (<see cref="ISubscriptionManager"/> + <see cref="ISubscriptionNotificationHandler"/>).
    /// </summary>
    internal sealed class ChannelV2EngineAdapter : ISubscriptionAdapter
    {
        public ChannelV2EngineAdapter(
            ManagedSession session,
            ITelemetryContext telemetry,
            PublishLogObserver? publishLog = null)
            : this((ISession)session, telemetry, publishLog)
        {
        }

        internal ChannelV2EngineAdapter(
            ISession session,
            ITelemetryContext telemetry,
            PublishLogObserver? publishLog = null)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(telemetry);
            m_session = session;
            m_log = telemetry.CreateLogger("ChannelV2Adapter");
            m_publishLog = publishLog;
            m_channel = Channel.CreateBounded<NotificationEvent>(new BoundedChannelOptions(8192)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
            Events = m_channel.Reader;
        }

        public SubscriptionCounters Counters { get; } = new();
        public ChannelReader<NotificationEvent> Events { get; }

        public TimeSpan CurrentPublishingInterval => m_subscription?.CurrentPublishingInterval ?? TimeSpan.Zero;
        public uint CurrentKeepAliveCount => m_subscription?.CurrentKeepAliveCount ?? 0;
        public uint CurrentLifetimeCount => m_subscription?.CurrentLifetimeCount ?? 0;

        public int PublishWorkerCount
            => m_session.TryGetSubscriptionManager(out ISubscriptionManager? mgr)
                ? mgr.PublishWorkerCount : 0;

        public int GoodPublishRequestCount => m_session.GoodPublishRequestCount;

        public int BadPublishRequestCount
            => m_session.TryGetSubscriptionManager(out ISubscriptionManager? mgr)
                ? mgr.BadPublishRequestCount : 0;

        public long MissingMessageCount
            => m_session.TryGetSubscriptionManager(out ISubscriptionManager? mgr)
                ? mgr.MissingMessageCount : 0;

        public long RepublishMessageCount
            => m_session.TryGetSubscriptionManager(out ISubscriptionManager? mgr)
                ? mgr.RepublishMessageCount : 0;

        public long DroppedNotificationCount => Volatile.Read(ref m_droppedCount);

        /// <summary>
        /// Currently-configured floor for the V2 worker pool.
        /// </summary>
        public int MinPublishWorkerCount
            => m_session.TryGetSubscriptionManager(out ISubscriptionManager? mgr)
                ? mgr.MinPublishWorkerCount : 0;

        /// <summary>
        /// Currently-configured ceiling for the V2 worker pool.
        /// </summary>
        public int MaxPublishWorkerCount
            => m_session.TryGetSubscriptionManager(out ISubscriptionManager? mgr)
                ? mgr.MaxPublishWorkerCount : 0;

        public int MinPublishRequestCount => m_session.MinPublishRequestCount;
        public int MaxPublishRequestCount => m_session.MaxPublishRequestCount;
        public bool HasWorkerPool => true;

        public ArrayOf<MonitoredItemConfig> Items
            => m_items.Values.OrderBy(e => e.Config.Id).Select(e => e.Config with
            {
                SamplingInterval = e.MonitoredItem.CurrentSamplingInterval,
                QueueSize = e.MonitoredItem.CurrentQueueSize,
                MonitoringMode = e.MonitoredItem.CurrentMonitoringMode
            }).ToArray();

        public Task ApplySubscriptionAsync(SubscriptionConfig config, CancellationToken ct)
        {
            lock (m_lock)
            {
                V2SubscriptionOptions options = ToOptions(config);
                // Both fields are set together; reuse the monitor once the subscription is up.
                OptionsMonitor<V2SubscriptionOptions> opts = m_subscriptionOptions ??=
                    new OptionsMonitor<V2SubscriptionOptions>(options);
                if (m_subscription is null)
                {
                    var handler = new Handler(this);
                    if (!m_session.TryGetSubscriptionManager(out ISubscriptionManager? manager))
                    {
                        throw new InvalidOperationException(
                            "The V2 subscription engine is not available on this session.");
                    }
                    m_subscription = manager.Add(handler, opts);
                    m_log.ChannelV2SubscriptionCreated();
                }
                else
                {
                    opts.CurrentValue = options;
                    m_log.ChannelV2SubscriptionOptionsUpdated();
                }
            }
            return Task.CompletedTask;
        }

        public Task<int> AddItemAsync(MonitoredItemConfig config, CancellationToken ct)
        {
            if (m_subscription is null)
            {
                throw new InvalidOperationException("Apply a subscription before adding items.");
            }

            int id = Interlocked.Increment(ref m_nextItemId);
            MonitoredItemConfig stored = config with { Id = id };
            var optionsMonitor = new OptionsMonitor<V2MonitoredItemOptions>(ToOptions(stored));

            lock (m_lock)
            {
                string name = $"item-{id}";
                if (!m_subscription.MonitoredItems.TryAdd(name, optionsMonitor, out IMonitoredItem? created))
                {
                    throw new InvalidOperationException($"Failed to add monitored item '{name}'.");
                }
                // TryAdd's out value is non-null on success, but lacks NotNullWhen(true).
                m_items[id] = new ItemEntry(stored, created!, optionsMonitor);
            }
            m_stats[id] = new MonitoredItemLiveStats();
            m_log.ChannelV2MonitoredItemAdded(id, stored.NodeId, stored.AttributeId);
            return Task.FromResult(id);
        }

        public Task RemoveItemAsync(int id, CancellationToken ct)
        {
            if (m_subscription is null || !m_items.TryRemove(id, out ItemEntry? entry))
            {
                return Task.CompletedTask;
            }
            m_stats.TryRemove(id, out _);
            lock (m_lock)
            {
                m_subscription.MonitoredItems.TryRemove(entry.MonitoredItem.ClientHandle);
            }
            m_log.ChannelV2MonitoredItemRemoved(id);
            return Task.CompletedTask;
        }

        public Task ConfigureItemAsync(MonitoredItemConfig config, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(config);
            ct.ThrowIfCancellationRequested();
            if (!m_items.TryGetValue(config.Id, out ItemEntry? entry))
            {
                throw new ServiceResultException(StatusCodes.BadMonitoredItemIdInvalid);
            }
            if (config.NodeId != entry.Config.NodeId ||
                config.AttributeId != entry.Config.AttributeId ||
                config.IsEvent != entry.Config.IsEvent)
            {
                throw new ArgumentException("Item settings cannot change the monitored target.", nameof(config));
            }
            entry.Options.CurrentValue = ToOptions(config);
            m_items[config.Id] = entry with { Config = config };
            return Task.CompletedTask;
        }

        public Task SetMonitoringModeAsync(int id, MonitoringMode mode, CancellationToken ct)
        {
            if (m_subscription is null || !m_items.TryGetValue(id, out ItemEntry? entry))
            {
                return Task.CompletedTask;
            }
            // Mutating the monitor schedules SetMonitoringMode on the next apply pass.
            // CurrentMonitoringMode reports the server-confirmed state.
            V2MonitoredItemOptions current = entry.Options.CurrentValue;
            entry.Options.CurrentValue = current with { MonitoringMode = mode };
            MonitoredItemConfig updated = entry.Config with { MonitoringMode = mode };
            m_items[id] = entry with { Config = updated };
            m_log.ChannelV2MonitoringModeChanged(id, mode);
            return Task.CompletedTask;
        }

        public bool TryGetItemStats(int id, [NotNullWhen(true)] out MonitoredItemLiveStats? stats)
        {
            return m_stats.TryGetValue(id, out stats);
        }

        public bool TryGetItemResult(int id, out MonitoredItemResult result)
        {
            if (m_items.TryGetValue(id, out ItemEntry? entry))
            {
                result = new MonitoredItemResult(
                    entry.MonitoredItem.Created,
                    entry.MonitoredItem is IMonitoredItemApplyState { HasPendingChanges: true },
                    entry.MonitoredItem.Error.StatusCode);
                return true;
            }
            result = default;
            return false;
        }

        public async ValueTask DisposeAsync()
        {
            ISubscription? sub;
            lock (m_lock)
            {
                sub = m_subscription;
                m_subscription = null;
            }
            try
            {
                if (sub is not null)
                {
                    await sub.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_channel.Writer.TryComplete();
            }
        }

        /// <summary>
        /// Writes a notification and samples channel capacity to count dropped events.
        /// Falls back to no count update when the channel does not expose its count.
        /// </summary>
        internal void WriteEventOrCount(NotificationEvent ev)
        {
            if (m_channel.Reader.CanCount && m_channel.Reader.Count >= 8192)
            {
                Interlocked.Increment(ref m_droppedCount);
            }
            m_channel.Writer.TryWrite(ev);
        }

        private static V2SubscriptionOptions ToOptions(SubscriptionConfig c)
        {
            return new()
            {
                PublishingInterval = c.PublishingInterval,
                KeepAliveCount = c.KeepAliveCount,
                LifetimeCount = c.LifetimeCount,
                Priority = c.Priority,
                MaxNotificationsPerPublish = c.MaxNotificationsPerPublish,
                PublishingEnabled = c.PublishingEnabled,
                Disabled = false,
                MinLifetimeInterval = TimeSpan.FromMinutes(1)
            };
        }

        private static V2MonitoredItemOptions ToOptions(MonitoredItemConfig c)
        {
            return new V2MonitoredItemOptions
            {
                StartNodeId = c.NodeId,
                AttributeId = c.AttributeId,
                SamplingInterval = c.SamplingInterval,
                QueueSize = c.QueueSize,
                DiscardOldest = c.DiscardOldest,
                MonitoringMode = c.MonitoringMode,
                Filter = c.IsEvent ? DefaultEventFilters.Build() : c.DataChangeFilter
            };
        }

        private readonly ISession m_session;
        private readonly ILogger m_log;
        private readonly Channel<NotificationEvent> m_channel;
        private readonly PublishLogObserver? m_publishLog;
        private readonly ConcurrentDictionary<int, ItemEntry> m_items = new();
        private readonly ConcurrentDictionary<int, MonitoredItemLiveStats> m_stats = new();
        private readonly Lock m_lock = new();
        private OptionsMonitor<V2SubscriptionOptions>? m_subscriptionOptions;
        private ISubscription? m_subscription;
        private int m_nextItemId;
        private long m_droppedCount;

        private sealed record ItemEntry(
            MonitoredItemConfig Config,
            IMonitoredItem MonitoredItem,
            OptionsMonitor<V2MonitoredItemOptions> Options);

        /// <summary>
        /// Counters always update; channel writes may drop the oldest notification under burst.
        /// </summary>
        private sealed class Handler : ISubscriptionNotificationHandler
        {
            public Handler(ChannelV2EngineAdapter owner)
            {
                m_owner = owner;
            }

            public ValueTask OnDataChangeNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                int n = notification.Length;
                m_owner.Counters.IncDataMessage(n);
                RecordPublish(subscription, sequenceNumber, publishTime, n, PublishLogKind.Data);
                // Keep one event per value. Non-numeric samples still appear in Dots and Bars.
                DateTime now = DateTime.UtcNow;
                ReadOnlySpan<DataValueChange> span = notification.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    int itemId = ResolveItemId(span[i].MonitoredItem);
                    if (itemId != 0 && m_owner.m_stats.TryGetValue(itemId, out MonitoredItemLiveStats? stats))
                    {
                        stats.RecordValue(span[i].Value);
                    }
                    double? d = VariantNumeric.TryToDouble(span[i].Value.WrappedValue, out double parsed)
                        ? parsed : null;
                    m_owner.WriteEventOrCount(new NotificationEvent(
                        NotificationKind.DataChange, itemId, 1, sequenceNumber, now, d));
                }
                return ValueTask.CompletedTask;
            }

            public ValueTask OnEventDataNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                int n = notification.Length;
                m_owner.Counters.IncEventMessage(n);
                RecordPublish(subscription, sequenceNumber, publishTime, n, PublishLogKind.Event);
                // Events have field arrays, so emit one event per item without a numeric value.
                ReadOnlySpan<EventNotification> span = notification.Span;
                DateTime now = DateTime.UtcNow;
                for (int i = 0; i < span.Length; i++)
                {
                    int itemId = ResolveItemId(span[i].MonitoredItem);
                    if (itemId != 0 && m_owner.m_stats.TryGetValue(itemId, out MonitoredItemLiveStats? stats))
                    {
                        stats.RecordEvent();
                    }
                    m_owner.WriteEventOrCount(new NotificationEvent(
                        NotificationKind.Event, itemId, 1, sequenceNumber, now));
                }
                return ValueTask.CompletedTask;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                PublishState publishStateMask)
            {
                m_owner.Counters.IncKeepAlive();
                RecordPublish(subscription, sequenceNumber, publishTime, 1, PublishLogKind.KeepAlive);
                m_owner.WriteEventOrCount(new NotificationEvent(
                    NotificationKind.KeepAlive, 0, 0, sequenceNumber, DateTime.UtcNow));
                return ValueTask.CompletedTask;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                ISubscription subscription,
                Opc.Ua.Client.Subscriptions.SubscriptionState state,
                PublishState publishStateMask,
                CancellationToken ct = default)
            {
                // Health comes from notification PublishState masks; avoid counting it twice.
                return ValueTask.CompletedTask;
            }

            private int ResolveItemId(IMonitoredItem? mi)
            {
                if (mi is null)
                {
                    return 0;
                }
                foreach (KeyValuePair<int, ItemEntry> kv in m_owner.m_items)
                {
                    if (ReferenceEquals(kv.Value.MonitoredItem, mi))
                    {
                        return kv.Key;
                    }
                }
                return 0;
            }

            private void RecordPublish(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                int count,
                PublishLogKind kind)
            {
                uint serverId = m_owner.m_subscription is IPartitionedSubscription { PartitionIds: { Count: 1 } ids }
                    ? ids[0] : 0;
                m_owner.m_publishLog?.RecordClient(subscription, serverId, sequenceNumber, publishTime, count, kind);
            }

            private readonly ChannelV2EngineAdapter m_owner;

        }
    }

    internal static partial class ChannelV2EngineAdapterLog
    {
        [LoggerMessage(EventId = UaLensEventIds.ChannelV2EngineAdapter + 0, Level = LogLevel.Information,
            Message = "V2 subscription created.")]
        public static partial void ChannelV2SubscriptionCreated(this ILogger logger);

        [LoggerMessage(EventId = UaLensEventIds.ChannelV2EngineAdapter + 1, Level = LogLevel.Information,
            Message = "V2 subscription options updated.")]
        public static partial void ChannelV2SubscriptionOptionsUpdated(this ILogger logger);

        [LoggerMessage(EventId = UaLensEventIds.ChannelV2EngineAdapter + 2, Level = LogLevel.Information,
            Message = "V2 monitored item added: id={Id} node={Node} attr={Attr}")]
        public static partial void ChannelV2MonitoredItemAdded(this ILogger logger, int id, NodeId node, uint attr);

        [LoggerMessage(EventId = UaLensEventIds.ChannelV2EngineAdapter + 3, Level = LogLevel.Information,
            Message = "V2 monitored item removed: id={Id}")]
        public static partial void ChannelV2MonitoredItemRemoved(this ILogger logger, int id);

        [LoggerMessage(EventId = UaLensEventIds.ChannelV2EngineAdapter + 4, Level = LogLevel.Information,
            Message = "V2 monitored item {Id} mode -> {Mode}")]
        public static partial void ChannelV2MonitoringModeChanged(this ILogger logger, int id, MonitoringMode mode);
    }
}
