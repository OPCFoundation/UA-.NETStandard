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
    /// Verifies that the Avro and JSON schema-exchange decoders convert a
    /// malformed peer payload into a single documented <see cref="FormatException"/>
    /// instead of leaking an assortment of runtime exceptions.
    /// </summary>
    [TestFixture]
    public sealed class SchemaExchangeDecodeHardeningTests
    {
        /// <summary>
        /// A truncated Avro announcement payload yields a FormatException.
        /// </summary>
        [Test]
        public void AvroSchemaAnnouncementDecodeTruncatedThrowsFormatException()
        {
            // 0x02 is a complete Avro varint (length 1) for the SchemaId bytes,
            // but the stream ends before those bytes can be read.
            byte[] payload = [0x02];
            Assert.That(
                () => AvroSchemaAnnouncement.Decode(payload),
                Throws.TypeOf<FormatException>());
        }

        /// <summary>
        /// A truncated Avro request payload yields a FormatException.
        /// </summary>
        [Test]
        public void AvroSchemaRequestDecodeTruncatedThrowsFormatException()
        {
            byte[] payload = [0x02];
            Assert.That(
                () => AvroSchemaRequest.Decode(payload),
                Throws.TypeOf<FormatException>());
        }

        /// <summary>
        /// A JSON announcement missing the SchemaJson property yields a
        /// FormatException rather than a KeyNotFoundException.
        /// </summary>
        [Test]
        public void JsonSchemaAnnouncementDecodeMissingPropertyThrowsFormatException()
        {
            byte[] payload = Encoding.UTF8.GetBytes("{\"schemaId\":\"AAAA\"}");
            Assert.That(
                () => JsonSchemaAnnouncement.Decode(payload),
                Throws.TypeOf<FormatException>());
        }

        /// <summary>
        /// A JSON request missing the SchemaIds property yields a
        /// FormatException rather than a KeyNotFoundException.
        /// </summary>
        [Test]
        public void JsonSchemaRequestDecodeMissingPropertyThrowsFormatException()
        {
            byte[] payload = Encoding.UTF8.GetBytes("{\"requesterId\":null}");
            Assert.That(
                () => JsonSchemaRequest.Decode(payload),
                Throws.TypeOf<FormatException>());
        }
    }
}
