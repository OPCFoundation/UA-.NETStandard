/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using V2MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using V2SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

namespace UaLens.Subscriptions;

/// <summary>
/// Adapter on top of the V2 channel-based subscription engine
/// (<see cref="ISubscriptionManager"/> + <see cref="ISubscriptionNotificationHandler"/>).
/// </summary>
internal sealed class ChannelV2EngineAdapter : ISubscriptionAdapter
{
    private readonly ManagedSession m_session;
    private readonly ILogger m_log;
    private readonly Channel<NotificationEvent> m_channel;
    private readonly ConcurrentDictionary<int, ItemEntry> m_items = new();
    private readonly object m_lock = new();
    private OptionsMonitor<V2SubscriptionOptions>? m_subscriptionOptions;
    private ISubscription? m_subscription;
    private SubscriptionConfig m_currentConfig = new();
    private int m_nextItemId;

    public SubscriptionCounters Counters { get; } = new();
    public ChannelReader<NotificationEvent> Events { get; }

    public TimeSpan CurrentPublishingInterval => m_subscription?.CurrentPublishingInterval ?? TimeSpan.Zero;
    public uint CurrentKeepAliveCount => m_subscription?.CurrentKeepAliveCount ?? 0;
    public uint CurrentLifetimeCount => m_subscription?.CurrentLifetimeCount ?? 0;

    public int PublishWorkerCount
    {
        get
        {
            try
            {
                return m_session.SubscriptionManager.PublishWorkerCount;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }
    }

    public int GoodPublishRequestCount => m_session.GoodPublishRequestCount;

    public int BadPublishRequestCount
    {
        get
        {
            try
            {
                return m_session.SubscriptionManager.BadPublishRequestCount;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }
    }

    public long MissingMessageCount
    {
        get
        {
            try
            { return m_session.SubscriptionManager.MissingMessageCount; }
            catch (InvalidOperationException) { return 0; }
        }
    }

    public long RepublishMessageCount
    {
        get
        {
            try
            { return m_session.SubscriptionManager.RepublishMessageCount; }
            catch (InvalidOperationException) { return 0; }
        }
    }

    public long DroppedNotificationCount => System.Threading.Volatile.Read(ref m_droppedCount);

    private long m_droppedCount;

    /// <summary>Currently-configured floor for the V2 worker pool.</summary>
    public int MinPublishWorkerCount
    {
        get
        {
            try
            { return m_session.SubscriptionManager.MinPublishWorkerCount; }
            catch (InvalidOperationException) { return 0; }
        }
    }

    /// <summary>Currently-configured ceiling for the V2 worker pool.</summary>
    public int MaxPublishWorkerCount
    {
        get
        {
            try
            { return m_session.SubscriptionManager.MaxPublishWorkerCount; }
            catch (InvalidOperationException) { return 0; }
        }
    }

    public int MinPublishRequestCount => m_session.MinPublishRequestCount;
    public int MaxPublishRequestCount => m_session.MaxPublishRequestCount;
    public bool HasWorkerPool => true;

    public IReadOnlyList<MonitoredItemConfig> Items
        => m_items.Values.OrderBy(e => e.Config.Id).Select(e => e.Config).ToList();

    public ChannelV2EngineAdapter(ManagedSession session, ITelemetryContext telemetry)
    {
        m_session = session;
        m_log = telemetry.CreateLogger("ChannelV2Adapter");
        m_channel = Channel.CreateBounded<NotificationEvent>(new BoundedChannelOptions(8192)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        Events = m_channel.Reader;
    }

    /// <summary>
    /// Writes a notification to the channel and increments the dropped
    /// counter when the channel is at capacity (DropOldest mode evicts an
    /// event to make room).  Uses <see cref="ChannelReader{T}.Count"/> via
    /// <see cref="ChannelReader{T}.CanCount"/> when supported, falling back
    /// to a no-op count update otherwise.
    /// </summary>
    internal void WriteEventOrCount(NotificationEvent ev)
    {
        if (m_channel.Reader.CanCount && m_channel.Reader.Count >= 8192)
        {
            System.Threading.Interlocked.Increment(ref m_droppedCount);
        }
        m_channel.Writer.TryWrite(ev);
    }

    public Task ApplySubscriptionAsync(SubscriptionConfig config, CancellationToken ct)
    {
        lock (m_lock)
        {
            m_currentConfig = config;
            V2SubscriptionOptions options = ToOptions(config);
            // m_subscriptionOptions and m_subscription are set together; reuse
            // the existing OptionsMonitor when the subscription is already up.
            OptionsMonitor<V2SubscriptionOptions> opts = m_subscriptionOptions ??=
                new OptionsMonitor<V2SubscriptionOptions>(options);
            if (m_subscription is null)
            {
                var handler = new Handler(this);
                m_subscription = m_session.SubscriptionManager.Add(handler, opts);
                m_log.LogInformation("V2 subscription created.");
            }
            else
            {
                opts.CurrentValue = options;
                m_log.LogInformation("V2 subscription options updated.");
            }
            ApplyEngineSettings(config);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Pushes the engine-level tuning knobs onto the V2 subscription manager
    /// and the underlying session's publish pipeline.  In the V2 engine the
    /// publish-worker pool size is the publish-request pipeline depth, so a
    /// single (min, max) pair drives both.
    /// </summary>
    private void ApplyEngineSettings(SubscriptionConfig config)
    {
        try
        {
            int minReq = Math.Max(1, config.MinPublishRequestCount);
            int maxReq = Math.Max(minReq, config.MaxPublishRequestCount);
            // Order matters — bump max first when growing, min first when shrinking.
            if (maxReq >= m_session.MaxPublishRequestCount)
            {
                m_session.MaxPublishRequestCount = maxReq;
                m_session.MinPublishRequestCount = minReq;
            }
            else
            {
                m_session.MinPublishRequestCount = minReq;
                m_session.MaxPublishRequestCount = maxReq;
            }
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Failed to apply publish-request pipeline limits.");
        }
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
            // TryAdd's out IMonitoredItem? is non-null when it returns true,
            // but the SDK's signature lacks [NotNullWhen(true)]; the throw
            // above guarantees we only reach here on success.
            m_items[id] = new ItemEntry(stored, created!, optionsMonitor);
        }
        m_log.LogInformation("V2 monitored item added: id={Id} node={Node} attr={Attr}", id, stored.NodeId, stored.AttributeId);
        return Task.FromResult(id);
    }

    public Task RemoveItemAsync(int id, CancellationToken ct)
    {
        if (m_subscription is null || !m_items.TryRemove(id, out ItemEntry? entry))
        {
            return Task.CompletedTask;
        }
        lock (m_lock)
        {
            m_subscription.MonitoredItems.TryRemove(entry.MonitoredItem.ClientHandle);
        }
        m_log.LogInformation("V2 monitored item removed: id={Id}", id);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        ISubscription? sub;
        lock (m_lock)
        {
            sub = m_subscription;
            m_subscription = null;
        }
        if (sub is not null)
        {
            try
            { await sub.DisposeAsync().ConfigureAwait(false); }
            catch { /* ignore on shutdown */ }
        }
        m_channel.Writer.TryComplete();
    }

    private static V2SubscriptionOptions ToOptions(SubscriptionConfig c)
        => new()
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

    private static V2MonitoredItemOptions ToOptions(MonitoredItemConfig c)
    {
        var opts = new V2MonitoredItemOptions
        {
            StartNodeId = c.NodeId,
            AttributeId = c.AttributeId,
            SamplingInterval = c.SamplingInterval,
            QueueSize = c.QueueSize,
            DiscardOldest = c.DiscardOldest,
            MonitoringMode = c.MonitoringMode,
            Filter = c.IsEvent ? DefaultEventFilters.Build() : null
        };
        return opts;
    }

    private sealed record ItemEntry(MonitoredItemConfig Config, IMonitoredItem MonitoredItem,
        OptionsMonitor<V2MonitoredItemOptions> Options);

    /// <summary>
    /// V2 notification handler: counters always update; the channel write may drop oldest under burst.
    /// </summary>
    private sealed class Handler : ISubscriptionNotificationHandler
    {
        private readonly ChannelV2EngineAdapter m_owner;

        public Handler(ChannelV2EngineAdapter owner) => m_owner = owner;

        public ValueTask OnDataChangeNotificationAsync(ISubscription subscription,
            uint sequenceNumber, DateTime publishTime,
            ReadOnlyMemory<DataValueChange> notification,
            PublishState publishStateMask, IReadOnlyList<string> stringTable)
        {
            int n = notification.Length;
            m_owner.Counters.IncDataMessage(n);
            // Emit one NotificationEvent per individual DataValueChange so
            // the Lines view can plot each value's converted-to-double
            // sample.  The Variant→double conversion (via VariantNumeric)
            // returns null when the value is non-numeric / non-scalar /
            // unparseable; the Lines lane silently skips those, while the
            // Dots and Bars views still see the event (keyed by ItemId).
            DateTime now = DateTime.UtcNow;
            ReadOnlySpan<DataValueChange> span = notification.Span;
            for (int i = 0; i < span.Length; i++)
            {
                int itemId = ResolveItemId(span[i].MonitoredItem);
                double? d = VariantNumeric.TryToDouble(span[i].Value.WrappedValue, out double parsed)
                    ? parsed : (double?)null;
                m_owner.WriteEventOrCount(new NotificationEvent(
                    NotificationKind.DataChange, itemId, 1, sequenceNumber, now, d));
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask OnEventDataNotificationAsync(ISubscription subscription,
            uint sequenceNumber, DateTime publishTime,
            ReadOnlyMemory<EventNotification> notification,
            PublishState publishStateMask, IReadOnlyList<string> stringTable)
        {
            int n = notification.Length;
            m_owner.Counters.IncEventMessage(n);
            // Events have field arrays — no single double — so we still
            // emit one event per item (without a Value).
            ReadOnlySpan<EventNotification> span = notification.Span;
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < span.Length; i++)
            {
                int itemId = ResolveItemId(span[i].MonitoredItem);
                m_owner.WriteEventOrCount(new NotificationEvent(
                    NotificationKind.Event, itemId, 1, sequenceNumber, now));
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask OnKeepAliveNotificationAsync(ISubscription subscription,
            uint sequenceNumber, DateTime publishTime, PublishState publishStateMask)
        {
            m_owner.Counters.IncKeepAlive();
            m_owner.WriteEventOrCount(new NotificationEvent(
                NotificationKind.KeepAlive, 0, 0, sequenceNumber, DateTime.UtcNow));
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
    }
}
