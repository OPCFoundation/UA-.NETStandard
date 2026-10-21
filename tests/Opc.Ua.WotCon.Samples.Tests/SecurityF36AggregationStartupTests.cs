/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.Linq;
using System.Net;
using AggregationServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.WotCon.Samples.Tests
{
    /// <summary>
    /// Checks the actual listen URL options composed by the sample without starting services or creating PKI.
    /// </summary>
    [TestFixture]
    public sealed class SecurityF36AggregationStartupTests
    {
        /// <summary>
        /// The explicitly insecure demonstration's effective default bind input is a numeric loopback address.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public void SecurityF36DefaultAggregationHostRemainsLoopback()
        {
            using IHost host = AggregationServerHost.Build(new AggregationServerOptions());

            AssertNumericLoopbackListenInput(host);
        }

        /// <summary>
        /// The no-argument CLI path uses the same safe effective bind input as the options path.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public void SecurityF36CommandLineFallbackRemainsLoopback()
        {
            using IHost host = AggregationServerHost.BuildFromCommandLine([]);

            AssertNumericLoopbackListenInput(host);
        }

        /// <summary>
        /// Explicit numeric, IPv6 and named remote hosts retain the operator's intentional configuration.
        /// </summary>
        [TestCase("127.0.0.1", UriHostNameType.IPv4)]
        [TestCase("::1", UriHostNameType.IPv6)]
        [TestCase("192.0.2.40", UriHostNameType.IPv4)]
        [TestCase("device.example", UriHostNameType.Dns)]
        [Category("SecurityRegression")]
        public void SecurityF36ExplicitBindHostIsPreserved(string hostName, UriHostNameType hostType)
        {
            const int Port = 62551;
            using IHost optionsHost = AggregationServerHost.Build(new AggregationServerOptions
            {
                Host = hostName,
                Port = Port
            });
            using IHost commandLineHost = AggregationServerHost.BuildFromCommandLine(
                ["--host", hostName, "--port", Port.ToString(CultureInfo.InvariantCulture)]);

            foreach (IHost host in new[] { optionsHost, commandLineHost })
            {
                Uri endpoint = ListenInput(host);
                Assert.That(endpoint.HostNameType, Is.EqualTo(hostType));
                Assert.That(endpoint.IdnHost.Trim('[', ']'), Is.EqualTo(hostName));
                Assert.That(endpoint.Port, Is.EqualTo(Port));
                Assert.That(endpoint.AbsolutePath, Is.EqualTo("/AggregationServer"));
            }
        }

        /// <summary>
        /// A complete endpoint override still takes precedence over the host and port fallback.
        /// </summary>
        [TestCase("opc.tcp://device.example:62552/custom")]
        [TestCase("opc.tcp://[::1]:62552/custom")]
        [Category("SecurityRegression")]
        public void SecurityF36ExplicitEndpointOverrideIsPreserved(string endpoint)
        {
            using IHost optionsHost = AggregationServerHost.Build(new AggregationServerOptions
            {
                EndpointUrl = endpoint,
                Host = "ignored.example",
                Port = 62553
            });
            using IHost commandLineHost = AggregationServerHost.BuildFromCommandLine(
                ["--endpoint", endpoint, "--host", "ignored.example", "--port", "62553"]);

            foreach (IHost host in new[] { optionsHost, commandLineHost })
            {
                OpcUaServerOptions options = host.Services.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
                Assert.That(options.EndpointUrls, Is.EqualTo([endpoint]));
            }
        }

        private static void AssertNumericLoopbackListenInput(IHost host)
        {
            Uri endpoint = ListenInput(host);

            Assert.That(endpoint.Scheme, Is.EqualTo("opc.tcp"));
            Assert.That(endpoint.HostNameType, Is.EqualTo(UriHostNameType.IPv4));
            Assert.That(IPAddress.TryParse(endpoint.Host, out IPAddress? address), Is.True);
            Assert.That(address, Is.EqualTo(IPAddress.Loopback));
            Assert.That(address, Is.Not.EqualTo(IPAddress.Any).And.Not.EqualTo(IPAddress.IPv6Any));
            Assert.That(endpoint.Port, Is.EqualTo(62550));
            Assert.That(endpoint.AbsolutePath, Is.EqualTo("/AggregationServer"));
        }

        private static Uri ListenInput(IHost host)
        {
            OpcUaServerOptions options = host.Services.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
            Assert.That(options.EndpointUrls, Has.Count.EqualTo(1));
            return new Uri(options.EndpointUrls.Single(), UriKind.Absolute);
        }
    }
}
