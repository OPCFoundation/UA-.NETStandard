// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Di.Client;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Argument-validation tests for the typed client helpers
    /// (<see cref="DiLockClient"/>, <see cref="DiTopologyClient"/>).
    /// End-to-end behaviour against a live server is covered by
    /// integration tests; this fixture verifies the construction
    /// contract.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("ClientHelpers")]
    public sealed class ClientHelpersTests
    {
        [Test]
        public void DiLockClientThrowsOnNullSession()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new DiLockClient(null!, new NodeId("lock-1", 2), NullTelemetry()))!;
            Assert.That(ex.ParamName, Is.EqualTo("session"));
        }

        [Test]
        public void DiLockClientThrowsOnNullLockNodeId()
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => new DiLockClient(FakeSession(), NodeId.Null, NullTelemetry()))!;
            Assert.That(ex.ParamName, Is.EqualTo("lockNodeId"));
        }

        [Test]
        public void DiLockClientExposesConstructorArguments()
        {
            ISession session = FakeSession();
            var nodeId = new NodeId("lock-1", 2);
            var client = new DiLockClient(session, nodeId, NullTelemetry());

            Assert.That(client.Session, Is.SameAs(session));
            Assert.That(client.LockNodeId, Is.EqualTo(nodeId));
        }

        [Test]
        public void DiTopologyClientThrowsOnNullSession()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new DiTopologyClient(null!, NullTelemetry()))!;
            Assert.That(ex.ParamName, Is.EqualTo("session"));
        }

        [Test]
        public void DiTopologyClientExposesWellKnownNodeIds()
        {
            ISession session = FakeSession();
            var client = new DiTopologyClient(session, NullTelemetry());

            Assert.That(client.DeviceSetId.IsNull, Is.False);
            Assert.That(client.NetworkSetId.IsNull, Is.False);
            Assert.That(client.DeviceTopologyId.IsNull, Is.False);
            // All three should be in the DI namespace.
            ushort diNs = session.NamespaceUris
                .GetIndexOrAppend(Opc.Ua.Di.Namespaces.OpcUaDi);
            Assert.That(client.DeviceSetId.NamespaceIndex, Is.EqualTo(diNs));
            Assert.That(client.NetworkSetId.NamespaceIndex, Is.EqualTo(diNs));
            Assert.That(client.DeviceTopologyId.NamespaceIndex, Is.EqualTo(diNs));
        }

        [Test]
        public void DiTopologyEntryRecordEquality()
        {
            // Verify the public record has structural equality so callers can
            // diff topology snapshots.
            var a = new TopologyEntry(
                new NodeId("device-1", 2),
                "Device 1",
                new NodeId(1, 0),
                new QualifiedName("Device #1", 2));
            var b = new TopologyEntry(
                new NodeId("device-1", 2),
                "Device 1",
                new NodeId(1, 0),
                new QualifiedName("Device #1", 2));

            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void SoftwareUpdateClientThrowsOnNullSession()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new SoftwareUpdateClient(null!, new NodeId("update-1", 2), NullTelemetry()))!;
            Assert.That(ex.ParamName, Is.EqualTo("session"));
        }

        [Test]
        public void SoftwareUpdateClientThrowsOnNullNodeId()
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => new SoftwareUpdateClient(FakeSession(), NodeId.Null, NullTelemetry()))!;
            Assert.That(ex.ParamName, Is.EqualTo("softwareUpdateNodeId"));
        }

        [Test]
        public void SoftwareUpdateClientExposesConstructorArguments()
        {
            ISession session = FakeSession();
            var nodeId = new NodeId("update-1", 2);
            var client = new SoftwareUpdateClient(session, nodeId, NullTelemetry());
            Assert.That(client.Session, Is.SameAs(session));
            Assert.That(client.SoftwareUpdateNodeId, Is.EqualTo(nodeId));
        }

        private static ISession FakeSession()
        {
            // Use the SessionStub from the existing DI client tests if
            // one exists; otherwise Moq the ISession. Since this test
            // never invokes Browse/Read/Call, an empty Moq is enough.
            var mock = new Moq.Mock<ISession>();
            var nsTable = new NamespaceTable();
            nsTable.GetIndexOrAppend(Opc.Ua.Di.Namespaces.OpcUaDi);
            mock.SetupGet(s => s.NamespaceUris).Returns(nsTable);
            return mock.Object;
        }

        private static ITelemetryContext NullTelemetry()
        {
            // NullTelemetryContext is the standard test double in the
            // stack. Fall back to Mock if missing.
            return new Moq.Mock<ITelemetryContext>().Object;
        }
    }
}
