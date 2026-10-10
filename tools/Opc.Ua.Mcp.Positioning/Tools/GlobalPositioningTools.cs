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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.Positioning;
using Opc.Ua.Positioning.Client;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Read-only GPOS discovery, Zone transforms and finite observations.
    /// </summary>
    [McpServerToolType]
    public sealed class GlobalPositioningTools
    {
        /// <summary>
        /// Initializes tools with an accessor and an optional host-provided CRS transformer.
        /// </summary>
        public GlobalPositioningTools(
            PositioningClientAccessor accessor,
            ICoordinateReferenceSystemTransformer? coordinateReferenceSystem = null)
        {
            m_accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
            m_coordinateReferenceSystem = coordinateReferenceSystem ??
                Wgs84CoordinateReferenceSystemTransformer.Instance;
        }

        /// <summary>
        /// Discovers Zones below GlobalLocations.
        /// </summary>
        [McpServerTool(Name = "positioning_gpos_list", ReadOnly = true, Destructive = false)]
        [Description("Discover GPOS Zones under GlobalLocations. Returns live pages with qualified names, " +
            "NodeIds and actual typeDefinitionIds; no inventory is cached.")]
        public Task<CallToolResult> ListAsync(
            [Description("Live enumeration offset, 0..1000000.")] int offset = 0,
            [Description("Page size, 1..500.")] int maxResults = 100,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                GlobalPositioningClient client = m_accessor.CreateGlobalClient(sessionName);
                return await McpCompanionTools.PageAsync(
                    client.EnumerateZonesAsync(token), PositioningJson.Entry, offset, maxResults, token)
                    .ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Reads a global value or fits a Zone's control points.
        /// </summary>
        [McpServerTool(Name = "positioning_gpos_read", ReadOnly = true, Destructive = false)]
        [Description("Read GlobalPosition or GlobalLocation on its variable NodeId, preserving the raw server " +
            "CRS code, source node, status, timestamp and optional fields. ZoneTransform instead fits the Zone's " +
            "GroundControlPoints and reports dimension/rank/residuals/determinant/invertibility. fitOptions " +
            "select actual control-point angular units, CRS, fit mode and reflection policy; no CRS is inferred.")]
        public Task<CallToolResult> ReadAsync(
            [Description("GlobalPosition/GlobalLocation variable NodeId, or Zone NodeId for ZoneTransform.")]
            string nodeId,
            [Description("GlobalPosition, GlobalLocation, or ZoneTransform.")] GlobalPositioningRead facet,
            [Description("Used only for ZoneTransform; default rigid, degrees, WGS84, no reflection.")]
            PositioningFitOptions? fitOptions = null,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                PositioningJson.Validate(facet);
                NodeId target = PositioningJson.ParseNodeId(nodeId);
                GroundControlPointFitOptions? options = fitOptions?.ToOptions(m_coordinateReferenceSystem);
                GlobalPositioningClient client = m_accessor.CreateGlobalClient(sessionName);
                switch (facet)
                {
                    case GlobalPositioningRead.GlobalPosition:
                        return PositioningJson.Position(
                            await client.ReadGlobalPositionAsync(target, token).ConfigureAwait(false),
                            client.Session.MessageContext);
                    case GlobalPositioningRead.GlobalLocation:
                        return PositioningJson.Location(
                            await client.ReadGlobalLocationAsync(target, token).ConfigureAwait(false),
                            client.Session.MessageContext);
                    case GlobalPositioningRead.ZoneTransform:
                        options ??= new PositioningFitOptions().ToOptions(m_coordinateReferenceSystem);
                        return PositioningJson.Fit(target,
                            await client.ReadZoneTransformAsync(target, options, token).ConfigureAwait(false),
                            options);
                    default:
                        throw new ArgumentOutOfRangeException(nameof(facet));
                }
            }, ct);
        }

        /// <summary>
        /// Converts numeric coordinates using the typed client's existing Zone transformation.
        /// </summary>
        [McpServerTool(Name = "positioning_gpos_transform", ReadOnly = true, Destructive = false)]
        [Description("Convert GlobalToLocal or LocalToGlobal through a Zone's freshly read GroundControlPoints. " +
            "Global x/y are longitude/latitude in angleUnit, optional z is elevation in metres; local x/y/z " +
            "are in the Zone's local coordinate units (omitted local z means zero). angleUnit governs input " +
            "or output geographic angles, independently of fitOptions.controlPointAngleUnit. " +
            "Only the configured CRS is accepted; built-in default is EPSG:4326. Does not write any node.")]
        public Task<CallToolResult> TransformAsync(
            [Description("Zone NodeId supplying GroundControlPoints.")] string zoneNodeId,
            [Description("GlobalToLocal or LocalToGlobal.")] PositioningTransformDirection direction,
            [Description("Longitude for global input; X for local input.")] double x,
            [Description("Latitude for global input; Y for local input.")] double y,
            [Description("Actual geographic input/output angular units: Radians or Degrees.")]
            PositioningAngleUnit angleUnit,
            [Description("Optional global elevation in metres, or local Z in Zone units.")] double? z = null,
            [Description("Control-point units, CRS, mode and reflection policy, separate from input units.")]
            PositioningFitOptions? fitOptions = null,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                PositioningJson.Validate(direction);
                AngleUnit unit = PositioningFitOptions.ToAngleUnit(angleUnit);
                ValidateCoordinates(x, y, z);
                NodeId target = PositioningJson.ParseNodeId(zoneNodeId);
                GroundControlPointFitOptions options =
                    (fitOptions ?? new PositioningFitOptions()).ToOptions(m_coordinateReferenceSystem);
                GlobalPositioningClient client = m_accessor.CreateGlobalClient(sessionName);
                JsonObject result = PositioningJson.FitContext(target, options);
                result["direction"] = direction.ToString();
                result["angleUnit"] = angleUnit.ToString();
                if (direction == PositioningTransformDirection.GlobalToLocal)
                {
                    var global = new Gpos.S3DGeographicCoordinateDataType
                    {
                        Longitude = x,
                        Latitude = y
                    };
                    if (z.HasValue)
                    {
                        global.EncodingMask = (uint)Gpos.S3DGeographicCoordinateDataTypeFields.Elevation;
                        global.Elevation = z.Value;
                    }

                    ThreeDCartesianCoordinates local = await client.GlobalToLocalAsync(
                        target, global, unit, options, token).ConfigureAwait(false);
                    result["valueType"] = nameof(ThreeDCartesianCoordinates);
                    result["localCoordinates"] = McpCompanionJson.Encode(local, client.Session.MessageContext);
                    result["linearUnits"] = "Zone local coordinate units";
                }
                else
                {
                    var local = new ThreeDCartesianCoordinates { X = x, Y = y, Z = z ?? 0.0 };
                    Gpos.S3DGeographicCoordinateDataType global = await client.LocalToGlobalAsync(
                        target, local, unit, options, token).ConfigureAwait(false);
                    result["valueType"] = nameof(Gpos.S3DGeographicCoordinateDataType);
                    result["globalCoordinates"] = McpCompanionJson.Encode(global, client.Session.MessageContext);
                    result["hasElevation"] =
                        (global.EncodingMask & (uint)Gpos.S3DGeographicCoordinateDataTypeFields.Elevation) != 0;
                    result["elevationUnits"] = "metres";
                }

                return result;
            }, ct);
        }

        /// <summary>
        /// Collects a bounded global-position or global-location notification window.
        /// </summary>
        [McpServerTool(Name = "positioning_gpos_observe", ReadOnly = true, Destructive = false)]
        [Description("Observe GlobalPosition or GlobalLocation for a finite window, preserving raw CRS, " +
            "source node, status, timestamp and optional fields. Borrows ManagedSession.DefaultStreaming; " +
            "finishing, timeout or cancellation releases this call's enumerator, never the host subscription.")]
        public Task<CallToolResult> ObserveAsync(
            [Description("GlobalPosition or GlobalLocation variable NodeId matching facet.")] string nodeId,
            [Description("GlobalPosition or GlobalLocation.")] GlobalPositioningObservation facet,
            [Description("Observation window in milliseconds, 1..30000.")] int durationMs = 1000,
            [Description("Maximum notifications, 1..500.")] int maxItems = 100,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                PositioningJson.Validate(facet);
                NodeId target = PositioningJson.ParseNodeId(nodeId);
                GlobalPositioningClient client = m_accessor.CreateGlobalClient(sessionName);
                var streaming = McpCompanionTools.GetStreaming(client.Session);
                if (facet == GlobalPositioningObservation.GlobalPosition)
                {
                    return await McpCompanionTools.ObserveAsync(
                        cancellation => client.ObserveGlobalPositionAsync(
                            target, streaming, cancellationToken: cancellation),
                        value => PositioningJson.Position(value, client.Session.MessageContext),
                        durationMs, maxItems, token).ConfigureAwait(false);
                }

                return await McpCompanionTools.ObserveAsync(
                    cancellation => client.ObserveGlobalLocationAsync(
                        target, streaming, cancellationToken: cancellation),
                    value => PositioningJson.Location(value, client.Session.MessageContext),
                    durationMs, maxItems, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Rejects non-finite numeric input before contacting a server.
        /// </summary>
        internal static void ValidateCoordinates(double x, double y, double? z)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y) || (z.HasValue && !double.IsFinite(z.Value)))
            {
                throw new ArgumentException("Coordinates must be finite.");
            }
        }

        /// <summary>
        /// Resolves the session anew for each call.
        /// </summary>
        private readonly PositioningClientAccessor m_accessor;

        /// <summary>
        /// The host's CRS transformer, or the existing WGS84 implementation.
        /// </summary>
        private readonly ICoordinateReferenceSystemTransformer m_coordinateReferenceSystem;
    }
}
