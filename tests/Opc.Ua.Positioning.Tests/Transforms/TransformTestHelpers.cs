// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
