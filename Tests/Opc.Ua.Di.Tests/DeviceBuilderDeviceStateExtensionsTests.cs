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

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di.Server.Builders;

#pragma warning disable CA2000

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Tests for <see cref="DeviceBuilderDeviceStateExtensions"/> —
    /// the typed <c>WithDeviceHealth</c> fluent extension.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("DeviceBuilder")]
    public sealed class DeviceBuilderDeviceStateExtensionsTests
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

        private async Task<IDeviceBuilder<DeviceState>> CreateDeviceWithHealthAsync(
            string browseName)
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    browseName, m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            // The default factory `p => new DeviceState(p)` does NOT
            // instantiate the optional DeviceHealth child variable.
            // Use the generated AddDeviceHealth() to attach a properly
            // configured BaseDataVariableState<DeviceHealthEnumeration>
            // so the success path on WithDeviceHealth can be exercised.
            builder.Device.AddDeviceHealth(builder.Context);
            return builder;
        }

        [Test]
        public void WithDeviceHealthOnNullBuilderThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => DeviceBuilderDeviceStateExtensions.WithDeviceHealth<DeviceState>(
                    null!, DeviceHealthEnumeration.NORMAL));
        }

        [Test]
        public async Task WithDeviceHealthMissingChildThrowsBadInvalidState()
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceNoHealthVar", m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => builder.WithDeviceHealth(DeviceHealthEnumeration.NORMAL))!;
            Assert.That(ex.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task WithDeviceHealthSetsNormalValue()
        {
            IDeviceBuilder<DeviceState> builder =
                await CreateDeviceWithHealthAsync("DeviceHealthNormal").ConfigureAwait(false);

            builder.WithDeviceHealth(DeviceHealthEnumeration.NORMAL);

            Assert.That(builder.Device.DeviceHealth!.Value,
                Is.EqualTo(DeviceHealthEnumeration.NORMAL));
        }

        [Test]
        public async Task WithDeviceHealthSetsFailureValue()
        {
            IDeviceBuilder<DeviceState> builder =
                await CreateDeviceWithHealthAsync("DeviceHealthFailure").ConfigureAwait(false);

            builder.WithDeviceHealth(DeviceHealthEnumeration.FAILURE);

            Assert.That(builder.Device.DeviceHealth!.Value,
                Is.EqualTo(DeviceHealthEnumeration.FAILURE));
        }

        [Test]
        public async Task WithDeviceHealthSetsCheckFunctionValue()
        {
            IDeviceBuilder<DeviceState> builder =
                await CreateDeviceWithHealthAsync("DeviceHealthCheck").ConfigureAwait(false);

            builder.WithDeviceHealth(DeviceHealthEnumeration.CHECK_FUNCTION);

            Assert.That(builder.Device.DeviceHealth!.Value,
                Is.EqualTo(DeviceHealthEnumeration.CHECK_FUNCTION));
        }

        [Test]
        public async Task WithDeviceHealthReturnsSameBuilderForChaining()
        {
            IDeviceBuilder<DeviceState> builder =
                await CreateDeviceWithHealthAsync("DeviceHealthChain").ConfigureAwait(false);

            IDeviceBuilder<DeviceState> returned =
                builder.WithDeviceHealth(DeviceHealthEnumeration.MAINTENANCE_REQUIRED);

            Assert.That(returned, Is.SameAs(builder));
            Assert.That(builder.Device.DeviceHealth!.Value,
                Is.EqualTo(DeviceHealthEnumeration.MAINTENANCE_REQUIRED));
        }
    }
}
