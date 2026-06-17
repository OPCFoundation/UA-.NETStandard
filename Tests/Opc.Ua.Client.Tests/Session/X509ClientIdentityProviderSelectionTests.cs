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
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.Identity
{
    [TestFixture]
    [Category("Identity")]
    public sealed class X509ClientIdentityProviderSelectionTests
    {
        [Test]
        public async Task RsaCertSelectsRsaPolicyOverEccPolicy()
        {
            using Certificate cert = CreateRsaCertificate();
            X509ClientIdentityProvider provider = CreateProvider(cert);

            UserTokenPolicy ecc = CreatePolicy("ecc", SecurityPolicies.ECC_nistP256);
            UserTokenPolicy rsa = CreatePolicy("rsa", SecurityPolicies.Basic256Sha256);
            EndpointDescription endpoint = CreateEndpoint(
                SecurityPolicies.Basic256Sha256,
                ecc,
                rsa);

            UserTokenPolicy selected = await provider
                .SelectUserTokenPolicyAsync(CreateContextWithAllPolicies(endpoint))
                .ConfigureAwait(false);

            Assert.That(selected.PolicyId, Is.EqualTo("rsa"));
        }

        [Test]
        public async Task NistP256CertSelectsNistP256PolicyOverNistP384()
        {
            using Certificate cert = CreateEccCertificate(ECCurve.NamedCurves.nistP256);
            X509ClientIdentityProvider provider = CreateProvider(cert);

            UserTokenPolicy nist384 = CreatePolicy("p384", SecurityPolicies.ECC_nistP384);
            UserTokenPolicy nist256 = CreatePolicy("p256", SecurityPolicies.ECC_nistP256);
            UserTokenPolicy rsa = CreatePolicy("rsa", SecurityPolicies.Basic256Sha256);
            EndpointDescription endpoint = CreateEndpoint(
                SecurityPolicies.ECC_nistP256,
                nist384,
                nist256,
                rsa);

            UserTokenPolicy selected = await provider
                .SelectUserTokenPolicyAsync(CreateContextWithAllPolicies(endpoint))
                .ConfigureAwait(false);

            Assert.That(selected.PolicyId, Is.EqualTo("p256"));
        }

        [Test]
        public void NistP384CertWithOnlyNistP256OfferedThrowsBadIdentityTokenRejected()
        {
            using Certificate cert = CreateEccCertificate(ECCurve.NamedCurves.nistP384);
            X509ClientIdentityProvider provider = CreateProvider(cert);

            UserTokenPolicy nist256 = CreatePolicy("p256", SecurityPolicies.ECC_nistP256);
            EndpointDescription endpoint = CreateEndpoint(
                SecurityPolicies.ECC_nistP256,
                nist256);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider
                    .SelectUserTokenPolicyAsync(CreateContextWithAllPolicies(endpoint))
                    .ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            Assert.That(ex.Message, Does.Contain("CertificateAlgorithmMismatch"));
            Assert.That(ex.Message, Does.Contain("NistP384"));
            Assert.That(ex.Message, Does.Contain("NistP256"));
        }

        [Test]
        public void DisabledPolicyIsRejectedWithReasonInDiagnostic()
        {
            using Certificate cert = CreateRsaCertificate();
            X509ClientIdentityProvider provider = CreateProvider(cert);

            UserTokenPolicy basic128 = CreatePolicy("basic128", SecurityPolicies.Basic128Rsa15);
            EndpointDescription endpoint = CreateEndpoint(
                SecurityPolicies.Basic256Sha256,
                basic128);
            var context = new IdentitySelectionContext(
                endpoint,
                endpoint.UserIdentityTokens,
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()),
                new[] { SecurityPolicies.Basic256Sha256 });

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider
                    .SelectUserTokenPolicyAsync(context)
                    .ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            Assert.That(ex.Message, Does.Contain("NotEnabledByClient"));
            Assert.That(ex.Message, Does.Contain(SecurityPolicies.Basic128Rsa15));
            Assert.That(ex.Message, Does.Contain("basic128"));
        }

        [Test]
        public async Task EnabledFilterPicksEnabledPolicyOverDisabledMatch()
        {
            using Certificate cert = CreateRsaCertificate();
            X509ClientIdentityProvider provider = CreateProvider(cert);

            UserTokenPolicy basic128 = CreatePolicy("basic128", SecurityPolicies.Basic128Rsa15);
            UserTokenPolicy basic256 = CreatePolicy("basic256", SecurityPolicies.Basic256Sha256);
            EndpointDescription endpoint = CreateEndpoint(
                SecurityPolicies.Basic256Sha256,
                basic128,
                basic256);
            var context = new IdentitySelectionContext(
                endpoint,
                endpoint.UserIdentityTokens,
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()),
                new[] { SecurityPolicies.Basic256Sha256 });

            UserTokenPolicy selected = await provider
                .SelectUserTokenPolicyAsync(context)
                .ConfigureAwait(false);

            Assert.That(selected.PolicyId, Is.EqualTo("basic256"));
        }

        [Test]
        public void CertificateLoadFailureSurfacesAsCertificateLoadFailedDiagnostic()
        {
            var certId = new CertificateIdentifier
            {
                Thumbprint = "0000000000000000000000000000000000000000",
                SubjectName = "CN=Missing"
            };
            var provider = new X509ClientIdentityProvider(
                certId,
                new FakeCertificatePasswordProvider(),
                new EmptyCertificateProvider());

            UserTokenPolicy policy = CreatePolicy("user", SecurityPolicies.Basic256Sha256);
            EndpointDescription endpoint = CreateEndpoint(
                SecurityPolicies.Basic256Sha256,
                policy);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider
                    .SelectUserTokenPolicyAsync(CreateContextWithAllPolicies(endpoint))
                    .ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            Assert.That(ex.Message, Does.Contain("CertificateLoadFailed"));
            Assert.That(ex.Message, Does.Contain("CN=Missing"));
        }

        [Test]
        public async Task UnsupportedTokenTypeIncludedInDiagnostic()
        {
            UserTokenPolicy anon = CreatePolicy("anon", SecurityPolicies.None, UserTokenType.Anonymous);
            EndpointDescription endpoint = CreateEndpoint(SecurityPolicies.None, anon);

            var provider = new UserNamePasswordIdentityProvider(
                "user1",
                new FakeSecretRegistry(),
                new SecretIdentifier("password", "fake"));

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider
                    .SelectUserTokenPolicyAsync(CreateContextWithAllPolicies(endpoint))
                    .ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            Assert.That(ex.Message, Does.Contain("TokenTypeNotSupported"));
            Assert.That(ex.Message, Does.Contain("anon"));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public void Rsa1024CertWithBasic256Sha256TriggersKeyLengthMismatch()
        {
            using Certificate cert = CreateRsaCertificate(keySize: 1024);
            X509ClientIdentityProvider provider = CreateProvider(cert);

            UserTokenPolicy basic256 = CreatePolicy("basic256", SecurityPolicies.Basic256Sha256);
            EndpointDescription endpoint = CreateEndpoint(SecurityPolicies.Basic256Sha256, basic256);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider
                    .SelectUserTokenPolicyAsync(CreateContextWithAllPolicies(endpoint))
                    .ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            Assert.That(ex.Message, Does.Contain("CertificateKeyLengthMismatch"));
            Assert.That(ex.Message, Does.Contain("1024-bit"));
        }

        [Test]
        public void EccUserNamePolicyWithRsaInstanceCertTriggersInstanceMismatch()
        {
            // The instance-cert gate fires for UserName/IssuedToken
            // (EccEncryptedSecret encryption) — not X509 / Anonymous.
            var provider = new UserNamePasswordIdentityProvider(
                "user1",
                new FakeSecretRegistry(),
                new SecretIdentifier("password", "fake"));

            UserTokenPolicy ecc = CreatePolicy(
                "ecc",
                SecurityPolicies.ECC_nistP256,
                UserTokenType.UserName);
            EndpointDescription endpoint = CreateEndpoint(SecurityPolicies.ECC_nistP256, ecc);
            IdentitySelectionContext context = CreateContextWithAllPolicies(endpoint) with
            {
                ClientInstanceCertificateAlgorithm = CertificateKeyAlgorithm.RSA,
                ClientInstanceCertificateKeySize = 2048
            };

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider
                    .SelectUserTokenPolicyAsync(context)
                    .ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            Assert.That(ex.Message, Does.Contain("ClientInstanceCertificateAlgorithmMismatch"));
            Assert.That(ex.Message, Does.Contain("RSA"));
            Assert.That(ex.Message, Does.Contain("NistP256"));
        }

        [Test]
        public void EccNistP256InstanceCertRejectsNistP384UserNamePolicy()
        {
            var provider = new UserNamePasswordIdentityProvider(
                "user1",
                new FakeSecretRegistry(),
                new SecretIdentifier("password", "fake"));

            UserTokenPolicy ecc = CreatePolicy(
                "p384",
                SecurityPolicies.ECC_nistP384,
                UserTokenType.UserName);
            EndpointDescription endpoint = CreateEndpoint(SecurityPolicies.ECC_nistP384, ecc);
            IdentitySelectionContext context = CreateContextWithAllPolicies(endpoint) with
            {
                ClientInstanceCertificateAlgorithm = CertificateKeyAlgorithm.NistP256
            };

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider
                    .SelectUserTokenPolicyAsync(context)
                    .ConfigureAwait(false));

            Assert.That(ex.Message, Does.Contain("ClientInstanceCertificateAlgorithmMismatch"));
            Assert.That(ex.Message, Does.Contain("NistP256"));
            Assert.That(ex.Message, Does.Contain("NistP384"));
        }

        [Test]
        public async Task EccNistP256InstanceCertSelectsMatchingUserNamePolicy()
        {
            var provider = new UserNamePasswordIdentityProvider(
                "user1",
                new FakeSecretRegistry(),
                new SecretIdentifier("password", "fake"));

            UserTokenPolicy ecc = CreatePolicy(
                "p256",
                SecurityPolicies.ECC_nistP256,
                UserTokenType.UserName);
            EndpointDescription endpoint = CreateEndpoint(SecurityPolicies.ECC_nistP256, ecc);
            IdentitySelectionContext context = CreateContextWithAllPolicies(endpoint) with
            {
                ClientInstanceCertificateAlgorithm = CertificateKeyAlgorithm.NistP256
            };

            UserTokenPolicy selected = await provider
                .SelectUserTokenPolicyAsync(context)
                .ConfigureAwait(false);

            Assert.That(selected.PolicyId, Is.EqualTo("p256"));
        }

        [Test]
        public async Task X509UserTokenIsExemptFromInstanceCertCurveGate()
        {
            // X509IdentityTokenHandler.EncryptAsync is a no-op (the
            // token carries a DER cert + signature using the user
            // cert's private key); the instance-cert curve is
            // irrelevant, so the gate must NOT block this flow.
            using Certificate userCert = CreateEccCertificate(ECCurve.NamedCurves.nistP256);
            X509ClientIdentityProvider provider = CreateProvider(userCert);

            UserTokenPolicy ecc = CreatePolicy("ecc", SecurityPolicies.ECC_nistP256);
            EndpointDescription endpoint = CreateEndpoint(SecurityPolicies.ECC_nistP256, ecc);
            IdentitySelectionContext context = CreateContextWithAllPolicies(endpoint) with
            {
                ClientInstanceCertificateAlgorithm = CertificateKeyAlgorithm.RSA,
                ClientInstanceCertificateKeySize = 2048
            };

            UserTokenPolicy selected = await provider
                .SelectUserTokenPolicyAsync(context)
                .ConfigureAwait(false);

            Assert.That(selected.PolicyId, Is.EqualTo("ecc"));
        }

        [Test]
        public async Task AnonymousTokenIsExemptFromInstanceCertCurveGate()
        {
            var provider = new AnonymousIdentityProvider();

            UserTokenPolicy anon = CreatePolicy(
                "anon",
                SecurityPolicies.ECC_nistP256,
                UserTokenType.Anonymous);
            EndpointDescription endpoint = CreateEndpoint(SecurityPolicies.ECC_nistP256, anon);
            IdentitySelectionContext context = CreateContextWithAllPolicies(endpoint) with
            {
                ClientInstanceCertificateAlgorithm = CertificateKeyAlgorithm.RSA,
                ClientInstanceCertificateKeySize = 2048
            };

            UserTokenPolicy selected = await provider
                .SelectUserTokenPolicyAsync(context)
                .ConfigureAwait(false);

            Assert.That(selected.PolicyId, Is.EqualTo("anon"));
        }

        [Test]
        public void BoundEphemeralKeyPinsSelectionAndNamesRenewApi()
        {
            using Certificate userCert = CreateEccCertificate(ECCurve.NamedCurves.nistP256);
            X509ClientIdentityProvider provider = CreateProvider(userCert);

            UserTokenPolicy p384 = CreatePolicy("p384", SecurityPolicies.ECC_nistP384);
            EndpointDescription endpoint = CreateEndpoint(SecurityPolicies.ECC_nistP384, p384);
            IdentitySelectionContext context = CreateContextWithAllPolicies(endpoint) with
            {
                ClientInstanceCertificateAlgorithm = CertificateKeyAlgorithm.NistP384,
                CurrentEphemeralKeyPolicyUri = SecurityPolicies.ECC_nistP256
            };

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider
                    .SelectUserTokenPolicyAsync(context)
                    .ConfigureAwait(false));

            Assert.That(ex.Message, Does.Contain("EphemeralKeyPolicyMismatch"));
            Assert.That(ex.Message, Does.Contain(SecurityPolicies.ECC_nistP256));
            Assert.That(ex.Message, Does.Contain("UpdateIdentityAsync"));
            Assert.That(ex.Message, Does.Contain("overrideUserTokenPolicyUri"));
        }

        [Test]
        public async Task BoundEphemeralKeyAllowsMatchingPolicy()
        {
            using Certificate userCert = CreateEccCertificate(ECCurve.NamedCurves.nistP256);
            X509ClientIdentityProvider provider = CreateProvider(userCert);

            UserTokenPolicy p256 = CreatePolicy("p256", SecurityPolicies.ECC_nistP256);
            UserTokenPolicy p384 = CreatePolicy("p384", SecurityPolicies.ECC_nistP384);
            EndpointDescription endpoint = CreateEndpoint(SecurityPolicies.ECC_nistP256, p384, p256);
            IdentitySelectionContext context = CreateContextWithAllPolicies(endpoint) with
            {
                ClientInstanceCertificateAlgorithm = CertificateKeyAlgorithm.NistP256,
                CurrentEphemeralKeyPolicyUri = SecurityPolicies.ECC_nistP256
            };

            UserTokenPolicy selected = await provider
                .SelectUserTokenPolicyAsync(context)
                .ConfigureAwait(false);

            Assert.That(selected.PolicyId, Is.EqualTo("p256"));
        }

        [Test]
        public void DiagnosticTruncatesLongStringsAndCapsRejectedPolicyCount()
        {
            // Build an endpoint with many offered policies and very long
            // server-supplied strings to exercise the bounded-diagnostic
            // hardening (CR 7.1 / CR/SR 3.7).
            var provider = new UserNamePasswordIdentityProvider(
                "user1",
                new FakeSecretRegistry(),
                new SecretIdentifier("password", "fake"));

            string longString = new('A', 4096);
            var policies = new List<UserTokenPolicy>();
            for (int i = 0; i < 50; i++)
            {
                policies.Add(new UserTokenPolicy
                {
                    PolicyId = longString + i,
                    TokenType = UserTokenType.Certificate,
                    SecurityPolicyUri = longString
                });
            }

            var endpoint = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens = [.. policies]
            };
            var context = new IdentitySelectionContext(
                endpoint,
                endpoint.UserIdentityTokens,
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider
                    .SelectUserTokenPolicyAsync(context)
                    .ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            Assert.That(ex.Message, Does.Contain("…(truncated)"));
            Assert.That(ex.Message, Does.Contain("more rejected policies"));
            // Diagnostic must not contain the full 4 KiB string verbatim
            // (we truncate at 256 chars).
            Assert.That(ex.Message, Does.Not.Contain(longString));
            // And must not contain every single PolicyId (cap is 32).
            Assert.That(ex.Message, Does.Not.Contain(longString + "49"));
        }

        private static X509ClientIdentityProvider CreateProvider(Certificate cert)
        {
            var certId = new CertificateIdentifier
            {
                Thumbprint = cert.Thumbprint,
                SubjectName = cert.Subject
            };
            return new X509ClientIdentityProvider(
                certId,
                new FakeCertificatePasswordProvider(),
                new InMemoryCertificateProvider(cert));
        }

        private static Certificate CreateRsaCertificate(ushort keySize = 0)
        {
            ICertificateBuilder builder = CertificateBuilder.Create("CN=User")
                .SetNotBefore(DateTime.UtcNow.AddMinutes(-5))
                .SetLifeTime(TimeSpan.FromDays(1));
            if (keySize != 0)
            {
                return builder.SetRSAKeySize(keySize).CreateForRSA();
            }
            return builder.CreateForRSA();
        }

        private static Certificate CreateEccCertificate(ECCurve curve)
        {
            return CertificateBuilder.Create("CN=User")
                .SetNotBefore(DateTime.UtcNow.AddMinutes(-5))
                .SetLifeTime(TimeSpan.FromDays(1))
                .SetECCurve(curve)
                .CreateForECDsa();
        }

        private static UserTokenPolicy CreatePolicy(
            string id,
            string securityPolicyUri,
            UserTokenType tokenType = UserTokenType.Certificate)
        {
            return new UserTokenPolicy
            {
                PolicyId = id,
                TokenType = tokenType,
                SecurityPolicyUri = securityPolicyUri
            };
        }

        private static EndpointDescription CreateEndpoint(
            string securityPolicyUri,
            params UserTokenPolicy[] policies)
        {
            return new EndpointDescription
            {
                SecurityPolicyUri = securityPolicyUri,
                UserIdentityTokens = [.. policies]
            };
        }

        private static IdentitySelectionContext CreateContextWithAllPolicies(EndpointDescription endpoint)
        {
            // Allows every offered SecurityPolicyUri so the curve / token-type filter is the only gate.
            var enabled = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(endpoint.SecurityPolicyUri))
            {
                enabled.Add(endpoint.SecurityPolicyUri);
            }
            foreach (UserTokenPolicy policy in endpoint.UserIdentityTokens)
            {
                if (!string.IsNullOrEmpty(policy.SecurityPolicyUri))
                {
                    enabled.Add(policy.SecurityPolicyUri);
                }
            }

            string[] enabledUris = new string[enabled.Count];
            enabled.CopyTo(enabledUris);
            return new IdentitySelectionContext(
                endpoint,
                endpoint.UserIdentityTokens,
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()),
                enabledUris);
        }

        private sealed class FakeCertificatePasswordProvider : ICertificatePasswordProvider
        {
            public char[] GetPassword(CertificateIdentifier certificateIdentifier)
            {
                return [];
            }
        }

        private sealed class InMemoryCertificateProvider : ICertificateProvider
        {
            private readonly Certificate m_certificate;

            public InMemoryCertificateProvider(Certificate certificate)
            {
                m_certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
            }

            public Certificate? TryGetPrivateKeyCertificate(string thumbprint)
            {
                if (string.IsNullOrEmpty(thumbprint) ||
                    !StringComparer.OrdinalIgnoreCase.Equals(thumbprint, m_certificate.Thumbprint))
                {
                    return null;
                }

                return m_certificate.AddRef();
            }

            public ValueTask<Certificate?> GetPrivateKeyCertificateAsync(
                CertificateIdentifier identifier,
                ICertificatePasswordProvider? passwordProvider = null,
                string? applicationUri = null,
                CancellationToken ct = default)
            {
                return Matches(identifier)
                    ? new ValueTask<Certificate?>(m_certificate.AddRef())
                    : new ValueTask<Certificate?>((Certificate?)null);
            }

            private bool Matches(CertificateIdentifier identifier)
            {
                if (identifier == null)
                {
                    return false;
                }
                if (!string.IsNullOrEmpty(identifier.Thumbprint))
                {
                    return StringComparer.OrdinalIgnoreCase.Equals(
                        identifier.Thumbprint,
                        m_certificate.Thumbprint);
                }
                return string.Equals(
                    identifier.SubjectName,
                    m_certificate.Subject,
                    StringComparison.Ordinal);
            }
        }

        private sealed class EmptyCertificateProvider : ICertificateProvider
        {
            public Certificate? TryGetPrivateKeyCertificate(string thumbprint)
            {
                return null;
            }

            public ValueTask<Certificate?> GetPrivateKeyCertificateAsync(
                CertificateIdentifier identifier,
                ICertificatePasswordProvider? passwordProvider = null,
                string? applicationUri = null,
                CancellationToken ct = default)
            {
                return new ValueTask<Certificate?>((Certificate?)null);
            }
        }

        private sealed class FakeSecretRegistry : ISecretRegistry
        {
            public void RegisterStore(ISecretStore store)
            {
            }

            public ISecret? TryGet(SecretIdentifier id)
            {
                return null;
            }

            public ValueTask<ISecret?> GetAsync(
                SecretIdentifier id,
                CancellationToken ct = default)
            {
                return new ValueTask<ISecret?>((ISecret?)null);
            }
        }
    }
}
