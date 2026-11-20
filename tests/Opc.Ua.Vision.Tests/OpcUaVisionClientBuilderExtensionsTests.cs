// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
            ITelemetryContext telemetry = new Mock<ITelemetryContext>().Object;
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
            ITelemetryContext telemetry = new Mock<ITelemetryContext>().Object;
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
            ITelemetryContext telemetry = new Mock<ITelemetryContext>().Object;
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
            ITelemetryContext telemetry = new Mock<ITelemetryContext>().Object;
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
