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
