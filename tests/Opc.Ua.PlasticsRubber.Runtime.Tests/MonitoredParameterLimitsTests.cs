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
using NUnit.Framework;
using Opc.Ua.PlasticsRubber.Server;

namespace Opc.Ua.PlasticsRubber.Runtime.Tests
{
    /// <summary>
    /// Verifies signed relative bands, exact boundaries and absolute alarm precedence.
    /// </summary>
    [TestFixture]
    public sealed class MonitoredParameterLimitsTests
    {
        /// <summary>
        /// Covers every applicable monitored status and its adjacent exact boundaries.
        /// </summary>
        [TestCase(69, 2)]
        [TestCase(70, 3)]
        [TestCase(79, 3)]
        [TestCase(80, 4)]
        [TestCase(94, 4)]
        [TestCase(95, 5)]
        [TestCase(100, 5)]
        [TestCase(105, 5)]
        [TestCase(106, 6)]
        [TestCase(120, 6)]
        [TestCase(121, 7)]
        [TestCase(130, 7)]
        [TestCase(131, 8)]
        public void SignedTolerancesPreserveExactBoundaries(double value, int expected)
        {
            var limits = new MonitoredParameterLimits
            {
                SetValue = 100,
                Minimum = 70,
                Maximum = 130,
                LowerTolerance = -5,
                UpperTolerance = 5,
                LowerTolerance2 = -20,
                UpperTolerance2 = 20
            };
            Assert.That(limits.Evaluate(value, StatusCodes.Good), Is.EqualTo((ushort)expected));
        }

        /// <summary>
        /// Missing optional boundaries must not become zero-valued limits.
        /// </summary>
        [TestCase(-100)]
        [TestCase(0)]
        [TestCase(100)]
        public void NoConfiguredLimitsReportsNoMonitoring(double value)
        {
            Assert.That(new MonitoredParameterLimits().Evaluate(value, StatusCodes.Good), Is.Zero);
        }

        /// <summary>
        /// Bad or uncertain measurements do not masquerade as known tolerance results.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void UnreliableMeasurementReportsUnknown(bool bad)
        {
            StatusCode quality = bad ? StatusCodes.BadNoCommunication : StatusCodes.UncertainLastUsableValue;
            var limits = new MonitoredParameterLimits { Minimum = 0, Maximum = 10 };
            Assert.That(limits.Evaluate(5, quality), Is.EqualTo((ushort)1));
        }

        /// <summary>
        /// Nonfinite measurements are unknown even if a provider mislabeled their quality.
        /// </summary>
        [TestCase(double.NaN)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(double.PositiveInfinity)]
        public void NonfiniteMeasurementReportsUnknown(double value)
        {
            Assert.That(new MonitoredParameterLimits().Evaluate(value, StatusCodes.Good), Is.EqualTo((ushort)1));
        }

        /// <summary>
        /// Absolute alarms take precedence over an overlapping relative warning.
        /// </summary>
        [TestCase(9, 2)]
        [TestCase(21, 8)]
        public void AbsoluteAlarmWinsOverRelativeBand(double value, int expected)
        {
            var limits = new MonitoredParameterLimits
            {
                SetValue = 15,
                Minimum = 10,
                Maximum = 20,
                LowerTolerance = -1,
                UpperTolerance = 1
            };
            Assert.That(limits.Evaluate(value, StatusCodes.Good), Is.EqualTo((ushort)expected));
        }

        /// <summary>
        /// A single-sided limit works without a fabricated opposite boundary or setpoint.
        /// </summary>
        [Test]
        public void AbsoluteLimitsDoNotRequireSetpoint()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    new MonitoredParameterLimits { Minimum = -5 }.Evaluate(-4, StatusCodes.Good),
                    Is.EqualTo((ushort)5));
                Assert.That(
                    new MonitoredParameterLimits { Maximum = -5 }.Evaluate(-4, StatusCodes.Good),
                    Is.EqualTo((ushort)8));
            });
        }

        /// <summary>
        /// A relative band requires its actual reference value.
        /// </summary>
        [Test]
        public void RelativeLimitsWithoutSetpointAreRejected()
        {
            var limits = new MonitoredParameterLimits { LowerTolerance = -5 };
            Assert.Throws<ArgumentException>(() => limits.Validate());
        }

        /// <summary>
        /// The two bands may not cross each other.
        /// </summary>
        [Test]
        public void InvertedOuterBandIsRejected()
        {
            var limits = new MonitoredParameterLimits
            {
                SetValue = 0,
                LowerTolerance = -10,
                LowerTolerance2 = -5
            };
            Assert.Throws<ArgumentException>(() => limits.Validate());
        }

        /// <summary>
        /// The public contract uses signed lower deviations, not positive magnitudes.
        /// </summary>
        [Test]
        public void PositiveLowerDeviationIsRejected()
        {
            var limits = new MonitoredParameterLimits { SetValue = 0, LowerTolerance = 5 };
            Assert.Throws<ArgumentOutOfRangeException>(() => limits.Validate());
        }

        /// <summary>
        /// A negative upper deviation is not silently converted to a magnitude.
        /// </summary>
        [Test]
        public void NegativeUpperDeviationIsRejected()
        {
            var limits = new MonitoredParameterLimits { SetValue = 0, UpperTolerance = -5 };
            Assert.Throws<ArgumentOutOfRangeException>(() => limits.Validate());
        }

        /// <summary>
        /// Reversed absolute alarm bounds fail explicitly.
        /// </summary>
        [Test]
        public void InvertedAbsoluteLimitsAreRejected()
        {
            var limits = new MonitoredParameterLimits { Minimum = 10, Maximum = 5 };
            Assert.Throws<ArgumentException>(() => limits.Validate());
        }

        /// <summary>
        /// Configuration cannot hide nonfinite values behind IEEE comparisons.
        /// </summary>
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void NonfiniteConfigurationIsRejected(double value)
        {
            var limits = new MonitoredParameterLimits { Maximum = value };
            Assert.Throws<ArgumentOutOfRangeException>(() => limits.Validate());
        }
    }
}
