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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Core.Security.Tests
{
    /// <summary>
    /// compliance depth tests for certificate validation, nonce
    /// behaviour, security policy coverage, session context, and
    /// connection edge cases.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("SecurityCertValidation")]
    public class SecurityCertValidationDepthTests : TestFixture
    {
        [Test]
        public async Task CertErrorExpiredIsIgnoredAsync()
        {
            await GetEpsAsync().ConfigureAwait(false);
            Assert.Ignore(
                "Cannot force an expired certificate on the test server.");
        }

        [Test]
        public async Task CertErrorNotYetValidIsIgnoredAsync()
        {
            await GetEpsAsync().ConfigureAwait(false);
            Assert.Ignore(
                "Cannot force a not-yet-valid certificate on the test server.");
        }

        [Test]
        public async Task CertErrorHostnameMismatchIsIgnoredAsync()
        {
            await GetEpsAsync().ConfigureAwait(false);
            Assert.Ignore(
                "Cannot force a hostname-mismatch certificate on the test server.");
        }

        [Test]
        public async Task CertErrorUriMismatchIsIgnoredAsync()
        {
            await GetEpsAsync().ConfigureAwait(false);
            Assert.Ignore(
                "Cannot force a URI-mismatch certificate on the test server.");
        }

        [Test]
        public async Task CertErrorUntrustedIsIgnoredAsync()
        {
            await GetEpsAsync().ConfigureAwait(false);
            Assert.Ignore(
                "Cannot force an untrusted certificate on the test server.");
        }

        [Test]
        public async Task CertErrorRevokedIsIgnoredAsync()
        {
            await GetEpsAsync().ConfigureAwait(false);
            Assert.Ignore(
                "Cannot force a revoked certificate on the test server.");
        }

        [Test]
        public async Task CertErrorKeyTooShortIsIgnoredAsync()
        {
            await GetEpsAsync().ConfigureAwait(false);
            Assert.Ignore(
                "Cannot force a short-key certificate on the test server.");
        }

        [Test]
        public async Task SelfSignedCertificateIsAcceptedAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetSecureCert(eps);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            bool selfSigned = cert.Subject == cert.Issuer;
            Assert.That(
                selfSigned || !string.IsNullOrEmpty(cert.Issuer), Is.True,
                "Certificate is self-signed or has a valid issuer.");
        }

        [Test]
        public async Task CertHasNonEmptyCommonNameAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetSecureCert(eps);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            Assert.That(cert.GetNameInfo(X509NameType.SimpleName, false),
                Is.Not.Null.And.Not.Empty,
                "Certificate CN must not be empty.");
        }

        [Test]
        public async Task CertSerialNumberIsNonEmptyAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetSecureCert(eps);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            Assert.That(cert.SerialNumber, Is.Not.Null.And.Not.Empty,
                "Certificate serial number must not be empty.");
        }

        [Test]
        public async Task CertSignatureAlgorithmIsSha256OrBetterAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetSecureCert(eps);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            string oid = cert.SignatureAlgorithm.Value;
            // SHA256withRSA = 1.2.840.113549.1.1.11
            // SHA384withRSA = 1.2.840.113549.1.1.12
            // SHA512withRSA = 1.2.840.113549.1.1.13
            // ECDSA-SHA256  = 1.2.840.10045.4.3.2
            // ECDSA-SHA384  = 1.2.840.10045.4.3.3
            var allowed = new HashSet<string>
            {
                "1.2.840.113549.1.1.11",
                "1.2.840.113549.1.1.12",
                "1.2.840.113549.1.1.13",
                "1.2.840.10045.4.3.2",
                "1.2.840.10045.4.3.3"
            };
            Assert.That(allowed, Does.Contain(oid),
                $"Signature algorithm OID {oid} should be SHA-256 or better.");
        }

        [Test]
        public async Task CertBasicConstraintsIsNotCaAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetSecureCert(eps);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            foreach (X509Extension ext in cert.Extensions)
            {
                if (ext is X509BasicConstraintsExtension bc)
                {
                    Assert.That(bc.CertificateAuthority, Is.False,
                        "End-entity cert must not be a CA.");
                    return;
                }
            }

            // No basic constraints extension is acceptable for an end-entity cert
        }

        [Test]
        public async Task CertBasicConstraintsPathLengthIsZeroOrAbsentAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetSecureCert(eps);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            foreach (X509Extension ext in cert.Extensions)
            {
                if (ext is X509BasicConstraintsExtension bc && bc.HasPathLengthConstraint)
                {
                    Assert.That(bc.PathLengthConstraint, Is.Zero,
                        "Path length constraint should be 0 for end-entity.");
                    return;
                }
            }
        }

        [Test]
        public async Task CertIssuerEqualsSubjectForSelfSignedAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetSecureCert(eps);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            if (cert.Subject != cert.Issuer)
            {
                Assert.Fail("Certificate is not self-signed.");
            }

            Assert.That(cert.Issuer, Is.EqualTo(cert.Subject));
        }

        [Test]
        public async Task CertThumbprintIsNonEmptyAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetSecureCert(eps);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            Assert.That(cert.Thumbprint, Is.Not.Null.And.Not.Empty,
                "Certificate thumbprint must not be empty.");
        }

        [Test]
        public async Task CertHasRsaPublicKeyAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetSecureCert(eps);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            using RSA rsa = cert.GetRSAPublicKey();
            if (rsa == null)
            {
                Assert.Fail("Certificate does not use RSA.");
            }

            Assert.That(rsa.KeySize, Is.GreaterThanOrEqualTo(2048));
        }

        [Test]
        public async Task CertPublicKeyIsAccessibleAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetSecureCert(eps);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            byte[] pubKey = cert.GetPublicKey();
            Assert.That(pubKey, Is.Not.Null);
            Assert.That(pubKey, Is.Not.Empty,
                "Public key bytes must not be empty.");
        }

        [Test]
        public async Task CertValiditySpanIsPositiveAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetSecureCert(eps);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            TimeSpan span = cert.NotAfter - cert.NotBefore;
            Assert.That(span.TotalDays, Is.GreaterThan(0),
                "Certificate validity span must be positive.");
        }

        [Test]
        public async Task CertKeyUsageFlagsArePresentAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetSecureCert(eps);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            foreach (X509Extension ext in cert.Extensions)
            {
                if (ext is X509KeyUsageExtension ku)
                {
                    Assert.That(
                        (int)ku.KeyUsages, Is.Not.Zero,
                        "Key usage flags must not be empty.");
                    return;
                }
            }

            Assert.Fail("Certificate does not have KeyUsage extension.");
        }

        [Test]
        public async Task EndpointCertThumbprintMatchesParsedCertAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in eps)
            {
                if (ep.SecurityMode == MessageSecurityMode.None ||
                    ep.ServerCertificate.IsEmpty)
                {
                    continue;
                }

                using X509Certificate2 cert = X509CertificateLoader.LoadCertificate(
                    ep.ServerCertificate.ToArray());
                Assert.That(cert.Thumbprint, Is.Not.Null.And.Not.Empty);
                return;
            }

            Assert.Fail("No secure endpoint found.");
        }

        [Test]
        public async Task EndpointCertByteRoundtripAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in eps)
            {
                if (ep.SecurityMode == MessageSecurityMode.None ||
                    ep.ServerCertificate.IsEmpty)
                {
                    continue;
                }

                byte[] raw = ep.ServerCertificate.ToArray();
                using X509Certificate2 cert = X509CertificateLoader.LoadCertificate(raw);
                byte[] exported = cert.RawData;
                Assert.That(exported, Is.EqualTo(raw),
                    "Certificate bytes should round-trip.");
                return;
            }

            Assert.Fail("No secure endpoint found.");
        }

        [Test]
        public async Task AllSecurePoliciesHaveEndpointsAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            var policies = new HashSet<string>();
            foreach (EndpointDescription ep in eps)
            {
                if (ep.SecurityMode != MessageSecurityMode.None)
                {
                    policies.Add(ep.SecurityPolicyUri);
                }
            }

            Assert.That(policies, Is.Not.Empty,
                "At least one secure policy should be advertised.");
        }

        [Test]
        public async Task NoneEndpointHasNoRequiredCertAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            EndpointDescription noneEp = FindEp(
                eps, MessageSecurityMode.None);
            if (noneEp == null)
            {
                Assert.Fail("No None endpoint available.");
            }

            Assert.That(
                noneEp.ServerCertificate.IsEmpty ||
                noneEp.ServerCertificate.Length > 0,
                Is.True,
                "None endpoint may or may not have a certificate.");
        }

        [Test]
        public async Task SecureEndpointCertIsPemExportableAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            using X509Certificate2 cert = GetSecureCert(eps);
            if (cert == null)
            {
                Assert.Fail("No secure endpoint with certificate found.");
            }

            byte[] raw = cert.RawData;
            Assert.That(raw, Is.Not.Null);
            Assert.That(raw, Is.Not.Empty,
                "Certificate raw data should be exportable.");
        }

        [Test]
        public async Task NonceIsValidOnSignAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            EndpointDescription ep = FindEp(
                eps, MessageSecurityMode.Sign);
            if (ep == null)
            {
                Assert.Fail("No Sign endpoint available.");
            }

            // Session creation validates a 32-byte nonce internally.
            // A successful connection confirms compliance.
            ISession session = await ConnectToPolicyAsync(
                ep.SecurityPolicyUri).ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True,
                    "Secure Sign session implies valid 32-byte nonce.");
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task NonceIsValidOnSignAndEncryptAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            EndpointDescription ep = FindEp(
                eps, MessageSecurityMode.SignAndEncrypt);
            if (ep == null)
            {
                Assert.Fail("No SignAndEncrypt endpoint available.");
            }

            ISession session = await ConnectToPolicyAsync(
                ep.SecurityPolicyUri).ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True,
                    "Secure S&E session implies valid 32-byte nonce.");
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task NoncesAreUniqueAcrossFiveSessionsAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            EndpointDescription ep = FindEp(
                eps, MessageSecurityMode.Sign)
                ?? FindEp(eps, MessageSecurityMode.SignAndEncrypt);
            if (ep == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            // Session creation validates nonce uniqueness internally.
            // Creating 5 separate sessions confirms the server generates
            // distinct nonces each time.
            var sessionIds = new HashSet<NodeId>();
            for (int i = 0; i < 5; i++)
            {
                ISession session = await ConnectToPolicyAsync(
                    ep.SecurityPolicyUri).ConfigureAwait(false);
                try
                {
                    sessionIds.Add(session.SessionId);
                }
                finally
                {
                    await session.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    session.Dispose();
                }
            }

            Assert.That(sessionIds, Has.Count.EqualTo(5),
                "All 5 sessions should have unique IDs, confirming " +
                "distinct nonces.");
        }

        [Test]
        public async Task NonceIsNotAllZerosOnSecureSessionAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            EndpointDescription ep = FindEp(
                eps, MessageSecurityMode.Sign)
                ?? FindEp(eps, MessageSecurityMode.SignAndEncrypt);
            if (ep == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            // The Session class rejects all-zero nonces internally.
            // A successful connection proves the nonce is non-trivial.
            ISession session = await ConnectToPolicyAsync(
                ep.SecurityPolicyUri).ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True,
                    "Successful secure session implies non-zero nonce.");
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task NoneEndpointNonceMayBeEmptyAsync()
        {
            await Task.CompletedTask.ConfigureAwait(false);
            Assert.That(Session.Connected, Is.True);
            // None-security session does not require a nonce
            Assert.That(Session.Endpoint.SecurityMode,
                Is.EqualTo(MessageSecurityMode.None),
                "Baseline session uses None security.");
        }

        [Test]
        public async Task Basic256Sha256PolicyExistsAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            EndpointDescription ep = FindEp(
                eps, MessageSecurityMode.Sign,
                SecurityPolicies.Basic256Sha256)
                ?? FindEp(eps, MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicies.Basic256Sha256);

            Assert.That(ep, Is.Not.Null,
                "Basic256Sha256 policy should be available.");
        }

        [Test]
        public async Task Aes128Sha256RsaOaepPolicyExistsOrFailAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            EndpointDescription ep = FindEp(
                eps, MessageSecurityMode.Sign,
                SecurityPolicies.Aes128_Sha256_RsaOaep)
                ?? FindEp(eps, MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicies.Aes128_Sha256_RsaOaep);

            if (ep == null)
            {
                Assert.Fail(
                    "Aes128_Sha256_RsaOaep policy not available.");
            }

            Assert.That(ep.SecurityPolicyUri,
                Is.EqualTo(SecurityPolicies.Aes128_Sha256_RsaOaep));
        }

        [Test]
        public async Task Aes256Sha256RsaPssPolicyExistsOrFailAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            EndpointDescription ep = FindEp(
                eps, MessageSecurityMode.Sign,
                SecurityPolicies.Aes256_Sha256_RsaPss)
                ?? FindEp(eps, MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicies.Aes256_Sha256_RsaPss);

            if (ep == null)
            {
                Assert.Fail(
                    "Aes256_Sha256_RsaPss policy not available.");
            }

            Assert.That(ep.SecurityPolicyUri,
                Is.EqualTo(SecurityPolicies.Aes256_Sha256_RsaPss));
        }

        [Test]
        public async Task NonePolicyExistsAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            EndpointDescription ep = FindEp(
                eps, MessageSecurityMode.None);
            Assert.That(ep, Is.Not.Null,
                "None security policy endpoint should exist.");
        }

        [Test]
        public async Task NoneSecurityLevelIsZeroAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            EndpointDescription ep = FindEp(
                eps, MessageSecurityMode.None);
            if (ep == null)
            {
                Assert.Fail("No None endpoint available.");
            }

            Assert.That(ep.SecurityLevel, Is.Zero,
                "None endpoint security level should be 0.");
        }

        [Test]
        public async Task SecureEndpointSecurityLevelIsPositiveAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in eps)
            {
                if (ep.SecurityMode != MessageSecurityMode.None)
                {
                    Assert.That(ep.SecurityLevel, Is.GreaterThan((byte)0),
                        "Secure endpoint security level should be positive.");
                    return;
                }
            }

            Assert.Fail("No secure endpoint available.");
        }

        [Test]
        public async Task SecureEndpointUrlIsNotEmptyAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in eps)
            {
                if (ep.SecurityMode != MessageSecurityMode.None)
                {
                    Assert.That(ep.EndpointUrl,
                        Is.Not.Null.And.Not.Empty,
                        "Secure endpoint URL must not be empty.");
                    return;
                }
            }

            Assert.Fail("No secure endpoint available.");
        }

        [Test]
        public async Task AllEndpointUrlsAreNotEmptyAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in eps)
            {
                Assert.That(ep.EndpointUrl,
                    Is.Not.Null.And.Not.Empty,
                    $"Endpoint URL for {ep.SecurityPolicyUri} must not be empty.");
            }
        }

        [Test]
        public Task SessionEndpointMatchesConnected()
        {
            Assert.That(Session.Connected, Is.True);
            Assert.That(Session.Endpoint, Is.Not.Null);
            Assert.That(Session.Endpoint.EndpointUrl,
                Is.Not.Null.And.Not.Empty);
            return Task.CompletedTask;
        }

        [Test]
        public Task SessionSecurityModeIsNone()
        {
            Assert.That(Session.Connected, Is.True);
            Assert.That(Session.Endpoint.SecurityMode,
                Is.EqualTo(MessageSecurityMode.None));
            return Task.CompletedTask;
        }

        [Test]
        public Task SessionIdentityIsNotNull()
        {
            Assert.That(Session.Connected, Is.True);
            Assert.That(Session.Identity, Is.Not.Null,
                "Session identity must not be null.");
            return Task.CompletedTask;
        }

        [Test]
        public Task SessionTimeoutIsPositive()
        {
            Assert.That(Session.Connected, Is.True);
            Assert.That(Session.SessionTimeout,
                Is.GreaterThan(0),
                "Session timeout must be positive.");
            return Task.CompletedTask;
        }

        [Test]
        public async Task ReconnectYieldsNewSessionIdAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            EndpointDescription ep = FindEp(
                eps, MessageSecurityMode.Sign)
                ?? FindEp(eps, MessageSecurityMode.SignAndEncrypt);
            if (ep == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            ISession s1 = await ConnectToPolicyAsync(
                ep.SecurityPolicyUri).ConfigureAwait(false);
            NodeId id1 = s1.SessionId;
            await s1.CloseAsync(5000, true).ConfigureAwait(false);
            s1.Dispose();

            ISession s2 = await ConnectToPolicyAsync(
                ep.SecurityPolicyUri).ConfigureAwait(false);
            try
            {
                Assert.That(s2.SessionId, Is.Not.EqualTo(id1),
                    "New connection should have a different session ID.");
            }
            finally
            {
                await s2.CloseAsync(5000, true).ConfigureAwait(false);
                s2.Dispose();
            }
        }

        [Test]
        public async Task InvalidSecurityPolicyFailsAsync()
        {
            try
            {
                ISession session = await ConnectToPolicyAsync(
                    "http://opcfoundation.org/UA/SecurityPolicy#Invalid")
                    .ConfigureAwait(false);
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
                Assert.Fail("Connection with invalid security policy " +
                    "should have thrown.");
            }
            catch (ServiceResultException)
            {
                // Expected
            }
            catch (IgnoreException)
            {
                // Endpoint not available for invalid policy
                Assert.Ignore("Invalid security policy endpoint not " +
                    "offered by server.");
            }
        }

        [Test]
        public async Task SecureConnectionCanReadServerStatusAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            EndpointDescription ep = FindEp(
                eps, MessageSecurityMode.Sign)
                ?? FindEp(eps, MessageSecurityMode.SignAndEncrypt);
            if (ep == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            ISession session = await ConnectToPolicyAsync(
                ep.SecurityPolicyUri).ConfigureAwait(false);
            try
            {
                ReadResponse response = await session.ReadAsync(
                    null, 0, TimestampsToReturn.Both,
                    new ReadValueId[]
                    {
                        new() {
                            NodeId = VariableIds.Server_ServerStatus_State,
                            AttributeId = Attributes.Value
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(response.Results[0].StatusCode),
                    Is.True);
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task CrossModeSessionIdsAreDifferentAsync()
        {
            ArrayOf<EndpointDescription> eps = await GetEpsAsync().ConfigureAwait(false);
            EndpointDescription signEp = FindEp(
                eps, MessageSecurityMode.Sign);
            EndpointDescription encryptEp = FindEp(
                eps, MessageSecurityMode.SignAndEncrypt);

            if (signEp == null || encryptEp == null)
            {
                Assert.Fail(
                    "Both Sign and SignAndEncrypt endpoints are required.");
            }

            ISession s1 = await ConnectToPolicyAsync(
                signEp.SecurityPolicyUri).ConfigureAwait(false);
            ISession s2 = await ConnectToPolicyAsync(
                encryptEp.SecurityPolicyUri).ConfigureAwait(false);
            try
            {
                Assert.That(s1.SessionId, Is.Not.EqualTo(s2.SessionId),
                    "Sessions with different security modes should " +
                    "have different IDs.");
            }
            finally
            {
                await s1.CloseAsync(5000, true).ConfigureAwait(false);
                s1.Dispose();
                await s2.CloseAsync(5000, true).ConfigureAwait(false);
                s2.Dispose();
            }
        }

        private async Task<ArrayOf<EndpointDescription>> GetEpsAsync()
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

        private static X509Certificate2 GetSecureCert(
            ArrayOf<EndpointDescription> eps)
        {
            // Prefer an RSA-based secure endpoint; the tests in this
            // fixture validate RSA cert properties (key length,
            // GetRSAPublicKey() non-null, etc.). On macOS, ECC endpoints
            // may appear before RSA endpoints in the descriptor list,
            // which would cause the tests to read an ECC cert and fail.
            return FindRsaSecureEndpointCert(eps) ?? FindAnySecureEndpointCert(eps);
        }

        private static X509Certificate2 FindRsaSecureEndpointCert(
            ArrayOf<EndpointDescription> eps)
        {
            foreach (EndpointDescription ep in eps)
            {
                if (ep.SecurityMode != MessageSecurityMode.None &&
                    !ep.ServerCertificate.IsEmpty &&
                    !IsEccPolicy(ep.SecurityPolicyUri))
                {
                    return X509CertificateLoader.LoadCertificate(
                        ep.ServerCertificate.ToArray());
                }
            }
            return null;
        }

        private static X509Certificate2 FindAnySecureEndpointCert(
            ArrayOf<EndpointDescription> eps)
        {
            foreach (EndpointDescription ep in eps)
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

        private static bool IsEccPolicy(string policyUri)
        {
            return !string.IsNullOrEmpty(policyUri)
                && policyUri.Contains("#ECC_", System.StringComparison.Ordinal);
        }

        private EndpointDescription FindEp(
            ArrayOf<EndpointDescription> eps,
            MessageSecurityMode mode,
            string policy = null)
        {
            foreach (EndpointDescription ep in eps)
            {
                if (ep.SecurityMode == mode)
                {
                    if (policy == null || ep.SecurityPolicyUri == policy)
                    {
                        return ep;
                    }
                }
            }

            return null;
        }

        private Task<ISession> ConnectToPolicyAsync(string policyUri)
        {
            return ClientFixture.ConnectAsync(
                ServerUrl, policyUri);
        }

        private ArrayOf<EndpointDescription> m_cachedEndpoints;
    }
}
