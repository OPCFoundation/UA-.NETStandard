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
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding.Uadp;

namespace Opc.Ua.PubSub.Tests.Encoding.Uadp
{
    /// <summary>
    /// Focused round-trip coverage for UADP raw binary scalar and padded
    /// array helper paths.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.5.4")]
    [TestSpec("7.2.4.5.11")]
    public sealed class UadpBinaryRawEncodingCoverageTests
    {
        private static readonly ServiceMessageContext s_context =
            ServiceMessageContext.CreateEmpty(null!);

        private static readonly bool[] s_boolValues = [true, false];
        private static readonly sbyte[] s_sbyteValues = [-1, 2];
        private static readonly byte[] s_byteValues = [1, 2];
        private static readonly byte[] s_expectedByteString = [0x10, 0x20];
        private static readonly short[] s_int16Values = [-2, 3];
        private static readonly ushort[] s_uint16Values = [2, 3];
        private static readonly int[] s_int32Values = [-4, 5];
        private static readonly uint[] s_uint32Values = [4, 5];
        private static readonly long[] s_int64Values = [-6, 7];
        private static readonly ulong[] s_uint64Values = [6, 7];
        private static readonly float[] s_floatValues = [1.5f, 2.5f];
        private static readonly double[] s_doubleValues = [1.5d, 2.5d];
        private static readonly string[] s_paddedStrings = ["ab", "cd"];

        private static readonly ByteString[] s_paddedByteStrings =
        [
            new ByteString(new byte[] { 1, 2 }),
            new ByteString(new byte[] { 3 })
        ];

        private static readonly int[] s_expectedInts = [1, 2, 3];
        private static readonly string[] s_expectedStrings = ["a", "b"];
        private static readonly Variant[] s_variantValues = [new Variant(1), new Variant("two")];
        private static readonly int[] s_overflowValues = [1, 2];
        private static readonly uint[] s_overflowDimensions = [int.MaxValue, 2u];

        private static IEnumerable<TestCaseData> ScalarCases()
        {
            yield return new TestCaseData(BuiltInType.Boolean, new Variant(true), true);
            yield return new TestCaseData(BuiltInType.SByte, new Variant((sbyte)-5), (sbyte)-5);
            yield return new TestCaseData(BuiltInType.Byte, new Variant((byte)250), (byte)250);
            yield return new TestCaseData(BuiltInType.Int16, new Variant((short)-32000), (short)-32000);
            yield return new TestCaseData(BuiltInType.UInt16, new Variant((ushort)65000), (ushort)65000);
            yield return new TestCaseData(BuiltInType.Int32, new Variant(-123456), -123456);
            yield return new TestCaseData(BuiltInType.UInt32, new Variant(123456u), 123456u);
            yield return new TestCaseData(BuiltInType.Int64, new Variant(-1234567890123L), -1234567890123L);
            yield return new TestCaseData(BuiltInType.UInt64, new Variant(1234567890123UL), 1234567890123UL);
            yield return new TestCaseData(BuiltInType.Float, new Variant(1.25f), 1.25f);
            yield return new TestCaseData(BuiltInType.Double, new Variant(9.5d), 9.5d);
            yield return new TestCaseData(BuiltInType.String, new Variant("raw"), "raw");
            yield return new TestCaseData(
                BuiltInType.DateTime,
                new Variant(new DateTimeUtc(new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc))),
                new DateTimeUtc(new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc)));
            yield return new TestCaseData(
                BuiltInType.Guid,
                new Variant(new Uuid(new Guid("12345678-1234-4321-9876-001122334455"))),
                new Uuid(new Guid("12345678-1234-4321-9876-001122334455")));
            yield return new TestCaseData(
                BuiltInType.ByteString,
                new Variant(new ByteString(new byte[] { 1, 2, 3, 4 })),
                new ByteString(new byte[] { 1, 2, 3, 4 }));
            yield return new TestCaseData(
                BuiltInType.XmlElement,
                new Variant(XmlElement.From("<a>1</a>")),
                XmlElement.From("<a>1</a>"));
            yield return new TestCaseData(
                BuiltInType.NodeId,
                new Variant(new NodeId(1234, 2)),
                new NodeId(1234, 2));
            yield return new TestCaseData(
                BuiltInType.ExpandedNodeId,
                new Variant(new ExpandedNodeId(1234, 2, "urn:test")),
                new ExpandedNodeId(1234, 2, "urn:test"));
            yield return new TestCaseData(
                BuiltInType.StatusCode,
                new Variant(StatusCodes.BadUnexpectedError),
                StatusCodes.BadUnexpectedError);
            yield return new TestCaseData(
                BuiltInType.QualifiedName,
                new Variant(new QualifiedName("Name", 2)),
                new QualifiedName("Name", 2));
            yield return new TestCaseData(
                BuiltInType.LocalizedText,
                new Variant(new LocalizedText("en-US", "Hello")),
                new LocalizedText("en-US", "Hello"));
            yield return new TestCaseData(
                BuiltInType.DataValue,
                new Variant(new DataValue(new Variant(42))),
                new DataValue(new Variant(42)));
            yield return new TestCaseData(
                BuiltInType.ExtensionObject,
                new Variant(new ExtensionObject(new NetworkAddressUrlDataType { Url = "opc.udp://localhost:4840" })),
                new ExtensionObject(new NetworkAddressUrlDataType { Url = "opc.udp://localhost:4840" }));
        }

        [TestCaseSource(nameof(ScalarCases))]
        public void RawScalarRoundTripsBuiltInType(BuiltInType builtInType, Variant value, object expected)
        {
            byte[] buffer = new byte[4096];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);

            writer.WriteRawScalar(value, builtInType, ValueRanks.Scalar, s_context);

            var reader = new UadpBinaryReader(buffer, 0, writer.Position);
            Variant decoded = reader.ReadRawScalar(builtInType, ValueRanks.Scalar, s_context);

            AssertDecodedValue(decoded, builtInType, expected);
            Assert.That(reader.Remaining, Is.Zero);
        }

        [Test]
        public void VariantAndDataValueHelpersRoundTrip()
        {
            byte[] buffer = new byte[1024];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            var variant = new Variant("wrapped");
            var dataValue = new DataValue(new Variant(123));

            writer.WriteVariant(variant, s_context);
            writer.WriteDataValue(dataValue, s_context);

            var reader = new UadpBinaryReader(buffer, 0, writer.Position);
            Variant decodedVariant = reader.ReadVariant(s_context);
            DataValue decodedDataValue = reader.ReadDataValue(s_context);

            Assert.Multiple(() =>
            {
                Assert.That(decodedVariant.TryGetValue(out string? text), Is.True);
                Assert.That(text, Is.EqualTo("wrapped"));
                Assert.That(decodedDataValue.WrappedValue.TryGetValue(out int number), Is.True);
                Assert.That(number, Is.EqualTo(123));
                Assert.That(reader.Remaining, Is.Zero);
            });
        }

        [Test]
        public void PaddedStringByteStringAndXmlScalarsRoundTrip()
        {
            byte[] buffer = new byte[128];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);

            writer.WriteRawScalar(new Variant("ua"), BuiltInType.String, ValueRanks.Scalar, 8, default, s_context);
            writer.WriteRawScalar(
                new Variant(new ByteString(new byte[] { 0x10, 0x20 })),
                BuiltInType.ByteString,
                ValueRanks.Scalar,
                6,
                default,
                s_context);
            writer.WriteRawScalar(
                new Variant(XmlElement.From("<x/>")),
                BuiltInType.XmlElement,
                ValueRanks.Scalar,
                8,
                default,
                s_context);

            var reader = new UadpBinaryReader(buffer, 0, writer.Position);
            Variant text = reader.ReadRawScalar(BuiltInType.String, ValueRanks.Scalar, 8, default, s_context);
            Variant bytes = reader.ReadRawScalar(BuiltInType.ByteString, ValueRanks.Scalar, 6, default, s_context);
            Variant xml = reader.ReadRawScalar(BuiltInType.XmlElement, ValueRanks.Scalar, 8, default, s_context);

            Assert.Multiple(() =>
            {
                Assert.That(text.TryGetValue(out string? decodedText), Is.True);
                Assert.That(decodedText, Is.EqualTo("ua"));
                Assert.That(bytes.TryGetValue(out ByteString decodedBytes), Is.True);
                Assert.That(decodedBytes.Span.ToArray(), Is.EqualTo(s_expectedByteString));
                Assert.That(xml.TryGetValue(out XmlElement decodedXml), Is.True);
                Assert.That(decodedXml.OuterXml, Is.EqualTo("<x />").Or.EqualTo("<x/>"));
            });
        }

        [Test]
        public void PaddedPrimitiveArraysRoundTripAndPadDefaults()
        {
            VerifyPaddedArray(BuiltInType.Boolean, new Variant(new ArrayOf<bool>(s_boolValues.AsMemory())), 4);
            VerifyPaddedArray(BuiltInType.SByte, new Variant(new ArrayOf<sbyte>(s_sbyteValues.AsMemory())), 4);
            VerifyPaddedArray(BuiltInType.Byte, new Variant(new ArrayOf<byte>(s_byteValues.AsMemory())), 4);
            VerifyPaddedArray(BuiltInType.Int16, new Variant(new ArrayOf<short>(s_int16Values.AsMemory())), 4);
            VerifyPaddedArray(BuiltInType.UInt16, new Variant(new ArrayOf<ushort>(s_uint16Values.AsMemory())), 4);
            VerifyPaddedArray(BuiltInType.Int32, new Variant(new ArrayOf<int>(s_int32Values.AsMemory())), 4);
            VerifyPaddedArray(BuiltInType.UInt32, new Variant(new ArrayOf<uint>(s_uint32Values.AsMemory())), 4);
            VerifyPaddedArray(BuiltInType.Int64, new Variant(new ArrayOf<long>(s_int64Values.AsMemory())), 4);
            VerifyPaddedArray(BuiltInType.UInt64, new Variant(new ArrayOf<ulong>(s_uint64Values.AsMemory())), 4);
            VerifyPaddedArray(BuiltInType.Float, new Variant(new ArrayOf<float>(s_floatValues.AsMemory())), 4);
            VerifyPaddedArray(BuiltInType.Double, new Variant(new ArrayOf<double>(s_doubleValues.AsMemory())), 4);
        }

        [Test]
        public void PaddedStringAndByteStringArraysRoundTrip()
        {
            VerifyPaddedArray(
                BuiltInType.String,
                new Variant(new ArrayOf<string>(s_paddedStrings.AsMemory())),
                3,
                maxStringLength: 4);
            VerifyPaddedArray(
                BuiltInType.ByteString,
                new Variant(new ArrayOf<ByteString>(s_paddedByteStrings.AsMemory())),
                3,
                maxStringLength: 4);
        }

        [Test]
        public void RawArrayFallbackRoundTripsLengthPrefixedArrays()
        {
            byte[] buffer = new byte[4096];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);

            writer.WriteRawScalar(
                new Variant(new ArrayOf<int>(s_expectedInts.AsMemory())),
                BuiltInType.Int32,
                ValueRanks.OneDimension,
                s_context);
            writer.WriteRawScalar(
                new Variant(new ArrayOf<string>(s_expectedStrings.AsMemory())),
                BuiltInType.String,
                ValueRanks.OneDimension,
                s_context);
            writer.WriteRawScalar(
                new Variant(new ArrayOf<Variant>(s_variantValues.AsMemory())),
                BuiltInType.Variant,
                ValueRanks.OneDimension,
                s_context);

            var reader = new UadpBinaryReader(buffer, 0, writer.Position);
            Variant ints = reader.ReadRawScalar(BuiltInType.Int32, ValueRanks.OneDimension, s_context);
            Variant strings = reader.ReadRawScalar(BuiltInType.String, ValueRanks.OneDimension, s_context);
            Variant variants = reader.ReadRawScalar(BuiltInType.Variant, ValueRanks.OneDimension, s_context);

            Assert.Multiple(() =>
            {
                Assert.That(ints.TryGetValue(out ArrayOf<int> intArray), Is.True);
                Assert.That(intArray.ToArray(), Is.EqualTo(s_expectedInts));
                Assert.That(strings.TryGetValue(out ArrayOf<string> stringArray), Is.True);
                Assert.That(stringArray.ToArray(), Is.EqualTo(s_expectedStrings));
                Assert.That(variants.TryGetValue(out ArrayOf<Variant> variantArray), Is.True);
                Assert.That(variantArray.Count, Is.EqualTo(2));
                Assert.That(reader.Remaining, Is.Zero);
            });
        }

        [Test]
        public void RawEncodingRejectsInvalidBoundsAndNullContext()
        {
            byte[] buffer = new byte[16];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new UadpBinaryWriter(null!, 0, 0),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => new UadpBinaryWriter(buffer, -1, 1),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new UadpBinaryWriter(buffer, 0, 17),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new UadpBinaryReader(null!, 0, 0),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => new UadpBinaryReader(buffer, -1, 1),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new UadpBinaryReader(buffer, 0, 17),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => writer.Advance(-1),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => writer.WriteVariant(new Variant(1), null!),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => writer.WriteDataValue(new DataValue(new Variant(1)), null!),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => writer.WriteRawScalar(
                        new Variant(new ArrayOf<int>(s_overflowValues.AsMemory())),
                        BuiltInType.Int32,
                        ValueRanks.OneDimension,
                        maxStringLength: 0,
                        arrayDimensions: new ArrayOf<uint>(s_overflowDimensions.AsMemory()),
                        s_context),
                    Throws.TypeOf<ArgumentException>());
            });

            var reader = new UadpBinaryReader(buffer, 0, buffer.Length);
            Assert.Multiple(() =>
            {
                Assert.That(() => reader.Position = 17, Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => reader.Advance(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => reader.Advance(17), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => reader.ReadVariant(null!), Throws.TypeOf<ArgumentNullException>());
                Assert.That(() => reader.ReadDataValue(null!), Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => reader.ReadRawScalar(BuiltInType.Int32, ValueRanks.Scalar, null!),
                    Throws.TypeOf<ArgumentNullException>());
            });
        }

        private static void VerifyPaddedArray(
            BuiltInType builtInType,
            Variant value,
            int expectedCount,
            uint maxStringLength = 0)
        {
            byte[] buffer = new byte[4096];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            var dimensions = new ArrayOf<uint>(new[] { (uint)expectedCount }.AsMemory());

            writer.WriteRawScalar(
                value,
                builtInType,
                ValueRanks.OneDimension,
                maxStringLength,
                dimensions,
                s_context);

            var reader = new UadpBinaryReader(buffer, 0, writer.Position);
            Variant decoded = reader.ReadRawScalar(
                builtInType,
                ValueRanks.OneDimension,
                maxStringLength,
                dimensions,
                s_context);

            Assert.That(decoded.IsNull, Is.False);
            Assert.That(reader.Remaining, Is.Zero);
        }

        private static void AssertDecodedValue(Variant decoded, BuiltInType builtInType, object expected)
        {
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                    Assert.That(decoded.TryGetValue(out bool b), Is.True);
                    Assert.That(b, Is.EqualTo(expected));
                    break;
                case BuiltInType.SByte:
                    Assert.That(decoded.TryGetValue(out sbyte sb), Is.True);
                    Assert.That(sb, Is.EqualTo(expected));
                    break;
                case BuiltInType.Byte:
                    Assert.That(decoded.TryGetValue(out byte by), Is.True);
                    Assert.That(by, Is.EqualTo(expected));
                    break;
                case BuiltInType.Int16:
                    Assert.That(decoded.TryGetValue(out short i16), Is.True);
                    Assert.That(i16, Is.EqualTo(expected));
                    break;
                case BuiltInType.UInt16:
                    Assert.That(decoded.TryGetValue(out ushort u16), Is.True);
                    Assert.That(u16, Is.EqualTo(expected));
                    break;
                case BuiltInType.Int32:
                    Assert.That(decoded.TryGetValue(out int i32), Is.True);
                    Assert.That(i32, Is.EqualTo(expected));
                    break;
                case BuiltInType.UInt32:
                    Assert.That(decoded.TryGetValue(out uint u32), Is.True);
                    Assert.That(u32, Is.EqualTo(expected));
                    break;
                case BuiltInType.Int64:
                    Assert.That(decoded.TryGetValue(out long i64), Is.True);
                    Assert.That(i64, Is.EqualTo(expected));
                    break;
                case BuiltInType.UInt64:
                    Assert.That(decoded.TryGetValue(out ulong u64), Is.True);
                    Assert.That(u64, Is.EqualTo(expected));
                    break;
                case BuiltInType.Float:
                    Assert.That(decoded.TryGetValue(out float f), Is.True);
                    Assert.That(f, Is.EqualTo(expected));
                    break;
                case BuiltInType.Double:
                    Assert.That(decoded.TryGetValue(out double d), Is.True);
                    Assert.That(d, Is.EqualTo(expected));
                    break;
                case BuiltInType.String:
                    Assert.That(decoded.TryGetValue(out string? s), Is.True);
                    Assert.That(s, Is.EqualTo(expected));
                    break;
                case BuiltInType.ByteString:
                    Assert.That(decoded.TryGetValue(out ByteString bs), Is.True);
                    Assert.That(bs.Span.ToArray(), Is.EqualTo(((ByteString)expected).Span.ToArray()));
                    break;
                case BuiltInType.XmlElement:
                    Assert.That(decoded.TryGetValue(out XmlElement xml), Is.True);
                    Assert.That(xml.OuterXml, Is.EqualTo(((XmlElement)expected).OuterXml));
                    break;
                case BuiltInType.DateTime:
                    Assert.That(decoded.TryGetValue(out DateTimeUtc dt), Is.True);
                    Assert.That(dt, Is.EqualTo(expected));
                    break;
                case BuiltInType.Guid:
                    Assert.That(decoded.TryGetValue(out Uuid guid), Is.True);
                    Assert.That(guid, Is.EqualTo(expected));
                    break;
                case BuiltInType.NodeId:
                    Assert.That(decoded.TryGetValue(out NodeId nodeId), Is.True);
                    Assert.That(nodeId, Is.EqualTo(expected));
                    break;
                case BuiltInType.ExpandedNodeId:
                    Assert.That(decoded.TryGetValue(out ExpandedNodeId expandedNodeId), Is.True);
                    Assert.That(expandedNodeId, Is.EqualTo(expected));
                    break;
                case BuiltInType.StatusCode:
                    Assert.That(decoded.TryGetValue(out StatusCode statusCode), Is.True);
                    Assert.That(statusCode, Is.EqualTo(expected));
                    break;
                case BuiltInType.QualifiedName:
                    Assert.That(decoded.TryGetValue(out QualifiedName qualifiedName), Is.True);
                    Assert.That(qualifiedName, Is.EqualTo(expected));
                    break;
                case BuiltInType.LocalizedText:
                    Assert.That(decoded.TryGetValue(out LocalizedText localizedText), Is.True);
                    Assert.That(localizedText, Is.EqualTo(expected));
                    break;
                case BuiltInType.DataValue:
                    Assert.That(decoded.TryGetValue(out DataValue dataValue), Is.True);
                    Assert.That(dataValue.WrappedValue.TryGetValue(out int dataValueNumber), Is.True);
                    Assert.That(dataValueNumber, Is.EqualTo(42));
                    break;
                case BuiltInType.ExtensionObject:
                    Assert.That(decoded.TryGetValue(out ExtensionObject extensionObject), Is.True);
                    Assert.That(extensionObject.TypeId.IsNull, Is.False);
                    break;
                default:
                    Assert.That(decoded.IsNull, Is.False);
                    break;
            }
        }
    }
}
