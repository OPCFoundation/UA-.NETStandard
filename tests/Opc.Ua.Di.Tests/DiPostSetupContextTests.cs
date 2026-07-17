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
