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
using System.Text;
using NUnit.Framework;
using Opc.Ua;

namespace Opc.Ua.Core.Tests
{
    /// <summary>
    /// Verifies stable schema identifier helpers for Avro single-object prefixes and canonical CRC fingerprints.
    /// </summary>
    [TestFixture]
    public sealed class SchemaIdTests
    {
        [Test]
        public void AvroSingleObjectPrefixUsesMagicAndLittleEndianFingerprint()
        {
            byte[] prefix = SchemaId.AvroSingleObjectPrefix(0x0102030405060708UL);

            Assert.That(
                prefix,
                Is.EqualTo(new byte[] { 0xC3, 0x01, 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 }));
        }

        [Test]
        public void RabinCrc64AvroIsStableForSameCanonicalBytes()
        {
            byte[] canonical = Encoding.UTF8.GetBytes("""{"type":"record","name":"Stable","fields":[]}""");

            ulong first = SchemaId.RabinCrc64Avro(canonical);
            ulong second = SchemaId.RabinCrc64Avro(canonical);

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void JsonSchemaIdUsesStableSha256Prefix()
        {
            byte[] schemaJson = Encoding.UTF8.GetBytes("""{"$schema":"https://json-schema.org/draft/2020-12/schema"}""");

            byte[] first = SchemaId.JsonSchemaId(schemaJson);
            byte[] second = SchemaId.JsonSchemaId(schemaJson);

            Assert.That(first, Has.Length.EqualTo(8));
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void JsonSchemaIdChangesWhenSchemaChanges()
        {
            byte[] firstSchemaJson = Encoding.UTF8.GetBytes("""{"type":"object","properties":{"a":{"type":"string"}}}""");
            byte[] secondSchemaJson = Encoding.UTF8.GetBytes("""{"type":"object","properties":{"a":{"type":"number"}}}""");

            byte[] first = SchemaId.JsonSchemaId(firstSchemaJson);
            byte[] second = SchemaId.JsonSchemaId(secondSchemaJson);

            Assert.That(second, Is.Not.EqualTo(first));
        }

        [Test]
        public void JsonSchemaIdHonorsRequestedLength()
        {
            byte[] schemaJson = Encoding.UTF8.GetBytes("""{"type":"object"}""");

            byte[] full = SchemaId.JsonSchemaId(schemaJson, 32);
            byte[] empty = SchemaId.JsonSchemaId(schemaJson, 0);

            Assert.Multiple(() =>
            {
                Assert.That(full, Has.Length.EqualTo(32));
                Assert.That(empty, Is.Empty);
                Assert.That(() => SchemaId.JsonSchemaId(schemaJson, -1), Throws.InstanceOf<ArgumentOutOfRangeException>());
                Assert.That(() => SchemaId.JsonSchemaId(schemaJson, 33), Throws.InstanceOf<ArgumentOutOfRangeException>());
            });
        }
    }
}
