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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Part 11 and Part 13 oracle tests for aggregate scenarios exercised by the CTT aggregate units.
    /// </summary>
    [TestFixture]
    [Category("Aggregators")]
    [Category("Historian")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class AggregateCttRegressionTests
    {
        [TestCase("Minimum", false)]
        [TestCase("Maximum", false)]
        [TestCase("Range", false)]
        [TestCase("TimeAverage", false)]
        [TestCase("Minimum", true)]
        [TestCase("Maximum", true)]
        [TestCase("Range", true)]
        [TestCase("TimeAverage", true)]
        public async Task DirectAndLiveTenIntervalResultsMatchPart13OracleAsync(
            string aggregateName,
            bool reverse)
        {
            NodeId aggregateId = GetAggregateId(aggregateName);
            List<DataValue> rawValues = CreateSharedRawValues();
            DateTimeUtc startTime = reverse ? AtSeconds(100) : s_baseTime;
            DateTimeUtc endTime = reverse ? s_baseTime : AtSeconds(100);
            AggregateConfiguration configuration = CreateConfiguration();

            List<DataValue> direct = RunDirect(
                aggregateId,
                rawValues,
                startTime,
                endTime,
                10_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                aggregateId,
                rawValues,
                startTime,
                endTime,
                10_000,
                configuration).ConfigureAwait(false);

            AssertSharedTenIntervalResults(direct, aggregateName, reverse);
            AssertSharedTenIntervalResults(live, aggregateName, reverse);
        }

        [TestCase("Minimum", 0.0)]
        [TestCase("Maximum", 19.0)]
        [TestCase("Range", 19.0)]
        [TestCase("TimeAverage", 9.75)]
        public async Task DirectAndLiveIntervalEqualToRangeReturnsOneValueAsync(
            string aggregateName,
            double expected)
        {
            NodeId aggregateId = GetAggregateId(aggregateName);
            List<DataValue> rawValues = CreateSharedRawValues();
            AggregateConfiguration configuration = CreateConfiguration();
            DateTimeUtc endTime = AtSeconds(100);

            List<DataValue> direct = RunDirect(
                aggregateId,
                rawValues,
                s_baseTime,
                endTime,
                100_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                aggregateId,
                rawValues,
                s_baseTime,
                endTime,
                100_000,
                configuration).ConfigureAwait(false);

            AssertSingleNumericResult(direct, expected, s_baseTime, AggregateBits.Calculated);
            AssertSingleNumericResult(live, expected, s_baseTime, AggregateBits.Calculated);
        }

        [TestCase("Start", false, 0.0, 0, AggregateBits.Raw)]
        [TestCase("End", false, 5.0, 5, AggregateBits.Raw)]
        [TestCase("StartBound", false, 0.0, 0, AggregateBits.Raw)]
        [TestCase("EndBound", false, 10.0, 0, AggregateBits.Calculated)]
        [TestCase("Start", true, 5.0, 5, AggregateBits.Raw)]
        [TestCase("End", true, 10.0, 10, AggregateBits.Raw)]
        [TestCase("StartBound", true, 10.0, 10, AggregateBits.Raw)]
        [TestCase("EndBound", true, 0.0, 10, AggregateBits.Calculated)]
        public async Task DirectAndLiveStartEndFamiliesMatchPart13OracleAsync(
            string aggregateName,
            bool reverse,
            double expected,
            int expectedTimestampSeconds,
            AggregateBits expectedBits)
        {
            NodeId aggregateId = GetAggregateId(aggregateName);
            List<DataValue> rawValues =
            [
                CreateValue(0, StatusCodes.Good, 0),
                CreateValue(5, StatusCodes.Good, 5),
                CreateValue(10, StatusCodes.Good, 10)
            ];
            DateTimeUtc startTime = reverse ? AtSeconds(10) : s_baseTime;
            DateTimeUtc endTime = reverse ? s_baseTime : AtSeconds(10);
            AggregateConfiguration configuration = CreateConfiguration();

            List<DataValue> direct = RunDirect(
                aggregateId,
                rawValues,
                startTime,
                endTime,
                10_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                aggregateId,
                rawValues,
                startTime,
                endTime,
                10_000,
                configuration).ConfigureAwait(false);

            DateTimeUtc expectedTimestamp = AtSeconds(expectedTimestampSeconds);
            AssertSingleNumericResult(direct, expected, expectedTimestamp, expectedBits);
            AssertSingleNumericResult(live, expected, expectedTimestamp, expectedBits);
        }

        [TestCase("PercentGood", false, 50.0)]
        [TestCase("PercentBad", false, 50.0)]
        [TestCase("PercentGood", true, 25.0)]
        [TestCase("PercentBad", true, 75.0)]
        public async Task DirectAndLivePercentAggregatesHonorExplicitUncertainConfigurationAsync(
            string aggregateName,
            bool treatUncertainAsBad,
            double expected)
        {
            NodeId aggregateId = GetAggregateId(aggregateName);
            List<DataValue> rawValues =
            [
                CreateValue(1, StatusCodes.Good, 0),
                CreateValue(1, StatusCodes.Uncertain, 5),
                CreateValue(1, StatusCodes.Bad, 10),
                CreateValue(1, StatusCodes.Bad, 15),
                CreateValue(1, StatusCodes.Good, 20)
            ];
            AggregateConfiguration configuration = CreateConfiguration(treatUncertainAsBad);
            DateTimeUtc endTime = AtSeconds(20);

            List<DataValue> direct = RunDirect(
                aggregateId,
                rawValues,
                s_baseTime,
                endTime,
                20_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                aggregateId,
                rawValues,
                s_baseTime,
                endTime,
                20_000,
                configuration).ConfigureAwait(false);

            AssertSingleNumericResult(direct, expected, s_baseTime, AggregateBits.Calculated);
            AssertSingleNumericResult(live, expected, s_baseTime, AggregateBits.Calculated);
        }

        [TestCase("WorstQuality")]
        [TestCase("WorstQuality2")]
        public async Task DirectAndLiveWorstQualitySetMultipleValuesForRepeatedGoodQualityAsync(
            string aggregateName)
        {
            NodeId aggregateId = GetAggregateId(aggregateName);
            List<DataValue> rawValues =
            [
                CreateValue(1, StatusCodes.Good, 0),
                CreateValue(2, StatusCodes.Good, 5),
                CreateValue(3, StatusCodes.Good, 10)
            ];
            AggregateConfiguration configuration = CreateConfiguration();
            DateTimeUtc endTime = AtSeconds(10);

            List<DataValue> direct = RunDirect(
                aggregateId,
                rawValues,
                s_baseTime,
                endTime,
                10_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                aggregateId,
                rawValues,
                s_baseTime,
                endTime,
                10_000,
                configuration).ConfigureAwait(false);

            AssertWorstQualityResult(direct);
            AssertWorstQualityResult(live);
        }

        [TestCase("DurationInStateZero", false)]
        [TestCase("DurationInStateNonZero", false)]
        [TestCase("DurationInStateZero", true)]
        [TestCase("DurationInStateNonZero", true)]
        public async Task DirectAndLiveDurationInStateMatchPart13OracleAsync(
            string aggregateName,
            bool reverse)
        {
            NodeId aggregateId = GetAggregateId(aggregateName);
            List<DataValue> rawValues =
            [
                CreateValue(0, StatusCodes.Good, 0),
                CreateValue(1, StatusCodes.Good, 5),
                CreateValue(0, StatusCodes.Good, 10),
                CreateValue(2, StatusCodes.Good, 15),
                CreateValue(0, StatusCodes.Good, 20)
            ];
            DateTimeUtc startTime = reverse ? AtSeconds(20) : s_baseTime;
            DateTimeUtc endTime = reverse ? s_baseTime : AtSeconds(20);
            AggregateConfiguration configuration = CreateConfiguration();

            List<DataValue> direct = RunDirect(
                aggregateId,
                rawValues,
                startTime,
                endTime,
                20_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                aggregateId,
                rawValues,
                startTime,
                endTime,
                20_000,
                configuration).ConfigureAwait(false);

            DateTimeUtc expectedTimestamp = reverse ? AtSeconds(20) : s_baseTime;
            AssertSingleNumericResult(direct, 10_000, expectedTimestamp, AggregateBits.Calculated);
            AssertSingleNumericResult(live, 10_000, expectedTimestamp, AggregateBits.Calculated);
        }

        [Test]
        public async Task LiveProcessedReadWithEqualTimesReturnsBadInvalidArgumentAsync()
        {
            using var harness = new AggregateHarness();
            var result = new HistoryReadResult();

            ServiceResult error = await harness.DispatchProcessedAsync(
                ObjectIds.AggregateFunction_Average,
                [],
                s_baseTime,
                s_baseTime,
                1,
                CreateConfiguration(),
                result).ConfigureAwait(false);

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        private static void AssertSharedTenIntervalResults(
            List<DataValue> results,
            string aggregateName,
            bool reverse)
        {
            Assert.That(results, Has.Count.EqualTo(10));

            for (int index = 0; index < results.Count; index++)
            {
                int interval = reverse ? 9 - index : index;
                double expected = aggregateName switch
                {
                    "Minimum" => interval,
                    "Maximum" => reverse ? interval + 11 : interval + 10,
                    "Range" => reverse ? 11 : 10,
                    "TimeAverage" => interval + 5.25,
                    _ => throw new ArgumentOutOfRangeException(nameof(aggregateName))
                };
                DateTimeUtc timestamp = reverse
                    ? AtSeconds(100 - (index * 10))
                    : AtSeconds(index * 10);
                AggregateBits expectedBits =
                    aggregateName == "Maximum" && !reverse
                        ? AggregateBits.Raw
                        : AggregateBits.Calculated;

                AssertNumericResult(results[index], expected, timestamp, expectedBits, index);
            }
        }

        private static void AssertSingleNumericResult(
            List<DataValue> results,
            double expected,
            DateTimeUtc expectedTimestamp,
            AggregateBits expectedBits)
        {
            Assert.That(results, Has.Count.EqualTo(1));
            AssertNumericResult(results[0], expected, expectedTimestamp, expectedBits, 0);
        }

        private static void AssertNumericResult(
            DataValue result,
            double expected,
            DateTimeUtc expectedTimestamp,
            AggregateBits expectedBits,
            int index)
        {
            Assert.That(
                result.WrappedValue.ConvertToDouble().GetDouble(),
                Is.EqualTo(expected).Within(0.000_001),
                $"value at index {index}");
            Assert.That(result.SourceTimestamp, Is.EqualTo(expectedTimestamp), $"timestamp at index {index}");
            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.Good), $"status at index {index}");
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(expectedBits), $"aggregate bits at index {index}");
        }

        private static void AssertWorstQualityResult(List<DataValue> results)
        {
            Assert.That(results, Has.Count.EqualTo(1));
            DataValue result = results[0];
            Assert.That(result.WrappedValue.TryGetValue(out StatusCode worstQuality), Is.True);
            Assert.That(worstQuality, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.SourceTimestamp, Is.EqualTo(s_baseTime));
            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.Good));
            Assert.That(
                result.StatusCode.AggregateBits,
                Is.EqualTo(AggregateBits.Calculated | AggregateBits.MultipleValues));
        }

        private static List<DataValue> RunDirect(
            NodeId aggregateId,
            List<DataValue> rawValues,
            DateTimeUtc startTime,
            DateTimeUtc endTime,
            double processingInterval,
            AggregateConfiguration configuration)
        {
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                aggregateId,
                startTime,
                endTime,
                processingInterval,
                false,
                configuration,
                NUnitTelemetryContext.Create())!;

            if (endTime < startTime)
            {
                for (int index = rawValues.Count - 1; index >= 0; index--)
                {
                    Assert.That(calculator.QueueRawValue(rawValues[index]), Is.True, $"raw value {index}");
                }
            }
            else
            {
                for (int index = 0; index < rawValues.Count; index++)
                {
                    Assert.That(calculator.QueueRawValue(rawValues[index]), Is.True, $"raw value {index}");
                }
            }

            var results = new List<DataValue>();
            while (calculator.TryGetProcessedValue(true, out DataValue value))
            {
                results.Add(value);
            }
            return results;
        }

        private static List<DataValue> CreateSharedRawValues()
        {
            var values = new List<DataValue>(21);
            for (int interval = 0; interval < 10; interval++)
            {
                values.Add(CreateValue(interval + 10, StatusCodes.Good, interval * 10));
                values.Add(CreateValue(interval, StatusCodes.Good, (interval * 10) + 5));
            }
            values.Add(CreateValue(20, StatusCodes.Good, 100));
            return values;
        }

        private static DataValue CreateValue(double value, StatusCode statusCode, int timestampSeconds)
        {
            DateTimeUtc timestamp = AtSeconds(timestampSeconds);
            return new DataValue(Variant.From(value), statusCode, timestamp, timestamp);
        }

        private static DateTimeUtc AtSeconds(int seconds)
        {
            return s_baseTime.AddMilliseconds(seconds * 1000);
        }

        private static AggregateConfiguration CreateConfiguration(bool treatUncertainAsBad = false)
        {
            return new AggregateConfiguration
            {
                UseServerCapabilitiesDefaults = false,
                TreatUncertainAsBad = treatUncertainAsBad,
                PercentDataBad = 100,
                PercentDataGood = 100,
                UseSlopedExtrapolation = false
            };
        }

        private static NodeId GetAggregateId(string aggregateName)
        {
            return aggregateName switch
            {
                "Minimum" => ObjectIds.AggregateFunction_Minimum,
                "Maximum" => ObjectIds.AggregateFunction_Maximum,
                "Range" => ObjectIds.AggregateFunction_Range,
                "TimeAverage" => ObjectIds.AggregateFunction_TimeAverage,
                "Start" => ObjectIds.AggregateFunction_Start,
                "End" => ObjectIds.AggregateFunction_End,
                "StartBound" => ObjectIds.AggregateFunction_StartBound,
                "EndBound" => ObjectIds.AggregateFunction_EndBound,
                "PercentGood" => ObjectIds.AggregateFunction_PercentGood,
                "PercentBad" => ObjectIds.AggregateFunction_PercentBad,
                "WorstQuality" => ObjectIds.AggregateFunction_WorstQuality,
                "WorstQuality2" => ObjectIds.AggregateFunction_WorstQuality2,
                "DurationInStateZero" => ObjectIds.AggregateFunction_DurationInStateZero,
                "DurationInStateNonZero" => ObjectIds.AggregateFunction_DurationInStateNonZero,
                _ => throw new ArgumentOutOfRangeException(nameof(aggregateName))
            };
        }

        private sealed class AggregateHarness : IDisposable
        {
            public AggregateHarness()
            {
                Provider = new InMemoryHistorianProvider();
                Telemetry = NUnitTelemetryContext.Create();

                var diagnostics = new Mock<IDiagnosticsNodeManager>();
                var session = new Mock<ISession>();
                MockServer = new Mock<IServerInternal>();
                MockServer.Setup(server => server.NamespaceUris).Returns(new NamespaceTable());
                MockServer.Setup(server => server.ServerUris).Returns(new StringTable());
                MockServer.Setup(server => server.TypeTree).Returns(new TypeTable(new NamespaceTable()));
                MockServer.Setup(server => server.Factory).Returns(EncodeableFactory.Create());
                MockServer.Setup(server => server.Telemetry).Returns(Telemetry);
                MockServer.Setup(server => server.DiagnosticsNodeManager).Returns(diagnostics.Object);

                AggregateManager = new AggregateManager(MockServer.Object);
                MockServer.Setup(server => server.AggregateManager).Returns(AggregateManager);

                var operationContext = new OperationContext(
                    new RequestHeader(),
                    null!,
                    RequestType.HistoryRead,
                    RequestLifetime.None,
                    session.Object);
                SystemContext = new ServerSystemContext(MockServer.Object, operationContext);
            }

            public AggregateManager AggregateManager { get; }
            public Mock<IServerInternal> MockServer { get; }
            public InMemoryHistorianProvider Provider { get; }
            public ServerSystemContext SystemContext { get; }
            public ITelemetryContext Telemetry { get; }

            public void Dispose()
            {
                AggregateManager.Dispose();
                Provider.Dispose();
            }

            public async Task<List<DataValue>> ReadProcessedAsync(
                NodeId aggregateId,
                List<DataValue> rawValues,
                DateTimeUtc startTime,
                DateTimeUtc endTime,
                double processingInterval,
                AggregateConfiguration configuration)
            {
                var result = new HistoryReadResult();
                ServiceResult error = await DispatchProcessedAsync(
                    aggregateId,
                    rawValues,
                    startTime,
                    endTime,
                    processingInterval,
                    configuration,
                    result).ConfigureAwait(false);

                Assert.That(ServiceResult.IsGood(error), Is.True, error.ToString());
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(result.HistoryData.TryGetValue(out HistoryData historyData), Is.True);
                return [.. historyData!.DataValues];
            }

            public async Task<ServiceResult> DispatchProcessedAsync(
                NodeId aggregateId,
                List<DataValue> rawValues,
                DateTimeUtc startTime,
                DateTimeUtc endTime,
                double processingInterval,
                AggregateConfiguration configuration,
                HistoryReadResult result)
            {
                QualifiedName aggregateName = Aggregators.GetNameForStandardAggregate(aggregateId);
                await AggregateManager.RegisterFactoryAsync(
                    aggregateId,
                    aggregateName.Name,
                    Aggregators.CreateStandardCalculator,
                    CancellationToken.None).ConfigureAwait(false);

                var nodeId = new NodeId($"ctt-aggregate-{Guid.NewGuid():N}", 1);
                Provider.Register(nodeId);
                if (rawValues.Count > 0)
                {
                    IList<StatusCode> insertResults = await Provider.InsertAsync(
                        CreateContext(),
                        nodeId,
                        rawValues,
                        CancellationToken.None).ConfigureAwait(false);
                    Assert.That(insertResults, Has.Count.EqualTo(rawValues.Count));
                    Assert.That(insertResults, Has.All.Matches<StatusCode>(StatusCode.IsGood));
                }

                var node = new BaseDataVariableState(null)
                {
                    NodeId = nodeId,
                    BrowseName = new QualifiedName("AggregateVariable"),
                    AccessLevel = AccessLevels.HistoryRead,
                    Historizing = true
                };
                var details = new ReadProcessedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    ProcessingInterval = processingInterval,
                    AggregateConfiguration = configuration
                };
                var nodeToRead = new HistoryReadValueId
                {
                    NodeId = nodeId,
                    ContinuationPoint = ByteString.Empty
                };

                return await HistorianDispatcher.DispatchProcessedReadAsync(
                    SystemContext,
                    Provider,
                    node,
                    nodeToRead,
                    details,
                    aggregateId,
                    TimestampsToReturn.Source,
                    result,
                    CancellationToken.None).ConfigureAwait(false);
            }

            private HistorianOperationContext CreateContext()
            {
                return new HistorianOperationContext(
                    SystemContext,
                    SystemContext.OperationContext!,
                    null,
                    HistoryUpdateType.Insert);
            }
        }

        private static readonly DateTimeUtc s_baseTime = new(2025, 1, 1, 0, 0, 0);
    }
}
