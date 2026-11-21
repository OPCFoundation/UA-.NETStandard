// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Client.Subscriptions.Streaming;

namespace Opc.Ua.AutoID.Client
{
    public sealed partial class AutoIdClient
    {
        /// <summary>
        /// Observes the scan events of a reader (<c>AutoIdScanEventType</c> and its subtypes,
        /// OPC 30010 7.2 to 7.7) as an asynchronous stream.
        /// </summary>
        /// <remarks>
        /// The events carry the results of <c>ScanStart</c> and of scans the device triggers itself.
        /// The monitored item lives as long as the enumeration: disposing the enumerator removes it.
        /// The caller owns <paramref name="streaming"/>. Each record's <c>ScanResult</c> holds
        /// instances of the result type of the reader kind (for example <see cref="RfidScanResult"/>).
        /// </remarks>
        /// <param name="streaming">The streaming subscription to add the monitored item to.</param>
        /// <param name="reader">The reader.</param>
        /// <param name="cancellationToken">Ends the stream.</param>
        public IAsyncEnumerable<AutoIdScanEventTypeRecord> ObserveScanEventsAsync(
            IStreamingSubscription streaming,
            AutoIdReaderEntry reader,
            CancellationToken cancellationToken = default)
        {
            return ObserveAsync<AutoIdScanEventTypeRecord>(
                streaming,
                reader,
                ObjectTypeIds.AutoIdScanEventType,
                cancellationToken);
        }

        /// <summary>
        /// Observes the access events of a reader (<c>AutoIdAccessEventType</c> and
        /// <c>RfidAccessEventType</c>, OPC 30010 7.10 and 7.11).
        /// </summary>
        /// <param name="streaming">The streaming subscription to add the monitored item to.</param>
        /// <param name="reader">The reader.</param>
        /// <param name="cancellationToken">Ends the stream.</param>
        public IAsyncEnumerable<AutoIdAccessEventTypeRecord> ObserveAccessEventsAsync(
            IStreamingSubscription streaming,
            AutoIdReaderEntry reader,
            CancellationToken cancellationToken = default)
        {
            return ObserveAsync<AutoIdAccessEventTypeRecord>(
                streaming,
                reader,
                ObjectTypeIds.AutoIdAccessEventType,
                cancellationToken);
        }

        /// <summary>
        /// Observes the presence events of a reader (<c>AutoIdPresenceEventType</c>, OPC 30010 7.12).
        /// </summary>
        /// <param name="streaming">The streaming subscription to add the monitored item to.</param>
        /// <param name="reader">The reader.</param>
        /// <param name="cancellationToken">Ends the stream.</param>
        public IAsyncEnumerable<AutoIdPresenceEventTypeRecord> ObservePresenceEventsAsync(
            IStreamingSubscription streaming,
            AutoIdReaderEntry reader,
            CancellationToken cancellationToken = default)
        {
            return ObserveAsync<AutoIdPresenceEventTypeRecord>(
                streaming,
                reader,
                ObjectTypeIds.AutoIdPresenceEventType,
                cancellationToken);
        }

        /// <summary>
        /// Observes the logbook entry events of a reader (<c>AutoIdLogEntryEventType</c>,
        /// OPC 30010 7.9); the entry is the record's <c>Message</c>.
        /// </summary>
        /// <param name="streaming">The streaming subscription to add the monitored item to.</param>
        /// <param name="reader">The reader.</param>
        /// <param name="cancellationToken">Ends the stream.</param>
        public IAsyncEnumerable<AutoIdLogEntryEventTypeRecord> ObserveLogEntriesAsync(
            IStreamingSubscription streaming,
            AutoIdReaderEntry reader,
            CancellationToken cancellationToken = default)
        {
            return ObserveAsync<AutoIdLogEntryEventTypeRecord>(
                streaming,
                reader,
                ObjectTypeIds.AutoIdLogEntryEventType,
                cancellationToken);
        }

        private IAsyncEnumerable<TRecord> ObserveAsync<TRecord>(
            IStreamingSubscription streaming,
            AutoIdReaderEntry reader,
            ExpandedNodeId eventType,
            CancellationToken cancellationToken)
            where TRecord : EventRecord
        {
            if (streaming is null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }
            RequireReader(reader);
            EventRecordDecoderRegistry registry = Registry;
            NodeId eventTypeId = TypeId(eventType);
            return ObserveCoreAsync<TRecord>(
                streaming,
                reader.NodeId,
                EventFilterFactory.Create(eventTypeId, registry),
                registry,
                eventTypeId,
                cancellationToken);
        }

        /// <summary>
        /// Gets the decoder registry of the AutoID event types for this session's namespace table.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private EventRecordDecoderRegistry Registry
        {
            get
            {
                lock (m_registryLock)
                {
                    if (m_registry is null)
                    {
                        if (!IsSupported)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadNotSupported,
                                "The server does not publish the OPC 30010 namespace.");
                        }
                        // A child scope keeps the process-wide default registry untouched.
                        m_registry = EventRecordDecoderRegistry.Default.CreateChildScope()
                            .RegisterAutoIDDecoders(Session.NamespaceUris);
                    }
                    return m_registry;
                }
            }
        }

        private static async IAsyncEnumerable<TRecord> ObserveCoreAsync<TRecord>(
            IStreamingSubscription streaming,
            NodeId notifier,
            EventFilter filter,
            EventRecordDecoderRegistry registry,
            NodeId eventTypeId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
            where TRecord : EventRecord
        {
            await foreach (EventNotification notification in streaming
                .SubscribeEventsAsync(notifier, filter, new MonitoredItemOptions { QueueSize = 1000 }, cancellationToken)
                .ConfigureAwait(false))
            {
                // A vendor subtype of an AutoID event type has no decoder of its own: it is read
                // as the event type that was asked for.
                EventRecord? record = registry.Decode(notification.Fields.ToArray() ?? []) ??
                    registry.DecodeAs(eventTypeId, notification.Fields.ToArray() ?? []);
                if (record is TRecord typed)
                {
                    yield return typed;
                }
            }
        }

        private readonly Lock m_registryLock = new();
        private EventRecordDecoderRegistry? m_registry;
    }
}
