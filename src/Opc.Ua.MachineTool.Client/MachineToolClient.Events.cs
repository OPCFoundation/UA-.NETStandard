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

namespace Opc.Ua.MachineTool.Client
{
    public sealed partial class MachineToolClient
    {
        /// <summary>
        /// Registers a generated companion event decoder without replacing subscription, refresh or lifetime handling.
        /// Fields must be qualified using this session's namespace table. The decoder must be pure and nonblocking.
        /// At most eight additional types, 256 fields per type and eight segments per field are accepted.
        /// Configuration is refused while any facade observation or bounded read is active.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="decode"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public MachineToolClient WithEventDecoder(
            ExpandedNodeId eventTypeId, ArrayOf<ArrayOf<QualifiedName>> fields,
            Func<ArrayOf<Variant>, BaseEventTypeRecord?> decode)
        {
            if (decode == null)
            {
                throw new ArgumentNullException(nameof(decode));
            }
            var type = ExpandedNodeId.ToNodeId(eventTypeId, Session.NamespaceUris);
            RequireNode(type);
            if (type.NamespaceIndex == 0 || fields.Count is < 1 or > 256)
            {
                throw new ArgumentOutOfRangeException(nameof(fields),
                    "A companion decoder needs a nonstandard type and one through 256 selected fields.");
            }
            var paths = new QualifiedName[fields.Count][];
            for (int field = 0; field < fields.Count; field++)
            {
                if (fields[field].Count > 8)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(fields), "Event field paths may have at most 8 segments.");
                }
                paths[field] = fields[field].ToArray() ?? [];
                foreach (QualifiedName segment in paths[field])
                {
                    if (segment.IsNull || segment.NamespaceIndex >= Session.NamespaceUris.Count)
                    {
                        throw new ArgumentException(
                            "An event field has an unknown namespace or null name.", nameof(fields));
                    }
                }
            }
            lock (m_observationGate)
            {
                EnsureActive();
                if (m_activeObservations != 0 ||
                    m_eventDecoders.Count == 8 ||
                    m_eventDecoders.Exists(registration => registration.TypeId == type))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "Event decoders must be unique, bounded and configured before beginning observations.");
                }
                m_eventDecoders.Add(new EventDecoderRegistration(type, paths, decode));
            }
            return this;
        }

        /// <summary>
        /// Observes all events propagated to the actual machine root using a private bounded managed subscription.
        /// It never enables, acknowledges or changes a condition or commands equipment.
        /// </summary>
        public ValueTask<MachineToolEventObservation> ObserveEventsAsync(
            NodeId machineId, MachineToolObservationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return ObserveMachineEventsAsync(machineId, options ?? new(), false, cancellationToken);
        }

        /// <summary>
        /// Refreshes retained conditions using the shared subscription engine and finishes at RefreshEnd.
        /// A timeout before RefreshEnd is an error; a count limit or overflow is explicitly incomplete.
        /// No condition is implicitly enabled, acknowledged, confirmed or cleared.
        /// </summary>
        public ValueTask<MachineToolEventObservation> ObserveConditionsAsync(
            NodeId machineId, MachineToolObservationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return ObserveMachineEventsAsync(machineId, options ?? new(), true, cancellationToken);
        }

        /// <summary>
        /// Admits a workflow into the facade's existing cancellation and disposal lifetime.
        /// </summary>
        private async ValueTask<MachineToolEventObservation> ObserveMachineEventsAsync(
            NodeId machineId, MachineToolObservationOptions settings, bool refresh,
            CancellationToken cancellationToken)
        {
            EnsureActive();
            RequireNode(machineId);
            settings.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            using CancellationTokenSource operation = BeginObservation(cancellationToken);
            try
            {
                return await ObserveMachineEventsCoreAsync(machineId, settings, refresh, operation.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                EndObservation();
            }
        }

        /// <summary>
        /// Creates and always disposes exactly one shared-engine subscription without duplicating its wire protocol.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        private async ValueTask<MachineToolEventObservation> ObserveMachineEventsCoreAsync(
            NodeId machineId, MachineToolObservationOptions settings, bool refresh, CancellationToken cancellationToken)
        {
            if (!Session.TryGetSubscriptionManager(out ISubscriptionManager? manager))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "Event observation requires an options-based subscription session.");
            }
            using CancellationTokenSource timeout = m_timeProvider.CreateCancellationTokenSource(settings.Window);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            ISubscription? subscription = null;
            Exception? failure = null;
            MachineToolEventObservation? result = null;
            bool ready = false;
            try
            {
                MachineToolTypeClient machine = GetMachineProxy(machineId);
                _ = await machine.GetNotificationAsync(Telemetry, linked.Token).ConfigureAwait(false) ??
                    throw MissingMandatory(BrowseNames.Notification);
                var collector = new MachineEventCollector(this, machineId, settings, refresh, linked.Token);
                subscription = manager.Add(collector, new OptionsMonitor<SubscriptionOptions>(new SubscriptionOptions
                {
                    PublishingEnabled = true,
                    PublishingInterval = TimeSpan.FromMilliseconds(100),
                    KeepAliveCount = 10,
                    LifetimeCount = 300,
                    DisableUnboundedItemMode = true
                }));
                if (!subscription.MonitoredItems.TryAdd("MachineEvents",
                    new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions
                    {
                        StartNodeId = machineId,
                        AttributeId = Attributes.EventNotifier,
                        Filter = collector.Filter,
                        QueueSize = 102,
                        DiscardOldest = false
                    }), out IMonitoredItem? item) ||
                    item == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                        "The event monitored item could not be registered.");
                }
                while (!item.Created)
                {
                    collector.ThrowIfFailed();
                    if (ServiceResult.IsBad(item.Error))
                    {
                        throw new ServiceResultException(item.Error);
                    }
                    await m_timeProvider.Delay(TimeSpan.FromMilliseconds(20), linked.Token).ConfigureAwait(false);
                }
                if (ServiceResult.IsBad(item.Error))
                {
                    throw new ServiceResultException(item.Error);
                }
                ready = true;
                if (refresh)
                {
                    await subscription.ConditionRefreshAsync(linked.Token).ConfigureAwait(false);
                }
                try
                {
                    await collector.Completion.WaitAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    !refresh && timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    // Only a live observation's normal window expiry is successful without a completion marker.
                }
                cancellationToken.ThrowIfCancellationRequested();
                result = collector.Snapshot();
            }
            catch (OperationCanceledException error) when (
                timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                failure = new TimeoutException(ready
                    ? "The condition refresh did not complete within its bounded window."
                    : "The event subscription was not ready before its bounded window expired.", error);
            }
            catch (Exception error)
            {
                failure = error;
            }
            if (subscription != null)
            {
                try
                {
                    await subscription.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanup)
                {
                    failure = failure == null ? cleanup :
                        new AggregateException("Event observation and subscription cleanup failed.", failure, cleanup);
                }
            }
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return result ?? throw new InvalidOperationException("The event observation produced no result.");
        }

        /// <summary>
        /// Decodes generated event records using session-qualified field tables and bounded ancestor traversal.
        /// </summary>
        private sealed class MachineEventCollector : ISubscriptionNotificationHandler
        {
            /// <summary>
            /// Builds an isolated decoder registry without mutating process-wide event decoding.
            /// </summary>
            public MachineEventCollector(
                MachineToolClient client, NodeId notifier, MachineToolObservationOptions options,
                bool refresh, CancellationToken cancellationToken)
            {
                m_client = client;
                m_notifier = notifier;
                m_options = options;
                m_refresh = refresh;
                m_cancellationToken = cancellationToken;
                Register(Ua.ObjectTypeIds.BaseEventType, BaseEventTypeRecord.Decoder.StandardFields,
                    BaseEventTypeRecord.Decoder.Decode);
                Register(Ua.ObjectTypeIds.ConditionType, ConditionTypeRecord.Decoder.StandardFields,
                    ConditionTypeRecord.Decoder.Decode);
                Register(Ua.ObjectTypeIds.AlarmConditionType, AlarmConditionTypeRecord.Decoder.StandardFields,
                    AlarmConditionTypeRecord.Decoder.Decode);
                Register(ExpandedNodeId.ToNodeId(ObjectTypeIds.AlertType, client.Session.NamespaceUris),
                    AlertTypeRecord.Decoder.GetStandardFields(client.Session.NamespaceUris),
                    AlertTypeRecord.Decoder.Decode);
                Register(ExpandedNodeId.ToNodeId(ObjectTypeIds.NotificationEventType, client.Session.NamespaceUris),
                    NotificationEventTypeRecord.Decoder.GetStandardFields(client.Session.NamespaceUris),
                    NotificationEventTypeRecord.Decoder.Decode);
                Register(ExpandedNodeId.ToNodeId(ObjectTypeIds.InterruptionConditionType, client.Session.NamespaceUris),
                    InterruptionConditionTypeRecord.Decoder.GetStandardFields(client.Session.NamespaceUris),
                    InterruptionConditionTypeRecord.Decoder.Decode);
                foreach (EventDecoderRegistration registration in client.m_eventDecoders)
                {
                    Register(registration.TypeId, registration.Fields, fields =>
                    {
                        var values = new Variant[fields.Count];
                        for (int index = 0; index < fields.Count; index++)
                        {
                            values[index] = fields[index];
                        }
                        return registration.Decode(values.ToArrayOf());
                    });
                }
                if (m_registry.StandardFields.Length > 512)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "The combined event decoder selection exceeds 512 fields.");
                }
                Filter = BaseEventTypeRecord.EventFilters.Build(m_registry);
                for (int index = 0; index < m_registry.StandardFields.Length; index++)
                {
                    QualifiedName[] path = m_registry.StandardFields[index];
                    if (path.Length == 1 && path[0] == new QualifiedName(Ua.BrowseNames.EventType))
                    {
                        m_eventTypeIndex = index;
                    }
                }
            }

            /// <summary>
            /// Gets the source-generated field filter, including the Part 9 condition identity.
            /// </summary>
            public EventFilter Filter { get; }

            /// <summary>
            /// Gets the count, refresh-completion or failure signal.
            /// </summary>
            public Task Completion => m_completion.Task;

            /// <summary>
            /// Returns only bounded accepted records and explicit refresh completeness.
            /// </summary>
            public MachineToolEventObservation Snapshot()
            {
                lock (m_gate)
                {
                    ThrowIfFailed();
                    return new MachineToolEventObservation(
                        m_values.ToArrayOf(), m_limitReached, m_dropped,
                        m_refreshEnded && !m_limitReached && m_dropped == 0);
                }
            }

            /// <summary>
            /// Preserves stream failures instead of returning empty Good observations.
            /// </summary>
            public void ThrowIfFailed()
            {
                lock (m_gate)
                {
                    if (m_failure != null)
                    {
                        ExceptionDispatchInfo.Capture(m_failure).Throw();
                    }
                }
            }

            /// <inheritdoc/>
            public async ValueTask OnEventDataNotificationAsync(
                ISubscription subscription, uint sequenceNumber, DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification, PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                try
                {
                    for (int index = 0; index < notification.Length; index++)
                    {
                        ArrayOf<Variant> fields = notification.Span[index].Fields;
                        if (fields.Count != m_registry.StandardFields.Length ||
                            !fields[m_eventTypeIndex].TryGetValue(out NodeId type) ||
                            type.IsNull)
                        {
                            throw new ServiceResultException(StatusCodes.BadDecodingError,
                                "The event has no valid type or does not match the selected field layout.");
                        }
                        lock (m_gate)
                        {
                            if (m_failure != null)
                            {
                                return;
                            }
                            if (++m_examined > 4096)
                            {
                                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                                    "The event observation exceeded its total notification-processing bound.");
                            }
                            using var encoder = new JsonEncoder(m_client.Session.MessageContext);
                            encoder.WriteVariantArray("Fields", fields);
                            m_bytes = checked(m_bytes + Encoding.UTF8.GetByteCount(encoder.CloseAndReturnText()));
                            if (m_bytes > 1024 * 1024)
                            {
                                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                                    "The event observation exceeded one mebibyte of decoded payload.");
                            }
                            if (type == Ua.ObjectTypeIds.EventQueueOverflowEventType)
                            {
                                m_dropped++;
                                continue;
                            }
                            if (m_refresh && type == Ua.ObjectTypeIds.RefreshStartEventType)
                            {
                                m_refreshStarted = true;
                                continue;
                            }
                            if (m_refresh && type == Ua.ObjectTypeIds.RefreshEndEventType)
                            {
                                if (!m_refreshStarted)
                                {
                                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                                        "RefreshEnd arrived without RefreshStart.");
                                }
                                m_refreshEnded = true;
                                m_completion.TrySetResult(true);
                                continue;
                            }
                        }
                        NodeId decoder = await ResolveDecoderAsync(type).ConfigureAwait(false);
                        BaseEventTypeRecord record = m_registry.DecodeAs(decoder, fields.ToArray()!) as
                            BaseEventTypeRecord ??
                            throw new ServiceResultException(StatusCodes.BadDecodingError,
                                "The generated event decoder returned no event.");
                        if (m_refresh && record is not ConditionTypeRecord)
                        {
                            continue;
                        }
                        lock (m_gate)
                        {
                            if (m_values.Count == m_options.MaximumNotifications)
                            {
                                m_dropped++;
                                m_limitReached = true;
                                m_completion.TrySetResult(true);
                                continue;
                            }
                            var raw = new MachineToolEventField[fields.Count];
                            for (int field = 0; field < fields.Count; field++)
                            {
                                raw[field] = new MachineToolEventField(
                                    m_registry.StandardFields[field].ToArrayOf(), fields[field]);
                            }
                            m_values.Add(new MachineToolObservedEvent(
                                m_notifier, record, raw.ToArrayOf(), publishTime, sequenceNumber));
                            if (!m_refresh && m_values.Count == m_options.MaximumNotifications)
                            {
                                m_limitReached = true;
                                m_completion.TrySetResult(true);
                            }
                        }
                    }
                }
                catch (Exception error)
                {
                    Fail(error);
                }
            }

            /// <inheritdoc/>
            public ValueTask OnDataChangeNotificationAsync(
                ISubscription subscription, uint sequenceNumber, DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification, PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                return default;
            }

            /// <inheritdoc/>
            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription, uint sequenceNumber, DateTime publishTime,
                PublishState publishStateMask)
            {
                return default;
            }

            /// <inheritdoc/>
            public ValueTask OnSubscriptionStateChangedAsync(
                ISubscription subscription, SubscriptionState state,
                PublishState publishStateMask, CancellationToken ct = default)
            {
                if ((publishStateMask & PublishState.Completed) == 0 &&
                    (state == SubscriptionState.Error ||
                        (publishStateMask &
                            (PublishState.Stopped | PublishState.Timeout | PublishState.Transferred)) != 0))
                {
                    Fail(new ServiceResultException(StatusCodes.BadNoCommunication,
                        "The event observation subscription stopped or failed."));
                }
                return default;
            }

            /// <summary>
            /// Installs a generated decoder without creating a duplicate decoding protocol.
            /// </summary>
            private void Register(
                NodeId type, QualifiedName[][] paths, Func<IReadOnlyList<Variant>, EventRecord?> decoder)
            {
                m_types.Add(type);
                m_registry.Register(type, paths, decoder);
            }

            /// <summary>
            /// Resolves actual vendor descendants without assuming a server's namespace ordering.
            /// </summary>
            /// <exception cref="ServiceResultException"></exception>
            private async ValueTask<NodeId> ResolveDecoderAsync(NodeId type)
            {
                lock (m_gate)
                {
                    if (m_typeCache.TryGetValue(type, out NodeId found))
                    {
                        return found;
                    }
                }
                NodeId current = type;
                var visited = new HashSet<NodeId>();
                while (!current.IsNull && visited.Add(current) && visited.Count <= 64)
                {
                    if (m_types.Contains(current))
                    {
                        lock (m_gate)
                        {
                            m_typeCache[type] = current;
                        }
                        return current;
                    }
                    current = await m_client.Session.NodeCache.FindSuperTypeAsync(current, m_cancellationToken)
                        .ConfigureAwait(false);
                }
                throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                    "The event type has no recognized ancestor within the bounded type hierarchy.");
            }

            /// <summary>
            /// Wakes the workflow with its actual subscription or decoder error.
            /// </summary>
            private void Fail(Exception error)
            {
                lock (m_gate)
                {
                    m_failure ??= error;
                    m_completion.TrySetException(m_failure);
                }
            }

            /// <summary>The borrowed facade and session.</summary>
            private readonly MachineToolClient m_client;

            /// <summary>The actual monitored machine root.</summary>
            private readonly NodeId m_notifier;

            /// <summary>The count and duration bounds.</summary>
            private readonly MachineToolObservationOptions m_options;

            /// <summary>Whether this workflow requests a complete retained-condition refresh.</summary>
            private readonly bool m_refresh;

            /// <summary>The admitted workflow cancellation token.</summary>
            private readonly CancellationToken m_cancellationToken;

            /// <summary>The isolated generated decoder registry.</summary>
            private readonly EventRecordDecoderRegistry m_registry = new();

            /// <summary>The canonical types with registered decoders.</summary>
            private readonly HashSet<NodeId> m_types = [];

            /// <summary>Bounded decoded ancestors for the at-most-one-hundred received records.</summary>
            private readonly Dictionary<NodeId, NodeId> m_typeCache = [];

            /// <summary>Protects the short record-buffer and completion updates.</summary>
            private readonly Lock m_gate = new();

            /// <summary>The bounded accepted records.</summary>
            private readonly List<MachineToolObservedEvent> m_values = [];

            /// <summary>The asynchronous count, refresh-end or failure signal.</summary>
            private readonly TaskCompletionSource<bool> m_completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>The selected EventType field position.</summary>
            private readonly int m_eventTypeIndex;

            /// <summary>The first stream failure.</summary>
            private Exception? m_failure;

            /// <summary>The accumulated native JSON payload size.</summary>
            private int m_bytes;

            /// <summary>The total examined notifications, including refresh markers and ignored events.</summary>
            private int m_examined;

            /// <summary>The observed local or server loss count.</summary>
            private long m_dropped;

            /// <summary>Whether a local count bound ended observation.</summary>
            private bool m_limitReached;

            /// <summary>Whether the standard refresh-start marker was observed.</summary>
            private bool m_refreshStarted;

            /// <summary>Whether the corresponding refresh-end marker was observed.</summary>
            private bool m_refreshEnded;
        }

        /// <summary>
        /// Retains one immutable session-qualified generated decoder registration.
        /// </summary>
        /// <param name="TypeId">The actual companion type in this session.</param>
        /// <param name="Fields">An owned copy of the generated positional field table.</param>
        /// <param name="Decode">The pure generated record adapter.</param>
        private sealed record EventDecoderRegistration(
            NodeId TypeId, QualifiedName[][] Fields, Func<ArrayOf<Variant>, BaseEventTypeRecord?> Decode);

        /// <summary>Bounded immutable registrations configured before observation admission.</summary>
        private readonly List<EventDecoderRegistration> m_eventDecoders = [];
    }
}
