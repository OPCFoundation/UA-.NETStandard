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
    public class MinMaxAggregateCalculatorTests
    {
        private static readonly DateTimeUtc s_baseTime = new(2024, 1, 1, 0, 0, 0);
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
                dataValues.Add(new DataValue(
                    new Variant(values[i]),
                    StatusCodes.Good,
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

        private static DataValue RawValue(DateTimeUtc timestamp, double value)
        {
            return new DataValue(new Variant(value), StatusCodes.Good, timestamp, timestamp);
        }

        private DataValue ComputeSingleInterval(
            NodeId aggregateId,
            List<DataValue> ascendingValues,
            DateTimeUtc startTime,
            DateTimeUtc endTime,
            double processingInterval)
        {
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                aggregateId, startTime, endTime, processingInterval, false, m_configuration, m_telemetry);

            // Reverse reads (endTime earlier than startTime) require the raw stream to be
            // queued latest-first; forward reads require earliest-first.
            if (endTime < startTime)
            {
                for (int index = ascendingValues.Count - 1; index >= 0; index--)
                {
                    calculator.QueueRawValue(ascendingValues[index]);
                }
            }
            else
            {
                foreach (DataValue value in ascendingValues)
                {
                    calculator.QueueRawValue(value);
                }
            }

            var results = new List<DataValue>();
            while (calculator.TryGetProcessedValue(true, out DataValue result))
            {
                results.Add(result);
            }

            Assert.That(results, Has.Count.EqualTo(1));
            return results[0];
        }

        [Test]
        public void Minimum_ReturnsSmallestValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 5, 20, 3, 15, 3];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Minimum,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(3.0).Within(0.0001));
        }

        [Test]
        public void Maximum_ReturnsLargestValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 50, 20, 30, 15, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Maximum,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(50.0).Within(0.0001));
        }

        [Test]
        public void Range_ReturnsMaxMinusMIn()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 50, 20, 30, 15, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Range,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(40.0).Within(0.0001));
        }

        [Test]
        public void MinimumActualTime_ReturnsTimestampOfMin()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 5, 20, 3, 15, 3];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_MinimumActualTime,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
        }

        [Test]
        public void MaximumActualTime_ReturnsTimestampOfMax()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 50, 20, 30, 15, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_MaximumActualTime,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
        }

        [Test]
        public void Minimum_SingleValue_ReturnsThatValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [42, 42];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(4000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Minimum,
                dataValues, startTime, endTime, 4000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(42.0).Within(0.0001));
        }

        [Test]
        public void Maximum_NegativeValues_ReturnsLargest()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [-10, -5, -20, -3, -15, -3];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Maximum,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(-3.0).Within(0.0001));
        }

        [Test]
        public void Range_AllSameValue_ReturnsZero()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [7, 7, 7, 7];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(8000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Range,
                dataValues, startTime, endTime, 8000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.Zero.Within(0.0001));
        }

        [Test]
        public void Minimum2_ReturnsSmallestValueIncludingSimpleBounds()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [50, 3, 20, 40, 15, 15];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Minimum2,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(3.0).Within(0.0001));
        }

        [Test]
        public void Maximum2_ReturnsLargestValueIncludingSimpleBounds()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 50, 20, 30, 15, 15];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Maximum2,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(50.0).Within(0.0001));
        }

        [Test]
        public void Range2_ReturnsMaximum2MinusMinimum2()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 50, 20, 3, 15, 15];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Range2,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(47.0).Within(0.0001));
        }

        [Test]
        public void MinimumActualTime2_ReturnsMinimumValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [50, 3, 20, 40, 15, 15];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_MinimumActualTime2,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(3.0).Within(0.0001));
            // Part 13 §5.4.3.17: timestamp is the actual time of the minimum value (within the range).
            Assert.That(result.SourceTimestamp, Is.GreaterThanOrEqualTo(startTime));
            Assert.That(result.SourceTimestamp, Is.LessThanOrEqualTo(endTime));
        }

        [Test]
        public void MaximumActualTime2_ReturnsMaximumValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 50, 20, 30, 15, 15];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_MaximumActualTime2,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(50.0).Within(0.0001));
            Assert.That(result.SourceTimestamp, Is.GreaterThanOrEqualTo(startTime));
            Assert.That(result.SourceTimestamp, Is.LessThanOrEqualTo(endTime));
        }

        // Regression tests for the timestamp provenance of ComputeMinMax and ComputeMinMax2.
        // Commit a711c4b28 removed the `else` clauses that returned the region timestamp,
        // relying only on the `if (returnActualTime)` branch returning early. The following
        // tests pin down both sides of that control flow so a future control-flow cleanup
        // cannot silently swap which timestamp is reported: the *ActualTime variants must
        // keep reporting the timestamp of the raw sample that was selected as the minimum or
        // maximum, while the plain variants must keep reporting the processing region's own
        // timestamp, never the raw sample's timestamp.

        [Test]
        public void MinimumActualTimeSourceTimestampMatchesSelectedMinimumValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 5, 20, 3, 15, 3];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);
            DateTimeUtc expectedMinimumTimestamp = firstValueTime.AddMilliseconds(3 * 2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_MinimumActualTime,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(3.0).Within(0.0001));
            // The timestamp must come from the raw sample that produced the minimum,
            // not from the processing region's own start/end timestamp.
            Assert.That(result.SourceTimestamp, Is.EqualTo(expectedMinimumTimestamp));
            Assert.That(result.ServerTimestamp, Is.EqualTo(expectedMinimumTimestamp));
            Assert.That(result.SourceTimestamp, Is.Not.EqualTo(startTime));
            Assert.That(result.SourceTimestamp, Is.Not.EqualTo(endTime));
        }

        [Test]
        public void MaximumActualTimeSourceTimestampMatchesSelectedMaximumValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 50, 20, 30, 15, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);
            DateTimeUtc expectedMaximumTimestamp = firstValueTime.AddMilliseconds(1 * 2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_MaximumActualTime,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(50.0).Within(0.0001));
            // The timestamp must come from the raw sample that produced the maximum,
            // not from the processing region's own start/end timestamp.
            Assert.That(result.SourceTimestamp, Is.EqualTo(expectedMaximumTimestamp));
            Assert.That(result.ServerTimestamp, Is.EqualTo(expectedMaximumTimestamp));
            Assert.That(result.SourceTimestamp, Is.Not.EqualTo(startTime));
            Assert.That(result.SourceTimestamp, Is.Not.EqualTo(endTime));
        }

        [Test]
        public void MinimumSourceTimestampMatchesSliceStartNotSelectedValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 5, 20, 3, 15, 3];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);
            DateTimeUtc rawMinimumTimestamp = firstValueTime.AddMilliseconds(3 * 2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Minimum,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(3.0).Within(0.0001));
            // The plain Minimum aggregate reports the processing region's own timestamp
            // (the slice start), never the timestamp of the raw sample that was selected.
            Assert.That(result.SourceTimestamp, Is.EqualTo(startTime));
            Assert.That(result.ServerTimestamp, Is.EqualTo(startTime));
            Assert.That(result.SourceTimestamp, Is.Not.EqualTo(rawMinimumTimestamp));
        }

        [Test]
        public void MaximumSourceTimestampMatchesSliceStartNotSelectedValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 50, 20, 30, 15, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);
            DateTimeUtc rawMaximumTimestamp = firstValueTime.AddMilliseconds(1 * 2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Maximum,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(50.0).Within(0.0001));
            // The plain Maximum aggregate reports the processing region's own timestamp
            // (the slice start), never the timestamp of the raw sample that was selected.
            Assert.That(result.SourceTimestamp, Is.EqualTo(startTime));
            Assert.That(result.ServerTimestamp, Is.EqualTo(startTime));
            Assert.That(result.SourceTimestamp, Is.Not.EqualTo(rawMaximumTimestamp));
        }

        [Test]
        public void MinimumActualTime2SourceTimestampMatchesSelectedMinimumValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 5, 20, 3, 15, 3];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);
            DateTimeUtc expectedMinimumTimestamp = firstValueTime.AddMilliseconds(3 * 2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_MinimumActualTime2,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(3.0).Within(0.0001));
            // Same provenance guarantee as MinimumActualTime, but exercised through
            // ComputeMinMax2's simple-bounds code path.
            Assert.That(result.SourceTimestamp, Is.EqualTo(expectedMinimumTimestamp));
            Assert.That(result.ServerTimestamp, Is.EqualTo(expectedMinimumTimestamp));
            Assert.That(result.SourceTimestamp, Is.Not.EqualTo(startTime));
            Assert.That(result.SourceTimestamp, Is.Not.EqualTo(endTime));
        }

        [Test]
        public void MaximumActualTime2SourceTimestampMatchesSelectedMaximumValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 50, 20, 30, 15, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);
            DateTimeUtc expectedMaximumTimestamp = firstValueTime.AddMilliseconds(1 * 2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_MaximumActualTime2,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(50.0).Within(0.0001));
            // Same provenance guarantee as MaximumActualTime, but exercised through
            // ComputeMinMax2's simple-bounds code path.
            Assert.That(result.SourceTimestamp, Is.EqualTo(expectedMaximumTimestamp));
            Assert.That(result.ServerTimestamp, Is.EqualTo(expectedMaximumTimestamp));
            Assert.That(result.SourceTimestamp, Is.Not.EqualTo(startTime));
            Assert.That(result.SourceTimestamp, Is.Not.EqualTo(endTime));
        }

        [Test]
        public void Minimum2SourceTimestampMatchesSliceStartNotSelectedValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 5, 20, 3, 15, 3];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);
            DateTimeUtc rawMinimumTimestamp = firstValueTime.AddMilliseconds(3 * 2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Minimum2,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(3.0).Within(0.0001));
            // The plain Minimum2 aggregate reports the processing region's own timestamp
            // (the slice start), never the timestamp of the raw sample that was selected.
            Assert.That(result.SourceTimestamp, Is.EqualTo(startTime));
            Assert.That(result.ServerTimestamp, Is.EqualTo(startTime));
            Assert.That(result.SourceTimestamp, Is.Not.EqualTo(rawMinimumTimestamp));
        }

        [Test]
        public void Maximum2SourceTimestampMatchesSliceStartNotSelectedValue()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc firstValueTime = startTime.AddMilliseconds(500);
            double[] values = [10, 50, 20, 30, 15, 50];
            List<DataValue> dataValues = CreateDataValues(firstValueTime, values, 2000);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);
            DateTimeUtc rawMaximumTimestamp = firstValueTime.AddMilliseconds(1 * 2000);

            DataValue result = ComputeAggregate(
                ObjectIds.AggregateFunction_Maximum2,
                dataValues, startTime, endTime, 12000);

            Assert.That(result.IsNull, Is.False);
            Assert.That((double)result.WrappedValue.ConvertToDouble(), Is.EqualTo(50.0).Within(0.0001));
            // The plain Maximum2 aggregate reports the processing region's own timestamp
            // (the slice start), never the timestamp of the raw sample that was selected.
            Assert.That(result.SourceTimestamp, Is.EqualTo(startTime));
            Assert.That(result.ServerTimestamp, Is.EqualTo(startTime));
            Assert.That(result.SourceTimestamp, Is.Not.EqualTo(rawMaximumTimestamp));
        }

        // Raw-versus-Calculated provenance tests for the Minimum/Maximum family.
        // Part 13 §5.4.3.10/§5.4.3.11 return the min/max sample stamped at the start of the
        // interval and mark it Good, Raw when the selected sample sits on that start timestamp,
        // otherwise Good, Calculated. Part 11/Part 13 §5.4.2.2 define the interval start for a
        // reverse read (startTime later than endTime) as the later timestamp, so the request
        // direction - not the chronological lower bound - decides Raw versus Calculated.
        // Each dataset spans the full interval so the Partial bit is never set, letting the
        // tests assert the exact aggregate info bits.

        [Test]
        public void MaximumForwardWithMaximumAtIntervalStartIsRaw()
        {
            DateTimeUtc endTime = s_baseTime.AddMilliseconds(12000);
            var values = new List<DataValue>
            {
                RawValue(s_baseTime, 50),
                RawValue(s_baseTime.AddMilliseconds(3000), 20),
                RawValue(s_baseTime.AddMilliseconds(6000), 10),
                RawValue(s_baseTime.AddMilliseconds(9000), 30),
                RawValue(endTime, 40)
            };

            DataValue result = ComputeSingleInterval(
                ObjectIds.AggregateFunction_Maximum, values, s_baseTime, endTime, 12000);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(50.0).Within(0.0001));
            Assert.That(result.SourceTimestamp, Is.EqualTo(s_baseTime));
            Assert.That(result.ServerTimestamp, Is.EqualTo(s_baseTime));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Raw));
        }

        [Test]
        public void MaximumForwardWithMaximumNotAtIntervalStartIsCalculated()
        {
            DateTimeUtc endTime = s_baseTime.AddMilliseconds(12000);
            var values = new List<DataValue>
            {
                RawValue(s_baseTime, 10),
                RawValue(s_baseTime.AddMilliseconds(3000), 20),
                RawValue(s_baseTime.AddMilliseconds(6000), 50),
                RawValue(s_baseTime.AddMilliseconds(9000), 30),
                RawValue(endTime, 40)
            };

            DataValue result = ComputeSingleInterval(
                ObjectIds.AggregateFunction_Maximum, values, s_baseTime, endTime, 12000);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(50.0).Within(0.0001));
            Assert.That(result.SourceTimestamp, Is.EqualTo(s_baseTime));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Calculated));
        }

        [Test]
        public void MaximumReverseWithMaximumAtRequestIntervalStartIsRaw()
        {
            DateTimeUtc startTime = s_baseTime.AddMilliseconds(12000);
            var values = new List<DataValue>
            {
                RawValue(s_baseTime, 10),
                RawValue(s_baseTime.AddMilliseconds(3000), 20),
                RawValue(s_baseTime.AddMilliseconds(6000), 30),
                RawValue(s_baseTime.AddMilliseconds(9000), 40),
                RawValue(startTime, 50)
            };

            DataValue result = ComputeSingleInterval(
                ObjectIds.AggregateFunction_Maximum, values, startTime, s_baseTime, 12000);

            // The maximum sits on the later timestamp, which is the reverse interval start,
            // so it must be reported Good, Raw (not Calculated) and stamped at that timestamp.
            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(50.0).Within(0.0001));
            Assert.That(result.SourceTimestamp, Is.EqualTo(startTime));
            Assert.That(result.ServerTimestamp, Is.EqualTo(startTime));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Raw));
        }

        [Test]
        public void MaximumReverseWithMaximumNotAtRequestIntervalStartIsCalculated()
        {
            DateTimeUtc startTime = s_baseTime.AddMilliseconds(12000);
            var values = new List<DataValue>
            {
                RawValue(s_baseTime, 10),
                RawValue(s_baseTime.AddMilliseconds(3000), 50),
                RawValue(s_baseTime.AddMilliseconds(6000), 30),
                RawValue(s_baseTime.AddMilliseconds(9000), 20),
                RawValue(startTime, 40)
            };

            DataValue result = ComputeSingleInterval(
                ObjectIds.AggregateFunction_Maximum, values, startTime, s_baseTime, 12000);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(50.0).Within(0.0001));
            Assert.That(result.SourceTimestamp, Is.EqualTo(startTime));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Calculated));
        }

        [Test]
        public void MinimumForwardWithMinimumAtIntervalStartIsRaw()
        {
            DateTimeUtc endTime = s_baseTime.AddMilliseconds(12000);
            var values = new List<DataValue>
            {
                RawValue(s_baseTime, 3),
                RawValue(s_baseTime.AddMilliseconds(3000), 20),
                RawValue(s_baseTime.AddMilliseconds(6000), 10),
                RawValue(s_baseTime.AddMilliseconds(9000), 30),
                RawValue(endTime, 40)
            };

            DataValue result = ComputeSingleInterval(
                ObjectIds.AggregateFunction_Minimum, values, s_baseTime, endTime, 12000);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(3.0).Within(0.0001));
            Assert.That(result.SourceTimestamp, Is.EqualTo(s_baseTime));
            Assert.That(result.ServerTimestamp, Is.EqualTo(s_baseTime));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Raw));
        }

        [Test]
        public void MinimumForwardWithMinimumNotAtIntervalStartIsCalculated()
        {
            DateTimeUtc endTime = s_baseTime.AddMilliseconds(12000);
            var values = new List<DataValue>
            {
                RawValue(s_baseTime, 10),
                RawValue(s_baseTime.AddMilliseconds(3000), 20),
                RawValue(s_baseTime.AddMilliseconds(6000), 3),
                RawValue(s_baseTime.AddMilliseconds(9000), 30),
                RawValue(endTime, 40)
            };

            DataValue result = ComputeSingleInterval(
                ObjectIds.AggregateFunction_Minimum, values, s_baseTime, endTime, 12000);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(3.0).Within(0.0001));
            Assert.That(result.SourceTimestamp, Is.EqualTo(s_baseTime));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Calculated));
        }

        [Test]
        public void MinimumReverseWithMinimumAtRequestIntervalStartIsRaw()
        {
            DateTimeUtc startTime = s_baseTime.AddMilliseconds(12000);
            var values = new List<DataValue>
            {
                RawValue(s_baseTime, 10),
                RawValue(s_baseTime.AddMilliseconds(3000), 40),
                RawValue(s_baseTime.AddMilliseconds(6000), 30),
                RawValue(s_baseTime.AddMilliseconds(9000), 20),
                RawValue(startTime, 3)
            };

            DataValue result = ComputeSingleInterval(
                ObjectIds.AggregateFunction_Minimum, values, startTime, s_baseTime, 12000);

            // The minimum sits on the later timestamp (the reverse interval start), so it must
            // be reported Good, Raw and stamped at that timestamp.
            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(3.0).Within(0.0001));
            Assert.That(result.SourceTimestamp, Is.EqualTo(startTime));
            Assert.That(result.ServerTimestamp, Is.EqualTo(startTime));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Raw));
        }

        [Test]
        public void MinimumReverseWithMinimumNotAtRequestIntervalStartIsCalculated()
        {
            DateTimeUtc startTime = s_baseTime.AddMilliseconds(12000);
            var values = new List<DataValue>
            {
                RawValue(s_baseTime, 10),
                RawValue(s_baseTime.AddMilliseconds(3000), 3),
                RawValue(s_baseTime.AddMilliseconds(6000), 20),
                RawValue(s_baseTime.AddMilliseconds(9000), 30),
                RawValue(startTime, 40)
            };

            DataValue result = ComputeSingleInterval(
                ObjectIds.AggregateFunction_Minimum, values, startTime, s_baseTime, 12000);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(3.0).Within(0.0001));
            Assert.That(result.SourceTimestamp, Is.EqualTo(startTime));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Calculated));
        }

        [Test]
        public void MaximumActualTimeReverseReportsActualSampleTimeAsRaw()
        {
            DateTimeUtc startTime = s_baseTime.AddMilliseconds(12000);
            DateTimeUtc expectedMaximumTimestamp = s_baseTime.AddMilliseconds(3000);
            var values = new List<DataValue>
            {
                RawValue(s_baseTime, 10),
                RawValue(expectedMaximumTimestamp, 50),
                RawValue(s_baseTime.AddMilliseconds(6000), 30),
                RawValue(s_baseTime.AddMilliseconds(9000), 20),
                RawValue(startTime, 40)
            };

            DataValue result = ComputeSingleInterval(
                ObjectIds.AggregateFunction_MaximumActualTime, values, startTime, s_baseTime, 12000);

            // The ActualTime variant reports the timestamp of the raw sample, not the interval
            // timestamp, and the raw sample is Good, Raw regardless of read direction.
            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(50.0).Within(0.0001));
            Assert.That(result.SourceTimestamp, Is.EqualTo(expectedMaximumTimestamp));
            Assert.That(result.ServerTimestamp, Is.EqualTo(expectedMaximumTimestamp));
            Assert.That(result.SourceTimestamp, Is.Not.EqualTo(startTime));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Raw));
        }

        [Test]
        public void MinimumActualTimeReverseReportsActualSampleTimeAsRaw()
        {
            DateTimeUtc startTime = s_baseTime.AddMilliseconds(12000);
            DateTimeUtc expectedMinimumTimestamp = s_baseTime.AddMilliseconds(3000);
            var values = new List<DataValue>
            {
                RawValue(s_baseTime, 10),
                RawValue(expectedMinimumTimestamp, 3),
                RawValue(s_baseTime.AddMilliseconds(6000), 20),
                RawValue(s_baseTime.AddMilliseconds(9000), 30),
                RawValue(startTime, 40)
            };

            DataValue result = ComputeSingleInterval(
                ObjectIds.AggregateFunction_MinimumActualTime, values, startTime, s_baseTime, 12000);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(3.0).Within(0.0001));
            Assert.That(result.SourceTimestamp, Is.EqualTo(expectedMinimumTimestamp));
            Assert.That(result.ServerTimestamp, Is.EqualTo(expectedMinimumTimestamp));
            Assert.That(result.SourceTimestamp, Is.Not.EqualTo(startTime));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Raw));
        }

        [Test]
        public void Maximum2ForwardWithMaximumAtIntervalStartIsRaw()
        {
            DateTimeUtc endTime = s_baseTime.AddMilliseconds(12000);
            var values = new List<DataValue>
            {
                RawValue(s_baseTime, 50),
                RawValue(s_baseTime.AddMilliseconds(3000), 20),
                RawValue(s_baseTime.AddMilliseconds(6000), 30),
                RawValue(s_baseTime.AddMilliseconds(9000), 40),
                RawValue(endTime, 45)
            };

            DataValue result = ComputeSingleInterval(
                ObjectIds.AggregateFunction_Maximum2, values, s_baseTime, endTime, 12000);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(50.0).Within(0.0001));
            Assert.That(result.SourceTimestamp, Is.EqualTo(s_baseTime));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Raw));
        }

        [Test]
        public void Maximum2ReverseWithMaximumAtRequestIntervalStartIsRaw()
        {
            DateTimeUtc startTime = s_baseTime.AddMilliseconds(12000);
            var values = new List<DataValue>
            {
                RawValue(s_baseTime, 10),
                RawValue(s_baseTime.AddMilliseconds(3000), 20),
                RawValue(s_baseTime.AddMilliseconds(6000), 30),
                RawValue(s_baseTime.AddMilliseconds(9000), 40),
                RawValue(startTime, 50)
            };

            DataValue result = ComputeSingleInterval(
                ObjectIds.AggregateFunction_Maximum2, values, startTime, s_baseTime, 12000);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(50.0).Within(0.0001));
            Assert.That(result.SourceTimestamp, Is.EqualTo(startTime));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Raw));
        }

        [Test]
        public void Maximum2ReverseWithMaximumNotAtRequestIntervalStartIsCalculated()
        {
            DateTimeUtc startTime = s_baseTime.AddMilliseconds(12000);
            var values = new List<DataValue>
            {
                RawValue(s_baseTime, 10),
                RawValue(s_baseTime.AddMilliseconds(3000), 50),
                RawValue(s_baseTime.AddMilliseconds(6000), 30),
                RawValue(s_baseTime.AddMilliseconds(9000), 20),
                RawValue(startTime, 40)
            };

            DataValue result = ComputeSingleInterval(
                ObjectIds.AggregateFunction_Maximum2, values, startTime, s_baseTime, 12000);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(50.0).Within(0.0001));
            Assert.That(result.SourceTimestamp, Is.EqualTo(startTime));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Calculated));
        }

        [Test]
        public void Minimum2ReverseWithMinimumAtRequestIntervalStartIsRaw()
        {
            DateTimeUtc startTime = s_baseTime.AddMilliseconds(12000);
            var values = new List<DataValue>
            {
                RawValue(s_baseTime, 40),
                RawValue(s_baseTime.AddMilliseconds(3000), 30),
                RawValue(s_baseTime.AddMilliseconds(6000), 20),
                RawValue(s_baseTime.AddMilliseconds(9000), 15),
                RawValue(startTime, 3)
            };

            DataValue result = ComputeSingleInterval(
                ObjectIds.AggregateFunction_Minimum2, values, startTime, s_baseTime, 12000);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(3.0).Within(0.0001));
            Assert.That(result.SourceTimestamp, Is.EqualTo(startTime));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Raw));
        }

        [Test]
        public void Minimum2ReverseWithMinimumNotAtRequestIntervalStartIsCalculated()
        {
            DateTimeUtc startTime = s_baseTime.AddMilliseconds(12000);
            var values = new List<DataValue>
            {
                RawValue(s_baseTime, 40),
                RawValue(s_baseTime.AddMilliseconds(3000), 3),
                RawValue(s_baseTime.AddMilliseconds(6000), 20),
                RawValue(s_baseTime.AddMilliseconds(9000), 30),
                RawValue(startTime, 35)
            };

            DataValue result = ComputeSingleInterval(
                ObjectIds.AggregateFunction_Minimum2, values, startTime, s_baseTime, 12000);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(3.0).Within(0.0001));
            Assert.That(result.SourceTimestamp, Is.EqualTo(startTime));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Calculated));
        }
    }
}
