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
    /// The built in <see cref="ICoordinateReferenceSystemTransformer"/> for the
    /// WGS84 datum (EPSG:4326). Longitude and latitude are geodetic angles and
    /// elevation is the ellipsoidal height in metres. The conversions use the
    /// standard WGS84 ellipsoid parameters and have no external dependencies, so
    /// the type is safe for Native AOT.
    /// </summary>
    public sealed class Wgs84CoordinateReferenceSystemTransformer : ICoordinateReferenceSystemTransformer
    {
        /// <summary>
        /// WGS84 semi-major axis in metres.
        /// </summary>
        internal const double SemiMajorAxis = 6378137.0;

        /// <summary>
        /// WGS84 inverse flattening.
        /// </summary>
        internal const double InverseFlattening = 298.257223563;

        private const double kFlattening = 1.0 / InverseFlattening;
        private const double kFirstEccentricitySquared = kFlattening * (2.0 - kFlattening);
        private const double kSemiMinorAxis = SemiMajorAxis * (1.0 - kFlattening);

        /// <summary>
        /// A shared, thread safe instance of the WGS84 transformer.
        /// </summary>
        public static Wgs84CoordinateReferenceSystemTransformer Instance { get; } = new();

        /// <inheritdoc/>
        public string CoordinateReferenceSystem => "EPSG:4326";

        /// <inheritdoc/>
        public ThreeDCartesianCoordinates ToEarthCenteredEarthFixed(
            S3DGeographicCoordinateDataType geographic, AngleUnit angleUnit)
        {
            geographic.ThrowIfNull(nameof(geographic));

            double lon = AngleMath.ToRadians(geographic.Longitude, angleUnit);
            double lat = AngleMath.ToRadians(geographic.Latitude, angleUnit);
            double h = GeographicCoordinates.HasElevation(geographic) ? geographic.Elevation : 0.0;

            double sinLat = Math.Sin(lat);
            double cosLat = Math.Cos(lat);
            double sinLon = Math.Sin(lon);
            double cosLon = Math.Cos(lon);

            double n = SemiMajorAxis / Math.Sqrt(1.0 - (kFirstEccentricitySquared * sinLat * sinLat));

            double x = (n + h) * cosLat * cosLon;
            double y = (n + h) * cosLat * sinLon;
            double z = ((n * (1.0 - kFirstEccentricitySquared)) + h) * sinLat;

            return new ThreeDCartesianCoordinates { X = x, Y = y, Z = z };
        }

        /// <inheritdoc/>
        public S3DGeographicCoordinateDataType FromEarthCenteredEarthFixed(
            ThreeDCartesianCoordinates earthCenteredEarthFixed, AngleUnit angleUnit, bool includeElevation)
        {
            earthCenteredEarthFixed.ThrowIfNull(
                nameof(earthCenteredEarthFixed));

            double x = earthCenteredEarthFixed.X;
            double y = earthCenteredEarthFixed.Y;
            double z = earthCenteredEarthFixed.Z;

            double lon = Math.Atan2(y, x);
            double p = Math.Sqrt((x * x) + (y * y));

            double lat;
            double height;

            if (p < 1e-9)
            {
                // On the polar axis latitude is +/- 90 degrees.
                lat = z >= 0.0 ? Math.PI / 2.0 : -Math.PI / 2.0;
                height = Math.Abs(z) - kSemiMinorAxis;
                lon = 0.0;
            }
            else
            {
                // Iterative solution converging in a few steps for terrestrial
                // heights.
                lat = Math.Atan2(z, p * (1.0 - kFirstEccentricitySquared));
                double n = SemiMajorAxis;
                for (int i = 0; i < 10; i++)
                {
                    double sinLat = Math.Sin(lat);
                    n = SemiMajorAxis / Math.Sqrt(1.0 - (kFirstEccentricitySquared * sinLat * sinLat));
                    double h = (p / Math.Cos(lat)) - n;
                    double newLat = Math.Atan2(z, p * (1.0 - (kFirstEccentricitySquared * n / (n + h))));
                    if (Math.Abs(newLat - lat) < 1e-14)
                    {
                        lat = newLat;
                        break;
                    }
                    lat = newLat;
                }
                height = (p / Math.Cos(lat)) - n;
            }

            double longitude = AngleMath.FromRadians(lon, angleUnit);
            double latitude = AngleMath.FromRadians(lat, angleUnit);

            return includeElevation
                ? GeographicCoordinates.Create(longitude, latitude, height)
                : GeographicCoordinates.Create(longitude, latitude);
        }
    }
}
