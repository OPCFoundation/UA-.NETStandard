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

using System;
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
    public class StdDevAggregateCalculatorTests
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
        public void StdDevPopulationWithLargeSpreadReturnsHighValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [1, 100, 1, 100, 1, 100];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationPopulation,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double stdDev = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(stdDev, Is.GreaterThan(10));
        }

        [Test]
        public void StdDevSampleWithLargeSpreadReturnsHighValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);
            double[] values = [1, 100, 1, 100, 1];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationSample,
                dataValues, startTime, endTime, 10000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double stdDev = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(stdDev, Is.GreaterThan(10));
        }

        [Test]
        public void VariancePopulationIsNonNegative()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [-50, 0, 50, -50, 0, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_VariancePopulation,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double variance = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(variance, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void VarianceSampleIsNonNegative()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);
            double[] values = [-50, 0, 50, -50, 0];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_VarianceSample,
                dataValues, startTime, endTime, 10000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double variance = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(variance, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void StdDevPopulationResultHasCalculatedAggregateBits()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 30];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(8000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationPopulation,
                dataValues, startTime, endTime, 8000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Calculated), Is.True);
        }

        [Test]
        public void StdDevSampleResultHasCalculatedAggregateBits()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);
            double[] values = [10, 20, 30, 40, 50];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationSample,
                dataValues, startTime, endTime, 10000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Calculated), Is.True);
        }

        [Test]
        public void VariancePopulationResultHasCalculatedAggregateBits()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 30];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(8000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_VariancePopulation,
                dataValues, startTime, endTime, 8000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Calculated), Is.True);
        }

        [Test]
        public void VarianceSampleResultHasCalculatedAggregateBits()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);
            double[] values = [10, 20, 30, 40, 50];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_VarianceSample,
                dataValues, startTime, endTime, 10000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Calculated), Is.True);
        }

        [Test]
        public void StdDevPopulationWithNegativeValues()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [-10, -20, -30, -40, -50, -50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationPopulation,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double stdDev = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(stdDev, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void StdDevSampleNegativeValues()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);
            double[] values = [-10, -20, -30, -40, -50];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationSample,
                dataValues, startTime, endTime, 10000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double stdDev = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(stdDev, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void StdDevPopulationIsLessThanOrEqualToStdDevSample()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);
            double[] values = [10, 20, 30, 40, 50];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);

            DataValue popResult = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationPopulation,
                dataValues, startTime, endTime, 10000);

            DataValue sampleResult = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationSample,
                dataValues, startTime, endTime, 10000);

            Assert.That(popResult, Is.Not.Null);
            Assert.That(sampleResult, Is.Not.Null);
            double popStdDev = (double)popResult.WrappedValue.ConvertToDouble();
            double sampleStdDev = (double)sampleResult.WrappedValue.ConvertToDouble();
            Assert.That(popStdDev, Is.LessThanOrEqualTo(sampleStdDev + 0.0001));
        }

        [Test]
        public void VariancePopulationWithKnownValues()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [2, 4, 4, 4, 5, 5, 7, 9, 9];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 1000);
            DateTimeUtc endTime = startTime.AddMilliseconds(9500);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_VariancePopulation,
                dataValues, startTime, endTime, 9500);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double variance = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(variance, Is.GreaterThan(0));
        }

        [Test]
        public void StdDevPopulationIsSquareRootOfVariancePopulation()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [3, 7, 11, 15, 19, 19];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue stdDevResult = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationPopulation,
                dataValues, startTime, endTime, 12000);

            DataValue varianceResult = ComputeAggregate(
                ObjectIds.AggregateFunction_VariancePopulation,
                dataValues, startTime, endTime, 12000);

            Assert.That(stdDevResult, Is.Not.Null);
            Assert.That(varianceResult, Is.Not.Null);
            double stdDev = (double)stdDevResult.WrappedValue.ConvertToDouble();
            double variance = (double)varianceResult.WrappedValue.ConvertToDouble();
            Assert.That(stdDev, Is.EqualTo(Math.Sqrt(variance)).Within(0.001));
        }
    }
}
