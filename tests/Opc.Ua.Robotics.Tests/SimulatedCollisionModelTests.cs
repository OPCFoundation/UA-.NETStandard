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
using Opc.Ua;
using Robotics.IntentEnabledRobot.Simulation;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Covers the collision model that keeps the arm out of the cell's furniture. The case
    /// that matters most is a link spanning a surface while both of its ends stay clear:
    /// the height test this replaces sampled joint origins only, so it passed exactly the
    /// configurations that render an arm passing through its own bench.
    /// </summary>
    [TestFixture]
    [Category("Robotics")]
    [Parallelizable]
    public sealed class SimulatedCollisionModelTests
    {
        [Test]
        public void LinkSpanningTheBenchIsRejectedThoughBothEndsAreAboveIt()
        {
            SimulatedCollisionModel model = BenchModel();

            // Shoulder high, elbow below the bench top, wrist high again: every joint
            // origin that the old height test sampled is above the surface except the one
            // in the middle, and the links between them sweep straight through the table.
            double[] throughBench = [-0.30, 0.0, 0.20, 0.0, 0.0, -0.02, 0.30, 0.0, 0.20];

            Assert.That(model.IsClear(throughBench, out string hit), Is.False,
                "A link that reaches below the bench between two joints must be refused.");
            Assert.That(hit, Is.EqualTo("Bench"));
        }

        [Test]
        public void LinkGrazingTheBenchSurfaceIsRejected()
        {
            SimulatedCollisionModel model = BenchModel();

            // Both ends clear the bench, but only by less than the link's own thickness.
            double[] grazing = [-0.30, 0.0, 0.01, 0.30, 0.0, 0.01, 0.30, 0.0, 0.30];

            Assert.That(model.IsClear(grazing, out string hit), Is.False,
                "A link is not a line: passing a hand's breadth under the surface is not "
                + "clear just because the centre line is above it.");
            Assert.That(hit, Is.EqualTo("Bench"));
        }

        [Test]
        public void ArmAboveTheBenchIsClear()
        {
            SimulatedCollisionModel model = BenchModel();

            double[] points = [0.0, 0.0, 0.40, 0.30, 0.0, 0.40, 0.38, 0.0, 0.25];

            Assert.That(model.IsClear(points, out _), Is.True);
        }

        [Test]
        public void LinkThroughABinWallIsRejected()
        {
            SimulatedCollisionModel model = BenchModel();

            // Held clear of the bench, but crossing the east wall of the bin at a height
            // the wall occupies.
            double[] points = [0.38, 0.0, 0.045, 0.60, 0.0, 0.045, 0.60, 0.0, 0.20];

            Assert.That(model.IsClear(points, out string hit), Is.False);
            Assert.That(hit, Is.EqualTo("BinWallE"));
        }

        [Test]
        public void ToolMayDescendIntoTheBinBetweenItsWalls()
        {
            SimulatedCollisionModel model = BenchModel();

            // Wrist above the bin, tool point down inside it, where the parts lie.
            double[] points = [0.38, 0.0, 0.40, 0.38, 0.0, 0.22, 0.38, 0.0, 0.056];

            Assert.That(model.IsClear(points, out _), Is.True,
                "The bin's inside is open, or the cell could never pick anything out of it.");
        }

        [Test]
        public void ToolMayReachAPartLyingOnTheBench()
        {
            SimulatedCollisionModel model = BenchModel();

            // The tool is slender and is meant to approach surfaces: a gripper that cannot
            // come within a link's thickness of the bench cannot pick anything off it.
            double[] points = [-0.20, 0.0, 0.40, -0.20, 0.0, 0.22, -0.20, 0.0, 0.021];

            Assert.That(model.IsClear(points, out _), Is.True);
        }

        [Test]
        public void ModelWithoutObstaclesAllowsEverything()
        {
            var model = new SimulatedCollisionModel(ArrayOf<SimulatedObstacleBox>.Empty, 0.03, 0.008);

            double[] points = [0.0, 0.0, -1.0, 0.0, 0.0, -2.0];

            Assert.That(model.IsClear(points, out _), Is.True);
        }

        [Test]
        public void PerSegmentRadiusKeepsAThinWristClearWhereAThickLinkWouldCollide()
        {
            SimulatedObstacleBox[] obstacles =
            [
                new("NearbyPart", 0.45, 0.04, 0.10, 0.01, 0.19, 0.21)
            ];
            double[] points =
            [
                0.0, 0.0, 0.20,
                0.30, 0.0, 0.20,
                0.60, 0.0, 0.20
            ];
            var perSegment = new SimulatedCollisionModel(
                ArrayOf.Create(obstacles.AsSpan()),
                ArrayOf.Create([0.05, 0.01]));
            var uniformlyThick = new SimulatedCollisionModel(
                ArrayOf.Create(obstacles.AsSpan()),
                0.05,
                0.05);

            Assert.Multiple(() =>
            {
                Assert.That(perSegment.IsClear(points, out _), Is.True);
                Assert.That(uniformlyThick.IsClear(points, out string hit), Is.False);
                Assert.That(hit, Is.EqualTo("NearbyPart"));
            });
        }

        [Test]
        public void WorkpieceBoxAllowsSupportContactButRejectsVolumeOverlap()
        {
            var model = new SimulatedCollisionModel(
                ArrayOf.Create(
                [
                    new SimulatedObstacleBox("Support", 0.0, 0.0, 0.40, 0.40, -0.10, 0.0)
                ]),
                0.02,
                0.01);

            Assert.Multiple(() =>
            {
                Assert.That(
                    model.IsBoxClear(0.0, 0.0, 0.05, 0.10, 0.10, 0.10, out _),
                    Is.True,
                    "Touching a support is valid contact, not penetration.");
                Assert.That(
                    model.IsBoxClear(0.0, 0.0, 0.04, 0.10, 0.10, 0.10, out string hit),
                    Is.False);
                Assert.That(hit, Is.EqualTo("Support"));
            });
        }

        private static SimulatedCollisionModel BenchModel()
        {
            SimulatedObstacleBox[] obstacles =
            [
                new("Bench", 0.0, 0.0, 1.4000, 0.9000, -0.0690, 0.0000),
                new("BinWallN", 0.3800, 0.1170, 0.2800, 0.0060, -0.0070, 0.0330),
                new("BinWallS", 0.3800, -0.1170, 0.2800, 0.0060, -0.0070, 0.0330),
                new("BinWallE", 0.5170, 0.0000, 0.0060, 0.2400, -0.0070, 0.0330),
                new("BinWallW", 0.2430, 0.0000, 0.0060, 0.2400, -0.0070, 0.0330)
            ];
            return new SimulatedCollisionModel(ArrayOf.Create(obstacles.AsSpan()), 0.030, 0.008);
        }
    }
}
