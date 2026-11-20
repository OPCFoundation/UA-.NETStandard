// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions.Streaming;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.PlasticsRubber.Client
{
    public sealed partial class PlasticsRubberClient
    {
        /// <summary>
        /// Observes completed diagnosis steps as generated records, including verified concrete event subtypes.
        /// </summary>
        public IAsyncEnumerable<DiagnosisStepEndEventTypeRecord> ObserveDiagnosticsStepsAsync(
            NodeId notifier,
            IStreamingSubscription? streaming = null,
            MonitoringOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            EventRecordDecoderRegistry registry = EventRegistry();
            return PlasticsRubberEventStream.ObserveAsync<DiagnosisStepEndEventTypeRecord>(
                Session, RequireInstance(notifier), ResolveType(ObjectTypeIds.DiagnosisStepEndEventType), registry,
                DiagnosisStepEndEventTypeRecord.EventFilters.Build(Session.NamespaceUris, registry),
                Streaming(streaming), options, cancellationToken);
        }

        /// <summary>
        /// Observes the required end-of-diagnostics events without consuming the shared session.
        /// </summary>
        public IAsyncEnumerable<DiagnosisEndEventTypeRecord> ObserveDiagnosticsCompletionsAsync(
            NodeId notifier,
            IStreamingSubscription? streaming = null,
            MonitoringOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            EventRecordDecoderRegistry registry = EventRegistry();
            return PlasticsRubberEventStream.ObserveAsync<DiagnosisEndEventTypeRecord>(
                Session, RequireInstance(notifier), ResolveType(ObjectTypeIds.DiagnosisEndEventType), registry,
                DiagnosisEndEventTypeRecord.EventFilters.Build(Session.NamespaceUris, registry),
                Streaming(streaming), options, cancellationToken);
        }

        /// <summary>
        /// Observes common logbook records from the actual device event notifier.
        /// </summary>
        public IAsyncEnumerable<LogbookEventTypeRecord> ObserveLogbookAsync(
            NodeId notifier,
            IStreamingSubscription? streaming = null,
            MonitoringOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            EventRecordDecoderRegistry registry = EventRegistry();
            return PlasticsRubberEventStream.ObserveAsync<LogbookEventTypeRecord>(
                Session, RequireInstance(notifier), ResolveType(ObjectTypeIds.LogbookEventType), registry,
                LogbookEventTypeRecord.EventFilters.Build(Session.NamespaceUris, registry),
                Streaming(streaming), options, cancellationToken);
        }

        /// <summary>
        /// Observes common help-bearing alarm records using generated field decoding.
        /// </summary>
        public IAsyncEnumerable<HelpOffNormalAlarmTypeRecord> ObserveAlarmsAsync(
            NodeId notifier,
            IStreamingSubscription? streaming = null,
            MonitoringOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            EventRecordDecoderRegistry registry = EventRegistry();
            return PlasticsRubberEventStream.ObserveAsync<HelpOffNormalAlarmTypeRecord>(
                Session, RequireInstance(notifier), ResolveType(ObjectTypeIds.HelpOffNormalAlarmType), registry,
                HelpOffNormalAlarmTypeRecord.EventFilters.Build(Session.NamespaceUris, registry),
                Streaming(streaming), options, cancellationToken);
        }

        /// <summary>
        /// Selects the caller's streaming subscription without allocating a hidden session.
        /// </summary>
        private IStreamingSubscription Streaming(IStreamingSubscription? streaming)
        {
            return streaming ?? (Session as ManagedSession)?.DefaultStreaming ??
                throw new InvalidOperationException("Supply a streaming subscription or use a managed session.");
        }

        /// <summary>
        /// Registers common generated records in a local decoder scope.
        /// </summary>
        private EventRecordDecoderRegistry EventRegistry()
        {
            return EventRecordDecoderRegistry.Default.CreateChildScope().RegisterPlasticsRubberDecoders(
                Session.NamespaceUris);
        }

        /// <summary>
        /// Resolves a standard event type through the connected namespace table.
        /// </summary>
        private NodeId ResolveType(ExpandedNodeId type)
        {
            return ExpandedNodeId.ToNodeId(type, Session.NamespaceUris);
        }
    }
}
