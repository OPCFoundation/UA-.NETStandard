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

#nullable enable

using System.Linq;
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
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            builder.Services.AddSingleton(context);

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
