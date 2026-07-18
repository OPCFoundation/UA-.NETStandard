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
using System.IO;
using NUnit.Framework;
using Opc.Ua;

#pragma warning disable UA_NETStandard_Encoders // experimental encoder surface under test

namespace Opc.Ua.Core.Tests
{
    /// <summary>
    /// Cross-implementation conformance harness for the experimental Avro encoding (Part B / Part 6
    /// §5.1). Each case encodes an OPC UA value with the .NET <see cref="AvroEncoder"/> and asserts
    /// the bytes are identical to the canonical Avro binary produced by the Python reference codec
    /// (fastavro <c>schemaless_writer</c>). Avro binary is version-stable, so these fixtures are a
    /// durable guardrail: this fixture pins the scalar built-ins (all byte-identical today) and is
    /// extended per type family as the structured encodings are canonicalised.
    /// </summary>
    [TestFixture]
    public sealed class AvroReferenceConformanceTests
    {
        private static IServiceMessageContext Context => ServiceMessageContext.CreateEmpty(null!);

        // (name, reference Avro binary hex, .NET encode action) — reference bytes produced by the
        // avro-encoding reference codec: avro_codec.encode(t.Builtin(<id>), <value>).hex().
        private static readonly (string Name, string ReferenceHex, Action<AvroEncoder> Write)[] s_scalars =
        {
            ("Boolean_true", "01", e => e.WriteBoolean(null, true)),
            ("SByte_m5", "09", e => e.WriteSByte(null, -5)),
            ("Byte_200", "9003", e => e.WriteByte(null, 200)),
            ("Int16_1000", "d00f", e => e.WriteInt16(null, 1000)),
            ("UInt16_40000", "80f104", e => e.WriteUInt16(null, 40000)),
            ("Int32_123", "f601", e => e.WriteInt32(null, 123)),
            ("UInt32_3000000000", "ff87fdd209", e => e.WriteUInt32(null, 3000000000u)),
            ("Int64_300", "d804", e => e.WriteInt64(null, 300)),
            ("Float_1_5", "0000c03f", e => e.WriteFloat(null, 1.5f)),
            ("Double_1_5", "000000000000f83f", e => e.WriteDouble(null, 1.5)),
            ("String_abc", "0206616263", e => e.WriteString(null, "abc")),
        };

        [Test]
        public void ScalarBuiltinsMatchReferenceAvroBinary()
        {
            Assert.Multiple(() =>
            {
                foreach ((string name, string referenceHex, Action<AvroEncoder> write) in s_scalars)
                {
                    string actual = ToHex(Encode(write));
                    Assert.That(actual, Is.EqualTo(referenceHex),
                        $"Avro binary mismatch vs reference for {name}");
                }
            });
        }

        private static byte[] Encode(Action<AvroEncoder> write)
        {
            using var stream = new MemoryStream();
            using (var encoder = new AvroEncoder(stream, Context, leaveOpen: true))
            {
                write(encoder);
                encoder.Close();
            }
            return stream.ToArray();
        }

        private static string ToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
