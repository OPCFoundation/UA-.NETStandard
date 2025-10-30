/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    /// <summary>
    /// Tests for the UANodeSet helper.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ClientTests
    {
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
        }

        [SetUp]
        protected void SetUp()
        {
        }

        [TearDown]
        protected void TearDown()
        {
        }

        /// <summary>
        /// Ensure that use of OriginalString preserves a scope id of a IPv6 address.
        /// </summary>
        [Test]
        [TestCase("opc.tcp://another.server.com:4840/CustomEndpoint")]
        [TestCase("opc.tcp://10.11.222.12:62541/ReferenceServer")]
        [TestCase("opc.tcp://[2003:d9:1f40:bc00:a115:d9c7:6134:f347]:4840/AnEndpoint")]
        [TestCase("opc.tcp://[fe80::280:deff:fa02:c63e%eth0]:4840/")]
        [TestCase("opc.tcp://[fe80::de39:6fff:feae:c78%12]:4840/Endpoint1")]
        public void DiscoveryEndPointUrls(string urlString)
        {
            var uri = new Uri(urlString);
            Assert.True(uri.IsWellFormedOriginalString());

            var uriBuilder = new UriBuilder
            {
                Scheme = uri.Scheme,
                Host = uri.IdnHost,
                Port = uri.Port,
                Path = uri.AbsolutePath
            };

            Assert.AreEqual(uri.OriginalString, uriBuilder.Uri.OriginalString);
        }

        /// <summary>
        /// Ensure that URIs with and without trailing slashes are normalized to be identical,
        /// while preserving IPv6 scope IDs.
        /// </summary>
        [Test]
        [TestCase("opc.tcp://hostname:4840/", "opc.tcp://hostname:4840", "opc.tcp://hostname:4840/")]
        [TestCase("opc.tcp://hostname:4840/path", "opc.tcp://hostname:4840/path", 
            "opc.tcp://hostname:4840/path")]
        [TestCase("opc.tcp://[fe80::280:deff:fa02:c63e%eth0]:4840/", 
            "opc.tcp://[fe80::280:deff:fa02:c63e%eth0]:4840", 
            "opc.tcp://[fe80::280:deff:fa02:c63e%eth0]:4840/")]
        [TestCase("opc.tcp://[fe80::de39:6fff:feae:c78%12]:4840/Endpoint1", 
            "opc.tcp://[fe80::de39:6fff:feae:c78%12]:4840/Endpoint1", 
            "opc.tcp://[fe80::de39:6fff:feae:c78%12]:4840/Endpoint1")]
        public void DiscoveryEndPointUrlNormalization(string url1, string url2, string expectedNormalized)
        {
            var uri1 = new Uri(url1);
            var uri2 = new Uri(url2);

            // Call the internal GetNormalizedEndpointUrl method
            var normalized1 = DiscoveryClient.GetNormalizedEndpointUrl(uri1);
            var normalized2 = DiscoveryClient.GetNormalizedEndpointUrl(uri2);

            // Both URIs should normalize to the same value
            Assert.AreEqual(expectedNormalized, normalized1, "First URI should normalize correctly");
            Assert.AreEqual(expectedNormalized, normalized2, "Second URI should normalize correctly");
            Assert.AreEqual(normalized1, normalized2, "Both URIs should normalize to the same value");

            // Verify IPv6 scope IDs are preserved
            if (url1.Contains("%"))
            {
                Assert.IsTrue(normalized1.Contains("%"), "IPv6 scope ID should be preserved");
            }
        }

        [Test]
        public void ValidateAppConfigWithoutAppCert()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var appConfig = new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "Test",
                ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier(),
                    TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" }
                }
            };
            NUnit.Framework.Assert.DoesNotThrowAsync(() =>
                appConfig.ValidateAsync(ApplicationType.Client));
        }
    }
}
