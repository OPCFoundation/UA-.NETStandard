// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Discovery;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Configuration;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    /// <summary>
    /// Exercises the <see cref="OpcUaDiscoveryService"/> happy paths
    /// (FindServersAsync/GetEndpointsAsync succeeding against a real
    /// server, and the <c>ConfigurationProvider</c> delegation branch)
    /// that are not covered by the constructor/argument-validation tests
    /// in <see cref="ClientInfrastructureCoverageTests"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("Discovery")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class OpcUaDiscoveryServiceCoverageTests : ClientTestFramework
    {
        public OpcUaDiscoveryServiceCoverageTests()
            : base(Utils.UriSchemeOpcTcp)
        {
        }

        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            return base.OneTimeSetUpAsync();
        }

        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        [Test]
        public async Task FindServersAsyncReturnsApplicationsFromRealServerAsync()
        {
            var service = new OpcUaDiscoveryService(
                new OpcUaClientOptions { Configuration = ClientFixture.Config },
                Telemetry);

            ArrayOf<ApplicationDescription> applications = await service
                .FindServersAsync(ServerUrl.ToString())
                .ConfigureAwait(false);

            Assert.That(applications.IsNull, Is.False);
            Assert.That(applications.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task GetEndpointsAsyncReturnsEndpointsFromRealServerAsync()
        {
            var service = new OpcUaDiscoveryService(
                new OpcUaClientOptions { Configuration = ClientFixture.Config },
                Telemetry);

            ArrayOf<EndpointDescription> endpoints = await service
                .GetEndpointsAsync(ServerUrl.ToString())
                .ConfigureAwait(false);

            Assert.That(endpoints.IsNull, Is.False);
            Assert.That(endpoints.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task FindServersAsyncUsesConfigurationProviderWhenSetAsync()
        {
            var configurationProvider = new FakeConfigurationProvider(ClientFixture.Config);
            var service = new OpcUaDiscoveryService(
                new OpcUaClientOptions
                {
                    ConfigurationProvider = configurationProvider,
                    Configuration = ClientFixture.Config
                },
                Telemetry);

            ArrayOf<ApplicationDescription> applications = await service
                .FindServersAsync(ServerUrl.ToString())
                .ConfigureAwait(false);

            Assert.That(applications.IsNull, Is.False);
            Assert.That(configurationProvider.GetAsyncCallCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Minimal <see cref="IOpcUaApplicationConfigurationProvider"/> fake
        /// that hands back a pre-built configuration, so the
        /// <c>ConfigurationProvider != null</c> delegation branch in
        /// <see cref="OpcUaDiscoveryService"/> can be exercised without a
        /// second real application instance.
        /// </summary>
        private sealed class FakeConfigurationProvider(ApplicationConfiguration configuration)
            : IOpcUaApplicationConfigurationProvider
        {
            public int GetAsyncCallCount { get; private set; }

            public IApplicationInstance Application => null!;

            public ApplicationConfiguration Configuration => configuration;

            public Task<ApplicationConfiguration> GetAsync(CancellationToken ct = default)
            {
                GetAsyncCallCount++;
                return Task.FromResult(configuration);
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }
        }
    }
}
