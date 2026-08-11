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

#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Unit tests for the ClientUserId derivation used to compare authenticated
    /// Session owners during ActivateSession transfer and Subscription transfer
    /// (OPC 10000-4 §5.7.3.1).
    /// </summary>
    [TestFixture]
    [Category("Session")]
    [Category("Security")]
    [Parallelizable]
    public sealed class ClientUserIdResolverTests
    {
        [Test]
        public void ResolveRejectsNullArguments()
        {
            var anonymousToken = new AnonymousIdentityTokenHandler();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => ClientUserIdResolver.Resolve(null!, new UserIdentity()),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => ClientUserIdResolver.Resolve(anonymousToken, null!),
                    Throws.TypeOf<ArgumentNullException>());
            });
        }

        [Test]
        public void ResolveRejectsTokenTypeThatDoesNotDefineAClientUserId()
        {
            var handler = new Mock<IUserIdentityTokenHandler>();
            handler.SetupGet(h => h.Token).Returns(new UserIdentityToken());
            var identity = new UserIdentity();

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => ClientUserIdResolver.Resolve(handler.Object, identity))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public void TryResolveReportsFailureInsteadOfThrowingForUnsupportedTokens()
        {
            var handler = new Mock<IUserIdentityTokenHandler>();
            handler.SetupGet(h => h.Token).Returns(new UserIdentityToken());

            bool resolved = ClientUserIdResolver.TryResolve(
                handler.Object,
                new UserIdentity(),
                out string? clientUserId);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.False);
                Assert.That(clientUserId, Is.Null);
            });
        }

        [Test]
        public void TryResolveReportsSuccessAndNullClientUserIdForAnonymousTokens()
        {
            var anonymousToken = new AnonymousIdentityTokenHandler();

            bool resolved = ClientUserIdResolver.TryResolve(
                anonymousToken,
                new UserIdentity(),
                out string? clientUserId);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.True);
                Assert.That(clientUserId, Is.Null);
            });
        }

        [Test]
        public void ResolveRejectsX509TokenWithoutCertificateData()
        {
            var x509Token = new X509IdentityTokenHandler(new X509IdentityToken());

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => ClientUserIdResolver.Resolve(x509Token, new UserIdentity(x509Token)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public void ResolveUsesIssuedTokenClaimsWhenIssuerAndSubjectPropertiesAreUnset()
        {
            var issuedToken = new IssuedIdentityTokenHandler(new IssuedIdentityToken
            {
                PolicyId = Profiles.JwtUserToken
            });
            var identity = new JwtUserIdentity(
                issuedToken,
                new Dictionary<string, object?>
                {
                    ["iss"] = "https://claims.example/",
                    ["sub"] = "claims-subject"
                },
                [],
                [],
                issuer: null,
                subject: null);

            Assert.That(
                ClientUserIdResolver.Resolve(issuedToken, identity),
                Is.EqualTo("https://claims.example/claims-subject"));
        }

        [Test]
        public void ResolveRejectsIssuedTokenWhoseClaimsOmitTheSubject()
        {
            var issuedToken = new IssuedIdentityTokenHandler(new IssuedIdentityToken
            {
                PolicyId = Profiles.JwtUserToken
            });
            var identity = new JwtUserIdentity(
                issuedToken,
                new Dictionary<string, object?>
                {
                    ["iss"] = "https://claims.example/"
                },
                [],
                [],
                issuer: null,
                subject: null);

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => ClientUserIdResolver.Resolve(issuedToken, identity))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public void ResolveRejectsJwtWithoutDecryptedTokenData()
        {
            var jwtToken = new IssuedIdentityTokenHandler(Profiles.JwtUserToken, []);

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => ClientUserIdResolver.Resolve(jwtToken, new UserIdentity(jwtToken)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
        }

        [TestCase("\"not-an-object\"", TestName = "PayloadIsNotAJsonObject")]
        [TestCase("{\"iss\":\"https://issuer.example/\"}", TestName = "PayloadOmitsSubject")]
        [TestCase("{\"sub\":42}", TestName = "PayloadSubjectIsNotAString")]
        [TestCase("{\"sub\":\"\"}", TestName = "PayloadSubjectIsEmpty")]
        [TestCase("{\"sub\":\"owner\",\"iss\":42}", TestName = "PayloadIssuerIsNotAString")]
        [TestCase("{\"sub\":", TestName = "PayloadIsMalformedJson")]
        public void ResolveRejectsJwtPayloadWithoutStableOwner(string payloadJson)
        {
            var jwtToken = new IssuedIdentityTokenHandler(
                Profiles.JwtUserToken,
                CreateJwt(Base64UrlEncode(payloadJson)));

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => ClientUserIdResolver.Resolve(jwtToken, new UserIdentity(jwtToken)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public void ResolveRejectsJwtWhosePayloadIsNotValidBase64Url()
        {
            var jwtToken = new IssuedIdentityTokenHandler(
                Profiles.JwtUserToken,
                CreateJwt("@@@@"));

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => ClientUserIdResolver.Resolve(jwtToken, new UserIdentity(jwtToken)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public void ResolveDecodesJwtPayloadThatRequiresTwoPaddingCharacters()
        {
            // The payload is 13 bytes, so its unpadded Base64Url form has a length
            // of 18 and the decoder has to append "==" before decoding it.
            const string payload = "{\"sub\":\"abc\"}";
            Assert.That(Base64UrlEncode(payload).Length % 4, Is.EqualTo(2));

            var jwtToken = new IssuedIdentityTokenHandler(
                Profiles.JwtUserToken,
                CreateJwt(Base64UrlEncode(payload)));

            Assert.That(
                ClientUserIdResolver.Resolve(jwtToken, new UserIdentity(jwtToken)),
                Is.EqualTo("abc"));
        }

        [Test]
        public void ResolveDecodesJwtPayloadThatRequiresOnePaddingCharacter()
        {
            // The payload is 11 bytes, so its unpadded Base64Url form has a length
            // of 15 and the decoder has to append a single "=" before decoding it.
            const string payload = "{\"sub\":\"a\"}";
            Assert.That(Base64UrlEncode(payload).Length % 4, Is.EqualTo(3));

            var jwtToken = new IssuedIdentityTokenHandler(
                Profiles.JwtUserToken,
                CreateJwt(Base64UrlEncode(payload)));

            Assert.That(
                ClientUserIdResolver.Resolve(jwtToken, new UserIdentity(jwtToken)),
                Is.EqualTo("a"));
        }

        /// <summary>
        /// Builds a JWT whose payload segment is used verbatim, so that malformed
        /// segments can be exercised.
        /// </summary>
        private static byte[] CreateJwt(string payloadSegment)
        {
            return Encoding.UTF8.GetBytes(
                $"{Base64UrlEncode("{}")}.{payloadSegment}.signature");
        }

        [Test]
        public void ContinuityKeyIsNullForAnonymousIdentities()
        {
            Assert.That(
                ClientUserIdResolver.ResolveContinuityKey(
                    new AnonymousIdentityTokenHandler(),
                    new UserIdentity()),
                Is.Null);
        }

        [Test]
        public void ContinuityKeySeparatesIssuerFromSubject()
        {
            // "ab" + "c" and "a" + "bc" both describe the same fused diagnostic
            // ClientUserId, so the continuity key has to keep them apart.
            string? first = ResolveIssuedTokenContinuityKey("ab", "c");
            string? second = ResolveIssuedTokenContinuityKey("a", "bc");

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.Null);
                Assert.That(second, Is.Not.Null);
                Assert.That(first, Is.Not.EqualTo(second));
            });
        }

        [Test]
        public void ContinuityKeySeparatesAnAbsentIssuerFromAnEmptyIssuer()
        {
            string? absent = ResolveIssuedTokenContinuityKey(null, "abc");
            string? empty = ResolveIssuedTokenContinuityKey(string.Empty, "abc");

            Assert.That(absent, Is.Not.EqualTo(empty));
        }

        [Test]
        public void ContinuityKeySeparatesTokenTypesSharingAnIdentifier()
        {
            // A user name may be spelled exactly like a certificate subject; the
            // two are different principals and must not transfer to each other.
            const string identifier = "CN=ClientUserIdResolverTests";
            var userNameToken = new UserNameIdentityTokenHandler(identifier, [1, 2, 3]);
            using Certificate certificate = DefaultCertificateFactory.Instance
                .CreateCertificate(identifier)
                .SetRSAKeySize(CertificateFactory.DefaultKeySize)
                .CreateForRSA();
            var x509Token = new X509IdentityTokenHandler(new X509IdentityToken
            {
                CertificateData = certificate.RawData.ToByteString()
            });

            string? userNameKey = ClientUserIdResolver.ResolveContinuityKey(
                userNameToken,
                new UserIdentity(userNameToken));
            string? certificateKey = ClientUserIdResolver.ResolveContinuityKey(
                x509Token,
                new UserIdentity(x509Token));

            Assert.Multiple(() =>
            {
                // The diagnostic ClientUserId is identical for both identities.
                Assert.That(
                    ClientUserIdResolver.Resolve(userNameToken, new UserIdentity(userNameToken)),
                    Is.EqualTo(ClientUserIdResolver.Resolve(x509Token, new UserIdentity(x509Token))));
                Assert.That(userNameKey, Is.Not.EqualTo(certificateKey));
            });
        }

        [Test]
        public void ResolveKeepsTheHumanReadableDiagnosticClientUserId()
        {
            // OPC 10000-5 reports ClientUserId for diagnostics, so it must stay
            // readable even though the continuity key is an encoded form.
            var userNameToken = new UserNameIdentityTokenHandler("alice", [1, 2, 3]);

            Assert.Multiple(() =>
            {
                Assert.That(
                    ClientUserIdResolver.Resolve(userNameToken, new UserIdentity(userNameToken)),
                    Is.EqualTo("alice"));
                Assert.That(
                    ResolveIssuedTokenDiagnosticId("https://issuer.example/", "subject-42"),
                    Is.EqualTo("https://issuer.example/subject-42"));
            });
        }

        [Test]
        public void TryResolveContinuityKeyReportsFailureForUnsupportedTokens()
        {
            var handler = new Mock<IUserIdentityTokenHandler>();
            handler.SetupGet(h => h.Token).Returns(new UserIdentityToken());

            bool resolved = ClientUserIdResolver.TryResolveContinuityKey(
                handler.Object,
                new UserIdentity(),
                out string? continuityKey);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.False);
                Assert.That(continuityKey, Is.Null);
            });
        }

        private static string? ResolveIssuedTokenContinuityKey(string? issuer, string subject)
        {
            (IssuedIdentityTokenHandler token, JwtUserIdentity identity) =
                CreateIssuedIdentity(issuer, subject);
            return ClientUserIdResolver.ResolveContinuityKey(token, identity);
        }

        private static string? ResolveIssuedTokenDiagnosticId(string? issuer, string subject)
        {
            (IssuedIdentityTokenHandler token, JwtUserIdentity identity) =
                CreateIssuedIdentity(issuer, subject);
            return ClientUserIdResolver.Resolve(token, identity);
        }

        private static (IssuedIdentityTokenHandler Token, JwtUserIdentity Identity) CreateIssuedIdentity(
            string? issuer,
            string subject)
        {
            var token = new IssuedIdentityTokenHandler(new IssuedIdentityToken
            {
                PolicyId = Profiles.JwtUserToken
            });
            var identity = new JwtUserIdentity(
                token,
                new Dictionary<string, object?>(),
                [],
                [],
                issuer,
                subject);
            return (token, identity);
        }

        private static string Base64UrlEncode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}
