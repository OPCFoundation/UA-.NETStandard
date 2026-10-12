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
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.CranesHoists.Client
{
    public sealed partial class CranesHoistsClient
    {
        /// <summary>
        /// Observes at most sixty-four selected values through a fresh, exclusively owned subscription.
        /// </summary>
        public ValueTask<CraneObservation> ObserveValuesAsync(
            ArrayOf<NodeId> nodes,
            CraneObservationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (nodes.IsNull || nodes.Count is < 1 or > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(nodes));
            }
            var seen = new HashSet<NodeId>();
            foreach (NodeId node in nodes)
            {
                RequireNodeId(node);
                if (!seen.Add(node))
                {
                    throw new ArgumentException("Monitored nodes must be distinct.", nameof(nodes));
                }
            }
            return ObserveAsync(nodes, events: false, options ?? new CraneObservationOptions(), cancellationToken);
        }

        /// <summary>
        /// Observes standard events without acknowledging conditions or changing equipment configuration.
        /// </summary>
        public ValueTask<CraneObservation> ObserveEventsAsync(
            NodeId notifier,
            CraneObservationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            RequireNodeId(notifier);
            return ObserveAsync([notifier], events: true, options ?? new CraneObservationOptions(), cancellationToken);
        }

        /// <summary>
        /// Owns setup, bounded collection and cleanup while leaving all unrelated subscriptions untouched.
        /// </summary>
        private async ValueTask<CraneObservation> ObserveAsync(
            ArrayOf<NodeId> nodes,
            bool events,
            CraneObservationOptions options,
            CancellationToken cancellationToken)
        {
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            if (!Session.TryGetSubscriptionManager(out ISubscriptionManager? manager))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported, "Observation requires an options-based subscription session.");
            }
            EventRecordDecoderRegistry registry = EventRecordDecoderRegistry.Default.CreateChildScope();
            EventFilter? filter = events ? BaseEventTypeRecord.EventFilters.Build(registry) : null;
            using CancellationTokenSource timeout = m_timeProvider.CreateCancellationTokenSource(options.Window);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var collector = new ObservationCollector(nodes, registry, options, Session.MessageContext);
            ISubscription subscription = manager.Add(collector, new OptionsMonitor<SubscriptionOptions>(
                new SubscriptionOptions
                {
                    PublishingEnabled = true,
                    PublishingInterval = TimeSpan.FromMilliseconds(50),
                    KeepAliveCount = 10,
                    LifetimeCount = 300,
                    DisableUnboundedItemMode = true
                }));
            Exception? failure = null;
            CraneObservation? result = null;
            try
            {
                var items = new List<IMonitoredItem>(nodes.Count);
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (!subscription.MonitoredItems.TryAdd(i.ToString(CultureInfo.InvariantCulture),
                        new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions
                        {
                            StartNodeId = nodes[i],
                            AttributeId = events ? Attributes.EventNotifier : Attributes.Value,
                            Filter = filter,
                            SamplingInterval = TimeSpan.FromMilliseconds(50),
                            QueueSize = (uint)options.MaximumNotifications,
                            DiscardOldest = false
                        }), out IMonitoredItem? item) || item == null)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadUnexpectedError, "The monitored item could not be registered.");
                    }
                    items.Add(item);
                }
                foreach (IMonitoredItem item in items)
                {
                    while (!item.Created)
                    {
                        if (collector.Completed.IsFaulted)
                        {
                            await collector.Completed.ConfigureAwait(false);
                        }
                        if (ServiceResult.IsBad(item.Error))
                        {
                            throw new ServiceResultException(item.Error);
                        }
                        await Task.Delay(20, linked.Token).ConfigureAwait(false);
                    }
                }
                try
                {
                    await collector.Completed.WaitAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    // An elapsed observation window is a normal, explicitly bounded result.
                }
                cancellationToken.ThrowIfCancellationRequested();
                result = collector.Snapshot();
            }
            catch (OperationCanceledException error) when (
                timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                failure = new TimeoutException("The observation window expired during subscription setup.", error);
            }
            catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested)
            {
                failure = new OperationCanceledException("The crane observation was canceled by its caller.",
                    error, cancellationToken);
            }
            catch (Exception error)
            {
                failure = error;
            }
            try
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanup)
            {
                failure = failure == null
                    ? cleanup
                    : new AggregateException("Observation and owned-subscription cleanup failed.", failure, cleanup);
            }
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return result ?? throw new InvalidOperationException("No observation result was produced.");
        }

        /// <summary>
        /// Copies bounded notifications and preserves acquisition quality, event identities and overflow evidence.
        /// </summary>
        private sealed class ObservationCollector : ISubscriptionNotificationHandler
        {
            /// <summary>
            /// Captures the immutable selection and its payload budget.
            /// </summary>
            internal ObservationCollector(
                ArrayOf<NodeId> nodes,
                EventRecordDecoderRegistry registry,
                CraneObservationOptions options,
                IServiceMessageContext messageContext)
            {
                m_nodes = nodes;
                m_paths = registry.StandardFields;
                m_options = options;
                m_messageContext = messageContext;
            }

            /// <summary>
            /// Gets completion at the exact count bound or on an explicit subscription failure.
            /// </summary>
            internal Task Completed => m_completed.Task;

            /// <summary>
            /// Returns a stable snapshot after a callback batch finishes.
            /// </summary>
            internal CraneObservation Snapshot()
            {
                lock (m_gate)
                {
                    var paths = new List<ArrayOf<QualifiedName>>(m_paths.Length);
                    foreach (QualifiedName[] path in m_paths)
                    {
                        paths.Add(ArrayOf.Wrapped(path));
                    }
                    return new CraneObservation(m_values.ToArrayOf(), m_events.ToArrayOf(), paths.ToArrayOf(),
                        Count == m_options.MaximumNotifications, m_dropped);
                }
            }

            /// <inheritdoc/>
            public ValueTask OnDataChangeNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                lock (m_gate)
                {
                    foreach (DataValueChange change in notification.Span)
                    {
                        if (change.Value.StatusCode.Overflow)
                        {
                            m_dropped++;
                        }
                        if (!CanRetain())
                        {
                            continue;
                        }
                        try
                        {
                            if (!int.TryParse(change.MonitoredItem?.Name, NumberStyles.None,
                                CultureInfo.InvariantCulture, out int index) || index < 0 || index >= m_nodes.Count)
                            {
                                throw new ServiceResultException(StatusCodes.BadDecodingError);
                            }
                            using var encoder = new JsonEncoder(m_messageContext);
                            encoder.WriteDataValue("Value", change.Value);
                            AddPayload(encoder.CloseAndReturnText());
                            m_values.Add(new CraneObservedValue(m_nodes[index], change.Value));
                            CompleteIfFull();
                        }
                        catch (Exception error)
                        {
                            m_completed.TrySetException(error);
                        }
                    }
                }
                return default;
            }

            /// <inheritdoc/>
            public ValueTask OnEventDataNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                lock (m_gate)
                {
                    foreach (EventNotification item in notification.Span)
                    {
                        GetField(item.Fields, Opc.Ua.BrowseNames.EventType).TryGetValue(out NodeId eventType);
                        if (eventType == Opc.Ua.ObjectTypeIds.EventQueueOverflowEventType)
                        {
                            m_dropped++;
                        }
                        if (!CanRetain())
                        {
                            continue;
                        }
                        try
                        {
                            using var encoder = new JsonEncoder(m_messageContext);
                            encoder.WriteVariantArray("Fields", item.Fields);
                            AddPayload(encoder.CloseAndReturnText());
                            GetField(item.Fields, Opc.Ua.BrowseNames.EventId).TryGetValue(out ByteString eventId);
                            m_events.Add(new CraneObservedEvent(eventType, eventId, item.Fields));
                            CompleteIfFull();
                        }
                        catch (Exception error)
                        {
                            m_completed.TrySetException(error);
                        }
                    }
                }
                return default;
            }

            /// <inheritdoc/>
            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                PublishState publishStateMask)
            {
                return default;
            }

            /// <inheritdoc/>
            public ValueTask OnSubscriptionStateChangedAsync(
                ISubscription subscription,
                SubscriptionState state,
                PublishState publishStateMask,
                CancellationToken ct = default)
            {
                if ((publishStateMask & PublishState.Completed) == 0 &&
                    (state == SubscriptionState.Error ||
                        (publishStateMask &
                            (PublishState.Stopped | PublishState.Timeout | PublishState.Transferred)) != 0))
                {
                    m_completed.TrySetException(new ServiceResultException(
                        StatusCodes.BadNoCommunication, "The observation subscription stopped or failed."));
                }
                return default;
            }

            /// <summary>
            /// Gets the number of retained notifications.
            /// </summary>
            private int Count => m_values.Count + m_events.Count;

            /// <summary>
            /// Enforces exact retention bounds, recording discarded entries from the same callback batch.
            /// </summary>
            private bool CanRetain()
            {
                if (Count == m_options.MaximumNotifications)
                {
                    m_dropped++;
                    return false;
                }
                return !m_completed.Task.IsFaulted;
            }

            /// <summary>
            /// Tracks the decoded payload budget rather than only counting notifications.
            /// </summary>
            private void AddPayload(string text)
            {
                m_payloadBytes += Encoding.UTF8.GetByteCount(text);
                if (m_payloadBytes > m_options.MaximumPayloadBytes)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded, "The observation payload exceeds its configured bound.");
                }
            }

            /// <summary>
            /// Resolves a generated event-filter field by its standard namespace-qualified path.
            /// </summary>
            private Variant GetField(ArrayOf<Variant> fields, string name)
            {
                for (int i = 0; i < m_paths.Length && i < fields.Count; i++)
                {
                    QualifiedName[] path = m_paths[i];
                    if (path.Length == 1 && path[0].NamespaceIndex == 0 && path[0].Name == name)
                    {
                        return fields[i];
                    }
                }
                return Variant.Null;
            }

            /// <summary>
            /// Signals completion only at the configured notification bound.
            /// </summary>
            private void CompleteIfFull()
            {
                if (Count == m_options.MaximumNotifications)
                {
                    m_completed.TrySetResult(true);
                }
            }

            private readonly Lock m_gate = new();
            private readonly ArrayOf<NodeId> m_nodes;
            private readonly QualifiedName[][] m_paths;
            private readonly CraneObservationOptions m_options;
            private readonly IServiceMessageContext m_messageContext;
            private readonly List<CraneObservedValue> m_values = [];
            private readonly List<CraneObservedEvent> m_events = [];
            private readonly TaskCompletionSource<bool> m_completed =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private long m_dropped;
            private long m_payloadBytes;
        }
    }
}
