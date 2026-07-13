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

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Value-validated tests for the status-based aggregates defined in OPC UA Part 13 v1.05.07
    /// §5.4.3.31-.36 (DurationGood, DurationBad, PercentGood, PercentBad, WorstQuality,
    /// WorstQuality2).
    /// </summary>
    [TestFixture]
    [Category("Aggregators")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class StatusAggregateCalculatorTests
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

        private static List<DataValue> CreateDataValues(
            DateTimeUtc startTime, (double Value, StatusCode Status)[] items, double intervalMs = 1000)
        {
            var dataValues = new List<DataValue>();
            for (int i = 0; i < items.Length; i++)
            {
                DateTimeUtc t = startTime.AddMilliseconds(i * intervalMs);
                dataValues.Add(new DataValue(new Variant(items[i].Value), items[i].Status, t, t));
            }
            return dataValues;
        }

        private DataValue ComputeAggregate(
            NodeId aggregateId,
            List<DataValue> values,
            DateTimeUtc startTime,
            DateTimeUtc endTime,
            double processingInterval)
        {
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                aggregateId, startTime, endTime, processingInterval, false, m_configuration, m_telemetry);

            if (endTime < startTime)
            {
                for (int ii = values.Count - 1; ii >= 0; ii--)
                {
                    Assert.That(calculator.QueueRawValue(values[ii]), Is.True);
                }
            }
            else
            {
                foreach (DataValue value in values)
                {
                    Assert.That(calculator.QueueRawValue(value), Is.True);
                }
            }

            var results = new List<DataValue>();
            while (calculator.TryGetProcessedValue(true, out DataValue result))
            {
                results.Add(result);
            }

            return results.Count > 0 ? results[0] : default;
        }

        [Test]
        public void WorstQualityAllGoodReturnsGood()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);
            List<DataValue> values = CreateDataValues(
                startTime,
                [
                    (1, StatusCodes.Good),
                    (2, StatusCodes.Good),
                    (3, StatusCodes.Good),
                    (4, StatusCodes.Good),
                    (5, StatusCodes.Good)
                ],
                2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_WorstQuality, values, startTime, endTime, 10000);

            Assert.That(result.WrappedValue.TryGetValue(out StatusCode worst), Is.True);
            Assert.That(StatusCode.IsGood(worst), Is.True);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.StatusCode.AggregateBits & AggregateBits.Calculated,
                Is.EqualTo(AggregateBits.Calculated));
        }

        [Test]
        public void WorstQualityWithBadValuesReturnsBadAndMultipleValuesBit()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            // Bad values placed in the interior so the result is independent of how boundary
            // values are handled.
            List<DataValue> values = CreateDataValues(
                startTime,
                [
                    (1, StatusCodes.Good),
                    (2, StatusCodes.Bad),
                    (3, StatusCodes.Bad),
                    (4, StatusCodes.Good),
                    (5, StatusCodes.Good)
                ],
                1000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_WorstQuality, values, startTime, endTime, 10000);

            Assert.That(result.WrappedValue.TryGetValue(out StatusCode worst), Is.True);
            Assert.That(StatusCode.IsBad(worst), Is.True);
            Assert.That(result.StatusCode.AggregateBits & AggregateBits.MultipleValues,
                Is.EqualTo(AggregateBits.MultipleValues));
        }

        [Test]
        public void WorstQualityWithUncertainValuesReturnsUncertain()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            List<DataValue> values = CreateDataValues(
                startTime,
                [
                    (1, StatusCodes.Good),
                    (2, StatusCodes.Uncertain),
                    (3, StatusCodes.Uncertain),
                    (4, StatusCodes.Good),
                    (5, StatusCodes.Good)
                ],
                1000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_WorstQuality, values, startTime, endTime, 10000);

            Assert.That(result.WrappedValue.TryGetValue(out StatusCode worst), Is.True);
            Assert.That(StatusCode.IsUncertain(worst), Is.True);
        }

        [Test]
        public void WorstQualityPreservesFirstGoodStatusCode()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(2000);
            List<DataValue> values = CreateDataValues(
                startTime,
                [
                    (1, StatusCodes.GoodClamped),
                    (2, StatusCodes.Good),
                    (3, StatusCodes.Good)
                ]);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_WorstQuality, values, startTime, endTime, 2000);

            Assert.That(result.WrappedValue.TryGetValue(out StatusCode worst), Is.True);
            Assert.That(worst, Is.EqualTo(StatusCodes.GoodClamped));
            Assert.That(
                result.StatusCode.AggregateBits,
                Is.EqualTo(AggregateBits.Calculated | AggregateBits.MultipleValues));
        }

        [Test]
        public void WorstQualityPreservesFirstUncertainStatusCodeAfterGood()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(4000);
            List<DataValue> values = CreateDataValues(
                startTime,
                [
                    (1, StatusCodes.GoodClamped),
                    (2, StatusCodes.Good),
                    (3, StatusCodes.UncertainLastUsableValue),
                    (4, StatusCodes.UncertainDataSubNormal),
                    (5, StatusCodes.Good)
                ]);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_WorstQuality, values, startTime, endTime, 4000);

            Assert.That(result.WrappedValue.TryGetValue(out StatusCode worst), Is.True);
            Assert.That(worst, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
            Assert.That(
                result.StatusCode.AggregateBits,
                Is.EqualTo(AggregateBits.Calculated | AggregateBits.MultipleValues));
        }

        [Test]
        public void WorstQualityPreservesFirstBadStatusCodeAfterUncertain()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(6000);
            List<DataValue> values = CreateDataValues(
                startTime,
                [
                    (1, StatusCodes.GoodClamped),
                    (2, StatusCodes.Good),
                    (3, StatusCodes.UncertainLastUsableValue),
                    (4, StatusCodes.UncertainDataSubNormal),
                    (5, StatusCodes.BadOutOfRange),
                    (6, StatusCodes.BadSensorFailure),
                    (7, StatusCodes.Good)
                ]);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_WorstQuality, values, startTime, endTime, 6000);

            Assert.That(result.WrappedValue.TryGetValue(out StatusCode worst), Is.True);
            Assert.That(worst, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(
                result.StatusCode.AggregateBits,
                Is.EqualTo(AggregateBits.Calculated | AggregateBits.MultipleValues));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WorstQuality2IncludesRequestStartAndExcludesRequestEnd(bool reverse)
        {
            var baseTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc startTime = baseTime.AddMilliseconds(reverse ? 8000 : 2000);
            DateTimeUtc endTime = baseTime.AddMilliseconds(reverse ? 2000 : 8000);
            List<DataValue> values = reverse
                ?
                [
                    new DataValue(
                        Variant.From(1),
                        StatusCodes.BadOutOfRange,
                        baseTime.AddMilliseconds(2000)),
                    new DataValue(
                        Variant.From(2),
                        StatusCodes.Good,
                        baseTime.AddMilliseconds(4000)),
                    new DataValue(
                        Variant.From(3),
                        StatusCodes.UncertainLastUsableValue,
                        baseTime.AddMilliseconds(10000))
                ]
                :
                [
                    new DataValue(
                        Variant.From(1),
                        StatusCodes.UncertainLastUsableValue,
                        baseTime),
                    new DataValue(
                        Variant.From(2),
                        StatusCodes.Good,
                        baseTime.AddMilliseconds(4000)),
                    new DataValue(
                        Variant.From(3),
                        StatusCodes.BadSensorFailure,
                        baseTime.AddMilliseconds(8000))
                ];

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_WorstQuality2, values, startTime, endTime, 6000);

            Assert.That(result.WrappedValue.TryGetValue(out StatusCode worst), Is.True);
            Assert.That(worst, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
            Assert.That(result.SourceTimestamp, Is.EqualTo(startTime));
            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Calculated));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WorstQuality2SetsMultipleValuesForRequestStartAndInteriorWorstQuality(bool reverse)
        {
            var earlyTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc lateTime = earlyTime.AddMilliseconds(10000);
            StatusCode earlyStatus = reverse ? StatusCodes.Good : StatusCodes.BadOutOfRange;
            StatusCode lateStatus = reverse ? StatusCodes.BadSensorFailure : StatusCodes.Good;
            List<DataValue> values = CreateDataValues(
                earlyTime,
                [
                    (1, earlyStatus),
                    (2, StatusCodes.BadOutOfRange),
                    (3, lateStatus)
                ],
                5000);
            DateTimeUtc startTime = reverse ? lateTime : earlyTime;
            DateTimeUtc endTime = reverse ? earlyTime : lateTime;
            StatusCode expected = reverse ? StatusCodes.BadSensorFailure : StatusCodes.BadOutOfRange;

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_WorstQuality2, values, startTime, endTime, 10000);

            Assert.That(result.WrappedValue.TryGetValue(out StatusCode worst), Is.True);
            Assert.That(worst, Is.EqualTo(expected));
            Assert.That(
                result.StatusCode.AggregateBits,
                Is.EqualTo(AggregateBits.Calculated | AggregateBits.MultipleValues));
        }

        [Test]
        public void PercentGoodAllGoodReturns100()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            // First value at the interval start so the start bound is a Good raw value.
            List<DataValue> values = CreateDataValues(
                startTime,
                [
                    (1, StatusCodes.Good),
                    (2, StatusCodes.Good),
                    (3, StatusCodes.Good),
                    (4, StatusCodes.Good),
                    (5, StatusCodes.Good)
                ],
                2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_PercentGood, values, startTime, endTime, 10000);

            Assert.That(result.WrappedValue.TryGetValue(out double percent), Is.True);
            Assert.That(percent, Is.EqualTo(100.0).Within(0.0001));
        }

        [Test]
        public void PercentBadAllGoodReturnsZero()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            List<DataValue> values = CreateDataValues(
                startTime,
                [
                    (1, StatusCodes.Good),
                    (2, StatusCodes.Good),
                    (3, StatusCodes.Good),
                    (4, StatusCodes.Good),
                    (5, StatusCodes.Good)
                ],
                2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_PercentBad, values, startTime, endTime, 10000);

            Assert.That(result.WrappedValue.TryGetValue(out double percent), Is.True);
            Assert.That(percent, Is.Zero);
        }

        [Test]
        public void DurationGoodResultIsGoodCalculated()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            List<DataValue> values = CreateDataValues(
                startTime,
                [
                    (1, StatusCodes.Good),
                    (2, StatusCodes.Good),
                    (3, StatusCodes.Good),
                    (4, StatusCodes.Good),
                    (5, StatusCodes.Good)
                ],
                2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_DurationGood, values, startTime, endTime, 10000);

            // Part 13 §5.4.3.31: StatusCode is always Good, Calculated.
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.StatusCode.AggregateBits & AggregateBits.Calculated,
                Is.EqualTo(AggregateBits.Calculated));
            Assert.That(result.WrappedValue.TryGetValue(out double duration), Is.True);
            Assert.That(duration, Is.GreaterThanOrEqualTo(0.0));
        }

        [Test]
        public void WorstQuality2WithBadValuesReturnsBad()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            List<DataValue> values = CreateDataValues(
                startTime,
                [
                    (1, StatusCodes.Good),
                    (2, StatusCodes.Bad),
                    (3, StatusCodes.Bad),
                    (4, StatusCodes.Good),
                    (5, StatusCodes.Good)
                ],
                1000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_WorstQuality2, values, startTime, endTime, 10000);

            Assert.That(result.WrappedValue.TryGetValue(out StatusCode worst), Is.True);
            Assert.That(StatusCode.IsBad(worst), Is.True);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public void DurationBadAllGoodReturnsZero()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            List<DataValue> values = CreateDataValues(
                startTime,
                [
                    (1, StatusCodes.Good),
                    (2, StatusCodes.Good),
                    (3, StatusCodes.Good),
                    (4, StatusCodes.Good),
                    (5, StatusCodes.Good)
                ],
                2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_DurationBad, values, startTime, endTime, 10000);

            // Part 13 §5.4.3.32: StatusCode is always Good, Calculated; no Bad data → duration 0.
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out double duration), Is.True);
            Assert.That(duration, Is.Zero);
        }
    }
}
