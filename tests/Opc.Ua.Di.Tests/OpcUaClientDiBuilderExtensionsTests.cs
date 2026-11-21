// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            IDiDiscoveryService? service = provider.GetService<IDiDiscoveryService>();

            Assert.That(service, Is.Not.Null);
        }

        [Test]
        public void AddOpcUaDiRegistersDiDeviceClientFactory()
        {
            ServiceProvider provider = BuildClientProvider();
            Func<NodeId, CancellationToken, ValueTask<DiDeviceClient>>? factory = provider.GetService<
                Func<NodeId, CancellationToken, ValueTask<DiDeviceClient>>>();

            Assert.That(factory, Is.Not.Null);
        }

        [Test]
        public void AddOpcUaDiRegistersDiLockClientFactory()
        {
            ServiceProvider provider = BuildClientProvider();
            Func<NodeId, CancellationToken, ValueTask<DiLockClient>>? factory = provider.GetService<
                Func<NodeId, CancellationToken, ValueTask<DiLockClient>>>();

            Assert.That(factory, Is.Not.Null);
        }

        [Test]
        public void AddOpcUaDiRegistersDiTopologyClientFactory()
        {
            ServiceProvider provider = BuildClientProvider();
            Func<CancellationToken, ValueTask<DiTopologyClient>>? factory = provider.GetService<
                Func<CancellationToken, ValueTask<DiTopologyClient>>>();

            Assert.That(factory, Is.Not.Null);
        }

        [Test]
        public void AddOpcUaDiRegistersSoftwareUpdateClientFactory()
        {
            ServiceProvider provider = BuildClientProvider();
            Func<NodeId, CancellationToken, ValueTask<SoftwareUpdateClient>>? factory = provider.GetService<
                Func<NodeId, CancellationToken, ValueTask<SoftwareUpdateClient>>>();

            Assert.That(factory, Is.Not.Null);
        }

        [Test]
        public void AddOpcUaDiRegistersDiTransferClientFactory()
        {
            ServiceProvider provider = BuildClientProvider();
            Func<NodeId, CancellationToken, ValueTask<DiTransferClient>>? factory = provider.GetService<
                Func<NodeId, CancellationToken, ValueTask<DiTransferClient>>>();

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
            int transferFactoryCount = services.Count(
                d => d.ServiceType ==
                    typeof(Func<NodeId, CancellationToken, ValueTask<DiTransferClient>>));

            Assert.That(discoveryCount, Is.EqualTo(1));
            Assert.That(deviceFactoryCount, Is.EqualTo(1));
            Assert.That(lockFactoryCount, Is.EqualTo(1));
            Assert.That(topologyFactoryCount, Is.EqualTo(1));
            Assert.That(softwareUpdateFactoryCount, Is.EqualTo(1));
            Assert.That(transferFactoryCount, Is.EqualTo(1));
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
