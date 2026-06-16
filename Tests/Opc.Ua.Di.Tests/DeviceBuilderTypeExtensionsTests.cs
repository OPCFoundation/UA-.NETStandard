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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di.Server.Builders;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Tests for the §5.4 device sub-type materialisation extensions
    /// (Software / Block / ConfigurableObject) on
    /// <see cref="IDeviceBuilder{TDevice}"/>.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("DeviceBuilder")]
    public sealed class DeviceBuilderTypeExtensionsTests
    {
        private DiServerFixture m_fixture = null!;

        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new DiServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task AddSoftwareCreatesChildUnderDevice()
        {
            IDeviceBuilder<DeviceState> device = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceForSoftware",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            SoftwareState software = device.AddSoftware(
                new QualifiedName("Firmware", m_fixture.Manager.DiNamespaceIndex));

            Assert.That(software, Is.Not.Null);
            Assert.That(software.BrowseName.Name, Is.EqualTo("Firmware"));
            Assert.That(software.Parent, Is.SameAs(device.Device));
            Assert.That(software.NodeId.IsNull, Is.False);
        }

        [Test]
        public async Task AddBlockCreatesChildUnderDevice()
        {
            IDeviceBuilder<DeviceState> device = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceForBlock",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            BlockState block = device.AddBlock(
                new QualifiedName("Block1", m_fixture.Manager.DiNamespaceIndex));

            Assert.That(block, Is.Not.Null);
            Assert.That(block.BrowseName.Name, Is.EqualTo("Block1"));
            Assert.That(block.Parent, Is.SameAs(device.Device));
        }

        [Test]
        public async Task AddConfigurableObjectCreatesChildUnderDevice()
        {
            IDeviceBuilder<DeviceState> device = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceForConfigurable",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            ConfigurableObjectState configurable = device.AddConfigurableObject(
                new QualifiedName("ConfigObj", m_fixture.Manager.DiNamespaceIndex));

            Assert.That(configurable, Is.Not.Null);
            Assert.That(configurable.BrowseName.Name, Is.EqualTo("ConfigObj"));
            Assert.That(configurable.Parent, Is.SameAs(device.Device));
        }

        [Test]
        public async Task AddSoftwareInvokesConfigureCallback()
        {
            IDeviceBuilder<DeviceState> device = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceForCallback",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            bool invoked = false;
            _ = device.AddSoftware(
                new QualifiedName("Sw2", m_fixture.Manager.DiNamespaceIndex),
                _ => invoked = true);

            Assert.That(invoked, Is.True);
        }

        [Test]
        public async Task AddSoftwareRegistersChildAsPredefinedNode()
        {
            IDeviceBuilder<DeviceState> device = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceForRegistration",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            SoftwareState software = device.AddSoftware(
                new QualifiedName("RegisteredFw", m_fixture.Manager.DiNamespaceIndex));

            // The manager should be able to find the child by NodeId.
            NodeState? found = m_fixture.Manager.FindPredefinedNode(software.NodeId);
            Assert.That(found, Is.SameAs(software));
        }

        [Test]
        public void AddSoftwareWithNullDeviceThrows()
        {
            Assert.That(() =>
                DeviceBuilderTypeExtensions.AddSoftware<DeviceState>(
                    null!, new QualifiedName("X")),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task AddSoftwareWithNullBrowseNameThrows()
        {
            IDeviceBuilder<DeviceState> device = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceForArgCheck1",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            Assert.That(() => device.AddSoftware(default),
                Throws.ArgumentException);
        }
    }
}
