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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.Tests.Encoding.Uadp
{
    [TestFixture]
    public sealed class UadpRawMatrixTests
    {
        /// <summary>
        /// Null matrix cells keep their String type and remain distinct from empty strings with either wire layout.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void StringMatrixPreservesNullAndEmptyCells(bool padded)
        {
            byte[] buffer = new byte[256];
            var context = ServiceMessageContext.CreateEmpty(null!);
            string[] values = [null!, "x", string.Empty, null!];
            var expected = Variant.From(values.ToMatrixOf([2, 2]));
            uint maximum = padded ? 8u : 0u;
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            writer.WriteRawScalar(
                expected,
                BuiltInType.String,
                2,
                maximum,
                [2u, 2u],
                context);
            writer.WriteUInt32Le(0x12345678u);
            var reader = new UadpBinaryReader(buffer, 0, writer.Position);

            Variant actual = reader.ReadRawScalar(
                BuiltInType.String,
                2,
                maximum,
                [2u, 2u],
                context);

            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(reader.TryReadUInt32Le(out uint following), Is.True);
            Assert.That(following, Is.EqualTo(0x12345678u));
            Assert.That(reader.Remaining, Is.Zero);
        }

        /// <summary>
        /// Matrix overflow uses the same retry signal as other RawData fields.
        /// </summary>
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(4096)]
        public void MatrixOverflowReportsTheWriterBufferLimit(int capacity)
        {
            byte[] buffer = new byte[capacity];
            var context = ServiceMessageContext.CreateEmpty(null!);
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            int[] values = Enumerable.Range(0, 2048).ToArray();
            var matrix = Variant.From(values.ToMatrixOf([32, 64]));

            Assert.That(
                () => writer.WriteRawScalar(
                    matrix,
                    BuiltInType.Int32,
                    2,
                    context),
                Throws.TypeOf<UadpBufferFullException>());
        }

        /// <summary>
        /// The NetworkMessage encoder grows its initial buffer and preserves the complete matrix payload.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LargeMatrixGrowsTheNetworkBufferAndRoundTripsAsync(bool padded)
        {
            var registry = new DataSetMetaDataRegistry();
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext(registry);
            var publisherId = PublisherId.FromByte(1);
            var version = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 };
            registry.Register(
                new DataSetMetaDataKey(publisherId, 1, 100, Uuid.Empty, 1),
                new DataSetMetaDataType
                {
                    Name = "LargeMatrix",
                    ConfigurationVersion = version,
                    Fields =
                    [
                        new FieldMetaData
                        {
                            Name = "Matrix",
                            BuiltInType = (byte)BuiltInType.Int32,
                            ValueRank = 2,
                            ArrayDimensions = padded ? [33u, 64u] : []
                        },
                        new FieldMetaData
                        {
                            Name = "Following",
                            BuiltInType = (byte)BuiltInType.UInt32,
                            ValueRank = ValueRanks.Scalar
                        }
                    ]
                });
            int[] values = Enumerable.Range(0, 2048).ToArray();
            var matrix = Variant.From(values.ToMatrixOf([32, 64]));
            var message = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = publisherId,
                WriterGroupId = 1,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 100,
                        ContentMask = UadpDataSetMessageContentMask.MajorVersion,
                        MetaDataVersion = version,
                        FieldEncoding = PubSubFieldEncoding.RawData,
                        Fields =
                        [
                            new DataSetField { Value = matrix },
                            new DataSetField { Value = Variant.From(0x12345678u) }
                        ]
                    }
                ]
            };

            ReadOnlyMemory<byte> encoded = await new UadpEncoder().EncodeAsync(message, context)
                .ConfigureAwait(false);
            Assert.That(encoded.Length, Is.GreaterThan(4096));
            var decoded = UadpDecoder.Decode(encoded, context) as UadpNetworkMessage;
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded!.DataSetMessages.Count, Is.EqualTo(1));
            Assert.That(decoded.DataSetMessages[0].Fields.Count, Is.EqualTo(2));
            Assert.Multiple(
                () =>
                {
                    Assert.That(decoded.DataSetMessages[0].Fields[0].Value, Is.EqualTo(matrix));
                    Assert.That(decoded.DataSetMessages[0].Fields[1].Value, Is.EqualTo(Variant.From(0x12345678u)));
                });
        }

        [TestCase(BuiltInType.DateTime)]
        [TestCase(BuiltInType.Guid)]
        [TestCase(BuiltInType.ByteString)]
        [TestCase(BuiltInType.NodeId)]
        [TestCase(BuiltInType.StatusCode)]
        public void NonNumericArraysUseThePart6LengthPrefixWithoutAVariantEnvelope(BuiltInType type)
        {
            var context = ServiceMessageContext.CreateEmpty(null!);
            byte[] buffer = new byte[256];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            Variant value = type switch
            {
                BuiltInType.DateTime => Variant.From(
                    [new DateTimeUtc(2026, 10, 10), new DateTimeUtc(2026, 10, 11)]),
                BuiltInType.Guid => Variant.From(
                    [new Uuid("6b128cc8-4159-42c8-b650-faf208f1e272"), Uuid.Empty]),
                BuiltInType.ByteString => Variant.From(
                    [ByteString.From([1, 0, 2]), default]),
                BuiltInType.NodeId => Variant.From([new NodeId(42u), new NodeId("value", 1)]),
                BuiltInType.StatusCode => Variant.From([StatusCodes.Good, StatusCodes.BadNoData]),
                _ => throw new ArgumentOutOfRangeException(nameof(type))
            };
            writer.WriteRawScalar(value, type, ValueRanks.OneDimension, context);
            Assert.That(
                System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(writer.WrittenSpan()),
                Is.EqualTo(2),
                "The first bytes must be the array length, not a Variant type tag.");
            var reader = new UadpBinaryReader(buffer, 0, writer.Position);
            Assert.That(reader.ReadRawScalar(type, ValueRanks.OneDimension, context), Is.EqualTo(value));
            Assert.That(reader.Remaining, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MatrixPreservesActualDimensionsAndTheFollowingField(bool padded)
        {
            byte[] buffer = new byte[128];
            var context = ServiceMessageContext.CreateEmpty(null!);
            int[] numbers = [11, 12, 21, 22];
            var matrix = Variant.From(numbers.ToMatrixOf([2, 2]));
            ArrayOf<uint> maxima = padded ? [2u, 3u] : [];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            writer.WriteRawScalar(matrix, BuiltInType.Int32, 2, 0, maxima, context);
            Assert.That(writer.Position, Is.EqualTo(padded ? 36 : 28));
            writer.WriteUInt32Le(0x12345678u);
            var reader = new UadpBinaryReader(buffer, 0, writer.Position);
            Variant actual = reader.ReadRawScalar(BuiltInType.Int32, 2, 0, maxima, context);
            Assert.That(actual, Is.EqualTo(matrix));
            Assert.That(reader.TryReadUInt32Le(out uint following), Is.True);
            Assert.That(following, Is.EqualTo(0x12345678u));
            Assert.That(reader.Remaining, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EmptyAndNullMatricesRemainDistinctWithPadding(bool isNull)
        {
            byte[] buffer = new byte[128];
            var context = ServiceMessageContext.CreateEmpty(null!);
            Variant matrix = isNull ? Variant.From(MatrixOf<int>.Null)
                : Variant.From(Array.Empty<int>().ToMatrixOf([0, 3]));
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            writer.WriteRawScalar(matrix, BuiltInType.Int32, 2, 0, [2u, 3u], context);
            var reader = new UadpBinaryReader(buffer, 0, writer.Position);
            Variant actual = reader.ReadRawScalar(BuiltInType.Int32, 2, 0, [2u, 3u], context);
            Assert.That(actual.TryGetValue(out MatrixOf<int> value), Is.True);
            Assert.That(value.IsNull, Is.EqualTo(isNull));
            if (!isNull)
            {
                Assert.That(value.Dimensions, Is.EqualTo([0, 3]));
            }
            Assert.That(reader.Remaining, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MatrixElementsRetainStringAndByteStringPadding(bool binary)
        {
            byte[] buffer = new byte[128];
            var context = ServiceMessageContext.CreateEmpty(null!);
            string[] strings = ["hello", "world"];
            ByteString[] bytes = [ByteString.From([1, 0, 2]), ByteString.From([3, 0, 4])];
            Variant matrix = binary ? Variant.From(bytes.ToMatrixOf([1, 2]))
                : Variant.From(strings.ToMatrixOf([1, 2]));
            BuiltInType type = binary ? BuiltInType.ByteString : BuiltInType.String;
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            writer.WriteRawScalar(matrix, type, 2, 8, [2u, 2u], context);
            Assert.That(writer.Position, Is.EqualTo(60));
            var reader = new UadpBinaryReader(buffer, 0, writer.Position);
            Assert.That(reader.ReadRawScalar(type, 2, 8, [2u, 2u], context), Is.EqualTo(matrix));
            Assert.That(reader.Remaining, Is.Zero);
        }

        [Test]
        public void FlatOrWrongTypeInputCannotBeSilentlyReinterpretedAsAMatrix()
        {
            byte[] buffer = new byte[128];
            var context = ServiceMessageContext.CreateEmpty(null!);
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            Assert.That(
                () => writer.WriteRawScalar(
                    Variant.From([1, 2, 3, 4]),
                    BuiltInType.Int32,
                    2,
                    0,
                    [2u, 2u],
                    context),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(writer.Position, Is.Zero);
            double[] values = [1, 2, 3, 4];
            Assert.That(
                () => writer.WriteRawScalar(
                    Variant.From(values.ToMatrixOf([2, 2])),
                    BuiltInType.Int32,
                    2,
                    0,
                    [2u, 2u],
                    context),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(writer.Position, Is.Zero);
        }

        [TestCase("rank")]
        [TestCase("dimension")]
        [TestCase("truncated")]
        [TestCase("quota")]
        public void InvalidMatrixHeadersAndLimitsAreRejected(string fault)
        {
            byte[] buffer = CoreUtils.FromHexString(
                "020000000200000003000000010000000200000003000000040000000500000006000000");
            var context = ServiceMessageContext.CreateEmpty(null!);
            switch (fault)
            {
                case "rank":
                    buffer[0] = 1;
                    break;
                case "dimension":
                    buffer[4] = 3;
                    break;
                case "quota":
                    context.MaxArrayLength = 5;
                    break;
            }
            var reader = new UadpBinaryReader(buffer, 0, fault == "truncated" ? 15 : buffer.Length);
            Assert.That(
                () => reader.ReadRawScalar(BuiltInType.Int32, 2, 0, [2u, 3u], context),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void MatrixWireLayoutIsDimensionsThenValuesWithoutAnElementCount()
        {
            byte[] buffer = new byte[128];
            var context = ServiceMessageContext.CreateEmpty(null!);
            int[] numbers = [1, 2, 3, 4, 5, 6];
            var matrix = Variant.From(numbers.ToMatrixOf([2, 3]));
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            writer.WriteRawScalar(matrix, BuiltInType.Int32, 2, 0, [2u, 3u], context);
            byte[] expected = CoreUtils.FromHexString(
                "020000000200000003000000010000000200000003000000040000000500000006000000");
            Assert.That(writer.WrittenSpan().ToArray(), Is.EqualTo(expected));
            var reader = new UadpBinaryReader(expected, 0, expected.Length);
            Variant actual = reader.ReadRawScalar(BuiltInType.Int32, 2, 0, [2u, 3u], context);
            Assert.That(actual, Is.EqualTo(matrix));
            Assert.That(actual.TypeInfo.ValueRank, Is.EqualTo(2));
            Assert.That(reader.Remaining, Is.Zero);
        }
    }
}
