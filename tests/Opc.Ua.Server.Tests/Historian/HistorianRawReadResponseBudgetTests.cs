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

// CA2007: tests run without a SynchronizationContext.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
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
    /// Verifies the response byte budget of a raw HistoryRead: a page holds at most the
    /// values that fit the rest of the budget, and the client continues with a
    /// continuation point instead of the whole read failing.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public sealed class HistorianRawReadResponseBudgetTests
    {
        private const int kValueCount = 300;
        private const int kArrayLength = 1024;
        private const int kBudget = 64 * 1024;

        /// <summary>
        /// A raw read with a huge NumValuesPerNode whose values exceed the response size
        /// limit returns the values that fit with a continuation point, and paging with
        /// the continuation points returns every value once and in order.
        /// </summary>
        [Test]
        public async Task RawReadPastTheResponseSizeLimitReturnsAPageThatFitsAsync()
        {
            var h = new Harness();
            NodeId nodeId = h.SeedArrays(kValueCount, kArrayLength);
            BaseDataVariableState variable = CreateVariable(nodeId);
            ReadRawModifiedDetails details = CreateDetails(uint.MaxValue);

            OperationContext operation = h.SystemContext.OperationContext!;
            operation.StartResponseBudget(kBudget);
            var first = new HistoryReadResult();
            ServiceResult error = await ReadAsync(h, variable, nodeId, ByteString.Empty, details, first);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(first.ContinuationPoint.IsEmpty, Is.False);
            ArrayOf<DataValue> firstPage = GetValues(first);
            // each value is a 1024 element Double array with a source timestamp:
            // 1 + (1 + 4 + 8192) + 8 bytes, seven of them fit 64 KiB.
            Assert.That(firstPage.Count, Is.EqualTo(7));
            Assert.That(operation.ResponseBudgetExceeded, Is.False);
            Assert.That(kBudget - operation.ResponseBytesRemaining, Is.EqualTo(7 * 8206));

            // page through the rest, each page with a budget of its own.
            var seen = new List<int>();
            AddIndexes(seen, firstPage);
            ByteString continuationPoint = first.ContinuationPoint;
            int pages = 1;
            while (!continuationPoint.IsEmpty)
            {
                operation.StartResponseBudget(kBudget);
                var next = new HistoryReadResult();
                error = await ReadAsync(h, variable, nodeId, continuationPoint, details, next);
                Assert.That(ServiceResult.IsGood(error), Is.True);
                Assert.That(next.StatusCode, Is.EqualTo(StatusCodes.Good));
                ArrayOf<DataValue> page = GetValues(next);
                Assert.That(page.Count, Is.InRange(1, 7));
                AddIndexes(seen, page);
                continuationPoint = next.ContinuationPoint;
                pages++;
            }

            Assert.That(pages, Is.EqualTo((kValueCount + 6) / 7));
            Assert.That(seen, Has.Count.EqualTo(kValueCount));
            for (int ii = 0; ii < seen.Count; ii++)
            {
                Assert.That(seen[ii], Is.EqualTo(ii));
            }
        }

        /// <summary>
        /// A raw read whose first value does not fit what other operations of the request
        /// left of the budget returns no values and a continuation point that resumes at
        /// that value.
        /// </summary>
        [Test]
        public async Task RawReadAfterOtherResultsSpentTheBudgetResumesAtItsFirstValueAsync()
        {
            var h = new Harness();
            NodeId nodeId = h.SeedArrays(10, kArrayLength);
            BaseDataVariableState variable = CreateVariable(nodeId);
            ReadRawModifiedDetails details = CreateDetails(uint.MaxValue);

            OperationContext operation = h.SystemContext.OperationContext!;
            operation.StartResponseBudget(kBudget);
            Assert.That(operation.TryChargeResponse(kBudget - 100), Is.True);
            var first = new HistoryReadResult();
            ServiceResult error = await ReadAsync(h, variable, nodeId, ByteString.Empty, details, first);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(first.ContinuationPoint.IsEmpty, Is.False);
            Assert.That(GetValues(first).Count, Is.Zero);
            Assert.That(operation.ResponseBytesRemaining, Is.EqualTo(100));

            operation.StartResponseBudget(kBudget);
            var next = new HistoryReadResult();
            error = await ReadAsync(h, variable, nodeId, first.ContinuationPoint, details, next);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            ArrayOf<DataValue> page = GetValues(next);
            Assert.That(page.Count, Is.EqualTo(7));
            var seen = new List<int>();
            AddIndexes(seen, page);
            Assert.That(seen[0], Is.Zero);
        }

        /// <summary>
        /// A raw read whose first value alone exceeds the whole response size limit fails
        /// with BadResponseTooLarge.
        /// </summary>
        [Test]
        public async Task RawReadOfAValueLargerThanTheResponseSizeLimitFailsAsync()
        {
            var h = new Harness();
            NodeId nodeId = h.SeedArrays(3, kArrayLength);
            BaseDataVariableState variable = CreateVariable(nodeId);
            ReadRawModifiedDetails details = CreateDetails(uint.MaxValue);

            OperationContext operation = h.SystemContext.OperationContext!;
            operation.StartResponseBudget(4096);
            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadAsync(h, variable, nodeId, ByteString.Empty, details, new HistoryReadResult())))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadResponseTooLarge));
            Assert.That(operation.ResponseBudgetExceeded, Is.True);
        }

        /// <summary>
        /// A raw read without a response byte budget (an in-process caller) returns the
        /// whole page the provider reads.
        /// </summary>
        [Test]
        public async Task RawReadWithoutAResponseBudgetReturnsTheWholePageAsync()
        {
            var h = new Harness();
            NodeId nodeId = h.SeedArrays(kValueCount, kArrayLength);
            BaseDataVariableState variable = CreateVariable(nodeId);

            var result = new HistoryReadResult();
            ServiceResult error = await ReadAsync(
                h, variable, nodeId, ByteString.Empty, CreateDetails(uint.MaxValue), result);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(GetValues(result).Count, Is.EqualTo(kValueCount));
            Assert.That(result.ContinuationPoint.IsEmpty, Is.True);
        }

        private static ValueTask<ServiceResult> ReadAsync(
            Harness h,
            BaseDataVariableState variable,
            NodeId nodeId,
            ByteString continuationPoint,
            ReadRawModifiedDetails details,
            HistoryReadResult result)
        {
            return HistorianDispatcher.DispatchRawReadAsync(
                h.SystemContext,
                h.Provider,
                variable,
                new HistoryReadValueId { NodeId = nodeId, ContinuationPoint = continuationPoint },
                details,
                TimestampsToReturn.Source,
                result,
                CancellationToken.None);
        }

        private static ReadRawModifiedDetails CreateDetails(uint numValuesPerNode)
        {
            return new ReadRawModifiedDetails
            {
                StartTime = Harness.BaseTime,
                EndTime = Harness.BaseTime.AddDays(1),
                NumValuesPerNode = numValuesPerNode,
                IsReadModified = false
            };
        }

        private static BaseDataVariableState CreateVariable(NodeId nodeId)
        {
            return new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("BudgetVar"),
                AccessLevel = AccessLevels.HistoryRead,
                Historizing = true
            };
        }

        private static ArrayOf<DataValue> GetValues(HistoryReadResult result)
        {
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? data), Is.True);
            return data!.DataValues;
        }

        /// <summary>
        /// Adds the index each seeded array carries in its first element.
        /// </summary>
        private static void AddIndexes(List<int> seen, ArrayOf<DataValue> values)
        {
            for (int ii = 0; ii < values.Count; ii++)
            {
                Assert.That(values[ii].WrappedValue.TryGetValue(out ArrayOf<double> array), Is.True);
                seen.Add((int)array[0]);
            }
        }

        private sealed class Harness
        {
            public static readonly DateTime BaseTime = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            public Harness()
            {
                Provider = new InMemoryHistorianProvider(
                    new InMemoryHistorianOptions(),
                    new FakeTimeProvider(BaseTime));
                var continuationPoints = new SessionContinuationPoints(
                    () => NodeId.Null,
                    maxBrowse: 10,
                    maxHistory: 10,
                    store: null);
                var session = new Mock<ISession>();
                session.Setup(s => s.ContinuationPoints).Returns(continuationPoints);
                var server = new Mock<IServerInternal>();
                server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
                server.Setup(s => s.ServerUris).Returns(new StringTable());
                server.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
                server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
                server.Setup(s => s.Telemetry).Returns(new Mock<ITelemetryContext>().Object);
                var operation = new OperationContext(
                    new RequestHeader(),
                    null!,
                    RequestType.HistoryRead,
                    RequestLifetime.None,
                    session.Object);
                SystemContext = new ServerSystemContext(server.Object, operation);
            }

            public InMemoryHistorianProvider Provider { get; }

            public ServerSystemContext SystemContext { get; }

            /// <summary>
            /// Stores <paramref name="count"/> Double arrays, one per second, each filled
            /// with its index.
            /// </summary>
            public NodeId SeedArrays(int count, int length)
            {
                var nodeId = new NodeId($"budget-{Guid.NewGuid():N}", 1);
                Provider.Register(nodeId);
                var values = new DataValue[count];
                for (int ii = 0; ii < count; ii++)
                {
                    double[] array = new double[length];
                    Array.Fill(array, ii);
                    values[ii] = new DataValue(
                        new Variant((ArrayOf<double>)array),
                        StatusCodes.Good,
                        sourceTimestamp: BaseTime.AddSeconds(ii),
                        serverTimestamp: BaseTime.AddSeconds(ii));
                }
                var context = new HistorianOperationContext(
                    SystemContext,
                    SystemContext.OperationContext!,
                    null,
                    HistoryUpdateType.Insert);
                HistorianUpdateOutcome<DataValue> outcome = Provider
                    .InsertAsync(context, nodeId, values, CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult();
                Assert.That(outcome.OperationResults.Count, Is.EqualTo(count));
                return nodeId;
            }
        }
    }
}
