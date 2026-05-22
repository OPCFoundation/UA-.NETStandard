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
    /// compliance depth tests for Discovery services: FindServers
    /// filter combinations, GetEndpoints transport profile filtering,
    /// and endpoint property validation.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Discovery")]
    public class DiscoveryDepthTests : TestFixture
    {
        [Test]
        public async Task FindServersNoFilterReturnsAtLeastOneAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync()
                .ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers =
                await client.FindServersAsync(
                    default, CancellationToken.None)
                    .ConfigureAwait(false);

            Assert.That(servers.Count, Is.GreaterThan(0),
                "FindServers with no filter should return at least one.");
        }

        [Test]
        public async Task FindServersMatchingUriReturnsServerAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync()
                .ConfigureAwait(false);

            ArrayOf<ApplicationDescription> all =
                await client.FindServersAsync(
                    default, CancellationToken.None)
                    .ConfigureAwait(false);
            Assert.That(all.Count, Is.GreaterThan(0));

            string uri = all[0].ApplicationUri;
            ArrayOf<ApplicationDescription> filtered =
                await client.FindServersAsync(
                    new string[] { uri }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(filtered.Count, Is.GreaterThan(0),
                "Filtering by matching URI should return the server.");
        }

        [Test]
        public async Task FindServersNonMatchingUriReturnsEmptyAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync()
                .ConfigureAwait(false);

            ArrayOf<ApplicationDescription> result =
                await client.FindServersAsync(
                    new string[]
                    {
                        "urn:nonexistent:test:server:12345"
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Count, Is.Zero,
                "Non-matching URI should return zero results.");
        }

        [Test]
        public async Task FindServersWithDefaultFilterReturnsResultsAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync()
                .ConfigureAwait(false);

            ArrayOf<ApplicationDescription> result =
                await client.FindServersAsync(
                    default, CancellationToken.None)
                    .ConfigureAwait(false);

            Assert.That(result.Count, Is.GreaterThan(0),
                "FindServers with default filter should return results.");
        }

        [Test]
        public async Task FindServersReturnsServerApplicationTypeAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync()
                .ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers =
                await client.FindServersAsync(
                    default, CancellationToken.None)
                    .ConfigureAwait(false);
            Assert.That(servers.Count, Is.GreaterThan(0));

            Assert.That(servers[0].ApplicationType,
                Is.EqualTo(ApplicationType.Server)
                    .Or.EqualTo(ApplicationType.ClientAndServer),
                "Application type should be Server or ClientAndServer.");
        }

        [Test]
        public async Task FindServersReturnsNonEmptyApplicationUriAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync()
                .ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers =
                await client.FindServersAsync(
                    default, CancellationToken.None)
                    .ConfigureAwait(false);
            Assert.That(servers.Count, Is.GreaterThan(0));

            Assert.That(servers[0].ApplicationUri,
                Is.Not.Null.And.Not.Empty,
                "ApplicationUri must not be empty.");
        }

        [Test]
        public async Task FindServersReturnsDiscoveryUrlsAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync()
                .ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers =
                await client.FindServersAsync(
                    default, CancellationToken.None)
                    .ConfigureAwait(false);
            Assert.That(servers.Count, Is.GreaterThan(0));

            Assert.That(servers[0].DiscoveryUrls.Count, Is.GreaterThan(0),
                "Server should advertise at least one discovery URL.");
        }

        [Test]
        public async Task GetEndpointsDefaultReturnsAtLeastOneAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync()
                .ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints =
                await client.GetEndpointsAsync(
                    default, CancellationToken.None)
                    .ConfigureAwait(false);

            Assert.That(endpoints.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task GetEndpointsWithUaTcpProfileFilterAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync()
                .ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints =
                await client.GetEndpointsAsync(
                    new string[]
                    {
                        Profiles.UaTcpTransport
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(endpoints.Count, Is.GreaterThan(0),
                "Filtering by UA-TCP profile should return endpoints.");

            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(ep.TransportProfileUri,
                    Is.EqualTo(Profiles.UaTcpTransport),
                    "All returned endpoints should match the UA-TCP profile.");
            }
        }

        [Test]
        public async Task GetEndpointsWithHttpsProfileFilterOrIgnoreAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync()
                .ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints =
                await client.GetEndpointsAsync(
                    new string[]
                    {
                        Profiles.HttpsBinaryTransport
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            if (endpoints.Count == 0)
            {
                Assert.Ignore(
                    "Server does not expose HTTPS Binary transport.");
            }

            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(ep.TransportProfileUri,
                    Is.EqualTo(Profiles.HttpsBinaryTransport));
            }
        }

        [Test]
        public async Task GetEndpointsWithDefaultProfileReturnsResultsAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync()
                .ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints =
                await client.GetEndpointsAsync(
                    default, CancellationToken.None)
                    .ConfigureAwait(false);

            Assert.That(endpoints.Count, Is.GreaterThan(0),
                "GetEndpoints with default profile should return results.");
        }

        [Test]
        public async Task AllEndpointsHaveNonEmptyUrlAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync()
                .ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints =
                await client.GetEndpointsAsync(
                    default, CancellationToken.None)
                    .ConfigureAwait(false);

            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(ep.EndpointUrl, Is.Not.Null.And.Not.Empty,
                    "Every endpoint must have a non-empty URL.");
            }
        }

        [Test]
        public async Task AllEndpointsHaveServerDescriptionAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync()
                .ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints =
                await client.GetEndpointsAsync(
                    default, CancellationToken.None)
                    .ConfigureAwait(false);

            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(ep.Server, Is.Not.Null,
                    "Endpoint must have a Server description.");
                Assert.That(ep.Server.ApplicationUri,
                    Is.Not.Null.And.Not.Empty,
                    "Server ApplicationUri must not be empty.");
            }
        }

        [Test]
        public async Task AllEndpointsHaveTransportProfileUriAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync()
                .ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints =
                await client.GetEndpointsAsync(
                    default, CancellationToken.None)
                    .ConfigureAwait(false);

            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(ep.TransportProfileUri,
                    Is.Not.Null.And.Not.Empty,
                    "TransportProfileUri must not be empty.");
            }
        }

        [Test]
        public async Task SecureEndpointsHaveNonEmptyCertAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync()
                .ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints =
                await client.GetEndpointsAsync(
                    default, CancellationToken.None)
                    .ConfigureAwait(false);

            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode != MessageSecurityMode.None)
                {
                    Assert.That(ep.ServerCertificate.Length,
                        Is.GreaterThan(0),
                        $"Secure endpoint {ep.SecurityPolicyUri} " +
                        "must have a non-empty certificate.");
                }
            }
        }

        [Test]
        public async Task AllEndpointsHaveValidSecurityModeAsync()
        {
            using DiscoveryClient client = await CreateDiscoveryClientAsync()
                .ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints =
                await client.GetEndpointsAsync(
                    default, CancellationToken.None)
                    .ConfigureAwait(false);

            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(ep.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.None)
                        .Or.EqualTo(MessageSecurityMode.Sign)
                        .Or.EqualTo(MessageSecurityMode.SignAndEncrypt),
                    "SecurityMode must be None, Sign, or SignAndEncrypt.");
            }
        }

        private Task<DiscoveryClient> CreateDiscoveryClientAsync()
        {
            var endpointConfiguration =
                EndpointConfiguration.Create(ClientFixture.Config);

            return DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None);
        }
    }
}
