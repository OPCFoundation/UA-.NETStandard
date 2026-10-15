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

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using GlobalPositionDataType = Opc.Ua.Gpos.GlobalPositionDataType;
using S3DGeographicCoordinateDataTypeFields = Opc.Ua.Gpos.S3DGeographicCoordinateDataTypeFields;

namespace UaLens.Tests.Companions
{
    public sealed partial class PositioningCompanionProviderTests
    {
        [Test]
        public async Task GlobalPositionKeepsEveryCoordinateTimestampAsync()
        {
            var test = new PositioningContext();
            CompanionTarget target = AddGlobalPosition(test, new GlobalPositionDataType
            {
                EncodingMask = (uint)S3DGeographicCoordinateDataTypeFields.Elevation,
                Longitude = 0,
                Latitude = 0,
                Elevation = 10
            });
            var timestamp = new DateTimeUtc(2026, 10, 10, 12);
            test.Services.ReadHandler = (read, _) =>
            {
                DataValue value = test.Services.Read(read);
                if (read.NodeId == target.NodeId && read.AttributeId == Attributes.Value)
                {
                    return value.WithSourceTimestamp(timestamp);
                }
                return value;
            };
            CompanionInspection snapshot = await test.Provider.InspectAsync(
                test.Context, target, CancellationToken.None).ConfigureAwait(false);
            string[] names =
            [
                "Longitude (degrees)", "Latitude (degrees)", "Ellipsoidal elevation (metres)",
                "ECEF X (metres)", "ECEF Y (metres)", "ECEF Z (metres)"
            ];
            foreach (string name in names)
            {
                CompanionValue field = snapshot.Values.ToList().Single(value => value.Name == name);
                Assert.That(field.SourceNodeId, Is.EqualTo(target.NodeId), name);
                Assert.That(field.SourceTimestamp, Is.EqualTo(timestamp.ToDateTime()), name);
                Assert.That(field.Quality, Is.EqualTo(StatusCodes.Good), name);
            }
            Assert.That(IndustrialCompanionTestSession.Field(snapshot.Values, "ECEF X (metres)")
                .TryGetValue(out double x), Is.True);
            Assert.That(x, Is.EqualTo(6378147.0).Within(1e-6));
            Assert.That(IndustrialCompanionTestSession.Field(snapshot.Values, "ECEF Y (metres)"),
                Is.EqualTo(Variant.From(0.0)));
            Assert.That(IndustrialCompanionTestSession.Field(snapshot.Values, "ECEF Z (metres)"),
                Is.EqualTo(Variant.From(0.0)));
        }

        [Test]
        public async Task GlobalPositionWithoutElevationDoesNotInventAnEcefHeightAsync()
        {
            var test = new PositioningContext();
            CompanionTarget target = AddGlobalPosition(test, new GlobalPositionDataType
            {
                Longitude = 11.5,
                Latitude = 48.1,
                Elevation = double.NaN
            });
            CompanionInspection snapshot = await test.Provider.InspectAsync(
                test.Context, target, CancellationToken.None).ConfigureAwait(false);
            Assert.That(IndustrialCompanionTestSession.Field(snapshot.Values, "Longitude (degrees)"),
                Is.EqualTo(Variant.From(11.5)));
            Assert.That(snapshot.Values.ToList().Select(value => value.Name),
                Does.Not.Contain("Ellipsoidal elevation (metres)").And.Not.Contain("ECEF X (metres)"));
        }

        [TestCase(0u)]
        [TestCase(3857u)]
        public async Task UnsupportedGlobalCrsIsNotSilentlyProjectedAsync(uint crs)
        {
            var test = new PositioningContext();
            CompanionTarget target = AddGlobalPosition(test, new GlobalPositionDataType
            {
                Longitude = 11.5,
                Latitude = 48.1
            }, crs);
            await Assert.ThatAsync(async () => await test.Provider.InspectAsync(
                test.Context, target, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNotSupported))
                .ConfigureAwait(false);
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task DiscoveryFindsTheGlobalPositionAttachedToTheParentRobotOnceAsync()
        {
            var test = new PositioningContext();
            CompanionTarget target = AddGlobalPosition(test, new GlobalPositionDataType());
            ArrayOf<CompanionTarget> targets = await test.Provider.DiscoverAsync(
                test.Context, CancellationToken.None).ConfigureAwait(false);
            Assert.That(targets, Has.Count.EqualTo(2));
            Assert.That(targets.ToList().Single(value => value.TypeName == "Global position").NodeId,
                Is.EqualTo(target.NodeId));
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task GlobalOnlyEntryDoesNotRequireRelativeSpatialLocationsAsync()
        {
            var test = new PositioningContext();
            CompanionTarget target = AddGlobalPosition(test, new GlobalPositionDataType());
            NodeId relativeRoot = test.Services.Resolve(Opc.Ua.Rsl.ObjectIds.RelativeSpatialLocations);
            test.Services.SetValue(relativeRoot, Variant.Null, Attributes.NodeClass, StatusCodes.BadNodeIdUnknown);
            test.Services.BrowseHandler = (description, _) => description.NodeId == relativeRoot
                ? new BrowseResult { StatusCode = StatusCodes.BadNodeIdUnknown }
                : test.Services.Browse(description);
            NodeId globalRoot = test.Services.Resolve(Opc.Ua.Gpos.ObjectIds.GlobalLocations);
            test.Services.SetReferences(globalRoot, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences,
            [
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(target.NodeId), NodeClass = NodeClass.Variable,
                    TypeDefinition = Opc.Ua.Gpos.VariableTypeIds.GlobalPositionType,
                    BrowseName = new QualifiedName("GlobalPosition")
                }
            ]);
            ArrayOf<CompanionTarget> targets = await test.Provider.DiscoverAsync(
                test.Context, CancellationToken.None).ConfigureAwait(false);
            Assert.That(targets, Has.Count.EqualTo(1));
            Assert.That(targets[0].NodeId, Is.EqualTo(target.NodeId));
            CompanionInspection snapshot = await test.Provider.InspectAsync(
                test.Context, targets[0], CancellationToken.None).ConfigureAwait(false);
            Assert.That(IndustrialCompanionTestSession.Field(snapshot.Values, "Longitude (degrees)"),
                Is.EqualTo(Variant.From(0.0)));
            Assert.That(test.Services.BrowseCalls.ToList().Any(call => call.NodeId == relativeRoot), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DeniedOrUncertainEntryIsNotReportedAsAbsentAsync(bool uncertain)
        {
            var test = new PositioningContext();
            _ = AddGlobalPosition(test, new GlobalPositionDataType());
            StatusCode quality = uncertain ? StatusCodes.Uncertain : StatusCodes.BadUserAccessDenied;
            test.Services.SetValue(test.Services.Resolve(Opc.Ua.Rsl.ObjectIds.RelativeSpatialLocations),
                Variant.From((int)NodeClass.Object), Attributes.NodeClass, quality);
            await Assert.ThatAsync(async () => await test.Provider.DiscoverAsync(
                test.Context, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(quality))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task DiscoveryEnforcesTheCombinedSpatialAndGlobalTargetLimitAsync()
        {
            var test = new PositioningContext();
            _ = AddGlobalPosition(test, new GlobalPositionDataType());
            await Assert.ThatAsync(async () => await test.Provider.DiscoverAsync(
                test.Services.Context(maxTargets: 1), CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadEncodingLimitsExceeded)).ConfigureAwait(false);
        }

        [TestCase(181.0, 0.0)]
        [TestCase(-181.0, 0.0)]
        [TestCase(0.0, 91.0)]
        [TestCase(0.0, -91.0)]
        [TestCase(double.NaN, 0.0)]
        [TestCase(0.0, double.PositiveInfinity)]
        public async Task GlobalPositionRejectsInvalidWgs84CoordinatesAsync(double longitude, double latitude)
        {
            var test = new PositioningContext();
            CompanionTarget target = AddGlobalPosition(test, new GlobalPositionDataType
            {
                Longitude = longitude,
                Latitude = latitude
            });
            await Assert.ThatAsync(async () => await test.Provider.InspectAsync(
                test.Context, target, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidArgument))
                .ConfigureAwait(false);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task GlobalLocationHonorsItsOptionalOrientationMaskAsync(bool encoded, bool invalid)
        {
            var test = new PositioningContext();
            _ = AddGlobalPosition(test, new GlobalPositionDataType());
            var location = new Opc.Ua.Gpos.GlobalLocationDataType
            {
                EncodingMask = encoded ? (uint)Opc.Ua.Gpos.GlobalLocationDataTypeFields.Orientation : 0,
                Position = new GlobalPositionDataType { Longitude = 12, Latitude = 45 },
                Orientation = new() { A = !encoded || invalid ? double.NaN : 10, B = 20, C = 30 }
            };
            NodeId node = test.Services.Id("global-location");
            test.Services.SetValue(node, Variant.FromStructure(location));
            test.Services.SetValue(node, Variant.From((int)NodeClass.Variable), Attributes.NodeClass);
            test.Services.SetReferences(node, BrowseDirection.Forward, ReferenceTypeIds.HasTypeDefinition,
            [
                new ReferenceDescription
                {
                    NodeId = Opc.Ua.Gpos.VariableTypeIds.GlobalLocationType, NodeClass = NodeClass.VariableType
                }
            ]);
            NodeId position = test.Services.AddProperty(
                node, Opc.Ua.Rsl.Namespaces.RSL, "Position", Variant.FromStructure(location.Position));
            test.Services.AddProperty(position, Opc.Ua.Gpos.Namespaces.GPOS, "SourceId", Variant.From(NodeId.Null));
            test.Services.AddProperty(
                position, Opc.Ua.Gpos.Namespaces.GPOS, "CoordinateReferenceSystem", Variant.From(4326u));
            var target = new CompanionTarget("positioning", node, "Global location", "Global location");
            if (invalid)
            {
                await Assert.ThatAsync(async () => await test.Provider.InspectAsync(
                    test.Context, target, CancellationToken.None).ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadInvalidArgument))
                    .ConfigureAwait(false);
                return;
            }
            CompanionInspection snapshot = await test.Provider.InspectAsync(
                test.Context, target, CancellationToken.None).ConfigureAwait(false);
            Assert.That(IndustrialCompanionTestSession.Field(snapshot.Values, "Longitude (degrees)"),
                Is.EqualTo(Variant.From(12.0)));
            CompanionValue? orientation = snapshot.Values.ToList()
                .SingleOrDefault(value => value.Name == "Encoded orientation");
            if (encoded)
            {
                Assert.That(orientation, Is.Not.Null);
                Assert.That(orientation!.Value, Is.EqualTo(Variant.FromStructure(location.Orientation)));
                Assert.That(orientation.SourceNodeId, Is.EqualTo(node));
            }
            else
            {
                Assert.That(orientation, Is.Null);
            }
        }

        private static CompanionTarget AddGlobalPosition(
            PositioningContext test, GlobalPositionDataType value, uint crs = 4326)
        {
            test.Services.NamespaceUris.GetIndexOrAppend(Opc.Ua.Gpos.Namespaces.GPOS);
            test.Services.AddObject(test.Services.Resolve(Opc.Ua.Gpos.ObjectIds.GlobalLocations),
                ObjectTypeIds.FolderType, "GlobalLocations");
            NodeId position = test.Services.Id("global-position");
            test.Services.SetValue(position, Variant.FromStructure(value));
            test.Services.SetValue(position, Variant.From((int)NodeClass.Variable), Attributes.NodeClass);
            NodeId type = test.Services.Resolve(Opc.Ua.Gpos.VariableTypeIds.GlobalPositionType);
            test.Services.SetReferences(position, BrowseDirection.Forward, ReferenceTypeIds.HasTypeDefinition,
                [new ReferenceDescription { NodeId = new ExpandedNodeId(type), NodeClass = NodeClass.VariableType }]);
            test.Services.AddProperty(position, Opc.Ua.Gpos.Namespaces.GPOS, "SourceId", Variant.From(NodeId.Null));
            test.Services.AddProperty(
                position, Opc.Ua.Gpos.Namespaces.GPOS, "CoordinateReferenceSystem", Variant.From(crs));
            NodeId robot = test.Services.Id("robot");
            test.Services.AddObject(robot, ObjectTypeIds.BaseObjectType);
            test.Services.AddChild(robot, test.Target.NodeId);
            test.Services.SetReferences(robot, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences,
            [
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(position), TypeDefinition = new ExpandedNodeId(type),
                    NodeClass = NodeClass.Variable, BrowseName = new QualifiedName("GlobalPosition")
                }
            ]);
            return new CompanionTarget("positioning", position, "Global position", "Global position");
        }
    }
}
