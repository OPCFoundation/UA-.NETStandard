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

#nullable disable

#pragma warning disable OPCUA_EXPERIMENTAL_KC_BRIDGE

using System;
using System.Collections.Generic;
using System.Linq;
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
    [Category("KeyCredential")]
    public class KeyCredentialBridgeAuthenticatorTests
    {
        private const string CredentialId = "credential-1";
        private static readonly byte[] s_secret = [11, 22, 33, 44, 55, 66];
        private static readonly string[] s_groups = ["engineering"];
        private static readonly string[] s_roles = ["operator"];
        private static readonly string[] s_scopes = ["ua.session"];

        [Test]
        public async Task AuthenticateAsyncValidProofAcceptedAndClaimsReturned()
        {
            using InMemoryKeyCredentialStore store = await CreateStoreAsync(DateTime.UtcNow.AddMinutes(10))
                .ConfigureAwait(false);
            var authenticator = new KeyCredentialBridgeAuthenticator(store);

            AuthenticationResult result = await authenticator.AuthenticateAsync(
                    CreateContext(CreateTokenData(CredentialId, s_secret)))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            var claims = result.Identity as IIdentityClaims;
            Assert.That(claims, Is.Not.Null);
            Assert.That(claims.Subject, Is.EqualTo("subject-1"));
            Assert.That(claims.Groups, Does.Contain("engineering"));
            Assert.That(claims.Roles, Does.Contain("operator"));
            Assert.That(claims.Claims["email"], Is.EqualTo("operator@example.test"));
        }

        [Test]
        public async Task AuthenticateAsyncBadProofRejected()
        {
            using InMemoryKeyCredentialStore store = await CreateStoreAsync(DateTime.UtcNow.AddMinutes(10))
                .ConfigureAwait(false);
            var authenticator = new KeyCredentialBridgeAuthenticator(store);

            byte[] tokenData = Encoding.UTF8.GetBytes(
                "{\"credentialId\":\"credential-1\",\"nonce\":\"nonce\",\"issuedAt\":" +
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() +
                ",\"proof\":\"bad\"}");
            AuthenticationResult result = await authenticator.AuthenticateAsync(CreateContext(tokenData))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public async Task RejectsTokenWithMalformedBase64UrlProof()
        {
            using InMemoryKeyCredentialStore store = await CreateStoreAsync(DateTime.UtcNow.AddMinutes(10))
                .ConfigureAwait(false);
            var authenticator = new KeyCredentialBridgeAuthenticator(store);

            byte[] tokenData = Encoding.UTF8.GetBytes(
                "{\"credentialId\":\"credential-1\",\"nonce\":\"nonce\",\"issuedAt\":" +
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() +
                ",\"proof\":\"not-base64!@#\"}");
            AuthenticationResult result = await authenticator.AuthenticateAsync(CreateContext(tokenData))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task AuthenticateAsyncExpiredCredentialRejected()
        {
            using InMemoryKeyCredentialStore store = await CreateStoreAsync(DateTime.UtcNow.AddMinutes(-1))
                .ConfigureAwait(false);
            var authenticator = new KeyCredentialBridgeAuthenticator(store);

            AuthenticationResult result = await authenticator.AuthenticateAsync(
                    CreateContext(CreateTokenData(CredentialId, s_secret)))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public async Task AuthenticateAsyncUnknownCredentialRejected()
        {
            using var store = new InMemoryKeyCredentialStore();
            var authenticator = new KeyCredentialBridgeAuthenticator(store);

            AuthenticationResult result = await authenticator.AuthenticateAsync(
                    CreateContext(CreateTokenData(CredentialId, s_secret)))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public void ExperimentalAttributeIsPresent()
        {
#if NET8_0_OR_GREATER
            object attribute = typeof(KeyCredentialBridgeAuthenticator)
                .GetCustomAttributes(false)
                .SingleOrDefault(a => a.GetType().FullName ==
                    "System.Diagnostics.CodeAnalysis.ExperimentalAttribute");
            Assert.That(attribute, Is.Not.Null);
#else
            Assert.Pass("ExperimentalAttribute is emitted on net8.0 and greater only.");
#endif
        }

        private static async Task<InMemoryKeyCredentialStore> CreateStoreAsync(DateTime expiration)
        {
            var store = new InMemoryKeyCredentialStore();
            var claims = new Dictionary<string, object>
            {
                ["iss"] = "urn:test:issuer",
                ["sub"] = "subject-1",
                ["email"] = "operator@example.test",
                ["groups"] = s_groups,
                ["roles"] = s_roles
            };
            await store.UpdateAsync(
                    CredentialId,
                    new Server.KeyCredential(s_secret, expiration, claims, s_scopes),
                    CancellationToken.None)
                .ConfigureAwait(false);
            return store;
        }

        private static AuthenticationContext CreateContext(byte[] tokenData)
        {
            return new AuthenticationContext(
                new IssuedIdentityTokenHandler(KeyCredentialBridgeOptions.DefaultProfileUri, tokenData),
                new UserTokenPolicy
                {
                    TokenType = UserTokenType.IssuedToken,
                    PolicyId = "keycredential",
                    IssuedTokenType = KeyCredentialBridgeOptions.DefaultProfileUri
                },
                new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
        }

        private static byte[] CreateTokenData(string credentialId, byte[] secret)
        {
            return KeyCredentialBridgeAuthenticator.CreateTokenData(
                credentialId,
                secret,
                "nonce-" + Guid.NewGuid().ToString("N"),
                DateTime.UtcNow);
        }
    }
}

#pragma warning restore OPCUA_EXPERIMENTAL_KC_BRIDGE
