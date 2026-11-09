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
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
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
    /// Pins protected SKS metadata, push-target parameters, group scope and malformed-server response handling.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionSecurityOperationTests
    {
        [TestCase((ushort)1, UserTokenType.Certificate)]
        [TestCase((ushort)16, UserTokenType.Anonymous)]
        public async Task AddPushTargetPreservesPublicIdentityAndPolicyWithoutEmbeddingCredentialsAsync(
            ushort requested, UserTokenType tokenType)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: Options("pubsub_security_add_target", "folder"), networkRules: TargetRules())
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            NodeId folder = fixture.Remote.Id("folder");
            NodeId target = fixture.Remote.Id("assigned-target");
            fixture.Remote.Type(folder, ObjectTypeIds.PubSubKeyPushTargetFolderType);
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(target));

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubSecurityTools>()
                .AddTargetAsync(folder.ToString(), Application, Endpoint, SecurityPolicies.Basic256Sha256,
                    tokenType, requested, 1234, Session).ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            CallMethodRequest call = fixture.Remote.Calls.Single();
            Assert.That(call.ObjectId, Is.EqualTo(folder));
            Assert.That(call.MethodId, Is.EqualTo(MethodIds.PubSubKeyPushTargetFolderType_AddPushTarget));
            Assert.That(call.InputArguments, Has.Count.EqualTo(6));
            Assert.That(call.InputArguments[0].TryGetValue(out string? application), Is.True);
            Assert.That(application, Is.EqualTo(Application));
            Assert.That(call.InputArguments[1].TryGetValue(out string? endpoint), Is.True);
            Assert.That(endpoint, Is.EqualTo(Endpoint));
            Assert.That(call.InputArguments[2].TryGetValue(out string? policy), Is.True);
            Assert.That(policy, Is.EqualTo(SecurityPolicies.Basic256Sha256));
            Assert.That(call.InputArguments[3].TryGetStructure<UserTokenPolicy>(
                fixture.Remote.Context, out UserTokenPolicy? token), Is.True);
            Assert.That(token!.TokenType, Is.EqualTo(tokenType));
            Assert.That(token.PolicyId, Is.Null.Or.Empty);
            Assert.That(token.IssuedTokenType, Is.Null.Or.Empty);
            Assert.That(call.InputArguments[4].TryGetValue(out ushort count), Is.True);
            Assert.That(count, Is.EqualTo(requested));
            Assert.That(call.InputArguments[5].TryGetValue(out double retry), Is.True);
            Assert.That(retry, Is.EqualTo(1234));
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("nodeId").GetString(),
                Is.EqualTo(target.ToString()));
            Assert.That(fixture.Transports.Factories, Is.Zero);
        }

        [TestCase("policy")]
        [TestCase("token")]
        [TestCase("zero-count")]
        [TestCase("high-count")]
        [TestCase("nan-retry")]
        [TestCase("endpoint")]
        [TestCase("application")]
        public async Task InvalidPushTargetParametersFailBeforeCreatingARemoteEntryAsync(string invalid)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: Options("pubsub_security_add_target", "folder"), networkRules: TargetRules())
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            NodeId folder = fixture.Remote.Id("folder");
            fixture.Remote.Type(folder, ObjectTypeIds.PubSubKeyPushTargetFolderType);

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubSecurityTools>()
                .AddTargetAsync(folder.ToString(), invalid == "application" ? "urn:other:application" : Application,
                    invalid == "endpoint" ? "opc.tcp://other.test:4840" : Endpoint,
                    invalid == "policy" ? SecurityPolicies.None : SecurityPolicies.Basic256Sha256,
                    invalid == "token" ? (UserTokenType)99 : UserTokenType.Certificate,
                    invalid == "zero-count" ? (ushort)0 : invalid == "high-count" ? (ushort)17 : (ushort)1,
                    invalid == "nan-retry" ? double.NaN : 1000, Session).ConfigureAwait(false);

            if (invalid is "endpoint" or "application")
            {
                RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            }
            else
            {
                RemoteModelTestSession.ArgumentError(result);
            }
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(fixture.Transports.Factories, Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task GroupAssociationsPreserveOrderAndPartialResultsForTheExactTargetAsync(bool connect)
        {
            string operation = connect ? "pubsub_security_connect_groups" : "pubsub_security_disconnect_groups";
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: Options(operation, "target", "group-a", "group-b")).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            NodeId target = fixture.Remote.Id("target");
            fixture.Remote.Type(target, ObjectTypeIds.PubSubKeyPushTargetType);
            ArrayOf<StatusCode> statuses = [StatusCodes.Good, StatusCodes.BadNodeIdUnknown];
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(statuses));
            var input = new PubSubNodesInput
            {
                Nodes =
                [
                    new PubSubNodeInput { NodeId = fixture.Remote.Id("group-b").ToString() },
                    new PubSubNodeInput { NodeId = fixture.Remote.Id("group-a").ToString() }
                ]
            };
            PubSubSecurityTools tools = fixture.Services.GetRequiredService<PubSubSecurityTools>();

            CallToolResult result = connect
                ? await tools.ConnectGroupsAsync(target.ToString(), input, Session).ConfigureAwait(false)
                : await tools.DisconnectGroupsAsync(target.ToString(), input, Session).ConfigureAwait(false);

            CallMethodRequest call = fixture.Remote.Calls.Single();
            Assert.That(call.ObjectId, Is.EqualTo(target));
            Assert.That(call.MethodId, Is.EqualTo(connect
                ? MethodIds.PubSubKeyPushTargetType_ConnectSecurityGroups
                : MethodIds.PubSubKeyPushTargetType_DisconnectSecurityGroups));
            Assert.That(call.InputArguments[0].TryGetValue(out ArrayOf<NodeId> groups), Is.True);
            Assert.That(groups[0], Is.EqualTo(fixture.Remote.Id("group-b")));
            Assert.That(groups[1], Is.EqualTo(fixture.Remote.Id("group-a")));
            JsonElement output = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.True);
            Assert.That(output.GetProperty("methodCompleted").GetBoolean(), Is.True);
            Assert.That(output.GetProperty("partial").GetBoolean(), Is.True);
            Assert.That(output.GetProperty("results")[0].GetProperty("code").GetUInt32(),
                Is.EqualTo(StatusCodes.Good.Code));
            Assert.That(output.GetProperty("results")[1].GetProperty("code").GetUInt32(),
                Is.EqualTo(StatusCodes.BadNodeIdUnknown.Code));
        }

        [TestCase(0)]
        [TestCase(17)]
        [TestCase(2)]
        public async Task EveryGroupRequiresAuthorityBeforeAnyBatchAssociationAsync(int count)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: Options("pubsub_security_connect_groups", "target", "group-a")).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            NodeId target = fixture.Remote.Id("target");
            fixture.Remote.Type(target, ObjectTypeIds.PubSubKeyPushTargetType);
            var input = new PubSubNodesInput
            {
                Nodes = new ArrayOf<PubSubNodeInput>(Enumerable.Range(0, count).Select(index =>
                    new PubSubNodeInput
                    {
                        NodeId = fixture.Remote.Id(index == 0 ? "group-a" : "unauthorized").ToString()
                    }).ToArray())
            };

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubSecurityTools>()
                .ConnectGroupsAsync(target.ToString(), input, Session).ConfigureAwait(false);

            if (count == 2)
            {
                RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            }
            else
            {
                RemoteModelTestSession.ArgumentError(result);
            }
            Assert.That(fixture.Remote.Calls, Is.Empty, "Do not mutate the authorized prefix of a rejected batch.");
        }

        [Test]
        public async Task TriggerAndRemoveTargetNeverClaimRecipientApplicationAcknowledgementAsync()
        {
            McpPubSubOptions options = Options("pubsub_security_trigger_update", "target") with
            {
                ModelRules =
                [
                    new McpPubSubModelRule
                    {
                        SessionName = Session, NodeId = Node("target"),
                        Operations = ["pubsub_security_trigger_update"]
                    },
                    new McpPubSubModelRule
                    {
                        SessionName = Session, NodeId = Node("folder"),
                        Operations = ["pubsub_security_remove_target"]
                    }
                ]
            };
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: options).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            fixture.Remote.Type(fixture.Remote.Id("target"), ObjectTypeIds.PubSubKeyPushTargetType);
            fixture.Remote.Type(fixture.Remote.Id("folder"), ObjectTypeIds.PubSubKeyPushTargetFolderType);
            fixture.Remote.Reply(StatusCodes.Good);
            PubSubSecurityTools tools = fixture.Services.GetRequiredService<PubSubSecurityTools>();

            CallToolResult triggered = await tools.TriggerUpdateAsync(
                fixture.Remote.Id("target").ToString(), Session).ConfigureAwait(false);
            Assert.That(triggered.IsError, Is.False);
            Assert.That(RemoteModelTestSession.Payload(triggered)
                .GetProperty("recipientApplyAcknowledged").GetBoolean(), Is.False);
            Assert.That(fixture.Remote.Calls[0].MethodId,
                Is.EqualTo(MethodIds.PubSubKeyPushTargetType_TriggerKeyUpdate));
            Assert.That(fixture.Remote.Calls[0].InputArguments, Is.Empty);

            CallToolResult removed = await tools.RemoveTargetAsync(
                fixture.Remote.Id("folder").ToString(), fixture.Remote.Id("target").ToString(), Session)
                .ConfigureAwait(false);
            Assert.That(removed.IsError, Is.False);
            Assert.That(fixture.Remote.Calls[1].MethodId,
                Is.EqualTo(MethodIds.PubSubKeyPushTargetFolderType_RemovePushTarget));
            Assert.That(fixture.Remote.Calls[1].InputArguments[0].TryGetValue(out NodeId sent), Is.True);
            Assert.That(sent, Is.EqualTo(fixture.Remote.Id("target")));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task PublicGroupInspectionDoesNotRetrieveAnyKeyMaterialAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: Options("pubsub_security_inspect", "group")).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            NodeId group = fixture.Remote.Id("group");
            fixture.Remote.Type(group, ObjectTypeIds.SecurityGroupType);
            fixture.Remote.Property(group, new QualifiedName(BrowseNames.SecurityGroupId), Variant.From("production"));
            fixture.Remote.Property(group, new QualifiedName(BrowseNames.KeyLifetime), Variant.From(12000.0));

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubSecurityTools>()
                .InspectAsync(group.ToString(), Session).ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            JsonElement output = RemoteModelTestSession.Payload(result);
            Assert.That(output.GetProperty(BrowseNames.SecurityGroupId).GetProperty("present").GetBoolean(), Is.True);
            using var decoder = new JsonDecoder("{\"value\":" +
                output.GetProperty(BrowseNames.SecurityGroupId)
                    .GetProperty("value").GetRawText() +
                "}", fixture.Remote.Context);
            Assert.That(decoder.ReadDataValue("value").WrappedValue.TryGetValue(out string? id), Is.True);
            Assert.That(id, Is.EqualTo("production"));
            Assert.That(output.GetProperty(BrowseNames.MaxPastKeyCount).GetProperty("present").GetBoolean(), Is.False);
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(fixture.Remote.Reads, Has.Count.EqualTo(2));
        }

        [TestCase("length")]
        [TestCase("count")]
        [TestCase("next-time")]
        [TestCase("duration")]
        [TestCase("policy")]
        public async Task MalformedSksResponsesAreRejectedAndEveryReturnedKeyBufferIsClearedAsync(string invalid)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: PullOptions()).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            byte[] first = RandomNumberGenerator.GetBytes(invalid == "length" ? 51 : 52);
            byte[] second = RandomNumberGenerator.GetBytes(52);
            string original = Convert.ToBase64String(first);
            fixture.Remote.Type(ObjectIds.PublishSubscribe, ObjectTypeIds.PubSubKeyServiceType);
            fixture.Remote.OnCall = request =>
            {
                if (request.MethodId == MethodIds.PubSubKeyServiceType_GetSecurityGroup)
                {
                    return Reply(Variant.From(fixture.Remote.Id("group")));
                }
                Assert.That(request.MethodId, Is.EqualTo(MethodIds.PubSubKeyServiceType_GetSecurityKeys));
                ArrayOf<ByteString> keys = invalid == "count"
                    ? [ByteString.From(first), ByteString.From(second)] : [ByteString.From(first)];
                return Reply(
                    Variant.From(invalid == "policy" ? SecurityPolicies.None : PubSubSecurityPolicyUri.PubSubAes128Ctr),
                    Variant.From(8u), Variant.From(keys),
                    Variant.From(invalid == "next-time" ? -1.0 : 1000.0),
                    Variant.From(invalid == "duration" ? double.PositiveInfinity : 1000.0));
            };

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubSecurityTools>()
                .PullKeysAsync("line", 1, Session).ConfigureAwait(false);

            if (invalid is "duration" or "policy")
            {
                RemoteModelTestSession.ArgumentError(result);
            }
            else
            {
                RemoteModelTestSession.Error(result, StatusCodes.BadDecodingError);
            }
            Assert.That(first, Is.All.Zero);
            if (invalid == "count")
            {
                Assert.That(second, Is.All.Zero);
            }
            Assert.That(RemoteModelTestSession.Payload(result).GetRawText(), Does.Not.Contain(original));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(2));
        }

        [TestCase(0.0)]
        [TestCase(500.0)]
        public async Task FutureKeyMetadataPreservesTokenRolloverAndActualActivationWindowsAsync(double toNext)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: PullOptions()).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            fixture.Remote.Type(ObjectIds.PublishSubscribe, ObjectTypeIds.PubSubKeyServiceType);
            byte[] first = RandomNumberGenerator.GetBytes(68);
            byte[] second = RandomNumberGenerator.GetBytes(68);
            byte[] expected = [.. second];
            fixture.Remote.OnCall = request =>
            {
                if (request.MethodId == MethodIds.PubSubKeyServiceType_GetSecurityGroup)
                {
                    return Reply(Variant.From(fixture.Remote.Id("group")));
                }
                ArrayOf<ByteString> keys = [ByteString.From(first), ByteString.From(second)];
                return Reply(Variant.From(PubSubSecurityPolicyUri.PubSubAes256Ctr),
                    Variant.From(uint.MaxValue), Variant.From(keys), Variant.From(toNext), Variant.From(1000.0));
            };
            DateTimeOffset now = fixture.Clock.GetUtcNow();
            double remaining = toNext == 0 ? 1000 : toNext;

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubSecurityTools>()
                .PullKeysAsync("line", 2, Session).ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            JsonElement keys = RemoteModelTestSession.Payload(result).GetProperty("keys");
            Assert.That(keys.GetArrayLength(), Is.EqualTo(2));
            Assert.That(keys[0].GetProperty("tokenId").GetUInt32(), Is.EqualTo(uint.MaxValue));
            Assert.That(keys[1].GetProperty("tokenId").GetUInt32(), Is.Zero);
            Assert.That(keys[0].GetProperty("current").GetBoolean(), Is.True);
            Assert.That(keys[1].GetProperty("current").GetBoolean(), Is.False);
            Assert.That(keys[0].GetProperty("expiresAt").GetDateTimeOffset(),
                Is.EqualTo(now.AddMilliseconds(remaining)));
            Assert.That(keys[1].GetProperty("notBefore").GetDateTimeOffset(),
                Is.EqualTo(now.AddMilliseconds(remaining)));
            Assert.That(keys[1].GetProperty("expiresAt").GetDateTimeOffset(),
                Is.EqualTo(now.AddMilliseconds(remaining + 1000)));
            using ISecret persisted = await fixture.Secrets.ResolveAsync(
                keys[1].GetProperty("reference").GetString()!, keys[1].GetProperty("resource").GetString()!,
                McpCapability.PubSubSecurity).ConfigureAwait(false);
            Assert.That(persisted.Bytes.Length, Is.EqualTo(68));
            Assert.That(persisted.Bytes.ToArray(), Is.EqualTo(expected));
            Assert.That(first, Is.All.Zero);
            Assert.That(second, Is.All.Zero);
        }

        private static McpPubSubOptions Options(string operation, params string[] nodes)
        {
            return new McpPubSubOptions
            {
                ModelRules = new ArrayOf<McpPubSubModelRule>(nodes.Select(name => new McpPubSubModelRule
                {
                    SessionName = Session,
                    NodeId = Node(name),
                    Operations = [operation]
                }).ToArray())
            };
        }

        private static McpPubSubOptions PullOptions()
        {
            return Options("pubsub_security_pull_keys", "group") with
            {
                ModelRules =
                [
                    new McpPubSubModelRule
                    {
                        SessionName = Session, NodeId = ObjectIds.PublishSubscribe.ToString(),
                        Operations = ["pubsub_security_pull_keys"]
                    },
                    new McpPubSubModelRule
                    {
                        SessionName = Session, NodeId = Node("group"), Operations = ["pubsub_security_pull_keys"]
                    }
                ]
            };
        }

        private static ArrayOf<McpNetworkRule> TargetRules()
        {
            return
            [
                new McpNetworkRule
                {
                    Scheme = "opc.tcp", Host = "target.test", Port = 4840, Networks = ["127.0.0.0/8"],
                    Resources = [Application], Operations = ["configure-sks-target"],
                    Capability = McpCapability.PubSubSecurity
                }
            ];
        }

        private static string Node(string name)
        {
            return "nsu=urn:test:remote-model;s=" + name;
        }

        private static CallMethodResult Reply(params Variant[] arguments)
        {
            return new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = arguments.ToArrayOf() };
        }

        private const string Session = PubSubExpansionTestContext.SessionName;
        private const string Endpoint = "opc.tcp://target.test:4840";
        private const string Application = "urn:test:sks-target";
    }
}
#endif
