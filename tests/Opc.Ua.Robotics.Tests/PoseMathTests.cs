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

namespace Opc.Ua.Robotics.Server.Tests
{
    [TestFixture]
    [Category("Robotics")]
    [Category("RobotIntent")]
    public sealed class PoseMathTests
    {
        [TestCase(0.0, 0.0, 0.0)]
        [TestCase(0.3, -0.2, 0.7)]
        [TestCase(-1.2, 0.4, 2.1)]
        [TestCase(2.6, -1.1, -2.4)]
        public void QuaternionThreeDOrientationRoundTripPreservesOrientation(double roll, double pitch, double yaw)
        {
            Pose3DDataType pose = PoseMath.FromThreeDFrame(Frame(roll, pitch, yaw), "base");

            var frame = PoseMath.ToThreeDFrame(pose);
            Pose3DDataType actual = PoseMath.FromThreeDFrame(frame, "base");

            Assert.Multiple(() =>
            {
                Assert.That(frame.Orientation.A, Is.EqualTo(roll).Within(1e-12));
                Assert.That(frame.Orientation.B, Is.EqualTo(pitch).Within(1e-12));
                Assert.That(frame.Orientation.C, Is.EqualTo(yaw).Within(1e-12));
                AssertQuaternionEqual(PoseMath.Normalize(pose.Orientation.Span).Span, actual.Orientation.Span, 1e-12);
                AssertPositionEqual(pose.Position.Span, actual.Position.Span, 1e-12);
            });
        }

        [Test]
        public void ToThreeDFrameMapsPureAxisQuaternionsToAnnexCRollPitchYaw()
        {
            double sinHalf45 = Math.Sin(Math.PI / 8.0);
            double cosHalf45 = Math.Cos(Math.PI / 8.0);

            // Annex C uses intrinsic Z-Y-X with A=roll about reference X, B=pitch about reference Y,
            // and C=yaw about reference Z. For a single +45 degree axis rotation, only that axis angle remains.
            var roll = PoseMath.ToThreeDFrame(Pose([sinHalf45, 0.0, 0.0, cosHalf45]));
            var pitch = PoseMath.ToThreeDFrame(Pose([0.0, sinHalf45, 0.0, cosHalf45]));
            var yaw = PoseMath.ToThreeDFrame(Pose([0.0, 0.0, sinHalf45, cosHalf45]));

            Assert.Multiple(() =>
            {
                AssertOrientationEqual(Math.PI / 4.0, 0.0, 0.0, roll.Orientation, 1e-12);
                AssertOrientationEqual(0.0, Math.PI / 4.0, 0.0, pitch.Orientation, 1e-12);
                AssertOrientationEqual(0.0, 0.0, Math.PI / 4.0, yaw.Orientation, 1e-12);
            });
        }

        [Test]
        public void ToThreeDFrameMapsCompositeQuaternionToAnnexCOracle()
        {
            const double expectedRoll = Math.PI / 6.0;
            const double expectedPitch = Math.PI / 4.0;
            const double expectedYaw = -Math.PI / 3.0;

            // Annex C: cr=cos(A/2), sr=sin(A/2), cp=cos(B/2), sp=sin(B/2), cy=cos(C/2),
            // sy=sin(C/2). For A=30, B=45, C=-60 degrees the half-angle values are evaluated here
            // from trigonometric identities, then combined with the Annex C quaternion equations by hand.
            double sr = (Math.Sqrt(6.0) - Math.Sqrt(2.0)) / 4.0;
            double cr = (Math.Sqrt(6.0) + Math.Sqrt(2.0)) / 4.0;
            double sp = Math.Sqrt(2.0 - Math.Sqrt(2.0)) / 2.0;
            double cp = Math.Sqrt(2.0 + Math.Sqrt(2.0)) / 2.0;
            double sy = -0.5;
            double cy = Math.Sqrt(3.0) / 2.0;

            var frame = PoseMath.ToThreeDFrame(Pose([
                (sr * cp * cy) - (cr * sp * sy),
                (cr * sp * cy) + (sr * cp * sy),
                (cr * cp * sy) - (sr * sp * cy),
                (cr * cp * cy) + (sr * sp * sy)
            ]));

            AssertOrientationEqual(expectedRoll, expectedPitch, expectedYaw, frame.Orientation, 1e-12);
        }

        [Test]
        public void FromThreeDFrameMapsAnnexCTripleToHandComputedQuaternion()
        {
            const double roll = Math.PI / 6.0;
            const double pitch = Math.PI / 4.0;
            const double yaw = -Math.PI / 3.0;

            // Annex C applies intrinsic Z-Y-X. Substituting A=30, B=45, C=-60 degrees gives the
            // exact half-angle terms below; the expected quaternion is the hand-evaluated Annex C formula.
            double sr = (Math.Sqrt(6.0) - Math.Sqrt(2.0)) / 4.0;
            double cr = (Math.Sqrt(6.0) + Math.Sqrt(2.0)) / 4.0;
            double sp = Math.Sqrt(2.0 - Math.Sqrt(2.0)) / 2.0;
            double cp = Math.Sqrt(2.0 + Math.Sqrt(2.0)) / 2.0;
            double sy = -0.5;
            double cy = Math.Sqrt(3.0) / 2.0;
            double[] expected =
            [
                (sr * cp * cy) - (cr * sp * sy),
                (cr * sp * cy) + (sr * cp * sy),
                (cr * cp * sy) - (sr * sp * cy),
                (cr * cp * cy) + (sr * sp * sy)
            ];

            Pose3DDataType pose = PoseMath.FromThreeDFrame(Frame(roll, pitch, yaw), "base");

            AssertQuaternionEqual(expected, pose.Orientation.Span, 1e-12);
        }

        [TestCase(Math.PI / 2.0)]
        [TestCase(-Math.PI / 2.0)]
        public void PolePitchConversionDoesNotProduceDomainError(double pitch)
        {
            Pose3DDataType pose = PoseMath.FromThreeDFrame(Frame(0.4, pitch, -0.8), "base");

            var frame = PoseMath.ToThreeDFrame(pose);

            Assert.That(frame.Orientation.B, Is.EqualTo(pitch).Within(1e-12));
            Assert.That(double.IsNaN(frame.Orientation.A), Is.False);
            Assert.That(double.IsNaN(frame.Orientation.C), Is.False);
        }

        [Test]
        public void AsinArgumentAtPoleIsClamped()
        {
            double component = Math.Sqrt(0.5);
            var pose = new Pose3DDataType
            {
                FrameId = "base",
                Position = ArrayOf.Create([0.0, 0.0, 0.0]),
                Orientation = ArrayOf.Create([0.0, component, 0.0, component])
            };

            var frame = PoseMath.ToThreeDFrame(pose);

            Assert.That(frame.Orientation.B, Is.EqualTo(Math.PI / 2.0).Within(1e-12));
        }

        [Test]
        public void NegativeQuaternionNormalizesToNonNegativeWRepresentative()
        {
            ArrayOf<double> normalized = PoseMath.Normalize([0.0, 0.0, -0.5, -0.5]);

            Assert.That(normalized[3], Is.GreaterThanOrEqualTo(0.0));
            AssertQuaternionEqual([0.0, 0.0, Math.Sqrt(0.5), Math.Sqrt(0.5)], normalized.Span, 1e-12);
        }

        [Test]
        public void ToThreeDFrameProducesSameOrientationForEquivalentQuaternionSigns()
        {
            double[] quaternion =
            [
                0.23911761839433449,
                0.36964381061438611,
                -0.09904576054128762,
                0.89239910083252284
            ];

            var positive = PoseMath.ToThreeDFrame(Pose(quaternion));
            var negative = PoseMath.ToThreeDFrame(Pose([
                -quaternion[0],
                -quaternion[1],
                -quaternion[2],
                -quaternion[3]
            ]));

            AssertOrientationEqual(
                positive.Orientation.A,
                positive.Orientation.B,
                positive.Orientation.C,
                negative.Orientation,
                1e-12);
        }

        [TestCase(1.000001, true)]
        [TestCase(1.0000009, true)]
        [TestCase(1.0000011, false)]
        [TestCase(0.9999991, true)]
        [TestCase(0.9999989, false)]
        public void UnitNormValidationHonorsToleranceBoundary(double w, bool expected)
        {
            var pose = new Pose3DDataType
            {
                FrameId = "base",
                Position = ArrayOf.Create([0.0, 0.0, 0.0]),
                Orientation = ArrayOf.Create([0.0, 0.0, 0.0, w])
            };

            bool actual = PoseMath.TryValidate(pose, 1e-6, out string? error);

            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(error is null, Is.EqualTo(expected));
        }

        [Test]
        public void ValidationRejectsNullAndWrongComponentCounts()
        {
            Assert.That(PoseMath.TryValidate(null, 1e-6, out string? nullError), Is.False);
            Assert.That(nullError, Is.Not.Empty);

            var badPosition = new Pose3DDataType
            {
                FrameId = "base",
                Position = ArrayOf.Create([0.0, 0.0]),
                Orientation = ArrayOf.Create([0.0, 0.0, 0.0, 1.0])
            };
            Assert.That(PoseMath.TryValidate(badPosition, 1e-6, out string? positionError), Is.False);
            Assert.That(positionError, Does.Contain("position"));

            var badOrientation = new Pose3DDataType
            {
                FrameId = "base",
                Position = ArrayOf.Create([0.0, 0.0, 0.0]),
                Orientation = ArrayOf.Create([0.0, 0.0, 1.0])
            };
            Assert.That(PoseMath.TryValidate(badOrientation, 1e-6, out string? orientationError), Is.False);
            Assert.That(orientationError, Does.Contain("orientation"));
        }

        [Test]
        public void ComposeWithInverseProducesIdentity()
        {
            Pose3DDataType pose = PoseMath.FromThreeDFrame(new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = 1.0, Y = 2.0, Z = 3.0 },
                Orientation = new ThreeDOrientation { A = 0.2, B = -0.3, C = 0.4 }
            }, "base");

            Pose3DDataType inverse = PoseMath.Invert(pose);
            Pose3DDataType identity = PoseMath.Compose(pose, inverse);

            AssertPositionEqual([0.0, 0.0, 0.0], identity.Position.Span, 1e-12);
            AssertQuaternionEqual([0.0, 0.0, 0.0, 1.0], identity.Orientation.Span, 1e-12);
        }

        [Test]
        public void ComposeAppliesLeftPoseRotationToRightPosePosition()
        {
            double half = Math.Sqrt(0.5);
            Pose3DDataType left = Pose([0.0, 0.0, half, half], [1.0, 2.0, 3.0]);
            Pose3DDataType right = Pose([0.0, 0.0, 0.0, 1.0], [4.0, 5.0, 6.0]);

            Pose3DDataType actual = PoseMath.Compose(left, right);

            // A +90 degree yaw maps the right-hand translation (4, 5, 6) to (-5, 4, 6),
            // then adds the left-hand position (1, 2, 3).
            AssertPositionEqual([-4.0, 6.0, 9.0], actual.Position.Span, 1e-12);
            AssertQuaternionEqual([0.0, 0.0, half, half], actual.Orientation.Span, 1e-12);
        }

        [Test]
        public void InvertMatchesManualRigidTransformOracle()
        {
            double half = Math.Sqrt(0.5);
            Pose3DDataType pose = Pose([0.0, 0.0, half, half], [1.0, 2.0, 3.0]);

            Pose3DDataType inverse = PoseMath.Invert(pose);

            // The inverse translation is -R^T*p. For +90 degree yaw, R^T*(1, 2, 3) is (2, -1, 3).
            AssertPositionEqual([-2.0, 1.0, -3.0], inverse.Position.Span, 1e-12);
            AssertQuaternionEqual([0.0, 0.0, -half, half], inverse.Orientation.Span, 1e-12);
        }

        [Test]
        public void FromThreeDFrameRejectsNullInputs()
        {
            Assert.Throws<ArgumentNullException>(() => PoseMath.FromThreeDFrame(null!, "base"));
            Assert.Throws<ArgumentNullException>(() => PoseMath.FromThreeDFrame(Frame(0, 0, 0), null!));
        }

        [Test]
        public void PoseOperationsRejectInvalidInputs()
        {
            Pose3DDataType badPosition = new()
            {
                Position = ArrayOf.Create([0.0, 0.0]),
                Orientation = ArrayOf.Create([0.0, 0.0, 0.0, 1.0])
            };
            Pose3DDataType badOrientation = new()
            {
                Position = ArrayOf.Create([0.0, 0.0, 0.0]),
                Orientation = ArrayOf.Create([0.0, 0.0, 1.0])
            };

            Assert.Throws<ArgumentNullException>(() => PoseMath.ToThreeDFrame(null!));
            Assert.Throws<ArgumentNullException>(() => PoseMath.Compose(null!, Pose()));
            Assert.Throws<ArgumentNullException>(() => PoseMath.Compose(Pose(), null!));
            Assert.Throws<ArgumentNullException>(() => PoseMath.Invert(null!));
            Assert.Throws<ArgumentException>(() => PoseMath.ToThreeDFrame(badPosition));
            Assert.Throws<ArgumentException>(() => PoseMath.Compose(Pose(), badPosition));
            Assert.Throws<ArgumentException>(() => PoseMath.Invert(badOrientation));
        }

        [Test]
        public void QuaternionHelpersValidateLengthsAndNorms()
        {
            Assert.That(PoseMath.IsUnitQuaternion([0.0, 0.0, 1.0], 1e-6), Is.False);
            Assert.Throws<ArgumentException>(() => PoseMath.Normalize([0.0, 0.0, 0.0, 0.0]));
            Assert.Throws<ArgumentException>(() => PoseMath.Normalize([0.0, 0.0, 0.0, double.NaN]));
            Assert.Throws<ArgumentException>(() => PoseMath.Multiply([0.0, 0.0, 0.0], [0.0, 0.0, 0.0, 1.0]));
            Assert.Throws<ArgumentException>(() => PoseMath.Multiply([0.0, 0.0, 0.0, 1.0], [0.0, 0.0, 0.0]));
            Assert.Throws<ArgumentException>(() => PoseMath.RotateVector([0.0, 0.0, 0.0, 1.0], [1.0, 0.0]));
        }

        [Test]
        public void QuaternionHelpersProduceExpectedRotationAndProduct()
        {
            double half = Math.Sqrt(0.5);
            ArrayOf<double> yaw90 = ArrayOf.Create([0.0, 0.0, half, half]);
            ArrayOf<double> rotated = PoseMath.RotateVector(yaw90.Span, [1.0, 0.0, 0.0]);
            ArrayOf<double> conjugate = PoseMath.Conjugate(yaw90.Span);
            ArrayOf<double> identity = PoseMath.Multiply(yaw90.Span, conjugate.Span);

            AssertPositionEqual([0.0, 1.0, 0.0], rotated.Span, 1e-12);
            AssertQuaternionEqual([0.0, 0.0, 0.0, 1.0], identity.Span, 1e-12);
        }

        private static Pose3DDataType Pose()
        {
            return Pose([0.0, 0.0, 0.0, 1.0]);
        }

        private static Pose3DDataType Pose(double[] orientation)
        {
            return Pose(orientation, [0.0, 0.0, 0.0]);
        }

        private static Pose3DDataType Pose(double[] orientation, double[] position)
        {
            return new Pose3DDataType
            {
                Position = ArrayOf.Create(position.AsSpan()),
                Orientation = ArrayOf.Create(orientation.AsSpan())
            };
        }

        private static ThreeDFrame Frame(double roll, double pitch, double yaw)
        {
            return new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = 1.0, Y = 2.0, Z = 3.0 },
                Orientation = new ThreeDOrientation { A = roll, B = pitch, C = yaw }
            };
        }


        private static void AssertOrientationEqual(
            double expectedA,
            double expectedB,
            double expectedC,
            ThreeDOrientation actual,
            double tolerance)
        {
            Assert.That(actual.A, Is.EqualTo(expectedA).Within(tolerance));
            Assert.That(actual.B, Is.EqualTo(expectedB).Within(tolerance));
            Assert.That(actual.C, Is.EqualTo(expectedC).Within(tolerance));
        }

        private static void AssertPositionEqual(
            ReadOnlySpan<double> expected,
            ReadOnlySpan<double> actual,
            double tolerance)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(tolerance));
            }
        }

        private static void AssertQuaternionEqual(
            ReadOnlySpan<double> expected,
            ReadOnlySpan<double> actual,
            double tolerance)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(tolerance));
            }
        }
    }
}
