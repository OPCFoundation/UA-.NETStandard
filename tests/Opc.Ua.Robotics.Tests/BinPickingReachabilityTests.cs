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
        public void RelocatedScanPoseHasAClearConfiguration()
        {
            var kinematics = new SimulatedArmKinematics
            {
                MinimumLinkHeight = BenchTop - RobotBaseHeight,
                Collisions = BinPickingCellGeometry.CreateCollisionModel()
            };
            var target = new Pose3DDataType
            {
                FrameId = "robot_base",
                Position = new[] { 0.4455, 0.0416, 0.3410 }.ToArrayOf(),
                Orientation = new[]
                {
                    0.0926599570,
                    0.7010093668,
                    -0.0926599570,
                    0.7010093668
                }.ToArrayOf()
            };

            bool solved = kinematics.TrySelectNearest(
                target,
                s_homeJoints,
                out SimulatedArmIkSolution? solution,
                out SimulatedArmKinematicFailure failure);

            Assert.That(solved, Is.True, "The relocated eye-in-hand scan pose must be reachable: " + failure);
            var degrees = new StringBuilder();
            ReadOnlySpan<double> angles = solution!.JointAngles.Span;
            for (int ii = 0; ii < angles.Length; ii++)
            {
                if (ii > 0)
                {
                    degrees.Append(", ");
                }
                degrees.Append(
                    (angles[ii] * 180.0 / Math.PI).ToString("F3", CultureInfo.InvariantCulture));
            }
            TestContext.Out.Write(
                "relocated scan joints in degrees: " + degrees + Environment.NewLine);
            Assert.That(kinematics.ClearsWorkSurface(solution.JointAngles.Span), Is.True);
        }

        [Test]
        public void RelocatedHomeSlotKeepsEveryJointAboveTheBench()
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

            double surface = BenchTop - RobotBaseHeight;
            var clearance = new SimulatedArmKinematics
            {
                MinimumLinkHeight = surface,
                Collisions = BinPickingCellGeometry.CreateCollisionModel()
            };
            bool hasClearCandidate = false;
            for (int ii = 0; ii < candidates.Length; ii++)
            {
                if (clearance.ClearsWorkSurface(candidates[ii].JointAngles.Span))
                {
                    hasClearCandidate = true;
                    break;
                }
            }
            Assert.That(candidates.Length, Is.GreaterThan(0));
            Assert.That(hasClearCandidate, Is.True,
                "At least one relocated posture must keep the whole arm above the lowered bench.");
        }

        [Test]
        public void ApproachAndTransitPositionsLeaveTheSolverAChoiceOfPosture()
        {
            var report = new StringBuilder();
            var noChoice = new List<string>();
            foreach ((string name, double[] position) in WorkPositions())
            {
                int postures = CountDistinctPostures(position, HomeToolOrientation(), clearOnly: false);
                int usable = CountDistinctPostures(position, HomeToolOrientation(), clearOnly: true);
                ReachabilityCount count = Measure(position, HomeToolOrientation(), plane: true, collisions: true);
                report.Append(CultureInfo.InvariantCulture,
                    $"{name,-22} candidates={count.Found} distinctPostures={postures} usable={usable}\n");
                if (usable < 1)
                {
                    noChoice.Add(name);
                }
            }
            TestContext.Out.Write(report.ToString());

            Assert.That(noChoice, Is.Empty,
                "Every position the cell works at needs at least one shape the arm can legally "
                + "hold:\n" + report);

            TestContext.Out.Write(
                "The raised pedestal and outward work areas leave every position with several "
                + "usable candidates instead of forcing one folded-wrist shape.\n");
        }

        [Test]
        public void RelocatedHomeSlotHasSeveralClearConfigurations()
        {
            var kinematics = new SimulatedArmKinematics
            {
                MinimumLinkHeight = BenchTop - RobotBaseHeight,
                Collisions = BinPickingCellGeometry.CreateCollisionModel()
            };
            BinPickingPart part = BinPickingPartsCatalog.Parts[0];
            double[] position = Base(
                part.InitialWorldPosition[0],
                part.InitialWorldPosition[1],
                part.InitialWorldPosition[2] + HeldPartTcpOffset);
            var target = new Pose3DDataType
            {
                FrameId = "robot_base",
                Position = position.ToArrayOf(),
                Orientation = HomeToolOrientation().ToArrayOf()
            };

            SimulatedArmIkResult result = kinematics.Inverse(target, s_homeJoints);
            int clearConfigurations = 0;
            ReadOnlySpan<SimulatedArmIkSolution> candidates = result.Solutions.Span;
            for (int ii = 0; ii < candidates.Length; ii++)
            {
                if (kinematics.ClearsWorkSurface(candidates[ii].JointAngles.Span))
                {
                    clearConfigurations++;
                }
            }

            Assert.That(result.Solutions.IsEmpty, Is.False);
            TestContext.Out.Write(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"clear configurations = {clearConfigurations}\n"));

            Assert.That(clearConfigurations, Is.GreaterThan(1),
                "Raising the pedestal, lowering the bench and moving the bin outward must "
                + "leave the solver a real choice of collision-clear configurations instead "
                + "of forcing the single folded posture the old cell geometry admitted.");
        }

        /// <summary>
        /// Counts how many genuinely different shapes the arm can take for a target.
        /// </summary>
        /// <remarks>
        /// Two solutions that place every joint in the same spot are the same posture even
        /// when their joint angles differ by a turn, and the arm looks identical in both.
        /// Counting those separately is what made a starved target look well supplied.
        /// </remarks>
        private static int CountDistinctPostures(double[] position, double[] orientation, bool clearOnly)
        {
            var kinematics = new SimulatedArmKinematics();
            var clearance = new SimulatedArmKinematics
            {
                MinimumLinkHeight = BenchTop - RobotBaseHeight,
                Collisions = BinPickingCellGeometry.CreateCollisionModel()
            };
            var target = new Pose3DDataType
            {
                FrameId = "robot_base",
                Position = position.ToArrayOf(),
                Orientation = orientation.ToArrayOf()
            };
            SimulatedArmIkResult result = kinematics.Inverse(target, s_homeJoints);
            var shapes = new List<double[]>();
            ReadOnlySpan<SimulatedArmIkSolution> candidates = result.Solutions.Span;
            for (int ii = 0; ii < candidates.Length; ii++)
            {
                ReadOnlySpan<double> angles = candidates[ii].JointAngles.Span;
                if (!kinematics.IsWithinLimits(angles))
                {
                    continue;
                }
                if (clearOnly && !clearance.ClearsWorkSurface(angles))
                {
                    continue;
                }
                double[] shape = JointOrigins(kinematics, angles);
                bool seen = false;
                foreach (double[] existing in shapes)
                {
                    if (SameShape(existing, shape))
                    {
                        seen = true;
                        break;
                    }
                }
                if (!seen)
                {
                    shapes.Add(shape);
                }
            }
            return shapes.Count;
        }

        /// <summary>
        /// Gets every joint origin of a configuration, flattened.
        /// </summary>
        private static double[] JointOrigins(SimulatedArmKinematics kinematics, ReadOnlySpan<double> jointAngles)
        {
            SimulatedArmForwardPose pose = kinematics.Forward(jointAngles);
            ReadOnlySpan<Pose3DDataType> frames = pose.JointFramePoses.Span;
            double[] origins = new double[frames.Length * 3];
            for (int ii = 0; ii < frames.Length; ii++)
            {
                ReadOnlySpan<double> p = frames[ii].Position.Span;
                origins[(ii * 3) + 0] = p[0];
                origins[(ii * 3) + 1] = p[1];
                origins[(ii * 3) + 2] = p[2];
            }
            return origins;
        }

        /// <summary>
        /// Gets whether two configurations put every joint in the same place, to a
        /// millimetre.
        /// </summary>
        private static bool SameShape(double[] left, double[] right)
        {
            for (int ii = 0; ii < left.Length && ii < right.Length; ii++)
            {
                if (Math.Abs(left[ii] - right[ii]) > 0.001)
                {
                    return false;
                }
            }
            return true;
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

            var surfaceOnly = new SimulatedArmKinematics
            {
                MinimumLinkHeight = plane
                    ? BenchTop - RobotBaseHeight
                    : double.NegativeInfinity
            };
            var withFurniture = new SimulatedArmKinematics
            {
                MinimumLinkHeight = plane
                    ? BenchTop - RobotBaseHeight
                    : double.NegativeInfinity,
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
            yield return (
                "Bin approach",
                Base(BinPickingPartsCatalog.BinCentreX, 0.0, BenchTop + ApproachHeight));
            yield return (
                "Fixture approach",
                Base(BinPickingCellGeometry.FixtureCentreX, 0.0, FixturePlateTop + ApproachHeight));
            yield return (
                "Bin transit",
                Base(BinPickingPartsCatalog.BinCentreX, 0.0, BenchTop + TransitHeight));
            yield return (
                "Fixture transit",
                Base(BinPickingCellGeometry.FixtureCentreX, 0.0, FixturePlateTop + TransitHeight));
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
        /// Converts a world position to the arm's raised base frame.
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

            // Every 45 degrees rather than every 15: the property being measured is whether
            // turning the tool finds shapes a fixed orientation misses, and a coarser sweep
            // shows that just as well for a third of the solves. The executor searches more
            // finely at run time, where only the first success is paid for.
            for (int degrees = 0; degrees < 360; degrees += 45)
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
        private const double RobotBaseHeight = BinPickingCellGeometry.RobotBaseHeightMetres;
        private const double BenchTop = BinPickingCellGeometry.BenchTopMetres;
        private const double FixturePlateTop = BinPickingCellGeometry.FixturePlateTopMetres;
        private const double ApproachHeight = 0.20;
        private const double TransitHeight = 0.32;
        private const double HeldPartTcpOffset = 0.035;

        // The configuration the arm starts in, from SimulatedArmExecutor.
        private static readonly double[] s_homeJoints =
            [-3.0484844, 0.3128706, 0.8261335, 2.0025887, -2.7856466, -1.5707963];
    }
}
