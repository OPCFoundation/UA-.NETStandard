/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for the distributed session DI seam:
    /// <see cref="DistributedSessionManagerFactory"/> and the
    /// <c>UseDistributedSessions</c> fluent registration.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class DistributedSessionFactoryTests
    {
        [Test]
        public void FactoryCreatesDistributedSessionManager()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(telemetry);
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.MessageContext).Returns(context);

            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MinSessionTimeout = 1000,
                    MaxSessionTimeout = 3_600_000,
                    MaxSessionCount = 100
                }
            };

            using var kv = new InMemorySharedKeyValueStore();
            var factory = new DistributedSessionManagerFactory(kv);

            using var manager = factory.Create(
                serverMock.Object,
                configuration,
                TimeProvider.System,
                _ => null) as DistributedSessionManager;

            Assert.That(manager, Is.Not.Null);
        }

        /// <summary>
        /// Issue #4627 RS-7: the public factory applies the same fail-closed rule as the
        /// dependency injection registration instead of mirroring session secrets in
        /// cleartext to an external store.
        /// </summary>
        [Test]
        public void FactoryRejectsExternalStoreWithoutProtector()
        {
            using var inner = new InMemorySharedKeyValueStore();
            var external = new SharedSessionStoreTests.NoCompareAndSwapStore(inner);

            Assert.That(
                () => new DistributedSessionManagerFactory(external),
                Throws.InvalidOperationException);
            Assert.That(
                () => new DistributedSessionManagerFactory(external, NullRecordProtector.Instance),
                Throws.Nothing);
        }

        [Test]
        public void FactoryConstructorRejectsNullStore()
        {
            Assert.That(
                () => new DistributedSessionManagerFactory(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task UseDistributedSessionsRegistersFactoryAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(_ => { })
                .UseDistributedSessions(o => o.EnableFastReconnect = true);
            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetRequiredService<ISessionManagerFactory>(),
                Is.InstanceOf<DistributedSessionManagerFactory>());
            Assert.That(
                provider.GetRequiredService<ISharedKeyValueStore>(),
                Is.InstanceOf<InMemorySharedKeyValueStore>());
        }

        [Test]
        public async Task UseDistributedSessionsSharesStoreWithAddressSpaceAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(_ => { })
                .UseDistributedAddressSpace()
                .UseDistributedSessions();
            await using ServiceProvider provider = services.BuildServiceProvider();

            ISharedKeyValueStore first = provider.GetRequiredService<ISharedKeyValueStore>();
            ISharedKeyValueStore second = provider.GetRequiredService<ISharedKeyValueStore>();

            // Both features compose over a single shared backend.
            Assert.That(ReferenceEquals(first, second), Is.True);
            Assert.That(
                provider.GetRequiredService<ISessionManagerFactory>(),
                Is.InstanceOf<DistributedSessionManagerFactory>());
        }

        [Test]
        public void DefaultOptionsDisableFastReconnect()
        {
            var options = new DistributedSessionOptions();

            Assert.That(options.EnableFastReconnect, Is.False);
        }
    }
}
