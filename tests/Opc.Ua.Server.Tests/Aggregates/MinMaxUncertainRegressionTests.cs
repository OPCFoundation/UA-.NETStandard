/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Aggregates
{
    [TestFixture]
    [Category("Aggregators")]
    [Parallelizable(ParallelScope.All)]
    public sealed class MinMaxUncertainRegressionTests
    {
        [TestCase(Objects.AggregateFunction_Minimum, 1.0, 5.0)]
        [TestCase(Objects.AggregateFunction_Maximum, 9.0, 5.0)]
        [TestCase(Objects.AggregateFunction_Range, 1.0, 0.0)]
        [TestCase(Objects.AggregateFunction_Range, 9.0, 0.0)]
        [TestCase(Objects.AggregateFunction_MinimumActualTime, 1.0, 5.0)]
        [TestCase(Objects.AggregateFunction_MaximumActualTime, 9.0, 5.0)]
        public void MinMaxPreservesGoodExtremaAndReportsUncertainInputs(
            uint aggregateTypeId,
            double uncertainValue,
            double expectedValue)
        {
            DateTimeUtc start = new(2025, 1, 1, 0, 0, 0);
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                new NodeId(aggregateTypeId),
                start,
                start.AddMilliseconds(10_000),
                10_000,
                false,
                new AggregateConfiguration
                {
                    TreatUncertainAsBad = false,
                    PercentDataBad = 100,
                    PercentDataGood = 100
                },
                NUnitTelemetryContext.Create());

            calculator.QueueRawValue(new DataValue(5.0, StatusCodes.Good, start, start));
            DateTimeUtc middle = start.AddMilliseconds(5_000);
            calculator.QueueRawValue(new DataValue(
                uncertainValue, StatusCodes.Uncertain, middle, middle));
            DateTimeUtc end = start.AddMilliseconds(10_000);
            calculator.QueueRawValue(new DataValue(7.0, StatusCodes.Good, end, end));

            Assert.That(calculator.TryGetProcessedValue(true, out DataValue result), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(result.WrappedValue.TryGetValue(out double value), Is.True);
                Assert.That(value, Is.EqualTo(expectedValue));
                Assert.That(
                    result.StatusCode,
                    Is.EqualTo(StatusCodes.UncertainDataSubNormal
                        .WithAggregateBits(AggregateBits.Calculated)));
                Assert.That(result.SourceTimestamp, Is.EqualTo(start));
            });
        }

        [TestCase(Objects.AggregateFunction_Minimum, 5.0, 0, AggregateBits.Raw)]
        [TestCase(Objects.AggregateFunction_Maximum, 10.0, 0, AggregateBits.Calculated)]
        [TestCase(Objects.AggregateFunction_Range, 5.0, 0, AggregateBits.Calculated)]
        [TestCase(Objects.AggregateFunction_MinimumActualTime, 5.0, 0, AggregateBits.Raw)]
        [TestCase(Objects.AggregateFunction_MaximumActualTime, 10.0, 2_000, AggregateBits.Raw)]
        public void UncertainInsideGoodRangePreservesGoodQuality(
            uint aggregateTypeId,
            double expectedValue,
            int timestampOffset,
            AggregateBits expectedBits)
        {
            DateTimeUtc start = new(2025, 1, 1, 0, 0, 0);
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                new NodeId(aggregateTypeId),
                start,
                start.AddMilliseconds(10_000),
                10_000,
                false,
                new AggregateConfiguration
                {
                    TreatUncertainAsBad = false,
                    PercentDataBad = 100,
                    PercentDataGood = 100
                },
                NUnitTelemetryContext.Create());

            calculator.QueueRawValue(new DataValue(5.0, StatusCodes.Good, start, start));
            DateTimeUtc maximum = start.AddMilliseconds(2_000);
            calculator.QueueRawValue(new DataValue(10.0, StatusCodes.Good, maximum, maximum));
            DateTimeUtc middle = start.AddMilliseconds(5_000);
            calculator.QueueRawValue(new DataValue(7.0, StatusCodes.Uncertain, middle, middle));
            DateTimeUtc end = start.AddMilliseconds(10_000);
            calculator.QueueRawValue(new DataValue(8.0, StatusCodes.Good, end, end));

            Assert.That(calculator.TryGetProcessedValue(true, out DataValue result), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(result.WrappedValue.TryGetValue(out double value), Is.True);
                Assert.That(value, Is.EqualTo(expectedValue));
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good.WithAggregateBits(expectedBits)));
                Assert.That(result.SourceTimestamp, Is.EqualTo(start.AddMilliseconds(timestampOffset)));
            });
        }

        [TestCase(Objects.AggregateFunction_Minimum, false)]
        [TestCase(Objects.AggregateFunction_Maximum, false)]
        [TestCase(Objects.AggregateFunction_Range, false)]
        [TestCase(Objects.AggregateFunction_MinimumActualTime, false)]
        [TestCase(Objects.AggregateFunction_MaximumActualTime, false)]
        [TestCase(Objects.AggregateFunction_Minimum, true)]
        public void AllUncertainIntervalDoesNotInventAGoodExtremum(
            uint aggregateTypeId,
            bool treatUncertainAsBad)
        {
            DateTimeUtc start = new(2025, 1, 1, 0, 0, 0);
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                new NodeId(aggregateTypeId),
                start,
                start.AddMilliseconds(10_000),
                10_000,
                false,
                new AggregateConfiguration
                {
                    TreatUncertainAsBad = treatUncertainAsBad,
                    PercentDataBad = 100,
                    PercentDataGood = 100
                },
                NUnitTelemetryContext.Create());

            calculator.QueueRawValue(new DataValue(5.0, StatusCodes.Uncertain, start, start));
            DateTimeUtc end = start.AddMilliseconds(10_000);
            calculator.QueueRawValue(new DataValue(7.0, StatusCodes.Good, end, end));

            Assert.That(calculator.TryGetProcessedValue(true, out DataValue result), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode.Code & 0xFFFF0000u, Is.EqualTo(StatusCodes.BadNoData.Code));
                Assert.That(result.WrappedValue.IsNull, Is.True);
            });
        }
    }
}
