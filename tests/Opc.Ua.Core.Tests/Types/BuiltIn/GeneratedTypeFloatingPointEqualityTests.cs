// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
