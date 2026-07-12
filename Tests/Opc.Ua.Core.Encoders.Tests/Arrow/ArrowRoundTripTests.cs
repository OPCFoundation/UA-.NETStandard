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
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using NUnit.Framework;
using Opc.Ua;

namespace Opc.Ua.Core.Tests
{
    /// <summary>
    /// Verifies that the Arrow experimental encoder preserves OPC UA built-ins, composite values, presence
    /// markers, and typed schema columns through round-trip serialization.
    /// </summary>
    [TestFixture]
    public sealed class ArrowRoundTripTests
    {
        private static readonly int[] MatrixValues = [1, 2, 3, 4];
        private static readonly string[] SchemaStringValues = ["a", "b"];
        private static readonly bool[] VariantBooleanValues = [true, false];
        private static readonly float[] VariantFloatValues = [1.25f, 2.5f];

        private static IServiceMessageContext Context
        {
            get
            {
                return ServiceMessageContext.CreateEmpty(null!);
            }
        }

        [Test]
        public void ArrowBuiltInsRoundTripWithFloatBits()
        {
            Assert.That(RoundTrip(e => e.WriteBoolean(null, true), d => d.ReadBoolean(null)), Is.True);
            Assert.That(
                RoundTrip(e => e.WriteSByte(null, sbyte.MinValue), d => d.ReadSByte(null)),
                Is.EqualTo(sbyte.MinValue));
            Assert.That(
                RoundTrip(e => e.WriteUInt64(null, ulong.MaxValue), d => d.ReadUInt64(null)),
                Is.EqualTo(ulong.MaxValue));
            Assert.That(
                BitConverter.SingleToInt32Bits(RoundTrip(e => e.WriteFloat(null, -0.0f), d => d.ReadFloat(null))),
                Is.EqualTo(BitConverter.SingleToInt32Bits(-0.0f)));
            Assert.That(float.IsNaN(RoundTrip(e => e.WriteFloat(null, float.NaN), d => d.ReadFloat(null))), Is.True);
            Assert.That(
                RoundTrip(e => e.WriteDouble(null, double.PositiveInfinity), d => d.ReadDouble(null)),
                Is.EqualTo(double.PositiveInfinity));
            Assert.That(
                RoundTrip(e => e.WriteDateTime(null, DateTimeUtc.MaxValue), d => d.ReadDateTime(null)),
                Is.EqualTo(DateTimeUtc.MaxValue));
            Assert.That(RoundTrip(e => e.WriteString(null, null), d => d.ReadString(null)), Is.Null);
            Assert.That(
                RoundTrip(e => e.WriteByteString(null, ByteString.From(1, 2, 3)), d => d.ReadByteString(null)),
                Is.EqualTo(ByteString.From(1, 2, 3)));
        }

        [Test]
        public void ArrowNodeIdKindsRoundTrip()
        {
            RoundTripEqual(new NodeId(123u, 2));
            RoundTripEqual(new NodeId("name", 3));
            RoundTripEqual(new NodeId(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), 4));
            RoundTripEqual(new NodeId(ByteString.From(0xaa, 0xbb), 5));
        }

        [Test]
        public void ArrowArraysVariantMatrixDataValueExtensionObjectAndDiagnosticsRoundTrip()
        {
            var strings = new ArrayOf<string>(new string[] { "a", null!, string.Empty });
            Assert.That(
                RoundTrip(e => e.WriteStringArray(null, strings), d => d.ReadStringArray(null)),
                Is.EqualTo(strings));

            Variant matrix = new Variant(new ArrayOf<int>(MatrixValues).ToMatrix(2, 2));
            Assert.That(RoundTrip(e => e.WriteVariant(null, matrix), d => d.ReadVariant(null)), Is.EqualTo(matrix));

            var value = new DataValue(
                new Variant("payload"),
                new StatusCode(0x80340000),
                new DateTimeUtc(123456789L),
                new DateTimeUtc(987654321L),
                10,
                20);
            Assert.That(RoundTrip(e => e.WriteDataValue(null, value), d => d.ReadDataValue(null)), Is.EqualTo(value));

            var extension = new ExtensionObject(new ExpandedNodeId(new NodeId(1u, 2)), ByteString.From(9, 8, 7));
            Assert.That(
                RoundTrip(e => e.WriteExtensionObject(null, extension), d => d.ReadExtensionObject(null)),
                Is.EqualTo(extension));

            var diagnostic = new DiagnosticInfo
            {
                SymbolicId = 1,
                AdditionalInfo = "info",
                InnerStatusCode = new StatusCode(0x80010000),
                InnerDiagnosticInfo = new DiagnosticInfo { SymbolicId = 2 }
            };
            Assert.That(
                RoundTrip(e => e.WriteDiagnosticInfo(null, diagnostic), d => d.ReadDiagnosticInfo(null)),
                Is.EqualTo(diagnostic));
        }

        [TestCaseSource(nameof(VariantBuiltInRoundTripCases))]
        public void ArrowVariantBuiltInBodiesRoundTrip(Variant value)
        {
            Assert.That(RoundTrip(e => e.WriteVariant(null, value), d => d.ReadVariant(null)), Is.EqualTo(value));
        }

        [Test]
        public void ArrowUnionAndOptionalPresenceSignalsRoundTrip()
        {
            byte[] bytes = Encode(e =>
            {
                e.WriteSwitchField(2, out _);
                e.WriteEncodingMask(0b101);
                e.WriteInt32("presentZero", 0);
                e.WriteString("presentNull", null);
            });

            using var decoder = new ArrowDecoder(bytes, Context);
            Assert.That(decoder.ReadSwitchField(Array.Empty<string>(), out _), Is.EqualTo(2));
            Assert.That(decoder.ReadEncodingMask(Array.Empty<string>()), Is.EqualTo(0b101));
            Assert.That(decoder.HasField("presentZero"), Is.True);
            Assert.That(decoder.HasField("absent"), Is.False);
            Assert.That(decoder.ReadInt32("presentZero"), Is.Zero);
            Assert.That(decoder.ReadString("presentNull"), Is.Null);
        }

        [Test]
        public void ArrowSchemaUsesTypedColumns()
        {
            AssertArrowType(e => e.WriteInt32(null, 42), typeof(Int32Type));
            AssertArrowType(e => e.WriteGuid(null, new Uuid(Guid.NewGuid())), typeof(FixedSizeBinaryType));
            AssertArrowType(e => e.WriteNodeId(null, new NodeId(123u, 2)), typeof(StructType));
            AssertArrowType(e => e.WriteStringArray(null, new ArrayOf<string>(SchemaStringValues)), typeof(ListType));
            AssertArrowType(e => e.WriteVariant(null, new Variant(123)), typeof(UnionType));
        }

        /// <summary>
        /// Encodes and decodes a <see cref="NodeId"/> with Arrow and asserts that the identifier kind is preserved.
        /// </summary>
        private static void RoundTripEqual(NodeId value)
        {
            Assert.That(RoundTrip(e => e.WriteNodeId(null, value), d => d.ReadNodeId(null)), Is.EqualTo(value));
        }

        private static IEnumerable<TestCaseData> VariantBuiltInRoundTripCases()
        {
            foreach (Variant value in VariantScalarCases())
            {
                yield return new TestCaseData(value).SetName($"ArrowVariantScalar{value.TypeInfo.BuiltInType}RoundTrip");
            }

            foreach (Variant value in VariantArrayCases())
            {
                yield return new TestCaseData(value).SetName($"ArrowVariantArray{value.TypeInfo.BuiltInType}RoundTrip");
            }

            foreach (Variant value in VariantMatrixCases())
            {
                yield return new TestCaseData(value).SetName($"ArrowVariantMatrix{value.TypeInfo.BuiltInType}RoundTrip");
            }
        }

        private static IEnumerable<Variant> VariantScalarCases()
        {
            yield return new Variant(true);
            yield return new Variant((sbyte)-2);
            yield return new Variant((byte)3);
            yield return new Variant((short)-4);
            yield return new Variant((ushort)5);
            yield return new Variant(-6);
            yield return new Variant(7u);
            yield return new Variant(-8L);
            yield return new Variant(9ul);
            yield return new Variant(1.25f);
            yield return new Variant(-2.5d);
            yield return new Variant("text");
            yield return new Variant(new DateTimeUtc(123456789L));
            yield return new Variant(new Uuid(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff")));
            yield return new Variant(ByteString.From(1, 2, 3));
            yield return new Variant(XmlElement.From("<a/>"));
            yield return new Variant(new NodeId("node", 2));
            yield return new Variant(new ExpandedNodeId(new NodeId(3u, 4), "urn:test", 5));
            yield return new Variant(new StatusCode(0x80340000));
            yield return new Variant(new QualifiedName("name", 6));
            yield return new Variant(new LocalizedText("en-US", "hello"));
            yield return new Variant(new ExtensionObject(new ExpandedNodeId(new NodeId(1u, 2)), ByteString.From(9, 8)));
        }

        private static IEnumerable<Variant> VariantArrayCases()
        {
            yield return new Variant(new ArrayOf<bool>(VariantBooleanValues));
            yield return new Variant(new ArrayOf<sbyte>(new sbyte[] { -1, 2 }));
            yield return new Variant(new ArrayOf<byte>(new byte[] { 1, 2 }));
            yield return new Variant(new ArrayOf<short>(new short[] { -3, 4 }));
            yield return new Variant(new ArrayOf<ushort>(new ushort[] { 3, 4 }));
            yield return new Variant(new ArrayOf<int>(new[] { -5, 6 }));
            yield return new Variant(new ArrayOf<uint>(new uint[] { 5, 6 }));
            yield return new Variant(new ArrayOf<long>(new long[] { -7, 8 }));
            yield return new Variant(new ArrayOf<ulong>(new ulong[] { 7, 8 }));
            yield return new Variant(new ArrayOf<float>(VariantFloatValues));
            yield return new Variant(new ArrayOf<double>(new[] { -1.25d, -2.5d }));
            yield return new Variant(new ArrayOf<string>(new string[] { "a", null! }));
            yield return new Variant(new ArrayOf<DateTimeUtc>(new[] { new DateTimeUtc(11L), default }));
            yield return new Variant(new ArrayOf<Uuid>(new[] { new Uuid(Guid.Parse("11112222-3333-4444-5555-666677778888")), default }));
            yield return new Variant(new ArrayOf<ByteString>(new[] { ByteString.From(1, 2), default }));
            yield return new Variant(new ArrayOf<XmlElement>(new[] { XmlElement.From("<x/>"), default }));
            yield return new Variant(new ArrayOf<NodeId>(new[] { new NodeId(1u, 1), NodeId.Null }));
            yield return new Variant(
                new ArrayOf<ExpandedNodeId>(new[] { new ExpandedNodeId(new NodeId(2u, 2), "urn:a", 3), ExpandedNodeId.Null }));
            yield return new Variant(new ArrayOf<StatusCode>(new[] { new StatusCode(0x80340000), StatusCodes.Good }));
            yield return new Variant(new ArrayOf<QualifiedName>(new[] { new QualifiedName("q", 1), QualifiedName.Null }));
            yield return new Variant(new ArrayOf<LocalizedText>(new[] { new LocalizedText("en", "t"), LocalizedText.Null }));
            yield return new Variant(
                new ArrayOf<ExtensionObject>(
                    new[] { new ExtensionObject(new ExpandedNodeId(new NodeId(1u, 2)), ByteString.From(3)), ExtensionObject.Null }));
        }

        private static IEnumerable<Variant> VariantMatrixCases()
        {
            yield return new Variant(new ArrayOf<bool>(VariantBooleanValues).ToMatrix(1, 2));
            yield return new Variant(new ArrayOf<sbyte>(new sbyte[] { -1, 2 }).ToMatrix(1, 2));
            yield return new Variant(new ArrayOf<byte>(new byte[] { 1, 2 }).ToMatrix(1, 2));
            yield return new Variant(new ArrayOf<short>(new short[] { -3, 4 }).ToMatrix(1, 2));
            yield return new Variant(new ArrayOf<ushort>(new ushort[] { 3, 4 }).ToMatrix(1, 2));
            yield return new Variant(new ArrayOf<int>(new[] { -5, 6 }).ToMatrix(1, 2));
            yield return new Variant(new ArrayOf<uint>(new uint[] { 5, 6 }).ToMatrix(1, 2));
            yield return new Variant(new ArrayOf<long>(new long[] { -7, 8 }).ToMatrix(1, 2));
            yield return new Variant(new ArrayOf<ulong>(new ulong[] { 7, 8 }).ToMatrix(1, 2));
            yield return new Variant(new ArrayOf<float>(VariantFloatValues).ToMatrix(1, 2));
            yield return new Variant(new ArrayOf<double>(new[] { -1.25d, -2.5d }).ToMatrix(1, 2));
            yield return new Variant(new ArrayOf<string>(new string[] { "a", null! }).ToMatrix(1, 2));
            yield return new Variant(new ArrayOf<DateTimeUtc>(new[] { new DateTimeUtc(11L), default }).ToMatrix(1, 2));
            yield return new Variant(
                new ArrayOf<Uuid>(new[] { new Uuid(Guid.Parse("11112222-3333-4444-5555-666677778888")), default }).ToMatrix(1, 2));
            yield return new Variant(new ArrayOf<ByteString>(new[] { ByteString.From(1, 2), default }).ToMatrix(1, 2));
            yield return new Variant(new ArrayOf<XmlElement>(new[] { XmlElement.From("<x/>"), default }).ToMatrix(1, 2));
            yield return new Variant(new ArrayOf<NodeId>(new[] { new NodeId(1u, 1), NodeId.Null }).ToMatrix(1, 2));
            yield return new Variant(
                new ArrayOf<ExpandedNodeId>(new[] { new ExpandedNodeId(new NodeId(2u, 2), "urn:a", 3), ExpandedNodeId.Null })
                    .ToMatrix(1, 2));
            yield return new Variant(new ArrayOf<StatusCode>(new[] { new StatusCode(0x80340000), StatusCodes.Good }).ToMatrix(1, 2));
            yield return new Variant(new ArrayOf<QualifiedName>(new[] { new QualifiedName("q", 1), QualifiedName.Null }).ToMatrix(1, 2));
            yield return new Variant(new ArrayOf<LocalizedText>(new[] { new LocalizedText("en", "t"), LocalizedText.Null }).ToMatrix(1, 2));
            yield return new Variant(
                new ArrayOf<ExtensionObject>(
                    new[] { new ExtensionObject(new ExpandedNodeId(new NodeId(1u, 2)), ByteString.From(3)), ExtensionObject.Null })
                    .ToMatrix(1, 2));
        }

        /// <summary>
        /// Serializes a value with the supplied Arrow writer delegate and reads it back with the matching decoder.
        /// </summary>
        private static T RoundTrip<T>(Action<ArrowEncoder> write, Func<ArrowDecoder, T> read)
        {
            byte[] bytes = Encode(write);
            using var decoder = new ArrowDecoder(bytes, Context);
            return read(decoder);
        }

        /// <summary>
        /// Writes an Arrow payload to an in-memory stream and returns the completed record-batch bytes.
        /// </summary>
        private static byte[] Encode(Action<ArrowEncoder> write)
        {
            using var stream = new MemoryStream();
            using (var encoder = new ArrowEncoder(stream, Context, leaveOpen: true))
            {
                write(encoder);
                encoder.Close();
            }
            return stream.ToArray();
        }

        /// <summary>
        /// Confirms that a single encoded Arrow field uses the expected typed Arrow column instead of a binary blob.
        /// </summary>
        private static void AssertArrowType(Action<ArrowEncoder> write, Type expectedType)
        {
            using var reader = new ArrowStreamReader(Encode(write));
            using var batch = reader.ReadNextRecordBatch();

            Assert.That(batch, Is.Not.Null);
            Assert.That(batch!.ColumnCount, Is.EqualTo(1));
            Assert.That(batch.Schema.GetFieldByIndex(0).DataType, Is.TypeOf(expectedType));
            Assert.That(batch.Schema.GetFieldByIndex(0).DataType, Is.Not.TypeOf<BinaryType>());
        }
    }
}
