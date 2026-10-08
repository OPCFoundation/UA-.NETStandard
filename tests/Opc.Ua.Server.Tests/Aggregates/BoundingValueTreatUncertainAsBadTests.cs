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

namespace Opc.Ua.Server.Tests.Aggregates
{
    /// <summary>
    /// Verifies that TreatUncertainAsBad does not apply to bounding values (Mantis 11462):
    /// bounds are formed from non-Bad raw values only (Part 13 §3.1.8, §3.1.9), and an
    /// Uncertain neighbour makes the bound Uncertain_DataSubNormal.
    /// </summary>
    [TestFixture]
    [Category("Aggregators")]
    [Parallelizable(ParallelScope.All)]
    public sealed class BoundingValueTreatUncertainAsBadTests
    {
        private static readonly DateTimeUtc s_origin = new(2025, 1, 1, 0, 0, 0);

        /// <summary>
        /// An Uncertain raw value before the start bound gives an Uncertain bound, not Bad_NoData:
        /// the start bound at 15 s is 15 (between 10 Uncertain and 20 Good), the end bound at 35 s is 35.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void DeltaBoundsWithUncertainValueBeforeBoundIsUncertain(bool treatUncertainAsBad)
        {
            List<DataValue> raw = Raw(
                (0, 0.0, StatusCodes.Good),
                (10, 10.0, StatusCodes.UncertainSubstituteValue),
                (20, 20.0, StatusCodes.Good),
                (30, 30.0, StatusCodes.Good),
                (40, 40.0, StatusCodes.Good));

            DataValue result = Compute(ObjectIds.AggregateFunction_DeltaBounds, raw, treatUncertainAsBad);

            AssertValue(result, 20.0, StatusCodes.UncertainDataSubNormal);
        }

        /// <summary>
        /// A Bad raw value after the end bound makes the bound the stepped value with
        /// Uncertain_DataSubNormal; DeltaBounds uses it (Part 13 §5.4.3.30).
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void DeltaBoundsWithBadValueAfterBoundIsUncertain(bool treatUncertainAsBad)
        {
            List<DataValue> raw = Raw(
                (0, 0.0, StatusCodes.Good),
                (10, 10.0, StatusCodes.Good),
                (20, 20.0, StatusCodes.Good),
                (30, 30.0, StatusCodes.Good),
                (40, 40.0, StatusCodes.BadDataUnavailable),
                (50, 50.0, StatusCodes.Good));

            DataValue result = Compute(ObjectIds.AggregateFunction_DeltaBounds, raw, treatUncertainAsBad);

            AssertValue(result, 15.0, StatusCodes.UncertainDataSubNormal);
        }

        /// <summary>
        /// A Bad raw value before the start bound still gives Bad_NoData (Part 13 §3.1.9).
        /// </summary>
        [Test]
        public void DeltaBoundsWithBadValueBeforeBoundIsBadNoData()
        {
            List<DataValue> raw = Raw(
                (0, 0.0, StatusCodes.Good),
                (10, 10.0, StatusCodes.BadDataUnavailable),
                (20, 20.0, StatusCodes.Good),
                (30, 30.0, StatusCodes.Good),
                (40, 40.0, StatusCodes.Good));

            DataValue result = Compute(ObjectIds.AggregateFunction_DeltaBounds, raw, true);

            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.BadNoData));
        }

        /// <summary>
        /// StartBound keeps the status of an Uncertain bound (Part 13 Table 76) with TreatUncertainAsBad.
        /// </summary>
        [Test]
        public void StartBoundWithUncertainValueBeforeBoundIsUncertain()
        {
            List<DataValue> raw = Raw(
                (0, 0.0, StatusCodes.Good),
                (10, 10.0, StatusCodes.UncertainSubstituteValue),
                (20, 20.0, StatusCodes.Good),
                (30, 30.0, StatusCodes.Good),
                (40, 40.0, StatusCodes.Good));

            DataValue result = Compute(ObjectIds.AggregateFunction_StartBound, raw, true);

            AssertValue(result, 15.0, StatusCodes.UncertainDataSubNormal);
        }

        /// <summary>
        /// A Bad raw value exactly at the bound time gives Bad_NoData without a value (Part 13 §3.1.9, §3.1.7).
        /// </summary>
        [Test]
        public void StartBoundWithBadRawValueAtBoundIsBadNoData()
        {
            List<DataValue> raw = Raw(
                (5, 5.0, StatusCodes.Good),
                (15, 15.0, StatusCodes.BadDataUnavailable),
                (25, 25.0, StatusCodes.Good),
                (35, 35.0, StatusCodes.Good));

            DataValue result = Compute(ObjectIds.AggregateFunction_StartBound, raw, false);

            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.BadNoData));
            Assert.That(result.WrappedValue.IsNull, Is.True);
        }

        /// <summary>
        /// An interval that ends exactly on a Bad raw value is not at the end of the data: the last Good
        /// region [25 s, 35 s) keeps its full duration, so DurationGood is 20 s.
        /// </summary>
        [Test]
        public void DurationGoodWithBadRawValueAtEndBoundKeepsLastRegion()
        {
            List<DataValue> raw = Raw(
                (5, 5.0, StatusCodes.Good),
                (15, 15.0, StatusCodes.Good),
                (25, 25.0, StatusCodes.Good),
                (35, 35.0, StatusCodes.BadDataUnavailable),
                (45, 45.0, StatusCodes.Good));

            DataValue result = Compute(ObjectIds.AggregateFunction_DurationGood, raw, true);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(20_000.0).Within(1e-9));
        }

        /// <summary>
        /// DurationGood counts only Good regions and DurationBad only Bad regions; an Uncertain region counts
        /// for neither, whatever TreatUncertainAsBad is (Part 13 §5.4.3.31/32, Mantis 11425 ~0025847).
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void DurationGoodAndBadIgnoreUncertainRegions(bool treatUncertainAsBad)
        {
            List<DataValue> raw = Raw(
                (5, 5.0, StatusCodes.Good),
                (15, 15.0, StatusCodes.Good),
                (25, 25.0, StatusCodes.UncertainSubstituteValue),
                (35, 35.0, StatusCodes.Good),
                (45, 45.0, StatusCodes.Good));

            DataValue good = Compute(ObjectIds.AggregateFunction_DurationGood, raw, treatUncertainAsBad);
            DataValue bad = Compute(ObjectIds.AggregateFunction_DurationBad, raw, treatUncertainAsBad);
            DataValue percentGood = Compute(ObjectIds.AggregateFunction_PercentGood, raw, treatUncertainAsBad);
            DataValue percentBad = Compute(ObjectIds.AggregateFunction_PercentBad, raw, treatUncertainAsBad);

            Assert.Multiple(() =>
            {
                Assert.That(good.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(10_000.0).Within(1e-9));
                Assert.That(bad.WrappedValue.ConvertToDouble().GetDouble(), Is.Zero);
                Assert.That(percentGood.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(50.0).Within(1e-9));
                Assert.That(percentBad.WrappedValue.ConvertToDouble().GetDouble(), Is.Zero);
            });
        }

        /// <summary>
        /// EndBound interpolates towards an Uncertain raw value after the bound (only a Bad one reverts
        /// to stepped, Part 13 §3.1.9): the end bound at 35 s is 35, not the stepped 30.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void EndBoundWithUncertainValueAfterBoundIsInterpolated(bool treatUncertainAsBad)
        {
            List<DataValue> raw = UncertainAfterEndBound();

            DataValue result = Compute(ObjectIds.AggregateFunction_EndBound, raw, treatUncertainAsBad);

            AssertValue(result, 35.0, StatusCodes.UncertainDataSubNormal);
        }

        /// <summary>
        /// TimeAverage2 and Total2 with an Uncertain raw value after the end bound. The end bound is
        /// interpolated (35, Uncertain_DataSubNormal) whatever TreatUncertainAsBad is, see
        /// <see cref="EndBoundWithUncertainValueAfterBoundIsInterpolated"/>. The values rise linearly
        /// from 15 to 35 over 20 s, so the average is 25 and the total 500. With TreatUncertainAsBad the
        /// Uncertain bound is then handled like any other Uncertain value in the interval (Part 13
        /// Table 56, §4.2.1.2), i.e. as Bad, so the last region keeps its Good start value 30:
        /// 24.375 and 487.5.
        /// </summary>
        [TestCase(Objects.AggregateFunction_TimeAverage2, true, 24.375)]
        [TestCase(Objects.AggregateFunction_TimeAverage2, false, 25.0)]
        [TestCase(Objects.AggregateFunction_Total2, true, 487.5)]
        [TestCase(Objects.AggregateFunction_Total2, false, 500.0)]
        public void TimeAverage2AndTotal2WithUncertainValueAfterEndBound(
            uint aggregateTypeId,
            bool treatUncertainAsBad,
            double expected)
        {
            List<DataValue> raw = UncertainAfterEndBound();

            DataValue result = Compute(new NodeId(aggregateTypeId), raw, treatUncertainAsBad);

            Assert.That(StatusCode.IsBad(result.StatusCode), Is.False, result.StatusCode.ToString());
            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(expected).Within(1e-9));
        }

        /// <summary>
        /// TimeAverage2 omits non-Good data (Part 13 §5.4.3.7); with TreatUncertainAsBad an Uncertain raw
        /// value at the interval start is Bad (§4.2.1.2), so the first region [15 s, 25 s) is omitted like
        /// any later Uncertain region and the average is that of [25 s, 35 s): 30. Without
        /// TreatUncertainAsBad both regions count: 25.
        /// </summary>
        [TestCase(true, 30.0)]
        [TestCase(false, 25.0)]
        public void TimeAverage2OmitsUncertainFirstRegionWithTreatUncertainAsBad(
            bool treatUncertainAsBad,
            double expected)
        {
            List<DataValue> raw = Raw(
                (5, 5.0, StatusCodes.Good),
                (15, 15.0, StatusCodes.UncertainSubstituteValue),
                (25, 25.0, StatusCodes.Good),
                (35, 35.0, StatusCodes.Good),
                (45, 45.0, StatusCodes.Good));

            DataValue result = Compute(ObjectIds.AggregateFunction_TimeAverage2, raw, treatUncertainAsBad);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(expected).Within(1e-9));
        }

        /// <summary>
        /// An interval that holds only data omitted by TimeAverage2/Total2 (an Uncertain start bound with
        /// TreatUncertainAsBad, then Bad raw values) has data, so its status is the PercentTime status
        /// Bad (Part 13 §5.4.3.2.1), not Bad_NoData.
        /// </summary>
        [TestCase(Objects.AggregateFunction_TimeAverage2)]
        [TestCase(Objects.AggregateFunction_Total2)]
        public void TimeAverage2AndTotal2WithOnlyOmittedDataAreBad(uint aggregateTypeId)
        {
            List<DataValue> raw = Raw(
                (0, 0.0, StatusCodes.Good),
                (10, 10.0, StatusCodes.Good),
                (20, 20.0, StatusCodes.BadDataUnavailable),
                (30, 30.0, StatusCodes.BadDataUnavailable),
                (40, 40.0, StatusCodes.BadDataUnavailable),
                (50, 50.0, StatusCodes.Good));

            DataValue result = Compute(new NodeId(aggregateTypeId), raw, true);

            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.Bad.CodeBits), result.StatusCode.ToString());
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Calculated));
        }

        /// <summary>
        /// Interpolated Bounding Values (Part 13 §3.1.8) use the nearest non-Bad raw values: with
        /// TreatUncertainAsBad the Uncertain value 100 at 10 s is still the early bound, so the
        /// Interpolative value at 15 s is 60 (between 100 and 20) with Uncertain_DataSubNormal.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void InterpolativeUsesUncertainNeighbourAsBound(bool treatUncertainAsBad)
        {
            List<DataValue> raw = Raw(
                (0, 0.0, StatusCodes.Good),
                (10, 100.0, StatusCodes.UncertainSubstituteValue),
                (20, 20.0, StatusCodes.Good),
                (30, 30.0, StatusCodes.Good),
                (40, 40.0, StatusCodes.Good));

            DataValue result = Compute(ObjectIds.AggregateFunction_Interpolative, raw, treatUncertainAsBad);

            AssertValue(result, 60.0, StatusCodes.UncertainDataSubNormal);
        }

        /// <summary>
        /// NumberOfTransitions compares the earliest non-Bad value in the interval with the previous non-Bad
        /// value (Part 13 §5.4.3.24, Table 72 "Bound Uncertain: Use as value"), whatever TreatUncertainAsBad
        /// is: the previous value of [30 s, 40 s) is the Uncertain 0 at 22 s, so the Good 1 at 35 s is a
        /// transition.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void NumberOfTransitionsUsesUncertainPreviousValue(bool treatUncertainAsBad)
        {
            List<DataValue> raw = Raw(
                (1, 1.0, StatusCodes.Good),
                (8, 0.0, StatusCodes.UncertainSubstituteValue),
                (15, 0.0, StatusCodes.UncertainSubstituteValue),
                (22, 0.0, StatusCodes.UncertainSubstituteValue),
                (35, 1.0, StatusCodes.Good));

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_NumberOfTransitions,
                s_origin,
                s_origin.AddMilliseconds(40_000),
                10_000,
                true,
                new AggregateConfiguration
                {
                    TreatUncertainAsBad = treatUncertainAsBad,
                    PercentDataBad = 100,
                    PercentDataGood = 100,
                    UseSlopedExtrapolation = false
                },
                NUnitTelemetryContext.Create())!;

            foreach (DataValue value in raw)
            {
                calculator.QueueRawValue(value);
            }

            var results = new List<DataValue>();
            while (calculator.TryGetProcessedValue(true, out DataValue result))
            {
                results.Add(result);
            }

            Assert.That(results, Has.Count.EqualTo(4));
            Assert.That(results[3].WrappedValue.TryGetValue(out int transitions), Is.True);
            Assert.That(transitions, Is.EqualTo(1));
        }

        /// <summary>
        /// A reverse read queues the raw values newest first; the interval [30 s, 40 s) uses the same
        /// previous non-Bad value (the Uncertain 0 at 22 s) as a forward read, so the Good 1 at 35 s is a
        /// transition.
        /// </summary>
        [Test]
        public void NumberOfTransitionsReverseReadUsesUncertainPreviousValue()
        {
            List<DataValue> raw = Raw(
                (1, 1.0, StatusCodes.Good),
                (8, 0.0, StatusCodes.UncertainSubstituteValue),
                (15, 0.0, StatusCodes.UncertainSubstituteValue),
                (22, 0.0, StatusCodes.UncertainSubstituteValue),
                (35, 1.0, StatusCodes.Good));
            raw.Reverse();

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_NumberOfTransitions,
                s_origin.AddMilliseconds(40_000),
                s_origin,
                10_000,
                true,
                new AggregateConfiguration
                {
                    TreatUncertainAsBad = true,
                    PercentDataBad = 100,
                    PercentDataGood = 100,
                    UseSlopedExtrapolation = false
                },
                NUnitTelemetryContext.Create())!;

            var results = new List<DataValue>();
            foreach (DataValue value in raw)
            {
                calculator.QueueRawValue(value);
                while (calculator.TryGetProcessedValue(false, out DataValue processed))
                {
                    results.Add(processed);
                }
            }

            while (calculator.TryGetProcessedValue(true, out DataValue processed))
            {
                results.Add(processed);
            }

            Assert.That(results, Has.Count.EqualTo(4));
            Assert.That(results[0].SourceTimestamp, Is.EqualTo(s_origin.AddMilliseconds(40_000)));
            Assert.That(results[0].WrappedValue.TryGetValue(out int transitions), Is.True);
            Assert.That(transitions, Is.EqualTo(1));
        }

        private static List<DataValue> UncertainAfterEndBound()
        {
            return Raw(
                (0, 0.0, StatusCodes.Good),
                (10, 10.0, StatusCodes.Good),
                (20, 20.0, StatusCodes.Good),
                (30, 30.0, StatusCodes.Good),
                (40, 40.0, StatusCodes.UncertainSubstituteValue),
                (50, 50.0, StatusCodes.Good));
        }

        private static List<DataValue> Raw(params (int Seconds, double Value, StatusCode Status)[] samples)
        {
            var values = new List<DataValue>();
            foreach ((int seconds, double value, StatusCode status) in samples)
            {
                DateTimeUtc timestamp = s_origin.AddMilliseconds(seconds * 1000);
                values.Add(new DataValue(new Variant(value), status, timestamp, timestamp));
            }
            return values;
        }

        /// <summary>
        /// Calculates one interval [15 s, 35 s) with sloped interpolation.
        /// </summary>
        private static DataValue Compute(NodeId aggregateId, List<DataValue> raw, bool treatUncertainAsBad)
        {
            DateTimeUtc start = s_origin.AddMilliseconds(15_000);
            DateTimeUtc end = s_origin.AddMilliseconds(35_000);
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                aggregateId,
                start,
                end,
                20_000,
                false,
                new AggregateConfiguration
                {
                    TreatUncertainAsBad = treatUncertainAsBad,
                    PercentDataBad = 100,
                    PercentDataGood = 100,
                    UseSlopedExtrapolation = false
                },
                NUnitTelemetryContext.Create())!;

            foreach (DataValue value in raw)
            {
                calculator.QueueRawValue(value);
            }

            Assert.That(calculator.TryGetProcessedValue(true, out DataValue result), Is.True);
            return result;
        }

        private static void AssertValue(DataValue result, double expected, StatusCode expectedCode)
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    result.StatusCode.CodeBits,
                    Is.EqualTo(expectedCode.CodeBits),
                    result.StatusCode.ToString());
                Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(expected).Within(1e-9));
            });
        }
    }
}
