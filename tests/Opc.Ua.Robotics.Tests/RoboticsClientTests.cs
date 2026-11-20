/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Robotics;
using Opc.Ua.Robotics.Client;

namespace Opc.Ua.Robotics.Client.Tests
{
    /// <summary>
    /// Unit tests for <see cref="RoboticsModel"/> and <see cref="RoboticsClient"/> type
    /// resolution, including the namespace-absent safety behaviour.
    /// </summary>
    [TestFixture]
    [Category("Robotics")]
    public sealed class RoboticsClientTests
    {
        private static NamespaceTable TableWithRobotics(out ushort roboticsIndex)
        {
            var table = new NamespaceTable();
            roboticsIndex = (ushort)table.GetIndexOrAppend(global::Opc.Ua.Robotics.Namespaces.Robotics);
            return table;
        }

        [Test]
        public void TypeNodeIdResolvesInRoboticsNamespace()
        {
            NamespaceTable table = TableWithRobotics(out ushort index);

            NodeId node = RoboticsModel.TypeNodeId(RoboticsModel.MotionDeviceSystemType, table);

            Assert.That(node, Is.EqualTo(new NodeId(RoboticsModel.MotionDeviceSystemType, index)));
        }

        [Test]
        public void TypeNodeIdNullTableThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => RoboticsModel.TypeNodeId(RoboticsModel.MotionDeviceSystemType, null!));
        }

        [Test]
        public void TypeNodeIdThrowsWhenNamespaceAbsent()
        {
            // Documents why the Try* / discovery helpers must not funnel through TypeNodeId:
            // NodeId.Create throws BadNodeIdInvalid when the namespace is not in the table.
            Assert.Throws<ServiceResultException>(
                () => RoboticsModel.TypeNodeId(RoboticsModel.MotionDeviceSystemType, new NamespaceTable()));
        }

        [TestCase(RoboticsModel.MotionDeviceSystemType, "MotionDeviceSystem")]
        [TestCase(RoboticsModel.MotionDeviceType, "MotionDevice")]
        [TestCase(RoboticsModel.AxisType, "Axis")]
        [TestCase(RoboticsModel.ControllerType, "Controller")]
        public void TryGetRoboticsTypeNameReturnsFriendlyName(uint typeId, string expected)
        {
            NamespaceTable table = TableWithRobotics(out ushort index);
            var typeDefinition = new NodeId(typeId, index);

            bool matched = RoboticsClient.TryGetRoboticsTypeName(typeDefinition, table, out string? name);

            Assert.That(matched, Is.True);
            Assert.That(name, Is.EqualTo(expected));
        }

        [Test]
        public void TryGetRoboticsTypeNameUnknownTypeReturnsFalse()
        {
            NamespaceTable table = TableWithRobotics(out ushort index);
            var typeDefinition = new NodeId(9999u, index);

            bool matched = RoboticsClient.TryGetRoboticsTypeName(typeDefinition, table, out string? name);

            Assert.That(matched, Is.False);
            Assert.That(name, Is.Null);
        }

        [Test]
        public void TryGetRoboticsTypeNameReturnsFalseWhenNamespaceAbsent()
        {
            // The Robotics namespace is not in the table: the method must return false
            // rather than throw (regression guard for the namespace-absent fix).
            var table = new NamespaceTable();
            var typeDefinition = new NodeId(RoboticsModel.MotionDeviceSystemType, 3);

            bool matched = RoboticsClient.TryGetRoboticsTypeName(typeDefinition, table, out string? name);

            Assert.That(matched, Is.False);
            Assert.That(name, Is.Null);
        }

        [Test]
        public void TryGetRoboticsTypeNameNullTableThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => RoboticsClient.TryGetRoboticsTypeName(NodeId.Null, null!, out _));
        }

        [Test]
        public async Task DiscoverMotionDeviceSystemsNullSessionThrows()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => RoboticsClient.DiscoverMotionDeviceSystemsAsync(null!, NodeId.Null, CancellationToken.None));
        }

        [Test]
        public async System.Threading.Tasks.Task DiscoverMotionDeviceSystemsReturnsEmptyWhenNamespaceAbsent()
        {
            var session = new Moq.Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());

            ArrayOf<NodeId> result = await RoboticsClient.DiscoverMotionDeviceSystemsAsync(
                session.Object, NodeId.Null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Count, Is.Zero);
        }
    }
}
