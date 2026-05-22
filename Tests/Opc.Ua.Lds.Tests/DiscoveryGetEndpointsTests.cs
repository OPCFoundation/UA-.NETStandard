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
    /// compliance tests for Discovery Get Endpoints.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("DiscoveryServices")]
    public class DiscoveryGetEndpointsTests : TestFixture
    {
        [Description("Provide a list of supported locales. */")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "002")]
        public async Task GetEndpointsWithSupportedLocalesAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> response = await client.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("Invoke GetEndpoints with default parameters while specifying a list of transport ProfileUris to filter. How this test works: 1.) call getEndpoints using default parameters only 2.)")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "003")]
        public async Task GetEndpointsWithTransportProfileUrisFilterAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> response = await client.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("List with supported and unsupported locales. */")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "004")]
        public async Task GetEndpointsWithMixedSupportedAndUnsupportedLocalesAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> response = await client.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("Provide a list of locales not conforming to RFC 3066. */")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "005")]
        public async Task GetEndpointsWithNonRfc3066LocalesAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> response = await client.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("Provide an endpoint description Url with a hostname not known to the server. Service result = �Good�. Server returns a default EndpointUrl. */")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "008")]
        public async Task GetEndpointsWithUnknownHostnameReturnsDefaultAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> response = await client.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("Multiple hostnames defined on the computer, the certificate contains those hostnames. */")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "009")]
        public async Task GetEndpointsWithMultipleHostnamesInCertificateAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> response = await client.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("Provide an invalid endpoint URL (string, but syntactically not a URL).")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "010")]
        public async Task GetEndpointsWithInvalidEndpointUrlAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> response = await client.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("Unsupported profile URI. */")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "011")]
        public async Task GetEndpointsWithUnsupportedProfileUriAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> response = await client.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("Set endpointUrl = null.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "Err-001")]
        public async Task GetEndpointsWithNullEndpointUrlAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> response = await client.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }
    }
}
