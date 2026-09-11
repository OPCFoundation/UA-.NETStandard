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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using UaNamespaces = Opc.Ua.Namespaces;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotEventConditionRegistryTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task RegistryContextConvertsCompanionConditionAndOccurrenceAsync(bool selection)
        {
            using RegistryContext registry = await CreateContextAsync().ConfigureAwait(false);
            var resolver = new SnapshotWotNodeResolver(registry.Service.Current, registry.Contents);
            using WotDocument consumer = Consumer(selection: selection);
            WotTypeDeclarationSet? declarations = await resolver.ResolveDeclarationsAsync(
                QueryId, WotDeclarationScope.Effective).ConfigureAwait(false);
            Assert.That(declarations, Is.Not.Null);
            Assert.That(declarations!.IsComplete, Is.True, declarations.Detail);
            Assert.That(declarations.Supertypes, Is.EqualTo(s_ancestors));
            Assert.That(
                declarations.Declarations.ToArray()!.Single(field => field.NodeId == "i=2042").DeclaringTypeNodeId,
                Is.EqualTo("i=2041"));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                consumer, null, QueryResolver(), null, resolver).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
            AssertTargets(result.Value!);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ProductionRegistryConverterKeepsVerifiedConditionTargetsAsync(bool hintOnly, bool selection)
        {
            using RegistryContext registry = await CreateContextAsync().ConfigureAwait(false);
            using WotDocument consumer = Consumer(hintOnly, selection);
            WotResource resource = await registry.StoreAsync(
                "device", consumer, WoTDocumentKindEnum.ThingDescription).ConfigureAwait(false);
            var converter = new WotNodeSetDocumentConverter();

            WotConversionOutput result = await converter.ConvertAsync(
                resource, ByteString.From(consumer.Utf8Json.Span), registry.Service.Current,
                registry.Contents, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Errors));
            Assert.That(result.Errors, Is.Empty);
            AssertTargets(result.NodeSet!);
        }

        [Test]
        public async Task NativeRegistryContextSuppliesInheritedOccurrenceAsync()
        {
            using var registry = new RegistryContext();
            using WotDocument native = WotNodeSetConverter.FromNodeSet(
                NativeGraph(), options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Always
                });
            await registry.StoreAsync("native", native, WoTDocumentKindEnum.ThingModel).ConfigureAwait(false);
            var resolver = new SnapshotWotNodeResolver(registry.Service.Current, registry.Contents);
            using WotDocument consumer = Consumer(selection: true);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                consumer, null, QueryResolver(), null, resolver).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
            WotTypeDeclarationSet? declarations = await resolver.ResolveDeclarationsAsync(
                QueryId, WotDeclarationScope.Effective).ConfigureAwait(false);
            Assert.That(declarations, Is.Not.Null);
            WotTypeDeclaration field = declarations!.Declarations.ToArray()!.Single(value => value.NodeId == "i=2042");
            Assert.That(field.DeclaringTypeNodeId, Is.EqualTo("i=2041"));
            Assert.That(field.DataType, Is.EqualTo("i=15"));
            Assert.That(field.ValueRank, Is.EqualTo(-1));
            AssertTargets(result.Value!);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RegistryConversionRejectsConflictingParentOrderAsync(bool reverse)
        {
            using RegistryContext registry = await CreateContextAsync(conflicting: true, reverse).ConfigureAwait(false);
            using WotDocument consumer = Consumer();
            WotResource resource = await registry.StoreAsync(
                "device", consumer, WoTDocumentKindEnum.ThingDescription).ConfigureAwait(false);
            var converter = new WotNodeSetDocumentConverter();

            WotConversionOutput result = await converter.ConvertAsync(
                resource, ByteString.From(consumer.Utf8Json.Span), registry.Service.Current,
                registry.Contents, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.NodeSet, Is.Null);
            Assert.That(result.Errors, Is.Not.Empty);
            Assert.That(result.Errors.Any(error => error.Contains(
                "/events/alarm/uav:conditionTypeId", StringComparison.Ordinal)), Is.True);
        }

        [TestCase("cycle", false)]
        [TestCase("cycle", true)]
        [TestCase("wrongParent", false)]
        [TestCase("wrongParent", true)]
        [TestCase("multipleParents", false)]
        [TestCase("multipleParents", true)]
        public async Task RegistryConversionRejectsSuppliedStandardAncestryAsync(string fact, bool standardPin)
        {
            using RegistryContext registry = await CreateContextAsync(standardFact: fact).ConfigureAwait(false);
            using WotDocument original = Consumer();
            JsonObject root = JsonNode.Parse(original.Utf8Json.Span)!.AsObject();
            if (standardPin)
            {
                root["events"]!["alarm"]!["uav:conditionType"] = "ua:ConditionType";
                root["events"]!["alarm"]!["uav:conditionTypeId"] = "i=2782";
            }
            using WotDocument consumer = Parse(root);
            WotResource resource = await registry.StoreAsync(
                "device", consumer, WoTDocumentKindEnum.ThingDescription).ConfigureAwait(false);
            var converter = new WotNodeSetDocumentConverter();

            WotConversionOutput result = await converter.ConvertAsync(
                resource, ByteString.From(consumer.Utf8Json.Span), registry.Service.Current,
                registry.Contents, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.NodeSet, Is.Null);
            Assert.That(result.Errors.Any(error => error.Contains(
                "/events/alarm/uav:conditionTypeId", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public async Task ProductionRegistryConverterPreservesCancellationAsync()
        {
            using RegistryContext registry = await CreateContextAsync().ConfigureAwait(false);
            using WotDocument consumer = Consumer();
            WotResource resource = await registry.StoreAsync(
                "device", consumer, WoTDocumentKindEnum.ThingDescription).ConfigureAwait(false);
            var converter = new WotNodeSetDocumentConverter();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThatAsync(async () =>
                await converter.ConvertAsync(
                    resource, ByteString.From(consumer.Utf8Json.Span), registry.Service.Current,
                    registry.Contents, cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        }

        private static void AssertTargets(UANodeSet nodeSet)
        {
            UANode[] nodes = nodeSet.Items ?? throw new AssertionException("Conversion returned no native nodes.");
            UAObjectType eventType = nodes.OfType<UAObjectType>().Single();
            Assert.That(eventType.References!.Single(reference => reference.ReferenceType == "HasSubtype").Value,
                Is.EqualTo("ns=2;i=7001"));
            Assert.That(nodeSet.NamespaceUris![1], Is.EqualTo(TypeNamespace));
            UAMethod method = nodes.OfType<UAMethod>().Single();
            Assert.That(method.MethodDeclarationId, Is.EqualTo("i=9029"));
            Assert.That(method.ParentNodeId, Is.EqualTo(eventType.NodeId));
            Assert.That(method.BrowseName, Is.EqualTo("AddComment"));
            UAVariable arguments = nodes.OfType<UAVariable>()
                .Single(field => field.BrowseName == "InputArguments");
            Assert.That(arguments.DataType, Is.EqualTo("i=296"));
            Assert.That(arguments.ValueRank, Is.EqualTo(1));
            Assert.That(arguments.Value!.GetElementsByTagName("Name", UaNamespaces.OpcUaXsd)
                .Cast<System.Xml.XmlElement>()
                .Select(value => value.InnerText), Is.EqualTo(s_argumentNames));
        }

        private static async Task<RegistryContext> CreateContextAsync(
            bool conflicting = false, bool reverse = false, string? standardFact = null)
        {
            var context = new RegistryContext();
            try
            {
                using WotDocument baseType = TypeDocument("i=2041", "ua:BaseEventType", [], occurrence: true);
                using WotDocument condition = TypeDocument("i=2782", "ua:ConditionType", standardFact switch
                {
                    "cycle" => [QueryId],
                    "wrongParent" => ["i=58"],
                    "multipleParents" => ["i=2041", OtherId],
                    _ => ["i=2041"]
                });
                using WotDocument custom = TypeDocument(
                    QueryId, "v:CustomAlarm",
                    conflicting ? reverse ? [OtherId, "i=2782"] : ["i=2782", OtherId] : ["i=2782"],
                    eventType: true);
                await context.StoreAsync("base", baseType, WoTDocumentKindEnum.ThingModel).ConfigureAwait(false);
                await context.StoreAsync("condition", condition, WoTDocumentKindEnum.ThingModel).ConfigureAwait(false);
                if (conflicting || standardFact == "multipleParents")
                {
                    using WotDocument other = TypeDocument(OtherId, "v:Other", ["i=58"]);
                    await context.StoreAsync("other", other, WoTDocumentKindEnum.ThingModel).ConfigureAwait(false);
                }
                await context.StoreAsync("custom", custom, WoTDocumentKindEnum.ThingModel).ConfigureAwait(false);
                return context;
            }
            catch
            {
                context.Dispose();
                throw;
            }
        }

        private static WotDocument TypeDocument(
            string id, string browseName, ArrayOf<string> parents, bool occurrence = false, bool eventType = false)
        {
            var links = new JsonArray();
            foreach (string parent in parents)
            {
                links.Add(new JsonObject { ["rel"] = "tm:extends", ["href"] = parent });
            }
            var root = new JsonObject
            {
                ["@context"] = Context(),
                ["@type"] = new JsonArray("tm:ThingModel", eventType ? "uav:eventType" : "uav:objectType"),
                ["uav:id"] = id,
                ["uav:browseName"] = browseName,
                ["links"] = links
            };
            if (eventType)
            {
                root["data"] = Data();
            }
            if (occurrence)
            {
                root["properties"] = new JsonObject
                {
                    ["EventId"] = new JsonObject
                    {
                        ["uav:id"] = "i=2042",
                        ["uav:browseName"] = "ua:EventId",
                        ["type"] = "string",
                        ["contentEncoding"] = "base64",
                        ["uav:mapToType"] = "i=15",
                        ["uav:modellingRule"] = "Mandatory",
                        ["links"] = new JsonArray(new JsonObject
                        {
                            ["rel"] = "ua:HasTypeDefinition",
                            ["href"] = "i=68"
                        })
                    }
                };
            }
            return Parse(root);
        }

        private static WotDocument Consumer(bool hintOnly = false, bool selection = false)
        {
            var affordance = new JsonObject
            {
                ["@type"] = "uav:eventType",
                ["uav:conditionType"] = "v:CustomAlarm",
                ["data"] = Data()
            };
            if (!hintOnly)
            {
                affordance["uav:conditionTypeId"] = QueryId;
            }
            if (selection)
            {
                affordance["uav:eventSelectClauses"] = new JsonArray(new JsonObject
                {
                    ["tm:ref"] = "custom",
                    ["uav:browsePath"] = "EventId"
                });
            }
            JsonObject input = Data();
            input["properties"]!["Comment"] = new JsonObject { ["type"] = "string", ["uav:mapToType"] = "i=21" };
            return Parse(new JsonObject
            {
                ["@context"] = Context(),
                ["@type"] = "uav:object",
                ["uav:id"] = "nsu=urn:registry-review:device;i=5001",
                ["uav:browseName"] = "nsu=urn:registry-review:device;Device",
                ["events"] = new JsonObject { ["alarm"] = affordance },
                ["actions"] = new JsonObject
                {
                    ["comment"] = new JsonObject
                    {
                        ["uav:conditionAction"] = "AddComment",
                        ["uav:actsOn"] = "alarm",
                        ["input"] = input
                    }
                }
            });
        }

        private static IWotThingResolver QueryResolver()
        {
            using WotDocument definition = TypeDocument(QueryId, "v:CustomAlarm", [], eventType: true);
            var bytes = ByteString.From(definition.Utf8Json.Span);
            var resolver = new Mock<IWotThingResolver>();
            resolver.Setup(value => value.ResolveThingAsync(
                    "custom", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(bytes.Memory));
            return resolver.Object;
        }

        private static UANodeSet NativeGraph()
        {
            var baseType = new UAObjectType
            {
                NodeId = "i=2041",
                BrowseName = "BaseEventType",
                References = [new Reference { ReferenceType = "HasProperty", IsForward = true, Value = "i=2042" }]
            };
            return new UANodeSet
            {
                NamespaceUris = ["urn:registry-review:device", TypeNamespace],
                Models = [new ModelTableEntry { ModelUri = "urn:registry-review:device" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=5001", BrowseName = "1:DeviceType",
                        References = [new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=58" }]
                    },
                    new UAObjectType
                    {
                        NodeId = "ns=2;i=7001", BrowseName = "2:CustomAlarm",
                        References =
                        [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=2782" }
                        ]
                    },
                    new UAObjectType
                    {
                        NodeId = "i=2782", BrowseName = "ConditionType",
                        References =
                        [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=2041" }
                        ]
                    },
                    baseType,
                    new UAVariable
                    {
                        NodeId = "i=2042", BrowseName = "EventId", DataType = "i=15", ValueRank = -1,
                        References =
                        [
                            new Reference { ReferenceType = "HasTypeDefinition", IsForward = true, Value = "i=68" }
                        ]
                    }
                ]
            };
        }

        private static JsonObject Context()
        {
            return new JsonObject
            {
                ["ua"] = UaNamespaces.OpcUa,
                ["tm"] = "https://www.w3.org/2019/wot/tm#",
                ["v"] = TypeNamespace
            };
        }

        private static JsonObject Data()
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["EventId"] = new JsonObject { ["type"] = "string", ["contentEncoding"] = "base64" }
                }
            };
        }

        private static WotDocument Parse(JsonObject value)
        {
            return WotDocument.Parse(Encoding.UTF8.GetBytes(value.ToJsonString()));
        }

        private sealed class RegistryContext : IDisposable
        {
            public WotRegistryService Service { get; } = new();

            public Dictionary<string, ByteString> Contents { get; } = new(StringComparer.Ordinal);

            public async Task<WotResource> StoreAsync(string id, WotDocument document, WoTDocumentKindEnum kind)
            {
                var content = ByteString.From(document.Utf8Json.Span);
                Contents[WotContentDigest.ToHex(WotContentDigest.Compute(content))] = content;
                await Service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = kind == WoTDocumentKindEnum.ThingModel
                        ? WotRegistryGroups.ThingModels : WotRegistryGroups.ThingDescriptions,
                    ResourceId = id,
                    Kind = kind,
                    Content = content
                }).ConfigureAwait(false);
                return Service.Current.AllResources().Single(resource => resource.ResourceId == id);
            }

            public void Dispose()
            {
                Service.Dispose();
            }
        }

        private const string TypeNamespace = "urn:registry-review:types";
        private const string QueryId = "nsu=urn:registry-review:types;i=7001";
        private const string OtherId = "nsu=urn:registry-review:types;i=7002";
        private static readonly string[] s_ancestors = ["i=2782", "i=2041"];
        private static readonly string[] s_argumentNames = ["EventId", "Comment"];
    }
}
