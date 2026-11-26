// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.ISA95.JobControl;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.Client
{
    /// <summary>
    /// Direct client for the three ISA-95 Job Control V2 endpoint objects.
    /// </summary>
    public sealed class Isa95JobControlV2Client
    {
        /// <summary>
        /// Initializes a Job Control V2 client.
        /// </summary>
        public Isa95JobControlV2Client(
            ISession session,
            NodeId jobOrderReceiverId,
            NodeId jobResponseProviderId,
            NodeId jobResponseReceiverId,
            ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            ValidateNodeId(jobOrderReceiverId, nameof(jobOrderReceiverId));
            ValidateNodeId(jobResponseProviderId, nameof(jobResponseProviderId));
            ValidateNodeId(jobResponseReceiverId, nameof(jobResponseReceiverId));

            Isa95EncodeableRegistration.Register(session);
            JobOrderReceiverId = jobOrderReceiverId;
            JobResponseProviderId = jobResponseProviderId;
            JobResponseReceiverId = jobResponseReceiverId;
            JobOrderReceiver = new V2.ISA95JobOrderReceiverObjectTypeClient(session, jobOrderReceiverId, telemetry);
            JobResponseProvider = new V2.ISA95JobResponseProviderObjectTypeClient(
                session,
                jobResponseProviderId,
                telemetry);
            JobResponseReceiver = new V2.ISA95JobResponseReceiverObjectTypeClient(
                session,
                jobResponseReceiverId,
                telemetry);
        }

        /// <summary>
        /// Gets the session used by this client.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the telemetry context used by the generated proxies.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets the Job Order Receiver object identifier.
        /// </summary>
        public NodeId JobOrderReceiverId { get; }

        /// <summary>
        /// Gets the Job Response Provider object identifier.
        /// </summary>
        public NodeId JobResponseProviderId { get; }

        /// <summary>
        /// Gets the Job Response Receiver object identifier.
        /// </summary>
        public NodeId JobResponseReceiverId { get; }

        /// <summary>
        /// Gets the generated Job Order Receiver proxy.
        /// </summary>
        public V2.ISA95JobOrderReceiverObjectTypeClient JobOrderReceiver { get; }

        /// <summary>
        /// Gets the generated Job Response Provider proxy.
        /// </summary>
        public V2.ISA95JobResponseProviderObjectTypeClient JobResponseProvider { get; }

        /// <summary>
        /// Gets the generated Job Response Receiver proxy.
        /// </summary>
        public V2.ISA95JobResponseReceiverObjectTypeClient JobResponseReceiver { get; }

        /// <summary>
        /// Invokes Abort on the Job Order Receiver.
        /// </summary>
        public ValueTask<ulong> AbortAsync(
            string jobOrderId,
            ArrayOf<LocalizedText> comment = default,
            CancellationToken ct = default)
        {
            return JobOrderReceiver.AbortAsync(jobOrderId, comment, ct);
        }

        /// <summary>
        /// Invokes Cancel on the Job Order Receiver.
        /// </summary>
        public ValueTask<ulong> CancelAsync(
            string jobOrderId,
            ArrayOf<LocalizedText> comment = default,
            CancellationToken ct = default)
        {
            return JobOrderReceiver.CancelAsync(jobOrderId, comment, ct);
        }

        /// <summary>
        /// Invokes Clear on the Job Order Receiver.
        /// </summary>
        public ValueTask<ulong> ClearAsync(
            string jobOrderId,
            ArrayOf<LocalizedText> comment = default,
            CancellationToken ct = default)
        {
            return JobOrderReceiver.ClearAsync(jobOrderId, comment, ct);
        }

        /// <summary>
        /// Invokes Pause on the Job Order Receiver.
        /// </summary>
        public ValueTask<ulong> PauseAsync(
            string jobOrderId,
            ArrayOf<LocalizedText> comment = default,
            CancellationToken ct = default)
        {
            return JobOrderReceiver.PauseAsync(jobOrderId, comment, ct);
        }

        /// <summary>
        /// Invokes Resume on the Job Order Receiver.
        /// </summary>
        public ValueTask<ulong> ResumeAsync(
            string jobOrderId,
            ArrayOf<LocalizedText> comment = default,
            CancellationToken ct = default)
        {
            return JobOrderReceiver.ResumeAsync(jobOrderId, comment, ct);
        }

        /// <summary>
        /// Invokes RevokeStart on the Job Order Receiver.
        /// </summary>
        public ValueTask<ulong> RevokeStartAsync(
            string jobOrderId,
            ArrayOf<LocalizedText> comment = default,
            CancellationToken ct = default)
        {
            return JobOrderReceiver.RevokeStartAsync(jobOrderId, comment, ct);
        }

        /// <summary>
        /// Invokes Start on the Job Order Receiver.
        /// </summary>
        public ValueTask<ulong> StartAsync(
            string jobOrderId,
            ArrayOf<LocalizedText> comment = default,
            CancellationToken ct = default)
        {
            return JobOrderReceiver.StartAsync(jobOrderId, comment, ct);
        }

        /// <summary>
        /// Invokes Stop on the Job Order Receiver.
        /// </summary>
        public ValueTask<ulong> StopAsync(
            string jobOrderId,
            ArrayOf<LocalizedText> comment = default,
            CancellationToken ct = default)
        {
            return JobOrderReceiver.StopAsync(jobOrderId, comment, ct);
        }

        /// <summary>
        /// Invokes Store on the Job Order Receiver.
        /// </summary>
        public ValueTask<ulong> StoreAsync(
            V2.ISA95JobOrderDataType jobOrder,
            ArrayOf<LocalizedText> comment = default,
            CancellationToken ct = default)
        {
            return JobOrderReceiver.StoreAsync(jobOrder, comment, ct);
        }

        /// <summary>
        /// Invokes StoreAndStart on the Job Order Receiver.
        /// </summary>
        public ValueTask<ulong> StoreAndStartAsync(
            V2.ISA95JobOrderDataType jobOrder,
            ArrayOf<LocalizedText> comment = default,
            CancellationToken ct = default)
        {
            return JobOrderReceiver.StoreAndStartAsync(jobOrder, comment, ct);
        }

        /// <summary>
        /// Invokes Update on the Job Order Receiver.
        /// </summary>
        public ValueTask<ulong> UpdateAsync(
            V2.ISA95JobOrderDataType jobOrder,
            ArrayOf<LocalizedText> comment = default,
            CancellationToken ct = default)
        {
            return JobOrderReceiver.UpdateAsync(jobOrder, comment, ct);
        }

        /// <summary>
        /// Invokes RequestJobResponseByJobOrderID on the Job Response Provider.
        /// </summary>
        public ValueTask<(V2.ISA95JobResponseDataType Response, ulong ReturnStatus)>
            RequestJobResponseByJobOrderIdAsync(
                string jobOrderId,
                CancellationToken ct = default)
        {
            return JobResponseProvider.RequestJobResponseByJobOrderIDAsync(jobOrderId, ct);
        }

        /// <summary>
        /// Invokes RequestJobResponseByJobOrderState on the Job Response Provider.
        /// </summary>
        public ValueTask<(ArrayOf<V2.ISA95JobResponseDataType> Responses, ulong ReturnStatus)>
            RequestJobResponseByJobOrderStateAsync(
                ArrayOf<V2.ISA95StateDataType> state,
                CancellationToken ct = default)
        {
            return JobResponseProvider.RequestJobResponseByJobOrderStateAsync(state, ct);
        }

        /// <summary>
        /// Invokes ReceiveJobResponse on the Job Response Receiver.
        /// </summary>
        public ValueTask<ulong> ReceiveJobResponseAsync(
            V2.ISA95JobResponseDataType response,
            CancellationToken ct = default)
        {
            return JobResponseReceiver.ReceiveJobResponseAsync(response, ct);
        }

        /// <summary>
        /// Invokes Store, StoreAndStart, or Update with a neutral order.
        /// </summary>
        public ValueTask<ulong> ReceiveNeutralJobOrderAsync(
            Isa95JobOperation operation,
            Isa95JobOrder order,
            ArrayOf<LocalizedText> comment = default,
            CancellationToken ct = default)
        {
            V2.ISA95JobOrderDataType projected = Isa95JobControlConversions.ToV2Order(order);
            ArrayOf<LocalizedText> effectiveComment = comment.IsNull ? order.Comment : comment;
            return operation switch
            {
                Isa95JobOperation.Store => StoreAsync(projected, effectiveComment, ct),
                Isa95JobOperation.StoreAndStart => StoreAndStartAsync(projected, effectiveComment, ct),
                Isa95JobOperation.Update => UpdateAsync(projected, effectiveComment, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }

        /// <summary>
        /// Sends a version-neutral response through the V2 interface.
        /// </summary>
        public ValueTask<ulong> ReceiveNeutralJobResponseAsync(
            Isa95JobResponse response,
            CancellationToken ct = default)
        {
            return ReceiveJobResponseAsync(
                Isa95JobControlConversions.ToV2Response(response, GetV2NamespaceIndex()), ct);
        }

        /// <summary>
        /// Requests a V2 response by identifier. The neutral response is absent on business failure.
        /// </summary>
        public async ValueTask<(Isa95JobResponse? Response, ulong ReturnStatus)> RequestNeutralJobResponseByIdAsync(
            string jobOrderId,
            CancellationToken ct = default)
        {
            (V2.ISA95JobResponseDataType response, ulong status) =
                await RequestJobResponseByJobOrderIdAsync(jobOrderId, ct).ConfigureAwait(false);
            return ((status & Isa95JobReturnStatusBits.Success) != 0 && response != null
                ? Isa95JobControlConversions.FromV2Response(response, GetV2NamespaceIndex())
                : null, status);
        }

        /// <summary>
        /// Requests V2 responses matching a canonical state and converts the results.
        /// </summary>
        public async ValueTask<(ArrayOf<Isa95JobResponse> Responses, ulong ReturnStatus)>
            RequestNeutralJobResponsesByStateAsync(
                Isa95JobCanonicalState state,
                CancellationToken ct = default)
        {
            ushort index = GetV2NamespaceIndex();
            (ArrayOf<V2.ISA95JobResponseDataType> responses, ulong status) =
                await RequestJobResponseByJobOrderStateAsync(
                    Isa95JobControlConversions.ToV2StateArray(state, index), ct).ConfigureAwait(false);
            if (responses.IsNull)
            {
                return (default, status);
            }
            var result = new Isa95JobResponse[responses.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = Isa95JobControlConversions.FromV2Response(responses[i], index);
            }
            return (result.ToArrayOf(), status);
        }

        private ushort GetV2NamespaceIndex()
        {
            int index = Session.NamespaceUris.GetIndex(V2.Namespaces.ISA95JobControlV2);
            return index is > 0 and <= ushort.MaxValue
                ? (ushort)index
                : throw new InvalidOperationException("The session does not contain the ISA95 Job Control V2 namespace.");
        }

        /// <summary>
        /// Streams typed V2 Job Order Status event records.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="streaming"/> is <c>null</c>.</exception>
        public IAsyncEnumerable<V2.ISA95JobOrderStatusEventTypeRecord>
            SubscribeJobOrderStatusEventsAsync(
            IStreamingSubscription streaming,
            NodeId notifierId,
            EventRecordDecoderRegistry? registry = null,
            MonitoringOptions? options = null,
            CancellationToken ct = default)
        {
            if (streaming == null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }
            ValidateNodeId(notifierId, nameof(notifierId));
            EventRecordDecoderRegistry effective = registry ??
                EventRecordDecoderRegistry.Default.CreateChildScope();
            V2.ISA95JobControlV2EventRecordDecoders
                .RegisterISA95JobControlV2Decoders(effective, Session.NamespaceUris);
            EventFilter filter =
                V2.ISA95JobOrderStatusEventTypeRecord.EventFilters.Build(
                    Session.NamespaceUris,
                    effective);
            return DecodeJobOrderStatusEventsAsync(
                streaming,
                notifierId,
                filter,
                effective,
                options,
                ct);
        }

        private async IAsyncEnumerable<V2.ISA95JobOrderStatusEventTypeRecord>
            DecodeJobOrderStatusEventsAsync(
                IStreamingSubscription streaming,
                NodeId notifierId,
                EventFilter filter,
                EventRecordDecoderRegistry registry,
                MonitoringOptions? options,
                [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken ct)
        {
            await foreach (EventNotification notification in streaming
                .SubscribeEventsAsync(notifierId, filter, options, ct)
                .ConfigureAwait(false))
            {
                IReadOnlyList<Variant> fields = notification.Fields.ToArray() ?? [];
                var record =
                    registry.Decode(fields) as V2.ISA95JobOrderStatusEventTypeRecord;
                if (record == null &&
                    TryGetEventType(registry.StandardFields, fields, out NodeId eventType))
                {
                    var statusEventType = ExpandedNodeId.ToNodeId(
                        V2.ObjectTypeIds.ISA95JobOrderStatusEventType,
                        Session.NamespaceUris);
                    if (!statusEventType.IsNull &&
                        await Session.NodeCache.IsTypeOfAsync(
                            eventType,
                            statusEventType,
                            ct).ConfigureAwait(false))
                    {
                        record = registry.DecodeAs(statusEventType, fields) as
                            V2.ISA95JobOrderStatusEventTypeRecord;
                    }
                }
                if (record != null)
                {
                    yield return record;
                }
            }
        }

        private static bool TryGetEventType(
            QualifiedName[][] standardFields,
            IReadOnlyList<Variant> fields,
            out NodeId eventType)
        {
            for (int ii = 0; ii < standardFields.Length && ii < fields.Count; ii++)
            {
                QualifiedName[] path = standardFields[ii];
                if (path.Length > 0 &&
                    string.Equals(
                        path[^1].Name,
                        Ua.BrowseNames.EventType,
                        StringComparison.Ordinal) &&
                    fields[ii].TryGetValue(out eventType) &&
                    !eventType.IsNull)
                {
                    return true;
                }
            }
            eventType = NodeId.Null;
            return false;
        }

        private static void ValidateNodeId(NodeId nodeId, string parameterName)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentException("An endpoint object NodeId is required.", parameterName);
            }
        }
    }
}
