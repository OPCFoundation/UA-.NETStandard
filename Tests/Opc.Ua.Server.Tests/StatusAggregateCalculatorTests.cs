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
            DateTimeUtc startTime, double[] values, StatusCode[] statusCodes, double intervalMs = 1000)
        {
            var dataValues = new List<DataValue>();
            for (int i = 0; i < values.Length; i++)
            {
                StatusCode sc = i < statusCodes.Length ? statusCodes[i] : StatusCodes.Good;
                dataValues.Add(new DataValue(
                    new Variant(values[i]),
                    sc,
                    startTime.AddMilliseconds(i * intervalMs),
                    startTime.AddMilliseconds(i * intervalMs)));
            }
            return dataValues;
        }

        private static List<DataValue> CreateGoodDataValues(
            DateTimeUtc startTime, double[] values, double intervalMs = 1000)
        {
            var statusCodes = new StatusCode[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                statusCodes[i] = StatusCodes.Good;
            }
            return CreateDataValues(startTime, values, statusCodes, intervalMs);
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

            foreach (DataValue value in values)
            {
                calculator.QueueRawValue(value);
            }

            var results = new List<DataValue>();
            bool hasData = true;
            while (hasData)
            {
                bool hasResult = calculator.TryGetProcessedValue(true, out DataValue result);
                if (hasResult)
                {
                    results.Add(result);
                }
                else
                {
                    hasData = false;
                }
            }

            return results.Count > 0 ? results[0] : default;
        }

        [Test]
        public void DurationGoodWithAllGoodValuesReturnsFullDuration()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            double[] values = [10, 20, 30, 40, 50, 50];
            List<DataValue> dataValues = CreateGoodDataValues(startTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_DurationGood,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double duration = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(duration, Is.GreaterThan(0));
        }

        [Test]
        public void DurationBadWithAllGoodValuesReturnsZero()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            double[] values = [10, 20, 30, 40, 50, 50];
            List<DataValue> dataValues = CreateGoodDataValues(startTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_DurationBad,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double duration = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(duration, Is.Zero.Within(0.001));
        }

        [Test]
        public void DurationBadWithBadValuesReturnsPositiveDuration()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            double[] values = [10, 20, 30, 40, 50, 50];
            StatusCode[] statusCodes =
            [
                StatusCodes.Good,
                StatusCodes.Bad,
                StatusCodes.Bad,
                StatusCodes.Good,
                StatusCodes.Good,
                StatusCodes.Good
            ];
            List<DataValue> dataValues = CreateDataValues(startTime, values, statusCodes, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_DurationBad,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double duration = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(duration, Is.GreaterThan(0));
        }

        [Test]
        public void PercentGoodWithAllGoodValuesReturns100()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            double[] values = [10, 20, 30, 40, 50, 50];
            List<DataValue> dataValues = CreateGoodDataValues(startTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_PercentGood,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double percent = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(percent, Is.EqualTo(100.0).Within(0.01));
        }

        [Test]
        public void PercentBadWithAllGoodValuesReturnsZero()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            double[] values = [10, 20, 30, 40, 50, 50];
            List<DataValue> dataValues = CreateGoodDataValues(startTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_PercentBad,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double percent = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(percent, Is.Zero.Within(0.01));
        }

        [Test]
        public void PercentBadWithMixedStatusReturnsNonZero()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            double[] values = [10, 20, 30, 40, 50, 50];
            StatusCode[] statusCodes =
            [
                StatusCodes.Good,
                StatusCodes.Bad,
                StatusCodes.Bad,
                StatusCodes.Good,
                StatusCodes.Good,
                StatusCodes.Good
            ];
            List<DataValue> dataValues = CreateDataValues(startTime, values, statusCodes, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_PercentBad,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double percent = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(percent, Is.GreaterThan(0));
            Assert.That(percent, Is.LessThan(100));
        }

        [Test]
        public void WorstQualityWithAllGoodReturnsGood()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            double[] values = [10, 20, 30, 40, 50, 50];
            List<DataValue> dataValues = CreateGoodDataValues(startTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_WorstQuality,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
        }

        [Test]
        public void WorstQualityWithBadValueReturnsBad()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            double[] values = [10, 20, 30, 40, 50, 50];
            StatusCode[] statusCodes =
            [
                StatusCodes.Good,
                StatusCodes.Good,
                StatusCodes.Bad,
                StatusCodes.Good,
                StatusCodes.Good,
                StatusCodes.Good
            ];
            List<DataValue> dataValues = CreateDataValues(startTime, values, statusCodes, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_WorstQuality,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That(result.WrappedValue.TryGetValue(out StatusCode worstQuality), Is.True);
            Assert.That(StatusCode.IsBad(worstQuality), Is.True);
        }

        [Test]
        public void WorstQualityWithMultipleBadValuesReturnsBad()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            double[] values = [10, 20, 30, 40, 50, 50];
            StatusCode[] statusCodes =
            [
                StatusCodes.Good,
                StatusCodes.Bad,
                StatusCodes.Bad,
                StatusCodes.Good,
                StatusCodes.Good,
                StatusCodes.Good
            ];
            List<DataValue> dataValues = CreateDataValues(startTime, values, statusCodes, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_WorstQuality,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That(result.WrappedValue.TryGetValue(out StatusCode worstQuality), Is.True);
            Assert.That(StatusCode.IsBad(worstQuality), Is.True);
        }

        [Test]
        public void WorstQuality2WithBoundsIncludesBoundValues()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            double[] values = [10, 20, 30, 40, 50, 50];
            List<DataValue> dataValues = CreateGoodDataValues(startTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_WorstQuality2,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
        }

        [Test]
        public void WorstQualityWithUncertainValueReturnsUncertain()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            double[] values = [10, 20, 30, 40, 50, 50];
            StatusCode[] statusCodes =
            [
                StatusCodes.Good,
                StatusCodes.Good,
                StatusCodes.Uncertain,
                StatusCodes.Good,
                StatusCodes.Good,
                StatusCodes.Good
            ];
            List<DataValue> dataValues = CreateDataValues(startTime, values, statusCodes, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_WorstQuality,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That(result.WrappedValue.TryGetValue(out StatusCode worstQuality), Is.True);
            Assert.That(StatusCode.IsUncertain(worstQuality), Is.True);
        }

        [Test]
        public void WorstQualityWithMultipleUncertainValuesReturnsUncertain()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            double[] values = [10, 20, 30, 40, 50, 50];
            StatusCode[] statusCodes =
            [
                StatusCodes.Good,
                StatusCodes.Uncertain,
                StatusCodes.Uncertain,
                StatusCodes.Good,
                StatusCodes.Good,
                StatusCodes.Good
            ];
            List<DataValue> dataValues = CreateDataValues(startTime, values, statusCodes, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_WorstQuality,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That(result.WrappedValue.TryGetValue(out StatusCode worstQuality), Is.True);
            Assert.That(StatusCode.IsUncertain(worstQuality), Is.True);
        }
    }
}
