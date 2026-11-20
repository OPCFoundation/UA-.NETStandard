// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
        /// A complete result that leaves its continuation point bytes unused returns them
        /// to the budget of the request: after an empty first result, a last value that
        /// fills the rest of the message size limit is returned, and the two-result
        /// response encodes within the limit.
        /// </summary>
        [Test]
        public async Task RawReadAfterAnEmptyResultUsesItsContinuationPointBytesAsync()
        {
            const int maxMessageSize = 64 * 1024;
            var h = new Harness();
            NodeId emptyNodeId = h.SeedValues(0, _ => default);
            // 65436 bytes with the source timestamp are a 65450 byte value, which needs
            // the continuation point bytes of both results.
            NodeId nodeId = h.SeedValues(1, _ => new Variant(ByteString.From(new byte[65436])));

            OperationContext operation = h.SystemContext.OperationContext!;
            operation.StartResponseBudget(ResponseSizeEstimator.GetHistoryReadResponseBudget(maxMessageSize, 2));
            var empty = new HistoryReadResult();
            ServiceResult error = await ReadAsync(
                h, CreateVariable(emptyNodeId), emptyNodeId, ByteString.Empty, CreateDetails(uint.MaxValue), empty);
            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(empty.ContinuationPoint.IsEmpty, Is.True);
            Assert.That(operation.ResponseBudgetUnused, Is.True);

            var result = new HistoryReadResult();
            error = await ReadAsync(
                h, CreateVariable(nodeId), nodeId, ByteString.Empty, CreateDetails(uint.MaxValue), result);

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
                Results = [empty, result],
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
        /// A string nested in a variant array that alone exceeds the rest of the budget is
        /// not encoded to be measured, so reading it does not allocate its UTF-8 bytes.
        /// </summary>
        [Test]
        public async Task RawReadOfANestedStringLargerThanTheBudgetDoesNotEncodeItAsync()
        {
            const int length = 4_000_000;
            var h = new Harness();
            NodeId smallNodeId = h.SeedValues(1, _ => CreateNestedString(100_000));
            NodeId nodeId = h.SeedValues(1, _ => CreateNestedString(length));
            BaseDataVariableState variable = CreateVariable(nodeId);
            ReadRawModifiedDetails details = CreateDetails(uint.MaxValue);

            OperationContext operation = h.SystemContext.OperationContext!;
            operation.StartResponseBudget(kBudget);
            // a smaller value that does not fit either warms up the code path, without
            // filling the array pool with the buffers a large string would rent.
            await Assert.ThatAsync(
                async () => await ReadAsync(
                    h, CreateVariable(smallNodeId), smallNodeId, ByteString.Empty, details, new HistoryReadResult()),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadResponseTooLarge));
#if NET
            operation.StartResponseBudget(kBudget);
            long before = GC.GetAllocatedBytesForCurrentThread();
            ValueTask<ServiceResult> read = ReadAsync(h, variable, nodeId, ByteString.Empty, details, new HistoryReadResult());
            Assert.That(read.IsCompleted, Is.True, "the in-memory read completes synchronously");
            Assert.Throws<ServiceResultException>(() => read.GetAwaiter().GetResult());
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.LessThan(length / 4));
#endif
        }

        private static Variant CreateNestedString(int length)
        {
            ArrayOf<Variant> elements = [new Variant(new string('a', length))];
            return new Variant(elements);
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
            // the framing alone exceeds the limit: the deficit is not clamped away.
            Assert.That(ResponseSizeEstimator.GetHistoryReadResponseBudget(100, 1000), Is.EqualTo(-1));
        }

        /// <summary>
        /// When the framing reserved for many results exceeds the message size limit, the
        /// continuation point bytes that complete results leave unused do not cover a
        /// value: after 999 empty results, a value that would make the 1000-result
        /// response exceed the limit fails the read instead of being returned.
        /// </summary>
        [Test]
        public async Task RawReadWhoseReservedFramingExceedsTheLimitReturnsNoValueAsync()
        {
            const int maxMessageSize = 32 * 1024;
            const int resultCount = 1000;
            var h = new Harness();
            NodeId emptyNodeId = h.SeedValues(0, _ => default);
            // 14986 bytes with the source timestamp are a 15000 byte value.
            NodeId nodeId = h.SeedValues(1, _ => new Variant(ByteString.From(new byte[14986])));
            BaseDataVariableState emptyVariable = CreateVariable(emptyNodeId);

            OperationContext operation = h.SystemContext.OperationContext!;
            StartHistoryReadBudget(operation, maxMessageSize, resultCount);
            var results = new List<HistoryReadResult>();
            for (int ii = 0; ii < resultCount - 1; ii++)
            {
                var empty = new HistoryReadResult();
                ServiceResult error = await ReadAsync(
                    h, emptyVariable, emptyNodeId, ByteString.Empty, CreateDetails(uint.MaxValue), empty);
                Assert.That(ServiceResult.IsGood(error), Is.True);
                Assert.That(empty.ContinuationPoint.IsEmpty, Is.True);
                results.Add(empty);
            }

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadAsync(
                    h, CreateVariable(nodeId), nodeId, ByteString.Empty, CreateDetails(uint.MaxValue),
                    new HistoryReadResult())))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadResponseTooLarge));

            // the response with the value would not encode within the limit.
            var withValue = new HistoryReadResult
            {
                StatusCode = StatusCodes.Good,
                HistoryData = new ExtensionObject(new HistoryData
                {
                    DataValues =
                    [
                        new DataValue(
                            new Variant(ByteString.From(new byte[14986])),
                            StatusCodes.Good,
                            sourceTimestamp: Harness.BaseTime)
                    ]
                })
            };
            results.Add(withValue);
            var response = new HistoryReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 1,
                    ServiceResult = StatusCodes.Good
                },
                Results = results.ToArray(),
                DiagnosticInfos = []
            };
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create())
            {
                MaxMessageSize = maxMessageSize
            };
            using var encoder = new BinaryEncoder(messageContext);
            Assert.That(
                () => encoder.EncodeMessage(response),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Starts the response budget of a HistoryRead as the server does.
        /// </summary>
        private static void StartHistoryReadBudget(OperationContext operation, int maxMessageSize, int resultCount)
        {
            int budget = ResponseSizeEstimator.GetHistoryReadResponseBudget(maxMessageSize, resultCount);
            if (budget < 0)
            {
                operation.StartExhaustedResponseBudget();
            }
            else
            {
                operation.StartResponseBudget(budget);
            }
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
