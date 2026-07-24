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

using NUnit.Framework;
using static Opc.Ua.Positioning.Tests.Transforms.TransformTestHelpers;

namespace Opc.Ua.Positioning.Tests.Transforms
{
    /// <summary>
    /// Tests for <see cref="RslFrameTransform"/> against the OPC 10000-210 Annex B
    /// frame conventions: right handed axes, roll A about X, pitch B about Y, yaw
    /// C about Z, intrinsic z-y'-x'' equivalent to Rz(C)*Ry(B)*Rx(A).
    /// </summary>
    [TestFixture]
    [Category("Positioning")]
    public sealed class RslFrameTransformTests
    {
        private const double kTolerance = 1e-12;

        [Test]
        public void YawNinetyDegreesRotatesXAxisToYAxis()
        {
            var t = RslFrameTransform.FromComponents(
                Orientation(0.0, 0.0, 90.0), Cartesian(0.0, 0.0, 0.0), AngleUnit.Degrees);

            ThreeDCartesianCoordinates p = t.TransformPoint(Cartesian(1.0, 0.0, 0.0));

            Assert.That(p.X, Is.Zero.Within(kTolerance));
            Assert.That(p.Y, Is.EqualTo(1.0).Within(kTolerance));
            Assert.That(p.Z, Is.Zero.Within(kTolerance));
        }

        [Test]
        public void RollNinetyDegreesRotatesYAxisToZAxis()
        {
            var t = RslFrameTransform.FromComponents(
                Orientation(90.0, 0.0, 0.0), Cartesian(0.0, 0.0, 0.0), AngleUnit.Degrees);

            ThreeDCartesianCoordinates p = t.TransformPoint(Cartesian(0.0, 1.0, 0.0));

            Assert.That(p.X, Is.Zero.Within(kTolerance));
            Assert.That(p.Y, Is.Zero.Within(kTolerance));
            Assert.That(p.Z, Is.EqualTo(1.0).Within(kTolerance));
        }

        [Test]
        public void PitchNinetyDegreesRotatesZAxisToXAxis()
        {
            var t = RslFrameTransform.FromComponents(
                Orientation(0.0, 90.0, 0.0), Cartesian(0.0, 0.0, 0.0), AngleUnit.Degrees);

            ThreeDCartesianCoordinates p = t.TransformPoint(Cartesian(0.0, 0.0, 1.0));

            Assert.That(p.X, Is.EqualTo(1.0).Within(kTolerance));
            Assert.That(p.Y, Is.Zero.Within(kTolerance));
            Assert.That(p.Z, Is.Zero.Within(kTolerance));
        }

        [Test]
        public void CompositionDoesNotCommuteGoldenVectors()
        {
            var roll = RslFrameTransform.FromComponents(
                Orientation(90.0, 0.0, 0.0), Cartesian(0.0, 0.0, 0.0), AngleUnit.Degrees);
            var yaw = RslFrameTransform.FromComponents(
                Orientation(0.0, 0.0, 90.0), Cartesian(0.0, 0.0, 0.0), AngleUnit.Degrees);

            // Apply yaw first, then roll.
            ThreeDCartesianCoordinates yawThenRoll = roll.Compose(yaw).TransformPoint(Cartesian(1.0, 0.0, 0.0));
            // Apply roll first, then yaw.
            ThreeDCartesianCoordinates rollThenYaw = yaw.Compose(roll).TransformPoint(Cartesian(1.0, 0.0, 0.0));

            // Golden absolute results, not a mere round trip.
            Assert.That(yawThenRoll.X, Is.Zero.Within(kTolerance));
            Assert.That(yawThenRoll.Y, Is.Zero.Within(kTolerance));
            Assert.That(yawThenRoll.Z, Is.EqualTo(1.0).Within(kTolerance));

            Assert.That(rollThenYaw.X, Is.Zero.Within(kTolerance));
            Assert.That(rollThenYaw.Y, Is.EqualTo(1.0).Within(kTolerance));
            Assert.That(rollThenYaw.Z, Is.Zero.Within(kTolerance));

            Assert.That(Distance(yawThenRoll, rollThenYaw), Is.GreaterThan(1.0));
        }

        [Test]
        public void IntrinsicOrderMatchesRzRyRxGoldenVector()
        {
            // A=90 (roll), C=90 (yaw), B=0 => R = Rz(90) * Rx(90).
            var t = RslFrameTransform.FromComponents(
                Orientation(90.0, 0.0, 90.0), Cartesian(0.0, 0.0, 0.0), AngleUnit.Degrees);

            ThreeDCartesianCoordinates onX = t.TransformPoint(Cartesian(1.0, 0.0, 0.0));
            ThreeDCartesianCoordinates onY = t.TransformPoint(Cartesian(0.0, 1.0, 0.0));
            ThreeDCartesianCoordinates onZ = t.TransformPoint(Cartesian(0.0, 0.0, 1.0));

            // e_x -> e_y
            Assert.That(onX.X, Is.Zero.Within(kTolerance));
            Assert.That(onX.Y, Is.EqualTo(1.0).Within(kTolerance));
            Assert.That(onX.Z, Is.Zero.Within(kTolerance));

            // e_y -> e_z (Rx sends e_y to e_z, Rz leaves e_z)
            Assert.That(onY.X, Is.Zero.Within(kTolerance));
            Assert.That(onY.Y, Is.Zero.Within(kTolerance));
            Assert.That(onY.Z, Is.EqualTo(1.0).Within(kTolerance));

            // e_z -> Rx sends e_z to -e_y, Rz sends -e_y to e_x
            Assert.That(onZ.X, Is.EqualTo(1.0).Within(kTolerance));
            Assert.That(onZ.Y, Is.Zero.Within(kTolerance));
            Assert.That(onZ.Z, Is.Zero.Within(kTolerance));
        }

        [Test]
        public void TransformPointAppliesRotationThenTranslation()
        {
            var t = RslFrameTransform.FromComponents(
                Orientation(0.0, 0.0, 90.0), Cartesian(5.0, -2.0, 3.0), AngleUnit.Degrees);

            ThreeDCartesianCoordinates p = t.TransformPoint(Cartesian(1.0, 0.0, 0.0));

            Assert.That(p.X, Is.EqualTo(5.0).Within(kTolerance));
            Assert.That(p.Y, Is.EqualTo(-1.0).Within(kTolerance));
            Assert.That(p.Z, Is.EqualTo(3.0).Within(kTolerance));
        }

        [Test]
        public void RoundTripRecoversAnglesAndTranslation()
        {
            ThreeDFrame frame = new()
            {
                CartesianCoordinates = Cartesian(1.5, -2.5, 3.5),
                Orientation = Orientation(10.0, 20.0, 30.0)
            };

            var t = RslFrameTransform.FromFrame(frame, AngleUnit.Degrees);
            ThreeDFrame recovered = t.ToFrame(AngleUnit.Degrees);

            Assert.That(recovered.Orientation.A, Is.EqualTo(10.0).Within(1e-9));
            Assert.That(recovered.Orientation.B, Is.EqualTo(20.0).Within(1e-9));
            Assert.That(recovered.Orientation.C, Is.EqualTo(30.0).Within(1e-9));
            Assert.That(recovered.CartesianCoordinates.X, Is.EqualTo(1.5).Within(kTolerance));
            Assert.That(recovered.CartesianCoordinates.Y, Is.EqualTo(-2.5).Within(kTolerance));
            Assert.That(recovered.CartesianCoordinates.Z, Is.EqualTo(3.5).Within(kTolerance));
        }

        [Test]
        public void RadiansAndDegreesAreConsistent()
        {
            var degrees = RslFrameTransform.FromComponents(
                Orientation(30.0, 45.0, 60.0), Cartesian(0.0, 0.0, 0.0), AngleUnit.Degrees);
            var radians = RslFrameTransform.FromComponents(
                Orientation(System.Math.PI / 6.0, System.Math.PI / 4.0, System.Math.PI / 3.0),
                Cartesian(0.0, 0.0, 0.0), AngleUnit.Radians);

            ThreeDCartesianCoordinates a = degrees.TransformPoint(Cartesian(1.0, 2.0, 3.0));
            ThreeDCartesianCoordinates b = radians.TransformPoint(Cartesian(1.0, 2.0, 3.0));

            Assert.That(Distance(a, b), Is.LessThan(1e-12));
        }

        [Test]
        public void InverseUndoesTransform()
        {
            var t = RslFrameTransform.FromComponents(
                Orientation(15.0, -35.0, 80.0), Cartesian(7.0, 8.0, -9.0), AngleUnit.Degrees);

            ThreeDCartesianCoordinates original = Cartesian(3.0, -4.0, 5.0);
            ThreeDCartesianCoordinates mapped = t.TransformPoint(original);
            ThreeDCartesianCoordinates back = t.Inverse().TransformPoint(mapped);

            Assert.That(Distance(back, original), Is.LessThan(1e-10));
        }

        [Test]
        public void InverseComposedWithTransformIsIdentity()
        {
            var t = RslFrameTransform.FromComponents(
                Orientation(22.0, 33.0, 44.0), Cartesian(1.0, 2.0, 3.0), AngleUnit.Degrees);

            RslFrameTransform identity = t.Inverse().Compose(t);
            ThreeDCartesianCoordinates p = identity.TransformPoint(Cartesian(9.0, -8.0, 7.0));

            Assert.That(p.X, Is.EqualTo(9.0).Within(1e-10));
            Assert.That(p.Y, Is.EqualTo(-8.0).Within(1e-10));
            Assert.That(p.Z, Is.EqualTo(7.0).Within(1e-10));
        }

        [Test]
        public void GimbalLockResolvesRollToZeroDeterministically()
        {
            var original = RslFrameTransform.FromComponents(
                Orientation(30.0, 90.0, 40.0), Cartesian(0.0, 0.0, 0.0), AngleUnit.Degrees);

            ThreeDFrame decomposed = original.ToFrame(AngleUnit.Degrees);

            // At the pitch singularity the roll is deterministically folded to
            // zero and the pitch is exactly +90 degrees.
            Assert.That(decomposed.Orientation.A, Is.Zero.Within(1e-9));
            Assert.That(decomposed.Orientation.B, Is.EqualTo(90.0).Within(1e-9));

            // Even though the recovered angles differ, they rebuild the same
            // rotation (same action on every basis vector).
            var rebuilt = RslFrameTransform.FromFrame(decomposed, AngleUnit.Degrees);
            foreach (ThreeDCartesianCoordinates basis in new[]
            {
                Cartesian(1.0, 0.0, 0.0),
                Cartesian(0.0, 1.0, 0.0),
                Cartesian(0.0, 0.0, 1.0)
            })
            {
                Assert.That(
                    Distance(original.TransformPoint(basis), rebuilt.TransformPoint(basis)),
                    Is.LessThan(1e-10));
            }
        }

        [Test]
        public void NegativePitchGimbalLockIsHandled()
        {
            var original = RslFrameTransform.FromComponents(
                Orientation(25.0, -90.0, 65.0), Cartesian(0.0, 0.0, 0.0), AngleUnit.Degrees);

            ThreeDFrame decomposed = original.ToFrame(AngleUnit.Degrees);
            Assert.That(decomposed.Orientation.A, Is.Zero.Within(1e-9));
            Assert.That(decomposed.Orientation.B, Is.EqualTo(-90.0).Within(1e-9));

            var rebuilt = RslFrameTransform.FromFrame(decomposed, AngleUnit.Degrees);
            Assert.That(
                Distance(
                    original.TransformPoint(Cartesian(1.0, 2.0, 3.0)),
                    rebuilt.TransformPoint(Cartesian(1.0, 2.0, 3.0))),
                Is.LessThan(1e-10));
        }

        [Test]
        public void IdentityTransformLeavesPointUnchanged()
        {
            ThreeDCartesianCoordinates p = RslFrameTransform.Identity.TransformPoint(Cartesian(4.0, 5.0, 6.0));
            Assert.That(p.X, Is.EqualTo(4.0).Within(kTolerance));
            Assert.That(p.Y, Is.EqualTo(5.0).Within(kTolerance));
            Assert.That(p.Z, Is.EqualTo(6.0).Within(kTolerance));
        }

        [Test]
        public void RotationMatrixIsProperOrthonormal()
        {
            var t = RslFrameTransform.FromComponents(
                Orientation(12.0, -34.0, 56.0), Cartesian(0.0, 0.0, 0.0), AngleUnit.Degrees);

            ArrayOf<double> r = t.RotationMatrix;
            Assert.That(r.Count, Is.EqualTo(9));

            // Column norms are one (orthonormal rotation).
            double col0 = (r[0] * r[0]) + (r[3] * r[3]) + (r[6] * r[6]);
            double col1 = (r[1] * r[1]) + (r[4] * r[4]) + (r[7] * r[7]);
            double col2 = (r[2] * r[2]) + (r[5] * r[5]) + (r[8] * r[8]);
            Assert.That(col0, Is.EqualTo(1.0).Within(1e-12));
            Assert.That(col1, Is.EqualTo(1.0).Within(1e-12));
            Assert.That(col2, Is.EqualTo(1.0).Within(1e-12));
        }
    }
}
