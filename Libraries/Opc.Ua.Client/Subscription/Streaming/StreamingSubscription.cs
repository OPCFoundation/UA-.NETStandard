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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client.Subscriptions.Streaming
{
    /// <summary>
    /// Default implementation of <see cref="IStreamingSubscription"/>.
    /// Wraps a single subscription created on demand via the
    /// session's <see cref="ISubscriptionManager"/>. Each
    /// SubscribeXxxAsync call adds a monitored item to the shared
    /// subscription and pipes notifications into a per-call channel.
    /// </summary>
    public sealed class StreamingSubscription : IStreamingSubscription
    {
        private readonly ISubscriptionManager m_subscriptionManager;
        private readonly SubscriptionOptions m_subscriptionOptions;
        private readonly SemaphoreSlim m_initLock = new(1, 1);
        private readonly ConcurrentDictionary<uint, Subscriber> m_subscribers = new();
        private readonly Notifier m_notifier;

        private ISubscription? m_subscription;
        private bool m_disposed;
        private long m_handleCounter;

        /// <summary>
        /// Creates a new streaming subscription backed by the supplied
        /// <see cref="ISubscriptionManager"/>.
        /// </summary>
        public StreamingSubscription(
            ISubscriptionManager subscriptionManager,
            SubscriptionOptions? subscriptionOptions = null)
        {
            m_subscriptionManager = subscriptionManager
                ?? throw new ArgumentNullException(nameof(subscriptionManager));
            m_subscriptionOptions = subscriptionOptions ?? new SubscriptionOptions();
            m_notifier = new Notifier(this);
        }

        /// <inheritdoc/>
        public IAsyncEnumerable<DataValueChange> SubscribeDataChangesAsync(
            NodeId nodeId,
            MonitoredItems.MonitoredItemOptions? options = null,
            CancellationToken ct = default)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentNullException(nameof(nodeId));
            }
            return SubscribeDataChangesImpl([nodeId], options, ct);
        }

        /// <inheritdoc/>
        public IAsyncEnumerable<DataValueChange> SubscribeDataChangesAsync(
            IReadOnlyList<NodeId> nodeIds,
            MonitoredItems.MonitoredItemOptions? options = null,
            CancellationToken ct = default)
        {
            if (nodeIds == null)
            {
                throw new ArgumentNullException(nameof(nodeIds));
            }
            return SubscribeDataChangesImpl(nodeIds, options, ct);
        }

        private async IAsyncEnumerable<DataValueChange> SubscribeDataChangesImpl(
            IReadOnlyList<NodeId> nodeIds,
            MonitoredItems.MonitoredItemOptions? options,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await EnsureSubscriptionAsync(ct).ConfigureAwait(false);

            var channel = Channel.CreateUnbounded<DataValueChange>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

            var monitoredItems = new List<IMonitoredItem>();
            uint handle = (uint)Interlocked.Increment(ref m_handleCounter);
            var subscriber = new Subscriber(channel, isEvent: false);
            m_subscribers[handle] = subscriber;

            try
            {
                foreach (NodeId nodeId in nodeIds)
                {
                    MonitoredItems.MonitoredItemOptions itemOptions = (options ?? new MonitoredItems.MonitoredItemOptions())
                        with
                    { StartNodeId = nodeId };

                    string name = $"stream_data_{handle}_{nodeId}";

                    if (m_subscription!.MonitoredItems.TryAdd(
                            name,
                            new OptionsMonitor<MonitoredItems.MonitoredItemOptions>(itemOptions),
                            out IMonitoredItem? item) && item != null)
                    {
                        monitoredItems.Add(item);
                        subscriber.AddClientHandle(item.ClientHandle);
                    }
                }

                await foreach (DataValueChange change in channel.Reader
                    .ReadAllAsync(ct).ConfigureAwait(false))
                {
                    yield return change;
                }
            }
            finally
            {
                m_subscribers.TryRemove(handle, out _);
                foreach (IMonitoredItem item in monitoredItems)
                {
                    try
                    {
                        m_subscription?.MonitoredItems.TryRemove(item.ClientHandle);
                    }
                    catch
                    {
                        // best effort cleanup
                    }
                }
                channel.Writer.TryComplete();
            }
        }

        /// <inheritdoc/>
        public IAsyncEnumerable<EventNotification> SubscribeEventsAsync(
            NodeId notifierId,
            EventFilter filter,
            MonitoredItems.MonitoredItemOptions? options = null,
            CancellationToken ct = default)
        {
            if (notifierId.IsNull)
            {
                throw new ArgumentNullException(nameof(notifierId));
            }
            if (filter == null)
            {
                throw new ArgumentNullException(nameof(filter));
            }
            return SubscribeEventsImpl(notifierId, filter, options, ct);
        }

        private async IAsyncEnumerable<EventNotification> SubscribeEventsImpl(
            NodeId notifierId,
            EventFilter filter,
            MonitoredItems.MonitoredItemOptions? options,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await EnsureSubscriptionAsync(ct).ConfigureAwait(false);

            MonitoredItems.MonitoredItemOptions itemOptions = (options ?? new MonitoredItems.MonitoredItemOptions())
                with
            {
                StartNodeId = notifierId,
                AttributeId = Attributes.EventNotifier,
                Filter = filter,
                QueueSize = options?.QueueSize > 0 ? options.QueueSize : 10
            };

            var channel = Channel.CreateUnbounded<EventNotification>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

            uint handle = (uint)Interlocked.Increment(ref m_handleCounter);
            var subscriber = new Subscriber(channel, isEvent: true);
            m_subscribers[handle] = subscriber;

            string name = $"stream_event_{handle}_{notifierId}";
            IMonitoredItem? item = null;

            try
            {
                if (m_subscription!.MonitoredItems.TryAdd(
                        name,
                        new OptionsMonitor<MonitoredItems.MonitoredItemOptions>(itemOptions),
                        out item) && item != null)
                {
                    subscriber.AddClientHandle(item.ClientHandle);
                }

                await foreach (EventNotification notification in channel.Reader
                    .ReadAllAsync(ct).ConfigureAwait(false))
                {
                    yield return notification;
                }
            }
            finally
            {
                m_subscribers.TryRemove(handle, out _);
                if (item != null)
                {
                    try
                    {
                        m_subscription?.MonitoredItems.TryRemove(item.ClientHandle);
                    }
                    catch
                    {
                        // best effort cleanup
                    }
                }
                channel.Writer.TryComplete();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;

            // Complete all open channels
            foreach (KeyValuePair<uint, Subscriber> kvp in m_subscribers)
            {
                kvp.Value.Complete();
            }
            m_subscribers.Clear();

            if (m_subscription != null)
            {
                try
                {
                    await m_subscription.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // best effort cleanup
                }
                m_subscription = null;
            }

            m_initLock.Dispose();
        }

        private async ValueTask EnsureSubscriptionAsync(CancellationToken ct)
        {
            if (m_subscription != null)
            {
                return;
            }

            await m_initLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Double-check after acquiring the init lock — another
                // caller may have created the subscription while we
                // were waiting. CA1508's single-threaded flow analysis
                // cannot model the concurrent write so it flags the
                // null check as 'always false'; the check is required
                // for correctness.
#pragma warning disable CA1508
                if (m_subscription != null)
                {
                    return;
                }
#pragma warning restore CA1508

                m_subscription = m_subscriptionManager.Add(
                    m_notifier,
                    new OptionsMonitor<SubscriptionOptions>(m_subscriptionOptions));
            }
            finally
            {
                m_initLock.Release();
            }
        }

        internal void DispatchDataChange(in DataValueChange change)
        {
            if (change.MonitoredItem == null)
            {
                return;
            }

            uint clientHandle = change.MonitoredItem.ClientHandle;
            foreach (KeyValuePair<uint, Subscriber> kvp in m_subscribers)
            {
                if (!kvp.Value.IsEvent && kvp.Value.HandlesClientHandle(clientHandle))
                {
                    kvp.Value.WriteDataChange(change);
                }
            }
        }

        internal void DispatchEvent(in EventNotification notification)
        {
            if (notification.MonitoredItem == null)
            {
                return;
            }

            uint clientHandle = notification.MonitoredItem.ClientHandle;
            foreach (KeyValuePair<uint, Subscriber> kvp in m_subscribers)
            {
                if (kvp.Value.IsEvent && kvp.Value.HandlesClientHandle(clientHandle))
                {
                    kvp.Value.WriteEvent(notification);
                }
            }
        }

        private sealed class Notifier : ISubscriptionNotificationHandler
        {
            private readonly StreamingSubscription m_parent;

            public Notifier(StreamingSubscription parent)
            {
                m_parent = parent;
            }

            public ValueTask OnDataChangeNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                ReadOnlySpan<DataValueChange> span = notification.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    m_parent.DispatchDataChange(span[i]);
                }
                return default;
            }

            public ValueTask OnEventDataNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                ReadOnlySpan<EventNotification> span = notification.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    m_parent.DispatchEvent(span[i]);
                }
                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                PublishState publishStateMask)
            {
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                ISubscription subscription,
                SubscriptionState state,
                PublishState publishStateMask,
                CancellationToken ct = default)
            {
                // Streaming subscription only cares about data/event
                // notification streams; lifecycle transitions are
                // observed by the streaming consumer via the channel
                // completion (DisposeAsync).
                return default;
            }
        }

        private sealed class Subscriber
        {
            private readonly object m_channelObj;
            private readonly HashSet<uint> m_clientHandles = [];
            private readonly object m_handlesLock = new();

            public bool IsEvent { get; }

            public Subscriber(Channel<DataValueChange> channel, bool isEvent)
            {
                m_channelObj = channel;
                IsEvent = isEvent;
            }

            public Subscriber(Channel<EventNotification> channel, bool isEvent)
            {
                m_channelObj = channel;
                IsEvent = isEvent;
            }

            public void AddClientHandle(uint handle)
            {
                lock (m_handlesLock)
                {
                    m_clientHandles.Add(handle);
                }
            }

            public bool HandlesClientHandle(uint handle)
            {
                lock (m_handlesLock)
                {
                    return m_clientHandles.Contains(handle);
                }
            }

            public void WriteDataChange(DataValueChange change)
            {
                if (m_channelObj is Channel<DataValueChange> ch)
                {
                    ch.Writer.TryWrite(change);
                }
            }

            public void WriteEvent(EventNotification notification)
            {
                if (m_channelObj is Channel<EventNotification> ch)
                {
                    ch.Writer.TryWrite(notification);
                }
            }

            public void Complete()
            {
                switch (m_channelObj)
                {
                    case Channel<DataValueChange> ch1:
                        ch1.Writer.TryComplete();
                        break;
                    case Channel<EventNotification> ch2:
                        ch2.Writer.TryComplete();
                        break;
                }
            }
        }

        private sealed class OptionsMonitor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>
            : IOptionsMonitor<T>
        {
            public OptionsMonitor(T value)
            {
                CurrentValue = value;
            }

            public T CurrentValue { get; }

            public T Get(string? name)
            {
                return CurrentValue;
            }

            public IDisposable? OnChange(Action<T, string?> listener)
            {
                return null;
            }
        }

    }
}

