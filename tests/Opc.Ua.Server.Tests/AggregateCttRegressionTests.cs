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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Part 11 and Part 13 oracle tests for aggregate scenarios exercised by the CTT aggregate units.
    /// </summary>
    [TestFixture]
    [Category("Aggregators")]
    [Category("Historian")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class AggregateCttRegressionTests
    {
        /// <summary>
        /// Verifies that direct and live results over ten intervals match the Part 13 aggregate oracle.
        /// </summary>
        [TestCase("Minimum", false)]
        [TestCase("Maximum", false)]
        [TestCase("Range", false)]
        [TestCase("TimeAverage", false)]
        [TestCase("Minimum", true)]
        [TestCase("Maximum", true)]
        [TestCase("Range", true)]
        [TestCase("TimeAverage", true)]
        public async Task DirectAndLiveTenIntervalResultsMatchPart13OracleAsync(
            string aggregateName,
            bool reverse)
        {
            NodeId aggregateId = GetAggregateId(aggregateName);
            List<DataValue> rawValues = CreateSharedRawValues();
            DateTimeUtc startTime = reverse ? AtSeconds(100) : s_baseTime;
            DateTimeUtc endTime = reverse ? s_baseTime : AtSeconds(100);
            AggregateConfiguration configuration = CreateConfiguration();

            List<DataValue> direct = RunDirect(
                aggregateId,
                rawValues,
                startTime,
                endTime,
                10_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                aggregateId,
                rawValues,
                startTime,
                endTime,
                10_000,
                configuration).ConfigureAwait(false);

            AssertSharedTenIntervalResults(direct, aggregateName, reverse);
            AssertSharedTenIntervalResults(live, aggregateName, reverse);
        }

        /// <summary>
        /// Verifies that an aggregate interval equal to the requested range returns one value directly and live.
        /// </summary>
        [TestCase("Minimum", 0.0)]
        [TestCase("Maximum", 19.0)]
        [TestCase("Range", 19.0)]
        [TestCase("TimeAverage", 9.75)]
        public async Task DirectAndLiveIntervalEqualToRangeReturnsOneValueAsync(
            string aggregateName,
            double expected)
        {
            NodeId aggregateId = GetAggregateId(aggregateName);
            List<DataValue> rawValues = CreateSharedRawValues();
            AggregateConfiguration configuration = CreateConfiguration();
            DateTimeUtc endTime = AtSeconds(100);

            List<DataValue> direct = RunDirect(
                aggregateId,
                rawValues,
                s_baseTime,
                endTime,
                100_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                aggregateId,
                rawValues,
                s_baseTime,
                endTime,
                100_000,
                configuration).ConfigureAwait(false);

            AssertSingleNumericResult(direct, expected, s_baseTime, AggregateBits.Calculated);
            AssertSingleNumericResult(live, expected, s_baseTime, AggregateBits.Calculated);
        }

        /// <summary>
        /// Verifies that direct and live start/end aggregate families match the Part 13 oracle.
        /// </summary>
        [TestCase("Start", false, 0.0, 0, AggregateBits.Raw)]
        [TestCase("End", false, 5.0, 5, AggregateBits.Raw)]
        [TestCase("StartBound", false, 0.0, 0, AggregateBits.Raw)]
        [TestCase("EndBound", false, 10.0, 0, AggregateBits.Calculated)]
        [TestCase("Start", true, 5.0, 5, AggregateBits.Raw)]
        [TestCase("End", true, 10.0, 10, AggregateBits.Raw)]
        [TestCase("StartBound", true, 10.0, 10, AggregateBits.Raw)]
        [TestCase("EndBound", true, 0.0, 10, AggregateBits.Calculated)]
        public async Task DirectAndLiveStartEndFamiliesMatchPart13OracleAsync(
            string aggregateName,
            bool reverse,
            double expected,
            int expectedTimestampSeconds,
            AggregateBits expectedBits)
        {
            NodeId aggregateId = GetAggregateId(aggregateName);
            List<DataValue> rawValues =
            [
                CreateValue(0, StatusCodes.Good, 0),
                CreateValue(5, StatusCodes.Good, 5),
                CreateValue(10, StatusCodes.Good, 10)
            ];
            DateTimeUtc startTime = reverse ? AtSeconds(10) : s_baseTime;
            DateTimeUtc endTime = reverse ? s_baseTime : AtSeconds(10);
            AggregateConfiguration configuration = CreateConfiguration();

            List<DataValue> direct = RunDirect(
                aggregateId,
                rawValues,
                startTime,
                endTime,
                10_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                aggregateId,
                rawValues,
                startTime,
                endTime,
                10_000,
                configuration).ConfigureAwait(false);

            DateTimeUtc expectedTimestamp = AtSeconds(expectedTimestampSeconds);
            AssertSingleNumericResult(direct, expected, expectedTimestamp, expectedBits);
            AssertSingleNumericResult(live, expected, expectedTimestamp, expectedBits);
        }

        /// <summary>
        /// Verifies that direct and live percentage aggregates honor the explicit uncertain-value configuration.
        /// </summary>
        [TestCase("PercentGood", false, 50.0)]
        [TestCase("PercentBad", false, 50.0)]
        [TestCase("PercentGood", true, 25.0)]
        [TestCase("PercentBad", true, 75.0)]
        public async Task DirectAndLivePercentAggregatesHonorExplicitUncertainConfigurationAsync(
            string aggregateName,
            bool treatUncertainAsBad,
            double expected)
        {
            NodeId aggregateId = GetAggregateId(aggregateName);
            List<DataValue> rawValues =
            [
                CreateValue(1, StatusCodes.Good, 0),
                CreateValue(1, StatusCodes.Uncertain, 5),
                CreateValue(1, StatusCodes.Bad, 10),
                CreateValue(1, StatusCodes.Bad, 15),
                CreateValue(1, StatusCodes.Good, 20)
            ];
            AggregateConfiguration configuration = CreateConfiguration(treatUncertainAsBad);
            DateTimeUtc endTime = AtSeconds(20);

            List<DataValue> direct = RunDirect(
                aggregateId,
                rawValues,
                s_baseTime,
                endTime,
                20_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                aggregateId,
                rawValues,
                s_baseTime,
                endTime,
                20_000,
                configuration).ConfigureAwait(false);

            AssertSingleNumericResult(direct, expected, s_baseTime, AggregateBits.Calculated);
            AssertSingleNumericResult(live, expected, s_baseTime, AggregateBits.Calculated);
        }

        /// <summary>
        /// Verifies that the value-based status counts Uncertain values as Good when
        /// TreatUncertainAsBad is false and as Bad otherwise (Part 13 §4.2.1.2, §5.4.3.2.1).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DirectAndLiveCountValueBasedStatusHonorsTreatUncertainAsBadAsync(
            bool treatUncertainAsBad)
        {
            // Two Good values and one Uncertain value: 100% Good without TreatUncertainAsBad,
            // otherwise 67% Good and 33% Bad, which meets neither threshold.
            StatusCode expectedCodeBits = treatUncertainAsBad
                ? StatusCodes.UncertainDataSubNormal
                : StatusCodes.Good;
            List<DataValue> rawValues =
            [
                CreateValue(1, StatusCodes.Good, 0),
                CreateValue(2, StatusCodes.UncertainSubstituteValue, 5),
                CreateValue(3, StatusCodes.Good, 10),
                CreateValue(4, StatusCodes.Good, 20)
            ];
            AggregateConfiguration configuration = CreateConfiguration(treatUncertainAsBad);
            DateTimeUtc endTime = AtSeconds(15);

            List<DataValue> direct = RunDirect(
                ObjectIds.AggregateFunction_Count,
                rawValues,
                s_baseTime,
                endTime,
                15_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                ObjectIds.AggregateFunction_Count,
                rawValues,
                s_baseTime,
                endTime,
                15_000,
                configuration).ConfigureAwait(false);

            foreach (List<DataValue> results in new[] { direct, live })
            {
                Assert.That(results, Has.Count.EqualTo(1));
                Assert.That(results[0].WrappedValue.TryGetValue(out int count), Is.True);
                Assert.That(count, Is.EqualTo(2));
                Assert.That(results[0].StatusCode.CodeBits, Is.EqualTo(expectedCodeBits));
                Assert.That(results[0].StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Calculated));
            }
        }

        /// <summary>
        /// Verifies that repeated Good quality sets the multiple-values flag for worst-quality aggregates.
        /// </summary>
        [TestCase("WorstQuality")]
        [TestCase("WorstQuality2")]
        public async Task DirectAndLiveWorstQualitySetMultipleValuesForRepeatedGoodQualityAsync(
            string aggregateName)
        {
            NodeId aggregateId = GetAggregateId(aggregateName);
            List<DataValue> rawValues =
            [
                CreateValue(1, StatusCodes.Good, 0),
                CreateValue(2, StatusCodes.Good, 5),
                CreateValue(3, StatusCodes.Good, 10)
            ];
            AggregateConfiguration configuration = CreateConfiguration();
            DateTimeUtc endTime = AtSeconds(10);

            List<DataValue> direct = RunDirect(
                aggregateId,
                rawValues,
                s_baseTime,
                endTime,
                10_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                aggregateId,
                rawValues,
                s_baseTime,
                endTime,
                10_000,
                configuration).ConfigureAwait(false);

            AssertWorstQualityResult(direct);
            AssertWorstQualityResult(live);
        }

        /// <summary>
        /// Verifies that direct and live duration-in-state aggregates match the Part 13 oracle.
        /// </summary>
        [TestCase("DurationInStateZero", false)]
        [TestCase("DurationInStateNonZero", false)]
        [TestCase("DurationInStateZero", true)]
        [TestCase("DurationInStateNonZero", true)]
        public async Task DirectAndLiveDurationInStateMatchPart13OracleAsync(
            string aggregateName,
            bool reverse)
        {
            NodeId aggregateId = GetAggregateId(aggregateName);
            List<DataValue> rawValues =
            [
                CreateValue(0, StatusCodes.Good, 0),
                CreateValue(1, StatusCodes.Good, 5),
                CreateValue(0, StatusCodes.Good, 10),
                CreateValue(2, StatusCodes.Good, 15),
                CreateValue(0, StatusCodes.Good, 20)
            ];
            DateTimeUtc startTime = reverse ? AtSeconds(20) : s_baseTime;
            DateTimeUtc endTime = reverse ? s_baseTime : AtSeconds(20);
            AggregateConfiguration configuration = CreateConfiguration();

            List<DataValue> direct = RunDirect(
                aggregateId,
                rawValues,
                startTime,
                endTime,
                20_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                aggregateId,
                rawValues,
                startTime,
                endTime,
                20_000,
                configuration).ConfigureAwait(false);

            DateTimeUtc expectedTimestamp = reverse ? AtSeconds(20) : s_baseTime;
            AssertSingleNumericResult(direct, 10_000, expectedTimestamp, AggregateBits.Calculated);
            AssertSingleNumericResult(live, 10_000, expectedTimestamp, AggregateBits.Calculated);
        }

        /// <summary>
        /// Verifies that the status of the duration-in-state aggregates takes the region ending at an
        /// Uncertain simple end bound into account when sloped interpolation is used
        /// (Part 13 §5.4.3.2.2), while the duration itself still includes that region because it
        /// starts at a Good raw value (Part 13 §5.4.3.22-.23).
        /// </summary>
        [TestCase("DurationInStateZero", true, 15_000.0)]
        [TestCase("DurationInStateNonZero", true, 5_000.0)]
        [TestCase("DurationInStateZero", false, 15_000.0)]
        [TestCase("DurationInStateNonZero", false, 5_000.0)]
        public async Task DirectAndLiveDurationInStateUncertainEndBoundMakesLastRegionUncertainAsync(
            string aggregateName,
            bool treatUncertainAsBad,
            double expected)
        {
            // Interval [5 s, 25 s): the raw data in the interval is Good, but the simple end bound
            // at 25 s is Uncertain_DataSubNormal because the raw value after it (30 s) is Bad.
            // The region 20-25 s therefore ends in an Uncertain value. With TreatUncertainAsBad it
            // counts as Bad (25%, neither threshold is met); otherwise it counts as Good.
            List<DataValue> rawValues =
            [
                CreateValue(0, StatusCodes.Good, 0),
                CreateValue(0, StatusCodes.Good, 10),
                CreateValue(1, StatusCodes.Good, 20),
                CreateValue(1, StatusCodes.BadDataUnavailable, 30),
                CreateValue(1, StatusCodes.Good, 40)
            ];
            NodeId aggregateId = GetAggregateId(aggregateName);
            AggregateConfiguration configuration = CreateConfiguration(treatUncertainAsBad);
            DateTimeUtc startTime = AtSeconds(5);
            DateTimeUtc endTime = AtSeconds(25);
            StatusCode expectedCodeBits = treatUncertainAsBad
                ? StatusCodes.UncertainDataSubNormal
                : StatusCodes.Good;

            List<DataValue> direct = RunDirect(
                aggregateId,
                rawValues,
                startTime,
                endTime,
                20_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                aggregateId,
                rawValues,
                startTime,
                endTime,
                20_000,
                configuration).ConfigureAwait(false);

            foreach (List<DataValue> results in new[] { direct, live })
            {
                Assert.That(results, Has.Count.EqualTo(1));
                Assert.That(
                    results[0].WrappedValue.ConvertToDouble().GetDouble(),
                    Is.EqualTo(expected).Within(0.000_001));
                Assert.That(results[0].StatusCode.CodeBits, Is.EqualTo(expectedCodeBits));
                Assert.That(results[0].StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Calculated));
            }
        }

        /// <summary>
        /// Verifies that direct and live transition counts follow Part 13 boundary rules.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DirectAndLiveNumberOfTransitionsMatchesPart13BoundaryRulesAsync(bool reverse)
        {
            List<DataValue> rawValues =
            [
                CreateValue(5, StatusCodes.Good, 0),
                CreateValue(5, StatusCodes.Good, 1),
                CreateValue(6, StatusCodes.Good, 2),
                CreateValue(7, StatusCodes.Good, 3)
            ];
            DateTimeUtc startTime = reverse ? AtSeconds(3) : s_baseTime;
            DateTimeUtc endTime = reverse ? s_baseTime : AtSeconds(3);
            AggregateConfiguration configuration = CreateConfiguration();

            List<DataValue> direct = RunDirect(
                ObjectIds.AggregateFunction_NumberOfTransitions,
                rawValues,
                startTime,
                endTime,
                3_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                ObjectIds.AggregateFunction_NumberOfTransitions,
                rawValues,
                startTime,
                endTime,
                3_000,
                configuration).ConfigureAwait(false);

            AssertSingleNumericResult(direct, 2, startTime, AggregateBits.Calculated);
            AssertSingleNumericResult(live, 2, startTime, AggregateBits.Calculated);
        }

        /// <summary>
        /// Verifies that direct and live transition counts include uncertain values only when
        /// TreatUncertainAsBad is false. With TreatUncertainAsBad the Uncertain values are equivalent
        /// to Bad (Part 13 §4.2.1.2) and Bad values are not counted (Part 13 §5.4.3.24).
        /// </summary>
        [TestCase(false, 22)]
        [TestCase(true, 20)]
        public async Task DirectAndLiveNumberOfTransitionsCountUncertainValuesOnlyWhenNotTreatedAsBadAsync(
            bool treatUncertainAsBad,
            int expectedTransitions)
        {
            var rawValues = new List<DataValue>(25);
            for (int index = 0; index <= 24; index++)
            {
                StatusCode status = (index % 10) switch
                {
                    7 => StatusCodes.BadDataUnavailable,
                    9 => StatusCodes.UncertainSubstituteValue,
                    _ => StatusCodes.Good
                };
                rawValues.Add(CreateValue(index, status, index));
            }
            DateTimeUtc endTime = AtSeconds(24);
            AggregateConfiguration configuration = CreateConfiguration(treatUncertainAsBad);

            List<DataValue> direct = RunDirect(
                ObjectIds.AggregateFunction_NumberOfTransitions,
                rawValues,
                s_baseTime,
                endTime,
                24_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                ObjectIds.AggregateFunction_NumberOfTransitions,
                rawValues,
                s_baseTime,
                endTime,
                24_000,
                configuration).ConfigureAwait(false);

            AssertNumberOfTransitionsWithMixedQuality(direct, expectedTransitions);
            AssertNumberOfTransitionsWithMixedQuality(live, expectedTransitions);
        }

        /// <summary>
        /// Verifies that a live processed-history read with equal times returns BadInvalidArgument.
        /// </summary>
        [Test]
        public async Task LiveProcessedReadWithEqualTimesReturnsBadInvalidArgumentAsync()
        {
            using var harness = new AggregateHarness();
            var result = new HistoryReadResult();

            ServiceResult error = await harness.DispatchProcessedAsync(
                ObjectIds.AggregateFunction_Average,
                [],
                s_baseTime,
                s_baseTime,
                1,
                CreateConfiguration(),
                result).ConfigureAwait(false);

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        /// <summary>
        /// Verifies that the interval overlapping the start or end of data carries the Partial
        /// bit in both time directions (Part 13 §5.3.3.2).
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task DirectAndLivePartialBitMarksIntervalOverlappingDataEdgeAsync(
            bool reverse,
            bool dataEndsInsideRange)
        {
            // Data starts at 5 s. It either ends inside the requested range (15 s) or at its
            // end (20 s), so the edge that is overlapped depends on the case.
            List<DataValue> rawValues =
            [
                CreateValue(1, StatusCodes.Good, 5),
                CreateValue(2, StatusCodes.Good, 10),
                CreateValue(3, StatusCodes.Good, dataEndsInsideRange ? 15 : 20)
            ];
            DateTimeUtc startTime = reverse ? AtSeconds(20) : s_baseTime;
            DateTimeUtc endTime = reverse ? s_baseTime : AtSeconds(20);
            AggregateConfiguration configuration = CreateConfiguration();

            List<DataValue> direct = RunDirect(
                ObjectIds.AggregateFunction_Count,
                rawValues,
                startTime,
                endTime,
                10_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                ObjectIds.AggregateFunction_Count,
                rawValues,
                startTime,
                endTime,
                10_000,
                configuration).ConfigureAwait(false);

            // The chronologically early interval overlaps the start of data; the late interval
            // overlaps the end of data only when the data ends inside the range. Backward reads
            // return the late interval first.
            bool[] expectedPartialChronological = [true, dataEndsInsideRange];
            AssertPartialBits(direct, expectedPartialChronological, reverse);
            AssertPartialBits(live, expectedPartialChronological, reverse);
        }

        /// <summary>
        /// Verifies that time-based status calculation treats Uncertain regions as Bad when
        /// TreatUncertainAsBad is true (Part 13 §5.4.3.2.1).
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task DirectAndLiveMinimum2TreatsUncertainRegionsAsBadWhenConfiguredAsync(
            bool treatUncertainAsBad)
        {
            StatusCode expectedCodeBits = treatUncertainAsBad
                ? StatusCodes.Bad
                : StatusCodes.UncertainDataSubNormal;

            // Interval [15 s, 35 s): the start bound is Bad (the raw value at 10 s is Bad), the
            // region 20-30 s ends on an Uncertain value and the region 30-35 s is Uncertain.
            // With TreatUncertainAsBad every region is Bad (100% >= PercentDataBad); otherwise
            // 25% is Bad and 75% Good, which meets neither threshold.
            List<DataValue> rawValues =
            [
                CreateValue(0, StatusCodes.Good, 0),
                CreateValue(1, StatusCodes.BadDataUnavailable, 10),
                CreateValue(2, StatusCodes.Good, 20),
                CreateValue(3, StatusCodes.UncertainSubstituteValue, 30),
                CreateValue(4, StatusCodes.Good, 40),
                CreateValue(5, StatusCodes.Good, 50)
            ];
            AggregateConfiguration configuration = CreateConfiguration(treatUncertainAsBad);
            DateTimeUtc startTime = AtSeconds(15);
            DateTimeUtc endTime = AtSeconds(35);

            List<DataValue> direct = RunDirect(
                ObjectIds.AggregateFunction_Minimum2,
                rawValues,
                startTime,
                endTime,
                20_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                ObjectIds.AggregateFunction_Minimum2,
                rawValues,
                startTime,
                endTime,
                20_000,
                configuration).ConfigureAwait(false);

            foreach (List<DataValue> results in new[] { direct, live })
            {
                Assert.That(results, Has.Count.EqualTo(1));
                Assert.That(results[0].StatusCode.CodeBits, Is.EqualTo(expectedCodeBits));
                Assert.That(
                    results[0].StatusCode.AggregateBits & AggregateBits.Calculated,
                    Is.EqualTo(AggregateBits.Calculated));
            }
        }

        /// <summary>
        /// Verifies that the first DurationGood/DurationBad region takes the status of the raw
        /// value at or before the interval start rather than of the simple bound
        /// (Part 13 §5.4.3.31-.32).
        /// </summary>
        [TestCase("DurationBad", true, 15_000.0)]
        [TestCase("DurationGood", true, 15_000.0)]
        [TestCase("DurationBad", false, 10_000.0)]
        [TestCase("DurationGood", false, 20_000.0)]
        [TestCase("PercentBad", true, 50.0)]
        [TestCase("PercentGood", true, 50.0)]
        [TestCase("PercentBad", false, 100.0 / 3.0)]
        [TestCase("PercentGood", false, 200.0 / 3.0)]
        public async Task DirectAndLiveDurationFirstRegionUsesRawStatusBeforeIntervalAsync(
            string aggregateName,
            bool treatUncertainAsBad,
            double expected)
        {
            // Interval [5 s, 35 s): the simple start bound is Uncertain because Bad data follows
            // the Good value at 0 s, but the first region (5-10 s) is Good. 10-20 s is Bad,
            // 20-30 s Good, and 30-35 s Uncertain (Bad only with TreatUncertainAsBad).
            List<DataValue> rawValues =
            [
                CreateValue(0, StatusCodes.Good, 0),
                CreateValue(1, StatusCodes.BadDataUnavailable, 10),
                CreateValue(2, StatusCodes.Good, 20),
                CreateValue(3, StatusCodes.UncertainSubstituteValue, 30),
                CreateValue(4, StatusCodes.Good, 40)
            ];
            NodeId aggregateId = GetAggregateId(aggregateName);
            AggregateConfiguration configuration = CreateConfiguration(treatUncertainAsBad);
            DateTimeUtc startTime = AtSeconds(5);
            DateTimeUtc endTime = AtSeconds(35);

            List<DataValue> direct = RunDirect(
                aggregateId,
                rawValues,
                startTime,
                endTime,
                30_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                aggregateId,
                rawValues,
                startTime,
                endTime,
                30_000,
                configuration).ConfigureAwait(false);

            AssertSingleNumericResult(direct, expected, startTime, AggregateBits.Calculated);
            AssertSingleNumericResult(live, expected, startTime, AggregateBits.Calculated);
        }

        /// <summary>
        /// Verifies that a backward WorstQuality2 read returns the forward results for the same
        /// intervals, with the start bound taken at the early time (Part 13 §5.4.2.2, §5.4.3.36).
        /// </summary>
        [Test]
        public async Task DirectAndLiveWorstQuality2BackwardMatchesForwardIntervalsAsync()
        {
            // No raw value sits on an interval boundary, so the forward [5,15) and [15,25) and
            // backward (5,15] and (15,25] intervals contain the same raw values.
            List<DataValue> rawValues =
            [
                CreateValue(0, StatusCodes.Good, 1),
                CreateValue(1, StatusCodes.UncertainSubstituteValue, 9),
                CreateValue(2, StatusCodes.Good, 12),
                CreateValue(3, StatusCodes.BadDataUnavailable, 14),
                CreateValue(4, StatusCodes.Good, 18),
                CreateValue(5, StatusCodes.Good, 21),
                CreateValue(6, StatusCodes.Good, 30)
            ];
            AggregateConfiguration configuration = CreateConfiguration();
            using var harness = new AggregateHarness();

            List<DataValue> forwardDirect = RunDirect(
                ObjectIds.AggregateFunction_WorstQuality2,
                rawValues,
                AtSeconds(5),
                AtSeconds(25),
                10_000,
                configuration);
            List<DataValue> backwardDirect = RunDirect(
                ObjectIds.AggregateFunction_WorstQuality2,
                rawValues,
                AtSeconds(25),
                AtSeconds(5),
                10_000,
                configuration);
            List<DataValue> backwardLive = await harness.ReadProcessedAsync(
                ObjectIds.AggregateFunction_WorstQuality2,
                rawValues,
                AtSeconds(25),
                AtSeconds(5),
                10_000,
                configuration).ConfigureAwait(false);

            // Forward: [5,15) has the Uncertain start bound, Uncertain, Good and Bad values, so the
            // worst is the single BadDataUnavailable; [15,25) starts on a Bad_NoData bound.
            StatusCode[] expectedChronological = [StatusCodes.BadDataUnavailable, StatusCodes.BadNoData];
            AssertWorstQualities(forwardDirect, expectedChronological, reverse: false);
            AssertWorstQualities(backwardDirect, expectedChronological, reverse: true);
            AssertWorstQualities(backwardLive, expectedChronological, reverse: true);
        }

        /// <summary>
        /// Verifies that a raw value rejected because it arrives out of order does not move the
        /// tracked start of data, so the interval overlapping the real start keeps its Partial bit.
        /// </summary>
        [Test]
        public void RejectedOutOfOrderValueDoesNotMoveStartOfData()
        {
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Count,
                s_baseTime,
                AtSeconds(20),
                10_000,
                false,
                CreateConfiguration(),
                NUnitTelemetryContext.Create())!;

            Assert.That(calculator.QueueRawValue(CreateValue(1, StatusCodes.Good, 5)), Is.True);
            Assert.That(calculator.QueueRawValue(CreateValue(2, StatusCodes.Good, 10)), Is.True);
            Assert.That(calculator.QueueRawValue(CreateValue(0, StatusCodes.Good, 0)), Is.False,
                "an earlier value after later ones must be rejected");
            Assert.That(calculator.QueueRawValue(CreateValue(3, StatusCodes.Good, 15)), Is.True);
            Assert.That(calculator.QueueRawValue(CreateValue(4, StatusCodes.Good, 20)), Is.True);

            var results = new List<DataValue>();
            while (calculator.TryGetProcessedValue(true, out DataValue value))
            {
                results.Add(value);
            }

            AssertPartialBits(results, [true, false], reverse: false);
        }

        /// <summary>
        /// Verifies that a backward WorstQuality read reports the chronologically first of two
        /// equally severe statuses, as the forward calculation over the same interval does
        /// (Part 13 §5.4.2.2).
        /// </summary>
        [Test]
        public async Task DirectAndLiveWorstQualityBackwardSelectsChronologicallyFirstStatusAsync()
        {
            List<DataValue> rawValues =
            [
                CreateValue(0, StatusCodes.Good, 0),
                CreateValue(1, StatusCodes.BadOutOfRange, 2),
                CreateValue(2, StatusCodes.BadSensorFailure, 8),
                CreateValue(3, StatusCodes.Good, 12)
            ];
            AggregateConfiguration configuration = CreateConfiguration();

            List<DataValue> direct = RunDirect(
                ObjectIds.AggregateFunction_WorstQuality,
                rawValues,
                AtSeconds(10),
                s_baseTime,
                10_000,
                configuration);

            using var harness = new AggregateHarness();
            List<DataValue> live = await harness.ReadProcessedAsync(
                ObjectIds.AggregateFunction_WorstQuality,
                rawValues,
                AtSeconds(10),
                s_baseTime,
                10_000,
                configuration).ConfigureAwait(false);

            foreach (List<DataValue> results in new[] { direct, live })
            {
                Assert.That(results, Has.Count.EqualTo(1));
                Assert.That(results[0].WrappedValue.TryGetValue(out StatusCode worst), Is.True);
                Assert.That(worst, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(
                    results[0].StatusCode.AggregateBits,
                    Is.EqualTo(AggregateBits.Calculated | AggregateBits.MultipleValues));
            }
        }

        private static void AssertPartialBits(
            List<DataValue> results,
            bool[] expectedPartialChronological,
            bool reverse)
        {
            Assert.That(results, Has.Count.EqualTo(expectedPartialChronological.Length));

            for (int index = 0; index < results.Count; index++)
            {
                int interval = reverse ? results.Count - 1 - index : index;
                bool isPartial = (results[index].StatusCode.AggregateBits & AggregateBits.Partial) != 0;
                Assert.That(
                    isPartial,
                    Is.EqualTo(expectedPartialChronological[interval]),
                    $"Partial bit at result {index} (chronological interval {interval})");
            }
        }

        private static void AssertWorstQualities(
            List<DataValue> results,
            StatusCode[] expectedChronological,
            bool reverse)
        {
            Assert.That(results, Has.Count.EqualTo(expectedChronological.Length));

            for (int index = 0; index < results.Count; index++)
            {
                int interval = reverse ? results.Count - 1 - index : index;
                Assert.That(results[index].WrappedValue.TryGetValue(out StatusCode worst), Is.True);
                Assert.That(
                    worst,
                    Is.EqualTo(expectedChronological[interval]),
                    $"worst quality at result {index} (chronological interval {interval})");
                Assert.That(
                    results[index].StatusCode.AggregateBits,
                    Is.EqualTo(AggregateBits.Calculated),
                    $"aggregate bits at result {index}");
            }
        }

        private static void AssertSharedTenIntervalResults(
            List<DataValue> results,
            string aggregateName,
            bool reverse)
        {
            Assert.That(results, Has.Count.EqualTo(10));

            for (int index = 0; index < results.Count; index++)
            {
                int interval = reverse ? 9 - index : index;
                double expected = aggregateName switch
                {
                    "Minimum" => interval,
                    "Maximum" => reverse ? interval + 11 : interval + 10,
                    "Range" => reverse ? 11 : 10,
                    "TimeAverage" => interval + 5.25,
                    _ => throw new ArgumentOutOfRangeException(nameof(aggregateName))
                };
                DateTimeUtc timestamp = reverse
                    ? AtSeconds(100 - (index * 10))
                    : AtSeconds(index * 10);
                // Part 13 §5.4.3.11: Maximum reports Good, Raw when the maximum sample sits on
                // the interval start. In this dataset the maximum is always at the interval
                // start for both forward (earlier bound) and reverse (later bound) reads, so it
                // is Raw in both directions; every other aggregate here is Calculated.
                AggregateBits expectedBits =
                    aggregateName == "Maximum"
                        ? AggregateBits.Raw
                        : AggregateBits.Calculated;

                AssertNumericResult(results[index], expected, timestamp, expectedBits, index);
            }
        }

        private static void AssertSingleNumericResult(
            List<DataValue> results,
            double expected,
            DateTimeUtc expectedTimestamp,
            AggregateBits expectedBits)
        {
            Assert.That(results, Has.Count.EqualTo(1));
            AssertNumericResult(results[0], expected, expectedTimestamp, expectedBits, 0);
        }

        private static void AssertNumericResult(
            DataValue result,
            double expected,
            DateTimeUtc expectedTimestamp,
            AggregateBits expectedBits,
            int index)
        {
            Assert.That(
                result.WrappedValue.ConvertToDouble().GetDouble(),
                Is.EqualTo(expected).Within(0.000_001),
                $"value at index {index}");
            Assert.That(result.SourceTimestamp, Is.EqualTo(expectedTimestamp), $"timestamp at index {index}");
            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.Good), $"status at index {index}");
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(expectedBits), $"aggregate bits at index {index}");
        }

        private static void AssertWorstQualityResult(List<DataValue> results)
        {
            Assert.That(results, Has.Count.EqualTo(1));
            DataValue result = results[0];
            Assert.That(result.WrappedValue.TryGetValue(out StatusCode worstQuality), Is.True);
            Assert.That(worstQuality, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.SourceTimestamp, Is.EqualTo(s_baseTime));
            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.Good));
            Assert.That(
                result.StatusCode.AggregateBits,
                Is.EqualTo(AggregateBits.Calculated | AggregateBits.MultipleValues));
        }

        private static void AssertNumberOfTransitionsWithMixedQuality(
            List<DataValue> results,
            int expectedTransitions)
        {
            Assert.That(results, Has.Count.EqualTo(1));
            DataValue result = results[0];
            Assert.That(result.WrappedValue.TryGetValue(out int transitions), Is.True);
            Assert.That(transitions, Is.EqualTo(expectedTransitions));
            Assert.That(result.SourceTimestamp, Is.EqualTo(s_baseTime));
            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Calculated));
        }

        private static List<DataValue> RunDirect(
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

            if (endTime < startTime)
            {
                for (int index = rawValues.Count - 1; index >= 0; index--)
                {
                    Assert.That(calculator.QueueRawValue(rawValues[index]), Is.True, $"raw value {index}");
                }
            }
            else
            {
                for (int index = 0; index < rawValues.Count; index++)
                {
                    Assert.That(calculator.QueueRawValue(rawValues[index]), Is.True, $"raw value {index}");
                }
            }

            var results = new List<DataValue>();
            while (calculator.TryGetProcessedValue(true, out DataValue value))
            {
                results.Add(value);
            }
            return results;
        }

        private static List<DataValue> CreateSharedRawValues()
        {
            var values = new List<DataValue>(21);
            for (int interval = 0; interval < 10; interval++)
            {
                values.Add(CreateValue(interval + 10, StatusCodes.Good, interval * 10));
                values.Add(CreateValue(interval, StatusCodes.Good, (interval * 10) + 5));
            }
            values.Add(CreateValue(20, StatusCodes.Good, 100));
            return values;
        }

        private static DataValue CreateValue(double value, StatusCode statusCode, int timestampSeconds)
        {
            DateTimeUtc timestamp = AtSeconds(timestampSeconds);
            return new DataValue(Variant.From(value), statusCode, timestamp, timestamp);
        }

        private static DateTimeUtc AtSeconds(int seconds)
        {
            return s_baseTime.AddMilliseconds(seconds * 1000);
        }

        private static AggregateConfiguration CreateConfiguration(bool treatUncertainAsBad = false)
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
                "Minimum" => ObjectIds.AggregateFunction_Minimum,
                "Maximum" => ObjectIds.AggregateFunction_Maximum,
                "Range" => ObjectIds.AggregateFunction_Range,
                "TimeAverage" => ObjectIds.AggregateFunction_TimeAverage,
                "Start" => ObjectIds.AggregateFunction_Start,
                "End" => ObjectIds.AggregateFunction_End,
                "StartBound" => ObjectIds.AggregateFunction_StartBound,
                "EndBound" => ObjectIds.AggregateFunction_EndBound,
                "PercentGood" => ObjectIds.AggregateFunction_PercentGood,
                "PercentBad" => ObjectIds.AggregateFunction_PercentBad,
                "WorstQuality" => ObjectIds.AggregateFunction_WorstQuality,
                "WorstQuality2" => ObjectIds.AggregateFunction_WorstQuality2,
                "DurationInStateZero" => ObjectIds.AggregateFunction_DurationInStateZero,
                "DurationInStateNonZero" => ObjectIds.AggregateFunction_DurationInStateNonZero,
                "DurationGood" => ObjectIds.AggregateFunction_DurationGood,
                "DurationBad" => ObjectIds.AggregateFunction_DurationBad,
                _ => throw new ArgumentOutOfRangeException(nameof(aggregateName))
            };
        }

        private sealed class AggregateHarness : IDisposable
        {
            public AggregateHarness()
            {
                Provider = new InMemoryHistorianProvider(
                    new InMemoryHistorianOptions(),
                    new FakeTimeProvider(s_baseTime.ToDateTime()));
                Telemetry = NUnitTelemetryContext.Create();

                var diagnostics = new Mock<IDiagnosticsNodeManager>();
                var session = new Mock<ISession>();
                MockServer = new Mock<IServerInternal>();
                MockServer.Setup(server => server.NamespaceUris).Returns(new NamespaceTable());
                MockServer.Setup(server => server.ServerUris).Returns(new StringTable());
                MockServer.Setup(server => server.TypeTree).Returns(new TypeTable(new NamespaceTable()));
                MockServer.Setup(server => server.Factory).Returns(EncodeableFactory.Create());
                MockServer.Setup(server => server.Telemetry).Returns(Telemetry);
                MockServer.Setup(server => server.DiagnosticsNodeManager).Returns(diagnostics.Object);

                AggregateManager = new AggregateManager(MockServer.Object);
                MockServer.Setup(server => server.AggregateManager).Returns(AggregateManager);

                var operationContext = new OperationContext(
                    new RequestHeader(),
                    null!,
                    RequestType.HistoryRead,
                    RequestLifetime.None,
                    session.Object);
                SystemContext = new ServerSystemContext(MockServer.Object, operationContext);
            }

            public AggregateManager AggregateManager { get; }
            public Mock<IServerInternal> MockServer { get; }
            public InMemoryHistorianProvider Provider { get; }
            public ServerSystemContext SystemContext { get; }
            public ITelemetryContext Telemetry { get; }

            public void Dispose()
            {
                AggregateManager.Dispose();
                Provider.Dispose();
            }

            public async Task<List<DataValue>> ReadProcessedAsync(
                NodeId aggregateId,
                List<DataValue> rawValues,
                DateTimeUtc startTime,
                DateTimeUtc endTime,
                double processingInterval,
                AggregateConfiguration configuration)
            {
                var result = new HistoryReadResult();
                ServiceResult error = await DispatchProcessedAsync(
                    aggregateId,
                    rawValues,
                    startTime,
                    endTime,
                    processingInterval,
                    configuration,
                    result).ConfigureAwait(false);

                Assert.That(ServiceResult.IsGood(error), Is.True, error.ToString());
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(result.HistoryData.TryGetValue(out HistoryData historyData), Is.True);
                return [.. historyData!.DataValues];
            }

            public async Task<ServiceResult> DispatchProcessedAsync(
                NodeId aggregateId,
                List<DataValue> rawValues,
                DateTimeUtc startTime,
                DateTimeUtc endTime,
                double processingInterval,
                AggregateConfiguration configuration,
                HistoryReadResult result)
            {
                QualifiedName aggregateName = Aggregators.GetNameForStandardAggregate(aggregateId);
                await AggregateManager.RegisterFactoryAsync(
                    aggregateId,
                    aggregateName.Name,
                    Aggregators.CreateStandardCalculator,
                    CancellationToken.None).ConfigureAwait(false);

                var nodeId = new NodeId($"ctt-aggregate-{Guid.NewGuid():N}", 1);
                Provider.Register(nodeId);
                if (rawValues.Count > 0)
                {
                    HistorianUpdateOutcome<DataValue> insertOutcome = await Provider.InsertAsync(
                        CreateContext(),
                        nodeId,
                        rawValues,
                        CancellationToken.None).ConfigureAwait(false);
                    Assert.That(insertOutcome.OperationResults, Has.Count.EqualTo(rawValues.Count));
                    Assert.That(insertOutcome.OperationResults.ToArray(), Has.All.Matches<StatusCode>(StatusCode.IsGood));
                }

                var node = new BaseDataVariableState(null)
                {
                    NodeId = nodeId,
                    BrowseName = new QualifiedName("AggregateVariable"),
                    AccessLevel = AccessLevels.HistoryRead,
                    Historizing = true
                };
                var details = new ReadProcessedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    ProcessingInterval = processingInterval,
                    AggregateConfiguration = configuration
                };
                var nodeToRead = new HistoryReadValueId
                {
                    NodeId = nodeId,
                    ContinuationPoint = ByteString.Empty
                };

                return await HistorianDispatcher.DispatchProcessedReadAsync(
                    SystemContext,
                    Provider,
                    node,
                    nodeToRead,
                    details,
                    aggregateId,
                    TimestampsToReturn.Source,
                    result,
                    CancellationToken.None).ConfigureAwait(false);
            }

            private HistorianOperationContext CreateContext()
            {
                return new HistorianOperationContext(
                    SystemContext,
                    SystemContext.OperationContext!,
                    null,
                    HistoryUpdateType.Insert);
            }
        }

        private static readonly DateTimeUtc s_baseTime = new(2025, 1, 1, 0, 0, 0);
    }
}
