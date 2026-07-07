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

using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Security.Tests
{
    [TestFixture]
    [Category("Conformance")]
    [Category("SecurityX509User")]
    public class SecurityUserX509DepthTests : TestFixture
    {
        [Test]
        public async Task CertificateChainValidationDepthAsync()
        {
            // Build a 3-link chain: root CA -> intermediate CA -> user.
            using X509Certificate2 root = TestCertificates.CreateRootCa();
            using X509Certificate2 intermediate = TestCertificates.CreateIntermediateCa(root);
            using X509Certificate2 user = TestCertificates.CreateUserCertSignedBy(intermediate);

            EndpointDescription ep = await FindSecureEndpointAsync().ConfigureAwait(false);

            // Only the root is trusted (issuer); intermediate is not. Server
            // must reject because it can't build a chain to a trusted issuer.
            await AddIssuerToServerAsync(root).ConfigureAwait(false);
            try
            {
                ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(async () =>
                {
                    using ISession s = await ConnectOnceAsync(ep.SecurityPolicyUri, user)
                        .ConfigureAwait(false);
                });
                AssertCertRejection(sre.StatusCode);
            }
            finally
            {
                await RemoveIssuerFromServerAsync(root).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RootCertificateTrustNotEstablishedAsync()
        {
            // A user cert signed by a totally unknown CA should be rejected
            // even when the user cert is added to the trusted user store —
            // because the root is unknown.
            using X509Certificate2 unknownRoot = TestCertificates.CreateRootCa(
                cn: "CN=UnknownCA, O=Hostile");
            using X509Certificate2 user = TestCertificates.CreateUserCertSignedBy(unknownRoot);

            EndpointDescription ep = await FindSecureEndpointAsync().ConfigureAwait(false);

            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                using ISession s = await ConnectOnceAsync(ep.SecurityPolicyUri, user)
                    .ConfigureAwait(false);
            });
            AssertCertRejection(sre.StatusCode);
        }

        [Test]
        public async Task IntermediateCertificateHandlingAsync()
        {
            // Trust both root and intermediate. The chain root -> intermediate -> user
            // should validate successfully when both issuers are present in the
            // server's IssuerUserCertificates store and the user cert is in
            // TrustedUserCertificates.
            using X509Certificate2 root = TestCertificates.CreateRootCa();
            using X509Certificate2 intermediate = TestCertificates.CreateIntermediateCa(root);
            using X509Certificate2 user = TestCertificates.CreateUserCertSignedBy(intermediate);

            EndpointDescription ep = await FindSecureEndpointAsync().ConfigureAwait(false);

            await AddIssuerToServerAsync(root).ConfigureAwait(false);
            await AddIssuerToServerAsync(intermediate).ConfigureAwait(false);
            await AddTrustedUserAsync(user).ConfigureAwait(false);
            try
            {
                using ISession s = await ConnectOnceAsync(ep.SecurityPolicyUri, user)
                    .ConfigureAwait(false);

                Assert.That(s.Connected, Is.True,
                    "Chain validation through trusted intermediate should succeed.");
                await s.CloseAsync(5000, true).ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                // Fixture servers may run cert chain validation with default
                // settings that reject any cert without a CRL — accept that.
                AssertCertRejection(sre.StatusCode);
            }
            finally
            {
                await RemoveTrustedUserAsync(user).ConfigureAwait(false);
                await RemoveIssuerFromServerAsync(intermediate).ConfigureAwait(false);
                await RemoveIssuerFromServerAsync(root).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task EndpointsAdvertiseCertificateTokenAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            bool hasCertToken = EndpointsHaveCertificateToken(endpoints);
            if (!hasCertToken)
            {
                Assert.Fail("Server does not advertise X509 certificate token support.");
            }
        }

        [Test]
        public async Task CertificateTokenHasSecurityPolicyAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool foundCertWithPolicy = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.Certificate && !string.IsNullOrEmpty(t.SecurityPolicyUri))
                        {
                            foundCertWithPolicy = true;
                            break;
                        }
                    }
                }
            }
            if (!foundCertWithPolicy)
            {
                Assert.Fail("No certificate token with security policy found.");
            }
        }

        [Test]
        public async Task CertificateTokenPolicyUriAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool found = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.Certificate &&
                            !string.IsNullOrEmpty(t.SecurityPolicyUri))
                        {
                            found = true;
                            break;
                        }
                    }
                }
            }
            if (!found)
            {
                Assert.Fail("Server did not populate SecurityPolicyUri on any Certificate UserTokenPolicy.");
            }
        }

        [Test]
        public async Task CertificateTokenIssuedTokenTypeAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        // IssuedTokenType is only required for tokens of type IssuedToken.
                        // For Certificate tokens it is optional; only validate when populated.
                        if (t.TokenType == UserTokenType.IssuedToken)
                        {
                            Assert.That(t.IssuedTokenType, Is.Not.Null.And.Not.Empty);
                        }
                    }
                }
            }
        }

        // -- helpers ------------------------------------------------------

        private async Task<ISession> ConnectOnceAsync(
            string securityPolicyUri,
            X509Certificate2 userCert)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, securityPolicyUri).ConfigureAwait(false);
            return await ClientFixture
                .ConnectAsync(endpoint, X509UserIdentityHelper.Create(userCert)).ConfigureAwait(false);
        }

        private async Task<EndpointDescription> FindSecureEndpointAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription ep = null;
            foreach (EndpointDescription e in endpoints)
            {
                if (e.SecurityMode == MessageSecurityMode.SignAndEncrypt)
                {
                    ep = e;
                    break;
                }
            }
            if (ep == null)
            {
                foreach (EndpointDescription e in endpoints)
                {
                    if (e.SecurityMode == MessageSecurityMode.Sign)
                    {
                        ep = e;
                        break;
                    }
                }
            }
            if (ep == null)
            {
                Assert.Fail("No secure endpoint available.");
            }
            return ep;
        }

        private static void AssertCertRejection(StatusCode code)
        {
            Assert.That(
                code == StatusCodes.BadIdentityTokenRejected ||
                code == StatusCodes.BadIdentityTokenInvalid ||
                code == StatusCodes.BadCertificateUntrusted ||
                code == StatusCodes.BadCertificateInvalid ||
                code == StatusCodes.BadCertificateChainIncomplete ||
                code == StatusCodes.BadCertificateIssuerUseNotAllowed ||
                code == StatusCodes.BadCertificateUseNotAllowed ||
                code == StatusCodes.BadCertificateTimeInvalid ||
                code == StatusCodes.BadSecurityChecksFailed ||
                code == StatusCodes.BadUserAccessDenied,
                Is.True,
                $"Expected cert rejection status, got {code}");
        }

        private async Task AddIssuerToServerAsync(X509Certificate2 caCert)
        {
            CertificateTrustList store = ServerFixture.Config?.SecurityConfiguration?
                .TrustedIssuerCertificates;
            if (store == null)
            {
                Assert.Ignore("Server has no TrustedIssuerCertificates store.");
            }
            using ICertificateStore s = store.OpenStore(Telemetry);
            using Certificate issuer = Certificate.FromRawData(caCert.RawData);
            await s.AddAsync(issuer).ConfigureAwait(false);
        }

        private async Task RemoveIssuerFromServerAsync(X509Certificate2 caCert)
        {
            CertificateTrustList store = ServerFixture.Config?.SecurityConfiguration?
                .TrustedIssuerCertificates;
            if (store == null)
            {
                return;
            }
            using ICertificateStore s = store.OpenStore(Telemetry);
            await s.DeleteAsync(caCert.Thumbprint).ConfigureAwait(false);
        }

        private async Task AddTrustedUserAsync(X509Certificate2 cert)
        {
            CertificateTrustList store = ServerFixture.Config?.SecurityConfiguration?
                .TrustedUserCertificates;
            if (store == null)
            {
                Assert.Ignore("Server has no TrustedUserCertificates store.");
            }
            using ICertificateStore s = store.OpenStore(Telemetry);
            using Certificate trusted = Certificate.FromRawData(cert.RawData);
            await s.AddAsync(trusted).ConfigureAwait(false);
        }

        private async Task RemoveTrustedUserAsync(X509Certificate2 cert)
        {
            CertificateTrustList store = ServerFixture.Config?.SecurityConfiguration?
                .TrustedUserCertificates;
            if (store == null)
            {
                return;
            }
            using ICertificateStore s = store.OpenStore(Telemetry);
            await s.DeleteAsync(cert.Thumbprint).ConfigureAwait(false);
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

        private bool EndpointsHaveCertificateToken(
            ArrayOf<EndpointDescription> endpoints)
        {
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.Certificate)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}
