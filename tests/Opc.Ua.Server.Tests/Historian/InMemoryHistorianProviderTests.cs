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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Verifies in-memory historical values, modifications, annotations, paging, and time-bound semantics.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class InMemoryHistorianProviderTests
    {
        private const ushort NamespaceIndex = 1;

        /// <summary>
        /// Verifies that inserted historical values are returned by raw-history reads.
        /// </summary>
        [Test]
        public async Task InsertAsyncStoresValuesAndRawReadReturnsThemAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("test.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            var values = new List<DataValue>
            {
                MakeValue(BaseTime.AddSeconds(10), 1.0),
                MakeValue(BaseTime.AddSeconds(20), 2.0),
                MakeValue(BaseTime.AddSeconds(30), 3.0)
            };

            HistorianUpdateOutcome<DataValue> insertOutcome = await provider.InsertAsync(
                context, nodeId, values, CancellationToken.None).ConfigureAwait(false);
            Assert.That(insertOutcome.OperationResults, Has.Count.EqualTo(3));
            foreach (StatusCode sc in insertOutcome.OperationResults)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }

            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddMinutes(1),
                    MaxValues = 0,
                    IsForward = true,
                    ReturnBounds = false
                },
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(3));
            Assert.That(page.IsFinal, Is.True);
            Assert.That(
                Convert.ToDouble(page.Values[0].Value.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.EqualTo(1.0));
            Assert.That(
                Convert.ToDouble(page.Values[2].Value.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.EqualTo(3.0));
        }

        /// <summary>
        /// Verifies that insertion rejects duplicate source timestamps.
        /// </summary>
        [Test]
        public async Task InsertRejectsDuplicateSourceTimestampAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("dup.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            DateTime when = BaseTime.AddSeconds(10);
            HistorianUpdateOutcome<DataValue> first = await provider.InsertAsync(
                context, nodeId, [MakeValue(when, 1.0)], CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(first.OperationResults[0]), Is.True);

            HistorianUpdateOutcome<DataValue> second = await provider.InsertAsync(
                context, nodeId, [MakeValue(when, 1.5)], CancellationToken.None).ConfigureAwait(false);
            Assert.That(second.OperationResults[0].Code, Is.EqualTo(StatusCodes.BadEntryExists.Code));
        }

        /// <summary>
        /// Verifies that replacement fails when no historical entry exists.
        /// </summary>
        [Test]
        public async Task ReplaceFailsWhenNoEntryExistsAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("rep.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            HistorianUpdateOutcome<DataValue> outcome = await provider.ReplaceAsync(
                context,
                nodeId,
                [MakeValue(BaseTime.AddSeconds(15), 42.0)],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(outcome.OperationResults[0].Code, Is.EqualTo(StatusCodes.BadNoEntryExists.Code));
        }

        /// <summary>
        /// Verifies that update inserts or replaces values and records modification metadata.
        /// </summary>
        [Test]
        public async Task UpdateUpsertsAndLogsModificationAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("up.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            DateTime when = BaseTime.AddSeconds(10);

            await provider.InsertAsync(
                context, nodeId, [MakeValue(when, 1.0)], CancellationToken.None).ConfigureAwait(false);
            await provider.UpdateAsync(
                context, nodeId, [MakeValue(when, 2.0)], CancellationToken.None).ConfigureAwait(false);

            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddMinutes(1),
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(page.Values, Has.Count.EqualTo(1));
            Assert.That(
                Convert.ToDouble(page.Values[0].Value.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.EqualTo(2.0));

            HistorianPage<ModifiedDataValue> mod = await provider.ReadModifiedAsync(
                context,
                new HistorianModifiedReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddMinutes(1),
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(mod.Values, Has.Count.EqualTo(1));
            Assert.That(mod.Values[0].Info.UpdateType, Is.EqualTo(HistoryUpdateType.Update));
        }

        /// <summary>
        /// Verifies that at-time deletion removes the matching entries.
        /// </summary>
        [Test]
        public async Task DeleteAtTimeRemovesEntriesAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("del.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            await provider.InsertAsync(context, nodeId,
                [MakeValue(BaseTime.AddSeconds(10), 1.0), MakeValue(BaseTime.AddSeconds(20), 2.0)],
                CancellationToken.None).ConfigureAwait(false);

            HistorianUpdateOutcome<DataValue> result = await provider.DeleteAtTimeAsync(
                context, nodeId, [(DateTimeUtc)BaseTime.AddSeconds(10)], CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.OperationResults[0]), Is.True);

            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddMinutes(1),
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(page.Values, Has.Count.EqualTo(1));
            Assert.That(
                Convert.ToDouble(page.Values[0].Value.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.EqualTo(2.0));
        }

        /// <summary>
        /// Verifies that history pagination follows resume tokens across pages.
        /// </summary>
        [Test]
        public async Task PaginationFollowsResumeTokensAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("page.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            var values = new List<DataValue>(50);
            for (int i = 0; i < 50; i++)
            {
                values.Add(MakeValue(BaseTime.AddSeconds(i), i));
            }
            await provider.InsertAsync(context, nodeId, values, CancellationToken.None).ConfigureAwait(false);

            const uint pageSize = 10;
            var allReturned = new List<DataValue>();
            HistorianResumeToken token = default;
            int pages = 0;
            while (true)
            {
                HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                    context,
                    new HistorianRawReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = BaseTime,
                        EndTime = BaseTime.AddMinutes(2),
                        MaxValues = pageSize,
                        IsForward = true
                    },
                    token,
                    CancellationToken.None).ConfigureAwait(false);
                foreach (HistoricalDataValue v in page.Values)
                {
                    allReturned.Add(v.Value);
                }
                pages++;
                if (page.IsFinal)
                {
                    break;
                }
                token = page.NextToken;
                Assert.That(pages, Is.LessThan(20), "Pagination did not terminate.");
            }
            Assert.That(allReturned, Has.Count.EqualTo(50));
            Assert.That(
                Convert.ToInt32(allReturned[0].WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.Zero);
            Assert.That(
                Convert.ToInt32(allReturned[^1].WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.EqualTo(49));
        }

        /// <summary>
        /// Verifies that equal raw-history start and end times return the exact matching value.
        /// </summary>
        [Test]
        public async Task ReadRawWithEqualTimesReturnsExactValueAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("equal.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            DateTime requestedTime = BaseTime.AddSeconds(20);
            await provider.InsertAsync(
                context,
                nodeId,
                [
                    MakeValue(BaseTime.AddSeconds(10), 1.0),
                    MakeValue(requestedTime, 2.0),
                    MakeValue(BaseTime.AddSeconds(30), 3.0)
                ],
                CancellationToken.None).ConfigureAwait(false);

            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = requestedTime,
                    EndTime = requestedTime,
                    MaxValues = 0,
                    IsForward = true,
                    ReturnBounds = false
                },
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(1));
            Assert.That(page.Values[0].Value.SourceTimestamp, Is.EqualTo(requestedTime));
            Assert.That(page.IsFinal, Is.True);
        }

        /// <summary>
        /// Verifies that equal-time bounded reads include the next value unless the value limit is one.
        /// </summary>
        [TestCase(0, 2)]
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        public async Task ReadRawEqualExactWithBoundsReturnsNextValueUnlessMaximumIsOneAsync(
            int maxValues,
            int expectedCount)
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId($"equal-bounds-{maxValues}", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            DateTime requestedTime = BaseTime.AddSeconds(20);
            DateTime nextTime = BaseTime.AddSeconds(30);
            await provider.InsertAsync(
                context,
                nodeId,
                [
                    MakeValue(BaseTime.AddSeconds(10), 1.0),
                    MakeValue(requestedTime, 2.0),
                    MakeValue(nextTime, 3.0)
                ],
                CancellationToken.None).ConfigureAwait(false);

            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = requestedTime,
                    EndTime = requestedTime,
                    MaxValues = (uint)maxValues,
                    IsForward = true,
                    ReturnBounds = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(expectedCount));
            Assert.That(page.Values[0].Value.SourceTimestamp, Is.EqualTo(requestedTime));
            if (expectedCount == 2)
            {
                Assert.That(page.Values[1].Value.SourceTimestamp, Is.EqualTo(nextTime));
                Assert.That(page.Values[1].IsBound, Is.True);
            }
            Assert.That(page.IsFinal, Is.True);
        }

        /// <summary>
        /// Verifies that bounds count toward page limits and remaining values continue on the next page.
        /// </summary>
        [Test]
        public async Task ReadRawBoundsCountTowardsMaximumAndContinueOnNextPageAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("bounded-page.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            var values = new List<DataValue>();
            for (int i = 0; i < 6; i++)
            {
                values.Add(MakeValue(BaseTime.AddSeconds(i), i));
            }
            await provider.InsertAsync(context, nodeId, values, CancellationToken.None).ConfigureAwait(false);

            var request = new HistorianRawReadRequest
            {
                NodeId = nodeId,
                StartTime = BaseTime,
                EndTime = BaseTime.AddSeconds(4).AddMilliseconds(3),
                MaxValues = 5,
                IsForward = true,
                ReturnBounds = true
            };

            HistorianPage<HistoricalDataValue> first = await provider.ReadRawAsync(
                context, request, default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(first.Values, Has.Count.EqualTo(5));
            Assert.That(first.Values[0].Value.SourceTimestamp, Is.EqualTo(BaseTime));
            Assert.That(first.Values[^1].Value.SourceTimestamp, Is.EqualTo(BaseTime.AddSeconds(4)));
            Assert.That(first.IsFinal, Is.False);

            HistorianPage<HistoricalDataValue> second = await provider.ReadRawAsync(
                context, request, first.NextToken, CancellationToken.None).ConfigureAwait(false);

            Assert.That(second.Values, Has.Count.EqualTo(1));
            Assert.That(second.Values[0].Value.SourceTimestamp, Is.EqualTo(BaseTime.AddSeconds(5)));
            Assert.That(second.Values[0].IsBound, Is.True);
            Assert.That(second.IsFinal, Is.True);
        }

        /// <summary>
        /// Verifies that missing raw-history bounds produce BadBoundNotFound at the requested timestamps.
        /// </summary>
        [Test]
        public async Task ReadRawMissingBoundsReturnsBadBoundNotFoundAtRequestedTimesAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("missing-bounds.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            await provider.InsertAsync(
                context,
                nodeId,
                [
                    MakeValue(BaseTime.AddSeconds(10), 1.0),
                    MakeValue(BaseTime.AddSeconds(20), 2.0)
                ],
                CancellationToken.None).ConfigureAwait(false);

            DateTime startTime = BaseTime;
            DateTime endTime = BaseTime.AddSeconds(30);
            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = startTime,
                    EndTime = endTime,
                    MaxValues = 0,
                    IsForward = true,
                    ReturnBounds = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(4));
            Assert.That(page.Values[0].Value.StatusCode, Is.EqualTo(StatusCodes.BadBoundNotFound));
            Assert.That(page.Values[0].Value.SourceTimestamp, Is.EqualTo(startTime));
            Assert.That(page.Values[^1].Value.StatusCode, Is.EqualTo(StatusCodes.BadBoundNotFound));
            Assert.That(page.Values[^1].Value.SourceTimestamp, Is.EqualTo(endTime));
        }

        /// <summary>
        /// Verifies that a one-sided raw-history request returns the requested count without a continuation.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task ReadRawOneSidedRequestReturnsRequestedCountWithoutContinuationAsync(bool isForward)
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId($"one-sided-{isForward}", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            var values = new List<DataValue>();
            for (int i = 0; i < 10; i++)
            {
                values.Add(MakeValue(BaseTime.AddSeconds(i), i));
            }
            await provider.InsertAsync(context, nodeId, values, CancellationToken.None).ConfigureAwait(false);

            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = isForward ? BaseTime.AddSeconds(2) : DateTimeUtc.MinValue,
                    EndTime = isForward ? DateTimeUtc.MaxValue : BaseTime.AddSeconds(7),
                    MaxValues = 5,
                    IsForward = isForward,
                    ReturnBounds = false
                },
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(5));
            Assert.That(page.Values[0].Value.SourceTimestamp,
                Is.EqualTo(isForward ? BaseTime.AddSeconds(2) : BaseTime.AddSeconds(7)));
            Assert.That(page.Values[^1].Value.SourceTimestamp,
                Is.EqualTo(isForward ? BaseTime.AddSeconds(6) : BaseTime.AddSeconds(3)));
            Assert.That(page.IsFinal, Is.True);
        }

        /// <summary>
        /// Verifies that one-sided bounded reads add a missing-bound marker after archive exhaustion.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task ReadRawOneSidedBoundsAddMissingBoundaryWhenArchiveIsExhaustedAsync(bool isForward)
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId($"one-sided-missing-{isForward}", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            var values = new List<DataValue>();
            for (int i = 0; i < 3; i++)
            {
                values.Add(MakeValue(BaseTime.AddSeconds(i), i));
            }
            await provider.InsertAsync(context, nodeId, values, CancellationToken.None).ConfigureAwait(false);

            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = isForward ? BaseTime : DateTimeUtc.MinValue,
                    EndTime = isForward ? DateTimeUtc.MaxValue : BaseTime.AddSeconds(2),
                    MaxValues = 5,
                    IsForward = isForward,
                    ReturnBounds = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(4));
            Assert.That(page.Values[0].Value.SourceTimestamp,
                Is.EqualTo(isForward ? BaseTime : BaseTime.AddSeconds(2)));
            Assert.That(page.Values[^1].Value.StatusCode, Is.EqualTo(StatusCodes.BadBoundNotFound));
            Assert.That(page.Values[^1].Value.SourceTimestamp,
                Is.EqualTo(isForward ? BaseTime.AddSeconds(3) : BaseTime.AddSeconds(-1)));
            Assert.That(page.IsFinal, Is.True);
        }

        /// <summary>
        /// Verifies that one-sided bounded reads stop at the requested maximum without a continuation.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task ReadRawOneSidedBoundsStopAtRequestedMaximumWithoutContinuationAsync(bool isForward)
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId($"one-sided-bounds-max-{isForward}", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            var values = new List<DataValue>();
            for (int i = 0; i < 10; i++)
            {
                values.Add(MakeValue(BaseTime.AddSeconds(i), i));
            }
            await provider.InsertAsync(context, nodeId, values, CancellationToken.None).ConfigureAwait(false);

            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = isForward ? BaseTime.AddSeconds(2) : DateTimeUtc.MinValue,
                    EndTime = isForward ? DateTimeUtc.MaxValue : BaseTime.AddSeconds(7),
                    MaxValues = 5,
                    IsForward = isForward,
                    ReturnBounds = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(5));
            Assert.That(page.Values[^1].Value.StatusCode, Is.Not.EqualTo(StatusCodes.BadBoundNotFound));
            Assert.That(page.IsFinal, Is.True);
        }

        /// <summary>
        /// Verifies that an annotation can be inserted, read with its message intact, and deleted successfully.
        /// </summary>
        [Test]
        public async Task AnnotationLifecycleAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("ann.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            DateTime when = BaseTime.AddSeconds(10);
            var annotation = new Annotation { Message = "test", UserName = "alice", AnnotationTime = when };

            HistorianUpdateOutcome<Annotation> insert = await provider.InsertAnnotationsAsync(
                context, nodeId, [annotation], CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(insert.OperationResults[0]), Is.True);

            HistorianPage<Annotation> page = await provider.ReadAnnotationsAsync(
                context,
                new HistorianAnnotationReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddMinutes(1),
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(page.Values, Has.Count.EqualTo(1));
            Assert.That(page.Values[0].Message, Is.EqualTo("test"));

            HistorianUpdateOutcome<Annotation> del = await provider.DeleteAnnotationsAsync(
                context, nodeId, [(DateTimeUtc)when], CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(del.OperationResults[0]), Is.True);
        }

        /// <summary>
        /// Verifies that an exactly filled final modified-history page has no continuation.
        /// </summary>
        [Test]
        public async Task ExactModifiedPageDoesNotReturnContinuationAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("modified.final.page", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();
            await provider.InsertAsync(
                context,
                nodeId,
                [
                    MakeValue(BaseTime.AddSeconds(10), 1),
                    MakeValue(BaseTime.AddSeconds(20), 2)
                ],
                CancellationToken.None).ConfigureAwait(false);
            await provider.ReplaceAsync(
                context,
                nodeId,
                [
                    MakeValue(BaseTime.AddSeconds(10), 10),
                    MakeValue(BaseTime.AddSeconds(20), 20)
                ],
                CancellationToken.None).ConfigureAwait(false);

            HistorianPage<ModifiedDataValue> page =
                await provider.ReadModifiedAsync(
                    context,
                    new HistorianModifiedReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = BaseTime,
                        EndTime = BaseTime.AddMinutes(1),
                        MaxValues = 2,
                        IsForward = true
                    },
                    default,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(2));
            Assert.That(page.IsFinal, Is.True);
        }

        /// <summary>
        /// Verifies that reverse modified-history reads include the start and exclude the end timestamp.
        /// </summary>
        [Test]
        public async Task ReverseModifiedHistoryIncludesStartAndExcludesEndAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("modified.reverse.boundaries", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();
            DateTime lower = BaseTime;
            DateTime middle = BaseTime.AddSeconds(5);
            DateTime upper = BaseTime.AddSeconds(10);
            await provider.InsertAsync(
                context,
                nodeId,
                [
                    MakeValue(lower, 0),
                    MakeValue(middle, 5),
                    MakeValue(upper, 10)
                ],
                CancellationToken.None).ConfigureAwait(false);
            await provider.ReplaceAsync(
                context,
                nodeId,
                [
                    MakeValue(lower, 100),
                    MakeValue(middle, 105),
                    MakeValue(upper, 110)
                ],
                CancellationToken.None).ConfigureAwait(false);

            HistorianPage<ModifiedDataValue> page =
                await provider.ReadModifiedAsync(
                    context,
                    new HistorianModifiedReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = upper,
                        EndTime = lower,
                        IsForward = false
                    },
                    default,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(2));
            Assert.That(
                page.Values[0].Value.SourceTimestamp,
                Is.EqualTo((DateTimeUtc)upper));
            Assert.That(
                page.Values[1].Value.SourceTimestamp,
                Is.EqualTo((DateTimeUtc)middle));
        }

        /// <summary>
        /// Verifies that a forward modification cursor includes backdated modifications.
        /// </summary>
        [Test]
        public async Task ModifiedHistoryForwardCursorIncludesBackdatedModificationAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("modified.forward.cursor", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext insertContext = CreateContext();
            HistorianOperationContext earlierContext = CreateContext();
            HistorianOperationContext middleContext = CreateContext();
            HistorianOperationContext laterContext = CreateContext();
            earlierContext.DefaultModificationInfo.ModificationTime =
                BaseTime.AddMinutes(1);
            middleContext.DefaultModificationInfo.ModificationTime =
                BaseTime.AddMinutes(2);
            laterContext.DefaultModificationInfo.ModificationTime =
                BaseTime.AddMinutes(3);
            DateTime sourceTimestamp = BaseTime.AddSeconds(10);
            await provider.InsertAsync(
                insertContext,
                nodeId,
                [MakeValue(sourceTimestamp, 0)],
                CancellationToken.None).ConfigureAwait(false);
            await provider.ReplaceAsync(
                laterContext,
                nodeId,
                [MakeValue(sourceTimestamp, 1)],
                CancellationToken.None).ConfigureAwait(false);
            await provider.ReplaceAsync(
                middleContext,
                nodeId,
                [MakeValue(sourceTimestamp, 2)],
                CancellationToken.None).ConfigureAwait(false);
            var request = new HistorianModifiedReadRequest
            {
                NodeId = nodeId,
                StartTime = BaseTime,
                EndTime = BaseTime.AddMinutes(1),
                MaxValues = 1,
                IsForward = true
            };

            HistorianPage<ModifiedDataValue> first = await provider.ReadModifiedAsync(
                laterContext,
                request,
                default,
                CancellationToken.None).ConfigureAwait(false);
            await provider.ReplaceAsync(
                earlierContext,
                nodeId,
                [MakeValue(sourceTimestamp, 3)],
                CancellationToken.None).ConfigureAwait(false);
            HistorianPage<ModifiedDataValue> second = await provider.ReadModifiedAsync(
                laterContext,
                request,
                first.NextToken,
                CancellationToken.None).ConfigureAwait(false);
            HistorianPage<ModifiedDataValue> third = await provider.ReadModifiedAsync(
                laterContext,
                request,
                second.NextToken,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(first.Values, Has.Count.EqualTo(1));
            Assert.That(first.Values[0].Value.WrappedValue.TryGetValue(out double firstValue), Is.True);
            Assert.That(firstValue, Is.Zero);
            Assert.That(first.IsFinal, Is.False);
            Assert.That(first.NextToken.TryGetCursor(out HistorianResumeCursor firstCursor), Is.True);
            Assert.That(firstCursor.Key.IsEmpty, Is.False);
            Assert.That(second.Values, Has.Count.EqualTo(1));
            Assert.That(second.Values[0].Value.WrappedValue.TryGetValue(out double secondValue), Is.True);
            Assert.That(secondValue, Is.EqualTo(1));
            Assert.That(second.IsFinal, Is.False);
            Assert.That(third.Values, Has.Count.EqualTo(1));
            Assert.That(third.Values[0].Value.WrappedValue.TryGetValue(out double thirdValue), Is.True);
            Assert.That(thirdValue, Is.EqualTo(2));
            Assert.That(third.IsFinal, Is.True);
        }

        /// <summary>
        /// Verifies that a reverse modification cursor uses the complete ordering tuple.
        /// </summary>
        [Test]
        public async Task ModifiedHistoryReverseCursorUsesCompleteOrderingTupleAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("modified.reverse.cursor", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext insertContext = CreateContext();
            HistorianOperationContext earlierContext = CreateContext();
            HistorianOperationContext laterContext = CreateContext();
            earlierContext.DefaultModificationInfo.ModificationTime =
                BaseTime.AddMinutes(1);
            laterContext.DefaultModificationInfo.ModificationTime =
                BaseTime.AddMinutes(2);
            DateTime sourceTimestamp = BaseTime.AddSeconds(10);
            await provider.InsertAsync(
                insertContext,
                nodeId,
                [MakeValue(sourceTimestamp, 0)],
                CancellationToken.None).ConfigureAwait(false);
            await provider.ReplaceAsync(
                laterContext,
                nodeId,
                [MakeValue(sourceTimestamp, 1)],
                CancellationToken.None).ConfigureAwait(false);
            await provider.ReplaceAsync(
                earlierContext,
                nodeId,
                [MakeValue(sourceTimestamp, 2)],
                CancellationToken.None).ConfigureAwait(false);
            var request = new HistorianModifiedReadRequest
            {
                NodeId = nodeId,
                StartTime = BaseTime.AddMinutes(1),
                EndTime = BaseTime,
                MaxValues = 1,
                IsForward = false
            };

            HistorianPage<ModifiedDataValue> first = await provider.ReadModifiedAsync(
                laterContext,
                request,
                default,
                CancellationToken.None).ConfigureAwait(false);
            HistorianPage<ModifiedDataValue> second = await provider.ReadModifiedAsync(
                laterContext,
                request,
                first.NextToken,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(first.Values, Has.Count.EqualTo(1));
            Assert.That(first.Values[0].Value.WrappedValue.TryGetValue(out double firstValue), Is.True);
            Assert.That(firstValue, Is.EqualTo(1));
            Assert.That(first.IsFinal, Is.False);
            Assert.That(second.Values, Has.Count.EqualTo(1));
            Assert.That(second.Values[0].Value.WrappedValue.TryGetValue(out double secondValue), Is.True);
            Assert.That(secondValue, Is.Zero);
            Assert.That(second.IsFinal, Is.True);
        }

        /// <summary>
        /// Verifies that modified-history reads accept legacy sequence-only cursors.
        /// </summary>
        [Test]
        public async Task ModifiedHistoryAcceptsLegacySequenceOnlyCursorAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("modified.legacy.cursor", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext insertContext = CreateContext();
            HistorianOperationContext earlierContext = CreateContext();
            HistorianOperationContext laterContext = CreateContext();
            earlierContext.DefaultModificationInfo.ModificationTime =
                BaseTime.AddMinutes(1);
            laterContext.DefaultModificationInfo.ModificationTime =
                BaseTime.AddMinutes(2);
            DateTime sourceTimestamp = BaseTime.AddSeconds(10);
            await provider.InsertAsync(
                insertContext,
                nodeId,
                [MakeValue(sourceTimestamp, 0)],
                CancellationToken.None).ConfigureAwait(false);
            await provider.ReplaceAsync(
                laterContext,
                nodeId,
                [MakeValue(sourceTimestamp, 1)],
                CancellationToken.None).ConfigureAwait(false);
            await provider.ReplaceAsync(
                earlierContext,
                nodeId,
                [MakeValue(sourceTimestamp, 2)],
                CancellationToken.None).ConfigureAwait(false);

            HistorianPage<ModifiedDataValue> page = await provider.ReadModifiedAsync(
                laterContext,
                new HistorianModifiedReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddMinutes(1),
                    MaxValues = 1,
                    IsForward = true
                },
                HistorianResumeToken.FromCursor(
                    new HistorianResumeCursor(
                        sourceTimestamp,
                        ByteString.Empty,
                        1)),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(1));
            Assert.That(page.Values[0].Value.WrappedValue.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(1));
            Assert.That(page.IsFinal, Is.True);
        }

        /// <summary>
        /// Verifies that modified-history reads reject unknown cursor-key versions.
        /// </summary>
        [Test]
        public async Task ModifiedHistoryRejectsUnknownCursorKeyVersionAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("modified.invalid.cursor", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();
            DateTime sourceTimestamp = BaseTime.AddSeconds(10);
            await provider.InsertAsync(
                context,
                nodeId,
                [MakeValue(sourceTimestamp, 0)],
                CancellationToken.None).ConfigureAwait(false);
            await provider.ReplaceAsync(
                context,
                nodeId,
                [MakeValue(sourceTimestamp, 1)],
                CancellationToken.None).ConfigureAwait(false);
            HistorianResumeToken malformed = HistorianResumeToken.FromCursor(
                new HistorianResumeCursor(
                    sourceTimestamp,
                    ByteString.From([1]),
                    1));

            ServiceResultException exception =
                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await provider.ReadModifiedAsync(
                        context,
                        new HistorianModifiedReadRequest
                        {
                            NodeId = nodeId,
                            StartTime = BaseTime,
                            EndTime = BaseTime.AddMinutes(1),
                            IsForward = true
                        },
                        malformed,
                        CancellationToken.None).ConfigureAwait(false));

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
        }

        /// <summary>
        /// Verifies that an exactly filled final annotation page has no continuation.
        /// </summary>
        [Test]
        public async Task ExactAnnotationPageDoesNotReturnContinuationAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("annotation.final.page", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();
            await provider.InsertAnnotationsAsync(
                context,
                nodeId,
                [
                    new Annotation
                    {
                        AnnotationTime = BaseTime.AddSeconds(10),
                        Message = "first"
                    },
                    new Annotation
                    {
                        AnnotationTime = BaseTime.AddSeconds(20),
                        Message = "second"
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);

            HistorianPage<Annotation> page =
                await provider.ReadAnnotationsAsync(
                    context,
                    new HistorianAnnotationReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = BaseTime,
                        EndTime = BaseTime.AddMinutes(1),
                        MaxValues = 2,
                        IsForward = true
                    },
                    default,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(2));
            Assert.That(page.IsFinal, Is.True);
        }

        private static readonly DateTime BaseTime = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static DataValue MakeValue(DateTime sourceTimestamp, double value)
        {
            return new DataValue(new Variant(value), StatusCodes.Good, sourceTimestamp: sourceTimestamp, serverTimestamp: sourceTimestamp);
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
    }
}
