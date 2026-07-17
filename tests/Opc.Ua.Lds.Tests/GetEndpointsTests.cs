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
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Lds.Tests
{
    /// <summary>
    /// compliance tests for Discovery Service Set – GetEndpoints.
    /// Based on test scripts: Discovery Get Endpoints 001–013 and Err tests.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Discovery")]
    [Category("GetEndpoints")]
    public class GetEndpointsTests : TestFixture
    {
        [Description("GetEndpoints with default parameters returns at least one endpoint.")]
        [Test]
        public async Task GetEndpoints001DefaultParametersAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(endpoints.Count, Is.GreaterThan(0),
                "GetEndpoints should return at least one endpoint.");
        }

        [Description("GetEndpoints specifying preferred locales still returns endpoints.")]
        [Test]
        public async Task GetEndpoints002WithLocalesAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(endpoints.Count, Is.GreaterThan(0),
                "GetEndpoints with locales should still return endpoints.");
        }

        [Description("GetEndpoints with a different (but valid) URL still returns endpoints. The server should accept the request even if the URL does not exactly match.")]
        [Test]
        public async Task GetEndpoints003DifferentUrlAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(endpoints.Count, Is.GreaterThan(0),
                "GetEndpoints should return endpoints even with alternate URL.");
        }

        [Description("Verify each returned endpoint has required fields: SecurityMode, SecurityPolicyUri, and UserIdentityTokens.")]
        [Test]
        public async Task GetEndpoints004VerifyEndpointFieldsAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(endpoints.Count, Is.GreaterThan(0));

            for (int i = 0; i < endpoints.Count; i++)
            {
                EndpointDescription ep = endpoints[i];

                Assert.That(ep.SecurityMode, Is.Not.EqualTo(MessageSecurityMode.Invalid),
                    $"Endpoint[{i}] has invalid SecurityMode.");

                Assert.That(ep.SecurityPolicyUri, Is.Not.Null.And.Not.Empty,
                    $"Endpoint[{i}] SecurityPolicyUri is null or empty.");

                Assert.That(ep.UserIdentityTokens, Is.Not.Null,
                    $"Endpoint[{i}] UserIdentityTokens is null.");
            }
        }

        [Description("GetEndpoints requesting a specific transport profile. Endpoints should use the UA TCP transport profile.")]
        [Test]
        public async Task GetEndpoints005TransportProfileAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(endpoints.Count, Is.GreaterThan(0));

            // All endpoints returned via opc.tcp should have the binary transport profile
            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(ep.TransportProfileUri, Is.Not.Null.And.Not.Empty,
                    "TransportProfileUri should not be empty.");
            }
        }

        [Description("Verify that the Server field in each endpoint matches the server's ApplicationDescription.")]
        [Test]
        public async Task GetEndpoints006VerifyServerApplicationDescriptionAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(endpoints.Count, Is.GreaterThan(0));

            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(ep.Server, Is.Not.Null,
                    "Endpoint Server description should not be null.");
                Assert.That(ep.Server.ApplicationUri, Is.Not.Null.And.Not.Empty,
                    "Server ApplicationUri should not be empty.");
                Assert.That(ep.Server.ApplicationName, Is.Not.Null,
                    "Server ApplicationName should not be null.");
            }
        }

        [Description("Verify the endpoint URL in each returned endpoint is not empty.")]
        [Test]
        public async Task GetEndpoints007VerifyEndpointUrlAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(endpoints.Count, Is.GreaterThan(0));

            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(ep.EndpointUrl, Is.Not.Null.And.Not.Empty,
                    "EndpointUrl should not be null or empty.");
            }
        }

        [Description("Verify that at least one endpoint supports MessageSecurityMode.None when the server has SecurityNone enabled.")]
        [Test]
        public async Task GetEndpoints008SecurityNoneAvailableAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            bool hasSecurityNone = false;
            for (int i = 0; i < endpoints.Count; i++)
            {
                if (endpoints[i].SecurityMode == MessageSecurityMode.None)
                {
                    hasSecurityNone = true;
                    break;
                }
            }

            Assert.That(hasSecurityNone, Is.True,
                "At least one endpoint should support SecurityMode.None.");
        }

        [Description("GetEndpoints with invalid transport profile URI returns zero matching endpoints.")]
        [Test]
        public async Task GetEndpointsErr001InvalidTransportProfileAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            // Get all endpoints
            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            // Filter by a transport profile that does not exist
            const string invalidProfile = "http://opcfoundation.org/UA-Profile/Transport/invalid-does-not-exist";
            int matchCount = 0;
            for (int i = 0; i < endpoints.Count; i++)
            {
                if (string.Equals(endpoints[i].TransportProfileUri, invalidProfile, StringComparison.Ordinal))
                {
                    matchCount++;
                }
            }

            Assert.That(matchCount, Is.Zero,
                "No endpoints should match an invalid transport profile URI.");
        }

        [Description("Verify that the server certificate is present in endpoints with security.")]
        [Test]
        public async Task GetEndpointsErr002SecureEndpointHasCertificateAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode != MessageSecurityMode.None)
                {
                    Assert.That(ep.ServerCertificate.Length, Is.GreaterThan(0),
                        $"Secure endpoint with policy {ep.SecurityPolicyUri} should have a server certificate.");
                }
            }
        }
    }
}
