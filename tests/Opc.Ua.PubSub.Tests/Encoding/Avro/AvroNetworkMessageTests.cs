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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using Uadp = Opc.Ua.PubSub.Encoding.Uadp;

namespace Opc.Ua.PubSub.Encoding.Tests
{
    /// <summary>
    /// Verifies Avro PubSub network-message encoding and decoding for multiple dataset messages and field encodings.
    /// </summary>
    [TestFixture]
    public sealed class AvroNetworkMessageTests
    {
        private static readonly ushort[] DataSetWriterIds = [101, 102];
        private static readonly double[] SampleValues = [1.0, 2.5, 4.25];
        private static readonly ushort[] SingleWriterId = [201];
        private static readonly PublisherId[] PublisherIds =
        [
            PublisherId.FromByte(1),
            PublisherId.FromUInt16(2),
            PublisherId.FromUInt32(3),
            PublisherId.FromUInt64(4),
            PublisherId.FromString("publisher-avro-string"),
            PublisherId.FromGuid(new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"))
        ];

        [Test]
        public async Task EncoderDecoderRoundTripsMultipleDataSetMessages()
        {
            PublisherId publisherId = PublisherId.FromString("publisher-avro");
            Uuid dataSetClassId = new(new Guid("8c2f8f1c-c9a1-48b0-a90b-7d8f6e725001"));
            DataSetMetaDataType metaData = CreateMetaData();
            PubSubDiagnostics diagnostics = new(PubSubDiagnosticsLevel.High);
            PubSubNetworkMessageContext context = CreateContext(
                publisherId,
                writerGroupId: 7,
                dataSetClassId,
                metaData,
                dataSetWriterIds: DataSetWriterIds,
                diagnostics);

            DateTimeUtc firstTimestamp = new(new DateTime(2026, 7, 4, 9, 0, 0, DateTimeKind.Utc));
            DateTimeUtc secondTimestamp = new(new DateTime(2026, 7, 4, 9, 0, 1, DateTimeKind.Utc));
            DateTimeUtc sourceTimestamp = new(new DateTime(2026, 7, 4, 8, 59, 59, DateTimeKind.Utc));

            AvroNetworkMessage message = new()
            {
                PublisherId = publisherId,
                WriterGroupId = 7,
                DataSetClassId = dataSetClassId,
                SchemaId = "schema-1",
                MetaData = metaData,
                DataSetMessages =
                [
                    new AvroDataSetMessage
                    {
                        DataSetWriterId = 101,
                        SequenceNumber = 10,
                        Timestamp = firstTimestamp,
                        Status = (StatusCode)StatusCodes.Good,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        MetaDataVersion = metaData.ConfigurationVersion,
                        Fields =
                        [
                            new DataSetField
                            {
                                Name = "Enabled",
                                Value = new Variant(true),
                                Encoding = PubSubFieldEncoding.RawData
                            },
                            new DataSetField
                            {
                                Name = "Temperature",
                                Value = new Variant(23.5),
                                Encoding = PubSubFieldEncoding.Variant
                            },
                            new DataSetField
                            {
                                Name = "Label",
                                Value = new Variant("pump-1"),
                                Encoding = PubSubFieldEncoding.RawData
                            },
                            new DataSetField
                            {
                                Name = "Samples",
                                Value = new Variant(new ArrayOf<double>(SampleValues.AsMemory())),
                                Encoding = PubSubFieldEncoding.RawData
                            },
                            new DataSetField
                            {
                                Name = "OptionalText",
                                Value = Variant.Null,
                                Encoding = PubSubFieldEncoding.Variant
                            }
                        ]
                    },
                    new AvroDataSetMessage
                    {
                        DataSetWriterId = 102,
                        SequenceNumber = 11,
                        Timestamp = secondTimestamp,
                        Status = (StatusCode)StatusCodes.Uncertain,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        MetaDataVersion = metaData.ConfigurationVersion,
                        FieldContentMask =
                            DataSetFieldContentMask.StatusCode | DataSetFieldContentMask.SourceTimestamp,
                        Fields =
                        [
                            new DataSetField
                            {
                                Name = "Temperature",
                                Value = new Variant(24.75),
                                StatusCode = (StatusCode)StatusCodes.BadWaitingForInitialData,
                                SourceTimestamp = sourceTimestamp,
                                Encoding = PubSubFieldEncoding.DataValue
                            }
                        ]
                    }
                ]
            };

            AvroNetworkMessageEncoder encoder = new();
            ReadOnlyMemory<byte> frame = await encoder.EncodeAsync(message, context);

            AvroNetworkMessageDecoder decoder = new();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(frame, context);

            Assert.That(
                decoded,
                Is.TypeOf<AvroNetworkMessage>(),
                diagnostics.LastError?.Message);
            AvroNetworkMessage decodedMessage = (AvroNetworkMessage)decoded!;
            Assert.That(decodedMessage.TransportProfileUri, Is.EqualTo(AvroNetworkMessage.PubSubMqttAvroTransport));
            Assert.That(decodedMessage.PublisherId, Is.EqualTo(message.PublisherId));
            Assert.That(decodedMessage.WriterGroupId, Is.EqualTo(message.WriterGroupId));
            Assert.That(decodedMessage.DataSetClassId, Is.EqualTo(message.DataSetClassId));
            // The fixed envelope no longer carries a NetworkMessage-level SchemaId; each
            // DataSetMessage is identified by its own per-DataSet SchemaId in its payload entry
            // (Part 14 Avro mapping §8.1).
            Assert.That(decodedMessage.SchemaId, Is.Empty);
            Assert.That(decodedMessage.DataSetMessages.Count, Is.EqualTo(2));

            AvroDataSetMessage first = (AvroDataSetMessage)decodedMessage.DataSetMessages[0];
            AssertHeader(first, (AvroDataSetMessage)message.DataSetMessages[0]);
            Assert.That(first.Fields.Count, Is.EqualTo(5));
            Assert.That(first.Fields[0].Value.TryGetValue(out bool enabled), Is.True);
            Assert.That(enabled, Is.True);
            Assert.That(first.Fields[1].Value.TryGetValue(out double temperature), Is.True);
            Assert.That(temperature, Is.EqualTo(23.5));
            Assert.That(first.Fields[2].Value.TryGetValue(out string label), Is.True);
            Assert.That(label, Is.EqualTo("pump-1"));
            Assert.That(first.Fields[3].Value.TryGetValue(out ArrayOf<double> samples), Is.True);
            Assert.That(samples.ToArray(), Is.EqualTo(SampleValues));
            Assert.That(first.Fields[4].Value.IsNull, Is.True);
            Assert.That(first.Fields[0].Encoding, Is.EqualTo(PubSubFieldEncoding.RawData));
            Assert.That(first.Fields[1].Encoding, Is.EqualTo(PubSubFieldEncoding.Variant));

            AvroDataSetMessage second = (AvroDataSetMessage)decodedMessage.DataSetMessages[1];
            AssertHeader(second, (AvroDataSetMessage)message.DataSetMessages[1]);
            Assert.That(
                second.FieldContentMask,
                Is.EqualTo(DataSetFieldContentMask.StatusCode | DataSetFieldContentMask.SourceTimestamp));
            Assert.That(second.Fields.Count, Is.EqualTo(1));
            Assert.That(second.Fields[0].Encoding, Is.EqualTo(PubSubFieldEncoding.DataValue));
            Assert.That(second.Fields[0].Value.TryGetValue(out double dataValueTemperature), Is.True);
            Assert.That(dataValueTemperature, Is.EqualTo(24.75));
            Assert.That(second.Fields[0].StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadWaitingForInitialData));
            Assert.That(second.Fields[0].SourceTimestamp, Is.EqualTo(sourceTimestamp));
        }

        /// <summary>
        /// A sparse DataSetMessage (a subset of the declared keys) announces the same schema and
        /// SchemaId as a full key frame, does not trigger a re-announcement, and round-trips its
        /// present keys while the absent keys are simply missing (nullable-keys design).
        /// </summary>
        [Test]
        public async Task SparseDataSetUsesSameSchemaIdAsFullKeyFrameAndDecodesPresentFields()
        {
            PublisherId publisherId = PublisherId.FromString("publisher-avro-sparse");
            Uuid dataSetClassId = new(new Guid("8c2f8f1c-c9a1-48b0-a90b-7d8f6e725002"));
            DataSetMetaDataType metaData = CreateMetaData();
            PubSubDiagnostics diagnostics = new(PubSubDiagnosticsLevel.High);
            PubSubNetworkMessageContext context = CreateContext(
                publisherId,
                writerGroupId: 7,
                dataSetClassId,
                metaData,
                dataSetWriterIds: DataSetWriterIds,
                diagnostics);

            DateTimeUtc timestamp = new(new DateTime(2026, 7, 4, 9, 0, 0, DateTimeKind.Utc));

            // Full key frame: every declared key present and non-null.
            AvroNetworkMessage fullFrame = new()
            {
                PublisherId = publisherId,
                WriterGroupId = 7,
                DataSetClassId = dataSetClassId,
                SchemaId = "schema-sparse",
                MetaData = metaData,
                DataSetMessages =
                [
                    new AvroDataSetMessage
                    {
                        DataSetWriterId = 101,
                        SequenceNumber = 1,
                        Timestamp = timestamp,
                        Status = (StatusCode)StatusCodes.Good,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        MetaDataVersion = metaData.ConfigurationVersion,
                        Fields =
                        [
                            new DataSetField
                            {
                                Name = "Enabled",
                                Value = new Variant(true),
                                Encoding = PubSubFieldEncoding.Variant
                            },
                            new DataSetField
                            {
                                Name = "Temperature",
                                Value = new Variant(23.5),
                                Encoding = PubSubFieldEncoding.Variant
                            },
                            new DataSetField
                            {
                                Name = "Label",
                                Value = new Variant("pump-1"),
                                Encoding = PubSubFieldEncoding.Variant
                            },
                            new DataSetField
                            {
                                Name = "Samples",
                                Value = new Variant(new ArrayOf<double>(SampleValues.AsMemory())),
                                Encoding = PubSubFieldEncoding.Variant
                            },
                            new DataSetField
                            {
                                Name = "OptionalText",
                                Value = new Variant("note"),
                                Encoding = PubSubFieldEncoding.Variant
                            }
                        ]
                    }
                ]
            };

            // Sparse frame: only a subset of keys present; the remaining keys are absent (missing).
            AvroNetworkMessage sparseFrame = new()
            {
                PublisherId = publisherId,
                WriterGroupId = 7,
                DataSetClassId = dataSetClassId,
                SchemaId = "schema-sparse",
                MetaData = metaData,
                DataSetMessages =
                [
                    new AvroDataSetMessage
                    {
                        DataSetWriterId = 101,
                        SequenceNumber = 2,
                        Timestamp = timestamp,
                        Status = (StatusCode)StatusCodes.Good,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        MetaDataVersion = metaData.ConfigurationVersion,
                        Fields =
                        [
                            new DataSetField
                            {
                                Name = "Temperature",
                                Value = new Variant(24.75),
                                Encoding = PubSubFieldEncoding.Variant
                            },
                            new DataSetField
                            {
                                Name = "Label",
                                Value = new Variant("pump-2"),
                                Encoding = PubSubFieldEncoding.Variant
                            }
                        ]
                    }
                ]
            };

            AvroNetworkMessageEncoder fullEncoder = new();
            _ = await fullEncoder.EncodeAsync(fullFrame, context);
            Assert.That(fullEncoder.LastSchemaAnnouncement, Is.Not.Null);
            ByteString fullSchemaId = fullEncoder.LastSchemaAnnouncement!.SchemaId;

            AvroNetworkMessageEncoder sparseEncoder = new();
            ReadOnlyMemory<byte> sparseFrameBytes = await sparseEncoder.EncodeAsync(sparseFrame, context);
            Assert.That(sparseEncoder.LastSchemaAnnouncement, Is.Not.Null);

            // Nullable keys: the sparse subset announces the SAME schema descriptor and SchemaId
            // as the full key frame - sparsity does not generate a new schema.
            Assert.That(sparseEncoder.LastSchemaAnnouncement!.SchemaId, Is.EqualTo(fullSchemaId));
            Assert.That(
                sparseEncoder.LastSchemaAnnouncement!.SchemaJson,
                Is.EqualTo(fullEncoder.LastSchemaAnnouncement!.SchemaJson));

            // Encoding the sparse frame on the encoder that already announced the full frame does
            // not re-announce (the SchemaId is unchanged).
            _ = await fullEncoder.EncodeAsync(sparseFrame, context);
            Assert.That(fullEncoder.LastSchemaAnnouncement, Is.Null);

            // The sparse frame round-trips: present keys decode; absent keys are simply missing.
            AvroNetworkMessageDecoder decoder = new();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(sparseFrameBytes, context);
            Assert.That(decoded, Is.TypeOf<AvroNetworkMessage>(), diagnostics.LastError?.Message);
            AvroDataSetMessage decodedDataSet =
                (AvroDataSetMessage)((AvroNetworkMessage)decoded!).DataSetMessages[0];
            Assert.That(decodedDataSet.Fields.Count, Is.EqualTo(2));
            Assert.That(FieldByName(decodedDataSet, "Temperature").Value.TryGetValue(out double temperature), Is.True);
            Assert.That(temperature, Is.EqualTo(24.75));
            Assert.That(FieldByName(decodedDataSet, "Label").Value.TryGetValue(out string label), Is.True);
            Assert.That(label, Is.EqualTo("pump-2"));
        }

        [Test]
        public async Task PublisherIdKindsAndKeepAliveMessagesRoundTrip()
        {
            Uuid dataSetClassId = new(new Guid("8c2f8f1c-c9a1-48b0-a90b-7d8f6e725010"));
            DataSetMetaDataType metaData = CreateMetaData();

            for (int i = 0; i < PublisherIds.Length; i++)
            {
                PubSubDiagnostics diagnostics = new(PubSubDiagnosticsLevel.High);
                PubSubNetworkMessageContext context = CreateContext(
                    PublisherIds[i],
                    writerGroupId: 0,
                    dataSetClassId,
                    metaData,
                    dataSetWriterIds: SingleWriterId,
                    diagnostics);
                AvroNetworkMessage message = new()
                {
                    PublisherId = PublisherIds[i],
                    WriterGroupId = null,
                    DataSetClassId = dataSetClassId,
                    SchemaId = string.Empty,
                    MetaData = metaData,
                    DataSetMessages =
                    [
                        new AvroDataSetMessage
                        {
                            DataSetWriterId = 201,
                            SequenceNumber = (uint)i,
                            Timestamp = new DateTimeUtc(
                                new DateTime(2026, 7, 4, 10, 0, 0, DateTimeKind.Utc).AddSeconds(i)),
                            Status = (StatusCode)StatusCodes.Good,
                            MessageType = PubSubDataSetMessageType.KeepAlive,
                            MetaDataVersion = metaData.ConfigurationVersion
                        }
                    ]
                };

                ReadOnlyMemory<byte> frame = await new AvroNetworkMessageEncoder().EncodeAsync(message, context);
                PubSubNetworkMessage? decoded = await new AvroNetworkMessageDecoder().TryDecodeAsync(frame, context);

                Assert.That(decoded, Is.TypeOf<AvroNetworkMessage>(), diagnostics.LastError?.Message);
                AvroNetworkMessage decodedMessage = (AvroNetworkMessage)decoded!;
                Assert.That(decodedMessage.PublisherId, Is.EqualTo(PublisherIds[i]));
                Assert.That(decodedMessage.WriterGroupId, Is.Null);
                Assert.That(decodedMessage.SchemaId, Is.Empty);
                Assert.That(decodedMessage.DataSetMessages.Count, Is.EqualTo(1));
                AvroDataSetMessage keepAlive = (AvroDataSetMessage)decodedMessage.DataSetMessages[0];
                Assert.That(keepAlive.MessageType, Is.EqualTo(PubSubDataSetMessageType.KeepAlive));
                Assert.That(keepAlive.Fields.Count, Is.Zero);
            }
        }

        [Test]
        public async Task DecoderUsesIngestedSchemaAnnouncementForParsableSchemaId()
        {
            PublisherId publisherId = PublisherId.FromString("publisher-avro-schema");
            Uuid dataSetClassId = new(new Guid("8c2f8f1c-c9a1-48b0-a90b-7d8f6e725011"));
            DataSetMetaDataType metaData = CreateMetaData();
            PubSubDiagnostics diagnostics = new(PubSubDiagnosticsLevel.High);
            PubSubNetworkMessageContext context = CreateContext(
                publisherId,
                writerGroupId: 8,
                dataSetClassId,
                metaData,
                dataSetWriterIds: SingleWriterId,
                diagnostics);
            AvroNetworkMessage template = new()
            {
                PublisherId = publisherId,
                WriterGroupId = 8,
                DataSetClassId = dataSetClassId,
                SchemaId = string.Empty,
                MetaData = metaData,
                DataSetMessages =
                [
                    new AvroDataSetMessage
                    {
                        DataSetWriterId = 201,
                        SequenceNumber = 1,
                        Timestamp = new DateTimeUtc(new DateTime(2026, 7, 4, 11, 0, 0, DateTimeKind.Utc)),
                        Status = (StatusCode)StatusCodes.Good,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        MetaDataVersion = metaData.ConfigurationVersion,
                        Fields = [CreateField("Enabled", new Variant(true), PubSubFieldEncoding.RawData)]
                    }
                ]
            };
            AvroNetworkMessageEncoder encoder = new();
            _ = await encoder.EncodeAsync(template, context);
            AvroSchemaAnnouncement announcement = encoder.LastSchemaAnnouncement!;

            // The fixed envelope carries each DataSetMessage opaquely with its own per-DataSet
            // SchemaId (Part 14 Avro mapping §8.1). A decoder primed with the announcement resolves
            // that SchemaId from its cache; an un-primed decoder still decodes the self-describing
            // body but does not have the schema cached.
            ReadOnlyMemory<byte> frame = await encoder.EncodeAsync(template, context);
            AvroNetworkMessageDecoder coldDecoder = new();
            AvroNetworkMessageDecoder primedDecoder = new();
            primedDecoder.Ingest(announcement);

            PubSubNetworkMessage? cold = await coldDecoder.TryDecodeAsync(frame, context);
            PubSubNetworkMessage? primed = await primedDecoder.TryDecodeAsync(frame, context);

            Assert.That(primed, Is.TypeOf<AvroNetworkMessage>(), diagnostics.LastError?.Message);
            Assert.That(cold, Is.TypeOf<AvroNetworkMessage>(), diagnostics.LastError?.Message);
            Assert.That(primedDecoder.SchemaCache.TryGet(announcement.SchemaId, out _), Is.True);
            Assert.That(coldDecoder.SchemaCache.TryGet(announcement.SchemaId, out _), Is.False);
            AvroDataSetMessage decodedDataSet =
                (AvroDataSetMessage)((AvroNetworkMessage)primed!).DataSetMessages[0];
            Assert.That(FieldByName(decodedDataSet, "Enabled").Value.TryGetValue(out bool enabled), Is.True);
            Assert.That(enabled, Is.True);
        }

        [Test]
        public async Task EncoderRejectsUnsupportedInputsBeforeWritingFrames()
        {
            PublisherId publisherId = PublisherId.FromString("publisher-avro-invalid");
            Uuid dataSetClassId = new(new Guid("8c2f8f1c-c9a1-48b0-a90b-7d8f6e725012"));
            DataSetMetaDataType metaData = CreateMetaData();
            PubSubNetworkMessageContext context = CreateContext(
                publisherId,
                writerGroupId: 9,
                dataSetClassId,
                metaData,
                dataSetWriterIds: SingleWriterId,
                new PubSubDiagnostics(PubSubDiagnosticsLevel.High));
            AvroNetworkMessage valid = CreateMinimalMessage(publisherId, 9, dataSetClassId, metaData);
            AvroNetworkMessage nonAvroDataSetMessage = CreateMessageWithNonAvroDataSetMessage(
                publisherId,
                dataSetClassId,
                metaData);
            AvroNetworkMessage rawWithoutMetaData = CreateRawDataMessageWithoutMetaData(publisherId, dataSetClassId);
            AvroNetworkMessageEncoder encoder = new();
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Assert.That(async () => await encoder.EncodeAsync(null!, context), Throws.TypeOf<ArgumentNullException>());
            Assert.That(async () => await encoder.EncodeAsync(valid, null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                async () => await encoder.EncodeAsync(valid, context, cancellation.Token),
                Throws.TypeOf<OperationCanceledException>());
            Assert.That(
                async () => await encoder.EncodeAsync(new Uadp.UadpNetworkMessage(), context),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                async () => await encoder.EncodeAsync(nonAvroDataSetMessage, context),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                async () => await encoder.EncodeAsync(rawWithoutMetaData, context),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task DecoderRejectsMalformedFramesAndQuotaViolations()
        {
            PublisherId publisherId = PublisherId.FromString("publisher-avro-malformed");
            Uuid dataSetClassId = new(new Guid("8c2f8f1c-c9a1-48b0-a90b-7d8f6e725013"));
            DataSetMetaDataType metaData = CreateMetaData();
            PubSubDiagnostics diagnostics = new(PubSubDiagnosticsLevel.High);
            PubSubNetworkMessageContext context = CreateContext(
                publisherId,
                writerGroupId: 10,
                dataSetClassId,
                metaData,
                dataSetWriterIds: SingleWriterId,
                diagnostics);
            AvroNetworkMessageDecoder decoder = new();
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Assert.That(
                async () => await decoder.TryDecodeAsync(ReadOnlyMemory<byte>.Empty, null!),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                async () => await decoder.TryDecodeAsync(ReadOnlyMemory<byte>.Empty, context, cancellation.Token),
                Throws.TypeOf<OperationCanceledException>());
            Assert.That(await decoder.TryDecodeAsync(ReadOnlyMemory<byte>.Empty, context), Is.Null);
            Assert.That(await decoder.TryDecodeAsync(CreateHeaderOnlyFrame("bad-magic", 1, context), context), Is.Null);
            ReadOnlyMemory<byte> badVersionFrame = CreateHeaderOnlyFrame("OPC-UA-PubSub-Avro", 2, context);
            Assert.That(await decoder.TryDecodeAsync(badVersionFrame, context), Is.Null);
            Assert.That(await decoder.TryDecodeAsync(CreateFrameWithDataSetCount(-1, context), context), Is.Null);

            PubSubNetworkMessageContext lowNetworkQuotaContext = CreateContext(
                publisherId,
                writerGroupId: 10,
                dataSetClassId,
                metaData,
                dataSetWriterIds: SingleWriterId,
                new PubSubDiagnostics(PubSubDiagnosticsLevel.High));
            ((ServiceMessageContext)lowNetworkQuotaContext.MessageContext).MaxArrayLength = 1;
            ReadOnlyMemory<byte> tooManyMessagesFrame = CreateFrameWithDataSetCount(2, lowNetworkQuotaContext);
            Assert.That(
                await decoder.TryDecodeAsync(tooManyMessagesFrame, lowNetworkQuotaContext),
                Is.Null);

            PubSubNetworkMessageContext lowFieldQuotaContext = CreateContext(
                publisherId,
                writerGroupId: 10,
                dataSetClassId,
                metaData,
                dataSetWriterIds: SingleWriterId,
                new PubSubDiagnostics(PubSubDiagnosticsLevel.High));
            ((ServiceMessageContext)lowFieldQuotaContext.MessageContext).MaxArrayLength = 1;
            Assert.That(
                await decoder.TryDecodeAsync(CreateFrameWithFieldCount(2, lowFieldQuotaContext), lowFieldQuotaContext),
                Is.Null);
            Assert.That(await decoder.TryDecodeAsync(CreateFrameWithFieldCount(-1, context), context), Is.Null);
            PubSubNetworkMessageContext emptyRegistryContext = CreateContextWithoutMetaData(
                new PubSubDiagnostics(PubSubDiagnosticsLevel.High));
            ReadOnlyMemory<byte> rawDataWithoutMetaData = CreateRawDataFrameWithoutRegisteredMetaData(context);
            Assert.That(
                await decoder.TryDecodeAsync(rawDataWithoutMetaData, emptyRegistryContext),
                Is.Null);
        }

        /// <summary>
        /// Returns the first field in the dataset message with the given name.
        /// </summary>
        private static DataSetField FieldByName(AvroDataSetMessage message, string name)
        {
            for (int i = 0; i < message.Fields.Count; i++)
            {
                if (message.Fields[i].Name == name)
                {
                    return message.Fields[i];
                }
            }
            throw new AssertionException($"Field '{name}' was not present in the decoded dataset.");
        }

        /// <summary>
        /// Asserts that a decoded Avro dataset header matches the message header that was originally encoded.
        /// </summary>
        private static void AssertHeader(AvroDataSetMessage actual, AvroDataSetMessage expected)
        {
            Assert.That(actual.DataSetWriterId, Is.EqualTo(expected.DataSetWriterId));
            Assert.That(actual.SequenceNumber, Is.EqualTo(expected.SequenceNumber));
            Assert.That(actual.Timestamp, Is.EqualTo(expected.Timestamp));
            Assert.That(actual.Status, Is.EqualTo(expected.Status));
            Assert.That(actual.MessageType, Is.EqualTo(expected.MessageType));
            Assert.That(actual.MetaDataVersion.MajorVersion, Is.EqualTo(expected.MetaDataVersion.MajorVersion));
            Assert.That(actual.MetaDataVersion.MinorVersion, Is.EqualTo(expected.MetaDataVersion.MinorVersion));
        }

        private static AvroNetworkMessage CreateMinimalMessage(
            PublisherId publisherId,
            ushort writerGroupId,
            Uuid dataSetClassId,
            DataSetMetaDataType metaData)
        {
            return new AvroNetworkMessage
            {
                PublisherId = publisherId,
                WriterGroupId = writerGroupId,
                DataSetClassId = dataSetClassId,
                SchemaId = "schema-minimal",
                MetaData = metaData,
                DataSetMessages =
                [
                    new AvroDataSetMessage
                    {
                        DataSetWriterId = 201,
                        SequenceNumber = 1,
                        Timestamp = new DateTimeUtc(new DateTime(2026, 7, 4, 12, 0, 0, DateTimeKind.Utc)),
                        Status = (StatusCode)StatusCodes.Good,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        MetaDataVersion = metaData.ConfigurationVersion,
                        Fields = [CreateField("Enabled", new Variant(true), PubSubFieldEncoding.RawData)]
                    }
                ]
            };
        }

        private static AvroNetworkMessage CreateMessageWithNonAvroDataSetMessage(
            PublisherId publisherId,
            Uuid dataSetClassId,
            DataSetMetaDataType metaData)
        {
            return new AvroNetworkMessage
            {
                PublisherId = publisherId,
                WriterGroupId = 9,
                DataSetClassId = dataSetClassId,
                SchemaId = "schema-non-avro",
                MetaData = metaData,
                DataSetMessages =
                [
                    new Uadp.UadpDataSetMessage
                    {
                        DataSetWriterId = 201,
                        MessageType = PubSubDataSetMessageType.KeyFrame
                    }
                ]
            };
        }

        private static AvroNetworkMessage CreateRawDataMessageWithoutMetaData(
            PublisherId publisherId,
            Uuid dataSetClassId)
        {
            return new AvroNetworkMessage
            {
                PublisherId = publisherId,
                WriterGroupId = 9,
                DataSetClassId = dataSetClassId,
                SchemaId = "schema-raw-no-metadata",
                DataSetMessages =
                [
                    new AvroDataSetMessage
                    {
                        DataSetWriterId = 201,
                        SequenceNumber = 1,
                        Timestamp = new DateTimeUtc(new DateTime(2026, 7, 4, 12, 1, 0, DateTimeKind.Utc)),
                        Status = (StatusCode)StatusCodes.Good,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        Fields = [CreateField("Enabled", new Variant(true), PubSubFieldEncoding.RawData)]
                    }
                ]
            };
        }

        private static DataSetField CreateField(string name, Variant value, PubSubFieldEncoding encoding)
        {
            return new DataSetField
            {
                Name = name,
                Value = value,
                Encoding = encoding
            };
        }

        private static ReadOnlyMemory<byte> CreateHeaderOnlyFrame(
            string magic,
            ushort version,
            PubSubNetworkMessageContext context)
        {
            using MemoryStream stream = new();
            using AvroEncoder encoder = new(stream, context.MessageContext, leaveOpen: true);
            encoder.WriteString(null, magic);
            encoder.WriteUInt16(null, version);
            encoder.Close();
            return stream.ToArray();
        }

        private static ReadOnlyMemory<byte> CreateFrameWithDataSetCount(
            int dataSetCount,
            PubSubNetworkMessageContext context)
        {
            using MemoryStream stream = new();
            using AvroEncoder encoder = new(stream, context.MessageContext, leaveOpen: true);
            WriteEnvelope(encoder, dataSetCount);
            encoder.Close();
            return stream.ToArray();
        }

        private static ReadOnlyMemory<byte> CreateFrameWithFieldCount(
            int fieldCount,
            PubSubNetworkMessageContext context)
        {
            using MemoryStream stream = new();
            using AvroEncoder encoder = new(stream, context.MessageContext, leaveOpen: true);
            WriteEnvelope(encoder, 1);
            using MemoryStream bodyStream = new();
            using (AvroEncoder bodyEncoder = new(bodyStream, context.MessageContext, leaveOpen: true))
            {
                WriteDataSetHeader(bodyEncoder);
                bodyEncoder.WriteInt32(null, fieldCount);
                bodyEncoder.Close();
            }
            WriteDataSetEntry(encoder, bodyStream.ToArray());
            encoder.Close();
            return stream.ToArray();
        }

        private static ReadOnlyMemory<byte> CreateRawDataFrameWithoutRegisteredMetaData(
            PubSubNetworkMessageContext context)
        {
            using MemoryStream stream = new();
            using AvroEncoder encoder = new(stream, context.MessageContext, leaveOpen: true);
            WriteEnvelope(encoder, 1);
            using MemoryStream bodyStream = new();
            using (AvroEncoder bodyEncoder = new(bodyStream, context.MessageContext, leaveOpen: true))
            {
                WriteDataSetHeader(bodyEncoder);
                bodyEncoder.WriteInt32(null, 1);
                bodyEncoder.WriteString(null, "Enabled");
                bodyEncoder.WriteInt32(null, 0);
                bodyEncoder.WriteEnumerated(null, PubSubFieldEncoding.RawData);
                using MemoryStream valueStream = new();
                using (AvroEncoder valueEncoder = new(valueStream, context.MessageContext, leaveOpen: true))
                {
                    valueEncoder.WriteVariantValue(null, new Variant(true));
                    valueEncoder.Close();
                }
                bodyEncoder.WriteByteString(null, ByteString.From(valueStream.ToArray()));
                bodyEncoder.Close();
            }
            WriteDataSetEntry(encoder, bodyStream.ToArray());
            encoder.Close();
            return stream.ToArray();
        }

        private static void WriteDataSetEntry(AvroEncoder encoder, byte[] body)
        {
            // Each opaque payload entry is { schemaId, dataSetMessage } (Part 14 Avro mapping §8.1);
            // the placeholder SchemaId is resolved best-effort and does not gate decoding.
            encoder.WriteByteString(null, ByteString.From(new byte[] { 0x01 }));
            encoder.WriteByteString(null, ByteString.From(body));
        }

        private static void WriteEnvelope(AvroEncoder encoder, int dataSetCount)
        {
            encoder.WriteString(null, "OPC-UA-PubSub-Avro");
            encoder.WriteUInt16(null, 1);
            encoder.WriteEnumerated(null, PublisherIdType.String);
            encoder.WriteString(null, "publisher-avro-malformed");
            encoder.WriteBoolean(null, true);
            encoder.WriteUInt16(null, 10);
            encoder.WriteGuid(null, new Uuid(new Guid("8c2f8f1c-c9a1-48b0-a90b-7d8f6e725013")));
            encoder.WriteInt32(null, dataSetCount);
        }

        private static void WriteDataSetHeader(AvroEncoder encoder)
        {
            encoder.WriteUInt16(null, 201);
            encoder.WriteEnumerated(null, PubSubDataSetMessageType.KeyFrame);
            encoder.WriteUInt32(null, 1);
            encoder.WriteUInt32(null, 2);
            encoder.WriteUInt32(null, 1);
            encoder.WriteStatusCode(null, (StatusCode)StatusCodes.Good);
            encoder.WriteDateTime(null, new DateTimeUtc(new DateTime(2026, 7, 4, 12, 2, 0, DateTimeKind.Utc)));
            encoder.WriteInt64(null, (long)(uint)DataSetFieldContentMask.None);
        }

        /// <summary>
        /// Builds a PubSub decoding context registered with metadata for all dataset writers used in the test.
        /// </summary>
        private static PubSubNetworkMessageContext CreateContext(
            PublisherId publisherId,
            ushort writerGroupId,
            Uuid dataSetClassId,
            DataSetMetaDataType metaData,
            ushort[] dataSetWriterIds,
            PubSubDiagnostics diagnostics)
        {
            DataSetMetaDataRegistry registry = new();
            foreach (ushort dataSetWriterId in dataSetWriterIds)
            {
                DataSetMetaDataKey key = new(
                    publisherId,
                    writerGroupId,
                    dataSetWriterId,
                    dataSetClassId,
                    metaData.ConfigurationVersion.MajorVersion);
                registry.Register(in key, metaData);
            }

            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                registry,
                diagnostics,
                TimeProvider.System);
        }

        private static PubSubNetworkMessageContext CreateContextWithoutMetaData(PubSubDiagnostics diagnostics)
        {
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                new DataSetMetaDataRegistry(),
                diagnostics,
                TimeProvider.System);
        }

        /// <summary>
        /// Defines the Avro dataset fields and configuration version expected by the test network message.
        /// </summary>
        private static DataSetMetaDataType CreateMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "AvroDataSet",
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 2
                },
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "Enabled",
                        BuiltInType = (byte)BuiltInType.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
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
                        Name = "OptionalText",
                        BuiltInType = (byte)BuiltInType.String,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
        }
    }
}
