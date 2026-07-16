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
using NUnit.Framework;
using Opc.Ua.Schema.Bsd;

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Tests for the OPC Binary schema generation of OPC UA data types.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    public class BsdSchemaGeneratorTests
    {
        [Test]
        public void StructureProducesFieldsForBuiltInOptionalArrayAndReferencedTypes()
        {
            UaTypeDescription inner = SchemaTestData.Structure(
                3202,
                "Inner",
                SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            UaTypeDescription color = SchemaTestData.Enumeration(3203, "Color", ("Red", 0), ("Green", 1));
            UaTypeDescription outer = SchemaTestData.Structure(
                3201,
                "Outer",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Name", SchemaTestData.BuiltIn(BuiltInType.String), optional: true),
                SchemaTestData.Field("Values", SchemaTestData.BuiltIn(BuiltInType.Double), ValueRanks.OneDimension),
                SchemaTestData.Field("Child", new NodeId(3202, SchemaTestData.TestNamespaceIndex)),
                SchemaTestData.Field("Shade", new NodeId(3203, SchemaTestData.TestNamespaceIndex)));
            DefaultSchemaProvider provider = CreateProvider(inner, color, outer);

            var schema = (BinarySchemaDocument)provider.GetBinarySchema(outer);
            var document = XDocument.Parse(schema.ToSchemaString());

            Assert.Multiple(() =>
            {
                Assert.That(schema.Format, Is.EqualTo(UaSchemaFormat.Bsd));
                Assert.That(schema.MediaType, Is.EqualTo("application/xml"));
                Assert.That(FieldAttribute(document, "Id", "TypeName"), Is.EqualTo("opc:Int32"));
                Assert.That(FieldAttribute(document, "NameSpecified", "TypeName"), Is.EqualTo("opc:Bit"));
                Assert.That(FieldAttribute(document, "Name", "SwitchField"), Is.EqualTo("NameSpecified"));
                Assert.That(FieldAttribute(document, "Name", "TypeName"), Is.EqualTo("opc:CharArray"));
                Assert.That(FieldAttribute(document, "NoOfValues", "TypeName"), Is.EqualTo("opc:Int32"));
                Assert.That(FieldAttribute(document, "Values", "LengthField"), Is.EqualTo("NoOfValues"));
                Assert.That(FieldAttribute(document, "Child", "TypeName"), Is.EqualTo("tns:Inner"));
                Assert.That(FieldAttribute(document, "Shade", "TypeName"), Is.EqualTo("tns:Color"));
            });
        }

        [Test]
        public void EnumProducesEnumeratedTypeWithValues()
        {
            UaTypeDescription color = SchemaTestData.Enumeration(3203, "Color", ("Red", 0), ("Green", 1));
            DefaultSchemaProvider provider = CreateProvider(color);

            var schema = (BinarySchemaDocument)provider.GetBinarySchema(color);
            var document = XDocument.Parse(schema.ToSchemaString());

            Assert.Multiple(() =>
            {
                Assert.That(HasType(document, "EnumeratedType", "Color"), Is.True);
                Assert.That(TypeAttribute(document, "EnumeratedType", "Color", "LengthInBits"), Is.EqualTo("32"));
                Assert.That(EnumeratedValue(document, "Red"), Is.EqualTo("0"));
                Assert.That(EnumeratedValue(document, "Green"), Is.EqualTo("1"));
            });
        }

        [Test]
        public void UnionProducesSwitchFieldAndSwitchedMembers()
        {
            UaTypeDescription choice = SchemaTestData.Union(
                3220,
                "Choice",
                SchemaTestData.Field("Number", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Text", SchemaTestData.BuiltIn(BuiltInType.String)));
            DefaultSchemaProvider provider = CreateProvider(choice);

            var schema = (BinarySchemaDocument)provider.GetBinarySchema(choice);
            var document = XDocument.Parse(schema.ToSchemaString());

            Assert.Multiple(() =>
            {
                Assert.That(FieldAttribute(document, "SwitchField", "TypeName"), Is.EqualTo("opc:UInt32"));
                Assert.That(FieldAttribute(document, "Number", "SwitchField"), Is.EqualTo("SwitchField"));
                Assert.That(FieldAttribute(document, "Number", "SwitchValue"), Is.EqualTo("1"));
                Assert.That(FieldAttribute(document, "Text", "SwitchValue"), Is.EqualTo("2"));
            });
        }

        [Test]
        public void NamespaceScopeIncludesAllNamespaceTypesAndStandardImport()
        {
            UaTypeDescription inner = SchemaTestData.Structure(
                3202,
                "Inner",
                SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            UaTypeDescription outer = SchemaTestData.Structure(
                3201,
                "Outer",
                SchemaTestData.Field("Child", new NodeId(3202, SchemaTestData.TestNamespaceIndex)));
            DefaultSchemaProvider provider = CreateProvider(inner, outer);

            var schema = (BinarySchemaDocument)provider.GetBinarySchema(outer, UaSchemaScope.Namespace);
            var document = XDocument.Parse(schema.ToSchemaString());

            Assert.Multiple(() =>
            {
                Assert.That(HasType(document, "StructuredType", "Inner"), Is.True);
                Assert.That(HasType(document, "StructuredType", "Outer"), Is.True);
                Assert.That(document.Descendants(Opc("Import")).Any(
                    x => (string?)x.Attribute("Namespace") == "http://opcfoundation.org/UA/"), Is.True);
                Assert.That(schema.Dictionary.Items, Has.Length.EqualTo(2));
            });
        }

        [Test]
        public void OptionalStructEmitsLeadingEncodingMask()
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3210,
                "OptionalType",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Note", SchemaTestData.BuiltIn(BuiltInType.String), optional: true));
            DefaultSchemaProvider provider = CreateProvider(type);

            var schema = (BinarySchemaDocument)provider.GetBinarySchema(type);
            var document = XDocument.Parse(schema.ToSchemaString());
            var fieldNames = document.Descendants(Opc("Field"))
                .Select(x => (string?)x.Attribute("Name"))
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(FieldAttribute(document, "NoteSpecified", "TypeName"), Is.EqualTo("opc:Bit"));
                Assert.That(FieldAttribute(document, "Reserved1", "TypeName"), Is.EqualTo("opc:Bit"));
                Assert.That(FieldAttribute(document, "Reserved1", "Length"), Is.EqualTo("31"));
                Assert.That(FieldAttribute(document, "Note", "SwitchField"), Is.EqualTo("NoteSpecified"));
                Assert.That(fieldNames.IndexOf("NoteSpecified"), Is.LessThan(fieldNames.IndexOf("Id")));
                Assert.That(fieldNames.IndexOf("Reserved1"), Is.LessThan(fieldNames.IndexOf("Id")));
            });
        }

        [Test]
        public void CrossNamespaceReferenceProducesImportAndPrefixedType()
        {
            UaTypeDescription foreign = SchemaTestData.Structure(
                3231,
                "Inner",
                SchemaTestData.OtherNamespace,
                SchemaTestData.OtherNamespaceIndex,
                SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            UaTypeDescription outer = SchemaTestData.Structure(
                3230,
                "Outer",
                SchemaTestData.Field("Child", new NodeId(3231, SchemaTestData.OtherNamespaceIndex)));
            DefaultSchemaProvider provider = CreateProvider(foreign, outer);

            var schema = (BinarySchemaDocument)provider.GetBinarySchema(outer);
            var document = XDocument.Parse(schema.ToSchemaString());

            Assert.Multiple(() =>
            {
                Assert.That(document.Root!.Attribute(XNamespace.Xmlns + "n1")!.Value,
                    Is.EqualTo(SchemaTestData.OtherNamespace));
                Assert.That(document.Descendants(Opc("Import")).Any(
                    x => (string?)x.Attribute("Namespace") == SchemaTestData.OtherNamespace), Is.True);
                Assert.That(FieldAttribute(document, "Child", "TypeName"), Is.EqualTo("n1:Inner"));
            });
        }

        private static DefaultSchemaProvider CreateProvider(params UaTypeDescription[] types)
        {
            var registry = new DataTypeDefinitionRegistry();
            foreach (UaTypeDescription type in types)
            {
                registry.Add(type);
            }
            return new DefaultSchemaProvider(registry, [new BsdSchemaGenerator()]);
        }

        private static bool HasType(XDocument document, string typeElement, string name)
        {
            return document.Descendants(Opc(typeElement)).Any(x => (string?)x.Attribute("Name") == name);
        }

        private static string? TypeAttribute(
            XDocument document,
            string typeElement,
            string name,
            string attributeName)
        {
            return document
                .Descendants(Opc(typeElement))
                .First(x => (string?)x.Attribute("Name") == name)
                .Attribute(attributeName)?
                .Value;
        }

        private static string? FieldAttribute(XDocument document, string name, string attributeName)
        {
            return document
                .Descendants(Opc("Field"))
                .First(x => (string?)x.Attribute("Name") == name)
                .Attribute(attributeName)?
                .Value;
        }

        private static string? EnumeratedValue(XDocument document, string name)
        {
            return document
                .Descendants(Opc("EnumeratedValue"))
                .First(x => (string?)x.Attribute("Name") == name)
                .Attribute("Value")?
                .Value;
        }

        private static XName Opc(string name)
        {
            return XName.Get(name, "http://opcfoundation.org/BinarySchema/");
        }
    }
}
