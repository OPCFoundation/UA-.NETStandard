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
