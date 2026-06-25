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
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Name", SchemaTestData.BuiltIn(BuiltInType.String), optional: true),
                SchemaTestData.Field("Values", SchemaTestData.BuiltIn(BuiltInType.Double), ValueRanks.OneDimension),
                SchemaTestData.Field("Child", new NodeId(4102, SchemaTestData.TestNamespaceIndex)),
                SchemaTestData.Field("Shade", new NodeId(4103, SchemaTestData.TestNamespaceIndex)));
            DefaultSchemaProvider provider = CreateProvider(inner, color, outer);

            XmlSchemaDocument typeSchema = (XmlSchemaDocument)provider.GetXmlSchema(outer);
            XmlSchemaDocument namespaceSchema = (XmlSchemaDocument)provider.GetXmlSchema(
                outer,
                UaSchemaScope.Namespace);
            XDocument typeDocument = XDocument.Parse(typeSchema.ToSchemaString());
            XDocument namespaceDocument = XDocument.Parse(namespaceSchema.ToSchemaString());

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

            XmlSchemaDocument schema = (XmlSchemaDocument)provider.GetXmlSchema(outer);
            XDocument document = XDocument.Parse(schema.ToSchemaString());

            Assert.Multiple(() =>
            {
                Assert.That(document.Descendants(Xsd("import")).Any(
                    x => (string?)x.Attribute("namespace") == foreignNamespace), Is.True);
                Assert.That(document.Root!.Attribute(XNamespace.Xmlns + "n1")!.Value, Is.EqualTo(foreignNamespace));
                Assert.That(Attribute(document, "Foreign", "type"), Is.EqualTo("n1:ValidatedForeign"));
                Assert.That(Attribute(document, "Foreign", "type"), Is.Not.EqualTo("tns:ValidatedForeign"));
            });
        }

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
            return "<xs:schema xmlns:xs=\"" + XmlSchema.Namespace + "\" targetNamespace=\"" + targetNamespace +
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
                .Attribute(attributeName)
                ?.Value;
        }

        private static XName Xsd(string name)
        {
            return XName.Get(name, XmlSchema.Namespace);
        }

        private const string UaTypesNamespace = "http://opcfoundation.org/UA/2008/02/Types.xsd";
    }
}
