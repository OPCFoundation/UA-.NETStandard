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
    /// <summary>
    /// <para>Tests for aggregate calculators, specifically Standard Deviation and Variance aggregates.</para>
    /// <para>
    /// Per OPC UA Part 13 v1.05.07 §5.4.3.37/.39, StandardDeviation/Variance (sample and population)
    /// operate on all Good raw values in the interval (UseBounds = None); only the divisor differs
    /// (sample n-1, population n).
    /// </para>
    /// </summary>
    [TestFixture]
    [Category("AggregateCalculator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class AggregateCalculatorTests
    {
        private ITelemetryContext m_telemetry;
        private AggregateConfiguration m_configuration;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();

            // Create a default aggregate configuration
            m_configuration = new AggregateConfiguration
            {
                TreatUncertainAsBad = false,
                PercentDataBad = 100,
                PercentDataGood = 100,
                UseSlopedExtrapolation = false
            };
        }

        /// <summary>
        /// Creates data values for testing
        /// </summary>
        private static List<DataValue> CreateDataValues(DateTimeUtc startTime, double[] values, double intervalMs = 1000)
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

        /// <summary>
        /// Computes aggregate value for a set of data values
        /// </summary>
        private DataValue ComputeAggregate(
            NodeId aggregateId,
            List<DataValue> values,
            DateTimeUtc startTime,
            DateTimeUtc endTime,
            double processingInterval)
        {
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                aggregateId,
                startTime,
                endTime,
                processingInterval,
                false, // stepped
                m_configuration,
                m_telemetry);

            // Queue all values
            foreach (DataValue value in values)
            {
                calculator.QueueRawValue(value);
            }

            // Get the processed values
            var results = new List<DataValue>();
            bool hasData = true;

            while (hasData)
            {
                // Use returnPartial=true to get results even without a late bound
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

            // Return the first result (we're testing single interval calculations)
            return results.Count > 0 ? results[0] : default;
        }

        /// <summary>
        /// Test StandardDeviationPopulation with the example from OPC UA Part 13 v1.05 §A.35:
        /// values [10, 20, 30, 40, 50] give a population std dev of sqrt(200) ≈ 14.142.
        /// </summary>
        [Test]
        public void StandardDeviationPopulation_SpecExample()
        {
            // Arrange
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 40, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            // Act
            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationPopulation,
                dataValues,
                startTime,
                endTime,
                12000);

            // Assert
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);

            double stdDev = (double)result.WrappedValue.ConvertToDouble();

            // Expected: sqrt(((10-30)^2 + (20-30)^2 + (30-30)^2 + (40-30)^2 + (50-30)^2) / 5)
            // = sqrt((400 + 100 + 0 + 100 + 400) / 5) = sqrt(200) ≈ 14.142135
            Assert.That(stdDev, Is.EqualTo(14.142135).Within(0.0001),
                "Standard deviation population should match expected value");
        }

        /// <summary>
        /// Test StandardDeviationPopulation with a single value - should return 0.
        /// </summary>
        [Test]
        public void StandardDeviationPopulation_SingleValue_ReturnsZero()
        {
            // Arrange
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [42.5];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(5000);

            // Act
            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationPopulation,
                dataValues,
                startTime,
                endTime,
                5000);

            // Assert
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);

            double stdDev = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(stdDev, Is.Zero,
                "Standard deviation of single value should be 0");
        }

        /// <summary>
        /// Test StandardDeviationPopulation with identical values - should return 0
        /// </summary>
        [Test]
        public void StandardDeviationPopulation_IdenticalValues_ReturnsZero()
        {
            // Arrange
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);
            double[] values = [100, 100, 100, 100, 100];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);

            // Act
            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationPopulation,
                dataValues,
                startTime,
                endTime,
                10000);

            // Assert
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);

            double stdDev = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(stdDev, Is.Zero,
                "Standard deviation of identical values should be 0");
        }

        /// <summary>
        /// Test StandardDeviationSample with example from OPC UA Part 13 v1.05 Section A.36
        /// Example: Values [10, 20, 30, 40, 50] should give sample std dev ≈ 15.811
        /// </summary>
        [Test]
        public void StandardDeviationSample_SpecExample()
        {
            // Arrange
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);
            double[] values = [10, 20, 30, 40, 50];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);

            // Act
            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationSample,
                dataValues,
                startTime,
                endTime,
                10000);

            // Assert
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);

            double stdDev = (double)result.WrappedValue.ConvertToDouble();

            // Expected: sqrt(((10-30)^2 + (20-30)^2 + (30-30)^2 + (40-30)^2 + (50-30)^2) / (5-1))
            // = sqrt(1000 / 4) = sqrt(250) ≈ 15.811388
            Assert.That(stdDev, Is.EqualTo(15.811388).Within(0.0001),
                "Standard deviation sample should match expected value");
        }

        /// <summary>
        /// Test StandardDeviationSample with single value - should return 0 per spec
        /// According to OPC UA Part 13 v1.05 section 5.4.3.37, sample std dev is 0 when n <= 1
        /// </summary>
        [Test]
        public void StandardDeviationSample_SingleValue_ReturnsZero()
        {
            // Arrange
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(5000);
            double[] values = [42.5];
            List<DataValue> dataValues = CreateDataValues(startTime, values);

            // Act
            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationSample,
                dataValues,
                startTime,
                endTime,
                5000);

            // Assert
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);

            double stdDev = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(stdDev, Is.Zero,
                "Sample standard deviation with single value should be 0");
        }

        /// <summary>
        /// Test StandardDeviationSample with two values
        /// </summary>
        [Test]
        public void StandardDeviationSample_TwoValues()
        {
            // Arrange
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(5000);
            double[] values = [10, 20];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);

            // Act
            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationSample,
                dataValues,
                startTime,
                endTime,
                5000);

            // Assert
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);

            double stdDev = (double)result.WrappedValue.ConvertToDouble();

            // Expected: sqrt(((10-15)^2 + (20-15)^2) / (2-1)) = sqrt(50) ≈ 7.0710678
            Assert.That(stdDev, Is.EqualTo(7.0710678).Within(0.0001),
                "Sample standard deviation with two values should be calculated correctly");
        }

        /// <summary>
        /// Test VariancePopulation with the example from OPC UA Part 13 v1.05 §A.37:
        /// values [10, 20, 30, 40, 50] give a population variance of 200.
        /// </summary>
        [Test]
        public void VariancePopulation_SpecExample()
        {
            // Arrange
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 20, 30, 40, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            // Act
            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_VariancePopulation,
                dataValues,
                startTime,
                endTime,
                12000);

            // Assert
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);

            double variance = (double)result.WrappedValue.ConvertToDouble();

            // Expected: ((10-30)^2 + (20-30)^2 + (30-30)^2 + (40-30)^2 + (50-30)^2) / 5
            // = (400 + 100 + 0 + 100 + 400) / 5 = 200
            Assert.That(variance, Is.EqualTo(200.0).Within(0.0001),
                "Variance population should match expected value");
        }

        /// <summary>
        /// Test VariancePopulation with a single value - should return 0.
        /// </summary>
        [Test]
        public void VariancePopulation_SingleValue_ReturnsZero()
        {
            // Arrange
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [42.5];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(5000);

            // Act
            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_VariancePopulation,
                dataValues,
                startTime,
                endTime,
                5000);

            // Assert
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);

            double variance = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(variance, Is.Zero,
                "Variance of single value should be 0");
        }

        /// <summary>
        /// Test VariancePopulation with identical values - should return 0
        /// </summary>
        [Test]
        public void VariancePopulation_IdenticalValues_ReturnsZero()
        {
            // Arrange
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);
            double[] values = [100, 100, 100, 100, 100];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);

            // Act
            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_VariancePopulation,
                dataValues,
                startTime,
                endTime,
                10000);

            // Assert
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);

            double variance = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(variance, Is.Zero,
                "Variance of identical values should be 0");
        }

        /// <summary>
        /// Test VarianceSample with example from OPC UA Part 13 v1.05 Section A.38
        /// Example: Values [10, 20, 30, 40, 50] should give sample variance = 250
        /// </summary>
        [Test]
        public void VarianceSample_SpecExample()
        {
            // Arrange
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);
            double[] values = [10, 20, 30, 40, 50];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);

            // Act
            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_VarianceSample,
                dataValues,
                startTime,
                endTime,
                10000);

            // Assert
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);

            double variance = (double)result.WrappedValue.ConvertToDouble();

            // Expected: ((10-30)^2 + (20-30)^2 + (30-30)^2 + (40-30)^2 + (50-30)^2) / (5-1)
            // = 1000 / 4 = 250
            Assert.That(variance, Is.EqualTo(250.0).Within(0.0001),
                "Variance sample should match expected value");
        }

        /// <summary>
        /// Test VarianceSample with single value - should return 0 per spec
        /// According to OPC UA Part 13 v1.05 section 5.4.3.38, sample variance is 0 when n <= 1
        /// </summary>
        [Test]
        public void VarianceSample_SingleValue_ReturnsZero()
        {
            // Arrange
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(5000);
            double[] values = [42.5];
            List<DataValue> dataValues = CreateDataValues(startTime, values);

            // Act
            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_VarianceSample,
                dataValues,
                startTime,
                endTime,
                5000);

            // Assert
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);

            double variance = (double)result.WrappedValue.ConvertToDouble();
            Assert.That(variance, Is.Zero,
                "Sample variance with single value should be 0");
        }

        /// <summary>
        /// Test VarianceSample with two values
        /// </summary>
        [Test]
        public void VarianceSample_TwoValues()
        {
            // Arrange
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(5000);
            double[] values = [10, 20];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);

            // Act
            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_VarianceSample,
                dataValues,
                startTime,
                endTime,
                5000);

            // Assert
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);

            double variance = (double)result.WrappedValue.ConvertToDouble();

            // Expected: ((10-15)^2 + (20-15)^2) / (2-1) = 50
            Assert.That(variance, Is.EqualTo(50.0).Within(0.0001),
                "Sample variance with two values should be calculated correctly");
        }

        /// <summary>
        /// Verify that sample standard deviation is the square root of sample variance
        /// </summary>
        [Test]
        public void SampleStdDevIsSquareRootOfSampleVariance()
        {
            // Arrange
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);
            double[] values = [5, 15, 25, 35, 45];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);

            // Act - compute both sample variance and sample standard deviation
            DataValue varianceResult = ComputeAggregate(
                ObjectIds.AggregateFunction_VarianceSample,
                dataValues,
                startTime,
                endTime,
                10000);

            DataValue stdDevResult = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationSample,
                dataValues,
                startTime,
                endTime,
                10000);

            // Assert
            Assert.That(varianceResult.IsNull, Is.False);
            Assert.That(stdDevResult.IsNull, Is.False);

            double variance = (double)varianceResult.WrappedValue.ConvertToDouble();
            double stdDev = (double)stdDevResult.WrappedValue.ConvertToDouble();

            Assert.That(stdDev, Is.EqualTo(Math.Sqrt(variance)).Within(0.0001),
                "Sample standard deviation should be the square root of sample variance");
        }

        /// <summary>
        /// Verify that population standard deviation is the square root of population variance
        /// </summary>
        [Test]
        public void PopulationStdDevIsSquareRootOfPopulationVariance()
        {
            // Arrange
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);
            double[] values = [5, 15, 25, 35, 45];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);

            // Act - compute both population variance and population standard deviation
            DataValue varianceResult = ComputeAggregate(
                ObjectIds.AggregateFunction_VariancePopulation,
                dataValues,
                startTime,
                endTime,
                10000);

            DataValue stdDevResult = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationPopulation,
                dataValues,
                startTime,
                endTime,
                10000);

            // Assert
            Assert.That(varianceResult.IsNull, Is.False);
            Assert.That(stdDevResult.IsNull, Is.False);

            double variance = (double)varianceResult.WrappedValue.ConvertToDouble();
            double stdDev = (double)stdDevResult.WrappedValue.ConvertToDouble();

            Assert.That(stdDev, Is.EqualTo(Math.Sqrt(variance)).Within(0.0001),
                "Population standard deviation should be the square root of population variance");
        }

        /// <summary>
        /// Verify that sample variance is always greater than or equal to population variance
        /// </summary>
        [Test]
        public void SampleVarianceIsGreaterThanOrEqualToPopulationVariance()
        {
            // Arrange
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);
            double[] values = [12, 18, 24, 36, 42];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);

            // Act
            DataValue sampleVarianceResult = ComputeAggregate(
                ObjectIds.AggregateFunction_VarianceSample,
                dataValues,
                startTime,
                endTime,
                10000);

            DataValue populationVarianceResult = ComputeAggregate(
                ObjectIds.AggregateFunction_VariancePopulation,
                dataValues,
                startTime,
                endTime,
                10000);

            // Assert
            Assert.That(sampleVarianceResult.IsNull, Is.False);
            Assert.That(populationVarianceResult.IsNull, Is.False);

            double sampleVariance = (double)sampleVarianceResult.WrappedValue.ConvertToDouble();
            double populationVariance = (double)populationVarianceResult.WrappedValue.ConvertToDouble();

            Assert.That(sampleVariance, Is.GreaterThanOrEqualTo(populationVariance),
                "Sample variance should be greater than or equal to population variance");
        }

        [Test]
        public void InterpolativeReturnsValueAtIntervalStart()
        {
            // Part 13 §5.4.3.4: Interpolative returns the (interpolated) bounding value at the start
            // of each interval. With a raw value exactly at the interval start, that raw value is
            // returned.
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(6000);
            double[] values = [10, 20, 30];
            List<DataValue> dataValues = CreateDataValues(startTime, values, 2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Interpolative,
                dataValues, startTime, endTime, 6000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(10.0).Within(0.0001));
        }
    }
}
