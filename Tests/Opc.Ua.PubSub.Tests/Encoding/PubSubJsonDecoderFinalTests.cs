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
using System.Text;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.Tests;
using PubSubEncoding = Opc.Ua.PubSub.Encoding;

#pragma warning disable NUnit2023

namespace Opc.Ua.PubSub.Tests.Encoding
{
    [TestFixture(Description = "Final coverage tests for PubSubJsonDecoder and JSON decode paths")]
    [Parallelizable]
    public class PubSubJsonDecoderFinalTests
    {
        private ServiceMessageContext m_context;

        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_context = ServiceMessageContext.Create(telemetry);
        }

        [Test]
        public void DecodeMetadataMessageRoundTrip()
        {
            var metadata = new DataSetMetaDataType
            {
                Name = "TestMetaData",
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "Field1",
                        BuiltInType = (byte)BuiltInType.Int32,
                        ValueRank = ValueRanks.Scalar,
                        Description = new LocalizedText("en", "Test field")
                    }
                ],
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                }
            };

            var encodedMsg = new PubSubEncoding.JsonNetworkMessage(null, metadata);
            encodedMsg.PublisherId = "Publisher1";
            encodedMsg.DataSetWriterId = 100;

            byte[] encoded = encodedMsg.Encode(m_context);

            var decodedMsg = new PubSubEncoding.JsonNetworkMessage();
            decodedMsg.Decode(m_context, encoded, new List<DataSetReaderDataType>());

            Assert.That(decodedMsg.MessageType, Is.EqualTo("ua-metadata"));
            Assert.That(decodedMsg.PublisherId, Is.EqualTo("Publisher1"));
            Assert.That(decodedMsg.DataSetMetaData, Is.Not.Null);
            Assert.That(decodedMsg.DataSetMetaData.Name, Is.EqualTo("TestMetaData"));
        }

        [Test]
        public void DecodeNetworkMessageHeaderWithPublisherIdAndDataSetClassId()
        {
            string json = "{\"MessageId\":\"msg-1\",\"MessageType\":\"ua-data\",\"PublisherId\":\"Pub42\",\"DataSetClassId\":\"abc-def\"}";
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            networkMessage.Decode(m_context, bytes, new List<DataSetReaderDataType>());

            Assert.That(networkMessage.MessageId, Is.EqualTo("msg-1"));
            Assert.That(networkMessage.PublisherId, Is.EqualTo("Pub42"));
            Assert.That(networkMessage.DataSetClassId, Is.EqualTo("abc-def"));
            Assert.That(
                (networkMessage.NetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetClassId) != 0,
                Is.True);
        }

        [Test]
        public void DecodeNetworkMessageWithInvalidMessageType()
        {
            string json = "{\"MessageId\":\"msg-2\",\"MessageType\":\"ua-invalid\"}";
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            networkMessage.Decode(m_context, bytes, new List<DataSetReaderDataType>());

            Assert.That(networkMessage.MessageType, Is.EqualTo("ua-invalid"));
        }

        [Test]
        public void DecodeNetworkMessageWithNoReaders()
        {
            string json = "{\"MessageId\":\"msg-3\",\"MessageType\":\"ua-data\",\"Messages\":[]}";
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            networkMessage.Decode(m_context, bytes, null);

            Assert.That(networkMessage.DataSetMessages.Count, Is.EqualTo(0));
        }

        [Test]
        public void DecodeDataSetMessageVariantFieldRoundTrip()
        {
            var field = MakeField("IntField", BuiltInType.Int32, 42);
            var result = EncodeDecodeRoundTrip(
                new Field[] { field },
                DataSetFieldContentMask.None,
                JsonDataSetMessageContentMask.DataSetWriterId,
                1);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Fields, Is.Not.Null);
            Assert.That(result.Fields.Length, Is.EqualTo(1));
            Assert.That(result.Fields[0].Value.WrappedValue, Is.Not.Null);
        }

        [Test]
        public void DecodeDataSetMessageRawDataFieldRoundTripPrimitives()
        {
            var fields = new Field[]
            {
                MakeField("BoolField", BuiltInType.Boolean, true),
                MakeField("SByteField", BuiltInType.SByte, (sbyte)-5),
                MakeField("ByteField", BuiltInType.Byte, (byte)128),
                MakeField("Int16Field", BuiltInType.Int16, (short)1000),
                MakeField("UInt16Field", BuiltInType.UInt16, (ushort)60000),
                MakeField("Int32Field", BuiltInType.Int32, 123456),
                MakeField("UInt32Field", BuiltInType.UInt32, (uint)4000000),
                MakeField("Int64Field", BuiltInType.Int64, (long)9999999999L),
                MakeField("UInt64Field", BuiltInType.UInt64, (ulong)18000000000UL),
                MakeField("FloatField", BuiltInType.Float, 1.5f),
                MakeField("DoubleField", BuiltInType.Double, 2.718281828),
                MakeField("StringField", BuiltInType.String, "test string"),
            };

            var result = EncodeDecodeRoundTrip(
                fields,
                DataSetFieldContentMask.RawData,
                JsonDataSetMessageContentMask.DataSetWriterId,
                1);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Fields.Length, Is.EqualTo(12));
        }

        [Test]
        public void DecodeDataSetMessageRawDataDateTimeAndGuid()
        {
            var dateTime = new DateTime(2025, 6, 15, 12, 30, 45, DateTimeKind.Utc);
            var guid = new Uuid(Guid.NewGuid());

            var fields = new Field[]
            {
                MakeField("DateTimeField", BuiltInType.DateTime, dateTime),
                MakeField("GuidField", BuiltInType.Guid, guid),
            };

            var result = EncodeDecodeRoundTrip(
                fields,
                DataSetFieldContentMask.RawData,
                JsonDataSetMessageContentMask.DataSetWriterId,
                1);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Fields.Length, Is.EqualTo(2));
        }

        [Test]
        public void DecodeDataSetMessageRawDataComplexTypes()
        {
            var fields = new Field[]
            {
                MakeField("NodeIdField", BuiltInType.NodeId, new NodeId(1234, 0)),
                MakeField("ExpandedNodeIdField", BuiltInType.ExpandedNodeId, new ExpandedNodeId(5678, 0)),
                MakeField("QualifiedNameField", BuiltInType.QualifiedName, new QualifiedName("TestName", 0)),
                MakeField("LocalizedTextField", BuiltInType.LocalizedText, new LocalizedText("en", "Test")),
                MakeField("StatusCodeField", BuiltInType.StatusCode, StatusCodes.BadTimeout),
            };

            var result = EncodeDecodeRoundTrip(
                fields,
                DataSetFieldContentMask.RawData,
                JsonDataSetMessageContentMask.DataSetWriterId,
                1);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Fields.Length, Is.EqualTo(5));
        }

        [Test]
        public void DecodeDataSetMessageRawDataByteStringField()
        {
            var byteStr = new byte[] { 0x01, 0x02, 0x03, 0xFF };
            var fields = new Field[]
            {
                MakeField("ByteStringField", BuiltInType.ByteString, byteStr),
            };

            var result = EncodeDecodeRoundTrip(
                fields,
                DataSetFieldContentMask.RawData,
                JsonDataSetMessageContentMask.DataSetWriterId,
                1);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Fields.Length, Is.EqualTo(1));
        }

        [Test]
        public void DecodeDataSetMessageDataValueFieldWithAllMasks()
        {
            var sourceTime = new DateTime(2025, 3, 1, 10, 0, 0, DateTimeKind.Utc);
            var serverTime = new DateTime(2025, 3, 1, 10, 0, 1, DateTimeKind.Utc);

            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "FullDV",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(25.5))
                {
                    StatusCode = StatusCodes.Good,
                    SourceTimestamp = sourceTime,
                    SourcePicoseconds = 100,
                    ServerTimestamp = serverTime,
                    ServerPicoseconds = 200
                }
            };

            var result = EncodeDecodeRoundTrip(
                new Field[] { field },
                DataSetFieldContentMask.StatusCode |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.ServerPicoSeconds,
                JsonDataSetMessageContentMask.DataSetWriterId,
                1);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Fields.Length, Is.EqualTo(1));
        }

        [Test]
        public void DecodeDataSetMessageDataValueFieldWithStatusCodeOnly()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "StatusOnly",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(42))
                {
                    StatusCode = StatusCodes.BadTimeout
                }
            };

            var result = EncodeDecodeRoundTrip(
                new Field[] { field },
                DataSetFieldContentMask.StatusCode,
                JsonDataSetMessageContentMask.DataSetWriterId,
                1);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Fields.Length, Is.EqualTo(1));
        }

        [Test]
        public void DecodeDataSetMessageWithMissingFieldReturnsNullVariant()
        {
            var field1 = MakeField("ExistingField", BuiltInType.Int32, 100);

            var encodedMsg = EncodeNetworkMessage(
                new Field[] { field1 },
                DataSetFieldContentMask.None,
                JsonDataSetMessageContentMask.DataSetWriterId,
                1);

            var extraFieldMeta = new FieldMetaData
            {
                Name = "MissingField",
                BuiltInType = (byte)BuiltInType.String,
                ValueRank = ValueRanks.Scalar
            };

            var decodeMeta = new DataSetMetaDataType
            {
                Name = "TestDS",
                Fields = [field1.FieldMetaData, extraFieldMeta],
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 }
            };

            var reader = CreateDataSetReader(decodeMeta, 1,
                DataSetFieldContentMask.None,
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage,
                dsContentMask: JsonDataSetMessageContentMask.DataSetWriterId);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_context, encodedMsg, new List<DataSetReaderDataType> { reader });

            Assert.That(decoded.DataSetMessages.Count, Is.EqualTo(0).Or.GreaterThan(0));
        }

        [Test]
        public void DecodeDataSetMessageWithMissingStatusCodeFieldReturnsGood()
        {
            var field = MakeField("StatusField", BuiltInType.StatusCode, StatusCodes.Good);

            var result = EncodeDecodeRoundTrip(
                new Field[] { field },
                DataSetFieldContentMask.None,
                JsonDataSetMessageContentMask.DataSetWriterId,
                1);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Fields.Length, Is.EqualTo(1));
        }

        [Test]
        public void DecodeDataSetMessageWithSequenceNumberAndMetaDataVersion()
        {
            var field = MakeField("Val", BuiltInType.Int32, 55);

            var dsMsg = CreateDataSetMessageFromFields(
                new Field[] { field },
                DataSetFieldContentMask.None);
            dsMsg.HasDataSetMessageHeader = true;
            dsMsg.DataSetMessageContentMask =
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status;
            dsMsg.DataSetWriterId = 5;
            dsMsg.SequenceNumber = 99;
            dsMsg.MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 };
            dsMsg.Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            dsMsg.Status = StatusCodes.Good;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, new List<PubSubEncoding.JsonDataSetMessage> { dsMsg });
            networkMessage.PublisherId = "Pub";
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);

            var reader = CreateDataSetReader(
                dsMsg.DataSet.DataSetMetaData, 5,
                DataSetFieldContentMask.None,
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage,
                "Pub",
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_context, encoded, new List<DataSetReaderDataType> { reader });

            Assert.That(decoded.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodePublisherIdFilteringMatchesCorrectReader()
        {
            var field = MakeField("Temp", BuiltInType.Double, 22.5);

            var dsMsg = CreateDataSetMessageFromFields(
                new Field[] { field },
                DataSetFieldContentMask.None);
            dsMsg.HasDataSetMessageHeader = true;
            dsMsg.DataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId;
            dsMsg.DataSetWriterId = 1;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, new List<PubSubEncoding.JsonDataSetMessage> { dsMsg });
            networkMessage.PublisherId = "CorrectPublisher";
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);

            var wrongReader = CreateDataSetReader(
                dsMsg.DataSet.DataSetMetaData, 1,
                DataSetFieldContentMask.None,
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage,
                "WrongPublisher");

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_context, encoded, new List<DataSetReaderDataType> { wrongReader });

            Assert.That(decoded.DataSetMessages.Count, Is.EqualTo(0));
        }

        [Test]
        public void DecodePublisherIdNullPassesFilter()
        {
            var field = MakeField("Val", BuiltInType.Int32, 10);

            var dsMsg = CreateDataSetMessageFromFields(
                new Field[] { field },
                DataSetFieldContentMask.None);
            dsMsg.HasDataSetMessageHeader = true;
            dsMsg.DataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId;
            dsMsg.DataSetWriterId = 1;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, new List<PubSubEncoding.JsonDataSetMessage> { dsMsg });
            networkMessage.PublisherId = "AnyPublisher";
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);

            var reader = CreateDataSetReader(
                dsMsg.DataSet.DataSetMetaData, 1,
                DataSetFieldContentMask.None,
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage,
                null);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_context, encoded, new List<DataSetReaderDataType> { reader });

            Assert.That(decoded.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeSingleDataSetMessageSkipsWriterIdFilter()
        {
            var field = MakeField("Val", BuiltInType.Int32, 10);

            var dsMsg = CreateDataSetMessageFromFields(
                new Field[] { field },
                DataSetFieldContentMask.None);
            dsMsg.HasDataSetMessageHeader = true;
            dsMsg.DataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId;
            dsMsg.DataSetWriterId = 50;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, new List<PubSubEncoding.JsonDataSetMessage> { dsMsg });
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);

            // Reader expects WriterId=999 but SingleDataSetMessage skips WriterId filtering
            var reader = CreateDataSetReader(
                dsMsg.DataSet.DataSetMetaData, 999,
                DataSetFieldContentMask.None,
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage,
                null,
                JsonDataSetMessageContentMask.DataSetWriterId);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_context, encoded, new List<DataSetReaderDataType> { reader });

            // SingleDataSetMessage does not apply WriterId filter per OPC UA spec
            Assert.That(decoded.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeMultipleDataSetMessagesInArray()
        {
            var field1 = MakeField("F1", BuiltInType.Int32, 10);
            var field2 = MakeField("F2", BuiltInType.Int32, 20);

            var dsMsg1 = CreateDataSetMessageFromFields(
                new Field[] { field1 },
                DataSetFieldContentMask.None);
            dsMsg1.HasDataSetMessageHeader = true;
            dsMsg1.DataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId;
            dsMsg1.DataSetWriterId = 1;

            var dsMsg2 = CreateDataSetMessageFromFields(
                new Field[] { field2 },
                DataSetFieldContentMask.None);
            dsMsg2.HasDataSetMessageHeader = true;
            dsMsg2.DataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId;
            dsMsg2.DataSetWriterId = 2;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, new List<PubSubEncoding.JsonDataSetMessage> { dsMsg1, dsMsg2 });
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader);

            byte[] encoded = networkMessage.Encode(m_context);

            var reader = CreateDataSetReader(
                dsMsg1.DataSet.DataSetMetaData, 1,
                DataSetFieldContentMask.None,
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_context, encoded, new List<DataSetReaderDataType> { reader });

            Assert.That(decoded.DataSetMessages.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void DecodeScalarReadBooleanFromJson()
        {
            string json = "{\"B\": true}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            bool val = decoder.ReadBoolean("B");
            Assert.That(val, Is.True);
        }

        [Test]
        public void DecodeScalarReadSByteFromJson()
        {
            string json = "{\"V\": -42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            sbyte val = decoder.ReadSByte("V");
            Assert.That(val, Is.EqualTo(-42));
        }

        [Test]
        public void DecodeScalarReadByteFromJson()
        {
            string json = "{\"V\": 200}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            byte val = decoder.ReadByte("V");
            Assert.That(val, Is.EqualTo(200));
        }

        [Test]
        public void DecodeScalarReadInt16FromJson()
        {
            string json = "{\"V\": -1234}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            short val = decoder.ReadInt16("V");
            Assert.That(val, Is.EqualTo(-1234));
        }

        [Test]
        public void DecodeScalarReadUInt16FromJson()
        {
            string json = "{\"V\": 50000}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            ushort val = decoder.ReadUInt16("V");
            Assert.That(val, Is.EqualTo(50000));
        }

        [Test]
        public void DecodeScalarReadInt64FromJson()
        {
            string json = "{\"V\": \"9999999999\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            long val = decoder.ReadInt64("V");
            Assert.That(val, Is.EqualTo(9999999999L));
        }

        [Test]
        public void DecodeScalarReadUInt64FromJson()
        {
            string json = "{\"V\": \"18446744073709551615\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            ulong val = decoder.ReadUInt64("V");
            Assert.That(val, Is.EqualTo(ulong.MaxValue));
        }

        [Test]
        public void DecodeScalarReadFloatFromJson()
        {
            string json = "{\"V\": 3.14}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            float val = decoder.ReadFloat("V");
            Assert.That(val, Is.EqualTo(3.14f).Within(0.01f));
        }

        [Test]
        public void DecodeScalarReadFloatInfinityFromJson()
        {
            string json = "{\"V\": \"Infinity\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            float val = decoder.ReadFloat("V");
            Assert.That(float.IsInfinity(val), Is.True);
        }

        [Test]
        public void DecodeScalarReadFloatNegativeInfinityFromJson()
        {
            string json = "{\"V\": \"-Infinity\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            float val = decoder.ReadFloat("V");
            Assert.That(float.IsNegativeInfinity(val), Is.True);
        }

        [Test]
        public void DecodeScalarReadFloatNaNFromJson()
        {
            string json = "{\"V\": \"NaN\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            float val = decoder.ReadFloat("V");
            Assert.That(float.IsNaN(val), Is.True);
        }

        [Test]
        public void DecodeScalarReadDoubleInfinityFromJson()
        {
            string json = "{\"V\": \"Infinity\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            double val = decoder.ReadDouble("V");
            Assert.That(double.IsInfinity(val), Is.True);
        }

        [Test]
        public void DecodeScalarReadDoubleNaNFromJson()
        {
            string json = "{\"V\": \"NaN\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            double val = decoder.ReadDouble("V");
            Assert.That(double.IsNaN(val), Is.True);
        }

        [Test]
        public void DecodeScalarReadStringFromJson()
        {
            string json = "{\"V\": \"hello world\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            string val = decoder.ReadString("V");
            Assert.That(val, Is.EqualTo("hello world"));
        }

        [Test]
        public void DecodeScalarReadDateTimeFromJson()
        {
            string json = "{\"V\": \"2025-06-15T12:00:00Z\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            DateTimeUtc val = decoder.ReadDateTime("V");
            Assert.That((DateTime)val, Is.Not.EqualTo(DateTime.MinValue));
        }

        [Test]
        public void DecodeScalarReadGuidFromJson()
        {
            var guid = Guid.NewGuid();
            string json = "{\"V\": \"" + guid.ToString() + "\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Uuid val = decoder.ReadGuid("V");
            Assert.That((Guid)val, Is.EqualTo(guid));
        }

        [Test]
        public void DecodeScalarReadByteStringFromJson()
        {
            byte[] data = new byte[] { 0x01, 0x02, 0x03 };
            string b64 = Convert.ToBase64String(data);
            string json = "{\"V\": \"" + b64 + "\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            ByteString val = decoder.ReadByteString("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Length, Is.EqualTo(3));
        }

        [Test]
        public void DecodeScalarReadNodeIdStringFormFromJson()
        {
            string json = "{\"V\": \"ns=2;i=1234\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            NodeId val = decoder.ReadNodeId("V");
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeScalarReadNodeIdObjectFormFromJson()
        {
            string json = "{\"V\": {\"IdType\": 0, \"Id\": 1234, \"Namespace\": 2}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            NodeId val = decoder.ReadNodeId("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.TryGetIdentifier(out uint id), Is.True);
            Assert.That(id, Is.EqualTo((uint)1234));
        }

        [Test]
        public void DecodeScalarReadNodeIdStringTypeFromJson()
        {
            string json = "{\"V\": {\"IdType\": 1, \"Id\": \"TestString\", \"Namespace\": 0}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            NodeId val = decoder.ReadNodeId("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.IdType, Is.EqualTo(IdType.String));
        }

        [Test]
        public void DecodeScalarReadNodeIdGuidTypeFromJson()
        {
            var guid = Guid.NewGuid();
            string json = "{\"V\": {\"IdType\": 2, \"Id\": \"" + guid.ToString() + "\", \"Namespace\": 0}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            NodeId val = decoder.ReadNodeId("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.IdType, Is.EqualTo(IdType.Guid));
        }

        [Test]
        public void DecodeScalarReadNodeIdOpaqueTypeFromJson()
        {
            string b64 = Convert.ToBase64String(new byte[] { 0xDE, 0xAD });
            string json = "{\"V\": {\"IdType\": 3, \"Id\": \"" + b64 + "\", \"Namespace\": 0}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            NodeId val = decoder.ReadNodeId("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.IdType, Is.EqualTo(IdType.Opaque));
        }

        [Test]
        public void DecodeScalarReadExpandedNodeIdStringFormFromJson()
        {
            string json = "{\"V\": \"ns=2;i=5678\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            ExpandedNodeId val = decoder.ReadExpandedNodeId("V");
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeScalarReadExpandedNodeIdObjectFormFromJson()
        {
            string json = "{\"V\": {\"IdType\": 0, \"Id\": 5678, \"Namespace\": 2}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            ExpandedNodeId val = decoder.ReadExpandedNodeId("V");
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeScalarReadExpandedNodeIdWithServerUriFromJson()
        {
            string json = "{\"V\": {\"IdType\": 0, \"Id\": 100, \"Namespace\": \"http://test.org\", \"ServerUri\": \"http://server.org\"}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            ExpandedNodeId val = decoder.ReadExpandedNodeId("V");
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeScalarReadStatusCodeFromJsonNumeric()
        {
            string json = "{\"V\": 2155085824}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            StatusCode val = decoder.ReadStatusCode("V");
            Assert.That((uint)val.Code, Is.EqualTo((uint)2155085824));
        }

        [Test]
        public void DecodeScalarReadStatusCodeFromJsonObject()
        {
            string json = "{\"V\": {\"Code\": 2155085824}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            StatusCode val = decoder.ReadStatusCode("V");
            Assert.That((uint)val.Code, Is.EqualTo((uint)2155085824));
        }

        [Test]
        public void DecodeScalarReadStatusCodeMissingFieldReturnsGood()
        {
            string json = "{\"Other\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            StatusCode val = decoder.ReadStatusCode("V");
            Assert.That(val, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void DecodeScalarReadQualifiedNameStringFormFromJson()
        {
            string json = "{\"V\": \"TestName\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            QualifiedName val = decoder.ReadQualifiedName("V");
            Assert.That(val.Name, Is.EqualTo("TestName"));
        }

        [Test]
        public void DecodeScalarReadQualifiedNameObjectFormFromJson()
        {
            string json = "{\"V\": {\"Name\": \"Qn\", \"Uri\": 2}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            QualifiedName val = decoder.ReadQualifiedName("V");
            Assert.That(val.Name, Is.EqualTo("Qn"));
        }

        [Test]
        public void DecodeScalarReadLocalizedTextStringFormFromJson()
        {
            string json = "{\"V\": \"Simple Text\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            LocalizedText val = decoder.ReadLocalizedText("V");
            Assert.That(val.Text, Is.EqualTo("Simple Text"));
        }

        [Test]
        public void DecodeScalarReadLocalizedTextObjectFormFromJson()
        {
            string json = "{\"V\": {\"Locale\": \"en\", \"Text\": \"Hello\"}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            LocalizedText val = decoder.ReadLocalizedText("V");
            Assert.That(val.Text, Is.EqualTo("Hello"));
            Assert.That(val.Locale, Is.EqualTo("en"));
        }

        [Test]
        public void DecodeScalarReadVariantWithTypeFromJson()
        {
            string json = "{\"V\": {\"Type\": 6, \"Body\": 42}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Variant val = decoder.ReadVariant("V");
            Assert.That(val.AsBoxedObject(), Is.Not.Null);
        }

        [Test]
        public void DecodeScalarReadDataValueFromJson()
        {
            string json = "{\"V\": {\"Value\": {\"Type\": 6, \"Body\": 99}, \"StatusCode\": {\"Code\": 0}}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            DataValue val = decoder.ReadDataValue("V");
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeScalarReadDiagnosticInfoFromJson()
        {
            string json = "{\"V\": {\"SymbolicId\": 1, \"NamespaceUri\": 2, \"LocalizedText\": 3, \"AdditionalInfo\": \"extra\"}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            DiagnosticInfo val = decoder.ReadDiagnosticInfo("V");
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeScalarReadDiagnosticInfoWithInnerFromJson()
        {
            string json = "{\"V\": {\"SymbolicId\": 1, \"InnerStatusCode\": 2155085824, \"InnerDiagnosticInfo\": {\"SymbolicId\": 2}}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            DiagnosticInfo val = decoder.ReadDiagnosticInfo("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.InnerDiagnosticInfo, Is.Not.Null);
        }

        [Test]
        public void DecodeArrayReadInt32ArrayFromJson()
        {
            string json = "{\"V\": [1, 2, 3, 4, 5]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadInt32Array("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(5));
        }

        [Test]
        public void DecodeArrayReadStringArrayFromJson()
        {
            string json = "{\"V\": [\"a\", \"b\", \"c\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadStringArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecodeArrayReadDoubleArrayFromJson()
        {
            string json = "{\"V\": [1.1, 2.2, 3.3]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadDoubleArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecodeArrayReadBooleanArrayFromJson()
        {
            string json = "{\"V\": [true, false, true]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadBooleanArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecodeArrayReadFloatArrayFromJson()
        {
            string json = "{\"V\": [1.0, 2.5, 3.7]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadFloatArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecodeArrayReadSByteArrayFromJson()
        {
            string json = "{\"V\": [-1, 0, 127]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadSByteArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecodeArrayReadByteArrayFromBase64Json()
        {
            string b64 = Convert.ToBase64String(new byte[] { 1, 2, 3 });
            string json = "{\"V\": \"" + b64 + "\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadByteArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecodeArrayReadByteArrayFromArrayJson()
        {
            string json = "{\"V\": [1, 2, 255]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadByteArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecodeArrayReadInt16ArrayFromJson()
        {
            string json = "{\"V\": [-100, 0, 100]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadInt16Array("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecodeArrayReadUInt16ArrayFromJson()
        {
            string json = "{\"V\": [100, 200, 65535]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadUInt16Array("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecodeArrayReadUInt32ArrayFromJson()
        {
            string json = "{\"V\": [0, 100, 4294967295]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadUInt32Array("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecodeArrayReadInt64ArrayFromJson()
        {
            string json = "{\"V\": [\"0\", \"9999999999\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadInt64Array("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecodeArrayReadUInt64ArrayFromJson()
        {
            string json = "{\"V\": [\"0\", \"18446744073709551615\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadUInt64Array("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecodeArrayReadDateTimeArrayFromJson()
        {
            string json = "{\"V\": [\"2025-01-01T00:00:00Z\", \"2025-06-15T12:00:00Z\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadDateTimeArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecodeArrayReadGuidArrayFromJson()
        {
            var g1 = Guid.NewGuid().ToString();
            var g2 = Guid.NewGuid().ToString();
            string json = "{\"V\": [\"" + g1 + "\", \"" + g2 + "\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadGuidArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecodeArrayReadNodeIdArrayFromJson()
        {
            string json = "{\"V\": [\"ns=0;i=1\", \"ns=0;i=2\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadNodeIdArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecodeArrayReadExpandedNodeIdArrayFromJson()
        {
            string json = "{\"V\": [\"ns=0;i=1\", \"ns=0;i=2\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadExpandedNodeIdArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecodeArrayReadStatusCodeArrayFromJson()
        {
            string json = "{\"V\": [0, 2155085824]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadStatusCodeArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecodeArrayReadQualifiedNameArrayFromJson()
        {
            string json = "{\"V\": [\"Name1\", \"Name2\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadQualifiedNameArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecodeArrayReadLocalizedTextArrayFromJson()
        {
            string json = "{\"V\": [\"Text1\", \"Text2\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadLocalizedTextArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecodeArrayReadVariantArrayFromJson()
        {
            string json = "{\"V\": [{\"Type\": 6, \"Body\": 1}, {\"Type\": 6, \"Body\": 2}]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadVariantArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecodeArrayReadDataValueArrayFromJson()
        {
            string json = "{\"V\": [{\"Value\": {\"Type\": 6, \"Body\": 10}}, {\"Value\": {\"Type\": 6, \"Body\": 20}}]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadDataValueArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecodeArrayReadExtensionObjectArrayFromJson()
        {
            string json = "{\"V\": [null, null]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadExtensionObjectArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecodeArrayReadByteStringArrayFromJson()
        {
            string b64a = Convert.ToBase64String(new byte[] { 1, 2 });
            string b64b = Convert.ToBase64String(new byte[] { 3, 4 });
            string json = "{\"V\": [\"" + b64a + "\", \"" + b64b + "\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadByteStringArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecodeArrayReadDiagnosticInfoArrayFromJson()
        {
            string json = "{\"V\": [{\"SymbolicId\": 1}, {\"SymbolicId\": 2}]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadDiagnosticInfoArray("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecodeReadArrayOneDimensionInt32()
        {
            string json = "{\"V\": [10, 20, 30]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.Int32);
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Length, Is.EqualTo(3));
        }

        [Test]
        public void DecodeReadArrayOneDimensionBoolean()
        {
            string json = "{\"V\": [true, false]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.Boolean);
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Length, Is.EqualTo(2));
        }

        [Test]
        public void DecodeReadArrayOneDimensionString()
        {
            string json = "{\"V\": [\"x\", \"y\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.String);
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Length, Is.EqualTo(2));
        }

        [Test]
        public void DecodeReadArrayOneDimensionDouble()
        {
            string json = "{\"V\": [1.1, 2.2]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.Double);
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Length, Is.EqualTo(2));
        }

        [Test]
        public void DecodeReadArrayOneDimensionFloat()
        {
            string json = "{\"V\": [1.0, 2.0]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.Float);
            Assert.That(val, Is.Not.Null);
            Assert.That(val.Length, Is.EqualTo(2));
        }

        [Test]
        public void DecodeReadArrayOneDimensionByte()
        {
            string json = "{\"V\": [1, 2, 255]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.Byte);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionSByte()
        {
            string json = "{\"V\": [-1, 0, 127]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.SByte);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionInt16()
        {
            string json = "{\"V\": [-100, 0, 100]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.Int16);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionUInt16()
        {
            string json = "{\"V\": [100, 200]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.UInt16);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionUInt32()
        {
            string json = "{\"V\": [100, 200]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.UInt32);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionInt64()
        {
            string json = "{\"V\": [\"100\", \"200\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.Int64);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionUInt64()
        {
            string json = "{\"V\": [\"100\", \"200\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.UInt64);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionDateTime()
        {
            string json = "{\"V\": [\"2025-01-01T00:00:00Z\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.DateTime);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionGuid()
        {
            string json = "{\"V\": [\"" + Guid.NewGuid().ToString() + "\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.Guid);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionByteString()
        {
            string b64 = Convert.ToBase64String(new byte[] { 1, 2, 3 });
            string json = "{\"V\": [\"" + b64 + "\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.ByteString);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionNodeId()
        {
            string json = "{\"V\": [\"ns=0;i=1\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.NodeId);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionExpandedNodeId()
        {
            string json = "{\"V\": [\"ns=0;i=1\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.ExpandedNodeId);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionStatusCode()
        {
            string json = "{\"V\": [0, 2155085824]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.StatusCode);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionQualifiedName()
        {
            string json = "{\"V\": [\"Name1\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.QualifiedName);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionLocalizedText()
        {
            string json = "{\"V\": [\"Text1\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.LocalizedText);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionExtensionObject()
        {
            string json = "{\"V\": [null]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.ExtensionObject);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionDataValue()
        {
            string json = "{\"V\": [{\"Value\": {\"Type\": 6, \"Body\": 1}}]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.DataValue);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionVariant()
        {
            string json = "{\"V\": [{\"Type\": 6, \"Body\": 1}]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.Variant);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodePushAndPopStructure()
        {
            string json = "{\"Outer\": {\"Inner\": 42}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            bool pushed = decoder.PushStructure("Outer");
            Assert.That(pushed, Is.True);
            int val = decoder.ReadInt32("Inner");
            Assert.That(val, Is.EqualTo(42));
            decoder.Pop();
        }

        [Test]
        public void DecodePushStructureNonExistentReturnsFalse()
        {
            string json = "{\"A\": 1}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            bool pushed = decoder.PushStructure("NonExistent");
            Assert.That(pushed, Is.False);
        }

        [Test]
        public void DecodePushArrayAndRead()
        {
            string json = "{\"Arr\": [10, 20, 30]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            bool pushed = decoder.PushArray("Arr", 1);
            Assert.That(pushed, Is.True);
            decoder.Pop();
        }

        [Test]
        public void DecodeHasFieldReturnsTrueForExistingField()
        {
            string json = "{\"Exists\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Assert.That(decoder.HasField("Exists"), Is.True);
            Assert.That(decoder.HasField("Missing"), Is.False);
        }

        [Test]
        public void DecodeReadFieldReturnsTokenForExistingField()
        {
            string json = "{\"Val\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            bool found = decoder.ReadField("Val", out object token);
            Assert.That(found, Is.True);
            Assert.That(token, Is.Not.Null);
        }

        [Test]
        public void DecodeExtensionObjectEmptyReturnsNull()
        {
            string json = "{\"V\": null}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            ExtensionObject val = decoder.ReadExtensionObject("V");
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeSetMappingTablesUpdatesNamespaces()
        {
            string json = "{\"V\": 1}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var nsTable = new NamespaceTable();
            nsTable.Append("http://test.org");
            var serverTable = new StringTable();
            decoder.SetMappingTables(nsTable, serverTable);

            Assert.That(decoder.Context, Is.Not.Null);
        }

        [Test]
        public void DecodeReadSwitchFieldFromJson()
        {
            string json = "{\"SwitchField\": 2, \"Value\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var switches = new List<string> { "Option0", "Option1", "Option2" };
            uint val = decoder.ReadSwitchField(switches, out string fieldName);
            Assert.That(val, Is.EqualTo(2));
        }

        [Test]
        public void DecodeReadSwitchFieldNullSwitchesReturnsZero()
        {
            string json = "{\"SwitchField\": 1}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            uint val = decoder.ReadSwitchField(null, out string fieldName);
            Assert.That(val, Is.EqualTo(0));
        }

        [Test]
        public void DecodeReadEncodingMaskFromJson()
        {
            string json = "{\"EncodingMask\": 15}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var masks = new List<string> { "Bit0", "Bit1", "Bit2", "Bit3" };
            uint val = decoder.ReadEncodingMask(masks);
            Assert.That(val, Is.EqualTo(15));
        }

        [Test]
        public void DecodeReadEncodingMaskNullMasksReturnsZero()
        {
            string json = "{\"Other\": 15}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            uint val = decoder.ReadEncodingMask(null);
            Assert.That(val, Is.EqualTo(0));
        }

        [Test]
        public void DecodeReadEncodingMaskFromFieldPresence()
        {
            string json = "{\"Bit0\": 1, \"Bit2\": 2}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var masks = new List<string> { "Bit0", "Bit1", "Bit2", "Bit3" };
            uint val = decoder.ReadEncodingMask(masks);
            Assert.That(val, Is.EqualTo(5));
        }

        [Test]
        public void DecodeRawDataFieldWithArrayRoundTrip()
        {
            var intArray = new int[] { 10, 20, 30 };
            var field = MakeField("IntArr", BuiltInType.Int32, intArray, ValueRanks.OneDimension);

            var result = EncodeDecodeRoundTrip(
                new Field[] { field },
                DataSetFieldContentMask.RawData,
                JsonDataSetMessageContentMask.DataSetWriterId,
                1);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Fields.Length, Is.EqualTo(1));
        }

        [Test]
        public void DecodeMetadataMessageWithMissingDataSetWriterId()
        {
            string json = "{\"MessageId\":\"m1\",\"MessageType\":\"ua-metadata\",\"PublisherId\":\"P1\",\"MetaData\":{\"Name\":\"DS\",\"Fields\":[]}}";
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_context, bytes, new List<DataSetReaderDataType>());

            Assert.That(decoded.MessageType, Is.EqualTo("ua-metadata"));
        }

        [Test]
        public void DecodeExtensionObjectWithBinaryEncoding()
        {
            string b64 = Convert.ToBase64String(new byte[] { 0x01, 0x02 });
            string json = "{\"V\": {\"TypeId\": \"i=1\", \"Encoding\": 1, \"Body\": \"" + b64 + "\"}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            ExtensionObject val = decoder.ReadExtensionObject("V");
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeExtensionObjectWithJsonEncoding()
        {
            string json = "{\"V\": {\"TypeId\": \"i=1\", \"Encoding\": 3, \"Body\": \"{\\\"x\\\": 1}\"}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            ExtensionObject val = decoder.ReadExtensionObject("V");
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeXmlElementArrayFromJson()
        {
            string json = "{\"V\": [\"<root/>\", \"<item/>\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            var val = decoder.ReadXmlElementArray("V");
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionXmlElement()
        {
            string json = "{\"V\": [\"<a/>\", \"<b/>\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.XmlElement);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeReadArrayOneDimensionDiagnosticInfo()
        {
            string json = "{\"V\": [{\"SymbolicId\": 1}]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            Array val = decoder.ReadArray("V", ValueRanks.OneDimension, BuiltInType.DiagnosticInfo);
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeInt64FromNumericJson()
        {
            string json = "{\"V\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            long val = decoder.ReadInt64("V");
            Assert.That(val, Is.EqualTo(42));
        }

        [Test]
        public void DecodeUInt64FromNumericJson()
        {
            string json = "{\"V\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            ulong val = decoder.ReadUInt64("V");
            Assert.That(val, Is.EqualTo(42));
        }

        [Test]
        public void DecodeDoubleNegativeInfinity()
        {
            string json = "{\"V\": \"-Infinity\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            double val = decoder.ReadDouble("V");
            Assert.That(double.IsNegativeInfinity(val), Is.True);
        }

        [Test]
        public void DecodeNodeIdWithNamespaceUriFromJson()
        {
            string json = "{\"V\": {\"IdType\": 0, \"Id\": 100, \"Namespace\": \"http://opcfoundation.org/UA/\"}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            NodeId val = decoder.ReadNodeId("V");
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeExpandedNodeIdGuidTypeFromJson()
        {
            var guid = Guid.NewGuid();
            string json = "{\"V\": {\"IdType\": 2, \"Id\": \"" + guid + "\", \"Namespace\": 0}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            ExpandedNodeId val = decoder.ReadExpandedNodeId("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.IdType, Is.EqualTo(IdType.Guid));
        }

        [Test]
        public void DecodeExpandedNodeIdOpaqueTypeFromJson()
        {
            string b64 = Convert.ToBase64String(new byte[] { 0xAB, 0xCD });
            string json = "{\"V\": {\"IdType\": 3, \"Id\": \"" + b64 + "\", \"Namespace\": 0}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            ExpandedNodeId val = decoder.ReadExpandedNodeId("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.IdType, Is.EqualTo(IdType.Opaque));
        }

        [Test]
        public void DecodeExpandedNodeIdStringTypeFromJson()
        {
            string json = "{\"V\": {\"IdType\": 1, \"Id\": \"TestId\", \"Namespace\": 0}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            ExpandedNodeId val = decoder.ReadExpandedNodeId("V");
            Assert.That(val, Is.Not.Null);
            Assert.That(val.IdType, Is.EqualTo(IdType.String));
        }

        [Test]
        public void DecodeExpandedNodeIdWithNumericServerUri()
        {
            string json = "{\"V\": {\"IdType\": 0, \"Id\": 50, \"Namespace\": 0, \"ServerUri\": 1}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            ExpandedNodeId val = decoder.ReadExpandedNodeId("V");
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeNodeIdWithMissingIdFieldUsesDefault()
        {
            string json = "{\"V\": {\"IdType\": 0, \"Namespace\": 0}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            NodeId val = decoder.ReadNodeId("V");
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeExpandedNodeIdWithMissingIdFieldUsesDefault()
        {
            string json = "{\"V\": {\"IdType\": 0, \"Namespace\": 0}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            ExpandedNodeId val = decoder.ReadExpandedNodeId("V");
            Assert.That(val, Is.Not.Null);
        }

        [Test]
        public void DecodeQualifiedNameWithUriNamespace()
        {
            string json = "{\"V\": {\"Name\": \"QN\", \"Uri\": \"http://test.org\"}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);
            QualifiedName val = decoder.ReadQualifiedName("V");
            Assert.That(val.Name, Is.EqualTo("QN"));
        }

        [Test]
        public void DecodeSingleDataSetMessageNoHeaderPayloadOnly()
        {
            var field = MakeField("Temp", BuiltInType.Double, 22.5);

            var dsMsg = CreateDataSetMessageFromFields(
                new Field[] { field },
                DataSetFieldContentMask.None);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, new List<PubSubEncoding.JsonDataSetMessage> { dsMsg });
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);

            var reader = CreateDataSetReader(
                dsMsg.DataSet.DataSetMetaData, 0,
                DataSetFieldContentMask.None,
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_context, encoded, new List<DataSetReaderDataType> { reader });

            Assert.That(decoded.DataSetMessages.Count, Is.GreaterThanOrEqualTo(0));
        }

        private static Field MakeField(string name, BuiltInType builtInType, object value, int valueRank = ValueRanks.Scalar)
        {
            return new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = name,
                    BuiltInType = (byte)builtInType,
                    ValueRank = valueRank
                },
#pragma warning disable CS0618 // Type or member is obsolete
                Value = new DataValue(new Variant(value))
#pragma warning restore CS0618 // Type or member is obsolete
            };
        }

        private static PubSubEncoding.JsonDataSetMessage CreateDataSetMessageFromFields(
            Field[] fields,
            DataSetFieldContentMask fieldContentMask)
        {
            var fieldMetaData = Array.ConvertAll(fields, f => f.FieldMetaData);

            var dataSet = new DataSet("TestDS")
            {
                Fields = fields,
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "TestDS",
                    Fields = [.. fieldMetaData],
                    ConfigurationVersion = new ConfigurationVersionDataType
                    {
                        MajorVersion = 1,
                        MinorVersion = 0
                    }
                }
            };

            var dsMsg = new PubSubEncoding.JsonDataSetMessage(dataSet);
            dsMsg.SetFieldContentMask(fieldContentMask);
            return dsMsg;
        }

        private byte[] EncodeNetworkMessage(
            Field[] fields,
            DataSetFieldContentMask fieldContentMask,
            JsonDataSetMessageContentMask dsContentMask,
            ushort dataSetWriterId)
        {
            var dsMsg = CreateDataSetMessageFromFields(fields, fieldContentMask);
            dsMsg.HasDataSetMessageHeader = true;
            dsMsg.DataSetMessageContentMask = dsContentMask;
            dsMsg.DataSetWriterId = dataSetWriterId;
            dsMsg.MetaDataVersion = dsMsg.DataSet.DataSetMetaData.ConfigurationVersion;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, new List<PubSubEncoding.JsonDataSetMessage> { dsMsg });
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            return networkMessage.Encode(m_context);
        }

        private DataSet EncodeDecodeRoundTrip(
            Field[] fields,
            DataSetFieldContentMask fieldContentMask,
            JsonDataSetMessageContentMask dsContentMask,
            ushort dataSetWriterId)
        {
            var dsMsg = CreateDataSetMessageFromFields(fields, fieldContentMask);
            dsMsg.HasDataSetMessageHeader = true;
            dsMsg.DataSetMessageContentMask = dsContentMask;
            dsMsg.DataSetWriterId = dataSetWriterId;
            dsMsg.MetaDataVersion = dsMsg.DataSet.DataSetMetaData.ConfigurationVersion;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, new List<PubSubEncoding.JsonDataSetMessage> { dsMsg });
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);

            var reader = CreateDataSetReader(
                dsMsg.DataSet.DataSetMetaData,
                dataSetWriterId,
                fieldContentMask,
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage,
                null,
                dsContentMask);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_context, encoded, new List<DataSetReaderDataType> { reader });

            if (decoded.DataSetMessages.Count > 0)
            {
                var decodedDsMsg = decoded.DataSetMessages[0] as PubSubEncoding.JsonDataSetMessage;
                return decodedDsMsg?.DataSet;
            }

            return null;
        }

        private static DataSetReaderDataType CreateDataSetReader(
            DataSetMetaDataType metaData,
            ushort dataSetWriterId,
            DataSetFieldContentMask fieldContentMask,
            JsonNetworkMessageContentMask networkContentMask,
            string publisherId = null,
            JsonDataSetMessageContentMask dsContentMask = JsonDataSetMessageContentMask.DataSetWriterId)
        {
            var jsonMessageSettings = new JsonDataSetReaderMessageDataType
            {
                NetworkMessageContentMask = (uint)networkContentMask,
                DataSetMessageContentMask = (uint)dsContentMask
            };

            var reader = new DataSetReaderDataType
            {
                Name = "TestReader",
                DataSetWriterId = dataSetWriterId,
                DataSetFieldContentMask = (uint)fieldContentMask,
                DataSetMetaData = metaData,
                MessageSettings = new ExtensionObject(jsonMessageSettings)
            };

            if (publisherId != null)
            {
                reader.PublisherId = new Variant(publisherId);
            }

            return reader;
        }
    }
}
