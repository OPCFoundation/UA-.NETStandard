// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Lds.Tests
{
    /// <summary>
    /// Sanity tests for the in-process LDS fixture. Verifies the LDS starts,
    /// FindServers returns its own description, and GetEndpoints surfaces the
    /// configured opc.tcp endpoints. Acts as a guard rail before the
    /// substantive Discovery Register / LDS-ME tests run.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("DiscoveryServices")]
    public class LdsFixtureSmokeTests : LdsTestFixture
    {
        [Test]
        public async Task FindServersReturnsAtLeastSelfAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync().ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers =
                await client.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(servers.Count, Is.GreaterThanOrEqualTo(1),
                "FindServers must return at least the LDS itself.");
        }

        [Test]
        public async Task GetEndpointsReturnsOpcTcpEndpointAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync().ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints =
                await client.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(endpoints.Count, Is.GreaterThanOrEqualTo(1));
            bool foundOpcTcp = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.EndpointUrl != null &&
                    ep.EndpointUrl.StartsWith("opc.tcp://", System.StringComparison.Ordinal))
                {
                    foundOpcTcp = true;
                    break;
                }
            }
            Assert.That(foundOpcTcp,
                "GetEndpoints should return at least one opc.tcp endpoint.");
        }

        [Test]
        public async Task FindServersOnNetworkReturnsEmptyByDefaultAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync().ConfigureAwait(false);

            (ArrayOf<ServerOnNetwork> servers, _) =
                await client.FindServersOnNetworkAsync(0, 0, default, CancellationToken.None)
                    .ConfigureAwait(false);

            Assert.That(servers, Has.Count.EqualTo(0));
        }

        private Task<DiscoveryClient> CreateDiscoveryClientAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            return DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None);
        }
    }
}
