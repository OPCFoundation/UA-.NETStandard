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
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Positioning.Client;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Read-only RSL discovery, frame resolution and finite observations.
    /// </summary>
    [McpServerToolType]
    public sealed class RelativePositioningTools
    {
        /// <summary>
        /// Initializes tools with a per-call session accessor.
        /// </summary>
        public RelativePositioningTools(PositioningClientAccessor accessor)
        {
            m_accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        }

        /// <summary>
        /// Discovers spatial object lists, objects or frames.
        /// </summary>
        [McpServerTool(Name = "positioning_rsl_list", ReadOnly = true, Destructive = false)]
        [Description("Discover RSL SpatialObjectLists, SpatialObjects within a list, or Frames within a folder. " +
            "Returns live pages with qualified names and actual typeDefinitionIds; no inventory is cached.")]
        public Task<CallToolResult> ListAsync(
            [Description("SpatialObjectLists, SpatialObjects, or Frames.")]
            RelativePositioningDiscovery scope = RelativePositioningDiscovery.SpatialObjectLists,
            [Description("Required list NodeId for SpatialObjects or folder NodeId for Frames.")]
            string? parentNodeId = null,
            [Description("Live enumeration offset, 0..1000000.")] int offset = 0,
            [Description("Page size, 1..500.")] int maxResults = 100,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                PositioningJson.Validate(scope);
                RelativeSpatialLocationClient client = m_accessor.CreateRelativeClient(sessionName);
                IAsyncEnumerable<PositioningObjectEntry> source = scope switch
                {
                    RelativePositioningDiscovery.SpatialObjectLists =>
                        client.EnumerateSpatialObjectListsAsync(token),
                    RelativePositioningDiscovery.SpatialObjects =>
                        client.EnumerateSpatialObjectsAsync(PositioningJson.ParseNodeId(parentNodeId), token),
                    RelativePositioningDiscovery.Frames =>
                        client.EnumerateFramesAsync(PositioningJson.ParseNodeId(parentNodeId), token),
                    _ => throw new ArgumentOutOfRangeException(nameof(scope))
                };
                return await McpCompanionTools.PageAsync(
                    source, PositioningJson.Entry, offset, maxResults, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Reads a raw frame or resolves a frame's base chain to world.
        /// </summary>
        [McpServerTool(Name = "positioning_rsl_read", ReadOnly = true, Destructive = false)]
        [Description("Read PositionFrame on a spatial object, a Frame variable, or resolve WorldFrame. " +
            "WorldFrame requires the actual orientation angleUnit and uses existing RSL chain resolution. " +
            "Raw reads preserve server units, status, source timestamp and BaseNodeId; no unit is inferred.")]
        public Task<CallToolResult> ReadAsync(
            [Description("Spatial object NodeId for PositionFrame; frame variable NodeId otherwise.")] string nodeId,
            [Description("PositionFrame, Frame, or WorldFrame.")] RelativePositioningRead facet,
            [Description("Required for WorldFrame: actual RSL orientation units, Radians or Degrees.")]
            PositioningAngleUnit? angleUnit = null,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                PositioningJson.Validate(facet);
                if (angleUnit.HasValue)
                {
                    PositioningJson.Validate(angleUnit.Value);
                }

                if (facet == RelativePositioningRead.WorldFrame && !angleUnit.HasValue)
                {
                    throw new ArgumentException("WorldFrame requires the actual angleUnit.", nameof(angleUnit));
                }

                NodeId target = PositioningJson.ParseNodeId(nodeId);
                RelativeSpatialLocationClient client = m_accessor.CreateRelativeClient(sessionName);
                if (facet == RelativePositioningRead.WorldFrame)
                {
                    PositioningAngleUnit unit = angleUnit!.Value;
                    ResolvedRelativeSpatialFrame resolved = await client.ResolveFrameToWorldAsync(
                        target, PositioningFitOptions.ToAngleUnit(unit), token).ConfigureAwait(false);
                    return PositioningJson.Resolved(resolved, unit, client.Session.MessageContext);
                }

                RelativeSpatialFrameValue value = facet == RelativePositioningRead.PositionFrame
                    ? await client.ReadPositionFrameAsync(target, token).ConfigureAwait(false)
                    : await client.ReadFrameAsync(target, token).ConfigureAwait(false);
                return PositioningJson.Frame(value, client.Session.MessageContext);
            }, ct);
        }

        /// <summary>
        /// Collects a bounded frame or NodeVersion notification window.
        /// </summary>
        [McpServerTool(Name = "positioning_rsl_observe", ReadOnly = true, Destructive = false)]
        [Description("Observe PositionFrame, Frame, or list NodeVersion for a finite window. Uses the selected " +
            "ManagedSession's shared streaming subscription, releases only this call's enumeration, and never " +
            "closes the host subscription. Missing optional nodes remain service errors, not empty values.")]
        public Task<CallToolResult> ObserveAsync(
            [Description("Spatial object, frame variable, or spatial object list NodeId matching facet.")]
            string nodeId,
            [Description("PositionFrame, Frame, or NodeVersion.")] RelativePositioningObservation facet,
            [Description("Observation window in milliseconds, 1..30000.")] int durationMs = 1000,
            [Description("Maximum notifications, 1..500.")] int maxItems = 100,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                PositioningJson.Validate(facet);
                NodeId target = PositioningJson.ParseNodeId(nodeId);
                RelativeSpatialLocationClient client = m_accessor.CreateRelativeClient(sessionName);
                var streaming = McpCompanionTools.GetStreaming(client.Session);
                if (facet == RelativePositioningObservation.NodeVersion)
                {
                    return await McpCompanionTools.ObserveAsync(
                        cancellation => client.ObserveNodeVersionAsync(
                            target, streaming, cancellationToken: cancellation),
                        value => PositioningJson.Version(value, client.Session.MessageContext),
                        durationMs, maxItems, token).ConfigureAwait(false);
                }

                return await McpCompanionTools.ObserveAsync(
                    cancellation => facet == RelativePositioningObservation.PositionFrame
                        ? client.ObservePositionFrameAsync(target, streaming, cancellationToken: cancellation)
                        : client.ObserveFrameAsync(target, streaming, cancellationToken: cancellation),
                    value => PositioningJson.Frame(value, client.Session.MessageContext),
                    durationMs, maxItems, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Resolves the session anew for each call.
        /// </summary>
        private readonly PositioningClientAccessor m_accessor;
    }
}
