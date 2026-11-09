/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root. */

#if NET10_0
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.WotCon;
using WotMethods = Opc.Ua.WotCon.MethodIds;
using WotNamespaces = Opc.Ua.WotCon.Namespaces;
using WotTypes = Opc.Ua.WotCon.ObjectTypeIds;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Pins server-facing WoT behavior independently from local document conversion.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class WotRemoteToolsTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task InspectServerDiscoversLegacyAndRegistryRootsIndependentlyAsync(bool legacy, bool registry)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots(legacy, registry);
            if (legacy)
            {
                fixture.Property(fixture.Id("management"), fixture.Name(WotNamespaces.WotCon, "SupportedWoTBindings"),
                    Variant.From(["urn:binding:opcua", "urn:binding:mqtt"]));
            }
            if (registry)
            {
                fixture.Property(fixture.Id("wot-registry"), fixture.Name(WotNamespaces.WotCon, "AutoRefresh"),
                    Variant.From(false));
                fixture.Property(fixture.Id("wot-registry"), fixture.Name(WotNamespaces.WotCon, "RefreshGeneration"),
                    Variant.From(17u));
            }
            int namespaces = fixture.Context.NamespaceUris.Count;

            CallToolResult result = await fixture.Wot.InspectServerAsync(sessionName: "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("namespaceUri").GetString(), Is.EqualTo(WotNamespaces.WotCon));
            Assert.That(json.GetProperty("legacy").GetProperty("supported").GetBoolean(), Is.EqualTo(legacy));
            Assert.That(json.GetProperty("registry").GetProperty("supported").GetBoolean(), Is.EqualTo(registry));
            if (legacy)
            {
                Assert.That(json.GetProperty("legacy").GetProperty("nodeId").GetString(),
                    Is.EqualTo(fixture.Id("management").ToString()));
                JsonElement bindings = json.GetProperty("legacy").GetProperty("properties")
                    .GetProperty("SupportedWoTBindings").GetProperty("dataValue");
                Assert.That(bindings.GetProperty("UaType").GetByte(), Is.EqualTo((byte)BuiltInType.String));
                Assert.That(bindings.GetProperty("Value").EnumerateArray().Select(value => value.GetString()),
                    Is.EqualTo(s_supportedBindings));
            }
            if (registry)
            {
                JsonElement properties = json.GetProperty("registry").GetProperty("properties");
                Assert.That(properties.GetProperty("AutoRefresh").GetProperty("dataValue")
                    .GetProperty("Value").GetBoolean(), Is.False);
                Assert.That(properties.GetProperty("RefreshGeneration").GetProperty("dataValue")
                    .GetProperty("Value").GetUInt32(), Is.EqualTo(17));
                Assert.That(properties.GetProperty("VocabularyVersion").GetProperty("supported").GetBoolean(),
                    Is.False);
            }
            Assert.That(fixture.Context.NamespaceUris.Count, Is.EqualTo(namespaces));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task InspectServerUsesExplicitRenamedTypedRootsWithoutConventionalLookupAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.Type(fixture.Id("renamed-manager"), WotTypes.WoTAssetConnectionManagementType);
            NodeId vendorType = fixture.Id("vendor-registry");
            fixture.Type(fixture.Id("renamed-registry"), vendorType);
            fixture.Subtype(vendorType, WotTypes.WoTRegistryType);

            CallToolResult result = await fixture.Wot.InspectServerAsync(
                fixture.Id("renamed-manager").ToString(), fixture.Id("renamed-registry").ToString(), "selected")
                .ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(json.GetProperty("legacy").GetProperty("nodeId").GetString(),
                Is.EqualTo(fixture.Id("renamed-manager").ToString()));
            Assert.That(json.GetProperty("registry").GetProperty("nodeId").GetString(),
                Is.EqualTo(fixture.Id("renamed-registry").ToString()));
            Assert.That(fixture.Paths.Select(path => path.StartingNode), Has.None.EqualTo(ObjectIds.ObjectsFolder));
            Assert.That(fixture.Paths.Select(path => path.StartingNode), Has.None.EqualTo(ObjectIds.Server));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task MissingWotNamespaceNeverAppendsNamespaceOrFabricatesSupportAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync(includeWot: false).ConfigureAwait(false);
            int count = fixture.Context.NamespaceUris.Count;
            CallToolResult inspected = await fixture.Wot.InspectServerAsync(sessionName: "selected")
                .ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(inspected);
            CallToolResult listed = await fixture.Wot.ListAssetsAsync(sessionName: "selected").ConfigureAwait(false);

            Assert.That(inspected.IsError, Is.False);
            Assert.That(json.GetProperty("legacy").GetProperty("supported").GetBoolean(), Is.False);
            Assert.That(json.GetProperty("registry").GetProperty("supported").GetBoolean(), Is.False);
            RemoteModelTestSession.Error(listed, StatusCodes.BadNotSupported);
            Assert.That(fixture.Context.NamespaceUris.Count, Is.EqualTo(count));
            Assert.That(fixture.Operations, Is.Empty);
        }

        [TestCase("lookup-denied")]
        [TestCase("wrong-type")]
        public async Task InspectServerDoesNotRelabelFailedRootResolutionAsUnsupportedAsync(string failure)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            StatusCode expected;
            if (failure == "lookup-denied")
            {
                fixture.Translations[(ObjectIds.ObjectsFolder,
                    fixture.Name(WotNamespaces.WotCon, "WoTAssetConnectionManagement"))] =
                    new BrowsePathResult { StatusCode = StatusCodes.BadUserAccessDenied };
                expected = StatusCodes.BadUserAccessDenied;
            }
            else
            {
                fixture.Type(fixture.Id("management"), ObjectTypeIds.FolderType);
                expected = StatusCodes.BadTypeMismatch;
            }
            CallToolResult result = await fixture.Wot.InspectServerAsync(sessionName: "selected").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, expected);
            Assert.That(RemoteModelTestSession.Payload(result).TryGetProperty("legacy", out _), Is.False);
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task ListAssetsFollowsServerPagesAndReturnsExactLiveSliceAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Browse(fixture.Id("management"), ReferenceTypeIds.Organizes,
                [fixture.Reference(fixture.Id("one"), ObjectTypeIds.BaseObjectType, "First")],
                continuation: ByteString.From([1]));
            fixture.NextPages.Enqueue(new BrowseResult
            {
                StatusCode = StatusCodes.Good,
                References = [fixture.Reference(fixture.Id("two"), ObjectTypeIds.BaseObjectType, "Second"),
                    fixture.Reference(fixture.Id("three"), ObjectTypeIds.BaseObjectType, "Third")],
                ContinuationPoint = ByteString.From([2])
            });

            CallToolResult result = await fixture.Wot.ListAssetsAsync(offset: 1, maxResults: 1, sessionName: "selected")
                .ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            Assert.That(json.GetProperty("items")[0].GetProperty("nodeId").GetString(),
                Is.EqualTo(fixture.Id("two").ToString()));
            Assert.That(json.GetProperty("items")[0].GetProperty("name").GetString(), Is.EqualTo("Second"));
            Assert.That(json.GetProperty("nextOffset").GetInt32(), Is.EqualTo(2));
            Assert.That(json.GetProperty("consistency").GetString(), Is.EqualTo("live"));
            Assert.That(fixture.Continuations.Select(value => (value.Release, value.Point)),
                Is.EqualTo([(false, ByteString.From([1])), (true, ByteString.From([2]))]));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task ListAssetsRefusesAbsentLegacyRootEvenWhenRegistryExistsAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots(legacy: false);
            CallToolResult result = await fixture.Wot.ListAssetsAsync(sessionName: "selected").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadNotSupported);
            Assert.That(fixture.Browses, Is.Empty);
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task InspectAssetPropertiesIncludesFilePermissionsTypedMeasurementAndExactNodeAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Asset();
            NodeId property = fixture.Id("temperature");
            fixture.Browse(fixture.Id("asset"), fixture.Model(WotCon.ReferenceTypeIds.HasWoTComponent),
                [fixture.Reference(property, ObjectTypeIds.BaseObjectType, "temperature")]);
            fixture.Values[(property, Attributes.Value)] = new DataValue(Variant.From(21.5), StatusCodes.Uncertain);

            CallToolResult result = await fixture.Wot.InspectAssetAsync(
                fixture.Id("asset").ToString(), sessionName: "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);
            JsonElement item = json.GetProperty("affordances").GetProperty("items")[0];
            JsonElement file = json.GetProperty("file").GetProperty("properties");

            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("displayName").GetString(), Is.EqualTo("Pump asset"));
            Assert.That(item.GetProperty("nodeId").GetString(), Is.EqualTo(property.ToString()));
            Assert.That(item.GetProperty("browseName").GetString(), Is.EqualTo("temperature"));
            Assert.That(item.GetProperty("dataValue").GetProperty("UaType").GetByte(),
                Is.EqualTo((byte)BuiltInType.Double));
            Assert.That(item.GetProperty("dataValue").GetProperty("Value").GetDouble(), Is.EqualTo(21.5));
            Assert.That(item.GetProperty("dataValue").GetProperty("Status").GetProperty("Code").GetUInt32(),
                Is.EqualTo(StatusCodes.Uncertain.Code));
            Assert.That(file.GetProperty("Size").GetProperty("dataValue").GetProperty("Value").GetString(),
                Is.EqualTo("73"));
            Assert.That(file.GetProperty("Writable").GetProperty("dataValue").GetProperty("Value").GetBoolean(),
                Is.True);
            Assert.That(file.GetProperty("UserWritable").GetProperty("dataValue").GetProperty("Value").GetBoolean(),
                Is.False);
            Assert.That(fixture.Browses[^1].IncludeSubtypes, Is.False);
            Assert.That(fixture.Browses[^1].NodeClassMask, Is.EqualTo((uint)NodeClass.Variable));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task InspectAssetActionsReturnsInputAndOutputMetadataWithoutCallingActionAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Asset();
            NodeId method = fixture.Id("set-speed");
            fixture.Browse(fixture.Id("asset"), ReferenceTypeIds.HasComponent,
                [fixture.Reference(method, ObjectTypeIds.BaseObjectType, "setSpeed")]);
            fixture.Property(method, new QualifiedName("InputArguments"), Variant.FromStructure(
                [new Argument { Name = "speed", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar }]));
            fixture.Property(method, new QualifiedName("OutputArguments"), Variant.FromStructure(
                [new Argument { Name = "accepted", DataType = DataTypeIds.Boolean, ValueRank = ValueRanks.Scalar }]));

            CallToolResult result = await fixture.Wot.InspectAssetAsync(
                fixture.Id("asset").ToString(), WotAssetFacet.Actions, sessionName: "selected").ConfigureAwait(false);
            JsonElement item = RemoteModelTestSession.Payload(result)
                .GetProperty("affordances").GetProperty("items")[0];
            JsonElement arguments = item.GetProperty("arguments").GetProperty("properties");
            JsonElement input = arguments.GetProperty("InputArguments").GetProperty("dataValue")
                .GetProperty("Value")[0];
            JsonElement output = arguments.GetProperty("OutputArguments").GetProperty("dataValue")
                .GetProperty("Value")[0];

            Assert.That(item.GetProperty("nodeId").GetString(), Is.EqualTo(method.ToString()));
            Assert.That(input.GetProperty("Name").GetString(), Is.EqualTo("speed"));
            Assert.That(input.GetProperty("DataType").GetString(), Is.EqualTo("i=11"));
            Assert.That(input.GetProperty("ValueRank").GetInt32(), Is.EqualTo(ValueRanks.Scalar));
            Assert.That(output.GetProperty("Name").GetString(), Is.EqualTo("accepted"));
            Assert.That(output.GetProperty("DataType").GetString(), Is.EqualTo("i=1"));
            Assert.That(fixture.Browses[^1].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
            Assert.That(fixture.Browses[^1].NodeClassMask, Is.EqualTo((uint)NodeClass.Method));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task InspectAssetEventsBrowsesDeclaredEventTypesNotPropertyOrActionNodesAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Asset();
            fixture.Browse(ObjectTypeIds.BaseObjectType, ReferenceTypeIds.GeneratesEvent,
                [fixture.Reference(fixture.Id("pressure-alarm-type"), ObjectTypeIds.BaseEventType, "PressureAlarm")]);

            CallToolResult result = await fixture.Wot.InspectAssetAsync(
                fixture.Id("asset").ToString(), WotAssetFacet.Events, sessionName: "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(json.GetProperty("facet").GetString(), Is.EqualTo("Events"));
            Assert.That(json.GetProperty("affordances").GetProperty("items")[0].GetProperty("nodeId").GetString(),
                Is.EqualTo(fixture.Id("pressure-alarm-type").ToString()));
            Assert.That(fixture.Browses[^1].NodeId, Is.EqualTo(ObjectTypeIds.BaseObjectType));
            Assert.That(fixture.Browses[^1].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.GeneratesEvent));
            Assert.That(fixture.Browses[^1].NodeClassMask, Is.EqualTo((uint)NodeClass.ObjectType));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase(WotAssetFacet.Properties)]
        [TestCase(WotAssetFacet.Actions)]
        [TestCase(WotAssetFacet.Events)]
        public async Task InspectAssetBoundsEveryFacetToRequestedLiveSliceAsync(WotAssetFacet facet)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Asset();
            ReferenceDescription[] entries = [.. Enumerable.Range(0, 3).Select(index =>
            {
                NodeId node = fixture.Id($"affordance-{index}");
                fixture.Values[(node, Attributes.Value)] = new DataValue(Variant.From(index + 10));
                ReferenceDescription reference = fixture.Reference(
                    node, ObjectTypeIds.BaseObjectType, $"affordance-{index}");
                reference.NodeClass = facet switch
                {
                    WotAssetFacet.Properties => NodeClass.Variable,
                    WotAssetFacet.Actions => NodeClass.Method,
                    _ => NodeClass.ObjectType
                };
                return reference;
            })];
            NodeId parent = facet == WotAssetFacet.Events ? ObjectTypeIds.BaseObjectType : fixture.Id("asset");
            NodeId referenceType = facet switch
            {
                WotAssetFacet.Properties => fixture.Model(WotCon.ReferenceTypeIds.HasWoTComponent),
                WotAssetFacet.Actions => ReferenceTypeIds.HasComponent,
                _ => ReferenceTypeIds.GeneratesEvent
            };
            fixture.Browse(parent, referenceType, entries.ToArrayOf());

            CallToolResult result = await fixture.Wot.InspectAssetAsync(
                fixture.Id("asset").ToString(), facet, offset: 1, maxResults: 1, sessionName: "selected")
                .ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);
            JsonElement page = json.GetProperty("affordances");

            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("facet").GetString(), Is.EqualTo(facet.ToString()));
            Assert.That(page.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            Assert.That(page.GetProperty("items")[0].GetProperty("nodeId").GetString(),
                Is.EqualTo(fixture.Id("affordance-1").ToString()));
            Assert.That(page.GetProperty("nextOffset").GetInt32(), Is.EqualTo(2));
            Assert.That(page.GetProperty("hasMore").GetBoolean(), Is.True);
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task InspectAssetEventReferencesExposeRemappedIdsAndRemoteInformationWithoutFollowingAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Asset();
            var local = new ReferenceDescription
            {
                NodeId = new ExpandedNodeId("local-event-type", WotNamespaces.WotCon),
                TypeDefinition = ObjectTypeIds.BaseEventType,
                NodeClass = NodeClass.ObjectType
            };
            var remote = new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(fixture.Id("foreign-event-type"), null, 8),
                TypeDefinition = ObjectTypeIds.BaseEventType,
                NodeClass = NodeClass.ObjectType
            };
            fixture.Browse(ObjectTypeIds.BaseObjectType, ReferenceTypeIds.GeneratesEvent, [local, remote]);

            CallToolResult result = await fixture.Wot.InspectAssetAsync(
                fixture.Id("asset").ToString(), WotAssetFacet.Events, sessionName: "selected").ConfigureAwait(false);
            JsonElement items = RemoteModelTestSession.Payload(result).GetProperty("affordances").GetProperty("items");

            Assert.That(result.IsError, Is.False);
            Assert.That(items.GetArrayLength(), Is.EqualTo(2));
            Assert.That(items[0].GetProperty("nodeId").GetString(),
                Is.EqualTo(fixture.Model(new ExpandedNodeId("local-event-type", WotNamespaces.WotCon)).ToString()));
            Assert.That(items[0].GetProperty("expandedNodeId").GetString(), Is.EqualTo(local.NodeId.ToString()));
            Assert.That(items[0].GetProperty("local").GetBoolean(), Is.True);
            Assert.That(items[1].GetProperty("nodeId").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(items[1].GetProperty("remote").GetBoolean(), Is.True);
            Assert.That(items[1].GetProperty("local").GetBoolean(), Is.False);
            Assert.That(items[1].GetProperty("expandedNodeId").GetString(), Is.EqualTo(remote.NodeId.ToString()));
            Assert.That(fixture.Calls, Is.Empty);
            Assert.That(fixture.Browses.Select(browse => browse.NodeId),
                Has.None.EqualTo(fixture.Id("foreign-event-type")));
        }

        [TestCase("missing-file")]
        [TestCase("bad-value")]
        [TestCase("unknown-facet")]
        public async Task InspectAssetRefusesIncompleteOrFailedFacetRatherThanReturningEmptySuccessAsync(string failure)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            if (failure != "missing-file")
            {
                fixture.Asset();
                fixture.Browse(fixture.Id("asset"), fixture.Model(WotCon.ReferenceTypeIds.HasWoTComponent),
                    [fixture.Reference(fixture.Id("value"), ObjectTypeIds.BaseObjectType)]);
                fixture.Values[(fixture.Id("value"), Attributes.Value)] =
                    DataValue.FromStatusCode(StatusCodes.BadUserAccessDenied);
            }
            CallToolResult result = await fixture.Wot.InspectAssetAsync(fixture.Id("asset").ToString(),
                failure == "unknown-facet" ? (WotAssetFacet)99 : WotAssetFacet.Properties, sessionName: "selected")
                .ConfigureAwait(false);

            if (failure == "unknown-facet")
            {
                RemoteModelTestSession.ArgumentError(result);
            }
            else if (failure == "bad-value")
            {
                Assert.That(result.IsError, Is.True);
                JsonElement item = RemoteModelTestSession.Payload(result)
                    .GetProperty("affordances").GetProperty("items")[0];
                Assert.That(item.GetProperty("error").GetBoolean(), Is.True);
                Assert.That(item.GetProperty("nodeId").GetString(), Is.EqualTo(fixture.Id("value").ToString()));
                Assert.That(item.GetProperty("dataValue").GetProperty("Status").GetProperty("Code").GetUInt32(),
                    Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
            }
            else
            {
                RemoteModelTestSession.Error(result, StatusCodes.BadNoMatch);
            }
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task DiscoverEndpointsPagesOpaqueAddressesWithoutTestingOrCreatingAssetsAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Reply(StatusCodes.GoodResultsMayBeIncomplete,
                Variant.From(["https://one.invalid/td", "mqtt://two.invalid/topic", "opc.tcp://three.invalid:4840"]));

            CallToolResult result = await fixture.Wot.DiscoverEndpointsAsync(
                offset: 1, maxResults: 1, sessionName: "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("items")[0].GetString(), Is.EqualTo("mqtt://two.invalid/topic"));
            Assert.That(json.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            Assert.That(json.GetProperty("nextOffset").GetInt32(), Is.EqualTo(2));
            Assert.That(json.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.GoodResultsMayBeIncomplete.Code));
            fixture.AssertCall(fixture.Id("management"), WotMethods.WoTAssetConnectionManagementType_DiscoverAssets);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ExplicitConnectionTestPreservesServerOutcomeAndCallsOnlyRequestedEndpointAsync(bool connected)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Reply(StatusCodes.Good, Variant.From(connected), Variant.From("server policy explanation"));

            CallToolResult result = await fixture.Wot.TestConnectionAsync(
                "https://device.invalid/td", sessionName: "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.EqualTo(!connected));
            Assert.That(json.GetProperty("success").GetBoolean(), Is.EqualTo(connected));
            Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("server policy explanation"));
            fixture.AssertCall(fixture.Id("management"), WotMethods.WoTAssetConnectionManagementType_ConnectionTest,
                Variant.From("https://device.invalid/td"));
        }

        [TestCase("discover", false)]
        [TestCase("discover", true)]
        [TestCase("test", false)]
        [TestCase("test", true)]
        public async Task EndpointOperationsPreserveFailureAndRejectMalformedSuccessAsync(
            string operation, bool malformed)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Reply(malformed ? StatusCodes.Good : StatusCodes.BadNotSupported, Variant.From(42));
            CallToolResult result = operation == "discover"
                ? await fixture.Wot.DiscoverEndpointsAsync(sessionName: "selected").ConfigureAwait(false)
                : await fixture.Wot.TestConnectionAsync("https://device.invalid/td", sessionName: "selected")
                    .ConfigureAwait(false);

            RemoteModelTestSession.Error(
                result, malformed ? StatusCodes.BadDecodingError : StatusCodes.BadNotSupported);
            Assert.That(fixture.Calls, Has.Count.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CreateAssetReturnsAssignedIdAndPreservesCompletedStatusAfterLateCancellationAsync(
            bool endpoint)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            using var cancellation = new CancellationTokenSource();
            fixture.OnCall = _ =>
            {
                cancellation.Cancel();
                return new CallMethodResult
                {
                    StatusCode = StatusCodes.GoodResultsMayBeIncomplete,
                    OutputArguments = [Variant.From(fixture.Id("server-assigned-asset"))]
                };
            };

            CallToolResult result = endpoint
                ? await fixture.Wot.CreateAssetForEndpointAsync("Pump", "https://device.invalid/td",
                    sessionName: "selected", ct: cancellation.Token).ConfigureAwait(false)
                : await fixture.Wot.CreateAssetAsync("Pump", sessionName: "selected", ct: cancellation.Token)
                    .ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("assetNodeId").GetString(),
                Is.EqualTo(fixture.Id("server-assigned-asset").ToString()));
            Assert.That(json.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.GoodResultsMayBeIncomplete.Code));
            Assert.That(cancellation.IsCancellationRequested, Is.True);
            fixture.AssertCall(fixture.Id("management"), endpoint
                ? WotMethods.WoTAssetConnectionManagementType_CreateAssetForEndpoint
                : WotMethods.WoTAssetConnectionManagementType_CreateAsset,
                endpoint ? [Variant.From("Pump"), Variant.From("https://device.invalid/td")] : [Variant.From("Pump")]);
            Assert.That(fixture.Operations[^1], Is.EqualTo("call"));
            Assert.That(fixture.Reads, Is.Empty,
                "A committed create must not re-open the asset and become cancellation.");
        }

        [TestCase("create")]
        [TestCase("create-endpoint")]
        [TestCase("delete")]
        public async Task LegacyMutationPreservesServerRefusalWithoutChainedOperationsAsync(string operation)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Reply(StatusCodes.BadUserAccessDenied);

            CallToolResult result = await LegacyMutationAsync(fixture, operation).ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Reads, Is.Empty);
        }

        [Test]
        public async Task DeleteAssetPassesExactAssetNodeToActualManagementRootAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Reply(StatusCodes.Good);

            CallToolResult result = await fixture.Wot.DeleteAssetAsync(
                fixture.Id("asset").ToString(), sessionName: "selected").ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            fixture.AssertCall(fixture.Id("management"), WotMethods.WoTAssetConnectionManagementType_DeleteAsset,
                Variant.From(fixture.Id("asset")));
            Assert.That(fixture.Reads, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CreateAssetRejectsMissingAssignedIdentityWithoutFollowUpAsync(bool endpoint)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Reply(StatusCodes.Good, Variant.From(NodeId.Null));

            CallToolResult result = await LegacyMutationAsync(fixture, endpoint ? "create-endpoint" : "create")
                .ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadDecodingError);
            Assert.That(fixture.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Reads, Is.Empty);
        }

        [Test]
        public async Task WotToolsResolveReplacementOfNamedSessionWithoutRetainingOldProxyAsync()
        {
            await using RemoteModelTestSession first = await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            await using RemoteModelTestSession replacement =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            first.WotRoots();
            replacement.WotRoots();
            first.Reply(StatusCodes.Good, Variant.From(first.Id("asset-on-first-server")));
            replacement.Reply(StatusCodes.Good, Variant.From(replacement.Id("asset-on-replacement-server")));
            Ua.Mcp.Tools.WotTools tools = first.Wot;
            await first.Manager.RegisterExistingSessionAsync("other", replacement.Session.Object, "Anonymous")
                .ConfigureAwait(false);

            CallToolResult initial = await tools.CreateAssetAsync("First", sessionName: "selected")
                .ConfigureAwait(false);
            await first.Manager.RegisterExistingSessionAsync("selected", replacement.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            CallToolResult next = await tools.CreateAssetAsync("Next", sessionName: "selected").ConfigureAwait(false);

            Assert.That(RemoteModelTestSession.Payload(initial).GetProperty("assetNodeId").GetString(),
                Is.EqualTo(first.Id("asset-on-first-server").ToString()));
            Assert.That(RemoteModelTestSession.Payload(next).GetProperty("assetNodeId").GetString(),
                Is.EqualTo(replacement.Id("asset-on-replacement-server").ToString()));
            first.AssertCall(first.Id("management"), WotMethods.WoTAssetConnectionManagementType_CreateAsset,
                Variant.From("First"));
            replacement.AssertCall(
                replacement.Id("management"), WotMethods.WoTAssetConnectionManagementType_CreateAsset,
                Variant.From("Next"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public async Task EndpointCreateRejectsMissingEndpointInsideEnvelopeWithoutSelectingPlainCreateAsync(
            string? endpoint)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();

            CallToolResult result = await fixture.Wot.CreateAssetForEndpointAsync(
                "Pump", endpoint!, sessionName: "selected").ConfigureAwait(false);

            RemoteModelTestSession.ArgumentError(result);
            Assert.That(fixture.Operations, Is.Empty);
        }

        [TestCase("file")]
        [TestCase("arguments")]
        public async Task InspectAssetAggregatesBadFileAndActionMetadataWithoutLosingTypedValuesAsync(
            string failingPart)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Asset();
            fixture.Browse(fixture.Id("asset"), ReferenceTypeIds.HasComponent,
                [fixture.Reference(fixture.Id("action"), ObjectTypeIds.BaseObjectType, "run")]);
            if (failingPart == "file")
            {
                fixture.Property(fixture.Id("td-file"), new QualifiedName("Writable"),
                    new DataValue(Variant.From(false), StatusCodes.BadUserAccessDenied));
            }
            else
            {
                fixture.Property(fixture.Id("action"), new QualifiedName("InputArguments"),
                    new DataValue(Variant.FromStructure([new Argument
                    {
                        Name = "speed", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar
                    }]), StatusCodes.BadOutOfRange));
            }

            CallToolResult result = await fixture.Wot.InspectAssetAsync(
                fixture.Id("asset").ToString(), WotAssetFacet.Actions, sessionName: "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.True);
            Assert.That(json.GetProperty("assetNodeId").GetString(), Is.EqualTo(fixture.Id("asset").ToString()));
            JsonElement failed = failingPart == "file" ? json.GetProperty("file") :
                json.GetProperty("affordances").GetProperty("items")[0].GetProperty("arguments");
            Assert.That(failed.GetProperty("error").GetBoolean(), Is.True);
            if (failingPart == "file")
            {
                Assert.That(failed.GetProperty("properties").GetProperty("Writable").GetProperty("dataValue")
                    .GetProperty("Value").GetBoolean(), Is.False);
            }
            else
            {
                Assert.That(failed.GetProperty("properties").GetProperty("InputArguments").GetProperty("dataValue")
                    .GetProperty("Value")[0].GetProperty("Name").GetString(), Is.EqualTo("speed"));
            }
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase("create")]
        [TestCase("create-endpoint")]
        [TestCase("delete")]
        [TestCase("validate")]
        [TestCase("enable")]
        [TestCase("default")]
        public async Task WotMutationsRequireEncryptedSelectedSessionAsync(string operation)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync(mode: MessageSecurityMode.None).ConfigureAwait(false);
            fixture.WotRoots();
            CallToolResult result = operation is "validate" or "enable" or "default"
                ? await DocumentMutationAsync(fixture, operation, 3).ConfigureAwait(false)
                : await LegacyMutationAsync(fixture, operation).ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadSecurityModeInsufficient);
            Assert.That(fixture.Calls, Is.Empty);
            Assert.That(fixture.Reads, Is.Empty);
        }

        [Test]
        public async Task ResourceStatePreservesVersionProjectionAndTypedOptionalStateAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Property(fixture.Id("document"), fixture.Name(WotNamespaces.WotCon, "Enabled"),
                Variant.From(false));
            fixture.Property(fixture.Id("document"), fixture.Name(WotNamespaces.WotCon, "DesiredVersionId"),
                Variant.From("v8"));
            fixture.Property(fixture.Id("document"), fixture.Name(WotNamespaces.WotCon, "ActiveVersionId"),
                Variant.From("v7"));
            fixture.Property(fixture.Id("document"), fixture.Name(WotNamespaces.WotCon, "MaterializedNodeCount"),
                Variant.From(0u));
            fixture.Property(fixture.Id("document"), fixture.Name(WotNamespaces.WotCon, "SelectedBindings"),
                Variant.From(["urn:binding:opcua"]));

            CallToolResult result = await fixture.Wot.ReadResourceStateAsync(
                fixture.Id("document").ToString(), "selected").ConfigureAwait(false);
            JsonElement properties = RemoteModelTestSession.Payload(result).GetProperty("properties");

            Assert.That(result.IsError, Is.False);
            Assert.That(properties.GetProperty("Enabled").GetProperty("dataValue").GetProperty("Value").GetBoolean(),
                Is.False);
            Assert.That(properties.GetProperty("DesiredVersionId").GetProperty("dataValue")
                .GetProperty("Value").GetString(), Is.EqualTo("v8"));
            Assert.That(properties.GetProperty("ActiveVersionId").GetProperty("dataValue")
                .GetProperty("Value").GetString(), Is.EqualTo("v7"));
            Assert.That(properties.GetProperty("MaterializedNodeCount").GetProperty("dataValue")
                .GetProperty("Value").GetUInt32(), Is.Zero);
            Assert.That(properties.GetProperty("SelectedBindings").GetProperty("dataValue")
                .GetProperty("Value")[0].GetString(),
                Is.EqualTo("urn:binding:opcua"));
            Assert.That(properties.GetProperty("RootNodeId").GetProperty("supported").GetBoolean(), Is.False);
            Assert.That(fixture.Calls, Is.Empty);
            Assert.That(fixture.Reads, Has.Count.EqualTo(5));
        }

        [Test]
        public async Task ResourceStateFailedQualityRemainsSupportedAndSignalsErrorAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Property(fixture.Id("document"), fixture.Name(WotNamespaces.WotCon, "Enabled"),
                new DataValue(Variant.From(false), StatusCodes.BadUserAccessDenied));

            CallToolResult result = await fixture.Wot.ReadResourceStateAsync(
                fixture.Id("document").ToString(), "selected").ConfigureAwait(false);
            JsonElement enabled = RemoteModelTestSession.Payload(result)
                .GetProperty("properties").GetProperty("Enabled");

            Assert.That(result.IsError, Is.True);
            Assert.That(enabled.GetProperty("supported").GetBoolean(), Is.True);
            Assert.That(enabled.GetProperty("dataValue").GetProperty("Status").GetProperty("Code").GetUInt32(),
                Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task ReadValidationOutcomeRegistersModelsBeforeDecodingBinaryServiceValueAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync(emptyFactory: true).ConfigureAwait(false);
            fixture.WotRoots();
            Assert.That(fixture.Context.Factory.ContainsEncodeableType(WotCon.DataTypeIds.WoTValidationOutcomeDataType),
                Is.False);
            var outcome = new WoTValidationOutcomeDataType
            {
                FormatValidated = true,
                FormatOutcome = WoTOutcomeEnum.Rejected,
                CompatibilityOutcome = WoTOutcomeEnum.Warning
            };
            byte[] encoded;
            using (var encoder = new BinaryEncoder(fixture.Context))
            {
                encoder.WriteDataValue(null, new DataValue(Variant.FromStructure(outcome)));
                encoded = encoder.CloseAndReturnBuffer()!;
            }
            NodeId valueNode = fixture.Property(fixture.Id("document"),
                fixture.Name(WotNamespaces.WotCon, "ValidationOutcome"), Variant.Null);
            fixture.OnRead = request =>
            {
                Assert.That(request.NodeId, Is.EqualTo(valueNode));
                Assert.That(
                    fixture.Context.Factory.ContainsEncodeableType(WotCon.DataTypeIds.WoTValidationOutcomeDataType),
                    Is.True);
                Assert.That(
                    fixture.Context.Factory.ContainsEncodeableType(XRegistry.DataTypeIds.RegistryCapabilitiesDataType),
                    Is.True);
                using var decoder = new BinaryDecoder(encoded, fixture.Context);
                DataValue decoded = decoder.ReadDataValue(null);
                Assert.That(decoded.WrappedValue.TryGetValue(out ExtensionObject extension), Is.True);
                Assert.That(extension.Encoding, Is.EqualTo(ExtensionObjectEncoding.EncodeableObject));
                return decoded;
            };

            CallToolResult result = await fixture.Wot.ReadResourceStateAsync(
                fixture.Id("document").ToString(), "selected").ConfigureAwait(false);
            CallToolResult repeated = await fixture.Wot.ReadResourceStateAsync(
                fixture.Id("document").ToString(), "selected").ConfigureAwait(false);
            JsonElement property = RemoteModelTestSession.Payload(result)
                .GetProperty("properties").GetProperty("ValidationOutcome");
            JsonElement data = property.GetProperty("dataValue");
            JsonElement body = data.GetProperty("Value");

            Assert.That(result.IsError, Is.False,
                "Reading a stored rejection is successful when its UA quality is Good.");
            Assert.That(repeated.IsError, Is.False);
            Assert.That(property.GetProperty("supported").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("UaType").GetByte(), Is.EqualTo((byte)BuiltInType.ExtensionObject));
            Assert.That(body.GetProperty("FormatValidated").GetBoolean(), Is.True);
            Assert.That(body.GetProperty("FormatOutcome").GetString(), Is.EqualTo("Rejected_4"));
            Assert.That(body.GetProperty("CompatibilityOutcome").GetString(), Is.EqualTo("Warning_2"));
            Assert.That(body.TryGetProperty("UaBody", out _), Is.False);
            Assert.That(fixture.Reads, Has.Count.EqualTo(2));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase(WoTOutcomeEnum.Success, WoTOutcomeEnum.Warning, false)]
        [TestCase(WoTOutcomeEnum.Rejected, WoTOutcomeEnum.Success, true)]
        [TestCase(WoTOutcomeEnum.Success, WoTOutcomeEnum.Failed, true)]
        public async Task ValidateResourcePreservesDistinctOutcomesAndRawMethodStatusAsync(
            WoTOutcomeEnum format, WoTOutcomeEnum compatibility, bool error)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Reply(StatusCodes.GoodResultsMayBeIncomplete, Variant.FromStructure(new WoTValidationOutcomeDataType
            {
                FormatValidated = true,
                FormatOutcome = format,
                CompatibilityOutcome = compatibility
            }));

            CallToolResult result = await fixture.Wot.ValidateResourceAsync(
                fixture.Id("document").ToString(), "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.EqualTo(error));
            Assert.That(json.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.GoodResultsMayBeIncomplete.Code));
            Assert.That(json.GetProperty("nodeId").GetString(), Is.EqualTo(fixture.Id("document").ToString()));
            Assert.That(json.GetProperty("outcome").GetProperty("FormatValidated").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("outcome").GetProperty("FormatOutcome").GetString(),
                Is.EqualTo($"{format}_{(int)format}"));
            Assert.That(json.GetProperty("outcome").GetProperty("CompatibilityOutcome").GetString(),
                Is.EqualTo($"{compatibility}_{(int)compatibility}"));
            fixture.AssertCall(fixture.Id("document"), WotMethods.WoTDocumentType_Validate);
        }

        [TestCase("enable")]
        [TestCase("default")]
        public async Task DocumentCommandPassesExplicitEpochAndDoesNotRefreshAsync(string operation)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Reply(StatusCodes.Good);

            CallToolResult result = await DocumentMutationAsync(fixture, operation, 29).ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            fixture.AssertCall(fixture.Id("document"), operation == "enable"
                ? WotMethods.WoTDocumentType_SetEnabled : WotMethods.WoTDocumentType_SetDefaultVersion,
                operation == "enable"
                    ? [Variant.From(false), Variant.From(29u)] : [Variant.From("v7"), Variant.From(29u)]);
            Assert.That(fixture.Reads, Is.Empty);
        }

        [TestCase("enable")]
        [TestCase("default")]
        public async Task DocumentCommandZeroEpochRequiresExplicitUnconditionalFlagAsync(string operation)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Reply(StatusCodes.Good);
            CallToolResult denied = await DocumentMutationAsync(fixture, operation, 0).ConfigureAwait(false);
            RemoteModelTestSession.ArgumentError(denied);
            Assert.That(fixture.Calls, Is.Empty);
            Assert.That(fixture.Browses, Is.Empty);

            CallToolResult accepted = await DocumentMutationAsync(fixture, operation, 0, true).ConfigureAwait(false);

            Assert.That(accepted.IsError, Is.False);
            Assert.That(fixture.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Calls[0].InputArguments[1], Is.EqualTo(Variant.From(0u)));
        }

        [TestCase("validate")]
        [TestCase("enable")]
        [TestCase("default")]
        public async Task DocumentCommandsPreserveRefusalWithoutRetryAsync(string operation)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.WotRoots();
            fixture.Reply(StatusCodes.BadInvalidState);
            CallToolResult result = await DocumentMutationAsync(fixture, operation, 7).ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadInvalidState);
            Assert.That(fixture.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Reads, Is.Empty);
        }

        [TestCase("state")]
        [TestCase("validate")]
        [TestCase("enable")]
        [TestCase("default")]
        public async Task DocumentOperationsRejectNonDocumentTypeBeforeCallOrValueReadAsync(string operation)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.Type(fixture.Id("document"), ObjectTypeIds.FolderType);
            CallToolResult result = operation == "state"
                ? await fixture.Wot.ReadResourceStateAsync(fixture.Id("document").ToString(), "selected")
                    .ConfigureAwait(false)
                : await DocumentMutationAsync(fixture, operation, 7).ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadTypeMismatch);
            Assert.That(fixture.Calls, Is.Empty);
            Assert.That(fixture.Reads, Is.Empty);
        }

        private static Task<CallToolResult> LegacyMutationAsync(RemoteModelTestSession fixture, string operation)
        {
            return operation switch
            {
                "create" => fixture.Wot.CreateAssetAsync("Pump", sessionName: "selected"),
                "create-endpoint" => fixture.Wot.CreateAssetForEndpointAsync(
                    "Pump", "https://device.invalid/td", sessionName: "selected"),
                "delete" => fixture.Wot.DeleteAssetAsync(fixture.Id("asset").ToString(), sessionName: "selected"),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }

        private static Task<CallToolResult> DocumentMutationAsync(
            RemoteModelTestSession fixture, string operation, uint epoch, bool unconditional = false)
        {
            return operation switch
            {
                "validate" => fixture.Wot.ValidateResourceAsync(fixture.Id("document").ToString(), "selected"),
                "enable" => fixture.Wot.SetEnabledAsync(
                    fixture.Id("document").ToString(), false, epoch, unconditional, "selected"),
                "default" => fixture.Wot.SetDefaultVersionAsync(
                    fixture.Id("document").ToString(), "v7", epoch, unconditional, "selected"),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }

        private static readonly string[] s_supportedBindings = ["urn:binding:opcua", "urn:binding:mqtt"];
    }
}
#endif
