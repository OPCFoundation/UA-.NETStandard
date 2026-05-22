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

namespace Opc.Ua.Conformance.Tests.DiscoveryServices
{
    /// <summary>
    /// compliance tests for Discovery endpoint validation
    /// and FindServers application type checks.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("DiscoveryEndpoint")]
    public class DiscoveryEndpointTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "003")]
        public async Task GetEndpointsWithTransportProfileFilterAsync()
        {
            ArrayOf<EndpointDescription> endpoints =
                await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            bool hasTcp = false;
            foreach (EndpointDescription e in endpoints)
            {
                if (e.TransportProfileUri != null &&
                    e.TransportProfileUri.Contains("uatcp"))
                {
                    hasTcp = true;
                    break;
                }
            }

            Assert.That(hasTcp, Is.True,
                "Expected at least one UA-TCP endpoint.");
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task GetEndpointsVerifyTransportProfileUriAsync()
        {
            ArrayOf<EndpointDescription> endpoints =
                await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(
                    ep.TransportProfileUri,
                    Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task GetEndpointsVerifySecurityPolicyUriAsync()
        {
            ArrayOf<EndpointDescription> endpoints =
                await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(
                    ep.SecurityPolicyUri,
                    Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task GetEndpointsVerifyApplicationUriAsync()
        {
            ArrayOf<EndpointDescription> endpoints =
                await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(
                    ep.Server.ApplicationUri,
                    Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task GetEndpointsVerifyAtLeastOneSecureEndpointAsync()
        {
            ArrayOf<EndpointDescription> endpoints =
                await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            bool hasSecure = false;
            foreach (EndpointDescription e in endpoints)
            {
                if (e.SecurityMode != MessageSecurityMode.None)
                {
                    hasSecure = true;
                    break;
                }
            }

            Assert.That(hasSecure, Is.True,
                "Expected at least one secure endpoint.");
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task GetEndpointsVerifyAnonymousTokenAvailableAsync()
        {
            ArrayOf<EndpointDescription> endpoints =
                await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            bool hasAnonymous = false;
            foreach (EndpointDescription e in endpoints)
            {
                if (e.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in e.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.Anonymous)
                        {
                            hasAnonymous = true;
                            break;
                        }
                    }
                }

                if (hasAnonymous)
                {
                    break;
                }
            }

            Assert.That(hasAnonymous, Is.True,
                "Expected Anonymous user token on at least one endpoint.");
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task GetEndpointsVerifyUsernameTokenAvailableAsync()
        {
            ArrayOf<EndpointDescription> endpoints =
                await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            bool hasUsername = false;
            foreach (EndpointDescription e in endpoints)
            {
                if (e.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in e.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName)
                        {
                            hasUsername = true;
                            break;
                        }
                    }
                }

                if (hasUsername)
                {
                    break;
                }
            }

            if (!hasUsername)
            {
                Assert.Fail(
                    "Server does not advertise Username user token.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "013")]
        public async Task FindServersVerifyApplicationTypeAsync()
        {
            var endpointConfiguration =
                EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers =
                await client.FindServersAsync(
                    default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(servers.Count, Is.GreaterThan(0));

            foreach (ApplicationDescription server in servers)
            {
                Assert.That(
                    server.ApplicationType,
                    Is.EqualTo(ApplicationType.Server)
                        .Or.EqualTo(ApplicationType.ClientAndServer));
            }
        }

        private async Task<ArrayOf<EndpointDescription>> GetEndpointsAsync()
        {
            var endpointConfiguration =
                EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            return await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
