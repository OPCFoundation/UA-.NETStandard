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
using Opc.Ua.RobotIntent;
using Robotics.IntentEnabledRobot.Kinematics;

namespace Opc.Ua.Robotics.Tests
{
    [TestFixture]
    public sealed class SimulatedArmKinematicsTests
    {
        [Test]
        public void ForwardThenInverseRoundTripsAcrossSampledJointSweep()
        {
            var kinematics = new SimulatedArmKinematics();
            foreach (double[] vector in s_roundTripJoints)
            {
                var joints = ArrayOf.Create(vector.AsSpan());
                Pose3DDataType pose = kinematics.Forward(joints).ToolPose;
                SimulatedArmIkResult result = kinematics.Inverse(pose, joints);

                Assert.Multiple(() =>
                {
                    Assert.That(result.Failure, Is.EqualTo(SimulatedArmKinematicFailure.None));
                    Assert.That(ContainsJointVector(result, joints, 2e-3), Is.True, string.Join(",", vector));
                });
            }
        }


        [Test]
        public void ForwardMatchesHandComputedLinkLengthOracles()
        {
            var kinematics = new SimulatedArmKinematics();
            double half = Math.Sqrt(0.5);

            SimulatedArmForwardPose home = kinematics.Forward(ArrayOf.Create([0.0, 0.0, 0.0, 0.0, 0.0, 0.0]));
            SimulatedArmForwardPose shoulderYaw90 = kinematics.Forward(
                ArrayOf.Create([Math.PI / 2.0, 0.0, 0.0, 0.0, 0.0, 0.0]));
            SimulatedArmForwardPose shoulderPitch90 = kinematics.Forward(
                ArrayOf.Create([0.0, Math.PI / 2.0, 0.0, 0.0, 0.0, 0.0]));

            Assert.Multiple(() =>
            {
                // Home: all rotations are identity, so x=A2+A3+TCP=-0.6522 and
                // z=D1+D4+D5+D6=0.4951.
                AssertPose(home.ToolPose, [-0.6522, 0.0, 0.4951], [0.0, 0.0, 0.0, 1.0], 1e-12);

                // Joint 0 is a +90 degree Z rotation, carrying home x into +Y while leaving z unchanged.
                AssertPose(shoulderYaw90.ToolPose, [0.0, -0.6522, 0.4951], [0.0, 0.0, half, half], 1e-12);

                // Joint 1 is a +90 degree Y rotation after D1. The remaining local vector is
                // (A2+A3+TCP, 0, D4+D5+D6), so world x is D4+D5+D6 and world z is
                // D1-(A2+A3+TCP).
                AssertPose(
                    shoulderPitch90.ToolPose,
                    [0.3326, 0.0, 0.8147],
                    [0.0, half, 0.0, half],
                    1e-12);
            });
        }

        [Test]
        public void GeneralReachablePoseReturnsExpectedSolutionsAndEverySolutionMatchesThePose()
        {
            var kinematics = new SimulatedArmKinematics();
            var joints = ArrayOf.Create(s_roundTripJoints[0].AsSpan());
            Pose3DDataType pose = kinematics.Forward(joints).ToolPose;
            SimulatedArmIkResult result = kinematics.Inverse(pose, joints);

            Assert.Multiple(() =>
            {
                Assert.That(result.Solutions.Count, Is.EqualTo(8));
                foreach (SimulatedArmIkSolution solution in result.Solutions)
                {
                    Pose3DDataType check = kinematics.Forward(solution.JointAngles).ToolPose;
                    Assert.That(PositionDistance(check, pose), Is.LessThan(2e-5));
                    Assert.That(QuaternionDistance(check.Orientation.Span, pose.Orientation.Span), Is.LessThan(2e-5));
                }
            });
        }

        [Test]
        public void JointLimitFilteringRemovesOnlyOutOfLimitSolutions()
        {
            var defaultKinematics = new SimulatedArmKinematics();
            var reference = ArrayOf.Create(s_roundTripJoints[0].AsSpan());
            Pose3DDataType pose = defaultKinematics.Forward(reference).ToolPose;
            SimulatedArmIkResult unfiltered = defaultKinematics.Inverse(pose, reference);
            var minimum = ArrayOf.Create([-0.45, -1.05, 1.25, -1.25, 0.65, 0.15]);
            var maximum = ArrayOf.Create([-0.35, -0.95, 1.35, -1.15, 0.75, 0.25]);
            var limitedKinematics = new SimulatedArmKinematics(minimum, maximum);
            SimulatedArmIkResult limited = limitedKinematics.Inverse(pose, reference);
            int expected = CountWithin(unfiltered, minimum.Span, maximum.Span);

            Assert.Multiple(() =>
            {
                Assert.That(limited.Failure, Is.EqualTo(SimulatedArmKinematicFailure.None));
                Assert.That(limited.Solutions.Count, Is.EqualTo(expected));
                Assert.That(AllWithin(limited, minimum.Span, maximum.Span), Is.True);
            });
        }

        [Test]
        public void NearestSolutionMinimisesWeightedJointTravel()
        {
            var kinematics = new SimulatedArmKinematics();
            var neutral = ArrayOf.Create([0.0, -1.0, 1.4, -1.1, 0.8, 0.0]);
            Pose3DDataType pose = kinematics.Forward(neutral).ToolPose;
            SimulatedArmIkResult neutralResult = kinematics.Inverse(
                pose, ArrayOf.Create([0.0, 0.0, 0.0, 0.0, 0.8, 0.0]));
            Assume.That(neutralResult.Solutions.Count, Is.GreaterThan(1));
            ArrayOf<double> current = neutralResult.Solutions[^1].JointAngles;

            bool selected = kinematics.TrySelectNearest(
                pose,
                current.Span,
                out SimulatedArmIkSolution? solution,
                out SimulatedArmKinematicFailure failure);
            double best = MinimumTravel(neutralResult, current.Span);

            Assert.Multiple(() =>
            {
                Assert.That(selected, Is.True);
                Assert.That(failure, Is.EqualTo(SimulatedArmKinematicFailure.None));
                Assert.That(Travel(solution!.JointAngles.Span, current.Span), Is.EqualTo(best).Within(1e-9));
                Assert.That(
                    MaxJointDelta(solution.JointAngles.Span, neutralResult.Solutions[0].JointAngles.Span),
                    Is.GreaterThan(1e-3));
            });
        }

        [Test]
        public void FailureReasonsMapToRobotIntentFailures()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SimulatedArmKinematics.ToIntentFailure(SimulatedArmKinematicFailure.Unreachable),
                    Is.EqualTo(IntentFailureEnum.Unreachable));
                Assert.That(SimulatedArmKinematics.ToIntentFailure(SimulatedArmKinematicFailure.Kinematics),
                    Is.EqualTo(IntentFailureEnum.Kinematics));
                Assert.That(SimulatedArmKinematics.ToIntentFailure(SimulatedArmKinematicFailure.JointLimit),
                    Is.EqualTo(IntentFailureEnum.JointLimit));
            });
        }

        [Test]
        public void InverseClassifiesUnreachableSingularAndJointLimitTargets()
        {
            var kinematics = new SimulatedArmKinematics();
            Pose3DDataType unreachable = Pose(2.0, 0.0, 0.3);
            Pose3DDataType singular = kinematics.Forward(ArrayOf.Create([-0.4, -1.0, 1.3, -1.2, 0.0, 0.2])).ToolPose;
            Pose3DDataType limitedPose = kinematics.Forward(ArrayOf.Create(s_roundTripJoints[0].AsSpan())).ToolPose;
            var limited = new SimulatedArmKinematics(
                ArrayOf.Create([0.2, 0.2, 0.2, 0.2, 0.2, 0.2]),
                ArrayOf.Create([0.3, 0.3, 0.3, 0.3, 0.3, 0.3]));

            Assert.Multiple(() =>
            {
                Assert.That(kinematics.Inverse(unreachable, ArrayOf.Create(s_roundTripJoints[0].AsSpan())).Failure,
                    Is.EqualTo(SimulatedArmKinematicFailure.Unreachable));
                Assert.That(kinematics.Inverse(singular, ArrayOf.Create(s_roundTripJoints[0].AsSpan())).Failure,
                    Is.EqualTo(SimulatedArmKinematicFailure.Kinematics));
                Assert.That(limited.Inverse(limitedPose, ArrayOf.Create(s_roundTripJoints[0].AsSpan())).Failure,
                    Is.EqualTo(SimulatedArmKinematicFailure.JointLimit));
            });
        }

        [Test]
        public void SlerpIsUnitExactAtEndpointsAndTakesShortestPath()
        {
            var kinematics = new SimulatedArmKinematics();
            Pose3DDataType start = Pose(0.2, 0.1, 0.5, [0.0, 0.0, 0.0, 1.0]);
            Pose3DDataType end = Pose(
                0.4, -0.1, 0.6, [0.0, 0.0, Math.Sin(3.0 * Math.PI / 4.0), Math.Cos(3.0 * Math.PI / 4.0)]);
            double previousAngle = 0.0;

            Assert.Multiple(() =>
            {
                for (int i = 0; i <= 20; i++)
                {
                    double fraction = i / 20.0;
                    Pose3DDataType sample = kinematics.InterpolateCartesian(start, end, fraction);
                    Assert.That(Norm(sample.Orientation.Span), Is.EqualTo(1.0).Within(1e-12));
                    double angle = 2.0 * Math.Atan2(Math.Abs(sample.Orientation[2]), Math.Abs(sample.Orientation[3]));
                    Assert.That(angle, Is.GreaterThanOrEqualTo(previousAngle - 1e-12));
                    Assert.That(angle, Is.LessThanOrEqualTo((Math.PI / 2.0) + 1e-12));
                    previousAngle = angle;
                }
                Assert.That(kinematics.InterpolateCartesian(start, end, 0.0).Position, Is.EqualTo(start.Position));
                Assert.That(kinematics.InterpolateCartesian(start, end, 1.0).Position, Is.EqualTo(end.Position));
            });
        }

        [Test]
        public void VelocityProfileHandlesTrapezoidalAndTriangularMovesExactly()
        {
            AssertProfile(0.3, 0.2, 0.4);
            AssertProfile(0.01, 0.2, 0.4);
        }

        [Test]
        public void JointAndCartesianInterpolationProduceDifferentToolPaths()
        {
            var kinematics = new SimulatedArmKinematics();
            var startJoints = ArrayOf.Create(s_roundTripJoints[0].AsSpan());
            var endJoints = ArrayOf.Create(s_roundTripJoints[3].AsSpan());
            Pose3DDataType startPose = kinematics.Forward(startJoints).ToolPose;
            Pose3DDataType endPose = kinematics.Forward(endJoints).ToolPose;
            Pose3DDataType cartesianMidpoint = kinematics.InterpolateCartesian(startPose, endPose, 0.5);
            Pose3DDataType jointMidpoint = kinematics.Forward(
                kinematics.InterpolateJoints(startJoints.Span, endJoints.Span, 0.5)).ToolPose;

            Assert.That(PositionDistance(cartesianMidpoint, jointMidpoint), Is.GreaterThan(0.01));
        }


        private static void AssertPose(
            Pose3DDataType actual,
            double[] expectedPosition,
            double[] expectedOrientation,
            double tolerance)
        {
            for (int i = 0; i < expectedPosition.Length; i++)
            {
                Assert.That(actual.Position[i], Is.EqualTo(expectedPosition[i]).Within(tolerance));
            }
            Assert.That(QuaternionDistance(actual.Orientation.Span, expectedOrientation), Is.LessThan(tolerance));
        }

        private static void AssertProfile(double distance, double speed, double acceleration)
        {
            var profile = new TrapezoidalVelocityProfile(distance, speed, acceleration);
            double maxVelocity = 0.0;
            double maxAcceleration = 0.0;
            double previousVelocity = profile.VelocityAt(0.0);
            double previousTime = 0.0;
            for (int i = 1; i <= 200; i++)
            {
                double time = profile.Duration * i / 200.0;
                double velocity = profile.VelocityAt(time);
                maxVelocity = Math.Max(maxVelocity, velocity);
                maxAcceleration = Math.Max(
                    maxAcceleration,
                    Math.Abs((velocity - previousVelocity) / (time - previousTime)));
                previousVelocity = velocity;
                previousTime = time;
            }

            Assert.Multiple(() =>
            {
                Assert.That(maxVelocity, Is.LessThanOrEqualTo(speed + 1e-12));
                Assert.That(maxAcceleration, Is.LessThanOrEqualTo(acceleration + 1e-9));
                Assert.That(profile.PositionAt(profile.Duration), Is.EqualTo(distance).Within(1e-12));
            });
        }

        private static bool ContainsJointVector(SimulatedArmIkResult result, ArrayOf<double> expected, double tolerance)
        {
            for (int i = 0; i < result.Solutions.Count; i++)
            {
                if (MaxJointDelta(result.Solutions[i].JointAngles.Span, expected.Span) <= tolerance)
                {
                    return true;
                }
            }
            return false;
        }

        private static int CountWithin(
            SimulatedArmIkResult result,
            ReadOnlySpan<double> minimum,
            ReadOnlySpan<double> maximum)
        {
            int count = 0;
            for (int i = 0; i < result.Solutions.Count; i++)
            {
                if (Within(result.Solutions[i].JointAngles.Span, minimum, maximum))
                {
                    count++;
                }
            }
            return count;
        }

        private static bool AllWithin(
            SimulatedArmIkResult result,
            ReadOnlySpan<double> minimum,
            ReadOnlySpan<double> maximum)
        {
            for (int i = 0; i < result.Solutions.Count; i++)
            {
                if (!Within(result.Solutions[i].JointAngles.Span, minimum, maximum))
                {
                    return false;
                }
            }
            return true;
        }

        private static double MinimumTravel(SimulatedArmIkResult result, ReadOnlySpan<double> current)
        {
            double best = double.PositiveInfinity;
            for (int i = 0; i < result.Solutions.Count; i++)
            {
                best = Math.Min(best, Travel(result.Solutions[i].JointAngles.Span, current));
            }
            return best;
        }

        private static bool Within(
            ReadOnlySpan<double> joints,
            ReadOnlySpan<double> minimum,
            ReadOnlySpan<double> maximum)
        {
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] < minimum[i] || joints[i] > maximum[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static double Travel(ReadOnlySpan<double> candidate, ReadOnlySpan<double> reference)
        {
            double sum = 0.0;
            for (int i = 0; i < candidate.Length; i++)
            {
                double delta = candidate[i] - reference[i];
                sum += delta * delta;
            }
            return sum;
        }

        private static double MaxJointDelta(ReadOnlySpan<double> left, ReadOnlySpan<double> right)
        {
            double max = 0.0;
            for (int i = 0; i < left.Length; i++)
            {
                max = Math.Max(max, Math.Abs(left[i] - right[i]));
            }
            return max;
        }

        private static double PositionDistance(Pose3DDataType left, Pose3DDataType right)
        {
            return Math.Sqrt(
                Math.Pow(left.Position[0] - right.Position[0], 2.0) +
                Math.Pow(left.Position[1] - right.Position[1], 2.0) +
                Math.Pow(left.Position[2] - right.Position[2], 2.0));
        }

        private static double QuaternionDistance(ReadOnlySpan<double> left, ReadOnlySpan<double> right)
        {
            double dot = Math.Abs(
                (left[0] * right[0]) + (left[1] * right[1]) + (left[2] * right[2]) + (left[3] * right[3]));
            return 1.0 - Math.Min(1.0, dot);
        }

        private static Pose3DDataType Pose(double x, double y, double z)
        {
            return Pose(x, y, z, [0.0, 0.0, 0.0, 1.0]);
        }

        private static Pose3DDataType Pose(double x, double y, double z, double[] orientation)
        {
            return new Pose3DDataType
            {
                FrameId = "base",
                Position = ArrayOf.Create([x, y, z]),
                Orientation = ArrayOf.Create(orientation.AsSpan())
            };
        }

        private static double Norm(ReadOnlySpan<double> values)
        {
            double sum = 0.0;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i] * values[i];
            }
            return Math.Sqrt(sum);
        }

        private static readonly double[][] s_roundTripJoints =
        [
            [-0.4, -1.0, 1.3, -1.2, 0.7, 0.2],
            [0.2, -1.25, 1.6, -1.1, 0.9, -0.35],
            [-1.0, -0.8, 1.1, -1.4, -0.8, 0.5],
            [0.8, -1.4, 1.8, -0.9, 1.1, -0.7],
            [-0.7, -0.65, 0.95, -1.6, 0.55, 1.0],
            [1.1, -1.1, 1.45, -1.25, -0.65, -0.8],
            [-1.4, -1.35, 1.7, -0.7, 0.85, 0.35],
            [0.55, -0.95, 1.25, -1.55, -1.0, 0.9],
            [-0.15, -1.55, 1.9, -0.55, 0.75, -1.1],
            [1.45, -0.75, 1.05, -1.7, -0.9, 0.65],
            [-1.2, -1.2, 1.55, -1.05, 1.2, -0.45],
            [0.95, -1.6, 1.75, -0.65, -0.75, 1.15]
        ];
    }
}
