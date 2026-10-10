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
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Bounded-channel value-capture pump that observes
    /// <see cref="NodeState.StateChanged"/> events on historized variables
    /// and forwards their fresh <see cref="DataValue"/>s into an
    /// <see cref="IHistorianProvider"/> in micro-batches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One instance is owned per <c>HistorianBuilder</c> binding (i.e.
    /// per per-node-manager or server-wide historian setup). All
    /// variables opted in to <c>Historize(autoCapture: true)</c> through
    /// the same builder share the same sink and the same consumer task.
    /// </para>
    /// <para>
    /// <strong>Threading.</strong> Writers
    /// (<see cref="Enqueue(NodeId, DataValue)"/>) are called synchronously
    /// from the thread that fires <c>StateChanged</c> on a variable;
    /// they must stay O(1). The channel's <c>TryWrite</c> is lock-free.
    /// The single consumer task drains the channel on a thread-pool
    /// thread and never reaches back into the producers' threads.
    /// </para>
    /// <para>
    /// <strong>Best-effort semantics.</strong> Auto-capture is
    /// deliberately not durable: if the queue is full and the
    /// <see cref="HistorianCaptureOptions.FullMode"/> is
    /// <see cref="CaptureFullMode.DropOldest"/> or
    /// <see cref="CaptureFullMode.DropNewest"/>, samples are dropped
    /// and counted in <see cref="DroppedSampleCount"/>. A failed provider
    /// call drops its samples, records a rate-limited warning, and leaves
    /// the consumer running for later batches. Callers needing durability
    /// should use the explicit HistoryUpdate Insert service instead.
    /// </para>
    /// </remarks>
    internal sealed class HistorianCaptureSink : IAsyncDisposable
    {
        /// <summary>
        /// Creates a new capture sink bound to the supplied provider.
        /// Starts the consumer task immediately; callers must
        /// <see cref="DisposeAsync"/> the sink to drain pending samples
        /// and stop the consumer.
        /// </summary>
        /// <param name="provider">The historian provider that receives the batches.</param>
        /// <param name="systemContext">
        /// The system context used to build the per-flush
        /// <see cref="HistorianOperationContext"/>. Stored once at
        /// construction; the same context flows through every flush.
        /// </param>
        /// <param name="options">
        /// Buffering / batching knobs. <c>null</c> uses
        /// <see cref="HistorianCaptureOptions"/> defaults.
        /// </param>
        /// <param name="timeProvider">
        /// Optional <see cref="TimeProvider"/> used for timeout
        /// scheduling. When <c>null</c>, the server-wide provider exposed
        /// via <see cref="ITimeProviderProvider"/> is used, falling back
        /// to <see cref="TimeProvider.System"/>.
        /// </param>
        public HistorianCaptureSink(
            IHistorianProvider provider,
            ServerSystemContext systemContext,
            HistorianCaptureOptions? options = null,
            TimeProvider? timeProvider = null)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            if (provider is not IHistorianBulkInsertProvider and
                not IHistorianDataProvider)
            {
                throw new ArgumentException(
                    "The capture provider must support bulk or per-node data inserts.",
                    nameof(provider));
            }
            m_systemContext = systemContext ?? throw new ArgumentNullException(nameof(systemContext));
            m_options = options ?? new HistorianCaptureOptions();
            if (m_options.MaxQueuedSamples <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "MaxQueuedSamples must be positive.");
            }
            if (m_options.BatchTarget <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "BatchTarget must be positive.");
            }
            if (m_options.BatchWindow < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "BatchWindow must not be negative.");
            }
            if (m_options.FullMode is not CaptureFullMode.DropOldest and
                not CaptureFullMode.DropNewest)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "FullMode is not supported.");
            }
            m_logger = systemContext.Server?.Telemetry?.CreateLogger<HistorianCaptureSink>();
            m_timeProvider = timeProvider
                ?? (systemContext.Server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;

            var channelOptions = new BoundedChannelOptions(m_options.MaxQueuedSamples)
            {
                FullMode = MapFullMode(m_options.FullMode),
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            };
            m_channel = Channel.CreateBounded<CaptureEvent>(
                channelOptions, OnSampleDropped);
            m_shutdownCts = new CancellationTokenSource();
            CancellationToken shutdownToken = m_shutdownCts.Token;
            m_consumer = Task.Run(() => ConsumeAsync(shutdownToken));
        }

        /// <summary>
        /// The number of samples lost to queue overflow, a failed provider call,
        /// or an unexpectedly unavailable consumer. Operation-level rejections
        /// are counted separately by <see cref="RejectedSampleCount"/>.
        /// </summary>
        public long DroppedSampleCount => Interlocked.Read(ref m_droppedSamples);

        /// <summary>
        /// The number of samples rejected by the provider with an operation-level
        /// bad status. Rejections do not fault the shared capture pipeline.
        /// </summary>
        public long RejectedSampleCount => Interlocked.Read(ref m_rejectedSamples);

        /// <summary>
        /// Enqueues a new sample for the supplied node without blocking the
        /// value-setting callback.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public void Enqueue(NodeId nodeId, DataValue value)
        {
            if (nodeId.IsNull || value.IsNull)
            {
                throw new ArgumentException(
                    "A capture sample requires a non-null NodeId and DataValue.");
            }
            if (Volatile.Read(ref m_disposed) != 0)
            {
                return;
            }
            if (m_consumer.IsFaulted)
            {
                Interlocked.Increment(ref m_droppedSamples);
                if (Interlocked.Exchange(ref m_unavailableReported, 1) == 0)
                {
                    m_logger?.HistorianCaptureSinkUnavailable(
                        m_consumer.Exception?.InnerException ??
                        m_consumer.Exception!,
                        nodeId);
                }
                return;
            }

            var ev = new CaptureEvent(nodeId, value);

            if (!m_channel.Writer.TryWrite(ev))
            {
                Interlocked.Increment(ref m_droppedSamples);
                if (Interlocked.Exchange(ref m_unavailableReported, 1) == 0)
                {
                    m_logger?.HistorianCaptureSinkQueueClosed(nodeId);
                }
            }
        }

        /// <summary>
        /// Flushes pending samples and shuts down the consumer task.
        /// Idempotent.
        /// </summary>
        /// <remarks>
        /// A timeout cancels capture and is surfaced to the caller. Token resources remain alive
        /// until an in-flight provider completes; its eventual failure is still observed.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            // Close the writer; the consumer drains remaining items and
            // exits its async-foreach loop normally.
            m_channel.Writer.TryComplete();
            try
            {
                // Bound the wait — if the consumer is stuck the host
                // shutdown should not block forever.
                await m_consumer
                    .WaitAsync(TimeSpan.FromSeconds(5), m_timeProvider)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                m_logger?.HistorianCaptureSinkConsumerDidNotDrainWithin5s();
                m_shutdownCts.Cancel();
                throw;
            }
            finally
            {
                if (m_consumer.IsCompleted)
                {
                    _ = m_consumer.Exception;
                    m_shutdownCts.Dispose();
                }
                else
                {
                    // A timed-out provider still owns the token. This one-shot cleanup
                    // only observes completion and disposes its source; it cannot capture this.
                    _ = m_consumer.ContinueWith(
                        static (completed, state) =>
                        {
                            _ = completed.Exception;
                            ((CancellationTokenSource)state!).Dispose();
                        },
                        m_shutdownCts,
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }
        }

        private async Task ConsumeAsync(CancellationToken ct)
        {
            try
            {
                while (await m_channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    // Drain up to BatchTarget items, waiting up to
                    // BatchWindow for further items to amortise the
                    // provider call.
                    Dictionary<NodeId, List<DataValue>> batch = await CollectBatchAsync(ct)
                        .ConfigureAwait(false);
                    if (batch.Count == 0)
                    {
                        continue;
                    }
                    try
                    {
                        await FlushAsync(batch, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception) when (!ct.IsCancellationRequested)
                    {
                        int dropped = 0;
                        foreach (List<DataValue> values in batch.Values)
                        {
                            dropped += values.Count;
                        }
                        ReportFailedFlush(exception, batch.Count, dropped);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // shutdown
            }
            catch (Exception ex)
            {
                m_channel.Writer.TryComplete(ex);
                while (m_channel.Reader.TryRead(out _))
                {
                    Interlocked.Increment(ref m_droppedSamples);
                }
                m_logger?.HistorianCaptureSinkConsumerTerminatedUnexpectedly(ex);
                throw;
            }
        }

        private async ValueTask<Dictionary<NodeId, List<DataValue>>> CollectBatchAsync(
            CancellationToken ct)
        {
            var batch = new Dictionary<NodeId, List<DataValue>>();
            int total = 0;
            // First drain the channel of any items already queued.
            while (total < m_options.BatchTarget && m_channel.Reader.TryRead(out CaptureEvent ev))
            {
                AppendToBatch(batch, ev);
                total++;
            }
            if (total >= m_options.BatchTarget)
            {
                return batch;
            }

            // Wait up to BatchWindow for additional items to pack the batch.
            using CancellationTokenSource windowTimeoutCts = m_timeProvider
                .CreateCancellationTokenSource(m_options.BatchWindow);
            using var windowCts = CancellationTokenSource.CreateLinkedTokenSource(
                ct, windowTimeoutCts.Token);
            try
            {
                while (total < m_options.BatchTarget &&
                    await m_channel.Reader.WaitToReadAsync(windowCts.Token).ConfigureAwait(false))
                {
                    while (total < m_options.BatchTarget && m_channel.Reader.TryRead(out CaptureEvent ev))
                    {
                        AppendToBatch(batch, ev);
                        total++;
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // batch window elapsed — flush what we have
            }
            return batch;
        }

        private static void AppendToBatch(Dictionary<NodeId, List<DataValue>> batch, CaptureEvent ev)
        {
            if (!batch.TryGetValue(ev.NodeId, out List<DataValue>? values))
            {
                values = [];
                batch[ev.NodeId] = values;
            }
            values.Add(ev.Value);
        }

        private async ValueTask FlushAsync(
            Dictionary<NodeId, List<DataValue>> batch,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using var opContext = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryUpdate, RequestLifetime.None);
            var historianContext = new HistorianOperationContext(
                m_systemContext, opContext, null, HistoryUpdateType.Insert);

            if (m_provider is IHistorianBulkInsertProvider bulk)
            {
                var entries = new HistorianDataBatch[batch.Count];
                int entryIndex = 0;
                foreach (KeyValuePair<NodeId, List<DataValue>> kv in batch)
                {
                    entries[entryIndex++] = new HistorianDataBatch(
                        kv.Key,
                        kv.Value);
                }
                ArrayOf<HistorianUpdateOutcome<DataValue>> outcomes =
                    await bulk.InsertBatchAsync(
                        historianContext,
                        entries,
                        ct)
                    .ConfigureAwait(false);
                if (outcomes.Count != entries.Length)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError,
                        "The historian bulk insert returned a mismatched node count.");
                }
                for (int i = 0; i < entries.Length; i++)
                {
                    HistorianUpdateOutcome<DataValue>? outcome =
                        outcomes[i] ??
                        throw new ServiceResultException(
                            StatusCodes.BadUnexpectedError,
                            "The historian bulk insert returned a null node result.");
                    ValidateInsertOutcome(
                        entries[i].NodeId,
                        outcome,
                        entries[i].Values.Count);
                }
                return;
            }

            var data = (IHistorianDataProvider)m_provider;
            foreach (KeyValuePair<NodeId, List<DataValue>> kv in batch)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    HistorianUpdateOutcome<DataValue> outcome = await data.InsertAsync(
                        historianContext,
                        kv.Key,
                        kv.Value,
                        ct).ConfigureAwait(false);
                    ValidateInsertOutcome(
                        kv.Key,
                        outcome,
                        kv.Value.Count);
                }
                catch (Exception exception) when (!ct.IsCancellationRequested)
                {
                    ReportFailedFlush(exception, 1, kv.Value.Count);
                }
            }
        }

        private void ReportFailedFlush(Exception exception, int nodes, int dropped)
        {
            Interlocked.Add(ref m_droppedSamples, dropped);
            long now = m_timeProvider.GetTimestamp();
            if (!m_failureReported ||
                m_timeProvider.GetElapsedTime(m_lastFailureReport, now) >= TimeSpan.FromSeconds(30))
            {
                m_failureReported = true;
                m_lastFailureReport = now;
                m_logger?.HistorianCaptureSinkFlushFailedForNodesNodeS(exception, nodes);
            }
        }

        private void ValidateInsertOutcome(
            NodeId nodeId,
            HistorianUpdateOutcome<DataValue> outcome,
            int expectedCount)
        {
            if (outcome.OperationResults.Count != expectedCount)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "The historian insert returned a mismatched operation count.");
            }
            int rejected = 0;
            StatusCode firstRejection = StatusCodes.Good;
            for (int i = 0; i < outcome.OperationResults.Count; i++)
            {
                StatusCode status = outcome.OperationResults[i];
                if (StatusCode.IsBad(status))
                {
                    if (rejected == 0)
                    {
                        firstRejection = status;
                    }
                    rejected++;
                }
            }
            if (rejected > 0)
            {
                Interlocked.Add(ref m_rejectedSamples, rejected);
                m_logger?.HistorianCaptureSinkRejectedSamples(
                    nodeId,
                    rejected,
                    firstRejection);
            }
        }

        private void OnSampleDropped(CaptureEvent ev)
        {
            Interlocked.Increment(ref m_droppedSamples);
            // Log at trace level — high-frequency drops would otherwise spam.
            m_logger?.HistorianCaptureSinkDroppedSampleForNodeIdMode(ev.NodeId, m_options.FullMode);
        }

        private static BoundedChannelFullMode MapFullMode(CaptureFullMode mode)
        {
            return mode switch
            {
                CaptureFullMode.DropOldest => BoundedChannelFullMode.DropOldest,
                CaptureFullMode.DropNewest => BoundedChannelFullMode.DropNewest,
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
        }

        private readonly record struct CaptureEvent(NodeId NodeId, DataValue Value);

        private readonly IHistorianProvider m_provider;
        private readonly ServerSystemContext m_systemContext;
        private readonly HistorianCaptureOptions m_options;
        private readonly ILogger? m_logger;
        private readonly Channel<CaptureEvent> m_channel;
        private readonly Task m_consumer;
        private readonly CancellationTokenSource m_shutdownCts;
        private readonly TimeProvider m_timeProvider;
        private long m_droppedSamples;
        private long m_rejectedSamples;
        private long m_lastFailureReport;
        private int m_unavailableReported;
        private bool m_failureReported;
        private int m_disposed;
    }

    /// <summary>
    /// Source-generated log messages for HistorianCaptureSink.
    /// </summary>
    internal static partial class HistorianCaptureSinkLog
    {
        /// <summary>
        /// Logs forced cancellation after the sample capture consumer failed to drain within five seconds.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.HistorianCaptureSink + 0, Level = LogLevel.Warning,
            Message = "HistorianCaptureSink consumer did not drain within 5s; cancelling forcibly.")]
        public static partial void HistorianCaptureSinkConsumerDidNotDrainWithin5s(this ILogger logger);

        /// <summary>
        /// Logs a sample capture consumer failure during shutdown.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.HistorianCaptureSink + 1, Level = LogLevel.Warning,
            Message = "HistorianCaptureSink consumer faulted during shutdown.")]
        public static partial void HistorianCaptureSinkConsumerFaultedDuringShutdown(this ILogger logger, Exception ex);

        /// <summary>
        /// Logs an exception that unexpectedly terminated the sample capture consumer.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.HistorianCaptureSink + 2, Level = LogLevel.Error,
            Message = "HistorianCaptureSink consumer terminated unexpectedly.")]
        public static partial void HistorianCaptureSinkConsumerTerminatedUnexpectedly(
            this ILogger logger,
            Exception ex);

        /// <summary>
        /// Logs a failed historian flush and the number of nodes whose samples were dropped.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.HistorianCaptureSink + 3, Level = LogLevel.Warning,
            Message = "HistorianCaptureSink flush failed for {Nodes} node(s); samples dropped.")]
        public static partial void HistorianCaptureSinkFlushFailedForNodesNodeS(
            this ILogger logger,
            Exception ex,
            int nodes);

        /// <summary>
        /// Logs a sample dropped under the capture queue's configured full mode.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.HistorianCaptureSink + 4, Level = LogLevel.Trace,
            Message = "HistorianCaptureSink dropped sample for {NodeId} ({Mode}).")]
        public static partial void HistorianCaptureSinkDroppedSampleForNodeIdMode(
            this ILogger logger,
            NodeId nodeId,
            CaptureFullMode mode);

        /// <summary>
        /// Logs the count and first status code of samples rejected by the historian provider.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.HistorianCaptureSink + 5, Level = LogLevel.Warning,
            Message = "HistorianCaptureSink provider rejected {Count} sample(s) for {NodeId}; " +
                "first status {StatusCode}.")]
        public static partial void HistorianCaptureSinkRejectedSamples(
            this ILogger logger,
            NodeId nodeId,
            int count,
            StatusCode statusCode);

        /// <summary>
        /// Logs a sample dropped because the capture consumer is unavailable.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.HistorianCaptureSink + 6, Level = LogLevel.Error,
            Message = "HistorianCaptureSink is unavailable; dropping the sample for {NodeId}.")]
        public static partial void HistorianCaptureSinkUnavailable(
            this ILogger logger,
            Exception exception,
            NodeId nodeId);

        /// <summary>
        /// Logs a sample dropped because the capture queue is closed.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.HistorianCaptureSink + 7, Level = LogLevel.Warning,
            Message = "HistorianCaptureSink queue is closed; dropping the sample for {NodeId}.")]
        public static partial void HistorianCaptureSinkQueueClosed(
            this ILogger logger,
            NodeId nodeId);
    }
}
