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

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Gpos;
using static Opc.Ua.Positioning.Tests.Transforms.TransformTestHelpers;

namespace Opc.Ua.Positioning.Tests.Transforms
{
    /// <summary>
    /// Tests for <see cref="GroundControlPointFitter"/> covering the rigid,
    /// similarity and affine modes with exact, noisy, inverse, reflection and
    /// degenerate inputs, as well as the two/three dimensional adaptivity.
    /// </summary>
    [TestFixture]
    [Category("Positioning")]
    public sealed class GroundControlPointFitterTests
    {
        private static readonly S3DGeographicCoordinateDataType s_origin = new()
        {
            EncodingMask = (uint)S3DGeographicCoordinateDataTypeFields.Elevation,
            Longitude = 8.0,
            Latitude = 47.0,
            Elevation = 400.0
        };

        private static readonly double[][] s_localCloud =
        [
            [0.0, 0.0, 0.0],
            [10.0, 0.0, 0.0],
            [0.0, 10.0, 0.0],
            [0.0, 0.0, 10.0],
            [5.0, 5.0, 2.0],
            [3.0, 4.0, 8.0]
        ];

        private static readonly double[][] s_planarCloud =
        [
            [0.0, 0.0, 0.0],
            [10.0, 0.0, 0.0],
            [0.0, 10.0, 0.0],
            [10.0, 10.0, 0.0],
            [5.0, 3.0, 0.0],
            [3.0, 7.0, 0.0]
        ];

        private static LocalTangentPlane Plane()
        {
            return new(s_origin, AngleUnit.Degrees);
        }

        private static double[,] Rotation(double a, double b, double c)
        {
            ArrayOf<double> r = RslFrameTransform
                .FromComponents(Orientation(a, b, c), Cartesian(0.0, 0.0, 0.0), AngleUnit.Degrees)
                .RotationMatrix;
            return new double[,]
            {
                { r[0], r[1], r[2] },
                { r[3], r[4], r[5] },
                { r[6], r[7], r[8] }
            };
        }

        private static double[] ApplyLinear(double[,] a, double[] t, double[] p, double scale = 1.0)
        {
            return
            [
                (scale * ((a[0, 0] * p[0]) + (a[0, 1] * p[1]) + (a[0, 2] * p[2]))) + t[0],
                (scale * ((a[1, 0] * p[0]) + (a[1, 1] * p[1]) + (a[1, 2] * p[2]))) + t[1],
                (scale * ((a[2, 0] * p[0]) + (a[2, 1] * p[1]) + (a[2, 2] * p[2]))) + t[2]
            ];
        }

        private static double Determinant3(double[,] a)
        {
            return (a[0, 0] * ((a[1, 1] * a[2, 2]) - (a[1, 2] * a[2, 1]))) -
                (a[0, 1] * ((a[1, 0] * a[2, 2]) - (a[1, 2] * a[2, 0]))) +
                (a[0, 2] * ((a[1, 0] * a[2, 1]) - (a[1, 1] * a[2, 0])));
        }

        private static GroundControlPointDataType Gcp(double[] local, double[] enu, bool elevation)
        {
            LocalTangentPlane plane = Plane();
            S3DGeographicCoordinateDataType geo = plane.EnuToGeographic(
                Cartesian(enu[0], enu[1], enu[2]), AngleUnit.Degrees, elevation);
            return new GroundControlPointDataType
            {
                LocalPosition = Cartesian(local[0], local[1], local[2]),
                GlobalPosition = geo
            };
        }

        private static ArrayOf<GroundControlPointDataType> BuildControlPoints(
            double[,] linear, double[] translation, double scale, bool elevation)
        {
            var points = new List<GroundControlPointDataType>();
            foreach (double[] local in s_localCloud)
            {
                double[] enu = ApplyLinear(linear, translation, local, scale);
                points.Add(Gcp(local, enu, elevation));
            }
            return ArrayOf.Create(points.ToArray());
        }

        [Test]
        public void RigidExactFitRecoversTransform()
        {
            double[,] rotation = Rotation(30.0, 10.0, 5.0);
            double[] translation = [2.0, -3.0, 1.0];
            ArrayOf<GroundControlPointDataType> gcps =
                BuildControlPoints(rotation, translation, 1.0, elevation: true);

            var fitter = new GroundControlPointFitter();
            GroundControlPointFitResult result = fitter.Fit(gcps);

            Assert.That(result.Mode, Is.EqualTo(GroundControlPointFitMode.Rigid));
            Assert.That(result.Dimension, Is.EqualTo(3));
            Assert.That(result.Rank, Is.EqualTo(3));
            Assert.That(result.IsInvertible, Is.True);
            Assert.That(result.Determinant, Is.EqualTo(1.0).Within(1e-6));
            Assert.That(result.RootMeanSquareError, Is.LessThan(1e-2));

            AssertRoundTrips(gcps, result, 1e-2);
        }

        [Test]
        public void SimilarityExactFitRecoversScale()
        {
            double[,] rotation = Rotation(-20.0, 15.0, 40.0);
            double[] translation = [5.0, 6.0, -7.0];
            const double scale = 1.7;
            ArrayOf<GroundControlPointDataType> gcps =
                BuildControlPoints(rotation, translation, scale, elevation: true);

            var options = new GroundControlPointFitOptions { Mode = GroundControlPointFitMode.Similarity };
            GroundControlPointFitResult result = new GroundControlPointFitter().Fit(gcps, options);

            Assert.That(result.Mode, Is.EqualTo(GroundControlPointFitMode.Similarity));
            Assert.That(result.Dimension, Is.EqualTo(3));
            Assert.That(result.Determinant, Is.EqualTo(scale * scale * scale).Within(1e-2));
            Assert.That(result.RootMeanSquareError, Is.LessThan(1e-2));

            AssertRoundTrips(gcps, result, 1e-2);
        }

        [Test]
        public void AffineExactFitRecoversTransform()
        {
            double[,] linear =
            {
                { 1.2, 0.1, 0.0 },
                { 0.0, 0.9, 0.05 },
                { 0.02, 0.0, 1.1 }
            };
            double[] translation = [1.0, 2.0, 3.0];
            ArrayOf<GroundControlPointDataType> gcps =
                BuildControlPoints(linear, translation, 1.0, elevation: true);

            var options = new GroundControlPointFitOptions { Mode = GroundControlPointFitMode.Affine };
            GroundControlPointFitResult result = new GroundControlPointFitter().Fit(gcps, options);

            Assert.That(result.Mode, Is.EqualTo(GroundControlPointFitMode.Affine));
            Assert.That(result.Dimension, Is.EqualTo(3));
            Assert.That(result.Determinant, Is.EqualTo(Determinant3(linear)).Within(1e-3));
            Assert.That(result.RootMeanSquareError, Is.LessThan(1e-2));

            AssertRoundTrips(gcps, result, 1e-2);
        }

        [Test]
        public void NoisyRigidFitStaysProperAndBounded()
        {
            double[,] rotation = Rotation(12.0, -8.0, 25.0);
            double[] translation = [1.0, 1.0, 1.0];

            var points = new List<GroundControlPointDataType>();
            double[] noise = [0.03, -0.04, 0.02, 0.05, -0.03, 0.01];
            for (int i = 0; i < s_localCloud.Length; i++)
            {
                double[] enu = ApplyLinear(rotation, translation, s_localCloud[i]);
                enu[0] += noise[i];
                enu[1] -= noise[i];
                enu[2] += noise[i] * 0.5;
                points.Add(Gcp(s_localCloud[i], enu, elevation: true));
            }
            var gcps = ArrayOf.Create(points.ToArray());

            GroundControlPointFitResult result = new GroundControlPointFitter().Fit(gcps);

            Assert.That(result.Determinant, Is.EqualTo(1.0).Within(1e-6));
            Assert.That(result.RootMeanSquareError, Is.GreaterThan(0.0));
            Assert.That(result.RootMeanSquareError, Is.LessThan(0.2));
            Assert.That(result.MaxResidual, Is.LessThan(0.5));
        }

        [Test]
        public void RigidFitOfMirroredInputReturnsProperRotation()
        {
            // A reflection (det -1) applied in ENU: a rigid fit must return the
            // closest proper rotation, not the improper one.
            double[,] reflection =
            {
                { 1.0, 0.0, 0.0 },
                { 0.0, 1.0, 0.0 },
                { 0.0, 0.0, -1.0 }
            };
            double[] translation = [0.0, 0.0, 0.0];
            ArrayOf<GroundControlPointDataType> gcps =
                BuildControlPoints(reflection, translation, 1.0, elevation: true);

            GroundControlPointFitResult result = new GroundControlPointFitter().Fit(gcps);

            // Proper rotation regardless of the mirrored data.
            Assert.That(result.Determinant, Is.GreaterThan(0.0));
            Assert.That(result.Determinant, Is.EqualTo(1.0).Within(1e-6));
            // The reflection cannot be represented, so the residual is large.
            Assert.That(result.RootMeanSquareError, Is.GreaterThan(1.0));
        }

        [Test]
        public void AffineRejectsReflectionByDefault()
        {
            double[,] reflection =
            {
                { 1.1, 0.0, 0.0 },
                { 0.0, 1.0, 0.0 },
                { 0.0, 0.0, -1.2 }
            };
            double[] translation = [0.0, 0.0, 0.0];
            ArrayOf<GroundControlPointDataType> gcps =
                BuildControlPoints(reflection, translation, 1.0, elevation: true);

            var options = new GroundControlPointFitOptions { Mode = GroundControlPointFitMode.Affine };
            Assert.That(() => new GroundControlPointFitter().Fit(gcps, options),
                Throws.ArgumentException);
        }

        [Test]
        public void AffineAllowsReflectionWhenRequested()
        {
            double[,] reflection =
            {
                { 1.1, 0.0, 0.0 },
                { 0.0, 1.0, 0.0 },
                { 0.0, 0.0, -1.2 }
            };
            double[] translation = [0.0, 0.0, 0.0];
            ArrayOf<GroundControlPointDataType> gcps =
                BuildControlPoints(reflection, translation, 1.0, elevation: true);

            var options = new GroundControlPointFitOptions
            {
                Mode = GroundControlPointFitMode.Affine,
                AllowReflection = true
            };
            GroundControlPointFitResult result = new GroundControlPointFitter().Fit(gcps, options);

            Assert.That(result.Determinant, Is.LessThan(0.0));
            Assert.That(result.Determinant, Is.EqualTo(Determinant3(reflection)).Within(1e-3));
            Assert.That(result.RootMeanSquareError, Is.LessThan(1e-2));
            AssertRoundTrips(gcps, result, 1e-2);
        }

        [Test]
        public void HorizontalFitWhenElevationMissing()
        {
            double[,] rotation = Rotation(0.0, 0.0, 35.0);
            double[] translation = [4.0, -5.0, 0.0];

            var points = new List<GroundControlPointDataType>();
            foreach (double[] local in s_planarCloud)
            {
                double[] planar = [local[0], local[1], 0.0];
                double[] enu = ApplyLinear(rotation, translation, planar);
                enu[2] = 0.0;
                points.Add(Gcp(planar, enu, elevation: false));
            }
            var gcps = ArrayOf.Create(points.ToArray());

            GroundControlPointFitResult result = new GroundControlPointFitter().Fit(gcps);

            Assert.That(result.Dimension, Is.EqualTo(2));
            Assert.That(result.Determinant, Is.EqualTo(1.0).Within(1e-6));

            // The forward mapping produces no elevation, the inverse produces a
            // zero Z (no fabricated elevation).
            S3DGeographicCoordinateDataType global =
                result.LocalToGlobal(Cartesian(2.0, 3.0, 99.0), AngleUnit.Degrees);
            Assert.That(GeographicCoordinates.HasElevation(global), Is.False);

            ThreeDCartesianCoordinates back = result.GlobalToLocal(global, AngleUnit.Degrees);
            Assert.That(back.Z, Is.Zero.Within(1e-9));
        }

        [Test]
        public void AffineWithCoplanarThreeDPointsFallsBackToHorizontal()
        {
            // Points carry elevation but are coplanar in the local frame (z=0),
            // so a full 3D affine fit is not possible and the fit drops to 2D.
            double[,] linear =
            {
                { 1.1, 0.05, 0.0 },
                { 0.0, 0.95, 0.0 },
                { 0.0, 0.0, 1.0 }
            };
            double[] translation = [1.0, 1.0, 5.0];

            var points = new List<GroundControlPointDataType>();
            foreach (double[] local in s_planarCloud)
            {
                double[] planar = [local[0], local[1], 0.0];
                double[] enu = ApplyLinear(linear, translation, planar);
                points.Add(Gcp(planar, enu, elevation: true));
            }
            var gcps = ArrayOf.Create(points.ToArray());

            var options = new GroundControlPointFitOptions { Mode = GroundControlPointFitMode.Affine };
            GroundControlPointFitResult result = new GroundControlPointFitter().Fit(gcps, options);

            Assert.That(result.Dimension, Is.EqualTo(2));
        }

        [Test]
        public void RejectsNullControlPoints()
        {
            Assert.That(() => new GroundControlPointFitter().Fit(ArrayOf<GroundControlPointDataType>.Null),
                Throws.ArgumentException);
        }

        [Test]
        public void RejectsInsufficientControlPoints()
        {
            var single = new GroundControlPointDataType
            {
                LocalPosition = Cartesian(0.0, 0.0, 0.0),
                GlobalPosition = s_origin
            };
            var gcps = ArrayOf.Create([single]);

            Assert.That(() => new GroundControlPointFitter().Fit(gcps), Throws.ArgumentException);
        }

        [Test]
        public void RejectsDuplicateLocalPositions()
        {
            GroundControlPointDataType a = Gcp([1.0, 2.0, 3.0], [1.0, 2.0, 3.0], elevation: true);
            GroundControlPointDataType b = Gcp([1.0, 2.0, 3.0], [4.0, 5.0, 6.0], elevation: true);
            GroundControlPointDataType c = Gcp([9.0, 8.0, 7.0], [7.0, 8.0, 9.0], elevation: true);
            var gcps = ArrayOf.Create([a, b, c]);

            Assert.That(() => new GroundControlPointFitter().Fit(gcps), Throws.ArgumentException);
        }

        [Test]
        public void RejectsNonFiniteLocalPosition()
        {
            var a = new GroundControlPointDataType
            {
                LocalPosition = Cartesian(double.NaN, 0.0, 0.0),
                GlobalPosition = s_origin
            };
            GroundControlPointDataType b = Gcp([1.0, 0.0, 0.0], [1.0, 0.0, 0.0], elevation: true);
            GroundControlPointDataType c = Gcp([0.0, 1.0, 0.0], [0.0, 1.0, 0.0], elevation: true);
            var gcps = ArrayOf.Create([a, b, c]);

            Assert.That(() => new GroundControlPointFitter().Fit(gcps), Throws.ArgumentException);
        }

        [Test]
        public void RejectsRankDeficientAffineInput()
        {
            // Collinear points (rank one) cannot support an affine fit.
            var points = new List<GroundControlPointDataType>();
            for (int i = 0; i < 4; i++)
            {
                double[] local = [i, 0.0, 0.0];
                double[] enu = [2.0 * i, 0.0, 0.0];
                points.Add(Gcp(local, enu, elevation: false));
            }
            var gcps = ArrayOf.Create(points.ToArray());

            var options = new GroundControlPointFitOptions { Mode = GroundControlPointFitMode.Affine };
            Assert.That(() => new GroundControlPointFitter().Fit(gcps, options), Throws.ArgumentException);
        }

        private static void AssertRoundTrips(
            ArrayOf<GroundControlPointDataType> gcps, GroundControlPointFitResult result, double toleranceMeters)
        {
            for (int i = 0; i < gcps.Count; i++)
            {
                GroundControlPointDataType gcp = gcps[i];
                S3DGeographicCoordinateDataType predicted =
                    result.LocalToGlobal(gcp.LocalPosition, AngleUnit.Degrees);
                Assert.That(
                    GeographicDistanceMeters(predicted, gcp.GlobalPosition),
                    Is.LessThan(toleranceMeters),
                    $"forward mapping mismatch at index {i}");

                ThreeDCartesianCoordinates back =
                    result.GlobalToLocal(gcp.GlobalPosition, AngleUnit.Degrees);
                Assert.That(
                    Distance(back, gcp.LocalPosition),
                    Is.LessThan(toleranceMeters),
                    $"inverse mapping mismatch at index {i}");
            }
        }
    }
}
