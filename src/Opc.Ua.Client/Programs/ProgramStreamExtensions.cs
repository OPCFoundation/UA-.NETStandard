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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Programs;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Client.Programs
{
    /// <summary>
    /// Typed streams of captured Program events, not CurrentState.Id subscriptions followed by reads.
    /// Streams are not durable and do not guarantee recovery of events lost during disconnection.
    /// Reported server queue overflow and observable local queue loss fail with BadDataLost.
    /// </summary>
    public static class ProgramStreamExtensions
    {
        /// <summary>
        /// Observes transition events for this invocation through its own or an owning notifier.
        /// A readiness callback runs only after server monitored-item creation and can issue a control
        /// without a subscribe-after-Start race. Supplied registries must not change during enumeration.
        /// </summary>
        public static IAsyncEnumerable<ProgramTransitionEventTypeRecord> ObserveProgramTransitionsAsync(
            this ProgramStateMachineTypeClient proxy,
            IStreamingSubscription streaming,
            NodeId notifierId,
            EventRecordDecoderRegistry? registry = null,
            MonitoringOptions? options = null,
            Func<CancellationToken, ValueTask>? onReady = null,
            CancellationToken ct = default)
        {
            ValidateInputs(proxy, streaming, notifierId);
            EventRecordDecoderRegistry effective = CreateRegistry(proxy, registry);
            EventFilter filter = ProgramTransitionEventTypeRecord.EventFilters.Build(effective);
            AddSourceFilter(filter, proxy.ObjectId);
            return ObserveAsync<ProgramTransitionEventTypeRecord>(
                proxy.ObjectId, streaming, notifierId, filter, effective,
                ObjectTypeIds.ProgramTransitionEventType, registry is null, options, onReady, ct);
        }

        /// <summary>
        /// Observes captured Program transition audit events. Server auditing policy and access rights
        /// determine whether events are emitted. This is not a stream of rejected method-call audits.
        /// </summary>
        public static IAsyncEnumerable<AuditProgramTransitionEventTypeRecord> ObserveProgramAuditsAsync(
            this ProgramStateMachineTypeClient proxy,
            IStreamingSubscription streaming,
            NodeId notifierId,
            EventRecordDecoderRegistry? registry = null,
            MonitoringOptions? options = null,
            Func<CancellationToken, ValueTask>? onReady = null,
            CancellationToken ct = default)
        {
            ValidateInputs(proxy, streaming, notifierId);
            EventRecordDecoderRegistry effective = CreateRegistry(proxy, registry);
            EventFilter filter = AuditProgramTransitionEventTypeRecord.EventFilters.Build(effective);
            AddSourceFilter(filter, proxy.ObjectId);
            return ObserveAsync<AuditProgramTransitionEventTypeRecord>(
                proxy.ObjectId, streaming, notifierId, filter, effective,
                ObjectTypeIds.AuditProgramTransitionEventType, registry is null, options, onReady, ct);
        }

        /// <summary>
        /// Waits for a future captured transition to the target state. The callback can invoke a
        /// generated method after server-side monitoring is ready. No state reread is performed,
        /// and neither timeout nor cancellation implies that an accepted Program control was undone.
        /// The caller supplies a deadline through the cancellation token. This helper matches standard
        /// state NodeIds; applications using other state identifiers can consume the typed event stream.
        /// </summary>
        public static async ValueTask<ProgramTransitionEventTypeRecord> WaitForProgramTransitionAsync(
            this ProgramStateMachineTypeClient proxy,
            IStreamingSubscription streaming,
            NodeId notifierId,
            ProgramState targetState,
            Func<CancellationToken, ValueTask>? onReady = null,
            MonitoringOptions? options = null,
            CancellationToken ct = default)
        {
            NodeId expected = ProgramStateMachineModel.GetStateId(targetState);
            await foreach (ProgramTransitionEventTypeRecord record in proxy.ObserveProgramTransitionsAsync(
                streaming, notifierId, options: options, onReady: onReady ?? ReadyAsync, ct: ct)
                .ConfigureAwait(false))
            {
                if (!record.ToStateId.TryGetValue(out NodeId state))
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "This Program uses non-NodeId state identifiers; consume its event stream directly.");
                }
                if (state == expected)
                {
                    return record;
                }
            }
            throw new ServiceResultException(StatusCodes.BadNoData,
                "The Program event stream ended before the requested transition.");
        }

        /// <summary>
        /// Requires readiness even when a wait has no control callback.
        /// </summary>
        private static ValueTask ReadyAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return default;
        }

        /// <summary>
        /// Creates a per-stream decoder scope with generated standard and SDK decoders.
        /// </summary>
        private static EventRecordDecoderRegistry CreateRegistry(
            ProgramStateMachineTypeClient proxy,
            EventRecordDecoderRegistry? registry)
        {
            EventRecordDecoderRegistry effective = registry?.CreateChildScope() ?? new EventRecordDecoderRegistry();
            effective.TryRegister(ObjectTypeIds.ProgramTransitionEventType,
                ProgramTransitionEventTypeRecord.Decoder.StandardFields,
                ProgramTransitionEventTypeRecord.Decoder.Decode);
            effective.TryRegister(ObjectTypeIds.AuditProgramTransitionEventType,
                AuditProgramTransitionEventTypeRecord.Decoder.StandardFields,
                AuditProgramTransitionEventTypeRecord.Decoder.Decode);
            if (registry is null &&
                proxy.Session.MessageContext.NamespaceUris.GetIndex(Ua.Programs.Namespaces.OpcUaPrograms) >= 0)
            {
                effective.RegisterOpcUaProgramsDecoders(proxy.Session.MessageContext.NamespaceUris);
            }
            return effective;
        }

        /// <summary>
        /// Restricts Program events to the source invocation but permits server overflow notifications.
        /// </summary>
        private static void AddSourceFilter(EventFilter filter, NodeId sourceId)
        {
            ContentFilterElement type = filter.WhereClause.Elements[0];
            ContentFilterElement source = filter.WhereClause.Push(FilterOperator.Equals,
                Variant.FromStructure(new SimpleAttributeOperand
                {
                    TypeDefinitionId = ObjectTypeIds.BaseEventType,
                    BrowsePath = [QualifiedName.From(BrowseNames.SourceNode)],
                    AttributeId = Attributes.Value
                }),
                Variant.From(sourceId));
            ContentFilterElement matching = filter.WhereClause.Push(FilterOperator.And,
                Variant.FromStructure(type), Variant.FromStructure(source));
            ContentFilterElement overflow = filter.WhereClause.Push(FilterOperator.OfType,
                Variant.From(ObjectTypeIds.EventQueueOverflowEventType));
            filter.WhereClause.Push(FilterOperator.Or,
                Variant.FromStructure(matching), Variant.FromStructure(overflow));
        }

        /// <summary>
        /// Decodes and validates event payloads without replacing errors with default records.
        /// </summary>
        /// <typeparam name="TRecord">The generated Program event record type.</typeparam>
        private static async IAsyncEnumerable<TRecord> ObserveAsync<TRecord>(
            NodeId sourceId,
            IStreamingSubscription streaming,
            NodeId notifierId,
            EventFilter filter,
            EventRecordDecoderRegistry registry,
            NodeId baseType,
            bool allowUnregisteredFallback,
            MonitoringOptions? options,
            Func<CancellationToken, ValueTask>? onReady,
            [EnumeratorCancellation] CancellationToken ct)
            where TRecord : BaseEventTypeRecord
        {
            ct.ThrowIfCancellationRequested();
            IAsyncEnumerable<EventNotification> source;
            if (onReady is not null)
            {
                if (streaming is not IStreamingSubscriptionReadiness readiness)
                {
                    throw new NotSupportedException(
                        "This subscription cannot report server monitored-item readiness.");
                }
                source = readiness.SubscribeEventsAsync(notifierId, filter, options,
                    async (item, token) =>
                    {
                        await item.WaitForMonitoredItemCreatedAsync(token).ConfigureAwait(false);
                        await onReady(token).ConfigureAwait(false);
                    }, ct);
            }
            else
            {
                source = streaming.SubscribeEventsAsync(notifierId, filter, options, ct);
            }
            long drops = GetDroppedCount(streaming);
            await foreach (EventNotification notification in source.WithCancellation(ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                CheckDroppedCount(streaming, drops);
                if (notification.Fields.Count != filter.SelectClauses.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError,
                        "Program event field count differs.");
                }
                ArrayOf<Variant> fields = notification.Fields;
                Variant eventTypeValue = GetField(filter, fields, BrowseNames.EventType);
                if (!eventTypeValue.TryGetValue(out NodeId eventType) || eventType.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError,
                        "The Program event type is missing.");
                }
                if (eventType == ObjectTypeIds.EventQueueOverflowEventType)
                {
                    throw new ServiceResultException(StatusCodes.BadDataLost,
                        "The server Program event queue overflowed.");
                }
                if (!GetField(filter, fields, BrowseNames.SourceNode).TryGetValue(out NodeId sourceNode) ||
                    sourceNode.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError,
                        "The Program event source is missing.");
                }
                if (sourceNode != sourceId)
                {
                    continue;
                }
                ValidateFields(filter, fields, baseType);
                Variant[] values = fields.ToArray()!;
                EventRecord? decoded = registry.Decode(values);
                if (decoded is null && allowUnregisteredFallback)
                {
                    // The server's OfType filter includes vendor subtypes without a registered decoder.
                    decoded = registry.DecodeAs(baseType, values);
                }
                if (decoded is not TRecord record)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError, "Program event decoding failed.");
                }
                yield return record;
            }
            ct.ThrowIfCancellationRequested();
            CheckDroppedCount(streaming, drops);
        }

        /// <summary>
        /// Validates required fields before invoking the permissive generated positional readers.
        /// </summary>
        private static void ValidateFields(EventFilter filter, ArrayOf<Variant> fields, NodeId baseType)
        {
            bool valid = GetField(filter, fields, BrowseNames.EventId).TryGetValue(out ByteString eventId) &&
                !eventId.IsNull &&
                GetField(filter, fields, BrowseNames.Time).TryGetValue(out DateTimeUtc _) &&
                GetField(filter, fields, BrowseNames.ReceiveTime).TryGetValue(out DateTimeUtc _) &&
                GetField(filter, fields, BrowseNames.Message).TryGetValue(out LocalizedText _) &&
                GetField(filter, fields, BrowseNames.Severity).TryGetValue(out ushort _);
            if (baseType == ObjectTypeIds.ProgramTransitionEventType)
            {
                valid = valid &&
                    GetField(filter, fields, BrowseNames.FromState).TryGetValue(out LocalizedText _) &&
                    GetField(filter, fields, BrowseNames.ToState).TryGetValue(out LocalizedText _) &&
                    GetField(filter, fields, BrowseNames.Transition).TryGetValue(out LocalizedText _) &&
                    IsIdentifier(GetField(filter, fields, BrowseNames.FromState, BrowseNames.Id)) &&
                    IsIdentifier(GetField(filter, fields, BrowseNames.ToState, BrowseNames.Id)) &&
                    IsIdentifier(GetField(filter, fields, BrowseNames.Transition, BrowseNames.Id));
            }
            else
            {
                valid = valid &&
                    GetField(filter, fields, BrowseNames.TransitionNumber).TryGetValue(out uint _) &&
                    GetField(filter, fields, BrowseNames.ActionTimeStamp).TryGetValue(out DateTimeUtc _) &&
                    GetField(filter, fields, BrowseNames.Status).TryGetValue(out bool _) &&
                    IsIdentifier(GetField(filter, fields, BrowseNames.OldStateId)) &&
                    IsIdentifier(GetField(filter, fields, BrowseNames.NewStateId));
            }
            if (!valid)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError,
                    "A required Program event field is invalid.");
            }
        }

        /// <summary>
        /// The base event model permits any BaseDataType identifier, not only NodeIds.
        /// A bad field status denotes a failed field projection, not a usable identifier.
        /// </summary>
        private static bool IsIdentifier(Variant value)
        {
            return !value.IsNull &&
                (!value.TryGetValue(out StatusCode status) || !StatusCode.IsBad(status));
        }

        /// <summary>
        /// Reads a generated select clause by its namespace-qualified browse path.
        /// </summary>
        private static Variant GetField(
            EventFilter filter,
            ArrayOf<Variant> values,
            string name,
            string? child = null)
        {
            for (int i = 0; i < filter.SelectClauses.Count; i++)
            {
                ArrayOf<QualifiedName> path = filter.SelectClauses[i].BrowsePath;
                if (path.Count == (child is null ? 1 : 2) && path[0] == QualifiedName.From(name) &&
                    (child is null || path[1] == QualifiedName.From(child)))
                {
                    return values[i];
                }
            }
            throw new ServiceResultException(StatusCodes.BadDecodingError,
                $"Required event field {name} is not selected.");
        }

        /// <summary>
        /// Reads the existing concrete streaming implementation's local overflow accounting.
        /// </summary>
        private static long GetDroppedCount(IStreamingSubscription streaming)
        {
            return streaming is StreamingSubscription concrete ? concrete.DroppedNotificationCount : 0;
        }

        /// <summary>
        /// Fails when the caller-owned subscription reports new local notification loss.
        /// </summary>
        private static void CheckDroppedCount(IStreamingSubscription streaming, long initial)
        {
            if (GetDroppedCount(streaming) != initial)
            {
                throw new ServiceResultException(StatusCodes.BadDataLost,
                    "The shared streaming subscription reported local notification loss.");
            }
        }

        /// <summary>
        /// Validates the invocation, notifier and caller-owned subscription.
        /// </summary>
        private static void ValidateInputs(
            ProgramStateMachineTypeClient proxy,
            IStreamingSubscription streaming,
            NodeId notifierId)
        {
            if (proxy is null)
            {
                throw new ArgumentNullException(nameof(proxy));
            }
            if (streaming is null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }
            if (notifierId.IsNull || proxy.ObjectId.IsNull)
            {
                throw new ArgumentException("Both the notifier and invocation must have non-null NodeIds.");
            }
        }
    }
}
