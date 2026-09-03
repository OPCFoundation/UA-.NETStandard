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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;

#nullable enable

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Advanced tests for <see cref="HistorianCaptureSink"/> covering
    /// channel-batching thresholds, dispose-with-pending semantics,
    /// provider-exception isolation, and enqueue guards.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistorianCaptureSinkAdvancedTests
    {
        private const ushort kNs = 2;

        [Test]
        public async Task SinkRespectsBatchSizeThresholdAsync()
        {
            var provider = new RecordingBulkProvider();
            ServerSystemContext ctx = CreateSystemContext();
            var options = new HistorianCaptureOptions
            {
                BatchTarget = 3,
                BatchWindow = TimeSpan.FromMilliseconds(5)
            };

            await using (var sink = new HistorianCaptureSink(provider, ctx, options))
            {
                for (int i = 0; i < 7; i++)
                {
                    sink.Enqueue(
                        new NodeId($"n{i}", kNs),
                        new DataValue(new Variant(i), StatusCodes.Good, DateTime.UtcNow));
                }
            }

            int totalSamples = provider.BatchSizes.Sum();
            Assert.That(totalSamples, Is.EqualTo(7), "All 7 samples must be flushed.");
            Assert.That(provider.BatchSizes, Has.All.LessThanOrEqualTo(3),
                "No single flush should exceed BatchTarget.");
        }

        [Test]
        public async Task DisposeAsyncFlushesPendingSamplesAsync()
        {
            var provider = new RecordingBulkProvider();
            ServerSystemContext ctx = CreateSystemContext();
            var options = new HistorianCaptureOptions
            {
                BatchTarget = 1000,
                BatchWindow = TimeSpan.FromSeconds(30)
            };
            var nodeId = new NodeId("pending", kNs);

            await using (var sink = new HistorianCaptureSink(provider, ctx, options))
            {
                for (int i = 0; i < 5; i++)
                {
                    sink.Enqueue(nodeId, new DataValue(new Variant(i), StatusCodes.Good, DateTime.UtcNow));
                }
            }

            int total = provider.Inserts
                .Where(x => x.NodeId == nodeId)
                .Sum(x => x.Values.Count);
            Assert.That(total, Is.EqualTo(5),
                "DisposeAsync must drain all pending samples even when BatchTarget is not reached.");
        }

        [Test]
        public async Task SinkCapturedAfterDisposeDoesNotInsertAsync()
        {
            var provider = new RecordingBulkProvider();
            ServerSystemContext ctx = CreateSystemContext();
            var options = new HistorianCaptureOptions
            {
                BatchTarget = 1,
                BatchWindow = TimeSpan.FromMilliseconds(5)
            };
            var nodeId = new NodeId("postDispose", kNs);

            var sink = new HistorianCaptureSink(provider, ctx, options);
            await sink.DisposeAsync().ConfigureAwait(false);
            int countBefore = provider.Inserts.Count;

            Assert.DoesNotThrow(() =>
                sink.Enqueue(nodeId, new DataValue(new Variant(99), StatusCodes.Good, DateTime.UtcNow)));

            await Task.Delay(50).ConfigureAwait(false);
            Assert.That(provider.Inserts, Has.Count.EqualTo(countBefore),
                "Enqueue after dispose must be a silent no-op.");
        }

        [Test]
        public async Task ProviderExceptionFaultsConsumerAndSurfacesOnDisposeAsync()
        {
            var provider = new ThrowThenSucceedProvider(throwCount: 2);
            ServerSystemContext ctx = CreateSystemContext();
            var options = new HistorianCaptureOptions
            {
                BatchTarget = 1,
                BatchWindow = TimeSpan.FromMilliseconds(5)
            };

            var sink = new HistorianCaptureSink(provider, ctx, options);
            sink.Enqueue(
                new NodeId("retry", kNs),
                new DataValue(new Variant(0), StatusCodes.Good, DateTime.UtcNow));

            // Give the consumer time to flush and fault on the first provider exception.
            await Task.Delay(200).ConfigureAwait(false);

            // The provider failure remains observable on disposal, but the
            // background archive must not break the live value-change path.
            Assert.DoesNotThrow(() =>
                sink.Enqueue(
                    new NodeId("retry", kNs),
                    new DataValue(new Variant(1), StatusCodes.Good, DateTime.UtcNow)));
            Assert.That(sink.DroppedSampleCount, Is.EqualTo(1));

            InvalidOperationException disposeEx = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await sink.DisposeAsync().ConfigureAwait(false))!;
            Assert.That(disposeEx.Message, Is.EqualTo("Simulated provider failure"));

            Assert.That(provider.TotalCalls, Is.EqualTo(1),
                "The faulted consumer must not retry after the first provider failure.");
            Assert.That(provider.SuccessfulInserts, Is.Zero,
                "No batch should succeed once the consumer has faulted.");
        }

        [Test]
        public async Task OperationRejectionDoesNotFaultSharedCapturePipelineAsync()
        {
            var provider = new RejectThenSucceedProvider();
            ServerSystemContext ctx = CreateSystemContext();
            var options = new HistorianCaptureOptions
            {
                BatchTarget = 1,
                BatchWindow = TimeSpan.FromMilliseconds(5)
            };
            var nodeId = new NodeId("rejected", kNs);
            var sink = new HistorianCaptureSink(provider, ctx, options);

            sink.Enqueue(
                nodeId,
                new DataValue(
                    new Variant(1),
                    StatusCodes.Good,
                    DateTime.UtcNow));
            await provider.FirstCall.Task.ConfigureAwait(false);
            Assert.DoesNotThrow(() => sink.Enqueue(
                nodeId,
                new DataValue(
                    new Variant(2),
                    StatusCodes.Good,
                    DateTime.UtcNow.AddSeconds(1))));
            await provider.SecondCall.Task.ConfigureAwait(false);
            await sink.DisposeAsync().ConfigureAwait(false);

            Assert.That(sink.RejectedSampleCount, Is.EqualTo(1));
            Assert.That(provider.TotalCalls, Is.EqualTo(2));
        }

        [Test]
        public async Task DropNewestModeDropsNewSamplesUnderBackpressureAsync()
        {
            var tcsUnblock = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new BlockingProvider(tcsUnblock.Task);
            ServerSystemContext ctx = CreateSystemContext();
            var options = new HistorianCaptureOptions
            {
                MaxQueuedSamples = 4,
                BatchTarget = 2,
                BatchWindow = TimeSpan.FromMilliseconds(5),
                FullMode = CaptureFullMode.DropNewest
            };

            await using var sink = new HistorianCaptureSink(provider, ctx, options);

            for (int i = 0; i < 20; i++)
            {
                sink.Enqueue(
                    new NodeId("drop", kNs),
                    new DataValue(new Variant(i), StatusCodes.Good, DateTime.UtcNow));
            }

            tcsUnblock.TrySetResult(true);

            await sink.DisposeAsync().ConfigureAwait(false);

            Assert.That(sink.DroppedSampleCount, Is.GreaterThan(0),
                "DropNewest mode must drop samples when the queue is full.");
            Assert.That(provider.TotalInsertedSamples, Is.GreaterThan(0),
                "Some samples must still be inserted despite drops.");
            Assert.That(provider.TotalInsertedSamples + sink.DroppedSampleCount,
                Is.LessThanOrEqualTo(20),
                "Inserted + dropped must not exceed total enqueued.");
        }

        private static ServerSystemContext CreateSystemContext()
        {
            var nsTable = new NamespaceTable();
            nsTable.Append("urn:test:advanced");
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockServer = new Mock<IServerInternal>();
            mockServer.Setup(s => s.NamespaceUris).Returns(nsTable);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(nsTable));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);
            return new ServerSystemContext(mockServer.Object);
        }

        /// <summary>
        /// Records every <see cref="IHistorianBulkInsertProvider.InsertBatchAsync"/>
        /// call and the per-node sample counts within each batch.
        /// </summary>
        private sealed class RecordingBulkProvider :
            HistorianProviderBase,
            IHistorianBulkInsertProvider
        {
            private readonly Lock m_lock = new();

            public List<(NodeId NodeId, ArrayOf<DataValue> Values)> Inserts { get; } = [];
            public List<int> BatchSizes { get; } = [];

            public ValueTask<ArrayOf<HistorianUpdateOutcome<DataValue>>> InsertBatchAsync(
                HistorianOperationContext context,
                ArrayOf<HistorianDataBatch> batch,
                CancellationToken cancellationToken)
            {
                var result =
                    new HistorianUpdateOutcome<DataValue>[batch.Count];
                int batchTotal = 0;
                lock (m_lock)
                {
                    for (int batchIndex = 0;
                        batchIndex < batch.Count;
                        batchIndex++)
                    {
                        HistorianDataBatch entry = batch[batchIndex];
                        Inserts.Add((entry.NodeId, entry.Values));
                        batchTotal += entry.Values.Count;
                        var statuses =
                            new StatusCode[entry.Values.Count];
                        for (int i = 0; i < statuses.Length; i++)
                        {
                            statuses[i] = StatusCodes.Good;
                        }
                        result[batchIndex] =
                            new HistorianUpdateOutcome<DataValue>(
                                statuses);
                    }

                    BatchSizes.Add(batchTotal);
                }

                return new ValueTask<
                    ArrayOf<HistorianUpdateOutcome<DataValue>>>(
                        result);
            }
        }

        /// <summary>
        /// Throws on the first N calls, then succeeds. Signals a
        /// <see cref="TaskCompletionSource{TResult}"/> on first success
        /// for deterministic synchronization.
        /// </summary>
        private sealed class ThrowThenSucceedProvider :
            HistorianProviderBase,
            IHistorianBulkInsertProvider
        {
            private readonly TaskCompletionSource<bool>? m_onFirstSuccess;
            private int m_remainingFailures;
            private int m_totalCalls;
            private int m_successfulInserts;

            public int TotalCalls => Volatile.Read(ref m_totalCalls);
            public int SuccessfulInserts => Volatile.Read(ref m_successfulInserts);

            public ThrowThenSucceedProvider(
                int throwCount,
                TaskCompletionSource<bool>? onFirstSuccess = null)
            {
                m_remainingFailures = throwCount;
                m_onFirstSuccess = onFirstSuccess;
            }

            public ValueTask<ArrayOf<HistorianUpdateOutcome<DataValue>>> InsertBatchAsync(
                HistorianOperationContext context,
                ArrayOf<HistorianDataBatch> batch,
                CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref m_totalCalls);
                if (Interlocked.Decrement(ref m_remainingFailures) >= 0)
                {
                    throw new InvalidOperationException("Simulated provider failure");
                }

                Interlocked.Increment(ref m_successfulInserts);
                m_onFirstSuccess?.TrySetResult(true);

                var result =
                    new HistorianUpdateOutcome<DataValue>[batch.Count];
                for (int batchIndex = 0;
                    batchIndex < batch.Count;
                    batchIndex++)
                {
                    var statuses =
                        new StatusCode[batch[batchIndex].Values.Count];
                    for (int i = 0; i < statuses.Length; i++)
                    {
                        statuses[i] = StatusCodes.Good;
                    }
                    result[batchIndex] =
                        new HistorianUpdateOutcome<DataValue>(
                            statuses);
                }

                return new ValueTask<
                    ArrayOf<HistorianUpdateOutcome<DataValue>>>(
                        result);
            }
        }

        private sealed class RejectThenSucceedProvider :
            HistorianProviderBase,
            IHistorianBulkInsertProvider
        {
            public TaskCompletionSource<bool> FirstCall { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> SecondCall { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            public int TotalCalls => Volatile.Read(ref m_totalCalls);

            public ValueTask<ArrayOf<HistorianUpdateOutcome<DataValue>>> InsertBatchAsync(
                HistorianOperationContext context,
                ArrayOf<HistorianDataBatch> batch,
                CancellationToken cancellationToken)
            {
                int call = Interlocked.Increment(ref m_totalCalls);
                var outcomes =
                    new HistorianUpdateOutcome<DataValue>[batch.Count];
                for (int batchIndex = 0;
                    batchIndex < batch.Count;
                    batchIndex++)
                {
                    var statuses =
                        new StatusCode[batch[batchIndex].Values.Count];
                    for (int i = 0; i < statuses.Length; i++)
                    {
                        statuses[i] = call == 1
                            ? StatusCodes.BadEntryExists
                            : StatusCodes.GoodEntryInserted;
                    }
                    outcomes[batchIndex] =
                        new HistorianUpdateOutcome<DataValue>(
                            statuses);
                }
                if (call == 1)
                {
                    FirstCall.TrySetResult(true);
                }
                else
                {
                    SecondCall.TrySetResult(true);
                }
                return new ValueTask<
                    ArrayOf<HistorianUpdateOutcome<DataValue>>>(
                        outcomes);
            }

            private int m_totalCalls;
        }

        /// <summary>
        /// Blocks on the first <see cref="InsertBatchAsync"/> until the
        /// supplied task completes, simulating a slow provider to induce
        /// channel backpressure.
        /// </summary>
        private sealed class BlockingProvider :
            HistorianProviderBase,
            IHistorianBulkInsertProvider
        {
            private readonly Task m_gate;
            private int m_firstCall = 1;
            private int m_totalInsertedSamples;

            public int TotalInsertedSamples => Volatile.Read(ref m_totalInsertedSamples);

            public BlockingProvider(Task gate)
            {
                m_gate = gate;
            }

            public async ValueTask<ArrayOf<HistorianUpdateOutcome<DataValue>>> InsertBatchAsync(
                HistorianOperationContext context,
                ArrayOf<HistorianDataBatch> batch,
                CancellationToken cancellationToken)
            {
                if (Interlocked.Exchange(ref m_firstCall, 0) == 1)
                {
                    await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                var result =
                    new HistorianUpdateOutcome<DataValue>[batch.Count];
                for (int batchIndex = 0;
                    batchIndex < batch.Count;
                    batchIndex++)
                {
                    HistorianDataBatch entry = batch[batchIndex];
                    Interlocked.Add(
                        ref m_totalInsertedSamples,
                        entry.Values.Count);
                    var statuses = new StatusCode[entry.Values.Count];
                    for (int i = 0; i < statuses.Length; i++)
                    {
                        statuses[i] = StatusCodes.Good;
                    }
                    result[batchIndex] =
                        new HistorianUpdateOutcome<DataValue>(
                            statuses);
                }

                return result;
            }
        }
    }
}
