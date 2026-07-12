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

using System.Text;
using NUnit.Framework;
using Opc.Ua;

#nullable enable

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
    }
}
