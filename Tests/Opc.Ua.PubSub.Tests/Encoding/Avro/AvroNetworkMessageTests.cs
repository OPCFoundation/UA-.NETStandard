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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;

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
            Assert.That(decodedMessage.SchemaId, Is.EqualTo(message.SchemaId));
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
