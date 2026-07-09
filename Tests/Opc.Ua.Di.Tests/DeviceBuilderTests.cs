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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// End-to-end tests for the device builder
    /// (<see cref="IDeviceBuilder{TDevice}"/> on
    /// <see cref="DiNodeManager"/>).
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("DeviceBuilder")]
    public sealed class DeviceBuilderTests
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
        public async Task CreateDeviceAsyncAttachesDeviceUnderDeviceSet()
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "Device1",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            Assert.That(builder.Device, Is.Not.Null);
            Assert.That(builder.Device.BrowseName.Name, Is.EqualTo("Device1"));
            Assert.That(builder.Device.Parent, Is.Not.Null);
            Assert.That(builder.Device.Parent!.BrowseName.Name,
                Is.EqualTo("DeviceSet"));
        }

        [Test]
        public async Task CreateDeviceAsyncAssignsManagerNodeId()
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "Device2",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            Assert.That(builder.Device.NodeId.IsNull, Is.False);
            Assert.That(builder.Device.NodeId.NamespaceIndex,
                Is.EqualTo(m_fixture.Manager.DiNamespaceIndex));
        }

        [Test]
        public async Task CreateDeviceAsyncRegistersInPredefinedNodes()
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "Device3",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            NodeState? resolved = m_fixture.Manager
                .FindPredefinedNode(builder.Device.NodeId);

            Assert.That(resolved, Is.SameAs(builder.Device));
        }

        [Test]
        public async Task CreateDeviceAsyncFailsOnDuplicateBrowseName()
        {
            ushort ns = m_fixture.Manager.DiNamespaceIndex;
            await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName("DeviceDup", ns))
                .ConfigureAwait(false);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_fixture.Manager
                        .CreateDeviceAsync(new QualifiedName("DeviceDup", ns))
                        .ConfigureAwait(false))!;

            Assert.That(ex.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public async Task WithIdentificationWritesNameplate()
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceIdent",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            builder.WithIdentification(id =>
            {
                id.Manufacturer = new LocalizedText("Acme Corp");
                id.SerialNumber = "SN-42";
                id.DeviceClass = "Pump";
                id.RevisionCounter = 7;
            });

            Assert.That(builder.Device.Manufacturer, Is.Not.Null);
            Assert.That(builder.Device.Manufacturer!.Value.Text, Is.EqualTo("Acme Corp"));
            Assert.That(builder.Device.SerialNumber, Is.Not.Null);
            Assert.That(builder.Device.SerialNumber!.Value, Is.EqualTo("SN-42"));
            Assert.That(builder.Device.DeviceClass, Is.Not.Null);
            Assert.That(builder.Device.DeviceClass!.Value, Is.EqualTo("Pump"));
            Assert.That(builder.Device.RevisionCounter, Is.Not.Null);
            Assert.That(builder.Device.RevisionCounter!.Value, Is.EqualTo(7));
        }

        [Test]
        public async Task WithIdentificationSkipsUnsetFields()
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DevicePartial",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            builder.WithIdentification(id => id.Manufacturer = new LocalizedText("Only Manufacturer Set"));

            Assert.That(builder.Device.Manufacturer, Is.Not.Null);
            Assert.That(builder.Device.Manufacturer!.Value.Text,
                Is.EqualTo("Only Manufacturer Set"));
            Assert.That(builder.Device.Model, Is.Null,
                "Model should remain unset when WithIdentification didn't assign it.");
            Assert.That(builder.Device.SerialNumber, Is.Null,
                "SerialNumber should remain unset when WithIdentification didn't assign it.");
        }

        [Test]
        public async Task WithConfigurationGroupCreatesFunctionalGroup()
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceFg",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            FunctionalGroupState? created = null;
            builder.WithConfigurationGroup(fg => created = fg.Group);

            Assert.That(created, Is.Not.Null);
            Assert.That(created!.BrowseName.Name, Is.EqualTo("Configuration"));
            Assert.That(created.Parent, Is.SameAs(builder.Device));
        }

        [Test]
        public async Task WithFunctionalGroupIsIdempotent()
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceFgTwice",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            FunctionalGroupState? first = null;
            FunctionalGroupState? second = null;
            builder.WithMaintenanceGroup(fg => first = fg.Group)
                .WithMaintenanceGroup(fg => second = fg.Group);

            Assert.That(second, Is.SameAs(first),
                "Calling WithMaintenanceGroup twice should reuse the existing group.");
        }

        [Test]
        public async Task DeviceWrapsExistingNode()
        {
            IDeviceBuilder<DeviceState> created = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceWrap",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            IDeviceBuilder<DeviceState> wrapped =
                m_fixture.Manager.Device<DeviceState>(created.Device);

            Assert.That(wrapped.Device, Is.SameAs(created.Device));
        }

        [Test]
        public async Task DeviceByNodeIdReturnsExistingBuilder()
        {
            IDeviceBuilder<DeviceState> created = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceById",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            IDeviceBuilder<DeviceState> wrapped =
                m_fixture.Manager.Device<DeviceState>(created.Device.NodeId);

            Assert.That(wrapped.Device, Is.SameAs(created.Device));
        }

        [Test]
        public async Task DeviceByBrowseNameReturnsExistingBuilder()
        {
            ushort ns = m_fixture.Manager.DiNamespaceIndex;
            IDeviceBuilder<DeviceState> created = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName("DeviceByName", ns))
                .ConfigureAwait(false);

            IDeviceBuilder<DeviceState> resolved = m_fixture.Manager
                .DeviceByBrowseName<DeviceState>(new QualifiedName("DeviceByName", ns));

            Assert.That(resolved.Device, Is.SameAs(created.Device));
        }

        [Test]
        public async Task ConnectsToAddsForwardReference()
        {
            ushort ns = m_fixture.Manager.DiNamespaceIndex;
            IDeviceBuilder<DeviceState> a = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName("DeviceA", ns))
                .ConfigureAwait(false);
            IDeviceBuilder<DeviceState> b = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName("DeviceB", ns))
                .ConfigureAwait(false);

            a.ConnectsTo(b.Device.NodeId);

            NodeId connectsToRefType = NodeId.Create(
                global::Opc.Ua.Di.ReferenceTypes.ConnectsTo,
                DiNodeManager.DiNamespaceUri,
                m_fixture.Server.CurrentInstance.NamespaceUris);

            Assert.That(
                a.Device.ReferenceExists(connectsToRefType, false, b.Device.NodeId),
                Is.True,
                "DeviceA should have a forward ConnectsTo reference to DeviceB.");
        }

        [Test]
        public async Task WithDeviceHealthWritesEnum()
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceHealthy",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            // The factory `p => new DeviceState(p)` doesn't instantiate
            // the DeviceHealth child variable, so this call should throw
            // BadInvalidState until the device is materialised with a
            // typed factory. Verifies the extension method routes the
            // failure mode correctly.
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => builder.WithDeviceHealth(DeviceHealthEnumeration.NORMAL))!;
            Assert.That(ex.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task ConfigureEscapeHatchProvidesRawAccess()
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceConfigure",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            DeviceState? observed = null;
            ISystemContext? observedContext = null;
            builder.Configure((device, ctx) =>
            {
                observed = device;
                observedContext = ctx;
            });

            Assert.That(observed, Is.SameAs(builder.Device));
            Assert.That(observedContext, Is.SameAs(m_fixture.Manager.SystemContext));
        }
    }
}
