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
    /// silently (counted in <see cref="DroppedSampleCount"/>). Provider
    /// exceptions during flush are logged and swallowed; the consumer
    /// continues. Callers needing durability should use the explicit
    /// HistoryUpdate Insert service instead.
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
        public HistorianCaptureSink(
            IHistorianProvider provider,
            ServerSystemContext systemContext,
            HistorianCaptureOptions? options = null)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            m_systemContext = systemContext ?? throw new ArgumentNullException(nameof(systemContext));
            m_options = options ?? new HistorianCaptureOptions();
            m_logger = systemContext.Server?.Telemetry?.CreateLogger<HistorianCaptureSink>();

            var channelOptions = new BoundedChannelOptions(m_options.MaxQueuedSamples)
            {
                FullMode = MapFullMode(m_options.FullMode),
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            };
            m_channel = Channel.CreateBounded<CaptureEvent>(
                channelOptions, OnSampleDropped);
            m_shutdownCts = new CancellationTokenSource();
            m_consumer = Task.Run(() => ConsumeAsync(m_shutdownCts.Token));
        }

        /// <summary>
        /// The number of samples that have been dropped because the
        /// queue was full. Increments on
        /// <see cref="CaptureFullMode.DropOldest"/> /
        /// <see cref="CaptureFullMode.DropNewest"/>; never increments
        /// when <see cref="CaptureFullMode.Wait"/> is selected.
        /// </summary>
        public long DroppedSampleCount => Interlocked.Read(ref m_droppedSamples);

        /// <summary>
        /// Enqueues a new sample for the supplied node. Non-blocking for
        /// <see cref="CaptureFullMode.DropOldest"/> /
        /// <see cref="CaptureFullMode.DropNewest"/>; blocks for
        /// <see cref="CaptureFullMode.Wait"/>.
        /// </summary>
        public void Enqueue(NodeId nodeId, DataValue value)
        {
            if (nodeId.IsNull || value.IsNull)
            {
                return;
            }
            if (m_disposed)
            {
                return;
            }

            var ev = new CaptureEvent(nodeId, value);

            if (m_options.FullMode == CaptureFullMode.Wait)
            {
                // Synchronous wait — must not block the value-setting
                // thread indefinitely if the consumer crashed; honour
                // the shutdown token.
                try
                {
                    m_channel.Writer.WriteAsync(ev, m_shutdownCts.Token)
                        .AsTask().GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    // sink shutting down — silently drop
                }
                catch (ChannelClosedException)
                {
                    // sink shutting down — silently drop
                }
                return;
            }

            _ = m_channel.Writer.TryWrite(ev);
        }

        /// <summary>
        /// Flushes pending samples and shuts down the consumer task.
        /// Idempotent.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            // Close the writer; the consumer drains remaining items and
            // exits its async-foreach loop normally.
            m_channel.Writer.TryComplete();
            try
            {
                // Bound the wait — if the consumer is stuck the host
                // shutdown should not block forever.
                await m_consumer
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                m_logger?.LogWarning(
                    "HistorianCaptureSink consumer did not drain within 5s; cancelling forcibly.");
                m_shutdownCts.Cancel();
            }
            catch (Exception ex)
            {
                m_logger?.LogWarning(ex, "HistorianCaptureSink consumer faulted during shutdown.");
            }
            m_shutdownCts.Dispose();
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
                    await FlushAsync(batch, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
            catch (Exception ex)
            {
                m_logger?.LogError(ex, "HistorianCaptureSink consumer terminated unexpectedly.");
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
            using var windowCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            windowCts.CancelAfter(m_options.BatchWindow);
            try
            {
                while (total < m_options.BatchTarget
                    && await m_channel.Reader.WaitToReadAsync(windowCts.Token).ConfigureAwait(false))
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
            var opContext = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryUpdate, RequestLifetime.None);
            var historianContext = new HistorianOperationContext(
                m_systemContext, opContext, null, HistoryUpdateType.Insert);

            try
            {
                if (m_provider is IHistorianBulkInsertProvider bulk)
                {
                    // Hand the same view to the provider; convert lists
                    // to IList<DataValue> via assignment.
                    var view = new Dictionary<NodeId, IList<DataValue>>(batch.Count);
                    foreach (KeyValuePair<NodeId, List<DataValue>> kv in batch)
                    {
                        view[kv.Key] = kv.Value;
                    }
                    _ = await bulk.InsertBatchAsync(historianContext, view, ct)
                        .ConfigureAwait(false);
                    return;
                }

                if (m_provider is IHistorianDataProvider data)
                {
                    foreach (KeyValuePair<NodeId, List<DataValue>> kv in batch)
                    {
                        _ = await data.InsertAsync(historianContext, kv.Key, kv.Value, ct)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
            catch (Exception ex)
            {
                m_logger?.LogWarning(
                    ex,
                    "HistorianCaptureSink flush failed for {Nodes} node(s); samples dropped.",
                    batch.Count);
            }
        }

        private void OnSampleDropped(CaptureEvent ev)
        {
            Interlocked.Increment(ref m_droppedSamples);
            // Log at trace level — high-frequency drops would otherwise spam.
            m_logger?.LogTrace(
                "HistorianCaptureSink dropped sample for {NodeId} ({Mode}).",
                ev.NodeId, m_options.FullMode);
        }

        private static BoundedChannelFullMode MapFullMode(CaptureFullMode mode)
        {
            return mode switch
            {
                CaptureFullMode.DropOldest => BoundedChannelFullMode.DropOldest,
                CaptureFullMode.DropNewest => BoundedChannelFullMode.DropNewest,
                CaptureFullMode.Wait => BoundedChannelFullMode.Wait,
                _ => BoundedChannelFullMode.DropOldest,
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
        private long m_droppedSamples;
        private bool m_disposed;
    }
}
