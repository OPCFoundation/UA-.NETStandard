/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.Hosting;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Direct unit tests for the internal
    /// <see cref="DiPostSetupContext"/> — exercises construction
    /// guards, property accessors, and the convenience pass-throughs
    /// to <see cref="DiNodeManager"/>.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Hosting")]
    public sealed class DiPostSetupContextTests
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
        public void ConstructorThrowsOnNullManager()
        {
            IServiceProvider sp = new ServiceCollection().BuildServiceProvider();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new DiPostSetupContext(
                    manager: null!,
                    services: sp,
                    cancellationToken: CancellationToken.None))!;
            Assert.That(ex.ParamName, Is.EqualTo("manager"));
        }

        [Test]
        public void ConstructorThrowsOnNullServices()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new DiPostSetupContext(
                    manager: m_fixture.Manager,
                    services: null!,
                    cancellationToken: CancellationToken.None))!;
            Assert.That(ex.ParamName, Is.EqualTo("services"));
        }

        [Test]
        public void ManagerPropertyReturnsConstructorArgument()
        {
            IServiceProvider sp = new ServiceCollection().BuildServiceProvider();
            var context = new DiPostSetupContext(
                m_fixture.Manager, sp, CancellationToken.None);

            Assert.That(context.Manager, Is.SameAs(m_fixture.Manager));
        }

        [Test]
        public void CancellationTokenPropertyReturnsConstructorArgument()
        {
            using var cts = new CancellationTokenSource();
            IServiceProvider sp = new ServiceCollection().BuildServiceProvider();
            var context = new DiPostSetupContext(
                m_fixture.Manager, sp, cts.Token);

            Assert.That(context.CancellationToken, Is.EqualTo(cts.Token));
        }

        [Test]
        public void GetRequiredServiceReturnsRegisteredService()
        {
            var marker = new MarkerService();
            IServiceProvider sp = new ServiceCollection()
                .AddSingleton(marker)
                .BuildServiceProvider();
            var context = new DiPostSetupContext(
                m_fixture.Manager, sp, CancellationToken.None);

            MarkerService resolved = context.GetRequiredService<MarkerService>();

            Assert.That(resolved, Is.SameAs(marker));
        }

        [Test]
        public void GetRequiredServiceThrowsWhenServiceMissing()
        {
            IServiceProvider sp = new ServiceCollection().BuildServiceProvider();
            var context = new DiPostSetupContext(
                m_fixture.Manager, sp, CancellationToken.None);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => context.GetRequiredService<MarkerService>())!;
            Assert.That(ex.Message, Does.Contain(typeof(MarkerService).FullName));
        }

        [Test]
        public async Task CreateDeviceAsyncDelegatesToManager()
        {
            IServiceProvider sp = new ServiceCollection().BuildServiceProvider();
            var context = new DiPostSetupContext(
                m_fixture.Manager, sp, CancellationToken.None);

            IDeviceBuilder<DeviceState> builder = await context
                .CreateDeviceAsync(new QualifiedName(
                    "CtxDevice1", m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            Assert.That(builder.Device.BrowseName.Name, Is.EqualTo("CtxDevice1"));
            // Round-trip via Manager.Device to verify both delegation
            // and that the device was registered with the manager.
            IDeviceBuilder<DeviceState> lookup =
                context.Device<DeviceState>(builder.Device.NodeId);
            Assert.That(lookup.Device, Is.SameAs(builder.Device));
        }

        private sealed class MarkerService;
    }
}
