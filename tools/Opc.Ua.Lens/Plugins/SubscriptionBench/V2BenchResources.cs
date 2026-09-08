/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using UaLens.Subscriptions;
using UaLens.Views;
using V2MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using V2SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

namespace UaLens.Plugins.SubscriptionBench;

/// <summary>
/// V2-stack-backed <see cref="IBenchResourceFactory"/>. Every bench subscription
/// shares one <see cref="ISubscriptionNotificationHandler"/> (feeding the throughput
/// counters) and one <see cref="OptionsMonitor{T}"/> of subscription options, so a
/// single parameter edit propagates to every live subscription. Only the V2 channel
/// engine exposes a subscription manager; on the classic engine
/// <see cref="IsReady"/> is false and the bench refuses to grow. The session-wide
/// publish pipeline is intentionally never touched here — that belongs to the
/// primary connection.
/// </summary>
internal sealed class V2BenchResourceFactory : IBenchResourceFactory
{
    private readonly ISession m_session;
    private readonly ISubscriptionNotificationHandler m_handler;
    private readonly OptionsMonitor<V2SubscriptionOptions> m_sharedOptions;

    public V2BenchResourceFactory(
        ISession session,
        BenchThroughputCounters counters,
        SubscriptionConfig initialConfig)
    {
        m_session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(initialConfig);
        m_handler = new BenchNotificationHandler(counters);
        m_sharedOptions = new OptionsMonitor<V2SubscriptionOptions>(V2BenchSubscription.Map(initialConfig));
    }

    public bool IsReady => m_session.TryGetSubscriptionManager(out _);

    public IBenchSubscription CreateSubscription()
    {
        if (!m_session.TryGetSubscriptionManager(out ISubscriptionManager? manager) || manager is null)
        {
            throw new InvalidOperationException(
                "The V2 (channel) subscription engine is required for the Subscription Bench.");
        }
        ISubscription subscription = manager.Add(m_handler, m_sharedOptions);
        return new V2BenchSubscription(subscription, m_sharedOptions);
    }

    private sealed class BenchNotificationHandler : ISubscriptionNotificationHandler
    {
        private readonly BenchThroughputCounters m_counters;

        public BenchNotificationHandler(BenchThroughputCounters counters)
        {
            m_counters = counters;
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
            if (n <= 0)
            {
                return ValueTask.CompletedTask;
            }
            int bad = 0;
            ReadOnlySpan<DataValueChange> span = notification.Span;
            for (int i = 0; i < span.Length; i++)
            {
                if (StatusCode.IsBad(span[i].Value.StatusCode))
                {
                    bad++;
                }
            }
            m_counters.Record(n, bad);
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
            // The bench only creates value-monitored items; events are ignored.
            return ValueTask.CompletedTask;
        }

        public ValueTask OnKeepAliveNotificationAsync(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            PublishState publishStateMask)
        {
            // Keep-alives carry no data; the throughput maths ignores them.
            return ValueTask.CompletedTask;
        }

        public ValueTask OnSubscriptionStateChangedAsync(
            ISubscription subscription,
            Opc.Ua.Client.Subscriptions.SubscriptionState state,
            PublishState publishStateMask,
            CancellationToken ct = default)
        {
            // Lifecycle transitions do not contribute to the throughput counters.
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// A live V2 <see cref="ISubscription"/> plus the shared options monitor. Editing
/// the monitor's value re-applies to every subscription bound to it.
/// </summary>
internal sealed class V2BenchSubscription : IBenchSubscription
{
    private readonly ISubscription m_subscription;
    private readonly OptionsMonitor<V2SubscriptionOptions> m_sharedOptions;

    public V2BenchSubscription(
        ISubscription subscription,
        OptionsMonitor<V2SubscriptionOptions> sharedOptions)
    {
        m_subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
        m_sharedOptions = sharedOptions ?? throw new ArgumentNullException(nameof(sharedOptions));
    }

    public double RevisedPublishingIntervalMs => m_subscription.CurrentPublishingInterval.TotalMilliseconds;

    public void ApplySubscriptionConfig(SubscriptionConfig config)
    {
        m_sharedOptions.CurrentValue = Map(config);
    }

    public IBenchItem? TryAddItem(string key, NodeId node, MonitoredItemSettings settings)
    {
        var options = new V2MonitoredItemOptions
        {
            StartNodeId = node,
            AttributeId = Attributes.Value,
            SamplingInterval = settings.SamplingInterval,
            QueueSize = settings.QueueSize,
            DiscardOldest = settings.DiscardOldest,
            MonitoringMode = settings.MonitoringMode,
            Filter = settings.DataChangeFilter
        };
        var monitor = new OptionsMonitor<V2MonitoredItemOptions>(options);
        if (m_subscription.MonitoredItems.TryAdd(key, monitor, out IMonitoredItem? created) && created is not null)
        {
            return new V2BenchItem(m_subscription.MonitoredItems, created, monitor);
        }
        return null;
    }

    public ValueTask DisposeAsync() => m_subscription.DisposeAsync();

    internal static V2SubscriptionOptions Map(SubscriptionConfig config)
    {
        return new V2SubscriptionOptions
        {
            PublishingInterval = config.PublishingInterval,
            KeepAliveCount = config.KeepAliveCount,
            LifetimeCount = config.LifetimeCount,
            MaxNotificationsPerPublish = config.MaxNotificationsPerPublish,
            Priority = config.Priority,
            PublishingEnabled = config.PublishingEnabled,
            Disabled = false,
            MinLifetimeInterval = TimeSpan.FromMinutes(1)
        };
    }
}

/// <summary>
/// A live V2 <see cref="IMonitoredItem"/> and its per-item options monitor. Settings
/// edits update the monitor in place, preserving the item's node and attribute so the
/// item is modified on the wire rather than recreated.
/// </summary>
internal sealed class V2BenchItem : IBenchItem
{
    private readonly IMonitoredItemCollection m_items;
    private readonly IMonitoredItem m_item;
    private readonly OptionsMonitor<V2MonitoredItemOptions> m_options;

    public V2BenchItem(
        IMonitoredItemCollection items,
        IMonitoredItem item,
        OptionsMonitor<V2MonitoredItemOptions> options)
    {
        m_items = items ?? throw new ArgumentNullException(nameof(items));
        m_item = item ?? throw new ArgumentNullException(nameof(item));
        m_options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool IsBad
    {
        get
        {
            ServiceResult error = m_item.Error;
            return error is not null && ServiceResult.IsBad(error);
        }
    }

    public void ApplySettings(MonitoredItemSettings settings)
    {
        V2MonitoredItemOptions current = m_options.CurrentValue;
        m_options.CurrentValue = new V2MonitoredItemOptions
        {
            StartNodeId = current.StartNodeId,
            AttributeId = current.AttributeId,
            SamplingInterval = settings.SamplingInterval,
            QueueSize = settings.QueueSize,
            DiscardOldest = settings.DiscardOldest,
            MonitoringMode = settings.MonitoringMode,
            Filter = settings.DataChangeFilter
        };
    }

    public bool Remove() => m_items.TryRemove(m_item.ClientHandle);
}
