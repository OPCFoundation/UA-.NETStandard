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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// StructuredHistoryData behaviour of the in-memory historian:
    /// composite-key storage, update semantics, paging and the ordinary
    /// raw-history regression guard.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class InMemoryHistorianStructuredDataTests
    {
        [Test]
        public async Task TwoStructuresAtOneTimestampAreStoredSeparatelyAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "two.structures");
            HistorianOperationContext context = CreateContext();

            HistorianUpdateOutcome<DataValue> outcome = await provider.InsertStructuredDataAsync(
                context,
                nodeId,
                new[]
                {
                    MakePair("Temperature", Capture, 21.5),
                    MakePair("Pressure", Capture, 1.013)
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(outcome.OperationResults.Count, Is.EqualTo(2));
            Assert.That(
                outcome.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.GoodEntryInserted.Code));
            Assert.That(
                outcome.OperationResults[1].Code,
                Is.EqualTo(StatusCodes.GoodEntryInserted.Code));

            ArrayOf<HistoricalDataValue> values = await ReadAllRawAsync(provider, context, nodeId).ConfigureAwait(false);
            Assert.That(values, Has.Count.EqualTo(2));
            Assert.That(values[0].Value.SourceTimestamp, Is.EqualTo((DateTimeUtc)Capture));
            Assert.That(values[1].Value.SourceTimestamp, Is.EqualTo((DateTimeUtc)Capture));

            // Ordered by the canonical uniqueness key: "Pressure" < "Temperature".
            Assert.That(ReadName(values[0].Value), Is.EqualTo("Pressure"));
            Assert.That(ReadReading(values[0].Value), Is.EqualTo(1.013));
            Assert.That(ReadName(values[1].Value), Is.EqualTo("Temperature"));
            Assert.That(ReadReading(values[1].Value), Is.EqualTo(21.5));
        }

        [Test]
        public async Task RawPagingAcrossSameTimestampEntriesReturnsEveryEntryOnceAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "paged.structures");
            HistorianOperationContext context = CreateContext();

            await provider.InsertStructuredDataAsync(
                context,
                nodeId,
                new[]
                {
                    MakePair("Alpha", Capture, 1.0),
                    MakePair("Bravo", Capture, 2.0),
                    MakePair("Charlie", Capture, 3.0),
                    MakePair("Alpha", Capture.AddSeconds(1), 4.0),
                    MakePair("Bravo", Capture.AddSeconds(1), 5.0)
                },
                CancellationToken.None).ConfigureAwait(false);

            var seen = new List<string>();
            HistorianResumeToken token = default;
            int pages = 0;
            while (true)
            {
                HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                    context,
                    new HistorianRawReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = Capture,
                        EndTime = Capture.AddMinutes(1),
                        MaxValues = 2,
                        IsForward = true
                    },
                    token,
                    CancellationToken.None).ConfigureAwait(false);

                foreach (HistoricalDataValue value in page.Values)
                {
                    string timestamp = value.Value.SourceTimestamp.ToDateTime().ToString("O");
                    seen.Add($"{timestamp}|{ReadName(value.Value)}|{ReadReading(value.Value)}");
                }

                pages++;
                if (page.IsFinal)
                {
                    break;
                }
                token = page.NextToken;
                Assert.That(pages, Is.LessThan(10), "Pagination did not terminate.");
            }

            Assert.That(seen, Has.Count.EqualTo(5));
            Assert.That(seen, Is.Unique);
            Assert.That(pages, Is.EqualTo(3));
        }

        [Test]
        public async Task ReverseRawPagingAcrossSameTimestampEntriesReturnsEveryEntryOnceAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "reverse.structures");
            HistorianOperationContext context = CreateContext();

            await provider.InsertStructuredDataAsync(
                context,
                nodeId,
                new[]
                {
                    MakePair("Alpha", Capture.AddSeconds(1), 1.0),
                    MakePair("Bravo", Capture.AddSeconds(1), 2.0),
                    MakePair("Charlie", Capture.AddSeconds(1), 3.0)
                },
                CancellationToken.None).ConfigureAwait(false);

            var seen = new List<string>();
            HistorianResumeToken token = default;
            int pages = 0;
            while (true)
            {
                HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                    context,
                    new HistorianRawReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = Capture.AddMinutes(1),
                        EndTime = Capture,
                        MaxValues = 1,
                        IsForward = false
                    },
                    token,
                    CancellationToken.None).ConfigureAwait(false);

                foreach (HistoricalDataValue value in page.Values)
                {
                    seen.Add(ReadName(value.Value));
                }

                pages++;
                if (page.IsFinal)
                {
                    break;
                }
                token = page.NextToken;
                Assert.That(pages, Is.LessThan(10), "Pagination did not terminate.");
            }

            Assert.That(seen, Is.EqualTo(ReverseOrderedNames));
        }

        [Test]
        public async Task ModifiedHistoryKeepsEveryPriorVersionAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "modified.structures");
            HistorianOperationContext context = CreateContext();

            await provider.InsertStructuredDataAsync(
                context,
                nodeId,
                new[] { MakePair("Temperature", Capture, 1.0) },
                CancellationToken.None).ConfigureAwait(false);
            await provider.ReplaceStructuredDataAsync(
                context,
                nodeId,
                new[] { MakePair("Temperature", Capture, 2.0) },
                CancellationToken.None).ConfigureAwait(false);
            HistorianUpdateOutcome<DataValue> last = await provider.ReplaceStructuredDataAsync(
                context,
                nodeId,
                new[] { MakePair("Temperature", Capture, 3.0) },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                last.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.GoodEntryReplaced.Code));
            Assert.That(last.OldValues.Count, Is.EqualTo(1));
            Assert.That(ReadReading(last.OldValues[0]), Is.EqualTo(2.0));

            HistorianPage<ModifiedDataValue> modified = await provider.ReadModifiedAsync(
                context,
                new HistorianModifiedReadRequest
                {
                    NodeId = nodeId,
                    StartTime = Capture,
                    EndTime = Capture.AddMinutes(1),
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(modified.Values, Has.Count.EqualTo(2));
            var readings = new List<double>();
            foreach (ModifiedDataValue value in modified.Values)
            {
                readings.Add(ReadReading(value.Value));
                Assert.That(value.Info.UpdateType, Is.EqualTo(HistoryUpdateType.Replace));
            }
            Assert.That(readings, Is.EquivalentTo(PriorReadings));
        }

        [Test]
        public async Task ModifiedHistoryPagesSameTimestampEntriesWithoutLossAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "modified.paged");
            HistorianOperationContext context = CreateContext();

            await provider.InsertStructuredDataAsync(
                context,
                nodeId,
                new[]
                {
                    MakePair("Alpha", Capture, 1.0),
                    MakePair("Bravo", Capture, 2.0)
                },
                CancellationToken.None).ConfigureAwait(false);
            await provider.ReplaceStructuredDataAsync(
                context,
                nodeId,
                new[]
                {
                    MakePair("Alpha", Capture, 10.0),
                    MakePair("Bravo", Capture, 20.0)
                },
                CancellationToken.None).ConfigureAwait(false);

            var readings = new List<double>();
            HistorianResumeToken token = default;
            int pages = 0;
            while (true)
            {
                HistorianPage<ModifiedDataValue> page = await provider.ReadModifiedAsync(
                    context,
                    new HistorianModifiedReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = Capture,
                        EndTime = Capture.AddMinutes(1),
                        MaxValues = 1,
                        IsForward = true
                    },
                    token,
                    CancellationToken.None).ConfigureAwait(false);

                foreach (ModifiedDataValue value in page.Values)
                {
                    readings.Add(ReadReading(value.Value));
                }

                pages++;
                if (page.IsFinal)
                {
                    break;
                }
                token = page.NextToken;
                Assert.That(pages, Is.LessThan(10), "Pagination did not terminate.");
            }

            Assert.That(readings, Is.EquivalentTo(PriorReadings));
        }

        [Test]
        public async Task AtTimeReadReturnsEveryEntryAtTheTimestampAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "attime.structures");
            HistorianOperationContext context = CreateContext();

            await provider.InsertStructuredDataAsync(
                context,
                nodeId,
                new[]
                {
                    MakePair("Alpha", Capture, 1.0),
                    MakePair("Bravo", Capture, 2.0),
                    MakePair("Alpha", Capture.AddSeconds(5), 3.0)
                },
                CancellationToken.None).ConfigureAwait(false);

            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = Capture,
                    EndTime = Capture,
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(2));
            Assert.That(page.IsFinal, Is.True);
            Assert.That(ReadName(page.Values[0].Value), Is.EqualTo("Alpha"));
            Assert.That(ReadName(page.Values[1].Value), Is.EqualTo("Bravo"));
        }

        [Test]
        public async Task AtTimeReadPagesEntriesAtTheTimestampAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "attime.paged");
            HistorianOperationContext context = CreateContext();

            await provider.InsertStructuredDataAsync(
                context,
                nodeId,
                new[]
                {
                    MakePair("Alpha", Capture, 1.0),
                    MakePair("Bravo", Capture, 2.0)
                },
                CancellationToken.None).ConfigureAwait(false);

            var request = new HistorianRawReadRequest
            {
                NodeId = nodeId,
                StartTime = Capture,
                EndTime = Capture,
                MaxValues = 1,
                IsForward = true
            };

            HistorianPage<HistoricalDataValue> first = await provider.ReadRawAsync(
                context,
                request,
                default,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(first.Values, Has.Count.EqualTo(1));
            Assert.That(ReadName(first.Values[0].Value), Is.EqualTo("Alpha"));
            Assert.That(first.IsFinal, Is.False);

            HistorianPage<HistoricalDataValue> second = await provider.ReadRawAsync(
                context,
                request,
                first.NextToken,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(second.Values, Has.Count.EqualTo(1));
            Assert.That(ReadName(second.Values[0].Value), Is.EqualTo("Bravo"));
            Assert.That(second.IsFinal, Is.True);
        }

        [Test]
        public async Task InsertReplaceUpdateRemoveFollowCompositeKeyAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "cycle.structures");
            HistorianOperationContext context = CreateContext();

            HistorianUpdateOutcome<DataValue> inserted = await provider.InsertStructuredDataAsync(
                context,
                nodeId,
                new[] { MakePair("Temperature", Capture, 1.0) },
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(
                inserted.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.GoodEntryInserted.Code));

            HistorianUpdateOutcome<DataValue> duplicate = await provider.InsertStructuredDataAsync(
                context,
                nodeId,
                new[] { MakePair("Temperature", Capture, 9.0) },
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(
                duplicate.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.BadEntryExists.Code));

            HistorianUpdateOutcome<DataValue> replaced = await provider.ReplaceStructuredDataAsync(
                context,
                nodeId,
                new[] { MakePair("Temperature", Capture, 2.0) },
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(
                replaced.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.GoodEntryReplaced.Code));

            HistorianUpdateOutcome<DataValue> upserted = await provider.UpdateStructuredDataAsync(
                context,
                nodeId,
                new[]
                {
                    MakePair("Temperature", Capture, 3.0),
                    MakePair("Pressure", Capture, 4.0)
                },
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(
                upserted.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.GoodEntryReplaced.Code));
            Assert.That(
                upserted.OperationResults[1].Code,
                Is.EqualTo(StatusCodes.GoodEntryInserted.Code));

            HistorianUpdateOutcome<DataValue> removed = await provider.RemoveStructuredDataAsync(
                context,
                nodeId,
                new[] { MakePair("Temperature", Capture, 0.0) },
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(removed.OperationResults[0]), Is.True);
            Assert.That(removed.OldValues.Count, Is.EqualTo(1));
            Assert.That(ReadReading(removed.OldValues[0]), Is.EqualTo(3.0));

            HistorianUpdateOutcome<DataValue> removedAgain = await provider.RemoveStructuredDataAsync(
                context,
                nodeId,
                new[] { MakePair("Temperature", Capture, 0.0) },
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(
                removedAgain.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.BadNoEntryExists.Code));

            ArrayOf<HistoricalDataValue> remaining = await ReadAllRawAsync(provider, context, nodeId).ConfigureAwait(false);
            Assert.That(remaining, Has.Count.EqualTo(1));
            Assert.That(ReadName(remaining[0].Value), Is.EqualTo("Pressure"));

            HistorianPage<ModifiedDataValue> modified = await provider.ReadModifiedAsync(
                context,
                new HistorianModifiedReadRequest
                {
                    NodeId = nodeId,
                    StartTime = Capture,
                    EndTime = Capture.AddMinutes(1),
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(modified.Values, Has.Count.EqualTo(3));
            var updateTypes = new List<HistoryUpdateType>();
            foreach (ModifiedDataValue value in modified.Values)
            {
                updateTypes.Add(value.Info.UpdateType);
            }
            Assert.That(updateTypes, Is.EquivalentTo(ExpectedUpdateTypes));
        }

        [Test]
        public async Task ReplaceWithChangedUniquenessFieldReturnsBadNoEntryExistsAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "changedkey.structures");
            HistorianOperationContext context = CreateContext();

            await provider.InsertStructuredDataAsync(
                context,
                nodeId,
                new[] { MakePair("Temperature", Capture, 1.0) },
                CancellationToken.None).ConfigureAwait(false);

            HistorianUpdateOutcome<DataValue> outcome = await provider.ReplaceStructuredDataAsync(
                context,
                nodeId,
                new[] { MakePair("Humidity", Capture, 1.0) },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                outcome.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.BadNoEntryExists.Code));
            Assert.That(outcome.OldValues.Count, Is.Zero);

            // The stored entry is untouched: the client has to remove and insert.
            ArrayOf<HistoricalDataValue> values = await ReadAllRawAsync(provider, context, nodeId).ConfigureAwait(false);
            Assert.That(values, Has.Count.EqualTo(1));
            Assert.That(ReadName(values[0].Value), Is.EqualTo("Temperature"));
        }

        [Test]
        public async Task DuplicateKeysInOneBatchReportEntryExistsAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "duplicate.structures");
            HistorianOperationContext context = CreateContext();

            HistorianUpdateOutcome<DataValue> outcome = await provider.InsertStructuredDataAsync(
                context,
                nodeId,
                new[]
                {
                    MakePair("Temperature", Capture, 1.0),
                    MakePair("Temperature", Capture, 2.0)
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                outcome.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.GoodEntryInserted.Code));
            Assert.That(
                outcome.OperationResults[1].Code,
                Is.EqualTo(StatusCodes.BadEntryExists.Code));

            ArrayOf<HistoricalDataValue> values = await ReadAllRawAsync(provider, context, nodeId).ConfigureAwait(false);
            Assert.That(values, Has.Count.EqualTo(1));
            Assert.That(ReadReading(values[0].Value), Is.EqualTo(1.0));
        }

        [Test]
        public async Task ForeignStructureIsRejectedWithTypeMismatchAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "mismatch.structures");
            HistorianOperationContext context = CreateContext();

            HistorianUpdateOutcome<DataValue> outcome = await provider.InsertStructuredDataAsync(
                context,
                nodeId,
                new[]
                {
                    new DataValue(
                        new Variant(42.0),
                        StatusCodes.Good,
                        sourceTimestamp: Capture,
                        serverTimestamp: Capture)
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                outcome.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.BadTypeMismatch.Code));

            ArrayOf<HistoricalDataValue> values = await ReadAllRawAsync(provider, context, nodeId).ConfigureAwait(false);
            Assert.That(values, Is.Empty);
        }

        [Test]
        public async Task DeleteAtTimeRemovesEveryEntryAtTheTimestampAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "deleteattime.structures");
            HistorianOperationContext context = CreateContext();

            await provider.InsertStructuredDataAsync(
                context,
                nodeId,
                new[]
                {
                    MakePair("Alpha", Capture, 1.0),
                    MakePair("Bravo", Capture, 2.0),
                    MakePair("Alpha", Capture.AddSeconds(5), 3.0)
                },
                CancellationToken.None).ConfigureAwait(false);

            HistorianUpdateOutcome<DataValue> outcome = await provider.DeleteAtTimeAsync(
                context,
                nodeId,
                [(DateTimeUtc)Capture],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(outcome.OperationResults[0]), Is.True);
            Assert.That(outcome.OldValues.Count, Is.EqualTo(2));

            ArrayOf<HistoricalDataValue> values = await ReadAllRawAsync(provider, context, nodeId).ConfigureAwait(false);
            Assert.That(values, Has.Count.EqualTo(1));
            Assert.That(values[0].Value.SourceTimestamp, Is.EqualTo((DateTimeUtc)Capture.AddSeconds(5)));
        }

        [Test]
        public async Task BulkInsertKeepsEntriesWithTheSameTimestampAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "bulk.structures");
            HistorianOperationContext context = CreateContext();

            ArrayOf<HistorianDataBatch> batch =
            [
                new(
                    nodeId,
                [
                    MakePair("Alpha", Capture, 1.0),
                    MakePair("Bravo", Capture, 2.0),
                    MakePair("Alpha", Capture, 3.0)
                ])
            ];

            ArrayOf<HistorianUpdateOutcome<DataValue>> result =
                await provider.InsertBatchAsync(context, batch, CancellationToken.None).ConfigureAwait(false);

            HistorianUpdateOutcome<DataValue> outcome = result[0];
            Assert.That(
                outcome.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.GoodEntryInserted.Code));
            Assert.That(
                outcome.OperationResults[1].Code,
                Is.EqualTo(StatusCodes.GoodEntryInserted.Code));
            Assert.That(
                outcome.OperationResults[2].Code,
                Is.EqualTo(StatusCodes.BadEntryExists.Code));

            ArrayOf<HistoricalDataValue> values = await ReadAllRawAsync(provider, context, nodeId).ConfigureAwait(false);
            Assert.That(values, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task AtomicInsertRollsBackDuplicateCompositeKeysAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "atomic.structures");
            HistorianOperationContext context = CreateContext();

            HistorianUpdateOutcome<DataValue> outcome = await provider.InsertAtomicAsync(
                context,
                nodeId,
                [
                    MakePair("Alpha", Capture, 1.0),
                    MakePair("Alpha", Capture, 2.0)
                ],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(outcome.TransactionRolledBack, Is.True);
            Assert.That(
                outcome.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.BadTransactionFailed.Code));
            Assert.That(
                outcome.OperationResults[1].Code,
                Is.EqualTo(StatusCodes.BadEntryExists.Code));

            ArrayOf<HistoricalDataValue> values = await ReadAllRawAsync(provider, context, nodeId).ConfigureAwait(false);
            Assert.That(values, Is.Empty);
        }

        [Test]
        public async Task AtomicInsertRollsBackForeignStructureAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "atomic.mismatch");
            HistorianOperationContext context = CreateContext();

            HistorianUpdateOutcome<DataValue> outcome = await provider.InsertAtomicAsync(
                context,
                nodeId,
                [
                    MakePair("Alpha", Capture, 1.0),
                    new DataValue(
                        new Variant(7.0),
                        StatusCodes.Good,
                        sourceTimestamp: Capture,
                        serverTimestamp: Capture)
                ],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(outcome.TransactionRolledBack, Is.True);
            Assert.That(
                outcome.OperationResults[1].Code,
                Is.EqualTo(StatusCodes.BadTypeMismatch.Code));

            ArrayOf<HistoricalDataValue> values = await ReadAllRawAsync(provider, context, nodeId).ConfigureAwait(false);
            Assert.That(values, Is.Empty);
        }

        [Test]
        public async Task BoundsForStructuredNodeUseAdjacentEntriesAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "bounds.structures");
            HistorianOperationContext context = CreateContext();

            await provider.InsertStructuredDataAsync(
                context,
                nodeId,
                new[]
                {
                    MakePair("Alpha", Capture, 1.0),
                    MakePair("Bravo", Capture, 2.0),
                    MakePair("Alpha", Capture.AddSeconds(20), 3.0),
                    MakePair("Bravo", Capture.AddSeconds(20), 4.0)
                },
                CancellationToken.None).ConfigureAwait(false);

            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = Capture.AddSeconds(5),
                    EndTime = Capture.AddSeconds(15),
                    IsForward = true,
                    ReturnBounds = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);

            // The window is empty, so exactly one bound is returned on each
            // side: the last entry before the window and the first entry at
            // or after its end, both in composite-key order.
            Assert.That(page.Values, Has.Count.EqualTo(2));
            Assert.That(page.Values[0].IsBound, Is.True);
            Assert.That(page.Values[1].IsBound, Is.True);
            Assert.That(ReadName(page.Values[0].Value), Is.EqualTo("Bravo"));
            Assert.That(page.Values[0].Value.SourceTimestamp, Is.EqualTo((DateTimeUtc)Capture));
            Assert.That(ReadName(page.Values[1].Value), Is.EqualTo("Alpha"));
            Assert.That(
                page.Values[1].Value.SourceTimestamp,
                Is.EqualTo((DateTimeUtc)Capture.AddSeconds(20)));
        }

        [Test]
        public async Task RegisterStructuredAdvertisesStructuredCapabilitiesAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "caps.structures");

            HistorianNodeCapabilities capabilities = await provider.GetCapabilitiesAsync(
                nodeId,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(capabilities.InsertStructuredData, Is.True);
            Assert.That(capabilities.ReplaceStructuredData, Is.True);
            Assert.That(capabilities.UpdateStructuredData, Is.True);
            Assert.That(capabilities.DeleteStructuredData, Is.True);
            Assert.That(capabilities.ReadStructuredData, Is.True);
            Assert.That(capabilities.SupportsAnyStructuredUpdate, Is.True);
            Assert.That(await provider.IsHistorizingAsync(nodeId, CancellationToken.None).ConfigureAwait(false), Is.True);
        }

        [Test]
        public async Task GetKeySelectorReturnsRegisteredOrDefaultSelectorAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId structured = RegisterStructured(provider, "selector.structures");
            var ordinary = new NodeId("selector.raw", NamespaceIndex);
            provider.Register(ordinary);

            IHistorianStructuredDataKeySelector structuredSelector =
                await provider.GetKeySelectorAsync(structured, CancellationToken.None).ConfigureAwait(false);
            IHistorianStructuredDataKeySelector ordinarySelector =
                await provider.GetKeySelectorAsync(ordinary, CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                structuredSelector,
                Is.SameAs(KeyValuePairStructuredDataKeySelector.Instance));
            Assert.That(
                ordinarySelector,
                Is.SameAs(TimestampStructuredDataKeySelector.Instance));
        }

        [Test]
        public async Task ForgetDropsStructuredRegistrationAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "forget.structures");
            HistorianOperationContext context = CreateContext();

            await provider.InsertStructuredDataAsync(
                context,
                nodeId,
                new[] { MakePair("Alpha", Capture, 1.0) },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(provider.Forget(nodeId), Is.True);

            IHistorianStructuredDataKeySelector selector =
                await provider.GetKeySelectorAsync(nodeId, CancellationToken.None).ConfigureAwait(false);
            Assert.That(selector, Is.SameAs(TimestampStructuredDataKeySelector.Instance));
        }

        [Test]
        public void RegisterStructuredValidatesArguments()
        {
            using var provider = new InMemoryHistorianProvider();

            Assert.That(
                () => provider.RegisterStructured(
                    new NodeId("valid", NamespaceIndex),
                    null!),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => provider.RegisterStructured(
                    NodeId.Null,
                    KeyValuePairStructuredDataKeySelector.Instance),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task OrdinaryRawNodeKeepsTimestampOnlyIdentityAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("regression.raw", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();

            HistorianUpdateOutcome<DataValue> inserted = await provider.InsertAsync(
                context,
                nodeId,
                [
                    MakeScalar(Capture, 1.0),
                    MakeScalar(Capture.AddSeconds(10), 2.0),
                    MakeScalar(Capture, 3.0)
                ],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                inserted.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.GoodEntryInserted.Code));
            Assert.That(
                inserted.OperationResults[1].Code,
                Is.EqualTo(StatusCodes.GoodEntryInserted.Code));
            Assert.That(
                inserted.OperationResults[2].Code,
                Is.EqualTo(StatusCodes.BadEntryExists.Code));

            ArrayOf<HistoricalDataValue> values = await ReadAllRawAsync(provider, context, nodeId).ConfigureAwait(false);
            Assert.That(values, Has.Count.EqualTo(2));
            Assert.That(ReadScalar(values[0].Value), Is.EqualTo(1.0));
            Assert.That(ReadScalar(values[1].Value), Is.EqualTo(2.0));

            HistorianUpdateOutcome<DataValue> replaced = await provider.ReplaceAsync(
                context,
                nodeId,
                [MakeScalar(Capture, 4.0)],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(
                replaced.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.GoodEntryReplaced.Code));

            HistorianUpdateOutcome<DataValue> deleted = await provider.DeleteAtTimeAsync(
                context,
                nodeId,
                [(DateTimeUtc)Capture],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(deleted.OperationResults[0]), Is.True);

            values = await ReadAllRawAsync(provider, context, nodeId).ConfigureAwait(false);
            Assert.That(values, Has.Count.EqualTo(1));
            Assert.That(ReadScalar(values[0].Value), Is.EqualTo(2.0));
        }

        [Test]
        public async Task OrdinaryRawNodeStillPagesAndBoundsAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("regression.paging", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();

            var values = new List<DataValue>();
            for (int i = 0; i < 10; i++)
            {
                values.Add(MakeScalar(Capture.AddSeconds(i), i));
            }
            await provider.InsertAsync(context, nodeId, values, CancellationToken.None).ConfigureAwait(false);

            var returned = new List<double>();
            HistorianResumeToken token = default;
            int pages = 0;
            while (true)
            {
                HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                    context,
                    new HistorianRawReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = Capture,
                        EndTime = Capture.AddMinutes(1),
                        MaxValues = 3,
                        IsForward = true
                    },
                    token,
                    CancellationToken.None).ConfigureAwait(false);

                foreach (HistoricalDataValue value in page.Values)
                {
                    returned.Add(ReadScalar(value.Value));
                }

                pages++;
                if (page.IsFinal)
                {
                    break;
                }
                token = page.NextToken;
                Assert.That(pages, Is.LessThan(10), "Pagination did not terminate.");
            }

            Assert.That(returned, Has.Count.EqualTo(10));
            Assert.That(returned, Is.Ordered);
            Assert.That(returned[0], Is.Zero);
            Assert.That(returned[^1], Is.EqualTo(9.0));

            HistorianPage<HistoricalDataValue> bounded = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = Capture.AddMilliseconds(2500),
                    EndTime = Capture.AddMilliseconds(4500),
                    IsForward = true,
                    ReturnBounds = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(bounded.Values, Has.Count.EqualTo(4));
            Assert.That(bounded.Values[0].IsBound, Is.True);
            Assert.That(ReadScalar(bounded.Values[0].Value), Is.EqualTo(2.0));
            Assert.That(ReadScalar(bounded.Values[1].Value), Is.EqualTo(3.0));
            Assert.That(ReadScalar(bounded.Values[2].Value), Is.EqualTo(4.0));
            Assert.That(bounded.Values[3].IsBound, Is.True);
            Assert.That(ReadScalar(bounded.Values[3].Value), Is.EqualTo(5.0));
        }

        [Test]
        public void InsertAnnotationAtNewTimestampDoesNotThrow()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("annotation.insert", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();

            HistorianUpdateOutcome<Annotation> outcome = default;
            Assert.That(
                async () => outcome = await provider.InsertAnnotationsAsync(
                    context,
                    nodeId,
                    [MakeAnnotation(Capture, "first")],
                    CancellationToken.None).ConfigureAwait(false),
                Throws.Nothing,
                "Inserting an annotation at a new timestamp must not read the archive slot before it is written.");

            Assert.That(outcome, Is.Not.Null);
            Assert.That(
                outcome.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.GoodEntryInserted.Code));
            Assert.That(outcome.OldValues.Count, Is.Zero);
        }

        [Test]
        public async Task InsertAnnotationAtNewTimestampIsReadBackAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("annotation.readback", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();

            await provider.InsertAnnotationsAsync(
                context,
                nodeId,
                [MakeAnnotation(Capture, "first")],
                CancellationToken.None).ConfigureAwait(false);

            HistorianPage<Annotation> page = await provider.ReadAnnotationsAsync(
                context,
                new HistorianAnnotationReadRequest
                {
                    NodeId = nodeId,
                    StartTime = Capture.AddMinutes(-1),
                    EndTime = Capture.AddMinutes(1),
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(1));
            Assert.That(page.Values[0].Message, Is.EqualTo("first"));
            Assert.That(page.IsFinal, Is.True);
        }

        [Test]
        public async Task InsertAnnotationBatchKeepsNewEntryWhenOneKeyIsDuplicateAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("annotation.batch", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();

            HistorianUpdateOutcome<Annotation> outcome = await provider.InsertAnnotationsAsync(
                context,
                nodeId,
                [
                    MakeAnnotation(Capture, "first"),
                    MakeAnnotation(Capture, "duplicate"),
                    MakeAnnotation(Capture.AddSeconds(1), "second")
                ],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                outcome.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.GoodEntryInserted.Code));
            Assert.That(
                outcome.OperationResults[1].Code,
                Is.EqualTo(StatusCodes.BadEntryExists.Code));
            Assert.That(
                outcome.OperationResults[2].Code,
                Is.EqualTo(StatusCodes.GoodEntryInserted.Code));
            Assert.That(outcome.OldValues.Count, Is.Zero);
        }

        [Test]
        public async Task ReplaceAnnotationReturnsPriorAnnotationAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("annotation.replace", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();

            await provider.InsertAnnotationsAsync(
                context,
                nodeId,
                [MakeAnnotation(Capture, "first")],
                CancellationToken.None).ConfigureAwait(false);

            HistorianUpdateOutcome<Annotation> missing = await provider.ReplaceAnnotationsAsync(
                context,
                nodeId,
                [MakeAnnotation(Capture.AddSeconds(30), "absent")],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(
                missing.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.BadNoEntryExists.Code));
            Assert.That(missing.OldValues.Count, Is.Zero);

            HistorianUpdateOutcome<Annotation> replaced = await provider.ReplaceAnnotationsAsync(
                context,
                nodeId,
                [MakeAnnotation(Capture, "second")],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                replaced.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.GoodEntryReplaced.Code));
            Assert.That(replaced.OldValues.Count, Is.EqualTo(1));
            Assert.That(replaced.OldValues[0].Message, Is.EqualTo("first"));
        }

        [Test]
        public void UpdateAnnotationAtNewTimestampDoesNotThrow()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("annotation.update", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();

            HistorianUpdateOutcome<Annotation> outcome = default;
            Assert.That(
                async () => outcome = await provider.UpdateAnnotationsAsync(
                    context,
                    nodeId,
                    [MakeAnnotation(Capture, "upsert")],
                    CancellationToken.None).ConfigureAwait(false),
                Throws.Nothing);

            Assert.That(outcome, Is.Not.Null);
            Assert.That(
                outcome.OperationResults[0].Code,
                Is.EqualTo(StatusCodes.GoodEntryInserted.Code));
            Assert.That(outcome.OldValues.Count, Is.Zero);
        }

        [Test]
        public async Task StructuredRegistrationAdvertisesStructuredProfileOnlyAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            NodeId nodeId = RegisterStructured(provider, "advertised.structures");

            HistorianNodeCapabilities rollup = await provider.GetCapabilitiesAsync(
                NodeId.Null,
                CancellationToken.None).ConfigureAwait(false);
            ArrayOf<HistoricalAccessProfileDescriptor> profiles =
                HistorianProfileCatalog.GetSupportedProfiles(provider, rollup);

            var families = new List<HistoricalAccessProfileFamily>();
            foreach (HistoricalAccessProfileDescriptor profile in profiles.Span)
            {
                families.Add(profile.Family);
            }

            Assert.That(nodeId.IsNull, Is.False);
            Assert.That(families, Does.Contain(HistoricalAccessProfileFamily.Structured));
            // A structured-only registration must not claim raw update,
            // annotation or event facets.
            Assert.That(families, Does.Not.Contain(HistoricalAccessProfileFamily.RawUpdates));
            Assert.That(families, Does.Not.Contain(HistoricalAccessProfileFamily.Annotation));
            Assert.That(families, Does.Not.Contain(HistoricalAccessProfileFamily.Events));
            Assert.That(families, Does.Not.Contain(HistoricalAccessProfileFamily.Aggregate));
        }

        [Test]
        public async Task DataOnlyRegistrationDoesNotAdvertiseStructuredProfileAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            provider.Register(
                new NodeId("advertised.raw", NamespaceIndex),
                HistorianNodeCapabilities.DataReadWrite);

            HistorianNodeCapabilities rollup = await provider.GetCapabilitiesAsync(
                NodeId.Null,
                CancellationToken.None).ConfigureAwait(false);
            ArrayOf<HistoricalAccessProfileDescriptor> profiles =
                HistorianProfileCatalog.GetSupportedProfiles(provider, rollup);

            var families = new List<HistoricalAccessProfileFamily>();
            foreach (HistoricalAccessProfileDescriptor profile in profiles.Span)
            {
                families.Add(profile.Family);
            }

            // The provider implements IHistorianStructuredDataProvider, so the
            // structured claim is gated by the capability flags alone here.
            Assert.That(provider, Is.InstanceOf<IHistorianStructuredDataProvider>());
            Assert.That(families, Does.Not.Contain(HistoricalAccessProfileFamily.Structured));
            Assert.That(families, Does.Contain(HistoricalAccessProfileFamily.RawUpdates));
        }

        [Test]
        public async Task ProviderWithoutRegisteredNodesAdvertisesNoProfilesAsync()
        {
            using var provider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions
                {
                    DefaultCapabilities = HistorianNodeCapabilities.ReadWrite
                });

            HistorianNodeCapabilities rollup = await provider.GetCapabilitiesAsync(
                NodeId.Null,
                CancellationToken.None).ConfigureAwait(false);
            ArrayOf<HistoricalAccessProfileDescriptor> profiles =
                HistorianProfileCatalog.GetSupportedProfiles(provider, rollup);

            Assert.That(rollup.ReadStructuredData, Is.False);
            Assert.That(rollup.SupportsAnyStructuredUpdate, Is.False);
            Assert.That(profiles.Count, Is.Zero);
        }

        private static Annotation MakeAnnotation(DateTime annotationTime, string message)
        {
            return new Annotation
            {
                Message = message,
                UserName = "tester",
                AnnotationTime = annotationTime
            };
        }

        private static NodeId RegisterStructured(InMemoryHistorianProvider provider, string name)
        {
            var nodeId = new NodeId(name, NamespaceIndex);
            provider.RegisterStructured(nodeId, KeyValuePairStructuredDataKeySelector.Instance);
            return nodeId;
        }

        private static async Task<ArrayOf<HistoricalDataValue>> ReadAllRawAsync(
            InMemoryHistorianProvider provider,
            HistorianOperationContext context,
            NodeId nodeId)
        {
            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = Capture.AddMinutes(-1),
                    EndTime = Capture.AddMinutes(10),
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(page.IsFinal, Is.True);
            return page.Values;
        }

        private static DataValue MakePair(string name, DateTime sourceTimestamp, double reading)
        {
            var pair = new KeyValuePair
            {
                Key = new QualifiedName(name, NamespaceIndex),
                Value = new Variant(reading)
            };
            return new DataValue(
                new Variant(new ExtensionObject(pair)),
                StatusCodes.Good,
                sourceTimestamp: sourceTimestamp,
                serverTimestamp: sourceTimestamp);
        }

        private static DataValue MakeScalar(DateTime sourceTimestamp, double value)
        {
            return new DataValue(
                new Variant(value),
                StatusCodes.Good,
                sourceTimestamp: sourceTimestamp,
                serverTimestamp: sourceTimestamp);
        }

        private static string ReadName(DataValue value)
        {
            return ReadPair(value).Key.Name ?? string.Empty;
        }

        private static double ReadReading(DataValue value)
        {
            KeyValuePair pair = ReadPair(value);
            Assert.That(pair.Value.TryGetValue(out double reading), Is.True);
            return reading;
        }

        private static double ReadScalar(DataValue value)
        {
            Assert.That(value.WrappedValue.TryGetValue(out double reading), Is.True);
            return reading;
        }

        private static KeyValuePair ReadPair(DataValue value)
        {
            Assert.That(value.WrappedValue.TryGetValue(out ExtensionObject extension), Is.True);
            Assert.That(extension.TryGetValue(out IEncodeable body), Is.True);
            Assert.That(body, Is.InstanceOf<KeyValuePair>());
            return (KeyValuePair)body;
        }

        private static HistorianOperationContext CreateContext()
        {
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockServer = new Mock<IServerInternal>();
            mockServer.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

            var opContext = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryUpdate, RequestLifetime.None);
            var systemContext = new ServerSystemContext(mockServer.Object, opContext);
            return new HistorianOperationContext(
                systemContext, opContext, null, HistoryUpdateType.Insert);
        }

        private const ushort NamespaceIndex = 1;

        private static readonly DateTime Capture = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private static readonly string[] ReverseOrderedNames = ["Charlie", "Bravo", "Alpha"];

        private static readonly double[] PriorReadings = [1.0, 2.0];

        private static readonly HistoryUpdateType[] ExpectedUpdateTypes =
        [
            HistoryUpdateType.Replace,
            HistoryUpdateType.Update,
            HistoryUpdateType.Delete
        ];
    }
}
