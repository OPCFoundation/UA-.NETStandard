/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use, copy,
 * modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS
 * BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
 * ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
 * CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

// CA2007: tests run without a SynchronizationContext.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Verifies the memory bounds of aggregate calculators and history continuation points.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public sealed class AggregateMemoryBoundsTests
    {
        /// <summary>
        /// The raw values an aggregate monitored item keeps for a long processing interval stay
        /// within the default per-item limit, for live values and for values primed from history.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MonitoredItemAggregateKeepsBoundedRawValuesInLongIntervalAsync(bool primed)
        {
            using var harness = new Harness();
            var processed = new List<DataValue>();
            AggregationFilterHandler handler = await harness.CreateHandlerAsync(
                ObjectIds.AggregateFunction_Count,
                processingInterval: 24 * 3600 * 1000.0,
                processed,
                primed).ConfigureAwait(false);

            const int valueCount = ServerConfiguration.DefaultMaxMonitoredItemAggregateRawValues + 5000;
            for (int ii = 0; ii < valueCount; ii++)
            {
                Assert.That(handler.TryQueueValue(CreateValue(ii, ii * 10), primed, out _), Is.True);
            }

            Assert.That(processed, Is.Empty);
            Assert.That(
                GetRawValueCount(handler),
                Is.LessThanOrEqualTo(ServerConfiguration.DefaultMaxMonitoredItemAggregateRawValues));
        }

        /// <summary>
        /// An interval that received more raw values than the per-item limit is still calculated,
        /// reported as Uncertain_DataSubNormal, and later intervals are Good again.
        /// </summary>
        [Test]
        public async Task MonitoredItemAggregateIntervalOverLimitIsUncertainAsync()
        {
            using var harness = new Harness();
            harness.AggregateManager.MaxMonitoredItemRawValues = 100;
            var processed = new List<DataValue>();
            AggregationFilterHandler handler = await harness.CreateHandlerAsync(
                ObjectIds.AggregateFunction_Maximum,
                processingInterval: 10_000,
                processed).ConfigureAwait(false);

            // 500 values in the first interval (the largest one last), then a few per interval.
            for (int ii = 0; ii < 500; ii++)
            {
                handler.TryQueueValue(CreateValue(ii == 499 ? 1000 : ii, ii * 10), false, out _);
            }
            for (int ii = 0; ii < 30; ii++)
            {
                handler.TryQueueValue(CreateValue(ii, 10_000 + (ii * 1000)), false, out _);
            }

            Assert.That(processed, Has.Count.GreaterThanOrEqualTo(2));
            Assert.That(processed[0].StatusCode.CodeBits, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
            Assert.That(processed[0].WrappedValue.GetInt32(), Is.EqualTo(1000));
            Assert.That(StatusCode.IsGood(processed[1].StatusCode), Is.True);
            Assert.That(GetRawValueCount(handler), Is.LessThanOrEqualTo(101));
        }

        /// <summary>
        /// Bad raw values of completed intervals are not kept, with or without an earlier Good value.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MonitoredItemAggregateDoesNotKeepBadValuesOfCompletedIntervalsAsync(
            bool startWithGoodValue)
        {
            using var harness = new Harness();
            var processed = new List<DataValue>();
            AggregationFilterHandler handler = await harness.CreateHandlerAsync(
                ObjectIds.AggregateFunction_Average,
                processingInterval: 1000,
                processed).ConfigureAwait(false);

            if (startWithGoodValue)
            {
                handler.TryQueueValue(CreateValue(1, 0), false, out _);
            }
            // the intervals of a live item are completed when they are published.
            for (int ii = 1; ii <= 3000; ii++)
            {
                DateTimeUtc timestamp = s_baseTime.AddMilliseconds(ii * 100);
                handler.TryQueueValue(
                    new DataValue(Variant.Null, StatusCodes.BadSensorFailure, timestamp, timestamp),
                    false,
                    out _);
                handler.Publish(timestamp.AddMilliseconds(50).ToDateTime());
            }

            Assert.That(processed, Has.Count.GreaterThan(250));
            Assert.That(GetRawValueCount(handler), Is.LessThan(30));
        }

        /// <summary>
        /// A processed read calculated from raw history fails with Bad_TooManyOperations when one
        /// interval would hold more raw values than the limit.
        /// </summary>
        [Test]
        public async Task ProcessedReadOverRawValueLimitReturnsTooManyOperationsAsync()
        {
            using var harness = new Harness();
            const int valueCount = ServerConfiguration.DefaultMaxProcessedHistoryRawValues + 2000;
            var result = new HistoryReadResult();

            ServiceResult error = await harness.ReadProcessedAsync(
                valueCount,
                s_baseTime,
                s_baseTime.AddMilliseconds(valueCount + 1000),
                processingInterval: 0,
                result).ConfigureAwait(false);

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
        }

        /// <summary>
        /// The same number of raw values spread over many intervals is processed normally.
        /// </summary>
        [Test]
        public async Task ProcessedReadOverManyIntervalsIsNotLimitedAsync()
        {
            using var harness = new Harness();
            harness.AggregateManager.MaxProcessedHistoryRawValues = 1000;
            const int valueCount = 20_000;
            var result = new HistoryReadResult();

            ServiceResult error = await harness.ReadProcessedAsync(
                valueCount,
                s_baseTime,
                s_baseTime.AddMilliseconds(valueCount),
                processingInterval: 100,
                result).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True, error.ToString());
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? data), Is.True);
            Assert.That(data!.DataValues, Has.Count.EqualTo(200));
            Assert.That(data.DataValues[1].WrappedValue.GetDouble(), Is.EqualTo(149.5));
        }

        /// <summary>
        /// The continuation points of one request cannot buffer more processed values than the
        /// session limit; the operation that does not fit gets Bad_NoContinuationPoints.
        /// </summary>
        [Test]
        public async Task ProcessedContinuationPointsOfOneRequestAreLimitedAsync()
        {
            using var harness = new Harness();
            const int outputs = (ServerConfiguration.DefaultMaxHistoryContinuationPointValues / 2) + 1000;
            var first = new HistoryReadResult();
            var second = new HistoryReadResult();

            using (harness.ContinuationPoints.BeginHistoryRequest(
                [new HistoryReadValueId(), new HistoryReadValueId()]))
            {
                await harness.ReadProcessedAsync(
                    0, s_baseTime, s_baseTime.AddMilliseconds(1000.0 * outputs), 1000, first).ConfigureAwait(false);
                await harness.ReadProcessedAsync(
                    0, s_baseTime, s_baseTime.AddMilliseconds(1000.0 * outputs), 1000, second).ConfigureAwait(false);
            }

            Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(first.ContinuationPoint.IsEmpty, Is.False);
            Assert.That(second.StatusCode, Is.EqualTo(StatusCodes.BadNoContinuationPoints));
            Assert.That(second.ContinuationPoint.IsEmpty, Is.True);
        }

        /// <summary>
        /// A later request releases the oldest buffered continuation point when the session limit
        /// is reached, and continuing the newer one still works.
        /// </summary>
        [Test]
        public async Task ProcessedContinuationPointOfEarlierRequestIsReleasedAsync()
        {
            using var harness = new Harness();
            harness.ContinuationPoints.MaxHistoryValues = 5000;
            var first = new HistoryReadResult();
            var second = new HistoryReadResult();

            await harness.ReadProcessedAsync(0, s_baseTime, s_baseTime.AddMilliseconds(3_000_000), 1000, first)
                .ConfigureAwait(false);
            await harness.ReadProcessedAsync(0, s_baseTime, s_baseTime.AddMilliseconds(3_000_000), 1000, second)
                .ConfigureAwait(false);

            Assert.That(first.ContinuationPoint.IsEmpty, Is.False);
            Assert.That(second.ContinuationPoint.IsEmpty, Is.False);

            var resumedFirst = new HistoryReadResult();
            await harness.ReadProcessedAsync(
                0, s_baseTime, s_baseTime.AddMilliseconds(3_000_000), 1000, resumedFirst, first.ContinuationPoint)
                .ConfigureAwait(false);
            Assert.That(resumedFirst.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));

            // the successor of a continued operation shares the buffer and always fits.
            var resumedSecond = new HistoryReadResult();
            await harness.ReadProcessedAsync(
                0, s_baseTime, s_baseTime.AddMilliseconds(3_000_000), 1000, resumedSecond, second.ContinuationPoint)
                .ConfigureAwait(false);
            Assert.That(resumedSecond.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(resumedSecond.ContinuationPoint.IsEmpty, Is.False);
            Assert.That(resumedSecond.HistoryData.TryGetValue(out HistoryData? data), Is.True);
            Assert.That(data!.DataValues, Has.Count.EqualTo(1000));
        }

        /// <summary>
        /// The continuation points of an event history request share the request's filter instead
        /// of keeping a copy each, and successors keep sharing it.
        /// </summary>
        [Test]
        public async Task EventContinuationPointsShareTheRequestFilterAsync()
        {
            var continuationPoints = new SessionContinuationPoints(
                () => NodeId.Null,
                maxBrowse: 10,
                maxHistory: 10,
                store: null);
            ServerSystemContext systemContext = CreateSystemContext(continuationPoints);
            var nodeId = new NodeId("event-filter-shared", 1);
            HistoricalEventAuthorizationTestHelper.RegisterUnrestrictedEventNodes(
                systemContext, [ObjectTypeIds.BaseEventType, nodeId]);
            var node = new BaseObjectState(null) { NodeId = nodeId };
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(p => p.GetCapabilitiesAsync(nodeId, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(HistorianNodeCapabilities.EventReadWrite));
            provider.As<IHistorianEventProvider>()
                .Setup(p => p.ReadEventsAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianEventReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianPage<HistorianEventRecord>>(
                    new HistorianPage<HistorianEventRecord>(
                        [CreateEventRecord(1)],
                        new HistorianResumeToken(ByteString.From([1])))));
            var filter = new EventFilter();
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.EventId, Attributes.Value);
            var details = new ReadEventDetails
            {
                StartTime = s_baseTime,
                EndTime = s_baseTime.AddMilliseconds(60_000),
                NumValuesPerNode = 1,
                Filter = filter
            };

            var tokens = new List<ByteString>();
            for (int ii = 0; ii < 3; ii++)
            {
                var result = new HistoryReadResult();
                ServiceResult error = await HistorianDispatcher.DispatchEventReadAsync(
                    systemContext,
                    provider.Object,
                    node,
                    new HistoryReadValueId { NodeId = nodeId },
                    details,
                    TimestampsToReturn.Source,
                    result,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(error), Is.True);
                Assert.That(result.ContinuationPoint.IsEmpty, Is.False);
                tokens.Add(result.ContinuationPoint);
            }

            // continue the first operation once, so that it has a successor.
            var continued = new HistoryReadResult();
            await HistorianDispatcher.DispatchEventReadAsync(
                systemContext,
                provider.Object,
                node,
                new HistoryReadValueId { NodeId = nodeId, ContinuationPoint = tokens[0] },
                details,
                TimestampsToReturn.Source,
                continued,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(continued.ContinuationPoint.IsEmpty, Is.False);
            tokens[0] = continued.ContinuationPoint;

            foreach (ByteString token in tokens)
            {
                var state = continuationPoints.RestoreHistory(token) as HistorianContinuationState;
                Assert.That(state, Is.Not.Null);
                Assert.That(state!.EventRequest!.Filter, Is.SameAs(filter));
            }
        }

        private static int GetRawValueCount(AggregationFilterHandler handler)
        {
            object calculator = typeof(AggregationFilterHandler)
                .GetField("m_calculator", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(handler)!;
            var values = (LinkedList<DataValue>)typeof(AggregateCalculator)
                .GetField("m_values", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(calculator)!;
            return values.Count;
        }

        private static DataValue CreateValue(int value, double offsetMilliseconds)
        {
            DateTimeUtc timestamp = s_baseTime.AddMilliseconds(offsetMilliseconds);
            return new DataValue(new Variant(value), StatusCodes.Good, timestamp, timestamp);
        }

        private static HistorianEventRecord CreateEventRecord(int value)
        {
            var eventId = ByteString.From([(byte)value]);
            return new HistorianEventRecord(
                eventId,
                ObjectTypeIds.BaseEventType,
                s_baseTime.AddMilliseconds(1000.0 * value),
                new Dictionary<string, Variant>(StringComparer.Ordinal)
                {
                    [BrowseNames.EventId] = new Variant(eventId)
                }.ToArrayOf());
        }

        private static ServerSystemContext CreateSystemContext(
            ISessionContinuationPoints continuationPoints,
            IServerInternal? server = null)
        {
            if (server == null)
            {
                var namespaceUris = new NamespaceTable();
                var mock = new Mock<IServerInternal>();
                mock.Setup(s => s.NamespaceUris).Returns(namespaceUris);
                mock.Setup(s => s.ServerUris).Returns(new StringTable());
                mock.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
                mock.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
                mock.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
                server = mock.Object;
            }
            var session = new Mock<ISession>();
            session.Setup(s => s.ContinuationPoints).Returns(continuationPoints);
            var operationContext = new OperationContext(
                new RequestHeader(),
                null!,
                RequestType.HistoryRead,
                RequestLifetime.None,
                session.Object);
            return new ServerSystemContext(server, operationContext);
        }

        /// <summary>
        /// A server with the standard aggregates, a session and a raw-only historian.
        /// </summary>
        private sealed class Harness : IDisposable
        {
            public Harness()
            {
                var namespaceUris = new NamespaceTable();
                var server = new Mock<IServerInternal>();
                server.Setup(s => s.NamespaceUris).Returns(namespaceUris);
                server.Setup(s => s.ServerUris).Returns(new StringTable());
                server.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
                server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
                server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
                server.Setup(s => s.DiagnosticsNodeManager).Returns(new Mock<IDiagnosticsNodeManager>().Object);
                AggregateManager = new AggregateManager(server.Object);
                server.Setup(s => s.AggregateManager).Returns(AggregateManager);
                ContinuationPoints = new SessionContinuationPoints(
                    () => NodeId.Null,
                    maxBrowse: 10,
                    maxHistory: 100,
                    store: null);
                SystemContext = CreateSystemContext(ContinuationPoints, server.Object);
            }

            public AggregateManager AggregateManager { get; }

            public SessionContinuationPoints ContinuationPoints { get; }

            public ServerSystemContext SystemContext { get; }

            private Mock<IHistorianProvider>? m_provider;

            private int m_rawValueCount;

            public void Dispose()
            {
                AggregateManager.Dispose();
            }

            public async Task<AggregationFilterHandler> CreateHandlerAsync(
                NodeId aggregateId,
                double processingInterval,
                List<DataValue> processed,
                bool primed = false)
            {
                await RegisterAsync(aggregateId).ConfigureAwait(false);
                var filter = new ServerAggregateFilter
                {
                    AggregateType = aggregateId,
                    StartTime = s_baseTime,
                    ProcessingInterval = processingInterval,
                    PrimeInitialValue = primed,
                    AggregateConfiguration = new AggregateConfiguration
                    {
                        PercentDataBad = 100,
                        PercentDataGood = 100,
                        UseSlopedExtrapolation = false,
                        TreatUncertainAsBad = true
                    }
                };
                return new AggregationFilterHandler(
                    AggregateManager,
                    filter,
                    (in DataValue value) => processed.Add(value),
                    primeInitialValue: primed);
            }

            /// <summary>
            /// Reads Average from a raw-only historian whose node holds one value per
            /// millisecond (value = index) from the base time.
            /// </summary>
            public async Task<ServiceResult> ReadProcessedAsync(
                int rawValueCount,
                DateTimeUtc startTime,
                DateTimeUtc endTime,
                double processingInterval,
                HistoryReadResult result,
                ByteString continuationPoint = default)
            {
                await RegisterAsync(ObjectIds.AggregateFunction_Average).ConfigureAwait(false);
                var nodeId = new NodeId("raw-only", 1);
                m_rawValueCount = rawValueCount;
                if (m_provider == null)
                {
                    // one provider for all reads, so that continuation points can be resumed.
                    var provider = new Mock<IHistorianProvider>();
                    provider
                        .Setup(p => p.IsHistorizingAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                        .Returns(new ValueTask<bool>(true));
                    provider
                        .Setup(p => p.GetCapabilitiesAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                        .Returns(new ValueTask<HistorianNodeCapabilities>(HistorianNodeCapabilities.ReadOnly));
                    provider.As<IHistorianDataProvider>()
                        .Setup(p => p.ReadRawAsync(
                            It.IsAny<HistorianOperationContext>(),
                            It.IsAny<HistorianRawReadRequest>(),
                            It.IsAny<HistorianResumeToken>(),
                            It.IsAny<CancellationToken>()))
                        .Returns((
                            HistorianOperationContext _,
                            HistorianRawReadRequest request,
                            HistorianResumeToken token,
                            CancellationToken _) =>
                            new ValueTask<HistorianPage<HistoricalDataValue>>(
                                ReadPage(request, token, m_rawValueCount)));
                    m_provider = provider;
                }
                var node = new BaseDataVariableState(null)
                {
                    NodeId = nodeId,
                    BrowseName = new QualifiedName("RawOnly"),
                    AccessLevel = AccessLevels.HistoryRead,
                    Historizing = true
                };
                return await HistorianDispatcher.DispatchProcessedReadAsync(
                    SystemContext,
                    m_provider!.Object,
                    node,
                    new HistoryReadValueId { NodeId = nodeId, ContinuationPoint = continuationPoint },
                    new ReadProcessedDetails
                    {
                        StartTime = startTime,
                        EndTime = endTime,
                        ProcessingInterval = processingInterval
                    },
                    ObjectIds.AggregateFunction_Average,
                    TimestampsToReturn.Source,
                    result,
                    CancellationToken.None).ConfigureAwait(false);
            }

            private static HistorianPage<HistoricalDataValue> ReadPage(
                HistorianRawReadRequest request,
                HistorianResumeToken token,
                int rawValueCount)
            {
                // pages of 1000 values in time order inside the requested range.
                int first = token.IsEmpty ? 0 : BitConverter.ToInt32(token.State.ToArray(), 0);
                if (!request.IsForward || request.StartTime > s_baseTime)
                {
                    return new HistorianPage<HistoricalDataValue>([]);
                }
                int count = Math.Min(1000, rawValueCount - first);
                var values = new List<HistoricalDataValue>(Math.Max(count, 0));
                for (int ii = first; ii < first + count; ii++)
                {
                    DateTimeUtc timestamp = s_baseTime.AddMilliseconds(ii);
                    if (timestamp > request.EndTime)
                    {
                        break;
                    }
                    values.Add(new HistoricalDataValue(
                        new DataValue(new Variant((double)ii), StatusCodes.Good, timestamp, timestamp)));
                }
                int next = first + count;
                return next < rawValueCount && values.Count == count
                    ? new HistorianPage<HistoricalDataValue>(
                        values.ToArrayOf(),
                        new HistorianResumeToken(ByteString.From(BitConverter.GetBytes(next))))
                    : new HistorianPage<HistoricalDataValue>(values.ToArrayOf());
            }

            private Task RegisterAsync(NodeId aggregateId)
            {
                return AggregateManager.RegisterFactoryAsync(
                    aggregateId,
                    Aggregators.GetNameForStandardAggregate(aggregateId).Name!,
                    Aggregators.CreateStandardCalculator,
                    CancellationToken.None).AsTask();
            }
        }

        private static readonly DateTimeUtc s_baseTime = new(2026, 1, 1, 0, 0, 0);
    }
}
