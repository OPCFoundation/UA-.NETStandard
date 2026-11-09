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
 *
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
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Identity
{
    [TestFixture]
    [Category("Identity")]
    public class JwtAuthenticatorTests
    {
        private const string Issuer = "https://issuer.example.test";
        private const string Audience = "urn:opcua:test-server";

        /// <summary>
        /// A freshly signed, lifetime-valid token cannot use a key withdrawn from an expired JWKS snapshot.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityR16WithdrawnCachedKeyCannotAuthenticateFreshJwtAsync(bool noKid)
        {
            using var retired = RSA.Create(2048);
            using var current = RSA.Create(2048);
            var clock = new FakeTimeProvider();
            using var handler = new RotatingJwksHandler(
                CreateJwks(retired, "retired"), CreateJwks(current, "current"));
            using var client = new HttpClient(handler, false);
            using var resolver = new JwksIssuerKeyResolver(
                Issuer, Issuer + "/keys", client, clock);
            var authenticator = new JwtAuthenticator(resolver, Audience);
            AuthenticationResult initial = await AuthenticateJwtAsync(
                authenticator, CreateFixedJwt(retired, noKid ? null : "retired")).ConfigureAwait(false);
            Assert.That(initial.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(((IIdentityClaims)initial.Identity!).Subject, Is.EqualTo("subject-r16"));
            clock.Advance(TimeSpan.FromMinutes(5));

            AuthenticationResult withdrawn = await AuthenticateJwtAsync(
                authenticator, CreateFixedJwt(retired, noKid ? null : "retired")).ConfigureAwait(false);
            AuthenticationResult replacement = await AuthenticateJwtAsync(
                authenticator, CreateFixedJwt(current, noKid ? null : "current")).ConfigureAwait(false);

            Assert.That(withdrawn.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(withdrawn.Error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadIdentityTokenRejected));
            Assert.That(withdrawn.Identity, Is.Null);
            Assert.That(replacement.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(((IIdentityClaims)replacement.Identity!).Subject, Is.EqualTo("subject-r16"));
            Assert.That(((IIdentityClaims)replacement.Identity).Roles, Is.EqualTo((string[])["operator"]));
            Assert.That(handler.RequestCount, Is.EqualTo(2));
        }

        [Test]
        public async Task AuthenticateAsyncRs256TokenAcceptedAndClaimsExtracted()
        {
            using var rsa = RSA.Create(2048);
            using IssuerVerificationKey key = CreateRsaVerificationKey(rsa, "kid-rsa");
            string jwt = CreateJwt(
                "RS256",
                "kid-rsa",
                Issuer,
                Quote(Audience),
                expiresInSeconds: 3600,
                signingInput => rsa.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

            AuthenticationResult result = await AuthenticateAsync(jwt, key).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            var claims = result.Identity as IIdentityClaims;
            Assert.That(claims, Is.Not.Null);
            Assert.That(claims.Issuer, Is.EqualTo(Issuer));
            Assert.That(claims.Subject, Is.EqualTo("subject-1"));
            Assert.That(claims.Groups, Does.Contain("engineering"));
            Assert.That(claims.Roles, Does.Contain("operator"));
        }

        [Test]
        public async Task AuthenticateAsyncEs256TokenAccepted()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using IssuerVerificationKey key = CreateEcdsaVerificationKey(ecdsa, "kid-ec");
            string jwt = CreateJwt(
                "ES256",
                "kid-ec",
                Issuer,
                "[" + Quote("other") + "," + Quote(Audience) + "]",
                expiresInSeconds: 3600,
                signingInput => ecdsa.SignData(signingInput, HashAlgorithmName.SHA256));

            AuthenticationResult result = await AuthenticateAsync(jwt, key).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(result.Identity, Is.TypeOf<JwtUserIdentity>());
        }

        [Test]
        public async Task AuthenticateAsyncExpiredTokenRejected()
        {
            using var rsa = RSA.Create(2048);
            using IssuerVerificationKey key = CreateRsaVerificationKey(rsa, "kid-rsa");
            string jwt = CreateJwt(
                "RS256",
                "kid-rsa",
                Issuer,
                Quote(Audience),
                expiresInSeconds: -3600,
                signingInput => rsa.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

            AuthenticationResult result = await AuthenticateAsync(jwt, key).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error!.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public async Task AuthenticateAsyncWrongAudienceRejected()
        {
            using var rsa = RSA.Create(2048);
            using IssuerVerificationKey key = CreateRsaVerificationKey(rsa, "kid-rsa");
            string jwt = CreateJwt(
                "RS256",
                "kid-rsa",
                Issuer,
                Quote("urn:opcua:other-server"),
                expiresInSeconds: 3600,
                signingInput => rsa.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

            AuthenticationResult result = await AuthenticateAsync(jwt, key).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
        }

        [Test]
        public async Task AuthenticateAsyncWrongIssuerIsNotHandled()
        {
            using var rsa = RSA.Create(2048);
            using IssuerVerificationKey key = CreateRsaVerificationKey(rsa, "kid-rsa");
            string jwt = CreateJwt(
                "RS256",
                "kid-rsa",
                "https://wrong-issuer.example.test",
                Quote(Audience),
                expiresInSeconds: 3600,
                signingInput => rsa.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

            AuthenticationResult result = await AuthenticateAsync(jwt, key).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.NotHandled));
        }

        [Test]
        public async Task AuthenticateAsyncMalformedTokenRejectedAsInvalid()
        {
            using var rsa = RSA.Create(2048);
            using IssuerVerificationKey key = CreateRsaVerificationKey(rsa, "kid-rsa");

            AuthenticationResult result = await AuthenticateAsync("not-a-jwt", key).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error!.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task RegistryAuthenticatesBothIssuersAndNeverUsesAnotherIssuersKeyAsync()
        {
            const string OtherIssuer = "https://other-issuer.example.test";
            using var first = RSA.Create(2048);
            using var second = RSA.Create(2048);
            using IssuerVerificationKey firstKey = CreateRsaVerificationKey(first, "shared-kid");
            using IssuerVerificationKey secondKey = CreateRsaVerificationKey(second, "shared-kid");
            var registry = new ServerIdentityRegistry(
                new JwtAuthenticator(new StaticKeyResolver(Issuer, firstKey), Audience),
                new JwtAuthenticator(new StaticKeyResolver(OtherIssuer, secondKey), Audience));

            foreach ((string issuer, RSA signer) in new[] { (Issuer, first), (OtherIssuer, second) })
            {
                string jwt = CreateJwt(
                    "RS256", "shared-kid", issuer, Quote(Audience), 3600,
                    data => signer.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
                AuthenticationResult accepted = await registry.AuthenticateAsync(CreateContext(
                    new IssuedIdentityTokenHandler(Profiles.JwtUserToken, Encoding.UTF8.GetBytes(jwt))))
                    .ConfigureAwait(false);
                Assert.That(accepted.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
                Assert.That(((IIdentityClaims)accepted.Identity!).Issuer, Is.EqualTo(issuer));
            }

            string forged = CreateJwt(
                "RS256", "shared-kid", Issuer, Quote(Audience), 3600,
                data => second.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            AuthenticationResult rejected = await registry.AuthenticateAsync(CreateContext(
                new IssuedIdentityTokenHandler(Profiles.JwtUserToken, Encoding.UTF8.GetBytes(forged))))
                .ConfigureAwait(false);
            Assert.That(rejected.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(rejected.Error!.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public async Task RegistryReplacesTheSameIssuerRatherThanKeepingAnOldSigningKeyAsync()
        {
            using var oldSigner = RSA.Create(2048);
            using var newSigner = RSA.Create(2048);
            using IssuerVerificationKey oldKey = CreateRsaVerificationKey(oldSigner, "shared-kid");
            using IssuerVerificationKey newKey = CreateRsaVerificationKey(newSigner, "shared-kid");
            var registry = new ServerIdentityRegistry(
                new JwtAuthenticator(new StaticKeyResolver(Issuer, oldKey), Audience),
                new JwtAuthenticator(new StaticKeyResolver(Issuer, newKey), Audience));
            string jwt = CreateJwt(
                "RS256", "shared-kid", Issuer, Quote(Audience), 3600,
                data => oldSigner.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

            AuthenticationResult result = await registry.AuthenticateAsync(CreateContext(
                new IssuedIdentityTokenHandler(Profiles.JwtUserToken, Encoding.UTF8.GetBytes(jwt))))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error!.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));

            string replacement = CreateJwt(
                "RS256", "shared-kid", Issuer, Quote(Audience), 3600,
                data => newSigner.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            AuthenticationResult accepted = await registry.AuthenticateAsync(CreateContext(
                new IssuedIdentityTokenHandler(Profiles.JwtUserToken, Encoding.UTF8.GetBytes(replacement))))
                .ConfigureAwait(false);
            Assert.That(accepted.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(((IIdentityClaims)accepted.Identity!).Issuer, Is.EqualTo(Issuer));
        }

        [Test]
        public void JwtUserIdentityExposesTokenAndClaimProperties()
        {
            var tokenHandler = new IssuedIdentityTokenHandler(
                Profiles.JwtUserToken,
                Encoding.UTF8.GetBytes("token"));
            var identity = new JwtUserIdentity(
                tokenHandler,
                new Dictionary<string, object?> { ["sub"] = "subject-1" },
                ["engineering"],
                ["operator"],
                Issuer,
                "subject-1");

            Assert.That(identity.DisplayName, Is.EqualTo("subject-1"));
            Assert.That(identity.PolicyId, Is.EqualTo(tokenHandler.Token.PolicyId ?? string.Empty));
            Assert.That(identity.TokenType, Is.EqualTo(UserTokenType.IssuedToken));
            Assert.That(identity.IssuedTokenType.Namespace, Is.EqualTo(Profiles.JwtUserToken));
            Assert.That(identity.SupportsSignatures, Is.False);
            Assert.That(identity.GrantedRoleIds, Is.Empty);
            Assert.That(((IUserIdentity)identity).TokenHandler, Is.SameAs(tokenHandler));
        }

        private static async Task<AuthenticationResult> AuthenticateAsync(
            string jwt,
            IssuerVerificationKey key)
        {
            var resolver = new StaticKeyResolver(Issuer, key);
            var authenticator = new JwtAuthenticator(resolver, Audience, TimeSpan.Zero);
            return await authenticator.AuthenticateAsync(
                CreateContext(new IssuedIdentityTokenHandler(
                    Profiles.JwtUserToken,
                    Encoding.UTF8.GetBytes(jwt))))
                .ConfigureAwait(false);
        }

        private static AuthenticationContext CreateContext(IUserIdentityTokenHandler handler)
        {
            return new AuthenticationContext(
                handler,
                new UserTokenPolicy
                {
                    TokenType = UserTokenType.IssuedToken,
                    PolicyId = "jwt",
                    IssuedTokenType = Profiles.JwtUserToken
                },
                new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
        }

        /// <summary>
        /// Authenticates through the real JWT signature and claim checks.
        /// </summary>
        private static ValueTask<AuthenticationResult> AuthenticateJwtAsync(JwtAuthenticator authenticator, string jwt)
        {
            return authenticator.AuthenticateAsync(CreateContext(
                new IssuedIdentityTokenHandler(Profiles.JwtUserToken, Encoding.UTF8.GetBytes(jwt))));
        }

        /// <summary>
        /// Creates a fixed-lifetime token whose validity is independent of the resolver's simulated clock.
        /// </summary>
        private static string CreateFixedJwt(RSA signer, string? kid)
        {
            string header = "{\"alg\":\"RS256\"" + (kid == null ? string.Empty : ",\"kid\":\"" + kid + "\"") + "}";
            const string payload = "{\"iss\":\"" + Issuer + "\",\"aud\":\"" + Audience +
                "\",\"sub\":\"subject-r16\",\"nbf\":1577836800,\"iat\":1577836800,\"exp\":4102444800," +
                "\"roles\":[\"operator\"]}";
            string input = Base64UrlEncode(Encoding.UTF8.GetBytes(header)) + "." +
                Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
            return input + "." + Base64UrlEncode(signer.SignData(
                Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }

        /// <summary>
        /// Publishes only the supplied RSA public key.
        /// </summary>
        private static string CreateJwks(RSA signer, string kid)
        {
            RSAParameters parameters = signer.ExportParameters(false);
            return "{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"" + kid + "\",\"alg\":\"RS256\",\"n\":\"" +
                Base64UrlEncode(parameters.Modulus!) + "\",\"e\":\"" + Base64UrlEncode(parameters.Exponent!) + "\"}]}";
        }

        /// <summary>
        /// Supplies deterministic initial and replacement keysets without a network connection.
        /// </summary>
        private sealed class RotatingJwksHandler(string initial, string replacement) : HttpMessageHandler
        {
            /// <summary>
            /// Gets the exact number of issuer refresh requests.
            /// </summary>
            public int RequestCount { get; private set; }

            /// <inheritdoc/>
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RequestCount == 1 ? initial : replacement)
                });
            }
        }

        private static string CreateJwt(
            string algorithm,
            string keyId,
            string issuer,
            string audienceJson,
            int expiresInSeconds,
            Func<byte[], byte[]> sign)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string header = "{\"alg\":\"" + algorithm + "\",\"kid\":\"" + keyId + "\",\"typ\":\"JWT\"}";
            string payload =
                "{\"iss\":\"" +
                issuer +
                "\",\"sub\":\"subject-1\",\"aud\":" +
                audienceJson +
                ",\"exp\":" +
                (now + expiresInSeconds) +
                ",\"nbf\":" +
                (now - 60) +
                ",\"iat\":" +
                (now - 1) +
                ",\"groups\":[\"engineering\"],\"roles\":[\"operator\"]}";

            string signingInput = Base64UrlEncode(Encoding.UTF8.GetBytes(header)) +
                "." +
                Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
            byte[] signature = sign(Encoding.ASCII.GetBytes(signingInput));
            return signingInput + "." + Base64UrlEncode(signature);
        }

        private static string Quote(string value)
        {
            return "\"" + value + "\"";
        }

        private static IssuerVerificationKey CreateRsaVerificationKey(RSA rsa, string keyId)
        {
            RSA? publicKey = null;
            try
            {
                publicKey = RSA.Create();
                publicKey.ImportParameters(rsa.ExportParameters(false));
                var key = new IssuerVerificationKey(keyId, publicKey, "RS256");
                publicKey = null!;
                return key;
            }
            finally
            {
                publicKey?.Dispose();
            }
        }

        private static IssuerVerificationKey CreateEcdsaVerificationKey(ECDsa ecdsa, string keyId)
        {
            ECDsa? publicKey = null;
            try
            {
                publicKey = ECDsa.Create(ecdsa.ExportParameters(false));
                var key = new IssuerVerificationKey(keyId, publicKey, "ES256");
                publicKey = null!;
                return key;
            }
            finally
            {
                publicKey?.Dispose();
            }
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private sealed class StaticKeyResolver : IIssuerKeyResolver
        {
            private readonly IReadOnlyList<IssuerVerificationKey> m_keys;

            public StaticKeyResolver(string issuerUri, IssuerVerificationKey key)
            {
                IssuerUri = issuerUri;
                m_keys = [key];
            }

            public string IssuerUri { get; }

            public ValueTask<IReadOnlyList<IIssuerVerificationKey>> GetKeysAsync(
                string? keyId,
                CancellationToken ct = default)
            {
                return new ValueTask<IReadOnlyList<IIssuerVerificationKey>>(m_keys);
            }
        }
    }
}
