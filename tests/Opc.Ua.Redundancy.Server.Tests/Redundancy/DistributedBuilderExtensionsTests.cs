/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for the fluent registration extensions
    /// (<see cref="DistributedServerBuilderExtensions"/>,
    /// <see cref="DistributedSessionMirroringBuilderExtensions"/>,
    /// <see cref="DistributedSubscriptionBuilderExtensions"/> and
    /// <see cref="ServerRedundancyBuilderExtensions"/>): the null-builder guards and that the registered
    /// factories resolve the expected services from dependency injection.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class DistributedBuilderExtensionsTests
    {
        [Test]
        public void AddServerServiceLevelThrowsOnNullBuilder()
        {
            Assert.That(
                () => DistributedServerBuilderExtensions.AddServerServiceLevel(
                    null!, new ConstantServiceLevelProvider()),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddServerServiceLevelThrowsOnNullProvider()
        {
            var builder = new DiTestServerBuilder();

            Assert.That(
                () => builder.AddServerServiceLevel(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddServerServiceLevelRegistersProviderAndStartupTask()
        {
            var builder = new DiTestServerBuilder();
            var provider = new ConstantServiceLevelProvider(123);

            IOpcUaServerBuilder result = builder.AddServerServiceLevel(provider);

            Assert.That(result, Is.SameAs(builder));
            using ServiceProvider sp = builder.Services.BuildServiceProvider();
            Assert.That(sp.GetRequiredService<IServiceLevelProvider>(), Is.SameAs(provider));
            Assert.That(sp.GetServices<IServerStartupTask>().ToArray(),
                Has.Some.InstanceOf<ServiceLevelStartupTask>());
        }

        [Test]
        public async Task ServiceLevelCompositionRegistersOnlyOnePublisherAsync()
        {
            var builder = new DiTestServerBuilder();
            var provider = new ConstantServiceLevelProvider(210);
            builder.UseDistributedAddressSpace()
                .AddServerServiceLevel(new ConstantServiceLevelProvider(240))
                .AddServerServiceLevel(provider);

            await using ServiceProvider services = builder.Services.BuildServiceProvider();

            Assert.That(services.GetRequiredService<IServiceLevelProvider>(), Is.SameAs(provider));
            Assert.That(services.GetServices<IServerStartupTask>().OfType<ServiceLevelStartupTask>().Count(),
                Is.EqualTo(1));
        }

        [Test]
        public void UseDistributedAddressSpaceThrowsOnNullBuilder()
        {
            Assert.That(
                () => DistributedServerBuilderExtensions.UseDistributedAddressSpace(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task UseDistributedAddressSpaceRegistersCustomRecordProtectorAsync()
        {
            var builder = new DiTestServerBuilder();

            builder.UseDistributedAddressSpace(options =>
                options.RecordProtectorFactory = _ => NullRecordProtector.Instance);

            await using ServiceProvider sp = builder.Services.BuildServiceProvider();
            Assert.That(sp.GetRequiredService<IRecordProtector>(), Is.SameAs(NullRecordProtector.Instance));
            Assert.That(sp.GetServices<IServerStartupTask>().ToArray(), Is.Not.Empty);
        }

        [Test]
        public async Task UseDistributedAddressSpaceRegistersDistributedValueCacheAsync()
        {
            var builder = new DiTestServerBuilder();

            builder.UseDistributedAddressSpace();

            await using ServiceProvider sp = builder.Services.BuildServiceProvider();

            IDistributedValueCache cache = sp.GetRequiredService<IDistributedValueCache>();
            Assert.That(cache, Is.Not.Null);

            // Before the server starts, the node-state store registry is not
            // populated, so the injected cache fails fast rather than silently
            // dropping writes to a store that does not exist yet.
            Assert.That(
                async () => await cache.TryGetAsync(new NodeId("v", 1), System.TimeSpan.FromMinutes(1)).ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public void UseDistributedSessionsThrowsOnNullBuilder()
        {
            Assert.That(
                () => DistributedServerBuilderExtensions.UseDistributedSessions(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void UseDistributedSessionsRegistersSessionManagerFactory()
        {
            var builder = new DiTestServerBuilder();

            builder.UseDistributedSessions(options => options.EnableFastReconnect = true);

            using ServiceProvider sp = builder.Services.BuildServiceProvider();
            Assert.That(sp.GetRequiredService<ISessionManagerFactory>(),
                Is.InstanceOf<DistributedSessionManagerFactory>());
        }

        [Test]
        public void UseDistributedSessionMirroringThrowsOnNullBuilder()
        {
            Assert.That(
                () => DistributedSessionMirroringBuilderExtensions.UseDistributedSessionMirroring(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void UseDistributedSessionMirroringRegistersSessionManagerFactory()
        {
            var builder = new DiTestServerBuilder();

            builder.UseDistributedSessionMirroring();

            using ServiceProvider sp = builder.Services.BuildServiceProvider();
            Assert.That(sp.GetRequiredService<ISessionManagerFactory>(),
                Is.InstanceOf<DistributedSessionManagerFactory>());
        }

        [Test]
        public void UseDistributedSubscriptionMirroringThrowsOnNullBuilder()
        {
            Assert.That(
                () => DistributedSubscriptionBuilderExtensions.UseDistributedSubscriptionMirroring(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task UseDistributedSubscriptionMirroringRegistersSubscriptionStoreAsync()
        {
            var builder = new DiTestServerBuilder();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(telemetry);
            builder.Services.AddSingleton(context);
            builder.Services.AddSingleton(telemetry);

            builder.UseDistributedSubscriptionMirroring();

            await using ServiceProvider sp = builder.Services.BuildServiceProvider();
            Assert.That(sp.GetRequiredService<ISubscriptionStore>(),
                Is.InstanceOf<SharedKeyValueSubscriptionStore>());
        }

        [Test]
        public void AddServerRedundancyThrowsOnNullBuilder()
        {
            Assert.That(
                () => ServerRedundancyBuilderExtensions.AddServerRedundancy(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddRequestServerStateChangeThrowsOnNullBuilder()
        {
            Assert.That(
                () => ServerRedundancyBuilderExtensions.AddRequestServerStateChange(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddRequestServerStateChangeRegistersStartupTask()
        {
            var builder = new DiTestServerBuilder();

            builder.AddRequestServerStateChange();

            using ServiceProvider sp = builder.Services.BuildServiceProvider();
            Assert.That(sp.GetServices<IServerStartupTask>().ToArray(),
                Has.Some.InstanceOf<RequestServerStateChangeStartupTask>());
        }

        [Test]
        public void AddManualFailoverDelegatesToRequestServerStateChange()
        {
            var builder = new DiTestServerBuilder();

#pragma warning disable CS0618 // exercising the obsolete alias on purpose
            IOpcUaServerBuilder result = builder.AddManualFailover();
#pragma warning restore CS0618

            Assert.That(result, Is.SameAs(builder));
            using ServiceProvider sp = builder.Services.BuildServiceProvider();
            Assert.That(sp.GetServices<IServerStartupTask>().ToArray(),
                Has.Some.InstanceOf<RequestServerStateChangeStartupTask>());
        }
    }
}
