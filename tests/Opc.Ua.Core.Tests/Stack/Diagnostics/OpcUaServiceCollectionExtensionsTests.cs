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

using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.Diagnostics
{
    /// <summary>
    /// Tests for the root <c>services.AddOpcUa()</c> entry point and the
    /// fluent <see cref="IOpcUaBuilder"/> extensions in
    /// <c>Opc.Ua.Core</c>.
    /// </summary>
    [TestFixture]
    [Category("Core")]
    [Parallelizable]
    public sealed class OpcUaServiceCollectionExtensionsTests
    {
        [Test]
        public void AddOpcUaThrowsForNullServices()
        {
            Assert.That(
                () => OpcUaServiceCollectionExtensions.AddOpcUa(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddOpcUaReturnsBuilderWithServices()
        {
            var services = new ServiceCollection();

            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(builder, Is.Not.Null);
            Assert.That(builder.Services, Is.SameAs(services));
        }

        [Test]
        public void AddOpcUaIsIdempotentForTelemetry()
        {
            var services = new ServiceCollection();

            services.AddOpcUa();
            int afterFirst = CountDescriptorsFor<ITelemetryContext>(services);
            services.AddOpcUa();
            int afterSecond = CountDescriptorsFor<ITelemetryContext>(services);

            Assert.That(afterFirst, Is.EqualTo(1));
            Assert.That(afterSecond, Is.EqualTo(1));
        }

        [Test]
        public void AddOpcUaIsIdempotentForBufferManagerFactory()
        {
            var services = new ServiceCollection();

            services.AddOpcUa();
            int factoriesAfterFirst = CountDescriptorsFor<IBufferManagerFactory>(services);
            int optionsAfterFirst = CountDescriptorsFor<BufferManagerFactoryOptions>(services);
            services.AddOpcUa();

            Assert.That(factoriesAfterFirst, Is.EqualTo(1));
            Assert.That(optionsAfterFirst, Is.EqualTo(1));
            Assert.That(CountDescriptorsFor<IBufferManagerFactory>(services), Is.EqualTo(1));
            Assert.That(CountDescriptorsFor<BufferManagerFactoryOptions>(services), Is.EqualTo(1));
        }

        [Test]
        public void AddOpcUaDoesNotOverrideExistingBufferManagerFactory()
        {
            var services = new ServiceCollection();
            var custom = new BufferManagerFactoryStub();
            services.AddSingleton<IBufferManagerFactory>(custom);

            services.AddOpcUa();

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<IBufferManagerFactory>(), Is.SameAs(custom));
        }

        [Test]
        public void AddOpcUaDoesNotOverrideExistingTelemetryContext()
        {
            var services = new ServiceCollection();
            ITelemetryContext custom = new TelemetryContextStub();
            services.AddSingleton(custom);

            services.AddOpcUa();

            using ServiceProvider sp = services.BuildServiceProvider();
            ITelemetryContext resolved = sp.GetRequiredService<ITelemetryContext>();
            Assert.That(resolved, Is.SameAs(custom));
        }

        [Test]
        public void TelemetryContextResolvesHostLoggerFactory()
        {
            var services = new ServiceCollection();
            using ILoggerFactory hostFactory = LoggerFactory.Create(b => b.AddDebug());
            services.AddSingleton(hostFactory);

            services.AddOpcUa();

            using ServiceProvider sp = services.BuildServiceProvider();
            ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
            Assert.That(telemetry, Is.InstanceOf<ServiceProviderTelemetryContext>());
            Assert.That(telemetry.LoggerFactory, Is.SameAs(hostFactory));
        }

        [Test]
        public void TelemetryContextFallsBackToNullLoggerFactory()
        {
            var services = new ServiceCollection();
            services.AddOpcUa();

            using ServiceProvider sp = services.BuildServiceProvider();
            ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
            Assert.That(telemetry, Is.InstanceOf<ServiceProviderTelemetryContext>());
            Assert.That(telemetry.LoggerFactory, Is.SameAs(NullLoggerFactory.Instance));
        }

        [Test]
        public void AddLoggingChainsToOpcUaBuilder()
        {
            var services = new ServiceCollection();

            IOpcUaBuilder result = services.AddOpcUa().AddLogging();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Services, Is.SameAs(services));

            using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetService<ILoggerFactory>(), Is.Not.Null);
        }

        [Test]
        public void AddLoggingWithConfigureChainsToOpcUaBuilder()
        {
            var services = new ServiceCollection();
            bool called = false;

            IOpcUaBuilder result = services.AddOpcUa()
                .AddLogging(b =>
                {
                    called = true;
                    b.SetMinimumLevel(LogLevel.Warning);
                });

            Assert.That(called, Is.True);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Services, Is.SameAs(services));

            using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetService<ILoggerFactory>(), Is.Not.Null);
        }

        [Test]
        public void AddMetricsChainsToOpcUaBuilder()
        {
            var services = new ServiceCollection();

            IOpcUaBuilder result = services.AddOpcUa().AddMetrics();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Services, Is.SameAs(services));
        }

        [Test]
        public void AddLoggingNullArgsThrow()
        {
            Assert.That(
                () => OpcUaServiceCollectionExtensions.AddLogging(null!),
                Throws.ArgumentNullException);

            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            Assert.That(
                () => builder.AddLogging((Action<ILoggingBuilder>)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddMetricsNullArgsThrow()
        {
            Assert.That(
                () => OpcUaServiceCollectionExtensions.AddMetrics(null!),
                Throws.ArgumentNullException);

            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            Assert.That(
                () => builder.AddMetrics((Action<IMetricsBuilder>)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void IOpcUaBuilderIsBackwardsCompatibleWithIDependencyInjectionBuilder()
        {
            // Existing extension methods on IDependencyInjectionBuilder must
            // still resolve through the new builder type.
            var services = new ServiceCollection();
            IDependencyInjectionBuilder legacy = services.AddOpcUa();
            Assert.That(legacy.Services, Is.SameAs(services));
        }

        private static int CountDescriptorsFor<TService>(IServiceCollection services)
        {
            int count = 0;
            foreach (ServiceDescriptor d in services)
            {
                if (d.ServiceType == typeof(TService))
                {
                    count++;
                }
            }
            return count;
        }

        private sealed class TelemetryContextStub : ITelemetryContext
        {
            public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;

            public ActivitySource ActivitySource { get; } = new("Stub");

            public Meter CreateMeter()
            {
                return new("Stub");
            }
        }

        private sealed class BufferManagerFactoryStub : IBufferManagerFactory
        {
            public IBufferManager Create(
                string name,
                int maxBufferSize,
                ITelemetryContext telemetry)
            {
                return new FastBufferManager(name, maxBufferSize, telemetry);
            }
        }
    }
}
