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
using System.Linq;
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
    }
}
