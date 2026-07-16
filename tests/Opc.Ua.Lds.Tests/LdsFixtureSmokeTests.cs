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
