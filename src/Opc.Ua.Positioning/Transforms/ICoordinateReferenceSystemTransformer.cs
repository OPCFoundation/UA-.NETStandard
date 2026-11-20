/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
