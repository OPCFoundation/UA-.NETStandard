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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gpos;
using Opc.Ua.Positioning.Server;
using Opc.Ua.Rsl;

namespace Opc.Ua.Positioning.Tests
{
    [TestFixture]
    [Category("Positioning")]
    [NonParallelizable]
    public sealed class PositioningAddressSpaceBuilderTests
    {
        private PositioningServerFixture? m_fixture;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new PositioningServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task BuilderCreatesRslAndGposInstancesAsync()
        {
            PositioningNodeManager manager = m_fixture!.Manager;
            PositioningAddressSpaceBuilder builder =
                manager.CreatePositioningBuilder();
            ushort rslNamespaceIndex = (ushort)manager.Server.NamespaceUris
                .GetIndex(Rsl.Namespaces.RSL);
            var owner = new BaseObjectState(null)
            {
                NodeId = new NodeId("TrackedAsset", rslNamespaceIndex),
                BrowseName = new QualifiedName("TrackedAsset", rslNamespaceIndex),
                DisplayName = new LocalizedText("TrackedAsset"),
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };
            await manager.AddPredefinedNodeAsync(owner).ConfigureAwait(false);

            EUInformation metres = new(
                "m",
                "metre",
                "http://www.opcfoundation.org/UA/units/un/cefact");
            EUInformation degrees = new(
                "deg",
                "degree",
                "http://www.opcfoundation.org/UA/units/un/cefact");
            var frame = new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates
                {
                    X = 1.0,
                    Y = 2.0,
                    Z = 3.0
                },
                Orientation = new ThreeDOrientation { C = 45.0 }
            };

            SpatialObjectsListState list = builder.CreateSpatialObjectsList(
                owner,
                new QualifiedName("CellFrames", rslNamespaceIndex),
                "cell",
                new ThreeDFrame
                {
                    CartesianCoordinates = new ThreeDCartesianCoordinates(),
                    Orientation = new ThreeDOrientation()
                },
                metres,
                degrees);
            await builder.RegisterAsync(list).ConfigureAwait(false);

            SpatialObjectState spatialObject = builder.AttachSpatialObject(
                owner,
                list,
                new QualifiedName("SpatialObject", rslNamespaceIndex),
                "asset",
                frame,
                metres,
                degrees);
            CartesianFrameAngleOrientationState internalFrame =
                builder.AddInternalFrame(
                    spatialObject,
                    new QualifiedName("SensorFrame", rslNamespaceIndex),
                    spatialObject.PositionFrame!.NodeId,
                    frame,
                    metres,
                    degrees);
            CartesianFrameAngleOrientationState alternativeFrame =
                builder.AddAlternativeFrame(
                    spatialObject,
                    new QualifiedName("SurveyFrame", rslNamespaceIndex),
                    spatialObject.PositionFrame.NodeId,
                    frame,
                    metres,
                    degrees);
            await builder.RegisterAsync(spatialObject).ConfigureAwait(false);

            var global = new S3DGeographicCoordinateDataType
            {
                EncodingMask =
                    (uint)S3DGeographicCoordinateDataTypeFields.Elevation,
                Longitude = 8.0,
                Latitude = 47.0,
                Elevation = 500.0
            };
            var controlPoint = new GroundControlPointDataType
            {
                GlobalPosition = global,
                LocalPosition = new ThreeDCartesianCoordinates()
            };
            ZoneState zone = builder.CreateZone(
                new QualifiedName("CellZone", rslNamespaceIndex),
                "cell-zone",
                ArrayOf.Create([controlPoint]));
            await builder.RegisterAsync(zone).ConfigureAwait(false);

            GlobalLocationState location = builder.AttachGlobalLocation(
                owner,
                new QualifiedName("GlobalLocation", rslNamespaceIndex),
                zone.NodeId,
                4326);
            await builder.RegisterAsync(location).ConfigureAwait(false);
            GlobalPositionState position = builder.AttachGlobalPosition(
                owner,
                new QualifiedName("GlobalPosition", rslNamespaceIndex),
                zone.NodeId,
                4326);
            await builder.RegisterAsync(position).ConfigureAwait(false);
            var sample = new GlobalPositionSample(
                "asset",
                new GlobalLocationDataType
                {
                    Position = new GlobalPositionDataType
                    {
                        Longitude = 8.0,
                        Latitude = 47.0
                    }
                },
                StatusCodes.Good,
                DateTimeUtc.Now);
            builder.SetGlobalPositionValue(position, sample);

            ZoneState proximityZone = builder.CreateProximityZone(
                new QualifiedName("ProximityZone", rslNamespaceIndex),
                "proximity-zone",
                global,
                2.5);
            await builder.RegisterAsync(proximityZone).ConfigureAwait(false);
            var positionFrameState =
                (CartesianFrameAngleOrientationState)spatialObject.PositionFrame!;
            var worldFrameState =
                (CartesianFrameAngleOrientationState)list.WorldFrame!;

            Assert.Multiple(() =>
            {
                Assert.That(list.WorldFrame, Is.TypeOf<CartesianFrameAngleOrientationState>());
                Assert.That(list.NodeVersion!.Value, Is.EqualTo("2"));
                Assert.That(
                    worldFrameState.Position!.X!.Value,
                    Is.Zero);
                Assert.That(spatialObject.PositionFrame,
                    Is.TypeOf<CartesianFrameAngleOrientationState>());
                Assert.That(spatialObject.PositionFrame!.Base!.Value,
                    Is.EqualTo(list.WorldFrame!.NodeId));
                Assert.That(positionFrameState.Position!.X!.Value,
                    Is.EqualTo(frame.CartesianCoordinates.X));
                Assert.That(positionFrameState.Orientation!.C!.Value,
                    Is.EqualTo(frame.Orientation.C));
                Assert.That(internalFrame.Base!.Value,
                    Is.EqualTo(spatialObject.PositionFrame.NodeId));
                Assert.That(internalFrame.Position!.X!.Value,
                    Is.EqualTo(frame.CartesianCoordinates.X));
                Assert.That(alternativeFrame.Base!.Value,
                    Is.EqualTo(spatialObject.PositionFrame.NodeId));
                Assert.That(zone.ZoneId!.Value, Is.EqualTo("cell-zone"));
                Assert.That(zone.GroundControlPoints!.Value.Count, Is.EqualTo(1));
                Assert.That(location.Position!.SourceId!.Value,
                    Is.EqualTo(zone.NodeId));
                Assert.That(location.Position.CoordinateReferenceSystem!.Value,
                    Is.EqualTo(4326));
                Assert.That(position.Value.Longitude, Is.EqualTo(8.0));
                Assert.That(position.SourceId!.Value, Is.EqualTo(zone.NodeId));
                Assert.That(proximityZone.Position!.Value, Is.SameAs(global));
                Assert.That(proximityZone.Radius!.Value, Is.EqualTo(2.5));
            });
        }

        [Test]
        public async Task BuilderRejectsInvalidAndDuplicateValuesAsync()
        {
            PositioningNodeManager manager = m_fixture!.Manager;
            PositioningAddressSpaceBuilder builder =
                manager.CreatePositioningBuilder();
            ushort namespaceIndex = (ushort)manager.Server.NamespaceUris
                .GetIndex(Rsl.Namespaces.RSL);
            var owner = new BaseObjectState(null)
            {
                NodeId = new NodeId("ValidationOwner", namespaceIndex),
                BrowseName = new QualifiedName("ValidationOwner", namespaceIndex),
                DisplayName = new LocalizedText("ValidationOwner"),
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };
            await manager.AddPredefinedNodeAsync(owner).ConfigureAwait(false);
            EUInformation metres = new(
                "m",
                "metre",
                "http://www.opcfoundation.org/UA/units/un/cefact");
            EUInformation degrees = new(
                "deg",
                "degree",
                "http://www.opcfoundation.org/UA/units/un/cefact");
            var frame = new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates(),
                Orientation = new ThreeDOrientation()
            };

            _ = builder.CreateSpatialObjectsList(
                owner,
                new QualifiedName("Frames", namespaceIndex),
                "frames",
                frame,
                metres,
                degrees);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => builder.CreateSpatialObjectsList(
                        owner,
                        new QualifiedName("Frames", namespaceIndex),
                        "duplicate",
                        frame,
                        metres,
                        degrees),
                    Throws.TypeOf<ServiceResultException>());
                Assert.That(
                    () => builder.AttachGlobalPosition(
                        owner,
                        new QualifiedName("Position", namespaceIndex),
                        NodeId.Null,
                        4326),
                    Throws.TypeOf<ArgumentException>());
                Assert.That(
                    () => builder.CreateProximityZone(
                        new QualifiedName("InvalidZone", namespaceIndex),
                        "invalid-zone",
                        new S3DGeographicCoordinateDataType
                        {
                            Longitude = 181.0,
                            Latitude = 47.0
                        },
                        1.0),
                    Throws.TypeOf<ServiceResultException>());
            });
        }

        [Test]
        public void FactoryAdvertisesBothNamespaces()
        {
            var factory = new PositioningNodeManagerFactory();
            Assert.That(
                factory.NamespacesUris.ToArray(),
                Is.EquivalentTo(
                [
                    Rsl.Namespaces.RSL,
                    Gpos.Namespaces.GPOS
                ]));
        }

        [Test]
        public async Task NodeManagerPublishesRslAndGposNamespaceMetadataAsync()
        {
            PositioningNodeManager manager = m_fixture!.Manager;
            NamespaceMetadataState? rsl = await manager.Server.NodeManager
                .ConfigurationNodeManager!
                .GetNamespaceMetadataStateAsync(Opc.Ua.Rsl.Namespaces.RSL)
                .ConfigureAwait(false);
            NamespaceMetadataState? gpos = await manager.Server.NodeManager
                .ConfigurationNodeManager!
                .GetNamespaceMetadataStateAsync(Opc.Ua.Gpos.Namespaces.GPOS)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(rsl, Is.Not.Null);
                Assert.That(gpos, Is.Not.Null);
                Assert.That(rsl!.BrowseName.NamespaceIndex,
                    Is.EqualTo(manager.Server.NamespaceUris.GetIndex(
                        Opc.Ua.Rsl.Namespaces.RSL)));
                Assert.That(gpos!.BrowseName.NamespaceIndex,
                    Is.EqualTo(manager.Server.NamespaceUris.GetIndex(
                        Opc.Ua.Gpos.Namespaces.GPOS)));
                Assert.That(rsl.NamespaceVersion!.Value, Is.EqualTo("1.00.1"));
                Assert.That(gpos.NamespaceVersion!.Value, Is.EqualTo("1.0.0"));
            });
        }
    }
}
