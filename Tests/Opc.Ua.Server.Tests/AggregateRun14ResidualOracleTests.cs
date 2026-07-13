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
    /// Part 13 oracle tests that reproduce the exact data pattern used by the CTT aggregate
    /// Conformance Units against the reference server (SeedHistoricalNodeAsync): a pure all-Good
    /// linear ramp of 1000 samples spaced 10 s apart (value == sample index). These tests pin the
    /// server's Part 13 behaviour for the residual value families reported by CTT run 14 and record
    /// where the divergence is on the CTT oracle side rather than a server defect:
    /// <list type="bullet">
    /// <item>Sloped interpolation of Float/Double bounds is byte-exact (server matches the CTT
    /// oracle); the CTT "not equal" comparisons only appear for integer-typed nodes.</item>
    /// <item>For integer-typed nodes the interpolated value is rounded to nearest (Convert.ToInt32,
    /// identical in 1.5.378/master378 and HEAD); Part 13 §5.4.3.2.2 does not mandate truncation, so
    /// the CTT oracle truncating toward the earlier integer is a CTT convention difference.</item>
    /// <item>Duration/Percent/WorstQuality2/DurationInState over all-Good data are full-Good; the CTT
    /// expectations assume a configured Bad-data region that the all-Good seed does not contain
    /// (the CTT "No start of bad data configured" warnings), so the mismatch is CTT configuration.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    [Category("Aggregators")]
    [Category("Historian")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class AggregateRun14ResidualOracleTests
    {
        private static readonly DateTimeUtc s_baseTime = new(2026, 1, 1, 0, 0, 0);

        private const double c_interval = 23_800;

        private static DateTimeUtc At(double seconds)
        {
            return s_baseTime.AddMilliseconds(seconds * 1000.0);
        }

        // Linear ramp: value v at t = v*10 s + 1.234 s, all Good (mirrors the reference server seed).
        private static List<DataValue> CreateRamp(BuiltInType type, int count = 40)
        {
            var raw = new List<DataValue>(count + 1);
            for (int v = 0; v <= count; v++)
            {
                DateTimeUtc t = At((v * 10) + 1.234);
                Variant value = type switch
                {
                    BuiltInType.Int32 => new Variant(v),
                    BuiltInType.Float => new Variant((float)v),
                    _ => new Variant((double)v)
                };
                raw.Add(new DataValue(value, StatusCodes.Good, t, t));
            }
            return raw;
        }

        [TestCase("Interpolative", 50.0, 4.8766)]
        [TestCase("StartBound", 50.0, 4.8766)]
        [TestCase("EndBound", 50.0, 7.2566)]
        [TestCase("Interpolative", 57.234, 5.6)]
        public async Task FloatSlopedInterpolationIsExactPart13OracleAsync(
            string aggregateName,
            double startSeconds,
            double expected)
        {
            // For a Double/Float variable the SlopedInterpolation bounding value is the exact linear
            // value on the line value(t) = (t - 1.234)/10. The server reproduces this to full
            // precision, which is why the CTT run-14 comparisons never fail on Float/Double nodes.
            NodeId aggregateId = GetAggregateId(aggregateName);
            List<DataValue> raw = CreateRamp(BuiltInType.Double);
            DateTimeUtc start = At(startSeconds);
            DateTimeUtc end = At(startSeconds + (c_interval / 1000.0));
            AggregateConfiguration configuration = CreateConfiguration();

            List<DataValue> direct = RunDirect(aggregateId, raw, start, end, c_interval, configuration);
            using var harness = new Harness();
            List<DataValue> live = await harness
                .ReadProcessedAsync(aggregateId, raw, start, end, c_interval, configuration)
                .ConfigureAwait(false);

            Assert.That(direct, Has.Count.EqualTo(1));
            Assert.That(live, Has.Count.EqualTo(1));
            Assert.That(
                direct[0].WrappedValue.ConvertToDouble().GetDouble(),
                Is.EqualTo(expected).Within(1e-6),
                "direct value");
            Assert.That(
                live[0].WrappedValue.ConvertToDouble().GetDouble(),
                Is.EqualTo(expected).Within(1e-6),
                "live value");
        }

        [TestCase("Interpolative", 50.0, 5)]
        [TestCase("StartBound", 50.0, 5)]
        [TestCase("Interpolative", 57.234, 6)]
        public void IntegerInterpolationRoundsToNearest(
            string aggregateName,
            double startSeconds,
            int expected)
        {
            // Integer nodes are the only aggregate comparisons that fail in CTT run 14 for the
            // interpolation/bound families. The exact linear value (e.g. 4.8766 or 5.6) is converted
            // to the source Int32 type via Convert.ToInt32, which rounds to nearest (5 and 6 here).
            // Part 13 §5.4.3.2.2 does not specify integer rounding; the CTT oracle truncates toward
            // the earlier integer (4 and 5). The behaviour is unchanged from 1.5.378/master378 and is
            // therefore a CTT convention difference, not a server regression.
            NodeId aggregateId = GetAggregateId(aggregateName);
            List<DataValue> raw = CreateRamp(BuiltInType.Int32);
            DateTimeUtc start = At(startSeconds);
            DateTimeUtc end = At(startSeconds + (c_interval / 1000.0));

            List<DataValue> direct = RunDirect(
                aggregateId, raw, start, end, c_interval, CreateConfiguration());

            Assert.That(direct, Has.Count.EqualTo(1));
            Assert.That(direct[0].WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(expected));
            Assert.That(direct[0].StatusCode.CodeBits, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task AllGoodRampStatusAggregatesAreFullyGoodAsync()
        {
            // The reference server seeds only Good samples. Over three full intervals every
            // status-based aggregate reflects "all Good": DurationGood is the full interval,
            // DurationBad is zero, PercentGood is 100, PercentBad is zero and WorstQuality2 is Good.
            // The CTT run-14 mismatches for these families require a configured Bad-data region that
            // the all-Good seed does not provide, so they are CTT configuration issues, not defects.
            List<DataValue> raw = CreateRamp(BuiltInType.Int32);
            DateTimeUtc start = At(50);
            DateTimeUtc end = At(50 + (3 * (c_interval / 1000.0)));
            AggregateConfiguration configuration = CreateConfiguration();

            await AssertConstantAsync("DurationGood", raw, start, end, c_interval, 23_800.0)
                .ConfigureAwait(false);
            await AssertConstantAsync("DurationBad", raw, start, end, c_interval, 0.0)
                .ConfigureAwait(false);
            await AssertConstantAsync("PercentGood", raw, start, end, c_interval, 100.0)
                .ConfigureAwait(false);
            await AssertConstantAsync("PercentBad", raw, start, end, c_interval, 0.0)
                .ConfigureAwait(false);
            await AssertConstantAsync("DurationInStateNonZero", raw, start, end, c_interval, 23_800.0)
                .ConfigureAwait(false);
            await AssertConstantAsync("DurationInStateZero", raw, start, end, c_interval, 0.0)
                .ConfigureAwait(false);

            List<DataValue> worst = RunDirect(
                GetAggregateId("WorstQuality2"), raw, start, end, c_interval, configuration);
            Assert.That(worst, Has.Count.EqualTo(3));
            foreach (DataValue value in worst)
            {
                Assert.That(value.WrappedValue.TryGetValue(out StatusCode quality), Is.True);
                Assert.That(quality, Is.EqualTo(StatusCodes.Good));
                Assert.That(value.StatusCode.CodeBits, Is.EqualTo(StatusCodes.Good));
            }
        }

        [TestCase("DurationGood")]
        [TestCase("PercentGood")]
        [TestCase("WorstQuality2")]
        [TestCase("StartBound")]
        public async Task BadBoundNotFoundPlaceholderIsNotAggregateInputAsync(string aggregateName)
        {
            List<DataValue> raw = CreateRamp(BuiltInType.Int32, 10);
            DateTimeUtc start = At(-10);
            DateTimeUtc end = At(50);
            AggregateConfiguration configuration = CreateConfiguration();
            NodeId aggregateId = GetAggregateId(aggregateName);

            List<DataValue> expected = RunDirect(
                aggregateId,
                raw,
                start,
                end,
                c_interval,
                configuration);

            var withMissingBound = new List<DataValue>(raw.Count + 1)
            {
                new(
                    Variant.Null,
                    StatusCodes.BadBoundNotFound,
                    sourceTimestamp: start,
                    serverTimestamp: DateTimeUtc.MinValue)
            };
            withMissingBound.AddRange(raw);

            List<DataValue> direct = RunDirect(
                aggregateId,
                withMissingBound,
                start,
                end,
                c_interval,
                configuration);

            using var harness = new Harness();
            List<DataValue> live = await harness
                .ReadProcessedAsync(aggregateId, raw, start, end, c_interval, configuration)
                .ConfigureAwait(false);

            Assert.That(direct, Is.EqualTo(expected), "direct calculator");
            Assert.That(live, Has.Count.EqualTo(expected.Count), "live historian count");
            for (int index = 0; index < expected.Count; index++)
            {
                Assert.That(
                    live[index].WrappedValue,
                    Is.EqualTo(expected[index].WrappedValue),
                    $"live historian value[{index}]");
                Assert.That(
                    live[index].StatusCode,
                    Is.EqualTo(expected[index].StatusCode),
                    $"live historian status[{index}]");
                Assert.That(
                    live[index].SourceTimestamp,
                    Is.EqualTo(expected[index].SourceTimestamp),
                    $"live historian timestamp[{index}]");
            }
            Assert.That(
                live,
                Has.None.Matches<DataValue>(value =>
                    value.StatusCode == StatusCodes.BadBoundNotFound),
                "A synthetic missing-bound marker must never appear in processed results.");
        }

        private static async Task AssertConstantAsync(
            string aggregateName,
            List<DataValue> raw,
            DateTimeUtc start,
            DateTimeUtc end,
            double interval,
            double expected)
        {
            NodeId aggregateId = GetAggregateId(aggregateName);
            AggregateConfiguration configuration = CreateConfiguration();

            List<DataValue> direct = RunDirect(aggregateId, raw, start, end, interval, configuration);
            using var harness = new Harness();
            List<DataValue> live = await harness
                .ReadProcessedAsync(aggregateId, raw, start, end, interval, configuration)
                .ConfigureAwait(false);

            Assert.That(direct, Has.Count.EqualTo(3), aggregateName);
            Assert.That(live, Has.Count.EqualTo(3), aggregateName);
            for (int index = 0; index < direct.Count; index++)
            {
                Assert.That(
                    direct[index].WrappedValue.ConvertToDouble().GetDouble(),
                    Is.EqualTo(expected).Within(1e-6),
                    $"{aggregateName} direct[{index}]");
                Assert.That(
                    live[index].WrappedValue.ConvertToDouble().GetDouble(),
                    Is.EqualTo(expected).Within(1e-6),
                    $"{aggregateName} live[{index}]");
                Assert.That(
                    direct[index].StatusCode.CodeBits,
                    Is.EqualTo(StatusCodes.Good),
                    $"{aggregateName} status[{index}]");
            }
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

            for (int index = 0; index < rawValues.Count; index++)
            {
                Assert.That(calculator.QueueRawValue(rawValues[index]), Is.True, $"raw value {index}");
            }

            var results = new List<DataValue>();
            while (calculator.TryGetProcessedValue(true, out DataValue value))
            {
                results.Add(value);
            }
            return results;
        }

        private static AggregateConfiguration CreateConfiguration()
        {
            return new AggregateConfiguration
            {
                UseServerCapabilitiesDefaults = false,
                TreatUncertainAsBad = false,
                PercentDataBad = 100,
                PercentDataGood = 100,
                UseSlopedExtrapolation = false
            };
        }

        private static NodeId GetAggregateId(string aggregateName)
        {
            return aggregateName switch
            {
                "Interpolative" => ObjectIds.AggregateFunction_Interpolative,
                "StartBound" => ObjectIds.AggregateFunction_StartBound,
                "EndBound" => ObjectIds.AggregateFunction_EndBound,
                "DurationGood" => ObjectIds.AggregateFunction_DurationGood,
                "DurationBad" => ObjectIds.AggregateFunction_DurationBad,
                "PercentGood" => ObjectIds.AggregateFunction_PercentGood,
                "PercentBad" => ObjectIds.AggregateFunction_PercentBad,
                "WorstQuality2" => ObjectIds.AggregateFunction_WorstQuality2,
                "DurationInStateZero" => ObjectIds.AggregateFunction_DurationInStateZero,
                "DurationInStateNonZero" => ObjectIds.AggregateFunction_DurationInStateNonZero,
                _ => throw new ArgumentOutOfRangeException(nameof(aggregateName))
            };
        }

        private sealed class Harness : IDisposable
        {
            public Harness()
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
                QualifiedName aggregateName = Aggregators.GetNameForStandardAggregate(aggregateId);
                await AggregateManager.RegisterFactoryAsync(
                    aggregateId,
                    aggregateName.Name,
                    Aggregators.CreateStandardCalculator,
                    CancellationToken.None).ConfigureAwait(false);

                var nodeId = new NodeId($"run14-aggregate-{Guid.NewGuid():N}", 1);
                Provider.Register(nodeId);
                IList<StatusCode> insertResults = await Provider.InsertAsync(
                    CreateContext(),
                    nodeId,
                    rawValues,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(insertResults, Has.Count.EqualTo(rawValues.Count));
                Assert.That(insertResults, Has.All.Matches<StatusCode>(StatusCode.IsGood));

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
                var result = new HistoryReadResult();

                ServiceResult error = await HistorianDispatcher.DispatchProcessedReadAsync(
                    SystemContext,
                    Provider,
                    node,
                    nodeToRead,
                    details,
                    aggregateId,
                    TimestampsToReturn.Source,
                    result,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(ServiceResult.IsGood(error), Is.True, error.ToString());
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(result.HistoryData.TryGetValue(out HistoryData historyData), Is.True);
                return [.. historyData!.DataValues];
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
    }
}
