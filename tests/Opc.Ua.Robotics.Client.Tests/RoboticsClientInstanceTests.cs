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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Robotics;
using Opc.Ua.Robotics.Client;

namespace Opc.Ua.Robotics.Client.Tests
{
    /// <summary>
    /// Unit tests for the instance surface of <see cref="RoboticsClient"/>: the
    /// Device Integration composition, subtype-aware discovery, and subtype-aware
    /// classification.
    /// </summary>
    [TestFixture]
    [Category("Robotics")]
    public sealed class RoboticsClientInstanceTests
    {
        private const uint VendorMotionDeviceType = 40001u;

        private static NamespaceTable TableWithRobotics(out ushort roboticsIndex)
        {
            var table = new NamespaceTable();
            roboticsIndex = (ushort)table.GetIndexOrAppend(global::Opc.Ua.Robotics.Namespaces.Robotics);
            return table;
        }

        private static Mock<ISession> SessionWithRobotics(out ushort roboticsIndex)
        {
            NamespaceTable table = TableWithRobotics(out roboticsIndex);
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(table);
            return session;
        }

        [Test]
        public void ConstructorNullSessionThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => new RoboticsClient(null!, new Mock<ITelemetryContext>().Object));
        }

        [Test]
        public void ConstructorNullTelemetryThrows()
        {
            Mock<ISession> session = SessionWithRobotics(out _);

            Assert.Throws<ArgumentNullException>(
                () => new RoboticsClient(session.Object, null!));
        }

        [Test]
        public void ConstructorExposesSessionTelemetryAndDiTopology()
        {
            Mock<ISession> session = SessionWithRobotics(out _);
            ITelemetryContext telemetry = new Mock<ITelemetryContext>().Object;

            var client = new RoboticsClient(session.Object, telemetry);

            Assert.That(client.Session, Is.SameAs(session.Object));
            Assert.That(client.Telemetry, Is.SameAs(telemetry));
            Assert.That(client.Topology, Is.Not.Null);
            Assert.That(client.Topology.Session, Is.SameAs(session.Object));
        }

        [Test]
        public async Task DiscoverMotionDevicesReturnsEmptyWhenNamespaceAbsent()
        {
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            var client = new RoboticsClient(
                session.Object, new Mock<ITelemetryContext>().Object);

            ArrayOf<NodeId> result = await client
                .DiscoverMotionDevicesAsync(NodeId.Null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.Count, Is.Zero);
        }

        [Test]
        public async Task DiscoverControllersReturnsEmptyWhenNamespaceAbsent()
        {
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            var client = new RoboticsClient(
                session.Object, new Mock<ITelemetryContext>().Object);

            ArrayOf<NodeId> result = await client
                .DiscoverControllersAsync(NodeId.Null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.Count, Is.Zero);
        }

        [Test]
        public async Task DiscoverAxesReturnsEmptyWhenNamespaceAbsent()
        {
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            var client = new RoboticsClient(
                session.Object, new Mock<ITelemetryContext>().Object);

            ArrayOf<NodeId> result = await client
                .DiscoverAxesAsync(NodeId.Null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.Count, Is.Zero);
        }

        [Test]
        public async Task GetRoboticsTypeNameReturnsNullForNullTypeDefinition()
        {
            Mock<ISession> session = SessionWithRobotics(out _);
            var client = new RoboticsClient(
                session.Object, new Mock<ITelemetryContext>().Object);

            string? name = await client
                .GetRoboticsTypeNameAsync(NodeId.Null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(name, Is.Null);
        }

        [Test]
        public async Task GetRoboticsTypeNameMatchesStandardTypeWithoutNodeCache()
        {
            Mock<ISession> session = SessionWithRobotics(out ushort robotics);
            var client = new RoboticsClient(
                session.Object, new Mock<ITelemetryContext>().Object);

            string? name = await client
                .GetRoboticsTypeNameAsync(
                    new NodeId(RoboticsModel.AxisType, robotics), CancellationToken.None)
                .ConfigureAwait(false);

            // The exact match short-circuits before the node cache is consulted.
            Assert.That(name, Is.EqualTo("Axis"));
        }

        [Test]
        public async Task GetRoboticsTypeNameResolvesVendorSubtypeToClosestStandardType()
        {
            Mock<ISession> session = SessionWithRobotics(out ushort robotics);
            var vendorType = new NodeId(VendorMotionDeviceType, robotics);
            var nodeCache = new Mock<INodeCache>();
            nodeCache
                .Setup(c => c.IsTypeOfAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId sub, NodeId super, CancellationToken _) =>
                    new ValueTask<bool>(
                        sub == vendorType &&
                        super == new NodeId(RoboticsModel.MotionDeviceType, robotics)));
            session.SetupGet(s => s.NodeCache).Returns(nodeCache.Object);
            var client = new RoboticsClient(
                session.Object, new Mock<ITelemetryContext>().Object);

            string? name = await client
                .GetRoboticsTypeNameAsync(vendorType, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(name, Is.EqualTo("MotionDevice"));
        }

        [Test]
        public async Task GetRoboticsTypeNameReturnsNullForUnrelatedType()
        {
            Mock<ISession> session = SessionWithRobotics(out ushort robotics);
            var nodeCache = new Mock<INodeCache>();
            nodeCache
                .Setup(c => c.IsTypeOfAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(false));
            session.SetupGet(s => s.NodeCache).Returns(nodeCache.Object);
            var client = new RoboticsClient(
                session.Object, new Mock<ITelemetryContext>().Object);

            string? name = await client
                .GetRoboticsTypeNameAsync(
                    new NodeId(9999u, robotics), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(name, Is.Null);
        }
    }
}
