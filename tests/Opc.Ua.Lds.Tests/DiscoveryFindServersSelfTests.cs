// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Lds.Tests
{
    /// <summary>
    /// compliance tests for Discovery Find Servers Self.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("DiscoveryServices")]
    public class DiscoveryFindServersSelfTests : TestFixture
    {
        [Description("Provide an endpoint description Url with a hostname not known to the server.")]
        [Test]
        public async Task FindServersWithUnknownHostnameAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> response = await client.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("Provide a list of locales not conforming to RFC 3066. */")]
        [Test]
        public async Task FindServersWithNonRfc3066LocalesAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> response = await client.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("Provide an invalid endpoint URL (string, but syntactically not a URL). */")]
        [Test]
        public async Task FindServersWithInvalidEndpointUrlAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> response = await client.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("Repeats test 008, 100 times. Must complete within 10-seconds. */")]
        [Test]
        public async Task FindServersRepeatedHundredTimesWithinTenSecondsAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> response = await client.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("The following test-case covers a multi-homed PC. Call FindServers to obtain a list of all endpoints. Identify if the endpoints returned indicate that the Server is on a multi-homed")]
        [Test]
        public async Task FindServersOnMultiHomedPcAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> response = await client.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("EndpointUrl=null")]
        [Test]
        public async Task FindServersWithNullEndpointUrlAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> response = await client.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("Include authenticationToken in requestHeader.")]
        [Test]
        public async Task FindServersWithAuthenticationTokenInRequestHeaderAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> response = await client.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }
    }
}
