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
using Opc.Ua.RobotIntent;
using Robotics.IntentEnabledRobot.Kinematics;
using Robotics.IntentEnabledRobot.Simulation;
using Vision.BinPickingCell;

namespace Opc.Ua.Robotics.Tests
{
    [TestFixture]
    [Category("Robotics")]
    [Parallelizable]
    public sealed class BinPickingPalletizerKinematicsTests
    {
        [Test]
        public void ZeroPoseKeepsToolDown()
        {
            var kinematics = new BinPickingPalletizerKinematics();

            SimulatedArmForwardPose pose = kinematics.Forward([0.0, 0.0, 0.0, 0.0]);
            ArrayOf<double> axis = PoseMath.RotateVector(pose.ToolPose.Orientation.Span, [1.0, 0.0, 0.0]);

            Assert.Multiple(() =>
            {
                Assert.That(pose.JointFramePoses.Count, Is.EqualTo(4));
                Assert.That(pose.ToolPose.Position[0],
                    Is.EqualTo(BinPickingPalletizerGeometry.MaximumReachMetres).Within(1e-9));
                Assert.That(pose.ToolPose.Position[2],
                    Is.EqualTo(
                        BinPickingPalletizerGeometry.ShoulderHeightMetres -
                        BinPickingPalletizerGeometry.FlangeToTcpMetres).Within(1e-9));
                Assert.That(axis[0], Is.Zero.Within(1e-9));
                Assert.That(axis[1], Is.Zero.Within(1e-9));
                Assert.That(axis[2], Is.EqualTo(-1.0).Within(1e-9));
            });
        }

        [Test]
        public void ExecutorStartsAtPalletizerInitialConfiguration()
        {
            var kinematics = new BinPickingPalletizerKinematics();
            var executor = new global::Robotics.IntentEnabledRobot.Simulation.SimulatedArmExecutor(
                kinematics);

            Assert.That(
                executor.CurrentSnapshot.JointAngles.Span.ToArray(),
                Is.EqualTo(kinematics.InitialJointAngles.Span.ToArray()));
            Assert.That(
                executor.CurrentSnapshot.ToolPose.Position.Span.ToArray(),
                Is.EqualTo(
                    kinematics.Forward(kinematics.InitialJointAngles.Span)
                        .ToolPose.Position.Span.ToArray()));
        }

        [TestCase(0.60, 0.00, -0.10, 0.0)]
        [TestCase(-0.60, 0.00, -0.08, 0.35)]
        [TestCase(0.55, -0.08, -0.16, -0.5)]
        public void InverseRoundTripsWorkPositions(
            double x,
            double y,
            double z,
            double toolRoll)
        {
            var kinematics = new BinPickingPalletizerKinematics();
            var target = new Pose3DDataType
            {
                FrameId = BinPickingPalletizerGeometry.RobotBaseFrameId,
                Position = new[] { x, y, z }.ToArrayOf(),
                Orientation = BinPickingPalletizerKinematics.ToolDownOrientation(
                    Math.Atan2(y, x),
                    toolRoll)
            };
            double[] reference = [Math.Atan2(y, x), 0.5, -1.0, toolRoll];

            bool solved = kinematics.TrySelectNearestConfiguration(
                target,
                reference,
                out SimulatedArmIkSolution? solution,
                out SimulatedArmKinematicFailure failure);

            Assert.That(solved, Is.True, failure.ToString());
            SimulatedArmForwardPose actual = kinematics.Forward(solution!.JointAngles.Span);
            Assert.Multiple(() =>
            {
                Assert.That(actual.ToolPose.Position[0], Is.EqualTo(x).Within(1e-6));
                Assert.That(actual.ToolPose.Position[1], Is.EqualTo(y).Within(1e-6));
                Assert.That(actual.ToolPose.Position[2], Is.EqualTo(z).Within(1e-6));
                Assert.That(
                    QuaternionEquivalent(
                        actual.ToolPose.Orientation.Span,
                        target.Orientation.Span),
                    Is.True);
            });
        }

        [Test]
        public void HeldObjectEnvelopeParticipatesInCollisionChecks()
        {
            var kinematics = new BinPickingPalletizerKinematics();
            Pose3DDataType tool = kinematics.Forward(kinematics.InitialJointAngles.Span).ToolPose;
            ReadOnlySpan<double> position = tool.Position.Span;
            kinematics.Collisions = new SimulatedCollisionModel(
                ArrayOf.Create(
                [
                    new SimulatedObstacleBox(
                        "HeldPathObstacle",
                        position[0],
                        position[1],
                        0.02,
                        0.02,
                        position[2] - 0.045,
                        position[2] - 0.025)
                ]),
                ArrayOf.Create([0.0, 0.047, 0.042, 0.018]));

            bool toolOnlyClear = kinematics.ClearsWorkSurface(
                kinematics.InitialJointAngles.Span);
            kinematics.SetHeldObjectEnvelope(0.04, 0.04, 0.04);
            bool heldObjectClear = kinematics.ClearsWorkSurface(
                kinematics.InitialJointAngles.Span);

            Assert.Multiple(() =>
            {
                Assert.That(toolOnlyClear, Is.True);
                Assert.That(heldObjectClear, Is.False);
            });
        }

        [Test]
        public void InverseReturnsTwoDistinctElbowBranches()
        {
            var kinematics = new BinPickingPalletizerKinematics();
            Pose3DDataType target = Target(0.60, 0.0, -0.10);

            SimulatedArmIkResult result = kinematics.Inverse(
                target,
                [0.0, 0.5, -1.0, 0.0]);

            Assert.That(result.Solutions.Count, Is.EqualTo(2));
            Assert.That(
                Math.Sign(result.Solutions[0].JointAngles[2]),
                Is.Not.EqualTo(Math.Sign(result.Solutions[1].JointAngles[2])));
        }

        [Test]
        public void SidewaysToolOrientationIsRefused()
        {
            var kinematics = new BinPickingPalletizerKinematics();
            var target = new Pose3DDataType
            {
                FrameId = BinPickingPalletizerGeometry.RobotBaseFrameId,
                Position = s_doubleValues1.ToArrayOf(),
                Orientation = s_doubleValues2.ToArrayOf()
            };

            SimulatedArmIkResult result = kinematics.Inverse(
                target,
                [0.0, 0.0, 0.0, 0.0]);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Failure, Is.EqualTo(SimulatedArmKinematicFailure.Kinematics));
            });
        }

        [Test]
        public void NearbyVerticalSamplesStayOnOneBranch()
        {
            var kinematics = new BinPickingPalletizerKinematics();
            bool initialSolved = kinematics.TrySelectNearestConfiguration(
                Target(0.60, 0.0, 0.12),
                [0.0, 0.4, -1.1, 0.0],
                out SimulatedArmIkSolution? initial,
                out SimulatedArmKinematicFailure initialFailure);
            Assert.That(initialSolved, Is.True, initialFailure.ToString());
            double[] reference = initial!.JointAngles.Span.ToArray();
            double previousElbowSign = Math.Sign(reference[2]);

            for (int step = 1; step <= 20; step++)
            {
                double z = 0.12 - (step * 0.012);
                bool solved = kinematics.TrySelectNearestConfiguration(
                    Target(0.60, 0.0, z),
                    reference,
                    out SimulatedArmIkSolution? solution,
                    out SimulatedArmKinematicFailure failure);

                Assert.That(solved, Is.True, $"step={step}, failure={failure}");
                double elbowSign = Math.Sign(solution!.JointAngles[2]);
                if (previousElbowSign != 0.0)
                {
                    Assert.That(elbowSign, Is.EqualTo(previousElbowSign));
                }
                Assert.That(MaxJointDelta(reference, solution.JointAngles.Span),
                    Is.LessThan(0.20));
                reference = solution.JointAngles.Span.ToArray();
                previousElbowSign = elbowSign;
            }
        }

        [Test]
        public void FixtureDescentHasAContinuousClearBranch()
        {
            var kinematics = new BinPickingPalletizerKinematics
            {
                MinimumLinkHeight =
                    BinPickingCellGeometry.BenchTopMetres -
                    BinPickingCellGeometry.RobotBaseHeightMetres,
                Collisions = BinPickingCellGeometry.CreateCollisionModel()
            };
            ArrayOf<double> orientation =
                BinPickingPalletizerKinematics.ToolDownOrientation(0.0, Math.PI / 2.0);
            Pose3DDataType transit = new()
            {
                FrameId = BinPickingPalletizerGeometry.RobotBaseFrameId,
                Position = new[] { -0.60, 0.0, 0.32 }.ToArrayOf(),
                Orientation = orientation
            };
            bool transitSolved = kinematics.TrySelectNearestConfiguration(
                transit,
                kinematics.InitialJointAngles.Span,
                out SimulatedArmIkSolution? transitSolution,
                out SimulatedArmKinematicFailure transitFailure);
            Assert.That(transitSolved, Is.True, transitFailure.ToString());
            double[] reference = transitSolution!.JointAngles.Span.ToArray();

            for (int step = 1; step <= 32; step++)
            {
                double z = 0.32 + ((-0.119 - 0.32) * step / 32.0);
                Pose3DDataType target = new()
                {
                    FrameId = BinPickingPalletizerGeometry.RobotBaseFrameId,
                    Position = new[] { -0.60, 0.0, z }.ToArrayOf(),
                    Orientation = orientation
                };
                bool solved = kinematics.TrySelectNearestConfiguration(
                    target,
                    reference,
                    out SimulatedArmIkSolution? solution,
                    out SimulatedArmKinematicFailure failure);
                Assert.That(solved, Is.True, $"step={step}, z={z:F4}, failure={failure}");
                Assert.That(MaxJointDelta(reference, solution!.JointAngles.Span), Is.LessThan(0.20));
                reference = solution.JointAngles.Span.ToArray();
            }
        }

        [Test]
        public void EveryBinPickingWorkPoseHasAClearSolution()
        {
            var kinematics = new BinPickingPalletizerKinematics
            {
                MinimumLinkHeight =
                    BinPickingCellGeometry.BenchTopMetres -
                    BinPickingCellGeometry.RobotBaseHeightMetres,
                Collisions = BinPickingCellGeometry.CreateCollisionModel()
            };
            var targets = new System.Collections.Generic.List<(string Name, double X, double Y, double Z)>
            {
                (
                    "Bin approach",
                    BinPickingPartsCatalog.BinCentreX,
                    0.0,
                    BinPickingCellGeometry.BenchTopMetres + 0.20 -
                    BinPickingCellGeometry.RobotBaseHeightMetres),
                (
                    "Fixture approach",
                    BinPickingCellGeometry.FixtureCentreX,
                    0.0,
                    BinPickingCellGeometry.FixturePlateTopMetres + 0.20 -
                    BinPickingCellGeometry.RobotBaseHeightMetres),
                ("Bin transit", BinPickingPartsCatalog.BinCentreX, 0.0, 0.32),
                ("Fixture transit", BinPickingCellGeometry.FixtureCentreX, 0.0, 0.32)
            };
            foreach (BinPickingPart part in BinPickingPartsCatalog.Parts)
            {
                targets.Add(
                    (
                        "Home " + part.ClassLabel,
                        part.InitialWorldPosition[0],
                        part.InitialWorldPosition[1],
                        part.InitialWorldPosition[2] +
                        global::Robotics.IntentEnabledRobot.Simulation.SimulatedArmExecutor.HeldPartTcpOffset -
                        BinPickingCellGeometry.RobotBaseHeightMetres));
            }

            foreach ((string name, double x, double y, double z) in targets)
            {
                double baseYaw = Math.Atan2(y, x);
                var target = new Pose3DDataType
                {
                    FrameId = BinPickingPalletizerGeometry.RobotBaseFrameId,
                    Position = new[] { x, y, z }.ToArrayOf(),
                    Orientation = BinPickingPalletizerKinematics.ToolDownOrientation(
                        baseYaw,
                        Math.PI / 2.0)
                };

                bool solved = kinematics.TrySelectNearestConfiguration(
                    target,
                    kinematics.InitialJointAngles.Span,
                    out _,
                    out SimulatedArmKinematicFailure failure);

                Assert.That(solved, Is.True, $"{name}: {failure}");
            }
        }

        [Test]
        public void OutsideWorkspaceIsRefused()
        {
            var kinematics = new BinPickingPalletizerKinematics();

            SimulatedArmIkResult result = kinematics.Inverse(
                Target(1.20, 0.0, 0.0),
                [0.0, 0.0, 0.0, 0.0]);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Failure, Is.EqualTo(SimulatedArmKinematicFailure.Unreachable));
            });
        }

        private static Pose3DDataType Target(double x, double y, double z)
        {
            return new Pose3DDataType
            {
                FrameId = BinPickingPalletizerGeometry.RobotBaseFrameId,
                Position = new[] { x, y, z }.ToArrayOf(),
                Orientation = BinPickingPalletizerKinematics.ToolDownOrientation(
                    Math.Atan2(y, x),
                    0.0)
            };
        }

        private static bool QuaternionEquivalent(
            ReadOnlySpan<double> left,
            ReadOnlySpan<double> right)
        {
            double dot =
                (left[0] * right[0]) +
                (left[1] * right[1]) +
                (left[2] * right[2]) +
                (left[3] * right[3]);
            return Math.Abs(Math.Abs(dot) - 1.0) <= 1e-6;
        }

        private static double MaxJointDelta(
            ReadOnlySpan<double> left,
            ReadOnlySpan<double> right)
        {
            double maximum = 0.0;
            for (int ii = 0; ii < left.Length; ii++)
            {
                maximum = Math.Max(maximum, Math.Abs(left[ii] - right[ii]));
            }
            return maximum;
        }

        private static readonly double[] s_doubleValues1 = [0.60, 0.0, 0.0];
        private static readonly double[] s_doubleValues2 = [0.0, 0.0, 0.0, 1.0];
    }
}
