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

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Edge-case tests for <see cref="StartEndAggregateCalculator"/> covering bad-data handling,
    /// empty slices, non-castable values and the base-class fall-through paths.
    /// </summary>
    [TestFixture]
    [Category("Aggregators")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class StartEndAggregateCalculatorEdgeTests
    {
        private ITelemetryContext m_telemetry;
        private AggregateConfiguration m_configuration;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_configuration = new AggregateConfiguration
            {
                TreatUncertainAsBad = false,
                PercentDataBad = 100,
                PercentDataGood = 100,
                UseSlopedExtrapolation = false
            };
        }

        private static DataValue Value(double value, StatusCode status, DateTimeUtc timestamp)
        {
            return new DataValue(new Variant(value), status, timestamp, timestamp);
        }

        private DataValue RunFirst(IAggregateCalculator calculator, IEnumerable<DataValue> values)
        {
            foreach (DataValue value in values)
            {
                calculator.QueueRawValue(value);
            }

            DataValue result = default;
            bool any = false;
            while (calculator.TryGetProcessedValue(true, out DataValue value))
            {
                if (!any)
                {
                    result = value;
                    any = true;
                }
            }

            return result;
        }

        private DataValue ComputeStandard(
            NodeId aggregateId, List<DataValue> values, DateTimeUtc startTime, DateTimeUtc endTime, double interval)
        {
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                aggregateId, startTime, endTime, interval, false, m_configuration, m_telemetry);
            return RunFirst(calculator, values);
        }

        [Test]
        public void DeltaWithLeadingBadDataMarksUncertain()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc t0 = startTime.AddMilliseconds(500);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            var values = new List<DataValue>
            {
                Value(1.0, StatusCodes.Bad, t0),
                Value(10.0, StatusCodes.Good, t0.AddMilliseconds(2000)),
                Value(20.0, StatusCodes.Good, t0.AddMilliseconds(4000)),
                Value(30.0, StatusCodes.Good, t0.AddMilliseconds(6000))
            };

            DataValue result = ComputeStandard(
                ObjectIds.AggregateFunction_Delta, values, startTime, endTime, 10000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(20.0).Within(0.0001));
        }

        [Test]
        public void DeltaWithAllBadDataReturnsNoData()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc t0 = startTime.AddMilliseconds(500);
            DateTimeUtc endTime = startTime.AddMilliseconds(6000);

            var values = new List<DataValue>
            {
                Value(1.0, StatusCodes.Bad, t0),
                Value(2.0, StatusCodes.Bad, t0.AddMilliseconds(2000)),
                Value(3.0, StatusCodes.Bad, t0.AddMilliseconds(4000))
            };

            DataValue result = ComputeStandard(
                ObjectIds.AggregateFunction_Delta, values, startTime, endTime, 6000);

            Assert.That(StatusCode.IsBad(result.StatusCode), Is.True);
        }

        [Test]
        public void DeltaBoundsWithBadBoundReturnsNoData()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc t0 = startTime.AddMilliseconds(500);
            DateTimeUtc endTime = startTime.AddMilliseconds(6000);

            var values = new List<DataValue>
            {
                Value(10.0, StatusCodes.Bad, t0),
                Value(20.0, StatusCodes.Bad, t0.AddMilliseconds(2000))
            };

            DataValue result = ComputeStandard(
                ObjectIds.AggregateFunction_DeltaBounds, values, startTime, endTime, 6000);

            Assert.That(StatusCode.IsBad(result.StatusCode), Is.True);
        }

        [Test]
        public void StartEndCalculatorWithUnhandledNumericIdFallsBackToBaseInterpolation()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            // Count is not a Start/End aggregate; the switch default falls back to base.ComputeValue.
            var calculator = new StartEndAggregateCalculator(
                ObjectIds.AggregateFunction_Count, startTime, endTime, 5000, false, m_configuration, m_telemetry);

            var values = new List<DataValue>
            {
                Value(10.0, StatusCodes.Good, startTime.AddMilliseconds(1000)),
                Value(20.0, StatusCodes.Good, startTime.AddMilliseconds(6000))
            };

            DataValue result = RunFirst(calculator, values);

            Assert.That(result.IsNull, Is.False);
        }

        [Test]
        public void StartEndCalculatorWithNonNumericIdFallsBackToBaseInterpolation()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            // A non-numeric aggregate id causes AggregateId.TryGetValue(out uint) to fail.
            var calculator = new StartEndAggregateCalculator(
                new NodeId("CustomAggregate", 1), startTime, endTime, 5000, false, m_configuration, m_telemetry);

            var values = new List<DataValue>
            {
                Value(10.0, StatusCodes.Good, startTime.AddMilliseconds(1000)),
                Value(20.0, StatusCodes.Good, startTime.AddMilliseconds(6000))
            };

            DataValue fallbackResult = RunFirst(calculator, values);

            Assert.That(fallbackResult.IsNull, Is.False);
        }

        private List<DataValue> RunAll(IAggregateCalculator calculator, IEnumerable<DataValue> values)
        {
            foreach (DataValue value in values)
            {
                calculator.QueueRawValue(value);
            }

            var results = new List<DataValue>();
            while (calculator.TryGetProcessedValue(true, out DataValue value))
            {
                results.Add(value);
            }

            return results;
        }

        private List<DataValue> RunAllStandard(
            NodeId aggregateId, List<DataValue> values, DateTimeUtc startTime, DateTimeUtc endTime, double interval)
        {
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                aggregateId, startTime, endTime, interval, false, m_configuration, m_telemetry);
            return RunAll(calculator, values);
        }

        [Test]
        public void StartAggregateReturnsFirstGoodValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc t0 = startTime.AddMilliseconds(500);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            var values = new List<DataValue>
            {
                Value(10.0, StatusCodes.Good, t0),
                Value(20.0, StatusCodes.Good, t0.AddMilliseconds(2000)),
                Value(30.0, StatusCodes.Good, t0.AddMilliseconds(4000))
            };

            DataValue result = ComputeStandard(
                ObjectIds.AggregateFunction_Start, values, startTime, endTime, 10000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(10.0).Within(0.0001));
        }

        [Test]
        public void EndAggregateReturnsLastGoodValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc t0 = startTime.AddMilliseconds(500);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            var values = new List<DataValue>
            {
                Value(10.0, StatusCodes.Good, t0),
                Value(20.0, StatusCodes.Good, t0.AddMilliseconds(2000)),
                Value(30.0, StatusCodes.Good, t0.AddMilliseconds(4000))
            };

            DataValue result = ComputeStandard(
                ObjectIds.AggregateFunction_End, values, startTime, endTime, 10000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(30.0).Within(0.0001));
        }

        [Test]
        public void DeltaAggregateComputesDifferenceForGoodData()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc t0 = startTime.AddMilliseconds(500);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            var values = new List<DataValue>
            {
                Value(5.0, StatusCodes.Good, t0),
                Value(15.0, StatusCodes.Good, t0.AddMilliseconds(2000)),
                Value(35.0, StatusCodes.Good, t0.AddMilliseconds(4000))
            };

            DataValue result = ComputeStandard(
                ObjectIds.AggregateFunction_Delta, values, startTime, endTime, 10000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(30.0).Within(0.0001));
        }

        [Test]
        public void StartAndEndAggregatesReturnNoDataForEmptySlices()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            // Values only in the first slice; trailing slices are empty and must return NoData.
            var values = new List<DataValue>
            {
                Value(10.0, StatusCodes.Good, startTime.AddMilliseconds(200)),
                Value(20.0, StatusCodes.Good, startTime.AddMilliseconds(400))
            };

            List<DataValue> startResults = RunAllStandard(
                ObjectIds.AggregateFunction_Start, values, startTime, endTime, 2000);
            List<DataValue> deltaResults = RunAllStandard(
                ObjectIds.AggregateFunction_Delta, values, startTime, endTime, 2000);

            Assert.That(startResults.Exists(r => StatusCode.IsBad(r.StatusCode)), Is.True);
            Assert.That(deltaResults.Exists(r => StatusCode.IsBad(r.StatusCode)), Is.True);
        }

        [Test]
        public void DeltaWithNonNumericGoodValueReturnsNoData()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc t0 = startTime.AddMilliseconds(500);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            var values = new List<DataValue>
            {
                new DataValue(new Variant("not-a-number"), StatusCodes.Good, t0, t0),
                new DataValue(
                    new Variant("also-bad"),
                    StatusCodes.Good,
                    t0.AddMilliseconds(2000),
                    t0.AddMilliseconds(2000))
            };

            DataValue result = ComputeStandard(
                ObjectIds.AggregateFunction_Delta, values, startTime, endTime, 10000);

            Assert.That(StatusCode.IsBad(result.StatusCode), Is.True);
        }

        [Test]
        public void StartBoundAggregateReturnsStartValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            var values = new List<DataValue>
            {
                Value(10.0, StatusCodes.Good, startTime),
                Value(20.0, StatusCodes.Good, startTime.AddMilliseconds(5000)),
                Value(30.0, StatusCodes.Good, endTime)
            };

            DataValue result = ComputeStandard(
                ObjectIds.AggregateFunction_StartBound, values, startTime, endTime, 10000);

            Assert.That(result.IsNull, Is.False);
        }

        [Test]
        public void EndBoundAggregateReturnsCalculatedEndValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            var values = new List<DataValue>
            {
                Value(10.0, StatusCodes.Good, startTime),
                Value(20.0, StatusCodes.Good, startTime.AddMilliseconds(5000)),
                Value(30.0, StatusCodes.Good, endTime)
            };

            DataValue result = ComputeStandard(
                ObjectIds.AggregateFunction_EndBound, values, startTime, endTime, 10000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Calculated), Is.True);
        }

        [Test]
        public void DeltaBoundsAggregateComputesDifference()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            var values = new List<DataValue>
            {
                Value(5.0, StatusCodes.Good, startTime),
                Value(15.0, StatusCodes.Good, startTime.AddMilliseconds(5000)),
                Value(25.0, StatusCodes.Good, endTime)
            };

            DataValue result = ComputeStandard(
                ObjectIds.AggregateFunction_DeltaBounds, values, startTime, endTime, 10000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(20.0).Within(0.0001));
        }

        [Test]
        public void BoundAggregatesReturnNoDataForEmptySlices()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(4000);

            // No raw values at all: every bound slice must return NoData.
            var values = new List<DataValue>();

            List<DataValue> startBound = RunAllStandard(
                ObjectIds.AggregateFunction_StartBound, values, startTime, endTime, 2000);
            List<DataValue> deltaBounds = RunAllStandard(
                ObjectIds.AggregateFunction_DeltaBounds, values, startTime, endTime, 2000);

            Assert.That(startBound.TrueForAll(r => StatusCode.IsNotGood(r.StatusCode)), Is.True);
            Assert.That(deltaBounds.TrueForAll(r => StatusCode.IsNotGood(r.StatusCode)), Is.True);
        }
    }
}
