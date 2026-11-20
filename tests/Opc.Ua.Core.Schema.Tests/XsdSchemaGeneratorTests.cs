// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Linq;
using System.Xml.Linq;
using System.Xml.Schema;
using NUnit.Framework;
using Opc.Ua.Schema.Xsd;

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Tests for the XML Schema generation of OPC UA data types.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    public class XsdSchemaGeneratorTests
    {
        [Test]
        public void StructureProducesElementsForBuiltInOptionalArrayAndReferencedFields()
        {
            UaTypeDescription inner = SchemaTestData.Structure(
                3102,
                "Inner",
                SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            UaTypeDescription color = SchemaTestData.Enumeration(3103, "Color", ("Red", 0), ("Green", 1));
            UaTypeDescription outer = SchemaTestData.Structure(
                3101,
                "Outer",
                StructureType.StructureWithOptionalFields,
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Name", SchemaTestData.BuiltIn(BuiltInType.String), optional: true),
                SchemaTestData.Field("Values", SchemaTestData.BuiltIn(BuiltInType.Double), ValueRanks.OneDimension),
                SchemaTestData.Field("Child", new NodeId(3102, SchemaTestData.TestNamespaceIndex)),
                SchemaTestData.Field("Shade", new NodeId(3103, SchemaTestData.TestNamespaceIndex)));
            ISchemaProvider provider = CreateProvider(inner, color, outer);

            var schema = (XmlSchemaDocument)provider.GetXmlSchema(outer);
            var document = XDocument.Parse(schema.ToSchemaString());

            Assert.Multiple(() =>
            {
                Assert.That(schema.Format, Is.EqualTo(UaSchemaFormat.Xsd));
                Assert.That(schema.MediaType, Is.EqualTo("application/xml"));
                Assert.That(Attribute(document, "Id", "type"), Is.EqualTo("xs:int"));
                Assert.That(Attribute(document, "Name", "minOccurs"), Is.EqualTo("0"));
                Assert.That(Attribute(document, "Values", "nillable"), Is.EqualTo("true"));
                Assert.That(document.ToString(), Does.Contain("maxOccurs=\"unbounded\""));
                Assert.That(Attribute(document, "Child", "type"), Is.EqualTo("tns:Inner"));
                Assert.That(Attribute(document, "Shade", "type"), Is.EqualTo("tns:Color"));
                Assert.That(() => Compile(schema), Throws.Nothing);
            });
        }

        [Test]
        public void EnumProducesStringRestrictionWithEnumerationFacets()
        {
            UaTypeDescription color = SchemaTestData.Enumeration(3103, "Color", ("Red", 0), ("Green", 1));
            ISchemaProvider provider = CreateProvider(color);

            var schema = (XmlSchemaDocument)provider.GetXmlSchema(color);
            var document = XDocument.Parse(schema.ToSchemaString());

            Assert.Multiple(() =>
            {
                Assert.That(document.ToString(), Does.Contain("simpleType name=\"Color\""));
                Assert.That(document.ToString(), Does.Contain("restriction base=\"xs:string\""));
                Assert.That(document.ToString(), Does.Contain("enumeration value=\"Red_0\""));
                Assert.That(document.ToString(), Does.Contain("enumeration value=\"Green_1\""));
                Assert.That(() => Compile(schema), Throws.Nothing);
            });
        }

        [Test]
        public void UnionProducesChoiceWithSwitchField()
        {
            UaTypeDescription choice = SchemaTestData.Union(
                3120,
                "Choice",
                SchemaTestData.Field("Number", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Text", SchemaTestData.BuiltIn(BuiltInType.String)));
            ISchemaProvider provider = CreateProvider(choice);

            var schema = (XmlSchemaDocument)provider.GetXmlSchema(choice);
            var document = XDocument.Parse(schema.ToSchemaString());

            Assert.Multiple(() =>
            {
                Assert.That(Attribute(document, "SwitchField", "type"), Is.EqualTo("xs:unsignedInt"));
                Assert.That(document.Descendants(Xsd("choice")).Any(), Is.True);
                Assert.That(Attribute(document, "Number", "minOccurs"), Is.EqualTo("0"));
                Assert.That(Attribute(document, "Text", "minOccurs"), Is.EqualTo("0"));
                Assert.That(() => Compile(schema), Throws.Nothing);
            });
        }

        [Test]
        public void NamespaceScopeIncludesAllNamespaceTypes()
        {
            UaTypeDescription inner = SchemaTestData.Structure(
                3102,
                "Inner",
                SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            UaTypeDescription outer = SchemaTestData.Structure(
                3101,
                "Outer",
                StructureType.StructureWithOptionalFields,
                SchemaTestData.Field("Child", new NodeId(3102, SchemaTestData.TestNamespaceIndex)));
            ISchemaProvider provider = CreateProvider(inner, outer);

            var schema = (XmlSchemaDocument)provider.GetXmlSchema(outer, UaSchemaScope.Namespace);
            var document = XDocument.Parse(schema.ToSchemaString());

            Assert.Multiple(() =>
            {
                Assert.That(HasComplexType(document, "Inner"), Is.True);
                Assert.That(HasComplexType(document, "Outer"), Is.True);
                Assert.That(() => Compile(schema), Throws.Nothing);
            });
        }

        [Test]
        public void CrossNamespaceReferenceProducesImportAndPrefixedType()
        {
            UaTypeDescription foreign = SchemaTestData.Structure(
                3131,
                "Inner",
                SchemaTestData.OtherNamespace,
                SchemaTestData.OtherNamespaceIndex,
                SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            UaTypeDescription outer = SchemaTestData.Structure(
                3130,
                "Outer",
                SchemaTestData.Field("Child", new NodeId(3131, SchemaTestData.OtherNamespaceIndex)));
            ISchemaProvider provider = CreateProvider(foreign, outer);

            var schema = (XmlSchemaDocument)provider.GetXmlSchema(outer);
            var document = XDocument.Parse(schema.ToSchemaString());

            Assert.Multiple(() =>
            {
                Assert.That(document.Root!.Attribute(XNamespace.Xmlns + "n1")!.Value,
                    Is.EqualTo(SchemaTestData.OtherNamespace));
                Assert.That(document.Descendants(Xsd("import")).Any(
                    x => (string?)x.Attribute("namespace") == SchemaTestData.OtherNamespace), Is.True);
                Assert.That(Attribute(document, "Child", "type"), Is.EqualTo("n1:Inner"));
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

        private static void Compile(XmlSchemaDocument document)
        {
            var set = new XmlSchemaSet();
            set.Add(document.Schema);
            set.Compile();
        }

        private static bool HasComplexType(XDocument document, string name)
        {
            return document.Descendants(Xsd("complexType")).Any(x => (string?)x.Attribute("name") == name);
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
    }
}
