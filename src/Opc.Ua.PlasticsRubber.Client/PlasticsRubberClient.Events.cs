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
