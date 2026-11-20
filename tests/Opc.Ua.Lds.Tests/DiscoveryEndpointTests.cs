/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Lds.Tests
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
        public async Task GetEndpointsWithTransportProfileFilterAsync()
        {
            ArrayOf<EndpointDescription> endpoints =
                await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            bool hasTcp = false;
            foreach (EndpointDescription e in endpoints)
            {
                if (e.TransportProfileUri != null &&
                    e.TransportProfileUri.Contains("uatcp", StringComparison.Ordinal))
                {
                    hasTcp = true;
                    break;
                }
            }

            Assert.That(hasTcp, Is.True,
                "Expected at least one UA-TCP endpoint.");
        }

        [Test]
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
        public async Task GetEndpointsVerifyAnonymousTokenAvailableAsync()
        {
            ArrayOf<EndpointDescription> endpoints =
                await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            bool hasAnonymous = false;
            foreach (EndpointDescription e in endpoints)
            {
                if (e.UserIdentityTokens != default!)
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
        public async Task GetEndpointsVerifyUsernameTokenAvailableAsync()
        {
            ArrayOf<EndpointDescription> endpoints =
                await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            bool hasUsername = false;
            foreach (EndpointDescription e in endpoints)
            {
                if (e.UserIdentityTokens != default!)
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
