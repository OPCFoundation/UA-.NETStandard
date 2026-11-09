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
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Vision.MachineVision;

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// Selects ordinary events or condition notifications.
    /// </summary>
    public enum MachineVisionObservationMode
    {
        /// <summary>Observe events from the selected notifier.</summary>
        Events,

        /// <summary>Observe conditions, optionally including retained-condition refresh.</summary>
        Conditions
    }

    /// <summary>
    /// Bounds a one-shot observation without creating persistent monitoring handles.
    /// </summary>
    public sealed record MachineVisionObservationOptions
    {
        /// <summary>
        /// Gets the total call window, including monitored-item creation.
        /// </summary>
        public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets the maximum notifications retained by the call.
        /// </summary>
        public int MaximumNotifications { get; init; } = 50;

        /// <summary>
        /// Gets the selected notification mode.
        /// </summary>
        public MachineVisionObservationMode Mode { get; init; }

        /// <summary>
        /// Gets whether condition mode requests a retained-condition refresh.
        /// </summary>
        public bool RefreshConditions { get; init; }

        /// <summary>
        /// Validates the one-shot limits before any server-side resources are created.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public void Validate()
        {
            if (Window <= TimeSpan.Zero || Window > TimeSpan.FromSeconds(60))
            {
                throw new ArgumentOutOfRangeException(nameof(Window));
            }
            if (MaximumNotifications is < 1 or > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumNotifications));
            }
            if (Mode is not (MachineVisionObservationMode.Events or MachineVisionObservationMode.Conditions))
            {
                throw new ArgumentOutOfRangeException(nameof(Mode));
            }
            if (RefreshConditions && Mode != MachineVisionObservationMode.Conditions)
            {
                throw new ArgumentException("Retained-condition refresh requires condition mode.");
            }
        }
    }

    /// <summary>
    /// An event's exact action identifiers and generated decoding, plus all selected field values.
    /// </summary>
    /// <param name="EventType">The server event type.</param>
    /// <param name="EventId">The exact event identifier to use for acknowledgement or confirmation.</param>
    /// <param name="ConditionId">The condition node, or null for other events.</param>
    /// <param name="Decoded">The source-generated decoding, when the type is registered.</param>
    /// <param name="Fields">Selected values in FieldPaths order, including bad status values.</param>
    public sealed record MachineVisionObservedEvent(
        NodeId EventType, ByteString EventId, NodeId ConditionId, EventRecord? Decoded, ArrayOf<Variant> Fields);

    /// <summary>
    /// A bounded observation, not an unconditional claim of a complete alarm inventory.
    /// </summary>
    /// <param name="Events">Notifications captured before the bound.</param>
    /// <param name="FieldPaths">Namespace-qualified paths corresponding to every event's Fields.</param>
    /// <param name="LimitReached">Whether the notification-count bound ended observation.</param>
    /// <param name="RefreshComplete">Whether the requested refresh start/end pair was observed.</param>
    /// <param name="DroppedNotifications">Local discards plus observed server overflow markers.</param>
    public sealed record MachineVisionObservation(
        ArrayOf<MachineVisionObservedEvent> Events,
        ArrayOf<ArrayOf<QualifiedName>> FieldPaths,
        bool LimitReached,
        bool RefreshComplete,
        long DroppedNotifications);

    public sealed partial class MachineVisionClient
    {
        /// <summary>
        /// Observes events through an owned V2 subscription and always disposes that subscription.
        /// Normal window expiry returns a bounded result; caller cancellation remains an error.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public async ValueTask<MachineVisionObservation> ObserveAsync(
            NodeId notifier,
            MachineVisionObservationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (notifier.IsNull)
            {
                throw new ArgumentNullException(nameof(notifier));
            }
            MachineVisionObservationOptions settings = options ?? new MachineVisionObservationOptions();
            settings.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            if (!Session.TryGetSubscriptionManager(out ISubscriptionManager? manager))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "Machine Vision observation requires an options-based subscription session.");
            }
            EventRecordDecoderRegistry registry = EventRecordDecoderRegistry.Default.CreateChildScope();
            if (SupportsControl)
            {
                registry.RegisterMachineVisionDecoders(Session.NamespaceUris);
            }
            EventFilter filter = BaseEventTypeRecord.EventFilters.Build(registry);
            using CancellationTokenSource timeout = m_timeProvider.CreateCancellationTokenSource(settings.Window);
            using var linked =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var collector = new ObservationCollector(registry, settings, Session.MessageContext);
            ISubscription subscription = manager.Add(collector, new OptionsMonitor<SubscriptionOptions>(
                new SubscriptionOptions
                {
                    PublishingEnabled = true,
                    PublishingInterval = TimeSpan.FromMilliseconds(100),
                    KeepAliveCount = 10,
                    LifetimeCount = 300,
                    DisableUnboundedItemMode = true
                }));
            Exception? failure = null;
            MachineVisionObservation? result = null;
            try
            {
                if (!subscription.MonitoredItems.TryAdd("MachineVisionObservation",
                    new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions
                    {
                        StartNodeId = notifier,
                        AttributeId = Attributes.EventNotifier,
                        Filter = filter,
                        QueueSize = 100,
                        DiscardOldest = false
                    }), out IMonitoredItem? item) ||
                    item == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                        "The event monitored item could not be registered.");
                }
                while (!item.Created)
                {
                    if (collector.LimitReached.IsFaulted)
                    {
                        await collector.LimitReached.ConfigureAwait(false);
                    }
                    if (ServiceResult.IsBad(item.Error))
                    {
                        throw new ServiceResultException(item.Error);
                    }
                    await Task.Delay(20, linked.Token).ConfigureAwait(false);
                }
                if (settings.RefreshConditions)
                {
                    await item.ConditionRefreshAsync(linked.Token).ConfigureAwait(false);
                }
                try
                {
                    await collector.LimitReached.WaitAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    // The configured observation window ending is a normal, explicitly bounded result.
                }
                cancellationToken.ThrowIfCancellationRequested();
                result = collector.Snapshot();
            }
            catch (OperationCanceledException error) when (
                timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                failure = new TimeoutException("The observation window expired during subscription setup or refresh.",
                    error);
            }
            catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested)
            {
                failure = new OperationCanceledException("The observation was canceled by its caller.",
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
                failure = failure == null ? cleanup :
                    new AggregateException("Observation and subscription cleanup both failed.", failure, cleanup);
            }
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return result ?? throw new InvalidOperationException("The observation produced no result.");
        }

        /// <summary>
        /// Collects bounded notifications without borrowing the session's shared streaming subscription.
        /// </summary>
        private sealed class ObservationCollector : ISubscriptionNotificationHandler
        {
            public ObservationCollector(
                EventRecordDecoderRegistry registry, MachineVisionObservationOptions options,
                IServiceMessageContext messageContext)
            {
                m_registry = registry;
                m_options = options;
                m_paths = registry.StandardFields;
                m_messageContext = messageContext;
            }

            public Task LimitReached => m_limitReached.Task;

            public MachineVisionObservation Snapshot()
            {
                lock (m_lock)
                {
                    var paths = new List<ArrayOf<QualifiedName>>();
                    foreach (QualifiedName[] path in m_paths)
                    {
                        paths.Add(ArrayOf.Wrapped(path));
                    }
                    return new MachineVisionObservation(m_events.ToArrayOf(), paths.ToArrayOf(),
                        m_events.Count == m_options.MaximumNotifications, m_refreshComplete, m_dropped);
                }
            }

            public ValueTask OnEventDataNotificationAsync(ISubscription subscription,
                uint sequenceNumber, DateTime publishTime, ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask, IReadOnlyList<string> stringTable)
            {
                foreach (EventNotification item in notification.Span)
                {
                    ArrayOf<Variant> fields = item.Fields;
                    GetField(fields, BrowseNames.EventType).TryGetValue(out NodeId eventType);
                    GetField(fields, BrowseNames.EventId).TryGetValue(out ByteString eventId);
                    GetField(fields, string.Empty).TryGetValue(out NodeId conditionId);
                    lock (m_lock)
                    {
                        if (eventType == ObjectTypeIds.RefreshStartEventType)
                        {
                            m_refreshStarted = true;
                            m_refreshComplete = false;
                            continue;
                        }
                        if (eventType == ObjectTypeIds.RefreshEndEventType)
                        {
                            m_refreshComplete = m_options.RefreshConditions && m_refreshStarted;
                            continue;
                        }
                        if (eventType == ObjectTypeIds.EventQueueOverflowEventType)
                        {
                            m_dropped++;
                        }
                        if (m_options.Mode == MachineVisionObservationMode.Conditions && conditionId.IsNull)
                        {
                            continue;
                        }
                        if (m_events.Count == m_options.MaximumNotifications)
                        {
                            m_dropped++;
                            continue;
                        }
                        if (m_limitReached.Task.IsFaulted)
                        {
                            continue;
                        }
                        EventRecord? decoded;
                        try
                        {
                            using var encoder = new JsonEncoder(m_messageContext);
                            encoder.WriteVariantArray("Fields", fields);
                            m_payloadBytes += Encoding.UTF8.GetByteCount(encoder.CloseAndReturnText());
                            if (m_payloadBytes > 1024 * 1024)
                            {
                                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                                    "The observation payload is too large; reduce the window or notification count.");
                            }
                            decoded = m_registry.Decode(fields.ToArray() ?? []);
                        }
                        catch (Exception error)
                        {
                            m_limitReached.TrySetException(error);
                            continue;
                        }
                        m_events.Add(new MachineVisionObservedEvent(eventType, eventId, conditionId, decoded, fields));
                        if (m_events.Count == m_options.MaximumNotifications)
                        {
                            m_limitReached.TrySetResult(true);
                        }
                    }
                }
                return default;
            }

            public ValueTask OnDataChangeNotificationAsync(ISubscription subscription,
                uint sequenceNumber, DateTime publishTime, ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask, IReadOnlyList<string> stringTable)
            {
                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(ISubscription subscription,
                uint sequenceNumber, DateTime publishTime, PublishState publishStateMask)
            {
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(ISubscription subscription,
                SubscriptionState state, PublishState publishStateMask, CancellationToken ct = default)
            {
                if ((publishStateMask & PublishState.Completed) != 0)
                {
                    return default;
                }
                if (state == SubscriptionState.Error ||
                    (publishStateMask & (PublishState.Stopped | PublishState.Timeout | PublishState.Transferred)) != 0)
                {
                    m_limitReached.TrySetException(new ServiceResultException(StatusCodes.BadNoCommunication,
                        "The observation subscription stopped or failed."));
                }
                return default;
            }

            private Variant GetField(ArrayOf<Variant> fields, string name)
            {
                for (int i = 0; i < m_paths.Length && i < fields.Count; i++)
                {
                    QualifiedName[] path = m_paths[i];
                    if ((name.Length == 0 && path.Length == 0) ||
                        (path.Length == 1 && path[0].NamespaceIndex == 0 && path[0].Name == name))
                    {
                        return fields[i];
                    }
                }
                return default;
            }

            private readonly Lock m_lock = new();
            private readonly EventRecordDecoderRegistry m_registry;
            private readonly MachineVisionObservationOptions m_options;
            private readonly IServiceMessageContext m_messageContext;
            private readonly QualifiedName[][] m_paths;
            private readonly List<MachineVisionObservedEvent> m_events = [];

            private readonly TaskCompletionSource<bool> m_limitReached =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private bool m_refreshStarted;
            private bool m_refreshComplete;
            private long m_dropped;
            private long m_payloadBytes;
        }
    }
}
