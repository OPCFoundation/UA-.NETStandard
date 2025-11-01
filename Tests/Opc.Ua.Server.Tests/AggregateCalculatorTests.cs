/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for aggregate calculators, specifically Standard Deviation and Variance aggregates
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

        #region Helper Methods

        /// <summary>
        /// Creates data values for testing
        /// </summary>
        private List<DataValue> CreateDataValues(DateTime startTime, double[] values, double intervalMs = 1000)
        {
            var dataValues = new List<DataValue>();
            
            for (int i = 0; i < values.Length; i++)
            {
                dataValues.Add(new DataValue
                {
                    Value = values[i],
                    SourceTimestamp = startTime.AddMilliseconds(i * intervalMs),
                    ServerTimestamp = startTime.AddMilliseconds(i * intervalMs),
                    StatusCode = StatusCodes.Good
                });
            }
            
            return dataValues;
        }

        /// <summary>
        /// Computes aggregate value for a set of data values
        /// </summary>
        private DataValue ComputeAggregate(
            NodeId aggregateId,
            List<DataValue> values,
            DateTime startTime,
            DateTime endTime,
            double processingInterval)
        {
            var calculator = Aggregators.CreateStandardCalculator(
                aggregateId,
                startTime,
                endTime,
                processingInterval,
                false, // stepped
                m_configuration,
                m_telemetry);

            // Queue all values
            foreach (var value in values)
            {
                calculator.QueueRawValue(value);
            }

            // Get the processed values
            var results = new List<DataValue>();
            bool hasData = true;
            
            while (hasData)
            {
                // Use returnPartial=true to get results even without a late bound
                var result = calculator.GetProcessedValue(true);
                if (result != null)
                {
                    results.Add(result);
                }
                else
                {
                    hasData = false;
                }
            }

            // Return the first result (we're testing single interval calculations)
            return results.Count > 0 ? results[0] : null;
        }

        #endregion

        #region StandardDeviationPopulation Tests

        /// <summary>
        /// Test StandardDeviationPopulation with example from OPC UA Part 13 v1.05 Section A.35
        /// Example: Values [10, 20, 30, 40, 50] should give population std dev ≈ 14.142
        /// </summary>
        [Test]
        public void StandardDeviationPopulation_SpecExample()
        {
            // Arrange
            // For population aggregates, values at interval boundaries are excluded
            // Place all data values strictly within (not at) the interval boundaries
            var startTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var firstValueTime = startTime.AddMilliseconds(500); // First value after start
            var values = new double[] { 10, 20, 30, 40, 50 };
            var dataValues = CreateDataValues(firstValueTime, values, 2000); // Values at 0.5s, 2.5s, 4.5s, 6.5s, 8.5s
            var endTime = startTime.AddSeconds(12); // End well after last value

            // Act
            var result = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationPopulation,
                dataValues,
                startTime,
                endTime,
                12000); // 12 second processing interval

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Value, Is.Not.Null);
            
            double stdDev = Convert.ToDouble(result.Value, CultureInfo.InvariantCulture);
            
            // Expected: sqrt(((10-30)^2 + (20-30)^2 + (30-30)^2 + (40-30)^2 + (50-30)^2) / 5)
            // = sqrt((400 + 100 + 0 + 100 + 400) / 5) = sqrt(200) ≈ 14.142135
            Assert.That(stdDev, Is.EqualTo(14.142135).Within(0.0001), 
                "Standard deviation population should match expected value");
        }

        /// <summary>
        /// Test StandardDeviationPopulation with single value - should return 0
        /// </summary>
        [Test]
        public void StandardDeviationPopulation_SingleValue_ReturnsZero()
        {
            // Arrange
            var startTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var firstValueTime = startTime.AddMilliseconds(500); // Value after start
            var values = new double[] { 42.5 };
            var dataValues = CreateDataValues(firstValueTime, values);
            var endTime = startTime.AddSeconds(5); // End well after value

            // Act
            var result = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationPopulation,
                dataValues,
                startTime,
                endTime,
                5000);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Value, Is.Not.Null);
            
            double stdDev = Convert.ToDouble(result.Value, CultureInfo.InvariantCulture);
            Assert.That(stdDev, Is.EqualTo(0.0), 
                "Standard deviation of single value should be 0");
        }

        /// <summary>
        /// Test StandardDeviationPopulation with identical values - should return 0
        /// </summary>
        [Test]
        public void StandardDeviationPopulation_IdenticalValues_ReturnsZero()
        {
            // Arrange
            var startTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var endTime = startTime.AddSeconds(10);
            var values = new double[] { 100, 100, 100, 100, 100 };
            var dataValues = CreateDataValues(startTime, values, 2000);

            // Act
            var result = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationPopulation,
                dataValues,
                startTime,
                endTime,
                10000);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Value, Is.Not.Null);
            
            double stdDev = Convert.ToDouble(result.Value, CultureInfo.InvariantCulture);
            Assert.That(stdDev, Is.EqualTo(0.0), 
                "Standard deviation of identical values should be 0");
        }

        #endregion

        #region StandardDeviationSample Tests

        /// <summary>
        /// Test StandardDeviationSample with example from OPC UA Part 13 v1.05 Section A.36
        /// Example: Values [10, 20, 30, 40, 50] should give sample std dev ≈ 15.811
        /// </summary>
        [Test]
        public void StandardDeviationSample_SpecExample()
        {
            // Arrange
            var startTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var endTime = startTime.AddSeconds(10);
            var values = new double[] { 10, 20, 30, 40, 50 };
            var dataValues = CreateDataValues(startTime, values, 2000);

            // Act
            var result = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationSample,
                dataValues,
                startTime,
                endTime,
                10000);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Value, Is.Not.Null);
            
            double stdDev = Convert.ToDouble(result.Value, CultureInfo.InvariantCulture);
            
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
            var startTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var endTime = startTime.AddSeconds(5);
            var values = new double[] { 42.5 };
            var dataValues = CreateDataValues(startTime, values);

            // Act
            var result = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationSample,
                dataValues,
                startTime,
                endTime,
                5000);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Value, Is.Not.Null);
            
            double stdDev = Convert.ToDouble(result.Value, CultureInfo.InvariantCulture);
            Assert.That(stdDev, Is.EqualTo(0.0), 
                "Sample standard deviation with single value should be 0");
        }

        /// <summary>
        /// Test StandardDeviationSample with two values
        /// </summary>
        [Test]
        public void StandardDeviationSample_TwoValues()
        {
            // Arrange
            var startTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var endTime = startTime.AddSeconds(5);
            var values = new double[] { 10, 20 };
            var dataValues = CreateDataValues(startTime, values, 2000);

            // Act
            var result = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationSample,
                dataValues,
                startTime,
                endTime,
                5000);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Value, Is.Not.Null);
            
            double stdDev = Convert.ToDouble(result.Value, CultureInfo.InvariantCulture);
            
            // Expected: sqrt(((10-15)^2 + (20-15)^2) / (2-1)) = sqrt(50) ≈ 7.0710678
            Assert.That(stdDev, Is.EqualTo(7.0710678).Within(0.0001), 
                "Sample standard deviation with two values should be calculated correctly");
        }

        #endregion

        #region VariancePopulation Tests

        /// <summary>
        /// Test VariancePopulation with example from OPC UA Part 13 v1.05 Section A.37
        /// Example: Values [10, 20, 30, 40, 50] should give population variance = 200
        /// </summary>
        [Test]
        public void VariancePopulation_SpecExample()
        {
            // Arrange
            var startTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var firstValueTime = startTime.AddMilliseconds(500); // First value after start
            var values = new double[] { 10, 20, 30, 40, 50 };
            var dataValues = CreateDataValues(firstValueTime, values, 2000);
            var endTime = startTime.AddSeconds(12); // End well after last value

            // Act
            var result = ComputeAggregate(
                ObjectIds.AggregateFunction_VariancePopulation,
                dataValues,
                startTime,
                endTime,
                12000);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Value, Is.Not.Null);
            
            double variance = Convert.ToDouble(result.Value, CultureInfo.InvariantCulture);
            
            // Expected: ((10-30)^2 + (20-30)^2 + (30-30)^2 + (40-30)^2 + (50-30)^2) / 5
            // = (400 + 100 + 0 + 100 + 400) / 5 = 200
            Assert.That(variance, Is.EqualTo(200.0).Within(0.0001), 
                "Variance population should match expected value");
        }

        /// <summary>
        /// Test VariancePopulation with single value - should return 0
        /// </summary>
        [Test]
        public void VariancePopulation_SingleValue_ReturnsZero()
        {
            // Arrange
            var startTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var firstValueTime = startTime.AddMilliseconds(500); // Value after start
            var values = new double[] { 42.5 };
            var dataValues = CreateDataValues(firstValueTime, values);
            var endTime = startTime.AddSeconds(5); // End well after value

            // Act
            var result = ComputeAggregate(
                ObjectIds.AggregateFunction_VariancePopulation,
                dataValues,
                startTime,
                endTime,
                5000);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Value, Is.Not.Null);
            
            double variance = Convert.ToDouble(result.Value, CultureInfo.InvariantCulture);
            Assert.That(variance, Is.EqualTo(0.0), 
                "Variance of single value should be 0");
        }

        /// <summary>
        /// Test VariancePopulation with identical values - should return 0
        /// </summary>
        [Test]
        public void VariancePopulation_IdenticalValues_ReturnsZero()
        {
            // Arrange
            var startTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var endTime = startTime.AddSeconds(10);
            var values = new double[] { 100, 100, 100, 100, 100 };
            var dataValues = CreateDataValues(startTime, values, 2000);

            // Act
            var result = ComputeAggregate(
                ObjectIds.AggregateFunction_VariancePopulation,
                dataValues,
                startTime,
                endTime,
                10000);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Value, Is.Not.Null);
            
            double variance = Convert.ToDouble(result.Value, CultureInfo.InvariantCulture);
            Assert.That(variance, Is.EqualTo(0.0), 
                "Variance of identical values should be 0");
        }

        #endregion

        #region VarianceSample Tests

        /// <summary>
        /// Test VarianceSample with example from OPC UA Part 13 v1.05 Section A.38
        /// Example: Values [10, 20, 30, 40, 50] should give sample variance = 250
        /// </summary>
        [Test]
        public void VarianceSample_SpecExample()
        {
            // Arrange
            var startTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var endTime = startTime.AddSeconds(10);
            var values = new double[] { 10, 20, 30, 40, 50 };
            var dataValues = CreateDataValues(startTime, values, 2000);

            // Act
            var result = ComputeAggregate(
                ObjectIds.AggregateFunction_VarianceSample,
                dataValues,
                startTime,
                endTime,
                10000);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Value, Is.Not.Null);
            
            double variance = Convert.ToDouble(result.Value, CultureInfo.InvariantCulture);
            
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
            var startTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var endTime = startTime.AddSeconds(5);
            var values = new double[] { 42.5 };
            var dataValues = CreateDataValues(startTime, values);

            // Act
            var result = ComputeAggregate(
                ObjectIds.AggregateFunction_VarianceSample,
                dataValues,
                startTime,
                endTime,
                5000);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Value, Is.Not.Null);
            
            double variance = Convert.ToDouble(result.Value, CultureInfo.InvariantCulture);
            Assert.That(variance, Is.EqualTo(0.0), 
                "Sample variance with single value should be 0");
        }

        /// <summary>
        /// Test VarianceSample with two values
        /// </summary>
        [Test]
        public void VarianceSample_TwoValues()
        {
            // Arrange
            var startTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var endTime = startTime.AddSeconds(5);
            var values = new double[] { 10, 20 };
            var dataValues = CreateDataValues(startTime, values, 2000);

            // Act
            var result = ComputeAggregate(
                ObjectIds.AggregateFunction_VarianceSample,
                dataValues,
                startTime,
                endTime,
                5000);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Value, Is.Not.Null);
            
            double variance = Convert.ToDouble(result.Value, CultureInfo.InvariantCulture);
            
            // Expected: ((10-15)^2 + (20-15)^2) / (2-1) = 50
            Assert.That(variance, Is.EqualTo(50.0).Within(0.0001), 
                "Sample variance with two values should be calculated correctly");
        }

        #endregion

        #region Relationship Tests

        /// <summary>
        /// Verify that sample standard deviation is the square root of sample variance
        /// </summary>
        [Test]
        public void SampleStdDevIsSquareRootOfSampleVariance()
        {
            // Arrange
            var startTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var endTime = startTime.AddSeconds(10);
            var values = new double[] { 5, 15, 25, 35, 45 };
            var dataValues = CreateDataValues(startTime, values, 2000);

            // Act - compute both sample variance and sample standard deviation
            var varianceResult = ComputeAggregate(
                ObjectIds.AggregateFunction_VarianceSample,
                dataValues,
                startTime,
                endTime,
                10000);

            var stdDevResult = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationSample,
                dataValues,
                startTime,
                endTime,
                10000);

            // Assert
            Assert.That(varianceResult, Is.Not.Null);
            Assert.That(stdDevResult, Is.Not.Null);
            
            double variance = Convert.ToDouble(varianceResult.Value, CultureInfo.InvariantCulture);
            double stdDev = Convert.ToDouble(stdDevResult.Value, CultureInfo.InvariantCulture);
            
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
            var startTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var endTime = startTime.AddSeconds(10);
            var values = new double[] { 5, 15, 25, 35, 45 };
            var dataValues = CreateDataValues(startTime, values, 2000);

            // Act - compute both population variance and population standard deviation
            var varianceResult = ComputeAggregate(
                ObjectIds.AggregateFunction_VariancePopulation,
                dataValues,
                startTime,
                endTime,
                10000);

            var stdDevResult = ComputeAggregate(
                ObjectIds.AggregateFunction_StandardDeviationPopulation,
                dataValues,
                startTime,
                endTime,
                10000);

            // Assert
            Assert.That(varianceResult, Is.Not.Null);
            Assert.That(stdDevResult, Is.Not.Null);
            
            double variance = Convert.ToDouble(varianceResult.Value, CultureInfo.InvariantCulture);
            double stdDev = Convert.ToDouble(stdDevResult.Value, CultureInfo.InvariantCulture);
            
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
            var startTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var endTime = startTime.AddSeconds(10);
            var values = new double[] { 12, 18, 24, 36, 42 };
            var dataValues = CreateDataValues(startTime, values, 2000);

            // Act
            var sampleVarianceResult = ComputeAggregate(
                ObjectIds.AggregateFunction_VarianceSample,
                dataValues,
                startTime,
                endTime,
                10000);

            var populationVarianceResult = ComputeAggregate(
                ObjectIds.AggregateFunction_VariancePopulation,
                dataValues,
                startTime,
                endTime,
                10000);

            // Assert
            Assert.That(sampleVarianceResult, Is.Not.Null);
            Assert.That(populationVarianceResult, Is.Not.Null);
            
            double sampleVariance = Convert.ToDouble(sampleVarianceResult.Value, CultureInfo.InvariantCulture);
            double populationVariance = Convert.ToDouble(populationVarianceResult.Value, CultureInfo.InvariantCulture);
            
            Assert.That(sampleVariance, Is.GreaterThanOrEqualTo(populationVariance),
                "Sample variance should be greater than or equal to population variance");
        }

        #endregion
    }
}
