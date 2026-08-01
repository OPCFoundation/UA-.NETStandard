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
    /// Extension point that converts between a geographic coordinate reference
    /// system (a <see cref="S3DGeographicCoordinateDataType"/> of longitude,
    /// latitude and optional elevation) and an earth centered earth fixed (ECEF)
    /// geocentric cartesian coordinate.
    /// <para>
    /// The built in <see cref="Wgs84CoordinateReferenceSystemTransformer"/>
    /// implements the WGS84 / EPSG:4326 datum. Provide a custom implementation to
    /// support a different datum or CRS and pass it to the positioning APIs.
    /// </para>
    /// </summary>
    public interface ICoordinateReferenceSystemTransformer
    {
        /// <summary>
        /// A stable identifier of the coordinate reference system, for example
        /// <c>"EPSG:4326"</c>.
        /// </summary>
        string CoordinateReferenceSystem { get; }

        /// <summary>
        /// Converts a geographic coordinate to an ECEF cartesian coordinate.
        /// When the geographic coordinate carries no elevation it is treated as
        /// lying on the reference ellipsoid (height zero); no elevation is
        /// fabricated on the result.
        /// </summary>
        /// <param name="geographic">
        /// The geographic coordinate to convert.
        /// </param>
        /// <param name="angleUnit">
        /// The unit the longitude and latitude are expressed in.
        /// </param>
        ThreeDCartesianCoordinates ToEarthCenteredEarthFixed(
            S3DGeographicCoordinateDataType geographic, AngleUnit angleUnit);

        /// <summary>
        /// Converts an ECEF cartesian coordinate to a geographic coordinate.
        /// </summary>
        /// <param name="earthCenteredEarthFixed">
        /// The ECEF coordinate to convert.
        /// </param>
        /// <param name="angleUnit">
        /// The unit the returned longitude and latitude are expressed in.
        /// </param>
        /// <param name="includeElevation">
        /// When true the ellipsoidal height is written to the elevation field of
        /// the result; when false the elevation field is omitted so no elevation
        /// is fabricated for horizontal only workflows.
        /// </param>
        S3DGeographicCoordinateDataType FromEarthCenteredEarthFixed(
            ThreeDCartesianCoordinates earthCenteredEarthFixed, AngleUnit angleUnit, bool includeElevation);
    }
}
