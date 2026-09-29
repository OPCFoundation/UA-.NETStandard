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
using System.IO;
using System.Text;
using NUnit.Framework;
using Opc.Ua.SchemaRegistry.Formats;
using Opc.Ua.XRegistry;

namespace Opc.Ua.SchemaRegistry.Tests
{
    /// <summary>
    /// Native schema content is exchanged as UA Binary; a decoded copy must serialize to the same document.
    /// </summary>
    [TestFixture]
    [Category("SchemaRegistry")]
    public sealed class SchemaContentBinaryTests
    {
        private static readonly Lazy<ServiceMessageContext> s_context = new(CreateContext);

        [TestCase("{\"type\":\"object\",\"properties\":{\"v\":{\"type\":\"number\",\"minimum\":0}}}")]
        [TestCase("{\"$defs\":{\"a\":true},\"items\":false,\"x-unit\":\"C\",\"default\":null}")]
        [TestCase("true")]
        public void JsonSchemaSurvivesBinaryEncoding(string source)
        {
            AssertSurvivesBinaryEncoding(new JsonSchemaFormatProvider(), Encoding.UTF8.GetBytes(source));
        }

        [TestCase("{\"type\":\"record\",\"name\":\"R\",\"fields\":[{\"name\":\"a\",\"type\":\"long\"}]}")]
        [TestCase("{\"type\":\"record\",\"name\":\"N\",\"fields\":[{\"name\":\"next\",\"type\":[\"null\",\"N\"]," +
            "\"default\":null},{\"name\":\"m\",\"type\":{\"type\":\"map\",\"values\":\"int\"}}]}")]
        [TestCase("{\"type\":\"fixed\",\"name\":\"D\",\"size\":12,\"logicalType\":\"duration\"}")]
        [TestCase("\"string\"")]
        public void AvroSchemaSurvivesBinaryEncoding(string source)
        {
            AssertSurvivesBinaryEncoding(new AvroSchemaFormatProvider(), Encoding.UTF8.GetBytes(source));
        }

        [TestCase("arrow-metadata.ipc")]
        [TestCase("arrow-families.ipc")]
        public void ArrowSchemaSurvivesBinaryEncoding(string fixture)
        {
            AssertSurvivesBinaryEncoding(new ArrowSchemaFormatProvider(),
                File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Assets", fixture)));
        }

        [Test]
        public void AbsentJsonSchemaStructureKeywordsAreNull()
        {
            var content = (JsonSchemaContentDataType)new JsonSchemaFormatProvider().Parse(
                Encoding.UTF8.GetBytes("{\"type\":\"object\"}"));
            var root = (JsonSchemaObjectDataType)content.Root;

            Assert.Multiple(() =>
            {
                Assert.That(root.Type, Is.TypeOf<RegistryStringValueDataType>());
                Assert.That(root.Const, Is.Null);
                Assert.That(root.Vocabulary, Is.Null);
                Assert.That(root.DependentRequired, Is.Null);
                Assert.That(root.Defs, Is.Null);
                Assert.That(root.Items, Is.Null);
                Assert.That(root.AllOf.Count, Is.Zero);
                Assert.That(root.Title, Is.Empty);
            });
        }

        [Test]
        public void AbsentAvroStructureMembersAreNull()
        {
            var content = (AvroSchemaContentDataType)new AvroSchemaFormatProvider().Parse(
                Encoding.UTF8.GetBytes("{\"type\":\"record\",\"name\":\"R\",\"fields\":[{\"name\":\"a\",\"type\":\"long\"}]}"));
            AvroDeclarationDataType declaration = ((AvroObjectDataType)content.Root).Declaration;
            AvroFieldDataType field = declaration.Fields[0];

            Assert.Multiple(() =>
            {
                Assert.That(declaration.Items, Is.Null);
                Assert.That(declaration.Values, Is.Null);
                Assert.That(declaration.Size, Is.Null);
                Assert.That(declaration.Precision, Is.Null);
                Assert.That(declaration.Scale, Is.Null);
                Assert.That(declaration.Default, Is.Null);
                Assert.That(field.Default, Is.Null);
                Assert.That(field.Type, Is.TypeOf<AvroNameDataType>());
            });
        }

        [TestCase("{}")]
        [TestCase("{\"type\":\"object\",\"properties\":{\"v\":{\"type\":\"number\",\"minimum\":0}}}")]
        public void EveryAbsentJsonSchemaKeywordHasItsUnusedValue(string source)
        {
            var content = (JsonSchemaContentDataType)new JsonSchemaFormatProvider().Parse(Encoding.UTF8.GetBytes(source));

            AssertAbsentFieldsAreUnused((JsonSchemaObjectDataType)content.Root);
        }

        [Test]
        public void EveryAbsentAvroMemberHasItsUnusedValue()
        {
            var content = (AvroSchemaContentDataType)new AvroSchemaFormatProvider().Parse(
                Encoding.UTF8.GetBytes("{\"type\":\"record\",\"name\":\"R\",\"fields\":[{\"name\":\"a\",\"type\":\"long\"}]}"));
            AvroDeclarationDataType declaration = ((AvroObjectDataType)content.Root).Declaration;

            AssertAbsentFieldsAreUnused(declaration);
            AssertAbsentFieldsAreUnused(declaration.Fields[0]);
        }

        private static void AssertAbsentFieldsAreUnused(RegistryRecordDataType record)
        {
            var present = new HashSet<string>(record.PresentFields.ToArray()!, StringComparer.Ordinal);
            var inherited = new HashSet<string>(["PresentFields", "AdditionalFields"], StringComparer.Ordinal);
            var checkedFields = new List<string>();
            foreach (RegistryNativeField field in RegistryNativeFields.Read(record, s_context.Value))
            {
                if (present.Contains(field.Name) || inherited.Contains(field.Name))
                {
                    continue;
                }
                checkedFields.Add(field.Name);
                Assert.That(IsUnused(field.Value), Is.True, field.Name + " = " + field.Value);
            }
            Assert.That(checkedFields, Is.Not.Empty);
        }

        private static bool IsUnused(Variant value)
        {
            if (value.TryGetValue(out string text))
            {
                return text is not null && text.Length == 0;
            }
            if (value.TryGetValue(out bool flag))
            {
                return !flag;
            }
            if (value.TryGetValue(out ExtensionObject extension))
            {
                return extension.IsNull;
            }
            if (value.TryGetValue(out ArrayOf<string> texts))
            {
                return !texts.IsNull && texts.Count == 0;
            }
            return value.TryGetValue(out ArrayOf<ExtensionObject> items) && !items.IsNull && items.Count == 0;
        }

        private static void AssertSurvivesBinaryEncoding(ISchemaFormatProvider provider, byte[] document)
        {
            SchemaContentDataType content = provider.Parse(document);
            ByteString expected = provider.Serialize(content);

            var decoded = (SchemaContentDataType)BinaryRoundTrip(content);

            Assert.Multiple(() =>
            {
                Assert.That(decoded.GetType(), Is.EqualTo(content.GetType()));
                Assert.That(decoded.IsEqual(content), Is.True);
                Assert.That(provider.Serialize(decoded), Is.EqualTo(expected));
            });
        }

        private static IEncodeable BinaryRoundTrip(IEncodeable value)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, s_context.Value, true))
            {
                encoder.WriteExtensionObject(null, new ExtensionObject(value));
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, s_context.Value, true);
            ExtensionObject decoded = decoder.ReadExtensionObject(null);
            return decoded.TryGetValue(out IEncodeable? body, s_context.Value) && body is not null
                ? body
                : throw new InvalidOperationException("The binary ExtensionObject cannot be decoded.");
        }

        private static ServiceMessageContext CreateContext()
        {
            var context = ServiceMessageContext.Create(null);
            context.NamespaceUris.GetIndexOrAppend(XRegistry.Namespaces.xRegistry);
            context.NamespaceUris.GetIndexOrAppend(Namespaces.SchemaRegistry);
            context.Factory.Builder
                .AddOpcUaXRegistry()
                .AddOpcUaSchemaRegistry()
                .Commit();
            return context;
        }
    }
}
