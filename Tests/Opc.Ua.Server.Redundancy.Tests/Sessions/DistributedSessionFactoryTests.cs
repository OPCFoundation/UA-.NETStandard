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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.Redundancy;
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
                _ => (Certificate?)null) as DistributedSessionManager;

            Assert.That(manager, Is.Not.Null);
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
