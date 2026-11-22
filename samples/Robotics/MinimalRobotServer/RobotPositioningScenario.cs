// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Gpos;
using Opc.Ua.Positioning;

namespace Robotics
{
    /// <summary>
    /// Stable Zone control points and fitted local/global transform for the sample.
    /// </summary>
    public sealed class RobotPositioningScenario
    {
        public const string ZoneId = "RobotCellZone";

        public RobotPositioningScenario()
        {
            var origin = new S3DGeographicCoordinateDataType
            {
                EncodingMask =
                    (uint)S3DGeographicCoordinateDataTypeFields.Elevation,
                Longitude = 11.576124,
                Latitude = 48.137154,
                Elevation = 520.0
            };
            var plane = new LocalTangentPlane(origin, AngleUnit.Degrees);
            var points = new List<GroundControlPointDataType>();
            foreach (ThreeDCartesianCoordinates local in new[]
            {
                new ThreeDCartesianCoordinates { X = -5.0, Y = -5.0, Z = 0.0 },
                new ThreeDCartesianCoordinates { X = 5.0, Y = -5.0, Z = 0.0 },
                new ThreeDCartesianCoordinates { X = 5.0, Y = 5.0, Z = 0.0 },
                new ThreeDCartesianCoordinates { X = -5.0, Y = 5.0, Z = 0.0 },
                new ThreeDCartesianCoordinates { X = 0.0, Y = 0.0, Z = 2.0 }
            })
            {
                points.Add(new GroundControlPointDataType
                {
                    LocalPosition = local,
                    GlobalPosition = plane.EnuToGeographic(
                        local,
                        AngleUnit.Degrees,
                        includeElevation: true)
                });
            }

            GroundControlPoints =
                ArrayOf.Create(points.ToArray());
            Fit = new GroundControlPointFitter().Fit(
                GroundControlPoints,
                new GroundControlPointFitOptions
                {
                    Mode = GroundControlPointFitMode.Rigid,
                    AngleUnit = AngleUnit.Degrees
                });
        }

        public ArrayOf<GroundControlPointDataType> GroundControlPoints { get; }

        public GroundControlPointFitResult Fit { get; }
    }
}
