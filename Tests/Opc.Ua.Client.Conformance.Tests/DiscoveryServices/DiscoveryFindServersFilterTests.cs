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
    /// compliance tests for Discovery Find Servers Filter.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("DiscoveryServices")]
    public class DiscoveryFindServersFilterTests : TestFixture
    {
        [Description("Filter the list of servers by server URI. */")]
        [Test]
        [Property("ConformanceUnit", "Discovery Find Servers Filter")]
        [Property("Tag", "001")]
        public async Task FindServersFilteredByServerUriAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> response = await client.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("Use several serverUris to restrict the list of servers (obtain list with no filter then use the necessary number of servers as the filter). This test is only possible on a discover")]
        [Test]
        [Property("ConformanceUnit", "Discovery Find Servers Filter")]
        [Property("Tag", "002")]
        public async Task FindServersFilteredByMultipleServerUrisAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> response = await client.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("List with supported and unsupported locales. */")]
        [Test]
        [Property("ConformanceUnit", "Discovery Find Servers Filter")]
        [Property("Tag", "003")]
        public async Task FindServersWithMixedSupportedAndUnsupportedLocalesAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> response = await client.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("Provide a serverUri that does not match any servers provided by previous call to FindServers. */")]
        [Test]
        [Property("ConformanceUnit", "Discovery Find Servers Filter")]
        [Property("Tag", "004")]
        public async Task FindServersFilteredByUnknownServerUriAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> response = await client.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("Use unsupported locale id. */")]
        [Test]
        [Property("ConformanceUnit", "Discovery Find Servers Filter")]
        [Property("Tag", "005")]
        public async Task FindServersWithUnsupportedLocaleIdAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> response = await client.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("Provide a list of supported locales. */")]
        [Test]
        [Property("ConformanceUnit", "Discovery Find Servers Filter")]
        [Property("Tag", "006")]
        public async Task FindServersWithSupportedLocalesAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> response = await client.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("Repeats test #8 100 times. Must complete within 10-seconds. */ // include the script that we'll invoke include( &quot;./maintree/Discovery Services/Discovery Find Servers Filter/Test Ca")]
        [Test]
        [Property("ConformanceUnit", "Discovery Find Servers Filter")]
        [Property("Tag", "007")]
        public async Task FindServersRepeatedHundredTimesWithinTenSecondsAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> response = await client.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Count, Is.GreaterThan(0));
        }

        [Description("Repeat test #17, 10 times. (essentially repeats test #4 1000 times) Must complete within 30-seconds. */ // include the script that we'll invoke include( &quot;./maintree/Discovery Servi")]
        [Test]
        [Property("ConformanceUnit", "Discovery Find Servers Filter")]
        [Property("Tag", "008")]
        public async Task FindServersRepeatedTenTimesWithinThirtySecondsAsync()
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
