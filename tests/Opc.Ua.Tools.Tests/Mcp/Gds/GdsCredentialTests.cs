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

#if NET10_0
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using GdsMethods = Opc.Ua.Gds.MethodIds;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Exercises concrete credential issuance, cancellation, ownership, persistence and token rotation.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class GdsCredentialTests
    {
        [Test]
        public async Task KeyStartBindsRequestedRoleArrayAndReturnsTheRealRequestIdAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(fixture.Remote.Id("key-request")));

            CallToolResult result = await fixture.InvokeAsync("credential_start_key_request", new JsonObject
            {
                ["serviceNodeId"] = fixture.Remote.Id("key-service").ToString(),
                ["input"] = new JsonObject
                {
                    ["applicationUri"] = "urn:test:client",
                    ["securityPolicyUri"] = SecurityPolicies.Basic256Sha256,
                    ["requestedRoles"] = new JsonArray("i=15644", "i=15668")
                }
            }).ConfigureAwait(false);

            fixture.Remote.AssertCall(fixture.Remote.Id("key-service"),
                GdsMethods.KeyCredentialServiceType_StartRequest,
                Variant.From("urn:test:client"), Variant.From(default(ByteString)),
                Variant.From(SecurityPolicies.Basic256Sha256),
                Variant.From([new NodeId(15644), new NodeId(15668)]));
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("requestId").GetString(),
                Is.EqualTo(fixture.Remote.Id("key-request").ToString()));
            Assert.That(json.GetProperty("resource").GetString(), Is.EqualTo("urn:resource"));
            Assert.That(fixture.Store.SetTokens, Is.Empty);
        }

        [Test]
        public async Task KeyFinishPersistsSecretAndPreservesGrantedRolesWithoutAnyPlaintextOutputAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            ReplyKey(fixture, "key-secret-do-not-output");

            CallToolResult result = await Tools(fixture).FinishKeyRequestAsync(
                fixture.Remote.Id("key-service").ToString(), fixture.Remote.Id("key-request").ToString(), "selected")
                .ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("credentialId").GetString(), Is.EqualTo("credential-17"));
            Assert.That(json.GetProperty("remoteIssuance").GetString(), Is.EqualTo("issued"));
            Assert.That(json.GetProperty("certificateThumbprint").GetString(), Is.EqualTo("public-thumbprint"));
            Assert.That(json.GetProperty("grantedRoles").EnumerateArray()
                .Select(value => value.GetString()), Is.EqualTo(s_grantedRoleIds));
            string reference = json.GetProperty("credential").GetProperty("reference").GetString()!;
            Assert.That(json.GetProperty("credential").GetProperty("resource").GetString(), Is.EqualTo("urn:resource"));
            Assert.That(json.GetProperty("credential").GetProperty("issuer").GetString(),
                Is.EqualTo(McpSecretReferences.GetResourceBinding(
                    fixture.Manager.GetSessionInfoOrThrow("selected"), fixture.Remote.Id("key-service"))));
            using ISecret resolved = await fixture.Secrets.ResolveAsync(
                reference, "urn:resource", McpCapability.CredentialIssuance).ConfigureAwait(false);
            Assert.That(Encoding.UTF8.GetString(resolved.Bytes), Is.EqualTo("key-secret-do-not-output"));
            Assert.That(json.GetRawText(), Does.Not.Contain("key-secret-do-not-output"));
            Assert.That(json.GetRawText(), Does.Not.Contain(Convert.ToBase64String(
                Encoding.UTF8.GetBytes("key-secret-do-not-output"))));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task KeyFinishPreservesRemoteIssuanceWhenLocalPersistenceFailsAsync(bool cleanupFails)
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            ReplyKey(fixture, "key-sensitive-value");
            fixture.Store.SetFailure = new IOException("provider included key-sensitive-value");
            fixture.Store.RemoveFailure = cleanupFails ? new IOException("cleanup included key-sensitive-value") : null;

            CallToolResult result = await Tools(fixture).FinishKeyRequestAsync(
                fixture.Remote.Id("key-service").ToString(), fixture.Remote.Id("key-request").ToString(), "selected")
                .ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.True);
            Assert.That(json.GetProperty("statusCodeValue").GetUInt32(), Is.EqualTo(StatusCodes.Good.Code));
            Assert.That(json.GetProperty("requestId").GetString(),
                Is.EqualTo(fixture.Remote.Id("key-request").ToString()));
            Assert.That(json.GetProperty("credentialId").GetString(), Is.EqualTo("credential-17"));
            Assert.That(json.GetProperty("remoteIssuance").GetString(), Is.EqualTo("issued"));
            Assert.That(json.GetProperty("key-credentialPersistence").GetProperty("localPersistence").GetString(),
                Is.EqualTo("failed"));
            Assert.That(json.GetProperty("key-credentialPersistence").GetProperty("cleanupRequired").GetBoolean(),
                Is.EqualTo(cleanupFails));
            Assert.That(json.GetRawText(), Does.Not.Contain("key-sensitive-value"));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Store.Values, Has.Count.EqualTo(cleanupFails ? 1 : 0));
        }

        [Test]
        public async Task KeyFinishPersistsCommittedMaterialAfterLateCallerCancellationAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            using var cancelled = new CancellationTokenSource();
            ReplyKey(fixture, "late-commit-secret");
            Func<CallMethodRequest, CallMethodResult> reply = fixture.Remote.OnCall;
            fixture.Remote.OnCall = request =>
            {
                cancelled.Cancel();
                return reply(request);
            };

            CallToolResult result = await Tools(fixture).FinishKeyRequestAsync(
                fixture.Remote.Id("key-service").ToString(), fixture.Remote.Id("request").ToString(),
                "selected", cancelled.Token).ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            Assert.That(fixture.Store.SetTokens, Has.Count.EqualTo(1));
            Assert.That(fixture.Store.SetTokens[0].IsCancellationRequested, Is.False);
            Assert.That(fixture.Store.Values, Has.Count.EqualTo(1));
            Assert.That(RemoteModelTestSession.Payload(result).GetRawText(), Does.Not.Contain("late-commit-secret"));
        }

        [TestCase("pending")]
        [TestCase("denied")]
        public async Task PendingAndDeniedKeyRequestsPreserveStatusAndNeverStoreOrRepeatAsync(string state)
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(maxReferences: 1)
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            StatusCode expected = state == "pending" ? StatusCodes.BadNothingToDo : StatusCodes.BadUserAccessDenied;
            fixture.Remote.Reply(expected);

            CallToolResult result = await Tools(fixture).FinishKeyRequestAsync(
                fixture.Remote.Id("key-service").ToString(), fixture.Remote.Id("request").ToString(), "selected")
                .ConfigureAwait(false);

            RemoteModelTestSession.Error(result, expected);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("requestId").GetString(),
                Is.EqualTo(fixture.Remote.Id("request").ToString()));
            Assert.That(fixture.Store.Values, Is.Empty);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            using McpSecretReservation next = fixture.Secrets.ReserveIssuance(McpCapability.CredentialIssuance);
        }

        [Test]
        public async Task KeyCancelCallsOnlyTheStandardFinishCancelOperationAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(writable: false)
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Remote.Reply(StatusCodes.Good);

            CallToolResult result = await Tools(fixture).CancelKeyRequestAsync(
                fixture.Remote.Id("key-service").ToString(), fixture.Remote.Id("request").ToString(), "selected")
                .ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            fixture.Remote.AssertCall(fixture.Remote.Id("key-service"),
                GdsMethods.KeyCredentialServiceType_FinishRequest,
                Variant.From(fixture.Remote.Id("request")), Variant.From(true));
            Assert.That(fixture.Store.SetTokens, Is.Empty);
        }

        [Test]
        public async Task KeyRevokeInvalidatesTheMatchingLocalReferenceAfterRemoteSuccessAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            McpSecretReference reference = await fixture.Secrets.StoreIssuedAsync(
                ByteString.From([17]), McpCapability.CredentialIssuance, "key-credential", "urn:resource",
                fixture.Clock.GetUtcNow().AddMinutes(1), "credential-17").ConfigureAwait(false);
            fixture.Remote.Reply(StatusCodes.Good);

            CallToolResult result = await fixture.Services.GetRequiredService<GdsCredentialTools>().RevokeKeyAsync(
                fixture.Remote.Id("key-service").ToString(), "credential-17", "selected").ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            fixture.Remote.AssertCall(fixture.Remote.Id("key-service"), GdsMethods.KeyCredentialServiceType_Revoke,
                Variant.From("credential-17"));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Secrets.ResolveAsync(
                    reference.Reference, "urn:resource", McpCapability.CredentialIssuance)
                    .ConfigureAwait(false));
            Assert.That(fixture.Store.Values, Is.Empty);
        }

        [Test]
        public async Task TokenStartProtectsServiceChallengeAndBindsTheServerRequestIdAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            var id = Guid.NewGuid();
            fixture.Remote.Reply(StatusCodes.Good,
                Variant.From(ByteString.From(Encoding.UTF8.GetBytes("sensitive-service-challenge"))),
                Variant.From((Uuid)id));

            CallToolResult result = await Tools(fixture).StartTokenRequestAsync(
                fixture.Remote.Id("auth-service").ToString(), "urn:resource", "jwt", sessionName: "selected")
                .ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("requestId").GetString(), Is.EqualTo(id.ToString()));
            Assert.That(json.GetRawText(), Does.Not.Contain("sensitive-service-challenge"));
            string reference = json.GetProperty("serviceData").GetProperty("reference").GetString()!;
            using ISecret challenge = await fixture.Secrets.ResolveAsync(
                reference, "urn:resource", McpCapability.CredentialIssuance).ConfigureAwait(false);
            Assert.That(Encoding.UTF8.GetString(challenge.Bytes), Is.EqualTo("sensitive-service-challenge"));
            fixture.Remote.AssertCall(fixture.Remote.Id("auth-service"),
                GdsMethods.AuthorizationServiceType_StartRequestToken,
                Variant.From("urn:resource"), Variant.From("jwt"), Variant.From(default(ByteString)));
        }

        [Test]
        public async Task TokenFinishReservesThePairBeforeRemoteDispatchAndDoesNotConsumeTheRequestAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(maxReferences: 1)
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            string id = await StartTokenAsync(fixture).ConfigureAwait(false);

            CallToolResult result = await Tools(fixture).FinishTokenRequestAsync(
                fixture.Remote.Id("auth-service").ToString(), id, new GdsTokenFinishInput(), "selected")
                .ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadTooManyOperations);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Remote.Calls.Single().MethodId,
                Is.EqualTo(fixture.Remote.Model(GdsMethods.AuthorizationServiceType_StartRequestToken)));
            Assert.That(fixture.Store.SetTokens, Is.Empty);
        }

        [Test]
        public async Task TokenFinishReturnsProtectedPairExactExpiriesAndNativeAnonymousProofAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(maxReferences: 2)
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            string id = await StartTokenAsync(fixture).ConfigureAwait(false);
            DateTimeOffset expiry = fixture.Clock.GetUtcNow().AddMinutes(5);
            ReplyTokens(fixture, expiry, "access-sensitive", "refresh-sensitive");

            CallToolResult result = await fixture.InvokeAsync("credential_finish_token_request", new JsonObject
            {
                ["serviceNodeId"] = fixture.Remote.Id("auth-service").ToString(),
                ["requestId"] = id,
                ["input"] = new JsonObject
                {
                    ["requestedRoles"] = new JsonArray("operator"),
                    ["proof"] = new JsonObject { ["kind"] = "Anonymous", ["policyId"] = "anonymous" }
                }
            }).ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("accessTokenExpiresAt").GetDateTimeOffset(), Is.EqualTo(expiry));
            Assert.That(json.GetProperty("refreshTokenExpiresAt").GetDateTimeOffset(),
                Is.EqualTo(expiry.AddMinutes(10)));
            Assert.That(json.GetRawText(), Does.Not.Contain("access-sensitive").And.Not.Contain("refresh-sensitive"));
            using ISecret access = await fixture.Secrets.ResolveAsync(
                json.GetProperty("accessToken").GetProperty("reference").GetString()!, "urn:resource",
                McpCapability.CredentialIssuance).ConfigureAwait(false);
            using ISecret refresh = await fixture.Secrets.ResolveAsync(
                json.GetProperty("refreshToken").GetProperty("reference").GetString()!, "urn:resource",
                McpCapability.CredentialIssuance).ConfigureAwait(false);
            Assert.That(Encoding.UTF8.GetString(access.Bytes), Is.EqualTo("access-sensitive"));
            Assert.That(Encoding.UTF8.GetString(refresh.Bytes), Is.EqualTo("refresh-sensitive"));
            CallMethodRequest finish = fixture.Remote.Calls[1];
            Assert.That(finish.MethodId, Is.EqualTo(fixture.Remote.Model(
                GdsMethods.AuthorizationServiceType_FinishRequestToken)));
            Assert.That(finish.InputArguments[1].TryGetValue(out ArrayOf<string> roles), Is.True);
            Assert.That(roles.ToArray(), Is.EqualTo(s_requestedRoles));
            Assert.That(finish.InputArguments[2].TryGetStructure<AnonymousIdentityToken>(
                fixture.Remote.Context, out AnonymousIdentityToken? identity), Is.True);
            Assert.That(identity!.PolicyId, Is.EqualTo("anonymous"));
            Assert.That(fixture.Store.Values, Has.Count.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TokenRequestIsBoundToCallerAndSessionGenerationAsync(bool replaceSession)
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            string id;
            using (fixture.Enter())
            {
                id = await StartTokenAsync(fixture).ConfigureAwait(false);
            }
            if (replaceSession)
            {
                await fixture.Manager.RegisterExistingSessionAsync(
                    "selected", fixture.Connection.Managed, "Anonymous")
                    .ConfigureAwait(false);
            }
            using IDisposable caller = fixture.Enter(replaceSession ? GdsTestContext.Owner : GdsTestContext.Other);

            CallToolResult result = await Tools(fixture).FinishTokenRequestAsync(
                fixture.Remote.Id("auth-service").ToString(), id, new GdsTokenFinishInput(), "selected")
                .ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Store.SetTokens, Is.Empty);
        }

        [Test]
        public async Task TokenRotationInvalidatesOldReferenceAndPersistsBothNewTokensAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(maxReferences: 3)
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            McpSecretReference old;
            using (McpSecretReservation admitted = fixture.Secrets.ReserveIssuance(McpCapability.CredentialIssuance))
            {
                old = await admitted.StoreForIssuerAsync(
                    ByteString.From(Encoding.UTF8.GetBytes("old-refresh-sensitive")), "refresh-token", "urn:resource",
                    McpSecretReferences.GetResourceBinding(
                        fixture.Manager.GetSessionInfoOrThrow("selected"), fixture.Remote.Id("auth-service")),
                    fixture.Clock.GetUtcNow().AddMinutes(1)).ConfigureAwait(false);
            }
            ReplyTokens(
                fixture, fixture.Clock.GetUtcNow().AddMinutes(2), "new-access-sensitive", "new-refresh-sensitive");

            CallToolResult result = await Tools(fixture).RefreshTokenAsync(
                fixture.Remote.Id("auth-service").ToString(), "urn:resource", old.Reference, "selected")
                .ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("previousReference").GetString(), Is.EqualTo("invalidated"));
            Assert.That(json.GetRawText(), Does.Not.Contain("old-refresh-sensitive")
                .And.Not.Contain("new-access-sensitive").And.Not.Contain("new-refresh-sensitive"));
            Assert.That(fixture.Store.Values, Has.Count.EqualTo(2));
            fixture.Remote.AssertCall(fixture.Remote.Id("auth-service"),
                GdsMethods.AuthorizationServiceType_RefreshToken,
                Variant.From("urn:resource"), Variant.From("old-refresh-sensitive"));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Secrets.ResolveAsync(old.Reference, "urn:resource", McpCapability.CredentialIssuance)
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task TokenPairRetainsTheRefreshResultWhenAccessPersistenceFailsAfterRemoteSuccessAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            string id = await StartTokenAsync(fixture).ConfigureAwait(false);
            ReplyTokens(fixture, fixture.Clock.GetUtcNow().AddMinutes(1), "access-sensitive", "refresh-sensitive");
            int writes = 0;
            fixture.Store.AfterSet = () =>
            {
                fixture.Store.SetFailure = writes++ == 0 ? new IOException("access-sensitive") : null;
                return ValueTask.CompletedTask;
            };

            CallToolResult result = await Tools(fixture).FinishTokenRequestAsync(
                fixture.Remote.Id("auth-service").ToString(), id, new GdsTokenFinishInput(), "selected")
                .ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.True);
            Assert.That(json.GetProperty("remoteIssuance").GetString(), Is.EqualTo("issued"));
            Assert.That(json.GetProperty("requestId").GetString(), Is.EqualTo(id));
            Assert.That(json.GetProperty("accessToken").ValueKind, Is.EqualTo(JsonValueKind.Null));
            string reference = json.GetProperty("refreshToken").GetProperty("reference").GetString()!;
            using ISecret retained = await fixture.Secrets.ResolveAsync(
                reference, "urn:resource", McpCapability.CredentialIssuance).ConfigureAwait(false);
            Assert.That(Encoding.UTF8.GetString(retained.Bytes), Is.EqualTo("refresh-sensitive"));
            Assert.That(fixture.Store.Values, Has.Count.EqualTo(1));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(2));
            Assert.That(json.GetRawText(), Does.Not.Contain("access-sensitive").And.Not.Contain("refresh-sensitive"));
        }

        [Test]
        public async Task RefreshTokenCannotBeForwardedToAnotherIssuerSharingTheSameAudienceAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            string id = await StartTokenAsync(fixture).ConfigureAwait(false);
            ReplyTokens(fixture, fixture.Clock.GetUtcNow().AddMinutes(1), "access-sensitive", "refresh-sensitive");
            CallToolResult issued = await Tools(fixture).FinishTokenRequestAsync(
                fixture.Remote.Id("auth-service").ToString(), id, new GdsTokenFinishInput(), "selected")
                .ConfigureAwait(false);
            string reference = RemoteModelTestSession.Payload(issued)
                .GetProperty("refreshToken").GetProperty("reference").GetString()!;
            fixture.Remote.Type(fixture.Remote.Id("other-issuer"), Gds.ObjectTypeIds.AuthorizationServiceType);

            CallToolResult refused = await Tools(fixture).RefreshTokenAsync(
                fixture.Remote.Id("other-issuer").ToString(), "urn:resource", reference, "selected")
                .ConfigureAwait(false);

            RemoteModelTestSession.Error(refused, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(2));
            Assert.That(fixture.Store.Views, Is.Empty,
                "Issuer refusal must precede materialization of the refresh token.");
            Assert.That(fixture.Store.Values, Has.Count.EqualTo(2));
        }

        [TestCase(GdsIdentityKind.UserName)]
        [TestCase(GdsIdentityKind.IssuedToken)]
        [TestCase(GdsIdentityKind.Certificate)]
        public async Task TokenProofUsesIssuerBoundProtectedBytesAndTheNativeIdentityTokenKindAsync(
            GdsIdentityKind kind)
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            string id = await StartTokenAsync(fixture).ConfigureAwait(false);
            string issuer = McpSecretReferences.GetResourceBinding(
                fixture.Manager.GetSessionInfoOrThrow("selected"), fixture.Remote.Id("auth-service"));
            McpSecretReference proof = await fixture.Secrets.StoreIssuedAsync(
                ByteString.From([1, 2, 3, 4]), McpCapability.CredentialIssuance, "identity-proof", issuer,
                fixture.Clock.GetUtcNow().AddMinutes(2)).ConfigureAwait(false);
            ReplyTokens(fixture, fixture.Clock.GetUtcNow().AddMinutes(1), "access-sensitive", string.Empty);

            CallToolResult result = await Tools(fixture).FinishTokenRequestAsync(
                fixture.Remote.Id("auth-service").ToString(), id, new GdsTokenFinishInput
                {
                    Proof = new GdsTokenProof
                    {
                        Kind = kind,
                        CredentialReference = proof.Reference,
                        PolicyId = "chosen",
                        UserName = "public-user-id",
                        EncryptionAlgorithm = SecurityAlgorithms.RsaOaep
                    }
                }, "selected").ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            Variant identity = fixture.Remote.Calls[1].InputArguments[2];
            if (kind == GdsIdentityKind.UserName)
            {
                Assert.That(identity.TryGetStructure<UserNameIdentityToken>(
                    fixture.Remote.Context, out UserNameIdentityToken? token), Is.True);
                Assert.That(token!.UserName, Is.EqualTo("public-user-id"));
                Assert.That(token.Password.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
                Assert.That(token.EncryptionAlgorithm, Is.EqualTo(SecurityAlgorithms.RsaOaep));
            }
            else if (kind == GdsIdentityKind.IssuedToken)
            {
                Assert.That(identity.TryGetStructure<IssuedIdentityToken>(
                    fixture.Remote.Context, out IssuedIdentityToken? token), Is.True);
                Assert.That(token!.TokenData.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
                Assert.That(token.PolicyId, Is.EqualTo("chosen"));
            }
            else
            {
                Assert.That(identity.TryGetStructure<X509IdentityToken>(
                    fixture.Remote.Context, out X509IdentityToken? token), Is.True);
                Assert.That(token!.CertificateData.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
                Assert.That(token.PolicyId, Is.EqualTo("chosen"));
            }
            Assert.That(RemoteModelTestSession.Payload(result).TryGetProperty("refreshToken", out _), Is.False);
        }

        /// <summary>
        /// Gets the real domain tool from the host.
        /// </summary>
        private static GdsCredentialTools Tools(GdsTestContext fixture)
        {
            return fixture.Services.GetRequiredService<GdsCredentialTools>();
        }

        /// <summary>
        /// Starts a real typed request with a server-generated identifier and no challenge payload.
        /// </summary>
        private static async Task<string> StartTokenAsync(GdsTestContext fixture)
        {
            var id = Guid.NewGuid();
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(default(ByteString)), Variant.From((Uuid)id));
            CallToolResult result = await Tools(fixture).StartTokenRequestAsync(
                fixture.Remote.Id("auth-service").ToString(), "urn:resource", "jwt", sessionName: "selected")
                .ConfigureAwait(false);
            Assert.That(result.IsError, Is.False);
            return RemoteModelTestSession.Payload(result).GetProperty("requestId").GetString()!;
        }

        /// <summary>
        /// Supplies the exact standard KeyCredential output tuple.
        /// </summary>
        private static void ReplyKey(GdsTestContext fixture, string secret)
        {
            fixture.Remote.Reply(StatusCodes.Good,
                Variant.From("credential-17"), Variant.From(ByteString.From(Encoding.UTF8.GetBytes(secret))),
                Variant.From("public-thumbprint"), Variant.From(SecurityPolicies.Basic256Sha256),
                Variant.From([new NodeId(15644), new NodeId(15668)]));
        }

        /// <summary>
        /// Supplies the modern token tuple with independently asserted server timestamps.
        /// </summary>
        private static void ReplyTokens(
            GdsTestContext fixture, DateTimeOffset expiry, string access, string refresh)
        {
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(access), Variant.From((DateTimeUtc)expiry.UtcDateTime),
                Variant.From(refresh), Variant.From((DateTimeUtc)expiry.AddMinutes(10).UtcDateTime));
        }

        private static readonly string[] s_grantedRoleIds = ["i=15644", "i=15668"];
        private static readonly string[] s_requestedRoles = ["operator"];
    }
}
#endif
