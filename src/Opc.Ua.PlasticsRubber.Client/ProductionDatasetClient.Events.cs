// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.PlasticsRubber.Client
{
    public sealed partial class ProductionDatasetClient
    {
        /// <summary>
        /// Observes generated list-request records using a caller-owned bounded streaming subscription.
        /// </summary>
        public async IAsyncEnumerable<RequestProductionDatasetListEventTypeRecord> ObserveListRequestsAsync(
            IStreamingSubscription streaming,
            MonitoringOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ProductionDatasetListsTypeClient lists = await GetListsProxyAsync(cancellationToken).ConfigureAwait(false);
            EventRecordDecoderRegistry registry = CreateEventRegistry();
            EventFilter filter = RequestProductionDatasetListEventTypeRecord.EventFilters.Build(
                Session.NamespaceUris, registry);
            await foreach (RequestProductionDatasetListEventTypeRecord record in ObserveRequestsAsync
                <RequestProductionDatasetListEventTypeRecord>(
                    lists.ObjectId, ObjectTypes.RequestProductionDatasetListEventType, registry, filter,
                    streaming, options, cancellationToken).ConfigureAwait(false))
            {
                yield return record;
            }
        }

        /// <summary>
        /// Observes generated download requests, including concrete application subtypes of the abstract event.
        /// </summary>
        public async IAsyncEnumerable<RequestProductionDatasetReadEventTypeRecord> ObserveReadRequestsAsync(
            IStreamingSubscription streaming,
            MonitoringOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            TemporaryFileTransferTypeClient? transfer = await Proxy.GetProductionDatasetTransferAsync(
                Session.MessageContext.Telemetry, cancellationToken).ConfigureAwait(false);
            if (transfer == null)
            {
                throw Unsupported("The dataset transfer object is absent.");
            }
            EventRecordDecoderRegistry registry = CreateEventRegistry();
            EventFilter filter = RequestProductionDatasetReadEventTypeRecord.EventFilters.Build(
                Session.NamespaceUris, registry);
            await foreach (RequestProductionDatasetReadEventTypeRecord record in ObserveRequestsAsync
                <RequestProductionDatasetReadEventTypeRecord>(
                    transfer.ObjectId, ObjectTypes.RequestProductionDatasetReadEventType, registry, filter,
                    streaming, options, cancellationToken).ConfigureAwait(false))
            {
                yield return record;
            }
        }

        /// <summary>
        /// Observes generated upload requests without losing destination bits or repeated component codes.
        /// </summary>
        public async IAsyncEnumerable<RequestProductionDatasetWriteEventTypeRecord> ObserveWriteRequestsAsync(
            IStreamingSubscription streaming,
            MonitoringOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            TemporaryFileTransferTypeClient? transfer = await Proxy.GetProductionDatasetTransferAsync(
                Session.MessageContext.Telemetry, cancellationToken).ConfigureAwait(false);
            if (transfer == null)
            {
                throw Unsupported("The dataset transfer object is absent.");
            }
            EventRecordDecoderRegistry registry = CreateEventRegistry();
            EventFilter filter = RequestProductionDatasetWriteEventTypeRecord.EventFilters.Build(
                Session.NamespaceUris, registry);
            await foreach (RequestProductionDatasetWriteEventTypeRecord record in ObserveRequestsAsync
                <RequestProductionDatasetWriteEventTypeRecord>(
                    transfer.ObjectId, ObjectTypes.RequestProductionDatasetWriteEventType, registry, filter,
                    streaming, options, cancellationToken).ConfigureAwait(false))
            {
                yield return record;
            }
        }

        /// <summary>
        /// Uses generated decoders after validating the exact event type or its standard subtype relationship.
        /// </summary>
        /// <typeparam name="T">The generated request-event record.</typeparam>
        private IAsyncEnumerable<T> ObserveRequestsAsync<T>(
            NodeId notifier,
            uint eventType,
            EventRecordDecoderRegistry registry,
            EventFilter filter,
            IStreamingSubscription streaming,
            MonitoringOptions? options,
            CancellationToken ct)
            where T : BaseEventTypeRecord
        {
            if (streaming == null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }
            NodeId declared = NodeId.Create(eventType, Namespaces.PlasticsRubber, Session.NamespaceUris);
            return PlasticsRubberEventStream.ObserveAsync<T>(
                Session, notifier, declared, registry, filter, streaming, options, ct);
        }

        /// <summary>
        /// Registers generated event decoders in a local scope, leaving process-wide decoder state unchanged.
        /// </summary>
        private EventRecordDecoderRegistry CreateEventRegistry()
        {
            return EventRecordDecoderRegistry.Default.CreateChildScope().RegisterPlasticsRubberDecoders(
                Session.NamespaceUris);
        }
    }
}
