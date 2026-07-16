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
        public void AcquireIdentityAsyncPropagatesBadIdentityTokenInvalid()
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

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider.AcquireIdentityAsync(endpoint, messageContext).ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public void AcquireIdentityAsyncRejectsNullEndpoint()
        {
            IServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            var provider = new StubProvider(UserTokenType.UserName);

            ArgumentNullException ex = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await provider.AcquireIdentityAsync(null!, messageContext).ConfigureAwait(false));
            Assert.That(ex.ParamName, Is.EqualTo("endpointDescription"));
        }

        [Test]
        public void AcquireIdentityAsyncRejectsNullMessageContext()
        {
            var endpoint = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens = []
            };
            var provider = new StubProvider(UserTokenType.UserName);

            ArgumentNullException ex = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await provider.AcquireIdentityAsync(endpoint, null!).ConfigureAwait(false));
            Assert.That(ex.ParamName, Is.EqualTo("messageContext"));
        }

        [Test]
        public void AcquireIdentityAsyncThrowsBadIdentityTokenRejectedWhenNoPolicyMatches()
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

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
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
