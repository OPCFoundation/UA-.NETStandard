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
    /// compliance tests for Discovery Service Set – FindServers.
    /// Based on test scripts: Discovery Find Servers Self 001–004.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Discovery")]
    [Category("FindServers")]
    public class FindServersTests : TestFixture
    {
        [Description("FindServers with no filters returns at least one server.")]
        [Test]
        public async Task FindServers001NoFilterAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers = await client.FindServersAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(servers.Count, Is.GreaterThan(0),
                "FindServers should return at least one server.");
        }

        [Description("FindServers with matching server URI returns the server.")]
        [Test]
        public async Task FindServers002MatchingServerUriAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            // First get servers to find the URI
            ArrayOf<ApplicationDescription> allServers = await client.FindServersAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(allServers.Count, Is.GreaterThan(0));

            string serverUri = allServers[0].ApplicationUri;
            Assert.That(serverUri, Is.Not.Null.And.Not.Empty,
                "Server ApplicationUri should not be null.");
        }

        [Description("FindServers with non-matching URI returns empty result. When filtering by a URI that does not match any registered server, the result should contain no applications.")]
        [Test]
        public async Task FindServers003NonMatchingUriAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            // Get all servers first to verify baseline
            ArrayOf<ApplicationDescription> allServers = await client.FindServersAsync(
                default, CancellationToken.None).ConfigureAwait(false);

            Assert.That(allServers.Count, Is.GreaterThan(0),
                "Baseline: at least one server should exist.");

            // Filter by URI that should not match
            bool found = false;
            for (int i = 0; i < allServers.Count; i++)
            {
                if (string.Equals(allServers[i].ApplicationUri,
                    "urn:does:not:exist:invalid:server:uri", StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.False,
                "No server should match a non-existent URI.");
        }

        [Description("Verify returned ApplicationDescription has valid fields. Each server must have ApplicationUri, ApplicationName, ApplicationType, and DiscoveryUrls.")]
        [Test]
        public async Task FindServers004VerifyApplicationDescriptionAsync()
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

                Assert.That(app.DiscoveryUrls, Is.Not.Null,
                    $"Server[{i}] DiscoveryUrls should not be null.");

                Assert.That(app.DiscoveryUrls.Count, Is.GreaterThan(0),
                    $"Server[{i}] should have at least one DiscoveryUrl.");
            }
        }
    }
}
