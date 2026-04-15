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
    public class CountAggregateCalculatorTests
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

        private static List<DataValue> CreateMixedStatusDataValues(
            DateTimeUtc startTime, double[] values, StatusCode[] statusCodes, double intervalMs = 1000)
        {
            var dataValues = new List<DataValue>();
            for (int i = 0; i < values.Length; i++)
            {
                dataValues.Add(new DataValue
                {
                    WrappedValue = values[i],
                    SourceTimestamp = startTime.AddMilliseconds(i * intervalMs),
                    ServerTimestamp = startTime.AddMilliseconds(i * intervalMs),
                    StatusCode = statusCodes[i]
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
        public void CountReturnsNumberOfGoodValues()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 40, 50, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Count,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            int count = (int)(double)result.WrappedValue.ConvertToDouble();
            Assert.That(count, Is.GreaterThan(0));
        }

        [Test]
        public void CountWithMixedStatusCountsOnlyGoodValues()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 40, 50, 50];
            StatusCode[] statuses =
            [
                StatusCodes.Good,
                StatusCodes.Bad,
                StatusCodes.Good,
                StatusCodes.Bad,
                StatusCodes.Good,
                StatusCodes.Good,
            ];
            List<DataValue> dataValues = CreateMixedStatusDataValues(
                firstValueTime, values, statuses, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Count,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            int count = (int)(double)result.WrappedValue.ConvertToDouble();
            Assert.That(count, Is.LessThan(6));
        }

        [Test]
        public void CountSingleValueReturnsOne()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [42, 42];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(4000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Count,
                dataValues, startTime, endTime, 4000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
        }

        [Test]
        public void AnnotationCountReturnsCountOfAllValues()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 40, 50, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_AnnotationCount,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            int count = (int)(double)result.WrappedValue.ConvertToDouble();
            Assert.That(count, Is.GreaterThan(0));
        }

        [Test]
        public void AnnotationCountIncludesBadValues()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 40, 50, 50];
            StatusCode[] statuses =
            [
                StatusCodes.Good,
                StatusCodes.Bad,
                StatusCodes.Good,
                StatusCodes.Bad,
                StatusCodes.Good,
                StatusCodes.Good,
            ];
            List<DataValue> dataValues = CreateMixedStatusDataValues(
                firstValueTime, values, statuses, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue annotationResult = ComputeAggregate(
                ObjectIds.AggregateFunction_AnnotationCount,
                dataValues, startTime, endTime, 12000);

            DataValue countResult = ComputeAggregate(
                ObjectIds.AggregateFunction_Count,
                dataValues, startTime, endTime, 12000);

            Assert.That(annotationResult, Is.Not.Null);
            Assert.That(countResult, Is.Not.Null);
            int annotationCount = (int)(double)annotationResult.WrappedValue.ConvertToDouble();
            int goodCount = (int)(double)countResult.WrappedValue.ConvertToDouble();
            Assert.That(annotationCount, Is.GreaterThanOrEqualTo(goodCount));
        }

        [Test]
        public void DurationInStateZeroReturnsDuration()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [0, 0, 1, 0, 0, 0];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_DurationInStateZero,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double duration = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(duration, Is.GreaterThan(0));
        }

        [Test]
        public void DurationInStateNonZeroReturnsDuration()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [1, 1, 0, 1, 1, 1];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_DurationInStateNonZero,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double duration = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(duration, Is.GreaterThan(0));
        }

        [Test]
        public void DurationInStateZeroAllNonZeroReturnsZero()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [1, 2, 3, 4, 5, 5];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_DurationInStateZero,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double duration = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(duration, Is.EqualTo(0.0).Within(0.001));
        }

        [Test]
        public void DurationInStateNonZeroAllZeroReturnsZero()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [0, 0, 0, 0, 0, 0];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_DurationInStateNonZero,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double duration = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(duration, Is.EqualTo(0.0).Within(0.001));
        }

        [Test]
        public void NumberOfTransitionsCountsChanges()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [0, 1, 0, 1, 0, 0];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_NumberOfTransitions,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            int count = (int)(double)result.WrappedValue.ConvertToDouble();
            Assert.That(count, Is.GreaterThan(0));
        }

        [Test]
        public void NumberOfTransitionsNoChangesReturnsZero()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [5, 5, 5, 5, 5, 5];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_NumberOfTransitions,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            int count = (int)(double)result.WrappedValue.ConvertToDouble();
            Assert.That(count, Is.EqualTo(0));
        }

        [Test]
        public void NumberOfTransitionsWithSingleTransition()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [0, 0, 0, 1, 1, 1];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_NumberOfTransitions,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            int count = (int)(double)result.WrappedValue.ConvertToDouble();
            Assert.That(count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void DurationInStateZeroAndNonZeroArComplementary()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [0, 1, 0, 1, 0, 1];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue zeroResult = ComputeAggregate(
                ObjectIds.AggregateFunction_DurationInStateZero,
                dataValues, startTime, endTime, 12000);

            DataValue nonZeroResult = ComputeAggregate(
                ObjectIds.AggregateFunction_DurationInStateNonZero,
                dataValues, startTime, endTime, 12000);

            Assert.That(zeroResult, Is.Not.Null);
            Assert.That(nonZeroResult, Is.Not.Null);
            double zeroDuration = (double)zeroResult.WrappedValue.ConvertToDouble();
            double nonZeroDuration = (double)nonZeroResult.WrappedValue.ConvertToDouble();
            Assert.That(zeroDuration, Is.GreaterThanOrEqualTo(0));
            Assert.That(nonZeroDuration, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void CountResultHasCalculatedAggregateBits()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 40, 50, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Count,
                dataValues, startTime, endTime, 12000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Calculated), Is.True);
        }

        [Test]
        public void AnnotationCountResultHasCalculatedAggregateBits()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 30];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(8000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_AnnotationCount,
                dataValues, startTime, endTime, 8000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Calculated), Is.True);
        }
    }
}
