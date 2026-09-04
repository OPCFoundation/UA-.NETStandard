/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use, copy,
 * modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS
 * BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
 * ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
 * CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Historian
{
    internal sealed class HistorianEventCapture : IAsyncDisposable
    {
        public HistorianEventCapture(
            IServerInternal server,
            IHistorianProvider provider,
            HistorianNodeCapabilities capabilities,
            HistorianCaptureOptions? options = null,
            TimeProvider? timeProvider = null)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_provider = provider as IHistorianEventProvider ??
                throw new ArgumentException(
                    "The historian provider does not support event history.",
                    nameof(provider));
            m_capabilities = capabilities ??
                throw new ArgumentNullException(nameof(capabilities));
            m_options = options ?? new HistorianCaptureOptions();
            ValidateOptions(m_options);
            m_timeProvider = timeProvider ??
                (server as ITimeProviderProvider)?.TimeProvider ??
                TimeProvider.System;
            m_logger = server.Telemetry.CreateLogger<HistorianEventCapture>();
            m_fields = BuildCaptureFields(
                capabilities.EventFields,
                capabilities.MandatoryEventFields);
            var channelOptions = new BoundedChannelOptions(
                m_options.MaxQueuedSamples)
            {
                FullMode = MapFullMode(m_options.FullMode),
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            };
            m_channel = Channel.CreateBounded<CaptureEvent>(
                channelOptions,
                OnEventDropped);
            m_consumer = Task.Run(
                () => ConsumeAsync(m_shutdownCts.Token));
        }

        public long DroppedEventCount =>
            Interlocked.Read(ref m_droppedEvents);

        public long RejectedEventCount =>
            Interlocked.Read(ref m_rejectedEvents);

        public void Enqueue(
            ISystemContext context,
            NodeState notifier,
            IFilterTarget eventInstance)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (notifier == null)
            {
                throw new ArgumentNullException(nameof(notifier));
            }
            if (eventInstance == null)
            {
                throw new ArgumentNullException(nameof(eventInstance));
            }
            if (m_disposed)
            {
                return;
            }
            if (m_consumer.IsFaulted)
            {
                Interlocked.Increment(ref m_droppedEvents);
                m_logger.HistorianEventCaptureUnavailable(
                    m_consumer.Exception?.InnerException ??
                    m_consumer.Exception!,
                    notifier.NodeId);
                return;
            }

            HistorianEventRecord record;
            try
            {
                record = CreateRecord(
                    context,
                    notifier,
                    eventInstance);
            }
            catch (Exception exception) when (
                exception is ServiceResultException or
                ArgumentException or
                InvalidOperationException)
            {
                Interlocked.Increment(ref m_droppedEvents);
                m_logger.HistorianEventCaptureSnapshotRejected(
                    exception,
                    notifier.NodeId);
                return;
            }
            if (!m_channel.Writer.TryWrite(
                new CaptureEvent(notifier, record)))
            {
                Interlocked.Increment(ref m_droppedEvents);
                m_logger.HistorianEventCaptureQueueClosed(
                    notifier.NodeId);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            m_channel.Writer.TryComplete();
            try
            {
                await m_consumer.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    m_timeProvider).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                m_logger.HistorianEventCaptureConsumerDidNotDrain();
                m_shutdownCts.Cancel();
                throw;
            }
            finally
            {
                m_shutdownCts.Dispose();
            }
        }

        private HistorianEventRecord CreateRecord(
            ISystemContext context,
            NodeState notifier,
            IFilterTarget eventInstance)
        {
            var filterContext = new FilterContext(
                m_server.NamespaceUris,
                m_server.TypeTree,
                (context as ServerSystemContext)?.OperationContext,
                m_server.Telemetry);
            var fields = new Dictionary<string, Variant>(
                StringComparer.Ordinal);
            var qualifiedFields =
                new Dictionary<HistorianEventFieldKey, Variant>();
            for (int i = 0; i < m_fields.Count; i++)
            {
                SimpleAttributeOperand operand = m_fields[i];
                ServiceResult validation = NumericRange.Validate(
                    operand.IndexRange ?? string.Empty,
                    out NumericRange indexRange);
                if (ServiceResult.IsBad(validation))
                {
                    throw new ServiceResultException(validation);
                }
                Variant value = eventInstance.GetAttributeValue(
                    filterContext,
                    operand.TypeDefinitionId,
                    operand.BrowsePath,
                    operand.AttributeId,
                    indexRange).Copy();
                fields[HistorianEventFieldKey.BuildPath(
                    operand.BrowsePath)] = value;
                qualifiedFields[
                    HistorianEventFieldKey.FromOperand(operand)] = value;
            }

            ByteString eventId = TryGetCanonicalField(
                qualifiedFields,
                BrowseNames.EventId,
                out Variant eventIdValue) &&
                eventIdValue.TryGetValue(out ByteString id) &&
                !id.IsEmpty
                ? id
                : ByteString.From(Guid.NewGuid().ToByteArray());
            NodeId eventType = ResolveEventType(
                filterContext,
                eventInstance,
                qualifiedFields);
            DateTimeUtc eventTime = TryGetCanonicalField(
                qualifiedFields,
                BrowseNames.Time,
                out Variant timeValue) &&
                timeValue.TryGetValue(out DateTimeUtc timestamp) &&
                timestamp != DateTimeUtc.MinValue
                ? timestamp
                : m_timeProvider.GetUtcNow().UtcDateTime;
            NodeId sourceNode = TryGetCanonicalField(
                qualifiedFields,
                BrowseNames.SourceNode,
                out Variant sourceNodeValue) &&
                sourceNodeValue.TryGetValue(out NodeId source) &&
                !source.IsNull
                ? source
                : notifier.NodeId;

            SetCanonicalField(
                fields,
                qualifiedFields,
                BrowseNames.EventId,
                new Variant(eventId));
            SetCanonicalField(
                fields,
                qualifiedFields,
                BrowseNames.EventType,
                new Variant(eventType));
            SetCanonicalField(
                fields,
                qualifiedFields,
                BrowseNames.Time,
                new Variant(eventTime));
            SetCanonicalField(
                fields,
                qualifiedFields,
                BrowseNames.SourceNode,
                new Variant(sourceNode));

            return new HistorianEventRecord(
                eventId,
                eventType,
                eventTime,
                fields.ToArrayOf())
            {
                QualifiedFields = qualifiedFields.ToArrayOf()
            };
        }

        private async Task ConsumeAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (await m_channel.Reader.WaitToReadAsync(
                    cancellationToken).ConfigureAwait(false))
                {
                    Dictionary<NodeId, EventBatch> batch =
                        await CollectBatchAsync(
                            cancellationToken).ConfigureAwait(false);
                    foreach (EventBatch events in batch.Values)
                    {
                        await FlushAsync(
                            events,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                m_logger.HistorianEventCaptureConsumerTerminated(exception);
                throw;
            }
        }

        private async ValueTask<Dictionary<NodeId, EventBatch>>
            CollectBatchAsync(CancellationToken cancellationToken)
        {
            var batch = new Dictionary<NodeId, EventBatch>();
            int total = 0;
            while (total < m_options.BatchTarget &&
                m_channel.Reader.TryRead(out CaptureEvent captured))
            {
                AppendToBatch(batch, captured);
                total++;
            }
            if (total >= m_options.BatchTarget)
            {
                return batch;
            }

            using CancellationTokenSource timeout = m_timeProvider
                .CreateCancellationTokenSource(m_options.BatchWindow);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);
            try
            {
                while (total < m_options.BatchTarget &&
                    await m_channel.Reader.WaitToReadAsync(
                        window.Token).ConfigureAwait(false))
                {
                    while (total < m_options.BatchTarget &&
                        m_channel.Reader.TryRead(
                            out CaptureEvent captured))
                    {
                        AppendToBatch(batch, captured);
                        total++;
                    }
                }
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
            }
            return batch;
        }

        private async ValueTask FlushAsync(
            EventBatch batch,
            CancellationToken cancellationToken)
        {
            using var operationContext = new OperationContext(
                new RequestHeader(),
                null,
                RequestType.HistoryUpdate,
                RequestLifetime.None);
            var systemContext = new ServerSystemContext(
                m_server,
                operationContext);
            var historianContext = new HistorianOperationContext(
                systemContext,
                operationContext,
                batch.Notifier,
                HistoryUpdateType.Update);
            HistorianUpdateOutcome<HistorianEventRecord> outcome =
                await m_provider.UpdateEventsAsync(
                    historianContext,
                    batch.Notifier.NodeId,
                    batch.Events,
                    cancellationToken).ConfigureAwait(false);
            if (outcome.OperationResults.Count != batch.Events.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "The event historian returned a mismatched operation count.");
            }
            int rejected = 0;
            StatusCode firstRejection = StatusCodes.Good;
            for (int i = 0; i < outcome.OperationResults.Count; i++)
            {
                if (StatusCode.IsBad(outcome.OperationResults[i]))
                {
                    if (rejected == 0)
                    {
                        firstRejection = outcome.OperationResults[i];
                    }
                    rejected++;
                }
            }
            if (rejected > 0)
            {
                Interlocked.Add(ref m_rejectedEvents, rejected);
                m_logger.HistorianEventCaptureRejected(
                    batch.Notifier.NodeId,
                    rejected,
                    firstRejection);
            }
        }

        private NodeId ResolveEventType(
            IFilterContext context,
            IFilterTarget eventInstance,
            IReadOnlyDictionary<HistorianEventFieldKey, Variant> fields)
        {
            NodeId eventType = NodeId.Null;
            if (TryGetCanonicalField(
                    fields,
                    BrowseNames.EventType,
                    out Variant eventTypeValue))
            {
                _ = eventTypeValue.TryGetValue(out eventType);
            }
            if (eventType.IsNull)
            {
                for (int i = 0; i < m_capabilities.EventTypes.Count; i++)
                {
                    NodeId candidate = m_capabilities.EventTypes[i];
                    if (!candidate.IsNull &&
                        eventInstance.IsTypeOf(context, candidate))
                    {
                        eventType = candidate;
                        break;
                    }
                }
            }
            if (eventType.IsNull &&
                eventInstance.IsTypeOf(
                    context,
                    ObjectTypeIds.BaseEventType))
            {
                eventType = ObjectTypeIds.BaseEventType;
            }
            if (eventType.IsNull ||
                !IsSupportedEventType(eventType))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeDefinitionInvalid,
                    "The reported event type is not configured for history.");
            }
            return eventType;
        }

        private bool IsSupportedEventType(NodeId eventType)
        {
            if (m_capabilities.EventTypes.IsEmpty)
            {
                return true;
            }
            for (int i = 0; i < m_capabilities.EventTypes.Count; i++)
            {
                NodeId supported = m_capabilities.EventTypes[i];
                if (eventType == supported ||
                    m_server.TypeTree.IsTypeOf(eventType, supported))
                {
                    return true;
                }
            }
            return false;
        }

        private void OnEventDropped(CaptureEvent captured)
        {
            Interlocked.Increment(ref m_droppedEvents);
            m_logger.HistorianEventCaptureDropped(
                captured.Notifier.NodeId,
                m_options.FullMode);
        }

        private static void AppendToBatch(
            Dictionary<NodeId, EventBatch> batch,
            CaptureEvent captured)
        {
            if (!batch.TryGetValue(
                captured.Notifier.NodeId,
                out EventBatch? events))
            {
                events = new EventBatch(captured.Notifier);
                batch[captured.Notifier.NodeId] = events;
            }
            events.Events.Add(captured.Record);
        }

        private static void ValidateOptions(
            HistorianCaptureOptions options)
        {
            if (options.MaxQueuedSamples <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "MaxQueuedSamples must be positive.");
            }
            if (options.BatchTarget <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "BatchTarget must be positive.");
            }
            if (options.BatchWindow < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "BatchWindow must not be negative.");
            }
            if (options.FullMode is not CaptureFullMode.DropOldest and
                not CaptureFullMode.DropNewest)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "FullMode is not supported.");
            }
        }

        private static BoundedChannelFullMode MapFullMode(
            CaptureFullMode fullMode)
        {
            return fullMode switch
            {
                CaptureFullMode.DropOldest =>
                    BoundedChannelFullMode.DropOldest,
                CaptureFullMode.DropNewest =>
                    BoundedChannelFullMode.DropNewest,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(fullMode))
            };
        }

        private static ArrayOf<SimpleAttributeOperand> BuildCaptureFields(
            ArrayOf<SimpleAttributeOperand> configured,
            ArrayOf<SimpleAttributeOperand> mandatory)
        {
            var fields = new List<SimpleAttributeOperand>();
            for (int i = 0; i < s_baseEventFields.Count; i++)
            {
                fields.Add(s_baseEventFields[i]);
            }
            AddCaptureFields(fields, configured, nameof(configured));
            AddCaptureFields(fields, mandatory, nameof(mandatory));
            return fields.ToArrayOf();
        }

        private static void AddCaptureFields(
            List<SimpleAttributeOperand> fields,
            ArrayOf<SimpleAttributeOperand> configured,
            string parameterName)
        {
            for (int i = 0; i < configured.Count; i++)
            {
                SimpleAttributeOperand operand = configured[i] ??
                    throw new ArgumentException(
                        "Event capture fields cannot contain null operands.",
                        parameterName);
                if (!ContainsOperand(fields, operand))
                {
                    fields.Add(operand);
                }
            }
        }

        private static bool ContainsOperand(
            List<SimpleAttributeOperand> fields,
            SimpleAttributeOperand candidate)
        {
            var candidateKey =
                HistorianEventFieldKey.FromOperand(candidate);
            for (int i = 0; i < fields.Count; i++)
            {
                var existingKey =
                    HistorianEventFieldKey.FromOperand(fields[i]);
                if (existingKey.TypeDefinitionId ==
                        candidateKey.TypeDefinitionId &&
                    existingKey.AttributeId == candidateKey.AttributeId &&
                    string.Equals(
                        existingKey.IndexRange,
                        candidateKey.IndexRange,
                        StringComparison.Ordinal) &&
                    PathsEqual(
                        existingKey.BrowsePath,
                        candidateKey.BrowsePath))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool PathsEqual(
            ArrayOf<QualifiedName> left,
            ArrayOf<QualifiedName> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }
            for (int i = 0; i < left.Count; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TryGetCanonicalField(
            IReadOnlyDictionary<HistorianEventFieldKey, Variant> fields,
            string browseName,
            out Variant value)
        {
            return fields.TryGetValue(
                CreateBaseEventFieldKey(browseName),
                out value);
        }

        private static void SetCanonicalField(
            Dictionary<string, Variant> fields,
            Dictionary<HistorianEventFieldKey, Variant> qualifiedFields,
            string browseName,
            Variant value)
        {
            fields[browseName] = value;
            qualifiedFields[CreateBaseEventFieldKey(browseName)] = value;
        }

        private static HistorianEventFieldKey CreateBaseEventFieldKey(
            string browseName)
        {
            return new HistorianEventFieldKey(
                ObjectTypeIds.BaseEventType,
                [new QualifiedName(browseName)],
                Attributes.Value,
                null);
        }

        private static SimpleAttributeOperand CreateBaseEventField(
            string browseName)
        {
            return new SimpleAttributeOperand
            {
                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                BrowsePath = [new QualifiedName(browseName)],
                AttributeId = Attributes.Value
            };
        }

        private static readonly ArrayOf<SimpleAttributeOperand>
            s_baseEventFields =
        [
            CreateBaseEventField(BrowseNames.EventId),
            CreateBaseEventField(BrowseNames.EventType),
            CreateBaseEventField(BrowseNames.SourceNode),
            CreateBaseEventField(BrowseNames.SourceName),
            CreateBaseEventField(BrowseNames.Time),
            CreateBaseEventField(BrowseNames.ReceiveTime),
            CreateBaseEventField(BrowseNames.LocalTime),
            CreateBaseEventField(BrowseNames.Message),
            CreateBaseEventField(BrowseNames.Severity)
        ];

        private readonly IServerInternal m_server;
        private readonly IHistorianEventProvider m_provider;
        private readonly HistorianNodeCapabilities m_capabilities;
        private readonly HistorianCaptureOptions m_options;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;
        private readonly ArrayOf<SimpleAttributeOperand> m_fields;
        private readonly Channel<CaptureEvent> m_channel;
        private readonly CancellationTokenSource m_shutdownCts = new();
        private readonly Task m_consumer;
        private long m_droppedEvents;
        private long m_rejectedEvents;
        private bool m_disposed;

        private readonly record struct CaptureEvent(
            NodeState Notifier,
            HistorianEventRecord Record);

        private sealed class EventBatch
        {
            public EventBatch(NodeState notifier)
            {
                Notifier = notifier;
            }

            public NodeState Notifier { get; }

            public List<HistorianEventRecord> Events { get; } = [];
        }
    }

    internal static partial class HistorianEventCaptureLog
    {
        [LoggerMessage(
            EventId = ServerEventIds.HistorianEventCapture + 0,
            Level = LogLevel.Error,
            Message = "The historian event capture consumer terminated unexpectedly.")]
        public static partial void HistorianEventCaptureConsumerTerminated(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(
            EventId = ServerEventIds.HistorianEventCapture + 1,
            Level = LogLevel.Warning,
            Message = "The historian event capture queue dropped an event for {NodeId} using {FullMode}.")]
        public static partial void HistorianEventCaptureDropped(
            this ILogger logger,
            NodeId nodeId,
            CaptureFullMode fullMode);

        [LoggerMessage(
            EventId = ServerEventIds.HistorianEventCapture + 2,
            Level = LogLevel.Error,
            Message = "The historian event capture consumer did not drain within five seconds.")]
        public static partial void HistorianEventCaptureConsumerDidNotDrain(
            this ILogger logger);

        [LoggerMessage(
            EventId = ServerEventIds.HistorianEventCapture + 3,
            Level = LogLevel.Error,
            Message = "The historian event capture consumer is unavailable; dropping the event for {NodeId}.")]
        public static partial void HistorianEventCaptureUnavailable(
            this ILogger logger,
            Exception exception,
            NodeId nodeId);

        [LoggerMessage(
            EventId = ServerEventIds.HistorianEventCapture + 4,
            Level = LogLevel.Warning,
            Message = "The historian event capture queue is closed; dropping the event for {NodeId}.")]
        public static partial void HistorianEventCaptureQueueClosed(
            this ILogger logger,
            NodeId nodeId);

        [LoggerMessage(
            EventId = ServerEventIds.HistorianEventCapture + 5,
            Level = LogLevel.Warning,
            Message = "The event historian rejected {Count} reported event(s) for {NodeId}; first status {StatusCode}.")]
        public static partial void HistorianEventCaptureRejected(
            this ILogger logger,
            NodeId nodeId,
            int count,
            StatusCode statusCode);

        [LoggerMessage(
            EventId = ServerEventIds.HistorianEventCapture + 6,
            Level = LogLevel.Warning,
            Message = "The reported event for {NodeId} is not valid for historical capture and was dropped.")]
        public static partial void HistorianEventCaptureSnapshotRejected(
            this ILogger logger,
            Exception exception,
            NodeId nodeId);
    }
}
