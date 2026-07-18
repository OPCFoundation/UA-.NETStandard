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
using System.Text;
using NUnit.Framework;
using Opc.Ua;

#pragma warning disable UA_NETStandard_Encoders // experimental encoder surface under test

namespace Opc.Ua.Core.Tests
{
    /// <summary>
    /// Proves the canonical Avro SchemaId (spec Part 6 §6.6): the .NET
    /// <see cref="AvroParsingCanonicalForm"/> reproduces Apache Avro's Parsing Canonical Form,
    /// and <see cref="SchemaId.RabinCrc64Avro"/> over it yields the CRC-64-AVRO fingerprint that
    /// the Python reference codec (fastavro <c>to_parsing_canonical_form</c> + CRC-64-AVRO)
    /// produces — so the SchemaId is byte-identical across implementations. The expected PCF
    /// strings and fingerprints below were produced by the reference codec.
    /// </summary>
    [TestFixture]
    public sealed class AvroParsingCanonicalFormTests
    {
        private static readonly (string Input, string Pcf, string WireHex)[] s_cases =
        {
            (
                "\"int\"",
                "\"int\"",
                "8f5c393f1ad57572"),
            (
                "{\"type\":\"record\",\"name\":\"N\",\"namespace\":\"org.opcfoundation.ua.avro\"," +
                "\"doc\":\"drop me\",\"fields\":[{\"name\":\"a\",\"type\":\"int\",\"doc\":\"x\"}," +
                "{\"name\":\"b\",\"type\":\"string\"}]}",
                "{\"name\":\"org.opcfoundation.ua.avro.N\",\"type\":\"record\",\"fields\":" +
                "[{\"name\":\"a\",\"type\":\"int\"},{\"name\":\"b\",\"type\":\"string\"}]}",
                "c5b84c177fd0efd4"),
            (
                "{\"type\":\"enum\",\"name\":\"Color\",\"namespace\":\"org.opcfoundation.ua.avro\"," +
                "\"symbols\":[\"R\",\"G\",\"B\"]}",
                "{\"name\":\"org.opcfoundation.ua.avro.Color\",\"type\":\"enum\",\"symbols\":" +
                "[\"R\",\"G\",\"B\"]}",
                "e9a6b0398a46f819"),
            (
                "{\"type\":\"array\",\"items\":\"long\"}",
                "{\"type\":\"array\",\"items\":\"long\"}",
                "715e2ea28bc91654"),
            (
                "{\"type\":\"record\",\"name\":\"U\",\"namespace\":\"org.opcfoundation.ua.avro\"," +
                "\"fields\":[{\"name\":\"v\",\"type\":[\"null\",\"int\"]}]}",
                "{\"name\":\"org.opcfoundation.ua.avro.U\",\"type\":\"record\",\"fields\":" +
                "[{\"name\":\"v\",\"type\":[\"null\",\"int\"]}]}",
                "bbcfa9040ef0ae0b"),
            (
                "{\"type\":\"fixed\",\"name\":\"F16\",\"namespace\":\"org.opcfoundation.ua.avro\"," +
                "\"size\":16}",
                "{\"name\":\"org.opcfoundation.ua.avro.F16\",\"type\":\"fixed\",\"size\":16}",
                "be9f623db05ee750"),
            (
                "{\"type\":\"record\",\"name\":\"Outer\",\"namespace\":\"org.opcfoundation.ua.avro\"," +
                "\"fields\":[{\"name\":\"inner\",\"type\":{\"type\":\"record\",\"name\":\"Inner\"," +
                "\"fields\":[{\"name\":\"x\",\"type\":\"double\"}]}},{\"name\":\"again\"," +
                "\"type\":\"Inner\"}]}",
                "{\"name\":\"org.opcfoundation.ua.avro.Outer\",\"type\":\"record\",\"fields\":" +
                "[{\"name\":\"inner\",\"type\":{\"name\":\"org.opcfoundation.ua.avro.Inner\"," +
                "\"type\":\"record\",\"fields\":[{\"name\":\"x\",\"type\":\"double\"}]}}," +
                "{\"name\":\"again\",\"type\":\"org.opcfoundation.ua.avro.Inner\"}]}",
                "a8c8c6713ca232da"),
        };

        /// <summary>
        /// The computed PCF must equal the reference codec's Parsing Canonical Form, and the
        /// on-wire SchemaId (the CRC-64-AVRO fingerprint serialized little-endian, per the Avro
        /// single-object encoding) must equal the reference fingerprint — for primitives, records
        /// (with dropped doc), enums, arrays, unions, fixed and nested records with
        /// namespace-inherited reference resolution.
        /// </summary>
        [Test]
        public void ComputeMatchesReferenceParsingCanonicalFormAndFingerprint()
        {
            Assert.Multiple(() =>
            {
                foreach ((string input, string expectedPcf, string expectedWireHex) in s_cases)
                {
                    string pcf = AvroParsingCanonicalForm.Compute(input);
                    Assert.That(pcf, Is.EqualTo(expectedPcf),
                        $"PCF mismatch for input: {input}");

                    // The on-wire SchemaId is the fingerprint serialized little-endian
                    // (SchemaId.AvroSingleObjectPrefix bytes [2..10)).
                    ulong fingerprint = SchemaId.RabinCrc64Avro(Encoding.UTF8.GetBytes(pcf));
                    byte[] wire = new byte[8];
                    SchemaId.AvroSingleObjectPrefix(fingerprint).AsSpan(2, 8).CopyTo(wire);
                    string wireHex = BitConverter.ToString(wire).Replace("-", string.Empty).ToLowerInvariant();

                    Assert.That(wireHex, Is.EqualTo(expectedWireHex),
                        $"On-wire SchemaId mismatch for input: {input}");
                }
            });
        }

        /// <summary>
        /// The canonical form is stable under input reformatting: two syntactically different but
        /// logically identical schemas (attribute order, whitespace, dropped doc) collapse to the
        /// same PCF and therefore the same SchemaId.
        /// </summary>
        [Test]
        public void ComputeIsStableUnderInputReformatting()
        {
            string a = "{\"type\":\"record\",\"name\":\"N\",\"namespace\":\"org.opcfoundation.ua.avro\"," +
                "\"fields\":[{\"name\":\"a\",\"type\":\"int\"},{\"name\":\"b\",\"type\":\"string\"}]}";
            string b = "{ \"name\" : \"org.opcfoundation.ua.avro.N\", \"doc\":\"ignored\", " +
                "\"type\":\"record\", \"fields\":[ {\"name\":\"a\",\"type\":{\"type\":\"int\"}}, " +
                "{\"type\":\"string\",\"name\":\"b\"} ] }";

            Assert.That(AvroParsingCanonicalForm.Compute(a), Is.EqualTo(AvroParsingCanonicalForm.Compute(b)));
        }
    }
}
