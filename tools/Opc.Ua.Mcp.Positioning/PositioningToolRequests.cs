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
using System.ComponentModel;
using System.Text.Json.Serialization;
using Opc.Ua.Positioning;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Selects an RSL discovery operation.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<RelativePositioningDiscovery>))]
    public enum RelativePositioningDiscovery
    {
        /// <summary>
        /// Lists in RelativeSpatialLocations.
        /// </summary>
        SpatialObjectLists,

        /// <summary>
        /// Objects in a spatial object list.
        /// </summary>
        SpatialObjects,

        /// <summary>
        /// Frames in a specified folder.
        /// </summary>
        Frames
    }

    /// <summary>
    /// Selects an RSL frame read.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<RelativePositioningRead>))]
    public enum RelativePositioningRead
    {
        /// <summary>
        /// The PositionFrame of a spatial object.
        /// </summary>
        PositionFrame,

        /// <summary>
        /// A frame variable.
        /// </summary>
        Frame,

        /// <summary>
        /// A frame resolved through its base-frame chain.
        /// </summary>
        WorldFrame
    }

    /// <summary>
    /// Selects an RSL notification stream.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<RelativePositioningObservation>))]
    public enum RelativePositioningObservation
    {
        /// <summary>
        /// PositionFrame changes on a spatial object.
        /// </summary>
        PositionFrame,

        /// <summary>
        /// Frame variable changes.
        /// </summary>
        Frame,

        /// <summary>
        /// NodeVersion changes on a spatial object list.
        /// </summary>
        NodeVersion
    }

    /// <summary>
    /// Selects a GPOS read.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<GlobalPositioningRead>))]
    public enum GlobalPositioningRead
    {
        /// <summary>
        /// A GlobalPosition variable.
        /// </summary>
        GlobalPosition,

        /// <summary>
        /// A GlobalLocation variable.
        /// </summary>
        GlobalLocation,

        /// <summary>
        /// A fit to a Zone's GroundControlPoints.
        /// </summary>
        ZoneTransform
    }

    /// <summary>
    /// Selects a GPOS notification stream.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<GlobalPositioningObservation>))]
    public enum GlobalPositioningObservation
    {
        /// <summary>
        /// GlobalPosition changes.
        /// </summary>
        GlobalPosition,

        /// <summary>
        /// GlobalLocation changes.
        /// </summary>
        GlobalLocation
    }

    /// <summary>
    /// Selects the direction of a Zone coordinate conversion.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<PositioningTransformDirection>))]
    public enum PositioningTransformDirection
    {
        /// <summary>
        /// Geographic longitude/latitude/elevation to local X/Y/Z.
        /// </summary>
        GlobalToLocal,

        /// <summary>
        /// Local X/Y/Z to geographic longitude/latitude/elevation.
        /// </summary>
        LocalToGlobal
    }

    /// <summary>
    /// Explicit angular units for geographic coordinates and RSL orientation.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<PositioningAngleUnit>))]
    public enum PositioningAngleUnit
    {
        /// <summary>
        /// Angles measured in radians.
        /// </summary>
        Radians,

        /// <summary>
        /// Angles measured in degrees.
        /// </summary>
        Degrees
    }

    /// <summary>
    /// Selects the existing ground-control-point fitting algorithm.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<PositioningFitMode>))]
    public enum PositioningFitMode
    {
        /// <summary>
        /// Rotation and translation.
        /// </summary>
        Rigid,

        /// <summary>
        /// Uniform scale, rotation and translation.
        /// </summary>
        Similarity,

        /// <summary>
        /// General affine fit.
        /// </summary>
        Affine
    }

    /// <summary>
    /// Concrete, AOT-safe options for the existing positioning fitter.
    /// </summary>
    public sealed class PositioningFitOptions
    {
        /// <summary>
        /// Gets or sets the fitting algorithm.
        /// </summary>
        [Description("Rigid, Similarity, or Affine.")]
        public PositioningFitMode Mode { get; set; } = PositioningFitMode.Rigid;

        /// <summary>
        /// Gets or sets the angular units of the server's GroundControlPoints.
        /// </summary>
        [Description("Actual angular units of Zone GroundControlPoints, independent of input/output angleUnit.")]
        public PositioningAngleUnit ControlPointAngleUnit { get; set; } = PositioningAngleUnit.Degrees;

        /// <summary>
        /// Gets or sets whether affine reflections are permitted.
        /// </summary>
        [Description("Permit an affine reflection; rigid and similarity fits retain proper rotations.")]
        public bool AllowReflection { get; set; }

        /// <summary>
        /// Gets or sets the CRS identifier required of the host's transformer.
        /// </summary>
        [Description("Transformer CRS identifier, default EPSG:4326 (WGS84). Other CRSs require host injection.")]
        public string CoordinateReferenceSystem { get; set; } = "EPSG:4326";

        /// <summary>
        /// Converts validated options without silently substituting a different CRS.
        /// </summary>
        internal GroundControlPointFitOptions ToOptions(ICoordinateReferenceSystemTransformer transformer)
        {
            ArgumentNullException.ThrowIfNull(transformer);
            if (!string.Equals(CoordinateReferenceSystem, transformer.CoordinateReferenceSystem,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException(
                    $"Requested CRS '{CoordinateReferenceSystem}' does not match the configured transformer " +
                    $"'{transformer.CoordinateReferenceSystem}'.");
            }

            return new GroundControlPointFitOptions
            {
                Mode = Mode switch
                {
                    PositioningFitMode.Rigid => GroundControlPointFitMode.Rigid,
                    PositioningFitMode.Similarity => GroundControlPointFitMode.Similarity,
                    PositioningFitMode.Affine => GroundControlPointFitMode.Affine,
                    _ => throw new ArgumentException("Unknown positioning fit mode.")
                },
                AngleUnit = ToAngleUnit(ControlPointAngleUnit),
                AllowReflection = AllowReflection,
                CoordinateReferenceSystem = transformer
            };
        }

        /// <summary>
        /// Maps only defined angular units to the typed client.
        /// </summary>
        internal static AngleUnit ToAngleUnit(PositioningAngleUnit angleUnit)
        {
            return angleUnit switch
            {
                PositioningAngleUnit.Radians => AngleUnit.Radians,
                PositioningAngleUnit.Degrees => AngleUnit.Degrees,
                _ => throw new ArgumentOutOfRangeException(nameof(angleUnit))
            };
        }
    }
}
