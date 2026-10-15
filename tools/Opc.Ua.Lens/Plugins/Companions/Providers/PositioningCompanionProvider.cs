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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Gpos;
using Opc.Ua.Positioning;
using Opc.Ua.Positioning.Client;
using UaLens.Connection;
using static UaLens.Plugins.Companions.CompanionMethodMetadata;
using GlobalLocationValue = Opc.Ua.Positioning.Client.GlobalLocationValue;
using GlobalPositionValue = Opc.Ua.Positioning.Client.GlobalPositionValue;
using UaNames = Opc.Ua.BrowseNames;
using UaRefs = Opc.Ua.ReferenceTypeIds;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// Numeric RSL/GPOS inspection and local previews using the existing bounded clients and transform implementation.
    /// No map, renderer, remote URI or device-write dependency is introduced.
    /// </summary>
    internal sealed class PositioningCompanionProvider : IPreparedCompanionProvider
    {
        /// <summary>
        /// Creates a read-only adapter with an injectable preparation clock.
        /// </summary>
        public PositioningCompanionProvider(TimeProvider? timeProvider = null)
        {
            m_time = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public CompanionDescriptor Descriptor { get; } = new(
            "positioning", "Positioning", Opc.Ua.Rsl.Namespaces.RSL,
            "OPC 10000-210 Relative Spatial Location and OPC 10000-211 Global Positioning. Read-only numeric tools.");

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
            CompanionContext context, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            var targets = new List<CompanionTarget>();
            var seen = new HashSet<NodeId>();
            var rsl = new RelativeSpatialLocationClient(context.Session, context.Telemetry, s_limits);
            bool globalSupported = context.Session.NamespaceUris.GetIndex(Opc.Ua.Gpos.Namespaces.GPOS) >= 0;
            var parents = new HashSet<NodeId>();
            var budget = new IndustrialBrowseBudget(context);
            ArrayOf<PositioningObjectEntry> lists = await ReadSpatialListsAsync(context, lifetime.Token)
                .ConfigureAwait(false);
            for (int index = 0; index < lists.Count; index++)
            {
                PositioningObjectEntry list = lists[index];
                await foreach (PositioningObjectEntry item in rsl.EnumerateSpatialObjectsAsync(
                    list.NodeId, lifetime.Token)
                    .ConfigureAwait(false))
                {
                    CellCompanionSupport.AddTarget(targets, seen, Target(item, SpatialKind), context.MaxTargets);
                    if (!globalSupported)
                    {
                        continue;
                    }
                    await foreach (ReferenceDescription parent in IndustrialCompanionAccess.BrowseAsync(
                        context, item.NodeId, BrowseDirection.Inverse, UaRefs.HierarchicalReferences,
                        NodeClass.Object, budget, lifetime.Token).ConfigureAwait(false))
                    {
                        NodeId parentId = IndustrialCompanionAccess.LocalId(context, parent.NodeId);
                        if (parentId.IsNull || parentId == list.NodeId || !parents.Add(parentId))
                        {
                            continue;
                        }
                        await AddGlobalVariablesAsync(
                            context, parentId, targets, seen, budget, lifetime.Token).ConfigureAwait(false);
                    }
                    await AddGlobalVariablesAsync(
                        context, item.NodeId, targets, seen, budget, lifetime.Token).ConfigureAwait(false);
                }
            }
            if (globalSupported &&
                await HasEntryAsync(
                    context, Opc.Ua.Gpos.ObjectIds.GlobalLocations, lifetime.Token).ConfigureAwait(false))
            {
                var global = new GlobalPositioningClient(context.Session, context.Telemetry, s_limits);
                await AddGlobalVariablesAsync(
                    context, global.GlobalLocationsId, targets, seen, budget, lifetime.Token).ConfigureAwait(false);
                await foreach (PositioningObjectEntry zone in global.EnumerateZonesAsync(lifetime.Token)
                    .ConfigureAwait(false))
                {
                    CellCompanionSupport.AddTarget(targets, seen, Target(zone, ZoneKind), context.MaxTargets);
                    await AddGlobalVariablesAsync(
                        context, zone.NodeId, targets, seen, budget, lifetime.Token).ConfigureAwait(false);
                }
            }
            lifetime.Token.ThrowIfCancellationRequested();
            return [.. targets];
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionInspection> InspectAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
        {
            CompanionOperationResult result = await ReadAsync(context, target, "snapshot", cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<CompanionOperation> operations = target.TypeName switch
            {
                SpatialKind => [s_snapshot, s_worldDegrees, s_worldRadians],
                ZoneKind => [s_snapshot, s_preview],
                _ => [s_snapshot]
            };
            return new CompanionInspection(result.Values, operations, result.Summary);
        }

        /// <inheritdoc/>
        public ValueTask<CompanionOperationResult> ExecuteAsync(
            CompanionContext context, CompanionTarget target, string operationId, string? input,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(target);
            CellCompanionSupport.RequireNoInput(input);
            if (operationId != "snapshot" &&
                !(target.TypeName == SpatialKind && operationId is "world-degrees" or "world-radians"))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "Select a supported read-only positioning task.");
            }
            return ReadAsync(context, target, operationId, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionTaskInput> PrepareInputAsync(
            CompanionContext context, CompanionTarget target, string operationId,
            ArrayOf<CompanionValue> inputs, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (operationId != "preview" || target.TypeName != ZoneKind)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "Only a zone accepts local preview coordinates.");
            }
            CompanionInputContract.ValidateShape(s_preview, inputs);
            double Read(string name)
            {
                CompanionValue field = inputs.ToList().Single(value => value.Name == name);
                if (!field.Value.TryGetValue(out double number) || !double.IsFinite(number) || Math.Abs(number) > 1e7)
                {
                    throw new ArgumentException(
                        "Preview coordinates must be finite and within ten million local units.");
                }
                return number;
            }
            var point = new ThreeDCartesianCoordinates { X = Read("x"), Y = Read("y"), Z = Read("z") };
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            await RequireTargetAsync(context, target, lifetime.Token).ConfigureAwait(false);
            var draft = new CompanionOperationDraft(
                target, s_preview, null, context.Session, m_time.GetUtcNow().AddMinutes(5));
            Variant points = await ReadGroundControlPointsAsync(context, target.NodeId, lifetime.Token)
                .ConfigureAwait(false);
            var client = new GlobalPositioningClient(context.Session, context.Telemetry, s_limits);
            GroundControlPointFitResult fit = await client.ReadZoneTransformAsync(
                target.NodeId, cancellationToken: lifetime.Token).ConfigureAwait(false);
            if (!fit.IsInvertible)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The zone fit is not invertible.");
            }
            if (fit.Dimension == 2 && point.Z != 0)
            {
                throw new ServiceResultException(StatusCodes.BadOutOfRange,
                    "A two-dimensional zone has no height calibration; local Z must be zero.");
            }
            if (points != await ReadGroundControlPointsAsync(context, target.NodeId, lifetime.Token)
                    .ConfigureAwait(false) ||
                !draft.Matches(context.Session, m_time.GetUtcNow()))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "The zone calibration changed during preparation.");
            }
            lifetime.Token.ThrowIfCancellationRequested();
            return new PreviewInput(this, context, draft, point, points);
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionOperationResult> ExecutePreparedAsync(
            CompanionContext context, CompanionTarget target, string operationId, CompanionTaskInput input,
            IProgress<CompanionTaskProgress>? progress, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (operationId != "preview" || target.TypeName != ZoneKind || input is not PreviewInput preview)
            {
                throw new ArgumentException("Prepare a zone preview first.", nameof(input));
            }
            preview.Consume(this, context, target, m_time);
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            await RequireTargetAsync(context, target, lifetime.Token).ConfigureAwait(false);
            if (preview.Points != await ReadGroundControlPointsAsync(
                context, target.NodeId, lifetime.Token).ConfigureAwait(false))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "The prepared zone calibration changed.");
            }
            var client = new GlobalPositioningClient(context.Session, context.Telemetry, s_limits);
            S3DGeographicCoordinateDataType geographic = await client.LocalToGlobalAsync(
                target.NodeId, preview.Point, AngleUnit.Degrees, cancellationToken: lifetime.Token)
                .ConfigureAwait(false);
            if (preview.Points != await ReadGroundControlPointsAsync(
                context, target.NodeId, lifetime.Token).ConfigureAwait(false))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "The zone calibration changed during preview.");
            }
            lifetime.Token.ThrowIfCancellationRequested();
            preview.RequireCurrent(context, m_time);
            var values = new List<CompanionValue>
            {
                new("Longitude", Variant.From(geographic.Longitude)),
                new("Latitude", Variant.From(geographic.Latitude))
            };
            if ((geographic.EncodingMask & (uint)S3DGeographicCoordinateDataTypeFields.Elevation) != 0)
            {
                values.Add(new CompanionValue("Ellipsoidal elevation (metres)", Variant.From(geographic.Elevation)));
            }
            CellCompanionSupport.CheckCount(values.Count, context.MaxFields, "Positioning preview fields");
            return new CompanionOperationResult(
                "Local numeric preview in WGS84 degrees; no position was written and no map was loaded.",
                [.. values]);
        }

        private async ValueTask<CompanionOperationResult> ReadAsync(
            CompanionContext context, CompanionTarget target, string operation, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            await RequireTargetAsync(context, target, lifetime.Token).ConfigureAwait(false);
            var stamp = new CompanionOperationDraft(
                target, s_snapshot, null, context.Session, DateTimeOffset.MaxValue);
            var fields = new List<CompanionValue>();
            Dictionary<NodeId, string> versions = await ReadVersionsAsync(context, lifetime.Token)
                .ConfigureAwait(false);
            if (target.TypeName == SpatialKind)
            {
                var client = new RelativeSpatialLocationClient(context.Session, context.Telemetry, s_limits);
                RelativeSpatialFrameValue frame = await client.ReadPositionFrameAsync(target.NodeId, lifetime.Token)
                    .ConfigureAwait(false);
                FrameUnits units = await ReadFrameUnitsAsync(context, frame.NodeId, lifetime.Token)
                    .ConfigureAwait(false);
                AddFrame(fields, "Position frame", frame.NodeId, frame.Frame, frame.StatusCode, frame.SourceTimestamp,
                    units.Length.EngineeringMetadata, units.Angle.EngineeringMetadata);
                if (!units.Length.SourceNodeId.IsNull)
                {
                    fields.Add(units.Length with { Name = "Position frame / Length unit" });
                }
                if (!units.Angle.SourceNodeId.IsNull)
                {
                    fields.Add(units.Angle with { Name = "Position frame / Angle unit" });
                }
                fields.Add(new CompanionValue("Base frame", Variant.From(frame.BaseNodeId)));
                if (operation != "snapshot")
                {
                    AngleUnit angles = operation == "world-degrees" ? AngleUnit.Degrees : AngleUnit.Radians;
                    ResolvedRelativeSpatialFrame world = await client.ResolveFrameToWorldAsync(
                        frame.NodeId, angles, lifetime.Token).ConfigureAwait(false);
                    await RequireWorldUnitsAsync(context, world.FrameChain, units, angles, lifetime.Token)
                        .ConfigureAwait(false);
                    AddFrame(fields, "World frame", default, world.TransformToWorld.ToFrame(angles),
                        StatusCodes.Good, default, units.Length.EngineeringMetadata, angles.ToString());
                    fields.Add(new CompanionValue("Frame chain", Variant.From(world.FrameChain)));
                    fields.Add(new CompanionValue("Selected angular interpretation", Variant.From(angles.ToString())));
                }
            }
            else if (target.TypeName == ZoneKind)
            {
                var client = new GlobalPositioningClient(context.Session, context.Telemetry, s_limits);
                GroundControlPointFitResult fit = await client.ReadZoneTransformAsync(
                    target.NodeId, cancellationToken: lifetime.Token).ConfigureAwait(false);
                fields.Add(new CompanionValue("Fit mode", Variant.From(fit.Mode.ToString())));
                fields.Add(new CompanionValue("Dimension", Variant.From(fit.Dimension)));
                fields.Add(new CompanionValue("Rank", Variant.From(fit.Rank)));
                fields.Add(new CompanionValue("RMS residual (metres)", Variant.From(fit.RootMeanSquareError)));
                fields.Add(new CompanionValue("Maximum residual (metres)", Variant.From(fit.MaxResidual)));
                fields.Add(new CompanionValue("Determinant", Variant.From(fit.Determinant)));
                fields.Add(new CompanionValue("Invertible", Variant.From(fit.IsInvertible)));
            }
            else
            {
                var client = new GlobalPositioningClient(context.Session, context.Telemetry, s_limits);
                GlobalPositionValue position;
                if (target.TypeName == LocationKind)
                {
                    GlobalLocationValue location = await client.ReadGlobalLocationAsync(target.NodeId, lifetime.Token)
                        .ConfigureAwait(false);
                    position = new GlobalPositionValue(
                        location.NodeId, location.Location.Position, location.SourceNodeId,
                        location.CoordinateReferenceSystem, location.StatusCode, location.SourceTimestamp);
                    if ((location.Location.EncodingMask & (uint)GlobalLocationDataTypeFields.Orientation) != 0)
                    {
                        fields.Add(new CompanionValue("Encoded orientation",
                            Variant.FromStructure(location.Location.Orientation))
                        {
                            SourceNodeId = location.NodeId,
                            Quality = location.StatusCode,
                            SourceTimestamp = location.SourceTimestamp.IsNull
                                ? null : location.SourceTimestamp.ToDateTime()
                        });
                    }
                }
                else
                {
                    position = await client.ReadGlobalPositionAsync(target.NodeId, lifetime.Token)
                        .ConfigureAwait(false);
                }
                if (position.CoordinateReferenceSystem != 4326)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "This tool supports explicit WGS84/EPSG:4326, not automatic projection of another CRS.");
                }
                AddCoordinate("Longitude (degrees)", position.Position.Longitude);
                AddCoordinate("Latitude (degrees)", position.Position.Latitude);
                fields.Add(new CompanionValue("Source zone", Variant.From(position.SourceNodeId)));
                fields.Add(new CompanionValue("CRS", Variant.From(position.CoordinateReferenceSystem)));
                if ((position.Position.EncodingMask & (uint)S3DGeographicCoordinateDataTypeFields.Elevation) != 0)
                {
                    AddCoordinate("Ellipsoidal elevation (metres)", position.Position.Elevation);
                    ThreeDCartesianCoordinates ecef = Wgs84CoordinateReferenceSystemTransformer.Instance
                        .ToEarthCenteredEarthFixed(position.Position, AngleUnit.Degrees);
                    AddCoordinate("ECEF X (metres)", ecef.X);
                    AddCoordinate("ECEF Y (metres)", ecef.Y);
                    AddCoordinate("ECEF Z (metres)", ecef.Z);
                }
                void AddCoordinate(string name, double number)
                {
                    fields.Add(new CompanionValue(name, Variant.From(number))
                    {
                        SourceNodeId = position.NodeId,
                        Quality = position.StatusCode,
                        SourceTimestamp = position.SourceTimestamp.IsNull
                            ? null : position.SourceTimestamp.ToDateTime()
                    });
                }
            }
            Dictionary<NodeId, string> currentVersions = await ReadVersionsAsync(context, lifetime.Token)
                .ConfigureAwait(false);
            lifetime.Token.ThrowIfCancellationRequested();
            if (versions.Count != currentVersions.Count ||
                versions.Any(pair =>
                    !currentVersions.TryGetValue(pair.Key, out string? value) || value != pair.Value) ||
                !stamp.MatchesSession(context.Session))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "The positioning metadata changed during the read.");
            }
            CellCompanionSupport.CheckCount(fields.Count, context.MaxFields, "Positioning fields");
            return new CompanionOperationResult(
                "Read bounded positioning data without changing device state or starting a renderer or geocoder. " +
                (versions.Count == 0
                    ? "No spatial-list NodeVersion was exposed." : "Spatial-list NodeVersions remained stable."),
                [.. fields]);
        }

        private async Task RequireTargetAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
        {
            CellCompanionSupport.ValidateTarget(context, target, Descriptor.Id);
            if (target.TypeName is SpatialKind or ZoneKind)
            {
                await IndustrialCompanionAccess.RequireTargetAsync(context, target, Descriptor.Id,
                    [new(Opc.Ua.Rsl.ObjectTypeIds.SpatialObjectType, SpatialKind),
                        new(Opc.Ua.Gpos.ObjectTypeIds.ZoneType, ZoneKind)], cancellationToken).ConfigureAwait(false);
            }
            else if (target.TypeName is LocationKind or PositionKind)
            {
                DataValue nodeClass = await ReadOneAsync(
                    context, target.NodeId, Attributes.NodeClass, cancellationToken)
                    .ConfigureAwait(false);
                if (!nodeClass.WrappedValue.TryGetValue(out int kind) || kind != (int)NodeClass.Variable)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                        "Select a global positioning Variable.");
                }
                var budget = new IndustrialBrowseBudget(context);
                int matches = 0;
                await foreach (ReferenceDescription reference in IndustrialCompanionAccess.BrowseAsync(
                    context, target.NodeId, BrowseDirection.Forward, UaRefs.HasTypeDefinition,
                    NodeClass.VariableType, budget, cancellationToken).ConfigureAwait(false))
                {
                    ExpandedNodeId expected = target.TypeName == LocationKind
                        ? Opc.Ua.Gpos.VariableTypeIds.GlobalLocationType
                        : Opc.Ua.Gpos.VariableTypeIds.GlobalPositionType;
                    if (!await context.Session.NodeCache.IsTypeOfAsync(reference.NodeId, expected, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                            "The global positioning type changed.");
                    }
                    matches++;
                }
                if (matches != 1)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                        "The positioning type is not unique.");
                }
            }
            else
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "Unknown positioning target.");
            }
        }

        private async Task AddGlobalVariablesAsync(
            CompanionContext context, NodeId parent, List<CompanionTarget> targets, HashSet<NodeId> seen,
            IndustrialBrowseBudget budget, CancellationToken cancellationToken)
        {
            await foreach (ReferenceDescription variable in IndustrialCompanionAccess.BrowseAsync(
                context, parent, BrowseDirection.Forward, UaRefs.HierarchicalReferences,
                NodeClass.Variable, budget, cancellationToken).ConfigureAwait(false))
            {
                string? kind = await context.Session.NodeCache.IsTypeOfAsync(
                    variable.TypeDefinition, Opc.Ua.Gpos.VariableTypeIds.GlobalLocationType, cancellationToken)
                    .ConfigureAwait(false) ? LocationKind :
                    await context.Session.NodeCache.IsTypeOfAsync(
                        variable.TypeDefinition, Opc.Ua.Gpos.VariableTypeIds.GlobalPositionType, cancellationToken)
                        .ConfigureAwait(false) ? PositionKind : null;
                if (kind is not null)
                {
                    NodeId id = IndustrialCompanionAccess.LocalId(context, variable.NodeId);
                    CellCompanionSupport.AddTarget(targets, seen, new CompanionTarget(
                        Descriptor.Id, id, variable.DisplayName.Text ?? variable.BrowseName.Name ?? kind, kind),
                        context.MaxTargets);
                }
            }
        }

        private static async Task<Dictionary<NodeId, string>> ReadVersionsAsync(
            CompanionContext context, CancellationToken cancellationToken)
        {
            var versions = new Dictionary<NodeId, string>();
            ArrayOf<PositioningObjectEntry> lists = await ReadSpatialListsAsync(context, cancellationToken)
                .ConfigureAwait(false);
            for (int index = 0; index < lists.Count; index++)
            {
                PositioningObjectEntry list = lists[index];
                NodeId node = await IndustrialCompanionAccess.ResolveChildAsync(
                    context, list.NodeId, Opc.Ua.Namespaces.OpcUa, UaNames.NodeVersion, true, cancellationToken)
                    .ConfigureAwait(false);
                if (node.IsNull)
                {
                    continue;
                }
                DataValue reading = await ReadOneAsync(context, node, Attributes.Value, cancellationToken)
                    .ConfigureAwait(false);
                if (!reading.WrappedValue.TryGetValue(out string? value) || value is null || value.Length > 512)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch, "NodeVersion must be bounded text.");
                }
                if (versions.TryGetValue(node, out string? previous) && previous != value)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "A repeated NodeVersion changed during reading.");
                }
                versions[node] = value;
            }
            return versions;
        }

        private static async Task<ArrayOf<PositioningObjectEntry>> ReadSpatialListsAsync(
            CompanionContext context, CancellationToken cancellationToken)
        {
            if (!await HasEntryAsync(context, Opc.Ua.Rsl.ObjectIds.RelativeSpatialLocations, cancellationToken)
                .ConfigureAwait(false))
            {
                return [];
            }
            var client = new RelativeSpatialLocationClient(context.Session, context.Telemetry, s_limits);
            var lists = new List<PositioningObjectEntry>();
            await foreach (PositioningObjectEntry list in client.EnumerateSpatialObjectListsAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                lists.Add(list);
            }
            return [.. lists];
        }

        private static async Task<bool> HasEntryAsync(
            CompanionContext context, ExpandedNodeId entry, CancellationToken cancellationToken)
        {
            NodeId node = IndustrialCompanionAccess.LocalId(context, entry);
            if (node.IsNull)
            {
                return false;
            }
            ReadResponse response = await context.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = node, AttributeId = Attributes.NodeClass }], cancellationToken)
                .ConfigureAwait(false);
            if (response.ResponseHeader is null || response.Results.Count != 1)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "The entry-point read is incomplete.");
            }
            if (!StatusCode.IsGood(response.ResponseHeader.ServiceResult))
            {
                throw new ServiceResultException(response.ResponseHeader.ServiceResult);
            }
            DataValue value = response.Results[0];
            if (value.StatusCode == StatusCodes.BadNodeIdUnknown)
            {
                return false;
            }
            if (!StatusCode.IsGood(value.StatusCode))
            {
                throw new ServiceResultException(value.StatusCode, "The positioning entry point could not be read.");
            }
            if (!value.WrappedValue.TryGetValue(out int nodeClass) || nodeClass != (int)NodeClass.Object)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "A positioning entry point must be an Object.");
            }
            return true;
        }

        private static async Task<Variant> ReadGroundControlPointsAsync(
            CompanionContext context, NodeId zone, CancellationToken cancellationToken)
        {
            NodeId node = await IndustrialCompanionAccess.ResolveChildAsync(
                context, zone, Opc.Ua.Gpos.Namespaces.GPOS, Opc.Ua.Gpos.BrowseNames.GroundControlPoints,
                false, cancellationToken).ConfigureAwait(false);
            DataValue reading = await ReadOneAsync(context, node, Attributes.Value, cancellationToken)
                .ConfigureAwait(false);
            if (!reading.WrappedValue.TryGetValue(out ArrayOf<GroundControlPointDataType> points,
                context.Session.MessageContext) ||
                points.IsNull ||
                points.Count is < 3 or > 128)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "A zone preview requires three through 128 ground control points.");
            }
            return DataValueCodec.Snapshot(reading.WrappedValue, context.Session.MessageContext);
        }

        private static async Task RequireWorldUnitsAsync(
            CompanionContext context, ArrayOf<NodeId> frames, FrameUnits selectedUnits, AngleUnit angles,
            CancellationToken cancellationToken)
        {
            for (int index = 0; index < frames.Count; index++)
            {
                FrameUnits units = await ReadFrameUnitsAsync(context, frames[index], cancellationToken)
                    .ConfigureAwait(false);
                if (index == 0 &&
                    (units.Length.Value != selectedUnits.Length.Value ||
                        units.Angle.Value != selectedUnits.Angle.Value))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "The selected frame units changed.");
                }
                if (units.Length.SourceNodeId.IsNull ||
                    units.Angle.SourceNodeId.IsNull ||
                    units.Length.Value != selectedUnits.Length.Value ||
                    !units.Angle.Value.TryGetValue<EUInformation>(
                        out EUInformation? angle, context.Session.MessageContext) ||
                    angle is null ||
                    !UsesAngleUnit(angle, angles))
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "World resolution requires matching declared length units and the selected angular unit " +
                        "throughout the frame chain. No implicit unit conversion is performed.");
                }
            }
        }

        private static bool UsesAngleUnit(EUInformation unit, AngleUnit expected)
        {
            if (unit.NamespaceUri != "http://www.opcfoundation.org/UA/units/un/cefact")
            {
                return false;
            }
            // UN/CEFACT common codes DD (degree) and C81 (radian).
            if (unit.UnitId > 0)
            {
                return unit.UnitId == (expected == AngleUnit.Degrees ? 17476 : 4405297);
            }
            string? name = unit.DisplayName.Text;
            return expected == AngleUnit.Degrees
                ? string.Equals(name, "deg", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "degree", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "degrees", StringComparison.OrdinalIgnoreCase) ||
                    name == "\u00b0"
                : string.Equals(name, "rad", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "radian", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "radians", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<FrameUnits> ReadFrameUnitsAsync(
            CompanionContext context, NodeId frame, CancellationToken cancellationToken)
        {
            CompanionValue length = await ReadUnitAsync("Position", "LengthUnit").ConfigureAwait(false);
            CompanionValue angle = await ReadUnitAsync("Orientation", "AngleUnit").ConfigureAwait(false);
            return new FrameUnits(length, angle);

            async Task<CompanionValue> ReadUnitAsync(string component, string name)
            {
                NodeId parent = await IndustrialCompanionAccess.ResolveChildAsync(
                    context, frame, Opc.Ua.Rsl.Namespaces.RSL, component, true, cancellationToken)
                    .ConfigureAwait(false);
                NodeId node = parent.IsNull ? NodeId.Null : await IndustrialCompanionAccess.ResolveChildAsync(
                    context, parent, Opc.Ua.Namespaces.OpcUa, name, true, cancellationToken).ConfigureAwait(false);
                if (node.IsNull)
                {
                    return new CompanionValue(name, Variant.Null)
                    {
                        EngineeringMetadata = "Unit not exposed.",
                        Quality = StatusCodes.BadNoMatch
                    };
                }
                DataValue reading = await ReadOneAsync(context, node, Attributes.Value, cancellationToken)
                    .ConfigureAwait(false);
                if (!reading.WrappedValue.TryGetValue<EUInformation>(
                    out EUInformation? unit, context.Session.MessageContext) ||
                    unit is null ||
                    unit.DisplayName.Text is { Length: > 4096 } ||
                    unit.Description.Text is { Length: > 4096 } ||
                    unit.NamespaceUri is { Length: > 4096 })
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                        "Frame units must be bounded EUInformation.");
                }
                return new CompanionValue(
                    name, DataValueCodec.Snapshot(reading.WrappedValue, context.Session.MessageContext))
                {
                    SourceNodeId = node,
                    Quality = reading.StatusCode,
                    SourceTimestamp = reading.SourceTimestamp.IsNull ? null : reading.SourceTimestamp.ToDateTime(),
                    EngineeringMetadata = unit.DisplayName.Text ?? unit.Description.Text ?? "Unnamed unit"
                };
            }
        }

        private static void AddFrame(
            List<CompanionValue> fields, string prefix, NodeId node, ThreeDFrame frame,
            StatusCode quality, DateTimeUtc timestamp, string? lengthMetadata, string? angleMetadata)
        {
            void Add(string name, double number, string? units)
            {
                if (!double.IsFinite(number))
                {
                    throw new ServiceResultException(StatusCodes.BadOutOfRange, "Positioning values must be finite.");
                }
                fields.Add(new CompanionValue(prefix + " / " + name, Variant.From(number))
                {
                    SourceNodeId = node,
                    Quality = quality,
                    SourceTimestamp = timestamp.IsNull ? null : timestamp.ToDateTime(),
                    EngineeringMetadata = units
                });
            }
            Add("X", frame.CartesianCoordinates.X, lengthMetadata);
            Add("Y", frame.CartesianCoordinates.Y, lengthMetadata);
            Add("Z", frame.CartesianCoordinates.Z, lengthMetadata);
            Add("A", frame.Orientation.A, angleMetadata);
            Add("B", frame.Orientation.B, angleMetadata);
            Add("C", frame.Orientation.C, angleMetadata);
        }

        private CompanionTarget Target(PositioningObjectEntry entry, string kind) =>
            new(Descriptor.Id, entry.NodeId, entry.DisplayName.Text ?? entry.BrowseName.Name ?? kind, kind);

        private static CancellationTokenSource Begin(CompanionContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            string model = context.Session.NamespaceUris.GetIndex(Opc.Ua.Rsl.Namespaces.RSL) >= 0
                ? Opc.Ua.Rsl.Namespaces.RSL : Opc.Ua.Gpos.Namespaces.GPOS;
            return CellCompanionSupport.BeginOperation(context, model, cancellationToken);
        }

        /// <summary>
        /// Declared frame units and their source evidence.
        /// </summary>
        private sealed record FrameUnits(CompanionValue Length, CompanionValue Angle);

        private const string SpatialKind = "Spatial object";
        private const string ZoneKind = "Global zone";
        private const string LocationKind = "Global location";
        private const string PositionKind = "Global position";
        private readonly TimeProvider m_time;

        private static readonly PositioningClientReadOptions s_limits = new()
        {
            MaximumBrowseReferences = 1024,
            MaximumBrowsePages = 128,
            MaximumFrameChainLength = 64,
            MaximumReadArrayItems = 128
        };

        private static readonly CompanionOperation s_snapshot = new(
            "snapshot", "Read positioning snapshot", CompanionOperationSafety.ReadOnly);

        private static readonly CompanionOperation s_worldDegrees = new(
            "world-degrees", "Resolve world frame (angles in degrees)", CompanionOperationSafety.ReadOnly);

        private static readonly CompanionOperation s_worldRadians = new(
            "world-radians", "Resolve world frame (angles in radians)", CompanionOperationSafety.ReadOnly);

        private static readonly CompanionOperation s_preview = new(
            "preview", "Preview local point in the zone", CompanionOperationSafety.ReadOnly)
        {
            Inputs =
            [
                new("x", "Local X", BuiltInType.Double, "Finite local coordinate."),
                new("y", "Local Y", BuiltInType.Double, "Finite local coordinate."),
                new("z", "Local Z", BuiltInType.Double, "Finite local coordinate.")
            ]
        };

        /// <summary>
        /// Immutable local coordinates for a read-only preview, not a device position request.
        /// </summary>
        private sealed class PreviewInput(
            PositioningCompanionProvider provider, CompanionContext context, CompanionOperationDraft draft,
            ThreeDCartesianCoordinates point, Variant points) : CompanionTaskInput
        {
            /// <inheritdoc/>
            public override string Review => string.Create(CultureInfo.InvariantCulture,
                $"Preview ({point.X}, {point.Y}, {point.Z}) using the pinned zone calibration. No device write.");

            /// <summary>
            /// Gets a detached copy of the selected local coordinates.
            /// </summary>
            public ThreeDCartesianCoordinates Point => new() { X = point.X, Y = point.Y, Z = point.Z };

            /// <summary>
            /// Gets the captured calibration used to detect changes.
            /// </summary>
            public Variant Points { get; } = points;

            /// <summary>
            /// Consumes preparation once for its owning provider, target and session.
            /// </summary>
            public void Consume(
                PositioningCompanionProvider owner, CompanionContext current,
                CompanionTarget target, TimeProvider time)
            {
                if (Interlocked.Exchange(ref m_consumed, 1) != 0)
                {
                    throw new InvalidOperationException("The positioning preview preparation was consumed.");
                }
                if (!ReferenceEquals(provider, owner) || target != draft.Target)
                {
                    throw new ArgumentException("The preview belongs to another provider or target.");
                }
                RequireCurrent(current, time);
            }

            /// <summary>
            /// Rejects a replaced session or an expired preparation.
            /// </summary>
            public void RequireCurrent(CompanionContext current, TimeProvider time)
            {
                if (!ReferenceEquals(context.Session, current.Session) ||
                    !draft.Matches(current.Session, time.GetUtcNow()))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "The preview's session or metadata changed.");
                }
            }

            private int m_consumed;
        }
    }
}
