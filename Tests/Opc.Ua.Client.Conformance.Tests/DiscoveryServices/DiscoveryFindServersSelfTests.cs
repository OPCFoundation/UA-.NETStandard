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

namespace Opc.Ua.Client.Conformance.Tests
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
        [Property("ConformanceUnit", "Discovery Find Servers Self")]
        [Property("Tag", "002")]
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
        [Property("ConformanceUnit", "Discovery Find Servers Self")]
        [Property("Tag", "005")]
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
        [Property("ConformanceUnit", "Discovery Find Servers Self")]
        [Property("Tag", "008")]
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
        [Property("ConformanceUnit", "Discovery Find Servers Self")]
        [Property("Tag", "009")]
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
        [Property("ConformanceUnit", "Discovery Find Servers Self")]
        [Property("Tag", "010")]
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
        [Property("ConformanceUnit", "Discovery Find Servers Self")]
        [Property("Tag", "Err-001")]
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
        [Property("ConformanceUnit", "Discovery Find Servers Self")]
        [Property("Tag", "Err-002")]
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
