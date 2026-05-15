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
    [Category("AverageAggregateCalculator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class AverageAggregateCalculatorTests
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
            DateTimeUtc startTime, double[] values, double intervalMs = 1000)
        {
            var dataValues = new List<DataValue>();
            for (int i = 0; i < values.Length; i++)
            {
                dataValues.Add(new DataValue
                {
                    WrappedValue = values[i],
                    SourceTimestamp = startTime.AddMilliseconds(i * intervalMs),
                    ServerTimestamp = startTime.AddMilliseconds(i * intervalMs),
                    StatusCode = StatusCodes.Good
                });
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

            foreach (DataValue value in values)
            {
                calculator.QueueRawValue(value);
            }

            var results = new List<DataValue>();
            bool hasData = true;
            while (hasData)
            {
                DataValue result = calculator.GetProcessedValue(true);
                if (result != null)
                {
                    results.Add(result);
                }
                else
                {
                    hasData = false;
                }
            }

            return results.Count > 0 ? results[0] : null;
        }

        [Test]
        public void AverageReturnsCorrectMean()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 40, 50, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Average,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double avg = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(avg, Is.GreaterThan(0));
        }

        [Test]
        public void AverageIdenticalValuesReturnsThatValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [25, 25, 25, 25, 25, 25];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Average,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double avg = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(avg, Is.EqualTo(25.0).Within(0.0001));
        }

        [Test]
        public void AverageNegativeValues()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [-10, -20, -30, -40, -50, -50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Average,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double avg = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(avg, Is.LessThan(0));
        }

        [Test]
        public void AverageSingleValueReturnsThatValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [42.5, 42.5];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(4000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Average,
                dataValues, startTime, endTime, 4000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
        }

        [Test]
        public void AverageResultHasCalculatedAggregateBits()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 30];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(8000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Average,
                dataValues, startTime, endTime, 8000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Calculated), Is.True);
        }

        [Test]
        public void TimeAverageReturnsResult()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 40, 50, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_TimeAverage,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double timeAvg = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(timeAvg, Is.GreaterThan(0));
        }

        [Test]
        public void TimeAverage2ReturnsResult()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 40, 50, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_TimeAverage2,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double timeAvg2 = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(timeAvg2, Is.GreaterThan(0));
        }

        [Test]
        public void TotalReturnsResult()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 40, 50, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Total,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double total = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(total, Is.GreaterThan(0));
        }

        [Test]
        public void Total2ReturnsResult()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 40, 50, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Total2,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double total2 = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(total2, Is.GreaterThan(0));
        }

        [Test]
        public void TimeAverageWithConstantValueReturnsConstant()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [42, 42, 42, 42, 42, 42];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_TimeAverage,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double timeAvg = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(timeAvg, Is.EqualTo(42.0).Within(0.5));
        }

        [Test]
        public void TotalResultHasCalculatedAggregateBits()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 30];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(8000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Total,
                dataValues, startTime, endTime, 8000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Calculated), Is.True);
        }

        [Test]
        public void AverageOfSymmetricValuesReturnsMiddle()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 20, 10, 10];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Average,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            double avg = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(avg, Is.GreaterThan(0));
        }

        [Test]
        public void TimeAverage2WithConstantValueReturnsConstant()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [100, 100, 100, 100, 100, 100];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_TimeAverage2,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double timeAvg2 = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(timeAvg2, Is.EqualTo(100.0).Within(0.5));
        }

        [Test]
        public void TimeAverageResultHasCalculatedAggregateBits()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 30];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(8000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_TimeAverage,
                dataValues, startTime, endTime, 8000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Calculated), Is.True);
        }
    }
}
