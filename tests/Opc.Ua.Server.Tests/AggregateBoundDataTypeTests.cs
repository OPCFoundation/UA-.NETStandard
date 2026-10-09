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

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests how the data type of the raw values affects interpolated bounds and the
    /// TreatUncertainAsBad handling of value-based aggregates (CTT aggregate units).
    /// </summary>
    [TestFixture]
    [Category("Aggregators")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class AggregateBoundDataTypeTests
    {
        /// <summary>
        /// Aggregates that return a Double use real-valued bounds on Int32 data (Part 13
        /// §3.1.8): a ramp from 0 to 1 over 10 s has the bounds 0.2 and 0.5 in [2 s, 5 s).
        /// Rounding the bounds to Int32 (0 and 1) would give 0.5 and 1.5.
        /// </summary>
        [TestCase("TimeAverage", 0.35)]
        [TestCase("TimeAverage2", 0.35)]
        [TestCase("Total", 1.05)]
        [TestCase("Total2", 1.05)]
        public void DoubleAggregatesKeepRealBoundsOnInt32Data(string aggregateName, double expected)
        {
            List<DataValue> results = Run(
                GetAggregateId(aggregateName),
                [IntAt(0, 0), IntAt(10, 1)],
                AtSeconds(2),
                AtSeconds(5),
                3000,
                CreateConfiguration(true));

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].WrappedValue.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(expected).Within(1e-9));
        }

        /// <summary>
        /// Interpolative returns the raw data type, so the interpolated Int32 value is
        /// rounded (Part 4 §7.7.3): 0.2 becomes 0 and 0.6 becomes 1.
        /// </summary>
        [Test]
        public void InterpolativeStillRoundsInt32Bounds()
        {
            List<DataValue> results = Run(
                ObjectIds.AggregateFunction_Interpolative,
                [IntAt(0, 0), IntAt(10, 1)],
                AtSeconds(2),
                AtSeconds(10),
                4000,
                CreateConfiguration(true));

            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results[0].WrappedValue.TryGetValue(out int first), Is.True);
            Assert.That(results[1].WrappedValue.TryGetValue(out int second), Is.True);
            Assert.That(first, Is.Zero);
            Assert.That(second, Is.EqualTo(1));
        }

        /// <summary>
        /// Boolean values cannot be interpolated with a sloped line (Part 13 §5.4.2.3), so the
        /// simple bounds use the prior value even when the variable is not Stepped.
        /// </summary>
        [Test]
        public void BooleanBoundsUseThePriorValue()
        {
            List<DataValue> rawValues = [BoolAt(0, false), BoolAt(10, true), BoolAt(20, false)];

            List<DataValue> start = Run(
                ObjectIds.AggregateFunction_StartBound,
                rawValues,
                AtSeconds(5),
                AtSeconds(15),
                10_000,
                CreateConfiguration(true));
            List<DataValue> end = Run(
                ObjectIds.AggregateFunction_EndBound,
                rawValues,
                AtSeconds(5),
                AtSeconds(15),
                10_000,
                CreateConfiguration(true));

            Assert.That(start[0].WrappedValue.TryGetValue(out bool startValue), Is.True);
            Assert.That(startValue, Is.False);
            Assert.That(StatusCode.IsGood(start[0].StatusCode), Is.True);
            Assert.That(end[0].WrappedValue.TryGetValue(out bool endValue), Is.True);
            Assert.That(endValue, Is.True);
            Assert.That(StatusCode.IsGood(end[0].StatusCode), Is.True);
        }

        /// <summary>
        /// A String bound between "23" and "24" is the prior value, not "23.5".
        /// </summary>
        [Test]
        public void StringEndBoundUsesThePriorValue()
        {
            List<DataValue> results = Run(
                ObjectIds.AggregateFunction_EndBound,
                [StringAt(0, "23"), StringAt(10, "24"), StringAt(20, "25")],
                AtSeconds(0),
                AtSeconds(5),
                5000,
                CreateConfiguration(true));

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].WrappedValue.TryGetValue(out string value), Is.True);
            Assert.That(value, Is.EqualTo("23"));
        }

        /// <summary>
        /// The Boolean state before the first value in the interval is the prior value
        /// (false), so only the 5 s after the transition to true count as non-zero.
        /// </summary>
        [TestCase(true, 5000)]
        [TestCase(false, 5000)]
        public void BooleanDurationInStateUsesThePriorValue(bool nonZero, double expected)
        {
            List<DataValue> results = Run(
                nonZero
                    ? ObjectIds.AggregateFunction_DurationInStateNonZero
                    : ObjectIds.AggregateFunction_DurationInStateZero,
                [BoolAt(0, false), BoolAt(10, true), BoolAt(20, false)],
                AtSeconds(5),
                AtSeconds(15),
                10_000,
                CreateConfiguration(true));

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].WrappedValue.TryGetValue(out double duration), Is.True);
            Assert.That(duration, Is.EqualTo(expected).Within(1e-9));
            Assert.That(StatusCode.IsGood(results[0].StatusCode), Is.True);
        }

        /// <summary>
        /// With TreatUncertainAsBad = false an Uncertain value is equivalent to Good
        /// (Part 13 §4.2.1.2), so Average includes it; otherwise it is excluded.
        /// </summary>
        [TestCase(false, 2.5)]
        [TestCase(true, 1.0)]
        public void AverageHonorsTreatUncertainAsBad(bool treatUncertainAsBad, double expected)
        {
            List<DataValue> results = Run(
                ObjectIds.AggregateFunction_Average,
                [
                    DoubleAt(0, 1, StatusCodes.Good),
                    DoubleAt(5, 4, StatusCodes.UncertainSubstituteValue),
                    DoubleAt(10, 9, StatusCodes.Good)
                ],
                AtSeconds(0),
                AtSeconds(10),
                10_000,
                CreateConfiguration(treatUncertainAsBad));

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].WrappedValue.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(expected).Within(1e-9));
        }

        private static List<DataValue> Run(
            NodeId aggregateId,
            List<DataValue> rawValues,
            DateTimeUtc startTime,
            DateTimeUtc endTime,
            double processingInterval,
            AggregateConfiguration configuration)
        {
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                aggregateId,
                startTime,
                endTime,
                processingInterval,
                false,
                configuration,
                NUnitTelemetryContext.Create())!;

            foreach (DataValue rawValue in rawValues)
            {
                Assert.That(calculator.QueueRawValue(rawValue), Is.True);
            }

            var results = new List<DataValue>();
            while (calculator.TryGetProcessedValue(true, out DataValue value))
            {
                results.Add(value);
            }
            return results;
        }

        private static DataValue IntAt(int seconds, int value)
        {
            return new DataValue(Variant.From(value), StatusCodes.Good, AtSeconds(seconds), AtSeconds(seconds));
        }

        private static DataValue BoolAt(int seconds, bool value)
        {
            return new DataValue(Variant.From(value), StatusCodes.Good, AtSeconds(seconds), AtSeconds(seconds));
        }

        private static DataValue StringAt(int seconds, string value)
        {
            return new DataValue(Variant.From(value), StatusCodes.Good, AtSeconds(seconds), AtSeconds(seconds));
        }

        private static DataValue DoubleAt(int seconds, double value, StatusCode statusCode)
        {
            return new DataValue(Variant.From(value), statusCode, AtSeconds(seconds), AtSeconds(seconds));
        }

        private static DateTimeUtc AtSeconds(int seconds)
        {
            return s_baseTime.AddMilliseconds(seconds * 1000);
        }

        private static AggregateConfiguration CreateConfiguration(bool treatUncertainAsBad)
        {
            return new AggregateConfiguration
            {
                UseServerCapabilitiesDefaults = false,
                TreatUncertainAsBad = treatUncertainAsBad,
                PercentDataBad = 100,
                PercentDataGood = 100,
                UseSlopedExtrapolation = false
            };
        }

        private static NodeId GetAggregateId(string aggregateName)
        {
            return aggregateName switch
            {
                "TimeAverage" => ObjectIds.AggregateFunction_TimeAverage,
                "TimeAverage2" => ObjectIds.AggregateFunction_TimeAverage2,
                "Total" => ObjectIds.AggregateFunction_Total,
                _ => ObjectIds.AggregateFunction_Total2
            };
        }

        private static readonly DateTimeUtc s_baseTime = new(2025, 1, 1, 0, 0, 0);
    }
}
