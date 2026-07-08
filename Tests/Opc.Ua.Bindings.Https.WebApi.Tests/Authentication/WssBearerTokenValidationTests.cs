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
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
using Opc.Ua.Bindings.WebApi.Authentication;
using Opc.Ua.Bindings.WebApi;

namespace Opc.Ua.Bindings.Https.WebApi.Tests.Authentication
{
    /// <summary>
    /// Regression tests for the WSS
    /// <c>opcua+openapi+&lt;accesstoken&gt;</c> bearer-token validation
    /// helper installed by
    /// <see cref="WebApiHttpsStartupContributor.ValidateWssBearerTokenAsync"/>.
    /// The server must validate the bearer token presented in the
    /// sub-protocol name (JWT signature / issuer / audience / expiry
    /// checks) before accepting the WebSocket upgrade. Accepting
    /// without validation would be a silent auth bypass (CWE-287).
    /// </summary>
    [TestFixture]
    [Category("WebApiAuthentication")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class WssBearerTokenValidationTests
    {
        private const string TestIssuer = "https://test-issuer.example";
        private const string TestAudience = "test-audience";
        /// <summary>
        /// 64-byte (512-bit) HMAC key — well above the minimum 256-bit
        /// requirement that recent Microsoft.IdentityModel releases enforce
        /// for HmacSha256-signed JWTs.
        /// </summary>
        private static readonly byte[] s_signingKeyBytes = GenerateKey(64);

        private static byte[] GenerateKey(int size)
        {
            byte[] bytes = new byte[size];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return bytes;
        }

        [Test]
        public async Task ValidateWssBearerTokenAsyncReturnsFalseWhenNoBearerSchemeRegisteredAsync()
        {
            // Anonymous-only registration → no JwtBearer scheme to delegate to.
            var services = new ServiceCollection();
            services.AddOpcUa().AddWebApiAnonymousAuth();
            services.AddAuthentication();

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            HttpContext context = CreateContext(serviceProvider, accessToken: "any.token.value");

            bool ok = await WebApiHttpsStartupContributor
                .ValidateWssBearerTokenAsync(context, "any.token.value")
                .ConfigureAwait(false);

            Assert.That(ok, Is.False,
                "Without a registered OpcUaWebApi.Bearer scheme, the WSS bearer-prefix " +
                "upgrade must be rejected — the server has no way to validate the credential.");
        }

        [Test]
        public async Task ValidateWssBearerTokenAsyncReturnsFalseForEmptyTokenAsync()
        {
            using ServiceProvider serviceProvider = BuildServiceProviderWithBearerAuth();
            HttpContext context = CreateContext(serviceProvider, accessToken: string.Empty);

            bool ok = await WebApiHttpsStartupContributor
                .ValidateWssBearerTokenAsync(context, string.Empty)
                .ConfigureAwait(false);

            Assert.That(ok, Is.False,
                "An empty access-token must never authenticate.");
        }

        [Test]
        public async Task ValidateWssBearerTokenAsyncReturnsFalseForBogusTokenAsync()
        {
            using ServiceProvider serviceProvider = BuildServiceProviderWithBearerAuth();
            HttpContext context = CreateContext(serviceProvider, accessToken: "not.a.jwt");

            bool ok = await WebApiHttpsStartupContributor
                .ValidateWssBearerTokenAsync(context, "not.a.jwt")
                .ConfigureAwait(false);

            Assert.That(ok, Is.False,
                "A malformed bearer token must be rejected — silent acceptance " +
                "was the original vulnerability.");
        }

        [Test]
        public async Task ValidateWssBearerTokenAsyncReturnsFalseForWrongSigningKeyAsync()
        {
            using ServiceProvider serviceProvider = BuildServiceProviderWithBearerAuth();
            // Token signed with a different key (same length, different bytes) — must be rejected.
            byte[] attackerKey = GenerateKey(64);
            string forgedToken = CreateJwt(attackerKey, TestIssuer, TestAudience, "alice");

            HttpContext context = CreateContext(serviceProvider, accessToken: forgedToken);
            bool ok = await WebApiHttpsStartupContributor
                .ValidateWssBearerTokenAsync(context, forgedToken)
                .ConfigureAwait(false);

            Assert.That(ok, Is.False,
                "JWT signed with a wrong key must fail signature validation.");
        }

        [Test]
        public async Task ValidateWssBearerTokenAsyncReturnsFalseForExpiredTokenAsync()
        {
            using ServiceProvider serviceProvider = BuildServiceProviderWithBearerAuth();
            string expiredToken = CreateJwt(
                s_signingKeyBytes,
                TestIssuer,
                TestAudience,
                "alice",
                expiresAt: DateTime.UtcNow.AddHours(-1));

            HttpContext context = CreateContext(serviceProvider, accessToken: expiredToken);
            bool ok = await WebApiHttpsStartupContributor
                .ValidateWssBearerTokenAsync(context, expiredToken)
                .ConfigureAwait(false);

            Assert.That(ok, Is.False,
                "Expired JWTs must fail expiry validation.");
        }

        [Test]
        public async Task ValidateWssBearerTokenAsyncReturnsTrueAndPublishesPrincipalForValidTokenAsync()
        {
            using ServiceProvider serviceProvider = BuildServiceProviderWithBearerAuth();
            string validToken = CreateJwt(s_signingKeyBytes, TestIssuer, TestAudience, "alice");

            HttpContext context = CreateContext(serviceProvider, accessToken: validToken);
            bool ok = await WebApiHttpsStartupContributor
                .ValidateWssBearerTokenAsync(context, validToken)
                .ConfigureAwait(false);

            Assert.That(ok, Is.True,
                "A well-formed, signed, non-expired JWT issued by the configured " +
                "issuer/audience must authenticate.");
            Assert.That(context.User.Identity?.IsAuthenticated, Is.True,
                "On success the resolved ClaimsPrincipal must be published on " +
                "HttpContext.User so the upstream-identity plumbing can route it.");
            Claim? subClaim = context.User.FindFirst(JwtRegisteredClaimNames.Sub)
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier);
            Assert.That(subClaim?.Value, Is.EqualTo("alice"),
                "The sub claim from the validated JWT must appear on the principal.");
        }

        [Test]
        public async Task ValidateWssBearerTokenAsyncRejectsWrongIssuerAsync()
        {
            using ServiceProvider serviceProvider = BuildServiceProviderWithBearerAuth();
            // Token issued by a different IdP — must be rejected even if signed correctly.
            string wrongIssuerToken = CreateJwt(
                s_signingKeyBytes,
                issuer: "https://attacker.example",
                TestAudience,
                "alice");

            HttpContext context = CreateContext(serviceProvider, accessToken: wrongIssuerToken);
            bool ok = await WebApiHttpsStartupContributor
                .ValidateWssBearerTokenAsync(context, wrongIssuerToken)
                .ConfigureAwait(false);

            Assert.That(ok, Is.False,
                "A JWT with an issuer the server does not trust must be rejected.");
        }

        private static ServiceProvider BuildServiceProviderWithBearerAuth()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddWebApiBearerAuth(o =>
            {
                o.RequireHttpsMetadata = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestIssuer,
                    ValidateAudience = true,
                    ValidAudience = TestAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(s_signingKeyBytes)
                };
            });
            return services.BuildServiceProvider();
        }

        private static HttpContext CreateContext(IServiceProvider services, string accessToken)
        {
            HttpContext context = new DefaultHttpContext
            {
                RequestServices = services
            };
            // The validator sets the Authorization header itself; here
            // we only seed RequestServices for ASP.NET Core auth resolution.
            _ = accessToken; // signature parity with caller
            return context;
        }

        private static string CreateJwt(
            byte[] signingKey,
            string issuer,
            string audience,
            string subject,
            DateTime? expiresAt = null)
        {
            var securityKey = new SymmetricSecurityKey(signingKey);
            // Use the JWT-style algorithm identifier "HS256" rather than the
            // XML-signature URI that the legacy System.IdentityModel.Tokens
            // SecurityAlgorithms constants resolve to.
            var credentials = new SigningCredentials(
                securityKey,
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
            DateTime now = DateTime.UtcNow;
            DateTime expires = expiresAt ?? now.AddMinutes(5);
            // notBefore must precede expires; anchor it 1 minute before
            // expires (for expired-token tests) or 1 minute before now
            // (whichever is earlier) so the lifetime spans the present.
            DateTime notBefore = expires < now ? expires.AddMinutes(-1) : now.AddMinutes(-1);
            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims:
                [
                    new Claim(JwtRegisteredClaimNames.Sub, subject),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
                ],
                notBefore: notBefore,
                expires: expires,
                signingCredentials: credentials);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
