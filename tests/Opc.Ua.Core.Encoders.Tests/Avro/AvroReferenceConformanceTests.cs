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

        // (name, reference hex, write) for arrays and records. Reference bytes from the codec:
        // arrays  = avro_codec.encode(t.Array(t.Builtin(Int32), False), <list>)  — a nullable-union
        //           array: 0x02 (non-null) + zigzag(count) + items + 0x00 terminator.
        // record  = avro_codec.encode(<Point struct>, {X,Y}) — fields concatenated, no framing.
        private static readonly (string Name, string ReferenceHex, Action<AvroEncoder> Write)[] s_composites =
        {
            ("Int32Array_1_2_3", "020602040600",
                e => e.WriteInt32Array(null, new ArrayOf<int>([1, 2, 3]))),
            ("Int32Array_empty", "0200",
                e => e.WriteInt32Array(null, new ArrayOf<int>([]))),
            ("Int32Array_single_7", "02020e00",
                e => e.WriteInt32Array(null, new ArrayOf<int>([7]))),
            ("ByteString_010203", "0206010203",
                e => e.WriteByteString(null, ByteString.From([1, 2, 3]))),
            // Point record {X: 1.25 (double), Y: -3.5 (double)} — a canonical Avro record is its
            // fields concatenated, so writing the two doubles in order reproduces the reference.
            ("Record_Point_1_25_m3_5", "000000000000f43f0000000000000cc0", e =>
            {
                e.WriteDouble(null, 1.25);
                e.WriteDouble(null, -3.5);
            }),
        };

        [Test]
        public void ArraysAndRecordsMatchReferenceAvroBinary()
        {
            Assert.Multiple(() =>
            {
                foreach ((string name, string referenceHex, Action<AvroEncoder> write) in s_composites)
                {
                    string actual = ToHex(Encode(write));
                    Assert.That(actual, Is.EqualTo(referenceHex),
                        $"Avro binary mismatch vs reference for {name}");
                }
            });
        }

        // Canonical Variant encoding (Part B2, review finding 6). The reference codec (avro_codec)
        // encodes a Variant as the record
        //   { builtInType: int, dimensions: nullable(array<int>), body: union[...] }
        // where the body union is [ null, then per built-in type in VARIANT_BODY_TYPES:
        //   Variant<Type>Scalar{value}, Variant<Type>Array{values}, Variant<Type>MatrixBody{matrix} ].
        // So the body branch index = 1 + 3*pos(builtInType in VARIANT_BODY_TYPES) + shapeOffset
        // (scalar=0, array=1, matrix=2); null value => branch 0. Example: Int32 is body position 5,
        // so Int32Scalar = branch 16 (0x20 zigzag), Int32Array = branch 17 (0x22).
        //
        // B2-scalar (landed): the scalar branch of WriteVariant now emits this canonical form —
        //   builtInType(int) + nullable(dimensions) + bodyBranch(long) + scalar value.
        // Reference bytes below are avro_codec.encode(Builtin(Variant), Variant(Builtin(<T>), <v>)).
        private static readonly (string Name, string ReferenceHex, Action<AvroEncoder> Write)[] s_variantScalarTargets =
        {
            ("Variant_Boolean_true", "02000201",
                e => e.WriteVariant(null, new Variant(true))),
            ("Variant_SByte_m5", "04000809",
                e => e.WriteVariant(null, new Variant((sbyte)-5))),
            ("Variant_Int16_1000", "080014d00f",
                e => e.WriteVariant(null, new Variant((short)1000))),
            ("Variant_Int32_99", "0c0020c601",
                e => e.WriteVariant(null, new Variant(99))),
            ("Variant_UInt32_3B", "0e0026ff87fdd209",
                e => e.WriteVariant(null, new Variant(3000000000u))),
            ("Variant_Int64_300", "10002cd804",
                e => e.WriteVariant(null, new Variant(300L))),
            ("Variant_Float_1_5", "1400380000c03f",
                e => e.WriteVariant(null, new Variant(1.5f))),
            ("Variant_Double_1_5", "16003e000000000000f83f",
                e => e.WriteVariant(null, new Variant(1.5))),
            ("Variant_String_v", "180044020276",
                e => e.WriteVariant(null, new Variant("v"))),
        };

        [Test]
        public void VariantScalarMatchesReferenceAvroBinary()
        {
            Assert.Multiple(() =>
            {
                foreach ((string name, string referenceHex, Action<AvroEncoder> write) in s_variantScalarTargets)
                {
                    string actual = ToHex(Encode(write));
                    Assert.That(actual, Is.EqualTo(referenceHex),
                        $"canonical scalar Variant mismatch vs reference for {name}");
                }
            });
        }

        // B2-array/matrix (deferred): the Variant array/matrix BODY must be a PLAIN Avro array
        // (count + items + 0 terminator, no leading 0x02 present-marker) — distinct from a standalone
        // nullable-union array. The scalar branch (above) is canonical; the array/matrix branch still
        // emits the nullable-union body, so these cases stay pinned as the executable target for the
        // WritePlainArray follow-up. Reference bytes from avro_codec (plain array bodies visible as the
        // count byte directly after the body branch, e.g. Int32Array = 0x22 then 0x06 count).
        private static readonly (string Name, string ReferenceHex, Action<AvroEncoder> Write)[] s_variantArrayMatrixTargets =
        {
            ("Variant_Int32Array_1_2_3", "0c00220602040600",
                e => e.WriteVariant(null, new Variant(new ArrayOf<int>([1, 2, 3])))),
            ("Variant_Int32Array_empty", "0c002200",
                e => e.WriteVariant(null, new Variant(new ArrayOf<int>([])))),
            ("Variant_Int32Matrix_2x2", "0c02040404002404040400080204060800",
                e => e.WriteVariant(null, new Variant(
                    new ArrayOf<int>([1, 2, 3, 4]).ToMatrix(2, 2)))),
        };

        [Test]
        [Ignore("finding 6 / Part B2-array: canonical Variant array/matrix body (plain array) not yet implemented — executable target")]
        public void VariantArrayMatrixMatchesReferenceAvroBinary()
        {
            Assert.Multiple(() =>
            {
                foreach ((string name, string referenceHex, Action<AvroEncoder> write) in s_variantArrayMatrixTargets)
                {
                    string actual = ToHex(Encode(write));
                    Assert.That(actual, Is.EqualTo(referenceHex),
                        $"canonical Variant array/matrix mismatch vs reference for {name}");
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
