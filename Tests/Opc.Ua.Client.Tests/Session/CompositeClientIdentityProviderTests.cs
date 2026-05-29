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
    public sealed class CompositeClientIdentityProviderTests
    {
        [Test]
        public async Task GetIdentityAsyncUsesRegistrationOrder()
        {
            UserTokenPolicy policy = CreatePolicy(UserTokenType.UserName);
            IdentitySelectionContext context = CreateContext(policy);
            var first = new StubProvider("first", UserTokenType.UserName, DateTime.UtcNow.AddMinutes(10));
            var second = new StubProvider("second", UserTokenType.UserName, DateTime.UtcNow.AddMinutes(5));
            var provider = new CompositeClientIdentityProvider(first, second);

            IUserIdentity identity = await provider.GetIdentityAsync(policy, context).ConfigureAwait(false);

            Assert.That(identity.DisplayName, Is.EqualTo("first"));
            Assert.That(first.GetIdentityCallCount, Is.EqualTo(1));
            Assert.That(second.GetIdentityCallCount, Is.Zero);
        }

        [Test]
        public async Task ExpiresAtAggregatesQueriedProviders()
        {
            DateTime later = DateTime.UtcNow.AddMinutes(10);
            DateTime earlier = DateTime.UtcNow.AddMinutes(5);
            var userName = new StubProvider("user", UserTokenType.UserName, later);
            var certificate = new StubProvider("cert", UserTokenType.Certificate, earlier);
            var provider = new CompositeClientIdentityProvider(userName, certificate);
            UserTokenPolicy userNamePolicy = CreatePolicy(UserTokenType.UserName);
            UserTokenPolicy certificatePolicy = CreatePolicy(UserTokenType.Certificate);

            await provider.GetIdentityAsync(userNamePolicy, CreateContext(userNamePolicy)).ConfigureAwait(false);
            Assert.That(provider.ExpiresAt, Is.EqualTo(later));

            await provider.GetIdentityAsync(certificatePolicy, CreateContext(certificatePolicy)).ConfigureAwait(false);
            Assert.That(provider.ExpiresAt, Is.EqualTo(earlier));
        }

        [Test]
        public void GetIdentityAsyncRejectsUnmatchedPolicy()
        {
            UserTokenPolicy policy = CreatePolicy(UserTokenType.Certificate);
            IdentitySelectionContext context = CreateContext(policy);
            var provider = new CompositeClientIdentityProvider(
                new StubProvider("user", UserTokenType.UserName, DateTime.MaxValue));

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider.GetIdentityAsync(policy, context).ConfigureAwait(false));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
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

        private sealed class StubProvider : IClientIdentityProvider
        {
            public StubProvider(string displayName, UserTokenType tokenType, DateTime expiresAt)
            {
                DisplayName = displayName;
                TokenType = tokenType;
                ExpiresAt = expiresAt;
            }

            public string DisplayName { get; }

            public UserTokenType TokenType { get; }

            public int GetIdentityCallCount { get; private set; }

            public IReadOnlyList<UserTokenType> SupportedTokenTypes => new[] { TokenType };

            public IReadOnlyList<string> SupportedIssuedTokenProfileUris => Array.Empty<string>();

            public DateTime ExpiresAt { get; }

            public bool CanSatisfy(UserTokenPolicy policy, IdentitySelectionContext context)
            {
                return policy.TokenType == TokenType;
            }

            public ValueTask<IUserIdentity> GetIdentityAsync(
                UserTokenPolicy policy,
                IdentitySelectionContext context,
                CancellationToken ct = default)
            {
                GetIdentityCallCount++;
                var identity = new UserIdentity(DisplayName, "password"u8)
                {
                    PolicyId = policy.PolicyId
                };
                return new ValueTask<IUserIdentity>(identity);
            }
        }
    }
}
