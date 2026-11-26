// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;

namespace Opc.Ua.Client.Tests.Identity
{
    [TestFixture]
    [Category("Client")]
    [Category("Identity")]
    public sealed class CompositeClientIdentityProviderBuilderTests
    {
        [Test]
        public void AddRejectsNullProvider()
        {
            var builder = new CompositeClientIdentityProviderBuilder();

            Assert.That(
                () => builder.Add(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task BuildPreservesAddedProviderOrderAsync()
        {
            UserTokenPolicy policy = CreatePolicy(UserTokenType.UserName);
            IdentitySelectionContext context = CreateContext(policy);
            var first = new StubProvider("first", UserTokenType.UserName);
            var second = new StubProvider("second", UserTokenType.UserName);

            CompositeClientIdentityProvider provider = new CompositeClientIdentityProviderBuilder()
                .Add(first)
                .Add(second)
                .Build();

            IUserIdentity identity = await provider.GetIdentityAsync(policy, context).ConfigureAwait(false);

            Assert.That(identity.DisplayName, Is.EqualTo("first"));
            Assert.That(first.GetIdentityCallCount, Is.EqualTo(1));
            Assert.That(second.GetIdentityCallCount, Is.Zero);
        }

        [Test]
        public void AddUserNameOptionsValidatesInputs()
        {
            var builder = new CompositeClientIdentityProviderBuilder();
            var registry = new Mock<ISecretRegistry>();

            Assert.That(
                () => builder.AddUserName(null!, registry.Object),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => builder.AddUserName(_ => { }, null!),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => builder.AddUserName(_ => { }, registry.Object),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => builder.AddUserName(
                    options =>
                    {
                        options.UserName = "user";
                        options.SecretName = "password";
                        options.SecretStoreType = "memory";
                    },
                    registry.Object),
                Throws.Nothing);
        }

        [Test]
        public void AddX509OptionsValidatesInputsAndAcceptsSubjectOrThumbprint()
        {
            var builder = new CompositeClientIdentityProviderBuilder();
            var provider = new Mock<ICertificateProvider>();
            var passwords = new Mock<ICertificatePasswordProvider>();

            Assert.That(
                () => builder.AddX509(null!, provider.Object, passwords.Object),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => builder.AddX509(_ => { }, null!, passwords.Object),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => builder.AddX509(_ => { }, provider.Object, null!),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => builder.AddX509(
                    options =>
                    {
                        options.StoreType = "Directory";
                        options.StorePath = "pki";
                    },
                    provider.Object,
                    passwords.Object),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => builder.AddX509(
                    options =>
                    {
                        options.StoreType = "Directory";
                        options.StorePath = "pki";
                        options.SubjectName = "CN=test";
                        options.Thumbprint = "thumb";
                    },
                    provider.Object,
                    passwords.Object),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => builder.AddX509(
                    options =>
                    {
                        options.StoreType = "Directory";
                        options.StorePath = "pki";
                        options.SubjectName = "CN=test";
                    },
                    provider.Object,
                    passwords.Object),
                Throws.Nothing);
        }

        [Test]
        public void AddIssuedTokenOptionsValidatesInputsAndProfileUri()
        {
            var builder = new CompositeClientIdentityProviderBuilder();
            var tokenProvider = new Mock<IAccessTokenProvider>();

            Assert.That(
                () => builder.AddIssuedToken(null!, tokenProvider.Object),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => builder.AddIssuedToken(_ => { }, null!),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => builder.AddIssuedToken(_ => { }, tokenProvider.Object),
                Throws.Nothing);
            Assert.That(
                () => builder.AddIssuedToken(
                    options => options.ProfileUri = Profiles.JwtUserToken,
                    tokenProvider.Object),
                Throws.Nothing);
        }

        [Test]
        public void AddConvenienceProvidersReturnBuilderForChaining()
        {
            var registry = new Mock<ISecretRegistry>();
            var passwords = new Mock<ICertificatePasswordProvider>();
            var certProvider = new Mock<ICertificateProvider>();
            var tokenProvider = new Mock<IAccessTokenProvider>();

            var builder = new CompositeClientIdentityProviderBuilder();
            CompositeClientIdentityProviderBuilder returned = builder
                .AddAnonymous()
                .AddUserName("user", registry.Object, new SecretIdentifier("name", "store"))
                .AddX509(new CertificateIdentifier(), passwords.Object, certProvider.Object)
                .AddIssuedToken(tokenProvider.Object);

            Assert.That(returned, Is.SameAs(builder));
            Assert.That(builder.Build(), Is.Not.Null);
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
                ServiceMessageContext.CreateEmpty(Opc.Ua.Tests.NUnitTelemetryContext.Create()));
        }

        private sealed class StubProvider : IClientIdentityProvider
        {
            public StubProvider(string displayName, UserTokenType tokenType)
            {
                DisplayName = displayName;
                TokenType = tokenType;
            }

            public string DisplayName { get; }

            public UserTokenType TokenType { get; }

            public int GetIdentityCallCount { get; private set; }

            public System.Collections.Generic.IReadOnlyList<UserTokenType> SupportedTokenTypes => [TokenType];

            public System.Collections.Generic.IReadOnlyList<string> SupportedIssuedTokenProfileUris => [];

            public DateTime ExpiresAt => DateTime.MaxValue;

            public ValueTask<CanSatisfyResult> CanSatisfyAsync(
                UserTokenPolicy policy,
                IdentitySelectionContext context,
                CancellationToken ct = default)
            {
                _ = context;
                _ = ct;
                return new ValueTask<CanSatisfyResult>(
                    policy.TokenType == TokenType
                        ? CanSatisfyResult.Yes
                        : CanSatisfyResult.No("Not supported."));
            }

            public ValueTask<IUserIdentity> GetIdentityAsync(
                UserTokenPolicy policy,
                IdentitySelectionContext context,
                CancellationToken ct = default)
            {
                _ = context;
                _ = ct;
                GetIdentityCallCount++;
                return new ValueTask<IUserIdentity>(new UserIdentity(DisplayName, "password"u8)
                {
                    PolicyId = policy.PolicyId!
                });
            }
        }
    }
}
