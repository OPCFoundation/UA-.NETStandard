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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Encoders;
using Opc.Ua.Export;
using Opc.Ua.Tests;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    public sealed partial class WotIndependentModelImportTests
    {
        [Test]
        public void IndependentRemoteIdentitySurvivesConfiguredLocalUriCollision()
        {
            UANodeSet a = CreateRemoteIdentityPartition("urn:review:a");
            UANodeSet b = CreateRemoteIdentityPartition("urn:review:b");
            b.Items![0].References =
                [new Reference { ReferenceType = "i=35", Value = "svr=1;ns=1;i=42" }];
            b.Items =
            [
                .. b.Items,
                new UAVariable
                {
                    NodeId = "ns=1;i=10",
                    BrowseName = "1:Value",
                    ParentNodeId = "ns=1;i=1",
                    DataType = "i=18",
                    Value = WotTestData.ParseValue(
                        "<Value xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">" +
                        "<ExpandedNodeId><Identifier>svr=1;ns=1;i=42</Identifier></ExpandedNodeId></Value>")
                }
            ];
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            context.NamespaceUris = new NamespaceTable([Namespaces.OpcUa, "urn:review:b", "urn:review:a"]);
            context.ServerUris = new StringTable(["urn:remote"]);
            WotNodeSetConverterOptions options = IndependentOptions();
            options.ValueEncodingContext = context;
            byte[] originalA = Serialize(a);
            byte[] originalB = Serialize(b);
            string[] originalNamespaces = context.NamespaceUris.ToArray();
            string[] originalServers = context.ServerUris.ToArray();
            using var documents = new WotDocumentSet("b",
                [new("a", CreateModel("urn:review:a")), new("b", CreateModel("urn:review:b"))]);
            byte[][] originalDocuments = documents.Entries.ToList()
                .Select(entry => entry.Document.Utf8Json.ToArray()).ToArray();
            AssertRemoteIdentityNativeImport(b, context);

            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.MergeNodeSetPartitions(documents, [a, b], options);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(result.Value!.Items!.OfType<UAVariable>().Single().Value!
                    .SelectSingleNode("descendant-or-self::*[local-name()='Identifier']")!.InnerText,
                    Is.EqualTo("svr=1;ns=2;i=42"));
                Assert.That(result.Value.Items.Single(node => node.NodeId == "ns=2;i=1").References![0].Value,
                    Is.EqualTo("svr=1;ns=2;i=42"));
                AssertRemoteIdentityNativeImport(result.Value, context);
                Assert.That(Serialize(a), Is.EqualTo(originalA));
                Assert.That(Serialize(b), Is.EqualTo(originalB));
                Assert.That(context.NamespaceUris.ToArray(), Is.EqualTo(originalNamespaces));
                Assert.That(context.ServerUris.ToArray(), Is.EqualTo(originalServers));
                for (int index = 0; index < documents.Entries.Count; index++)
                {
                    Assert.That(documents.Entries[index].Document.Utf8Json.ToArray(),
                        Is.EqualTo(originalDocuments[index]));
                }
            });
        }

        [Test]
        public void IndependentRemoteIdentityKeepsLocalAndRemoteSlots(
            [Values(1, 2)] int remoteCount,
            [Values("Default", "Same", "Different", "EmptySlot", "EmptyTable", "ExistingRemote")] string contextKind,
            [Values(false, true)] bool relocate)
        {
            ArrayOf<string> remotes = RemoteIdentityHeaders(remoteCount);
            UANodeSet a = CreateRemoteIdentityPartition("urn:review:a");
            UANodeSet b = CreateRemoteIdentityPartition("urn:review:b");
            a.ServerUris = remotes.ToArray();
            b.ServerUris = remotes.ToArray();
            b.Items![0].References =
                [new Reference { ReferenceType = "i=35", Value = RemoteIdentityXml(remoteCount, false) }];
            for (int index = 0; index <= remoteCount; index++)
            {
                b.Items =
                [
                    .. b.Items,
                    CreateRemoteIdentityVariable(index, RemoteIdentityXml(index, false))
                ];
            }
            ServiceMessageContext context = CreateRemoteIdentityContext(remotes, contextKind);
            WotNodeSetConverterOptions options = IndependentOptions();
            if (contextKind != "Default")
            {
                options.ValueEncodingContext = context;
            }
            byte[] originalA = Serialize(a);
            byte[] originalB = Serialize(b);
            string[] originalNamespaces = context.NamespaceUris.ToArray();
            string[] originalServers = context.ServerUris.ToArray();
            using WotDocumentSet documents = RemoteIdentityDocuments(relocate);
            byte[][] originalDocuments = documents.Entries.ToList()
                .Select(entry => entry.Document.Utf8Json.ToArray()).ToArray();
            AssertRemoteIdentityImportValues(b, context, remotes);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                documents, relocate ? [a, b] : [b], options);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.ServerUris, Is.EqualTo(remotes.ToArray()));
            UAVariable[] variables = result.Value.Items!.OfType<UAVariable>()
                .OrderBy(node => node.NodeId, StringComparer.Ordinal).ToArray();
            Assert.That(variables, Has.Length.EqualTo(remoteCount + 1));
            for (int index = 0; index < variables.Length; index++)
            {
                Assert.That(RemoteIdentityValueIdentifiers(variables[index].Value!),
                    Is.EqualTo(new[] { RemoteIdentityXml(index, relocate) }));
            }
            string rootId = relocate ? "ns=2;i=1" : "ns=1;i=1";
            Assert.That(result.Value.Items.Single(node => node.NodeId == rootId).References![0].Value,
                Is.EqualTo(RemoteIdentityXml(remoteCount, relocate)));
            AssertRemoteIdentityImportValues(result.Value, context, remotes);
            Assert.That(Serialize(a), Is.EqualTo(originalA));
            Assert.That(Serialize(b), Is.EqualTo(originalB));
            Assert.That(context.NamespaceUris.ToArray(), Is.EqualTo(originalNamespaces));
            Assert.That(context.ServerUris.ToArray(), Is.EqualTo(originalServers));
            for (int index = 0; index < documents.Entries.Count; index++)
            {
                Assert.That(documents.Entries[index].Document.Utf8Json.ToArray(),
                    Is.EqualTo(originalDocuments[index]));
            }
        }

        [Test]
        public void IndependentRemoteIdentityPreservesNestedTypedValues(
            [Values(1, 2)] int remoteCount,
            [Values("Array", "Variants", "DataValue", "DataValueArray", "Matrix",
                "Structure", "Optional", "Union", "VariableType")]
            string kind,
            [Values(false, true)] bool relocate)
        {
            ArrayOf<string> remotes = RemoteIdentityHeaders(remoteCount);
            ServiceMessageContext context = CreateRemoteIdentityContext(remotes, "Same");
            ArrayOf<ExpandedNodeId> identities =
            [
                new ExpandedNodeId(new NodeId(42, 1)),
                new ExpandedNodeId(new NodeId(42, 1), null, (uint)remoteCount)
            ];
            Variant nested = CreateRemoteIdentityNestedValue(kind, identities, context);
            System.Xml.XmlElement xml;
            using (var encoder = new XmlEncoder(context))
            {
                encoder.WriteVariantValue(null, nested);
                xml = WotTestData.ParseValue(encoder.CloseAndReturnText()!);
            }
            Assert.That(RemoteIdentityValueIdentifiers(xml),
                Is.EqualTo(new[] { "ns=1;i=42", RemoteIdentityXml(remoteCount, false) }));
            UANodeSet a = CreateRemoteIdentityPartition("urn:review:a");
            UANodeSet b = CreateRemoteIdentityPartition("urn:review:b");
            a.ServerUris = remotes.ToArray();
            b.ServerUris = remotes.ToArray();
            UANode valueNode = kind == "VariableType"
                ? new UAVariableType
                {
                    NodeId = "ns=1;i=10",
                    BrowseName = "1:Value",
                    DataType = "i=24",
                    ValueRank = nested.TypeInfo.ValueRank,
                    Value = xml
                }
                : new UAVariable
                {
                    NodeId = "ns=1;i=10",
                    BrowseName = "1:Value",
                    ParentNodeId = "ns=1;i=1",
                    DataType = "i=24",
                    ValueRank = nested.TypeInfo.ValueRank,
                    Value = xml
                };
            b.Items = [.. b.Items!, valueNode];
            WotNodeSetConverterOptions options = IndependentOptions();
            options.ValueEncodingContext = context;
            byte[] originalA = Serialize(a);
            byte[] originalB = Serialize(b);
            string[] originalNamespaces = context.NamespaceUris.ToArray();
            string[] originalServers = context.ServerUris.ToArray();
            ExpandedNodeId[] originalTypes = context.Factory.KnownTypeIds.ToArray();
            AssertRemoteIdentityNestedImport(b, context, remotes, kind);
            using WotDocumentSet documents = RemoteIdentityDocuments(relocate);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                documents, relocate ? [a, b] : [b], options);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.ServerUris, Is.EqualTo(remotes.ToArray()));
            System.Xml.XmlElement returned = kind == "VariableType"
                ? result.Value.Items!.OfType<UAVariableType>().Single().Value!
                : result.Value.Items!.OfType<UAVariable>().Single().Value!;
            Assert.That(RemoteIdentityValueIdentifiers(returned),
                Is.EqualTo(new[] { RemoteIdentityXml(0, relocate), RemoteIdentityXml(remoteCount, relocate) }));
            AssertRemoteIdentityNestedImport(result.Value, context, remotes, kind);
            Assert.That(Serialize(a), Is.EqualTo(originalA));
            Assert.That(Serialize(b), Is.EqualTo(originalB));
            Assert.That(context.NamespaceUris.ToArray(), Is.EqualTo(originalNamespaces));
            Assert.That(context.ServerUris.ToArray(), Is.EqualTo(originalServers));
            Assert.That(context.Factory.KnownTypeIds, Is.EquivalentTo(originalTypes));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void IndependentRemoteIdentityNativeExportPreservesEqualUriSlots(int remoteCount)
        {
            ArrayOf<string> remotes = RemoteIdentityHeaders(remoteCount);
            ServiceMessageContext context = CreateRemoteIdentityContext(remotes, "ExistingRemote");
            context.ServerUris = new StringTable([remotes[^1], .. remotes]);
            var source = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                EncodeableFactory = context.Factory
            };
            var root = new BaseObjectTypeState
            {
                NodeId = new NodeId(1, 1),
                BrowseName = new QualifiedName("Root", 1),
                DisplayName = new LocalizedText("Root"),
                SuperTypeId = new NodeId(58)
            };
            root.AddReference(new NodeId(35), false,
                new ExpandedNodeId(new NodeId(42, 1), null, (uint)remoteCount));
            var exported = new UANodeSet
            {
                NamespaceUris = ["urn:review:b"],
                ServerUris = remotes.ToArray(),
                Models = [new ModelTableEntry { ModelUri = "urn:review:b" }]
            };
            string[] originalServers = source.ServerUris.ToArray();

            exported.Export(source, root);
            for (int index = 0; index <= remoteCount; index++)
            {
                var variable = new BaseDataVariableState(root)
                {
                    NodeId = new NodeId((uint)(10 + index), 1),
                    BrowseName = new QualifiedName("Value" + index.ToString(CultureInfo.InvariantCulture), 1),
                    DataType = new NodeId(18),
                    WrappedValue = new ExpandedNodeId(new NodeId(42, 1), null, (uint)index)
                };
                exported.Export(source, variable);
            }

            Assert.That(exported.ServerUris, Is.EqualTo(remotes.ToArray()));
            UAVariable[] variables = exported.Items!.OfType<UAVariable>()
                .OrderBy(node => node.NodeId, StringComparer.Ordinal).ToArray();
            Assert.That(variables, Has.Length.EqualTo(remoteCount + 1));
            for (int index = 0; index < variables.Length; index++)
            {
                Assert.That(RemoteIdentityValueIdentifiers(variables[index].Value!),
                    Is.EqualTo(new[] { RemoteIdentityXml(index, false) }));
            }
            AssertRemoteIdentityImportValues(exported, context, remotes);
            Assert.That(source.ServerUris.ToArray(), Is.EqualTo(originalServers));
        }

        [Test]
        public void IndependentRemoteIdentityNativeImportUsesActualDestinationServer()
        {
            UANodeSet source = CreateRemoteIdentityPartition("urn:review:b");
            source.Items![0].References =
                [new Reference { ReferenceType = "i=35", Value = "svr=1;ns=1;i=42" }];
            source.Items = [.. source.Items, CreateRemoteIdentityVariable(0, "svr=1;ns=1;i=42")];
            ServiceMessageContext context = CreateRemoteIdentityContext(RemoteIdentityHeaders(1), "Same");
            var destination = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                EncodeableFactory = context.Factory
            };
            byte[] original = Serialize(source);
            string[] originalServers = destination.ServerUris.ToArray();
            var imported = new NodeStateCollection();

            source.Import(destination, imported);

            var references = new List<IReference>();
            imported.OfType<BaseObjectTypeState>().Single().GetReferences(destination, references);
            ExpandedNodeId reference = references.Single(item => item.ReferenceTypeId == new NodeId(35)).TargetId;
            Assert.That(reference.ToString(), Is.EqualTo("ns=1;i=42"));
            Assert.That(imported.OfType<BaseVariableState>().Single().WrappedValue.TryGetValue(
                out ExpandedNodeId value), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(value, Is.EqualTo(reference));
                Assert.That(destination.ServerUris.GetString(value.ServerIndex), Is.EqualTo("urn:remote"));
                Assert.That(destination.ServerUris.ToArray(), Is.EqualTo(originalServers));
                Assert.That(Serialize(source), Is.EqualTo(original));
            });
        }

        private static UANodeSet CreateRemoteIdentityPartition(string namespaceUri)
        {
            return new UANodeSet
            {
                NamespaceUris = [namespaceUri],
                ServerUris = ["urn:remote"],
                Models = [new ModelTableEntry { ModelUri = namespaceUri }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1",
                        BrowseName = "1:Root",
                        DisplayName = [new Export.LocalizedText { Value = "Root" }]
                    }
                ]
            };
        }

        private static void AssertRemoteIdentityNativeImport(UANodeSet source, ServiceMessageContext context)
        {
            var target = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable([Namespaces.OpcUa, "urn:review:b", "urn:review:a"]),
                ServerUris = new StringTable(["urn:import:local", "urn:remote"]),
                EncodeableFactory = context.Factory
            };
            var imported = new NodeStateCollection();
            source.Import(target, imported);
            BaseVariableState variable = imported.OfType<BaseVariableState>().Single();
            Assert.That(variable.WrappedValue.TryGetValue(out ExpandedNodeId identity), Is.True);
            Assert.That(identity.ToString(), Is.EqualTo("svr=1;ns=1;i=42"));
            Assert.That(target.ServerUris.GetString(identity.ServerIndex), Is.EqualTo("urn:remote"));
            var references = new List<IReference>();
            imported.OfType<BaseObjectTypeState>().Single(node => node.NodeId == new NodeId(1, 1))
                .GetReferences(target, references);
            Assert.That(references.Single(reference => reference.TargetId.ServerIndex != 0).TargetId.ToString(),
                Is.EqualTo("svr=1;nsu=urn:review:b;i=42"));
        }

        private static WotDocumentSet RemoteIdentityDocuments(bool relocate)
        {
            return new WotDocumentSet("b", relocate
                ? [new("a", CreateModel("urn:review:a")), new("b", CreateModel("urn:review:b"))]
                : [new("b", CreateModel("urn:review:b"))]);
        }

        private static ArrayOf<string> RemoteIdentityHeaders(int remoteCount)
        {
            return remoteCount switch
            {
                1 => ["urn:remote"],
                2 => ["urn:review:remote:first", "urn:review:remote:last"],
                _ => throw new ArgumentOutOfRangeException(nameof(remoteCount))
            };
        }

        private static string RemoteIdentityXml(int serverIndex, bool relocate)
        {
            return (serverIndex, relocate) switch
            {
                (0, false) => "ns=1;i=42",
                (0, true) => "ns=2;i=42",
                (1, false) => "svr=1;ns=1;i=42",
                (1, true) => "svr=1;ns=2;i=42",
                (2, false) => "svr=2;ns=1;i=42",
                (2, true) => "svr=2;ns=2;i=42",
                _ => throw new ArgumentOutOfRangeException(nameof(serverIndex))
            };
        }

        private static UAVariable CreateRemoteIdentityVariable(int index, string identifier)
        {
            return new UAVariable
            {
                NodeId = new NodeId((uint)(10 + index), 1).ToString(),
                BrowseName = "1:Value" + index.ToString(CultureInfo.InvariantCulture),
                ParentNodeId = "ns=1;i=1",
                DataType = "i=18",
                Value = WotTestData.ParseValue(
                    "<Value xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">" +
                    "<ExpandedNodeId><Identifier>" + identifier + "</Identifier></ExpandedNodeId></Value>")
            };
        }

        private static ServiceMessageContext CreateRemoteIdentityContext(ArrayOf<string> remotes, string kind)
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            context.NamespaceUris = new NamespaceTable([Namespaces.OpcUa, "urn:review:b", "urn:review:a"]);
            context.ServerUris = new StringTable(kind switch
            {
                "Default" or "EmptyTable" => [],
                "Same" => remotes.Count == 1 ? [remotes[0]] : [remotes[1], "urn:unused", remotes[0]],
                "Different" => ["urn:context:local", .. Enumerable.Reverse(remotes.ToArray())],
                "EmptySlot" => [string.Empty, .. Enumerable.Reverse(remotes.ToArray())],
                "ExistingRemote" => [remotes[^1], .. Enumerable.Reverse(remotes.ToArray())],
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            });
            return context;
        }

        private static SystemContext CreateRemoteIdentityImportContext(
            ServiceMessageContext context,
            ArrayOf<string> remotes)
        {
            return new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable([Namespaces.OpcUa, "urn:review:b", "urn:review:a"]),
                ServerUris = new StringTable(["urn:import:local", .. Enumerable.Reverse(remotes.ToArray())]),
                EncodeableFactory = context.Factory
            };
        }

        private static void AssertRemoteIdentityImportValues(
            UANodeSet source,
            ServiceMessageContext context,
            ArrayOf<string> remotes)
        {
            SystemContext target = CreateRemoteIdentityImportContext(context, remotes);
            var imported = new NodeStateCollection();
            source.Import(target, imported);
            for (int index = 0; index <= remotes.Count; index++)
            {
                BaseVariableState variable = imported.OfType<BaseVariableState>()
                    .Single(node => node.NodeId == new NodeId((uint)(10 + index), 1));
                Assert.That(variable.WrappedValue.TryGetValue(out ExpandedNodeId identity), Is.True);
                Assert.That(identity.InnerNodeId, Is.EqualTo(new NodeId(42, 1)));
                Assert.That(identity.ServerIndex, Is.EqualTo(index == 0 ? 0 : remotes.Count - index + 1));
                Assert.That(target.ServerUris.GetString(identity.ServerIndex),
                    Is.EqualTo(index == 0 ? "urn:import:local" : remotes[index - 1]));
            }
            var references = new List<IReference>();
            imported.OfType<BaseObjectTypeState>().Single(node => node.NodeId == new NodeId(1, 1))
                .GetReferences(target, references);
            Assert.That(references.Single(reference => reference.TargetId.ServerIndex != 0).TargetId.ToString(),
                Is.EqualTo("svr=1;nsu=urn:review:b;i=42"));
        }

        private static string[] RemoteIdentityValueIdentifiers(System.Xml.XmlElement value)
        {
            return value.SelectNodes("descendant-or-self::*[local-name()='ExpandedNodeId']" +
                "/*[local-name()='Identifier']")!.Cast<XmlNode>().Select(node => node.InnerText).ToArray();
        }

        private static Variant CreateRemoteIdentityNestedValue(
            string kind,
            ArrayOf<ExpandedNodeId> identities,
            ServiceMessageContext context)
        {
            switch (kind)
            {
                case "Array":
                    return Variant.From(identities);
                case "Variants":
                case "VariableType":
                    ArrayOf<Variant> variants = [identities[0], identities[1]];
                    return Variant.From(variants);
                case "DataValue":
                    return Variant.From(new DataValue(Variant.From(identities)));
                case "DataValueArray":
                    ArrayOf<DataValue> dataValues = [new DataValue(identities[0]), new DataValue(identities[1])];
                    return Variant.From(dataValues);
                case "Matrix":
                    return Variant.From(identities.ToMatrix(s_remoteIdentityDimensions));
                case "Structure":
                case "Optional":
                case "Union":
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
            var definition = new StructureDefinition
            {
                StructureType = kind switch
                {
                    "Optional" => StructureType.StructureWithOptionalFields,
                    "Union" => StructureType.Union,
                    _ => StructureType.Structure
                },
                Fields =
                [
                    new StructureField
                    {
                        Name = "Targets",
                        DataType = new NodeId(18),
                        ValueRank = ValueRanks.OneDimension,
                        IsOptional = kind == "Optional"
                    }
                ]
            };
            var name = new XmlQualifiedName("RemoteTargets", "urn:review:b");
            var typeId = new ExpandedNodeId(700, "urn:review:b");
            var binaryId = new ExpandedNodeId(701, "urn:review:b");
            var xmlId = new ExpandedNodeId(702, "urn:review:b");
            var fields = new Dictionary<string, BuiltInType> { ["Targets"] = BuiltInType.ExpandedNodeId };
            Structure structure = kind switch
            {
                "Optional" => new StructureWithOptionalFields(name, typeId, binaryId, xmlId, definition, fields),
                "Union" => new Opc.Ua.Encoders.Union(name, typeId, binaryId, xmlId, definition, fields),
                _ => new Structure(name, typeId, binaryId, xmlId, definition, fields)
            };
            structure["Targets"] = Variant.From(identities);
            context.Factory.Builder.AddEncodeableType(structure).Commit();
            return new ExtensionObject(structure);
        }

        private static void AssertRemoteIdentityNestedImport(
            UANodeSet source,
            ServiceMessageContext context,
            ArrayOf<string> remotes,
            string kind)
        {
            SystemContext target = CreateRemoteIdentityImportContext(context, remotes);
            var imported = new NodeStateCollection();
            source.Import(target, imported);
            Variant value = kind == "VariableType"
                ? imported.OfType<BaseVariableTypeState>().Single().WrappedValue
                : imported.OfType<BaseVariableState>().Single().WrappedValue;
            ArrayOf<ExpandedNodeId> identities = ReadRemoteIdentityNestedValue(value, kind);
            Assert.That(identities.Count, Is.EqualTo(2));
            Assert.That(identities[0].ToString(), Is.EqualTo("ns=1;i=42"));
            Assert.That(identities[1].ToString(), Is.EqualTo("svr=1;ns=1;i=42"));
            Assert.That(target.ServerUris.GetString(identities[0].ServerIndex), Is.EqualTo("urn:import:local"));
            Assert.That(target.ServerUris.GetString(identities[1].ServerIndex), Is.EqualTo(remotes[^1]));
        }

        private static ArrayOf<ExpandedNodeId> ReadRemoteIdentityNestedValue(Variant value, string kind)
        {
            switch (kind)
            {
                case "Array":
                    Assert.That(value.TryGetValue(out ArrayOf<ExpandedNodeId> array), Is.True);
                    return array;
                case "Variants":
                case "VariableType":
                    Assert.That(value.TryGetValue(out ArrayOf<Variant> variants), Is.True);
                    Assert.That(variants.Count, Is.EqualTo(2));
                    Assert.That(variants[0].TryGetValue(out ExpandedNodeId first), Is.True);
                    Assert.That(variants[1].TryGetValue(out ExpandedNodeId second), Is.True);
                    return [first, second];
                case "DataValue":
                    Assert.That(value.TryGetValue(out DataValue dataValue), Is.True);
                    return ReadRemoteIdentityNestedValue(dataValue.WrappedValue, "Array");
                case "DataValueArray":
                    Assert.That(value.TryGetValue(out ArrayOf<DataValue> dataValues), Is.True);
                    Assert.That(dataValues.Count, Is.EqualTo(2));
                    Assert.That(dataValues[0].WrappedValue.TryGetValue(out ExpandedNodeId local), Is.True);
                    Assert.That(dataValues[1].WrappedValue.TryGetValue(out ExpandedNodeId remote), Is.True);
                    return [local, remote];
                case "Matrix":
                    Assert.That(value.TryGetValue(out MatrixOf<ExpandedNodeId> matrix), Is.True);
                    Assert.That(matrix.Dimensions, Is.EqualTo(s_remoteIdentityDimensions));
                    return matrix.ToArrayOf();
                case "Structure":
                case "Optional":
                case "Union":
                    Assert.That(value.TryGetValue(out ExtensionObject extension), Is.True);
                    Assert.That(extension.TryGetValue(out IEncodeable body), Is.True);
                    if (body is not Structure structure)
                    {
                        throw new InvalidOperationException("The imported value is not the registered structure.");
                    }
                    Assert.That(structure.StructureType, Is.EqualTo(kind switch
                    {
                        "Optional" => StructureType.StructureWithOptionalFields,
                        "Union" => StructureType.Union,
                        _ => StructureType.Structure
                    }));
                    return ReadRemoteIdentityNestedValue(structure["Targets"], "Array");
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static readonly int[] s_remoteIdentityDimensions = [1, 2];
    }
}
