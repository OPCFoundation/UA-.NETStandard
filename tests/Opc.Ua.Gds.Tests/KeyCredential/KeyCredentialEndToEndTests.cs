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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Gds.Server;
using Opc.Ua.Identity;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.KeyCredential
{
    [TestFixture]
    [Category("KeyCredential")]
    public class KeyCredentialEndToEndTests
    {
        private static readonly string[] s_roles = ["operator"];

        [Test]
        public async Task PullPushBridgeAuthenticatesOneSessionSmoke()
        {
            var gdsStore = new InMemoryKeyCredentialRequestStore(new InMemorySecretStore("GdsKeyCredentialTest"));
            NodeId requestId = await gdsStore.StartRequestAsync(
                    "urn:test:client",
                    default,
                    SecurityPolicies.Basic256Sha256,
                    [],
                    CancellationToken.None)
                .ConfigureAwait(false);
            FinishKeyCredentialRequestResult finished = await gdsStore.FinishRequestAsync(
                    requestId,
                    cancelRequest: false,
                    CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(finished.State, Is.EqualTo(KeyCredentialRequestState.Completed));

            string credentialId = finished.CredentialId!;
            ByteString credentialSecret = finished.CredentialSecret;
            string securityPolicyUri = finished.SecurityPolicyUri!;

            using var resourceStore = new InMemoryKeyCredentialStore();
            await resourceStore.UpdateAsync(
                    credentialId,
                    new Ua.Server.KeyCredential(
                        credentialSecret.ToArray(),
                        DateTime.UtcNow.AddMinutes(10),
                        new Dictionary<string, object>
                        {
                            ["iss"] = "urn:test:gds",
                            ["sub"] = "urn:test:client",
                            ["roles"] = s_roles
                        },
                        [securityPolicyUri]),
                    CancellationToken.None)
                .ConfigureAwait(false);

            using var provider = new GdsKeyCredentialAccessTokenProvider(
                _ => new ValueTask<GdsIssuedKeyCredential>(new GdsIssuedKeyCredential(
                    credentialId,
                    credentialSecret.ToArray(),
                    DateTime.UtcNow.AddMinutes(5),
                    [securityPolicyUri])),
                "urn:test:gds");
            var policy = new UserTokenPolicy
            {
                TokenType = UserTokenType.IssuedToken,
                PolicyId = "keycredential",
                IssuedTokenType = KeyCredentialBridgeOptions.DefaultProfileUri,
                IssuerEndpointUrl = /*lang=json,strict*/ "{\"authorityUri\":\"urn:test:gds\"}"
            };
            var endpoint = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                Server = new ApplicationDescription { ApplicationUri = "urn:test:resource-server" },
                UserIdentityTokens = [policy]
            };
            var identityProvider = new IssuedTokenIdentityProvider(provider, KeyCredentialBridgeOptions.DefaultProfileUri);
            IUserIdentity clientIdentity = await identityProvider.GetIdentityAsync(
                policy,
                new IdentitySelectionContext(
                    endpoint, [policy], ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create())))
                .ConfigureAwait(false);
            AuthenticationResult result;
            try
            {
                var tokenHandler = (IssuedIdentityTokenHandler)clientIdentity.TokenHandler;
                using var payload = JsonDocument.Parse(tokenHandler.DecryptedTokenData);
                Assert.That(payload.RootElement.GetProperty("version").GetInt32(), Is.EqualTo(2));
                Assert.That(payload.RootElement.GetProperty("aud").GetString(), Is.EqualTo("urn:test:resource-server"));
                var authenticator = new KeyCredentialBridgeAuthenticator(resourceStore);
                AuthenticationResult wrongTarget = await authenticator.AuthenticateAsync(
                    CreateContext(tokenHandler, "urn:test:another-server")).ConfigureAwait(false);
                Assert.That(wrongTarget.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
                Assert.That(wrongTarget.Error.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
                result = await authenticator.AuthenticateAsync(
                        CreateContext(tokenHandler, endpoint.Server.ApplicationUri))
                    .ConfigureAwait(false);
            }
            finally
            {
                (clientIdentity as IDisposable)?.Dispose();
            }

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            var claims = result.Identity as IIdentityClaims;
            Assert.That(claims, Is.Not.Null);
            Assert.That(claims.Subject, Is.EqualTo("urn:test:client"));
            Assert.That(claims.Roles, Does.Contain("operator"));
        }

        private static AuthenticationContext CreateContext(
            IssuedIdentityTokenHandler tokenHandler,
            string targetApplicationUri)
        {
            return new AuthenticationContext(
                tokenHandler,
                new UserTokenPolicy
                {
                    TokenType = UserTokenType.IssuedToken,
                    PolicyId = "keycredential",
                    IssuedTokenType = KeyCredentialBridgeOptions.DefaultProfileUri
                },
                new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    Server = new ApplicationDescription { ApplicationUri = targetApplicationUri }
                },
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
        }
    }
}

#pragma warning restore OPCUA_EXPERIMENTAL_KC_BRIDGE
