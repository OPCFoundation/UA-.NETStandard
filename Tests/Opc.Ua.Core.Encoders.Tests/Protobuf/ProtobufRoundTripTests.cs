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
using NUnit.Framework;
using Opc.Ua;

#nullable enable

namespace Opc.Ua.Core.Tests
{
    /// <summary>
    /// Validates Protobuf experimental encoding for OPC UA built-ins, node identifiers, composite values,
    /// variants, and optional-field presence semantics.
    /// </summary>
    [TestFixture]
    public sealed class ProtobufRoundTripTests
    {
        private static readonly byte[] BuiltInByteStringBytes = [0, 1, 255];
        private static readonly byte[] NodeIdByteStringBytes = [9, 8, 7];
        private static readonly ulong[] UInt64ArrayValues = [0, 1, ulong.MaxValue];
        private static readonly byte[] ExtensionObjectBytes = [1, 2, 3];
        private static readonly string[] OptionalCountFieldNames = ["count"];

        private static readonly IServiceMessageContext Context = ServiceMessageContext.CreateEmpty(null!);

        [Test]
        public void BuiltInsRoundTripEdges()
        {
            byte[] bytes = Encode(e =>
            {
                e.WriteBoolean("b", true);
                e.WriteSByte("sb", sbyte.MinValue);
                e.WriteByte("by", byte.MaxValue);
                e.WriteInt16("i16", short.MinValue);
                e.WriteUInt16("u16", ushort.MaxValue);
                e.WriteInt32("i32", int.MinValue);
                e.WriteUInt32("u32", uint.MaxValue);
                e.WriteInt64("i64", long.MinValue);
                e.WriteUInt64("u64", ulong.MaxValue);
                e.WriteFloat("f", -0.0f);
                e.WriteDouble("d", double.NaN);
                e.WriteString("sn", null);
                e.WriteString("se", string.Empty);
                e.WriteDateTime("dt0", DateTimeUtc.MinValue);
                e.WriteDateTime("dt1", DateTimeUtc.MaxValue);
                e.WriteGuid("g", new Uuid(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff")));
                e.WriteByteString("bs", ByteString.From(BuiltInByteStringBytes));
            });

            var d = new ProtobufDecoder(bytes, Context);
            Assert.That(d.ReadBoolean("b"), Is.True);
            Assert.That(d.ReadSByte("sb"), Is.EqualTo(sbyte.MinValue));
            Assert.That(d.ReadByte("by"), Is.EqualTo(byte.MaxValue));
            Assert.That(d.ReadInt16("i16"), Is.EqualTo(short.MinValue));
            Assert.That(d.ReadUInt16("u16"), Is.EqualTo(ushort.MaxValue));
            Assert.That(d.ReadInt32("i32"), Is.EqualTo(int.MinValue));
            Assert.That(d.ReadUInt32("u32"), Is.EqualTo(uint.MaxValue));
            Assert.That(d.ReadInt64("i64"), Is.EqualTo(long.MinValue));
            Assert.That(d.ReadUInt64("u64"), Is.EqualTo(ulong.MaxValue));
            Assert.That(
                BitConverter.SingleToUInt32Bits(d.ReadFloat("f")),
                Is.EqualTo(BitConverter.SingleToUInt32Bits(-0.0f)));
            Assert.That(
                BitConverter.DoubleToUInt64Bits(d.ReadDouble("d")),
                Is.EqualTo(BitConverter.DoubleToUInt64Bits(double.NaN)));
            Assert.That(d.ReadString("sn"), Is.Null);
            Assert.That(d.ReadString("se"), Is.EqualTo(string.Empty));
            Assert.That(d.ReadDateTime("dt0"), Is.EqualTo(DateTimeUtc.MinValue));
            Assert.That(d.ReadDateTime("dt1"), Is.EqualTo(DateTimeUtc.MaxValue));
            Assert.That((Guid)d.ReadGuid("g"), Is.EqualTo(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff")));
            Assert.That(d.ReadByteString("bs").Span.ToArray(), Is.EqualTo(BuiltInByteStringBytes));
        }

        [Test]
        public void NodeIdIdentifierKindsRoundTrip()
        {
            NodeId[] values =
            [
                new NodeId(123u, 2),
                new NodeId("name", 3),
                new NodeId(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), 4),
                new NodeId(ByteString.From(NodeIdByteStringBytes), 5)
            ];
            byte[] bytes = Encode(e =>
            {
                for (int ii = 0; ii < values.Length; ii++)
                {
                    e.WriteNodeId("n" + ii, values[ii]);
                }
            });
            var d = new ProtobufDecoder(bytes, Context);
            for (int ii = 0; ii < values.Length; ii++)
            {
                Assert.That(d.ReadNodeId("n" + ii), Is.EqualTo(values[ii]));
            }
        }

        [Test]
        public void NullableArraysAndCompositeValuesRoundTrip()
        {
            var numbers = new ArrayOf<ulong>(UInt64ArrayValues);
            var value = new DataValue(new Variant(42), StatusCodes.BadUnexpectedError);
            var info = new DiagnosticInfo
            {
                SymbolicId = 1,
                AdditionalInfo = "detail",
                InnerStatusCode = StatusCodes.BadInternalError
            };
            var xo = new ExtensionObject(
                new ExpandedNodeId(new NodeId(5001u, 2)),
                ByteString.From(ExtensionObjectBytes));

            byte[] bytes = Encode(e =>
            {
                e.WriteUInt64Array("ua", numbers);
                e.WriteDataValue("dv", value);
                e.WriteDiagnosticInfo("di", info);
                e.WriteExtensionObject("eo", xo);
            });

            var d = new ProtobufDecoder(bytes, Context);
            Assert.That(d.ReadUInt64Array("ua").Span.ToArray(), Is.EqualTo(numbers.Span.ToArray()));
            Assert.That(d.ReadDataValue("dv"), Is.EqualTo(value));
            Assert.That(d.ReadDiagnosticInfo("di")!.AdditionalInfo, Is.EqualTo("detail"));
            Assert.That(d.ReadExtensionObject("eo").TypeId, Is.EqualTo(xo.TypeId));
        }

        [Test]
        public void OptionalScalarPresenceDistinguishesAbsentFromPresentZero()
        {
            var absent = new OptionalScalars { Id = 7 };
            var zero = new OptionalScalars { Id = 7, HasCount = true, Count = 0 };

            OptionalScalars absentRoundTrip = RoundTrip(absent);
            OptionalScalars zeroRoundTrip = RoundTrip(zero);

            Assert.That(absentRoundTrip.HasCount, Is.False);
            Assert.That(zeroRoundTrip.HasCount, Is.True);
            Assert.That(zeroRoundTrip.Count, Is.Zero);
        }

        [Test]
        public void VariantScalarRoundTrip()
        {
            Variant value = new Variant(ulong.MaxValue);
            byte[] bytes = Encode(e => e.WriteVariant("v", value));
            var decoded = new ProtobufDecoder(bytes, Context).ReadVariant("v");
            Assert.That(decoded.GetUInt64(), Is.EqualTo(ulong.MaxValue));
        }

        [TestCaseSource(nameof(VariantBodyRoundTripCases))]
        public void VariantBodiesRoundTrip(Variant value)
        {
            Variant decoded = RoundTripVariant(value);
            AssertVariantEqual(decoded, value);
        }

        [Test]
        public void VariantEnumerationArrayRoundTrip()
        {
            // Regression: array/matrix enumeration variants box their elements as EnumValue, which is
            // not IConvertible, so the encoder previously threw InvalidCastException on this path.
            Variant value = Variant.From(ArrayOf.Wrapped(
                new EnumValue(10), new EnumValue(20), new EnumValue(30)));
            Variant decoded = RoundTripVariant(value);
            AssertVariantEqual(decoded, value);
        }

        [Test]
        public void VariantEnumerationMatrixRoundTrip()
        {
            Variant value = Variant.From(ArrayOf.Wrapped(
                new EnumValue(1), new EnumValue(2), new EnumValue(3), new EnumValue(4)).ToMatrix([2, 2]));
            Variant decoded = RoundTripVariant(value);
            AssertVariantEqual(decoded, value);
        }

        [Test]
        public void DeeplyNestedDataValueThrowsEncodingLimitsInsteadOfStackOverflow()
        {
            // Regression: the decoder must bound recursion (DataValue <-> Variant nesting) with
            // Context.MaxEncodingNestingLevels, like the built-in decoders, instead of overflowing
            // the stack on hostile input.
            var context = ServiceMessageContext.CreateEmpty(null!);
            context.MaxEncodingNestingLevels = 8;

            var value = new DataValue(new Variant(42));
            for (int i = 0; i < 20; i++)
            {
                value = new DataValue(Variant.From(value));
            }

            byte[] bytes = Encode(e => e.WriteDataValue("v", value), context);

            var decoder = new ProtobufDecoder(bytes, context);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadDataValue("v"))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void UnionSwitchFieldRoundTripsNonFirstMember()
        {
            // Regression: the union discriminator must be carried on the wire. Selecting a non-first
            // member (switch 2) previously decoded as member 1 because presence was probed positionally.
            byte[] bytes = Encode(e =>
            {
                e.WriteSwitchField(2, out _);
                e.WriteString("B", "hello");
            });

            var decoder = new ProtobufDecoder(bytes, Context);
            string[] switches = ["A", "B", "C"];
            uint sw = decoder.ReadSwitchField(switches, out string? field);
            Assert.That(sw, Is.EqualTo(2u));
            Assert.That(field, Is.EqualTo("B"));
            Assert.That(decoder.ReadString("B"), Is.EqualTo("hello"));
        }

        [Test]
        public void EncodingMaskRoundTripsWithAbsentEarlierOptional()
        {
            // Regression: with an absent earlier optional (A) and a present later one (B), the mask must
            // be reconstructed authoritatively from the wire (0b10), not by positional presence probing.
            byte[] bytes = Encode(e =>
            {
                e.WriteEncodingMask(0b10u);
                e.WriteString("B", "world");
            });

            var decoder = new ProtobufDecoder(bytes, Context);
            string[] masks = ["A", "B"];
            uint mask = decoder.ReadEncodingMask(masks);
            Assert.That(mask, Is.EqualTo(0b10u));
            Assert.That(decoder.ReadString("B"), Is.EqualTo("world"));
        }

        [Test]
        public void TruncatedFixed64ThrowsBadDecodingError()
        {
            // field 3, wire 1 (fixed64) with only two payload bytes instead of eight.
            byte[] truncated = [0x19, 0x01, 0x02];
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => new ProtobufDecoder(truncated, Context))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void TruncatedLengthDelimitedThrowsBadDecodingError()
        {
            // field 1, wire 2 (length-delimited) declaring length 10 with no payload bytes following.
            byte[] truncated = [0x0A, 0x0A];
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => new ProtobufDecoder(truncated, Context))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        private static IEnumerable<TestCaseData> VariantBodyRoundTripCases
        {
            get
            {
                DateTimeUtc firstDate = new DateTimeUtc(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
                DateTimeUtc secondDate = new DateTimeUtc(new DateTime(2025, 6, 7, 8, 9, 10, DateTimeKind.Utc));
                Uuid firstGuid = new Uuid(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));
                Uuid secondGuid = new Uuid(Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f"));
                NodeId firstNode = new NodeId("node-a", 2);
                NodeId secondNode = new NodeId(123u, 3);
                StatusCode firstStatus = StatusCodes.Good;
                StatusCode secondStatus = StatusCodes.BadUnexpectedError;

                foreach (Variant value in CreateVariantBodyCases(true, false, true, false, Variant.From, Variant.From, Variant.From))
                {
                    yield return new TestCaseData(value).SetName("VariantBodiesRoundTrip_Boolean_" + value.TypeInfo.ValueRank);
                }

                foreach (Variant value in CreateVariantBodyCases(1, 2, 3, 4, Variant.From, Variant.From, Variant.From))
                {
                    yield return new TestCaseData(value).SetName("VariantBodiesRoundTrip_Int32_" + value.TypeInfo.ValueRank);
                }

                foreach (Variant value in CreateVariantBodyCases(1L, 2L, 3L, 4L, Variant.From, Variant.From, Variant.From))
                {
                    yield return new TestCaseData(value).SetName("VariantBodiesRoundTrip_Int64_" + value.TypeInfo.ValueRank);
                }

                foreach (Variant value in CreateVariantBodyCases(1.25d, 2.5d, 3.75d, 4.0d, Variant.From, Variant.From, Variant.From))
                {
                    yield return new TestCaseData(value).SetName("VariantBodiesRoundTrip_Double_" + value.TypeInfo.ValueRank);
                }

                yield return new TestCaseData(Variant.From("scalar")).SetName("VariantBodiesRoundTrip_String_Scalar");
                yield return new TestCaseData(Variant.From(ArrayOf.Wrapped("alpha", null!, "omega")))
                    .SetName("VariantBodiesRoundTrip_String_ArrayWithNull");
                yield return new TestCaseData(Variant.From(ArrayOf.Wrapped("a", "b", "c", "d").ToMatrix([2, 2])))
                    .SetName("VariantBodiesRoundTrip_String_Matrix");

                foreach (Variant value in CreateVariantBodyCases(
                    firstDate,
                    secondDate,
                    firstDate,
                    secondDate,
                    Variant.From,
                    Variant.From,
                    Variant.From))
                {
                    yield return new TestCaseData(value).SetName("VariantBodiesRoundTrip_DateTime_" + value.TypeInfo.ValueRank);
                }

                foreach (Variant value in CreateVariantBodyCases(
                    firstGuid,
                    secondGuid,
                    firstGuid,
                    secondGuid,
                    Variant.From,
                    Variant.From,
                    Variant.From))
                {
                    yield return new TestCaseData(value).SetName("VariantBodiesRoundTrip_Guid_" + value.TypeInfo.ValueRank);
                }

                foreach (Variant value in CreateVariantBodyCases(
                    ByteString.From(1, 2),
                    ByteString.From(3, 4),
                    ByteString.From(5, 6),
                    ByteString.From(7, 8),
                    Variant.From,
                    Variant.From,
                    Variant.From))
                {
                    yield return new TestCaseData(value).SetName("VariantBodiesRoundTrip_ByteString_" + value.TypeInfo.ValueRank);
                }

                foreach (Variant value in CreateVariantBodyCases(
                    firstNode,
                    secondNode,
                    firstNode,
                    secondNode,
                    Variant.From,
                    Variant.From,
                    Variant.From))
                {
                    yield return new TestCaseData(value).SetName("VariantBodiesRoundTrip_NodeId_" + value.TypeInfo.ValueRank);
                }

                foreach (Variant value in CreateVariantBodyCases(
                    firstStatus,
                    secondStatus,
                    firstStatus,
                    secondStatus,
                    Variant.From,
                    Variant.From,
                    Variant.From))
                {
                    yield return new TestCaseData(value).SetName("VariantBodiesRoundTrip_StatusCode_" + value.TypeInfo.ValueRank);
                }
            }
        }

        private static IEnumerable<Variant> CreateVariantBodyCases<T>(
            T scalar,
            T arraySecond,
            T matrixThird,
            T matrixFourth,
            Func<T, Variant> fromScalar,
            Func<ArrayOf<T>, Variant> fromArray,
            Func<MatrixOf<T>, Variant> fromMatrix)
        {
            yield return fromScalar(scalar);
            yield return fromArray(ArrayOf.Wrapped(scalar, arraySecond));
            yield return fromMatrix(ArrayOf.Wrapped(scalar, arraySecond, matrixThird, matrixFourth).ToMatrix([2, 2]));
        }

        private static Variant RoundTripVariant(Variant value)
        {
            byte[] bytes = Encode(e => e.WriteVariant("v", value));
            return new ProtobufDecoder(bytes, Context).ReadVariant("v");
        }

        private static void AssertVariantEqual(Variant decoded, Variant expected)
        {
            Assert.That(decoded.TypeInfo, Is.EqualTo(expected.TypeInfo));
            Assert.That(decoded.AsBoxedObject(Variant.BoxingBehavior.None), Is.EqualTo(
                expected.AsBoxedObject(Variant.BoxingBehavior.None)));
        }

        /// <summary>
        /// Encodes and decodes an encodeable value with Protobuf to verify custom structure state is preserved.
        /// </summary>
        private static T RoundTrip<T>(T value) where T : IEncodeable, new()
        {
            byte[] bytes = Encode(value.Encode);
            var decoded = new T();
            decoded.Decode(new ProtobufDecoder(bytes, Context));
            return decoded;
        }

        /// <summary>
        /// Writes a Protobuf payload to an in-memory stream and returns the completed bytes.
        /// </summary>
        private static byte[] Encode(Action<ProtobufEncoder> encode)
        {
            return Encode(encode, Context);
        }

        /// <summary>
        /// Writes a Protobuf payload using the supplied context and returns the completed bytes.
        /// </summary>
        private static byte[] Encode(Action<ProtobufEncoder> encode, IServiceMessageContext context)
        {
            using var stream = new MemoryStream();
            using var encoder = new ProtobufEncoder(stream, context);
            encode(encoder);
            encoder.Close();
            return stream.ToArray();
        }

        /// <summary>
        /// Minimal encodeable used to verify that Protobuf distinguishes absent optional scalars from present zeros.
        /// </summary>
        private sealed class OptionalScalars : IEncodeable
        {
            public int Id { get; set; }
            public bool HasFlag { get; set; }
            public bool Flag { get; set; }
            public bool HasCount { get; set; }
            public int Count { get; set; }
            public bool HasRatio { get; set; }
            public double Ratio { get; set; }
            public ExpandedNodeId TypeId
            {
                get
                {
                    return new NodeId(9001u, 1);
                }
            }
            public ExpandedNodeId BinaryEncodingId
            {
                get
                {
                    return new NodeId(9002u, 1);
                }
            }
            public ExpandedNodeId XmlEncodingId
            {
                get
                {
                    return new NodeId(9003u, 1);
                }
            }
            public object Clone()
            {
                return MemberwiseClone();
            }
            public bool IsEqual(IEncodeable? encodeable)
            {
                return encodeable is OptionalScalars other && Equals(other);
            }

            public void Encode(IEncoder encoder)
            {
                encoder.WriteInt32("id", Id);
                if (HasFlag)
                {
                    encoder.WriteBoolean("flag", Flag);
                }

                if (HasCount)
                {
                    encoder.WriteInt32("count", Count);
                }

                if (HasRatio)
                {
                    encoder.WriteDouble("ratio", Ratio);
                }
            }

            public void Decode(IDecoder decoder)
            {
                Id = decoder.ReadInt32("id");
                uint mask = decoder.ReadEncodingMask(OptionalCountFieldNames);
                HasCount = (mask & 1u) != 0;
                if (HasFlag)
                {
                    Flag = decoder.ReadBoolean("flag");
                }

                if (HasCount)
                {
                    Count = decoder.ReadInt32("count");
                }

                if (HasRatio)
                {
                    Ratio = decoder.ReadDouble("ratio");
                }
            }
        }
    }
}
