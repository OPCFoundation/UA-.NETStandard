/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

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

            IOpcUaServerBuilder result = builder.UseDistributedPushConfiguration();

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
            builder.UseDistributedPushConfiguration(options => options.RequireLeadership = true);

            await using ServiceProvider sp = builder.Services.BuildServiceProvider();

            Assert.That(
                () => sp.GetRequiredService<IPushConfigurationTransactionCoordinator>(),
                Throws.InvalidOperationException);
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

            builder.UseDistributedPushConfiguration();

            await using ServiceProvider sp = builder.Services.BuildServiceProvider();
            Assert.That(
                sp.GetRequiredService<IPendingCertificateKeyStore>(),
                Is.InstanceOf<SharedKeyValuePendingCertificateKeyStore>());
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
