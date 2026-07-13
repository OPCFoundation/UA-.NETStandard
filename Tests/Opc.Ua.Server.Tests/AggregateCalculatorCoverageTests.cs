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
    /// Tests for the shared helpers on the <see cref="AggregateCalculator"/> base class,
    /// in particular the public stepped/sloped interpolation routines and the reverse-time
    /// and sloped-extrapolation processing branches.
    /// </summary>
    [TestFixture]
    [Category("Aggregators")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class AggregateCalculatorCoverageTests
    {
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        private static DataValue Good(double value, DateTimeUtc timestamp)
        {
            return new DataValue(new Variant(value), StatusCodes.Good, timestamp, timestamp);
        }

        [Test]
        public void SteppedInterpolateWithGoodEarlyBoundReturnsInterpolatedGood()
        {
            var timestamp = new DateTimeUtc(2024, 1, 1, 0, 0, 5);
            DataValue earlyBound = Good(42.0, new DateTimeUtc(2024, 1, 1, 0, 0, 0));

            DataValue result = AggregateCalculator.SteppedInterpolate(timestamp, earlyBound);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(42.0));
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Interpolated), Is.True);
        }

        [Test]
        public void SteppedInterpolateWithBadEarlyBoundReturnsBadNoData()
        {
            var timestamp = new DateTimeUtc(2024, 1, 1, 0, 0, 5);
            var earlyBound = new DataValue(
                new Variant(1.0), StatusCodes.Bad, timestamp, timestamp);

            DataValue result = AggregateCalculator.SteppedInterpolate(timestamp, earlyBound);

            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.BadNoData));
        }

        [Test]
        public void SteppedInterpolateWithUncertainEarlyBoundReturnsUncertain()
        {
            var timestamp = new DateTimeUtc(2024, 1, 1, 0, 0, 5);
            var earlyBound = new DataValue(
                new Variant(1.0), StatusCodes.UncertainDataSubNormal, timestamp, timestamp);

            DataValue result = AggregateCalculator.SteppedInterpolate(timestamp, earlyBound);

            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Interpolated), Is.True);
        }

        [Test]
        public void SlopedInterpolateWithBadEarlyBoundReturnsBadNoData()
        {
            var timestamp = new DateTimeUtc(2024, 1, 1, 0, 0, 5);
            var earlyBound = new DataValue(
                new Variant(1.0), StatusCodes.Bad,
                new DateTimeUtc(2024, 1, 1, 0, 0, 0), new DateTimeUtc(2024, 1, 1, 0, 0, 0));
            DataValue lateBound = Good(10.0, new DateTimeUtc(2024, 1, 1, 0, 0, 10));

            DataValue result = AggregateCalculator.SlopedInterpolate(timestamp, earlyBound, lateBound);

            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.BadNoData));
        }

        [Test]
        public void SlopedInterpolateWithBadLateBoundFallsBackToStepped()
        {
            var timestamp = new DateTimeUtc(2024, 1, 1, 0, 0, 5);
            DataValue earlyBound = Good(7.0, new DateTimeUtc(2024, 1, 1, 0, 0, 0));
            var lateBound = new DataValue(
                new Variant(10.0), StatusCodes.Bad,
                new DateTimeUtc(2024, 1, 1, 0, 0, 10), new DateTimeUtc(2024, 1, 1, 0, 0, 10));

            DataValue result = AggregateCalculator.SlopedInterpolate(timestamp, earlyBound, lateBound);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(7.0));
            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
        }

        [Test]
        public void SlopedInterpolateWithGoodBoundsInterpolatesLinearly()
        {
            var early = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            var late = new DateTimeUtc(2024, 1, 1, 0, 0, 10);
            var timestamp = new DateTimeUtc(2024, 1, 1, 0, 0, 5);
            DataValue earlyBound = Good(0.0, early);
            DataValue lateBound = Good(100.0, late);

            DataValue result = AggregateCalculator.SlopedInterpolate(timestamp, earlyBound, lateBound);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(50.0).Within(0.0001));
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Interpolated), Is.True);
        }

        [Test]
        public void SlopedInterpolateWithUncertainBoundMarksUncertain()
        {
            var early = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            var late = new DateTimeUtc(2024, 1, 1, 0, 0, 10);
            var timestamp = new DateTimeUtc(2024, 1, 1, 0, 0, 5);
            var earlyBound = new DataValue(
                new Variant(0.0), StatusCodes.UncertainDataSubNormal, early, early);
            DataValue lateBound = Good(100.0, late);

            DataValue result = AggregateCalculator.SlopedInterpolate(timestamp, earlyBound, lateBound);

            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
        }

        [Test]
        public void SlopedInterpolateWithNonNumericValuesReturnsBadTypeMismatch()
        {
            var early = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            var late = new DateTimeUtc(2024, 1, 1, 0, 0, 10);
            var timestamp = new DateTimeUtc(2024, 1, 1, 0, 0, 5);
            var earlyBound = new DataValue(new Variant("not-a-number"), StatusCodes.Good, early, early);
            var lateBound = new DataValue(new Variant("also-not"), StatusCodes.Good, late, late);

            DataValue result = AggregateCalculator.SlopedInterpolate(timestamp, earlyBound, lateBound);

            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void HasEndTimePassedReflectsProcessingWindow()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);
            var configuration = new AggregateConfiguration
            {
                TreatUncertainAsBad = false,
                PercentDataBad = 100,
                PercentDataGood = 100,
                UseSlopedExtrapolation = false
            };

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Average, startTime, endTime, 10000, false, configuration, m_telemetry);

            calculator.QueueRawValue(new DataValue(new Variant(1.0), StatusCodes.Good, startTime, startTime));
            calculator.TryGetProcessedValue(false, out _);

            Assert.That(calculator.HasEndTimePassed(startTime.AddMilliseconds(5000)), Is.False);
            Assert.That(calculator.HasEndTimePassed(endTime.AddMilliseconds(5000)), Is.True);
        }

        [Test]
        public void ReverseTimeAverageComputesOverInterval()
        {
            // endTime < startTime exercises the TimeFlowsBackward branches in the base class.
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 12);
            var endTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            var configuration = new AggregateConfiguration
            {
                TreatUncertainAsBad = false,
                PercentDataBad = 100,
                PercentDataGood = 100,
                UseSlopedExtrapolation = false
            };

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Average, startTime, endTime, 12000, false, configuration, m_telemetry);

            for (int i = 0; i < 6; i++)
            {
                DateTimeUtc ts = endTime.AddMilliseconds(500 + (i * 2000));
                calculator.QueueRawValue(Good(10.0 + i, ts));
            }

            DataValue result = default;
            bool any = false;
            while (calculator.TryGetProcessedValue(true, out DataValue value))
            {
                if (!any)
                {
                    result = value;
                    any = true;
                }
            }

            Assert.That(any, Is.True);
            Assert.That(result.WrappedValue.IsNull, Is.False);
        }

        [Test]
        public void SlopedExtrapolationConfigurationProducesInterpolatedResult()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(20000);
            var configuration = new AggregateConfiguration
            {
                TreatUncertainAsBad = false,
                PercentDataBad = 100,
                PercentDataGood = 100,
                UseSlopedExtrapolation = true
            };

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Interpolative, startTime, endTime, 5000, false, configuration, m_telemetry);

            var values = new List<DataValue>
            {
                Good(0.0, startTime.AddMilliseconds(1000)),
                Good(40.0, startTime.AddMilliseconds(9000))
            };
            foreach (DataValue value in values)
            {
                calculator.QueueRawValue(value);
            }

            bool any = false;
            while (calculator.TryGetProcessedValue(true, out DataValue _))
            {
                any = true;
            }

            Assert.That(any, Is.True);
        }
    }
}
