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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Gpos;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Positioning;
using Opc.Ua.Positioning.Client;
using Opc.Ua.Tests;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Exercises positioning tools through real typed clients and deterministic external wire responses.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PositioningCoverageTests
    {
        /// <summary>
        /// Zone discovery retains qualified names and vendor types while excluding unrelated objects.
        /// </summary>
        [Test]
        public async Task ListGlobalZonesPreservesVendorIdentityAndLivePagingAsync()
        {
            await using ServiceProvider services = Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            var root = NodeId.Create(Gpos.Objects.GlobalLocations, Gpos.Namespaces.GPOS, wire.Context.NamespaceUris);
            NodeId vendorType = wire.Node("SurveyedZoneType");
            wire.Subtype(vendorType, Gpos.ObjectTypeIds.ZoneType);
            wire.Browse(root, ReferenceTypeIds.Organizes, NodeClass.Object,
            [
                wire.Reference(wire.Node("not-zone"), "Folder", ObjectTypeIds.FolderType),
                wire.Reference(wire.Node("zone-north"), "Nord", vendorType),
                wire.Reference(wire.Node("zone-south"), "Süd", vendorType)
            ]);
            await manager.RegisterExistingSessionAsync("survey", wire.Session.Object, "Anonymous")
                .ConfigureAwait(false);

            JsonElement page = Success(await new GlobalPositioningTools(new PositioningClientAccessor(manager))
                .ListAsync(maxResults: 1, sessionName: "survey").ConfigureAwait(false));

            Assert.That(page.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            JsonElement item = page.GetProperty("items")[0];
            Assert.That(item.GetProperty("nodeId").GetString(), Is.EqualTo(wire.Node("zone-north").ToString()));
            Assert.That(item.GetProperty("typeDefinitionId").GetString(), Is.EqualTo(vendorType.ToString()));
            Assert.That(item.GetProperty("browseName").GetProperty("namespaceIndex").GetUInt16(),
                Is.EqualTo(wire.VendorIndex));
            Assert.That(item.GetProperty("browseName").GetProperty("name").GetString(), Is.EqualTo("Nord"));
            Assert.That(item.GetProperty("displayName").GetProperty("locale").GetString(), Is.EqualTo("de"));
            Assert.That(page.GetProperty("hasMore").GetBoolean(), Is.True);
            Assert.That(page.GetProperty("nextOffset").GetInt32(), Is.EqualTo(1));
            Assert.That(wire.Browses.Single().ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Organizes));
            Assert.That(wire.Browses[0].NodeId, Is.EqualTo(root));
        }

        /// <summary>
        /// Relative discovery distinguishes lists, objects and frame variables by reference and type.
        /// </summary>
        [TestCase(RelativePositioningDiscovery.SpatialObjectLists)]
        [TestCase(RelativePositioningDiscovery.SpatialObjects)]
        [TestCase(RelativePositioningDiscovery.Frames)]
        public async Task ListRelativeObjectsUsesRequestedContainerAndTypeAsync(RelativePositioningDiscovery scope)
        {
            await using ServiceProvider services = Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId parent = scope == RelativePositioningDiscovery.SpatialObjectLists
                ? NodeId.Create(Rsl.Objects.RelativeSpatialLocations, Rsl.Namespaces.RSL, wire.Context.NamespaceUris)
                : wire.Node("positioning-container");
            ExpandedNodeId expectedType = scope switch
            {
                RelativePositioningDiscovery.SpatialObjectLists => Rsl.ObjectTypeIds.SpatialObjectsListType,
                RelativePositioningDiscovery.SpatialObjects => Rsl.ObjectTypeIds.SpatialObjectType,
                _ => Rsl.VariableTypeIds.SpatialLocationType
            };
            NodeId type = wire.Node("VendorSpatialType");
            wire.Subtype(type, expectedType);
            bool frames = scope == RelativePositioningDiscovery.Frames;
            NodeClass nodeClass = frames ? NodeClass.Variable : NodeClass.Object;
            NodeId reference = frames ? ReferenceTypeIds.HasComponent : ReferenceTypeIds.Organizes;
            NodeId child = wire.Node("gantry-location");
            wire.Browse(parent, reference, nodeClass,
                [wire.Reference(child, "Gantry", type, nodeClass),
                    wire.Reference(wire.Node("unrelated"), "Other", ObjectTypeIds.BaseObjectType, nodeClass)]);
            await manager.RegisterExistingSessionAsync("survey", wire.Session.Object, "Anonymous")
                .ConfigureAwait(false);

            JsonElement page = Success(await new RelativePositioningTools(new PositioningClientAccessor(manager))
                .ListAsync(scope, scope == RelativePositioningDiscovery.SpatialObjectLists ? null : parent.ToString(),
                    sessionName: "survey").ConfigureAwait(false));

            Assert.That(page.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            Assert.That(page.GetProperty("items")[0].GetProperty("nodeId").GetString(), Is.EqualTo(child.ToString()));
            Assert.That(page.GetProperty("items")[0].GetProperty("browseName").GetProperty("name").GetString(),
                Is.EqualTo("Gantry"));
            Assert.That(wire.Browses.Single().NodeId, Is.EqualTo(parent));
            Assert.That(wire.Browses[0].ReferenceTypeId, Is.EqualTo(reference));
            Assert.That(wire.Browses[0].NodeClassMask, Is.EqualTo((uint)nodeClass));
        }

        /// <summary>
        /// Raw frame reads preserve coordinates, orientation, quality, timestamps and the actual Base relationship.
        /// </summary>
        [TestCase(RelativePositioningRead.PositionFrame)]
        [TestCase(RelativePositioningRead.Frame)]
        public async Task ReadRelativeFramePreservesCoordinatesAndProvenanceAsync(RelativePositioningRead facet)
        {
            await using ServiceProvider services = Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId spatialObject = wire.Node("gantry");
            NodeId frame = wire.Node("gantry-frame");
            NodeId parent = wire.Node("cell-frame");
            wire.Path(spatialObject, frame, wire.Name("PositionFrame", Rsl.Namespaces.RSL));
            wire.Child(frame, wire.Name("Base", Rsl.Namespaces.RSL), Variant.From(parent));
            wire.Value(frame, new DataValue(Variant.FromStructure(new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = 1.25, Y = -2.5, Z = 3.75 },
                Orientation = new ThreeDOrientation { A = 10, B = 20, C = 30 }
            }), StatusCodes.Uncertain, new DateTimeUtc(2026, 7, 8, 9, 10, 11)));
            await manager.RegisterExistingSessionAsync("survey", wire.Session.Object, "Anonymous")
                .ConfigureAwait(false);

            JsonElement json = Success(await new RelativePositioningTools(new PositioningClientAccessor(manager))
                .ReadAsync((facet == RelativePositioningRead.PositionFrame ? spatialObject : frame).ToString(),
                    facet, sessionName: "survey").ConfigureAwait(false));

            Assert.That(json.GetProperty("nodeId").GetString(), Is.EqualTo(frame.ToString()));
            Assert.That(json.GetProperty("baseNodeId").GetString(), Is.EqualTo(parent.ToString()));
            Assert.That(json.GetProperty("statusCode").GetUInt32(), Is.EqualTo(StatusCodes.Uncertain.Code));
            Assert.That(json.GetProperty("metadata").GetProperty("sourceTimestamp").GetString(),
                Does.StartWith("2026-07-08T09:10:11"));
            JsonElement value = json.GetProperty("frame");
            Assert.That(value.GetProperty("CartesianCoordinates").GetProperty("X").GetDouble(), Is.EqualTo(1.25));
            Assert.That(value.GetProperty("CartesianCoordinates").GetProperty("Y").GetDouble(), Is.EqualTo(-2.5));
            Assert.That(value.GetProperty("CartesianCoordinates").GetProperty("Z").GetDouble(), Is.EqualTo(3.75));
            Assert.That(value.GetProperty("Orientation").GetProperty("A").GetDouble(), Is.EqualTo(10));
            Assert.That(value.GetProperty("Orientation").GetProperty("B").GetDouble(), Is.EqualTo(20));
            Assert.That(value.GetProperty("Orientation").GetProperty("C").GetDouble(), Is.EqualTo(30));
            Assert.That(wire.Paths.All(path =>
                path.RelativePath.Elements[0].ReferenceTypeId == ReferenceTypeIds.HasComponent), Is.True);
        }

        /// <summary>
        /// Resolving a parent rotation moves the child's translation into world coordinates in the stated units.
        /// </summary>
        [TestCase(PositioningAngleUnit.Degrees, 90.0)]
        [TestCase(PositioningAngleUnit.Radians, Math.PI / 2)]
        public async Task ReadWorldFrameComposesParentRotationAndTranslationAsync(PositioningAngleUnit unit, double yaw)
        {
            await using ServiceProvider services = Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId child = wire.Node("child-frame");
            NodeId parent = wire.Node("parent-frame");
            wire.Child(child, wire.Name("Base", Rsl.Namespaces.RSL), Variant.From(parent));
            wire.Child(parent, wire.Name("Base", Rsl.Namespaces.RSL), Variant.From(NodeId.Null));
            wire.Value(child, Variant.FromStructure(new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = 2, Y = 0, Z = 1 },
                Orientation = new ThreeDOrientation()
            }));
            wire.Value(parent, Variant.FromStructure(new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = 10, Y = 20, Z = 30 },
                Orientation = new ThreeDOrientation { C = yaw }
            }));
            await manager.RegisterExistingSessionAsync("survey", wire.Session.Object, "Anonymous")
                .ConfigureAwait(false);

            JsonElement json = Success(await new RelativePositioningTools(new PositioningClientAccessor(manager))
                .ReadAsync(child.ToString(), RelativePositioningRead.WorldFrame, unit, "survey").ConfigureAwait(false));

            Assert.That(json.GetProperty("translation").GetProperty("X").GetDouble(), Is.EqualTo(10).Within(1e-12));
            Assert.That(json.GetProperty("translation").GetProperty("Y").GetDouble(), Is.EqualTo(22).Within(1e-12));
            Assert.That(json.GetProperty("translation").GetProperty("Z").GetDouble(), Is.EqualTo(31).Within(1e-12));
            Assert.That(json.GetProperty("rotationMatrixRowMajor").EnumerateArray().Select(value => value.GetDouble()),
                Is.EqualTo([0.0, -1.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0]).Within(1e-12));
            Assert.That(json.GetProperty("frameChain").EnumerateArray().Select(value => value.GetString()),
                Is.EqualTo([child.ToString(), parent.ToString()]));
            Assert.That(json.GetProperty("angleUnit").GetString(), Is.EqualTo(unit.ToString()));
            Assert.That(json.GetProperty("frameToWorld").GetProperty("Orientation").GetProperty("C").GetDouble(),
                Is.EqualTo(yaw).Within(1e-12));
        }

        /// <summary>
        /// Cyclic frame relationships are a service error rather than a partial world-frame result.
        /// </summary>
        [Test]
        public async Task ReadWorldFrameRejectsCyclicBaseChainAsync()
        {
            await using ServiceProvider services = Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId frame = wire.Node("cyclic-frame");
            wire.Child(frame, wire.Name("Base", Rsl.Namespaces.RSL), Variant.From(frame));
            wire.Value(frame, Variant.FromStructure(new ThreeDFrame()));
            await manager.RegisterExistingSessionAsync("survey", wire.Session.Object, "Anonymous")
                .ConfigureAwait(false);

            CallToolResult result = await new RelativePositioningTools(new PositioningClientAccessor(manager))
                .ReadAsync(frame.ToString(), RelativePositioningRead.WorldFrame,
                    PositioningAngleUnit.Degrees, "survey").ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            Assert.That(result.StructuredContent!.Value.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.BadInvalidState.Code));
            Assert.That(result.StructuredContent.Value.GetProperty("message").GetString(), Does.Contain("cycle"));
            Assert.That(wire.Reads, Has.Count.EqualTo(2));
        }

        /// <summary>
        /// Global-position reads retain the raw server CRS and optional geographic fields without inference.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReadGlobalPositionPreservesCrsQualityAndOptionalFieldsAsync(bool optionalFields)
        {
            await using ServiceProvider services = Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId position = wire.Node("survey-position");
            NodeId source = wire.Node("survey-origin");
            PositionMetadata(wire, position, source, 4979);
            wire.Value(position, new DataValue(Variant.FromStructure(new GlobalPositionDataType
            {
                Longitude = 8.55,
                Latitude = 47.37,
                EncodingMask = optionalFields
                    ? (uint)S3DGeographicCoordinateDataTypeFields.Elevation |
                        (uint)GlobalPositionDataTypeFields.Accuracy |
                        (uint)GlobalPositionDataTypeFields.Floor
                    : 0,
                Elevation = 408.2,
                Accuracy = double.NaN,
                Floor = 2.5f
            }), StatusCodes.Uncertain, new DateTimeUtc(2026, 7, 8, 9, 10, 11)));
            await manager.RegisterExistingSessionAsync("survey", wire.Session.Object, "Anonymous")
                .ConfigureAwait(false);

            JsonElement json = Success(await new GlobalPositioningTools(new PositioningClientAccessor(manager))
                .ReadAsync(position.ToString(), GlobalPositioningRead.GlobalPosition,
                    sessionName: "survey").ConfigureAwait(false));

            Assert.That(json.GetProperty("sourceNodeId").GetString(), Is.EqualTo(source.ToString()));
            Assert.That(json.GetProperty("coordinateReferenceSystem").GetUInt32(), Is.EqualTo(4979));
            Assert.That(json.GetProperty("statusCode").GetUInt32(), Is.EqualTo(StatusCodes.Uncertain.Code));
            Assert.That(json.GetProperty("metadata").GetProperty("sourceTimestamp").GetString(),
                Does.StartWith("2026-07-08T09:10:11"));
            JsonElement value = json.GetProperty("position");
            Assert.That(value.GetProperty("Longitude").GetDouble(), Is.EqualTo(8.55));
            Assert.That(value.GetProperty("Latitude").GetDouble(), Is.EqualTo(47.37));
            Assert.That(value.TryGetProperty("Elevation", out JsonElement elevation), Is.EqualTo(optionalFields));
            Assert.That(value.TryGetProperty("Accuracy", out JsonElement accuracy), Is.EqualTo(optionalFields));
            Assert.That(value.TryGetProperty("Floor", out JsonElement floor), Is.EqualTo(optionalFields));
            if (optionalFields)
            {
                Assert.That(elevation.GetDouble(), Is.EqualTo(408.2));
                Assert.That(accuracy.GetString(), Is.EqualTo("NaN"));
                Assert.That(floor.GetSingle(), Is.EqualTo(2.5f));
            }
            Assert.That(wire.Paths.Single(path => path.RelativePath.Elements[0].TargetName.Name == "SourceId")
                .RelativePath.Elements[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasProperty));
            Assert.That(wire.Paths.Single(path =>
                path.RelativePath.Elements[0].TargetName.Name == "CoordinateReferenceSystem")
                .RelativePath.Elements[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
        }

        /// <summary>
        /// Aggregate location reads preserve nested coordinates and orientation only when it is advertised.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReadGlobalLocationPreservesNestedPositionAndOrientationPresenceAsync(bool orientationPresent)
        {
            await using ServiceProvider services = Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId location = wire.Node("global-location");
            NodeId position = wire.Node("position-component");
            NodeId source = wire.Node("source-zone");
            var coordinates = new GlobalPositionDataType { Longitude = -73.5, Latitude = 45.5 };
            wire.Path(location, position, wire.Name("Position", Rsl.Namespaces.RSL));
            wire.Value(position, Variant.FromStructure(coordinates));
            PositionMetadata(wire, position, source, 4326);
            wire.Value(location, Variant.FromStructure(new GlobalLocationDataType
            {
                Position = coordinates,
                Orientation = new ThreeDOrientation { A = 1.5, B = -2.5, C = 90 },
                EncodingMask = orientationPresent ? (uint)GlobalLocationDataTypeFields.Orientation : 0
            }));
            await manager.RegisterExistingSessionAsync("survey", wire.Session.Object, "Anonymous")
                .ConfigureAwait(false);

            JsonElement json = Success(await new GlobalPositioningTools(new PositioningClientAccessor(manager))
                .ReadAsync(location.ToString(), GlobalPositioningRead.GlobalLocation,
                    sessionName: "survey").ConfigureAwait(false));

            Assert.That(json.GetProperty("nodeId").GetString(), Is.EqualTo(location.ToString()));
            Assert.That(json.GetProperty("sourceNodeId").GetString(), Is.EqualTo(source.ToString()));
            Assert.That(json.GetProperty("valueType").GetString(), Is.EqualTo("GlobalLocationDataType"));
            JsonElement value = json.GetProperty("location");
            Assert.That(value.GetProperty("Position").GetProperty("Longitude").GetDouble(), Is.EqualTo(-73.5));
            Assert.That(value.GetProperty("Position").GetProperty("Latitude").GetDouble(), Is.EqualTo(45.5));
            Assert.That(value.TryGetProperty("Orientation", out JsonElement orientation),
                Is.EqualTo(orientationPresent));
            if (orientationPresent)
            {
                Assert.That(orientation.GetProperty("A").GetDouble(), Is.EqualTo(1.5));
                Assert.That(orientation.GetProperty("B").GetDouble(), Is.EqualTo(-2.5));
                Assert.That(orientation.GetProperty("C").GetDouble(), Is.EqualTo(90));
            }
            Assert.That(wire.Reads.Select(read => read.NodeId).Take(2), Is.EqualTo([location, position]));
        }

        /// <summary>
        /// Zone reads expose real fit diagnostics rather than reporting success for unfitted coordinates.
        /// </summary>
        [TestCase(PositioningFitMode.Rigid)]
        [TestCase(PositioningFitMode.Similarity)]
        [TestCase(PositioningFitMode.Affine)]
        public async Task ReadZoneTransformReportsRankResidualsAndEffectiveOptionsAsync(PositioningFitMode mode)
        {
            await using ServiceProvider services = Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId zone = wire.Node("equatorial-zone");
            NodeId points = ControlPoints(wire, zone);
            await manager.RegisterExistingSessionAsync("survey", wire.Session.Object, "Anonymous")
                .ConfigureAwait(false);

            JsonElement json = Success(await new GlobalPositioningTools(new PositioningClientAccessor(manager))
                .ReadAsync(zone.ToString(), GlobalPositioningRead.ZoneTransform,
                    new PositioningFitOptions { Mode = mode }, "survey").ConfigureAwait(false));

            Assert.That(json.GetProperty("zoneNodeId").GetString(), Is.EqualTo(zone.ToString()));
            Assert.That(json.GetProperty("mode").GetString(), Is.EqualTo(mode.ToString()));
            Assert.That(json.GetProperty("dimension").GetInt32(), Is.EqualTo(3));
            Assert.That(json.GetProperty("rank").GetInt32(), Is.EqualTo(3));
            Assert.That(json.GetProperty("isInvertible").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("rootMeanSquareError").GetDouble(), Is.LessThan(1e-5));
            Assert.That(json.GetProperty("maxResidual").GetDouble(), Is.LessThan(1e-5));
            Assert.That(json.GetProperty("determinant").GetDouble(), Is.EqualTo(1).Within(1e-6));
            Assert.That(json.GetProperty("controlPointAngleUnit").GetString(), Is.EqualTo("Degrees"));
            Assert.That(json.GetProperty("transformerCoordinateReferenceSystem").GetString(), Is.EqualTo("EPSG:4326"));
            Assert.That(json.GetProperty("allowReflection").GetBoolean(), Is.False);
            Assert.That(wire.Reads.Single().NodeId, Is.EqualTo(points));
        }

        /// <summary>
        /// Both transformation directions are checked against independent equatorial WGS84 coordinates.
        /// </summary>
        [TestCase(PositioningTransformDirection.LocalToGlobal, PositioningAngleUnit.Degrees, false)]
        [TestCase(PositioningTransformDirection.LocalToGlobal, PositioningAngleUnit.Radians, true)]
        [TestCase(PositioningTransformDirection.GlobalToLocal, PositioningAngleUnit.Degrees, false)]
        [TestCase(PositioningTransformDirection.GlobalToLocal, PositioningAngleUnit.Radians, true)]
        public async Task TransformUsesRequestedDirectionUnitsAndElevationAsync(
            PositioningTransformDirection direction,
            PositioningAngleUnit unit,
            bool elevationPresent)
        {
            await using ServiceProvider services = Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId zone = wire.Node("equatorial-zone");
            ControlPoints(wire, zone);
            await manager.RegisterExistingSessionAsync("survey", wire.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            const double equatorialRadius = 6378137;
            double elevation = elevationPresent ? 5.0 : 0.0;
            double longitudeRadians = Math.Atan2(10, equatorialRadius + elevation);
            double longitude = unit == PositioningAngleUnit.Radians
                ? longitudeRadians : longitudeRadians * 180 / Math.PI;
            double geographicElevation = Math.Sqrt(
                ((equatorialRadius + elevation) * (equatorialRadius + elevation)) + 100) -
                equatorialRadius;
            bool localToGlobal = direction == PositioningTransformDirection.LocalToGlobal;

            JsonElement json = Success(await new GlobalPositioningTools(new PositioningClientAccessor(manager))
                .TransformAsync(zone.ToString(), direction, localToGlobal ? 10 : longitude, 0, unit,
                    elevationPresent ? localToGlobal ? elevation : geographicElevation : null,
                    sessionName: "survey").ConfigureAwait(false));

            Assert.That(json.GetProperty("direction").GetString(), Is.EqualTo(direction.ToString()));
            Assert.That(json.GetProperty("angleUnit").GetString(), Is.EqualTo(unit.ToString()));
            Assert.That(json.GetProperty("controlPointAngleUnit").GetString(), Is.EqualTo("Degrees"));
            if (localToGlobal)
            {
                JsonElement global = json.GetProperty("globalCoordinates");
                Assert.That(global.GetProperty("Longitude").GetDouble(), Is.EqualTo(longitude).Within(1e-9));
                Assert.That(global.GetProperty("Latitude").GetDouble(), Is.Zero.Within(1e-9));
                Assert.That(global.GetProperty("Elevation").GetDouble(),
                    Is.EqualTo(geographicElevation).Within(1e-5));
                Assert.That(json.GetProperty("hasElevation").GetBoolean(), Is.True);
                Assert.That(json.GetProperty("elevationUnits").GetString(), Is.EqualTo("metres"));
            }
            else
            {
                JsonElement local = json.GetProperty("localCoordinates");
                Assert.That(local.GetProperty("X").GetDouble(), Is.EqualTo(10).Within(1e-5));
                Assert.That(local.GetProperty("Y").GetDouble(), Is.Zero.Within(1e-5));
                Assert.That(local.GetProperty("Z").GetDouble(), Is.EqualTo(elevation).Within(1e-5));
                Assert.That(json.GetProperty("linearUnits").GetString(), Is.EqualTo("Zone local coordinate units"));
            }
            wire.Session.Verify(value => value.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Wrong wire types fail with the original service status instead of returning default coordinates.
        /// </summary>
        [Test]
        public async Task ReadGlobalPositionRejectsWrongWireTypeAsync()
        {
            await using ServiceProvider services = Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId position = wire.Node("not-a-position");
            wire.Value(position, Variant.From("8,47"));
            await manager.RegisterExistingSessionAsync("survey", wire.Session.Object, "Anonymous")
                .ConfigureAwait(false);

            CallToolResult result = await new GlobalPositioningTools(new PositioningClientAccessor(manager))
                .ReadAsync(position.ToString(), GlobalPositioningRead.GlobalPosition,
                    sessionName: "survey").ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            Assert.That(result.StructuredContent!.Value.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.BadTypeMismatch.Code));
            Assert.That(wire.Paths, Is.Empty, "An invalid value must not trigger unrelated metadata reads.");
        }

        /// <summary>
        /// A finite NodeVersion observation reports exact version metadata and disposes only its enumerator.
        /// </summary>
        [Test]
        public async Task ObserveNodeVersionPreservesVersionQualityAndEnumeratorOwnershipAsync()
        {
            var wire = new JobsCoverageWireSession(NUnitTelemetryContext.Create());
            NodeId list = wire.Node("spatial-list");
            NodeId version = wire.Node("list-version");
            wire.Path(list, version, new QualifiedName("NodeVersion"));
            var client = new RelativeSpatialLocationClient(wire.Session.Object, NUnitTelemetryContext.Create());
            var streaming = new Mock<IStreamingSubscription>(MockBehavior.Strict);
            bool disposed = false;
            streaming.Setup(value => value.SubscribeDataChangesAsync(
                    version, It.IsAny<MonitoringOptions?>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId _, MonitoringOptions? _, CancellationToken ct) => ChangesAsync(ct));

            JsonObject json = await McpCompanionTools.ObserveAsync(
                ct => client.ObserveNodeVersionAsync(list, streaming.Object, cancellationToken: ct),
                value => PositioningJson.Version(value, wire.Context), 30000, 1).ConfigureAwait(false);

            Assert.That(json["stoppedBy"]!.GetValue<string>(), Is.EqualTo("itemLimit"));
            Assert.That(json["items"]![0]!["nodeVersion"]!.GetValue<string>(), Is.EqualTo("revision-42"));
            Assert.That(json["items"]![0]!["listNodeId"]!.GetValue<string>(), Is.EqualTo(list.ToString()));
            Assert.That(json["items"]![0]!["statusCode"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.Uncertain.Code));
            Assert.That(json["items"]![0]!["metadata"]!["sourceTimestamp"]!.GetValue<string>(),
                Does.StartWith("2026-07-08T09:10:11"));
            Assert.That(disposed, Is.True);
            streaming.Verify(value => value.DisposeAsync(), Times.Never);

            async IAsyncEnumerable<DataValueChange> ChangesAsync([EnumeratorCancellation] CancellationToken ct)
            {
                await Task.Yield();
                try
                {
                    ct.ThrowIfCancellationRequested();
                    yield return new DataValueChange(null, new DataValue(
                        Variant.From("revision-42"), StatusCodes.Uncertain,
                        new DateTimeUtc(2026, 7, 8, 9, 10, 11)), null);
                    Assert.Fail("A one-item observation must not request the next version.");
                }
                finally
                {
                    disposed = true;
                }
            }
        }

        /// <summary>
        /// Plain externally registered sessions explicitly refuse observations that need a managed subscription.
        /// </summary>
        [Test]
        public async Task ObserveToolsRejectUnmanagedSessionWithoutCreatingWireSubscriptionsAsync()
        {
            await using ServiceProvider services = Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            await manager.RegisterExistingSessionAsync("survey", wire.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            var accessor = new PositioningClientAccessor(manager);

            CallToolResult global = await new GlobalPositioningTools(accessor).ObserveAsync(
                wire.Node("global-position").ToString(), GlobalPositioningObservation.GlobalPosition,
                sessionName: "survey").ConfigureAwait(false);
            CallToolResult relative = await new RelativePositioningTools(accessor).ObserveAsync(
                wire.Node("frame").ToString(), RelativePositioningObservation.Frame,
                sessionName: "survey").ConfigureAwait(false);

            Assert.That(global.IsError, Is.True);
            Assert.That(relative.IsError, Is.True);
            Assert.That(global.StructuredContent!.Value.GetProperty("errorType").GetString(),
                Is.EqualTo(nameof(NotSupportedException)));
            Assert.That(relative.StructuredContent!.Value.GetProperty("message").GetString(),
                Does.Contain("ManagedSession"));
            Assert.That(wire.Reads, Is.Empty);
            Assert.That(wire.Paths, Is.Empty);
        }

        private static NodeId ControlPoints(JobsCoverageWireSession wire, NodeId zone)
        {
            var origin = new S3DGeographicCoordinateDataType
            {
                Longitude = 0,
                Latitude = 0,
                Elevation = 0,
                EncodingMask = (uint)S3DGeographicCoordinateDataTypeFields.Elevation
            };
            var plane = new LocalTangentPlane(origin, AngleUnit.Degrees);
            ArrayOf<GroundControlPointDataType> points =
            [
                Point(0, 0, 0),
                Point(30, 0, 0),
                Point(0, 40, 0),
                Point(0, 0, 50)
            ];
            return wire.Child(zone, wire.Name("GroundControlPoints", Gpos.Namespaces.GPOS),
                Variant.FromStructure(points));

            GroundControlPointDataType Point(double x, double y, double z)
            {
                var local = new ThreeDCartesianCoordinates { X = x, Y = y, Z = z };
                return new GroundControlPointDataType
                {
                    LocalPosition = local,
                    GlobalPosition = plane.EnuToGeographic(local, AngleUnit.Degrees, true)
                };
            }
        }

        private static void PositionMetadata(JobsCoverageWireSession wire, NodeId position, NodeId source, uint crs)
        {
            wire.Child(position, wire.Name("SourceId", Gpos.Namespaces.GPOS), Variant.From(source));
            wire.Child(position, wire.Name("CoordinateReferenceSystem", Gpos.Namespaces.GPOS), Variant.From(crs));
        }

        private static ServiceProvider Services()
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore().AddOpcUaMcpPositioning();
            return services.BuildServiceProvider();
        }

        private static JsonElement Success(CallToolResult result)
        {
            Assert.That(result.IsError, Is.False, result.StructuredContent?.ToString());
            Assert.That(result.StructuredContent.HasValue, Is.True);
            return result.StructuredContent!.Value;
        }
    }
}
#endif
