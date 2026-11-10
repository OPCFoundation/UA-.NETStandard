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
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Schema;
using Opc.Ua.Schema.Binary;
using Opc.Ua.Schema.Xml;

namespace Opc.Ua.Types.Tests.Schema
{
    /// <summary>
    /// Limits that apply when schemas and type dictionaries are loaded:
    /// element nesting of XML schemas, the depth of dictionary import chains
    /// and the resolution of schema includes.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class SchemaLoadLimitTests
    {
        [Test]
        public void XmlSchemaWithModerateNestingValidates()
        {
            var validator = new XmlSchemaValidator(new Dictionary<string, byte[]>());
            using var stream = new MemoryStream(BuildNestedChoiceSchema(20));

            validator.Validate(stream, NullLogger.Instance);

            Assert.That(validator.SchemaSet, Is.Not.Null);
            Assert.That(validator.SchemaSet!.IsCompiled, Is.True);
        }

        [Test]
        public void XmlSchemaNestedDeeperThanTheLimitIsRejected()
        {
            var validator = new XmlSchemaValidator(new Dictionary<string, byte[]>());
            using var stream = new MemoryStream(
                BuildNestedChoiceSchema(SchemaValidator.MaxSchemaElementDepth + 10));

            Exception? error = Assert.Catch(() => validator.Validate(stream, NullLogger.Instance));

            Assert.That(error!.InnerException, Is.InstanceOf<XmlException>());
            Assert.That(validator.SchemaSet, Is.Null);
        }

        [Test]
        public void XmlSchemaImportNestedDeeperThanTheLimitIsRejected()
        {
            var imports = new Dictionary<string, byte[]>
            {
                ["urn:deep"] = BuildNestedChoiceSchema(SchemaValidator.MaxSchemaElementDepth + 10)
            };
            const string target =
                "<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" targetNamespace=\"urn:top\">" +
                "<xs:import namespace=\"urn:deep\" />" +
                "</xs:schema>";
            var validator = new XmlSchemaValidator(imports);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(target));

            Exception? error = Assert.Catch(() => validator.Validate(stream, NullLogger.Instance));

            Assert.That(error!.InnerException, Is.InstanceOf<XmlException>());
        }

        [Test]
        public void XmlSchemaFromNonSeekableStreamValidates()
        {
            var validator = new XmlSchemaValidator(new Dictionary<string, byte[]>());
            using var stream = new ForwardOnlyStream(BuildNestedChoiceSchema(5));

            validator.Validate(stream, NullLogger.Instance);

            Assert.That(validator.TargetSchema, Is.Not.Null);
            Assert.That(validator.SchemaSet!.IsCompiled, Is.True);
        }

        [Test]
        public void XmlSchemaIncludeIsNotResolved()
        {
            string directory = Path.Combine(Path.GetTempPath(), "OpcUaSchemaInclude" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string includePath = Path.Combine(directory, "included.xsd");
                File.WriteAllText(
                    includePath,
                    "<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" targetNamespace=\"urn:inc\">" +
                    "<xs:element name=\"Included\" type=\"xs:int\" />" +
                    "</xs:schema>");
                string target =
                    "<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" targetNamespace=\"urn:inc\">" +
                    "<xs:include schemaLocation=\"" + new Uri(includePath).AbsoluteUri + "\" />" +
                    "<xs:element name=\"Own\" type=\"xs:int\" />" +
                    "</xs:schema>";
                var validator = new XmlSchemaValidator(new Dictionary<string, byte[]>());
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(target));

                validator.Validate(stream, NullLogger.Instance);

                Assert.That(
                    validator.SchemaSet!.GlobalElements.Contains(new XmlQualifiedName("Own", "urn:inc")),
                    Is.True);
                Assert.That(
                    validator.SchemaSet.GlobalElements.Contains(new XmlQualifiedName("Included", "urn:inc")),
                    Is.False,
                    "the location named by xs:include must not be opened");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void BinaryDictionaryImportChainWithinTheLimitValidates()
        {
            Dictionary<string, byte[]> table = BuildImportChain(10);
            var validator = new BinarySchemaValidator(table);
            using var stream = new MemoryStream(table["urn:chain:0"]);

            validator.Validate(stream);

            Assert.That(validator.Dictionary, Is.Not.Null);
        }

        [Test]
        public void BinaryDictionaryImportChainDeeperThanTheLimitIsRejected()
        {
            Dictionary<string, byte[]> table = BuildImportChain(BinarySchemaValidator.MaxImportDepth + 10);
            var validator = new BinarySchemaValidator(table);
            using var stream = new MemoryStream(table["urn:chain:0"]);

            InvalidOperationException? error = Assert.Throws<InvalidOperationException>(
                () => validator.Validate(stream));

            Assert.That(error!.Message, Does.Contain(
                BinarySchemaValidator.MaxImportDepth.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        [Test]
        public void BinaryDictionaryImportCycleValidates()
        {
            var table = new Dictionary<string, byte[]>
            {
                ["urn:a"] = Dictionary("urn:a", "urn:b"),
                ["urn:b"] = Dictionary("urn:b", "urn:a")
            };
            var validator = new BinarySchemaValidator(table);
            using var stream = new MemoryStream(table["urn:a"]);

            Assert.That(() => validator.Validate(stream), Throws.Nothing);
        }

        private static Dictionary<string, byte[]> BuildImportChain(int length)
        {
            var table = new Dictionary<string, byte[]>();
            for (int i = 0; i < length; i++)
            {
                string ns = "urn:chain:" + i;
                table[ns] = Dictionary(ns, "urn:chain:" + (i + 1));
            }
            string last = "urn:chain:" + length;
            table[last] = Dictionary(last, null);
            return table;
        }

        private static byte[] Dictionary(string targetNamespace, string? import)
        {
            var sb = new StringBuilder();
            sb.Append("<opc:TypeDictionary xmlns:opc=\"http://opcfoundation.org/BinarySchema/\" TargetNamespace=\"")
                .Append(targetNamespace)
                .Append("\">");
            if (import != null)
            {
                sb.Append("<opc:Import Namespace=\"").Append(import).Append("\" />");
            }
            sb.Append("</opc:TypeDictionary>");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private static byte[] BuildNestedChoiceSchema(int depth)
        {
            var sb = new StringBuilder();
            sb.Append("<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" ")
                .Append("targetNamespace=\"urn:deep\" xmlns=\"urn:deep\" elementFormDefault=\"qualified\">")
                .Append("<xs:complexType name=\"T\"><xs:sequence>");
            for (int i = 0; i < depth; i++)
            {
                sb.Append("<xs:choice>");
            }
            sb.Append("<xs:element name=\"leaf\" type=\"xs:int\"/>");
            for (int i = 0; i < depth; i++)
            {
                sb.Append("</xs:choice>");
            }
            sb.Append("</xs:sequence></xs:complexType>")
                .Append("<xs:element name=\"Root\" type=\"T\"/>")
                .Append("</xs:schema>");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private sealed class ForwardOnlyStream(byte[] data) : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return m_inner.Read(buffer, offset, count);
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            private readonly MemoryStream m_inner = new(data);
        }
    }
}
