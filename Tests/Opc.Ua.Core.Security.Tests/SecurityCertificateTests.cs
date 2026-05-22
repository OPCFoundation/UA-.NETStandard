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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Security.Certificates;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Core.Security.Tests
{
    /// <summary>
    /// compliance tests for server certificate validation,
    /// SAN fields, and secure endpoint requirements.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("SecurityCertificate")]
    public class SecurityCertificateTests : TestFixture
    {
        [Test]
        public async Task ServerCertificateHasValidDatesAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            Assert.That(cert.NotBefore, Is.LessThanOrEqualTo(DateTime.UtcNow),
                "Certificate NotBefore should be in the past.");
            Assert.That(cert.NotAfter, Is.GreaterThan(DateTime.UtcNow),
                "Certificate NotAfter should be in the future.");
        }

        [Test]
        public async Task ServerCertificateSanContainsApplicationUriAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            string appUri = endpoints[0].Server.ApplicationUri;

            // Parse SAN from the raw extension
            bool found = false;
            foreach (X509Extension ext in cert.Extensions)
            {
                // OID 2.5.29.17 is SubjectAlternativeName
                if (ext.Oid?.Value == "2.5.29.17")
                {
                    string formatted = ext.Format(true);
                    if (formatted.Contains(
                        appUri, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                    }
                }
            }

            if (!found)
            {
                Assert.Fail(
                    $"SAN does not contain ApplicationUri '{appUri}'.");
            }
        }

        [Test]
        public async Task ServerCertificateSanContainsHostnameAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            bool found = false;
            foreach (X509Extension ext in cert.Extensions)
            {
                if (ext.Oid?.Value is X509SubjectAltNameExtension.SubjectAltNameOid
                    or X509SubjectAltNameExtension.SubjectAltName2Oid)
                {
                    var san = new X509SubjectAltNameExtension(ext, ext.Critical);
                    foreach (string dns in san.DomainNames)
                    {
                        if (!string.IsNullOrEmpty(dns))
                        {
                            found = true;
                            break;
                        }
                    }
                }

                if (found)
                {
                    break;
                }
            }

            if (!found)
            {
                Assert.Fail("SAN does not contain any DNS names.");
            }
        }

        [Test]
        public async Task ServerHasAtLeastOneSecureEndpointAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool hasSecure = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode != MessageSecurityMode.None)
                {
                    hasSecure = true;
                    break;
                }
            }

            Assert.That(hasSecure, Is.True,
                "Server should have at least one secure endpoint.");
        }

        [Test]
        public async Task EachSecureEndpointHasRecognizedPolicyAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode != MessageSecurityMode.None)
                {
                    Assert.That(ep.SecurityPolicyUri,
                        Does.StartWith(SecurityPolicies.BaseUri),
                        $"Policy should start with OPC Foundation base URI: {ep.SecurityPolicyUri}");
                }
            }
        }

        [Test]
        public async Task ConnectWithTrustedCertSucceedsAsync()
        {
            // AutoAccept is true in the fixture, so trusted certs work
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription secureEp = null;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode != MessageSecurityMode.None)
                {
                    secureEp = ep;
                    break;
                }
            }

            if (secureEp == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            ISession session = await ClientFixture
                .ConnectAsync(ServerUrl, secureEp.SecurityPolicyUri)
                .ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task SecureEndpointCertificateKeyIsAdequateAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            int keySize;
            using (RSA rsa = cert.GetRSAPublicKey())
            using (ECDsa ecdsa = rsa is null ? cert.GetECDsaPublicKey() : null)
            {
                keySize = rsa?.KeySize ?? ecdsa?.KeySize ?? 0;
            }
            Assert.That(keySize, Is.GreaterThanOrEqualTo(256),
                "Certificate key size should be at least 256 bits.");
        }

        [Test]
        public async Task DefaultCert001CheckInitialCertificateStateAsync()
        {
            // Verify the server's application instance certificate
            // exists and is accessible via the endpoints
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert =
                GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail(
                    "No secure endpoint with certificate found.");
            }

            Assert.That(cert.Subject, Is.Not.Null.And.Not.Empty,
                "Default app certificate should have a Subject.");
            Assert.That(cert.HasPrivateKey, Is.False,
                "Public cert from endpoint should not expose " +
                "private key.");
        }

        [Test]
        public async Task DefaultCert002EstablishCommunicationAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription secureEp = null;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode != MessageSecurityMode.None)
                {
                    secureEp = ep;
                    break;
                }
            }

            if (secureEp == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            ISession session = await ClientFixture
                .ConnectAsync(ServerUrl, secureEp.SecurityPolicyUri)
                .ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True,
                    "Should connect using the default " +
                    "application certificate.");
            }
            finally
            {
                await session.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task DefaultCert003EnsureCurrentCertIsValidAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert =
                GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail(
                    "No secure endpoint with certificate found.");
            }

            Assert.That(cert.NotBefore,
                Is.LessThanOrEqualTo(DateTime.UtcNow),
                "Certificate should be currently valid (NotBefore).");
            Assert.That(cert.NotAfter,
                Is.GreaterThan(DateTime.UtcNow),
                "Certificate should be currently valid (NotAfter).");

            // Verify it has a reasonable key size
            int keySize;
            using (RSA rsa = cert.GetRSAPublicKey())
            using (ECDsa ecdsa = rsa is null ? cert.GetECDsaPublicKey() : null)
            {
                keySize = rsa?.KeySize ?? ecdsa?.KeySize ?? 0;
            }
            Assert.That(keySize, Is.GreaterThanOrEqualTo(2048),
                "Application certificate key size >= 2048.");
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

        private X509Certificate2 GetFirstSecureEndpointCert(
            ArrayOf<EndpointDescription> endpoints)
        {
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode != MessageSecurityMode.None &&
                    !ep.ServerCertificate.IsEmpty)
                {
                    return X509CertificateLoader.LoadCertificate(
                        ep.ServerCertificate.ToArray());
                }
            }

            return null;
        }
    }
}
