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
using UaLens.Diagnostics;
using ClassicMonitoredItem = Opc.Ua.Client.MonitoredItem;
using ClassicMonitoredItemOptions = Opc.Ua.Client.MonitoredItemOptions;
using ClassicSubscription = Opc.Ua.Client.Subscription;
using ClassicSubscriptionOptions = Opc.Ua.Client.SubscriptionOptions;

namespace UaLens.Subscriptions
{
    /// <summary>
    /// Adapter on top of the legacy <see cref="ClassicSubscription"/> with
    /// <c>FastDataChange / FastEvent / FastKeepAlive</c> callbacks.
    /// </summary>
    internal sealed class ClassicEngineAdapter : ISubscriptionAdapter
    {
        private readonly ManagedSession m_session;
        private readonly ILogger m_log;
        private readonly Channel<NotificationEvent> m_channel;
        private readonly PublishLogObserver? m_publishLog;
        private readonly ConcurrentDictionary<int, ItemEntry> m_items = new();
        private readonly ConcurrentDictionary<int, MonitoredItemLiveStats> m_stats = new();
        private readonly Lock m_lock = new();
        // CA2213: m_subscription IS disposed in DisposeAsync below, but the
        // analyzer can't track lifecycle through Interlocked.Exchange.
#pragma warning disable CA2213
        private ClassicSubscription? m_subscription;
#pragma warning restore CA2213
        private int m_nextItemId;

        public SubscriptionCounters Counters { get; } = new();
        public ChannelReader<NotificationEvent> Events { get; }

        public TimeSpan CurrentPublishingInterval
            => TimeSpan.FromMilliseconds(m_subscription?.CurrentPublishingInterval ?? 0);

        public uint CurrentKeepAliveCount => m_subscription?.CurrentKeepAliveCount ?? 0;
        public uint CurrentLifetimeCount => m_subscription?.CurrentLifetimeCount ?? 0;

        /// <summary>
        /// Classic engine has no worker pool.  Surface the session's outstanding
        /// publish request count for parity, and report 0 for worker / bad counts.
        /// </summary>
        public int PublishWorkerCount => 0;
        public int GoodPublishRequestCount => m_session.GoodPublishRequestCount;
        public int BadPublishRequestCount => 0;
        public long MissingMessageCount => 0;
        public long RepublishMessageCount => 0;
        public long DroppedNotificationCount => Volatile.Read(ref m_droppedCount);

        private long m_droppedCount;

        public int MinPublishWorkerCount => 0;
        public int MaxPublishWorkerCount => 0;
        public int MinPublishRequestCount => m_session.MinPublishRequestCount;
        public int MaxPublishRequestCount => m_session.MaxPublishRequestCount;
        public bool HasWorkerPool => false;

        public ArrayOf<MonitoredItemConfig> Items
            => m_items.Values.OrderBy(e => e.Config.Id).Select(e => e.Config with
            {
                SamplingInterval = TimeSpan.FromMilliseconds(e.MonitoredItem.Status.SamplingInterval),
                QueueSize = e.MonitoredItem.Status.QueueSize,
                MonitoringMode = e.MonitoredItem.Status.MonitoringMode
            }).ToArray();

        public ClassicEngineAdapter(ManagedSession session, ITelemetryContext telemetry,
            PublishLogObserver? publishLog = null)
        {
            m_session = session;
            m_log = telemetry.CreateLogger("ClassicAdapter");
            m_publishLog = publishLog;
            m_channel = Channel.CreateBounded<NotificationEvent>(new BoundedChannelOptions(8192)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
            Events = m_channel.Reader;
        }

        public async Task ApplySubscriptionAsync(SubscriptionConfig config, CancellationToken ct)
        {
            if (m_subscription is null)
            {
                var sub = new ClassicSubscription(m_session.MessageContext.Telemetry, new ClassicSubscriptionOptions
                {
                    DisplayName = "UaLens",
                    PublishingInterval = (int)config.PublishingInterval.TotalMilliseconds,
                    LifetimeCount = config.LifetimeCount,
                    KeepAliveCount = config.KeepAliveCount,
                    MaxNotificationsPerPublish = config.MaxNotificationsPerPublish,
                    Priority = config.Priority,
                    PublishingEnabled = config.PublishingEnabled,
                    MinLifetimeInterval = 60_000
                })
                {
                    FastDataChangeCallback = OnDataChange,
                    FastEventCallback = OnEvent,
                    FastKeepAliveCallback = OnKeepAlive
                };
                var creation = new ClassicSubscriptionLease(m_session, sub);
                await using (creation.ConfigureAwait(false))
                {
                    if (!m_session.AddSubscription(sub))
                    {
                        throw new InvalidOperationException("The session rejected the monitor subscription.");
                    }
                    await sub.CreateAsync(ct).ConfigureAwait(false);
                    m_subscription = creation.Transfer();
                }
                m_log.ClassicSubscriptionCreated();
            }
            else
            {
                m_subscription.PublishingInterval = (int)config.PublishingInterval.TotalMilliseconds;
                m_subscription.LifetimeCount = config.LifetimeCount;
                m_subscription.KeepAliveCount = config.KeepAliveCount;
                m_subscription.MaxNotificationsPerPublish = config.MaxNotificationsPerPublish;
                m_subscription.Priority = config.Priority;
                m_subscription.PublishingEnabled = config.PublishingEnabled;
                await m_subscription.ModifyAsync(ct).ConfigureAwait(false);
                m_log.ClassicSubscriptionModified();
            }
        }

        public async Task<int> AddItemAsync(MonitoredItemConfig config, CancellationToken ct)
        {
            if (m_subscription is null)
            {
                throw new InvalidOperationException("Apply a subscription before adding items.");
            }
            int id = Interlocked.Increment(ref m_nextItemId);
            MonitoredItemConfig stored = config with { Id = id };

            var mi = new ClassicMonitoredItem(m_session.MessageContext.Telemetry, new ClassicMonitoredItemOptions
            {
                DisplayName = stored.DisplayName,
                StartNodeId = stored.NodeId,
                AttributeId = stored.AttributeId,
                MonitoringMode = stored.MonitoringMode,
                SamplingInterval = (int)stored.SamplingInterval.TotalMilliseconds,
                QueueSize = stored.QueueSize,
                DiscardOldest = stored.DiscardOldest,
                Filter = stored.IsEvent
                    ? DefaultEventFilters.Build()
                    : stored.DataChangeFilter
            });

            lock (m_lock)
            {
                m_subscription.AddItem(mi);
                m_items[id] = new ItemEntry(stored, mi);
            }
            m_stats[id] = new MonitoredItemLiveStats();

            await m_subscription.ApplyChangesAsync(ct).ConfigureAwait(false);
            m_log.ClassicMonitoredItemAdded(id, stored.NodeId);
            return id;
        }

        public async Task RemoveItemAsync(int id, CancellationToken ct)
        {
            if (m_subscription is null || !m_items.TryRemove(id, out ItemEntry? entry))
            {
                return;
            }
            m_stats.TryRemove(id, out _);
            lock (m_lock)
            {
                m_subscription.RemoveItem(entry.MonitoredItem);
            }
            await m_subscription.ApplyChangesAsync(ct).ConfigureAwait(false);
            m_log.ClassicMonitoredItemRemoved(id);
        }

        public async Task ConfigureItemAsync(MonitoredItemConfig config, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(config);
            ct.ThrowIfCancellationRequested();
            if (m_subscription is null || !m_items.TryGetValue(config.Id, out ItemEntry? entry))
            {
                throw new ServiceResultException(StatusCodes.BadMonitoredItemIdInvalid);
            }
            if (config.NodeId != entry.Config.NodeId ||
                config.AttributeId != entry.Config.AttributeId ||
                config.IsEvent != entry.Config.IsEvent)
            {
                throw new ArgumentException("Item settings cannot change the monitored target.", nameof(config));
            }
            entry.MonitoredItem.SamplingInterval = checked((int)config.SamplingInterval.TotalMilliseconds);
            entry.MonitoredItem.QueueSize = config.QueueSize;
            entry.MonitoredItem.DiscardOldest = config.DiscardOldest;
            entry.MonitoredItem.Filter = config.IsEvent ? DefaultEventFilters.Build() : config.DataChangeFilter;
            await m_subscription.ApplyChangesAsync(ct).ConfigureAwait(false);
            if (entry.MonitoredItem.Status.Error is { } error && ServiceResult.IsBad(error))
            {
                throw new ServiceResultException(error);
            }
            if (entry.MonitoredItem.Status.MonitoringMode != config.MonitoringMode)
            {
                await SetMonitoringModeAsync(config.Id, config.MonitoringMode, ct).ConfigureAwait(false);
            }
            m_items[config.Id] = entry with { Config = config };
        }

        public async Task SetMonitoringModeAsync(int id, MonitoringMode mode, CancellationToken ct)
        {
            if (m_subscription is null || !m_items.TryGetValue(id, out ItemEntry? entry))
            {
                return;
            }
            ArrayOf<ClassicMonitoredItem> batch = new[] { entry.MonitoredItem };
            List<ServiceResult?>? errors = await m_subscription
                .SetMonitoringModeAsync(mode, batch, ct)
                .ConfigureAwait(false);
            if (errors is { Count: > 0 } && errors[0] is { } err && ServiceResult.IsBad(err))
            {
                throw new ServiceResultException(err);
            }
            // The classic Subscription already updated the item's MonitoringMode
            // on success; mirror that into the adapter's cached config snapshot.
            MonitoredItemConfig updated = entry.Config with { MonitoringMode = mode };
            m_items[id] = entry with { Config = updated };
            m_log.ClassicMonitoringModeChanged(id, mode);
        }

        public bool TryGetItemStats(int id, [NotNullWhen(true)] out MonitoredItemLiveStats? stats)
        {
            return m_stats.TryGetValue(id, out stats);
        }

        public bool TryGetItemResult(int id, out MonitoredItemResult result)
        {
            if (m_items.TryGetValue(id, out ItemEntry? entry))
            {
                result = new MonitoredItemResult(entry.MonitoredItem.Created, !entry.MonitoredItem.Created,
                    entry.MonitoredItem.Status.Error is { } error ? error.StatusCode : StatusCodes.Good);
                return true;
            }
            result = default;
            return false;
        }

        public async ValueTask DisposeAsync()
        {
            ClassicSubscription? sub = Interlocked.Exchange(ref m_subscription, null);
            try
            {
                if (sub is not null)
                {
                    try
                    {
                        await m_session.RemoveSubscriptionsAsync([sub], CancellationToken.None).ConfigureAwait(false);
                    }
                    finally
                    {
                        sub.Dispose();
                    }
                }
            }
            finally
            {
                m_channel.Writer.TryComplete();
            }
        }

        private void OnDataChange(
            ClassicSubscription subscription,
            DataChangeNotification notification,
            ArrayOf<string> stringTable)
        {
            int n = notification.MonitoredItems.Count;
            Counters.IncDataMessage(n);
            m_publishLog?.Record(subscription.Id, subscription.SequenceNumber,
                (DateTime)subscription.PublishTime, n, PublishLogKind.Data);
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < n; i++)
            {
                MonitoredItemNotification mi = notification.MonitoredItems[i];
                int itemId = ResolveItemId(mi.ClientHandle);
                if (itemId != 0 && m_stats.TryGetValue(itemId, out MonitoredItemLiveStats? stats))
                {
                    stats.RecordValue(mi.Value);
                }
                double? d = VariantNumeric.TryToDouble(mi.Value.WrappedValue, out double parsed)
                    ? parsed : null;
                m_channel.Writer.TryWrite(new NotificationEvent(
                    NotificationKind.DataChange, itemId, 1, subscription.SequenceNumber, now, d));
                CountDroppedNotificationAfterWrite();
            }
        }

        private void OnEvent(
            ClassicSubscription subscription,
            EventNotificationList notification,
            ArrayOf<string> stringTable)
        {
            int n = notification.Events.Count;
            Counters.IncEventMessage(n);
            m_publishLog?.Record(subscription.Id, subscription.SequenceNumber,
                (DateTime)subscription.PublishTime, n, PublishLogKind.Event);
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < n; i++)
            {
                int itemId = ResolveItemId(notification.Events[i].ClientHandle);
                if (itemId != 0 && m_stats.TryGetValue(itemId, out MonitoredItemLiveStats? stats))
                {
                    stats.RecordEvent();
                }
                m_channel.Writer.TryWrite(new NotificationEvent(
                    NotificationKind.Event, itemId, 1, subscription.SequenceNumber, now));
                CountDroppedNotificationAfterWrite();
            }
        }

        private void OnKeepAlive(ClassicSubscription subscription, NotificationData notification)
        {
            Counters.IncKeepAlive();
            m_publishLog?.Record(subscription.Id, subscription.SequenceNumber,
                (DateTime)subscription.PublishTime, 1, PublishLogKind.KeepAlive);
            m_channel.Writer.TryWrite(new NotificationEvent(
                NotificationKind.KeepAlive, 0, 0, subscription.SequenceNumber, DateTime.UtcNow));
            CountDroppedNotificationAfterWrite();
        }

        /// <summary>
        /// Drop-counter increment after a TryWrite (the channel was at
        /// capacity, so DropOldest evicted one).  Kept as a separate post-hoc
        /// check because Classic's hot paths emit ad-hoc; we sample
        /// <see cref="ChannelReader{T}.Count"/> and bump the counter when
        /// the channel was already saturated.
        /// </summary>
        private void CountDroppedNotificationAfterWrite()
        {
            if (m_channel.Reader.CanCount && m_channel.Reader.Count >= 8192)
            {
                Interlocked.Increment(ref m_droppedCount);
            }
        }

        private int ResolveItemId(uint clientHandle)
        {
            foreach (KeyValuePair<int, ItemEntry> kv in m_items)
            {
                if (kv.Value.MonitoredItem.ClientHandle == clientHandle)
                {
                    return kv.Key;
                }
            }
            return 0;
        }

        private sealed record ItemEntry(MonitoredItemConfig Config, ClassicMonitoredItem MonitoredItem);
    }

    internal static partial class ClassicEngineAdapterLog
    {
        [LoggerMessage(EventId = UaLensEventIds.ClassicEngineAdapter + 0, Level = LogLevel.Information,
            Message = "Classic subscription created.")]
        public static partial void ClassicSubscriptionCreated(this ILogger logger);

        [LoggerMessage(EventId = UaLensEventIds.ClassicEngineAdapter + 1, Level = LogLevel.Information,
            Message = "Classic subscription modified.")]
        public static partial void ClassicSubscriptionModified(this ILogger logger);

        [LoggerMessage(EventId = UaLensEventIds.ClassicEngineAdapter + 2, Level = LogLevel.Information,
            Message = "Classic monitored item added: id={Id} node={Node}")]
        public static partial void ClassicMonitoredItemAdded(this ILogger logger, int id, NodeId node);

        [LoggerMessage(EventId = UaLensEventIds.ClassicEngineAdapter + 3, Level = LogLevel.Information,
            Message = "Classic monitored item removed: id={Id}")]
        public static partial void ClassicMonitoredItemRemoved(this ILogger logger, int id);

        [LoggerMessage(EventId = UaLensEventIds.ClassicEngineAdapter + 4, Level = LogLevel.Information,
            Message = "Classic monitored item {Id} mode -> {Mode}")]
        public static partial void ClassicMonitoringModeChanged(this ILogger logger, int id, MonitoringMode mode);
    }
}
