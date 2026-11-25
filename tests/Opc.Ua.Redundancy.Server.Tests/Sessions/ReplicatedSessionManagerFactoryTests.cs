// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2000: system-under-test disposables are created per test and released at teardown;
//   there is no cross-test resource leak. Suppressed file-level for the suite.
#pragma warning disable CA2000 // Dispose objects before losing scope

#pragma warning disable CA2007

using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Redundancy.Server.Tests
{
    /// <summary>
    /// Tests for the CRDT session DI seam: <see cref="ReplicatedSessionManagerFactory"/>
    /// and the <c>UseReplicatedSessions</c> / <c>UseReplicatedAddressSpace</c>
    /// fluent registrations.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class ReplicatedSessionManagerFactoryTests
    {
        [Test]
        public async Task FactoryCreatesDistributedSessionManagerAsync()
        {
            // In-process gossip unit tests knowingly opt out of record protection.
            await using ServiceProvider services = ServicesWithNullProtector();

            await using var factory = new ReplicatedSessionManagerFactory(
                services, new ReplicatedSessionOptions());

            using var manager = factory.Create(
                NewServer().Object,
                NewConfiguration(),
                TimeProvider.System,
                _ => null) as DistributedSessionManager;

            Assert.That(manager, Is.Not.Null);
        }

        [Test]
        public void ConstructorRejectsNullArguments()
        {
            Assert.That(
                () => new ReplicatedSessionManagerFactory(null!, new ReplicatedSessionOptions()),
                Throws.ArgumentNullException);
            Assert.That(
                () => new ReplicatedSessionManagerFactory(Mock.Of<IServiceProvider>(), null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task FactoryRejectsReplicatedSessionStoreWithoutProtectorAsync()
        {
            await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            await using var factory = new ReplicatedSessionManagerFactory(
                services, new ReplicatedSessionOptions());

            Assert.That(
                () => factory.Create(
                    NewServer().Object,
                    NewConfiguration(),
                    TimeProvider.System,
                    _ => null),
                Throws.InvalidOperationException
                    .With.Message.Contains(nameof(IRecordProtector)));
        }

        [Test]
        public async Task FactoryRejectsFastReconnectWithoutACommittedSessionStoreAsync()
        {
            // Record protection alone cannot prove that the nonce and owner are current.
            await using ServiceProvider services = ServicesWithNullProtector();
            var options = new ReplicatedSessionOptions();
            options.Session.EnableFastReconnect = true;
            await using var factory = new ReplicatedSessionManagerFactory(services, options);

            Assert.That(
                () => factory.Create(
                    NewServer().Object,
                    NewConfiguration(),
                    TimeProvider.System,
                    _ => null),
                Throws.InvalidOperationException
                    .With.Message.Contains("independent freshness authority"));
        }

        [Test]
        public async Task FactoryRejectsFastReconnectWithProcessLocalRaftAsync()
        {
            var registrations = new ServiceCollection()
                .AddSingleton<IRecordProtector>(NullRecordProtector.Instance);
            registrations.AddOpcUa().AddServer(_ => { })
                .UseRedundancyConsistency();
            await using ServiceProvider services = registrations.BuildServiceProvider();
            var options = new ReplicatedSessionOptions();
            options.Session.EnableFastReconnect = true;
            await using var factory = new ReplicatedSessionManagerFactory(services, options);

            Assert.That(
                () => factory.Create(
                    NewServer().Object,
                    NewConfiguration(),
                    TimeProvider.System,
                    _ => null),
                Throws.InvalidOperationException.With.Message.Contains("independent freshness authority"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FactoryRejectsFastReconnectWithAnUnprotectedStoreAsync(bool hasCapability)
        {
            var backend = new Mock<ISharedKeyValueStore>(MockBehavior.Strict);
            if (hasCapability)
            {
                backend.As<ISharedKeyValueStoreConsistency>()
                    .Setup(s => s.IsLinearizable(It.IsAny<string>()))
                    .Returns(true);
            }
            await using ServiceProvider services = new ServiceCollection()
                .AddSingleton<IRecordProtector>(NullRecordProtector.Instance)
                .AddSingleton(backend.Object)
                .BuildServiceProvider();
            var options = new ReplicatedSessionOptions();
            options.Session.EnableFastReconnect = true;
            await using var factory = new ReplicatedSessionManagerFactory(services, options);

            Assert.That(
                () => factory.Create(
                    NewServer().Object,
                    NewConfiguration(),
                    TimeProvider.System,
                    _ => null),
                Throws.InvalidOperationException.With.Message.Contains("independent freshness authority"));
        }

        [Test]
        public async Task FactoryCreatesWithFastReconnectAndIndependentAuthorityAsync()
        {
            using var bulk = new InMemorySharedKeyValueStore();
            using var authority = new SimulatedSharedKeyValueStore();
            using var rng = RandomNumberGenerator.Create();
            byte[] key = new byte[32];
            rng.GetBytes(key);
            using var protector = new AesCbcHmacRecordProtector(key);
            Array.Clear(key, 0, key.Length);
            await using ServiceProvider services = new ServiceCollection()
                .AddSingleton<IRecordProtector>(protector)
                .AddSingleton<ISharedKeyValueStore>(bulk)
                .AddSharedRecordFreshness(_ => authority, Guid.NewGuid(), Guid.NewGuid())
                .BuildServiceProvider();
            await services.GetRequiredService<FreshnessProtectedKeyValueStore>().EnrollAsync().ConfigureAwait(false);
            var options = new ReplicatedSessionOptions();
            options.Session.EnableFastReconnect = true;
            await using var factory = new ReplicatedSessionManagerFactory(services, options);

            using var manager = factory.Create(
                NewServer().Object,
                NewConfiguration(),
                TimeProvider.System,
                _ => null) as DistributedSessionManager;

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
                Is.InstanceOf<ReplicatedSessionManagerFactory>());
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
                Has.Some.InstanceOf<ReplicatedAddressSpaceStartupTask>());
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
