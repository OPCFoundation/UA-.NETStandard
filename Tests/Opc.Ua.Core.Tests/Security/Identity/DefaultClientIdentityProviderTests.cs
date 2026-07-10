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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Identity
{
    [TestFixture]
    [Category("Identity")]
    [Parallelizable]
    public sealed class DefaultClientIdentityProviderTests
    {
        [Test]
        public async Task AnonymousProviderReturnsAnonymousIdentity()
        {
            var provider = new AnonymousIdentityProvider();
            UserTokenPolicy policy = CreatePolicy(UserTokenType.Anonymous);

            IUserIdentity identity = await provider.GetIdentityAsync(policy, CreateContext(policy)).ConfigureAwait(false);

            Assert.That(identity.TokenType, Is.EqualTo(UserTokenType.Anonymous));
            Assert.That(identity.PolicyId, Is.EqualTo(policy.PolicyId));
            Assert.That(provider.ExpiresAt, Is.EqualTo(DateTime.MaxValue));
            Assert.That(provider.SupportedTokenTypes, Is.EqualTo(s_anonymousTokenTypes));
            Assert.That(provider.SupportedIssuedTokenProfileUris, Is.Empty);
        }

        [Test]
        public async Task AnonymousProviderRejectsUserNamePolicy()
        {
            var provider = new AnonymousIdentityProvider();
            UserTokenPolicy policy = CreatePolicy(UserTokenType.UserName);

            CanSatisfyResult result = await provider.CanSatisfyAsync(policy, CreateContext(policy)).ConfigureAwait(false);

            Assert.That(result.CanSatisfy, Is.False);
            Assert.That(result.RejectionReason, Does.Contain("TokenTypeNotSupported"));
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider.GetIdentityAsync(policy, CreateContext(policy)).ConfigureAwait(false));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public async Task UserNameProviderResolvesAndDisposesPasswordSecret()
        {
            var registry = new FakeSecretRegistry("password"u8.ToArray());
            var id = new SecretIdentifier("password", "fake");
            var provider = new UserNamePasswordIdentityProvider("user1", registry, id);
            UserTokenPolicy policy = CreatePolicy(UserTokenType.UserName);

            IUserIdentity identity = await provider.GetIdentityAsync(policy, CreateContext(policy)).ConfigureAwait(false);

            Assert.That(identity.TokenType, Is.EqualTo(UserTokenType.UserName));
            Assert.That(identity.DisplayName, Is.EqualTo("user1"));
            Assert.That(identity.PolicyId, Is.EqualTo(policy.PolicyId));
            Assert.That(registry.LastSecret.Disposed, Is.True);
            var handler = (UserNameIdentityTokenHandler)identity.TokenHandler;
            Assert.That(handler.DecryptedPassword, Is.EqualTo("password"u8.ToArray()));
        }

        [Test]
        public void UserNameProviderThrowsWhenSecretMissing()
        {
            var provider = new UserNamePasswordIdentityProvider(
                "user1",
                new FakeSecretRegistry(null),
                new SecretIdentifier("password", "fake"));
            UserTokenPolicy policy = CreatePolicy(UserTokenType.UserName);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider.GetIdentityAsync(policy, CreateContext(policy)).ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            Assert.That(ex.Message, Does.Contain("Password secret could not be resolved"));
        }

        [Test]
        public async Task IssuedTokenProviderCopiesTokenAndUpdatesExpiry()
        {
            byte[] tokenBytes = "jwt-token"u8.ToArray();
            DateTime expiresAt = DateTime.UtcNow.AddMinutes(10);
            var accessTokens = new FakeAccessTokenProvider(tokenBytes, expiresAt);
            var provider = new IssuedTokenIdentityProvider(accessTokens);
            UserTokenPolicy policy = CreatePolicy(UserTokenType.IssuedToken);
            policy.IssuedTokenType = Profiles.JwtUserToken;
            policy.IssuerEndpointUrl = /*lang=json,strict*/ "{\"authorityUri\":\"https://issuer.example\"}";

            IUserIdentity identity = await provider.GetIdentityAsync(policy, CreateContext(policy)).ConfigureAwait(false);

            Assert.That(identity.TokenType, Is.EqualTo(UserTokenType.IssuedToken));
            Assert.That(identity.PolicyId, Is.EqualTo(policy.PolicyId));
            Assert.That(provider.ExpiresAt, Is.EqualTo(expiresAt));
            Assert.That(provider.SupportedIssuedTokenProfileUris, Is.EqualTo(s_jwtProfileUris));
            var handler = (IssuedIdentityTokenHandler)identity.TokenHandler;
            Assert.That(handler.DecryptedTokenData, Is.EqualTo("jwt-token"u8.ToArray()));
            Assert.That(tokenBytes, Is.All.EqualTo(0));
        }

        [Test]
        public void IssuedTokenProviderRejectsWrongIssuedTokenProfile()
        {
            var provider = new IssuedTokenIdentityProvider(
                new FakeAccessTokenProvider("jwt-token"u8.ToArray(), DateTime.UtcNow.AddMinutes(10)));
            UserTokenPolicy policy = CreatePolicy(UserTokenType.IssuedToken);
            policy.IssuedTokenType = "urn:wrong-profile";

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider.GetIdentityAsync(policy, CreateContext(policy)).ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            Assert.That(ex.Message, Does.Contain("IssuedTokenProfileNotSupported"));
        }

        [Test]
        public async Task X509ProviderCreatesCertificateIdentity()
        {
            using Certificate certificate = CertificateBuilder.Create("CN=User")
                .SetNotBefore(DateTime.UtcNow.AddMinutes(-5))
                .SetLifeTime(TimeSpan.FromDays(1))
                .CreateForRSA();
            var certificateId = new CertificateIdentifier
            {
                Thumbprint = certificate.Thumbprint,
                SubjectName = certificate.Subject
            };
            var provider = new X509ClientIdentityProvider(
                certificateId,
                new FakeCertificatePasswordProvider(),
                new InMemoryCertificateProvider(certificate));
            UserTokenPolicy policy = CreatePolicy(UserTokenType.Certificate);

            IUserIdentity identity = await provider.GetIdentityAsync(policy, CreateContext(policy)).ConfigureAwait(false);

            Assert.That(identity.TokenType, Is.EqualTo(UserTokenType.Certificate));
            Assert.That(identity.PolicyId, Is.EqualTo(policy.PolicyId));
            Assert.That(provider.ExpiresAt, Is.EqualTo(DateTime.MaxValue));
        }

        [Test]
        public async Task X509ProviderReportsCertificateLoadFailure()
        {
            var provider = new X509ClientIdentityProvider(
                new CertificateIdentifier { SubjectName = "CN=Missing", Thumbprint = "012345" },
                new FakeCertificatePasswordProvider(),
                new EmptyCertificateProvider());
            UserTokenPolicy policy = CreatePolicy(UserTokenType.Certificate);

            CanSatisfyResult result = await provider.CanSatisfyAsync(policy, CreateContext(policy)).ConfigureAwait(false);

            Assert.That(result.CanSatisfy, Is.False);
            Assert.That(result.RejectionReason, Does.Contain("CertificateLoadFailed"));
            Assert.That(result.RejectionReason, Does.Contain("CN=Missing"));
        }

        [Test]
        public void CompositeProviderValidatesConstructorArguments()
        {
            Assert.That(
                () => _ = new CompositeClientIdentityProvider((IEnumerable<IClientIdentityProvider>)null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => _ = new CompositeClientIdentityProvider(Array.Empty<IClientIdentityProvider>()),
                Throws.ArgumentException);
            Assert.That(
                () => _ = new CompositeClientIdentityProvider(new IClientIdentityProvider[] { (IClientIdentityProvider)null }),
                Throws.ArgumentException);
        }

        [Test]
        public async Task CompositeProviderAggregatesCapabilitiesAndDispatchesInOrder()
        {
            DateTime later = DateTime.UtcNow.AddMinutes(10);
            DateTime earlier = DateTime.UtcNow.AddMinutes(5);
            var userName = new StubProvider("user", UserTokenType.UserName, later, ["urn:profile:a"]);
            var certificate = new StubProvider("cert", UserTokenType.Certificate, earlier, ["urn:profile:a", "urn:profile:b"]);
            var provider = new CompositeClientIdentityProvider(userName, certificate);
            UserTokenPolicy userNamePolicy = CreatePolicy(UserTokenType.UserName);
            UserTokenPolicy certificatePolicy = CreatePolicy(UserTokenType.Certificate);

            IUserIdentity userIdentity = await provider
                .GetIdentityAsync(userNamePolicy, CreateContext(userNamePolicy))
                .ConfigureAwait(false);
            Assert.That(userIdentity.DisplayName, Is.EqualTo("user"));
            Assert.That(provider.ExpiresAt, Is.EqualTo(later));

            IUserIdentity certificateIdentity = await provider
                .GetIdentityAsync(certificatePolicy, CreateContext(certificatePolicy))
                .ConfigureAwait(false);
            Assert.That(certificateIdentity.DisplayName, Is.EqualTo("cert"));
            Assert.That(provider.ExpiresAt, Is.EqualTo(earlier));
            Assert.That(provider.SupportedTokenTypes, Is.EquivalentTo(s_userNameAndCertificateTokenTypes));
            Assert.That(provider.SupportedIssuedTokenProfileUris, Is.EquivalentTo(s_stubProfileUris));
        }

        [Test]
        public void CompositeProviderRejectsUnmatchedPolicy()
        {
            UserTokenPolicy policy = CreatePolicy(UserTokenType.Certificate);
            var provider = new CompositeClientIdentityProvider(
                new StubProvider("user", UserTokenType.UserName, DateTime.MaxValue, []));

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider.GetIdentityAsync(policy, CreateContext(policy)).ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            Assert.That(ex.Message, Does.Contain("TokenTypeNotSupported"));
        }

        private static UserTokenPolicy CreatePolicy(UserTokenType tokenType)
        {
            return new UserTokenPolicy
            {
                PolicyId = tokenType.ToString(),
                TokenType = tokenType,
                SecurityPolicyUri = SecurityPolicies.None
            };
        }

        private static IdentitySelectionContext CreateContext(UserTokenPolicy policy)
        {
            var endpoint = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens = [policy]
            };
            return new IdentitySelectionContext(
                endpoint,
                new[] { policy },
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
        }

        private static readonly UserTokenType[] s_anonymousTokenTypes = [UserTokenType.Anonymous];
        private static readonly UserTokenType[] s_userNameAndCertificateTokenTypes =
            [UserTokenType.UserName, UserTokenType.Certificate];
        private static readonly string[] s_jwtProfileUris = [Profiles.JwtUserToken];
        private static readonly string[] s_stubProfileUris = ["urn:profile:a", "urn:profile:b"];

        private sealed class FakeSecretRegistry : ISecretRegistry
        {
            private readonly byte[] m_secretBytes;

            public FakeSecretRegistry(byte[] secretBytes)
            {
                m_secretBytes = secretBytes;
                LastSecret = new FakeSecret([]);
            }

            public FakeSecret LastSecret { get; private set; }

            public void RegisterStore(ISecretStore store)
            {
            }

            public ISecret TryGet(SecretIdentifier id)
            {
                LastSecret = new FakeSecret(m_secretBytes ?? []);
                return m_secretBytes == null ? null : LastSecret;
            }

            public ValueTask<ISecret> GetAsync(
                SecretIdentifier id,
                CancellationToken ct = default)
            {
                LastSecret = new FakeSecret(m_secretBytes ?? []);
                return new ValueTask<ISecret>(m_secretBytes == null ? null : LastSecret);
            }
        }

        private sealed class FakeSecret : ISecret
        {
            private readonly byte[] m_bytes;

            public FakeSecret(byte[] bytes)
            {
                m_bytes = bytes;
            }

            public bool Disposed { get; private set; }

            public ReadOnlySpan<byte> Bytes => m_bytes;

            public void Dispose()
            {
                Disposed = true;
            }
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

            public Certificate TryGetPrivateKeyCertificate(string thumbprint)
            {
                return StringComparer.OrdinalIgnoreCase.Equals(thumbprint, m_certificate.Thumbprint)
                    ? m_certificate.AddRef()
                    : null;
            }

            public ValueTask<Certificate> GetPrivateKeyCertificateAsync(
                CertificateIdentifier identifier,
                ICertificatePasswordProvider passwordProvider = null,
                string applicationUri = null,
                CancellationToken ct = default)
            {
                if (identifier == null)
                {
                    throw new ArgumentNullException(nameof(identifier));
                }

                bool matches = !string.IsNullOrEmpty(identifier.Thumbprint)
                    ? StringComparer.OrdinalIgnoreCase.Equals(identifier.Thumbprint, m_certificate.Thumbprint)
                    : string.Equals(identifier.SubjectName, m_certificate.Subject, StringComparison.Ordinal);
                return new ValueTask<Certificate>(matches ? m_certificate.AddRef() : null);
            }
        }

        private sealed class EmptyCertificateProvider : ICertificateProvider
        {
            public Certificate TryGetPrivateKeyCertificate(string thumbprint)
            {
                return null;
            }

            public ValueTask<Certificate> GetPrivateKeyCertificateAsync(
                CertificateIdentifier identifier,
                ICertificatePasswordProvider passwordProvider = null,
                string applicationUri = null,
                CancellationToken ct = default)
            {
                return new ValueTask<Certificate>((Certificate)null);
            }
        }

        private sealed class FakeAccessTokenProvider : IAccessTokenProvider
        {
            private readonly byte[] m_tokenBytes;
            private readonly DateTime m_expiresAt;

            public FakeAccessTokenProvider(byte[] tokenBytes, DateTime expiresAt)
            {
                m_tokenBytes = tokenBytes;
                m_expiresAt = expiresAt;
            }

            public string AuthorityUri => "https://issuer.example";

            public ValueTask<AccessToken> AcquireAsync(
                AuthorizationServerMetadata metadata,
                CancellationToken ct = default)
            {
#pragma warning disable CA2000 // ownership transfers to the provider under test
                return new ValueTask<AccessToken>(new AccessToken(
                    Profiles.JwtUserToken,
                    m_tokenBytes,
                    m_expiresAt,
                    "jwt"));
#pragma warning restore CA2000
            }
        }

        private sealed class StubProvider : IClientIdentityProvider
        {
            private readonly IReadOnlyList<string> m_profileUris;

            public StubProvider(
                string displayName,
                UserTokenType tokenType,
                DateTime expiresAt,
                IReadOnlyList<string> profileUris)
            {
                DisplayName = displayName;
                TokenType = tokenType;
                ExpiresAt = expiresAt;
                m_profileUris = profileUris;
            }

            public string DisplayName { get; }

            public UserTokenType TokenType { get; }

            public IReadOnlyList<UserTokenType> SupportedTokenTypes => [TokenType];

            public IReadOnlyList<string> SupportedIssuedTokenProfileUris => m_profileUris;

            public DateTime ExpiresAt { get; }

            public ValueTask<CanSatisfyResult> CanSatisfyAsync(
                UserTokenPolicy policy,
                IdentitySelectionContext context,
                CancellationToken ct = default)
            {
                return new ValueTask<CanSatisfyResult>(
                    policy.TokenType == TokenType
                        ? CanSatisfyResult.Yes
                        : CanSatisfyResult.No(
                            $"TokenTypeNotSupported (provider handles {TokenType}, policy is {policy.TokenType})."));
            }

            public ValueTask<IUserIdentity> GetIdentityAsync(
                UserTokenPolicy policy,
                IdentitySelectionContext context,
                CancellationToken ct = default)
            {
                var identity = new UserIdentity(DisplayName, "password"u8)
                {
                    PolicyId = policy.PolicyId
                };
                return new ValueTask<IUserIdentity>(identity);
            }
        }
    }
}

