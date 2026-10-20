/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root. */

#if NET10_0
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.WotCon;
using RegistryMethods = Opc.Ua.XRegistry.MethodIds;
using RegistryTypes = Opc.Ua.XRegistry.ObjectTypeIds;
using WotMethods = Opc.Ua.WotCon.MethodIds;
using WotNamespaces = Opc.Ua.WotCon.Namespaces;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Actual MCP SDK argument binding, generated refresh DTOs and in-process tool composition.
    /// The UA transport seam is deterministic; these tests do not claim live-server interoperability.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class WotRegistryMcpBindingTests
    {
        [Test]
        public async Task RefreshMcpBinderPreservesSelectorsEveryOptionGenerationAndRequestIdAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            ReplyRefresh(fixture, StatusCodes.GoodResultsMayBeIncomplete, failures: true);
            JsonObject arguments = JsonNode.Parse("""
                {
                  "registryNodeId":"placeholder",
                  "expectedGeneration":17,
                  "maxResults":1,
                    "selection":[
                      {"kind":"ThingDescription","groupId":"things","resourceId":"pump","versionId":"v7","xid":""},
                      {"kind":"ThingModel","groupId":"","resourceId":"","versionId":"v2","xid":"/models/motor"}
                    ],
                  "options":{
                    "allResources":false,
                    "atomicity":"PerClosure",
                    "forceRebuild":true,
                    "dryRun":true,
                    "includeDependents":true,
                    "deletePolicy":"Retire",
                    "maxParallelism":7,
                    "timeoutMilliseconds":42000,
                    "requestId":"refresh-caller-17"
                  }
                }
                """)!.AsObject();
            arguments["registryNodeId"] = fixture.Id("wot-registry").ToString();

            CallToolResult result = await fixture.InvokeAsync("wot_refresh", arguments).ConfigureAwait(false);

            Assert.That(fixture.Calls, Has.Count.EqualTo(1));
            CallMethodRequest call = fixture.Calls[0];
            Assert.That(call.ObjectId, Is.EqualTo(fixture.Id("wot-registry")));
            Assert.That(call.MethodId, Is.EqualTo(fixture.Model(WotMethods.WoTRegistryType_Refresh)));
            Assert.That(call.InputArguments.Count, Is.EqualTo(4));
            Assert.That(call.InputArguments[0].TryGetStructure(
                fixture.Context, out ArrayOf<WoTResourceSelectorDataType> selection), Is.True);
            Assert.That(selection.Count, Is.EqualTo(2));
            Assert.That(selection[0].Kind, Is.EqualTo(WoTDocumentKindEnum.ThingDescription));
            Assert.That(selection[0].GroupId, Is.EqualTo("things"));
            Assert.That(selection[0].ResourceId, Is.EqualTo("pump"));
            Assert.That(selection[0].VersionId, Is.EqualTo("v7"));
            Assert.That(selection[0].Xid, Is.Empty);
            Assert.That(selection[1].Kind, Is.EqualTo(WoTDocumentKindEnum.ThingModel));
            Assert.That(selection[1].Xid, Is.EqualTo("/models/motor"));
            Assert.That(selection[1].VersionId, Is.EqualTo("v2"));
            Assert.That(call.InputArguments[1].TryGetStructure<WoTRefreshOptionsDataType>(
                fixture.Context, out WoTRefreshOptionsDataType? options), Is.True);
            Assert.That(options!.Atomicity, Is.EqualTo(WoTAtomicityEnum.PerClosure));
            Assert.That(options.Force, Is.True);
            Assert.That(options.DryRun, Is.True);
            Assert.That(options.IncludeDependents, Is.True);
            Assert.That(options.DeletePolicy, Is.EqualTo(WoTDeletePolicyEnum.Retire));
            Assert.That(options.MaxParallelism, Is.EqualTo(7));
            Assert.That(options.Timeout, Is.EqualTo(42000));
            Assert.That(call.InputArguments[2], Is.EqualTo(Variant.From(17u)));
            Assert.That(call.InputArguments[3], Is.EqualTo(Variant.From("refresh-caller-17")));
            AssertPartialRefresh(result);
            Assert.That(fixture.Reads, Is.Empty);
            Assert.That(fixture.Operations[^1], Is.EqualTo("call"));
        }

        [TestCaseSource(nameof(s_nonBadStatuses))]
        public async Task RefreshPreservesPartialFailureOutsideReturnedDetailsAndGenerationAsync(
            StatusCode methodStatus)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            ReplyRefresh(fixture, methodStatus, failures: true);

            CallToolResult result = await fixture.Wot.RefreshAsync(
                fixture.Id("wot-registry").ToString(), Selection(), 17, maxResults: 1, sessionName: "selected")
                .ConfigureAwait(false);

            AssertPartialRefresh(result);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(methodStatus.Code));
            Assert.That(fixture.Calls, Has.Count.EqualTo(1), "Paging details must never repeat the refresh mutation.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RefreshIndependentlyRecognizesPerResourceAndSummaryFailuresAsync(bool summaryOnly)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Reply(StatusCodes.Good,
                Variant.FromStructure(new WoTRefreshSummaryDataType
                {
                    RequestId = "distinct-failure",
                    Generation = 25,
                    Total = 1,
                    Failed = summaryOnly ? 1u : 0u,
                    Outcome = summaryOnly ? WoTOutcomeEnum.Failed : WoTOutcomeEnum.Success
                }),
                Variant.FromStructure([new WoTResourceLoadResultDataType
                {
                    ResourceId = "pump", VersionId = "v7",
                    Outcome = summaryOnly ? WoTOutcomeEnum.Success : WoTOutcomeEnum.Failed
                }]),
                Variant.From(25u));

            CallToolResult result = await fixture.Wot.RefreshAsync(
                fixture.Id("wot-registry").ToString(), Selection(), 24, sessionName: "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.True);
            Assert.That(json.GetProperty("newGeneration").GetUInt32(), Is.EqualTo(25));
            Assert.That(json.GetProperty("summary").GetProperty("Failed").GetUInt32(), Is.EqualTo(summaryOnly ? 1 : 0));
            Assert.That(json.GetProperty("results")[0].GetProperty("Outcome").GetString(),
                Is.EqualTo(summaryOnly ? "Success_0" : "Failed_5"));
            Assert.That(fixture.Calls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task RefreshExplicitAllResourcesUsesEmptyArrayAndPreservesFalseOptionsAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            ReplyRefresh(fixture, StatusCodes.Good, failures: false);

            CallToolResult result = await fixture.InvokeAsync("wot_refresh", new JsonObject
            {
                ["registryNodeId"] = fixture.Id("wot-registry").ToString(),
                ["expectedGeneration"] = 1,
                ["selection"] = new JsonArray(),
                ["options"] = JsonNode.Parse("""{"allResources":true,"requestId":"all-explicit"}""")
            }).ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            Assert.That(fixture.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Calls[0].InputArguments[0].TryGetStructure(
                fixture.Context, out ArrayOf<WoTResourceSelectorDataType> selection), Is.True);
            Assert.That(selection.IsEmpty, Is.True);
            Assert.That(selection.IsNull, Is.False);
            Assert.That(fixture.Calls[0].InputArguments[1].TryGetStructure<WoTRefreshOptionsDataType>(
                fixture.Context, out WoTRefreshOptionsDataType? options), Is.True);
            Assert.That(options!.Atomicity, Is.EqualTo(WoTAtomicityEnum.PerResource));
            Assert.That(options.Force, Is.False);
            Assert.That(options.DryRun, Is.False);
            Assert.That(options.IncludeDependents, Is.False);
            Assert.That(options.DeletePolicy, Is.EqualTo(WoTDeletePolicyEnum.Reject));
            Assert.That(options.MaxParallelism, Is.EqualTo(1));
            Assert.That(options.Timeout, Is.EqualTo(30000));
            Assert.That(fixture.Calls[0].InputArguments[3], Is.EqualTo(Variant.From("all-explicit")));
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("newGeneration").GetUInt32(), Is.EqualTo(18));
            Assert.That(json.GetProperty("truncated").GetBoolean(), Is.False);
            Assert.That(json.GetProperty("results").GetArrayLength(), Is.EqualTo(2));
        }

        [TestCase("empty-without-all")]
        [TestCase("selection-with-all")]
        [TestCase("null-selection")]
        [TestCase("null-item")]
        [TestCase("invalid-kind")]
        [TestCase("missing-identity")]
        [TestCase("atomicity")]
        [TestCase("delete-policy")]
        [TestCase("parallelism-zero")]
        [TestCase("parallelism-33")]
        [TestCase("parallelism-34")]
        [TestCase("timeout-zero")]
        [TestCase("timeout-120001")]
        [TestCase("timeout-120002")]
        public async Task RefreshRejectsAmbiguousScopeAndInvalidEnumsOrBoundsBeforeUaWorkAsync(string invalid)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            WotRefreshInput input = Selection();
            switch (invalid)
            {
                case "empty-without-all":
                    input.Selection = [];
                    break;
                case "selection-with-all":
                    input.AllResources = true;
                    break;
                case "null-selection":
                    input.Selection = ArrayOf<WotResourceSelection>.Null;
                    input.AllResources = true;
                    break;
                case "null-item":
                    input.Selection = [null!];
                    break;
                case "invalid-kind":
                    input.Selection[0].Kind = (WoTDocumentKindEnum)99;
                    break;
                case "missing-identity":
                    input.Selection[0].GroupId = " ";
                    input.Selection[0].Xid = " ";
                    break;
                case "atomicity":
                    input.Atomicity = (WoTAtomicityEnum)99;
                    break;
                case "delete-policy":
                    input.DeletePolicy = (WoTDeletePolicyEnum)99;
                    break;
                case "parallelism-zero":
                    input.MaxParallelism = 0;
                    break;
                case "parallelism-33":
                    input.MaxParallelism = 33;
                    break;
                case "parallelism-34":
                    input.MaxParallelism = 34;
                    break;
                case "timeout-zero":
                    input.TimeoutMilliseconds = 0;
                    break;
                case "timeout-120002":
                    input.TimeoutMilliseconds = 120002;
                    break;
                default:
                    input.TimeoutMilliseconds = 120001;
                    break;
            }

            CallToolResult result = await fixture.Wot.RefreshAsync(
                fixture.Id("wot-registry").ToString(), input, 17, sessionName: "selected").ConfigureAwait(false);

            RemoteModelTestSession.ArgumentError(result);
            Assert.That(fixture.Operations, Is.Empty);
        }

        [TestCase(1u, 1u, 1)]
        [TestCase(32u, 120000u, 500)]
        public async Task RefreshAcceptsExactParallelismTimeoutAndResultBoundsAsync(
            uint parallelism, uint timeout, int maxResults)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            ReplyRefresh(fixture, StatusCodes.Good, failures: false);
            WotRefreshInput input = Selection();
            input.MaxParallelism = parallelism;
            input.TimeoutMilliseconds = timeout;

            CallToolResult result = await fixture.Wot.RefreshAsync(
                fixture.Id("wot-registry").ToString(), input, uint.MaxValue,
                maxResults: maxResults, sessionName: "selected")
                .ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            Assert.That(fixture.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Calls[0].InputArguments[1].TryGetStructure<WoTRefreshOptionsDataType>(
                fixture.Context, out WoTRefreshOptionsDataType? options), Is.True);
            Assert.That(options!.MaxParallelism, Is.EqualTo(parallelism));
            Assert.That(options.Timeout, Is.EqualTo(timeout));
            Assert.That(fixture.Calls[0].InputArguments[2], Is.EqualTo(Variant.From(uint.MaxValue)));
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("results").GetArrayLength(),
                Is.EqualTo(maxResults == 1 ? 1 : 2));
        }

        [TestCase(500)]
        [TestCase(501)]
        public async Task RefreshSelectorLimitAccepts500AndRejects501WithoutMutationAsync(int count)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            ReplyRefresh(fixture, StatusCodes.Good, failures: false);
            WotRefreshInput input = Selection();
            input.Selection = Enumerable.Range(0, count).Select(index => new WotResourceSelection
            {
                GroupId = "things",
                ResourceId = $"pump-{index}",
                Kind = WoTDocumentKindEnum.ThingDescription
            }).ToArrayOf();

            CallToolResult result = await fixture.Wot.RefreshAsync(
                fixture.Id("wot-registry").ToString(), input, 17, sessionName: "selected").ConfigureAwait(false);

            if (count == 501)
            {
                RemoteModelTestSession.ArgumentError(result);
                Assert.That(fixture.Operations, Is.Empty);
            }
            else
            {
                Assert.That(result.IsError, Is.False);
                Assert.That(fixture.Calls, Has.Count.EqualTo(1));
                Assert.That(fixture.Calls[0].InputArguments[0].TryGetStructure(
                    fixture.Context, out ArrayOf<WoTResourceSelectorDataType> selection), Is.True);
                Assert.That(selection.Count, Is.EqualTo(500));
                Assert.That(selection[499].ResourceId, Is.EqualTo("pump-499"));
            }
        }

        [TestCase(/*lang=json,strict*/ """{"selection":{"memory":[]}}""")]
        [TestCase(/*lang=json,strict*/ """{"selection":[null]}""")]
        [TestCase(/*lang=json,strict*/ """{"selection":[7]}""")]
        [TestCase(/*lang=json,strict*/ """{"selection":[{"kind":"invalid-kind","groupId":"things"}]}""")]
        public void RefreshSourceGeneratedArrayContractRejectsNonObjectAndInvalidEnumShapes(string json)
        {
            Assert.That(() => JsonSerializer.Deserialize(json, WotInputJsonContext.Default.WotRefreshInput),
                Throws.TypeOf<JsonException>());
        }

        [TestCase(500)]
        [TestCase(501)]
        public void RefreshSourceGeneratedArrayContractEnforcesExactBindingLimit(int count)
        {
            string json = "{\"selection\":[" +
                string.Join(",", Enumerable.Repeat(
                    /*lang=json,strict*/
                    """{"kind":"ThingDescription","groupId":"things","resourceId":"pump"}""", count)) +
                "]}";
            if (count == 501)
            {
                Assert.That(() => JsonSerializer.Deserialize(json, WotInputJsonContext.Default.WotRefreshInput),
                    Throws.TypeOf<JsonException>().With.Message.Contains("500"));
            }
            else
            {
                WotRefreshInput input = JsonSerializer.Deserialize(json, WotInputJsonContext.Default.WotRefreshInput)!;
                Assert.That(input.Selection.Count, Is.EqualTo(500));
                Assert.That(input.Selection[499].GroupId, Is.EqualTo("things"));
                Assert.That(input.Selection[499].Kind, Is.EqualTo(WoTDocumentKindEnum.ThingDescription));
                Assert.That(input.AllResources, Is.False);
            }
        }

        [TestCase(0)]
        [TestCase(501)]
        public async Task RefreshRejectsOutputLimitBeforeMutationAsync(int maxResults)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            CallToolResult result = await fixture.Wot.RefreshAsync(fixture.Id("wot-registry").ToString(),
                Selection(), 17, maxResults: maxResults, sessionName: "selected").ConfigureAwait(false);

            RemoteModelTestSession.ArgumentError(result);
            Assert.That(fixture.Operations, Is.Empty);
        }

        [Test]
        public async Task RefreshZeroGenerationRequiresExplicitUnconditionalOptInAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            ReplyRefresh(fixture, StatusCodes.Good, failures: false);
            CallToolResult denied = await fixture.Wot.RefreshAsync(
                fixture.Id("wot-registry").ToString(), Selection(), 0, sessionName: "selected").ConfigureAwait(false);
            RemoteModelTestSession.ArgumentError(denied);
            Assert.That(fixture.Operations, Is.Empty);

            CallToolResult accepted = await fixture.Wot.RefreshAsync(
                fixture.Id("wot-registry").ToString(), Selection(), 0, true, sessionName: "selected")
                .ConfigureAwait(false);

            Assert.That(accepted.IsError, Is.False);
            Assert.That(fixture.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Calls[0].InputArguments[2], Is.EqualTo(Variant.From(0u)));
        }

        [TestCase("security")]
        [TestCase("wrong-root")]
        [TestCase("method")]
        [TestCase("malformed")]
        public async Task RefreshPreservesRefusalsWithoutRetryOrPartialSuccessFabricationAsync(string refusal)
        {
            await using RemoteModelTestSession fixture = await RemoteModelTestSession.CreateAsync(
                mode: refusal == "security" ? MessageSecurityMode.Sign : MessageSecurityMode.SignAndEncrypt)
                .ConfigureAwait(false);
            fixture.WotRoots();
            StatusCode expected;
            if (refusal == "security")
            {
                expected = StatusCodes.BadSecurityModeInsufficient;
            }
            else if (refusal == "wrong-root")
            {
                fixture.Type(fixture.Id("wot-registry"), ObjectTypeIds.FolderType);
                expected = StatusCodes.BadTypeMismatch;
            }
            else
            {
                fixture.Reply(refusal == "method" ? StatusCodes.BadInvalidState : StatusCodes.Good, Variant.From(42));
                expected = refusal == "method" ? StatusCodes.BadInvalidState : StatusCodes.BadUnexpectedError;
            }

            CallToolResult result = await fixture.Wot.RefreshAsync(
                fixture.Id("wot-registry").ToString(), Selection(), 17, sessionName: "selected").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, expected);
            Assert.That(fixture.Calls, Has.Count.EqualTo(refusal is "method" or "malformed" ? 1 : 0));
            Assert.That(RemoteModelTestSession.Payload(result).TryGetProperty("newGeneration", out _), Is.False);
        }

        [Test]
        public async Task
            McpBindingCompositionInspectsCreatesListsVersionsReadsStateAndRefusesUnconditionalMutationAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.RegistryHierarchy();
            fixture.Property(fixture.Id("document"), fixture.Name(WotNamespaces.WotCon, "ActiveVersionId"),
                Variant.From("v7"));
            fixture.Property(fixture.Id("document"), fixture.Name(WotNamespaces.WotCon, "Enabled"),
                Variant.From(false));
            fixture.OnCall = request =>
            {
                if (request.MethodId == fixture.Model(RegistryMethods.RegistryType_CreateGroup))
                {
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = [Variant.From(fixture.Id("group"))]
                    };
                }
                Assert.That(request.MethodId, Is.EqualTo(fixture.Model(RegistryMethods.GroupType_CreateResource)));
                fixture.Browse(fixture.Id("versions"), ReferenceTypeIds.Organizes,
                    [fixture.Reference(fixture.Id("created-v7"), RegistryTypes.ResourceType, "v7")]);
                return new CallMethodResult
                {
                    StatusCode = StatusCodes.Good,
                    OutputArguments = [Variant.From(fixture.Id("created-v7")), Variant.From("v7"), Variant.From(0u)]
                };
            };

            CallToolResult inspected = await fixture.InvokeAsync("wot_inspect_server", []).ConfigureAwait(false);
            CallToolResult group = await fixture.InvokeAsync("xregistry_create_group", new JsonObject
            {
                ["registryNodeId"] = fixture.Id("registry").ToString(),
                ["groupId"] = "things"
            }).ConfigureAwait(false);
            string groupId = RemoteModelTestSession.Payload(group).GetProperty("groupNodeId").GetString()!;
            CallToolResult version = await fixture.InvokeAsync("xregistry_create_resource", new JsonObject
            {
                ["groupNodeId"] = groupId,
                ["resourceId"] = "pump",
                ["versionId"] = "v7",
                ["ifNotExists"] = false
            }).ConfigureAwait(false);
            CallToolResult listed = await fixture.InvokeAsync("xregistry_list", new JsonObject
            {
                ["nodeId"] = fixture.Id("resource").ToString(),
                ["collection"] = "Versions",
                ["maxResults"] = 1
            }).ConfigureAwait(false);
            CallToolResult state = await fixture.InvokeAsync("wot_read_resource_state", new JsonObject
            {
                ["nodeId"] = fixture.Id("document").ToString()
            }).ConfigureAwait(false);
            CallToolResult refused = await fixture.InvokeAsync("wot_set_enabled", new JsonObject
            {
                ["nodeId"] = fixture.Id("document").ToString(),
                ["enabled"] = true,
                ["expectedEpoch"] = 0
            }).ConfigureAwait(false);

            Assert.That(RemoteModelTestSession.Payload(inspected).GetProperty("registry")
                .GetProperty("supported").GetBoolean(), Is.True);
            Assert.That(groupId, Is.EqualTo(fixture.Id("group").ToString()));
            Assert.That(RemoteModelTestSession.Payload(version).GetProperty("versionNodeId").GetString(),
                Is.EqualTo(fixture.Id("created-v7").ToString()));
            Assert.That(RemoteModelTestSession.Payload(version).GetProperty("versionId").GetString(), Is.EqualTo("v7"));
            Assert.That(RemoteModelTestSession.Payload(listed).GetProperty("items")[0]
                .GetProperty("nodeId").GetString(),
                Is.EqualTo(fixture.Id("created-v7").ToString()));
            JsonElement properties = RemoteModelTestSession.Payload(state).GetProperty("properties");
            Assert.That(properties.GetProperty("ActiveVersionId").GetProperty("dataValue")
                .GetProperty("Value").GetString(), Is.EqualTo("v7"));
            Assert.That(properties.GetProperty("Enabled").GetProperty("dataValue").GetProperty("Value").GetBoolean(),
                Is.False);
            RemoteModelTestSession.ArgumentError(refused);
            Assert.That(fixture.Calls, Has.Count.EqualTo(2),
                "The refusal and reads cannot cause hidden mutation or activation.");
            Assert.That(fixture.Calls[0].ObjectId, Is.EqualTo(fixture.Id("registry")));
            Assert.That(fixture.Calls[1].ObjectId, Is.EqualTo(fixture.Id("group")));
            Assert.That(fixture.Calls[1].InputArguments.ToArray(),
                Is.EqualTo([Variant.From("pump"), Variant.From("v7"), Variant.From(false)]));
        }

        private static WotRefreshInput Selection()
        {
            return new WotRefreshInput
            {
                Selection = [new WotResourceSelection
                {
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    GroupId = "things", ResourceId = "pump", VersionId = "v7"
                }],
                RequestId = "refresh-caller-17"
            };
        }

        private static void ReplyRefresh(RemoteModelTestSession fixture, StatusCode status, bool failures)
        {
            fixture.Reply(status,
                Variant.FromStructure(new WoTRefreshSummaryDataType
                {
                    RequestId = "refresh-caller-17",
                    Generation = 18,
                    Total = 2,
                    Succeeded = failures ? 1u : 2u,
                    Failed = failures ? 1u : 0u,
                    Outcome = failures ? WoTOutcomeEnum.Failed : WoTOutcomeEnum.Success
                }),
                Variant.FromStructure(
                [
                    new WoTResourceLoadResultDataType
                    {
                        GroupId = "things", ResourceId = "pump", VersionId = "v7", Outcome = WoTOutcomeEnum.Success
                    },
                    new WoTResourceLoadResultDataType
                    {
                        GroupId = "things", ResourceId = "motor", VersionId = "v2",
                        Outcome = failures ? WoTOutcomeEnum.Rejected : WoTOutcomeEnum.Unchanged
                    }
                ]),
                Variant.From(18u));
        }

        private static void AssertPartialRefresh(CallToolResult result)
        {
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.True);
            Assert.That(json.GetProperty("newGeneration").GetUInt32(), Is.EqualTo(18));
            Assert.That(json.GetProperty("summary").GetProperty("RequestId").GetString(),
                Is.EqualTo("refresh-caller-17"));
            Assert.That(json.GetProperty("summary").GetProperty("Succeeded").GetUInt32(), Is.EqualTo(1));
            Assert.That(json.GetProperty("summary").GetProperty("Failed").GetUInt32(), Is.EqualTo(1));
            Assert.That(json.GetProperty("resultCount").GetInt32(), Is.EqualTo(2));
            Assert.That(json.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("results").GetArrayLength(), Is.EqualTo(1));
            Assert.That(json.GetProperty("results")[0].GetProperty("ResourceId").GetString(), Is.EqualTo("pump"));
            Assert.That(json.GetProperty("results")[0].GetProperty("Outcome").GetString(), Is.EqualTo("Success_0"));
            Assert.That(json.TryGetProperty("nextOffset", out _), Is.False, "The mutation has no paging cursor.");
        }

        private static readonly StatusCode[] s_nonBadStatuses =
            [StatusCodes.Good, StatusCodes.GoodResultsMayBeIncomplete, StatusCodes.Uncertain];
    }
}
#endif
