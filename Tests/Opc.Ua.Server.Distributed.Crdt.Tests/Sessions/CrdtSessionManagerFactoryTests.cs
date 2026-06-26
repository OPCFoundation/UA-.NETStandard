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

#pragma warning disable CA2007

#nullable enable

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.Distributed;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Distributed.Crdt.Tests
{
    /// <summary>
    /// Tests for the CRDT session DI seam: <see cref="CrdtSessionManagerFactory"/>
    /// and the <c>UseReplicatedSessions</c> / <c>UseReplicatedAddressSpace</c>
    /// fluent registrations.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class CrdtSessionManagerFactoryTests
    {
        [Test]
        public async Task FactoryCreatesDistributedSessionManagerAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(telemetry);
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.MessageContext).Returns(context);

            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MinSessionTimeout = 1000,
                    MaxSessionTimeout = 3_600_000,
                    MaxSessionCount = 100
                }
            };

            await using var factory = new CrdtSessionManagerFactory(
                EmptyServices(), new ReplicatedSessionOptions());

            using var manager = factory.Create(
                server.Object,
                configuration,
                TimeProvider.System,
                _ => (Certificate?)null) as DistributedSessionManager;

            Assert.That(manager, Is.Not.Null);
        }

        [Test]
        public void ConstructorRejectsNullArguments()
        {
            Assert.That(
                () => new CrdtSessionManagerFactory(null!, new ReplicatedSessionOptions()),
                Throws.ArgumentNullException);
            Assert.That(
                () => new CrdtSessionManagerFactory(EmptyServices(), null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task UseReplicatedSessionsRegistersFactoryAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(_ => { })
                .UseReplicatedSessions(o => o.Session.EnableFastReconnect = true);
            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetRequiredService<ISessionManagerFactory>(),
                Is.InstanceOf<CrdtSessionManagerFactory>());
        }

        [Test]
        public async Task UseReplicatedAddressSpaceRegistersStartupTaskAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(_ => { })
                .UseReplicatedAddressSpace();
            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetServices<IServerStartupTask>(),
                Has.Some.InstanceOf<CrdtAddressSpaceStartupTask>());
        }

        private static IServiceProvider EmptyServices()
        {
            return Mock.Of<IServiceProvider>();
        }
    }
}
