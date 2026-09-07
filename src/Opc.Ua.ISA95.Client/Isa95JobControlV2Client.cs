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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
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
