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

namespace Opc.Ua.Core.Tests.Types.BuiltIn
{
    /// <summary>
    /// Source-generated <c>IsEqual</c> implementations must compare scalar
    /// floating point fields NaN-safely. The generator previously emitted the
    /// <c>!=</c> operator for double/float fields, but <c>NaN != NaN</c> is true,
    /// which made a decoded value compare unequal to an identical copy and tripped
    /// the binary encoder idempotency fuzz gate (issue #3546). These tests pin the
    /// reflexive contract for representative generated data types.
    /// </summary>
    [TestFixture]
    [Category("Equality")]
    public class GeneratedTypeFloatingPointEqualityTests
    {
        [Test]
        public void RangeWithNaNBoundsIsEqualToIdenticalRange()
        {
            var left = new Range { Low = double.NaN, High = double.NaN };
            var right = new Range { Low = double.NaN, High = double.NaN };

            Assert.That(left.IsEqual(right), Is.True);
        }

        [Test]
        public void RangeWithDifferentBoundsIsNotEqual()
        {
            var left = new Range { Low = 1.0, High = 2.0 };
            var right = new Range { Low = 1.0, High = 3.0 };

            Assert.That(left.IsEqual(right), Is.False);
        }

        [Test]
        public void AxisInformationWithNaNRangeIsEqualToIdenticalCopy()
        {
            var left = new AxisInformation
            {
                EngineeringUnits = new EUInformation(),
                EURange = new Range { Low = double.NaN, High = double.NaN },
                Title = new LocalizedText("axis"),
                AxisScaleType = AxisScaleEnumeration.Linear,
                AxisSteps = [double.NaN, 1.0]
            };
            var right = new AxisInformation
            {
                EngineeringUnits = new EUInformation(),
                EURange = new Range { Low = double.NaN, High = double.NaN },
                Title = new LocalizedText("axis"),
                AxisScaleType = AxisScaleEnumeration.Linear,
                AxisSteps = [double.NaN, 1.0]
            };

            Assert.That(left.IsEqual(right), Is.True);
        }
    }
}
