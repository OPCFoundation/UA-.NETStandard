/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root. */

#if NET10_0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;
using RegistryTypes = Opc.Ua.XRegistry.ObjectTypeIds;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Direct behavioral tests for shared model access, below both remote tool catalogues.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class McpModelAccessRemoteTests
    {
        [Test]
        public async Task ModelNodeAndNameResolveAdvertisedNamespaceWithoutAppendingAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            int count = fixture.Context.NamespaceUris.Count;
            NodeId actual = McpModelAccess.ModelNode(fixture.Session.Object, RegistryTypes.RegistryType);
            QualifiedName name = McpModelAccess.Name(
                fixture.Session.Object, XRegistry.XRegistryWellKnown.XRegistryNamespaceUri, "Epoch");

            Assert.That(actual, Is.EqualTo(fixture.Model(RegistryTypes.RegistryType)));
            Assert.That(name, Is.EqualTo(new QualifiedName("Epoch", actual.NamespaceIndex)));
            Assert.That(fixture.Context.NamespaceUris.Count, Is.EqualTo(count));
            Assert.That(fixture.Operations, Is.Empty);
        }

        [TestCase("missing")]
        [TestCase("remote")]
        [TestCase("null")]
        public async Task ModelNodeRejectsNonLocalIdentityWithoutNamespaceMutationAsync(string kind)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            ExpandedNodeId id = kind switch
            {
                "missing" => new ExpandedNodeId("x", "urn:not-advertised"),
                "remote" => new ExpandedNodeId(fixture.Id("x"), null, 2),
                _ => ExpandedNodeId.Null
            };
            int count = fixture.Context.NamespaceUris.Count;

            Assert.That(() => McpModelAccess.ModelNode(fixture.Session.Object, id),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNotSupported));
            Assert.That(() => McpModelAccess.Name(fixture.Session.Object, "urn:not-advertised", "Epoch"),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNotSupported));
            Assert.That(fixture.Context.NamespaceUris.Count, Is.EqualTo(count));
            Assert.That(fixture.Operations, Is.Empty);
        }

        [Test]
        public async Task ChildResolvesExactQualifiedHierarchicalPathAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            var name = new QualifiedName("Epoch", fixture.InstanceNamespace);
            fixture.Child(fixture.Id("owner"), name, fixture.Id("counter"));
            NodeId actual = await McpModelAccess.ChildAsync(
                fixture.Session.Object, fixture.Id("owner"), name).ConfigureAwait(false);

            Assert.That(actual, Is.EqualTo(fixture.Id("counter")));
            Assert.That(fixture.Paths, Has.Count.EqualTo(1));
            Assert.That(fixture.Paths[0].StartingNode, Is.EqualTo(fixture.Id("owner")));
            RelativePathElement element = fixture.Paths[0].RelativePath.Elements[0];
            Assert.That(element.TargetName, Is.EqualTo(name));
            Assert.That(element.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
            Assert.That(element.IncludeSubtypes, Is.True);
            Assert.That(element.IsInverse, Is.False);
            Assert.That(fixture.Reads, Is.Empty);
        }

        [TestCaseSource(nameof(s_optionalStatuses))]
        public async Task ChildOptionalAbsenceDoesNotReadFabricatedNodeAsync(StatusCode status)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            var name = new QualifiedName("Optional", fixture.InstanceNamespace);
            fixture.Translations[(fixture.Id("owner"), name)] =
                new BrowsePathResult { StatusCode = status, Targets = [] };

            (NodeId node, DataValue value) = await McpModelAccess.PropertyAsync(
                fixture.Session.Object, fixture.Id("owner"), name).ConfigureAwait(false);

            Assert.That(node.IsNull, Is.True);
            Assert.That(value.IsNull, Is.True);
            Assert.That(fixture.Paths, Has.Count.EqualTo(1));
            Assert.That(fixture.Reads, Is.Empty);
        }

        [TestCase("ambiguous")]
        [TestCase("partial")]
        [TestCase("denied")]
        [TestCase("remote")]
        public async Task ChildRejectsInvalidResolutionInsteadOfReportingOptionalAbsenceAsync(string shape)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            var name = new QualifiedName("Epoch", fixture.InstanceNamespace);
            var target = new BrowsePathTarget { TargetId = fixture.Id("counter"), RemainingPathIndex = uint.MaxValue };
            var response = new BrowsePathResult { StatusCode = StatusCodes.Good, Targets = [target] };
            StatusCode expected = StatusCodes.BadNotSupported;
            switch (shape)
            {
                case "ambiguous":
                    response.Targets = [target, target];
                    expected = StatusCodes.BadBrowseNameDuplicated;
                    break;
                case "partial":
                    target.RemainingPathIndex = 0;
                    break;
                case "denied":
                    response.StatusCode = StatusCodes.BadUserAccessDenied;
                    expected = StatusCodes.BadUserAccessDenied;
                    break;
                default:
                    target.TargetId = new ExpandedNodeId(fixture.Id("counter"), null, 1);
                    break;
            }
            fixture.Translations[(fixture.Id("owner"), name)] = response;

            await Assert.ThatAsync(async () => await McpModelAccess.PropertyAsync(
                fixture.Session.Object, fixture.Id("owner"), name).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(expected))
                .ConfigureAwait(false);
            Assert.That(fixture.Reads, Is.Empty);
        }

        [Test]
        public async Task PropertiesPreserveFalseZeroQualityAndBothTimestampsDistinctFromAbsenceAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            NodeId owner = fixture.Id("resource");
            var enabled = new QualifiedName("Enabled", fixture.InstanceNamespace);
            var epoch = new QualifiedName("Epoch", fixture.InstanceNamespace);
            var absent = new QualifiedName("Optional", fixture.InstanceNamespace);
            var source = new DateTimeUtc(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            var server = new DateTimeUtc(new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc));
            NodeId enabledNode = fixture.Property(owner, enabled,
                new DataValue(Variant.From(false), StatusCodes.UncertainLastUsableValue, source, server));
            fixture.Property(owner, epoch, new DataValue(Variant.From(0u), StatusCodes.BadOutOfRange, source, server));

            JsonObject result = await McpModelAccess.PropertiesAsync(
                fixture.Session.Object, owner, [enabled, epoch, absent]).ConfigureAwait(false);

            Assert.That(result["nodeId"]!.GetValue<string>(), Is.EqualTo(owner.ToString()));
            Assert.That(result["error"]!.GetValue<bool>(), Is.True);
            JsonNode properties = result["properties"]!;
            JsonNode data = properties["Enabled"]!["dataValue"]!;
            Assert.That(properties["Enabled"]!["nodeId"]!.GetValue<string>(), Is.EqualTo(enabledNode.ToString()));
            Assert.That(properties["Enabled"]!["supported"]!.GetValue<bool>(), Is.True);
            Assert.That(data["UaType"]!.GetValue<byte>(), Is.EqualTo((byte)BuiltInType.Boolean));
            Assert.That(data["Value"]!.GetValue<bool>(), Is.False);
            Assert.That(data["Status"]!["Code"]!.GetValue<uint>(),
                Is.EqualTo(StatusCodes.UncertainLastUsableValue.Code));
            Assert.That(DateTime.Parse(data["SourceTimestamp"]!.GetValue<string>(),
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                Is.EqualTo(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
            Assert.That(DateTime.Parse(data["ServerTimestamp"]!.GetValue<string>(),
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                Is.EqualTo(new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc)));
            Assert.That(properties["Epoch"]!["dataValue"]!["UaType"]!.GetValue<byte>(),
                Is.EqualTo((byte)BuiltInType.UInt32));
            Assert.That(properties["Epoch"]!["dataValue"]!["Value"]!.GetValue<uint>(), Is.Zero);
            Assert.That(properties["Epoch"]!["dataValue"]!["Status"]!["Code"]!.GetValue<uint>(),
                Is.EqualTo(StatusCodes.BadOutOfRange.Code));
            Assert.That(properties["Optional"]!["supported"]!.GetValue<bool>(), Is.False);
            Assert.That(properties["Optional"]!["nodeId"], Is.Null);
            Assert.That(properties["Optional"]!["dataValue"], Is.Null);
            Assert.That(fixture.Reads, Has.Count.EqualTo(2));
            Assert.That(fixture.Reads.Select(value => value.AttributeId), Is.All.EqualTo(Attributes.Value));
            Assert.That(fixture.ReadTimestamps, Is.All.EqualTo(TimestampsToReturn.Both));
        }

        [Test]
        public async Task ReadDataValuePreservesBadQualityAndExactTimestampsWithoutThrowingAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            var source = new DateTimeUtc(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            var server = new DateTimeUtc(new DateTime(2026, 1, 2, 3, 4, 7, DateTimeKind.Utc));
            fixture.Values[(fixture.Id("raw"), Attributes.Value)] =
                new DataValue(Variant.From(19u), StatusCodes.BadOutOfRange, source, server);

            DataValue result = await McpModelAccess.ReadDataValueAsync(
                fixture.Session.Object, fixture.Id("raw")).ConfigureAwait(false);

            Assert.That(result.WrappedValue, Is.EqualTo(Variant.From(19u)));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(result.SourceTimestamp, Is.EqualTo(source));
            Assert.That(result.ServerTimestamp, Is.EqualTo(server));
            Assert.That(fixture.Reads, Has.Count.EqualTo(1));
            Assert.That(fixture.Reads[0].NodeId, Is.EqualTo(fixture.Id("raw")));
            Assert.That(fixture.Reads[0].AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(fixture.ReadTimestamps, Is.EqualTo([TimestampsToReturn.Both]));
            Assert.That(fixture.Paths, Is.Empty);
        }

        [TestCase(0)]
        [TestCase(2)]
        public async Task ReadDataValueRejectsInvalidResultCardinalityAsync(int results)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.Session.Setup(value => value.ReadAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = Enumerable.Repeat(new DataValue(Variant.From(1)), results).ToArrayOf()
                });

            await Assert.ThatAsync(async () => await McpModelAccess.ReadDataValueAsync(
                fixture.Session.Object, fixture.Id("raw")).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadUnexpectedError)).ConfigureAwait(false);
            fixture.Session.Verify(value => value.ReadAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<double>(), TimestampsToReturn.Both,
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestCase("{}", false)]
        [TestCase(/*lang=json,strict*/ """{"error":false}""", false)]
        [TestCase(/*lang=json,strict*/ """{"error":"true"}""", false)]
        [TestCase(/*lang=json,strict*/ """{"items":[null,0,"text",{"error":false}]}""", false)]
        [TestCase(/*lang=json,strict*/ """{"error":true,"items":[]}""", true)]
        [TestCase(/*lang=json,strict*/ """{"error":false,"items":[{"error":false},{"error":true}]}""", true)]
        [TestCase(/*lang=json,strict*/ """{"items":[{"items":[{"error":true}]}]}""", true)]
        public void HasErrorAggregatesBooleanFlagsInSelectedItemsOnly(string text, bool expected)
        {
            JsonObject value = JsonNode.Parse(text)!.AsObject();

            Assert.That(McpModelAccess.HasError(value), Is.EqualTo(expected));
            Assert.That(value.ToJsonString(), Is.EqualTo(JsonNode.Parse(text)!.ToJsonString()),
                "Error aggregation must not consume or rewrite typed response data.");
        }

        [Test]
        public async Task ReferenceProjectionPreservesLocaleRemoteIdentityAndActualTypeAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            ReferenceDescription reference = fixture.Reference(
                fixture.Id("remote-asset"), fixture.Id("vendor-type"), "Asset");
            reference.NodeId = new ExpandedNodeId(fixture.Id("remote-asset"), null, 9);
            reference.DisplayName = new LocalizedText("de", "Pumpe");

            JsonObject result = McpModelAccess.Reference(reference);

            Assert.That(result["nodeId"]!.GetValue<string>(), Is.EqualTo(reference.NodeId.ToString()));
            Assert.That(result["remote"]!.GetValue<bool>(), Is.True);
            Assert.That(result["browseName"]!.GetValue<string>(),
                Is.EqualTo(new QualifiedName("Asset", fixture.InstanceNamespace).ToString()));
            Assert.That(result["typeDefinitionId"]!.GetValue<string>(),
                Is.EqualTo(fixture.Id("vendor-type").ToString()));
            Assert.That(result["nodeClass"]!.GetValue<string>(), Is.EqualTo("Object"));
            Assert.That(result["locale"]!.GetValue<string>(), Is.EqualTo("de"));
            Assert.That(result["displayName"]!.GetValue<string>(), Is.EqualTo("Pumpe"));
            Assert.That(fixture.Operations, Is.Empty);
        }

        [Test]
        public async Task SessionReferenceRemapsNamespaceUriToUsableLocalNodeAndTypeIdsAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync(includeRegistry: false).ConfigureAwait(false);
            ushort modelIndex = fixture.Context.NamespaceUris.GetIndexOrAppend(
                XRegistry.XRegistryWellKnown.XRegistryNamespaceUri);
            var expanded = new ExpandedNodeId(
                "uri-qualified-group", XRegistry.XRegistryWellKnown.XRegistryNamespaceUri);
            var reference = new ReferenceDescription
            {
                NodeId = expanded,
                TypeDefinition = RegistryTypes.GroupType,
                BrowseName = new QualifiedName("things", modelIndex),
                DisplayName = new LocalizedText("en", "Things"),
                NodeClass = NodeClass.Object
            };
            int namespaces = fixture.Context.NamespaceUris.Count;

            JsonObject result = McpModelAccess.Reference(fixture.Session.Object, reference);

            Assert.That(result["nodeId"]!.GetValue<string>(),
                Is.EqualTo(new NodeId("uri-qualified-group", modelIndex).ToString()));
            Assert.That(result["typeDefinitionId"]!.GetValue<string>(),
                Is.EqualTo(fixture.Model(RegistryTypes.GroupType).ToString()));
            Assert.That(result["expandedNodeId"]!.GetValue<string>(), Is.EqualTo(expanded.ToString()));
            Assert.That(result["local"]!.GetValue<bool>(), Is.True);
            Assert.That(result["remote"]!.GetValue<bool>(), Is.False);
            Assert.That(result["displayName"]!.GetValue<string>(), Is.EqualTo("Things"));
            Assert.That(fixture.Context.NamespaceUris.Count, Is.EqualTo(namespaces));
            Assert.That(fixture.Operations, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SessionReferenceKeepsRemoteOrUnresolvableIdentityInformationalWithoutFollowingAsync(
            bool remote)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            ExpandedNodeId expanded = remote
                ? new ExpandedNodeId(fixture.Id("foreign"), null, 7)
                : new ExpandedNodeId("foreign", "urn:unsupported-model");
            var reference = new ReferenceDescription
            {
                NodeId = expanded,
                TypeDefinition = new ExpandedNodeId("type", "urn:unsupported-model"),
                BrowseName = new QualifiedName("foreign"),
                NodeClass = NodeClass.Object
            };
            int count = fixture.Context.NamespaceUris.Count;

            JsonObject result = McpModelAccess.Reference(fixture.Session.Object, reference);

            Assert.That(result["nodeId"], Is.Null);
            Assert.That(result["typeDefinitionId"], Is.Null);
            Assert.That(result["expandedNodeId"]!.GetValue<string>(), Is.EqualTo(expanded.ToString()));
            Assert.That(result["local"]!.GetValue<bool>(), Is.False);
            Assert.That(result["remote"]!.GetValue<bool>(), Is.EqualTo(remote));
            Assert.That(fixture.Context.NamespaceUris.Count, Is.EqualTo(count));
            Assert.That(fixture.Operations, Is.Empty);
        }

        [TestCase(0)]
        [TestCase(64)]
        [TestCase(65)]
        public async Task PropertiesEnforceExactFiniteProjectionLimitAsync(int count)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            var names = Enumerable.Range(0, count)
                .Select(index => new QualifiedName($"property-{index}", fixture.InstanceNamespace)).ToArrayOf();
            if (count == 65)
            {
                await Assert.ThatAsync(async () => await McpModelAccess.PropertiesAsync(
                    fixture.Session.Object, fixture.Id("owner"), names).ConfigureAwait(false),
                    Throws.TypeOf<ArgumentOutOfRangeException>()).ConfigureAwait(false);
                Assert.That(fixture.Paths, Is.Empty);
            }
            else
            {
                JsonObject result = await McpModelAccess.PropertiesAsync(
                    fixture.Session.Object, fixture.Id("owner"), names).ConfigureAwait(false);
                Assert.That(result["properties"]!.AsObject(), Has.Count.EqualTo(count));
                Assert.That(result["error"]!.GetValue<bool>(), Is.False);
                Assert.That(fixture.Paths, Has.Count.EqualTo(count));
            }
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RequireTypeAcceptsDirectAndVendorTypeWithoutReplacingActualIdentityAsync(bool vendor)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            NodeId actual = vendor ? fixture.Id("vendor-registry") : fixture.Model(RegistryTypes.RegistryType);
            fixture.Type(fixture.Id("registry"), actual);
            fixture.Subtype(actual, RegistryTypes.RegistryType);

            NodeId resolved = await McpModelAccess.RequireTypeAsync(
                fixture.Session.Object, fixture.Id("registry"), RegistryTypes.RegistryType).ConfigureAwait(false);

            Assert.That(resolved, Is.EqualTo(actual));
            Assert.That(fixture.Browses, Has.Count.EqualTo(1));
            Assert.That(fixture.Browses[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasTypeDefinition));
            Assert.That(fixture.Browses[0].NodeClassMask, Is.EqualTo((uint)NodeClass.ObjectType));
        }

        [TestCase("missing")]
        [TestCase("ambiguous")]
        [TestCase("wrong")]
        public async Task RequireTypeRejectsMissingAmbiguousOrWrongTypeAsync(string kind)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            NodeId node = fixture.Id("candidate");
            if (kind == "ambiguous")
            {
                fixture.Browse(node, ReferenceTypeIds.HasTypeDefinition,
                    [new ReferenceDescription { NodeId = ObjectTypeIds.BaseObjectType },
                        new ReferenceDescription { NodeId = fixture.Model(RegistryTypes.RegistryType) }]);
            }
            else if (kind == "wrong")
            {
                fixture.Type(node, ObjectTypeIds.BaseObjectType);
            }
            StatusCode status = kind == "wrong" ? StatusCodes.BadTypeMismatch : StatusCodes.BadTypeDefinitionInvalid;

            await Assert.ThatAsync(async () => await McpModelAccess.RequireTypeAsync(
                fixture.Session.Object, node, RegistryTypes.RegistryType).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(status))
                .ConfigureAwait(false);
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase(0, 1)]
        [TestCase(1, 2)]
        [TestCase(6, 2)]
        public async Task DiscoveryRespectsDepthVendorTypesCyclesAndRemoteBoundariesAsync(int depth, int count)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            NodeId root = fixture.Id("root");
            NodeId folder = fixture.Id("folder");
            NodeId direct = fixture.Id("direct");
            NodeId derived = fixture.Id("derived");
            NodeId vendor = fixture.Id("vendor-type");
            fixture.Subtype(vendor, RegistryTypes.RegistryType);
            ReferenceDescription remote = fixture.Reference(fixture.Id("remote"), RegistryTypes.RegistryType);
            remote.NodeId = new ExpandedNodeId(fixture.Id("remote"), null, 8);
            fixture.Browse(root, ReferenceTypeIds.HierarchicalReferences,
                [fixture.Reference(direct, RegistryTypes.RegistryType, "direct"),
                    fixture.Reference(folder, ObjectTypeIds.FolderType), remote,
                    fixture.Reference(direct, RegistryTypes.RegistryType, "duplicate")]);
            fixture.Browse(folder, ReferenceTypeIds.HierarchicalReferences,
                [fixture.Reference(derived, vendor, "derived"), fixture.Reference(root, ObjectTypeIds.FolderType)]);

            var results = new List<ReferenceDescription>();
            await foreach (ReferenceDescription reference in McpModelAccess.DiscoverAsync(
                fixture.Session.Object, root, RegistryTypes.RegistryType, depth).ConfigureAwait(false))
            {
                results.Add(reference);
            }

            Assert.That(results, Has.Count.EqualTo(count));
            Assert.That(results[0].NodeId, Is.EqualTo((ExpandedNodeId)direct));
            if (depth > 0)
            {
                Assert.That(results[1].NodeId, Is.EqualTo((ExpandedNodeId)derived));
                Assert.That(results[1].TypeDefinition, Is.EqualTo((ExpandedNodeId)vendor));
            }
            Assert.That(fixture.Browses.Select(value => value.NodeId),
                Is.EqualTo(depth == 0 ? new[] { root } : [root, folder]));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase(5, false)]
        [TestCase(6, true)]
        public async Task DiscoveryReachesLastAllowedHierarchyLevelButNotBeyondRequestedDepthAsync(
            int depth, bool found)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            for (int index = 0; index < 6; index++)
            {
                fixture.Browse(fixture.Id($"level-{index}"), ReferenceTypeIds.HierarchicalReferences,
                    [fixture.Reference(fixture.Id($"level-{index + 1}"), ObjectTypeIds.FolderType)]);
            }
            fixture.Browse(fixture.Id("level-6"), ReferenceTypeIds.HierarchicalReferences,
                [fixture.Reference(fixture.Id("deep-registry"), RegistryTypes.RegistryType)]);

            JsonObject page = await McpCompanionTools.PageAsync(
                McpModelAccess.DiscoverAsync(
                    fixture.Session.Object, fixture.Id("level-0"), RegistryTypes.RegistryType, depth),
                McpModelAccess.Reference).ConfigureAwait(false);

            Assert.That(page["items"]!.AsArray(), Has.Count.EqualTo(found ? 1 : 0));
            if (found)
            {
                Assert.That(page["items"]![0]!["nodeId"]!.GetValue<string>(),
                    Is.EqualTo(fixture.Id("deep-registry").ToString()));
            }
            Assert.That(fixture.Browses, Has.Count.EqualTo(depth + 1));
            Assert.That(fixture.Browses[^1].NodeId, Is.EqualTo(fixture.Id($"level-{depth}")));
        }

        [TestCase(1000)]
        [TestCase(1001)]
        public async Task DiscoveryEnforcesTraversalBudgetNotOnlyReturnedItemCountAsync(int count)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.Browse(fixture.Id("root"), ReferenceTypeIds.HierarchicalReferences,
                Enumerable.Range(0, count).Select(index =>
                    fixture.Reference(fixture.Id($"unrelated-{index}"), ObjectTypeIds.FolderType)).ToArrayOf());
            if (count == 1001)
            {
                await Assert.ThatAsync(async () => await McpCompanionTools.PageAsync(
                    McpModelAccess.DiscoverAsync(
                        fixture.Session.Object, fixture.Id("root"), RegistryTypes.RegistryType, 0),
                    McpModelAccess.Reference).ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadEncodingLimitsExceeded)).ConfigureAwait(false);
            }
            else
            {
                JsonObject page = await McpCompanionTools.PageAsync(
                    McpModelAccess.DiscoverAsync(
                        fixture.Session.Object, fixture.Id("root"), RegistryTypes.RegistryType, 0),
                    McpModelAccess.Reference).ConfigureAwait(false);
                Assert.That(page["items"]!.AsArray(), Is.Empty);
                Assert.That(page["hasMore"]!.GetValue<bool>(), Is.False);
            }
            Assert.That(fixture.Browses, Has.Count.EqualTo(1));
        }

        [TestCase(-1)]
        [TestCase(7)]
        public async Task DiscoveryRejectsDepthOutsideContractBeforeBrowsingAsync(int depth)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            Assert.That(() => McpModelAccess.DiscoverAsync(
                fixture.Session.Object, fixture.Id("root"), RegistryTypes.RegistryType, depth),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(fixture.Browses, Is.Empty);
        }

        [TestCaseSource(nameof(s_methodStatuses))]
        public async Task MethodProjectionRetainsRawStatusArgumentResultsAndTypedOutputsAsync(StatusCode status)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            JsonObject result = McpModelAccess.MethodResult(new CallMethodResult
            {
                StatusCode = status,
                InputArgumentResults = [StatusCodes.Good, StatusCodes.BadOutOfRange],
                OutputArguments = [Variant.From(false), Variant.From(0u), Variant.From("server-v7")]
            }, fixture.Context);

            Assert.That(result["error"]!.GetValue<bool>(), Is.EqualTo(StatusCode.IsBad(status)));
            Assert.That(result["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(status.Code));
            Assert.That(result["statusCode"]!.GetValue<string>(), Is.EqualTo(status.ToString()));
            Assert.That(result["inputArgumentResults"]!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(result["inputArgumentResults"]![1]!.GetValue<uint>(),
                Is.EqualTo(StatusCodes.BadOutOfRange.Code));
            Assert.That(result["outputArguments"]!.AsArray(), Has.Count.EqualTo(3));
            Assert.That(result["outputArguments"]![0]!["UaType"]!.GetValue<byte>(),
                Is.EqualTo((byte)BuiltInType.Boolean));
            Assert.That(result["outputArguments"]![0]!["Value"]!.GetValue<bool>(), Is.False);
            Assert.That(result["outputArguments"]![1]!["UaType"]!.GetValue<byte>(),
                Is.EqualTo((byte)BuiltInType.UInt32));
            Assert.That(result["outputArguments"]![1]!["Value"]!.GetValue<uint>(), Is.Zero);
            Assert.That(result["outputArguments"]![2]!["UaType"]!.GetValue<byte>(),
                Is.EqualTo((byte)BuiltInType.String));
            Assert.That(result["outputArguments"]![2]!["Value"]!.GetValue<string>(), Is.EqualTo("server-v7"));
        }

        [TestCase(MessageSecurityMode.None)]
        [TestCase(MessageSecurityMode.Sign)]
        [TestCase(MessageSecurityMode.Invalid)]
        public async Task MutationRejectsEveryNonEncryptedModeEvenWithForceAsync(MessageSecurityMode mode)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync(mode: mode).ConfigureAwait(false);
            Assert.That(() => McpModelAccess.RequireSecureMutation(fixture.Session.Object, 0, true),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadSecurityModeInsufficient));
            Assert.That(fixture.Operations, Is.Empty);
        }

        [Test]
        public async Task MutationRequiresExplicitZeroOptInButAcceptsPositiveAndAbsentCountersAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            Assert.That(() => McpModelAccess.RequireSecureMutation(fixture.Session.Object, 0),
                Throws.ArgumentException.With.Message.Contains("zero counter"));
            Assert.That(() => McpModelAccess.RequireSecureMutation(fixture.Session.Object, 0, true), Throws.Nothing);
            Assert.That(() => McpModelAccess.RequireSecureMutation(fixture.Session.Object, 1), Throws.Nothing);
            Assert.That(() => McpModelAccess.RequireSecureMutation(fixture.Session.Object, uint.MaxValue),
                Throws.Nothing);
            Assert.That(() => McpModelAccess.RequireSecureMutation(fixture.Session.Object), Throws.Nothing);
            Assert.That(fixture.Operations, Is.Empty);
        }

        private static readonly StatusCode[] s_optionalStatuses =
            [StatusCodes.BadNoMatch, StatusCodes.BadNotFound, StatusCodes.Good];

        private static readonly StatusCode[] s_methodStatuses =
            [
                StatusCodes.Good, StatusCodes.GoodResultsMayBeIncomplete,
                StatusCodes.Uncertain, StatusCodes.BadInvalidArgument
            ];
    }
}
#endif
