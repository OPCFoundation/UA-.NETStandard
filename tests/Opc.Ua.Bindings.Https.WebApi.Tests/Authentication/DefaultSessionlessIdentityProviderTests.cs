// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using Opc.Ua.Bindings.WebApi;

namespace Opc.Ua.Bindings.Https.WebApi.Tests.Authentication
{
    /// <summary>
    /// Unit tests for the default ASP.NET Core principal to OPC UA identity
    /// mapper used by sessionless REST calls.
    /// </summary>
    [TestFixture]
    [Category("WebApiAuthentication")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DefaultSessionlessIdentityProviderTests
    {
        [Test]
        public void UnauthenticatedReturnsNull()
        {
            var provider = new DefaultSessionlessIdentityProvider();
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity())
            };

            IUserIdentity? identity = provider.Resolve(context);

            Assert.That(identity, Is.Null);
        }

        [Test]
        public void AuthenticatedWithNameClaimReturnsNull()
        {
            // Never synthesize a UserName token with an empty /
            // placeholder password. The upstream-authenticated principal
            // (if any) flows out-of-band via
            // SecureChannelContext.UpstreamIdentity. Custom providers
            // (e.g. JwtClaimSessionlessIdentityProvider) own the richer
            // mapping.
            var provider = new DefaultSessionlessIdentityProvider();
            ClaimsIdentity claimsIdentity = new(
                [new Claim(ClaimTypes.Name, "alice")],
                authenticationType: "Test");
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(claimsIdentity)
            };

            IUserIdentity? identity = provider.Resolve(context);

            Assert.That(identity, Is.Null,
                "DefaultSessionlessIdentityProvider must never forge a UserName " +
                "token with an empty password. Upstream identity flows through " +
                "SecureChannelContext.UpstreamIdentity.");
        }

        [Test]
        public void AuthenticatedWithoutNameClaimReturnsNull()
        {
            var provider = new DefaultSessionlessIdentityProvider();
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Test"))
            };

            IUserIdentity? identity = provider.Resolve(context);

            Assert.That(identity, Is.Null,
                "Authenticated principal with no name claim still returns null — " +
                "the binding does not synthesize identity tokens.");
        }
    }
}
