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

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies exact values, interpolation, bounds, and error statuses for at-time aggregate calculations.
    /// </summary>
    [TestFixture]
    [Category("AggregateCalculator")]
    [Parallelizable(ParallelScope.All)]
    public sealed class AggregateCalculatorAtTimeTests
    {
        /// <summary>
        /// Verifies that an at-time calculation returns the exact raw value at a matching timestamp.
        /// </summary>
        [TestCaseSource(nameof(ExactValues))]
        public void CalculateAtTimeReturnsExactRawValue(
            StatusCode statusCode)
        {
            DateTimeUtc timestamp = TimeAt(5);
            DataValue result = AggregateCalculator.CalculateAtTime(
                [
                    ValueAt(5, 42, statusCode)
                ],
                timestamp,
                useSimpleBounds: false,
                stepped: false);

            Assert.That(result.WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(42));
            Assert.That(
                result.StatusCode.CodeBits,
                Is.EqualTo(statusCode.CodeBits));
            Assert.That(
                result.StatusCode.AggregateBits,
                Is.EqualTo(AggregateBits.Raw));
            Assert.That(result.SourceTimestamp, Is.EqualTo(timestamp));
        }

        /// <summary>
        /// Verifies that an at-time calculation uses sloped interpolation between bounds.
        /// </summary>
        [Test]
        public void CalculateAtTimeUsesSlopedInterpolation()
        {
            DataValue result = AggregateCalculator.CalculateAtTime(
                [
                    ValueAt(0, 0),
                    ValueAt(10, 10)
                ],
                TimeAt(5),
                useSimpleBounds: false,
                stepped: false);

            Assert.That(result.WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(5));
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(
                result.StatusCode.AggregateBits,
                Is.EqualTo(AggregateBits.Interpolated));
        }

        /// <summary>
        /// Verifies that an at-time calculation uses the preceding value for stepped interpolation.
        /// </summary>
        [Test]
        public void CalculateAtTimeUsesSteppedInterpolation()
        {
            DataValue result = AggregateCalculator.CalculateAtTime(
                [
                    ValueAt(0, 3),
                    ValueAt(10, 10)
                ],
                TimeAt(5),
                useSimpleBounds: false,
                stepped: true);

            Assert.That(result.WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(3));
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(
                result.StatusCode.AggregateBits,
                Is.EqualTo(AggregateBits.Interpolated));
        }

        /// <summary>
        /// Verifies that simple-bound at-time reads still calculate an interpolated value.
        /// </summary>
        [Test]
        public void CalculateAtTimeSimpleBoundsStillCalculateValue()
        {
            DataValue result = AggregateCalculator.CalculateAtTime(
                [
                    ValueAt(0, 0),
                    ValueAt(10, 10)
                ],
                TimeAt(5),
                useSimpleBounds: true,
                stepped: false);

            Assert.That(result.WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(5));
            Assert.That(
                result.StatusCode.AggregateBits,
                Is.EqualTo(AggregateBits.Interpolated));
        }

        /// <summary>
        /// Verifies that interpolated at-time bounds skip bad values.
        /// </summary>
        [Test]
        public void CalculateAtTimeInterpolatedBoundsSkipBadValues()
        {
            DataValue result = AggregateCalculator.CalculateAtTime(
                [
                    ValueAt(0, 0),
                    ValueAt(5, 100, StatusCodes.BadNoData),
                    ValueAt(10, 10)
                ],
                TimeAt(7),
                useSimpleBounds: false,
                stepped: false);

            Assert.That(result.WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(7));
            Assert.That(StatusCode.IsUncertain(result.StatusCode), Is.True);
            Assert.That(
                result.StatusCode.AggregateBits,
                Is.EqualTo(AggregateBits.Interpolated));
        }

        /// <summary>
        /// Verifies that a bad late simple bound produces an uncertain stepped value.
        /// </summary>
        [Test]
        public void CalculateAtTimeSimpleBadLateBoundUsesUncertainStep()
        {
            DataValue result = AggregateCalculator.CalculateAtTime(
                [
                    ValueAt(0, 4),
                    ValueAt(10, 10, StatusCodes.BadNoData)
                ],
                TimeAt(5),
                useSimpleBounds: true,
                stepped: false);

            Assert.That(result.WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(4));
            Assert.That(StatusCode.IsUncertain(result.StatusCode), Is.True);
            Assert.That(
                result.StatusCode.AggregateBits,
                Is.EqualTo(AggregateBits.Interpolated));
        }

        /// <summary>
        /// Verifies that an at-time calculation without a starting value returns BadNoData.
        /// </summary>
        [Test]
        public void CalculateAtTimeWithoutStartingValueReturnsBadNoData()
        {
            DataValue result = AggregateCalculator.CalculateAtTime(
                [
                    ValueAt(10, 10)
                ],
                TimeAt(5),
                useSimpleBounds: false,
                stepped: false);

            Assert.That(
                result.StatusCode,
                Is.EqualTo(StatusCodes.BadNoData));
            Assert.That(result.SourceTimestamp, Is.EqualTo(TimeAt(5)));
        }

        /// <summary>
        /// Verifies that sloped interpolation of an unsupported type returns BadTypeMismatch.
        /// </summary>
        [Test]
        public void CalculateAtTimeUnsupportedSlopedTypeReturnsBadTypeMismatch()
        {
            DataValue result = AggregateCalculator.CalculateAtTime(
                [
                    TextValueAt(0, "before"),
                    TextValueAt(10, "after")
                ],
                TimeAt(5),
                useSimpleBounds: false,
                stepped: false);

            Assert.That(
                result.StatusCode,
                Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        private static readonly StatusCode[] ExactValues =
        [
            StatusCodes.Good,
            StatusCodes.Uncertain,
            StatusCodes.Bad
        ];

        private static DataValue ValueAt(
            int second,
            int value,
            StatusCode statusCode = default)
        {
            if (statusCode == default)
            {
                statusCode = StatusCodes.Good;
            }
            return new DataValue(
                Variant.From(value),
                statusCode,
                TimeAt(second),
                DateTimeUtc.MinValue);
        }

        private static DataValue TextValueAt(int second, string value)
        {
            return new DataValue(
                Variant.From(value),
                StatusCodes.Good,
                TimeAt(second),
                DateTimeUtc.MinValue);
        }

        private static DateTimeUtc TimeAt(int second)
        {
            return new DateTimeUtc(2026, 1, 1, 0, 0, second);
        }
    }
}
