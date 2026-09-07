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

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Tests for <see cref="OpcUaVisionClientBuilderExtensions"/>. The
    /// extension registers a factory over the managed-session factory
    /// registered by <c>AddClient</c>. It is a small piece of glue but
    /// it is what a Vision consumer relies on to compose the client via
    /// DI.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed class OpcUaVisionClientBuilderExtensionsTests
    {
        [Test]
        public void AddVisionClientThrowsOnNullBuilder()
        {
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaVisionClientBuilderExtensions.AddVisionClient(null!));
        }

        [Test]
        public void AddVisionClientRegistersFactorySingleton()
        {
            IServiceCollection services = new ServiceCollection();
            var telemetry = new Mock<ITelemetryContext>().Object;
            services.AddSingleton(telemetry);
            services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(
                _ => Task.FromResult<ManagedSession>(null!));

            var builder = new TestClientBuilder(services);
            builder.AddVisionClient();

            Assert.Multiple(() =>
            {
                Assert.That(
                    services.Any(s => s.ServiceType == typeof(VisionClientFactory)),
                    Is.True);
                Assert.That(
                    services.Any(s =>
                        s.ServiceType == typeof(Func<CancellationToken, Task<VisionClient>>)),
                    Is.True);
            });
        }

        [Test]
        public void AddVisionClientFactoryThrowsWhenAddClientWasNotCalled()
        {
            IServiceCollection services = new ServiceCollection();
            var telemetry = new Mock<ITelemetryContext>().Object;
            services.AddSingleton(telemetry);

            var builder = new TestClientBuilder(services);
            builder.AddVisionClient();

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Throws<InvalidOperationException>(() =>
                provider.GetRequiredService<VisionClientFactory>());
        }

        [Test]
        public void AddVisionClientReturnsSameBuilder()
        {
            IServiceCollection services = new ServiceCollection();
            var telemetry = new Mock<ITelemetryContext>().Object;
            services.AddSingleton(telemetry);
            services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(
                _ => Task.FromResult<ManagedSession>(null!));

            var builder = new TestClientBuilder(services);
            IOpcUaClientBuilder returned = builder.AddVisionClient();

            Assert.That(returned, Is.SameAs(builder));
        }

        [Test]
        public void AddVisionClientIsIdempotent()
        {
            IServiceCollection services = new ServiceCollection();
            var telemetry = new Mock<ITelemetryContext>().Object;
            services.AddSingleton(telemetry);
            services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(
                _ => Task.FromResult<ManagedSession>(null!));

            var builder = new TestClientBuilder(services);
            builder.AddVisionClient();
            builder.AddVisionClient();

            int factoryCount = services.Count(s =>
                s.ServiceType == typeof(VisionClientFactory));

            Assert.That(factoryCount, Is.EqualTo(1));
        }

        private sealed class TestClientBuilder(IServiceCollection services) : IOpcUaClientBuilder
        {
            public IServiceCollection Services { get; } = services;
        }
    }
}
