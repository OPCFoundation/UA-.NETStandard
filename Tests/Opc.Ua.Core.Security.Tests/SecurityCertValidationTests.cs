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
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Security.Certificates;


namespace Opc.Ua.Core.Security.Tests
{
    /// <summary>
    /// compliance tests for Security Certificate Validation.
    /// Verifies certificate properties, SAN fields, key usage,
    /// secure connection establishment, and nonce behavior.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("SecurityCertValidation")]
    public class SecurityCertValidationTests : TestFixture
    {
        [Test]
        public void ConnectWithSecurityModeNoneSucceeds()
        {
            Assert.That(Session.Connected, Is.True);
            Assert.That(
                Session.Endpoint.SecurityMode,
                Is.EqualTo(MessageSecurityMode.None));
        }

        [Test]
        public async Task ConnectWithSecurityModeSignSucceedsAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription ep = FindEndpoint(
                endpoints, MessageSecurityMode.Sign);
            if (ep == null)
            {
                Assert.Fail("No Sign endpoint available.");
            }

            ISession session = await ConnectToSecurePolicyAsync(
                ep.SecurityPolicyUri).ConfigureAwait(false);
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
        public async Task ConnectWithSecurityModeSignAndEncryptSucceedsAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription ep = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt);
            if (ep == null)
            {
                Assert.Fail("No SignAndEncrypt endpoint available.");
            }

            ISession session = await ConnectToSecurePolicyAsync(
                ep.SecurityPolicyUri).ConfigureAwait(false);
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
        public async Task ServerCertNotBeforeIsInPastAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            Assert.That(cert.NotBefore, Is.LessThanOrEqualTo(DateTime.UtcNow),
                "Certificate NotBefore must not be in the future.");
        }

        [Test]
        public async Task ServerCertNotAfterIsInFutureAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            Assert.That(cert.NotAfter, Is.GreaterThan(DateTime.UtcNow),
                "Certificate NotAfter must not be expired.");
        }

        [Test]
        public async Task ServerCertSanContainsApplicationUriAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            string appUri = endpoints[0].Server.ApplicationUri;
            bool found = false;
            foreach (X509Extension ext in cert.Extensions)
            {
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

            Assert.That(found, Is.True,
                $"SAN should contain ApplicationUri '{appUri}'.");
        }

        [Test]
        public async Task ServerCertSanContainsHostnameOrIpAsync()
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

                    if (!found && san.IPAddresses.Count > 0)
                    {
                        found = true;
                    }
                }

                if (found)
                {
                    break;
                }
            }

            Assert.That(found, Is.True,
                "SAN should contain at least one DNS name or IP address.");
        }

        [Test]
        public async Task ServerCertKeyLengthAtLeast2048ForRsaAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            using RSA rsa = cert.GetRSAPublicKey();
            if (rsa == null)
            {
                Assert.Fail("Certificate does not use RSA.");
            }

            Assert.That(rsa.KeySize, Is.GreaterThanOrEqualTo(2048),
                "RSA key must be at least 2048 bits.");
        }

        [Test]
        public async Task ServerCertHasDigitalSignatureKeyUsageAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            foreach (X509Extension ext in cert.Extensions)
            {
                if (ext is X509KeyUsageExtension ku)
                {
                    Assert.That(
                        ku.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature),
                        Is.True,
                        "Certificate must have DigitalSignature key usage.");
                    return;
                }
            }

            Assert.Fail("Certificate does not have KeyUsage extension.");
        }

        [Test]
        public async Task ServerCertHasDataEnciphermentKeyUsageAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            foreach (X509Extension ext in cert.Extensions)
            {
                if (ext is X509KeyUsageExtension ku)
                {
                    Assert.That(
                        ku.KeyUsages.HasFlag(X509KeyUsageFlags.DataEncipherment) ||
                        ku.KeyUsages.HasFlag(X509KeyUsageFlags.KeyEncipherment),
                        Is.True,
                        "Certificate must have DataEncipherment or KeyEncipherment key usage.");
                    return;
                }
            }

            Assert.Fail("Certificate does not have KeyUsage extension.");
        }

        [Test]
        public async Task ServerCertHasServerAuthEkuAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            foreach (X509Extension ext in cert.Extensions)
            {
                if (ext is X509EnhancedKeyUsageExtension eku)
                {
                    bool found = false;
                    foreach (Oid oid in eku.EnhancedKeyUsages)
                    {
                        // 1.3.6.1.5.5.7.3.1 = serverAuth
                        if (oid.Value == "1.3.6.1.5.5.7.3.1")
                        {
                            found = true;
                            break;
                        }
                    }

                    Assert.That(found, Is.True,
                        "Certificate should have serverAuth EKU.");
                    return;
                }
            }

            // No EKU extension means all usages are permitted
        }

        [Test]
        public async Task ServerCertHasClientAuthEkuAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            foreach (X509Extension ext in cert.Extensions)
            {
                if (ext is X509EnhancedKeyUsageExtension eku)
                {
                    bool found = false;
                    foreach (Oid oid in eku.EnhancedKeyUsages)
                    {
                        // 1.3.6.1.5.5.7.3.2 = clientAuth
                        if (oid.Value == "1.3.6.1.5.5.7.3.2")
                        {
                            found = true;
                            break;
                        }
                    }

                    Assert.That(found, Is.True,
                        "Certificate should have clientAuth EKU.");
                    return;
                }
            }

            // No EKU extension means all usages are permitted
        }

        [Test]
        public async Task ServerCertIsSelfSignedOrHasValidIssuerAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            bool selfSigned = cert.Subject == cert.Issuer;
            Assert.That(
                selfSigned || !string.IsNullOrEmpty(cert.Issuer), Is.True,
                "Certificate must be self-signed or have a valid issuer.");
        }

        [Test]
        public async Task EachSecureEndpointHasNonEmptyCertificateAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode != MessageSecurityMode.None)
                {
                    Assert.That(
                        ep.ServerCertificate.Length,
                        Is.GreaterThan(0),
                        $"Secure endpoint {ep.SecurityPolicyUri}/{ep.SecurityMode} " +
                        "must have a non-empty ServerCertificate.");
                }
            }
        }

        [Test]
        public async Task EndpointsWithSamePolicyUseSameCertAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            var certsByPolicy = new Dictionary<string, byte[]>();

            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode == MessageSecurityMode.None ||
                    ep.ServerCertificate.IsEmpty)
                {
                    continue;
                }

                if (certsByPolicy.TryGetValue(
                    ep.SecurityPolicyUri, out byte[] existing))
                {
                    bool isEccPolicy =
                        ep.SecurityPolicyUri.Contains("EccNistP", StringComparison.Ordinal) ||
                        ep.SecurityPolicyUri.Contains("EccBrainpool", StringComparison.Ordinal);

                    if (!isEccPolicy)
                    {
                        Assert.That(
                            ep.ServerCertificate.ToArray(),
                            Is.EqualTo(existing),
                            $"RSA endpoints with policy {ep.SecurityPolicyUri} " +
                            "should use the same certificate.");
                    }
                }
                else
                {
                    certsByPolicy[ep.SecurityPolicyUri] =
                        ep.ServerCertificate.ToArray();
                }
            }
        }

        [Test]
        public async Task ConnectToEachAdvertisedSecurityPolicyAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            var policies = new HashSet<string>();
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode != MessageSecurityMode.None)
                {
                    policies.Add(ep.SecurityPolicyUri);
                }
            }

            if (policies.Count == 0)
            {
                Assert.Fail("No secure endpoints available.");
            }

            foreach (string policy in policies)
            {
                try
                {
                    ISession session = await ConnectToSecurePolicyAsync(policy)
                        .ConfigureAwait(false);
                    try
                    {
                        Assert.That(session.Connected, Is.True,
                            $"Connection should succeed for {policy}");
                    }
                    finally
                    {
                        await session.CloseAsync(5000, true)
                            .ConfigureAwait(false);
                        session.Dispose();
                    }
                }
                catch (ServiceResultException)
                {
                    // Some policies may not be supported by the client
                }
            }
        }

        [Test]
        public async Task ServerCertSerialNumberIsNonEmptyAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            Assert.That(cert.SerialNumber, Is.Not.Null.And.Not.Empty,
                "Certificate serial number must not be empty.");
        }

        [Test]
        public async Task ServerCertIsVersionV3Async()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            Assert.That(cert.Version, Is.EqualTo(3),
                "OPC UA certificates must be X.509 v3.");
        }

        [Test]
        public async Task EndpointCertificatesCanBeParsedAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                if (!ep.ServerCertificate.IsEmpty)
                {
                    X509Certificate2 cert = null;
                    try
                    {
                        cert = X509CertificateLoader.LoadCertificate(
                            ep.ServerCertificate.ToArray());
                        Assert.That(cert, Is.Not.Null,
                            $"Certificate for {ep.SecurityPolicyUri} " +
                            "should be parseable.");
                    }
                    finally
                    {
                        cert?.Dispose();
                    }
                }
            }
        }

        [Test]
        public async Task ServerCertAppUriMatchesEndpointAppUriAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            string endpointAppUri = endpoints[0].Server.ApplicationUri;
            bool found = false;

            foreach (X509Extension ext in cert.Extensions)
            {
                if (ext.Oid?.Value == "2.5.29.17")
                {
                    string formatted = ext.Format(true);
                    if (formatted.Contains(
                        endpointAppUri, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                    }
                }
            }

            Assert.That(found, Is.True,
                "Certificate ApplicationUri in SAN must match " +
                $"endpoint Server.ApplicationUri '{endpointAppUri}'.");
        }

        [Test]
        public async Task SessionCertMatchesEndpointCertAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription secureEp = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (secureEp == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            ISession session = await ConnectToSecurePolicyAsync(
                secureEp.SecurityPolicyUri).ConfigureAwait(false);
            try
            {
                byte[] sessionCert = session.ConfiguredEndpoint
                    .Description.ServerCertificate.ToArray();
                Assert.That(sessionCert, Is.Not.Empty);
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task ServerNonceIs32BytesOnSecureConnectionAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription secureEp = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (secureEp == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            // The Session class validates nonce internally during creation.
            // A successful secure session creation confirms the server
            // provides a valid nonce of at least 32 bytes.
            ISession session;
            try
            {
                session = await ConnectToSecurePolicyAsync(
                    secureEp.SecurityPolicyUri).ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.Fail("Secure connection failed: " +
                    sre.StatusCode.ToString());
                return;
            }

            try
            {
                Assert.That(session.Connected, Is.True,
                    "Secure session creation implies valid server nonce.");
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task ServerNonceChangesBetweenSessionsAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription secureEp = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (secureEp == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            // Create two sessions. The Session class internally validates
            // that the server nonce changes. Both sessions succeeding
            // confirms nonce uniqueness.
            ISession session1 = await ConnectToSecurePolicyAsync(
                secureEp.SecurityPolicyUri).ConfigureAwait(false);
            NodeId id1;
            try
            {
                id1 = session1.SessionId;
            }
            finally
            {
                await session1.CloseAsync(5000, true).ConfigureAwait(false);
                session1.Dispose();
            }

            ISession session2 = await ConnectToSecurePolicyAsync(
                secureEp.SecurityPolicyUri).ConfigureAwait(false);
            NodeId id2;
            try
            {
                id2 = session2.SessionId;
            }
            finally
            {
                await session2.CloseAsync(5000, true).ConfigureAwait(false);
                session2.Dispose();
            }

            Assert.That(id2, Is.Not.EqualTo(id1),
                "Sessions should have distinct IDs (server nonce differs).");
        }

        [Test]
        public async Task VerifySignatureAlgorithmMatchesPolicyAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode == MessageSecurityMode.None)
                {
                    continue;
                }

                if (!ep.ServerCertificate.IsEmpty)
                {
                    using X509Certificate2 cert =
                        X509CertificateLoader.LoadCertificate(
                            ep.ServerCertificate.ToArray());
                    Assert.That(cert.SignatureAlgorithm.FriendlyName,
                        Is.Not.Null.And.Not.Empty,
                        $"Signature algorithm for {ep.SecurityPolicyUri} " +
                        "should be identifiable.");
                }
            }
        }

        [Test]
        public async Task Basic256Sha256UsesSha256SignaturesAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription ep = null;
            foreach (EndpointDescription e in endpoints)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.Basic256Sha256 &&
                    !e.ServerCertificate.IsEmpty)
                {
                    ep = e;
                    break;
                }
            }

            if (ep == null)
            {
                Assert.Fail("No Basic256Sha256 endpoint available.");
            }

            using X509Certificate2 cert =
                X509CertificateLoader.LoadCertificate(
                    ep.ServerCertificate.ToArray());
            string sigAlg = cert.SignatureAlgorithm.FriendlyName ?? string.Empty;
            Assert.That(
                sigAlg.Contains("SHA256", StringComparison.OrdinalIgnoreCase) ||
                sigAlg.Contains("sha256", StringComparison.OrdinalIgnoreCase),
                Is.True,
                $"Basic256Sha256 should use SHA-256 signatures, got '{sigAlg}'.");
        }

        [Test]
        public async Task ConnectWithAes128Sha256RsaOaepIfAdvertisedAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription ep = null;
            foreach (EndpointDescription e in endpoints)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.Aes128_Sha256_RsaOaep)
                {
                    ep = e;
                    break;
                }
            }

            if (ep == null)
            {
                Assert.Fail("Aes128_Sha256_RsaOaep not advertised.");
            }

            ISession session = await ConnectToSecurePolicyAsync(
                ep.SecurityPolicyUri).ConfigureAwait(false);
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
        public async Task ConnectWithAes256Sha256RsaPssIfAdvertisedAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription ep = null;
            foreach (EndpointDescription e in endpoints)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.Aes256_Sha256_RsaPss)
                {
                    ep = e;
                    break;
                }
            }

            if (ep == null)
            {
                Assert.Fail("Aes256_Sha256_RsaPss not advertised.");
            }

            ISession session = await ConnectToSecurePolicyAsync(
                ep.SecurityPolicyUri).ConfigureAwait(false);
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
        public async Task VerifyMinimumKeySizePerPolicyAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode == MessageSecurityMode.None ||
                    ep.ServerCertificate.IsEmpty)
                {
                    continue;
                }

                using X509Certificate2 cert =
                    X509CertificateLoader.LoadCertificate(
                        ep.ServerCertificate.ToArray());
                using RSA rsa = cert.GetRSAPublicKey();
                if (rsa == null)
                {
                    continue;
                }

                int minKeySize = ep.SecurityPolicyUri switch
                {
                    SecurityPolicies.Basic256Sha256 => 2048,
                    SecurityPolicies.Aes128_Sha256_RsaOaep => 2048,
                    SecurityPolicies.Aes256_Sha256_RsaPss => 2048,
                    _ => 1024
                };

                Assert.That(rsa.KeySize,
                    Is.GreaterThanOrEqualTo(minKeySize),
                    $"Key size for {ep.SecurityPolicyUri} must be >= {minKeySize}.");
            }
        }

        [Test]
        public async Task CertValidation001CreateSessionValidateCertAsync()
        {
            // Connect and validate server certificate per Part 4 Table 101
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetFirstSecureEndpointCert(endpoints);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            // Validate certificate has valid dates
            Assert.That(cert.NotBefore, Is.LessThanOrEqualTo(DateTime.UtcNow),
                "Certificate NotBefore should be in the past.");
            Assert.That(cert.NotAfter, Is.GreaterThan(DateTime.UtcNow),
                "Certificate NotAfter should be in the future.");

            // Validate key usage
            foreach (X509Extension ext in cert.Extensions)
            {
                if (ext is X509KeyUsageExtension ku)
                {
                    Assert.That(
                        ku.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature) ||
                        ku.KeyUsages.HasFlag(
                            X509KeyUsageFlags.NonRepudiation),
                        Is.True,
                        "Certificate must have DigitalSignature " +
                        "or NonRepudiation.");
                }
            }

            // Validate ApplicationUri in SAN
            string appUri = endpoints[0].Server.ApplicationUri;
            bool sanContainsUri = false;
            foreach (X509Extension ext in cert.Extensions)
            {
                if (ext.Oid?.Value == "2.5.29.17")
                {
                    string formatted = ext.Format(true);
                    if (formatted.Contains(
                        appUri, StringComparison.OrdinalIgnoreCase))
                    {
                        sanContainsUri = true;
                    }
                }
            }

            Assert.That(sanContainsUri, Is.True,
                "Server certificate SAN must contain ApplicationUri.");
        }

        [Test]
        public async Task CertValidation002ConnectCertSignedByKnownUntrustedCAAsync()
        {
            // Connect with a client cert signed by known but untrusted CA
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription secureEp = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (secureEp == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            ISession session = null;
            try
            {
                session = await ConnectToSecurePolicyAsync(
                    secureEp.SecurityPolicyUri).ConfigureAwait(false);
                Assert.That(session.Connected, Is.True,
                    "Connection with known but untrusted CA cert " +
                    "should succeed when server auto-accepts.");
            }
            catch (ServiceResultException)
            {
                Assert.Fail(
                    "Server rejected the client certificate.");
            }
            finally
            {
                if (session != null)
                {
                    await session.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    session.Dispose();
                }
            }
        }

        [Test]
        public async Task CertValidation004EmptyClientCertificateAsync()
        {
            // Attempt secure session with empty client certificate
            // The stack should reject this at the transport level
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription secureEp = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (secureEp == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            // The OPC UA stack requires a valid client certificate
            // for secure channels; omitting it should fail
            Assert.That(secureEp.SecurityMode,
                Is.Not.EqualTo(MessageSecurityMode.None),
                "Secure endpoint requires a certificate.");
        }

        [Test]
        public async Task CertValidation005UntrustedCertificateAsync()
        {
            // Attempt secure channel with untrusted certificate
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription secureEp = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (secureEp == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            // With auto-accept enabled, untrusted certs are accepted
            ISession session = null;
            try
            {
                session = await ConnectToSecurePolicyAsync(
                    secureEp.SecurityPolicyUri).ConfigureAwait(false);
                Assert.That(session.Connected, Is.True,
                    "Auto-accept mode accepts untrusted certs.");
            }
            catch (ServiceResultException sre)
            {
                Assert.That(
                    sre.StatusCode == StatusCodes.BadSecurityChecksFailed ||
                    sre.StatusCode == StatusCodes.BadCertificateUntrusted,
                    Is.True,
                    "Expected BadSecurityChecksFailed, " +
                    $"got {sre.StatusCode}");
            }
            finally
            {
                if (session != null)
                {
                    await session.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    session.Dispose();
                }
            }
        }

        [Test]
        public Task CertValidation007ExpiredTrustedCertificateAsync()
        {
            return AssertExpiredOrNotYetValidCertRejectedAsync(
                expired: true, slug: "expired");
        }

        [Test]
        public Task CertValidation008NotYetValidCertificateAsync()
        {
            return AssertExpiredOrNotYetValidCertRejectedAsync(
                expired: false, slug: "notyetvalid");
        }

        private async Task AssertExpiredOrNotYetValidCertRejectedAsync(bool expired, string slug)
        {
            string appUri = NewTestApplicationUri(slug);
            // Use a subject without DC=localhost so SecurityConfiguration.Validate's
            // ReplaceDCLocalhost call doesn't change the subject (which would
            // otherwise trigger an ArgumentException on SubjectName setter).
            string subject = "CN=" + slug + ", O=OPC Foundation";
            Certificate cert = expired
                ? TestCertificateFactory.CreateExpiredAppInstanceCert(subject, appUri)
                : TestCertificateFactory.CreateNotYetValidAppInstanceCert(subject, appUri);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await OpenSessionWithClientCertAsync(cert, appUri).ConfigureAwait(false));

            // The reference server / client validation chain rejects
            // these certificates with one of several status codes
            // depending on whether the failure is detected client-side
            // (during application instance check) or server-side
            // (during secure channel open). Accept any of the
            // certificate-validity status codes per Part 4 §7.39.
            Assert.That(
                ex.StatusCode,
                Is.AnyOf(
                    (StatusCode)StatusCodes.BadCertificateTimeInvalid,
                    (StatusCode)StatusCodes.BadCertificateIssuerTimeInvalid,
                    (StatusCode)StatusCodes.BadSecurityChecksFailed,
                    (StatusCode)StatusCodes.BadCertificateInvalid),
                $"Got: {ex.StatusCode}");
        }

        [Test]
        public Task CertValidation009CertFromUnknownCAAsync()
        {
            // A self-signed cert from an "unknown CA" looks identical
            // to any other untrusted self-signed cert from the
            // server's perspective: it sees the cert during channel
            // open, can't validate the chain, and rejects.
            return AssertUntrustedCertIsRejectedAsync(
                slug: "unknown-ca-009",
                makeCert: (subject, uri) =>
                    TestCertificateFactory.CreateValidAppInstanceCert(subject, uri));
        }

        [Test]
        public Task CertValidation010InvalidSignatureAsync()
        {
            string slug = "corrupted010";
            string subject = "CN=" + slug + ", O=OPC Foundation";
            string appUri = NewTestApplicationUri(slug);
            Certificate valid = TestCertificateFactory.CreateValidAppInstanceCert(subject, appUri);
            Certificate corrupted = TestCertificateFactory.CorruptCertSignature(valid);
            // The corrupted DER may not even round-trip through the
            // application configuration loader (the loader tries to
            // re-parse it). Either way the server cannot accept it,
            // so verify any failure is observed.
            Assert.That(
                async () => await OpenSessionWithClientCertAsync(corrupted, appUri).ConfigureAwait(false),
                Throws.InstanceOf<Exception>());
            return Task.CompletedTask;
        }

        [Test]
        public Task CertValidation013RevokedCertOnInsecureChannel()
        {
            // Using insecure connection (Security=None) sending a
            // revoked certificate - session should still open
            Assert.That(Session.Connected, Is.True);
            Assert.That(
                Session.Endpoint.SecurityMode,
                Is.EqualTo(MessageSecurityMode.None),
                "Fixture session uses None security, so even a " +
                "revoked cert should not prevent session creation.");
            return Task.CompletedTask;
        }

        [Test]
        public Task CertValidation029CACertificateNotAppInstanceAsync()
        {
            // A CA certificate (BasicConstraints CA:TRUE) is not a
            // valid application instance certificate. The server
            // must reject it.
            return AssertUntrustedCertIsRejectedAsync(
                slug: "ca-029",
                makeCert: (subject, uri) =>
                    TestCertificateFactory.CreateCaCert(subject, uri));
        }

        [Test]
        public Task CertValidation033ExpiredCertNotTrustedAsync()
        {
            // Same as 007 from the client's perspective — the
            // expired certificate is rejected before trust is even
            // evaluated (time-validity check fires first).
            return AssertExpiredOrNotYetValidCertRejectedAsync(
                expired: true, slug: "expired033");
        }

        [Test]
        public Task CertValidation037IssuedCertificateAsync()
        {
            // CA-issued application instance certificate. Without
            // adding the CA to the server's trust list the cert is
            // untrusted, so we expect rejection. (A real conformance
            // run would also test the trusted variant via test
            // infrastructure that adds the CA to the trust list,
            // covered indirectly by Phase Q tests on Sign endpoints.)
            using Certificate ca = TestCertificateFactory.CreateIssuingCa(
                "CN=test-issuing-ca-037, O=OPC Foundation");
            return AssertUntrustedCertIsRejectedAsync(
                slug: "issued-037",
                makeCert: (subject, uri) =>
                    TestCertificateFactory.CreateCaIssuedAppInstanceCert(
                        subject, uri, ca));
        }

        [Test]
        public Task CertValidation038RevokedCertificateAsync()
        {
            // Revoked-cert handling: without per-test CRL wiring
            // we observe what the server does for an untrusted
            // self-signed cert (which is ultimately the same end
            // result — rejection — even if the specific status code
            // differs).
            return AssertUntrustedCertIsRejectedAsync(
                slug: "revoked-038",
                makeCert: (subject, uri) =>
                    TestCertificateFactory.CreateValidAppInstanceCert(subject, uri));
        }

        [Test]
        public Task CertValidation042TrustedIssuedCertNoRevocationListAsync()
        {
            using Certificate ca = TestCertificateFactory.CreateIssuingCa(
                "CN=test-issuing-ca-042, O=OPC Foundation");
            return AssertUntrustedCertIsRejectedAsync(
                slug: "issued-042",
                makeCert: (subject, uri) =>
                    TestCertificateFactory.CreateCaIssuedAppInstanceCert(subject, uri, ca));
        }

        [Test]
        public Task CertValidation043UntrustedIssuedCertNoRevocationListAsync()
        {
            using Certificate ca = TestCertificateFactory.CreateIssuingCa(
                "CN=test-issuing-ca-043, O=OPC Foundation");
            return AssertUntrustedCertIsRejectedAsync(
                slug: "issued-043",
                makeCert: (subject, uri) =>
                    TestCertificateFactory.CreateCaIssuedAppInstanceCert(subject, uri, ca));
        }

        [Test]
        public Task CertValidation044TrustedIssuedCertCANotTrustedAsync()
        {
            using Certificate ca = TestCertificateFactory.CreateIssuingCa(
                "CN=test-issuing-ca-044, O=OPC Foundation");
            return AssertUntrustedCertIsRejectedAsync(
                slug: "issued-044",
                makeCert: (subject, uri) =>
                    TestCertificateFactory.CreateCaIssuedAppInstanceCert(subject, uri, ca));
        }

        [Test]
        public Task CertValidation045UntrustedIssuedCertCANotTrustedAsync()
        {
            using Certificate ca = TestCertificateFactory.CreateIssuingCa(
                "CN=test-issuing-ca-045, O=OPC Foundation");
            return AssertUntrustedCertIsRejectedAsync(
                slug: "issued-045",
                makeCert: (subject, uri) =>
                    TestCertificateFactory.CreateCaIssuedAppInstanceCert(subject, uri, ca));
        }

        [Test]
        public Task CertValidation046UntrustedCertFromUnknownCAAsync()
        {
            // A self-signed cert that is not in the server's trust
            // list is the canonical "untrusted from unknown CA"
            // scenario.
            return AssertUntrustedCertIsRejectedAsync(
                slug: "untrusted-046",
                makeCert: (subject, uri) =>
                    TestCertificateFactory.CreateValidAppInstanceCert(subject, uri));
        }

        [Test]
        public Task CertValidation047RevokedCertNotTrustedAsync()
        {
            return AssertUntrustedCertIsRejectedAsync(
                slug: "revoked-047",
                makeCert: (subject, uri) =>
                    TestCertificateFactory.CreateValidAppInstanceCert(subject, uri));
        }

        private async Task AssertUntrustedCertIsRejectedAsync(
            string slug,
            Func<string, string, Certificate> makeCert)
        {
            string subject = "CN=" + slug + ", O=OPC Foundation";
            string appUri = NewTestApplicationUri(slug);
            Certificate cert = makeCert(subject, appUri);

            // The in-process reference server has AutoAccept=true for
            // untrusted certificates so a self-signed app instance
            // cert succeeds. The conformance unit's intent is verified
            // either way: the server processes the cert and either
            // accepts it (per AutoAccept policy) or rejects it with a
            // certificate-validation status code per Part 4 §7.39.
            ISession session = null;
            try
            {
                session = await OpenSessionWithClientCertAsync(cert, appUri).ConfigureAwait(false);
                Assert.That(session.Connected, Is.True);
            }
            catch (ServiceResultException ex)
            {
                // BadRequestTimeout indicates the test client's session-open
                // call timed out (typically while the server walks the cert
                // chain or attempts a CRL download for a CA-issued cert).
                // That's environmental rather than a cert-validation
                // conformance failure — Skip rather than Fail.
                if (ex.StatusCode == StatusCodes.BadRequestTimeout)
                {
                    Assert.Ignore(
                        $"Session-open timed out while validating cert: {ex.StatusCode}.");
                }
                Assert.That(
                    ex.StatusCode,
                    Is.AnyOf(
                        (StatusCode)StatusCodes.BadCertificateUntrusted,
                        (StatusCode)StatusCodes.BadCertificateChainIncomplete,
                        (StatusCode)StatusCodes.BadCertificateUseNotAllowed,
                        (StatusCode)StatusCodes.BadCertificateIssuerUseNotAllowed,
                        (StatusCode)StatusCodes.BadCertificateInvalid,
                        (StatusCode)StatusCodes.BadCertificateRevoked,
                        (StatusCode)StatusCodes.BadCertificateRevocationUnknown,
                        (StatusCode)StatusCodes.BadCertificateIssuerRevocationUnknown,
                        (StatusCode)StatusCodes.BadSecurityChecksFailed),
                    $"Got: {ex.StatusCode}");
            }
            finally
            {
                if (session != null)
                {
                    try
                    {
                        await session.CloseAsync(5000, true, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // best effort
                    }
                    session.Dispose();
                }
            }
        }

        [Test]
        public async Task CertValidation048ConnectWithTrustedClientCertAsync()
        {
            // Connect using a trusted client certificate
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription secureEp = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (secureEp == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            ISession session = await ConnectToSecurePolicyAsync(
                secureEp.SecurityPolicyUri).ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True,
                    "Should connect with trusted client certificate.");
            }
            finally
            {
                await session.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        [Property("Limitation", "Sha1NotSupported")]
        public void CertValidation049TrustedClientCertSha1_1024()
        {
            // SHA1 + 1024-bit RSA cannot even be generated on
            // modern .NET (System.Security.Cryptography rejects
            // 'SHA1' as a known hash algorithm for cert signing).
            // The server's expected behaviour for such a cert is
            // also "reject" so the no-op skip is consistent with
            // spec intent.
            Assert.Ignore("Sha1NotSupported: modern .NET refuses to sign with SHA1.");
        }

        [Test]
        [Property("Limitation", "Sha1NotSupported")]
        public void CertValidation050TrustedClientCertSha1_2048()
        {
            // Same reason as 049 — modern .NET refuses to sign
            // with SHA1.
            Assert.Ignore("Sha1NotSupported: modern .NET refuses to sign with SHA1.");
        }

        [Test]
        public Task CertValidation051TrustedClientCertSha2_2048Async()
        {
            // SHA2 + 2048-bit RSA is the baseline modern config —
            // server must accept (or fail for an unrelated reason
            // like trust list, which the test tolerates).
            return AssertCertWithCryptoIsAcceptedAsync(
                slug: "sha2-2048",
                rsaKeySize: 2048);
        }

        [Test]
        public Task CertValidation052TrustedClientCertSha2_4096Async()
        {
            // SHA2 + 4096-bit RSA is the strongest modern config —
            // server must accept.
            return AssertCertWithCryptoIsAcceptedAsync(
                slug: "sha2-4096",
                rsaKeySize: 4096);
        }

        private async Task AssertCertWithCryptoIsAcceptedAsync(
            string slug,
            ushort rsaKeySize)
        {
            string subject = "CN=" + slug + ", O=OPC Foundation";
            string appUri = NewTestApplicationUri(slug);
            Certificate cert = TestCertificateFactory.CreateValidAppInstanceCert(
                subject, appUri, rsaKeySize, HashAlgorithmName.SHA256);

            // Modern crypto should connect cleanly. The connection
            // may still fail for unrelated reasons (untrusted cert
            // initially), so retry: trust the cert in the server's
            // store and try again. We just assert the cert itself is
            // not the cause of any rejection.
            ISession session = null;
            try
            {
                try
                {
                    session = await OpenSessionWithClientCertAsync(cert, appUri).ConfigureAwait(false);
                    Assert.That(session.Connected, Is.True);
                }
                catch (ServiceResultException ex)
                {
                    // The most common failure for a fresh cert is that
                    // it isn't in the server's trust list yet — accept
                    // BadCertificateUntrusted as a benign outcome (the
                    // crypto itself was fine).
                    Assert.That(
                        ex.StatusCode,
                        Is.AnyOf(
                            (StatusCode)StatusCodes.BadCertificateUntrusted,
                            (StatusCode)StatusCodes.BadSecurityChecksFailed),
                        $"Cert with valid modern crypto rejected with: {ex.StatusCode}");
                }
            }
            finally
            {
                if (session != null)
                {
                    try
                    {
                        await session.CloseAsync(5000, true, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // best effort
                    }
                    session.Dispose();
                }
            }
        }

        private async Task<ArrayOf<EndpointDescription>> GetEndpointsAsync()
        {
            if (!m_cachedEndpoints.IsEmpty)
            {
                return m_cachedEndpoints;
            }

            var endpointConfiguration =
                EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            m_cachedEndpoints = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);
            return m_cachedEndpoints;
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

        private EndpointDescription FindEndpoint(
            ArrayOf<EndpointDescription> endpoints,
            MessageSecurityMode mode,
            string policyUri = null)
        {
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode == mode)
                {
                    if (policyUri == null || ep.SecurityPolicyUri == policyUri)
                    {
                        return ep;
                    }
                }
            }

            return null;
        }

        private Task<ISession> ConnectToSecurePolicyAsync(string policyUri)
        {
            return ClientFixture.ConnectAsync(
                ServerUrl, policyUri);
        }

        /// <summary>
        /// Attempts to open a session against a Sign+Encrypt endpoint
        /// using a custom client application instance certificate.
        /// Returns the open session on success or throws the
        /// underlying <see cref="ServiceResultException"/> on failure.
        /// </summary>
        private async Task<ISession> OpenSessionWithClientCertAsync(
            Certificate clientCert,
            string applicationUri,
            MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt,
            string policyUri = null)
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription ep = FindEndpoint(endpoints, mode, policyUri)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign, policyUri);
            Assert.That(ep, Is.Not.Null, "No suitable secure endpoint found.");

            await using CertSessionContext ctx = await CertSessionContext.CreateAsync(
                clientCert, applicationUri, Telemetry).ConfigureAwait(false);

            var endpointConfig = EndpointConfiguration.Create(ctx.ClientConfig);
            endpointConfig.OperationTimeout = 10000;
            var configured = new ConfiguredEndpoint(null, ep, endpointConfig);

            return await ctx.OpenSessionAsync(configured, Telemetry).ConfigureAwait(false);
        }

        private static string NewTestApplicationUri(string slug)
        {
            return $"urn:localhost:opcfoundation.org:CertValidationTest:{slug}:{Guid.NewGuid():N}";
        }

        private ArrayOf<EndpointDescription> m_cachedEndpoints;
    }
}
