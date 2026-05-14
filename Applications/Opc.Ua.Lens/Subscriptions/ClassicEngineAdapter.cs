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
using ClassicMonitoredItem = Opc.Ua.Client.MonitoredItem;
using ClassicMonitoredItemOptions = Opc.Ua.Client.MonitoredItemOptions;
using ClassicSubscription = Opc.Ua.Client.Subscription;
using ClassicSubscriptionOptions = Opc.Ua.Client.SubscriptionOptions;

namespace UaLens.Subscriptions;

/// <summary>
/// Adapter on top of the legacy <see cref="ClassicSubscription"/> with
/// <c>FastDataChange / FastEvent / FastKeepAlive</c> callbacks.
/// </summary>
internal sealed class ClassicEngineAdapter : ISubscriptionAdapter
{
    private readonly ManagedSession m_session;
    private readonly ILogger m_log;
    private readonly Channel<NotificationEvent> m_channel;
    private readonly ConcurrentDictionary<int, ItemEntry> m_items = new();
    private readonly object m_lock = new();
    // CA2213: m_subscription IS disposed in DisposeAsync below, but the
    // analyzer can't track lifecycle through Interlocked.Exchange.
#pragma warning disable CA2213
    private ClassicSubscription? m_subscription;
#pragma warning restore CA2213
    private SubscriptionConfig m_currentConfig = new();
    private int m_nextItemId;

    public SubscriptionCounters Counters { get; } = new();
    public ChannelReader<NotificationEvent> Events { get; }

    public TimeSpan CurrentPublishingInterval
        => TimeSpan.FromMilliseconds(m_subscription?.CurrentPublishingInterval ?? 0);

    public uint CurrentKeepAliveCount => m_subscription?.CurrentKeepAliveCount ?? 0;
    public uint CurrentLifetimeCount => m_subscription?.CurrentLifetimeCount ?? 0;

    // Classic engine has no worker pool.  Surface the session's outstanding
    // publish request count for parity, and report 0 for worker / bad counts.
    public int PublishWorkerCount => 0;
    public int GoodPublishRequestCount => m_session.GoodPublishRequestCount;
    public int BadPublishRequestCount => 0;
    public long MissingMessageCount => 0;
    public long RepublishMessageCount => 0;
    public long DroppedNotificationCount => System.Threading.Volatile.Read(ref m_droppedCount);

    private long m_droppedCount;

    private void WriteEventOrCount(NotificationEvent ev)
    {
        if (m_channel.Reader.CanCount && m_channel.Reader.Count >= 8192)
        {
            System.Threading.Interlocked.Increment(ref m_droppedCount);
        }
        m_channel.Writer.TryWrite(ev);
    }
    public int MinPublishWorkerCount => 0;
    public int MaxPublishWorkerCount => 0;
    public int MinPublishRequestCount => m_session.MinPublishRequestCount;
    public int MaxPublishRequestCount => m_session.MaxPublishRequestCount;
    public bool HasWorkerPool => false;

    public IReadOnlyList<MonitoredItemConfig> Items
        => m_items.Values.OrderBy(e => e.Config.Id).Select(e => e.Config).ToList();

    public ClassicEngineAdapter(ManagedSession session, ITelemetryContext telemetry)
    {
        m_session = session;
        m_log = telemetry.CreateLogger("ClassicAdapter");
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
        m_currentConfig = config;
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
            m_session.AddSubscription(sub);
            await sub.CreateAsync(ct).ConfigureAwait(false);
            m_subscription = sub;
            m_log.LogInformation("Classic subscription created.");
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
            m_log.LogInformation("Classic subscription modified.");
        }
        ApplyEngineSettings(config);
    }

    /// <summary>
    /// Pushes the publish-request pipeline depth onto the underlying session.
    /// The Classic engine has no worker pool, so worker fields in
    /// <paramref name="config"/> are ignored.
    /// </summary>
    private void ApplyEngineSettings(SubscriptionConfig config)
    {
        try
        {
            int minReq = Math.Max(1, config.MinPublishRequestCount);
            int maxReq = Math.Max(minReq, config.MaxPublishRequestCount);
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
            Filter = stored.IsEvent ? DefaultEventFilters.Build() : null
        });

        lock (m_lock)
        {
            m_subscription.AddItem(mi);
            m_items[id] = new ItemEntry(stored, mi);
        }

        await m_subscription.ApplyChangesAsync(ct).ConfigureAwait(false);
        m_log.LogInformation("Classic monitored item added: id={Id} node={Node}", id, stored.NodeId);
        return id;
    }

    public async Task RemoveItemAsync(int id, CancellationToken ct)
    {
        if (m_subscription is null || !m_items.TryRemove(id, out ItemEntry? entry))
        {
            return;
        }
        lock (m_lock)
        {
            m_subscription.RemoveItem(entry.MonitoredItem);
        }
        await m_subscription.ApplyChangesAsync(ct).ConfigureAwait(false);
        m_log.LogInformation("Classic monitored item removed: id={Id}", id);
    }

    public async ValueTask DisposeAsync()
    {
        ClassicSubscription? sub = Interlocked.Exchange(ref m_subscription, null);
        if (sub is not null)
        {
            try
            {
                await sub.DeleteAsync(silent: true, CancellationToken.None).ConfigureAwait(false);
                sub.Dispose();
            }
            catch { /* ignore on shutdown */ }
        }
        m_channel.Writer.TryComplete();
    }

    private void OnDataChange(ClassicSubscription subscription, DataChangeNotification notification, ArrayOf<string> stringTable)
    {
        int n = notification.MonitoredItems.Count;
        Counters.IncDataMessage(n);
        DateTime now = DateTime.UtcNow;
        for (int i = 0; i < n; i++)
        {
            MonitoredItemNotification mi = notification.MonitoredItems[i];
            int itemId = ResolveItemId(mi.ClientHandle);
            double? d = VariantNumeric.TryToDouble(mi.Value.WrappedValue, out double parsed)
                ? parsed : (double?)null;
            m_channel.Writer.TryWrite(new NotificationEvent(
                NotificationKind.DataChange, itemId, 1, subscription.SequenceNumber, now, d));
            WriteEventOrCount_NoOpHelper();
        }
    }

    private void OnEvent(ClassicSubscription subscription, EventNotificationList notification, ArrayOf<string> stringTable)
    {
        int n = notification.Events.Count;
        Counters.IncEventMessage(n);
        DateTime now = DateTime.UtcNow;
        for (int i = 0; i < n; i++)
        {
            int itemId = ResolveItemId(notification.Events[i].ClientHandle);
            m_channel.Writer.TryWrite(new NotificationEvent(
                NotificationKind.Event, itemId, 1, subscription.SequenceNumber, now));
            WriteEventOrCount_NoOpHelper();
        }
    }

    private void OnKeepAlive(ClassicSubscription subscription, NotificationData notification)
    {
        Counters.IncKeepAlive();
        m_channel.Writer.TryWrite(new NotificationEvent(NotificationKind.KeepAlive, 0, 0, subscription.SequenceNumber, DateTime.UtcNow));
        WriteEventOrCount_NoOpHelper();
    }

    /// <summary>
    /// Drop-counter increment after a TryWrite (the channel was at
    /// capacity, so DropOldest evicted one).  Kept as a separate post-hoc
    /// check because Classic's hot paths emit ad-hoc; we sample
    /// <see cref="ChannelReader{T}.Count"/> and bump the counter when
    /// the channel was already saturated.
    /// </summary>
    private void WriteEventOrCount_NoOpHelper()
    {
        if (m_channel.Reader.CanCount && m_channel.Reader.Count >= 8192)
        {
            System.Threading.Interlocked.Increment(ref m_droppedCount);
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
