/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
