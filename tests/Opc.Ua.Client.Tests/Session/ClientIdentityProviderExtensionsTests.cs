// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.Identity
{
    [TestFixture]
    [Category("Identity")]
    public sealed class ClientIdentityProviderExtensionsTests
    {
        [Test]
        public async Task AcquireIdentityAsyncSelectsMatchingPolicyAndStampsPolicyId()
        {
            var fallbackPolicy = new UserTokenPolicy
            {
                PolicyId = "fallback",
                TokenType = UserTokenType.UserName,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            var matchingPolicy = new UserTokenPolicy
            {
                PolicyId = "matching",
                TokenType = UserTokenType.UserName,
                SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss
            };
            var endpoint = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss,
                UserIdentityTokens = [fallbackPolicy, matchingPolicy]
            };
            IServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            var provider = new StubProvider(UserTokenType.UserName);

            IUserIdentity identity = await provider
                .AcquireIdentityAsync(endpoint, messageContext)
                .ConfigureAwait(false);

            Assert.That(provider.SelectedPolicy, Is.SameAs(matchingPolicy));
            Assert.That(provider.SelectionContext.MessageContext, Is.SameAs(messageContext));
            Assert.That(identity.TokenHandler.Token.PolicyId, Is.EqualTo("matching"));
        }

        [Test]
        public async Task AcquireIdentityAsyncPropagatesBadIdentityTokenInvalid()
        {
            var policy = new UserTokenPolicy
            {
                PolicyId = "user",
                TokenType = UserTokenType.UserName,
                SecurityPolicyUri = SecurityPolicies.None
            };
            var endpoint = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens = [policy]
            };
            IServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            var provider = new StubProvider(
                UserTokenType.UserName,
                ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenInvalid,
                    "identity token invalid"));

            ServiceResultException ex = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider.AcquireIdentityAsync(endpoint, messageContext).ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task AcquireIdentityAsyncRejectsNullEndpoint()
        {
            IServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            var provider = new StubProvider(UserTokenType.UserName);

            ArgumentNullException ex = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await provider.AcquireIdentityAsync(null!, messageContext).ConfigureAwait(false));
            Assert.That(ex.ParamName, Is.EqualTo("endpointDescription"));
        }

        [Test]
        public async Task AcquireIdentityAsyncRejectsNullMessageContext()
        {
            var endpoint = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens = []
            };
            var provider = new StubProvider(UserTokenType.UserName);

            ArgumentNullException ex = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await provider.AcquireIdentityAsync(endpoint, null!).ConfigureAwait(false));
            Assert.That(ex.ParamName, Is.EqualTo("messageContext"));
        }

        [Test]
        public async Task AcquireIdentityAsyncThrowsBadIdentityTokenRejectedWhenNoPolicyMatches()
        {
            var endpoint = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "anon",
                        TokenType = UserTokenType.Anonymous,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ]
            };
            IServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            var provider = new StubProvider(UserTokenType.UserName);

            ServiceResultException ex = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider.AcquireIdentityAsync(endpoint, messageContext).ConfigureAwait(false));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
        }

        private sealed class StubProvider : IClientIdentityProvider
        {
            private readonly UserTokenType m_tokenType;
            private readonly ServiceResultException? m_exception;

            public StubProvider(UserTokenType tokenType, ServiceResultException? exception = null)
            {
                m_tokenType = tokenType;
                m_exception = exception;
            }

            public UserTokenPolicy? SelectedPolicy { get; private set; }

            public IdentitySelectionContext SelectionContext { get; private set; }

            public IReadOnlyList<UserTokenType> SupportedTokenTypes => [m_tokenType];

            public IReadOnlyList<string> SupportedIssuedTokenProfileUris => [];

            public DateTime ExpiresAt => DateTime.MaxValue;

            public ValueTask<CanSatisfyResult> CanSatisfyAsync(
                UserTokenPolicy policy,
                IdentitySelectionContext context,
                CancellationToken ct = default)
            {
                return new ValueTask<CanSatisfyResult>(
                    policy.TokenType == m_tokenType
                        ? CanSatisfyResult.Yes
                        : CanSatisfyResult.No(
                            $"TokenTypeNotSupported (provider handles {m_tokenType}, policy is {policy.TokenType})."));
            }

            public ValueTask<IUserIdentity> GetIdentityAsync(
                UserTokenPolicy policy,
                IdentitySelectionContext context,
                CancellationToken ct = default)
            {
                SelectedPolicy = policy;
                SelectionContext = context;
                if (m_exception != null)
                {
                    throw m_exception;
                }

                return new ValueTask<IUserIdentity>(new UserIdentity("user", "password"u8));
            }
        }
    }
}
