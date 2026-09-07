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
    /// The result of fitting ground control points: the estimated transform
    /// together with quality diagnostics. The transform maps between a local
    /// engineering frame and a geographic coordinate reference system through an
    /// East-North-Up tangent plane fitted at the control point centroid.
    /// <para>
    /// When <see cref="Dimension"/> is two the fit is a horizontal only fit; the
    /// local Z component is ignored on the forward mapping and no elevation is
    /// produced on the geographic output. The inverse mapping returns a local
    /// point with a zero Z component.
    /// </para>
    /// </summary>
    public sealed class GroundControlPointFitResult
    {
        private readonly LocalTangentPlane m_tangentPlane;
        private readonly double[,] m_forward;
        private readonly double[,] m_inverse;
        private readonly double[] m_translation;

        internal GroundControlPointFitResult(
            GroundControlPointFitMode mode,
            int dimension,
            LocalTangentPlane tangentPlane,
            double[,] forward,
            double[,] inverse,
            double[] translation,
            double rootMeanSquareError,
            double maxResidual,
            double determinant,
            int rank,
            bool isInvertible)
        {
            Mode = mode;
            Dimension = dimension;
            m_tangentPlane = tangentPlane;
            m_forward = forward;
            m_inverse = inverse;
            m_translation = translation;
            RootMeanSquareError = rootMeanSquareError;
            MaxResidual = maxResidual;
            Determinant = determinant;
            Rank = rank;
            IsInvertible = isInvertible;
        }

        /// <summary>
        /// The class of transform that was estimated.
        /// </summary>
        public GroundControlPointFitMode Mode { get; }

        /// <summary>
        /// The spatial dimension of the fit: three when a full three dimensional
        /// fit was possible (all control points carried an elevation and the rank
        /// permitted it), otherwise two for a horizontal only fit.
        /// </summary>
        public int Dimension { get; }

        /// <summary>
        /// The root mean square of the residual distances between the fitted and
        /// the observed geographic control points, in metres.
        /// </summary>
        public double RootMeanSquareError { get; }

        /// <summary>
        /// The largest residual distance between a fitted and an observed
        /// geographic control point, in metres.
        /// </summary>
        public double MaxResidual { get; }

        /// <summary>
        /// The determinant of the linear part of the fitted transform. It is +1
        /// for a rigid fit and the scale raised to the dimension for a similarity
        /// fit; for an affine fit it is the determinant of the estimated matrix.
        /// </summary>
        public double Determinant { get; }

        /// <summary>
        /// The numerical rank of the control point configuration in the fit
        /// dimension.
        /// </summary>
        public int Rank { get; }

        /// <summary>
        /// True when the fitted transform is invertible (so
        /// <see cref="GlobalToLocal"/> is well defined).
        /// </summary>
        public bool IsInvertible { get; }

        /// <summary>
        /// Maps a point from the local engineering frame to a geographic
        /// coordinate.
        /// </summary>
        /// <param name="local">
        /// The local point to map.
        /// </param>
        /// <param name="angleUnit">
        /// The unit the returned longitude and latitude are expressed in.
        /// </param>
        public S3DGeographicCoordinateDataType LocalToGlobal(
            ThreeDCartesianCoordinates local, AngleUnit angleUnit)
        {
            local.ThrowIfNull(nameof(local));

            Span<double> source = [local.X, local.Y, local.Z];
            Span<double> enu = stackalloc double[3];
            for (int k = 0; k < Dimension; k++)
            {
                double sum = m_translation[k];
                for (int j = 0; j < Dimension; j++)
                {
                    sum += m_forward[k, j] * source[j];
                }
                enu[k] = sum;
            }

            var enuCoordinate = new ThreeDCartesianCoordinates
            {
                X = enu[0],
                Y = enu[1],
                Z = Dimension == 3 ? enu[2] : 0.0
            };

            return m_tangentPlane.EnuToGeographic(enuCoordinate, angleUnit, Dimension == 3);
        }

        /// <summary>
        /// Maps a geographic coordinate to a point in the local engineering
        /// frame. Requires <see cref="IsInvertible"/> to be true.
        /// </summary>
        /// <param name="global">
        /// The geographic coordinate to map.
        /// </param>
        /// <param name="angleUnit">
        /// The unit the geographic longitude and latitude are expressed in.
        /// </param>
        /// <exception cref="InvalidOperationException"></exception>
        public ThreeDCartesianCoordinates GlobalToLocal(
            S3DGeographicCoordinateDataType global, AngleUnit angleUnit)
        {
            global.ThrowIfNull(nameof(global));
            if (!IsInvertible)
            {
                throw new InvalidOperationException(
                    "The fitted transform is not invertible; the global to local mapping is undefined.");
            }

            ThreeDCartesianCoordinates enu = m_tangentPlane.GeographicToEnu(global, angleUnit);
            Span<double> target = [enu.X, enu.Y, enu.Z];
            Span<double> local = stackalloc double[3];
            for (int k = 0; k < Dimension; k++)
            {
                double sum = 0.0;
                for (int j = 0; j < Dimension; j++)
                {
                    sum += m_inverse[k, j] * (target[j] - m_translation[j]);
                }
                local[k] = sum;
            }

            return new ThreeDCartesianCoordinates
            {
                X = local[0],
                Y = local[1],
                Z = Dimension == 3 ? local[2] : 0.0
            };
        }
    }
}
