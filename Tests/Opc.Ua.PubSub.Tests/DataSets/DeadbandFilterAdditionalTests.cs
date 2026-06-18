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
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.DataSets
{
    /// <summary>
    /// Additional coverage for <see cref="DeadbandFilter"/> focusing on
    /// the numeric-type conversion paths inside the private TryGetDouble
    /// helper and edge-case branches not exercised by the base tests.
    /// </summary>
    /// <remarks>
    /// Reflection is used only for the private helper method TryGetDouble;
    /// PassesFilter (the public API) is used wherever possible.
    /// </remarks>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    [TestSpec("6.2.11.1", Summary = "DeadbandFilter numeric type conversions and edge cases")]
    public sealed class DeadbandFilterAdditionalTests
    {
        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_BothNull_ReturnsFalse()
        {
            bool result = DeadbandFilter.PassesFilter(
                null!,
                null!,
                new DeadbandDescriptor(DeadbandType.None, 0, null));

            Assert.That(result, Is.False,
                "null previous + null current: current is null so the guard returns false.");
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_PreviousNullCurrentNotNull_ReturnsTrue()
        {
            DataSetField current = Field(1.0);

            bool result = DeadbandFilter.PassesFilter(
                null!,
                current,
                new DeadbandDescriptor(DeadbandType.None, 0, null));

            Assert.That(result, Is.True, "previous null → current is not null → true.");
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_CurrentNull_ReturnsTrue()
        {
            DataSetField previous = Field(1.0);

            bool result = DeadbandFilter.PassesFilter(
                previous,
                null!,
                new DeadbandDescriptor(DeadbandType.Absolute, 0.1, null));

            Assert.That(result, Is.True, "current null → always passes.");
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_Int32Type_UsesNumericDeadband()
        {
            DataSetField prev = new DataSetField { Name = "f", Value = new Variant((int)100) };
            DataSetField curr = new DataSetField { Name = "f", Value = new Variant((int)105) };

            // Deadband = 10 → |105 - 100| = 5 ≤ 10 → suppress
            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Absolute, 10.0, null));

            Assert.That(result, Is.False);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_UInt32Type_UsesNumericDeadband()
        {
            DataSetField prev = new DataSetField { Name = "f", Value = new Variant((uint)100u) };
            DataSetField curr = new DataSetField { Name = "f", Value = new Variant((uint)120u) };

            // |120 - 100| = 20 > 10 → pass
            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Absolute, 10.0, null));

            Assert.That(result, Is.True);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_Int64Type_UsesNumericDeadband()
        {
            DataSetField prev = new DataSetField { Name = "f", Value = new Variant((long)1000L) };
            DataSetField curr = new DataSetField { Name = "f", Value = new Variant((long)1005L) };

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Absolute, 10.0, null));

            Assert.That(result, Is.False);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_UInt64Type_UsesNumericDeadband()
        {
            DataSetField prev = new DataSetField { Name = "f", Value = new Variant((ulong)500UL) };
            DataSetField curr = new DataSetField { Name = "f", Value = new Variant((ulong)520UL) };

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Absolute, 10.0, null));

            Assert.That(result, Is.True);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_Int16Type_UsesNumericDeadband()
        {
            DataSetField prev = new DataSetField { Name = "f", Value = new Variant((short)200) };
            DataSetField curr = new DataSetField { Name = "f", Value = new Variant((short)203) };

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Absolute, 10.0, null));

            Assert.That(result, Is.False);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_UInt16Type_UsesNumericDeadband()
        {
            DataSetField prev = new DataSetField { Name = "f", Value = new Variant((ushort)200) };
            DataSetField curr = new DataSetField { Name = "f", Value = new Variant((ushort)215) };

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Absolute, 10.0, null));

            Assert.That(result, Is.True);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_SByteType_UsesNumericDeadband()
        {
            DataSetField prev = new DataSetField { Name = "f", Value = new Variant((sbyte)10) };
            DataSetField curr = new DataSetField { Name = "f", Value = new Variant((sbyte)12) };

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Absolute, 5.0, null));

            Assert.That(result, Is.False);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_ByteType_UsesNumericDeadband()
        {
            DataSetField prev = new DataSetField { Name = "f", Value = new Variant((byte)10) };
            DataSetField curr = new DataSetField { Name = "f", Value = new Variant((byte)20) };

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Absolute, 5.0, null));

            Assert.That(result, Is.True);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_FloatType_UsesNumericDeadband()
        {
            DataSetField prev = new DataSetField { Name = "f", Value = new Variant(1.0f) };
            DataSetField curr = new DataSetField { Name = "f", Value = new Variant(1.5f) };

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Absolute, 1.0, null));

            Assert.That(result, Is.False);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_DoubleType_UsesNumericDeadband()
        {
            DataSetField prev = new DataSetField { Name = "f", Value = new Variant(10.0) };
            DataSetField curr = new DataSetField { Name = "f", Value = new Variant(25.0) };

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Absolute, 10.0, null));

            Assert.That(result, Is.True);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_PercentDeadband_ZeroPreviousValue_AnyDiffPasses()
        {
            // When previous = 0 and no EuRange, scale = |0| = 0 →
            // PassesNumeric falls back to diff > 0.
            DataSetField prev = Field(0.0);
            DataSetField curr = Field(0.001);

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Percent, 10.0, null));

            Assert.That(result, Is.True, "Any non-zero change passes when previous value is 0.");
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_PercentDeadband_ZeroPreviousValue_ZeroDiffSuppressed()
        {
            DataSetField prev = Field(0.0);
            DataSetField curr = Field(0.0);

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Percent, 10.0, null));

            Assert.That(result, Is.False, "Zero change from zero previous must be suppressed.");
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_NoneDeadbandWithPositiveValue_UsesEqualityCheck()
        {
            // DeadbandType.None → PassesFilter should return !previous.Value.Equals(current.Value)
            // regardless of DeadbandValue magnitude.
            DataSetField prev = Field(10.0);
            DataSetField curr = Field(10.0);

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.None, 99.9, null));

            Assert.That(result, Is.False, "Identical values must be suppressed under None deadband.");
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_AbsoluteExactlyAtThreshold_Suppressed()
        {
            // |diff| == threshold → NOT strictly greater → suppress
            DataSetField prev = Field(10.0);
            DataSetField curr = Field(11.0);

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Absolute, 1.0, null));

            Assert.That(result, Is.False, "Equal to threshold is not strictly above → suppress.");
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_DifferentTimestampSameValueWithinAbsoluteDeadband_Suppressed()
        {
            // Two fields with different SourceTimestamps but values within deadband.
            // The timestamp branch checks numeric deadband when timestamps differ AND
            // deadband is active.
            var ts1 = new DateTimeUtc(new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc));
            var ts2 = new DateTimeUtc(new System.DateTime(2024, 1, 2, 0, 0, 0, System.DateTimeKind.Utc));

            DataSetField prev = new DataSetField
            {
                Name = "f",
                Value = new Variant(10.0),
                SourceTimestamp = ts1
            };
            DataSetField curr = new DataSetField
            {
                Name = "f",
                Value = new Variant(10.5),  // delta = 0.5 < deadband 1.0
                SourceTimestamp = ts2
            };

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Absolute, 1.0, null));

            // The timestamp-changed numeric path: diff = 0.5 ≤ 1.0 → suppress
            Assert.That(result, Is.False);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_DifferentTimestampLargeValueAboveAbsoluteDeadband_Passes()
        {
            var ts1 = new DateTimeUtc(new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc));
            var ts2 = new DateTimeUtc(new System.DateTime(2024, 1, 2, 0, 0, 0, System.DateTimeKind.Utc));

            DataSetField prev = new DataSetField
            {
                Name = "f",
                Value = new Variant(10.0),
                SourceTimestamp = ts1
            };
            DataSetField curr = new DataSetField
            {
                Name = "f",
                Value = new Variant(20.0),  // delta = 10 > deadband 1.0
                SourceTimestamp = ts2
            };

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Absolute, 1.0, null));

            Assert.That(result, Is.True);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_NonNumericEqualStrings_Suppressed()
        {
            DataSetField prev = new DataSetField { Name = "f", Value = new Variant("hello") };
            DataSetField curr = new DataSetField { Name = "f", Value = new Variant("hello") };

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Absolute, 100.0, null));

            Assert.That(result, Is.False, "Identical non-numeric values must be suppressed.");
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_NonNumericDifferentStrings_Passes()
        {
            DataSetField prev = new DataSetField { Name = "f", Value = new Variant("a") };
            DataSetField curr = new DataSetField { Name = "f", Value = new Variant("z") };

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Percent, 100.0, 1000.0));

            Assert.That(result, Is.True, "Different non-numeric values must pass.");
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_GoodToBadStatus_ReturnsTrue()
        {
            DataSetField prev = Field(5.0); // Good status (default)
            DataSetField curr = new DataSetField
            {
                Name = "f",
                Value = new Variant(5.0),
                StatusCode = (StatusCode)StatusCodes.BadNotFound
            };

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.None, 0, null));

            Assert.That(result, Is.True, "Any status change must pass immediately.");
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_UncertainStatus_WhenSameStatus_UsesDeadbandCheck()
        {
            StatusCode uncertain = (StatusCode)StatusCodes.UncertainInitialValue;

            DataSetField prev = new DataSetField
            {
                Name = "f",
                Value = new Variant(10.0),
                StatusCode = uncertain
            };
            DataSetField curr = new DataSetField
            {
                Name = "f",
                Value = new Variant(10.5),
                StatusCode = uncertain
            };

            // Same status → proceed to deadband check; |Δ| = 0.5 < 1.0 → suppress
            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Absolute, 1.0, null));

            Assert.That(result, Is.False);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_AbsoluteDeadbandWithZeroValue_UsesEquality()
        {
            DataSetField prev = Field(5.0);
            DataSetField curr = Field(5.0);

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Absolute, 0.0, null));

            Assert.That(result, Is.False, "Zero deadband with equal values → suppress.");
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_AbsoluteDeadbandWithZeroValue_AnyDiffPasses()
        {
            DataSetField prev = Field(5.0);
            DataSetField curr = Field(5.001);

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Absolute, 0.0, null));

            Assert.That(result, Is.True, "Zero deadband: any change passes via equality path.");
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void PassesFilter_PercentWithZeroEuRange_FallsBackToPreviousMagnitude()
        {
            // EuRange = 0 → treated as absent → scale by |previous|
            DataSetField prev = Field(50.0);
            DataSetField curr = Field(54.0); // delta = 4; 10% of |50| = 5 → 4 ≤ 5 → suppress

            bool result = DeadbandFilter.PassesFilter(
                prev, curr, new DeadbandDescriptor(DeadbandType.Percent, 10.0, 0.0));

            Assert.That(result, Is.False);
        }

        private static DataSetField Field(double v)
        {
            return new DataSetField { Name = "f", Value = new Variant(v) };
        }
    }
}
