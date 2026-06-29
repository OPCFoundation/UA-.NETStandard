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
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Name", SchemaTestData.BuiltIn(BuiltInType.String), optional: true),
                SchemaTestData.Field("Values", SchemaTestData.BuiltIn(BuiltInType.Double), ValueRanks.OneDimension),
                SchemaTestData.Field("Child", new NodeId(3102, SchemaTestData.TestNamespaceIndex)),
                SchemaTestData.Field("Shade", new NodeId(3103, SchemaTestData.TestNamespaceIndex)));
            ISchemaProvider provider = CreateProvider(inner, color, outer);

            XmlSchemaDocument schema = (XmlSchemaDocument)provider.GetXmlSchema(outer);
            XDocument document = XDocument.Parse(schema.ToSchemaString());

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

            XmlSchemaDocument schema = (XmlSchemaDocument)provider.GetXmlSchema(color);
            XDocument document = XDocument.Parse(schema.ToSchemaString());

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

            XmlSchemaDocument schema = (XmlSchemaDocument)provider.GetXmlSchema(choice);
            XDocument document = XDocument.Parse(schema.ToSchemaString());

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
                SchemaTestData.Field("Child", new NodeId(3102, SchemaTestData.TestNamespaceIndex)));
            ISchemaProvider provider = CreateProvider(inner, outer);

            XmlSchemaDocument schema = (XmlSchemaDocument)provider.GetXmlSchema(outer, UaSchemaScope.Namespace);
            XDocument document = XDocument.Parse(schema.ToSchemaString());

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

            XmlSchemaDocument schema = (XmlSchemaDocument)provider.GetXmlSchema(outer);
            XDocument document = XDocument.Parse(schema.ToSchemaString());

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
                .Attribute(attributeName)
                ?.Value;
        }

        private static XName Xsd(string name)
        {
            return XName.Get(name, XmlSchema.Namespace);
        }
    }
}
