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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Positioning;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed partial class PositioningCompanionProviderTests
    {
        [Test]
        public async Task DiscoveryPreservesRemappedSpatialIdentityWithoutStartingWorkAsync()
        {
            var test = new PositioningContext();
            ArrayOf<CompanionTarget> targets = await test.Provider.DiscoverAsync(
                test.Context, CancellationToken.None).ConfigureAwait(false);
            Assert.That(targets, Has.Count.EqualTo(1));
            Assert.That(targets[0].NodeId, Is.EqualTo(test.Target.NodeId));
            Assert.That(targets[0].TypeName, Is.EqualTo("Spatial object"));
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task SnapshotKeepsFrameCoordinatesAndSourceWithoutRendererAsync()
        {
            var test = new PositioningContext();
            CompanionInspection result = await test.Provider.InspectAsync(
                test.Context, test.Target, CancellationToken.None).ConfigureAwait(false);
            CompanionValue x = result.Values.ToList().Single(value => value.Name == "Position frame / X");
            Assert.That(x.Value, Is.EqualTo(Variant.From(3.0)));
            Assert.That(x.SourceNodeId, Is.EqualTo(test.FrameNode));
            Assert.That(x.Quality, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Operations.ToList()
                .All(operation => operation.Safety == CompanionOperationSafety.ReadOnly),
                Is.True);
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task SnapshotPreservesDeclaredFrameUnitsAsync()
        {
            var test = new PositioningContext();
            (NodeId length, NodeId angle) = test.SetFrameUnits(test.FrameNode, "mm", "deg");
            CompanionInspection snapshot = await test.Provider.InspectAsync(
                test.Context, test.Target, CancellationToken.None).ConfigureAwait(false);
            CompanionValue x = snapshot.Values.ToList().Single(value => value.Name == "Position frame / X");
            CompanionValue c = snapshot.Values.ToList().Single(value => value.Name == "Position frame / C");
            Assert.That(x.EngineeringMetadata, Does.Contain("mm"));
            Assert.That(c.EngineeringMetadata, Does.Contain("deg"));
            CompanionValue lengthUnit = snapshot.Values.ToList()
                .Single(value => value.Name == "Position frame / Length unit");
            CompanionValue angleUnit = snapshot.Values.ToList()
                .Single(value => value.Name == "Position frame / Angle unit");
            Assert.That(lengthUnit.SourceNodeId, Is.EqualTo(length));
            Assert.That(angleUnit.SourceNodeId, Is.EqualTo(angle));
            Assert.That(lengthUnit.Value.TryGetValue<EUInformation>(
                out EUInformation? unit, test.Services.MessageContext), Is.True);
            Assert.That(unit!.DisplayName.Text, Is.EqualTo("mm"));
        }

        [Test]
        public async Task WorldResolutionReusesTheExistingRslCompositionOrderAsync()
        {
            var test = new PositioningContext();
            NodeId parent = test.Services.Id("parent-frame");
            test.Services.SetValue(parent, Variant.FromStructure(new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = 10, Y = 20, Z = 30 },
                Orientation = new ThreeDOrientation { C = 90 }
            }));
            test.Services.AddProperty(parent, Opc.Ua.Rsl.Namespaces.RSL, "Base", Variant.From(NodeId.Null));
            test.Services.SetValue(test.BaseNode, Variant.From(parent));
            test.SetFrameUnits(parent, "m", "deg");
            CompanionOperationResult world = await test.Provider.ExecuteAsync(
                test.Context, test.Target, "world-degrees", null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(world.Values.ToList().Single(value => value.Name == "World frame / X").Value
                .TryGetValue(out double x), Is.True);
            Assert.That(world.Values.ToList().Single(value => value.Name == "World frame / Y").Value
                .TryGetValue(out double y), Is.True);
            Assert.That(x, Is.EqualTo(6).Within(1e-9));
            Assert.That(y, Is.EqualTo(23).Within(1e-9));
            Assert.That(world.Values.ToList().Single(value => value.Name == "Frame chain").Value
                .TryGetValue(out ArrayOf<NodeId> chain), Is.True);
            Assert.That(chain, Has.Count.EqualTo(2));
            Assert.That(chain[0], Is.EqualTo(test.FrameNode));
            Assert.That(chain[1], Is.EqualTo(parent));
        }

        [TestCase("mm", "deg", "world-degrees")]
        [TestCase("m", "rad", "world-degrees")]
        [TestCase("m", "deg", "world-radians")]
        public async Task WorldResolutionRejectsIncompatibleDeclaredUnitsAsync(
            string parentLength, string parentAngle, string operation)
        {
            var test = new PositioningContext();
            NodeId parent = test.Services.Id("parent-frame");
            test.Services.SetValue(parent, Variant.FromStructure(new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = 10 },
                Orientation = new ThreeDOrientation { C = 90 }
            }));
            test.Services.AddProperty(parent, Opc.Ua.Rsl.Namespaces.RSL, "Base", Variant.From(NodeId.Null));
            test.Services.SetValue(test.BaseNode, Variant.From(parent));
            test.SetFrameUnits(parent, parentLength, parentAngle);
            await Assert.ThatAsync(async () => await test.Provider.ExecuteAsync(
                test.Context, test.Target, operation, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNotSupported))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task BaseCycleCannotPublishAWorldPoseAsync()
        {
            var test = new PositioningContext();
            test.Services.SetValue(test.BaseNode, Variant.From(test.FrameNode));
            await Assert.ThatAsync(async () => await test.Provider.ExecuteAsync(
                test.Context, test.Target, "world-degrees", null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState))
                .ConfigureAwait(false);
        }

        [TestCase("frame")]
        [TestCase("base")]
        public async Task UncertainFrameOrBaseCannotBeUsedForAPoseAsync(string uncertain)
        {
            var test = new PositioningContext();
            NodeId bad = uncertain == "frame" ? test.FrameNode : test.BaseNode;
            DataValue value = test.Services.Read(new ReadValueId { NodeId = bad, AttributeId = Attributes.Value });
            test.Services.SetValue(bad, value.WrappedValue, status: StatusCodes.Uncertain);
            await Assert.ThatAsync(async () => await test.Provider.InspectAsync(
                test.Context, test.Target, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.Uncertain))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ChangedSpatialListVersionRejectsTheWholeSnapshotAsync()
        {
            var test = new PositioningContext();
            test.Services.ReadHandler = (read, _) =>
            {
                if (read.NodeId == test.FrameNode)
                {
                    test.Services.SetValue(test.VersionNode, Variant.From("2"));
                }
                return test.Services.Read(read);
            };
            await Assert.ThatAsync(async () => await test.Provider.InspectAsync(
                test.Context, test.Target, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task CanceledDiscoveryDoesNotReadOrAppendNamespacesAsync()
        {
            var test = new PositioningContext();
            int count = test.Services.NamespaceUris.Count;
            await Assert.ThatAsync(async () => await test.Provider.DiscoverAsync(
                test.Context, new CancellationToken(true)).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(test.Services.NamespaceUris.Count, Is.EqualTo(count));
            Assert.That(test.Services.BrowseCalls.IsEmpty, Is.True);
        }

        private sealed class PositioningContext
        {
            public PositioningContext(TimeProvider? timeProvider = null)
            {
                Provider = new PositioningCompanionProvider(timeProvider);
                Services.NamespaceUris.GetIndexOrAppend(Opc.Ua.Rsl.Namespaces.RSL);
                Services.Session.SetupGet(session => session.Factory).Returns(Services.MessageContext.Factory);
                Services.Session.SetupGet(session => session.Connected).Returns(true);
                Services.Session.SetupGet(session => session.SessionId).Returns(new NodeId(1234u));
                Services.Session.SetupGet(session => session.Identity).Returns(new UserIdentity());
                Services.Session.SetupGet(session => session.Endpoint)
                    .Returns(new EndpointDescription("opc.tcp://127.0.0.1:58124/Positioning"));
                var cache = new Mock<INodeCache>(MockBehavior.Strict);
                cache.Setup(value => value.IsTypeOfAsync(
                    It.IsAny<ExpandedNodeId>(), It.IsAny<ExpandedNodeId>(), It.IsAny<CancellationToken>()))
                    .Returns((ExpandedNodeId actual, ExpandedNodeId expected, CancellationToken _) =>
                        new ValueTask<bool>(Services.Resolve(actual) == Services.Resolve(expected)));
                Services.Session.SetupGet(session => session.NodeCache).Returns(cache.Object);
                NodeId root = Services.Resolve(Opc.Ua.Rsl.ObjectIds.RelativeSpatialLocations);
                Services.AddObject(root, ObjectTypeIds.FolderType, "RelativeSpatialLocations");
                NodeId list = Services.Id("spatial-list");
                NodeId spatial = Services.Id("spatial-object");
                Services.AddObject(list, Opc.Ua.Rsl.ObjectTypeIds.SpatialObjectsListType, "Frames");
                Services.AddObject(spatial, Opc.Ua.Rsl.ObjectTypeIds.SpatialObjectType, "Robot frame");
                AddOrganizes(root, list, Opc.Ua.Rsl.ObjectTypeIds.SpatialObjectsListType);
                AddOrganizes(list, spatial, Opc.Ua.Rsl.ObjectTypeIds.SpatialObjectType);
                Services.AddChild(list, spatial);
                VersionNode = Services.AddProperty(list, Namespaces.OpcUa, BrowseNames.NodeVersion, Variant.From("1"));
                FrameNode = Services.Id("position-frame");
                Services.AddObject(FrameNode, ObjectTypeIds.BaseObjectType);
                Services.AddChild(spatial, FrameNode, Opc.Ua.Rsl.Namespaces.RSL, "PositionFrame");
                Services.SetValue(FrameNode, Variant.FromStructure(new ThreeDFrame
                {
                    CartesianCoordinates = new ThreeDCartesianCoordinates { X = 3, Y = 4, Z = 5 },
                    Orientation = new ThreeDOrientation()
                }));
                BaseNode = Services.AddProperty(
                    FrameNode, Opc.Ua.Rsl.Namespaces.RSL, "Base", Variant.From(NodeId.Null));
                SetFrameUnits(FrameNode, "m", "deg");
                Target = new CompanionTarget("positioning", spatial, "Robot frame", "Spatial object");
            }

            public IndustrialCompanionTestSession Services { get; } = new();
            public CompanionContext Context => Services.Context();
            public PositioningCompanionProvider Provider { get; }
            public CompanionTarget Target { get; }
            public NodeId FrameNode { get; }
            public NodeId BaseNode { get; }
            public NodeId VersionNode { get; }

            public (NodeId Length, NodeId Angle) SetFrameUnits(NodeId frame, string length, string angle)
            {
                NodeId position = Services.AddProperty(frame, Opc.Ua.Rsl.Namespaces.RSL, "Position", Variant.Null);
                NodeId orientation = Services.AddProperty(
                    frame, Opc.Ua.Rsl.Namespaces.RSL, "Orientation", Variant.Null);
                NodeId lengthUnit = Services.AddProperty(position, Namespaces.OpcUa, "LengthUnit",
                    Variant.FromStructure(new EUInformation(
                        length, "http://www.opcfoundation.org/UA/units/un/cefact")));
                NodeId angleUnit = Services.AddProperty(orientation, Namespaces.OpcUa, "AngleUnit",
                    Variant.FromStructure(new EUInformation(
                        angle, "http://www.opcfoundation.org/UA/units/un/cefact")));
                return (lengthUnit, angleUnit);
            }

            private void AddOrganizes(NodeId root, NodeId node, ExpandedNodeId type)
            {
                Services.SetReferences(root, BrowseDirection.Forward, ReferenceTypeIds.Organizes,
                [
                    new ReferenceDescription
                    {
                        NodeId = new ExpandedNodeId(node), TypeDefinition = new ExpandedNodeId(Services.Resolve(type)),
                        NodeClass = NodeClass.Object, BrowseName = new QualifiedName(node.ToString())
                    }
                ]);
            }
        }
    }
}
