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
using NUnit.Framework;
using Opc.Ua;
using Robotics.IntentEnabledRobot.Simulation;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Covers the resting model that decides where a released part ends up, which is what
    /// keeps a placed part on a surface instead of in the air and makes a second part
    /// placed on the same spot stand on the first rather than inside it.
    /// </summary>
    [TestFixture]
    [Category("Robotics")]
    [Parallelizable]
    public sealed class SimulatedSupportModelTests
    {
        [Test]
        public void PartReleasedOverTheBenchRestsOnTheBench()
        {
            SimulatedSupportModel model = BenchModel();

            double z = model.RestingCentreHeight(
                0.2, 0.1, CubeSize, CubeSize, CubeSize, ArrayOf<SimulatedSupportSolid>.Empty);

            Assert.That(z, Is.EqualTo(BenchTop + (CubeSize * 0.5)).Within(1e-9),
                "A part released over the bench must stand on it; leaving it at the tool " +
                "centre point is what left parts hanging in the air.");
        }

        [Test]
        public void PartReleasedOverAnotherPartStandsOnTopOfIt()
        {
            SimulatedSupportModel model = BenchModel();
            ArrayOf<SimulatedSupportSolid> standing = ArrayOf.Create<SimulatedSupportSolid>(
                [new("First", 0.2, 0.1, CubeSize, CubeSize, BenchTop + CubeSize)]);

            double z = model.RestingCentreHeight(0.2, 0.1, CubeSize, CubeSize, CubeSize, standing);

            Assert.That(z, Is.EqualTo(BenchTop + CubeSize + (CubeSize * 0.5)).Within(1e-9),
                "Stacking is the resting rule applied twice: the second part's base sits on " +
                "the first part's top.");
        }

        [Test]
        public void ThreePartsOnOneSpotStackRatherThanIntersect()
        {
            SimulatedSupportModel model = BenchModel();
            var standing = new List<SimulatedSupportSolid>();
            double previousTop = BenchTop;

            for (int ii = 0; ii < 3; ii++)
            {
                double centre = model.RestingCentreHeight(
                    0.2, 0.1, CubeSize, CubeSize, CubeSize,
                    ArrayOf.Create(standing.ToArray().AsSpan()));
                Assert.That(centre - (CubeSize * 0.5), Is.EqualTo(previousTop).Within(1e-9),
                    "Each part must land exactly on the one below, with no gap and no overlap.");
                previousTop = centre + (CubeSize * 0.5);
                standing.Add(new SimulatedSupportSolid(
                    "Part" + ii, 0.2, 0.1, CubeSize, CubeSize, previousTop));
            }

            Assert.That(previousTop, Is.EqualTo(BenchTop + (3 * CubeSize)).Within(1e-9));
        }

        [Test]
        public void PartOffToTheSideIsNotHeldUpByAPartItDoesNotOverlap()
        {
            SimulatedSupportModel model = BenchModel();
            ArrayOf<SimulatedSupportSolid> standing = ArrayOf.Create<SimulatedSupportSolid>(
                [new("Elsewhere", 0.5, 0.5, CubeSize, CubeSize, BenchTop + CubeSize)]);

            double z = model.RestingCentreHeight(0.2, 0.1, CubeSize, CubeSize, CubeSize, standing);

            Assert.That(z, Is.EqualTo(BenchTop + (CubeSize * 0.5)).Within(1e-9),
                "Only what is actually underneath can hold a part up.");
        }

        [Test]
        public void PartOverTheFixturePlateRestsOnThePlateNotTheBench()
        {
            SimulatedSupportModel model = BenchModel();

            double z = model.RestingCentreHeight(
                -0.32, 0.0, CubeSize, CubeSize, CubeSize, ArrayOf<SimulatedSupportSolid>.Empty);

            Assert.That(z, Is.EqualTo(PlateTop + (CubeSize * 0.5)).Within(1e-9),
                "The fixture plate stands on the bench, so a part on the fixture stands on " +
                "the plate.");
        }

        [Test]
        public void RequestBelowTheSupportIsRaisedToIt()
        {
            SimulatedSupportModel model = BenchModel();

            double z = model.ClampAboveSupport(
                0.2, 0.1, CubeSize, CubeSize, CubeSize,
                BenchTop - 0.05, ArrayOf<SimulatedSupportSolid>.Empty);

            Assert.That(z, Is.EqualTo(BenchTop + (CubeSize * 0.5)).Within(1e-9),
                "A part cannot be put through the bench, which is the whole point of " +
                "clamping rather than trusting the requested height.");
        }

        [Test]
        public void RequestAboveTheSupportIsLeftAlone()
        {
            SimulatedSupportModel model = BenchModel();
            const double held = BenchTop + 0.30;

            double z = model.ClampAboveSupport(
                0.2, 0.1, CubeSize, CubeSize, CubeSize, held, ArrayOf<SimulatedSupportSolid>.Empty);

            Assert.That(z, Is.EqualTo(held).Within(1e-9),
                "A part in the gripper is legitimately above its resting height, so the " +
                "clamp must not drag it down.");
        }

        [Test]
        public void FootprintOffEverySolidFallsToGroundLevel()
        {
            SimulatedSupportModel model = BenchModel();

            double z = model.RestingCentreHeight(
                5.0, 5.0, CubeSize, CubeSize, CubeSize, ArrayOf<SimulatedSupportSolid>.Empty);

            Assert.That(z, Is.EqualTo(GroundLevel + (CubeSize * 0.5)).Within(1e-9));
        }

        [Test]
        public void FootprintsThatOnlyTouchDoNotOverlap()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    SimulatedSupportModel.FootprintsOverlap(0.0, 0.0, 0.1, 0.1,
                        new SimulatedSupportSolid("Overlapping", 0.08, 0.0, 0.1, 0.1, 1.0)), Is.True,
                    "Centres 80 mm apart with 100 mm footprints overlap by 20 mm.");
                Assert.That(
                    SimulatedSupportModel.FootprintsOverlap(0.0, 0.0, 0.1, 0.1,
                        new SimulatedSupportSolid("EdgeToEdge", 0.10, 0.0, 0.1, 0.1, 1.0)), Is.False,
                    "Footprints exactly edge to edge are not stacked on one another.");
                Assert.That(
                    SimulatedSupportModel.FootprintsOverlap(0.0, 0.0, 0.1, 0.1,
                        new SimulatedSupportSolid("Apart", 0.14, 0.0, 0.1, 0.1, 1.0)), Is.False,
                    "A 40 mm gap is a gap.");
            });
        }

        private static SimulatedSupportModel BenchModel()
        {
            return new SimulatedSupportModel(
                ArrayOf.Create<SimulatedSupportSolid>(
                [
                    new("Bench", 0.0, 0.0, 1.4, 0.9, BenchTop),
                    new("FixturePlate", -0.32, 0.0, 0.14, 0.14, PlateTop)
                ]),
                GroundLevel);
        }

        private const double BenchTop = 0.829;
        private const double PlateTop = 0.838;
        private const double GroundLevel = 0.0;
        private const double CubeSize = 0.04;
    }
}
