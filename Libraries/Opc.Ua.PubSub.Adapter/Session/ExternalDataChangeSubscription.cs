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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.PubSub.Adapter.Session
{
    /// <summary>
    /// Default <see cref="IExternalDataChangeSubscription"/> implementation
    /// backed by a single managed client subscription
    /// (<see cref="ISubscription"/>) created through the session's
    /// <see cref="ISubscriptionManager"/>. Monitored items are added
    /// dynamically and the latest value of each is surfaced through the
    /// <see cref="DataChanged"/> event.
    /// </summary>
    internal sealed class ExternalDataChangeSubscription : IExternalDataChangeSubscription
    {
        private static readonly TimeSpan s_applyPollInterval = TimeSpan.FromMilliseconds(25);

        private readonly ISubscription m_subscription;
        private readonly ILogger m_logger;
        private readonly TimeSpan m_publishingInterval;
        private readonly ConcurrentDictionary<uint, NodeId> m_handleToNodeId = new();
        private readonly ConcurrentDictionary<uint, IMonitoredItem> m_items = new();
        private long m_nameCounter;
        private bool m_disposed;

        /// <summary>
        /// Creates a new subscription on the supplied subscription manager using
        /// the requested publishing interval.
        /// </summary>
        public ExternalDataChangeSubscription(
            ISubscriptionManager subscriptionManager,
            double publishingIntervalMs,
            ITelemetryContext telemetry)
        {
            if (subscriptionManager == null)
            {
                throw new ArgumentNullException(nameof(subscriptionManager));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            m_logger = telemetry.CreateLogger<ExternalDataChangeSubscription>();
            m_publishingInterval = publishingIntervalMs > 0
                ? TimeSpan.FromMilliseconds(publishingIntervalMs)
                : TimeSpan.Zero;

            var options = new SubscriptionOptions
            {
                PublishingInterval = m_publishingInterval,
                PublishingEnabled = true
            };
            m_subscription = subscriptionManager.Add(
                new Notifier(this),
                new SingletonOptionsMonitor<SubscriptionOptions>(options));
        }

        /// <inheritdoc/>
        public event EventHandler<ExternalDataChangeEventArgs>? DataChanged;

        /// <inheritdoc/>
        public ValueTask<uint> AddMonitoredItemAsync(
            NodeId nodeId,
            uint attributeId,
            double samplingIntervalMs,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (nodeId.IsNull)
            {
                throw new ArgumentException(
                    "A non-null node id is required.", nameof(nodeId));
            }
            ct.ThrowIfCancellationRequested();

            var options = new MonitoredItemOptions
            {
                StartNodeId = nodeId,
                AttributeId = attributeId,
                SamplingInterval = samplingIntervalMs >= 0
                    ? TimeSpan.FromMilliseconds(samplingIntervalMs)
                    : TimeSpan.FromMilliseconds(-1)
            };

            long ordinal = Interlocked.Increment(ref m_nameCounter);
            string name = string.Format(
                CultureInfo.InvariantCulture, "ext_{0}_{1}", ordinal, nodeId);

            if (m_subscription.MonitoredItems.TryAdd(
                    name,
                    new SingletonOptionsMonitor<MonitoredItemOptions>(options),
                    out IMonitoredItem? item) &&
                item != null)
            {
                m_handleToNodeId[item.ClientHandle] = nodeId;
                m_items[item.ClientHandle] = item;
                return new ValueTask<uint>(item.ClientHandle);
            }

            throw ServiceResultException.Create(
                StatusCodes.BadMonitoredItemIdInvalid,
                "Failed to add monitored item for node {0}.",
                nodeId);
        }

        /// <inheritdoc/>
        public async ValueTask ApplyChangesAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            // The managed subscription engine applies queued monitored items
            // asynchronously. Await until the subscription and every added item
            // is created (or has settled with an error). A best-effort deadline
            // derived from the publishing interval prevents an unbounded wait if
            // no cancellation token is supplied.
            TimeSpan budget = m_publishingInterval > TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(Math.Max(5000, m_publishingInterval.TotalMilliseconds * 10))
                : TimeSpan.FromMilliseconds(5000);
            var watch = Stopwatch.StartNew();

            while (!AllItemsSettled())
            {
                ct.ThrowIfCancellationRequested();
                if (watch.Elapsed >= budget)
                {
                    m_logger.LogDebug(
                        "ExternalDataChangeSubscription: ApplyChangesAsync timed out " +
                        "waiting for monitored item creation; engine continues applying.");
                    return;
                }
                await Task.Delay(s_applyPollInterval, ct).ConfigureAwait(false);
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

            DataChanged = null;
            m_handleToNodeId.Clear();
            m_items.Clear();

            try
            {
                await m_subscription.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.LogDebug(ex,
                    "ExternalDataChangeSubscription: subscription dispose failed.");
            }
        }

        private bool AllItemsSettled()
        {
            if (!m_subscription.Created)
            {
                return false;
            }
            foreach (IMonitoredItem item in m_items.Values)
            {
                if (!item.Created && StatusCode.IsGood(item.Error.StatusCode))
                {
                    return false;
                }
            }
            return true;
        }

        private void DispatchDataChange(in DataValueChange change)
        {
            EventHandler<ExternalDataChangeEventArgs>? handler = DataChanged;
            if (handler == null || change.MonitoredItem == null)
            {
                return;
            }

            uint clientHandle = change.MonitoredItem.ClientHandle;
            NodeId nodeId = m_handleToNodeId.TryGetValue(clientHandle, out NodeId mapped)
                ? mapped
                : NodeId.Null;
            handler(this, new ExternalDataChangeEventArgs(clientHandle, nodeId, change.Value));
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(ExternalDataChangeSubscription));
            }
        }

        private sealed class Notifier : ISubscriptionNotificationHandler
        {
            private readonly ExternalDataChangeSubscription m_parent;

            public Notifier(ExternalDataChangeSubscription parent)
            {
                m_parent = parent;
            }

            public ValueTask OnDataChangeNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask,
                System.Collections.Generic.IReadOnlyList<string> stringTable)
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
                System.Collections.Generic.IReadOnlyList<string> stringTable)
            {
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
                return default;
            }
        }

        private sealed class SingletonOptionsMonitor<[DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>
            : IOptionsMonitor<T>
        {
            public SingletonOptionsMonitor(T value)
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
