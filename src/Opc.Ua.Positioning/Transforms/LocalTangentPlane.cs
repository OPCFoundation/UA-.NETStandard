// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.Gpos;

namespace Opc.Ua.Positioning
{
    /// <summary>
    /// A local East-North-Up (ENU) tangent plane anchored at a geographic
    /// origin. It converts between the earth centered earth fixed (ECEF) frame
    /// of a coordinate reference system and a local metric ENU frame whose X axis
    /// points east, Y axis points north and Z axis points up along the ellipsoid
    /// normal at the origin.
    /// </summary>
    public sealed class LocalTangentPlane
    {
        private readonly Vector3 m_originEcef;
        private readonly Matrix3x3 m_ecefToEnu;
        private readonly Matrix3x3 m_enuToEcef;

        /// <summary>
        /// Creates a tangent plane anchored at the given geographic origin.
        /// </summary>
        /// <param name="origin">
        /// The geographic origin of the tangent plane.
        /// </param>
        /// <param name="angleUnit">
        /// The unit the origin longitude and latitude are expressed in.
        /// </param>
        /// <param name="coordinateReferenceSystem">
        /// The coordinate reference system to use; defaults to WGS84 when null.
        /// </param>
        public LocalTangentPlane(
            S3DGeographicCoordinateDataType origin,
            AngleUnit angleUnit,
            ICoordinateReferenceSystemTransformer? coordinateReferenceSystem = null)
        {
            origin.ThrowIfNull(nameof(origin));

            CoordinateReferenceSystem = coordinateReferenceSystem ?? Wgs84CoordinateReferenceSystemTransformer.Instance;
            Origin = origin;

            ThreeDCartesianCoordinates originEcef = CoordinateReferenceSystem.ToEarthCenteredEarthFixed(origin, angleUnit);
            m_originEcef = new Vector3(originEcef.X, originEcef.Y, originEcef.Z);

            double lon = AngleMath.ToRadians(origin.Longitude, angleUnit);
            double lat = AngleMath.ToRadians(origin.Latitude, angleUnit);

            double sinLon = Math.Sin(lon);
            double cosLon = Math.Cos(lon);
            double sinLat = Math.Sin(lat);
            double cosLat = Math.Cos(lat);

            // Rotation from ECEF into ENU at the origin.
            m_ecefToEnu = new Matrix3x3(
                -sinLon, cosLon, 0.0,
                -sinLat * cosLon, -sinLat * sinLon, cosLat,
                cosLat * cosLon, cosLat * sinLon, sinLat);

            m_enuToEcef = m_ecefToEnu.Transpose();
        }

        /// <summary>
        /// The geographic origin of the tangent plane.
        /// </summary>
        public S3DGeographicCoordinateDataType Origin { get; }

        /// <summary>
        /// The coordinate reference system used by the tangent plane.
        /// </summary>
        public ICoordinateReferenceSystemTransformer CoordinateReferenceSystem { get; }

        /// <summary>
        /// Converts an ECEF coordinate into the local ENU frame.
        /// </summary>
        /// <param name="earthCenteredEarthFixed">
        /// The ECEF coordinate to convert.
        /// </param>
        public ThreeDCartesianCoordinates EarthCenteredEarthFixedToEnu(
            ThreeDCartesianCoordinates earthCenteredEarthFixed)
        {
            earthCenteredEarthFixed.ThrowIfNull(
                nameof(earthCenteredEarthFixed));
            var ecef = new Vector3(
                earthCenteredEarthFixed.X, earthCenteredEarthFixed.Y, earthCenteredEarthFixed.Z);
            Vector3 enu = m_ecefToEnu.Transform(ecef - m_originEcef);
            return new ThreeDCartesianCoordinates { X = enu.X, Y = enu.Y, Z = enu.Z };
        }

        /// <summary>
        /// Converts a local ENU coordinate back into the ECEF frame.
        /// </summary>
        /// <param name="enu">
        /// The local ENU coordinate to convert.
        /// </param>
        public ThreeDCartesianCoordinates EnuToEarthCenteredEarthFixed(ThreeDCartesianCoordinates enu)
        {
            enu.ThrowIfNull(nameof(enu));
            var local = new Vector3(enu.X, enu.Y, enu.Z);
            Vector3 ecef = m_enuToEcef.Transform(local) + m_originEcef;
            return new ThreeDCartesianCoordinates { X = ecef.X, Y = ecef.Y, Z = ecef.Z };
        }

        /// <summary>
        /// Converts a geographic coordinate directly into the local ENU frame.
        /// </summary>
        /// <param name="geographic">
        /// The geographic coordinate to convert.
        /// </param>
        /// <param name="angleUnit">
        /// The unit the geographic longitude and latitude are expressed in.
        /// </param>
        public ThreeDCartesianCoordinates GeographicToEnu(
            S3DGeographicCoordinateDataType geographic, AngleUnit angleUnit)
        {
            geographic.ThrowIfNull(nameof(geographic));
            return EarthCenteredEarthFixedToEnu(CoordinateReferenceSystem.ToEarthCenteredEarthFixed(geographic, angleUnit));
        }

        /// <summary>
        /// Converts a local ENU coordinate directly into a geographic coordinate.
        /// </summary>
        /// <param name="enu">
        /// The local ENU coordinate to convert.
        /// </param>
        /// <param name="angleUnit">
        /// The unit the returned geographic longitude and latitude are expressed in.
        /// </param>
        /// <param name="includeElevation">
        /// When true the ellipsoidal height is written to the elevation field of
        /// the result.
        /// </param>
        public S3DGeographicCoordinateDataType EnuToGeographic(
            ThreeDCartesianCoordinates enu, AngleUnit angleUnit, bool includeElevation)
        {
            enu.ThrowIfNull(nameof(enu));
            return CoordinateReferenceSystem.FromEarthCenteredEarthFixed(
                EnuToEarthCenteredEarthFixed(enu), angleUnit, includeElevation);
        }
    }
}
