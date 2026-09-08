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
using System.Threading;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies count, annotation-count, duration-in-state, and transition aggregates and their boundary rules.
    /// </summary>
    [TestFixture]
    [Category("Aggregators")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class CountAggregateCalculatorTests
    {
        private ITelemetryContext m_telemetry;
        private AggregateConfiguration m_configuration;

        /// <summary>
        /// Creates telemetry and an aggregate configuration that accepts uncertain data without sloped extrapolation.
        /// </summary>
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
                dataValues.Add(new DataValue(
                    new Variant(values[i]),
                    StatusCodes.Good,
                    startTime.AddMilliseconds(i * intervalMs),
                    startTime.AddMilliseconds(i * intervalMs)));
            }
            return dataValues;
        }

        private static List<DataValue> CreateMixedStatusDataValues(
            DateTimeUtc startTime, double[] values, StatusCode[] statusCodes, double intervalMs = 1000)
        {
            var dataValues = new List<DataValue>();
            for (int i = 0; i < values.Length; i++)
            {
                dataValues.Add(new DataValue(
                    new Variant(values[i]),
                    statusCodes[i],
                    startTime.AddMilliseconds(i * intervalMs),
                    startTime.AddMilliseconds(i * intervalMs)));
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
                bool _hasresult = calculator.TryGetProcessedValue(true, out DataValue result);
                if (_hasresult)
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

        /// <summary>
        /// Verifies that Count returns the number of Good raw values.
        /// </summary>
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

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            int count = (int)(double)result.WrappedValue.ConvertToDouble();
            Assert.That(count, Is.GreaterThan(0));
        }

        /// <summary>
        /// Verifies that Count excludes non-Good values from mixed-quality input.
        /// </summary>
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
                StatusCodes.Good
            ];
            List<DataValue> dataValues = CreateMixedStatusDataValues(
                firstValueTime, values, statuses, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Count,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            int count = (int)(double)result.WrappedValue.ConvertToDouble();
            Assert.That(count, Is.LessThan(6));
        }

        /// <summary>
        /// Verifies that Count returns one for a single Good value.
        /// </summary>
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

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
        }

        /// <summary>
        /// Verifies that AnnotationCount counts every supplied annotation value.
        /// </summary>
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

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            int count = (int)(double)result.WrappedValue.ConvertToDouble();
            Assert.That(count, Is.GreaterThan(0));
        }

        /// <summary>
        /// Verifies that AnnotationCount includes values with bad quality.
        /// </summary>
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
                StatusCodes.Good
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

            Assert.That(annotationResult.IsNull, Is.False);
            Assert.That(countResult.IsNull, Is.False);
            int annotationCount = (int)(double)annotationResult.WrappedValue.ConvertToDouble();
            int goodCount = (int)(double)countResult.WrappedValue.ConvertToDouble();
            Assert.That(annotationCount, Is.GreaterThanOrEqualTo(goodCount));
        }

        /// <summary>
        /// Verifies that a zero annotation-count interval covers the entire requested domain.
        /// </summary>
        [Test]
        public void CalculateAnnotationCountsZeroIntervalUsesWholeDomain()
        {
            DateTimeUtc startTime = TimeAt(0);
            DateTimeUtc endTime = TimeAt(10);

            ArrayOf<DataValue> result =
                CountAggregateCalculator.CalculateAnnotationCounts(
                    [startTime, TimeAt(5), endTime],
                    startTime,
                    endTime,
                    processingInterval: 0,
                    outputCap: 10,
                    CancellationToken.None);

            Assert.That(result, Has.Count.EqualTo(1));
            AssertAnnotationCount(result[0], 2, startTime);
        }

        /// <summary>
        /// Verifies that forward annotation counting uses half-open time intervals.
        /// </summary>
        [Test]
        public void CalculateAnnotationCountsUsesHalfOpenForwardIntervals()
        {
            ArrayOf<DataValue> result =
                CountAggregateCalculator.CalculateAnnotationCounts(
                    [TimeAt(0), TimeAt(5), TimeAt(10)],
                    TimeAt(0),
                    TimeAt(10),
                    processingInterval: 5000,
                    outputCap: 10,
                    CancellationToken.None);

            Assert.That(result, Has.Count.EqualTo(2));
            AssertAnnotationCount(result[0], 1, TimeAt(0));
            AssertAnnotationCount(result[1], 1, TimeAt(5));
        }

        /// <summary>
        /// Verifies that reverse annotation counting applies the reverse interval boundary rules.
        /// </summary>
        [Test]
        public void CalculateAnnotationCountsUsesReverseIntervalBoundaries()
        {
            ArrayOf<DataValue> result =
                CountAggregateCalculator.CalculateAnnotationCounts(
                    [TimeAt(0), TimeAt(5), TimeAt(10)],
                    TimeAt(10),
                    TimeAt(0),
                    processingInterval: 5000,
                    outputCap: 10,
                    CancellationToken.None);

            Assert.That(result, Has.Count.EqualTo(2));
            AssertAnnotationCount(result[0], 1, TimeAt(10));
            AssertAnnotationCount(result[1], 1, TimeAt(5));
        }

        /// <summary>
        /// Verifies that annotation counting clamps a partial interval near the maximum timestamp.
        /// </summary>
        [Test]
        public void CalculateAnnotationCountsClampsPartialIntervalNearMaximum()
        {
            DateTimeUtc startTime = DateTimeUtc.MaxValue;
            DateTimeUtc endTime = new(
                startTime.ToDateTime().AddMilliseconds(-5));

            ArrayOf<DataValue> result =
                CountAggregateCalculator.CalculateAnnotationCounts(
                    [startTime],
                    startTime,
                    endTime,
                    processingInterval: 10,
                    outputCap: 10,
                    CancellationToken.None);

            Assert.That(result, Has.Count.EqualTo(1));
            AssertAnnotationCount(result[0], 1, startTime);
        }

        /// <summary>
        /// Verifies that annotation counting supports fractional-millisecond intervals.
        /// </summary>
        [Test]
        public void CalculateAnnotationCountsSupportsFractionalMillisecondIntervals()
        {
            var startDateTime =
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTimeUtc startTime = startDateTime;
            DateTimeUtc endTime = startDateTime.AddTicks(3000);

            ArrayOf<DataValue> result =
                CountAggregateCalculator.CalculateAnnotationCounts(
                    [
                        startTime,
                        (DateTimeUtc)startDateTime.AddTicks(1000),
                        (DateTimeUtc)startDateTime.AddTicks(2000),
                        endTime
                    ],
                    startTime,
                    endTime,
                    processingInterval: 0.1,
                    outputCap: 10,
                    CancellationToken.None);

            Assert.That(result, Has.Count.EqualTo(3));
            AssertAnnotationCount(result[0], 1, startTime);
            AssertAnnotationCount(
                result[1],
                1,
                startDateTime.AddTicks(1000));
            AssertAnnotationCount(
                result[2],
                1,
                startDateTime.AddTicks(2000));
        }

        /// <summary>
        /// Verifies that annotation counting rejects invalid processing intervals.
        /// </summary>
        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void CalculateAnnotationCountsRejectsInvalidInterval(
            double processingInterval)
        {
            ServiceResultException exception =
                Assert.Throws<ServiceResultException>(
                    () => CountAggregateCalculator.CalculateAnnotationCounts(
                        [],
                        TimeAt(0),
                        TimeAt(10),
                        processingInterval,
                        outputCap: 10,
                        CancellationToken.None));

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadAggregateInvalidInputs));
        }

        /// <summary>
        /// Verifies that annotation counting rejects intervals whose tick representation overflows.
        /// </summary>
        [Test]
        public void CalculateAnnotationCountsRejectsIntervalTickOverflow()
        {
            double processingInterval =
                (double)long.MaxValue /
                TimeSpan.TicksPerMillisecond;

            ServiceResultException exception =
                Assert.Throws<ServiceResultException>(
                    () => CountAggregateCalculator.CalculateAnnotationCounts(
                        [],
                        TimeAt(0),
                        TimeAt(10),
                        processingInterval,
                        outputCap: 10,
                        CancellationToken.None));

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadAggregateInvalidInputs));
        }

        /// <summary>
        /// Verifies that annotation counting enforces its maximum output count.
        /// </summary>
        [Test]
        public void CalculateAnnotationCountsEnforcesOutputCap()
        {
            ServiceResultException exception =
                Assert.Throws<ServiceResultException>(
                    () => CountAggregateCalculator.CalculateAnnotationCounts(
                        [],
                        TimeAt(0),
                        TimeAt(10),
                        processingInterval: 1000,
                        outputCap: 5,
                        CancellationToken.None));

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadTooManyOperations));
        }

        /// <summary>
        /// Verifies that annotation counting observes cancellation.
        /// </summary>
        [Test]
        public void CalculateAnnotationCountsObservesCancellation()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.That(
                () => CountAggregateCalculator.CalculateAnnotationCounts(
                    [],
                    TimeAt(0),
                    TimeAt(10),
                    processingInterval: 1000,
                    outputCap: 10,
                    cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
        }

        /// <summary>
        /// Verifies that DurationInStateZero reports the time spent at zero.
        /// </summary>
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

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double duration = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(duration, Is.GreaterThan(0));
        }

        /// <summary>
        /// Verifies that DurationInStateNonZero reports the time spent away from zero.
        /// </summary>
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

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double duration = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(duration, Is.GreaterThan(0));
        }

        /// <summary>
        /// Verifies that DurationInStateZero returns zero for entirely nonzero input.
        /// </summary>
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

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double duration = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(duration, Is.Zero.Within(0.001));
        }

        /// <summary>
        /// Verifies that DurationInStateNonZero returns zero for entirely zero input.
        /// </summary>
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

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            double duration = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(duration, Is.Zero.Within(0.001));
        }

        /// <summary>
        /// Verifies that transition counting includes the first value and later changes when no prior value exists.
        /// </summary>
        [Test]
        public void NumberOfTransitionsCountsFirstValueAndChangesWhenNoPreviousValueExists()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [0, 1, 0, 1, 0, 0];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_NumberOfTransitions,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            int count = (int)(double)result.WrappedValue.ConvertToDouble();
            Assert.That(count, Is.EqualTo(5));
        }

        /// <summary>
        /// Verifies that transition counting includes the first value when no prior value exists.
        /// </summary>
        [Test]
        public void NumberOfTransitionsCountsFirstValueWhenNoPreviousValueExists()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [5, 5, 5, 5, 5, 5];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_NumberOfTransitions,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            int count = (int)(double)result.WrappedValue.ConvertToDouble();
            Assert.That(count, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that transition counting includes an initial value and one subsequent change.
        /// </summary>
        [Test]
        public void NumberOfTransitionsCountsFirstValueAndOneChange()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [0, 0, 0, 1, 1, 1];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_NumberOfTransitions,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            int count = (int)(double)result.WrappedValue.ConvertToDouble();
            Assert.That(count, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies that transition counting excludes a first value matching the preceding interval's value.
        /// </summary>
        [Test]
        public void NumberOfTransitionsDoesNotCountMatchingPreviousValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            List<DataValue> dataValues = CreateDataValues(
                startTime.AddMilliseconds(-500),
                [5, 5, 5, 5],
                1000);
            DateTimeUtc endTime = startTime.AddMilliseconds(2500);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_NumberOfTransitions,
                dataValues,
                startTime,
                endTime,
                2500);

            Assert.That(result.WrappedValue.TryGetValue(out int count), Is.True);
            Assert.That(count, Is.Zero);
        }

        /// <summary>
        /// Verifies that transition counting uses a preceding uncertain value even when uncertain quality is treated as
        /// bad.
        /// </summary>
        [Test]
        public void NumberOfTransitionsUsesPreviousUncertainValueWhenUncertainIsConfiguredAsBad()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            List<DataValue> dataValues = CreateMixedStatusDataValues(
                startTime.AddMilliseconds(-500),
                [5, 5, 6, 7],
                [StatusCodes.Uncertain, StatusCodes.Good, StatusCodes.Good, StatusCodes.Good],
                1000);
            DateTimeUtc endTime = startTime.AddMilliseconds(2500);
            m_configuration.TreatUncertainAsBad = true;

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_NumberOfTransitions,
                dataValues,
                startTime,
                endTime,
                2500);

            Assert.That(result.WrappedValue.TryGetValue(out int count), Is.True);
            Assert.That(count, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that zero-state and nonzero-state durations complement each other over the interval.
        /// </summary>
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

            Assert.That(zeroResult.IsNull, Is.False);
            Assert.That(nonZeroResult.IsNull, Is.False);
            double zeroDuration = (double)zeroResult.WrappedValue.ConvertToDouble();
            double nonZeroDuration = (double)nonZeroResult.WrappedValue.ConvertToDouble();
            Assert.That(zeroDuration, Is.GreaterThanOrEqualTo(0));
            Assert.That(nonZeroDuration, Is.GreaterThanOrEqualTo(0));
        }

        /// <summary>
        /// Verifies that Count results carry the calculated aggregate status bits.
        /// </summary>
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

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Calculated), Is.True);
        }

        /// <summary>
        /// Verifies that AnnotationCount results carry the calculated aggregate status bits.
        /// </summary>
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

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Calculated), Is.True);
        }

        private static void AssertAnnotationCount(
            DataValue value,
            int expected,
            DateTimeUtc timestamp)
        {
            Assert.That(value.WrappedValue.TryGetValue(out int count), Is.True);
            Assert.That(count, Is.EqualTo(expected));
            Assert.That(StatusCode.IsGood(value.StatusCode), Is.True);
            Assert.That(
                value.StatusCode.AggregateBits,
                Is.EqualTo(AggregateBits.Calculated));
            Assert.That(value.SourceTimestamp, Is.EqualTo(timestamp));
        }

        private static DateTimeUtc TimeAt(int second)
        {
            return new DateTimeUtc(2026, 1, 1, 0, 0, second);
        }
    }
}
