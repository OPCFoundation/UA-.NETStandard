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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Verifies batch-sized transaction projection and exact sample-cap outcomes.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [NonParallelizable]
    public sealed class HistorianTransactionalScalingTests
    {
        [Test]
        public async Task AtomicSingleValueAllocationDoesNotScaleWithArchiveAsync(
            [Values(HistoryUpdateType.Insert, HistoryUpdateType.Replace, HistoryUpdateType.Update)]
            HistoryUpdateType operation,
            [Values] bool capped)
        {
            long small = await MeasureUpdatesAsync(32, operation, capped).ConfigureAwait(false);
            long large = await MeasureUpdatesAsync(32_768, operation, capped).ConfigureAwait(false);
            TestContext.Out.WriteLine(
                $"{operation}, capped={capped}: 8 updates, 32 keys={small} bytes, 32768 keys={large} bytes.");
            Assert.That(small, Is.GreaterThan(0));
            Assert.That(large, Is.GreaterThan(0));
            Assert.That(large, Is.LessThanOrEqualTo(small + 65_536),
                "A single-value transaction must not allocate a copy of the existing archive.");
        }

        [TestCase("existingDuplicate")]
        [TestCase("batchDuplicate")]
        [TestCase("evictedExisting")]
        [TestCase("evictedBatch")]
        public async Task CappedAtomicInsertRollsBackWithExactFailingOperationAsync(string scenario)
        {
            using var provider = new InMemoryHistorianProvider(new InMemoryHistorianOptions
            {
                RawDataRetentionPeriod = TimeSpan.Zero,
                MaxSamplesPerNode = 2
            });
            HistorianOperationContext context = CreateContext();
            await provider.InsertAsync(
                context, s_nodeId, [MakeValue(20, 20), MakeValue(40, 40)], CancellationToken.None)
                .ConfigureAwait(false);
            ArrayOf<DataValue> batch = scenario switch
            {
                "existingDuplicate" => [MakeValue(30, 30), MakeValue(50, 50), MakeValue(40, 400)],
                "batchDuplicate" => [MakeValue(30, 30), MakeValue(30, 300)],
                "evictedExisting" => [MakeValue(30, 30), MakeValue(20, 200)],
                "evictedBatch" => [MakeValue(30, 30), MakeValue(35, 35), MakeValue(30, 300)],
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };

            HistorianUpdateOutcome<DataValue> outcome = await provider.InsertAtomicAsync(
                context, s_nodeId, batch, CancellationToken.None).ConfigureAwait(false);
            Assert.That(outcome.TransactionRolledBack, Is.True);
            Assert.That(outcome.OperationResults, Has.Count.EqualTo(batch.Count));
            StatusCode expectedFailure = scenario is "existingDuplicate" or "batchDuplicate"
                ? StatusCodes.BadEntryExists
                : StatusCodes.BadOutOfRange;
            for (int i = 0; i < batch.Count; i++)
            {
                Assert.That(outcome.OperationResults[i],
                    Is.EqualTo(i == batch.Count - 1 ? expectedFailure : StatusCodes.BadTransactionFailed));
            }
            Assert.That(outcome.OldValues, Is.Empty);
            HistorianPage<HistoricalDataValue> raw = await ReadRawAsync(provider, context).ConfigureAwait(false);
            Assert.That(raw.Values.ToArray().Select(value => value.Value.SourceTimestamp),
                Is.EqualTo(new DateTimeUtc[] { s_start.AddTicks(20), s_start.AddTicks(40) }));
            Assert.That(raw.Values[0].Value.WrappedValue.TryGetValue(out int first), Is.True);
            Assert.That(first, Is.EqualTo(20));
            Assert.That(raw.Values[1].Value.WrappedValue.TryGetValue(out int last), Is.True);
            Assert.That(last, Is.EqualTo(40));
            HistorianPage<ModifiedDataValue> modified = await provider.ReadModifiedAsync(
                context,
                new HistorianModifiedReadRequest
                {
                    NodeId = s_nodeId,
                    StartTime = DateTimeUtc.MinValue,
                    EndTime = DateTimeUtc.MaxValue,
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(modified.Values, Has.Count.EqualTo(2));
            Assert.That(modified.Values.ToArray().Select(value => value.Info.UpdateType),
                Has.All.EqualTo(HistoryUpdateType.Insert));
        }

        [Test]
        public async Task CappedAtomicUpdateKeepsReplacementStatusesAndPriorValuesAsync()
        {
            using var provider = new InMemoryHistorianProvider(new InMemoryHistorianOptions
            {
                RawDataRetentionPeriod = TimeSpan.Zero,
                MaxSamplesPerNode = 2
            });
            HistorianOperationContext context = CreateContext();
            await provider.InsertAsync(
                context, s_nodeId, [MakeValue(20, 20), MakeValue(40, 40)], CancellationToken.None)
                .ConfigureAwait(false);
            HistorianUpdateOutcome<DataValue> outcome = await provider.UpdateAtomicAsync(
                context, s_nodeId,
                [MakeValue(30, 30), MakeValue(40, 400), MakeValue(50, 50), MakeValue(40, 4000)],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(outcome.TransactionRolledBack, Is.False);
            Assert.That(outcome.OperationResults.ToArray(), Is.EqualTo(
            [
                StatusCodes.GoodEntryInserted,
                StatusCodes.GoodEntryReplaced,
                StatusCodes.GoodEntryInserted,
                StatusCodes.GoodEntryReplaced
            ]));
            Assert.That(outcome.OldValues, Has.Count.EqualTo(2));
            Assert.That(outcome.OldValues[0].WrappedValue.TryGetValue(out int firstPrior), Is.True);
            Assert.That(firstPrior, Is.EqualTo(40));
            Assert.That(outcome.OldValues[1].WrappedValue.TryGetValue(out int secondPrior), Is.True);
            Assert.That(secondPrior, Is.EqualTo(400));
            HistorianPage<HistoricalDataValue> raw = await ReadRawAsync(provider, context).ConfigureAwait(false);
            Assert.That(raw.Values, Has.Count.EqualTo(2));
            Assert.That(raw.Values[0].Value.SourceTimestamp, Is.EqualTo((DateTimeUtc)s_start.AddTicks(40)));
            Assert.That(raw.Values[0].Value.WrappedValue.TryGetValue(out int replaced), Is.True);
            Assert.That(replaced, Is.EqualTo(4000));
            Assert.That(raw.Values[1].Value.SourceTimestamp, Is.EqualTo((DateTimeUtc)s_start.AddTicks(50)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CappedAtomicInsertUsesCapacityReleasedByDeletionAsync(bool deleteAtTime)
        {
            using var provider = new InMemoryHistorianProvider(new InMemoryHistorianOptions
            {
                RawDataRetentionPeriod = TimeSpan.Zero,
                MaxSamplesPerNode = 3
            });
            HistorianOperationContext context = CreateContext();
            await provider.InsertAsync(
                context, s_nodeId, [MakeValue(10, 10), MakeValue(20, 20), MakeValue(30, 30)], CancellationToken.None)
                .ConfigureAwait(false);
            HistorianUpdateOutcome<DataValue> deleted = deleteAtTime
                ? await provider.DeleteAtTimeAsync(
                    context, s_nodeId, [(DateTimeUtc)s_start.AddTicks(20)], CancellationToken.None)
                    .ConfigureAwait(false)
                : await provider.DeleteRawAsync(
                    context, s_nodeId, s_start.AddTicks(20), s_start.AddTicks(21), false, CancellationToken.None)
                    .ConfigureAwait(false);
            Assert.That(deleted.OperationResults[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(deleted.OldValues, Has.Count.EqualTo(1));
            HistorianUpdateOutcome<DataValue> outcome = await provider.InsertAtomicAsync(
                context, s_nodeId, [MakeValue(5, 5), MakeValue(15, 15)], CancellationToken.None).ConfigureAwait(false);
            Assert.That(outcome.TransactionRolledBack, Is.False);
            Assert.That(outcome.OperationResults.ToArray(),
                Is.EqualTo([StatusCodes.GoodEntryInserted, StatusCodes.GoodEntryInserted]));
            HistorianPage<HistoricalDataValue> raw = await ReadRawAsync(provider, context).ConfigureAwait(false);
            Assert.That(raw.Values.ToArray().Select(value => value.Value.SourceTimestamp),
                Is.EqualTo(new DateTimeUtc[] { s_start.AddTicks(10), s_start.AddTicks(15), s_start.AddTicks(30) }));
        }

        [TestCase(0u)]
        [TestCase(2u)]
        [TestCase(3u)]
        public async Task AtomicUpdateProjectsStartBoundReplacementBeforeLaterOperationsAsync(uint maximumSamples)
        {
            var clock = new FakeTimeProvider(s_start);
            using var provider = new InMemoryHistorianProvider(new InMemoryHistorianOptions
            {
                MaxSamplesPerNode = maximumSamples
            }, clock);
            HistorianOperationContext context = CreateContext();
            DataValue older = new(1, StatusCodes.Good, s_start.AddMinutes(-30));
            DataValue newer = new(2, StatusCodes.Good, s_start.AddMinutes(30));
            await provider.InsertAsync(context, s_nodeId, [older, newer], CancellationToken.None)
                .ConfigureAwait(false);
            clock.Advance(TimeSpan.FromHours(1));

            HistorianUpdateOutcome<DataValue> rejected = await provider.UpdateAtomicAsync(
                context, s_nodeId, [new DataValue(3, StatusCodes.Good, s_start), older], CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(rejected.TransactionRolledBack, Is.True);
            Assert.That(rejected.OperationResults.ToArray(),
                Is.EqualTo([StatusCodes.BadTransactionFailed, StatusCodes.BadOutOfRange]));
            Assert.That(rejected.OldValues, Is.Empty);
            HistorianPage<HistoricalDataValue> unchanged = await ReadRawAsync(provider, context).ConfigureAwait(false);
            Assert.That(unchanged.Values.ToArray().Select(value => value.Value.SourceTimestamp),
                Is.EqualTo([older.SourceTimestamp, newer.SourceTimestamp]));

            HistorianUpdateOutcome<DataValue> committed = await provider.UpdateAtomicAsync(
                context, s_nodeId,
                [
                    new DataValue(3, StatusCodes.Good, s_start),
                    new DataValue(4, StatusCodes.Good, s_start.AddMinutes(15))
                ],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(committed.TransactionRolledBack, Is.False);
            Assert.That(committed.OperationResults.ToArray(),
                Is.EqualTo([StatusCodes.GoodEntryInserted, StatusCodes.GoodEntryInserted]));
            HistorianPage<HistoricalDataValue> raw = await ReadRawAsync(provider, context).ConfigureAwait(false);
            DateTimeUtc[] expected = maximumSamples == 2
                ? [s_start.AddMinutes(15), newer.SourceTimestamp]
                : [s_start, s_start.AddMinutes(15), newer.SourceTimestamp];
            Assert.That(raw.Values.ToArray().Select(value => value.Value.SourceTimestamp), Is.EqualTo(expected));
        }

        private static async Task<long> MeasureUpdatesAsync(
            int archiveSize,
            HistoryUpdateType operation,
            bool capped)
        {
            using var provider = new InMemoryHistorianProvider(new InMemoryHistorianOptions
            {
                RawDataRetentionPeriod = TimeSpan.Zero,
                MaxSamplesPerNode = capped ? (uint)archiveSize : 0
            });
            HistorianOperationContext context = CreateContext();
            var seed = new DataValue[archiveSize];
            for (int i = 0; i < seed.Length; i++)
            {
                seed[i] = MakeValue(i, i);
            }
            ArrayOf<HistorianUpdateOutcome<DataValue>> seeded = await provider.InsertBatchAsync(
                context, [new HistorianDataBatch(s_nodeId, seed)], CancellationToken.None).ConfigureAwait(false);
            Assert.That(seeded[0].OperationResults, Has.Count.EqualTo(archiveSize));
            Assert.That(seeded[0].OperationResults.ToArray(), Has.All.EqualTo(StatusCodes.GoodEntryInserted));
            await UpdateOneAsync(provider, context, operation, archiveSize, 0).ConfigureAwait(false);
            int threadId = Environment.CurrentManagedThreadId;
            long before = ReadAllocatedBytes();
            for (int i = 1; i <= 8; i++)
            {
                await UpdateOneAsync(provider, context, operation, archiveSize, i).ConfigureAwait(false);
            }
            long allocated = ReadAllocatedBytes() - before;
            Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(threadId),
                "The synchronous in-memory operations must be measured on one thread.");
            return allocated;
        }

        private static async Task UpdateOneAsync(
            InMemoryHistorianProvider provider,
            HistorianOperationContext context,
            HistoryUpdateType operation,
            int archiveSize,
            int iteration)
        {
            int ticks = operation == HistoryUpdateType.Replace ? archiveSize - 1 : archiveSize + iteration;
            ArrayOf<DataValue> values = [MakeValue(ticks, iteration)];
            HistorianUpdateOutcome<DataValue> outcome = operation switch
            {
                HistoryUpdateType.Insert => await provider.InsertAtomicAsync(
                    context, s_nodeId, values, CancellationToken.None).ConfigureAwait(false),
                HistoryUpdateType.Replace => await provider.ReplaceAtomicAsync(
                    context, s_nodeId, values, CancellationToken.None).ConfigureAwait(false),
                HistoryUpdateType.Update => await provider.UpdateAtomicAsync(
                    context, s_nodeId, values, CancellationToken.None).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            Assert.That(outcome.TransactionRolledBack, Is.False);
            Assert.That(outcome.OperationResults, Has.Count.EqualTo(1));
            Assert.That(outcome.OperationResults[0], Is.EqualTo(operation == HistoryUpdateType.Replace
                ? StatusCodes.GoodEntryReplaced
                : StatusCodes.GoodEntryInserted));
        }

        private static long ReadAllocatedBytes()
        {
#if NETFRAMEWORK
            AppDomain.MonitoringIsEnabled = true;
            GC.Collect();
            return AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
#else
            return GC.GetAllocatedBytesForCurrentThread();
#endif
        }

        private static async Task<HistorianPage<HistoricalDataValue>> ReadRawAsync(
            InMemoryHistorianProvider provider,
            HistorianOperationContext context)
        {
            return await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = s_nodeId,
                    StartTime = DateTimeUtc.MinValue,
                    EndTime = DateTimeUtc.MaxValue,
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);
        }

        private static DataValue MakeValue(int ticks, int value)
        {
            return new DataValue(value, StatusCodes.Good, s_start.AddTicks(ticks));
        }

        private static HistorianOperationContext CreateContext()
        {
            var server = new Mock<IServerInternal>();
            server.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            server.SetupGet(s => s.ServerUris).Returns(new StringTable());
            server.SetupGet(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            server.SetupGet(s => s.Factory).Returns(EncodeableFactory.Create());
            server.SetupGet(s => s.Telemetry).Returns(Mock.Of<ITelemetryContext>());
            var operation = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryUpdate, RequestLifetime.None);
            return new HistorianOperationContext(
                new ServerSystemContext(server.Object, operation), operation, null, HistoryUpdateType.Insert);
        }

        private static readonly NodeId s_nodeId = new("review.transaction", 1);
        private static readonly DateTime s_start = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
