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

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        private static IEnumerable<TestCaseData> ReviewServerCases
        {
            get
            {
                for (int count = 1; count <= 2; count++)
                {
                    for (int index = 0; index <= count + 1; index++)
                    {
                        foreach (bool changed in s_reviewChangedTables)
                        {
                            foreach (bool reference in s_reviewChangedTables)
                            {
                                yield return new TestCaseData(count, index, changed, reference);
                            }
                        }
                    }
                }
            }
        }

        [TestCaseSource(nameof(ReviewServerCases))]
        public void IndependentReviewRemoteServerIndexesFollowNodeSetHeaders(
            int serverCount,
            int serverIndex,
            bool changed,
            bool reference)
        {
            string[] servers = s_reviewRemoteServers.Take(serverCount).ToArray();
            UANodeSet b = CreateValuePartition(
                ReviewExpandedXml(new ExpandedNodeId(new NodeId(42, 1), null, (uint)serverIndex).ToString()));
            b.ServerUris = servers;
            if (reference)
            {
                b.Items = [b.Items![0]];
                b.Items[0].References =
                [
                    new Reference
                    {
                        ReferenceType = "i=35",
                        Value = new ExpandedNodeId(new NodeId(42, 1), null, (uint)serverIndex).ToString()
                    }
                ];
            }
            byte[] original = Serialize(b);
            using WotDocumentSet documents = ReviewDocuments(changed);
            UANodeSet a = CreatePartition("urn:test:a");
            a.ServerUris = servers;

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                documents, changed ? [a, b] : [b], IndependentOptions());

            Assert.That(result.Success, Is.EqualTo(serverIndex <= serverCount), Describe(result));
            Assert.That(Serialize(b), Is.EqualTo(original));
            if (serverIndex > serverCount)
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.NamespaceRebaseUnsupported &&
                    diagnostic.Location?.Reference == "b"), Is.True, Describe(result));
                return;
            }
            Assert.That(result.Value!.ServerUris, Is.EqualTo(servers));
            ushort namespaceIndex = changed ? (ushort)2 : (ushort)1;
            var expected = new ExpandedNodeId(new NodeId(42, namespaceIndex), null, (uint)serverIndex);
            if (reference)
            {
                Assert.That(result.Value.Items!.Single(node =>
                    node.NodeId == new NodeId(1, namespaceIndex).ToString()).References![0].Value,
                    Is.EqualTo(expected.ToString()));
            }
            else
            {
                System.Xml.XmlElement value = result.Value.Items!.OfType<UAVariable>().Single().Value!;
                Assert.That(ReviewReadExpanded(value, result.Value).ToString(), Is.EqualTo(expected.ToString()));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IndependentReviewNativeExportImportKeepsRemoteReferencesAndValues(bool configuredContext)
        {
            UANodeSet a = ReviewExportRemote("urn:test:a");
            UANodeSet b = ReviewExportRemote("urn:test:b");
            Assert.That(b.ServerUris, Is.EqualTo(s_reviewSingleRemote));
            SystemContext importedContext = ReviewSystemContext("urn:test:b");
            var imported = new NodeStateCollection();
            b.Import(importedContext, imported);
            var references = new List<IReference>();
            imported.Single().GetReferences(importedContext, references);
            IReference originalRemote = references.Single(reference => reference.TargetId.ServerIndex != 0);
            Assert.That(originalRemote.TargetId.ServerIndex, Is.EqualTo(1));
            Assert.That(importedContext.ServerUris.GetString(1), Is.EqualTo("urn:test:remote:first"));
            UAVariable nativeValue = (UAVariable)CreateValuePartition(
                ReviewExpandedXml("svr=1;ns=1;i=42")).Items![1];
            nativeValue.ParentNodeId = "ns=1;i=1";
            b.Items = [.. b.Items!, nativeValue];
            byte[] originalA = Serialize(a);
            byte[] originalB = Serialize(b);
            WotNodeSetConverterOptions options = IndependentOptions();
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            context.NamespaceUris.Append("urn:test:b");
            context.NamespaceUris.Append("urn:test:a");
            context.ServerUris.Update(
                ["urn:test:context:local", "urn:test:unused", "urn:test:remote:first"]);
            if (configuredContext)
            {
                options.ValueEncodingContext = context;
            }
            string[] namespaces = context.NamespaceUris.ToArray();
            string[] servers = context.ServerUris.ToArray();
            using WotDocumentSet documents = ReviewDocuments(changed: true);

            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.MergeNodeSetPartitions(documents, [a, b], options);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.ServerUris, Is.EqualTo(s_reviewSingleRemote));
            Assert.That(ReviewReadExpanded(
                result.Value.Items!.OfType<UAVariable>().Single().Value!, result.Value).ToString(),
                Is.EqualTo("svr=1;ns=2;i=42"));
            var targetContext = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable([Namespaces.OpcUa, "urn:test:a", "urn:test:b"]),
                ServerUris = new StringTable(["urn:test:target:local", "urn:test:unused", "urn:test:remote:first"]),
                EncodeableFactory = context.Factory
            };
            var combined = new NodeStateCollection();
            result.Value.Import(targetContext, combined);
            BaseObjectTypeState owner = combined.OfType<BaseObjectTypeState>()
                .Single(node => node.NodeId == new NodeId(1, 2));
            references.Clear();
            owner.GetReferences(targetContext, references);
            IReference remote = references.Single(reference => reference.TargetId.ServerIndex != 0);
            Assert.That(remote.TargetId.ServerIndex, Is.EqualTo(2));
            Assert.That(targetContext.ServerUris.GetString(remote.TargetId.ServerIndex),
                Is.EqualTo("urn:test:remote:first"));
            Assert.That(remote.TargetId.ToString(), Is.EqualTo("svr=2;nsu=urn:test:b;i=42"));
            Assert.That(combined.OfType<BaseVariableState>().Single().WrappedValue.TryGetValue(
                out ExpandedNodeId importedValue), Is.True);
            Assert.That(importedValue.ServerIndex, Is.EqualTo(2));
            Assert.That(importedValue.NamespaceIndex, Is.EqualTo(2));
            Assert.That(targetContext.ServerUris.GetString(importedValue.ServerIndex),
                Is.EqualTo("urn:test:remote:first"));
            Assert.That(Serialize(a), Is.EqualTo(originalA));
            Assert.That(Serialize(b), Is.EqualTo(originalB));
            Assert.That(context.NamespaceUris.ToArray(), Is.EqualTo(namespaces));
            Assert.That(context.ServerUris.ToArray(), Is.EqualTo(servers));
        }

        [TestCase("String", false)]
        [TestCase("String", true)]
        [TestCase("StringArray", false)]
        [TestCase("StringArray", true)]
        [TestCase("Variants", false)]
        [TestCase("Variants", true)]
        [TestCase("DataValue", false)]
        [TestCase("DataValue", true)]
        [TestCase("DataValueArray", false)]
        [TestCase("DataValueArray", true)]
        [TestCase("Matrix", false)]
        [TestCase("Matrix", true)]
        [TestCase("Structure", false)]
        [TestCase("Structure", true)]
        [TestCase("Optional", false)]
        [TestCase("Optional", true)]
        [TestCase("Union", false)]
        [TestCase("Union", true)]
        public void IndependentReviewStringNullnessSurvivesValueImport(string kind, bool nil)
        {
            foreach (bool changed in s_reviewChangedTables)
            {
                (string xml, ServiceMessageContext? context, string elementName) =
                    ReviewStringPayload(kind, nil);
                UANodeSet b = CreateValuePartition(xml);
                byte[] original = Serialize(b);
                System.Xml.XmlElement source = b.Items!.OfType<UAVariable>().Single().Value!;
                System.Xml.XmlElement[] originalStrings = ReviewStringElements(source, elementName);
                Assert.That(originalStrings, Is.Not.Empty);
                Assert.That(ReviewReadString(originalStrings[0]), nil ? Is.Null : Is.Empty);
                using WotDocumentSet documents = ReviewDocuments(changed);
                WotNodeSetConverterOptions options = IndependentOptions();
                options.ValueEncodingContext = context;
                string[] namespaces = context?.NamespaceUris.ToArray() ?? [];
                ExpandedNodeId[] knownTypes = context?.Factory.KnownTypeIds.ToArray() ?? [];

                WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                    documents, changed ? [CreatePartition("urn:test:a"), b] : [b], options);

                Assert.That(result.Success, Is.True, $"{kind}/{nil}/{changed}: {Describe(result)}");
                System.Xml.XmlElement[] values = ReviewStringElements(
                    result.Value!.Items!.OfType<UAVariable>().Single().Value!, elementName);
                Assert.That(values, Has.Length.EqualTo(originalStrings.Length));
                Assert.That(ReviewReadString(values[0]), nil ? Is.Null : Is.Empty);
                for (int index = 0; index < values.Length; index++)
                {
                    Assert.That(ReviewReadString(values[index]), Is.EqualTo(ReviewReadString(originalStrings[index])));
                    Assert.That(values[index].InnerText, Is.EqualTo(originalStrings[index].InnerText));
                }
                Assert.That(Serialize(b), Is.EqualTo(original));
                if (context is not null)
                {
                    Assert.That(context.NamespaceUris.ToArray(), Is.EqualTo(namespaces));
                    Assert.That(context.Factory.KnownTypeIds, Is.EquivalentTo(knownTypes));
                }
            }
        }

        [Test]
        public void IndependentReviewOrdinaryStringDecodingRemainsCompatible()
        {
            UANodeSet source = CreateValuePartition(ReviewNullableString(nil: true));
            System.Xml.XmlElement value = source.Items!.OfType<UAVariable>().Single().Value!;
            Assert.That(ReviewReadString(ReviewStringElements(value, "String").Single()), Is.Null);
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            using var decoder = new XmlDecoder(value, context);

            Variant decoded = decoder.ReadVariant(null);

            Assert.That(decoded.TypeInfo, Is.EqualTo(TypeInfo.Scalars.String));
            Assert.That(decoded.TryGetValue(out string? text), Is.True);
            Assert.That(text, Is.Empty);
        }

        [Test]
        public void IndependentReviewKnownValueIndexesAreValidatedWithoutRelocation(
            [Values("NodeId", "ExpandedServer", "QualifiedName", "Argument", "Variants", "DataValue", "VariableType")]
            string kind,
            [Values(false, true)] bool declared,
            [Values(false, true)] bool changed)
        {
            int index = declared ? 1 : 9;
            string node = "<uax:NodeId><uax:Identifier>ns=" +
                index.ToString(CultureInfo.InvariantCulture) + ";i=42</uax:Identifier></uax:NodeId>";
            string xml = kind switch
            {
                "ExpandedServer" => ReviewExpandedXml(declared ? "ns=1;i=42" : "svr=9;ns=1;i=42"),
                "QualifiedName" => "<uax:QualifiedName><uax:NamespaceIndex>" +
                    index.ToString(CultureInfo.InvariantCulture) + "</uax:NamespaceIndex>" +
                    "<uax:Name>Value</uax:Name></uax:QualifiedName>",
                "Argument" => "<uax:ExtensionObject><uax:TypeId><uax:Identifier>i=297</uax:Identifier>" +
                    "</uax:TypeId><uax:Body><uax:Argument><uax:DataType><uax:Identifier>ns=" +
                    index.ToString(CultureInfo.InvariantCulture) + ";i=42</uax:Identifier></uax:DataType>" +
                    "</uax:Argument></uax:Body></uax:ExtensionObject>",
                "Variants" => "<uax:ListOfVariant><uax:Variant><uax:Value>" + node +
                    "</uax:Value></uax:Variant></uax:ListOfVariant>",
                "DataValue" => ReviewDataValue(node),
                _ => node
            };
            UANodeSet b = CreateValuePartition(xml);
            if (kind == "VariableType")
            {
                b.Items![1] = new UAVariableType
                {
                    NodeId = "ns=1;i=10",
                    BrowseName = "1:Value",
                    Value = ((UAVariable)b.Items[1]).Value
                };
            }
            byte[] original = Serialize(b);
            using WotDocumentSet documents = ReviewDocuments(changed);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                documents, changed ? [CreatePartition("urn:test:a"), b] : [b], IndependentOptions());

            Assert.That(result.Success, Is.EqualTo(declared), Describe(result));
            Assert.That(Serialize(b), Is.EqualTo(original));
            if (!declared)
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.NamespaceRebaseUnsupported &&
                    diagnostic.Location?.NodeId == "ns=1;i=10" &&
                    diagnostic.Location.Reference == "b"), Is.True, Describe(result));
                return;
            }
            UANode converted = result.Value!.Items!.Single(node => node.NodeId ==
                (changed ? "ns=2;i=10" : "ns=1;i=10"));
            System.Xml.XmlElement value = converted is UAVariable variable
                ? variable.Value!
                : ((UAVariableType)converted).Value!;
            string? actual = kind switch
            {
                "QualifiedName" => ValueText(value, "NamespaceIndex"),
                "Argument" => ValueText(value, "DataType", "Identifier"),
                _ => ValueText(value, "Identifier")
            };
            Assert.That(actual,
                Is.EqualTo(kind == "QualifiedName"
                    ? (changed ? "2" : "1")
                    : (changed ? "ns=2;i=42" : "ns=1;i=42")));
        }

        [TestCase("binary", false)]
        [TestCase("binary", true)]
        [TestCase("xml", false)]
        [TestCase("xml", true)]
        [TestCase("XmlElement", false)]
        public void IndependentReviewOpaqueValuesKeepTheirUnchangedContract(string kind, bool invalidOuterIdentity)
        {
            string xml = kind == "XmlElement"
                ? "<uax:XmlElement><custom xmlns=\"urn:opaque\">ns=9;svr=9</custom></uax:XmlElement>"
                : "<uax:ExtensionObject><uax:TypeId><uax:Identifier>" +
                    (invalidOuterIdentity ? "ns=9;i=200" : "ns=1;i=200") +
                    "</uax:Identifier></uax:TypeId><uax:Body>" +
                    (kind == "binary"
                        ? "<uax:ByteString>AQI=</uax:ByteString>"
                        : "<custom xmlns=\"urn:opaque\">ns=9;svr=9</custom>") +
                    "</uax:Body></uax:ExtensionObject>";
            foreach (bool changed in s_reviewChangedTables)
            {
                UANodeSet b = CreateValuePartition(xml);
                byte[] original = Serialize(b);
                using var serialized = new MemoryStream(original);
                string value = UANodeSet.Read(serialized)!.Items!.OfType<UAVariable>().Single().Value!.OuterXml;
                using WotDocumentSet documents = ReviewDocuments(changed);

                WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                    documents, changed ? [CreatePartition("urn:test:a"), b] : [b], IndependentOptions());

                Assert.That(result.Success, Is.EqualTo(!changed && !invalidOuterIdentity), Describe(result));
                Assert.That(Serialize(b), Is.EqualTo(original));
                if (result.Success)
                {
                    Assert.That(result.Value!.Items!.OfType<UAVariable>().Single().Value!.OuterXml, Is.EqualTo(value));
                }
                else
                {
                    Assert.That(result.Value, Is.Null);
                    Assert.That(result.Diagnostics.Any(diagnostic =>
                        diagnostic.Code == WotDiagnosticCode.NamespaceRebaseUnsupported &&
                        diagnostic.Location?.Reference == "b"), Is.True, Describe(result));
                }
            }
        }

        private static WotDocumentSet ReviewDocuments(bool changed)
        {
            return new WotDocumentSet("b", changed
                ? [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:b"))]
                : [new("b", CreateModel("urn:test:b"))]);
        }

        private static string ReviewExpandedXml(string identifier)
        {
            return "<uax:ExpandedNodeId><uax:Identifier>" + identifier +
                "</uax:Identifier></uax:ExpandedNodeId>";
        }

        private static ExpandedNodeId ReviewReadExpanded(System.Xml.XmlElement value, UANodeSet source)
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            context.NamespaceUris = new NamespaceTable([Namespaces.OpcUa, .. source.NamespaceUris ?? []]);
            context.ServerUris = new StringTable(["urn:test:local", .. source.ServerUris ?? []]);
            var element = (System.Xml.XmlElement)value.SelectSingleNode(
                "descendant-or-self::*[local-name()='ExpandedNodeId']")!;
            using var decoder = new XmlDecoder(element, context);
            decoder.SetMappingTables(context.NamespaceUris, context.ServerUris);
            decoder.PushNamespace(element.NamespaceURI);
            return decoder.ReadExpandedNodeId(element.LocalName);
        }

        private static SystemContext ReviewSystemContext(string uri)
        {
            return new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable([Namespaces.OpcUa, uri]),
                ServerUris = new StringTable(["urn:test:local", "urn:test:remote:first"]),
                EncodeableFactory = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()).Factory
            };
        }

        private static UANodeSet ReviewExportRemote(string uri)
        {
            SystemContext context = ReviewSystemContext(uri);
            var root = new BaseObjectTypeState
            {
                NodeId = new NodeId(1, 1),
                BrowseName = new QualifiedName("Root", 1),
                DisplayName = new LocalizedText("Root"),
                SuperTypeId = new NodeId(58)
            };
            root.AddReference(new NodeId(35), false, new ExpandedNodeId(new NodeId(42, 1), null, 1));
            var result = new UANodeSet { Models = [new ModelTableEntry { ModelUri = uri }] };
            result.Export(context, root);
            return result;
        }

        private static (string Xml, ServiceMessageContext? Context, string ElementName) ReviewStringPayload(
            string kind,
            bool nil)
        {
            string scalar = ReviewNullableString(nil);
            const string literal = "<uax:String> ns=1;i=42 </uax:String>";
            string xml = kind switch
            {
                "String" => scalar,
                "StringArray" => "<uax:ListOfString>" + scalar + literal + "</uax:ListOfString>",
                "Variants" => "<uax:ListOfVariant><uax:Variant><uax:Value>" + scalar +
                    "</uax:Value></uax:Variant><uax:Variant><uax:Value>" + literal +
                    "</uax:Value></uax:Variant></uax:ListOfVariant>",
                "DataValue" => ReviewDataValue(scalar),
                "DataValueArray" => "<uax:ListOfDataValue>" + ReviewDataValue(scalar) +
                    ReviewDataValue(literal) + "</uax:ListOfDataValue>",
                "Matrix" => "<uax:Matrix><uax:Dimensions><uax:Int32>1</uax:Int32><uax:Int32>2</uax:Int32>" +
                    "</uax:Dimensions><uax:Elements>" + scalar + literal + "</uax:Elements></uax:Matrix>",
                _ => string.Empty
            };
            if (xml.Length != 0)
            {
                return (xml, null, "String");
            }
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            context.NamespaceUris.Append("urn:test:b");
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
                        Name = "Text",
                        DataType = new NodeId(12),
                        ValueRank = -1,
                        IsOptional = kind == "Optional"
                    }
                ]
            };
            var name = new XmlQualifiedName("ReviewSample", "urn:test:b");
            var id = new ExpandedNodeId(700, "urn:test:b");
            var binaryId = new ExpandedNodeId(701, "urn:test:b");
            var xmlId = new ExpandedNodeId(702, "urn:test:b");
            var fields = new Dictionary<string, BuiltInType> { ["Text"] = BuiltInType.String };
            Structure structure = kind switch
            {
                "Optional" => new StructureWithOptionalFields(name, id, binaryId, xmlId, definition, fields),
                "Union" => new Opc.Ua.Encoders.Union(name, id, binaryId, xmlId, definition, fields),
                _ => new Structure(name, id, binaryId, xmlId, definition, fields)
            };
            structure["Text"] = "seed";
            context.Factory.Builder.AddEncodeableType(structure).Commit();
            using var encoder = new XmlEncoder(context);
            encoder.WriteVariantValue(null, new ExtensionObject(structure));
            System.Xml.XmlElement encoded = WotTestData.ParseValue(encoder.CloseAndReturnText()!);
            System.Xml.XmlElement text = ReviewStringElements(encoded, "String").Single();
            text.InnerText = string.Empty;
            if (nil)
            {
                text.SetAttribute("nil", "http://www.w3.org/2001/XMLSchema-instance", "true");
            }
            return (encoded.OuterXml, context, "String");
        }

        private static string ReviewNullableString(bool nil)
        {
            return nil
                ? "<uax:String xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:nil=\"true\"/>"
                : "<uax:String/>";
        }

        private static string ReviewDataValue(string value)
        {
            return "<uax:DataValue><uax:Value><uax:Value>" + value +
                "</uax:Value></uax:Value></uax:DataValue>";
        }

        private static System.Xml.XmlElement[] ReviewStringElements(System.Xml.XmlElement value, string name)
        {
            return value.SelectNodes("descendant-or-self::*[local-name()='" + name + "']")!
                .Cast<XmlNode>().OfType<System.Xml.XmlElement>().ToArray();
        }

        private static string? ReviewReadString(System.Xml.XmlElement value)
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            using var decoder = new XmlDecoder(value, context);
            decoder.PushNamespace(value.NamespaceURI);
            return decoder.ReadString(value.LocalName);
        }

        private static readonly bool[] s_reviewChangedTables = [false, true];
        private static readonly string[] s_reviewRemoteServers = ["urn:test:remote:first", "urn:test:remote:last"];
        private static readonly string[] s_reviewSingleRemote = ["urn:test:remote:first"];
    }
}
