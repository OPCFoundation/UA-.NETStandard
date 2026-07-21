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
            roboticsIndex = (ushort)table.GetIndexOrAppend(RoboticsNamespaces.Robotics);
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
        public void DiscoverMotionDeviceSystemsNullSessionThrows()
        {
            Assert.ThrowsAsync<ArgumentNullException>(
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
