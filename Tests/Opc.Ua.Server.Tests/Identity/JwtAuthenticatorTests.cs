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

#nullable disable

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

        [Test]
        public async Task AuthenticateAsyncRs256TokenAcceptedAndClaimsExtracted()
        {
            using RSA rsa = RSA.Create(2048);
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
            using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
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
            using RSA rsa = RSA.Create(2048);
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
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public async Task AuthenticateAsyncWrongAudienceRejected()
        {
            using RSA rsa = RSA.Create(2048);
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
        public async Task AuthenticateAsyncWrongIssuerRejected()
        {
            using RSA rsa = RSA.Create(2048);
            using IssuerVerificationKey key = CreateRsaVerificationKey(rsa, "kid-rsa");
            string jwt = CreateJwt(
                "RS256",
                "kid-rsa",
                "https://wrong-issuer.example.test",
                Quote(Audience),
                expiresInSeconds: 3600,
                signingInput => rsa.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

            AuthenticationResult result = await AuthenticateAsync(jwt, key).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
        }

        [Test]
        public async Task AuthenticateAsyncMalformedTokenRejectedAsInvalid()
        {
            using RSA rsa = RSA.Create(2048);
            using IssuerVerificationKey key = CreateRsaVerificationKey(rsa, "kid-rsa");

            AuthenticationResult result = await AuthenticateAsync("not-a-jwt", key).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenInvalid));
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
                "{\"iss\":\"" + issuer +
                "\",\"sub\":\"subject-1\",\"aud\":" + audienceJson +
                ",\"exp\":" + (now + expiresInSeconds) +
                ",\"nbf\":" + (now - 60) +
                ",\"iat\":" + (now - 1) +
                ",\"groups\":[\"engineering\"],\"roles\":[\"operator\"]}";

            string signingInput = Base64UrlEncode(Encoding.UTF8.GetBytes(header)) + "." +
                Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
            byte[] signature = sign(Encoding.ASCII.GetBytes(signingInput));
            return signingInput + "." + Base64UrlEncode(signature);
        }

        private static string Quote(string value)
        {
            return "\"" + value + "\"";
        }

#pragma warning disable CA2000 // IssuerVerificationKey owns verification keys; TODO: add ownership annotations when available.
        private static IssuerVerificationKey CreateRsaVerificationKey(RSA rsa, string keyId)
        {
            RSA publicKey = RSA.Create();
            publicKey.ImportParameters(rsa.ExportParameters(false));
            return new IssuerVerificationKey(keyId, publicKey, "RS256");
        }

        private static IssuerVerificationKey CreateEcdsaVerificationKey(ECDsa ecdsa, string keyId)
        {
            ECDsa publicKey = ECDsa.Create(ecdsa.ExportParameters(false));
            return new IssuerVerificationKey(keyId, publicKey, "ES256");
        }
#pragma warning restore CA2000

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
                m_keys = new[] { key };
            }

            public string IssuerUri { get; }

            public ValueTask<IReadOnlyList<IssuerVerificationKey>> GetKeysAsync(
                string keyId,
                CancellationToken ct = default)
            {
                return new ValueTask<IReadOnlyList<IssuerVerificationKey>>(m_keys);
            }
        }
    }
}
