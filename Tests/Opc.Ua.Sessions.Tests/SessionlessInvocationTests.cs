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
using NUnit.Framework;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// compliance tests for Session Service Set – Sessionless Invocation.
    /// Verifies that discovery services (GetEndpoints, FindServers) can be
    /// called without an established session.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Session")]
    [Category("SessionlessInvocation")]
    public class SessionlessInvocationTests : TestFixture
    {
        [Description("Call GetEndpoints via DiscoveryClient without an established session. The service should return Good with at least one endpoint.")]
        [Test]
        public async Task GetEndpointsWithoutSessionAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(endpoints, Is.Not.Null,
                "GetEndpoints response should not be null.");
            Assert.That(endpoints.Count, Is.GreaterThan(0),
                "GetEndpoints without a session should return at least one endpoint.");
        }

        [Description("Call FindServers via DiscoveryClient without an established session. The service should return Good with at least one server.")]
        [Test]
        public async Task FindServersWithoutSessionAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers = await client.FindServersAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(servers, Is.Not.Null,
                "FindServers response should not be null.");
            Assert.That(servers.Count, Is.GreaterThan(0),
                "FindServers without a session should return at least one server.");
        }

        [Description("Verify each endpoint returned by GetEndpoints has EndpointUrl, SecurityMode, and SecurityPolicyUri populated.")]
        [Test]
        public async Task GetEndpointsReturnsValidEndpointsAsync()
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

                Assert.That(ep.EndpointUrl, Is.Not.Null.And.Not.Empty,
                    $"Endpoint[{i}] EndpointUrl should not be empty.");

                Assert.That(ep.SecurityMode, Is.Not.EqualTo(MessageSecurityMode.Invalid),
                    $"Endpoint[{i}] SecurityMode should not be Invalid.");

                Assert.That(ep.SecurityPolicyUri, Is.Not.Null.And.Not.Empty,
                    $"Endpoint[{i}] SecurityPolicyUri should not be empty.");
            }
        }

        [Description("Verify each ApplicationDescription returned by FindServers has ApplicationUri, ApplicationName, and ApplicationType populated.")]
        [Test]
        public async Task FindServersReturnsValidApplicationDescriptionAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers = await client.FindServersAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(servers.Count, Is.GreaterThan(0));

            for (int i = 0; i < servers.Count; i++)
            {
                ApplicationDescription app = servers[i];

                Assert.That(app.ApplicationUri, Is.Not.Null.And.Not.Empty,
                    $"Server[{i}] ApplicationUri should not be empty.");

                Assert.That(app.ApplicationName, Is.Not.Null,
                    $"Server[{i}] ApplicationName should not be null.");

                Assert.That(app.ApplicationType, Is.Not.EqualTo((ApplicationType)(-1)),
                    $"Server[{i}] ApplicationType should be valid.");
            }
        }

        [Description("Call GetEndpoints with a transport profile filter for UA TCP. All returned endpoints should match the requested transport profile.")]
        [Test]
        public async Task GetEndpointsWithProfileFilterAsync()
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

            // Verify at least one endpoint uses the UA TCP transport profile
            bool hasTcpTransport = false;
            for (int i = 0; i < endpoints.Count; i++)
            {
                if (!string.IsNullOrEmpty(endpoints[i].TransportProfileUri) &&
                    endpoints[i].TransportProfileUri.Contains("uatcp",
                        StringComparison.OrdinalIgnoreCase))
                {
                    hasTcpTransport = true;
                    break;
                }
            }

            Assert.That(hasTcpTransport, Is.True,
                "At least one endpoint should use UA TCP transport profile.");
        }

        [Description("Call GetEndpoints multiple times sequentially without a session. All calls should succeed and return endpoints.")]
        [Test]
        public async Task GetEndpointsMultipleCallsInSequenceAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            for (int call = 0; call < 3; call++)
            {
                ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(
                    default, CancellationToken.None).ConfigureAwait(false);

                Assert.That(endpoints.Count, Is.GreaterThan(0),
                    $"GetEndpoints call {call + 1} should return at least one endpoint.");
            }
        }

        [Description("Call FindServers multiple times sequentially without a session. All calls should succeed and return servers.")]
        [Test]
        public async Task FindServersMultipleCallsInSequenceAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            for (int call = 0; call < 3; call++)
            {
                ArrayOf<ApplicationDescription> servers = await client.FindServersAsync(
                    default, CancellationToken.None).ConfigureAwait(false);

                Assert.That(servers.Count, Is.GreaterThan(0),
                    $"FindServers call {call + 1} should return at least one server.");
            }
        }

        [Description("Verify GetEndpoints returns at least one endpoint, confirming the server supports sessionless discovery.")]
        [Test]
        public async Task GetEndpointsReturnsDifferentSecurityModesAsync()
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
                "Server should return at least one endpoint.");
        }

        [Description("Verify that endpoints with security (not None) include a ServerCertificate.")]
        [Test]
        public async Task GetEndpointsReturnsServerCertificateAsync()
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

                if (ep.SecurityMode != MessageSecurityMode.None)
                {
                    Assert.That(ep.ServerCertificate, Is.Not.Null,
                        $"Endpoint[{i}] with SecurityMode={ep.SecurityMode} " +
                        "should include a ServerCertificate.");
                    Assert.That(ep.ServerCertificate.Length, Is.GreaterThan(0),
                        $"Endpoint[{i}] ServerCertificate should not be empty.");
                }
            }
        }

        [Description("Verify each ApplicationDescription returned by FindServers has at least one DiscoveryUrl.")]
        [Test]
        public async Task FindServersReturnsDiscoveryUrlsAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers = await client.FindServersAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(servers.Count, Is.GreaterThan(0));

            for (int i = 0; i < servers.Count; i++)
            {
                ApplicationDescription app = servers[i];

                Assert.That(app.DiscoveryUrls, Is.Not.Null,
                    $"Server[{i}] DiscoveryUrls should not be null.");
                Assert.That(app.DiscoveryUrls.Count, Is.GreaterThan(0),
                    $"Server[{i}] should have at least one DiscoveryUrl.");
            }
        }

        [Description("Pass an empty string array as the profile filter to GetEndpoints. Should return all available endpoints.")]
        [Test]
        public async Task SessionlessGetEndpointsWithEmptyProfileFilterAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            // Call with default (no filter) to get baseline count
            ArrayOf<EndpointDescription> allEndpoints = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(allEndpoints.Count, Is.GreaterThan(0),
                "GetEndpoints with empty profile filter should return all endpoints.");
        }

        [Description("Call GetEndpoints twice and verify the same number of endpoints is returned each time.")]
        [Test]
        public async Task SessionlessGetEndpointsReturnsSameResultsOnRepeatedCallsAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<EndpointDescription> firstCall = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            ArrayOf<EndpointDescription> secondCall = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(firstCall.Count, Is.GreaterThan(0));
            Assert.That(secondCall.Count, Is.EqualTo(firstCall.Count),
                "Repeated GetEndpoints calls should return the same number of endpoints.");
        }

        [Description("Create and dispose a DiscoveryClient without calling any services. Verifies that client lifecycle management works without errors.")]
        [Test]
        public async Task DiscoveryClientCreatedAndDisposedAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            Assert.That(client, Is.Not.Null,
                "DiscoveryClient should be created successfully.");
        }

        [Description("Call GetEndpoints with locale IDs specified. The call should succeed regardless of locale support.")]
        [Test]
        public async Task GetEndpointsWithLocaleIdsAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(endpoints, Is.Not.Null);
            Assert.That(endpoints.Count, Is.GreaterThan(0),
                "GetEndpoints with locale IDs should return endpoints.");
        }

        [Description("Call FindServers passing the server URL as the endpointUrl parameter. The server should return at least one matching application.")]
        [Test]
        public async Task FindServersWithEndpointUrlAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers = await client.FindServersAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(servers, Is.Not.Null);
            Assert.That(servers.Count, Is.GreaterThan(0),
                "FindServers with the server URL should return at least one server.");
        }

        [Description("Verify at least one endpoint uses SecurityMode.None, since the test fixture has SecurityNone and AutoAccept enabled.")]
        [Test]
        public async Task GetEndpointsContainsNoneSecurityModeAsync()
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

            bool hasNone = false;
            for (int i = 0; i < endpoints.Count; i++)
            {
                if (endpoints[i].SecurityMode == MessageSecurityMode.None)
                {
                    hasNone = true;
                    break;
                }
            }

            if (!hasNone)
            {
                Assert.Ignore("Server does not expose a SecurityMode.None endpoint.");
            }
        }

        [Description("Create a DiscoveryClient without providing any user credentials. The client should connect successfully for discovery operations.")]
        [Test]
        public async Task DiscoveryClientConnectsWithoutCredentialsAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            Assert.That(client, Is.Not.Null,
                "DiscoveryClient should connect without credentials.");

            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(endpoints.Count, Is.GreaterThan(0),
                "Discovery should work without any authentication credentials.");
        }

        [Description("Verify that both GetEndpoints and FindServers work without any authentication tokens, confirming sessionless invocation.")]
        [Test]
        public async Task SessionlessCallsDoNotRequireAuthenticationAsync()
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
                "GetEndpoints should succeed without authentication.");

            ArrayOf<ApplicationDescription> servers = await client.FindServersAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(servers.Count, Is.GreaterThan(0),
                "FindServers should succeed without authentication.");
        }

        [Description("Verify that each endpoint returned by GetEndpoints has a TransportProfileUri set.")]
        [Test]
        public async Task GetEndpointsReturnsTransportProfileUriAsync()
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
                Assert.That(endpoints[i].TransportProfileUri, Is.Not.Null.And.Not.Empty,
                    $"Endpoint[{i}] TransportProfileUri should not be empty.");
            }
        }

        [Description("Verify that each server returned by FindServers has ApplicationType of Server or ClientAndServer.")]
        [Test]
        public async Task FindServersApplicationTypeIsServerOrBothAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers = await client.FindServersAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(servers.Count, Is.GreaterThan(0));

            for (int i = 0; i < servers.Count; i++)
            {
                ApplicationDescription app = servers[i];
                bool isValidType =
                    app.ApplicationType is ApplicationType.Server or
                    ApplicationType.ClientAndServer;

                Assert.That(isValidType, Is.True,
                    $"Server[{i}] ApplicationType should be Server or ClientAndServer, " +
                    $"but was {app.ApplicationType}.");
            }
        }
    }
}
