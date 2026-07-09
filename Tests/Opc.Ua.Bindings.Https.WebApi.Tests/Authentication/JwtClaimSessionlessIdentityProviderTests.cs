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

using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using Opc.Ua.Bindings.WebApi;

namespace Opc.Ua.Bindings.Https.WebApi.Tests.Authentication
{
    /// <summary>
    /// Unit tests for <see cref="JwtClaimSessionlessIdentityProvider"/>:
    /// the projection of JWT-style claims (<c>sub</c> / <c>scope</c> /
    /// <c>roles</c>) onto OPC UA <see cref="IUserIdentity"/> instances.
    /// </summary>
    [TestFixture]
    [Category("WebApiAuthentication")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class JwtClaimSessionlessIdentityProviderTests
    {
        private const string SampleJwt =
            "eyJhbGciOiJub25lIn0.eyJzdWIiOiJhbGljZSJ9.";

        /// <summary>
        /// CA1861: hoisted to static readonly so repeated test invocations do not
        /// re-allocate the same array argument.
        /// </summary>
        private static readonly string[] s_expectedScopes =
            ["read.values", "browse.address-space", "write.values"];

        private static readonly string[] s_expectedRoles =
            ["operator", "observer"];

        private static readonly string[] s_expectedCustomScopes =
            ["a", "b"];

        private static readonly string[] s_expectedCustomRoles =
            ["engineering", "qa"];

        [Test]
        public void UnauthenticatedReturnsNullByDefault()
        {
            var provider = new JwtClaimSessionlessIdentityProvider();
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity())
            };

            IUserIdentity? identity = provider.Resolve(context);

            Assert.That(identity, Is.Null);
        }

        [Test]
        public void UnauthenticatedReturnsAnonymousWhenConfigured()
        {
            var provider = new JwtClaimSessionlessIdentityProvider(
                new JwtClaimSessionlessIdentityProviderOptions
                {
                    ReturnAnonymousForUnauthenticated = true
                });
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity())
            };

            IUserIdentity? identity = provider.Resolve(context);

            Assert.That(identity, Is.Not.Null);
            Assert.That(identity!.TokenHandler.Token, Is.InstanceOf<AnonymousIdentityToken>());
        }

        [Test]
        public void AuthenticatedWithBearerHeaderProjectsIssuedToken()
        {
            var provider = new JwtClaimSessionlessIdentityProvider();
            DefaultHttpContext context = BuildContext(
                bearer: SampleJwt,
                claims: new Claim("sub", "alice"));

            IUserIdentity? identity = provider.Resolve(context);

            Assert.That(identity, Is.Not.Null);
            Assert.That(identity!.TokenHandler, Is.InstanceOf<IssuedIdentityTokenHandler>(),
                "Bearer-recovered identity must surface as an IssuedToken so the " +
                "server-side authenticator can re-introduce the JWT downstream.");
            var handler = (IssuedIdentityTokenHandler)identity.TokenHandler;
            Assert.That(handler.IssuedTokenType, Is.EqualTo(IssuedTokenType.JWT));
            Assert.That(handler.DecryptedTokenData, Is.Not.Null);
            Assert.That(Encoding.UTF8.GetString(handler.DecryptedTokenData!),
                Is.EqualTo(SampleJwt));
        }

        [Test]
        public void SubjectClaimSurfacesViaGetSubject()
        {
            var provider = new JwtClaimSessionlessIdentityProvider();
            DefaultHttpContext context = BuildContext(
                bearer: SampleJwt,
                claims: new Claim("sub", "alice"));

            IUserIdentity? identity = provider.Resolve(context);

            Assert.That(JwtClaimSessionlessIdentityProvider.GetSubject(identity),
                Is.EqualTo("alice"));
        }

        [Test]
        public void MissingSubjectFallsBackToIdentityName()
        {
            var provider = new JwtClaimSessionlessIdentityProvider();
            DefaultHttpContext context = BuildContext(
                bearer: SampleJwt,
                identityName: "bob",
                claims: new Claim(ClaimTypes.Name, "bob"));

            IUserIdentity? identity = provider.Resolve(context);

            Assert.That(JwtClaimSessionlessIdentityProvider.GetSubject(identity),
                Is.EqualTo("bob"),
                "Missing 'sub' claim must fall back to ClaimsIdentity.Name.");
        }

        [Test]
        public void ScopeClaimSpaceSeparatedSplits()
        {
            var provider = new JwtClaimSessionlessIdentityProvider();
            DefaultHttpContext context = BuildContext(
                bearer: SampleJwt,
                claims:
                [
                    new Claim("sub", "alice"),
                    new Claim("scope", "read.values browse.address-space write.values")
                ]);

            IUserIdentity? identity = provider.Resolve(context);
            IReadOnlyList<string> scopes = JwtClaimSessionlessIdentityProvider.GetScopes(identity);

            Assert.That(scopes, Is.EquivalentTo(s_expectedScopes));
        }

        [Test]
        public void RolesRepeatedClaimAccumulates()
        {
            var provider = new JwtClaimSessionlessIdentityProvider();
            DefaultHttpContext context = BuildContext(
                bearer: SampleJwt,
                claims:
                [
                    new Claim("sub", "alice"),
                    new Claim("roles", "operator"),
                    new Claim("roles", "observer")
                ]);

            IUserIdentity? identity = provider.Resolve(context);
            IReadOnlyList<string> roles = JwtClaimSessionlessIdentityProvider.GetRoles(identity);

            Assert.That(roles, Is.EquivalentTo(s_expectedRoles));
        }

        [Test]
        public void RolesAlsoPicksUpClaimTypesRoleMappings()
        {
            // Some bearer middleware maps "roles" claims to ClaimTypes.Role.
            var provider = new JwtClaimSessionlessIdentityProvider();
            DefaultHttpContext context = BuildContext(
                bearer: SampleJwt,
                claims:
                [
                    new Claim("sub", "alice"),
                    new Claim(ClaimTypes.Role, "admin")
                ]);

            IUserIdentity? identity = provider.Resolve(context);
            IReadOnlyList<string> roles = JwtClaimSessionlessIdentityProvider.GetRoles(identity);

            Assert.That(roles, Does.Contain("admin"));
        }

        [Test]
        public void CustomClaimNameOverridesAreHonored()
        {
            var provider = new JwtClaimSessionlessIdentityProvider(
                new JwtClaimSessionlessIdentityProviderOptions
                {
                    SubjectClaim = "preferred_username",
                    ScopeClaim = "scp",
                    RolesClaim = "groups"
                });
            DefaultHttpContext context = BuildContext(
                bearer: SampleJwt,
                claims:
                [
                    new Claim("preferred_username", "carol"),
                    new Claim("scp", "a b"),
                    new Claim("groups", "engineering"),
                    new Claim("groups", "qa")
                ]);

            IUserIdentity? identity = provider.Resolve(context);

            Assert.That(JwtClaimSessionlessIdentityProvider.GetSubject(identity), Is.EqualTo("carol"));
            Assert.That(JwtClaimSessionlessIdentityProvider.GetScopes(identity),
                Is.EquivalentTo(s_expectedCustomScopes));
            Assert.That(JwtClaimSessionlessIdentityProvider.GetRoles(identity),
                Is.EquivalentTo(s_expectedCustomRoles));
        }

        [Test]
        public void TransformIdentityHookReplacesProjection()
        {
            var replacement = new UserIdentity();
            var provider = new JwtClaimSessionlessIdentityProvider(
                new JwtClaimSessionlessIdentityProviderOptions
                {
                    TransformIdentity = (ctx, principal, identity) => replacement
                });
            DefaultHttpContext context = BuildContext(
                bearer: SampleJwt,
                claims: new Claim("sub", "alice"));

            IUserIdentity? identity = provider.Resolve(context);

            Assert.That(identity, Is.SameAs(replacement),
                "TransformIdentity hook must be allowed to swap the projected identity.");
        }

        [Test]
        public void AuthenticatedWithoutBearerHeaderReturnsAnonymous()
        {
            // When no raw bearer token is recoverable, the provider
            // must not fall back to a UserName projection with an empty
            // password — same anti-pattern as
            // DefaultSessionlessIdentityProvider. It returns null (or
            // anonymous per options) so an upstream-authenticated
            // principal cannot impersonate a UserName account if the
            // server trusts upstream auth without re-verifying passwords.
            var provider = new JwtClaimSessionlessIdentityProvider();
            DefaultHttpContext context = BuildContext(
                bearer: null,
                claims: new Claim("sub", "alice"));

            IUserIdentity? identity = provider.Resolve(context);

            Assert.That(identity, Is.Null,
                "Default options (ReturnAnonymousForUnauthenticated = false) must " +
                "return null when no bearer is recoverable, NOT a UserName token " +
                "with an empty password. Upstream subject still available via " +
                "SecureChannelContext.UpstreamIdentity.");
        }

        private static DefaultHttpContext BuildContext(
            string? bearer,
            string? identityName = null,
            params Claim[] claims)
        {
            return BuildContext(bearer, identityName, (IEnumerable<Claim>)claims);
        }

        private static DefaultHttpContext BuildContext(
            string? bearer,
            string? identityName,
            IEnumerable<Claim> claims)
        {
            var identity = new ClaimsIdentity(
                claims,
                authenticationType: "JwtBearer",
                nameType: identityName != null ? ClaimTypes.Name : "name",
                roleType: ClaimTypes.Role);

            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            };
            if (bearer != null)
            {
                context.Request.Headers.Authorization = "Bearer " + bearer;
            }
            return context;
        }
    }
}
