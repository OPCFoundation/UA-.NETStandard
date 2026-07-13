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
using NUnit.Framework;
using Opc.Ua;

#nullable enable

namespace Opc.Ua.Core.Tests
{
    /// <summary>
    /// Exercises the Avro experimental encoder and decoder for OPC UA scalar, array, matrix, variant,
    /// structure, optional-field, and union round-trip scenarios.
    /// </summary>
    [TestFixture]
    public sealed class AvroRoundTripTests
    {
        private static readonly string[] UnionFieldNames = ["None", "A", "B"];
        private static readonly double[] MatrixValues = [1.0, -0.0, double.NaN, double.PositiveInfinity];
        private static readonly int[] ArrayVariantValues = [10, 20, 30];

        private static IServiceMessageContext Context
        {
            get
            {
                return ServiceMessageContext.CreateEmpty(null!);
            }
        }

        [Test]
        public void AvroEncoderAndDecoderReportAvroEncodingType()
        {
            using var stream = new MemoryStream();
            using var encoder = new AvroEncoder(stream, Context, true);
            Assert.That(encoder.EncodingType, Is.EqualTo(EncodingType.Avro));

            using var decoder = new AvroDecoder(new byte[] { 0 }, Context);
            Assert.That(decoder.EncodingType, Is.EqualTo(EncodingType.Avro));
        }

        [Test]
        public void AvroBuiltInEdgesRoundTrip()
        {
            Assert.That(RoundTrip(e => e.WriteBoolean(null, true), d => d.ReadBoolean(null)), Is.True);
            Assert.That(
                RoundTrip(e => e.WriteSByte(null, sbyte.MinValue), d => d.ReadSByte(null)),
                Is.EqualTo(sbyte.MinValue));
            Assert.That(
                RoundTrip(e => e.WriteByte(null, byte.MaxValue), d => d.ReadByte(null)),
                Is.EqualTo(byte.MaxValue));
            Assert.That(
                RoundTrip(e => e.WriteInt16(null, short.MinValue), d => d.ReadInt16(null)),
                Is.EqualTo(short.MinValue));
            Assert.That(
                RoundTrip(e => e.WriteUInt16(null, ushort.MaxValue), d => d.ReadUInt16(null)),
                Is.EqualTo(ushort.MaxValue));
            Assert.That(
                RoundTrip(e => e.WriteInt32(null, int.MinValue), d => d.ReadInt32(null)),
                Is.EqualTo(int.MinValue));
            Assert.That(
                RoundTrip(e => e.WriteUInt32(null, uint.MaxValue), d => d.ReadUInt32(null)),
                Is.EqualTo(uint.MaxValue));
            Assert.That(
                RoundTrip(e => e.WriteInt64(null, long.MinValue), d => d.ReadInt64(null)),
                Is.EqualTo(long.MinValue));
            Assert.That(
                RoundTrip(e => e.WriteUInt64(null, ulong.MaxValue), d => d.ReadUInt64(null)),
                Is.EqualTo(ulong.MaxValue));
            Assert.That(
                EncoderCompat.SingleToInt32Bits(RoundTrip(e => e.WriteFloat(null, -0.0f), d => d.ReadFloat(null))),
                Is.EqualTo(EncoderCompat.SingleToInt32Bits(-0.0f)));
            Assert.That(float.IsNaN(RoundTrip(e => e.WriteFloat(null, float.NaN), d => d.ReadFloat(null))), Is.True);
            Assert.That(
                RoundTrip(e => e.WriteDouble(null, double.NegativeInfinity), d => d.ReadDouble(null)),
                Is.EqualTo(double.NegativeInfinity));
            Assert.That(
                EncoderCompat.DoubleToInt64Bits(RoundTrip(e => e.WriteDouble(null, -0.0d), d => d.ReadDouble(null))),
                Is.EqualTo(EncoderCompat.DoubleToInt64Bits(-0.0d)));
            Assert.That(
                RoundTrip(e => e.WriteDateTime(null, DateTimeUtc.MinValue), d => d.ReadDateTime(null)),
                Is.EqualTo(DateTimeUtc.MinValue));
            Assert.That(
                RoundTrip(e => e.WriteDateTime(null, new DateTimeUtc(0)), d => d.ReadDateTime(null)),
                Is.EqualTo(new DateTimeUtc(0)));
            Assert.That(
                RoundTrip(e => e.WriteDateTime(null, DateTimeUtc.MaxValue), d => d.ReadDateTime(null)),
                Is.EqualTo(DateTimeUtc.MaxValue));
            Assert.That(RoundTrip(e => e.WriteString(null, null), d => d.ReadString(null)), Is.Null);
            Assert.That(
                RoundTrip(e => e.WriteString(null, string.Empty), d => d.ReadString(null)),
                Is.EqualTo(string.Empty));
            Assert.That(
                RoundTrip(e => e.WriteByteString(null, default(ByteString)), d => d.ReadByteString(null)).IsNull,
                Is.True);
            Assert.That(
                RoundTrip(e => e.WriteByteString(null, ByteString.From()), d => d.ReadByteString(null)),
                Is.EqualTo(ByteString.From()));
            Assert.That(
                RoundTrip(e => e.WriteQualifiedName(null, new QualifiedName(null, 7)), d => d.ReadQualifiedName(null)),
                Is.EqualTo(new QualifiedName(null, 7)));
            Assert.That(
                RoundTrip(
                    e => e.WriteLocalizedText(null, new LocalizedText((string?)null, (string?)null)),
                    d => d.ReadLocalizedText(null)),
                Is.EqualTo(new LocalizedText((string?)null, (string?)null)));
        }

        [Test]
        public void AvroNodeIdKindsAndCompositeBuiltInsRoundTrip()
        {
            RoundTripNodeId(new NodeId(123u, 2));
            RoundTripNodeId(new NodeId("name", 3));
            RoundTripNodeId(new NodeId(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), 4));
            RoundTripNodeId(new NodeId(ByteString.From(0xaa, 0xbb), 5));
            var expanded = new ExpandedNodeId(new NodeId("x", 1), "urn:test", 42);
            Assert.That(
                RoundTrip(e => e.WriteExpandedNodeId(null, expanded), d => d.ReadExpandedNodeId(null)),
                Is.EqualTo(expanded));
            var diagnostic = new DiagnosticInfo
            {
                SymbolicId = 0,
                NamespaceUri = 1,
                Locale = 2,
                LocalizedText = 3,
                AdditionalInfo = "info",
                InnerStatusCode = new StatusCode(0x80010000),
                InnerDiagnosticInfo = new DiagnosticInfo { SymbolicId = 4 }
            };
            Assert.That(
                SameDiagnostic(
                    RoundTrip(e => e.WriteDiagnosticInfo(null, diagnostic), d => d.ReadDiagnosticInfo(null))!,
                    diagnostic),
                Is.True);
        }

        [Test]
        public void AvroArraysMatricesVariantsDataValuesAndExtensionObjectsRoundTrip()
        {
            var strings = new ArrayOf<string>(new string[] { "a", null!, string.Empty });
            Assert.That(
                RoundTrip(e => e.WriteStringArray(null, strings), d => d.ReadStringArray(null)),
                Is.EqualTo(strings));
            var bytes = new ArrayOf<ByteString>(new[] { default, ByteString.From(), ByteString.From(1, 2) });
            Assert.That(
                RoundTrip(e => e.WriteByteStringArray(null, bytes), d => d.ReadByteStringArray(null)),
                Is.EqualTo(bytes));
            Variant matrix = new Variant(new ArrayOf<double>(MatrixValues).ToMatrix(2, 2));
            Variant matrix2 = RoundTrip(e => e.WriteVariant(null, matrix), d => d.ReadVariant(null));
            Assert.That(matrix2.TypeInfo, Is.EqualTo(matrix.TypeInfo));
            Assert.That(
                EncoderCompat.DoubleToInt64Bits(matrix2.GetDoubleMatrix().Span[1]),
                Is.EqualTo(EncoderCompat.DoubleToInt64Bits(-0.0)));
            var ext = new ExtensionObject(new ExpandedNodeId(new NodeId(9001u, 2)), ByteString.From(9, 8, 7));
            Assert.That(
                RoundTrip(e => e.WriteExtensionObject(null, ext), d => d.ReadExtensionObject(null)),
                Is.EqualTo(ext));
            Assert.That(
                RoundTrip(e => e.WriteVariant(null, Variant.From(ext)), d => d.ReadVariant(null)),
                Is.EqualTo(Variant.From(ext)));
            var value = new DataValue(
                new Variant("payload"),
                new StatusCode(0x80340000),
                new DateTimeUtc(123456789L),
                new DateTimeUtc(987654321L),
                10,
                20);
            Assert.That(RoundTrip(e => e.WriteDataValue(null, value), d => d.ReadDataValue(null)), Is.EqualTo(value));
            var partial = DataValue.FromStatusCode(new StatusCode(0x80000000));
            Assert.That(
                RoundTrip(e => e.WriteDataValue(null, partial), d => d.ReadDataValue(null)),
                Is.EqualTo(partial));
        }

        [Test]
        public void AvroStructuresOptionalsAndUnionsRoundTrip()
        {
            var plain = new PlainStruct { Id = 42, Name = "forty-two" };
            Assert.That(
                RoundTrip(e => e.WriteEncodeable(null, plain), d => d.ReadEncodeable<PlainStruct>(null)),
                Is.EqualTo(plain));
            byte[] absent = Encode(e => e.WriteEncodingMask(0));
            byte[] presentZero = Encode(e =>
            {
                e.WriteEncodingMask(1);
                e.WriteInt32("Optional", 0);
            });
            using var d1 = new AvroDecoder(absent, Context);
            using var d2 = new AvroDecoder(presentZero, Context);
            Assert.That(d1.ReadEncodingMask(Array.Empty<string>()), Is.Zero);
            Assert.That(d2.ReadEncodingMask(Array.Empty<string>()), Is.EqualTo(1));
            Assert.That(d2.ReadInt32("Optional"), Is.Zero);
            byte[] union = Encode(e =>
            {
                e.WriteSwitchField(2, out _);
                e.WriteString("Body", null);
            });
            using var du = new AvroDecoder(union, Context);
            Assert.That(du.ReadSwitchField(UnionFieldNames, out _), Is.EqualTo(2));
            Assert.That(du.ReadString("Body"), Is.Null);
        }

        [Test]
        public void AvroEmptyArraysAndArrayVariantsRoundTripWithoutDesync()
        {
            // Regression (C1): a present-empty array must not emit a spurious Avro block
            // terminator that desyncs the following field, and 1-D array Variants (which carry
            // a present-empty dimensions array) must not silently decode as Null.
            byte[] emptyThenSentinel = Encode(e =>
            {
                e.WriteInt32Array("arr", new ArrayOf<int>(Array.Empty<int>()));
                e.WriteInt32("sentinel", 1234567);
            });
            using (var d = new AvroDecoder(emptyThenSentinel, Context))
            {
                Assert.That(d.ReadInt32Array("arr").Count, Is.Zero);
                Assert.That(d.ReadInt32("sentinel"), Is.EqualTo(1234567));
            }

            Variant arrayVariant = new Variant(new ArrayOf<int>(ArrayVariantValues));
            Variant decoded = RoundTrip(e => e.WriteVariant(null, arrayVariant), d => d.ReadVariant(null));
            Assert.That(decoded.TypeInfo, Is.EqualTo(arrayVariant.TypeInfo));
            Assert.That(decoded.GetInt32Array().ToArray(), Is.EqualTo(ArrayVariantValues));

            Variant emptyArrayVariant = new Variant(new ArrayOf<int>(Array.Empty<int>()));
            Variant decodedEmpty = RoundTrip(e => e.WriteVariant(null, emptyArrayVariant), d => d.ReadVariant(null));
            Assert.That(decodedEmpty.TypeInfo, Is.EqualTo(emptyArrayVariant.TypeInfo));
            Assert.That(decodedEmpty.GetInt32Array().Count, Is.Zero);
        }

        [Test]
        public void DeeplyNestedDiagnosticInfoThrowsEncodingLimitsInsteadOfStackOverflow()
        {
            // Regression: the decoder must bound recursion with Context.MaxEncodingNestingLevels,
            // like the built-in decoders, instead of overflowing the stack on hostile input.
            var di = new DiagnosticInfo { SymbolicId = 1 };
            for (int i = 0; i < 40; i++)
            {
                di = new DiagnosticInfo { InnerDiagnosticInfo = di };
            }

            byte[] bytes = Encode(e => e.WriteDiagnosticInfo(null, di));

            var limited = ServiceMessageContext.CreateEmpty(null!);
            limited.MaxEncodingNestingLevels = 8;
            using var decoder = new AvroDecoder(bytes, limited);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadDiagnosticInfo(null))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void OversizedStringThrowsEncodingLimits()
        {
            // Regression: decoded string/byte-string lengths must be validated against the context
            // limits before allocating, instead of trusting an attacker-controlled length prefix.
            byte[] bytes = Encode(e => e.WriteString(null, new string('x', 100)));

            var limited = ServiceMessageContext.CreateEmpty(null!);
            limited.MaxStringLength = 10;
            using var decoder = new AvroDecoder(bytes, limited);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadString(null))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void OversizedByteStringThrowsEncodingLimits()
        {
            byte[] bytes = Encode(e => e.WriteByteString(null, ByteString.From(new byte[100])));

            var limited = ServiceMessageContext.CreateEmpty(null!);
            limited.MaxByteStringLength = 10;
            using var decoder = new AvroDecoder(bytes, limited);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadByteString(null))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void AvroBufferedLargeMatrixAndStringRoundTrip()
        {
            int[] values = new int[2500];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = ii - 1250;
            }

            Variant matrix = new Variant(new ArrayOf<int>(values).ToMatrix(50, 50));
            string text = new string('å', 4096);
            byte[] payload = Encode(e =>
            {
                e.WriteVariant("Int32Matrix", matrix);
                e.WriteString("LongString", text);
            });

            using var decoder = new AvroDecoder(payload, Context);
            Variant decodedMatrix = decoder.ReadVariant("Int32Matrix");
            string? decodedText = decoder.ReadString("LongString");
            Assert.That(decodedMatrix.TypeInfo, Is.EqualTo(matrix.TypeInfo));
            Assert.That(decodedMatrix.GetInt32Matrix().Memory.ToArray(), Is.EqualTo(values));
            Assert.That(decodedText, Is.EqualTo(text));
        }

        /// <summary>
        /// Compares diagnostic information recursively so nested diagnostic fields survive Avro round trips.
        /// </summary>
        private static bool SameDiagnostic(DiagnosticInfo a, DiagnosticInfo b)
        {
            return a.SymbolicId == b.SymbolicId &&
                a.NamespaceUri == b.NamespaceUri &&
                a.Locale == b.Locale &&
                a.LocalizedText == b.LocalizedText &&
                a.AdditionalInfo == b.AdditionalInfo &&
                a.InnerStatusCode.Equals(b.InnerStatusCode, StatusCodeComparison.AllBits) &&
                ((a.InnerDiagnosticInfo == null && b.InnerDiagnosticInfo == null) ||
                    (a.InnerDiagnosticInfo != null &&
                     b.InnerDiagnosticInfo != null &&
                     SameDiagnostic(a.InnerDiagnosticInfo, b.InnerDiagnosticInfo)));
        }

        /// <summary>
        /// Encodes and decodes a <see cref="NodeId"/> with Avro and asserts that the identifier kind is preserved.
        /// </summary>
        private static void RoundTripNodeId(NodeId value)
        {
            Assert.That(RoundTrip(e => e.WriteNodeId(null, value), d => d.ReadNodeId(null)), Is.EqualTo(value));
        }

        /// <summary>
        /// Serializes a value with the supplied Avro writer delegate and reads it back with the matching decoder.
        /// </summary>
        private static T RoundTrip<T>(Action<AvroEncoder> write, Func<AvroDecoder, T> read)
        {
            byte[] bytes = Encode(write);
            using var decoder = new AvroDecoder(bytes, Context);
            return read(decoder);
        }

        /// <summary>
        /// Writes an Avro payload to an in-memory stream and returns the completed bytes.
        /// </summary>
        private static byte[] Encode(Action<AvroEncoder> write)
        {
            using var stream = new MemoryStream();
            using (var encoder = new AvroEncoder(stream, Context, true))
            {
                write(encoder);
                encoder.Close();
            }
            return stream.ToArray();
        }

        /// <summary>
        /// Minimal encodeable used to prove custom structures retain field values through Avro serialization.
        /// </summary>
        private sealed class PlainStruct : IEncodeable, IEquatable<PlainStruct>
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public ExpandedNodeId TypeId
            {
                get
                {
                    return new NodeId(5001u, 2);
                }
            }
            public ExpandedNodeId BinaryEncodingId
            {
                get
                {
                    return new NodeId(5002u, 2);
                }
            }
            public ExpandedNodeId XmlEncodingId
            {
                get
                {
                    return new NodeId(5003u, 2);
                }
            }
            public void Encode(IEncoder encoder)
            {
                encoder.WriteInt32(nameof(Id), Id);
                encoder.WriteString(nameof(Name), Name);
            }

            public void Decode(IDecoder decoder)
            {
                Id = decoder.ReadInt32(nameof(Id));
                Name = decoder.ReadString(nameof(Name));
            }
            public bool IsEqual(IEncodeable? encodeable)
            {
                return encodeable is PlainStruct other && Equals(other);
            }
            public object Clone()
            {
                return new PlainStruct { Id = Id, Name = Name };
            }
            public bool Equals(PlainStruct? other)
            {
                return other != null && Id == other.Id && Name == other.Name;
            }
            public override bool Equals(object? obj)
            {
                return Equals(obj as PlainStruct);
            }
            public override int GetHashCode()
            {
                return HashCode.Combine(Id, Name);
            }
        }
    }
}
