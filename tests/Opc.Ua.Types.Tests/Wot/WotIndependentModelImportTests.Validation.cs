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
 *
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
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    public sealed partial class WotIndependentModelImportTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void IndependentPartitionsDoNotHideConflictingContextCopies(bool conflict)
        {
            using var documents = new WotDocumentSet(
                "a", [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:b"))]);
            UANodeSet b = CreatePartition("urn:test:b");
            b.NamespaceUris = ["urn:test:b", "urn:test:a"];
            b.Items =
            [
                b.Items![0],
                new UAObjectType
                {
                    NodeId = "ns=2;i=1",
                    BrowseName = conflict ? "2:Different" : "2:Root",
                    DisplayName = [new Export.LocalizedText { Value = "Root" }]
                }
            ];

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                documents, [CreatePartition("urn:test:a"), b], IndependentOptions());

            Assert.That(result.Success, Is.EqualTo(!conflict), Describe(result));
            if (conflict)
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict &&
                    diagnostic.Location?.NodeId == "ns=1;i=1" && diagnostic.Location.Reference == "b"),
                    Is.True, Describe(result));
            }
            else
            {
                Assert.That(result.Value!.Items, Has.Length.EqualTo(2));
            }
        }

        [Test]
        public async Task IndependentReadableAffordancesCannotClaimAnotherDocumentsRootAsync()
        {
            using WotDocument model = CreateModel("urn:test:b");
            JsonObject second = JsonNode.Parse(model.Utf8Json.Span)!.AsObject();
            second["properties"] = new JsonObject
            {
                ["Root"] = new JsonObject
                {
                    ["uav:id"] = "nsu=urn:test:a;i=1",
                    ["uav:browseName"] = "nsu=urn:test:a;Root",
                    ["type"] = "boolean"
                }
            };
            using var documents = new WotDocumentSet("a",
            [
                new("a", CreateModel("urn:test:a")),
                new("b", WotDocument.Parse(Encoding.UTF8.GetBytes(second.ToJsonString())))
            ]);

            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetAsync(documents, IndependentOptions()).ConfigureAwait(false);

            Assert.That(result.Value, Is.Null, Describe(result));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict &&
                diagnostic.Location?.NodeId == "ns=1;i=1" && diagnostic.Location.Reference == "b"),
                Is.True, Describe(result));
        }

        [Test]
        public async Task IndependentReadableParentLinksKeepTheOwningRootsAsync()
        {
            using WotDocument first = CreateModel("urn:test:a");
            using WotDocument second = CreateModel("urn:test:b");
            JsonObject a = JsonNode.Parse(first.Utf8Json.Span)!.AsObject();
            JsonObject b = JsonNode.Parse(second.Utf8Json.Span)!.AsObject();
            a["@type"] = "uav:object";
            b["@type"] = "uav:object";
            b["uav:componentOf"] = new JsonArray("nsu=urn:test:a;i=1");
            b["links"] = new JsonArray(new JsonObject
            {
                ["rel"] = "uav:componentOf",
                ["href"] = "a"
            });
            using var documents = new WotDocumentSet("a",
            [
                new("a", WotDocument.Parse(Encoding.UTF8.GetBytes(a.ToJsonString()))),
                new("b", WotDocument.Parse(Encoding.UTF8.GetBytes(b.ToJsonString())))
            ]);

            Assert.That(documents.Entries.ToList().Select(entry => entry.Document.Kind),
                Is.All.EqualTo(WotDocumentKind.ThingDescription));

            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetAsync(documents, IndependentOptions()).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Describe(result));
            UAObject parent = result.Value!.Items!.OfType<UAObject>().Single(node => node.NodeId == "ns=1;i=1");
            UAObject child = result.Value.Items!.OfType<UAObject>().Single(node => node.NodeId == "ns=2;i=1");
            Assert.That(parent.BrowseName, Is.EqualTo("1:Root"));
            Assert.That(child.ParentNodeId, Is.EqualTo("ns=1;i=1"));
            Assert.That(child.References!.Any(reference => !reference.IsForward &&
                reference.ReferenceType == "i=47" && reference.Value == "ns=1;i=1"), Is.True);
        }

        [TestCase("root")]
        [TestCase("href")]
        public async Task IndependentDocumentsRequireUnambiguousSourceOwnershipAsync(string collision)
        {
            using var documents = new WotDocumentSet("a",
            [
                new("a", CreateModel("urn:test:a")),
                new(collision == "href" ? "a" : "b", CreateModel(collision == "root" ? "urn:test:a" : "urn:test:b"))
            ]);

            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetAsync(documents, IndependentOptions()).ConfigureAwait(false);

            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Location?.Reference == (collision == "href" ? "a" : "b")),
                Is.True, Describe(result));
        }

        [Test]
        public void IndependentAliasRenamingDoesNotOverwriteAnExistingAlias()
        {
            using var documents = new WotDocumentSet(
                "a", [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:b"))]);
            UANodeSet a = CreatePartition("urn:test:a");
            a.Aliases = [.. a.Aliases!, new NodeIdAlias { Alias = "Root_1", Value = "i=33" }];

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                documents, [a, CreatePartition("urn:test:b")], IndependentOptions());

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.Aliases!.Single(alias => alias.Alias == "Root_1").Value, Is.EqualTo("i=33"));
            Assert.That(result.Value.Aliases.Select(alias => alias.Alias).Distinct().Count(),
                Is.EqualTo(result.Value.Aliases.Length));
            Assert.That(result.Value.Aliases.Select(alias => alias.Value),
                Does.Contain("ns=1;i=1").And.Contain("ns=2;i=1"));
        }

        [Test]
        public async Task ImportModeDoesNotChangeVerifiedPartitionExportAsync()
        {
            var source = new UANodeSet
            {
                NamespaceUris = ["urn:test:b", "urn:test:a"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:b" }],
                Aliases = [new NodeIdAlias { Alias = "HasSubtype", Value = "i=45" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1",
                        BrowseName = "1:Root",
                        DisplayName = [new Export.LocalizedText { Value = "Root" }],
                        References = [new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=58" }]
                    }
                ]
            };
            WotNodeSetConverterOptions options = IndependentOptions();
            options.PreservationMode = WotNodeSetPreservationMode.Never;
            byte[] original = Serialize(source);

            WotConversionResult<WotDocumentSet> result = await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                source, "root", options: options).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Describe(result));
            using WotDocumentSet documents = result.Value!;
            Assert.That(documents.Entries[0].Document.TryGetNativeProjection(out _), Is.False);
            Assert.That(documents.Entries[0].Document.TryGetEnvelope(out _), Is.False);
            Assert.That(options.DocumentSetMode, Is.EqualTo(WotDocumentSetMode.IndependentReadableModels));
            Assert.That(options.PreservationMode, Is.EqualTo(WotNodeSetPreservationMode.Never));
            Assert.That(Serialize(source), Is.EqualTo(original));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task IndependentReadableDefinitionsAndArgumentsKeepTheirOwnersAsync(bool reverse)
        {
            WotDocumentSetEntry a = new("a", CreateDefinitionModel("a"));
            WotDocumentSetEntry b = new("b", CreateDefinitionModel("b"));
            using var documents = new WotDocumentSet("a", reverse ? [b, a] : [a, b]);
            byte[][] original = documents.Entries.ToList()
                .Select(entry => entry.Document.Utf8Json.ToArray()).ToArray();

            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetAsync(documents, IndependentOptions()).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.NamespaceUris, Is.EqualTo(s_namespaceUris));
            UADataType first = result.Value.Items!.OfType<UADataType>().Single(node => node.NodeId == "ns=1;i=300");
            UADataType second = result.Value.Items!.OfType<UADataType>().Single(node => node.NodeId == "ns=2;i=300");
            Assert.That(first.BrowseName, Is.EqualTo("1:Record"));
            Assert.That(second.BrowseName, Is.EqualTo("2:Record"));
            Assert.That(first.Definition!.Field![0].DataType, Is.EqualTo("i=17"));
            Assert.That(second.Definition!.Field![0].DataType, Is.EqualTo("ns=1;i=300"));
            UAVariable inputA = result.Value.Items!.OfType<UAVariable>().Single(node =>
                node.BrowseName == "InputArguments" && node.ParentNodeId == "ns=1;i=400");
            UAVariable inputB = result.Value.Items!.OfType<UAVariable>().Single(node =>
                node.BrowseName == "InputArguments" && node.ParentNodeId == "ns=2;i=400");
            Assert.That(ValueText(inputA.Value!, "DataType", "Identifier"), Is.EqualTo("ns=2;i=300"));
            Assert.That(ValueText(inputB.Value!, "DataType", "Identifier"), Is.EqualTo("ns=1;i=300"));
            Assert.That(result.Value.Items!.OfType<UAMethod>().Single(node => node.NodeId == "ns=1;i=400")
                .ParentNodeId, Is.EqualTo("ns=1;i=1"));
            Assert.That(result.Value.Items!.OfType<UAMethod>().Single(node => node.NodeId == "ns=2;i=400")
                .ParentNodeId, Is.EqualTo("ns=2;i=1"));
            for (int index = 0; index < original.Length; index++)
            {
                Assert.That(documents.Entries[index].Document.Utf8Json.ToArray(), Is.EqualTo(original[index]));
            }
        }

        [Test]
        public async Task IndependentReadableDataTypeCyclesFailWithoutPartialModelsAsync()
        {
            using WotDocument a = CreateDefinitionModel("a");
            using WotDocument b = CreateDefinitionModel("b");
            JsonObject first = JsonNode.Parse(a.Utf8Json.Span)!.AsObject();
            JsonObject second = JsonNode.Parse(b.Utf8Json.Span)!.AsObject();
            first["uav:dataTypeDefinitions"]![0]!["uav:dataTypeSubtypeOf"] =
                new JsonObject { ["@id"] = "urn:definition:b" };
            second["uav:dataTypeDefinitions"]![0]!["uav:dataTypeSubtypeOf"] =
                new JsonObject { ["@id"] = "urn:definition:a" };
            using var documents = new WotDocumentSet("a",
            [
                new("a", WotDocument.Parse(Encoding.UTF8.GetBytes(first.ToJsonString()))),
                new("b", WotDocument.Parse(Encoding.UTF8.GetBytes(second.ToJsonString())))
            ]);

            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetAsync(documents, IndependentOptions()).ConfigureAwait(false);

            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                diagnostic.Location?.JsonPointer is not null), Is.True, Describe(result));
        }

        [Test]
        public async Task GeneratedIdentitiesUseTheOwningModelRatherThanNamespaceOneAsync()
        {
            using WotDocument b = CreateModel("urn:test:b");
            JsonObject root = JsonNode.Parse(b.Utf8Json.Span)!.AsObject();
            root["@context"]!["ns1"] = "urn:test:a";
            root["@context"]!["ns2"] = "urn:test:b";
            root["uav:browseName"] = "ns2:Root";
            root["properties"] = new JsonObject
            {
                ["Auto"] = new JsonObject { ["type"] = "boolean", ["const"] = true }
            };
            using var documents = new WotDocumentSet("a",
            [
                new("a", CreateModel("urn:test:a")),
                new("b", WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString())))
            ]);

            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetAsync(documents, IndependentOptions()).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Describe(result));
            UAVariable variable = result.Value!.Items!.OfType<UAVariable>().Single();
            Assert.That(NodeId.Parse(variable.NodeId!).NamespaceIndex, Is.EqualTo(2));
            Assert.That(variable.BrowseName, Is.EqualTo("2:Auto"));
            Assert.That(variable.ParentNodeId, Is.EqualTo("ns=2;i=1"));
            Assert.That(variable.Value!.InnerText, Is.EqualTo("true"));
        }

        [Test]
        public void NamespaceUnionUsesUnicodeCodePointOrder()
        {
            using var documents = new WotDocumentSet("first",
            [
                new("first", CreateModel("urn:test:\U00010000")),
                new("second", CreateModel("urn:test:\uE000"))
            ]);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                documents, [CreatePartition("urn:test:\U00010000"), CreatePartition("urn:test:\uE000")],
                IndependentOptions());

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.NamespaceUris, Is.EqualTo(s_unicodeNamespaces));
        }

        [TestCase("aliasCycle")]
        [TestCase("nodeIndex")]
        [TestCase("browseIndex")]
        [TestCase("emptyNamespace")]
        [TestCase("remoteIndex")]
        public void IndependentPartitionsRejectUnresolvedIdentityIndexesAndAliases(string invalid)
        {
            using var documents = new WotDocumentSet(
                "a", [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:b"))]);
            UANodeSet b = CreatePartition("urn:test:b");
            switch (invalid)
            {
                case "aliasCycle":
                    b.Aliases =
                    [
                        new NodeIdAlias { Alias = "Root", Value = "Other" },
                        new NodeIdAlias { Alias = "Other", Value = "Root" }
                    ];
                    break;
                case "nodeIndex":
                    b.Items![0].NodeId = "ns=2;i=1";
                    break;
                case "browseIndex":
                    b.Items![0].BrowseName = "2:Root";
                    break;
                case "emptyNamespace":
                    b.NamespaceUris = [string.Empty];
                    break;
                default:
                    b.Items![0].References =
                        [new Reference { ReferenceType = "i=35", Value = "svr=9;ns=1;i=42" }];
                    break;
            }
            byte[] original = Serialize(b);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                documents, [CreatePartition("urn:test:a"), b], IndependentOptions());

            Assert.That(result.Value, Is.Null, Describe(result));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == WotDiagnosticSeverity.Error && diagnostic.Location?.Reference == "b"),
                Is.True, Describe(result));
            Assert.That(Serialize(b), Is.EqualTo(original));
        }

        [Test]
        public void IndependentPartitionsPreserveDeclaredRemoteReferencesAndValues()
        {
            using var documents = new WotDocumentSet(
                "a", [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:b"))]);
            UANodeSet a = CreatePartition("urn:test:a");
            UANodeSet b = CreateValuePartition(
                "<uax:ExpandedNodeId><uax:Identifier>svr=1;ns=1;i=42</uax:Identifier></uax:ExpandedNodeId>");
            a.ServerUris = ["urn:server:local", "urn:server:remote"];
            b.ServerUris = ["urn:server:local", "urn:server:remote"];
            b.Items![0].References = [new Reference { ReferenceType = "i=35", Value = "svr=1;ns=1;i=99" }];

            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.MergeNodeSetPartitions(documents, [a, b], IndependentOptions());

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.ServerUris, Is.EqualTo(a.ServerUris));
            Assert.That(result.Value.Items!.Single(node => node.NodeId == "ns=2;i=1").References![0].Value,
                Is.EqualTo("svr=1;ns=2;i=99"));
            Assert.That(ValueText(result.Value.Items!.OfType<UAVariable>().Single().Value!, "Identifier"),
                Is.EqualTo("svr=1;ns=2;i=42"));
        }

        [TestCase("<uax:NodeId><uax:Identifier>ns=9;i=42</uax:Identifier></uax:NodeId>")]
        [TestCase("<uax:QualifiedName><uax:NamespaceIndex>9</uax:NamespaceIndex>" +
            "<uax:Name>Unknown</uax:Name></uax:QualifiedName>")]
        [TestCase("<uax:ExpandedNodeId><uax:Identifier>svr=9;ns=1;i=42</uax:Identifier></uax:ExpandedNodeId>")]
        [TestCase("<uax:NodeId extra=\"ns=1;i=42\"><uax:Identifier>ns=1;i=42</uax:Identifier></uax:NodeId>")]
        [TestCase("<uax:XmlElement><custom xmlns=\"urn:opaque\">ns=1;i=42</custom></uax:XmlElement>")]
        [TestCase("<uax:ExtensionObject><uax:TypeId><uax:Identifier>i=297</uax:Identifier></uax:TypeId>" +
            "<uax:Body><uax:Argument><uax:FutureField>ns=1;i=42</uax:FutureField>" +
            "</uax:Argument></uax:Body></uax:ExtensionObject>")]
        public void IndependentValueRebasingNeverDiscardsUnknownFieldsOrUndeclaredIndexes(string xml)
        {
            using var documents = new WotDocumentSet(
                "a", [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:b"))]);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                documents, [CreatePartition("urn:test:a"), CreateValuePartition(xml)], IndependentOptions());

            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.NamespaceRebaseUnsupported &&
                diagnostic.Location?.NodeId == "ns=1;i=10" && diagnostic.Location.Reference == "b"),
                Is.True, Describe(result));
        }

        [Test]
        public void IndependentVariableTypeValuesAreRebased()
        {
            using var documents = new WotDocumentSet(
                "a", [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:b"))]);
            UANodeSet b = CreateValuePartition(IndependentValueXml("NodeId"));
            b.Items![1] = new UAVariableType
            {
                NodeId = "ns=1;i=10",
                BrowseName = "1:ValueType",
                Value = ((UAVariable)b.Items[1]).Value
            };

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                documents, [CreatePartition("urn:test:a"), b], IndependentOptions());

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(ValueText(result.Value!.Items!.OfType<UAVariableType>().Single().Value!, "Identifier"),
                Is.EqualTo("ns=2;s=literal;ns=1;i=42"));
        }

        [Test]
        public void OpaqueValuesStayUnchangedWhenNoTableNeedsRebasing()
        {
            using var documents = new WotDocumentSet("b", [new("b", CreateModel("urn:test:b"))]);
            UANodeSet b = CreateValuePartition(
                "<uax:ExtensionObject><uax:TypeId><uax:Identifier>ns=1;i=200</uax:Identifier>" +
                "</uax:TypeId><uax:Body><uax:ByteString>AQI=</uax:ByteString></uax:Body></uax:ExtensionObject>");
            byte[] original = Serialize(b);

            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.MergeNodeSetPartitions(documents, [b], IndependentOptions());

            Assert.That(result.Success, Is.True, Describe(result));
            System.Xml.XmlElement value = result.Value!.Items!.OfType<UAVariable>().Single().Value!;
            Assert.That(ValueText(value, "Identifier"), Is.EqualTo("ns=1;i=200"));
            Assert.That(ValueText(value, "ByteString"), Is.EqualTo("AQI="));
            Assert.That(Serialize(b), Is.EqualTo(original));
        }

        [Test]
        public void IndependentPartitionsHonorXmlDepthBeforeCloningOpaqueValues()
        {
            using var documents = new WotDocumentSet("b", [new("b", CreateModel("urn:test:b"))]);
            UANodeSet b = CreateValuePartition(IndependentValueXml("Argument"));
            WotNodeSetConverterOptions options = IndependentOptions();
            options.MaxXmlDepth = 4;

            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.MergeNodeSetPartitions(documents, [b], options);

            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DepthExceeded && diagnostic.Location?.Reference == "b"),
                Is.True, Describe(result));
        }

        private static WotDocument CreateDefinitionModel(string owner)
        {
            string peer = owner == "a" ? "b" : "a";
            using WotDocument model = CreateModel("urn:test:" + owner);
            JsonObject root = JsonNode.Parse(model.Utf8Json.Span)!.AsObject();
            root["@context"]!["peer"] = "urn:test:" + peer;
            var field = new JsonObject { ["@type"] = "uav:StructureField", ["uav:fieldName"] = "Value" };
            if (owner == "a")
            {
                field["uav:fieldDataTypeId"] = "i=17";
            }
            else
            {
                field["uav:fieldDataTypeName"] = "peer:Record";
            }
            root["uav:dataTypeDefinitions"] = new JsonArray(new JsonObject
            {
                ["@id"] = "urn:definition:" + owner,
                ["@type"] = "uav:StructureDefinition",
                ["uav:dataTypeName"] = "ns1:Record",
                ["uav:dataTypeId"] = "nsu=urn:test:" + owner + ";i=300",
                ["uav:structureType"] = "Structure",
                ["uav:fields"] = new JsonArray(field)
            });
            root["actions"] = new JsonObject
            {
                ["Run"] = new JsonObject
                {
                    ["uav:id"] = "nsu=urn:test:" + owner + ";i=400",
                    ["input"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["uav:dataTypeDefinition"] = new JsonObject { ["@id"] = "urn:definition:" + peer }
                    }
                }
            };
            return WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
        }

        private static readonly string[] s_unicodeNamespaces = ["urn:test:\uE000", "urn:test:\U00010000"];
    }
}
