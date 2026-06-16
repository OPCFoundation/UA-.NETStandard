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
    /// Validates the per-field deadband filter logic from
    /// Part 14 §6.2.11.1: None passes any change, Absolute uses
    /// |Δ| comparison, Percent scales by EU range or previous value.
    /// </summary>
    [TestFixture]
    [TestSpec("6.2.11.1", Summary = "DeadbandFilter Absolute / Percent / None")]
    public class DeadbandFilterTests
    {
        [Test]
        [TestSpec("6.2.11.1")]
        public void NoDeadband_AnyChangePasses()
        {
            DataSetField prev = Field(1.0);
            DataSetField curr = Field(1.0000001);
            bool passes = DeadbandFilter.PassesFilter(prev, curr,
                new DeadbandDescriptor(DeadbandType.None, 0, null));
            Assert.That(passes, Is.True);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void NoDeadband_IdenticalValueSuppressed()
        {
            DataSetField prev = Field(2.0);
            DataSetField curr = Field(2.0);
            bool passes = DeadbandFilter.PassesFilter(prev, curr,
                new DeadbandDescriptor(DeadbandType.None, 0, null));
            Assert.That(passes, Is.False);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void Absolute_BelowThresholdSuppressed()
        {
            DataSetField prev = Field(10.0);
            DataSetField curr = Field(10.5);
            bool passes = DeadbandFilter.PassesFilter(prev, curr,
                new DeadbandDescriptor(DeadbandType.Absolute, 1.0, null));
            Assert.That(passes, Is.False);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void Absolute_AboveThresholdPasses()
        {
            DataSetField prev = Field(10.0);
            DataSetField curr = Field(12.0);
            bool passes = DeadbandFilter.PassesFilter(prev, curr,
                new DeadbandDescriptor(DeadbandType.Absolute, 1.0, null));
            Assert.That(passes, Is.True);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void Percent_WithEuRangeBelowThresholdSuppressed()
        {
            DataSetField prev = Field(50.0);
            DataSetField curr = Field(51.0);
            // 10% of 100 = 10; |Δ| = 1 → suppress
            bool passes = DeadbandFilter.PassesFilter(prev, curr,
                new DeadbandDescriptor(DeadbandType.Percent, 10.0, 100.0));
            Assert.That(passes, Is.False);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void Percent_WithEuRangeAbovePasses()
        {
            DataSetField prev = Field(50.0);
            DataSetField curr = Field(70.0);
            bool passes = DeadbandFilter.PassesFilter(prev, curr,
                new DeadbandDescriptor(DeadbandType.Percent, 10.0, 100.0));
            Assert.That(passes, Is.True);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void Percent_WithoutEuRangeScalesByPreviousMagnitude()
        {
            DataSetField prev = Field(100.0);
            DataSetField curr = Field(105.0);
            // 10% of |100| = 10; |Δ| = 5 → suppress
            bool passes = DeadbandFilter.PassesFilter(prev, curr,
                new DeadbandDescriptor(DeadbandType.Percent, 10.0, null));
            Assert.That(passes, Is.False);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void StatusChangeAlwaysPasses()
        {
            DataSetField prev = Field(1.0);
            var curr = new DataSetField
            {
                Name = "f",
                Value = new Variant(1.0),
                StatusCode = (StatusCode)StatusCodes.BadInternalError
            };
            bool passes = DeadbandFilter.PassesFilter(prev, curr,
                new DeadbandDescriptor(DeadbandType.Absolute, 100, null));
            Assert.That(passes, Is.True);
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public void NonNumericValueFallsBackToEquality()
        {
            var prev = new DataSetField { Name = "f", Value = new Variant("a") };
            var curr = new DataSetField { Name = "f", Value = new Variant("b") };
            bool passes = DeadbandFilter.PassesFilter(prev, curr,
                new DeadbandDescriptor(DeadbandType.Absolute, 100, null));
            Assert.That(passes, Is.True);
        }

        private static DataSetField Field(double v)
        {
            return new DataSetField { Name = "f", Value = new Variant(v) };
        }
    }
}
