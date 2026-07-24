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
#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.Encoding.Tests
{
    /// <summary>
    /// Verifies that Arrow PubSub network messages produce typed record-batch columns and round-trip dataset rows.
    /// </summary>
    [TestFixture]
    public sealed class ArrowPubSubRoundTripTests
    {
        private static readonly double[] FirstSamples = [1.0, 2.0];
        private static readonly double[] SecondSamples = [];
        private static readonly double[] ThirdSamples = [3.5, 4.5, 5.5];
        private static readonly bool[] BooleanArrayValues = [true, false];
        private static readonly sbyte[] SByteArrayValues = [-8, 9];
        private static readonly byte[] ByteArrayValues = [8, 9];
        private static readonly short[] Int16ArrayValues = [-16, 17];
        private static readonly ushort[] UInt16ArrayValues = [16, 17];
        private static readonly int[] Int32ArrayValues = [-32, 33];
        private static readonly uint[] UInt32ArrayValues = [32, 33];
        private static readonly long[] Int64ArrayValues = [-64, 65];
        private static readonly ulong[] UInt64ArrayValues = [64, 65];
        private static readonly float[] FloatArrayValues = [1.25f, 2.5f];
        private static readonly double[] DoubleArrayValues = [2.25, 4.5];
        private static readonly string[] StringArrayValues = ["alpha", null!];
        private static readonly DateTimeUtc DateTimeValue = new(new DateTime(2026, 7, 4, 12, 0, 0, DateTimeKind.Utc));
        private static readonly DateTimeUtc[] DateTimeArrayValues = [new DateTimeUtc(123456789L), default];
        private static readonly Uuid GuidValue = new(new Guid("11112222-3333-4444-5555-666677778888"));
        private static readonly Uuid[] GuidArrayValues =
        [
            new Uuid(new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")),
            Uuid.Empty
        ];
        private static readonly ByteString ByteStringValue = ByteString.From(0xAA, 0xBB, 0xCC);
        private static readonly ByteString[] ByteStringArrayValues = [ByteString.From(0x01, 0x02), default];
        private static readonly StatusCode StatusCodeValue = new(0x80340000u);
        private static readonly StatusCode[] StatusCodeArrayValues = [new StatusCode(0x80340000u), StatusCodes.Good];

        [Test]
        public async Task EncoderProducesTypedColumnarBatchAndDecoderRoundTripsRows()
        {
            PublisherId publisherId = PublisherId.FromString("publisher-arrow");
            Uuid dataSetClassId = new(new Guid("95669f76-285a-41c6-ac2b-27793a3eac10"));
            DataSetMetaDataType metaData = CreateMetaData();
            PubSubNetworkMessageContext context = CreateContext(
                publisherId,
                writerGroupId: 9,
                dataSetClassId,
                metaData,
                dataSetWriterId: 501);

            ArrowNetworkMessage message = new()
            {
                PublisherId = publisherId,
                WriterGroupId = 9,
                DataSetClassId = dataSetClassId,
                SchemaId = "arrow-schema-1",
                MetaData = metaData,
                DataSetMessages =
                [
                    CreateSample(501, 100, 21.5, "pump-a", FirstSamples, true, metaData),
                    CreateSample(501, 101, 22.25, null, SecondSamples, false, metaData),
                    CreateSample(501, 102, 23.75, "pump-c", ThirdSamples, true, metaData)
                ]
            };

            ArrowNetworkMessageEncoder encoder = new() { Framing = ArrowIpcFraming.Stream };
            ReadOnlyMemory<byte> frame = await encoder.EncodeAsync(message, context);

            using (ArrowStreamReader reader = new(new MemoryStream(frame.ToArray(), writable: false)))
            using (RecordBatch? batch = reader.ReadNextRecordBatch())
            {
                Assert.That(batch, Is.Not.Null);
                Assert.That(batch!.Length, Is.EqualTo(message.DataSetMessages.Count));
                Assert.That(batch.Schema.GetFieldByIndex(5).Name, Is.EqualTo("Temperature"));
                Assert.That(batch.Schema.GetFieldByIndex(5).DataType, Is.TypeOf<DoubleType>());
                Assert.That(batch.Schema.GetFieldByIndex(7).DataType, Is.TypeOf<ListType>());
                for (int i = 5; i < batch.ColumnCount; i++)
                {
                    Assert.That(batch.Schema.GetFieldByIndex(i).DataType, Is.Not.TypeOf<BinaryType>());
                }
            }

            ArrowNetworkMessageDecoder decoder = new();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(frame, context);

            Assert.That(decoded, Is.TypeOf<ArrowNetworkMessage>());
            ArrowNetworkMessage decodedMessage = (ArrowNetworkMessage)decoded!;
            Assert.That(decodedMessage.TransportProfileUri, Is.EqualTo(ArrowNetworkMessage.PubSubMqttArrowTransport));
            Assert.That(decodedMessage.PublisherId, Is.EqualTo(message.PublisherId));
            Assert.That(decodedMessage.WriterGroupId, Is.EqualTo(message.WriterGroupId));
            Assert.That(decodedMessage.DataSetMessages.Count, Is.EqualTo(3));

            for (int i = 0; i < decodedMessage.DataSetMessages.Count; i++)
            {
                ArrowDataSetMessage actual = (ArrowDataSetMessage)decodedMessage.DataSetMessages[i];
                ArrowDataSetMessage expected = (ArrowDataSetMessage)message.DataSetMessages[i];
                Assert.That(actual.DataSetWriterId, Is.EqualTo(expected.DataSetWriterId));
                Assert.That(actual.SequenceNumber, Is.EqualTo(expected.SequenceNumber));
                Assert.That(actual.Status, Is.EqualTo(expected.Status));
                Assert.That(actual.Timestamp, Is.EqualTo(expected.Timestamp));
                Assert.That(actual.MessageType, Is.EqualTo(PubSubDataSetMessageType.KeyFrame));
            }

            ArrowDataSetMessage first = (ArrowDataSetMessage)decodedMessage.DataSetMessages[0];
            Assert.That(first.Fields[0].Value.TryGetValue(out double temperature), Is.True);
            Assert.That(temperature, Is.EqualTo(21.5));
            Assert.That(first.Fields[1].Value.TryGetValue(out string label), Is.True);
            Assert.That(label, Is.EqualTo("pump-a"));
            Assert.That(first.Fields[2].Value.TryGetValue(out ArrayOf<double> samples), Is.True);
            Assert.That(samples.ToArray(), Is.EqualTo(FirstSamples));
            Assert.That(first.Fields[3].Value.TryGetValue(out bool enabled), Is.True);
            Assert.That(enabled, Is.True);

            ArrowDataSetMessage second = (ArrowDataSetMessage)decodedMessage.DataSetMessages[1];
            Assert.That(second.Fields[1].Value.IsNull, Is.True);
            Assert.That(second.Fields[2].Value.TryGetValue(out ArrayOf<double> emptySamples), Is.True);
            Assert.That(emptySamples.Count, Is.Zero);
        }

        [Test]
        public async Task ShortRecordBatchIsRejectedInsteadOfThrowing()
        {
            // Regression: a record batch with fewer than the required header columns must be treated
            // as an invalid message (null + ReceivedInvalidNetworkMessages) instead of crashing the
            // receive loop with an uncaught ArgumentOutOfRangeException from RecordBatch.Column.
            PublisherId publisherId = PublisherId.FromString("publisher-arrow");
            Uuid dataSetClassId = new(new Guid("95669f76-285a-41c6-ac2b-27793a3eac10"));
            DataSetMetaDataType metaData = CreateMetaData();
            PubSubNetworkMessageContext context = CreateContext(
                publisherId, writerGroupId: 9, dataSetClassId, metaData, dataSetWriterId: 501);

            var schemaMetadata = new Dictionary<string, string>
            {
                ["magic"] = "OPC-UA-PubSub-Arrow",
                ["version"] = "1"
            };
            var schema = new Apache.Arrow.Schema(
                new[]
                {
                    new Field("writerId", UInt16Type.Default, nullable: false),
                    new Field("sequence", UInt32Type.Default, nullable: false)
                },
                schemaMetadata);

            UInt16Array writerIds = new UInt16Array.Builder().Append((ushort)1).Build();
            UInt32Array sequences = new UInt32Array.Builder().Append(1u).Build();
            using var batch = new RecordBatch(schema, new IArrowArray[] { writerIds, sequences }, length: 1);

            using var stream = new MemoryStream();
            using (var writer = new ArrowStreamWriter(stream, schema))
            {
                await writer.WriteRecordBatchAsync(batch);
                await writer.WriteEndAsync();
            }

            ArrowNetworkMessageDecoder decoder = new();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(stream.ToArray(), context);
            Assert.That(decoded, Is.Null);
        }

        [Test]
        public async Task BareRecordBatchFramingRoundTripsUsingCachedSchema()
        {
            PublisherId publisherId = PublisherId.FromString("publisher-arrow");
            Uuid dataSetClassId = new(new Guid("95669f76-285a-41c6-ac2b-27793a3eac10"));
            DataSetMetaDataType metaData = CreateMetaData();
            PubSubNetworkMessageContext context = CreateContext(
                publisherId, writerGroupId: 9, dataSetClassId, metaData, dataSetWriterId: 501);
            ArrowNetworkMessage message = CreateNetworkMessage(publisherId, dataSetClassId, metaData);

            // Bare-batch framing omits the embedded Arrow Schema message.
            ArrowNetworkMessageEncoder batchEncoder = new() { Framing = ArrowIpcFraming.Batch };
            ReadOnlyMemory<byte> bareBatch = await batchEncoder.EncodeAsync(message, context);

            // The self-contained stream for the same message is larger by the schema message.
            ArrowNetworkMessageEncoder streamEncoder = new() { Framing = ArrowIpcFraming.Stream };
            ReadOnlyMemory<byte> fullStream = await streamEncoder.EncodeAsync(message, context);

            Assert.That(batchEncoder.LastSchemaMessageLength, Is.GreaterThan(0));
            Assert.That(bareBatch.Length, Is.LessThan(fullStream.Length));
            Assert.That(
                fullStream.Length - bareBatch.Length,
                Is.GreaterThanOrEqualTo(batchEncoder.LastSchemaMessageLength));

            // The decoder resolves the schema out-of-band by SchemaId, then decodes the bare batch.
            ArrowNetworkMessageDecoder decoder = new();
            decoder.CacheSchema(message.SchemaId!, batchEncoder.LastSchemaMessage);
            PubSubNetworkMessage? decoded =
                await decoder.TryDecodeBatchAsync(bareBatch, message.SchemaId!, context);

            Assert.That(decoded, Is.TypeOf<ArrowNetworkMessage>());
            ArrowNetworkMessage decodedMessage = (ArrowNetworkMessage)decoded!;
            Assert.That(decodedMessage.DataSetMessages.Count, Is.EqualTo(3));

            ArrowDataSetMessage first = (ArrowDataSetMessage)decodedMessage.DataSetMessages[0];
            Assert.That(first.Fields[0].Value.TryGetValue(out double temperature), Is.True);
            Assert.That(temperature, Is.EqualTo(21.5));
            Assert.That(first.Fields[1].Value.TryGetValue(out string label), Is.True);
            Assert.That(label, Is.EqualTo("pump-a"));
            Assert.That(first.Fields[2].Value.TryGetValue(out ArrayOf<double> samples), Is.True);
            Assert.That(samples.ToArray(), Is.EqualTo(FirstSamples));

            ArrowDataSetMessage second = (ArrowDataSetMessage)decodedMessage.DataSetMessages[1];
            Assert.That(second.Fields[1].Value.IsNull, Is.True);

            // Without the schema the bare batch cannot be decoded.
            ArrowNetworkMessageDecoder coldDecoder = new();
            PubSubNetworkMessage? cold =
                await coldDecoder.TryDecodeBatchAsync(bareBatch, message.SchemaId!, context);
            Assert.That(cold, Is.Null);
        }

        [Test]
        public async Task BareRecordBatchDecodesAfterSchemaPrimedByFullStream()
        {
            PublisherId publisherId = PublisherId.FromString("publisher-arrow");
            Uuid dataSetClassId = new(new Guid("95669f76-285a-41c6-ac2b-27793a3eac10"));
            DataSetMetaDataType metaData = CreateMetaData();
            PubSubNetworkMessageContext context = CreateContext(
                publisherId, writerGroupId: 9, dataSetClassId, metaData, dataSetWriterId: 501);
            ArrowNetworkMessage message = CreateNetworkMessage(publisherId, dataSetClassId, metaData);

            ArrowNetworkMessageEncoder streamEncoder = new() { Framing = ArrowIpcFraming.Stream };
            ReadOnlyMemory<byte> fullStream = await streamEncoder.EncodeAsync(message, context);
            ArrowNetworkMessageEncoder batchEncoder = new() { Framing = ArrowIpcFraming.Batch };
            ReadOnlyMemory<byte> bareBatch = await batchEncoder.EncodeAsync(message, context);

            // Decoding a full stream primes the schema cache for its SchemaId.
            ArrowNetworkMessageDecoder decoder = new();
            PubSubNetworkMessage? primed = await decoder.TryDecodeAsync(fullStream, context);
            Assert.That(primed, Is.Not.Null);

            // Subsequent bare batches reuse the cached schema.
            PubSubNetworkMessage? decoded =
                await decoder.TryDecodeBatchAsync(bareBatch, message.SchemaId!, context);
            Assert.That(decoded, Is.TypeOf<ArrowNetworkMessage>());
            Assert.That(((ArrowNetworkMessage)decoded!).DataSetMessages.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task SparseDataSetUsesSameSchemaAsFullKeyFrameAndDecodesMissingAsNull()
        {
            PublisherId publisherId = PublisherId.FromString("publisher-arrow");
            Uuid dataSetClassId = new(new Guid("95669f76-285a-41c6-ac2b-27793a3eac10"));
            DataSetMetaDataType metaData = CreateMetaData();
            PubSubNetworkMessageContext context = CreateContext(
                publisherId, writerGroupId: 9, dataSetClassId, metaData, dataSetWriterId: 501);

            ArrowNetworkMessage full = new()
            {
                PublisherId = publisherId,
                WriterGroupId = 9,
                DataSetClassId = dataSetClassId,
                SchemaId = "arrow-schema-1",
                MetaData = metaData,
                DataSetMessages = [CreateSample(501, 100, 21.5, "pump-a", FirstSamples, true, metaData)]
            };

            // Sparse frame: only Temperature and Enabled carry values; Label and Samples are absent.
            var sparseMessage = new ArrowDataSetMessage
            {
                DataSetWriterId = 501,
                SequenceNumber = 101,
                Status = (StatusCode)StatusCodes.Good,
                Timestamp = new DateTimeUtc(new DateTime(2026, 7, 4, 9, 31, 0, DateTimeKind.Utc)),
                MessageType = PubSubDataSetMessageType.KeyFrame,
                MetaDataVersion = metaData.ConfigurationVersion,
                FieldContentMask = DataSetFieldContentMask.RawData,
                Fields =
                [
                    new DataSetField
                    {
                        Name = "Temperature",
                        Value = new Variant(23.75),
                        Encoding = PubSubFieldEncoding.RawData
                    },
                    new DataSetField
                    {
                        Name = "Enabled",
                        Value = new Variant(true),
                        Encoding = PubSubFieldEncoding.RawData
                    }
                ]
            };
            ArrowNetworkMessage sparse = new()
            {
                PublisherId = publisherId,
                WriterGroupId = 9,
                DataSetClassId = dataSetClassId,
                SchemaId = "arrow-schema-1",
                MetaData = metaData,
                DataSetMessages = [sparseMessage]
            };

            ReadOnlyMemory<byte> fullFrame = await new ArrowNetworkMessageEncoder { Framing = ArrowIpcFraming.Stream }
                .EncodeAsync(full, context);
            var sparseEncoder = new ArrowNetworkMessageEncoder { Framing = ArrowIpcFraming.Stream };
            ReadOnlyMemory<byte> sparseFrame = await sparseEncoder.EncodeAsync(sparse, context);

            // The sparse frame carries the identical schema (same column set + types) as the full key
            // frame — so the same SchemaId — because absent keys are null cells, not dropped columns.
            Assert.That(ReadSchemaSignature(sparseFrame), Is.EqualTo(ReadSchemaSignature(fullFrame)));

            // Decode the sparse frame: present keys round-trip; absent keys decode as null (missing).
            ArrowNetworkMessageDecoder decoder = new();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(sparseFrame, context);
            Assert.That(decoded, Is.TypeOf<ArrowNetworkMessage>());
            ArrowDataSetMessage row = (ArrowDataSetMessage)((ArrowNetworkMessage)decoded!).DataSetMessages[0];

            DataSetField temperature = FieldByName(row, "Temperature");
            Assert.That(temperature.Value.TryGetValue(out double t), Is.True);
            Assert.That(t, Is.EqualTo(23.75));
            DataSetField enabled = FieldByName(row, "Enabled");
            Assert.That(enabled.Value.TryGetValue(out bool e), Is.True);
            Assert.That(e, Is.True);
            Assert.That(FieldByName(row, "Label").Value.IsNull, Is.True);
            Assert.That(FieldByName(row, "Samples").Value.IsNull, Is.True);
        }

        [Test]
        public async Task EncoderDecoderRoundTripsEverySupportedRawDataType()
        {
            PublisherId publisherId = PublisherId.FromString("publisher-arrow-types");
            Uuid dataSetClassId = new(new Guid("95669f76-285a-41c6-ac2b-27793a3eac20"));
            DataSetMetaDataType metaData = CreateAllTypesMetaData();
            PubSubNetworkMessageContext context = CreateContext(
                publisherId, writerGroupId: 10, dataSetClassId, metaData, dataSetWriterId: 601);
            ArrowNetworkMessage message = new()
            {
                PublisherId = publisherId,
                WriterGroupId = 10,
                DataSetClassId = dataSetClassId,
                SchemaId = "arrow-schema-types",
                MetaData = metaData,
                DataSetMessages =
                [
                    CreateAllTypesMessage(601, 1, metaData, includeValues: true),
                    CreateAllTypesMessage(601, 2, metaData, includeValues: false)
                ]
            };

            ArrowNetworkMessageEncoder encoder = new() { Framing = ArrowIpcFraming.Stream };
            ReadOnlyMemory<byte> frame = await encoder.EncodeAsync(message, context);
            ArrowNetworkMessageDecoder decoder = new();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(frame, context);

            Assert.That(decoded, Is.TypeOf<ArrowNetworkMessage>());
            ArrowNetworkMessage decodedMessage = (ArrowNetworkMessage)decoded!;
            Assert.That(decodedMessage.DataSetMessages.Count, Is.EqualTo(2));
            ArrowDataSetMessage first = (ArrowDataSetMessage)decodedMessage.DataSetMessages[0];
            ArrowDataSetMessage second = (ArrowDataSetMessage)decodedMessage.DataSetMessages[1];
            Assert.That(first.Fields.Count, Is.EqualTo(metaData.Fields.Count));
            Assert.That(AllFieldsAreNull(second), Is.True);

            Assert.That(FieldByName(first, "Boolean").Value.GetBoolean(), Is.True);
            Assert.That(FieldByName(first, "SByte").Value.GetSByte(), Is.EqualTo((sbyte)-8));
            Assert.That(FieldByName(first, "Byte").Value.GetByte(), Is.EqualTo((byte)8));
            Assert.That(FieldByName(first, "Int16").Value.GetInt16(), Is.EqualTo((short)-16));
            Assert.That(FieldByName(first, "UInt16").Value.GetUInt16(), Is.EqualTo((ushort)16));
            Assert.That(FieldByName(first, "Int32").Value.GetInt32(), Is.EqualTo(-32));
            Assert.That(FieldByName(first, "UInt32").Value.GetUInt32(), Is.EqualTo(32u));
            Assert.That(FieldByName(first, "Int64").Value.GetInt64(), Is.EqualTo(-64L));
            Assert.That(FieldByName(first, "UInt64").Value.GetUInt64(), Is.EqualTo(64UL));
            Assert.That(FieldByName(first, "Float").Value.GetFloat(), Is.EqualTo(1.25f));
            Assert.That(FieldByName(first, "Double").Value.GetDouble(), Is.EqualTo(2.25));
            Assert.That(FieldByName(first, "String").Value.GetString(), Is.EqualTo("alpha"));
            Assert.That(FieldByName(first, "DateTime").Value.GetDateTime(), Is.EqualTo(DateTimeValue));
            Assert.That(FieldByName(first, "Guid").Value.GetGuid(), Is.EqualTo(GuidValue));
            Assert.That(FieldByName(first, "ByteString").Value.GetByteString(), Is.EqualTo(ByteStringValue));
            Assert.That(FieldByName(first, "StatusCode").Value.GetStatusCode(), Is.EqualTo(StatusCodeValue));
            Assert.That(
                FieldByName(first, "BooleanArray").Value.GetBooleanArray().ToArray(),
                Is.EqualTo(BooleanArrayValues));
            Assert.That(FieldByName(first, "SByteArray").Value.GetSByteArray().ToArray(), Is.EqualTo(SByteArrayValues));
            Assert.That(FieldByName(first, "ByteArray").Value.GetByteArray().ToArray(), Is.EqualTo(ByteArrayValues));
            Assert.That(FieldByName(first, "Int16Array").Value.GetInt16Array().ToArray(), Is.EqualTo(Int16ArrayValues));
            Assert.That(
                FieldByName(first, "UInt16Array").Value.GetUInt16Array().ToArray(),
                Is.EqualTo(UInt16ArrayValues));
            Assert.That(FieldByName(first, "Int32Array").Value.GetInt32Array().ToArray(), Is.EqualTo(Int32ArrayValues));
            Assert.That(
                FieldByName(first, "UInt32Array").Value.GetUInt32Array().ToArray(),
                Is.EqualTo(UInt32ArrayValues));
            Assert.That(FieldByName(first, "Int64Array").Value.GetInt64Array().ToArray(), Is.EqualTo(Int64ArrayValues));
            Assert.That(
                FieldByName(first, "UInt64Array").Value.GetUInt64Array().ToArray(),
                Is.EqualTo(UInt64ArrayValues));
            Assert.That(FieldByName(first, "FloatArray").Value.GetFloatArray().ToArray(), Is.EqualTo(FloatArrayValues));
            Assert.That(
                FieldByName(first, "DoubleArray").Value.GetDoubleArray().ToArray(),
                Is.EqualTo(DoubleArrayValues));
            Assert.That(
                FieldByName(first, "StringArray").Value.GetStringArray().ToArray(),
                Is.EqualTo(StringArrayValues));
            Assert.That(
                FieldByName(first, "DateTimeArray").Value.GetDateTimeArray().ToArray(),
                Is.EqualTo(DateTimeArrayValues));
            Assert.That(FieldByName(first, "GuidArray").Value.GetGuidArray().ToArray(), Is.EqualTo(GuidArrayValues));
            Assert.That(
                FieldByName(first, "ByteStringArray").Value.GetByteStringArray().ToArray(),
                Is.EqualTo(ByteStringArrayValues));
            Assert.That(
                FieldByName(first, "StatusCodeArray").Value.GetStatusCodeArray().ToArray(),
                Is.EqualTo(StatusCodeArrayValues));
        }

        [Test]
        public async Task KeepAliveSchemaBatchHasZeroRowsAndDecodesEnvelope()
        {
            PublisherId publisherId = PublisherId.FromString("publisher-arrow-keepalive");
            Uuid dataSetClassId = new(new Guid("95669f76-285a-41c6-ac2b-27793a3eac21"));
            DataSetMetaDataType metaData = CreateMetaData();
            PubSubNetworkMessageContext context = CreateContext(
                publisherId, writerGroupId: 11, dataSetClassId, metaData, dataSetWriterId: 602);
            ArrowNetworkMessage message = new()
            {
                PublisherId = publisherId,
                WriterGroupId = 11,
                DataSetClassId = dataSetClassId,
                SchemaId = "arrow-schema-keepalive",
                MetaData = metaData,
                DataSetMessages =
                [
                    new ArrowDataSetMessage
                    {
                        DataSetWriterId = 602,
                        MessageType = PubSubDataSetMessageType.KeepAlive,
                        MetaDataVersion = metaData.ConfigurationVersion
                    }
                ]
            };

            ArrowNetworkMessageEncoder encoder = new() { Framing = ArrowIpcFraming.Stream };
            ReadOnlyMemory<byte> frame = await encoder.EncodeAsync(message, context);
            PubSubNetworkMessage? decoded = await new ArrowNetworkMessageDecoder().TryDecodeAsync(frame, context);

            Assert.That(encoder.LastRecordBatchMessageLength, Is.GreaterThan(0));
            Assert.That(decoded, Is.TypeOf<ArrowNetworkMessage>());
            ArrowNetworkMessage decodedMessage = (ArrowNetworkMessage)decoded!;
            Assert.That(decodedMessage.PublisherId, Is.EqualTo(publisherId));
            Assert.That(decodedMessage.WriterGroupId, Is.EqualTo(11));
            Assert.That(decodedMessage.DataSetMessages.Count, Is.Zero);
        }

        [Test]
        public async Task EncoderRejectsUnsupportedInputsWithoutProducingFrames()
        {
            PublisherId publisherId = PublisherId.FromString("publisher-arrow-invalid");
            Uuid dataSetClassId = new(new Guid("95669f76-285a-41c6-ac2b-27793a3eac22"));
            DataSetMetaDataType metaData = CreateMetaData();
            PubSubNetworkMessageContext context = CreateContext(
                publisherId, writerGroupId: 12, dataSetClassId, metaData, dataSetWriterId: 603);
            ArrowNetworkMessage valid = CreateNetworkMessage(publisherId, dataSetClassId, metaData);
            var encoder = new ArrowNetworkMessageEncoder();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            ArrowNetworkMessage nullFirst = CreateMessageWithFirstNullAndNoMetaData(publisherId, dataSetClassId);
            ArrowNetworkMessage unsupported = CreateUnsupportedTypeMessage(publisherId, dataSetClassId);
            ArrowNetworkMessage nonArrow = CreateMessageWithNonArrowDataSetMessage(publisherId, dataSetClassId);
            ArrowNetworkMessage deltaFrame = CreateDeltaFrameMessage(publisherId, dataSetClassId, metaData);

            Assert.That(
                async () => await encoder.EncodeAsync(null!, context),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                async () => await encoder.EncodeAsync(valid, null!),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                async () => await encoder.EncodeAsync(valid, context, cancellation.Token),
                Throws.TypeOf<OperationCanceledException>());
            Assert.That(
                async () => await encoder.EncodeAsync(new AvroNetworkMessage(), context),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                async () => await encoder.EncodeAsync(nullFirst, context),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                async () => await encoder.EncodeAsync(unsupported, context),
                Throws.TypeOf<NotSupportedException>());
            Assert.That(
                async () => await encoder.EncodeAsync(nonArrow, context),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                async () => await encoder.EncodeAsync(deltaFrame, context),
                Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public async Task DecoderRejectsMalformedStreamsAndMissingSchemaIds()
        {
            PublisherId publisherId = PublisherId.FromString("publisher-arrow-decoder-invalid");
            Uuid dataSetClassId = new(new Guid("95669f76-285a-41c6-ac2b-27793a3eac23"));
            DataSetMetaDataType metaData = CreateMetaData();
            PubSubNetworkMessageContext context = CreateContext(
                publisherId, writerGroupId: 13, dataSetClassId, metaData, dataSetWriterId: 604);
            var decoder = new ArrowNetworkMessageDecoder();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.That(
                async () => await decoder.TryDecodeAsync(ReadOnlyMemory<byte>.Empty, null!),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                async () => await decoder.TryDecodeAsync(ReadOnlyMemory<byte>.Empty, context, cancellation.Token),
                Throws.TypeOf<OperationCanceledException>());
            Assert.That(
                async () => await decoder.TryDecodeAsync(ReadOnlyMemory<byte>.Empty, context),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(await decoder.TryDecodeAsync(await CreateBadMagicStreamAsync(), context), Is.Null);
            Assert.That(await decoder.TryDecodeBatchAsync(ReadOnlyMemory<byte>.Empty, null!, context), Is.Null);

            string uncachedSchemaId = SchemaCache.ToKey(
                SchemaCache.ComputeSchemaId(ByteString.From(1, 2, 3), SchemaCache.ArrowFormat));
            ArrowNetworkMessage message = CreateNetworkMessage(publisherId, dataSetClassId, metaData) with
            {
                WriterGroupId = 13,
                SchemaId = uncachedSchemaId
            };
            ReadOnlyMemory<byte> frame = await new ArrowNetworkMessageEncoder { Framing = ArrowIpcFraming.Stream }
                .EncodeAsync(message, context);

            Assert.That(await decoder.TryDecodeAsync(frame, context), Is.Null);
        }

        private static DataSetField FieldByName(ArrowDataSetMessage message, string name)
        {
            for (int i = 0; i < message.Fields.Count; i++)
            {
                if (string.Equals(message.Fields[i].Name, name, StringComparison.Ordinal))
                {
                    return message.Fields[i];
                }
            }
            throw new InvalidOperationException($"field '{name}' not found");
        }

        private static bool AllFieldsAreNull(ArrowDataSetMessage message)
        {
            for (int i = 0; i < message.Fields.Count; i++)
            {
                if (!message.Fields[i].Value.IsNull)
                {
                    return false;
                }
            }
            return true;
        }

        private static string ReadSchemaSignature(ReadOnlyMemory<byte> frame)
        {
            using ArrowStreamReader reader = new(new MemoryStream(frame.ToArray(), writable: false));
            RecordBatch? batch = reader.ReadNextRecordBatch();
            Assert.That(batch, Is.Not.Null);
            return string.Join(
                "|",
                batch!.Schema.FieldsList.Select(f => $"{f.Name}:{f.DataType.TypeId}:{f.IsNullable}"));
        }

        /// <summary>
        /// Builds the three-sample Arrow network message shared by the framing round-trip tests.
        /// </summary>
        private static ArrowNetworkMessage CreateNetworkMessage(
            PublisherId publisherId,
            Uuid dataSetClassId,
            DataSetMetaDataType metaData)
        {
            return new ArrowNetworkMessage
            {
                PublisherId = publisherId,
                WriterGroupId = 9,
                DataSetClassId = dataSetClassId,
                SchemaId = "arrow-schema-1",
                MetaData = metaData,
                DataSetMessages =
                [
                    CreateSample(501, 100, 21.5, "pump-a", FirstSamples, true, metaData),
                    CreateSample(501, 101, 22.25, null, SecondSamples, false, metaData),
                    CreateSample(501, 102, 23.75, "pump-c", ThirdSamples, true, metaData)
                ]
            };
        }

        /// <summary>
        /// Creates a keyed Arrow dataset message containing scalar, nullable, array, and boolean sample fields.
        /// </summary>
        private static ArrowDataSetMessage CreateSample(
            ushort writerId,
            uint sequenceNumber,
            double temperature,
            string? label,
            double[] samples,
            bool enabled,
            DataSetMetaDataType metaData)
        {
            return new ArrowDataSetMessage
            {
                DataSetWriterId = writerId,
                SequenceNumber = sequenceNumber,
                Status = (StatusCode)StatusCodes.Good,
                Timestamp = new DateTimeUtc(
                    new DateTime(2026, 7, 4, 9, 30, 0, DateTimeKind.Utc).AddSeconds(sequenceNumber)),
                MessageType = PubSubDataSetMessageType.KeyFrame,
                MetaDataVersion = metaData.ConfigurationVersion,
                FieldContentMask = DataSetFieldContentMask.RawData,
                Fields =
                [
                    new DataSetField
                    {
                        Name = "Temperature",
                        Value = new Variant(temperature),
                        Encoding = PubSubFieldEncoding.RawData
                    },
                    new DataSetField
                    {
                        Name = "Label",
                        Value = label is null ? Variant.Null : new Variant(label),
                        Encoding = PubSubFieldEncoding.RawData
                    },
                    new DataSetField
                    {
                        Name = "Samples",
                        Value = new Variant(new ArrayOf<double>(samples.AsMemory())),
                        Encoding = PubSubFieldEncoding.RawData
                    },
                    new DataSetField
                    {
                        Name = "Enabled",
                        Value = new Variant(enabled),
                        Encoding = PubSubFieldEncoding.RawData
                    }
                ]
            };
        }

        private static ArrowDataSetMessage CreateAllTypesMessage(
            ushort writerId,
            uint sequenceNumber,
            DataSetMetaDataType metaData,
            bool includeValues)
        {
            return new ArrowDataSetMessage
            {
                DataSetWriterId = writerId,
                SequenceNumber = sequenceNumber,
                Status = (StatusCode)StatusCodes.Good,
                Timestamp = new DateTimeUtc(
                    new DateTime(2026, 7, 4, 10, 0, 0, DateTimeKind.Utc).AddSeconds(sequenceNumber)),
                MessageType = PubSubDataSetMessageType.KeyFrame,
                MetaDataVersion = metaData.ConfigurationVersion,
                FieldContentMask = DataSetFieldContentMask.RawData,
                Fields = includeValues ? CreateAllTypesFields() : CreateNullFields(metaData)
            };
        }

        private static ArrayOf<DataSetField> CreateAllTypesFields()
        {
            return
            [
                CreateField("Boolean", new Variant(true)),
                CreateField("SByte", new Variant((sbyte)-8)),
                CreateField("Byte", new Variant((byte)8)),
                CreateField("Int16", new Variant((short)-16)),
                CreateField("UInt16", new Variant((ushort)16)),
                CreateField("Int32", new Variant(-32)),
                CreateField("UInt32", new Variant(32u)),
                CreateField("Int64", new Variant(-64L)),
                CreateField("UInt64", new Variant(64UL)),
                CreateField("Float", new Variant(1.25f)),
                CreateField("Double", new Variant(2.25)),
                CreateField("String", new Variant("alpha")),
                CreateField("DateTime", new Variant(DateTimeValue)),
                CreateField("Guid", new Variant(GuidValue)),
                CreateField("ByteString", new Variant(ByteStringValue)),
                CreateField("StatusCode", new Variant(StatusCodeValue)),
                CreateField("BooleanArray", new Variant(new ArrayOf<bool>(BooleanArrayValues.AsMemory()))),
                CreateField("SByteArray", new Variant(new ArrayOf<sbyte>(SByteArrayValues.AsMemory()))),
                CreateField("ByteArray", new Variant(new ArrayOf<byte>(ByteArrayValues.AsMemory()))),
                CreateField("Int16Array", new Variant(new ArrayOf<short>(Int16ArrayValues.AsMemory()))),
                CreateField("UInt16Array", new Variant(new ArrayOf<ushort>(UInt16ArrayValues.AsMemory()))),
                CreateField("Int32Array", new Variant(new ArrayOf<int>(Int32ArrayValues.AsMemory()))),
                CreateField("UInt32Array", new Variant(new ArrayOf<uint>(UInt32ArrayValues.AsMemory()))),
                CreateField("Int64Array", new Variant(new ArrayOf<long>(Int64ArrayValues.AsMemory()))),
                CreateField("UInt64Array", new Variant(new ArrayOf<ulong>(UInt64ArrayValues.AsMemory()))),
                CreateField("FloatArray", new Variant(new ArrayOf<float>(FloatArrayValues.AsMemory()))),
                CreateField("DoubleArray", new Variant(new ArrayOf<double>(DoubleArrayValues.AsMemory()))),
                CreateField("StringArray", new Variant(new ArrayOf<string>(StringArrayValues.AsMemory()))),
                CreateField("DateTimeArray", new Variant(new ArrayOf<DateTimeUtc>(DateTimeArrayValues.AsMemory()))),
                CreateField("GuidArray", new Variant(new ArrayOf<Uuid>(GuidArrayValues.AsMemory()))),
                CreateField("ByteStringArray", new Variant(new ArrayOf<ByteString>(ByteStringArrayValues.AsMemory()))),
                CreateField("StatusCodeArray", new Variant(new ArrayOf<StatusCode>(StatusCodeArrayValues.AsMemory())))
            ];
        }

        private static ArrayOf<DataSetField> CreateNullFields(DataSetMetaDataType metaData)
        {
            var fields = new DataSetField[metaData.Fields.Count];
            for (int i = 0; i < fields.Length; i++)
            {
                fields[i] = CreateField(
                    metaData.Fields[i].Name ?? FormattableString.Invariant($"Field{i}"),
                    Variant.Null);
            }
            return fields;
        }

        private static DataSetField CreateField(string name, Variant value)
        {
            return new DataSetField
            {
                Name = name,
                Value = value,
                Encoding = PubSubFieldEncoding.RawData
            };
        }

        private static ArrowNetworkMessage CreateMessageWithFirstNullAndNoMetaData(
            PublisherId publisherId,
            Uuid dataSetClassId)
        {
            return new ArrowNetworkMessage
            {
                PublisherId = publisherId,
                WriterGroupId = 12,
                DataSetClassId = dataSetClassId,
                SchemaId = "arrow-schema-null-first",
                DataSetMessages =
                [
                    new ArrowDataSetMessage
                    {
                        DataSetWriterId = 603,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        Fields = [CreateField("Unknown", Variant.Null)]
                    }
                ]
            };
        }

        private static ArrowNetworkMessage CreateUnsupportedTypeMessage(
            PublisherId publisherId,
            Uuid dataSetClassId)
        {
            DataSetMetaDataType metaData = new()
            {
                Name = "UnsupportedArrowDataSet",
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 },
                Fields = [CreateFieldMetaData("Variant", BuiltInType.Variant, ValueRanks.Scalar)]
            };
            return new ArrowNetworkMessage
            {
                PublisherId = publisherId,
                WriterGroupId = 12,
                DataSetClassId = dataSetClassId,
                SchemaId = "arrow-schema-unsupported",
                MetaData = metaData,
                DataSetMessages =
                [
                    new ArrowDataSetMessage
                    {
                        DataSetWriterId = 603,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        Fields = [CreateField("Variant", new Variant(1))]
                    }
                ]
            };
        }

        private static ArrowNetworkMessage CreateMessageWithNonArrowDataSetMessage(
            PublisherId publisherId,
            Uuid dataSetClassId)
        {
            return new ArrowNetworkMessage
            {
                PublisherId = publisherId,
                WriterGroupId = 12,
                DataSetClassId = dataSetClassId,
                SchemaId = "arrow-schema-non-arrow",
                MetaData = CreateMetaData(),
                DataSetMessages =
                [
                    new AvroDataSetMessage
                    {
                        DataSetWriterId = 603,
                        MessageType = PubSubDataSetMessageType.KeyFrame
                    }
                ]
            };
        }

        private static ArrowNetworkMessage CreateDeltaFrameMessage(
            PublisherId publisherId,
            Uuid dataSetClassId,
            DataSetMetaDataType metaData)
        {
            return new ArrowNetworkMessage
            {
                PublisherId = publisherId,
                WriterGroupId = 12,
                DataSetClassId = dataSetClassId,
                SchemaId = "arrow-schema-delta",
                MetaData = metaData,
                DataSetMessages =
                [
                    new ArrowDataSetMessage
                    {
                        DataSetWriterId = 603,
                        MessageType = PubSubDataSetMessageType.DeltaFrame,
                        Fields = [CreateField("Temperature", new Variant(12.5))]
                    }
                ]
            };
        }

        /// <summary>
        /// Builds a PubSub decoding context registered with the dataset metadata for the requested writer.
        /// </summary>
        private static PubSubNetworkMessageContext CreateContext(
            PublisherId publisherId,
            ushort writerGroupId,
            Uuid dataSetClassId,
            DataSetMetaDataType metaData,
            ushort dataSetWriterId)
        {
            DataSetMetaDataRegistry registry = new();
            DataSetMetaDataKey key = new(
                publisherId,
                writerGroupId,
                dataSetWriterId,
                dataSetClassId,
                metaData.ConfigurationVersion.MajorVersion);
            registry.Register(in key, metaData);

            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                registry,
                new PubSubDiagnostics(PubSubDiagnosticsLevel.High),
                TimeProvider.System);
        }

        /// <summary>
        /// Defines the Arrow dataset fields and configuration version expected by the test network message.
        /// </summary>
        private static DataSetMetaDataType CreateMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "ArrowDataSet",
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 },
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "Temperature",
                        BuiltInType = (byte)BuiltInType.Double,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Label",
                        BuiltInType = (byte)BuiltInType.String,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Samples",
                        BuiltInType = (byte)BuiltInType.Double,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData
                    {
                        Name = "Enabled",
                        BuiltInType = (byte)BuiltInType.Boolean,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
        }

        private static DataSetMetaDataType CreateAllTypesMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "ArrowAllTypesDataSet",
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 },
                Fields =
                [
                    CreateFieldMetaData("Boolean", BuiltInType.Boolean, ValueRanks.Scalar),
                    CreateFieldMetaData("SByte", BuiltInType.SByte, ValueRanks.Scalar),
                    CreateFieldMetaData("Byte", BuiltInType.Byte, ValueRanks.Scalar),
                    CreateFieldMetaData("Int16", BuiltInType.Int16, ValueRanks.Scalar),
                    CreateFieldMetaData("UInt16", BuiltInType.UInt16, ValueRanks.Scalar),
                    CreateFieldMetaData("Int32", BuiltInType.Int32, ValueRanks.Scalar),
                    CreateFieldMetaData("UInt32", BuiltInType.UInt32, ValueRanks.Scalar),
                    CreateFieldMetaData("Int64", BuiltInType.Int64, ValueRanks.Scalar),
                    CreateFieldMetaData("UInt64", BuiltInType.UInt64, ValueRanks.Scalar),
                    CreateFieldMetaData("Float", BuiltInType.Float, ValueRanks.Scalar),
                    CreateFieldMetaData("Double", BuiltInType.Double, ValueRanks.Scalar),
                    CreateFieldMetaData("String", BuiltInType.String, ValueRanks.Scalar),
                    CreateFieldMetaData("DateTime", BuiltInType.DateTime, ValueRanks.Scalar),
                    CreateFieldMetaData("Guid", BuiltInType.Guid, ValueRanks.Scalar),
                    CreateFieldMetaData("ByteString", BuiltInType.ByteString, ValueRanks.Scalar),
                    CreateFieldMetaData("StatusCode", BuiltInType.StatusCode, ValueRanks.Scalar),
                    CreateFieldMetaData("BooleanArray", BuiltInType.Boolean, ValueRanks.OneDimension),
                    CreateFieldMetaData("SByteArray", BuiltInType.SByte, ValueRanks.OneDimension),
                    CreateFieldMetaData("ByteArray", BuiltInType.Byte, ValueRanks.OneDimension),
                    CreateFieldMetaData("Int16Array", BuiltInType.Int16, ValueRanks.OneDimension),
                    CreateFieldMetaData("UInt16Array", BuiltInType.UInt16, ValueRanks.OneDimension),
                    CreateFieldMetaData("Int32Array", BuiltInType.Int32, ValueRanks.OneDimension),
                    CreateFieldMetaData("UInt32Array", BuiltInType.UInt32, ValueRanks.OneDimension),
                    CreateFieldMetaData("Int64Array", BuiltInType.Int64, ValueRanks.OneDimension),
                    CreateFieldMetaData("UInt64Array", BuiltInType.UInt64, ValueRanks.OneDimension),
                    CreateFieldMetaData("FloatArray", BuiltInType.Float, ValueRanks.OneDimension),
                    CreateFieldMetaData("DoubleArray", BuiltInType.Double, ValueRanks.OneDimension),
                    CreateFieldMetaData("StringArray", BuiltInType.String, ValueRanks.OneDimension),
                    CreateFieldMetaData("DateTimeArray", BuiltInType.DateTime, ValueRanks.OneDimension),
                    CreateFieldMetaData("GuidArray", BuiltInType.Guid, ValueRanks.OneDimension),
                    CreateFieldMetaData("ByteStringArray", BuiltInType.ByteString, ValueRanks.OneDimension),
                    CreateFieldMetaData("StatusCodeArray", BuiltInType.StatusCode, ValueRanks.OneDimension)
                ]
            };
        }

        private static FieldMetaData CreateFieldMetaData(string name, BuiltInType builtInType, int valueRank)
        {
            return new FieldMetaData
            {
                Name = name,
                BuiltInType = (byte)builtInType,
                ValueRank = valueRank
            };
        }

        private static async Task<ReadOnlyMemory<byte>> CreateBadMagicStreamAsync()
        {
            var schemaMetadata = new Dictionary<string, string>
            {
                ["magic"] = "not-arrow-pubsub",
                ["version"] = "1"
            };
            Field[] fields =
            [
                new("dataSetWriterId", UInt16Type.Default, nullable: false),
                new("sequenceNumber", UInt32Type.Default, nullable: false),
                new("status", UInt32Type.Default, nullable: false),
                new("timestamp", Int64Type.Default, nullable: false),
                new("messageType", Int32Type.Default, nullable: false)
            ];
            var schema = new Apache.Arrow.Schema(fields, schemaMetadata);
            IArrowArray[] arrays =
            [
                new UInt16Array.Builder().Build(),
                new UInt32Array.Builder().Build(),
                new UInt32Array.Builder().Build(),
                new Int64Array.Builder().Build(),
                new Int32Array.Builder().Build()
            ];
            using var batch = new RecordBatch(schema, arrays, length: 0);
            using var stream = new MemoryStream();
            using (var writer = new ArrowStreamWriter(stream, schema, leaveOpen: true))
            {
                await writer.WriteRecordBatchAsync(batch);
                await writer.WriteEndAsync();
            }
            return stream.ToArray();
        }
    }
}
#endif
