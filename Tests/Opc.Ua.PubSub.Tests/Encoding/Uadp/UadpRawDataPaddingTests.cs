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
 *
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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using SysText = System.Text;
using UadpDataSetMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Encoding.Uadp
{
    /// <summary>
    /// Validates Part 14 v1.05.06 §7.2.4.5.11 RawData field padding:
    /// String / ByteString / XmlElement scalars are padded to
    /// MaxStringLength, arrays are padded to product(ArrayDimensions),
    /// the length prefix is suppressed, and decoders trim trailing NUL
    /// fill. Regression coverage for GitHub issue #3566.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.5.11")]
    public class UadpRawDataPaddingTests
    {
        [TestCase(0)]
        [TestCase(3)]
        [TestCase(7)]
        [TestCase(10)]
        [TestSpec("7.2.4.5.11")]
        public void String_WithMaxStringLength10_AlwaysEmits10Bytes(int payloadLength)
        {
            string payload = new('x', payloadLength);
            byte[] buffer = new byte[64];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(null!);

            writer.WriteRawScalar(
                (Variant)payload,
                BuiltInType.String,
                ValueRanks.Scalar,
                maxStringLength: 10,
                arrayDimensions: default,
                context);

            Assert.That(writer.Position, Is.EqualTo(10),
                "RawData padded String must emit exactly MaxStringLength bytes.");
            ReadOnlySpan<byte> written = writer.WrittenSpan();
            for (int i = 0; i < payloadLength; i++)
            {
                Assert.That(written[i], Is.EqualTo((byte)'x'));
            }
            for (int i = payloadLength; i < 10; i++)
            {
                Assert.That(written[i], Is.Zero,
                    $"Padding byte at index {i} must be NUL.");
            }
        }

        [Test]
        [TestSpec("7.2.4.5.11")]
        public void String_ExceedingMaxStringLength_Throws()
        {
            byte[] buffer = new byte[64];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(null!);

            Assert.That(
                () => writer.WriteRawScalar(
                    (Variant)"0123456789X",
                    BuiltInType.String,
                    ValueRanks.Scalar,
                    maxStringLength: 10,
                    arrayDimensions: default,
                    context),
                Throws.TypeOf<ArgumentException>(),
                "Payload exceeding MaxStringLength must throw ArgumentException.");
        }

        [Test]
        [TestSpec("7.2.4.5.11")]
        public void String_RoundTrip_TrimsTrailingNulsOnDecode()
        {
            byte[] buffer = new byte[64];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(null!);

            writer.WriteRawScalar(
                (Variant)"hello",
                BuiltInType.String,
                ValueRanks.Scalar,
                maxStringLength: 10,
                arrayDimensions: default,
                context);

            var reader = new UadpBinaryReader(buffer, 0, writer.Position);
            Variant decoded = reader.ReadRawScalar(
                BuiltInType.String,
                ValueRanks.Scalar,
                maxStringLength: 10,
                arrayDimensions: default,
                context);

            Assert.That(decoded.TryGetValue(out string? text), Is.True);
            Assert.That(text, Is.EqualTo("hello"));
            Assert.That(reader.Position, Is.EqualTo(10));
        }

        [Test]
        [TestSpec("7.2.4.5.11")]
        public void ByteString_WithMaxLength16_AlwaysEmits16Bytes()
        {
            byte[] payload = [0xDE, 0xAD, 0xBE, 0xEF];
            byte[] buffer = new byte[64];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(null!);

            writer.WriteRawScalar(
                (Variant)new ByteString(payload),
                BuiltInType.ByteString,
                ValueRanks.Scalar,
                maxStringLength: 16,
                arrayDimensions: default,
                context);

            Assert.That(writer.Position, Is.EqualTo(16));
            ReadOnlySpan<byte> written = writer.WrittenSpan();
            Assert.That(written[0], Is.EqualTo((byte)0xDE));
            Assert.That(written[1], Is.EqualTo((byte)0xAD));
            Assert.That(written[2], Is.EqualTo((byte)0xBE));
            Assert.That(written[3], Is.EqualTo((byte)0xEF));
            for (int i = 4; i < 16; i++)
            {
                Assert.That(written[i], Is.Zero);
            }
        }

        [Test]
        [TestSpec("7.2.4.5.11")]
        public void ByteString_RoundTrip_TrimsTrailingNuls()
        {
            byte[] payload = [1, 2, 3, 4, 5];
            byte[] buffer = new byte[64];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(null!);

            writer.WriteRawScalar(
                (Variant)new ByteString(payload),
                BuiltInType.ByteString,
                ValueRanks.Scalar,
                maxStringLength: 16,
                arrayDimensions: default,
                context);

            var reader = new UadpBinaryReader(buffer, 0, writer.Position);
            Variant decoded = reader.ReadRawScalar(
                BuiltInType.ByteString,
                ValueRanks.Scalar,
                maxStringLength: 16,
                arrayDimensions: default,
                context);

            Assert.That(decoded.TryGetValue(out ByteString result), Is.True);
            Assert.That(result.IsNull, Is.False);
            byte[] resultBytes = result.Span.ToArray();
            Assert.That(resultBytes, Is.EqualTo(payload),
                "ByteString round-trip must trim trailing NUL fill.");
        }

        [Test]
        [TestSpec("7.2.4.5.11")]
        public void XmlElement_WithMaxStringLength64_AlwaysEmits64Bytes()
        {
            const string xml = "<a/>";
            byte[] buffer = new byte[128];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(null!);

            writer.WriteRawScalar(
                (Variant)XmlElement.From(xml),
                BuiltInType.XmlElement,
                ValueRanks.Scalar,
                maxStringLength: 64,
                arrayDimensions: default,
                context);

            Assert.That(writer.Position, Is.EqualTo(64));
            ReadOnlySpan<byte> written = writer.WrittenSpan();
            int xmlLen = SysText.Encoding.UTF8.GetByteCount(xml);
            for (int i = xmlLen; i < 64; i++)
            {
                Assert.That(written[i], Is.Zero);
            }

            var reader = new UadpBinaryReader(buffer, 0, writer.Position);
            Variant decoded = reader.ReadRawScalar(
                BuiltInType.XmlElement,
                ValueRanks.Scalar,
                maxStringLength: 64,
                arrayDimensions: default,
                context);
            Assert.That(decoded.TryGetValue(out XmlElement decodedXml), Is.True);
            Assert.That(decodedXml.IsNull, Is.False);
            Assert.That(decodedXml.OuterXml, Is.EqualTo(xml));
        }

        [Test]
        [TestSpec("7.2.4.5.11")]
        public void Int32Array_WithArrayDimensions3_AlwaysEmits12Bytes()
        {
            int[] payload = [1, 2];
            uint[] arrayDimensions = [3u];
            byte[] buffer = new byte[64];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(null!);

            writer.WriteRawScalar(
                (Variant)new ArrayOf<int>(payload),
                BuiltInType.Int32,
                ValueRanks.OneDimension,
                maxStringLength: 0,
                arrayDimensions: new ArrayOf<uint>(arrayDimensions),
                context);

            Assert.That(writer.Position, Is.EqualTo(12),
                "RawData padded Int32 array must emit 3 * sizeof(Int32) bytes.");

            var reader = new UadpBinaryReader(buffer, 0, writer.Position);
            Variant decoded = reader.ReadRawScalar(
                BuiltInType.Int32,
                ValueRanks.OneDimension,
                maxStringLength: 0,
                arrayDimensions: new ArrayOf<uint>(arrayDimensions),
                context);
            Assert.That(decoded.TryGetValue(out ArrayOf<int> arr), Is.True);
            Assert.That(arr.Count, Is.EqualTo(3));
            Assert.That(arr[0], Is.EqualTo(1));
            Assert.That(arr[1], Is.EqualTo(2));
            Assert.That(arr[2], Is.Zero,
                "Missing element must be padded with default value 0.");
        }

        [Test]
        [TestSpec("7.2.4.5.11")]
        public void Int32Array_ExceedingArrayDimensions_Throws()
        {
            int[] payload = [1, 2, 3, 4];
            uint[] arrayDimensions = [3u];
            byte[] buffer = new byte[64];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(null!);

            Assert.That(
                () => writer.WriteRawScalar(
                    (Variant)new ArrayOf<int>(payload),
                    BuiltInType.Int32,
                    ValueRanks.OneDimension,
                    maxStringLength: 0,
                    arrayDimensions: new ArrayOf<uint>(arrayDimensions),
                    context),
                Throws.TypeOf<ArgumentException>(),
                "Array longer than product(ArrayDimensions) must throw ArgumentException.");
        }

        [Test]
        [TestSpec("7.2.4.5")]
        public void PaddedStringArrayHugeCountShortBufferThrowsBoundsException()
        {
            byte[] buffer = [0];
            var reader = new UadpBinaryReader(buffer, 0, buffer.Length);
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(null!);
            uint[] dimensions = [int.MaxValue];
            var arrayDimensions = new ArrayOf<uint>(dimensions);

            Assert.That(
                () => reader.ReadRawScalar(
                    BuiltInType.String,
                    ValueRanks.OneDimension,
                    maxStringLength: 1,
                    arrayDimensions,
                    context),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Padded RawData payload is truncated"));
        }

        [Test]
        [TestSpec("7.2.4.5")]
        public void PaddedByteStringArrayHugeCountShortBufferThrowsBoundsException()
        {
            byte[] buffer = [0];
            var reader = new UadpBinaryReader(buffer, 0, buffer.Length);
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(null!);
            uint[] dimensions = [int.MaxValue];
            var arrayDimensions = new ArrayOf<uint>(dimensions);

            Assert.That(
                () => reader.ReadRawScalar(
                    BuiltInType.ByteString,
                    ValueRanks.OneDimension,
                    maxStringLength: 1,
                    arrayDimensions,
                    context),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Padded RawData payload is truncated"));
        }

        [Test]
        [TestSpec("7.2.4.5.11", Summary = "Direct repro of issue #3566")]
        public async Task Issue3566_DirectRepro()
        {
            int[] sizes = new int[3];
            string[] payloads = ["hi", "hello", "hello!"];

            for (int i = 0; i < payloads.Length; i++)
            {
                ReadOnlyMemory<byte> encoded =
                    await EncodeSingleStringFieldRawDataAsync(
                        payloads[i], maxStringLength: 10).ConfigureAwait(false);
                sizes[i] = encoded.Length;
            }

            Assert.That(sizes[0], Is.EqualTo(sizes[1]),
                $"Issue #3566: encoded payload sizes differ between '{payloads[0]}' " +
                $"({sizes[0]}) and '{payloads[1]}' ({sizes[1]}).");
            Assert.That(sizes[1], Is.EqualTo(sizes[2]),
                $"Issue #3566: encoded payload sizes differ between '{payloads[1]}' " +
                $"({sizes[1]}) and '{payloads[2]}' ({sizes[2]}).");
        }

        [Test]
        [TestSpec("7.2.4.5.11")]
        public void WithoutMaxStringLength_FallsBackToLengthPrefix()
        {
            byte[] buffer = new byte[64];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(null!);

            writer.WriteRawScalar(
                (Variant)"hi",
                BuiltInType.String,
                ValueRanks.Scalar,
                maxStringLength: 0,
                arrayDimensions: default,
                context);

            Assert.That(writer.Position, Is.EqualTo(6),
                "Legacy fallback writes 4-byte length prefix + UTF-8 payload.");
            ReadOnlySpan<byte> written = writer.WrittenSpan();
            Assert.That(written[0], Is.EqualTo((byte)2));
            Assert.That(written[1], Is.Zero);
            Assert.That(written[2], Is.Zero);
            Assert.That(written[3], Is.Zero);
            Assert.That(written[4], Is.EqualTo((byte)'h'));
            Assert.That(written[5], Is.EqualTo((byte)'i'));
        }

        [Test]
        [TestSpec("7.2.4.5.11")]
        public void WithoutArrayDimensions_FallsBackToLengthPrefix()
        {
            int[] payload = [10, 20];
            byte[] buffer = new byte[64];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(null!);

            writer.WriteRawScalar(
                (Variant)new ArrayOf<int>(payload),
                BuiltInType.Int32,
                ValueRanks.OneDimension,
                maxStringLength: 0,
                arrayDimensions: default,
                context);

            Assert.That(writer.Position, Is.EqualTo(4 + 2 * 4),
                "Legacy fallback writes 4-byte length prefix + N * sizeof(Int32).");
            ReadOnlySpan<byte> written = writer.WrittenSpan();
            Assert.That(written[0], Is.EqualTo((byte)2));
            Assert.That(written[1], Is.Zero);
            Assert.That(written[2], Is.Zero);
            Assert.That(written[3], Is.Zero);
        }

        [Test]
        [TestSpec("7.2.4.5.11")]
        public void DeltaFrameRawDataPaddedFieldsThrows()
        {
            var registry = new DataSetMetaDataRegistry();
            PubSubNetworkMessageContext context =
                UadpTestUtilities.NewContext(registry);

            var publisherId = PublisherId.FromByte(1);
            const ushort writerGroupId = 1;
            const ushort writerId = 100;
            var classId = (Uuid)Guid.Empty;
            const uint majorVer = 1;

            var meta = new DataSetMetaDataType
            {
                Name = "DeltaPaddedMeta",
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = majorVer,
                    MinorVersion = 0
                },
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "f0",
                        BuiltInType = (byte)BuiltInType.String,
                        ValueRank = ValueRanks.Scalar,
                        MaxStringLength = 10
                    }
                ]
            };
            registry.Register(
                new DataSetMetaDataKey(publisherId, writerGroupId, writerId,
                    classId, majorVer),
                meta);

            var msg = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = publisherId,
                WriterGroupId = writerGroupId,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = writerId,
                        ContentMask = UadpDataSetMessageContentMask.MajorVersion,
                        MetaDataVersion = new ConfigurationVersionDataType
                        {
                            MajorVersion = majorVer, MinorVersion = 0
                        },
                        FieldEncoding = PubSubFieldEncoding.RawData,
                        MessageType = PubSubDataSetMessageType.DeltaFrame,
                        Fields =
                        [
                            new DataSetField
                            {
                                Value = (Variant)"delta"
                            }
                        ]
                    }
                ]
            };

            Assert.That(
                async () => await new UadpEncoder().EncodeAsync(msg, context).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("RawData"),
                "Part 14 §7.2.4.5.11 restricts RawData to Data Key Frame DataSetMessages.");
        }

        private static async Task<ReadOnlyMemory<byte>>
            EncodeSingleStringFieldRawDataAsync(
                string payload, uint maxStringLength)
        {
            var registry = new DataSetMetaDataRegistry();
            PubSubNetworkMessageContext context =
                UadpTestUtilities.NewContext(registry);

            var publisherId = PublisherId.FromByte(1);
            const ushort writerGroupId = 1;
            const ushort writerId = 100;
            var classId = (Uuid)Guid.Empty;
            const uint majorVer = 1;

            var meta = new DataSetMetaDataType
            {
                Name = "Issue3566Meta",
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = majorVer,
                    MinorVersion = 0
                },
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "stringField",
                        BuiltInType = (byte)BuiltInType.String,
                        ValueRank = ValueRanks.Scalar,
                        MaxStringLength = maxStringLength
                    }
                ]
            };
            registry.Register(
                new DataSetMetaDataKey(publisherId, writerGroupId, writerId,
                    classId, majorVer),
                meta);

            var msg = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = publisherId,
                WriterGroupId = writerGroupId,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = writerId,
                        ContentMask = UadpDataSetMessageContentMask.MajorVersion,
                        MetaDataVersion = new ConfigurationVersionDataType
                        {
                            MajorVersion = majorVer, MinorVersion = 0
                        },
                        FieldEncoding = PubSubFieldEncoding.RawData,
                        Fields = [new DataSetField { Value = (Variant)payload }]
                    }
                ]
            };
            return await new UadpEncoder().EncodeAsync(msg, context).ConfigureAwait(false);
        }
    }
}
