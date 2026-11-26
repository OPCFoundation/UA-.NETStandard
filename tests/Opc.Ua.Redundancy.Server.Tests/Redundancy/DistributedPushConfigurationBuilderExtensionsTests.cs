// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for the fluent registration extension
    /// <see cref="DistributedPushConfigurationBuilderExtensions.UseDistributedPushConfiguration"/>:
    /// the null-builder guard, that the registered factories resolve the
    /// distributed coordinator and pending-key store, shared-store reuse, and
    /// the fail-closed record-protection default for external stores.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class DistributedPushConfigurationBuilderExtensionsTests
    {
        [Test]
        public void UseDistributedPushConfigurationThrowsOnNullBuilder()
        {
            Assert.That(
                () => DistributedPushConfigurationBuilderExtensions.UseDistributedPushConfiguration(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task RegistersDistributedCoordinatorAndPendingKeyStoreAsync()
        {
            var builder = new DiTestServerBuilder();
            builder.Services.AddSingleton(NUnitTelemetryContext.Create());
            AddFreshnessStore(builder.Services);

            IOpcUaServerBuilder result = builder.UseDistributedPushConfiguration(ConfigureMember);

            Assert.That(result, Is.SameAs(builder));
            await using ServiceProvider sp = builder.Services.BuildServiceProvider();

            Assert.That(
                sp.GetRequiredService<IPushConfigurationTransactionCoordinator>(),
                Is.InstanceOf<DistributedPushConfigurationTransactionCoordinator>());
            Assert.That(
                sp.GetRequiredService<IPendingCertificateKeyStore>(),
                Is.InstanceOf<SharedKeyValuePendingCertificateKeyStore>());
        }

        [Test]
        public async Task RequireLeadershipWithoutElectionFailsFastAsync()
        {
            var builder = new DiTestServerBuilder();
            builder.Services.AddSingleton(NUnitTelemetryContext.Create());
            AddFreshnessStore(builder.Services);
            builder.UseDistributedPushConfiguration(options =>
            {
                ConfigureMember(options);
                options.RequireLeadership = true;
            });

            await using ServiceProvider sp = builder.Services.BuildServiceProvider();

            Assert.That(
                () => sp.GetRequiredService<IPushConfigurationTransactionCoordinator>(),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task MissingFreshnessStoreDoesNotRegisterOrUseLocalFallbackAsync()
        {
            var builder = new DiTestServerBuilder();
            builder.Services.AddSingleton(NUnitTelemetryContext.Create());
            builder.UseDistributedPushConfiguration(ConfigureMember);

            await using ServiceProvider sp = builder.Services.BuildServiceProvider();
            Assert.That(sp.GetService<ISharedKeyValueStore>(), Is.Null);
            Assert.That(() => sp.GetRequiredService<IPushConfigurationTransactionCoordinator>(),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task MissingMembershipRejectsProtectedLeaseRegistrationAsync()
        {
            var builder = new DiTestServerBuilder();
            builder.Services.AddSingleton(NUnitTelemetryContext.Create());
            AddFreshnessStore(builder.Services);
            builder.UseDistributedPushConfiguration(options => options.ReplicaId = "A");

            await using ServiceProvider sp = builder.Services.BuildServiceProvider();
            Assert.That(() => sp.GetRequiredService<IPushConfigurationTransactionCoordinator>(),
                Throws.ArgumentException);
        }

        [Test]
        public async Task ReusesAlreadyRegisteredSharedStoreAsync()
        {
            var builder = new DiTestServerBuilder();
            builder.Services.AddSingleton(NUnitTelemetryContext.Create());
            using var shared = new InMemorySharedKeyValueStore();
            builder.Services.AddSingleton<ISharedKeyValueStore>(shared);

            builder.UseDistributedPushConfiguration();

            await using ServiceProvider sp = builder.Services.BuildServiceProvider();
            Assert.That(sp.GetRequiredService<ISharedKeyValueStore>(), Is.SameAs(shared));
        }

        [Test]
        public async Task InMemoryStoreWithoutProtectorResolvesPendingKeyStoreAsync()
        {
            var builder = new DiTestServerBuilder();
            builder.Services.AddSingleton(NUnitTelemetryContext.Create());

            builder.Services.AddSingleton<ISharedKeyValueStore, InMemorySharedKeyValueStore>();
            builder.UseDistributedPushConfiguration();

            await using ServiceProvider sp = builder.Services.BuildServiceProvider();
            Assert.That(
                sp.GetRequiredService<IPendingCertificateKeyStore>(),
                Is.InstanceOf<SharedKeyValuePendingCertificateKeyStore>());
        }

        private static void ConfigureMember(DistributedPushConfigurationOptions options)
        {
            options.ReplicaId = "A";
            options.LeaseMemberIds = ["A", "B"];
        }

        private static void AddFreshnessStore(IServiceCollection services)
        {
            services.AddSingleton<ISharedKeyValueStore, InMemorySharedKeyValueStore>();
            services.AddSingleton<SimulatedSharedKeyValueStore>();
            services.AddSingleton<IRecordProtector>(_ =>
            {
                using var random = RandomNumberGenerator.Create();
                var key = new byte[32];
                random.GetBytes(key);
                return new AesCbcHmacRecordProtector(key);
            });
            services.AddSharedRecordFreshness(
                sp => sp.GetRequiredService<SimulatedSharedKeyValueStore>(), Guid.NewGuid(), Guid.NewGuid());
        }

        [Test]
        public async Task ExternalStoreWithoutProtectorFailsClosedAsync()
        {
            var builder = new DiTestServerBuilder();
            builder.Services.AddSingleton(NUnitTelemetryContext.Create());
            builder.Services.AddSingleton<ISharedKeyValueStore>(
                new ThrowingSharedKeyValueStore(new InMemorySharedKeyValueStore()));

            builder.UseDistributedPushConfiguration();

            await using ServiceProvider sp = builder.Services.BuildServiceProvider();
            Assert.That(
                () => sp.GetRequiredService<IPendingCertificateKeyStore>(),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task ExternalStoreWithConfiguredProtectorResolvesAsync()
        {
            var builder = new DiTestServerBuilder();
            builder.Services.AddSingleton(NUnitTelemetryContext.Create());
            builder.Services.AddSingleton<ISharedKeyValueStore>(
                new ThrowingSharedKeyValueStore(new InMemorySharedKeyValueStore()));

            builder.UseDistributedPushConfiguration(options =>
                options.RecordProtectorFactory = _ => NullRecordProtector.Instance);

            await using ServiceProvider sp = builder.Services.BuildServiceProvider();
            Assert.That(
                sp.GetRequiredService<IPendingCertificateKeyStore>(),
                Is.InstanceOf<SharedKeyValuePendingCertificateKeyStore>());
        }
    }
}
