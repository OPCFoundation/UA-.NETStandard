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

using NUnit.Framework;
using Opc.Ua.Gpos;
using static Opc.Ua.Positioning.Tests.Transforms.TransformTestHelpers;

namespace Opc.Ua.Positioning.Tests.Transforms
{
    /// <summary>
    /// Tests for the WGS84 (EPSG:4326) geodetic to ECEF conversion and the local
    /// ENU tangent plane.
    /// </summary>
    [TestFixture]
    [Category("Positioning")]
    public sealed class Wgs84TransformTests
    {
        private const double kSemiMajorAxis = 6378137.0;
        private const double kSemiMinorAxis = 6356752.314245179;

        private static S3DGeographicCoordinateDataType Geo(double lon, double lat, double elevation)
        {
            return new()
            {
                EncodingMask = (uint)S3DGeographicCoordinateDataTypeFields.Elevation,
                Longitude = lon,
                Latitude = lat,
                Elevation = elevation
            };
        }

        [Test]
        public void EquatorPrimeMeridianMapsToSemiMajorAxisOnX()
        {
            Wgs84CoordinateReferenceSystemTransformer crs =
                Wgs84CoordinateReferenceSystemTransformer.Instance;

            ThreeDCartesianCoordinates ecef =
                crs.ToEarthCenteredEarthFixed(Geo(0.0, 0.0, 0.0), AngleUnit.Degrees);

            Assert.That(ecef.X, Is.EqualTo(kSemiMajorAxis).Within(1e-3));
            Assert.That(ecef.Y, Is.Zero.Within(1e-6));
            Assert.That(ecef.Z, Is.Zero.Within(1e-6));
        }

        [Test]
        public void NorthPoleMapsToSemiMinorAxisOnZ()
        {
            Wgs84CoordinateReferenceSystemTransformer crs =
                Wgs84CoordinateReferenceSystemTransformer.Instance;

            ThreeDCartesianCoordinates ecef =
                crs.ToEarthCenteredEarthFixed(Geo(0.0, 90.0, 0.0), AngleUnit.Degrees);

            Assert.That(ecef.Z, Is.EqualTo(kSemiMinorAxis).Within(1e-3));
            Assert.That(ecef.X, Is.Zero.Within(1e-6));
            Assert.That(ecef.Y, Is.Zero.Within(1e-6));
        }

        [Test]
        public void NinetyEastMapsToSemiMajorAxisOnY()
        {
            Wgs84CoordinateReferenceSystemTransformer crs =
                Wgs84CoordinateReferenceSystemTransformer.Instance;

            ThreeDCartesianCoordinates ecef =
                crs.ToEarthCenteredEarthFixed(Geo(90.0, 0.0, 0.0), AngleUnit.Degrees);

            Assert.That(ecef.Y, Is.EqualTo(kSemiMajorAxis).Within(1e-3));
            Assert.That(ecef.X, Is.Zero.Within(1e-6));
            Assert.That(ecef.Z, Is.Zero.Within(1e-6));
        }

        [Test]
        public void GeodeticEcefRoundTrip()
        {
            Wgs84CoordinateReferenceSystemTransformer crs =
                Wgs84CoordinateReferenceSystemTransformer.Instance;

            S3DGeographicCoordinateDataType input = Geo(11.6, 48.1, 519.0);
            ThreeDCartesianCoordinates ecef = crs.ToEarthCenteredEarthFixed(input, AngleUnit.Degrees);
            S3DGeographicCoordinateDataType output =
                crs.FromEarthCenteredEarthFixed(ecef, AngleUnit.Degrees, includeElevation: true);

            Assert.That(output.Longitude, Is.EqualTo(11.6).Within(1e-9));
            Assert.That(output.Latitude, Is.EqualTo(48.1).Within(1e-9));
            Assert.That(output.Elevation, Is.EqualTo(519.0).Within(1e-4));
            Assert.That(GeographicCoordinates.HasElevation(output), Is.True);
        }

        [Test]
        public void FromEcefWithoutElevationDoesNotFabricateElevation()
        {
            Wgs84CoordinateReferenceSystemTransformer crs =
                Wgs84CoordinateReferenceSystemTransformer.Instance;

            ThreeDCartesianCoordinates ecef =
                crs.ToEarthCenteredEarthFixed(Geo(8.0, 47.0, 400.0), AngleUnit.Degrees);
            S3DGeographicCoordinateDataType output =
                crs.FromEarthCenteredEarthFixed(ecef, AngleUnit.Degrees, includeElevation: false);

            Assert.That(GeographicCoordinates.HasElevation(output), Is.False);
            Assert.That(output.Longitude, Is.EqualTo(8.0).Within(1e-9));
            Assert.That(output.Latitude, Is.EqualTo(47.0).Within(1e-9));
        }

        [Test]
        public void CoordinateReferenceSystemIsEpsg4326()
        {
            Assert.That(
                Wgs84CoordinateReferenceSystemTransformer.Instance.CoordinateReferenceSystem,
                Is.EqualTo("EPSG:4326"));
        }

        [Test]
        public void TangentPlaneOriginMapsToEnuZero()
        {
            var plane = new LocalTangentPlane(Geo(8.0, 47.0, 400.0), AngleUnit.Degrees);
            ThreeDCartesianCoordinates enu = plane.GeographicToEnu(Geo(8.0, 47.0, 400.0), AngleUnit.Degrees);

            Assert.That(enu.X, Is.Zero.Within(1e-6));
            Assert.That(enu.Y, Is.Zero.Within(1e-6));
            Assert.That(enu.Z, Is.Zero.Within(1e-6));
        }

        [Test]
        public void TangentPlaneAxesAtEquatorPrimeMeridian()
        {
            // At lat=0, lon=0 the ECEF origin is (a,0,0). East = +Y, North = +Z,
            // Up = +X.
            var plane = new LocalTangentPlane(Geo(0.0, 0.0, 0.0), AngleUnit.Degrees);

            ThreeDCartesianCoordinates east =
                plane.EarthCenteredEarthFixedToEnu(Cartesian(kSemiMajorAxis, 10.0, 0.0));
            ThreeDCartesianCoordinates north =
                plane.EarthCenteredEarthFixedToEnu(Cartesian(kSemiMajorAxis, 0.0, 10.0));
            ThreeDCartesianCoordinates up =
                plane.EarthCenteredEarthFixedToEnu(Cartesian(kSemiMajorAxis + 10.0, 0.0, 0.0));

            Assert.That(east.X, Is.EqualTo(10.0).Within(1e-6));
            Assert.That(north.Y, Is.EqualTo(10.0).Within(1e-6));
            Assert.That(up.Z, Is.EqualTo(10.0).Within(1e-6));
        }

        [Test]
        public void TangentPlaneEnuEcefRoundTrip()
        {
            var plane = new LocalTangentPlane(Geo(8.0, 47.0, 400.0), AngleUnit.Degrees);
            ThreeDCartesianCoordinates enu = Cartesian(123.0, -456.0, 78.0);

            ThreeDCartesianCoordinates ecef = plane.EnuToEarthCenteredEarthFixed(enu);
            ThreeDCartesianCoordinates back = plane.EarthCenteredEarthFixedToEnu(ecef);

            Assert.That(Distance(back, enu), Is.LessThan(1e-6));
        }

        [Test]
        public void TangentPlaneGeographicRoundTrip()
        {
            var plane = new LocalTangentPlane(Geo(8.0, 47.0, 400.0), AngleUnit.Degrees);
            S3DGeographicCoordinateDataType point = Geo(8.001, 47.001, 410.0);

            ThreeDCartesianCoordinates enu = plane.GeographicToEnu(point, AngleUnit.Degrees);
            S3DGeographicCoordinateDataType back =
                plane.EnuToGeographic(enu, AngleUnit.Degrees, includeElevation: true);

            Assert.That(GeographicDistanceMeters(point, back), Is.LessThan(1e-4));
        }
    }
}
