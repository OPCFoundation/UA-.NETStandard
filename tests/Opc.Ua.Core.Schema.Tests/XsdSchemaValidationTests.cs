/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using NUnit.Framework;
using Opc.Ua.Schema.Xsd;

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Validation tests for generated XML Schema documents.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    public class XsdSchemaValidationTests
    {
        [Test]
        public void GeneratedStructureSchemaCompilesForTypeAndNamespaceScope()
        {
            UaTypeDescription inner = SchemaTestData.Structure(
                4102,
                "ValidatedInner",
                SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            UaTypeDescription color = SchemaTestData.Enumeration(
                4103,
                "ValidatedColor",
                ("Red", 0),
                ("Green", 1));
            UaTypeDescription outer = SchemaTestData.Structure(
                4101,
                "ValidatedOuter",
                StructureType.StructureWithOptionalFields,
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Name", SchemaTestData.BuiltIn(BuiltInType.String), optional: true),
                SchemaTestData.Field("Values", SchemaTestData.BuiltIn(BuiltInType.Double), ValueRanks.OneDimension),
                SchemaTestData.Field("Child", new NodeId(4102, SchemaTestData.TestNamespaceIndex)),
                SchemaTestData.Field("Shade", new NodeId(4103, SchemaTestData.TestNamespaceIndex)));
            DefaultSchemaProvider provider = CreateProvider(inner, color, outer);

            var typeSchema = (XmlSchemaDocument)provider.GetXmlSchema(outer);
            var namespaceSchema = (XmlSchemaDocument)provider.GetXmlSchema(
                outer,
                UaSchemaScope.Namespace);
            var typeDocument = XDocument.Parse(typeSchema.ToSchemaString());
            var namespaceDocument = XDocument.Parse(namespaceSchema.ToSchemaString());

            Assert.Multiple(() =>
            {
                Assert.That(Compile(typeSchema), Is.Empty);
                Assert.That(Compile(namespaceSchema), Is.Empty);
                Assert.That(HasComplexType(typeDocument, "ValidatedInner"), Is.True);
                Assert.That(HasSimpleType(typeDocument, "ValidatedColor"), Is.True);
                Assert.That(HasComplexType(typeDocument, "ValidatedOuter"), Is.True);
                Assert.That(HasComplexType(namespaceDocument, "ValidatedInner"), Is.True);
                Assert.That(HasSimpleType(namespaceDocument, "ValidatedColor"), Is.True);
                Assert.That(HasComplexType(namespaceDocument, "ValidatedOuter"), Is.True);
                Assert.That(Attribute(typeDocument, "Name", "minOccurs"), Is.EqualTo("0"));
                Assert.That(Attribute(typeDocument, "Values", "nillable"), Is.EqualTo("true"));
                Assert.That(Attribute(typeDocument, "Child", "type"), Is.EqualTo("tns:ValidatedInner"));
                Assert.That(Attribute(typeDocument, "Shade", "type"), Is.EqualTo("tns:ValidatedColor"));
            });
        }

        [Test]
        public void CrossNamespaceReferenceProducesImportAndForeignPrefix()
        {
            const string foreignNamespace = "http://validation.other.test.org/UA/schema";
            const ushort foreignNamespaceIndex = 7;
            UaTypeDescription foreign = CreateForeignStructure(foreignNamespace, foreignNamespaceIndex);
            UaTypeDescription outer = SchemaTestData.Structure(
                4110,
                "ValidatedCrossNamespaceOuter",
                SchemaTestData.Field("Foreign", new NodeId(4111, foreignNamespaceIndex)));
            DefaultSchemaProvider provider = CreateProvider(foreign, outer);

            var schema = (XmlSchemaDocument)provider.GetXmlSchema(outer);
            var document = XDocument.Parse(schema.ToSchemaString());

            Assert.Multiple(() =>
            {
                Assert.That(document.Descendants(Xsd("import")).Any(
                    x => (string?)x.Attribute("namespace") == foreignNamespace), Is.True);
                Assert.That(document.Root!.Attribute(XNamespace.Xmlns + "n1")!.Value, Is.EqualTo(foreignNamespace));
                Assert.That(Attribute(document, "Foreign", "type"), Is.EqualTo("n1:ValidatedForeign"));
                Assert.That(Attribute(document, "Foreign", "type"), Is.Not.EqualTo("tns:ValidatedForeign"));
            });
        }

        /// <summary>
        /// The generated schema validates what the XmlEncoder writes: the leading EncodingMask
        /// of a structure with optional fields (Part 6 5.3.6), the wrapped ua:Guid and
        /// ua:StatusCode (5.3.1.7/5.3.1.12), OptionSet bit combinations (Part 3 8.52) and
        /// standard structures from the Types.xsd namespace (Part 6 F.1).
        /// </summary>
        [Test]
        public void GeneratedSchemaValidatesXmlEncoderOutput()
        {
            UaTypeDescription flags = Describe(
                4121,
                "ValidatedFlags",
                SchemaTestData.TestNamespace,
                SchemaTestData.TestNamespaceIndex,
                new EnumDefinition
                {
                    IsOptionSet = true,
                    Fields =
                    [
                        new EnumField { Name = "Bit0", Value = 0 },
                        new EnumField { Name = "Bit2", Value = 2 }
                    ]
                });
            UaTypeDescription range = Describe(
                884,
                "Range",
                Namespaces.OpcUa,
                0,
                new StructureDefinition
                {
                    BaseDataType = DataTypeIds.Structure,
                    StructureType = StructureType.Structure,
                    Fields =
                    [
                        SchemaTestData.Field("Low", SchemaTestData.BuiltIn(BuiltInType.Double)),
                        SchemaTestData.Field("High", SchemaTestData.BuiltIn(BuiltInType.Double))
                    ]
                });
            UaTypeDescription encoded = SchemaTestData.Structure(
                4120,
                "ValidatedEncoded",
                StructureType.StructureWithOptionalFields,
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Guid)),
                SchemaTestData.Field("Status", SchemaTestData.BuiltIn(BuiltInType.StatusCode)),
                SchemaTestData.Field("Note", SchemaTestData.BuiltIn(BuiltInType.String), optional: true),
                SchemaTestData.Field("Flags", new NodeId(4121, SchemaTestData.TestNamespaceIndex)),
                SchemaTestData.Field("Limits", DataTypeIds.Range));
            DefaultSchemaProvider provider = CreateProvider(flags, range, encoded);
            var schema = (XmlSchemaDocument)provider.GetXmlSchema(encoded);

            var context = ServiceMessageContext.Create(null);
            var encoder = new XmlEncoder(
                new XmlQualifiedName("ValidatedEncoded", SchemaTestData.TestNamespace),
                null!,
                context);
            encoder.PushNamespace(SchemaTestData.TestNamespace);
            encoder.WriteEncodingMask(1);
            encoder.WriteGuid("Id", Uuid.NewUuid());
            encoder.WriteStatusCode("Status", StatusCodes.BadUnexpectedError);
            encoder.WriteString("Note", "note");
            encoder.WriteUInt32("Flags", 5);
            encoder.WriteEncodeable("Limits", new Range { Low = 1.0, High = 2.0 });
            encoder.PopNamespace();
            string xml = encoder.CloseAndReturnText()!;

            var errors = new List<string>();
            var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema };
            settings.ValidationEventHandler += (_, e) => errors.Add(e.Severity + ": " + e.Message);
            AddSchema(settings.Schemas, UaTypesNamespace, kUaTypesStub);
            AddSchema(settings.Schemas, schema.TargetNamespace, schema.ToSchemaString());
            using (var reader = XmlReader.Create(new StringReader(xml), settings))
            {
                while (reader.Read())
                {
                }
            }

            Assert.That(errors, Is.Empty, xml + "\n" + schema.ToSchemaString());
        }

        /// <summary>
        /// A4-1: in a structure with subtyped values IsOptional means AllowSubTypes (Part 3
        /// 8.51); the XmlEncoder writes no EncodingMask and every field, so the schema has
        /// neither an EncodingMask nor optional fields.
        /// </summary>
        [Test]
        public void SubtypedValuesStructureHasNoEncodingMask()
        {
            UaTypeDescription subtyped = SchemaTestData.Structure(
                4130,
                "ValidatedSubtyped",
                StructureType.StructureWithSubtypedValues,
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Payload", SchemaTestData.BuiltIn(BuiltInType.Int32), optional: true));
            DefaultSchemaProvider provider = CreateProvider(subtyped);
            var schema = (XmlSchemaDocument)provider.GetXmlSchema(subtyped);
            var document = XDocument.Parse(schema.ToSchemaString());

            var context = ServiceMessageContext.Create(null);
            var encoder = new XmlEncoder(
                new XmlQualifiedName("ValidatedSubtyped", SchemaTestData.TestNamespace),
                null!,
                context);
            encoder.PushNamespace(SchemaTestData.TestNamespace);
            encoder.WriteInt32("Id", 1);
            encoder.WriteInt32("Payload", 2);
            encoder.PopNamespace();
            string xml = encoder.CloseAndReturnText()!;

            var errors = new List<string>();
            var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema };
            settings.ValidationEventHandler += (_, e) => errors.Add(e.Severity + ": " + e.Message);
            AddSchema(settings.Schemas, UaTypesNamespace, CreateStubSchema(UaTypesNamespace));
            AddSchema(settings.Schemas, schema.TargetNamespace, schema.ToSchemaString());
            using (var reader = XmlReader.Create(new StringReader(xml), settings))
            {
                while (reader.Read())
                {
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    document.Descendants(Xsd("element")).Any(x => (string?)x.Attribute("name") == "EncodingMask"),
                    Is.False);
                Assert.That(Attribute(document, "Payload", "minOccurs"), Is.Not.EqualTo("0"));
                Assert.That(errors, Is.Empty, xml + "\n" + schema.ToSchemaString());
            });
        }

        private static UaTypeDescription Describe(
            uint id,
            string name,
            string namespaceUri,
            ushort namespaceIndex,
            DataTypeDefinition definition)
        {
            return new UaTypeDescription(
                new ExpandedNodeId(new NodeId(id, namespaceIndex)),
                new QualifiedName(name, namespaceIndex),
                definition,
                namespaceUri);
        }

        /// <summary>
        /// The subset of the standard Types.xsd used by <see cref="GeneratedSchemaValidatesXmlEncoderOutput"/>.
        /// </summary>
        private const string kUaTypesStub =
            "<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" " +
            "xmlns:ua=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" " +
            "targetNamespace=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" elementFormDefault=\"qualified\">" +
            "<xs:complexType name=\"Guid\"><xs:sequence>" +
            "<xs:element name=\"String\" type=\"xs:string\" minOccurs=\"0\" nillable=\"true\"/>" +
            "</xs:sequence></xs:complexType>" +
            "<xs:complexType name=\"StatusCode\"><xs:sequence>" +
            "<xs:element name=\"Code\" type=\"xs:unsignedInt\" minOccurs=\"0\"/>" +
            "</xs:sequence></xs:complexType>" +
            "<xs:complexType name=\"Range\"><xs:sequence>" +
            "<xs:element name=\"Low\" type=\"xs:double\" minOccurs=\"0\"/>" +
            "<xs:element name=\"High\" type=\"xs:double\" minOccurs=\"0\"/>" +
            "</xs:sequence></xs:complexType>" +
            "</xs:schema>";

        private static DefaultSchemaProvider CreateProvider(params UaTypeDescription[] types)
        {
            var registry = new DataTypeDefinitionRegistry();
            foreach (UaTypeDescription type in types)
            {
                registry.Add(type);
            }
            return new DefaultSchemaProvider(registry, [new XsdSchemaGenerator()]);
        }

        private static UaTypeDescription CreateForeignStructure(string namespaceUri, ushort namespaceIndex)
        {
            var definition = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields =
                [
                    SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32))
                ]
            };
            return new UaTypeDescription(
                new ExpandedNodeId(new NodeId(4111, namespaceIndex)),
                new QualifiedName("ValidatedForeign", namespaceIndex),
                definition,
                namespaceUri);
        }

        private static List<string> Compile(XmlSchemaDocument document)
        {
            var errors = new List<string>();
            var set = new XmlSchemaSet();
            set.ValidationEventHandler += (_, e) => errors.Add(e.Severity + ": " + e.Message);

            // The generated schema always imports the standard UA Types namespace. The validation fixtures use
            // built-ins that are mapped to XML Schema primitives, so an empty in-memory stub keeps the compile offline.
            AddSchema(set, UaTypesNamespace, CreateStubSchema(UaTypesNamespace));
            AddSchema(set, document.TargetNamespace, document.ToSchemaString());
            set.Compile();

            if (!set.IsCompiled)
            {
                errors.Add("The XML schema set was not compiled.");
            }

            if (set.Count == 0)
            {
                errors.Add("The XML schema set does not contain compiled schemas.");
            }

            return errors;
        }

        private static void AddSchema(XmlSchemaSet set, string targetNamespace, string schemaText)
        {
            using var reader = XmlReader.Create(new StringReader(schemaText));
            set.Add(targetNamespace, reader);
        }

        private static string CreateStubSchema(string targetNamespace)
        {
            return "<xs:schema xmlns:xs=\"" +
                XmlSchema.Namespace +
                "\" targetNamespace=\"" +
                targetNamespace +
                "\" elementFormDefault=\"qualified\" />";
        }

        private static bool HasComplexType(XDocument document, string name)
        {
            return document.Descendants(Xsd("complexType")).Any(x => (string?)x.Attribute("name") == name);
        }

        private static bool HasSimpleType(XDocument document, string name)
        {
            return document.Descendants(Xsd("simpleType")).Any(x => (string?)x.Attribute("name") == name);
        }

        private static string? Attribute(XDocument document, string elementName, string attributeName)
        {
            return document
                .Descendants(Xsd("element"))
                .First(x => (string?)x.Attribute("name") == elementName)
                .Attribute(attributeName)?
                .Value;
        }

        private static XName Xsd(string name)
        {
            return XName.Get(name, XmlSchema.Namespace);
        }

        private const string UaTypesNamespace = "http://opcfoundation.org/UA/2008/02/Types.xsd";
    }
}
