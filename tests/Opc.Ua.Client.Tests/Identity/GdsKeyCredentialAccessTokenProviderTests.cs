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
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.Identity
{
    [TestFixture]
    [Category("Client")]
    [Category("Identity")]
    public sealed class GdsKeyCredentialAccessTokenProviderTests
    {
        [TestCase(null, null, "urn:target-server")]
        [TestCase("urn:explicit", "urn:resource", "urn:explicit")]
        [TestCase(null, "urn:resource", "urn:resource")]
        [TestCase(" ", "", "urn:target-server")]
        public async Task IssuedIdentityAcquisitionBindsProofToSelectedAudienceAsync(
            string? audience,
            string? resource,
            string expectedAudience)
        {
            var clock = new FakeTimeProvider();
            using var accessTokens = new GdsKeyCredentialAccessTokenProvider(
                _ => new ValueTask<GdsIssuedKeyCredential>(new GdsIssuedKeyCredential(
                    "credential", [1, 2, 3, 4], clock.GetUtcNow().UtcDateTime.AddMinutes(10))),
                "urn:authority", timeProvider: clock);
            var provider = new IssuedTokenIdentityProvider(accessTokens, GdsKeyCredentialAccessTokenProvider.ProfileUri);
            var metadata = new JsonObject
            {
                ["authorityUri"] = "urn:authority",
                ["audience"] = audience,
                ["ua:resourceUri"] = resource
            };
            var policy = new UserTokenPolicy
            {
                PolicyId = "keycredential",
                TokenType = UserTokenType.IssuedToken,
                IssuedTokenType = GdsKeyCredentialAccessTokenProvider.ProfileUri,
                IssuerEndpointUrl = metadata.ToJsonString()
            };
            var endpoint = new EndpointDescription
            {
                Server = new ApplicationDescription { ApplicationUri = "urn:target-server" },
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                UserIdentityTokens = [policy]
            };
            var context = new IdentitySelectionContext(
                endpoint, [policy], ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));

            if (expectedAudience != "urn:target-server")
            {
                ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await provider.GetIdentityAsync(policy, context).ConfigureAwait(false));
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
                return;
            }
            IUserIdentity identity = await provider.GetIdentityAsync(policy, context).ConfigureAwait(false);
            try
            {
                var handler = (IssuedIdentityTokenHandler)identity.TokenHandler;
                using var payload = JsonDocument.Parse(handler.DecryptedTokenData);
                Assert.That(identity.PolicyId, Is.EqualTo("keycredential"));
                Assert.That(payload.RootElement.GetProperty("version").GetInt32(), Is.EqualTo(2));
                Assert.That(payload.RootElement.GetProperty("aud").GetString(), Is.EqualTo(expectedAudience));
                Assert.That(payload.RootElement.GetProperty("issuedAt").GetInt64(),
                    Is.EqualTo(clock.GetUtcNow().ToUnixTimeSeconds()));
            }
            finally
            {
                (identity as IDisposable)?.Dispose();
            }
        }

        [TestCase("urn:server:B", null)]
        [TestCase(null, "urn:server:B")]
        [TestCase("urn:server:B", "urn:server:B")]
        [TestCase("urn:server:A", "urn:server:B")]
        [TestCase("urn:server:B", "urn:server:A")]
        [TestCase("URN:server:A", null)]
        [Category("SecurityRegression")]
        public async Task SecurityR03RecipientCannotChooseAnotherServerAudienceAsync(string? audience, string? resource)
        {
            int acquisitions = 0;
            using var accessTokens = new GdsKeyCredentialAccessTokenProvider(
                _ =>
                {
                    acquisitions++;
                    return new ValueTask<GdsIssuedKeyCredential>(
                        new GdsIssuedKeyCredential("credential", [1, 2, 3, 4], DateTime.MaxValue));
                }, "urn:authority");
            var provider = new IssuedTokenIdentityProvider(accessTokens, GdsKeyCredentialAccessTokenProvider.ProfileUri);
            var policy = new UserTokenPolicy
            {
                PolicyId = "experimental-keycredential",
                TokenType = UserTokenType.IssuedToken,
                IssuedTokenType = GdsKeyCredentialAccessTokenProvider.ProfileUri,
                IssuerEndpointUrl = new JsonObject
                {
                    ["authorityUri"] = "urn:authority",
                    ["audience"] = audience,
                    ["ua:resourceUri"] = resource
                }.ToJsonString()
            };
            var endpoint = new EndpointDescription
            {
                Server = new ApplicationDescription { ApplicationUri = "urn:server:A" },
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                UserIdentityTokens = [policy]
            };
            var context = new IdentitySelectionContext(
                endpoint, [policy], ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));

            ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.GetIdentityAsync(policy, context).ConfigureAwait(false));

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
            Assert.That(acquisitions, Is.Zero, "The recipient is checked before credential acquisition or delivery.");
        }

        [TestCase(null, null)]
        [TestCase(" ", "")]
        [TestCase("urn:server:A", "urn:server:A")]
        [Category("SecurityRegression")]
        public async Task SecurityR03EndpointAudienceProducesVerifiableTargetProofAsync(string? audience, string? resource)
        {
            var clock = new FakeTimeProvider();
            byte[] secret = [1, 2, 3, 4];
            using var provider = new GdsKeyCredentialAccessTokenProvider(
                _ => new ValueTask<GdsIssuedKeyCredential>(
                    new GdsIssuedKeyCredential("credential", [.. secret], DateTime.MaxValue)),
                "urn:authority", timeProvider: clock);
            using AccessToken token = await provider.AcquireAsync(
                new AuthorizationServerMetadata
                {
                    AuthorityUri = "urn:authority", Audience = audience, ResourceUri = resource
                },
                new EndpointDescription { Server = new ApplicationDescription { ApplicationUri = "urn:server:A" } })
                .ConfigureAwait(false);
            using var payload = JsonDocument.Parse(token.TokenData.ToArray());
            JsonElement root = payload.RootElement;
            Assert.That(root.GetProperty("version").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("aud").GetString(), Is.EqualTo("urn:server:A"));
            Assert.That(root.GetProperty("issuedAt").GetInt64(), Is.EqualTo(clock.GetUtcNow().ToUnixTimeSeconds()));
            string nonce = root.GetProperty("nonce").GetString()!;
            Assert.That(Convert.FromBase64String(nonce.Replace('-', '+').Replace('_', '/') + "="),
                Has.Length.EqualTo(32));
            Assert.That(VerifyProof(root, secret, "urn:server:A"), Is.True);
            Assert.That(VerifyProof(root, secret, "urn:server:B"), Is.False);
        }

        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR03ConflictingMetadataAndCachedCredentialsCannotRetargetProofAsync()
        {
            int acquisitions = 0;
            byte[] secret = [1, 2, 3, 4];
            using var provider = new GdsKeyCredentialAccessTokenProvider(
                _ =>
                {
                    acquisitions++;
                    return new ValueTask<GdsIssuedKeyCredential>(
                        new GdsIssuedKeyCredential("credential", [.. secret], DateTime.MaxValue));
                }, "urn:authority");
            var metadata = new AuthorizationServerMetadata { AuthorityUri = "urn:authority" };
            using AccessToken first = await provider.AcquireAsync(metadata,
                new EndpointDescription { Server = new ApplicationDescription { ApplicationUri = "urn:server:A" } })
                .ConfigureAwait(false);
            using AccessToken second = await provider.AcquireAsync(metadata,
                new EndpointDescription { Server = new ApplicationDescription { ApplicationUri = "urn:server:B" } })
                .ConfigureAwait(false);
            using var a = JsonDocument.Parse(first.TokenData.ToArray());
            using var b = JsonDocument.Parse(second.TokenData.ToArray());
            Assert.That(acquisitions, Is.EqualTo(1));
            Assert.That(VerifyProof(a.RootElement, secret, "urn:server:A"), Is.True);
            Assert.That(VerifyProof(b.RootElement, secret, "urn:server:B"), Is.True);
            Assert.That(VerifyProof(a.RootElement, secret, "urn:server:B"), Is.False);
            ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.AcquireAsync(
                    new AuthorizationServerMetadata { Audience = "urn:server:B", AuthorityUri = "urn:authority" },
                    new EndpointDescription { Server = new ApplicationDescription { ApplicationUri = string.Empty } })
                    .ConfigureAwait(false));
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
            Assert.That(acquisitions, Is.EqualTo(1));
            using AccessToken trustedExplicit = await provider.AcquireAsync(
                new AuthorizationServerMetadata { AuthorityUri = "urn:authority", Audience = "urn:server:B" })
                .ConfigureAwait(false);
            using var explicitPayload = JsonDocument.Parse(trustedExplicit.TokenData.ToArray());
            Assert.That(VerifyProof(explicitPayload.RootElement, secret, "urn:server:B"), Is.True);
        }

        private static bool VerifyProof(JsonElement token, byte[] secret, string audience)
        {
            string input = token.GetProperty("credentialId").GetString() + "\n" +
                token.GetProperty("nonce").GetString() + "\n" +
                token.GetProperty("issuedAt").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "\n" + audience;
            using var hmac = new HMACSHA256(secret);
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(input)))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_') ==
                token.GetProperty("proof").GetString();
        }

        [Test]
        public async Task EndpointAudienceIsNotCachedWithTheCredentialAsync()
        {
            int acquisitions = 0;
            using var provider = new GdsKeyCredentialAccessTokenProvider(
                _ =>
                {
                    acquisitions++;
                    return new ValueTask<GdsIssuedKeyCredential>(
                        new GdsIssuedKeyCredential("credential", [1, 2, 3], DateTime.MaxValue));
                }, "urn:authority");
            var metadata = new AuthorizationServerMetadata { AuthorityUri = "urn:authority" };
            using AccessToken first = await provider.AcquireAsync(metadata, new EndpointDescription
            {
                Server = new ApplicationDescription { ApplicationUri = "urn:first-server" }
            }).ConfigureAwait(false);
            using AccessToken second = await provider.AcquireAsync(metadata, new EndpointDescription
            {
                Server = new ApplicationDescription { ApplicationUri = "urn:second-server" }
            }).ConfigureAwait(false);
            using var firstPayload = JsonDocument.Parse(first.TokenData.ToArray());
            using var secondPayload = JsonDocument.Parse(second.TokenData.ToArray());

            Assert.That(acquisitions, Is.EqualTo(1));
            Assert.That(firstPayload.RootElement.GetProperty("aud").GetString(), Is.EqualTo("urn:first-server"));
            Assert.That(secondPayload.RootElement.GetProperty("aud").GetString(), Is.EqualTo("urn:second-server"));
            Assert.That(firstPayload.RootElement.GetProperty("nonce").GetString(),
                Is.Not.EqualTo(secondPayload.RootElement.GetProperty("nonce").GetString()));
        }

        [Test]
        public async Task MissingAudienceIsRejectedBeforeAcquiringCredential()
        {
            int acquisitions = 0;
            using var provider = new GdsKeyCredentialAccessTokenProvider(
                _ =>
                {
                    acquisitions++;
                    return new ValueTask<GdsIssuedKeyCredential>(
                        new GdsIssuedKeyCredential("credential", [1], DateTime.MaxValue));
                }, "urn:authority");

            ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.AcquireAsync(new AuthorizationServerMetadata { AuthorityUri = "urn:authority" })
                    .ConfigureAwait(false));

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
            Assert.That(acquisitions, Is.Zero);
        }

        [Test]
        public async Task AcquireAsyncBuildsProofTokenAndCachesCredentialAsync()
        {
            int acquireCount = 0;
            using var provider = new GdsKeyCredentialAccessTokenProvider(
                ct =>
                {
                    acquireCount++;
                    return new ValueTask<GdsIssuedKeyCredential>(
                        new GdsIssuedKeyCredential(
                            "cred\"\\id",
                            [1, 2, 3, 4],
                            DateTime.UtcNow.AddMinutes(10),
                            ["scope-a"]));
                },
                "urn:authority");
            var metadata = new AuthorizationServerMetadata { AuthorityUri = "urn:authority", Audience = "urn:target" };

            AccessToken first = await provider.AcquireAsync(metadata).ConfigureAwait(false);
            AccessToken second = await provider.AcquireAsync(metadata).ConfigureAwait(false);

            Assert.That(acquireCount, Is.EqualTo(1));
            Assert.That(first.ProfileUri, Is.EqualTo(GdsKeyCredentialAccessTokenProvider.ProfileUri));
            Assert.That(first.DisplayName, Is.EqualTo("cred\"\\id"));
            Assert.That(first.GrantedScopes, Has.Length.EqualTo(1));
            Assert.That(first.GrantedScopes[0], Is.EqualTo("scope-a"));
            Assert.That(Encoding.UTF8.GetString(first.TokenData.ToArray()), Does.Contain("\\\"").And.Contain("\\\\"));
            Assert.That(second.DisplayName, Is.EqualTo(first.DisplayName));
        }

        [Test]
        public async Task AcquireAsyncUsesDefaultExpirationWhenCredentialHasNoneAsync()
        {
            DateTime before = DateTime.UtcNow;
            using var provider = new GdsKeyCredentialAccessTokenProvider(
                _ => new ValueTask<GdsIssuedKeyCredential>(
                    new GdsIssuedKeyCredential("cred", [1, 2, 3], DateTime.MinValue)),
                "urn:authority",
                TimeSpan.FromMinutes(1));
            var metadata = new AuthorizationServerMetadata { AuthorityUri = "urn:authority", Audience = "urn:target" };

            AccessToken token = await provider.AcquireAsync(metadata).ConfigureAwait(false);

            Assert.That(token.ExpiresAt, Is.GreaterThanOrEqualTo(before.AddSeconds(30)));
            Assert.That(token.ExpiresAt, Is.LessThanOrEqualTo(DateTime.UtcNow.AddMinutes(2)));
        }

        [Test]
        public void ConstructorRejectsInvalidArguments()
        {
            Assert.That(
                () => new GdsKeyCredentialAccessTokenProvider(
                    null!,
                    "urn:authority"),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => new GdsKeyCredentialAccessTokenProvider(
                    _ => new ValueTask<GdsIssuedKeyCredential>(
                        new GdsIssuedKeyCredential("cred", [1], DateTime.UtcNow)),
                    null!),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => new GdsKeyCredentialAccessTokenProvider(
                    new object(),
                    "urn:authority",
                    string.Empty),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task DisposeClearsCachedCredentialAndPreventsAcquireAsync()
        {
            var provider = new GdsKeyCredentialAccessTokenProvider(
                _ => new ValueTask<GdsIssuedKeyCredential>(
                    new GdsIssuedKeyCredential("cred", [1, 2, 3], DateTime.UtcNow.AddMinutes(10))),
                "urn:authority");
            var metadata = new AuthorizationServerMetadata { AuthorityUri = "urn:authority", Audience = "urn:target" };

            await provider.AcquireAsync(metadata).ConfigureAwait(false);
            FieldInfo cachedCredentialField = typeof(GdsKeyCredentialAccessTokenProvider).GetField(
                "m_cachedCredential",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var cachedCredential = (GdsIssuedKeyCredential)cachedCredentialField.GetValue(provider)!;
            byte[] cachedSecret = cachedCredential.CredentialSecret;

            provider.Dispose();
            provider.Dispose();

            Assert.That(cachedSecret, Is.EqualTo(new byte[] { 0, 0, 0 }));
            Assert.That(
                async () => await provider.AcquireAsync(metadata).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task ReflectionClientSupportsValueTaskAndTaskShapesAsync()
        {
            var client = new ReflectionShapeClient();
            using var provider = new GdsKeyCredentialAccessTokenProvider(
                client,
                "urn:authority",
                "urn:app",
                (ByteString)"public-key"u8.ToArray(),
                SecurityPolicies.Basic256Sha256,
                [ObjectIds.WellKnownRole_AuthenticatedUser]);
            var metadata = new AuthorizationServerMetadata { AuthorityUri = "urn:authority", Audience = "urn:target" };

            AccessToken token = await provider.AcquireAsync(metadata).ConfigureAwait(false);

            Assert.That(client.StartRequestApplicationUri, Is.EqualTo("urn:app"));
            Assert.That(client.StartRequestSecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
            Assert.That(client.StartRequestRoles, Is.EqualTo([ObjectIds.WellKnownRole_AuthenticatedUser]));
            Assert.That(token.DisplayName, Is.EqualTo("credential-id"));
            Assert.That(token.GrantedScopes, Does.Contain(SecurityPolicies.Basic256Sha256));
            Assert.That(token.GrantedScopes, Does.Contain(ObjectIds.WellKnownRole_AuthenticatedUser.ToString()));
        }

        private sealed class ReflectionShapeClient
        {
            public string? StartRequestApplicationUri { get; private set; }

            public string? StartRequestSecurityPolicyUri { get; private set; }

            public ArrayOf<NodeId> StartRequestRoles { get; private set; } = [];

            public ValueTask<NodeId> StartRequestAsync(
                string applicationUri,
                ByteString publicKey,
                string? securityPolicyUri,
                ArrayOf<NodeId> requestedRoles,
                CancellationToken ct)
            {
                _ = publicKey;
                _ = ct;
                StartRequestApplicationUri = applicationUri;
                StartRequestSecurityPolicyUri = securityPolicyUri;
                StartRequestRoles = requestedRoles;
                return new ValueTask<NodeId>(new NodeId(1234));
            }

            public Task<(string credentialId, ByteString credentialSecret, string certificateThumbprint,
                string securityPolicyUri, ArrayOf<NodeId> grantedRoles)> FinishRequestAsync(
                    NodeId requestId,
                    bool cancel,
                    CancellationToken ct)
            {
                _ = requestId;
                _ = cancel;
                _ = ct;
                ArrayOf<NodeId> grantedRoles = [ObjectIds.WellKnownRole_AuthenticatedUser];
                return Task.FromResult((
                    "credential-id",
                    (ByteString)"secret"u8.ToArray(),
                    "thumbprint",
                    SecurityPolicies.Basic256Sha256,
                    grantedRoles));
            }
        }
    }
}
