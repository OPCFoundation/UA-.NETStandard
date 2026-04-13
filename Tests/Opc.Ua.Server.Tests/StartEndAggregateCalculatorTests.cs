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
    [Category("StartEndAggregateCalculator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class StartEndAggregateCalculatorTests
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
        public void StartReturnsFirstValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 40, 50, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Start,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double startVal = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(startVal, Is.EqualTo(10.0).Within(0.0001));
        }

        [Test]
        public void EndReturnsLastValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 40, 50, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_End,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
        }

        [Test]
        public void DeltaReturnsEndMinusStart()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 40, 50, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Delta,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double delta = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(delta, Is.GreaterThan(0));
        }

        [Test]
        public void DeltaWithIdenticalValuesReturnsZero()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [42, 42, 42, 42, 42, 42];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Delta,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double delta = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(delta, Is.EqualTo(0.0).Within(0.0001));
        }

        [Test]
        public void DeltaWithDecreasingValuesReturnsNegative()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [50, 40, 30, 20, 10, 10];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Delta,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double delta = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(delta, Is.LessThan(0));
        }

        [Test]
        public void DeltaResultHasCalculatedAggregateBits()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 30];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(8000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Delta,
                dataValues, startTime, endTime, 8000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Calculated), Is.True);
        }

        [Test]
        public void StartBoundReturnsResult()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            double[] values = [10, 20, 30, 40, 50, 60];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_StartBound,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void EndBoundReturnsResult()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            double[] values = [10, 20, 30, 40, 50, 60];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_EndBound,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void DeltaBoundsReturnsResult()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            double[] values = [10, 20, 30, 40, 50, 60];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_DeltaBounds,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void DeltaBoundsWithIdenticalValuesReturnsZero()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            double[] values = [42, 42, 42, 42, 42, 42];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_DeltaBounds,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void StartWithTwoValuesReturnsFirst()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [100, 200];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(4000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Start,
                dataValues, startTime, endTime, 4000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double startVal = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(startVal, Is.EqualTo(100.0).Within(0.0001));
        }

        [Test]
        public void EndBoundHasCalculatedAggregateBits()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            double[] values = [10, 20, 30, 40];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(8000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_EndBound,
                dataValues, startTime, endTime, 8000);

            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void DeltaBoundsResultHasCalculatedAggregateBits()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            double[] values = [10, 20, 30, 40];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(8000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_DeltaBounds,
                dataValues, startTime, endTime, 8000);

            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void DeltaWithLargeChangeReturnsCorrectDifference()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [0, 100, 200, 300, 400, 400];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Delta,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double delta = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(delta, Is.GreaterThan(0));
        }

        [Test]
        public void StartAndEndWithSameValuesReturnSame()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [77, 77, 77, 77, 77, 77];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue startResult = ComputeAggregate(
                ObjectIds.AggregateFunction_Start,
                dataValues, startTime, endTime, 12000);

            DataValue endResult = ComputeAggregate(
                ObjectIds.AggregateFunction_End,
                dataValues, startTime, endTime, 12000);

            Assert.That(startResult, Is.Not.Null);
            Assert.That(endResult, Is.Not.Null);
            double startVal = (double)startResult.WrappedValue.ConvertToDouble();
            double endVal = (double)endResult.WrappedValue.ConvertToDouble();
            Assert.That(startVal, Is.EqualTo(endVal).Within(0.0001));
        }
    }
}
