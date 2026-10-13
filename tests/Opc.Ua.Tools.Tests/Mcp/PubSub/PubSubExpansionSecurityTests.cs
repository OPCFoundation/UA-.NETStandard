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
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.PubSub.Security;
using Opc.Ua.Tools.Tests.McpCompanion;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Protected storage, group/token ownership, expiry and secret-free SKS method results.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionSecurityTests
    {
        [Test]
        public async Task GroupCreateRotationAndInvalidationUseTypedMethodsAndInvalidateTrackedKeysAsync()
        {
            string[] operations =
            [
                "pubsub_security_add_group", "pubsub_security_rotate",
                "pubsub_security_invalidate", "pubsub_security_remove_group"
            ];
            var options = new McpPubSubOptions
            {
                ModelRules = new ArrayOf<McpPubSubModelRule>(s_groupNodes.Select(node =>
                    new McpPubSubModelRule
                    {
                        SessionName = PubSubExpansionTestContext.SessionName,
                        NodeId = "nsu=urn:test:remote-model;s=" + node,
                        Operations = new ArrayOf<string>(operations)
                    }).ToArray())
            };
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext
                .CreateAsync(options: options).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            NodeId folder = fixture.Remote.Id("folder");
            NodeId group = fixture.Remote.Id("group");
            fixture.Remote.Type(folder, ObjectTypeIds.SecurityGroupFolderType);
            fixture.Remote.Type(group, ObjectTypeIds.SecurityGroupType);
            PubSubSecurityTools tools = fixture.Services.GetRequiredService<PubSubSecurityTools>();
            fixture.Remote.Reply(StatusCodes.Good, Variant.From("line"), Variant.From(group));

            CallToolResult created = await tools.AddGroupAsync(folder.ToString(), "line",
                PubSubSecurityPolicyUri.PubSubAes128Ctr, 1000, 2, 1, PubSubExpansionTestContext.SessionName)
                .ConfigureAwait(false);
            Assert.That(created.IsError, Is.False);
            Assert.That(RemoteModelTestSession.Payload(created).GetProperty("securityGroupId").GetString(),
                Is.EqualTo("line"));
            Assert.That(fixture.Remote.Calls[^1].MethodId,
                Is.EqualTo(MethodIds.SecurityGroupFolderType_AddSecurityGroup));
            Assert.That(fixture.Remote.Calls[^1].InputArguments[1].TryGetValue(out double duration), Is.True);
            Assert.That(duration, Is.EqualTo(1000));
            string resource = McpSecretReferences.GetResourceBinding(
                fixture.Sessions.GetSessionInfoOrThrow(PubSubExpansionTestContext.SessionName), group);
            McpSecretReference key = await fixture.Secrets.StoreIssuedAsync(
                ByteString.From(RandomNumberGenerator.GetBytes(52)), McpCapability.PubSubSecurity,
                "pubsub-key:line", resource, fixture.Clock.GetUtcNow().AddMinutes(1), "7").ConfigureAwait(false);
            McpPubSubKeyReferences keys = fixture.Services.GetRequiredService<McpPubSubKeyReferences>();
            keys.Track(key, PubSubSecurityPolicyUri.PubSubAes128Ctr, keys.Epoch(resource),
                fixture.Sessions.GetSessionInfoOrThrow(PubSubExpansionTestContext.SessionName).Endpoint);
            var input = new PubSubSecurityInput
            {
                GroupId = "line",
                PolicyUri = PubSubSecurityPolicyUri.PubSubAes128Ctr,
                KeyReference = key.Reference,
                Resource = resource,
                TokenId = 7,
                ExpiresAt = key.ExpiresAt
            };
            McpSecretDependency dependency = keys.Dependency(input);
            fixture.Remote.Reply(StatusCodes.Good);
            CallToolResult rotated = await tools.RotateAsync(group.ToString(), PubSubExpansionTestContext.SessionName)
                .ConfigureAwait(false);
            Assert.That(rotated.IsError, Is.False);
            Assert.That(fixture.Remote.Calls[^1].MethodId, Is.EqualTo(MethodIds.SecurityGroupType_ForceKeyRotation));
            Assert.That(dependency.Invalidated.IsCompleted, Is.False, "Rotation is not key invalidation.");

            CallToolResult invalidated = await tools.InvalidateAsync(
                group.ToString(), PubSubExpansionTestContext.SessionName).ConfigureAwait(false);
            await dependency.Invalidated.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(invalidated.IsError, Is.False);
            Assert.That(RemoteModelTestSession.Payload(invalidated)
                .GetProperty("localReferencesInvalidated").GetInt32(),
                Is.EqualTo(1));
            Assert.That(fixture.Remote.Calls[^1].MethodId, Is.EqualTo(MethodIds.SecurityGroupType_InvalidateKeys));
            Assert.Throws<ServiceResultException>(() => keys.Dependency(input));

            CallToolResult removed = await tools.RemoveGroupAsync(
                folder.ToString(), group.ToString(), PubSubExpansionTestContext.SessionName).ConfigureAwait(false);
            Assert.That(removed.IsError, Is.False);
            Assert.That(fixture.Remote.Calls[^1].MethodId,
                Is.EqualTo(MethodIds.SecurityGroupFolderType_RemoveSecurityGroup));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(4));
        }

        [Test]
        public async Task ProtectedPushUsesExactTokenAndKeyBytesButNeverReturnsThemAsync()
        {
            var options = new McpPubSubOptions
            {
                ModelRules =
                [new McpPubSubModelRule
                {
                    SessionName = PubSubExpansionTestContext.SessionName,
                    NodeId = ObjectIds.PublishSubscribe.ToString(), Operations = ["pubsub_security_push_keys"]
                }]
            };
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext
                .CreateAsync(options: options).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            fixture.Remote.Type(ObjectIds.PublishSubscribe, ObjectTypeIds.PublishSubscribeType);
            PubSubSecurityInput input = await StoreKeyAsync(fixture, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
            using ISecret expected = await fixture.Secrets.ResolveAsync(
                input.KeyReference, input.Resource, McpCapability.PubSubSecurity).ConfigureAwait(false);
            bool matched = false;
            fixture.Remote.OnCall = call =>
            {
                Assert.That(call.MethodId, Is.EqualTo(MethodIds.PublishSubscribeType_SetSecurityKeys));
                Assert.That(call.InputArguments[2].TryGetValue(out uint token), Is.True);
                Assert.That(token, Is.EqualTo(7));
                Assert.That(call.InputArguments[3].TryGetValue(out ByteString key), Is.True);
                matched = key.Span.SequenceEqual(expected.Bytes);
                Assert.That(call.InputArguments[5].TryGetValue(out double remaining), Is.True);
                Assert.That(remaining, Is.EqualTo(60000));
                return new CallMethodResult { StatusCode = StatusCodes.Good };
            };

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubSecurityTools>()
                .PushKeysAsync(input, PubSubExpansionTestContext.SessionName).ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            Assert.That(matched, Is.True);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            Assert.That(RemoteModelTestSession.Payload(result).GetRawText(),
                Does.Not.Contain(Convert.ToBase64String(expected.Bytes)));
        }

        [Test]
        public async Task IssuedKeyExpiryClosesOwnedTransportWithoutAnotherRequestAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            PubSubSecurityInput security = await StoreKeyAsync(fixture, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            PubSubApplicationInput input = PubSubExpansionTestContext.Input() with { Security = security };
            string handle = (await fixture.Runtimes.CreateAsync(input).ConfigureAwait(false))
                ["handle"]!.GetValue<string>();
            _ = await fixture.Runtimes.StartAsync(handle).ConfigureAwait(false);
            PubSubExpansionTransport transport = fixture.Transports.Created.Single();
            McpRuntimeUse<McpPubSubRuntime> use = await fixture.Runtimes.AcquireAsync(handle, CancellationToken.None)
                .ConfigureAwait(false);
            await using (use.ConfigureAwait(false))
            {
                EndpointDescription keyService = use.Runtime.Application().GetConfiguration()
                    .Connections[0].WriterGroups[0].SecurityKeyServices.ToList().Single();
                Assert.That(keyService.EndpointUrl, Is.EqualTo(fixture.Sessions
                    .GetSessionInfoOrThrow(PubSubExpansionTestContext.SessionName).Endpoint.EndpointUrl));
                Assert.That(keyService.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                Assert.That(keyService.UserIdentityTokens, Is.Empty);
            }

            fixture.Clock.Advance(TimeSpan.FromSeconds(30));
            await transport.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(transport.IsConnected, Is.False);
            Assert.That(transport.DisposeCalls, Is.EqualTo(1));
            Assert.That(fixture.Transports.Factories, Is.EqualTo(1));
        }

        [Test]
        public async Task RevocationClosesActiveRuntimeAndRefusesReuseBeforeActivationAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            PubSubSecurityInput security = await StoreKeyAsync(fixture, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            PubSubApplicationInput input = PubSubExpansionTestContext.Input() with { Security = security };
            string handle = (await fixture.Runtimes.CreateAsync(input).ConfigureAwait(false))
                ["handle"]!.GetValue<string>();
            _ = await fixture.Runtimes.StartAsync(handle).ConfigureAwait(false);
            PubSubExpansionTransport transport = fixture.Transports.Created.Single();

            await fixture.Secrets.RevokeReferenceAsync(security.KeyReference).ConfigureAwait(false);
            await transport.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            ServiceResultException? refused = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Runtimes.CreateAsync(input with { Name = "retry" }).ConfigureAwait(false));

            Assert.That(refused!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(transport.DisposeCalls, Is.EqualTo(1));
            Assert.That(fixture.Transports.Factories, Is.EqualTo(1));
        }

        [Test]
        public async Task FutureKeyCannotActivateBeforeItsTrackedNotBeforeBoundaryAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            DateTimeOffset boundary = fixture.Clock.GetUtcNow().AddMinutes(1);
            PubSubSecurityInput security = await StoreKeyAsync(
                fixture, TimeSpan.FromMinutes(2), notBefore: boundary).ConfigureAwait(false);
            PubSubApplicationInput input = PubSubExpansionTestContext.Input() with { Security = security };

            ServiceResultException? early = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Runtimes.CreateAsync(input).ConfigureAwait(false));
            fixture.Clock.Advance(TimeSpan.FromMinutes(1));
            JsonObject accepted = await fixture.Runtimes.CreateAsync(input).ConfigureAwait(false);

            Assert.That(early!.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            Assert.That(accepted["started"]!.GetValue<bool>(), Is.False);
            Assert.That(fixture.Registry.List(), Has.Count.EqualTo(1));
            Assert.That(fixture.Transports.Factories, Is.Zero);
        }

        [Test]
        public async Task GroupPolicyTokenAndCallerMustMatchTheProtectedKeyAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            PubSubSecurityInput security;
            using (fixture.Enter())
            {
                security = await StoreKeyAsync(fixture, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
                foreach (PubSubSecurityInput invalid in new[]
                {
                    security with { GroupId = "other" },
                    security with { TokenId = 99 },
                    security with { PolicyUri = PubSubSecurityPolicyUri.PubSubAes256Ctr }
                })
                {
                    ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await fixture.Runtimes.CreateAsync(
                            PubSubExpansionTestContext.Input() with { Security = invalid })
                            .ConfigureAwait(false));
                    Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
                }
            }
            using (fixture.Enter(PubSubExpansionTestContext.Other))
            {
                Assert.ThrowsAsync<ServiceResultException>(async () => await fixture.Runtimes.CreateAsync(
                    PubSubExpansionTestContext.Input() with { Security = security }).ConfigureAwait(false));
                Assert.That(fixture.Registry.List(), Is.Empty);
            }
            Assert.That(fixture.Transports.Factories, Is.Zero);
        }

        [Test]
        public async Task TypedSksPullPersistsBeforeReturningReferenceAndNeverReturnsRawKeyAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: PullOptions()).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            byte[] expected = RandomNumberGenerator.GetBytes(52);
            ConfigureKeyService(fixture, expected);

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubSecurityTools>()
                .PullKeysAsync("line", sessionName: PubSubExpansionTestContext.SessionName).ConfigureAwait(false);
            JsonElement output = RemoteModelTestSession.Payload(result);
            JsonElement key = output.GetProperty("keys")[0];
            using ISecret stored = await fixture.Secrets.ResolveAsync(key.GetProperty("reference").GetString()!,
                key.GetProperty("resource").GetString()!, McpCapability.PubSubSecurity).ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            Assert.That(output.GetProperty("keys").GetArrayLength(), Is.EqualTo(1));
            Assert.That(key.GetProperty("tokenId").GetUInt32(), Is.EqualTo(7));
            Assert.That(stored.Bytes.SequenceEqual(expected), Is.True);
            Assert.That(output.GetRawText(), Does.Not.Contain(Convert.ToBase64String(expected)));
            Assert.That(output.GetRawText(), Does.Not.Contain(Convert.ToHexString(expected)));
            Assert.That(fixture.Remote.Calls[^1].MethodId,
                Is.EqualTo(MethodIds.PubSubKeyServiceType_GetSecurityKeys));
            Assert.That(fixture.Remote.Calls[^1].InputArguments[2].TryGetValue(out uint requested), Is.True);
            Assert.That(requested, Is.EqualTo(1));
        }

        [Test]
        public async Task StorageFailureReportsRetrievedKeysAndPartialPersistenceWithoutLeakingProviderTextAsync()
        {
            var store = new FailingStore();
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: PullOptions(), issuanceStore: store).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            byte[] expected = RandomNumberGenerator.GetBytes(52);
            ConfigureKeyService(fixture, expected);

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubSecurityTools>()
                .PullKeysAsync("line", sessionName: PubSubExpansionTestContext.SessionName).ConfigureAwait(false);
            JsonElement output = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.True);
            Assert.That(output.GetProperty("remoteKeysRetrieved").GetBoolean(), Is.True);
            Assert.That(output.GetProperty("localPersistence").GetString(), Is.EqualTo("failed"));
            Assert.That(output.GetProperty("partial").GetBoolean(), Is.True);
            Assert.That(output.GetProperty("keys").GetArrayLength(), Is.Zero);
            Assert.That(output.GetRawText(), Does.Not.Contain(Convert.ToBase64String(expected)));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(2),
                "No hidden repeat of the completed GetSecurityKeys call.");
            Assert.That(store.SetCalls, Is.EqualTo(1));
        }

        private static async Task<PubSubSecurityInput> StoreKeyAsync(
            PubSubExpansionTestContext fixture, TimeSpan lifetime, DateTimeOffset? notBefore = null)
        {
            DateTimeOffset expiry = fixture.Clock.GetUtcNow() + lifetime;
            McpSecretReference reference = await fixture.Secrets.StoreIssuedAsync(
                ByteString.From(RandomNumberGenerator.GetBytes(52)), McpCapability.PubSubSecurity,
                "pubsub-key:line", "urn:test:line", expiry, "7").ConfigureAwait(false);
            McpPubSubKeyReferences references = fixture.Services.GetRequiredService<McpPubSubKeyReferences>();
            references.Track(reference, PubSubSecurityPolicyUri.PubSubAes128Ctr,
                references.Epoch(reference.Resource),
                fixture.Sessions.GetSessionInfoOrThrow(PubSubExpansionTestContext.SessionName).Endpoint, notBefore);
            return new PubSubSecurityInput
            {
                GroupId = "line",
                PolicyUri = PubSubSecurityPolicyUri.PubSubAes128Ctr,
                KeyReference = reference.Reference,
                Resource = reference.Resource,
                TokenId = 7,
                ExpiresAt = expiry
            };
        }

        private static McpPubSubOptions PullOptions()
        {
            return new McpPubSubOptions
            {
                ModelRules =
                [
                    new McpPubSubModelRule
                    {
                        SessionName = PubSubExpansionTestContext.SessionName,
                        NodeId = ObjectIds.PublishSubscribe.ToString(), Operations = ["pubsub_security_pull_keys"]
                    },
                    new McpPubSubModelRule
                    {
                        SessionName = PubSubExpansionTestContext.SessionName,
                        NodeId = "nsu=urn:test:remote-model;s=group", Operations = ["pubsub_security_pull_keys"]
                    }
                ]
            };
        }

        private static void ConfigureKeyService(PubSubExpansionTestContext fixture, byte[] key)
        {
            fixture.Remote.Type(ObjectIds.PublishSubscribe, ObjectTypeIds.PublishSubscribeType);
            fixture.Remote.Subtype(ObjectTypeIds.PublishSubscribeType, ObjectTypeIds.PubSubKeyServiceType);
            fixture.Remote.OnCall = request =>
            {
                ArrayOf<Variant> output = request.MethodId == MethodIds.PubSubKeyServiceType_GetSecurityGroup
                    ? [Variant.From(fixture.Remote.Id("group"))]
                    :
                    [
                        Variant.From(PubSubSecurityPolicyUri.PubSubAes128Ctr), Variant.From(7u),
                        Variant.From(new ArrayOf<ByteString>(new[] { ByteString.From(key.ToArray()) })),
                        Variant.From(1000d), Variant.From(1000d)
                    ];
                return new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = output };
            };
        }

        private static readonly string[] s_groupNodes = ["folder", "group"];

        private sealed class FailingStore : ISecretStore
        {
            public string StoreType => "FailingPubSub";
            internal int SetCalls { get; private set; }

            public ISecret? TryGet(SecretIdentifier id)
            {
                return null;
            }

            public ValueTask<ISecret?> GetAsync(SecretIdentifier id, CancellationToken ct = default)
            {
                return ValueTask.FromResult<ISecret?>(null);
            }

            public ValueTask SetAsync(SecretIdentifier id, ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
            {
                SetCalls++;
                throw new IOException("Provider diagnostic " + Convert.ToBase64String(bytes.Span));
            }

            public ValueTask<bool> RemoveAsync(SecretIdentifier id, CancellationToken ct = default)
            {
                return ValueTask.FromResult(true);
            }
        }
    }
}
#endif
