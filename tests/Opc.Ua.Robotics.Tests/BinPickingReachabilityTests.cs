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
using System.Globalization;
using System.Text;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.RobotIntent;
using Robotics.IntentEnabledRobot.Kinematics;
using Robotics.IntentEnabledRobot.Simulation;
using Vision.BinPickingCell;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Measures how much room the solver actually has at each of the bin-picking cell's
    /// work positions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cell asks for one pose per Location and inherits the tool orientation from
    /// wherever the arm happens to be, and the solver is numerical: it iterates from a
    /// fixed seed list, caps the result at eight, and drops wrist-singular answers. Every
    /// filter after that - joint limits, the work surface, and the collision model - can
    /// only take candidates away. When the last one goes, a Pick or a Place fails as
    /// Unreachable and there is no planner to find a way round.
    /// </para>
    /// <para>
    /// These tests count what survives each stage, so "the arm stopped" becomes a number
    /// per position rather than a guess, and so the effect of widening the search can be
    /// measured rather than asserted.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("Robotics")]
    [Parallelizable]
    public sealed class BinPickingReachabilityTests
    {
        [Test]
        public void EveryWorkPositionHasAReachableConfigurationWithTheInheritedOrientation()
        {
            var report = new StringBuilder();
            var starved = new List<string>();
            foreach ((string name, double[] position) in WorkPositions())
            {
                ReachabilityCount count = Measure(position, HomeToolOrientation(), plane: true, collisions: false);
                report.Append(CultureInfo.InvariantCulture, $"{name,-22} {count}\n");
                if (count.ClearOfWorkSurface == 0)
                {
                    starved.Add(name);
                }
            }
            TestContext.Out.Write(report.ToString());

            Assert.That(starved, Is.Empty,
                "These positions have no configuration the arm can hold with the orientation it "
                + "inherits, so a Pick or Place aimed at them fails as Unreachable:\n" + report);
        }

        [Test]
        public void EveryWorkPositionHasAConfigurationThatClearsTheCellFurniture()
        {
            var report = new StringBuilder();
            var starved = new List<string>();
            foreach ((string name, double[] position) in WorkPositions())
            {
                ReachabilityCount count = Measure(position, HomeToolOrientation(), plane: true, collisions: true);
                report.Append(CultureInfo.InvariantCulture, $"{name,-22} {count}\n");
                if (count.ClearOfObstacles == 0)
                {
                    starved.Add(name);
                }
            }
            TestContext.Out.Write(report.ToString());

            Assert.That(starved, Is.Empty,
                "These positions have no configuration that clears the bench and the bin, which "
                + "is why the collision model cannot be switched on in the demo yet:\n" + report);
        }

        [Test]
        public void FreeingTheYawAboutTheToolAxisNeverCostsOptionsAndUsuallyAddsThem()
        {
            var report = new StringBuilder();
            var lost = new List<string>();
            int improved = 0;
            foreach ((string name, double[] position) in WorkPositions())
            {
                ReachabilityCount fixedYaw = Measure(position, HomeToolOrientation(), plane: true, collisions: true);
                int best = 0;
                foreach (double[] orientation in YawSpread())
                {
                    ReachabilityCount searched = Measure(position, orientation, plane: true, collisions: true);
                    best = Math.Max(best, searched.ClearOfObstacles);
                }
                report.Append(CultureInfo.InvariantCulture,
                    $"{name,-22} fixed={fixedYaw.ClearOfObstacles} bestOverYaw={best}\n");
                if (best < fixedYaw.ClearOfObstacles)
                {
                    lost.Add(name);
                }
                if (best > fixedYaw.ClearOfObstacles)
                {
                    improved++;
                }
            }
            TestContext.Out.Write(report.ToString());

            Assert.Multiple(() =>
            {
                Assert.That(lost, Is.Empty,
                    "Searching the yaw includes the orientation the arm already holds, so it "
                    + "cannot come out with fewer options than not searching:\n" + report);
                Assert.That(improved, Is.GreaterThan(0),
                    "The point of searching the yaw a parallel gripper is free to choose is that "
                    + "it finds configurations a single fixed orientation misses. If it never "
                    + "does, the search is not worth its complexity:\n" + report);
            });
        }

        [Test]
        public void StarvedHomeSlotShowsWhichJointIsInTheBench()
        {
            BinPickingPart part = BinPickingPartsCatalog.Parts[0];
            double[] position = Base(
                part.InitialWorldPosition[0],
                part.InitialWorldPosition[1],
                part.InitialWorldPosition[2] + HeldPartTcpOffset);
            var kinematics = new SimulatedArmKinematics();
            var target = new Pose3DDataType
            {
                FrameId = "robot_base",
                Position = position.ToArrayOf(),
                Orientation = HomeToolOrientation().ToArrayOf()
            };
            SimulatedArmIkResult result = kinematics.Inverse(target, s_homeJoints);

            var report = new StringBuilder();
            report.Append(CultureInfo.InvariantCulture,
                $"{part.ClassLabel} tool at z={position[2]:F3} in the base frame\n");
            ReadOnlySpan<SimulatedArmIkSolution> candidates = result.Solutions.Span;
            for (int ii = 0; ii < candidates.Length; ii++)
            {
                ReadOnlySpan<double> angles = candidates[ii].JointAngles.Span;
                SimulatedArmForwardPose pose = kinematics.Forward(angles);
                ReadOnlySpan<Pose3DDataType> frames = pose.JointFramePoses.Span;
                report.Append(CultureInfo.InvariantCulture, $"  candidate {ii}: joint z =");
                for (int jj = 0; jj < frames.Length; jj++)
                {
                    report.Append(CultureInfo.InvariantCulture, $" J{jj + 1}={frames[jj].Position.Span[2]:F3}");
                }
                report.Append(CultureInfo.InvariantCulture, $" tcp={pose.ToolPose.Position.Span[2]:F3}\n");
            }
            TestContext.Out.Write(report.ToString());

            Assert.That(result.Solutions.Count, Is.GreaterThan(0),
                "The starved position must at least have geometric solutions to inspect.");
        }

        /// <summary>
        /// Counts how many inverse-kinematic candidates survive each successive filter.
        /// </summary>
        private static ReachabilityCount Measure(
            double[] position,
            double[] orientation,
            bool plane,
            bool collisions)
        {
            var kinematics = new SimulatedArmKinematics();
            var target = new Pose3DDataType
            {
                FrameId = "robot_base",
                Position = position.ToArrayOf(),
                Orientation = orientation.ToArrayOf()
            };
            SimulatedArmIkResult result = kinematics.Inverse(target, s_homeJoints);
            int found = result.Solutions.Count;
            int withinLimits = 0;
            int clearOfSurface = 0;
            int clearOfObstacles = 0;

            var surfaceOnly = new SimulatedArmKinematics { MinimumLinkHeight = plane ? 0.0 : double.NegativeInfinity };
            var withFurniture = new SimulatedArmKinematics
            {
                MinimumLinkHeight = plane ? 0.0 : double.NegativeInfinity,
                Collisions = collisions ? BinPickingCellGeometry.CreateCollisionModel() : null
            };

            ReadOnlySpan<SimulatedArmIkSolution> candidates = result.Solutions.Span;
            string blockedBy = string.Empty;
            for (int ii = 0; ii < candidates.Length; ii++)
            {
                ReadOnlySpan<double> angles = candidates[ii].JointAngles.Span;
                if (!kinematics.IsWithinLimits(angles))
                {
                    continue;
                }
                withinLimits++;
                if (surfaceOnly.ClearsWorkSurface(angles))
                {
                    clearOfSurface++;
                }
                if (withFurniture.ClearsWorkSurface(angles))
                {
                    clearOfObstacles++;
                }
                else if (collisions && blockedBy.Length == 0)
                {
                    blockedBy = FirstObstacleHit(withFurniture, angles);
                }
            }
            return new ReachabilityCount(found, withinLimits, clearOfSurface, clearOfObstacles, blockedBy);
        }

        /// <summary>
        /// Names the first solid a configuration reaches into, so a starved position says
        /// what is in its way rather than only that something is.
        /// </summary>
        private static string FirstObstacleHit(SimulatedArmKinematics kinematics, ReadOnlySpan<double> jointAngles)
        {
            SimulatedCollisionModel? collisions = kinematics.Collisions;
            if (collisions == null)
            {
                return string.Empty;
            }
            SimulatedArmForwardPose pose = kinematics.Forward(jointAngles);
            ReadOnlySpan<Pose3DDataType> frames = pose.JointFramePoses.Span;
            double[] points = new double[(frames.Length + 1) * 3];
            for (int ii = 0; ii < frames.Length; ii++)
            {
                ReadOnlySpan<double> position = frames[ii].Position.Span;
                points[(ii * 3) + 0] = position[0];
                points[(ii * 3) + 1] = position[1];
                points[(ii * 3) + 2] = position[2];
            }
            ReadOnlySpan<double> tool = pose.ToolPose.Position.Span;
            points[(frames.Length * 3) + 0] = tool[0];
            points[(frames.Length * 3) + 1] = tool[1];
            points[(frames.Length * 3) + 2] = tool[2];
            _ = collisions.IsClear(points, out string hit);
            return hit;
        }

        /// <summary>
        /// The positions the cell actually sends the tool to, in the arm's base frame: the
        /// bin and the fixture at approach height, and every part's own home slot.
        /// </summary>
        private static IEnumerable<(string Name, double[] Position)> WorkPositions()
        {
            yield return ("Bin approach", Base(0.38, 0.0, BenchTop + ApproachHeight));
            yield return ("Fixture approach", Base(-0.32, 0.0, FixturePlateTop + ApproachHeight));
            yield return ("Bin transit", Base(0.38, 0.0, BenchTop + TransitHeight));
            yield return ("Fixture transit", Base(-0.32, 0.0, FixturePlateTop + TransitHeight));
            foreach (BinPickingPart part in BinPickingPartsCatalog.Parts)
            {
                // A Place descends to leave the part resting on the bench, so the tool ends
                // up one held-part offset above where the part comes to rest.
                yield return (
                    "Home " + part.ClassLabel,
                    Base(
                        part.InitialWorldPosition[0],
                        part.InitialWorldPosition[1],
                        part.InitialWorldPosition[2] + HeldPartTcpOffset));
            }
        }

        /// <summary>
        /// Converts a world position to the arm's base frame, whose origin sits on the
        /// bench top.
        /// </summary>
        private static double[] Base(double worldX, double worldY, double worldZ)
        {
            return [worldX, worldY, worldZ - RobotBaseHeight];
        }

        /// <summary>
        /// The tool orientation the arm holds at home, which is what every Pick and Place
        /// inherits today.
        /// </summary>
        private static double[] HomeToolOrientation()
        {
            SimulatedArmForwardPose pose = new SimulatedArmKinematics().Forward(s_homeJoints);
            return [.. pose.ToolPose.Orientation.Span];
        }

        /// <summary>
        /// The home orientation turned about the world vertical in fixed steps. A parallel
        /// gripper approaching straight down is free to choose this angle.
        /// </summary>
        private static IEnumerable<double[]> YawSpread()
        {
            double[] home = HomeToolOrientation();
            for (int degrees = 0; degrees < 360; degrees += 15)
            {
                double half = degrees * Math.PI / 360.0;
                double sin = Math.Sin(half);
                double cos = Math.Cos(half);

                // Quaternion product of a rotation about world Z with the home orientation.
                double x = (cos * home[0]) - (sin * home[1]);
                double y = (cos * home[1]) + (sin * home[0]);
                double z = (cos * home[2]) + (sin * home[3]);
                double w = (cos * home[3]) - (sin * home[2]);
                yield return [x, y, z, w];
            }
        }

        /// <summary>
        /// How many candidates survive each filter, for one position.
        /// </summary>
        private readonly record struct ReachabilityCount(
            int Found,
            int WithinLimits,
            int ClearOfWorkSurface,
            int ClearOfObstacles,
            string BlockedBy)
        {
            public override string ToString()
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"found={Found} limits={WithinLimits} surface={ClearOfWorkSurface} "
                    + $"furniture={ClearOfObstacles} blockedBy={(BlockedBy.Length == 0 ? "-" : BlockedBy)}");
            }
        }

        // The cell's own numbers, from BinPickingRobotCell and Assets/Cell.usda.
        private const double RobotBaseHeight = 0.829;
        private const double BenchTop = 0.829;
        private const double FixturePlateTop = 0.838;
        private const double ApproachHeight = 0.20;
        private const double TransitHeight = 0.32;
        private const double HeldPartTcpOffset = 0.035;

        // The configuration the arm starts in, from SimulatedArmExecutor.
        private static readonly double[] s_homeJoints =
            [-2.9594, 1.8674, -1.6455, 2.9210, -2.6965, -1.5697];
    }
}
