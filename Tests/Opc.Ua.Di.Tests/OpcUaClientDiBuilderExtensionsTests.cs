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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Di.Client;
using Opc.Ua.Di.Client.Hosting;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Direct unit tests for
    /// <see cref="OpcUaClientDiBuilderExtensions"/> — verifies the four
    /// factory registrations and the AddClient() prerequisite check.
    /// Resolution is performed against a built ServiceProvider but no
    /// real session is opened: the factories are returned as
    /// delegate-typed singletons.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Hosting")]
    public sealed class OpcUaClientDiBuilderExtensionsTests
    {
        [Test]
        public void AddOpcUaDiThrowsOnNullBuilder()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => OpcUaClientDiBuilderExtensions.AddOpcUaDi(builder: null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("builder"));
        }

        [Test]
        public void AddOpcUaDiRegistersDiDiscoveryService()
        {
            ServiceProvider provider = BuildClientProvider();
            var service = provider.GetService<IDiDiscoveryService>();

            Assert.That(service, Is.Not.Null);
        }

        [Test]
        public void AddOpcUaDiRegistersDiDeviceClientFactory()
        {
            ServiceProvider provider = BuildClientProvider();
            var factory = provider.GetService<
                Func<NodeId, CancellationToken, ValueTask<DiDeviceClient>>>();

            Assert.That(factory, Is.Not.Null);
        }

        [Test]
        public void AddOpcUaDiRegistersDiLockClientFactory()
        {
            ServiceProvider provider = BuildClientProvider();
            var factory = provider.GetService<
                Func<NodeId, CancellationToken, ValueTask<DiLockClient>>>();

            Assert.That(factory, Is.Not.Null);
        }

        [Test]
        public void AddOpcUaDiRegistersDiTopologyClientFactory()
        {
            ServiceProvider provider = BuildClientProvider();
            var factory = provider.GetService<
                Func<CancellationToken, ValueTask<DiTopologyClient>>>();

            Assert.That(factory, Is.Not.Null);
        }

        [Test]
        public void AddOpcUaDiRegistersSoftwareUpdateClientFactory()
        {
            ServiceProvider provider = BuildClientProvider();
            var factory = provider.GetService<
                Func<NodeId, CancellationToken, ValueTask<SoftwareUpdateClient>>>();

            Assert.That(factory, Is.Not.Null);
        }

        [Test]
        public void ResolvingDiDiscoveryServiceWithoutAddClientThrows()
        {
            // Skip AddClient() — the IDiDiscoveryService factory must
            // throw InvalidOperationException pointing the user at the
            // missing AddClient() call.
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa();
            var clientBuilder = new StubClientBuilder(services);
            clientBuilder.AddOpcUaDi();

            ServiceProvider provider = services.BuildServiceProvider();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => provider.GetService<IDiDiscoveryService>())!;
            Assert.That(ex.Message, Does.Contain("AddClient"));
        }

        [Test]
        public void AddOpcUaDiCalledTwiceDoesNotDuplicateRegistrations()
        {
            // TryAddSingleton semantics: second AddOpcUaDi() should
            // silently no-op for each of the four factory types.
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddClient(o => { });
            var clientBuilder = new StubClientBuilder(services);

            clientBuilder.AddOpcUaDi();
            clientBuilder.AddOpcUaDi();

            int discoveryCount = services.Count(
                d => d.ServiceType == typeof(IDiDiscoveryService));
            int deviceFactoryCount = services.Count(
                d => d.ServiceType ==
                    typeof(Func<NodeId, CancellationToken, ValueTask<DiDeviceClient>>));
            int lockFactoryCount = services.Count(
                d => d.ServiceType ==
                    typeof(Func<NodeId, CancellationToken, ValueTask<DiLockClient>>));
            int topologyFactoryCount = services.Count(
                d => d.ServiceType ==
                    typeof(Func<CancellationToken, ValueTask<DiTopologyClient>>));
            int softwareUpdateFactoryCount = services.Count(
                d => d.ServiceType ==
                    typeof(Func<NodeId, CancellationToken, ValueTask<SoftwareUpdateClient>>));

            Assert.That(discoveryCount, Is.EqualTo(1));
            Assert.That(deviceFactoryCount, Is.EqualTo(1));
            Assert.That(lockFactoryCount, Is.EqualTo(1));
            Assert.That(topologyFactoryCount, Is.EqualTo(1));
            Assert.That(softwareUpdateFactoryCount, Is.EqualTo(1));
        }

        // ---------------------------------------------------------------
        // Test helpers
        // ---------------------------------------------------------------

        /// <summary>
        /// Builds a ServiceProvider with the standard AddClient +
        /// AddOpcUaDi pipeline. The action-overload of AddClient does
        /// not require a non-null Configuration / Endpoint to register
        /// the core services consumed by the DI factories
        /// (ITelemetryContext and the managed-session accessor).
        /// </summary>
        private static ServiceProvider BuildClientProvider()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddClient(o => { });
            var clientBuilder = new StubClientBuilder(services);
            clientBuilder.AddOpcUaDi();
            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Minimal <see cref="IOpcUaClientBuilder"/> implementation used
        /// for tests — the concrete builder returned by
        /// <c>AddClient(...)</c> is internal to the client assembly.
        /// </summary>
        private sealed class StubClientBuilder : IOpcUaClientBuilder
        {
            public StubClientBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }
    }
}
