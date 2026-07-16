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

using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Schema.Bsd;
using Opc.Ua.Schema.Json;
using Opc.Ua.Schema.Xsd;

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Tests for schema document serialization and provider edge cases.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    public class SchemaSerializationTests
    {
        [TestCase(UaSchemaFormat.JsonCompact, "application/schema+json")]
        [TestCase(UaSchemaFormat.JsonVerbose, "application/schema+json")]
        [TestCase(UaSchemaFormat.Xsd, "application/xml")]
        [TestCase(UaSchemaFormat.Bsd, "application/xml")]
        public void WriteToProducesSchemaStringContentAndExpectedMediaType(
            UaSchemaFormat format,
            string mediaType)
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3401,
                "SerializableType",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Name", SchemaTestData.BuiltIn(BuiltInType.String)));
            IUaSchema schema = CreateProvider(type).CreateSchema(type, format);
            string expected = schema.ToSchemaString();

            using var stream = new MemoryStream();
            schema.WriteTo(stream);
            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string streamText = reader.ReadToEnd();
            using var writer = new StringWriter();
            schema.WriteTo(writer);

            Assert.Multiple(() =>
            {
                Assert.That(schema.MediaType, Is.EqualTo(mediaType));
                Assert.That(writer.ToString(), Is.EqualTo(expected));
                if (format is UaSchemaFormat.JsonCompact or UaSchemaFormat.JsonVerbose)
                {
                    Assert.That(streamText, Is.EqualTo(expected));
                    Assert.That(() => JsonNode.Parse(streamText), Throws.Nothing);
                }
                else
                {
                    Assert.That(NormalizeXml(streamText), Is.EqualTo(NormalizeXml(expected)));
                    Assert.That(() => XDocument.Parse(streamText), Throws.Nothing);
                }
            });
        }

        [TestCase(UaSchemaFormat.JsonCompact)]
        [TestCase(UaSchemaFormat.Xsd)]
        [TestCase(UaSchemaFormat.Bsd)]
        public void WriteToWithNullArgumentsThrowsArgumentNullException(UaSchemaFormat format)
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3401,
                "SerializableType",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            IUaSchema schema = CreateProvider(type).CreateSchema(type, format);

            Assert.Multiple(() =>
            {
                Assert.That(() => schema.WriteTo((Stream)null!), Throws.TypeOf<ArgumentNullException>());
                Assert.That(() => schema.WriteTo((TextWriter)null!), Throws.TypeOf<ArgumentNullException>());
            });
        }

        [TestCase(UaSchemaFormat.JsonCompact)]
        [TestCase(UaSchemaFormat.JsonVerbose)]
        [TestCase(UaSchemaFormat.Xsd)]
        [TestCase(UaSchemaFormat.Bsd)]
        public void UnregisteredReferencedTypeProducesValidDocument(UaSchemaFormat format)
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3401,
                "HasUnknownReference",
                SchemaTestData.Field("Unknown", new NodeId(9999, SchemaTestData.TestNamespaceIndex)));
            IUaSchema schema = CreateProvider(type).CreateSchema(type, format);
            string text = schema.ToSchemaString();

            Assert.Multiple(() =>
            {
                Assert.That(text, Is.Not.Empty);
                if (format is UaSchemaFormat.JsonCompact or UaSchemaFormat.JsonVerbose)
                {
                    Assert.That(() => JsonNode.Parse(text), Throws.Nothing);
                }
                else
                {
                    Assert.That(() => XDocument.Parse(text), Throws.Nothing);
                }
            });
        }

        [Test]
        public void TryGetSchemaReturnsFalseForUnknownTypeId()
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3401,
                "KnownType",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            DefaultSchemaProvider provider = CreateProvider(type);

            bool resolved = provider.TryGetSchema(
                new ExpandedNodeId(new NodeId(9999, SchemaTestData.TestNamespaceIndex)),
                UaSchemaFormat.JsonCompact,
                UaSchemaScope.Type,
                out IUaSchema? schema);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.False);
                Assert.That(schema, Is.Null);
            });
        }

        [Test]
        public void NullProviderAndTypeArgumentsThrowArgumentNullException()
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3401,
                "KnownType",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            DefaultSchemaProvider provider = CreateProvider(type);
            ISchemaProvider? nullProvider = null;

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new DefaultSchemaProvider(null!, []),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => new DefaultSchemaProvider(new DataTypeDefinitionRegistry(), null!),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => provider.CreateSchema(null!, UaSchemaFormat.JsonCompact),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => nullProvider!.GetJsonSchema(type),
                    Throws.TypeOf<ArgumentNullException>());
            });
        }

        private static DefaultSchemaProvider CreateProvider(params UaTypeDescription[] types)
        {
            var registry = new DataTypeDefinitionRegistry();
            foreach (UaTypeDescription type in types)
            {
                registry.Add(type);
            }

            return new DefaultSchemaProvider(
                registry,
                [new JsonSchemaGenerator(), new XsdSchemaGenerator(), new BsdSchemaGenerator()]);
        }

        private static string NormalizeXml(string xml)
        {
            return XDocument.Parse(xml).ToString(SaveOptions.DisableFormatting);
        }
    }
}
