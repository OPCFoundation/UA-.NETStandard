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

namespace Opc.Ua.Positioning.Tests.Transforms
{
    /// <summary>
    /// Shared helpers for the positioning transform tests.
    /// </summary>
    internal static class TransformTestHelpers
    {
        public static ThreeDCartesianCoordinates Cartesian(double x, double y, double z)
        {
            return new() { X = x, Y = y, Z = z };
        }

        public static ThreeDOrientation Orientation(double a, double b, double c)
        {
            return new() { A = a, B = b, C = c };
        }

        public static double Distance(ThreeDCartesianCoordinates a, ThreeDCartesianCoordinates b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            double dz = a.Z - b.Z;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        public static double GeographicDistanceMeters(
            S3DGeographicCoordinateDataType a, S3DGeographicCoordinateDataType b)
        {
            Wgs84CoordinateReferenceSystemTransformer crs =
                Wgs84CoordinateReferenceSystemTransformer.Instance;
            ThreeDCartesianCoordinates ea = crs.ToEarthCenteredEarthFixed(a, AngleUnit.Degrees);
            ThreeDCartesianCoordinates eb = crs.ToEarthCenteredEarthFixed(b, AngleUnit.Degrees);
            return Distance(ea, eb);
        }
    }
}
