// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.Topology;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Tests for <see cref="DiDeviceTopology"/> — verifies well-known
    /// node resolution, device enumeration, and browse-name lookup
    /// against a running <see cref="DiNodeManager"/>.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Topology")]
    public sealed class DiDeviceTopologyTests
    {
        private DiServerFixture m_fixture = null!;
        private DiDeviceTopology m_topology = null!;

        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new DiServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_topology = new DiDeviceTopology(m_fixture.Manager);
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public void ConstructorThrowsOnNullManager()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => new DiDeviceTopology(manager: null!));
        }

        [Test]
        public void DeviceSetResolvesToWellKnownNode()
        {
            BaseObjectState? deviceSet = m_topology.DeviceSet;

            Assert.That(deviceSet, Is.Not.Null);
            Assert.That(deviceSet!.BrowseName.Name, Is.EqualTo("DeviceSet"));
        }

        [Test]
        public void NetworkSetResolvesToWellKnownNode()
        {
            BaseObjectState? networkSet = m_topology.NetworkSet;

            Assert.That(networkSet, Is.Not.Null);
            Assert.That(networkSet!.BrowseName.Name, Is.EqualTo("NetworkSet"));
        }

        [Test]
        public void DeviceTopologyResolvesToWellKnownNode()
        {
            BaseObjectState? topologyNode = m_topology.DeviceTopology;

            Assert.That(topologyNode, Is.Not.Null);
            Assert.That(topologyNode!.BrowseName.Name, Is.EqualTo("DeviceTopology"));
        }

        [Test]
        public void FindDeviceReturnsNullForNullBrowseName()
        {
            ComponentState? found = m_topology.FindDevice(QualifiedName.Null);

            Assert.That(found, Is.Null);
        }

        [Test]
        public void FindDeviceReturnsNullForUnknownBrowseName()
        {
            ComponentState? found = m_topology.FindDevice(
                new QualifiedName("NoSuchDevice", m_fixture.Manager.DiNamespaceIndex));

            Assert.That(found, Is.Null);
        }

        [Test]
        public async Task FindDeviceReturnsRegisteredDeviceAsync()
        {
            ushort ns = m_fixture.Manager.DiNamespaceIndex;
            var browseName = new QualifiedName("TopologyDeviceA", ns);

            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(browseName)
                .ConfigureAwait(false);

            ComponentState? found = m_topology.FindDevice(browseName);

            Assert.That(found, Is.Not.Null);
            Assert.That(found, Is.SameAs(builder.Device));
        }

        [Test]
        public async Task DevicesEnumeratesCreatedDevicesAsync()
        {
            ushort ns = m_fixture.Manager.DiNamespaceIndex;
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName("TopologyDeviceB", ns))
                .ConfigureAwait(false);

            ComponentState[] devices = [.. m_topology.Devices];

            Assert.That(devices, Does.Contain(builder.Device));
        }
    }
}
