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

// CA2000: test code; disposables are short-lived or ownership-transferred.
#pragma warning disable CA2000
// CA2007: tests run without a SynchronizationContext.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;

#nullable enable

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Extra gap-coverage tests for <see cref="HistorianCaptureSink"/>
    /// targeting non-blocking queue modes and invalid configuration.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistorianCaptureSinkExtraTests
    {
        private const ushort Ns = 2;

        [Test]
        public async Task EnqueueWithDropNewestDeliversSamplesToProviderAsync()
        {
            var provider = new RecordingProvider();
            ServerSystemContext ctx = CreateSystemContext();

            var options = new HistorianCaptureOptions
            {
                FullMode = CaptureFullMode.DropNewest,
                MaxQueuedSamples = 64,
                BatchTarget = 10,
                BatchWindow = TimeSpan.FromMilliseconds(10)
            };

            var nodeId = new NodeId("drop-newest-node", Ns);

            await using var sink = new HistorianCaptureSink(provider, ctx, options);

            for (int i = 0; i < 4; i++)
            {
                sink.Enqueue(
                    nodeId,
                    new DataValue(new Variant(i), StatusCodes.Good,
                        sourceTimestamp: DateTime.UtcNow.AddSeconds(i)));
            }

            // DisposeAsync flushes all pending samples.
            await sink.DisposeAsync().ConfigureAwait(false);

            int total = 0;
            foreach (int b in provider.BatchSizes)
            {
                total += b;
            }
            Assert.That(total, Is.EqualTo(4),
                "All samples below queue capacity must reach the provider.");
            Assert.That(sink.DroppedSampleCount, Is.Zero);
        }

        [Test]
        public async Task EnqueueAfterDisposeIsSilentAsync()
        {
            var provider = new RecordingProvider();
            ServerSystemContext ctx = CreateSystemContext();

            var options = new HistorianCaptureOptions
            {
                FullMode = CaptureFullMode.DropNewest,
                MaxQueuedSamples = 64,
                BatchTarget = 10,
                BatchWindow = TimeSpan.FromMilliseconds(5)
            };

            var sink = new HistorianCaptureSink(provider, ctx, options);
            await sink.DisposeAsync().ConfigureAwait(false);
            int countBefore = 0;
            foreach (int b in provider.BatchSizes)
            {
                countBefore += b;
            }

            // Enqueue after dispose must be a silent no-op (no exception,
            // and no new samples appear).
            Assert.DoesNotThrow(() => sink.Enqueue(
                new NodeId("post-dispose", Ns),
                new DataValue(new Variant(42), StatusCodes.Good, DateTime.UtcNow)));

            await Task.Delay(30).ConfigureAwait(false);

            int countAfter = 0;
            foreach (int b in provider.BatchSizes)
            {
                countAfter += b;
            }
            Assert.That(countAfter, Is.EqualTo(countBefore),
                "No new samples must be inserted after dispose.");
        }

        [Test]
        public void SinkWithInvalidFullModeEnumThrows()
        {
            var provider = new RecordingProvider();
            ServerSystemContext ctx = CreateSystemContext();

            var options = new HistorianCaptureOptions
            {
                FullMode = (CaptureFullMode)99,
                MaxQueuedSamples = 32,
                BatchTarget = 5,
                BatchWindow = TimeSpan.FromMilliseconds(10)
            };

            Assert.That(
                () => new HistorianCaptureSink(provider, ctx, options),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        private static ServerSystemContext CreateSystemContext()
        {
            var nsTable = new NamespaceTable();
            nsTable.Append("urn:test:extra");
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockServer = new Mock<IServerInternal>();
            mockServer.Setup(s => s.NamespaceUris).Returns(nsTable);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(nsTable));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);
            return new ServerSystemContext(mockServer.Object);
        }

        private sealed class RecordingProvider :
            HistorianProviderBase,
            IHistorianBulkInsertProvider
        {
            private readonly Lock m_lock = new();

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
    }
}
