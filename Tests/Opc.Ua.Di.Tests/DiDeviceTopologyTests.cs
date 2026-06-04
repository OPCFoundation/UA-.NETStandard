/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di;
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

            ComponentState[] devices = m_topology.Devices.ToArray();

            Assert.That(devices, Does.Contain(builder.Device));
        }
    }
}
