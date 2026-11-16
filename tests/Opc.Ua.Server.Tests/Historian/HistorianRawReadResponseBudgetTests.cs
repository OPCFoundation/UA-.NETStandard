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
using Opc.Ua.Tests;

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

        /// <summary>
        /// A raw page read with the budget of a HistoryRead encodes within the message size
        /// limit, so the client gets the page and its continuation point. The page holds
        /// fewer values than their lower bounds fit: the response and result framing is
        /// reserved, and values that encode larger than their lower bound (UTF-8 strings)
        /// are measured.
        /// </summary>
        [TestCase(32754, false, 1)]
        [TestCase(10000, true, 3)]
        public async Task RawReadPageWithinTheBudgetEncodesWithinTheMessageSizeLimitAsync(
            int length, bool isString, int expectedCount)
        {
            const int maxMessageSize = 64 * 1024;
            var h = new Harness();
            // a ByteString of 32754 bytes with its source timestamp is 32768 bytes, two of
            // them the whole limit. A string of 10000 U+00FC is at least 10000 bytes and
            // encodes 20000 UTF-8 bytes.
            NodeId nodeId = h.SeedValues(10, ii => isString
                ? new Variant(new string((char)0x00FC, length))
                : new Variant(ByteString.From(new byte[length])));
            BaseDataVariableState variable = CreateVariable(nodeId);

            OperationContext operation = h.SystemContext.OperationContext!;
            operation.StartResponseBudget(ResponseSizeEstimator.GetHistoryReadResponseBudget(maxMessageSize, 1));
            var result = new HistoryReadResult();
            ServiceResult error = await ReadAsync(
                h, variable, nodeId, ByteString.Empty, CreateDetails(uint.MaxValue), result);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.ContinuationPoint.IsEmpty, Is.False);
            Assert.That(GetValues(result).Count, Is.EqualTo(expectedCount));

            var response = new HistoryReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 1,
                    ServiceResult = StatusCodes.Good
                },
                Results = [result],
                DiagnosticInfos = []
            };
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create())
            {
                MaxMessageSize = maxMessageSize
            };
            using var encoder = new BinaryEncoder(messageContext);
            Assert.DoesNotThrow(() => encoder.EncodeMessage(response));
        }

        /// <summary>
        /// The last page of a read is sent without a continuation point and may use the
        /// bytes the budget reserves for one: a single value that fills the message size
        /// limit with the response framing is returned, and the response encodes within
        /// the limit.
        /// </summary>
        [TestCase(65450)]
        [TestCase(65464)]
        public async Task RawReadOfALastValueThatFitsWithoutAContinuationPointReturnsItAsync(int length)
        {
            const int maxMessageSize = 64 * 1024;
            var h = new Harness();
            // a ByteString of 65450 bytes with its source timestamp is a 65464 byte value,
            // more than the budget less the continuation point reserve (65462 bytes).
            NodeId nodeId = h.SeedValues(1, _ => new Variant(ByteString.From(new byte[length])));
            BaseDataVariableState variable = CreateVariable(nodeId);

            OperationContext operation = h.SystemContext.OperationContext!;
            operation.StartResponseBudget(ResponseSizeEstimator.GetHistoryReadResponseBudget(maxMessageSize, 1));
            var result = new HistoryReadResult();
            ServiceResult error = await ReadAsync(
                h, variable, nodeId, ByteString.Empty, CreateDetails(uint.MaxValue), result);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.ContinuationPoint.IsEmpty, Is.True);
            Assert.That(GetValues(result).Count, Is.EqualTo(1));

            var response = new HistoryReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 1,
                    ServiceResult = StatusCodes.Good
                },
                Results = [result],
                DiagnosticInfos = []
            };
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create())
            {
                MaxMessageSize = maxMessageSize
            };
            using var encoder = new BinaryEncoder(messageContext);
            Assert.DoesNotThrow(() => encoder.EncodeMessage(response));
        }

        /// <summary>
        /// History values are measured with the nesting limit the server encodes responses
        /// with, not the default one: values nested deeper than the default but within the
        /// configured limit are returned.
        /// </summary>
        [Test]
        public async Task RawReadOfValuesNestedWithinTheServerNestingLimitReturnsThemAsync()
        {
            const int depth = 250;
            var h = new Harness(maxEncodingNestingLevels: 300);
            NodeId nodeId = h.SeedValues(3, ii =>
            {
                Variant value = Variant.From(ii);
                for (int level = 0; level < depth; level++)
                {
                    ArrayOf<Variant> elements = [value];
                    value = new Variant(elements);
                }
                return value;
            });
            BaseDataVariableState variable = CreateVariable(nodeId);

            OperationContext operation = h.SystemContext.OperationContext!;
            operation.StartResponseBudget(kBudget);
            var result = new HistoryReadResult();
            ServiceResult error = await ReadAsync(
                h, variable, nodeId, ByteString.Empty, CreateDetails(uint.MaxValue), result);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(GetValues(result).Count, Is.EqualTo(3));
            Assert.That(result.ContinuationPoint.IsEmpty, Is.True);
        }

        /// <summary>
        /// The budget of a HistoryRead is the message size limit less the framing of the
        /// response and of each result, and an empty diagnostic info per result when the
        /// client asks for operation diagnostics; a limit of zero is no budget.
        /// </summary>
        [Test]
        public void HistoryReadBudgetReservesTheResponseAndResultFraming()
        {
            Assert.That(ResponseSizeEstimator.GetHistoryReadResponseBudget(0, 10), Is.Zero);
            Assert.That(
                ResponseSizeEstimator.GetHistoryReadResponseBudget(65536, 2),
                Is.EqualTo(65536 - ResponseSizeEstimator.HistoryReadResponseFramingSize -
                    (2 * ResponseSizeEstimator.HistoryReadResultFramingSize)));
            Assert.That(
                ResponseSizeEstimator.GetHistoryReadResponseBudget(65536, 2, returnsOperationDiagnostics: true),
                Is.EqualTo(65536 - ResponseSizeEstimator.HistoryReadResponseFramingSize -
                    (2 * (ResponseSizeEstimator.HistoryReadResultFramingSize + 1))));
            Assert.That(ResponseSizeEstimator.GetHistoryReadResponseBudget(100, 1000), Is.EqualTo(1));
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

            public Harness(int maxEncodingNestingLevels = 0)
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
                if (maxEncodingNestingLevels > 0)
                {
                    server.Setup(s => s.MessageContext).Returns(new ServiceMessageContext(
                        NUnitTelemetryContext.Create())
                    {
                        MaxEncodingNestingLevels = maxEncodingNestingLevels
                    });
                }
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
                return SeedValues(count, ii =>
                {
                    double[] array = new double[length];
                    for (int jj = 0; jj < length; jj++)
                    {
                        array[jj] = ii;
                    }
                    return new Variant((ArrayOf<double>)array);
                });
            }

            /// <summary>
            /// Stores <paramref name="count"/> values, one per second.
            /// </summary>
            public NodeId SeedValues(int count, Func<int, Variant> createValue)
            {
                var nodeId = new NodeId($"budget-{Guid.NewGuid():N}", 1);
                Provider.Register(nodeId);
                var values = new DataValue[count];
                for (int ii = 0; ii < count; ii++)
                {
                    values[ii] = new DataValue(
                        createValue(ii),
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
