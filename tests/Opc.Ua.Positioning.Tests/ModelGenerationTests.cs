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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Gpos;
using Opc.Ua.Rsl;

namespace Opc.Ua.Positioning.Tests
{
    [TestFixture]
    [Category("Positioning")]
    public sealed class ModelGenerationTests
    {
        [Test]
        public void GeneratedNamespacesAndIdentifiersMatchSpecifications()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    Rsl.Namespaces.RSL,
                    Is.EqualTo("http://opcfoundation.org/UA/RSL/"));
                Assert.That(
                    Gpos.Namespaces.GPOS,
                    Is.EqualTo("http://opcfoundation.org/UA/GPOS/"));
                Assert.That(Rsl.ObjectTypes.SpatialObjectType, Is.EqualTo(1002));
                Assert.That(Rsl.Objects.RelativeSpatialLocations, Is.EqualTo(5001));
                Assert.That(Gpos.ObjectTypes.ZoneType, Is.EqualTo(1005));
                Assert.That(Gpos.Objects.GlobalLocations, Is.EqualTo(5013));
            });
        }

        [Test]
        public void GeneratedLoadersPopulateBothModels()
        {
            var context = new SystemContext(telemetry: null!)
            {
                NamespaceUris = new NamespaceTable()
            };
            context.NamespaceUris.GetIndexOrAppend(Rsl.Namespaces.RSL);
            context.NamespaceUris.GetIndexOrAppend(Gpos.Namespaces.GPOS);
            var nodes = new NodeStateCollection();

            nodes.AddOpcUaRsl(context);
            int rslCount = CountNodes(context, nodes);
            nodes.AddOpcUaGpos(context);
            int totalCount = CountNodes(context, nodes);

            Assert.Multiple(() =>
            {
                Assert.That(rslCount, Is.EqualTo(46));
                Assert.That(totalCount, Is.EqualTo(94));
                Assert.That(
                    nodes.Any(node => node.NodeId == NodeId.Create(
                        Rsl.Objects.RelativeSpatialLocations,
                        Rsl.Namespaces.RSL,
                        context.NamespaceUris)),
                    Is.True);
                Assert.That(
                    nodes.Any(node => node.NodeId == NodeId.Create(
                        Gpos.Objects.GlobalLocations,
                        Gpos.Namespaces.GPOS,
                        context.NamespaceUris)),
                    Is.True);
            });
        }

        private static int CountNodes(
            ISystemContext context,
            NodeStateCollection roots)
        {
            var visited = new HashSet<NodeId>();
            foreach (NodeState root in roots)
            {
                CountNode(context, root, visited);
            }
            return visited.Count;
        }

        private static void CountNode(
            ISystemContext context,
            NodeState node,
            HashSet<NodeId> visited)
        {
            if (!visited.Add(node.NodeId))
            {
                return;
            }

            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                CountNode(context, child, visited);
            }
        }

        [Test]
        public void GeneratedFactoriesMaterializeMandatoryChildren()
        {
            var context = new SystemContext(telemetry: null!)
            {
                NamespaceUris = new NamespaceTable()
            };
            context.NamespaceUris.GetIndexOrAppend(Rsl.Namespaces.RSL);
            context.NamespaceUris.GetIndexOrAppend(Gpos.Namespaces.GPOS);

            CartesianFrameAngleOrientationState frame =
                context.CreateInstanceOfCartesianFrameAngleOrientationType();
            GlobalLocationState location =
                context.CreateInstanceOfGlobalLocationType();
            ZoneState zone = context.CreateInstanceOfZoneType();

            Assert.Multiple(() =>
            {
                Assert.That(frame.Base, Is.Not.Null);
                Assert.That(frame.Position, Is.Not.Null);
                Assert.That(frame.Orientation, Is.Not.Null);
                Assert.That(location.Base, Is.Not.Null);
                Assert.That(location.Position, Is.Not.Null);
                Assert.That(location.Position!.Longitude, Is.Not.Null);
                Assert.That(location.Position.Latitude, Is.Not.Null);
                Assert.That(location.Position.SourceId, Is.Not.Null);
                Assert.That(zone.ZoneId, Is.Not.Null);
            });
        }

        [Test]
        public void GposOptionalFieldsUseEncodingMasks()
        {
            var position = new GlobalPositionDataType
            {
                EncodingMask =
                    (uint)S3DGeographicCoordinateDataTypeFields.Elevation |
                    (uint)GlobalPositionDataTypeFields.Accuracy |
                    (uint)GlobalPositionDataTypeFields.Floor,
                Longitude = 8.0,
                Latitude = 47.0,
                Elevation = 500.0,
                Accuracy = 0.1,
                Floor = 2.0f
            };
            var location = new GlobalLocationDataType
            {
                EncodingMask =
                    (uint)GlobalLocationDataTypeFields.Orientation,
                Position = position,
                Orientation = new ThreeDOrientation { C = 90.0 }
            };

            Assert.Multiple(() =>
            {
                Assert.That(position.Elevation, Is.EqualTo(500.0));
                Assert.That(position.Accuracy, Is.EqualTo(0.1));
                Assert.That(position.Floor, Is.EqualTo(2.0f));
                Assert.That(location.Position, Is.SameAs(position));
                Assert.That(location.Orientation.C, Is.EqualTo(90.0));
            });
        }

        [Test]
        public void DerivedGlobalPositionSharesEncodingMaskWithGeographicBase()
        {
            var position = new GlobalPositionDataType
            {
                EncodingMask =
                    (uint)GlobalPositionDataTypeFields.Elevation |
                    (uint)GlobalPositionDataTypeFields.Accuracy,
                Longitude = 8.0,
                Latitude = 47.0,
                Elevation = 500.0,
                Accuracy = 0.1
            };
            S3DGeographicCoordinateDataType geographic = position;

            Assert.Multiple(() =>
            {
                Assert.That(
                    geographic.EncodingMask,
                    Is.EqualTo(position.EncodingMask));
                Assert.That(
                    geographic.EncodingMask &
                        (uint)S3DGeographicCoordinateDataTypeFields.Elevation,
                    Is.Not.Zero);
            });
        }
    }
}
