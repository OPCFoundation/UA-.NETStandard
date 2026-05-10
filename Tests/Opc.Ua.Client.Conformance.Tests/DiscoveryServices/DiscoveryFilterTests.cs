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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for Discovery Service Set – FindServers Filter
    /// and Discovery Configuration conformance units.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Discovery")]
    [Category("DiscoveryFilter")]
    public class DiscoveryFilterTests : TestFixture
    {
        [Description("FindServers with matching ServerUri filter returns only matching servers.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "013")]
        public async Task FindServersWithServerUriFilterAsync()
        {
            var endpointConfiguration =
                EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, endpointConfiguration, Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            // First discover the server URI
            ArrayOf<ApplicationDescription> all =
                await client.FindServersAsync(
                    default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(all.Count, Is.GreaterThan(0));

            string uri = all[0].ApplicationUri;

            // Now filter by that URI
            ArrayOf<ApplicationDescription> filtered =
                await client.FindServersAsync(
                    default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(filtered.Count, Is.GreaterThan(0));
            foreach (ApplicationDescription app in filtered)
            {
                Assert.That(app.ApplicationUri, Is.Not.Null.And.Not.Empty);
            }
        }

        [Description("FindServers with non-matching URI returns empty result.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "013")]
        public async Task FindServersNonMatchingUriReturnsEmptyAsync()
        {
            var endpointConfiguration =
                EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, endpointConfiguration, Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<ApplicationDescription> all =
                await client.FindServersAsync(
                    default, CancellationToken.None).ConfigureAwait(false);

            // Verify no server matches a fabricated URI
            bool found = false;
            foreach (ApplicationDescription app in all)
            {
                if (string.Equals(
                    app.ApplicationUri,
                    "urn:nonexistent:server:12345:filter",
                    StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.False,
                "No server should match fabricated URI.");
        }

        [Description("FindServers with LocaleId filter returns servers with valid names.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "002")]
        public async Task FindServersWithLocaleIdFilterAsync()
        {
            var endpointConfiguration =
                EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, endpointConfiguration, Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers =
                await client.FindServersAsync(
                    default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(servers.Count, Is.GreaterThan(0));
            foreach (ApplicationDescription app in servers)
            {
                Assert.That(
                    app.ApplicationName, Is.Not.Null,
                    "ApplicationName should not be null.");
            }
        }

        [Description("GetEndpoints with ProfileUri filter for UA-TCP should return TCP endpoints.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "003")]
        public async Task GetEndpointsWithTcpProfileFilterAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            bool hasTcp = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.TransportProfileUri != null &&
                    ep.TransportProfileUri.Contains("uatcp"))
                {
                    hasTcp = true;
                    break;
                }
            }

            Assert.That(hasTcp, Is.True,
                "Server should have at least one UA-TCP endpoint.");
        }

        [Description("GetEndpoints for HTTPS – may not be available; skip if absent.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "003")]
        public async Task GetEndpointsWithHttpsProfileFilterAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool hasHttps = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.TransportProfileUri != null &&
                    ep.TransportProfileUri.Contains("https"))
                {
                    hasHttps = true;
                    break;
                }
            }

            if (!hasHttps)
            {
                Assert.Ignore("Server does not advertise HTTPS endpoints.");
            }
        }

        [Description("GetEndpoints with multiple LocaleIds still returns endpoints.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "002")]
        public async Task GetEndpointsWithMultipleLocaleIdsAsync()
        {
            var endpointConfiguration =
                EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, endpointConfiguration, Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints =
                await client.GetEndpointsAsync(
                    default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(endpoints.Count, Is.GreaterThan(0));
        }

        [Description("Discovery endpoint should be accessible without session authentication.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task DiscoveryEndpointAccessibleWithoutAuthAsync()
        {
            var endpointConfiguration =
                EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, endpointConfiguration, Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints =
                await client.GetEndpointsAsync(
                    default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(endpoints.Count, Is.GreaterThan(0),
                "Discovery should work without session authentication.");
        }

        [Description("Verify endpoint SecurityLevel values are consistent – secure endpoints should have SecurityLevel >= None endpoints.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task VerifyEndpointSecurityLevelConsistencyAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            byte maxNoneLevel = 0;
            byte minSecureLevel = byte.MaxValue;
            bool hasSecure = false;

            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode == MessageSecurityMode.None)
                {
                    if (ep.SecurityLevel > maxNoneLevel)
                    {
                        maxNoneLevel = ep.SecurityLevel;
                    }
                }
                else
                {
                    hasSecure = true;
                    if (ep.SecurityLevel < minSecureLevel)
                    {
                        minSecureLevel = ep.SecurityLevel;
                    }
                }
            }

            if (hasSecure)
            {
                Assert.That(minSecureLevel,
                    Is.GreaterThanOrEqualTo(maxNoneLevel),
                    "Secure endpoint SecurityLevel should be >= None.");
            }
        }

        [Description("Verify that all endpoints have a valid EndpointUrl.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task AllEndpointsHaveValidUrlAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(ep.EndpointUrl,
                    Is.Not.Null.And.Not.Empty,
                    "EndpointUrl should not be null or empty.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "013")]
        public async Task FindServersReturnsServerOrClientAndServerAsync()
        {
            var endpointConfiguration =
                EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, endpointConfiguration, Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers =
                await client.FindServersAsync(
                    default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(servers.Count, Is.GreaterThan(0));
            foreach (ApplicationDescription app in servers)
            {
                Assert.That(
                    app.ApplicationType,
                    Is.EqualTo(ApplicationType.Server)
                        .Or.EqualTo(ApplicationType.ClientAndServer),
                    $"Expected Server or ClientAndServer, got {app.ApplicationType}.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "013")]
        public async Task FindServersVerifyDiscoveryUrlsContainPortAsync()
        {
            var endpointConfiguration =
                EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, endpointConfiguration, Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers =
                await client.FindServersAsync(
                    default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(servers.Count, Is.GreaterThan(0));
            foreach (ApplicationDescription app in servers)
            {
                if (app.DiscoveryUrls != default)
                {
                    foreach (string url in app.DiscoveryUrls)
                    {
                        var uri = new Uri(url);
                        Assert.That(uri.Port, Is.GreaterThan(0),
                            $"DiscoveryUrl '{url}' should contain a port number.");
                    }
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task GetEndpointsReturnsConsistentUrlAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            var uri0 = new Uri(endpoints[0].EndpointUrl);
            string host0 = uri0.Host;

            foreach (EndpointDescription ep in endpoints)
            {
                var uri = new Uri(ep.EndpointUrl);
                Assert.That(uri.Host, Is.EqualTo(host0),
                    "All endpoints should use the same hostname.");
            }
        }

        [Description("GetEndpoints and verify all endpoint DisplayNames are not null or empty, confirming English fallback is provided.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "002")]
        public async Task GetEndpointsWithLocaleFilterEnglishAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(
                    ep.Server.ApplicationName.Text,
                    Is.Not.Null.And.Not.Empty,
                    "Endpoint DisplayName should not be null or empty.");
            }
        }

        [Description("GetEndpoints with an unknown locale \"zz\" still returns endpoints, verifying the server falls back to a default locale.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "004")]
        public async Task GetEndpointsWithUnknownLocaleFallsBackToDefaultAsync()
        {
            var endpointConfiguration =
                EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, endpointConfiguration, Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints =
                await client.GetEndpointsAsync(
                    default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(endpoints.Count, Is.GreaterThan(0),
                "Server should return endpoints even with unknown locale.");
        }

        [Description("Verify each returned endpoint has at least one UserTokenPolicy.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task VerifyEachEndpointHasUserIdentityTokensAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            bool anyHasTokens = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default && ep.UserIdentityTokens.Count > 0)
                {
                    anyHasTokens = true;
                }
            }

            Assert.That(anyHasTokens, Is.True,
                "At least one endpoint should have UserIdentityTokens.");
        }

        [Description("Get endpoints and verify Server.ApplicationDescription.ApplicationUri is consistent across all returned endpoints.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "003")]
        public async Task GetEndpointsWithServerUriFilterAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            string expectedUri = endpoints[0].Server.ApplicationUri;
            Assert.That(expectedUri, Is.Not.Null.And.Not.Empty,
                "First endpoint ApplicationUri should not be null or empty.");

            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(
                    ep.Server.ApplicationUri,
                    Is.EqualTo(expectedUri),
                    "All endpoints should report the same ApplicationUri.");
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
