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

using System.Collections.Generic;
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
        [TestCase("Structure")]
        [TestCase("Optional")]
        [TestCase("Union")]
        [TestCase("Nested")]
        public void IndependentPartitionsUseTheSuppliedStructureContextWithoutMutatingIt(string kind)
        {
            using var documents = new WotDocumentSet(
                "a", [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:b"))]);
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            context.NamespaceUris.Append("urn:test:b");
            context.NamespaceUris.Append("urn:test:a");
            Structure structure = CreateValueStructure(kind);
            context.Factory.Builder.AddEncodeableType(structure).Commit();
            structure["Target"] = new NodeId(42, 1);
            if (kind == "Nested")
            {
                Structure inner = structure;
                structure = new Structure(
                    new XmlQualifiedName("Outer", "urn:test:b"),
                    new ExpandedNodeId(400, "urn:test:b"),
                    new ExpandedNodeId(401, "urn:test:b"),
                    new ExpandedNodeId(402, "urn:test:b"),
                    new StructureDefinition
                    {
                        Fields = [new StructureField { Name = "Inner", DataType = new NodeId(300, 1), ValueRank = -1 }]
                    },
                    new Dictionary<string, BuiltInType> { ["Inner"] = BuiltInType.ExtensionObject });
                context.Factory.Builder.AddEncodeableType(structure).Commit();
                structure["Inner"] = new ExtensionObject(inner);
            }
            using var encoder = new XmlEncoder(context);
            encoder.WriteVariantValue(null, new ExtensionObject(structure));
            string xml = encoder.CloseAndReturnText()!;
            UANodeSet b = CreateValuePartition(xml);
            string[] namespaces = context.NamespaceUris.ToArray();
            ExpandedNodeId[] knownTypes = context.Factory.KnownTypeIds.ToArray();
            byte[] original = Serialize(b);
            WotNodeSetConverterOptions options = IndependentOptions();
            options.ValueEncodingContext = context;

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                documents, [CreatePartition("urn:test:a"), b], options);

            Assert.That(result.Success, Is.True, Describe(result));
            System.Xml.XmlElement value = result.Value!.Items!.OfType<UAVariable>().Single().Value!;
            Assert.That(ValueText(value, "TypeId", "Identifier"),
                Is.EqualTo(kind == "Nested" ? "ns=2;i=402" : "ns=2;i=302"));
            Assert.That(ValueText(value, "Target"), Is.EqualTo("ns=2;i=42"));
            if (kind == "Optional")
            {
                Assert.That(ValueText(value, "EncodingMask"), Is.EqualTo("1"));
            }
            if (kind == "Union")
            {
                Assert.That(ValueText(value, "SwitchField"), Is.EqualTo("1"));
            }
            Assert.That(context.NamespaceUris.ToArray(), Is.EqualTo(namespaces));
            Assert.That(context.Factory.KnownTypeIds, Is.EquivalentTo(knownTypes));
            Assert.That(Serialize(b), Is.EqualTo(original));
        }

        [TestCase("NodeId")]
        [TestCase("ExpandedNodeId")]
        [TestCase("QualifiedName")]
        [TestCase("NodeIdArray")]
        [TestCase("QualifiedNameArray")]
        [TestCase("Argument")]
        [TestCase("DataValue")]
        [TestCase("NestedVariants")]
        [TestCase("Matrix")]
        public void IndependentPartitionsRebaseTypedValues(string kind)
        {
            using var documents = new WotDocumentSet(
                "a", [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:b"))]);
            UANodeSet a = CreatePartition("urn:test:a");
            UANodeSet b = CreateValuePartition(IndependentValueXml(kind));
            byte[] original = Serialize(b);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                documents, [a, b], IndependentOptions());

            Assert.That(result.Success, Is.True, Describe(result));
            System.Xml.XmlElement value = result.Value!.Items!.OfType<UAVariable>().Single().Value!;
            if (kind == "Argument")
            {
                Assert.That(ValueText(value, "TypeId", "Identifier"), Is.EqualTo("i=297"));
                Assert.That(ValueText(value, "DataType", "Identifier"), Is.EqualTo("ns=2;i=20"));
                Assert.That(ValueText(value, "Name"), Is.EqualTo("ns=1;Argument"));
                Assert.That(ValueText(value, "ValueRank"), Is.EqualTo("1"));
                Assert.That(ValueText(value, "ArrayDimensions", "UInt32"), Is.EqualTo("3"));
                Assert.That(ValueText(value, "Description", "Locale"), Is.EqualTo("de"));
                Assert.That(ValueText(value, "Description", "Text"), Is.EqualTo("ns=1;i=20"));
            }
            else
            {
                string[] identifiers = ValueTexts(value, "Identifier");
                string[] namespaceIndices = ValueTexts(value, "NamespaceIndex");
                if (kind is "NodeIdArray" or "Matrix")
                {
                    Assert.That(identifiers, Is.EqualTo(s_mixedIdentifiers));
                }
                else if (kind is not ("QualifiedName" or "QualifiedNameArray"))
                {
                    Assert.That(identifiers, Is.EqualTo(s_rebasedIdentifiers));
                }
                if (kind is "QualifiedName" or "QualifiedNameArray" or "NestedVariants")
                {
                    Assert.That(namespaceIndices, Is.EqualTo(s_rebasedNameIndexes));
                    Assert.That(ValueTexts(value, "Name"), Is.EqualTo(s_literalNames));
                }
                if (kind == "Matrix")
                {
                    Assert.That(ValueTexts(value, "Int32"), Is.EqualTo(s_matrixDimensions));
                }
                if (kind == "DataValue")
                {
                    Assert.That(ValueText(value, "StatusCode", "Code"), Is.EqualTo("1073741824"));
                }
                if (kind == "NestedVariants")
                {
                    Assert.That(ValueText(value, "String"), Is.EqualTo("ns=1;i=42"));
                }
            }
            Assert.That(Serialize(b), Is.EqualTo(original));
        }

        [TestCase("binary")]
        [TestCase("xml")]
        [TestCase("nested")]
        public void IndependentPartitionsRejectUnresolvedOpaqueValues(string kind)
        {
            using var documents = new WotDocumentSet(
                "a", [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:b"))]);
            string body = kind == "binary"
                ? "<uax:ByteString>AQI=</uax:ByteString>"
                : "<custom xmlns=\"urn:test:unknown\"><Identifier>ns=1;i=42</Identifier></custom>";
            string extension = "<uax:ExtensionObject><uax:TypeId><uax:Identifier>ns=1;i=200</uax:Identifier>" +
                "</uax:TypeId><uax:Body>" + body + "</uax:Body></uax:ExtensionObject>";
            UANodeSet b = CreateValuePartition(kind == "nested"
                ? "<uax:ListOfVariant><uax:Variant><uax:Value>" + extension +
                    "</uax:Value></uax:Variant></uax:ListOfVariant>"
                : extension);
            byte[] original = Serialize(b);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                documents, [CreatePartition("urn:test:a"), b], IndependentOptions());

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.NamespaceRebaseUnsupported &&
                diagnostic.Location?.Reference == "b" &&
                diagnostic.Location.NodeId == "ns=1;i=10"), Is.True, Describe(result));
            Assert.That(Serialize(b), Is.EqualTo(original));
        }

        [TestCase("<uax:String>ns=1;i=42</uax:String>", "ns=1;i=42")]
        [TestCase("<uax:ByteString>AQI=</uax:ByteString>", "AQI=")]
        public void IndependentPartitionsPreserveNamespaceIndependentPayloads(string xml, string expected)
        {
            using var documents = new WotDocumentSet(
                "a", [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:b"))]);
            UANodeSet b = CreateValuePartition(xml);
            b.Extensions = [WotTestData.ParseValue("<annotation xmlns=\"urn:annotation\">ns=1;i=42</annotation>")];
            byte[] original = Serialize(b);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                documents, [CreatePartition("urn:test:a"), b], IndependentOptions());

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.Items!.OfType<UAVariable>().Single().Value!.InnerText, Is.EqualTo(expected));
            Assert.That(result.Value.Extensions![0].InnerText, Is.EqualTo("ns=1;i=42"));
            Assert.That(result.Value.Extensions[0].NamespaceURI, Is.EqualTo("urn:annotation"));
            Assert.That(Serialize(b), Is.EqualTo(original));
        }

        private static UANodeSet CreateValuePartition(string xml)
        {
            UANodeSet part = CreatePartition("urn:test:b");
            part.Items =
            [
                part.Items![0],
                new UAVariable
                {
                    NodeId = "ns=1;i=10",
                    BrowseName = "1:Value",
                    ParentNodeId = "Root",
                    Value = WotTestData.ParseValue(
                        "<uax:Value xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">" +
                        xml + "</uax:Value>")
                }
            ];
            return part;
        }

        private static Structure CreateValueStructure(string kind)
        {
            var name = new XmlQualifiedName("Sample", "urn:test:b");
            var id = new ExpandedNodeId(300, "urn:test:b");
            var binaryId = new ExpandedNodeId(301, "urn:test:b");
            var xmlId = new ExpandedNodeId(302, "urn:test:b");
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
                        Name = "Target",
                        DataType = new NodeId(17),
                        ValueRank = -1,
                        IsOptional = kind == "Optional"
                    }
                ]
            };
            var fieldTypes = new Dictionary<string, BuiltInType> { ["Target"] = BuiltInType.NodeId };
            return kind switch
            {
                "Optional" => new StructureWithOptionalFields(name, id, binaryId, xmlId, definition, fieldTypes),
                "Union" => new Opc.Ua.Encoders.Union(name, id, binaryId, xmlId, definition, fieldTypes),
                _ => new Structure(name, id, binaryId, xmlId, definition, fieldTypes)
            };
        }

        private static WotNodeSetConverterOptions IndependentOptions()
        {
            return new WotNodeSetConverterOptions { DocumentSetMode = WotDocumentSetMode.IndependentReadableModels };
        }

        private static string IndependentValueXml(string kind)
        {
            const string nodeId = "<uax:NodeId><uax:Identifier>ns=1;s=literal;ns=1;i=42</uax:Identifier></uax:NodeId>";
            const string name = "<uax:QualifiedName><uax:NamespaceIndex>1</uax:NamespaceIndex>" +
                "<uax:Name>ns=1;Literal</uax:Name></uax:QualifiedName>";
            return kind switch
            {
                "NodeId" => nodeId,
                "ExpandedNodeId" => "<uax:ExpandedNodeId><uax:Identifier>ns=1;s=literal;ns=1;i=42" +
                    "</uax:Identifier></uax:ExpandedNodeId>",
                "QualifiedName" => name,
                "NodeIdArray" => "<uax:ListOfNodeId>" + nodeId +
                    "<uax:NodeId><uax:Identifier>i=33</uax:Identifier></uax:NodeId></uax:ListOfNodeId>",
                "QualifiedNameArray" => "<uax:ListOfQualifiedName>" + name + "</uax:ListOfQualifiedName>",
                "Argument" => "<uax:ListOfExtensionObject><uax:ExtensionObject>" +
                    "<uax:TypeId><uax:Identifier>i=297</uax:Identifier></uax:TypeId><uax:Body><uax:Argument>" +
                    "<uax:Name>ns=1;Argument</uax:Name><uax:DataType><uax:Identifier>ns=1;i=20</uax:Identifier>" +
                    "</uax:DataType><uax:ValueRank>1</uax:ValueRank><uax:ArrayDimensions>" +
                    "<uax:UInt32>3</uax:UInt32></uax:ArrayDimensions><uax:Description><uax:Locale>de</uax:Locale>" +
                    "<uax:Text>ns=1;i=20</uax:Text></uax:Description></uax:Argument></uax:Body>" +
                    "</uax:ExtensionObject></uax:ListOfExtensionObject>",
                "DataValue" => "<uax:DataValue><uax:Value><uax:Value>" + nodeId +
                    "</uax:Value></uax:Value><uax:StatusCode><uax:Code>1073741824</uax:Code>" +
                    "</uax:StatusCode></uax:DataValue>",
                "NestedVariants" => "<uax:ListOfVariant><uax:Variant><uax:Value>" + nodeId +
                    "</uax:Value></uax:Variant><uax:Variant><uax:Value>" + name +
                    "</uax:Value></uax:Variant><uax:Variant><uax:Value><uax:String>ns=1;i=42</uax:String>" +
                    "</uax:Value></uax:Variant></uax:ListOfVariant>",
                _ => "<uax:Matrix><uax:Dimensions><uax:Int32>1</uax:Int32><uax:Int32>2</uax:Int32>" +
                    "</uax:Dimensions><uax:Elements>" + nodeId +
                    "<uax:NodeId><uax:Identifier>i=33</uax:Identifier></uax:NodeId></uax:Elements></uax:Matrix>"
            };
        }

        private static string? ValueText(System.Xml.XmlElement value, string name, string? child = null)
        {
            return value.SelectSingleNode(".//*[local-name()='" + name + "']" +
                (child is null ? string.Empty : "/*[local-name()='" + child + "']"))?.InnerText;
        }

        private static string[] ValueTexts(System.Xml.XmlElement value, string name)
        {
            return value.SelectNodes(".//*[local-name()='" + name + "']")!
                .Cast<XmlNode>().Select(node => node.InnerText).ToArray();
        }

        private static readonly string[] s_matrixDimensions = ["1", "2"];
        private static readonly string[] s_mixedIdentifiers = ["ns=2;s=literal;ns=1;i=42", "i=33"];
        private static readonly string[] s_rebasedIdentifiers = ["ns=2;s=literal;ns=1;i=42"];
        private static readonly string[] s_rebasedNameIndexes = ["2"];
        private static readonly string[] s_literalNames = ["ns=1;Literal"];
    }
}
