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
using Opc.Ua.Gpos;

namespace Opc.Ua.Positioning
{
    /// <summary>
    /// Fits a transform between a local engineering frame and a geographic
    /// coordinate reference system from a set of ground control points. The fit
    /// is carried out in a metric East-North-Up tangent plane placed at the
    /// centroid of the control points so that the numerics stay well
    /// conditioned.
    /// <para>
    /// Three fit modes are supported. <see cref="GroundControlPointFitMode.Rigid"/>
    /// (the default) and <see cref="GroundControlPointFitMode.Similarity"/> always
    /// return a proper rotation (determinant +1) and correctly handle mirrored
    /// inputs by returning the closest proper rotation rather than a reflection.
    /// <see cref="GroundControlPointFitMode.Affine"/> estimates a general linear
    /// map and rejects reflections (negative determinant) unless
    /// <see cref="GroundControlPointFitOptions.AllowReflection"/> is set.
    /// </para>
    /// A three dimensional fit is only attempted when every control point carries
    /// an elevation and the configuration rank permits it; otherwise a horizontal
    /// only two dimensional fit is performed and no elevation is fabricated.
    /// </summary>
    public sealed class GroundControlPointFitter
    {
        private readonly ICoordinateReferenceSystemTransformer m_crs;

        private const double kRankRelativeTolerance = 1e-9;
        private const double kConditionLimit = 1e12;

        /// <summary>
        /// Creates a fitter using the given coordinate reference system, or WGS84
        /// when none is supplied.
        /// </summary>
        /// <param name="coordinateReferenceSystem">
        /// The coordinate reference system transformer to use; defaults to WGS84.
        /// </param>
        public GroundControlPointFitter(
            ICoordinateReferenceSystemTransformer? coordinateReferenceSystem = null)
        {
            m_crs = coordinateReferenceSystem ?? Wgs84CoordinateReferenceSystemTransformer.Instance;
        }

        /// <summary>
        /// Fits a transform from the supplied ground control points.
        /// </summary>
        /// <param name="controlPoints">
        /// The ground control points that pair a local position with a geographic
        /// position.
        /// </param>
        /// <param name="options">
        /// The fit options; when null the defaults (rigid fit, degrees) are used.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when the inputs are insufficient, duplicated, non-finite, rank
        /// deficient, ill-conditioned or would produce a non-invertible or
        /// reflected transform that the options do not allow.
        /// </exception>
        public GroundControlPointFitResult Fit(
            ArrayOf<GroundControlPointDataType> controlPoints,
            GroundControlPointFitOptions? options = null)
        {
            options ??= new GroundControlPointFitOptions();
            ICoordinateReferenceSystemTransformer crs = options.CoordinateReferenceSystem ?? m_crs;
            AngleUnit unit = options.AngleUnit;

            if (controlPoints.IsNull || controlPoints.Count < 2)
            {
                throw new ArgumentException(
                    "At least two ground control points are required for a fit.", nameof(controlPoints));
            }

            int n = controlPoints.Count;
            double[,] local = new double[n, 3];
            var geographic = new S3DGeographicCoordinateDataType[n];
            bool allElevations = true;

            for (int i = 0; i < n; i++)
            {
                GroundControlPointDataType gcp = controlPoints[i]
                    ?? throw new ArgumentException(
                        $"Ground control point at index {i} is null.", nameof(controlPoints));
                ThreeDCartesianCoordinates lp = gcp.LocalPosition
                    ?? throw new ArgumentException(
                        $"Ground control point at index {i} has no local position.", nameof(controlPoints));
                S3DGeographicCoordinateDataType gp = gcp.GlobalPosition
                    ?? throw new ArgumentException(
                        $"Ground control point at index {i} has no global position.", nameof(controlPoints));

                if (!lp.X.IsFinite() || !lp.Y.IsFinite() || !lp.Z.IsFinite())
                {
                    throw new ArgumentException(
                        $"Ground control point at index {i} has a non-finite local position.",
                        nameof(controlPoints));
                }
                if (!gp.Longitude.IsFinite() || !gp.Latitude.IsFinite())
                {
                    throw new ArgumentException(
                        $"Ground control point at index {i} has a non-finite geographic position.",
                        nameof(controlPoints));
                }

                bool hasElevation = GeographicCoordinates.HasElevation(gp);
                if (hasElevation && !gp.Elevation.IsFinite())
                {
                    throw new ArgumentException(
                        $"Ground control point at index {i} has a non-finite elevation.",
                        nameof(controlPoints));
                }

                allElevations &= hasElevation;
                local[i, 0] = lp.X;
                local[i, 1] = lp.Y;
                local[i, 2] = lp.Z;
                geographic[i] = gp;
            }

            // Anchor the tangent plane at the geographic centroid (mean ECEF).
            double[] meanEcef = new double[3];
            for (int i = 0; i < n; i++)
            {
                ThreeDCartesianCoordinates e = crs.ToEarthCenteredEarthFixed(geographic[i], unit);
                meanEcef[0] += e.X;
                meanEcef[1] += e.Y;
                meanEcef[2] += e.Z;
            }
            var centroidEcef = new ThreeDCartesianCoordinates
            {
                X = meanEcef[0] / n,
                Y = meanEcef[1] / n,
                Z = meanEcef[2] / n
            };
            S3DGeographicCoordinateDataType origin =
                crs.FromEarthCenteredEarthFixed(centroidEcef, unit, true);
            var tangentPlane = new LocalTangentPlane(origin, unit, crs);

            double[,] enu = new double[n, 3];
            for (int i = 0; i < n; i++)
            {
                ThreeDCartesianCoordinates e = tangentPlane.GeographicToEnu(geographic[i], unit);
                enu[i, 0] = e.X;
                enu[i, 1] = e.Y;
                enu[i, 2] = e.Z;
            }

            RejectDuplicates(local, n, "local");
            RejectDuplicates(enu, n, "geographic");

            int dimension = SelectDimension(options.Mode, allElevations, local, enu, n, out int rank);

            return FitInDimension(options, tangentPlane, local, enu, n, dimension, rank);
        }

        private static int SelectDimension(
            GroundControlPointFitMode mode, bool allElevations, double[,] local, double[,] enu, int n, out int rank)
        {
            if (allElevations)
            {
                int rank3 = Math.Min(
                    ConfigurationRank(local, n, 3),
                    ConfigurationRank(enu, n, 3));
                if (MeetsRequirement(mode, 3, n, rank3))
                {
                    rank = rank3;
                    return 3;
                }
            }

            int rank2 = Math.Min(
                ConfigurationRank(local, n, 2),
                ConfigurationRank(enu, n, 2));
            if (!MeetsRequirement(mode, 2, n, rank2))
            {
                throw new ArgumentException(
                    "The ground control points are insufficient or rank deficient for the requested fit: " +
                    $"mode={mode}, points={n}, horizontal rank={rank2}.");
            }

            rank = rank2;
            return 2;
        }

        private static bool MeetsRequirement(GroundControlPointFitMode mode, int dimension, int count, int rank)
        {
            if (mode == GroundControlPointFitMode.Affine)
            {
                // A general affine map needs full rank and one more point than the
                // dimension.
                return count >= dimension + 1 && rank >= dimension;
            }

            // Rigid and similarity: the rotation is determined once the points
            // span at least a hyperplane (dimension - 1) and there are enough of
            // them.
            int minRank = Math.Max(1, dimension - 1);
            return count >= dimension && rank >= minRank;
        }

        private GroundControlPointFitResult FitInDimension(
            GroundControlPointFitOptions options,
            LocalTangentPlane tangentPlane,
            double[,] local,
            double[,] enu,
            int n,
            int dimension,
            int rank)
        {
            double[] sourceMean = Mean(local, n, dimension);
            double[] targetMean = Mean(enu, n, dimension);

            double[,] forward;
            double determinant;

            if (options.Mode == GroundControlPointFitMode.Affine)
            {
                forward = FitAffine(options, local, enu, n, dimension, sourceMean, targetMean, out determinant);
            }
            else
            {
                forward = FitRigidOrSimilarity(
                    options.Mode, local, enu, n, dimension, sourceMean, targetMean, out determinant);
            }

            double[] translation = new double[dimension];
            for (int a = 0; a < dimension; a++)
            {
                double sum = targetMean[a];
                for (int b = 0; b < dimension; b++)
                {
                    sum -= forward[a, b] * sourceMean[b];
                }
                translation[a] = sum;
            }

            bool isInvertible = SmallLinearAlgebra.TryInvert(forward, out double[,] inverse);
            if (!isInvertible)
            {
                throw new ArgumentException(
                    "The fitted transform is not invertible (the control point configuration is degenerate).");
            }

            ComputeResiduals(
                forward, translation, local, enu, n, dimension,
                out double rms, out double maxResidual);

            return new GroundControlPointFitResult(
                options.Mode, dimension, tangentPlane, forward, inverse, translation,
                rms, maxResidual, determinant, rank, isInvertible);
        }

        private static double[,] FitRigidOrSimilarity(
            GroundControlPointFitMode mode,
            double[,] local,
            double[,] enu,
            int n,
            int dimension,
            double[] sourceMean,
            double[] targetMean,
            out double determinant)
        {
            // Cross covariance target x source, normalised by the point count.
            double[,] sigma = new double[dimension, dimension];
            double sourceVariance = 0.0;
            for (int i = 0; i < n; i++)
            {
                for (int a = 0; a < dimension; a++)
                {
                    double qa = enu[i, a] - targetMean[a];
                    double pa = local[i, a] - sourceMean[a];
                    sourceVariance += pa * pa;
                    for (int b = 0; b < dimension; b++)
                    {
                        sigma[a, b] += qa * (local[i, b] - sourceMean[b]);
                    }
                }
            }
            for (int a = 0; a < dimension; a++)
            {
                for (int b = 0; b < dimension; b++)
                {
                    sigma[a, b] /= n;
                }
            }
            sourceVariance /= n;

            SmallLinearAlgebra.JacobiSvd(sigma, out double[,] u, out double[] s, out double[,] v);

            double detU = SmallLinearAlgebra.Determinant(u);
            double detV = SmallLinearAlgebra.Determinant(v);

            // Reflection correction so the rotation stays proper (det +1). This is
            // what makes a mirrored input resolve to the closest proper rotation
            // instead of an improper one.
            double[,] reflection = SmallLinearAlgebra.Identity(dimension);
            reflection[dimension - 1, dimension - 1] = detU * detV < 0.0 ? -1.0 : 1.0;

            double[,] rotation = SmallLinearAlgebra.Multiply(
                SmallLinearAlgebra.Multiply(u, reflection),
                SmallLinearAlgebra.Transpose(v));

            double scale = 1.0;
            if (mode == GroundControlPointFitMode.Similarity)
            {
                double traceDs = 0.0;
                for (int k = 0; k < dimension; k++)
                {
                    traceDs += s[k] * reflection[k, k];
                }
                scale = sourceVariance > 1e-300 ? traceDs / sourceVariance : 1.0;
                if (!scale.IsFinite() || scale <= 0.0)
                {
                    throw new ArgumentException(
                        "The similarity fit produced a non-positive scale; the control points are degenerate.");
                }
            }

            double[,] forward = new double[dimension, dimension];
            for (int a = 0; a < dimension; a++)
            {
                for (int b = 0; b < dimension; b++)
                {
                    forward[a, b] = scale * rotation[a, b];
                }
            }

            determinant = SmallLinearAlgebra.Determinant(forward);
            return forward;
        }

        private static double[,] FitAffine(
            GroundControlPointFitOptions options,
            double[,] local,
            double[,] enu,
            int n,
            int dimension,
            double[] sourceMean,
            double[] targetMean,
            out double determinant)
        {
            double[,] m = new double[dimension, dimension];
            double[,] cross = new double[dimension, dimension];
            for (int i = 0; i < n; i++)
            {
                for (int a = 0; a < dimension; a++)
                {
                    double pa = local[i, a] - sourceMean[a];
                    double qa = enu[i, a] - targetMean[a];
                    for (int b = 0; b < dimension; b++)
                    {
                        double pb = local[i, b] - sourceMean[b];
                        m[a, b] += pa * pb;
                        cross[a, b] += qa * pb;
                    }
                }
            }

            if (IsIllConditioned(m, dimension))
            {
                throw new ArgumentException(
                    "The ground control points are ill-conditioned or rank deficient for an affine fit.");
            }

            if (!SmallLinearAlgebra.TryInvert(m, out double[,] mInverse))
            {
                throw new ArgumentException(
                    "The ground control points are rank deficient for an affine fit.");
            }

            double[,] forward = SmallLinearAlgebra.Multiply(cross, mInverse);
            determinant = SmallLinearAlgebra.Determinant(forward);

            if (Math.Abs(determinant) < 1e-12)
            {
                throw new ArgumentException(
                    "The affine fit is not invertible (near-zero determinant).");
            }
            if (determinant < 0.0 && !options.AllowReflection)
            {
                throw new ArgumentException(
                    "The affine fit requires a reflection (negative determinant); " +
                    "set AllowReflection to accept it.");
            }

            return forward;
        }

        private static bool IsIllConditioned(double[,] m, int dimension)
        {
            SmallLinearAlgebra.JacobiSvd(m, out _, out double[] s, out _);
            double max = s[0];
            double min = s[dimension - 1];
            if (max <= 1e-300)
            {
                return true;
            }
            if (min <= max * kRankRelativeTolerance)
            {
                return true;
            }
            return max / min > kConditionLimit;
        }

        private static void ComputeResiduals(
            double[,] forward,
            double[] translation,
            double[,] local,
            double[,] enu,
            int n,
            int dimension,
            out double rootMeanSquare,
            out double maxResidual)
        {
            double sumSquares = 0.0;
            maxResidual = 0.0;
            for (int i = 0; i < n; i++)
            {
                double squared = 0.0;
                for (int a = 0; a < dimension; a++)
                {
                    double predicted = translation[a];
                    for (int b = 0; b < dimension; b++)
                    {
                        predicted += forward[a, b] * local[i, b];
                    }
                    double diff = predicted - enu[i, a];
                    squared += diff * diff;
                }
                sumSquares += squared;
                double distance = Math.Sqrt(squared);
                if (distance > maxResidual)
                {
                    maxResidual = distance;
                }
            }
            rootMeanSquare = Math.Sqrt(sumSquares / n);
        }

        private static double[] Mean(double[,] points, int n, int dimension)
        {
            double[] mean = new double[dimension];
            for (int i = 0; i < n; i++)
            {
                for (int a = 0; a < dimension; a++)
                {
                    mean[a] += points[i, a];
                }
            }
            for (int a = 0; a < dimension; a++)
            {
                mean[a] /= n;
            }
            return mean;
        }

        private static int ConfigurationRank(double[,] points, int n, int dimension)
        {
            double[] mean = Mean(points, n, dimension);
            double[,] covariance = new double[dimension, dimension];
            for (int i = 0; i < n; i++)
            {
                for (int a = 0; a < dimension; a++)
                {
                    double da = points[i, a] - mean[a];
                    for (int b = 0; b < dimension; b++)
                    {
                        covariance[a, b] += da * (points[i, b] - mean[b]);
                    }
                }
            }

            SmallLinearAlgebra.JacobiSvd(covariance, out _, out double[] s, out _);
            double max = s[0];
            if (max <= 1e-300)
            {
                return 0;
            }

            int rank = 0;
            for (int k = 0; k < dimension; k++)
            {
                if (s[k] > max * kRankRelativeTolerance)
                {
                    rank++;
                }
            }
            return rank;
        }

        private static void RejectDuplicates(double[,] points, int n, string label)
        {
            double scale = 0.0;
            for (int i = 0; i < n; i++)
            {
                double magnitude = Math.Sqrt(
                    (points[i, 0] * points[i, 0]) +
                    (points[i, 1] * points[i, 1]) +
                    (points[i, 2] * points[i, 2]));
                if (magnitude > scale)
                {
                    scale = magnitude;
                }
            }
            double tolerance = 1e-9 * (1.0 + scale);

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double dx = points[i, 0] - points[j, 0];
                    double dy = points[i, 1] - points[j, 1];
                    double dz = points[i, 2] - points[j, 2];
                    double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                    if (distance <= tolerance)
                    {
                        throw new ArgumentException(
                            $"Ground control points at indices {i} and {j} have duplicate {label} positions.");
                    }
                }
            }
        }
    }
}
