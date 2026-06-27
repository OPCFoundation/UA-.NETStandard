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
            // In-process gossip unit tests knowingly opt out of record protection.
            await using ServiceProvider services = ServicesWithNullProtector();

            await using var factory = new CrdtSessionManagerFactory(
                services, new ReplicatedSessionOptions());

            using var manager = factory.Create(
                NewServer().Object,
                NewConfiguration(),
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
                () => new CrdtSessionManagerFactory(Mock.Of<IServiceProvider>(), null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task FactoryRejectsReplicatedSessionStoreWithoutProtectorAsync()
        {
            await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            await using var factory = new CrdtSessionManagerFactory(
                services, new ReplicatedSessionOptions());

            Assert.That(
                () => factory.Create(
                    NewServer().Object,
                    NewConfiguration(),
                    TimeProvider.System,
                    _ => (Certificate?)null),
                Throws.InvalidOperationException
                    .With.Message.Contains(nameof(IRecordProtector)));
        }

        [Test]
        public async Task FactoryRejectsFastReconnectWithoutStronglyConsistentNonceStoreAsync()
        {
            // Fast reconnect replays a session by AuthenticationToken; the
            // single-use nonce must be strongly consistent across the replica
            // set. A record protector alone (no shared nonce store) must fail
            // closed rather than silently use a per-process registry.
            await using ServiceProvider services = ServicesWithNullProtector();
            var options = new ReplicatedSessionOptions();
            options.Session.EnableFastReconnect = true;
            await using var factory = new CrdtSessionManagerFactory(services, options);

            Assert.That(
                () => factory.Create(
                    NewServer().Object,
                    NewConfiguration(),
                    TimeProvider.System,
                    _ => (Certificate?)null),
                Throws.InvalidOperationException
                    .With.Message.Contains(nameof(ISharedKeyValueStore)));
        }

        [Test]
        public async Task FactoryCreatesWithFastReconnectAndRegisteredNonceStoreAsync()
        {
            await using ServiceProvider services = new ServiceCollection()
                .AddSingleton<IRecordProtector>(NullRecordProtector.Instance)
                .AddSingleton<ISharedKeyValueStore>(new InMemorySharedKeyValueStore())
                .BuildServiceProvider();
            var options = new ReplicatedSessionOptions();
            options.Session.EnableFastReconnect = true;
            await using var factory = new CrdtSessionManagerFactory(services, options);

            using var manager = factory.Create(
                NewServer().Object,
                NewConfiguration(),
                TimeProvider.System,
                _ => (Certificate?)null) as DistributedSessionManager;

            Assert.That(manager, Is.Not.Null);
        }

        [Test]
        public async Task UseReplicatedSessionsRegistersFactoryAsync()
        {
            var services = new ServiceCollection();
            // In-process gossip unit tests knowingly opt out of record protection.
            services.AddSingleton<IRecordProtector>(NullRecordProtector.Instance);
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

        private static ServiceProvider ServicesWithNullProtector()
        {
            return new ServiceCollection()
                .AddSingleton<IRecordProtector>(NullRecordProtector.Instance)
                .BuildServiceProvider();
        }

        private static Mock<IServerInternal> NewServer()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(telemetry);
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.MessageContext).Returns(context);
            return server;
        }

        private static ApplicationConfiguration NewConfiguration()
        {
            return new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MinSessionTimeout = 1000,
                    MaxSessionTimeout = 3_600_000,
                    MaxSessionCount = 100
                }
            };
        }
    }
}
