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
    /// Tests for the §5.15 ISupportInfoType extension on
    /// <see cref="IDeviceBuilder{TDevice}"/>.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("DeviceBuilder")]
    public sealed class DeviceBuilderSupportInfoExtensionsTests
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
        public async Task WithSupportInfoCreatesISupportInfoChild()
        {
            IDeviceBuilder<DeviceState> device = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "SupportInfoDevice1",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            ISupportInfoState? captured = null;
            device.WithSupportInfo(info => captured = info);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.BrowseName.Name, Is.EqualTo("SupportInfo"));
            Assert.That(captured.Parent, Is.SameAs(device.Device));
        }

        [Test]
        public async Task WithSupportInfoIsIdempotent()
        {
            IDeviceBuilder<DeviceState> device = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "SupportInfoDevice2",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            ISupportInfoState? first = null;
            device.WithSupportInfo(info => first = info);

            ISupportInfoState? second = null;
            device.WithSupportInfo(info => second = info);

            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public async Task WithSupportInfoRegistersChildWithManager()
        {
            IDeviceBuilder<DeviceState> device = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "SupportInfoDevice3",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            ISupportInfoState? captured = null;
            device.WithSupportInfo(info => captured = info);

            NodeState? found = m_fixture.Manager.FindPredefinedNode(captured!.NodeId);
            Assert.That(found, Is.SameAs(captured));
        }

        [Test]
        public async Task WithSupportInfoAcceptsCustomConfiguration()
        {
            IDeviceBuilder<DeviceState> device = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "SupportInfoDevice4",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            bool invoked = false;
            device.WithSupportInfo(info =>
            {
                invoked = true;
                info.Description = new LocalizedText("Custom support info");
            });

            Assert.That(invoked, Is.True);
        }

        [Test]
        public void WithSupportInfoWithNullDeviceThrows()
        {
            Assert.That(() =>
                DeviceBuilderSupportInfoExtensions.WithSupportInfo<DeviceState>(
                    null!, _ => { }),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task WithSupportInfoWithNullConfigureThrows()
        {
            IDeviceBuilder<DeviceState> device = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "SupportInfoDeviceArg",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            Assert.That(() => device.WithSupportInfo(null!),
                Throws.ArgumentNullException);
        }
    }
}
