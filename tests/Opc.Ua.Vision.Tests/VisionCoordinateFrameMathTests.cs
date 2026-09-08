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
using Opc.Ua.Vision;
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Nails down §5.12 pose composition — quaternion order (x, y, z, w),
    /// positions in metres, empty-covariance sentinel, and the
    /// frame-precedence rule (the composed pose reports its parent's
    /// FrameId). Uses precomputed numeric oracles so a silent sign / axis
    /// swap in the rotation kernel cannot slip through as a "did not throw"
    /// pass.
    /// </summary>
    [TestFixture]
    public sealed class VisionCoordinateFrameMathTests
    {
        private const double Tol = 1e-10;

        [Test]
        public void IdentityReturnsPoseWithZeroPositionUnitQuaternionAndEmptyCovariance()
        {
            VisionPose3DDataType identity = VisionCoordinateFrameMath.Identity("base");

            Assert.Multiple(() =>
            {
                Assert.That(identity.FrameId, Is.EqualTo("base"));
                Assert.That(identity.Position.Count, Is.EqualTo(3));
                Assert.That(identity.Position[0], Is.EqualTo(0.0));
                Assert.That(identity.Position[1], Is.EqualTo(0.0));
                Assert.That(identity.Position[2], Is.EqualTo(0.0));
                Assert.That(identity.Orientation.Count, Is.EqualTo(4));
                Assert.That(identity.Orientation[0], Is.EqualTo(0.0));
                Assert.That(identity.Orientation[1], Is.EqualTo(0.0));
                Assert.That(identity.Orientation[2], Is.EqualTo(0.0));
                Assert.That(identity.Orientation[3], Is.EqualTo(1.0));
                Assert.That(identity.Covariance.Count, Is.EqualTo(0));
            });
        }

        [Test]
        public void IdentityWithNullFrameIdCollapsesToEmptyString()
        {
            VisionPose3DDataType identity = VisionCoordinateFrameMath.Identity(null!);

            Assert.That(identity.FrameId, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ComposeWithIdentityChildReturnsParent()
        {
            VisionPose3DDataType parent = MakePose(
                "world",
                s_doubleValues1,
                UnitQuaternion(0.0, 0.0, Math.PI / 6.0));
            VisionPose3DDataType child = VisionCoordinateFrameMath.Identity("child");

            VisionPose3DDataType composed = VisionCoordinateFrameMath.Compose(parent, child);

            AssertPositionEqual(composed, 1.0, 2.0, 3.0);
            AssertOrientationEqual(composed, parent.Orientation[0], parent.Orientation[1], parent.Orientation[2], parent.Orientation[3]);
            Assert.That(composed.FrameId, Is.EqualTo("world"));
            Assert.That(composed.Covariance.Count, Is.EqualTo(0));
        }

        [Test]
        public void ComposeAppliesParentRotationToChildTranslation()
        {
            VisionPose3DDataType parent = MakePose(
                "world",
                s_doubleValues2,
                UnitQuaternion(0.0, 0.0, Math.PI / 2.0));
            VisionPose3DDataType child = MakePose(
                "arm",
                s_doubleValues3,
                s_doubleValues4);

            VisionPose3DDataType composed = VisionCoordinateFrameMath.Compose(parent, child);

            AssertPositionEqual(composed, 10.0, 1.0, 0.0);
            AssertOrientationEqual(composed, 0.0, 0.0, Math.Sin(Math.PI / 4.0), Math.Cos(Math.PI / 4.0));
            Assert.That(composed.FrameId, Is.EqualTo("world"), "The frame precedence rule requires the composed pose to inherit the parent's FrameId.");
        }

        [Test]
        public void ComposeThreeStagesCameraToFlangeToBaseAgreesWithManualMultiplication()
        {
            VisionPose3DDataType baseToWorld = MakePose(
                "world",
                s_doubleValues5,
                s_doubleValues4);
            VisionPose3DDataType flangeInBase = MakePose(
                "base",
                s_doubleValues6,
                UnitQuaternion(0.0, 0.0, Math.PI / 4.0));
            VisionPose3DDataType cameraInFlange = MakePose(
                "flange",
                s_doubleValues7,
                UnitQuaternion(Math.PI / 12.0, 0.0, 0.0));

            VisionPose3DDataType flangeInWorld = VisionCoordinateFrameMath.Compose(baseToWorld, flangeInBase);
            VisionPose3DDataType cameraInWorldViaFlange = VisionCoordinateFrameMath.Compose(flangeInWorld, cameraInFlange);
            VisionPose3DDataType cameraInBase = VisionCoordinateFrameMath.Compose(flangeInBase, cameraInFlange);
            VisionPose3DDataType cameraInWorldDirect = VisionCoordinateFrameMath.Compose(baseToWorld, cameraInBase);

            Assert.Multiple(() =>
            {
                Assert.That(cameraInWorldViaFlange.Position[0], Is.EqualTo(cameraInWorldDirect.Position[0]).Within(Tol));
                Assert.That(cameraInWorldViaFlange.Position[1], Is.EqualTo(cameraInWorldDirect.Position[1]).Within(Tol));
                Assert.That(cameraInWorldViaFlange.Position[2], Is.EqualTo(cameraInWorldDirect.Position[2]).Within(Tol));
                Assert.That(cameraInWorldViaFlange.Orientation[0], Is.EqualTo(cameraInWorldDirect.Orientation[0]).Within(Tol));
                Assert.That(cameraInWorldViaFlange.Orientation[1], Is.EqualTo(cameraInWorldDirect.Orientation[1]).Within(Tol));
                Assert.That(cameraInWorldViaFlange.Orientation[2], Is.EqualTo(cameraInWorldDirect.Orientation[2]).Within(Tol));
                Assert.That(cameraInWorldViaFlange.Orientation[3], Is.EqualTo(cameraInWorldDirect.Orientation[3]).Within(Tol));
            });
        }

        [Test]
        public void ComposeCameraToFlangeToToolCentrePointYieldsRealNumbers()
        {
            VisionPose3DDataType tcpInFlange = MakePose(
                "flange",
                s_doubleValues8,
                UnitQuaternion(0.0, Math.PI, 0.0));
            VisionPose3DDataType flangeInBase = MakePose(
                "base",
                s_doubleValues9,
                UnitQuaternion(0.0, 0.0, Math.PI / 3.0));

            VisionPose3DDataType tcpInBase = VisionCoordinateFrameMath.Compose(flangeInBase, tcpInFlange);

            Assert.Multiple(() =>
            {
                Assert.That(IsFinite(tcpInBase.Position[0]), Is.True);
                Assert.That(IsFinite(tcpInBase.Position[1]), Is.True);
                Assert.That(IsFinite(tcpInBase.Position[2]), Is.True);
                Assert.That(IsFinite(tcpInBase.Orientation[0]), Is.True);
                Assert.That(IsFinite(tcpInBase.Orientation[1]), Is.True);
                Assert.That(IsFinite(tcpInBase.Orientation[2]), Is.True);
                Assert.That(IsFinite(tcpInBase.Orientation[3]), Is.True);
                Assert.That(tcpInBase.Position[2], Is.EqualTo(0.6).Within(Tol),
                    "TCP is 0.2 m along the flange +Z, and after a π rotation about Y that vector points into +Z of base, so ExpectedZ = 0.4 + 0.2 = 0.6.");
            });
        }

        [Test]
        public void ComposeRenormalisesNonUnitQuaternionAndPreservesAxisDirection()
        {
            VisionPose3DDataType parent = MakePose(
                "world",
                s_doubleValues5,
                s_doubleValues10);
            VisionPose3DDataType child = VisionCoordinateFrameMath.Identity("child");

            VisionPose3DDataType composed = VisionCoordinateFrameMath.Compose(parent, child);

            double norm = Math.Sqrt(
                (composed.Orientation[0] * composed.Orientation[0]) +
                (composed.Orientation[1] * composed.Orientation[1]) +
                (composed.Orientation[2] * composed.Orientation[2]) +
                (composed.Orientation[3] * composed.Orientation[3]));
            Assert.Multiple(() =>
            {
                Assert.That(norm, Is.EqualTo(1.0).Within(Tol));
                Assert.That(composed.Orientation[2], Is.EqualTo(Math.Sqrt(0.5)).Within(Tol));
                Assert.That(composed.Orientation[3], Is.EqualTo(Math.Sqrt(0.5)).Within(Tol));
            });
        }

        [Test]
        public void InvertFollowedByComposeReturnsIdentityWithinTolerance()
        {
            VisionPose3DDataType pose = MakePose(
                "camera",
                new[] { 0.3, -0.2, 0.5 },
                UnitQuaternion(Math.PI / 5.0, Math.PI / 7.0, Math.PI / 3.0));

            VisionPose3DDataType inverse = VisionCoordinateFrameMath.Invert(pose);
            VisionPose3DDataType composed = VisionCoordinateFrameMath.Compose(pose, inverse);

            AssertPositionEqual(composed, 0.0, 0.0, 0.0);
            AssertOrientationEqual(composed, 0.0, 0.0, 0.0, 1.0);
        }

        [Test]
        public void ComposeThrowsArgumentExceptionWhenPositionLengthIsWrong()
        {
            VisionPose3DDataType broken = new VisionPose3DDataType
            {
                FrameId = "broken",
                Position = new double[] { 1.0, 2.0 },
                Orientation = new double[] { 0.0, 0.0, 0.0, 1.0 },
                Covariance = ArrayOf<double>.Empty
            };
            VisionPose3DDataType identity = VisionCoordinateFrameMath.Identity("root");

            Assert.That(
                () => VisionCoordinateFrameMath.Compose(broken, identity),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void ComposeThrowsArgumentExceptionWhenOrientationLengthIsWrong()
        {
            VisionPose3DDataType broken = new VisionPose3DDataType
            {
                FrameId = "broken",
                Position = new double[] { 0.0, 0.0, 0.0 },
                Orientation = new double[] { 0.0, 0.0, 1.0 },
                Covariance = ArrayOf<double>.Empty
            };
            VisionPose3DDataType identity = VisionCoordinateFrameMath.Identity("root");

            Assert.That(
                () => VisionCoordinateFrameMath.Compose(broken, identity),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void ComposeRefusesAZeroNormQuaternionRatherThanSubstitutingIdentity()
        {
            VisionPose3DDataType zeroRotation = new VisionPose3DDataType
            {
                FrameId = "sensor",
                Position = new double[] { 0.1, 0.2, 0.3 },
                Orientation = new double[] { 0.0, 0.0, 0.0, 0.0 },
                Covariance = ArrayOf<double>.Empty
            };
            VisionPose3DDataType identity = VisionCoordinateFrameMath.Identity("root");

            // Treating a zero quaternion as identity would compose a pose that looks plausible
            // and points the wrong way. For a grasp that is worse than a refusal.
            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => VisionCoordinateFrameMath.Compose(zeroRotation, identity))!;
            Assert.That(exception.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void ComposeAlwaysResetsCovarianceToEmptyArraySentinel()
        {
            VisionPose3DDataType a = MakePose("world", s_doubleValues11, s_doubleValues4);
            VisionPose3DDataType b = MakePose("child", s_doubleValues5, s_doubleValues4);
            a.Covariance = new double[36];
            b.Covariance = new double[36];

            VisionPose3DDataType composed = VisionCoordinateFrameMath.Compose(a, b);

            Assert.That(composed.Covariance.Count, Is.EqualTo(0));
        }

        [Test]
        public void TransformFromToOnIdenticalFrameReturnsIdentityInTargetFrame()
        {
            var frames = new Dictionary<string, VisionCoordinateFrameMath.CoordinateFrameSnapshot>(StringComparer.Ordinal)
            {
                ["base"] = new(
                    "base",
                    VisionFrameRoleEnum.Base,
                    string.Empty,
                    VisionCoordinateFrameMath.Identity("base"))
            };

            VisionPose3DDataType pose = VisionCoordinateFrameMath.TransformFromTo(frames, "base", "base");

            AssertPositionEqual(pose, 0.0, 0.0, 0.0);
            AssertOrientationEqual(pose, 0.0, 0.0, 0.0, 1.0);
            Assert.That(pose.FrameId, Is.EqualTo("base"));
        }

        [Test]
        public void TransformFromToWalksTreeAndComposesTransforms()
        {
            VisionPose3DDataType flangeInBase = MakePose(
                "base",
                s_doubleValues3,
                s_doubleValues4);
            VisionPose3DDataType cameraInFlange = MakePose(
                "flange",
                s_doubleValues12,
                s_doubleValues4);
            var frames = new Dictionary<string, VisionCoordinateFrameMath.CoordinateFrameSnapshot>(StringComparer.Ordinal)
            {
                ["base"] = new("base", VisionFrameRoleEnum.Base, string.Empty, VisionCoordinateFrameMath.Identity("base")),
                ["flange"] = new("flange", VisionFrameRoleEnum.MechanicalInterface, "base", flangeInBase),
                ["camera"] = new("camera", VisionFrameRoleEnum.Camera, "flange", cameraInFlange)
            };

            VisionPose3DDataType poseCameraInBase = VisionCoordinateFrameMath.TransformFromTo(frames, "camera", "base");

            AssertPositionEqual(poseCameraInBase, 1.0, 0.0, 0.2);
            AssertOrientationEqual(poseCameraInBase, 0.0, 0.0, 0.0, 1.0);
            Assert.That(poseCameraInBase.FrameId, Is.EqualTo("base"));
        }

        [Test]
        public void TransformFromToDetectsCycleAndThrowsBadInvalidArgument()
        {
            var identity = VisionCoordinateFrameMath.Identity(string.Empty);
            var frames = new Dictionary<string, VisionCoordinateFrameMath.CoordinateFrameSnapshot>(StringComparer.Ordinal)
            {
                ["a"] = new("a", VisionFrameRoleEnum.Base, "b", identity),
                ["b"] = new("b", VisionFrameRoleEnum.Base, "a", identity)
            };

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => VisionCoordinateFrameMath.TransformFromTo(frames, "a", "b"))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void TransformFromToWithUnknownFrameThrowsBadNodeIdUnknown()
        {
            var identity = VisionCoordinateFrameMath.Identity(string.Empty);
            var frames = new Dictionary<string, VisionCoordinateFrameMath.CoordinateFrameSnapshot>(StringComparer.Ordinal)
            {
                ["base"] = new("base", VisionFrameRoleEnum.Base, string.Empty, identity)
            };

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => VisionCoordinateFrameMath.TransformFromTo(frames, "ghost", "base"))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void TransformFromToWithMissingParentPointerReportsBadNodeIdUnknown()
        {
            var identity = VisionCoordinateFrameMath.Identity(string.Empty);
            var frames = new Dictionary<string, VisionCoordinateFrameMath.CoordinateFrameSnapshot>(StringComparer.Ordinal)
            {
                ["camera"] = new("camera", VisionFrameRoleEnum.Camera, "missing-parent", identity),
                ["base"] = new("base", VisionFrameRoleEnum.Base, string.Empty, identity)
            };

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => VisionCoordinateFrameMath.TransformFromTo(frames, "camera", "base"))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void TransformFromToWithEmptyFrameIdArgumentThrowsBadInvalidArgument()
        {
            var frames = new Dictionary<string, VisionCoordinateFrameMath.CoordinateFrameSnapshot>(StringComparer.Ordinal);

            ServiceResultException ex1 = Assert.Throws<ServiceResultException>(
                () => VisionCoordinateFrameMath.TransformFromTo(frames, string.Empty, "base"))!;
            ServiceResultException ex2 = Assert.Throws<ServiceResultException>(
                () => VisionCoordinateFrameMath.TransformFromTo(frames, "base", string.Empty))!;

            Assert.Multiple(() =>
            {
                Assert.That(ex1.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(ex2.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            });
        }

        [Test]
        public void TransformFromToThrowsArgumentNullExceptionForNullFramesDictionary()
        {
            Assert.That(
                () => VisionCoordinateFrameMath.TransformFromTo(null!, "a", "b"),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void InvertThrowsArgumentExceptionOnBadPoseShape()
        {
            VisionPose3DDataType broken = new VisionPose3DDataType
            {
                FrameId = "broken",
                Position = new double[] { 0.0, 0.0 },
                Orientation = new double[] { 0.0, 0.0, 0.0, 1.0 },
                Covariance = ArrayOf<double>.Empty
            };

            Assert.That(() => VisionCoordinateFrameMath.Invert(broken),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void PositionAndOrientationLengthConstantsMatchSpecification()
        {
            Assert.Multiple(() =>
            {
                Assert.That(VisionCoordinateFrameMath.PositionLength, Is.EqualTo(3));
                Assert.That(VisionCoordinateFrameMath.OrientationLength, Is.EqualTo(4));
            });
        }

        private static VisionPose3DDataType MakePose(string frameId, double[] position, double[] quaternion)
        {
            return new VisionPose3DDataType
            {
                FrameId = frameId,
                Position = position,
                Orientation = quaternion,
                Covariance = ArrayOf<double>.Empty
            };
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double[] UnitQuaternion(double rollX, double pitchY, double yawZ)
        {
            double cx = Math.Cos(rollX / 2.0);
            double sx = Math.Sin(rollX / 2.0);
            double cy = Math.Cos(pitchY / 2.0);
            double sy = Math.Sin(pitchY / 2.0);
            double cz = Math.Cos(yawZ / 2.0);
            double sz = Math.Sin(yawZ / 2.0);
            return new double[]
            {
                (sx * cy * cz) - (cx * sy * sz),
                (cx * sy * cz) + (sx * cy * sz),
                (cx * cy * sz) - (sx * sy * cz),
                (cx * cy * cz) + (sx * sy * sz)
            };
        }

        private static void AssertPositionEqual(VisionPose3DDataType pose, double x, double y, double z)
        {
            Assert.Multiple(() =>
            {
                Assert.That(pose.Position[0], Is.EqualTo(x).Within(Tol));
                Assert.That(pose.Position[1], Is.EqualTo(y).Within(Tol));
                Assert.That(pose.Position[2], Is.EqualTo(z).Within(Tol));
            });
        }

        private static void AssertOrientationEqual(VisionPose3DDataType pose, double x, double y, double z, double w)
        {
            Assert.Multiple(() =>
            {
                Assert.That(pose.Orientation[0], Is.EqualTo(x).Within(Tol));
                Assert.That(pose.Orientation[1], Is.EqualTo(y).Within(Tol));
                Assert.That(pose.Orientation[2], Is.EqualTo(z).Within(Tol));
                Assert.That(pose.Orientation[3], Is.EqualTo(w).Within(Tol));
            });
        }

        private static readonly double[] s_doubleValues1 = [1.0, 2.0, 3.0];
        private static readonly double[] s_doubleValues2 = [10.0, 0.0, 0.0];
        private static readonly double[] s_doubleValues3 = [1.0, 0.0, 0.0];
        private static readonly double[] s_doubleValues4 = [0.0, 0.0, 0.0, 1.0];
        private static readonly double[] s_doubleValues5 = [0.0, 0.0, 0.0];
        private static readonly double[] s_doubleValues6 = [0.5, 0.0, 0.75];
        private static readonly double[] s_doubleValues7 = [0.02, 0.0, 0.1];
        private static readonly double[] s_doubleValues8 = [0.0, 0.0, 0.20];
        private static readonly double[] s_doubleValues9 = [0.6, 0.1, 0.4];
        private static readonly double[] s_doubleValues10 = [0.0, 0.0, 4.0, 4.0];
        private static readonly double[] s_doubleValues11 = [0.1, 0.2, 0.3];
        private static readonly double[] s_doubleValues12 = [0.0, 0.0, 0.2];
    }
}
