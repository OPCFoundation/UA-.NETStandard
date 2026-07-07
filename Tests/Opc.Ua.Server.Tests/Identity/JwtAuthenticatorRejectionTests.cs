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

#nullable enable annotations

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
    public class JwtAuthenticatorRejectionTests
    {
        private const string Issuer = "https://issuer.example.test";
        private const string Audience = "urn:opcua:test-server";

        [Test]
        public async Task AuthenticateAsyncAnonymousTokenReturnsNotHandled()
        {
            using var rsa = RSA.Create(2048);
            using IssuerVerificationKey key = CreateRsaVerificationKey(rsa, "kid-rsa");
            var authenticator = new JwtAuthenticator(new StubResolver(Issuer, key), Audience, TimeSpan.Zero);

            AuthenticationResult result = await authenticator
                .AuthenticateAsync(new AuthenticationContext(
                    new AnonymousIdentityTokenHandler(),
                    new UserTokenPolicy(UserTokenType.Anonymous),
                    new EndpointDescription { SecurityMode = MessageSecurityMode.None },
                    ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create())))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.NotHandled));
        }

        [Test]
        public async Task AuthenticateAsyncSamlIssuedTokenReturnsNotHandled()
        {
            using var rsa = RSA.Create(2048);
            using IssuerVerificationKey key = CreateRsaVerificationKey(rsa, "kid-rsa");
            var authenticator = new JwtAuthenticator(new StubResolver(Issuer, key), Audience, TimeSpan.Zero);

            AuthenticationResult result = await authenticator
                .AuthenticateAsync(CreateContext(
                    new IssuedIdentityTokenHandler(
                        "http://opcfoundation.org/UA/UserToken#SAML",
                        [1, 2, 3])))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.NotHandled));
        }

        [Test]
        public async Task AuthenticateAsyncEmptyTokenDataRejectedAsInvalid()
        {
            using var rsa = RSA.Create(2048);
            using IssuerVerificationKey key = CreateRsaVerificationKey(rsa, "kid-rsa");
            AuthenticationResult result = await Authenticate(string.Empty, key).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task AuthenticateAsyncTokenWithBadBase64UrlRejectedAsInvalid()
        {
            using var rsa = RSA.Create(2048);
            using IssuerVerificationKey key = CreateRsaVerificationKey(rsa, "kid-rsa");
            AuthenticationResult result = await Authenticate("!@#.!@#.!@#", key).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task AuthenticateAsyncTokenWithHeaderNotJsonRejectedAsInvalid()
        {
            using var rsa = RSA.Create(2048);
            using IssuerVerificationKey key = CreateRsaVerificationKey(rsa, "kid-rsa");
            string header = Base64UrlEncode(Encoding.UTF8.GetBytes("not-json"));
            string payload = Base64UrlEncode(Encoding.UTF8.GetBytes("{}"));
            string signature = Base64UrlEncode([1, 2, 3]);
            AuthenticationResult result = await Authenticate(header + "." + payload + "." + signature, key)
                .ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task AuthenticateAsyncTokenWithoutAlgRejectedAsInvalid()
        {
            using var rsa = RSA.Create(2048);
            using IssuerVerificationKey key = CreateRsaVerificationKey(rsa, "kid-rsa");
            string header = Base64UrlEncode(Encoding.UTF8.GetBytes(/*lang=json,strict*/ "{\"typ\":\"JWT\"}"));
            string payload = Base64UrlEncode(Encoding.UTF8.GetBytes("{}"));
            string signature = Base64UrlEncode([1, 2, 3]);
            AuthenticationResult result = await Authenticate(header + "." + payload + "." + signature, key)
                .ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task AuthenticateAsyncPayloadNotJsonRejectedAsInvalid()
        {
            using var rsa = RSA.Create(2048);
            using IssuerVerificationKey key = CreateRsaVerificationKey(rsa, "kid-rsa");
            const string header = /*lang=json,strict*/ "{\"alg\":\"RS256\",\"kid\":\"kid-rsa\",\"typ\":\"JWT\"}";
            string headerEncoded = Base64UrlEncode(Encoding.UTF8.GetBytes(header));
            string payloadEncoded = Base64UrlEncode(Encoding.UTF8.GetBytes("not-json"));
            string signingInput = headerEncoded + "." + payloadEncoded;
            byte[] signature = rsa.SignData(
                Encoding.ASCII.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            AuthenticationResult result = await Authenticate(
                signingInput + "." + Base64UrlEncode(signature),
                key).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task AuthenticateAsyncMissingExpClaimRejectedAsInvalid()
        {
            using var rsa = RSA.Create(2048);
            using IssuerVerificationKey key = CreateRsaVerificationKey(rsa, "kid-rsa");
            const string payload = "{\"iss\":\"" + Issuer + "\",\"sub\":\"x\",\"aud\":\"" + Audience + "\"}";
            string jwt = SignRsa(rsa, "RS256", "kid-rsa", payload);

            AuthenticationResult result = await Authenticate(jwt, key).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task AuthenticateAsyncNotBeforeInFutureRejected()
        {
            using var rsa = RSA.Create(2048);
            using IssuerVerificationKey key = CreateRsaVerificationKey(rsa, "kid-rsa");
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string payload = "{\"iss\":\"" +
                Issuer +
                "\",\"sub\":\"x\",\"aud\":\"" +
                Audience +
                "\",\"exp\":" +
                (now + 3600) +
                ",\"nbf\":" +
                (now + 3600) +
                "}";
            string jwt = SignRsa(rsa, "RS256", "kid-rsa", payload);

            AuthenticationResult result = await Authenticate(jwt, key).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public async Task AuthenticateAsyncIssuedAtInFutureRejected()
        {
            using var rsa = RSA.Create(2048);
            using IssuerVerificationKey key = CreateRsaVerificationKey(rsa, "kid-rsa");
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string payload = "{\"iss\":\"" +
                Issuer +
                "\",\"sub\":\"x\",\"aud\":\"" +
                Audience +
                "\",\"exp\":" +
                (now + 7200) +
                ",\"iat\":" +
                (now + 3600) +
                "}";
            string jwt = SignRsa(rsa, "RS256", "kid-rsa", payload);

            AuthenticationResult result = await Authenticate(jwt, key).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public async Task AuthenticateAsyncTokenWithMismatchedKidRejected()
        {
            using var rsa = RSA.Create(2048);
            using IssuerVerificationKey key = CreateRsaVerificationKey(rsa, "kid-rsa");
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string payload = "{\"iss\":\"" +
                Issuer +
                "\",\"sub\":\"x\",\"aud\":\"" +
                Audience +
                "\",\"exp\":" +
                (now + 3600) +
                "}";
            string jwt = SignRsa(rsa, "RS256", "kid-other", payload);

            AuthenticationResult result = await Authenticate(jwt, key).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public void AuthenticateAsyncResolverExceptionPropagates()
        {
            var resolver = new ThrowingResolver(Issuer, new InvalidOperationException("kaboom"));
            var authenticator = new JwtAuthenticator(resolver, Audience, TimeSpan.Zero);
            byte[] tokenData = Encoding.UTF8.GetBytes(
                Base64UrlEncode(Encoding.UTF8.GetBytes(/*lang=json,strict*/ "{\"alg\":\"RS256\",\"kid\":\"x\"}")) +
                "." +
                Base64UrlEncode(Encoding.UTF8.GetBytes("{}")) +
                "." +
                Base64UrlEncode([1]));

            InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await authenticator.AuthenticateAsync(CreateContext(
                    new IssuedIdentityTokenHandler(Profiles.JwtUserToken, tokenData))).ConfigureAwait(false));

            Assert.That(ex.Message, Is.EqualTo("kaboom"));
        }

        [Test]
        public async Task DelegateConstructorReturningNullRejects()
        {
            var authenticator = new JwtAuthenticator(
                (handler, ct) => new ValueTask<IUserIdentity?>((IUserIdentity)null));
            AuthenticationResult result = await authenticator
                .AuthenticateAsync(CreateContext(
                    new IssuedIdentityTokenHandler(Profiles.JwtUserToken, [1])))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public async Task DelegateConstructorThrowingServiceResultExceptionRejects()
        {
            var authenticator = new JwtAuthenticator((handler, ct) =>
                throw ServiceResultException.Create(StatusCodes.BadIdentityTokenInvalid, "bad"));

            AuthenticationResult result = await authenticator
                .AuthenticateAsync(CreateContext(
                    new IssuedIdentityTokenHandler(Profiles.JwtUserToken, [1])))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public void ConstructorRejectsNullKeyResolver()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new JwtAuthenticator(null, Audience));
            Assert.That(ex.ParamName, Is.EqualTo("keyResolver"));
        }

        [Test]
        public void ConstructorRejectsEmptyAudience()
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => new JwtAuthenticator(new StubResolver(Issuer), string.Empty));
            Assert.That(ex.ParamName, Is.EqualTo("expectedAudience"));
        }

        private static Task<AuthenticationResult> Authenticate(string jwt, IssuerVerificationKey key)
        {
            var resolver = new StubResolver(Issuer, key);
            var authenticator = new JwtAuthenticator(resolver, Audience, TimeSpan.Zero);
            return authenticator.AuthenticateAsync(CreateContext(
                new IssuedIdentityTokenHandler(Profiles.JwtUserToken, Encoding.UTF8.GetBytes(jwt))))
                .AsTask();
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

        private static IssuerVerificationKey CreateRsaVerificationKey(RSA rsa, string keyId)
        {
            RSA publicKey = null;
            try
            {
                publicKey = RSA.Create();
                publicKey.ImportParameters(rsa.ExportParameters(false));
                var key = new IssuerVerificationKey(keyId, publicKey, "RS256");
                publicKey = null;
                return key;
            }
            finally
            {
                publicKey?.Dispose();
            }
        }

        private static string SignRsa(RSA rsa, string algorithm, string kid, string payload)
        {
            string header = "{\"alg\":\"" + algorithm + "\",\"kid\":\"" + kid + "\",\"typ\":\"JWT\"}";
            string headerEncoded = Base64UrlEncode(Encoding.UTF8.GetBytes(header));
            string payloadEncoded = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
            string signingInput = headerEncoded + "." + payloadEncoded;
            byte[] signature = rsa.SignData(
                Encoding.ASCII.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            return signingInput + "." + Base64UrlEncode(signature);
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private sealed class StubResolver : IIssuerKeyResolver
        {
            private readonly IReadOnlyList<IssuerVerificationKey> m_keys;

            public StubResolver(string issuerUri, params IssuerVerificationKey[] keys)
            {
                IssuerUri = issuerUri;
                m_keys = keys ?? [];
            }

            public string IssuerUri { get; }

            public ValueTask<IReadOnlyList<IIssuerVerificationKey>> GetKeysAsync(
                string keyId,
                CancellationToken ct = default)
            {
                return new ValueTask<IReadOnlyList<IIssuerVerificationKey>>(m_keys);
            }
        }

        private sealed class ThrowingResolver : IIssuerKeyResolver
        {
            private readonly Exception m_exception;

            public ThrowingResolver(string issuerUri, Exception exception)
            {
                IssuerUri = issuerUri;
                m_exception = exception;
            }

            public string IssuerUri { get; }

            public ValueTask<IReadOnlyList<IIssuerVerificationKey>> GetKeysAsync(
                string keyId,
                CancellationToken ct = default)
            {
                throw m_exception;
            }
        }
    }
}
