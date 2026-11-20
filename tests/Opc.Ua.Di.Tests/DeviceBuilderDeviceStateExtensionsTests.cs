/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
