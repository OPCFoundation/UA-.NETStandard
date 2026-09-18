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
    /// Verifies wall-clock retention and the bounded prior-value index exposed by raw history.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public sealed class HistorianReviewRetentionTests
    {
        [Test]
        public void DefaultModifiedHistoryHasFiniteCapacity()
        {
            Assert.That(new InMemoryHistorianOptions().MaxModifiedEntriesPerNode, Is.EqualTo(10_000));
        }

        [Test]
        public async Task FluentProviderUsesInjectedServerClockAsync()
        {
            var clock = new FakeTimeProvider(s_now);
            var namespaces = new NamespaceTable();
            var server = new Mock<IServerInternal>();
            server.As<IHistorianRegistryProvider>().SetupGet(s => s.HistorianRegistry)
                .Returns(new HistorianProviderRegistry(namespaces));
            server.As<ITimeProviderProvider>().SetupGet(s => s.TimeProvider).Returns(clock);
            await using var builder = new HistorianBuilder(server.Object);
            using InMemoryHistorianProvider provider = builder.UseInMemory();
            HistorianOperationContext context = CreateContext();
            HistorianUpdateOutcome<DataValue> outcome = await provider.InsertAsync(
                context, s_nodeId, [MakeValue(s_now, 1)], CancellationToken.None).ConfigureAwait(false);
            Assert.That(outcome.OperationResults[0], Is.EqualTo(StatusCodes.GoodEntryInserted));
            clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromTicks(1));
            Assert.That((await ReadRawAsync(provider, context).ConfigureAwait(false)).Values, Is.Empty);
        }

        [Test]
        public async Task DefaultModifiedHistoryEvictsOldestEntriesAtCapacityAsync()
        {
            using var provider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions(), new FakeTimeProvider(s_now));
            HistorianOperationContext context = CreateContext();
            var input = new List<DataValue>();
            for (int i = 0; i < 10_002; i++)
            {
                input.Add(MakeValue(s_now.AddTicks(i), i));
            }
            await provider.InsertAsync(context, s_nodeId, input.ToArrayOf(), CancellationToken.None)
                .ConfigureAwait(false);
            List<ModifiedDataValue> modified = await ReadModifiedAsync(provider, context).ConfigureAwait(false);
            Assert.That(modified, Has.Count.EqualTo(10_000));
            Assert.That(modified[0].Value.SourceTimestamp, Is.EqualTo((DateTimeUtc)s_now.AddTicks(2)));
            Assert.That(modified[^1].Value.SourceTimestamp, Is.EqualTo((DateTimeUtc)s_now.AddTicks(10_001)));
        }

        [Test]
        public async Task ExpiredBackfillIsRejectedAtExactWallClockBoundaryAsync(
            [Values("insert", "update", "bulk", "atomicInsert", "atomicUpdate")] string operation,
            [Values(-1, 0, 1)] int ticksFromBoundary)
        {
            using var provider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions(), new FakeTimeProvider(s_now));
            HistorianOperationContext context = CreateContext();
            DataValue value = MakeValue(s_now.AddHours(-1).AddTicks(ticksFromBoundary), 1);
            HistorianUpdateOutcome<DataValue> outcome;
            switch (operation)
            {
                case "bulk":
                    ArrayOf<HistorianUpdateOutcome<DataValue>> batch = await provider.InsertBatchAsync(
                        context, [new HistorianDataBatch(s_nodeId, [value])], CancellationToken.None)
                        .ConfigureAwait(false);
                    outcome = batch[0];
                    break;
                case "update":
                    outcome = await provider.UpdateAsync(context, s_nodeId, [value], CancellationToken.None)
                        .ConfigureAwait(false);
                    break;
                case "atomicInsert":
                    outcome = await provider.InsertAtomicAsync(context, s_nodeId, [value], CancellationToken.None)
                        .ConfigureAwait(false);
                    break;
                case "atomicUpdate":
                    outcome = await provider.UpdateAtomicAsync(context, s_nodeId, [value], CancellationToken.None)
                        .ConfigureAwait(false);
                    break;
                default:
                    outcome = await provider.InsertAsync(context, s_nodeId, [value], CancellationToken.None)
                        .ConfigureAwait(false);
                    break;
            }
            bool accepted = ticksFromBoundary >= 0;
            Assert.That(outcome.OperationResults[0],
                Is.EqualTo(accepted ? StatusCodes.GoodEntryInserted : StatusCodes.BadOutOfRange));
            HistorianPage<HistoricalDataValue> raw = await ReadRawAsync(provider, context).ConfigureAwait(false);
            Assert.That(raw.Values, Has.Count.EqualTo(accepted ? 1 : 0));
            List<ModifiedDataValue> modified = await ReadModifiedAsync(provider, context).ConfigureAwait(false);
            Assert.That(modified, Has.Count.EqualTo(accepted && operation != "bulk" ? 1 : 0),
                "A rejected backfill must not create an INSERT modification.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FutureTimestampsCannotEvictPresentHistoryAsync(bool bulk)
        {
            var clock = new FakeTimeProvider(s_now);
            using var provider = new InMemoryHistorianProvider(new InMemoryHistorianOptions(), clock);
            HistorianOperationContext context = CreateContext();
            ArrayOf<DataValue> input =
            [
                MakeValue(s_now.AddMinutes(-30), 1),
                MakeValue(s_now.AddYears(1), 2),
                MakeValue(s_now.AddMinutes(-15), 3)
            ];
            HistorianUpdateOutcome<DataValue> outcome;
            if (bulk)
            {
                ArrayOf<HistorianUpdateOutcome<DataValue>> outcomes = await provider.InsertBatchAsync(
                    context, [new HistorianDataBatch(s_nodeId, input)], CancellationToken.None).ConfigureAwait(false);
                outcome = outcomes[0];
            }
            else
            {
                outcome = await provider.InsertAsync(context, s_nodeId, input, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            Assert.That(outcome.OperationResults.ToArray(), Has.All.EqualTo(StatusCodes.GoodEntryInserted));
            HistorianPage<HistoricalDataValue> raw = await ReadRawAsync(provider, context).ConfigureAwait(false);
            Assert.That(raw.Values.ToArray().Select(v => v.Value.SourceTimestamp), Is.EqualTo(new[]
            {
                (DateTimeUtc)s_now.AddMinutes(-30),
                (DateTimeUtc)s_now.AddMinutes(-15),
                (DateTimeUtc)s_now.AddYears(1)
            }));
            clock.Advance(TimeSpan.FromMinutes(31));
            raw = await ReadRawAsync(provider, context).ConfigureAwait(false);
            Assert.That(raw.Values, Has.Count.EqualTo(2));
            Assert.That(raw.Values[0].Value.SourceTimestamp, Is.EqualTo((DateTimeUtc)s_now.AddMinutes(-15)));
        }

        [Test]
        public async Task RetentionIsIndependentOfTimestampInsertionOrderAsync()
        {
            int[] offsets = [-61, -60, -59, 0, 1, 100_000];
            for (int permutation = 0; permutation < 12; permutation++)
            {
                using var provider = new InMemoryHistorianProvider(
                    new InMemoryHistorianOptions(), new FakeTimeProvider(s_now));
                HistorianOperationContext context = CreateContext();
                for (int i = 0; i < offsets.Length; i++)
                {
                    int index = (permutation / 2 + (permutation % 2 == 0 ? i : offsets.Length - i - 1)) %
                        offsets.Length;
                    int offset = offsets[index];
                    HistorianUpdateOutcome<DataValue> outcome = await provider.InsertAsync(
                        context, s_nodeId, [MakeValue(s_now.AddMinutes(offset), offset)], CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(outcome.OperationResults[0],
                        Is.EqualTo(offset == -61 ? StatusCodes.BadOutOfRange : StatusCodes.GoodEntryInserted));
                }
                HistorianPage<HistoricalDataValue> raw = await ReadRawAsync(provider, context).ConfigureAwait(false);
                DateTimeUtc[] expected =
                [
                    s_now.AddMinutes(-60), s_now.AddMinutes(-59), s_now, s_now.AddMinutes(1), s_now.AddMinutes(100_000)
                ];
                Assert.That(raw.Values.ToArray().Select(v => v.Value.SourceTimestamp), Is.EqualTo(expected));
            }
        }

        [Test]
        public async Task RawExtraDataReflectsOnlyRetainedPriorVersionsAsync()
        {
            using var provider = new InMemoryHistorianProvider(new InMemoryHistorianOptions
            {
                RawDataRetentionPeriod = TimeSpan.Zero,
                MaxModifiedEntriesPerNode = 2
            });
            HistorianOperationContext context = CreateContext();
            await provider.InsertAsync(context, s_nodeId, [MakeValue(s_now, 1)], CancellationToken.None)
                .ConfigureAwait(false);
            HistorianPage<HistoricalDataValue> raw = await ReadRawAsync(provider, context).ConfigureAwait(false);
            Assert.That(raw.Values[0].Value.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Raw));
            await provider.ReplaceAsync(context, s_nodeId, [MakeValue(s_now, 2)], CancellationToken.None)
                .ConfigureAwait(false);
            raw = await ReadRawAsync(provider, context).ConfigureAwait(false);
            Assert.That(raw.Values[0].Value.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.ExtraData));
            await provider.InsertAsync(
                context, s_nodeId, [MakeValue(s_now.AddSeconds(1), 3)], CancellationToken.None).ConfigureAwait(false);
            raw = await ReadRawAsync(provider, context).ConfigureAwait(false);
            Assert.That(raw.Values[0].Value.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.ExtraData));
            Assert.That(raw.Values[1].Value.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Raw));
            await provider.InsertAsync(
                context, s_nodeId, [MakeValue(s_now.AddSeconds(2), 4)], CancellationToken.None).ConfigureAwait(false);
            raw = await ReadRawAsync(provider, context).ConfigureAwait(false);
            Assert.That(raw.Values.ToArray().Select(v => v.Value.StatusCode.AggregateBits),
                Has.All.EqualTo(AggregateBits.Raw));
            Assert.That(await ReadModifiedAsync(provider, context).ConfigureAwait(false), Has.Count.EqualTo(2));
        }

        [Test]
        public async Task DeletingModifiedHistoryRemovesOnlyItsExtraDataReferencesAsync()
        {
            using var provider = new InMemoryHistorianProvider(new InMemoryHistorianOptions
            {
                RawDataRetentionPeriod = TimeSpan.Zero
            });
            HistorianOperationContext context = CreateContext();
            ArrayOf<DataValue> values = [MakeValue(s_now, 1), MakeValue(s_now.AddSeconds(1), 2)];
            await provider.InsertAsync(context, s_nodeId, values, CancellationToken.None).ConfigureAwait(false);
            await provider.ReplaceAsync(context, s_nodeId, values, CancellationToken.None).ConfigureAwait(false);
            await provider.DeleteRawAsync(
                context, s_nodeId, s_now, s_now.AddTicks(1), true, CancellationToken.None).ConfigureAwait(false);
            HistorianPage<HistoricalDataValue> raw = await ReadRawAsync(provider, context).ConfigureAwait(false);
            Assert.That(raw.Values[0].Value.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Raw));
            Assert.That(raw.Values[1].Value.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.ExtraData));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task ExtraDataIsConsistentForExactValuesAndBoundsAsync(bool reverse, bool annotation)
        {
            using var provider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions(), new FakeTimeProvider(s_now));
            HistorianOperationContext context = CreateContext();
            DataValue first = MakeValue(s_now.AddSeconds(2), 2);
            await provider.InsertAsync(
                context, s_nodeId, [first, MakeValue(s_now.AddSeconds(8), 8)], CancellationToken.None)
                .ConfigureAwait(false);
            if (annotation)
            {
                await provider.InsertAnnotationsWithTimestampsAsync(
                    context, s_nodeId, [MakeAnnotation(2, 10)], CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await provider.ReplaceAsync(context, s_nodeId, [first], CancellationToken.None).ConfigureAwait(false);
            }
            HistorianPage<HistoricalDataValue> exact = await provider.ReadRawAsync(context,
                new HistorianRawReadRequest
                {
                    NodeId = s_nodeId,
                    StartTime = s_now.AddSeconds(2),
                    EndTime = s_now.AddSeconds(2),
                    IsForward = !reverse,
                    MaxValues = 1
                }, default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(exact.Values, Has.Count.EqualTo(1));
            Assert.That(exact.Values[0].Value.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.ExtraData));
            HistorianPage<HistoricalDataValue> bounds = await provider.ReadRawAsync(context,
                new HistorianRawReadRequest
                {
                    NodeId = s_nodeId,
                    StartTime = s_now.AddSeconds(3),
                    EndTime = s_now.AddSeconds(7),
                    IsForward = !reverse,
                    ReturnBounds = true
                }, default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(bounds.Values, Has.Count.EqualTo(2));
            Assert.That(bounds.Values[reverse ? 1 : 0].Value.StatusCode.AggregateBits,
                Is.EqualTo(AggregateBits.ExtraData));
            Assert.That(bounds.Values[reverse ? 0 : 1].Value.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Raw));
        }

        [Test]
        public async Task AnnotationExtraDataTracksSiblingDeletionAndCapacityEvictionAsync()
        {
            using var provider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { MaxAnnotationsPerNode = 2 }, new FakeTimeProvider(s_now));
            HistorianOperationContext context = CreateContext();
            await provider.InsertAsync(context, s_nodeId,
                [MakeValue(s_now, 0), MakeValue(s_now.AddSeconds(1), 1), MakeValue(s_now.AddSeconds(2), 2)],
                CancellationToken.None).ConfigureAwait(false);
            await provider.InsertAnnotationsWithTimestampsAsync(
                context, s_nodeId, [MakeAnnotation(0, 10), MakeAnnotation(0, 11)], CancellationToken.None)
                .ConfigureAwait(false);
            await provider.DeleteAnnotationsWithTimestampsAsync(
                context, s_nodeId, [MakeAnnotation(0, 10)], CancellationToken.None).ConfigureAwait(false);
            HistorianPage<HistoricalDataValue> raw = await ReadRawAsync(provider, context).ConfigureAwait(false);
            Assert.That(raw.Values[0].Value.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.ExtraData));
            await provider.InsertAnnotationsWithTimestampsAsync(
                context, s_nodeId, [MakeAnnotation(1, 12), MakeAnnotation(2, 13)], CancellationToken.None)
                .ConfigureAwait(false);
            raw = await ReadRawAsync(provider, context).ConfigureAwait(false);
            Assert.That(raw.Values[0].Value.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Raw));
            Assert.That(raw.Values[1].Value.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.ExtraData));
            Assert.That(raw.Values[2].Value.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.ExtraData));
        }

        [Test]
        public async Task StructuredModificationIndexDoesNotMarkSiblingKeysAsync()
        {
            using var provider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions(), new FakeTimeProvider(s_now));
            provider.RegisterStructured(s_nodeId, KeyValuePairStructuredDataKeySelector.Instance);
            HistorianOperationContext context = CreateContext();
            DataValue first = MakeStructuredValue("A", 1);
            await provider.InsertStructuredDataAsync(
                context, s_nodeId, [first, MakeStructuredValue("B", 2)], CancellationToken.None).ConfigureAwait(false);
            await provider.ReplaceStructuredDataAsync(
                context, s_nodeId, [first], CancellationToken.None).ConfigureAwait(false);
            HistorianPage<HistoricalDataValue> raw = await ReadRawAsync(provider, context).ConfigureAwait(false);
            Assert.That(raw.Values, Has.Count.EqualTo(2));
            Assert.That(raw.Values[0].Value.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.ExtraData));
            Assert.That(raw.Values[1].Value.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Raw));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CompositeBackfillThatWouldImmediatelyEvictIsRejectedAsync(bool atomic)
        {
            using var provider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { MaxSamplesPerNode = 1 }, new FakeTimeProvider(s_now));
            provider.RegisterStructured(s_nodeId, KeyValuePairStructuredDataKeySelector.Instance);
            HistorianOperationContext context = CreateContext();
            if (atomic)
            {
                HistorianUpdateOutcome<DataValue> outcome = await provider.InsertAtomicAsync(
                    context, s_nodeId, [MakeStructuredValue("B", 2), MakeStructuredValue("A", 1)],
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(outcome.TransactionRolledBack, Is.True);
                Assert.That(outcome.OperationResults[0], Is.EqualTo(StatusCodes.BadTransactionFailed));
                Assert.That(outcome.OperationResults[1], Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That((await ReadRawAsync(provider, context).ConfigureAwait(false)).Values, Is.Empty);
                Assert.That(await ReadModifiedAsync(provider, context).ConfigureAwait(false), Is.Empty);
            }
            else
            {
                await provider.InsertStructuredDataAsync(
                    context, s_nodeId, [MakeStructuredValue("B", 2)], CancellationToken.None).ConfigureAwait(false);
                HistorianUpdateOutcome<DataValue> outcome = await provider.InsertStructuredDataAsync(
                    context, s_nodeId, [MakeStructuredValue("A", 1)], CancellationToken.None).ConfigureAwait(false);
                Assert.That(outcome.OperationResults[0], Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That((await ReadRawAsync(provider, context).ConfigureAwait(false)).Values, Has.Count.EqualTo(1));
                Assert.That(await ReadModifiedAsync(provider, context).ConfigureAwait(false), Has.Count.EqualTo(1));
            }
        }

        private static DataValue MakeStructuredValue(string key, int value)
        {
            return new DataValue(new Variant(new ExtensionObject(new KeyValuePair
            {
                Key = new QualifiedName(key),
                Value = new Variant(value)
            })), StatusCodes.Good, s_now);
        }

        private static HistorianAnnotation MakeAnnotation(int sourceSeconds, int annotationSeconds)
        {
            return new HistorianAnnotation(s_now.AddSeconds(sourceSeconds), new Annotation
            {
                AnnotationTime = s_now.AddSeconds(annotationSeconds),
                Message = "review"
            });
        }

        private static async Task<HistorianPage<HistoricalDataValue>> ReadRawAsync(
            InMemoryHistorianProvider provider,
            HistorianOperationContext context)
        {
            return await provider.ReadRawAsync(context, new HistorianRawReadRequest
            {
                NodeId = s_nodeId,
                StartTime = DateTimeUtc.MinValue,
                EndTime = DateTimeUtc.MaxValue,
                IsForward = true
            }, default, CancellationToken.None).ConfigureAwait(false);
        }

        private static async Task<List<ModifiedDataValue>> ReadModifiedAsync(
            InMemoryHistorianProvider provider,
            HistorianOperationContext context)
        {
            var values = new List<ModifiedDataValue>();
            HistorianResumeToken token = default;
            do
            {
                HistorianPage<ModifiedDataValue> page = await provider.ReadModifiedAsync(context,
                    new HistorianModifiedReadRequest
                    {
                        NodeId = s_nodeId,
                        StartTime = DateTimeUtc.MinValue,
                        EndTime = DateTimeUtc.MaxValue,
                        IsForward = true
                    }, token, CancellationToken.None).ConfigureAwait(false);
                values.AddRange(page.Values.ToArray());
                token = page.NextToken;
            }
            while (!token.IsEmpty);
            return values;
        }

        private static DataValue MakeValue(DateTime timestamp, int value)
        {
            return new DataValue(value, StatusCodes.Good, timestamp);
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

        private static readonly NodeId s_nodeId = new("review.retention", 1);
        private static readonly DateTime s_now = new(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    }
}
