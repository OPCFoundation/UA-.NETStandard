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
using System.Threading;
using Opc.Ua.Gpos;

namespace Opc.Ua.Positioning.Client
{
    internal sealed partial class PositioningClientOperations
    {
        /// <summary>
        /// Validates an RSL frame without changing its coordinates or angle convention.
        /// </summary>
        public void ValidateFrame(ThreeDFrame frame)
        {
            if (Strict)
            {
                ValidateCartesian(frame.CartesianCoordinates);
                ValidateOrientation(frame.Orientation);
            }
        }

        /// <summary>
        /// Uses the existing RSL transform implementation after validating strict inputs.
        /// </summary>
        public RslFrameTransform CreateFrameTransform(ThreeDFrame frame, AngleUnit angleUnit)
        {
            ValidateFrame(frame);
            ValidateAngleUnit(angleUnit);
            return RslFrameTransform.FromFrame(frame, angleUnit);
        }

        /// <summary>
        /// Rejects non-finite composed results rather than returning an invalid world transform.
        /// </summary>
        public void ValidateTransform(in RslFrameTransform transform)
        {
            if (!Strict)
            {
                return;
            }
            ValidateCartesian(transform.Translation);
            foreach (double value in transform.RotationMatrix)
            {
                if (!value.IsFinite())
                {
                    throw InvalidGeometry();
                }
            }
        }

        /// <summary>
        /// Validates optional fields and interprets WGS84 only when the metadata identifies it.
        /// Other CRS identifiers are retained without assuming they use geographic degrees.
        /// </summary>
        public void ValidateGlobalPosition(GlobalPositionDataType position, uint coordinateReferenceSystem)
        {
            if (!Strict)
            {
                return;
            }
            ValidateGeographic(position, AngleUnit.Degrees, coordinateReferenceSystem == 4326);
            if (((position.EncodingMask & (uint)GlobalPositionDataTypeFields.Accuracy) != 0 &&
                (!position.Accuracy.IsFinite() || position.Accuracy < 0)) ||
                ((position.EncodingMask & (uint)GlobalPositionDataTypeFields.Floor) != 0 &&
                    !((double)position.Floor).IsFinite()))
            {
                throw InvalidGeometry();
            }
        }

        /// <summary>
        /// Validates the required position and any encoded orientation of a global location.
        /// </summary>
        public void ValidateGlobalLocation(GlobalLocationDataType location)
        {
            if (!Strict)
            {
                return;
            }
            ValidateGlobalPosition(location.Position, 0);
            if ((location.EncodingMask & (uint)GlobalLocationDataTypeFields.Orientation) != 0)
            {
                ValidateOrientation(location.Orientation);
            }
        }

        /// <summary>
        /// Checks finite coordinates and WGS84 ranges; conversions stay in the existing transformer.
        /// Optional elevation is validated only when encoded.
        /// </summary>
        public void ValidateGeographic(
            S3DGeographicCoordinateDataType geographic,
            AngleUnit angleUnit,
            bool wgs84)
        {
            if (!Strict)
            {
                return;
            }
            ValidateAngleUnit(angleUnit);
            if (geographic is null ||
                !geographic.Longitude.IsFinite() ||
                !geographic.Latitude.IsFinite() ||
                ((geographic.EncodingMask & (uint)S3DGeographicCoordinateDataTypeFields.Elevation) != 0 &&
                    !geographic.Elevation.IsFinite()))
            {
                throw InvalidGeometry();
            }
            if (wgs84)
            {
                double longitudeLimit = angleUnit == AngleUnit.Degrees ? 180 : Math.PI;
                double latitudeLimit = angleUnit == AngleUnit.Degrees ? 90 : Math.PI / 2;
                if (Math.Abs(geographic.Longitude) > longitudeLimit || Math.Abs(geographic.Latitude) > latitudeLimit)
                {
                    throw InvalidGeometry();
                }
                ValidateCartesian(
                    Wgs84CoordinateReferenceSystemTransformer.Instance
                        .ToEarthCenteredEarthFixed(geographic, angleUnit));
            }
        }

        /// <summary>
        /// Validates geographic ranges before the existing fitter checks rank, duplicates and conditioning.
        /// </summary>
        public void ValidateGroundControlPoints(
            ArrayOf<GroundControlPointDataType> points,
            GroundControlPointFitOptions? options,
            CancellationToken cancellationToken)
        {
            if (!Strict)
            {
                return;
            }
            AngleUnit angleUnit = options?.AngleUnit ?? AngleUnit.Degrees;
            ValidateAngleUnit(angleUnit);
            if (options is not null &&
                options.Mode is not
                    (GroundControlPointFitMode.Rigid or
                    GroundControlPointFitMode.Similarity or
                    GroundControlPointFitMode.Affine))
            {
                throw InvalidGeometry();
            }
            bool wgs84 = options?.CoordinateReferenceSystem is null ||
                options.CoordinateReferenceSystem is Wgs84CoordinateReferenceSystemTransformer;
            for (int ii = 0; ii < points.Count; ii++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (points[ii] is null)
                {
                    throw InvalidGeometry();
                }
                ValidateGeographic(points[ii].GlobalPosition, angleUnit, wgs84);
            }
        }

        /// <summary>
        /// Rejects an invalid numerical result produced from extreme fitting inputs.
        /// </summary>
        public void ValidateFit(GroundControlPointFitResult result)
        {
            if (Strict &&
                (!result.IsInvertible ||
                    !result.RootMeanSquareError.IsFinite() ||
                    !result.MaxResidual.IsFinite() ||
                    !result.Determinant.IsFinite()))
            {
                throw InvalidGeometry();
            }
        }

        /// <summary>
        /// Requires finite Cartesian coordinates in strict mode.
        /// </summary>
        public void ValidateCartesian(ThreeDCartesianCoordinates coordinates)
        {
            if (Strict &&
                (coordinates is null ||
                    !coordinates.X.IsFinite() ||
                    !coordinates.Y.IsFinite() ||
                    !coordinates.Z.IsFinite()))
            {
                throw InvalidGeometry();
            }
        }

        /// <summary>
        /// Rejects unknown angle units rather than treating them as radians.
        /// </summary>
        public void ValidateAngleUnit(AngleUnit angleUnit)
        {
            if (Strict && angleUnit is not (AngleUnit.Degrees or AngleUnit.Radians))
            {
                throw InvalidGeometry();
            }
        }

        private static void ValidateOrientation(ThreeDOrientation orientation)
        {
            if (orientation is null ||
                !orientation.A.IsFinite() ||
                !orientation.B.IsFinite() ||
                !orientation.C.IsFinite())
            {
                throw InvalidGeometry();
            }
        }

        private static ServiceResultException InvalidGeometry()
        {
            return new ServiceResultException(
                StatusCodes.BadInvalidArgument,
                "The Positioning geometry is invalid or non-finite.");
        }
    }
}
