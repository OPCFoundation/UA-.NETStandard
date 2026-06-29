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
    /// Validation tests for generated OPC Binary schema documents.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    public class BsdSchemaValidationTests
    {
        [Test]
        public void GeneratedBinarySchemasAreStructurallyValidForStructureEnumAndUnion()
        {
            UaTypeDescription inner = SchemaTestData.Structure(
                4202,
                "ValidatedBsdInner",
                SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            UaTypeDescription color = SchemaTestData.Enumeration(
                4203,
                "ValidatedBsdColor",
                ("Red", 0),
                ("Green", 1));
            UaTypeDescription outer = SchemaTestData.Structure(
                4201,
                "ValidatedBsdOuter",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Name", SchemaTestData.BuiltIn(BuiltInType.String), optional: true),
                SchemaTestData.Field("Values", SchemaTestData.BuiltIn(BuiltInType.Double), ValueRanks.OneDimension),
                SchemaTestData.Field("Child", new NodeId(4202, SchemaTestData.TestNamespaceIndex)),
                SchemaTestData.Field("Shade", new NodeId(4203, SchemaTestData.TestNamespaceIndex)));
            UaTypeDescription choice = SchemaTestData.Union(
                4210,
                "ValidatedBsdChoice",
                SchemaTestData.Field("Number", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Text", SchemaTestData.BuiltIn(BuiltInType.String)));
            DefaultSchemaProvider provider = CreateProvider(inner, color, outer, choice);

            var structureSchema = (BinarySchemaDocument)provider.GetBinarySchema(outer);
            var enumSchema = (BinarySchemaDocument)provider.GetBinarySchema(color);
            var unionSchema = (BinarySchemaDocument)provider.GetBinarySchema(choice);
            var structureDocument = XDocument.Parse(structureSchema.ToSchemaString());
            var enumDocument = XDocument.Parse(enumSchema.ToSchemaString());
            var unionDocument = XDocument.Parse(unionSchema.ToSchemaString());

            Assert.Multiple(() =>
            {
                Assert.That(structureSchema.Dictionary.Items, Has.Length.EqualTo(3));
                Assert.That(enumSchema.Dictionary.Items, Has.Length.EqualTo(1));
                Assert.That(unionSchema.Dictionary.Items, Has.Length.EqualTo(1));
                Assert.That(structureDocument.Descendants(Opc("Import")).Any(
                    x => (string?)x.Attribute("Namespace") == "http://opcfoundation.org/UA/"), Is.True);
                Assert.That(HasType(structureDocument, "StructuredType", "ValidatedBsdInner"), Is.True);
                Assert.That(HasType(structureDocument, "EnumeratedType", "ValidatedBsdColor"), Is.True);
                Assert.That(HasType(structureDocument, "StructuredType", "ValidatedBsdOuter"), Is.True);
                Assert.That(HasType(enumDocument, "EnumeratedType", "ValidatedBsdColor"), Is.True);
                Assert.That(EnumeratedValue(enumDocument, "Red"), Is.EqualTo("0"));
                Assert.That(EnumeratedValue(enumDocument, "Green"), Is.EqualTo("1"));
                Assert.That(FieldAttribute(structureDocument, "NameSpecified", "TypeName"), Is.EqualTo("opc:Bit"));
                Assert.That(FieldAttribute(structureDocument, "Reserved1", "Length"), Is.EqualTo("31"));
                Assert.That(FieldAttribute(structureDocument, "Name", "SwitchField"), Is.EqualTo("NameSpecified"));
                Assert.That(FieldAttribute(structureDocument, "Values", "LengthField"), Is.EqualTo("NoOfValues"));
                Assert.That(FieldAttribute(structureDocument, "Child", "TypeName"), Is.EqualTo("tns:ValidatedBsdInner"));
                Assert.That(FieldAttribute(structureDocument, "Shade", "TypeName"), Is.EqualTo("tns:ValidatedBsdColor"));
                Assert.That(FieldAttribute(unionDocument, "SwitchField", "TypeName"), Is.EqualTo("opc:UInt32"));
                Assert.That(FieldAttribute(unionDocument, "Number", "SwitchField"), Is.EqualTo("SwitchField"));
                Assert.That(FieldAttribute(unionDocument, "Number", "SwitchValue"), Is.EqualTo("1"));
                Assert.That(FieldAttribute(unionDocument, "Text", "SwitchValue"), Is.EqualTo("2"));
            });
        }

        [Test]
        public void CrossNamespaceReferenceProducesImportAndForeignTypeName()
        {
            const string foreignNamespace = "http://validation.other.test.org/UA/schema";
            const ushort foreignNamespaceIndex = 8;
            UaTypeDescription foreign = CreateForeignStructure(foreignNamespace, foreignNamespaceIndex);
            UaTypeDescription outer = SchemaTestData.Structure(
                4220,
                "ValidatedBsdCrossNamespaceOuter",
                SchemaTestData.Field("Foreign", new NodeId(4221, foreignNamespaceIndex)));
            DefaultSchemaProvider provider = CreateProvider(foreign, outer);

            var schema = (BinarySchemaDocument)provider.GetBinarySchema(outer);
            var document = XDocument.Parse(schema.ToSchemaString());

            Assert.Multiple(() =>
            {
                Assert.That(document.Descendants(Opc("Import")).Any(
                    x => (string?)x.Attribute("Namespace") == foreignNamespace), Is.True);
                Assert.That(document.Root!.Attribute(XNamespace.Xmlns + "n1")!.Value, Is.EqualTo(foreignNamespace));
                Assert.That(FieldAttribute(document, "Foreign", "TypeName"), Is.EqualTo("n1:ValidatedBsdForeign"));
                Assert.That(FieldAttribute(document, "Foreign", "TypeName"), Is.Not.EqualTo("tns:ValidatedBsdForeign"));
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
                new ExpandedNodeId(new NodeId(4221, namespaceIndex)),
                new QualifiedName("ValidatedBsdForeign", namespaceIndex),
                definition,
                namespaceUri);
        }

        /// <summary>
        /// BinarySchemaValidator resolves imports by namespace but the generated standard UA import has no location.
        /// Keeping this test offline is therefore more deterministic with structural XML assertions over the emitted BSD.
        /// </summary>
        /// <param name="document"></param>
        /// <param name="typeElement"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        private static bool HasType(XDocument document, string typeElement, string name)
        {
            return document.Descendants(Opc(typeElement)).Any(x => (string?)x.Attribute("Name") == name);
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
