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
using Robotics;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Tests for <see cref="RobotKinematics"/>, the forward kinematics the sample uses to
    /// keep a carried part welded to the gripper.
    /// </summary>
    [TestFixture]
    [Category("Robotics")]
    [Parallelizable]
    public class RobotKinematicsTests
    {
        private const double Tolerance = 1e-9;

        /// <summary>
        /// The home pose of the axis template, hand-computed from the link offsets authored
        /// in robot.usda. If the asset's offsets change this is the test that says so.
        /// </summary>
        private static readonly double[] s_homePose = [0.0, -60.0, 75.0, 0.0, 45.0, 0.0];

        [Test]
        public void IdentityComposesToItself()
        {
            RigidTransform composed = RigidTransform.Identity
                .Compose(RigidTransform.Identity);

            Assert.That(composed, Is.EqualTo(RigidTransform.Identity));
        }

        [Test]
        public void TranslationsAccumulate()
        {
            RigidTransform composed = RigidTransform.Translation(1.0, 2.0, 3.0)
                .Compose(RigidTransform.Translation(0.5, -1.0, 0.25));

            (double x, double y, double z) = composed.Origin;
            Assert.That(x, Is.EqualTo(1.5).Within(Tolerance));
            Assert.That(y, Is.EqualTo(1.0).Within(Tolerance));
            Assert.That(z, Is.EqualTo(3.25).Within(Tolerance));
        }

        /// <summary>
        /// A child offset is expressed in the parent's rotated frame, which is the property
        /// the whole arm chain depends on.
        /// </summary>
        [Test]
        public void RotationAppliesToTheChildOffset()
        {
            RigidTransform composed = RigidTransform.RotationZ(90.0)
                .Compose(RigidTransform.Translation(1.0, 0.0, 0.0));

            (double x, double y, double z) = composed.Origin;
            Assert.That(x, Is.Zero.Within(1e-12));
            Assert.That(y, Is.EqualTo(1.0).Within(1e-12));
            Assert.That(z, Is.Zero.Within(1e-12));
        }

        [Test]
        public void ToUsdRowMajorPutsTheTranslationInTheLastRow()
        {
            double[] m = RigidTransform.Translation(1.0, 2.0, 3.0).ToUsdRowMajor();

            Assert.That(m, Has.Length.EqualTo(16));
            Assert.That(m[12], Is.EqualTo(1.0).Within(Tolerance));
            Assert.That(m[13], Is.EqualTo(2.0).Within(Tolerance));
            Assert.That(m[14], Is.EqualTo(3.0).Within(Tolerance));
            Assert.That(m[15], Is.EqualTo(1.0).Within(Tolerance));
        }

        /// <summary>
        /// The tool centre point of the home pose, computed by hand from the link offsets:
        /// base 0.365, J1 0.290, J2 (0.260, 0.385) then -60 deg, J3 0.680 then +75 deg,
        /// J4 (0.670, -0.035), J5 +45 deg, flange 0.158 and the 0.150 jaw offset.
        /// </summary>
        [Test]
        public void HomePoseResolvesToTheHandComputedToolCentrePoint()
        {
            RigidTransform mount = RobotKinematics.CreateMountPose(0.0, 0.0, 0.0, 0.0);

            RigidTransform tcp = RobotKinematics.ComputeToolCentrePoint(mount, s_homePose);

            (double x, double y, double z) = tcp.Origin;
            Assert.That(x, Is.EqualTo(1.3919).Within(0.001));
            Assert.That(y, Is.Zero.Within(1e-9));
            Assert.That(z, Is.EqualTo(1.1550).Within(0.001));
        }

        /// <summary>
        /// The mount heading has to carry the whole arm round with it, otherwise a robot
        /// that turns to face a table would reach in the direction it started from.
        /// </summary>
        [Test]
        public void MountHeadingRotatesTheWholeArm()
        {
            RigidTransform facingX = RobotKinematics.CreateMountPose(0.0, 0.0, 0.0, 0.0);
            RigidTransform facingY = RobotKinematics.CreateMountPose(0.0, 0.0, 0.0, 90.0);

            (double x0, double y0, double z0) = RobotKinematics
                .ComputeToolCentrePoint(facingX, s_homePose).Origin;
            (double x1, double y1, double z1) = RobotKinematics
                .ComputeToolCentrePoint(facingY, s_homePose).Origin;

            Assert.That(x1, Is.EqualTo(-y0).Within(1e-9));
            Assert.That(y1, Is.EqualTo(x0).Within(1e-9));
            Assert.That(z1, Is.EqualTo(z0).Within(1e-9));
        }

        [Test]
        public void MountTranslationOffsetsTheToolCentrePoint()
        {
            RigidTransform atOrigin = RobotKinematics.CreateMountPose(0.0, 0.0, 0.0, 0.0);
            RigidTransform moved = RobotKinematics.CreateMountPose(-2.4, 0.35, 0.0, 0.0);

            (double x0, double y0, _) = RobotKinematics
                .ComputeToolCentrePoint(atOrigin, s_homePose).Origin;
            (double x1, double y1, _) = RobotKinematics
                .ComputeToolCentrePoint(moved, s_homePose).Origin;

            Assert.That(x1 - x0, Is.EqualTo(-2.4).Within(1e-9));
            Assert.That(y1 - y0, Is.EqualTo(0.35).Within(1e-9));
        }

        /// <summary>
        /// The reach constant is what the cell layout is dimensioned against, so it must
        /// stay consistent with the chain the kinematics actually walks.
        /// </summary>
        [Test]
        public void MaximumReachBoundsTheFullyExtendedArm()
        {
            RigidTransform mount = RobotKinematics.CreateMountPose(0.0, 0.0, 0.0, 0.0);
            double[] extended = [0.0, -90.0, 0.0, 0.0, 0.0, 0.0];

            (double x, double y, _) = RobotKinematics
                .ComputeToolCentrePoint(mount, extended).Origin;

            double planarReach = Math.Sqrt((x * x) + (y * y));
            Assert.That(planarReach, Is.LessThanOrEqualTo(RobotKinematics.MaximumReach + 1e-9));
        }

        [Test]
        public void WrongAxisCountIsRejected()
        {
            RigidTransform mount = RigidTransform.Identity;

            Assert.That(
                () => RobotKinematics.ComputeToolCentrePoint(mount, [0.0, 0.0]),
                Throws.ArgumentException);
        }
    }
}
