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

using Opc.Ua.Gpos;

namespace Opc.Ua.Positioning
{
    /// <summary>
    /// Helpers for reading and building <see cref="S3DGeographicCoordinateDataType"/>
    /// values, in particular the optional elevation field that is guarded by the
    /// <see cref="S3DGeographicCoordinateDataTypeFields.Elevation"/> encoding mask
    /// bit.
    /// </summary>
    internal static class GeographicCoordinates
    {
        /// <summary>
        /// Returns true when the geographic coordinate carries an elevation.
        /// </summary>
        public static bool HasElevation(S3DGeographicCoordinateDataType coordinate)
        {
            return (coordinate.EncodingMask & (uint)S3DGeographicCoordinateDataTypeFields.Elevation) != 0;
        }

        /// <summary>
        /// Creates a geographic coordinate with longitude and latitude only (no
        /// elevation set on the encoding mask).
        /// </summary>
        public static S3DGeographicCoordinateDataType Create(double longitude, double latitude)
        {
            return new()
            {
                EncodingMask = (uint)S3DGeographicCoordinateDataTypeFields.None,
                Longitude = longitude,
                Latitude = latitude,
                Elevation = 0.0
            };
        }

        /// <summary>
        /// Creates a geographic coordinate with longitude, latitude and
        /// elevation (the elevation encoding mask bit is set).
        /// </summary>
        public static S3DGeographicCoordinateDataType Create(double longitude, double latitude, double elevation)
        {
            return new()
            {
                EncodingMask = (uint)S3DGeographicCoordinateDataTypeFields.Elevation,
                Longitude = longitude,
                Latitude = latitude,
                Elevation = elevation
            };
        }
    }
}
