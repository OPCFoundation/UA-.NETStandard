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
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.Tests;
using PubSubEncoding = Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class PubSubJsonEncoderFinalTests
    {
        private ServiceMessageContext m_context;

        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_context = ServiceMessageContext.Create(telemetry);
        }

        [Test]
        public void EncodeMetadataMessageWithWriterIdProducesValidJson()
        {
            var metadata = new DataSetMetaDataType
            {
                Name = "TestMetaData",
                Fields =
                [
                    new FieldMetaData { Name = "Field1", BuiltInType = (byte)BuiltInType.Int32, ValueRank = ValueRanks.Scalar }
                ],
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 }
            };

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(null, metadata)
            {
                PublisherId = "Publisher1",
                DataSetWriterId = 100
            };

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Is.Not.Null.And.Not.Empty);
            Assert.That(json, Does.Contain("ua-metadata"));
            Assert.That(json, Does.Contain("Publisher1"));
            Assert.That(json, Does.Contain("MetaData"));
            Assert.That(json, Does.Contain("TestMetaData"));
        }

        [Test]
        public void EncodeMetadataMessageWithoutWriterIdStillProducesJson()
        {
            var metadata = new DataSetMetaDataType
            {
                Name = "TestMeta",
                Fields = []
            };

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(null, metadata)
            {
                PublisherId = "Pub1"
            };

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("ua-metadata"));
            Assert.That(json, Does.Contain("Pub1"));
        }

        [Test]
        public void EncodeNetworkMessageWithPublisherIdAndReplyTo()
        {
            PubSubEncoding.JsonDataSetMessage dsMsg = CreateSimpleDataSetMessage(
                FieldTypeEncodingMask.Variant,
                "IntField", BuiltInType.Int32, 42);
            dsMsg.HasDataSetMessageHeader = true;
            dsMsg.DataSetMessageContentMask =
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.SequenceNumber;
            dsMsg.DataSetWriterId = 10;
            dsMsg.SequenceNumber = 5;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg])
            {
                PublisherId = "TestPublisher",
                ReplyTo = "mqtt://reply/topic"
            };
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.ReplyTo |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("TestPublisher"));
            Assert.That(json, Does.Contain("ReplyTo"));
            Assert.That(json, Does.Contain("mqtt://reply/topic"));
            Assert.That(json, Does.Contain("ua-data"));
            Assert.That(json, Does.Contain("DataSetWriterId"));
            Assert.That(json, Does.Contain("SequenceNumber"));
        }

        [Test]
        public void EncodeNetworkMessageWithDataSetClassId()
        {
            var classId = Uuid.NewUuid();
            var metaData = new DataSetMetaDataType
            {
                Name = "ClassIdTest",
                DataSetClassId = classId,
                Fields =
                [
                    new FieldMetaData { Name = "F1", BuiltInType = (byte)BuiltInType.String, ValueRank = ValueRanks.Scalar }
                ]
            };

            var dsMsg = new PubSubEncoding.JsonDataSetMessage(new DataSet("ClassIdTest")
            {
                DataSetMetaData = metaData,
                Fields =
                [
                    new Field { FieldMetaData = metaData.Fields[0], Value = new DataValue(new Variant("hello")) }
                ]
            });
            dsMsg.SetFieldContentMask(DataSetFieldContentMask.None);
            dsMsg.HasDataSetMessageHeader = true;
            dsMsg.DataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId;
            dsMsg.DataSetWriterId = 1;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg])
            {
                PublisherId = "Pub"
            };
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetClassId |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("DataSetClassId"));
            Assert.That(json, Does.Contain(classId.ToString()));
        }

        [Test]
        public void EncodeMultipleDataSetMessagesAsArray()
        {
            PubSubEncoding.JsonDataSetMessage dsMsg1 = CreateSimpleDataSetMessage(
                FieldTypeEncodingMask.Variant,
                "F1", BuiltInType.Int32, 10);
            dsMsg1.HasDataSetMessageHeader = true;
            dsMsg1.DataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId;
            dsMsg1.DataSetWriterId = 1;

            PubSubEncoding.JsonDataSetMessage dsMsg2 = CreateSimpleDataSetMessage(
                FieldTypeEncodingMask.Variant,
                "F2", BuiltInType.Int32, 20);
            dsMsg2.HasDataSetMessageHeader = true;
            dsMsg2.DataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId;
            dsMsg2.DataSetWriterId = 2;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg1, dsMsg2])
            {
                PublisherId = "Pub"
            };
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("Messages"));
        }

        [Test]
        public void EncodeNoHeaderNoSingleDataSetProducesTopLevelArray()
        {
            PubSubEncoding.JsonDataSetMessage dsMsg1 = CreateSimpleDataSetMessage(
                FieldTypeEncodingMask.Variant,
                "F1", BuiltInType.Int32, 100);
            dsMsg1.HasDataSetMessageHeader = false;

            PubSubEncoding.JsonDataSetMessage dsMsg2 = CreateSimpleDataSetMessage(
                FieldTypeEncodingMask.Variant,
                "F2", BuiltInType.Int32, 200);
            dsMsg2.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg1, dsMsg2]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.DataSetMessageHeader);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void EncodeSingleDataSetNoHeadersProducesPayloadOnly()
        {
            PubSubEncoding.JsonDataSetMessage dsMsg = CreateSimpleDataSetMessage(
                FieldTypeEncodingMask.Variant,
                "Temperature", BuiltInType.Double, 36.6);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("Temperature"));
            Assert.That(json, Does.Not.Contain("MessageId"));
        }

        [Test]
        public void EncodeSingleDataSetWithHeaderNoNetworkHeader()
        {
            PubSubEncoding.JsonDataSetMessage dsMsg = CreateSimpleDataSetMessage(
                FieldTypeEncodingMask.Variant,
                "Pressure", BuiltInType.Float, 101.3f);
            dsMsg.HasDataSetMessageHeader = true;
            dsMsg.DataSetMessageContentMask =
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.Timestamp;
            dsMsg.DataSetWriterId = 42;
            dsMsg.Timestamp = DateTime.UtcNow;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.SingleDataSetMessage |
                JsonNetworkMessageContentMask.DataSetMessageHeader);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("DataSetWriterId"));
            Assert.That(json, Does.Contain("Timestamp"));
            Assert.That(json, Does.Not.Contain("MessageId"));
        }

        [Test]
        public void EncodeDataSetMessageWithAllHeaderFlags()
        {
            PubSubEncoding.JsonDataSetMessage dsMsg = CreateSimpleDataSetMessage(
                FieldTypeEncodingMask.Variant,
                "Val", BuiltInType.UInt32, (uint)999);
            dsMsg.HasDataSetMessageHeader = true;
            dsMsg.DataSetMessageContentMask =
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status;
            dsMsg.DataSetWriterId = 7;
            dsMsg.SequenceNumber = 42;
            dsMsg.MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 2, MinorVersion = 1 };
            dsMsg.Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            dsMsg.Status = StatusCodes.BadTimeout;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("DataSetWriterId"));
            Assert.That(json, Does.Contain("SequenceNumber"));
            Assert.That(json, Does.Contain("MetaDataVersion"));
            Assert.That(json, Does.Contain("Timestamp"));
        }

        [Test]
        public void EncodeRawDataFieldEncodingWithVariousTypes()
        {
            var fields = new List<Field>
            {
                MakeField("BoolField", BuiltInType.Boolean, true),
                MakeField("SByteField", BuiltInType.SByte, (sbyte)-10),
                MakeField("ByteField", BuiltInType.Byte, (byte)200),
                MakeField("Int16Field", BuiltInType.Int16, (short)-1000),
                MakeField("UInt16Field", BuiltInType.UInt16, (ushort)5000),
                MakeField("Int64Field", BuiltInType.Int64, (long)123456789012L),
                MakeField("UInt64Field", BuiltInType.UInt64, (ulong)999999999999UL),
                MakeField("FloatField", BuiltInType.Float, 3.14f),
                MakeField("DoubleField", BuiltInType.Double, 2.71828),
                MakeField("StringField", BuiltInType.String, "hello world"),
                MakeField("DateTimeField", BuiltInType.DateTime, new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc)),
                MakeField("GuidField", BuiltInType.Guid, Uuid.NewUuid())
            };

            FieldMetaData[] fieldMetaData = Array.ConvertAll(fields.ToArray(), f => f.FieldMetaData);

            var dsMsg = new PubSubEncoding.JsonDataSetMessage(new DataSet("RawTest")
            {
                Fields = [.. fields],
                DataSetMetaData = new DataSetMetaDataType { Name = "RawTest", Fields = [.. fieldMetaData] }
            });
            dsMsg.SetFieldContentMask(DataSetFieldContentMask.RawData);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("BoolField"));
            Assert.That(json, Does.Contain("hello world"));
            Assert.That(json, Does.Contain("FloatField"));
            Assert.That(json, Does.Contain("Int64Field"));
        }

        [Test]
        public void EncodeRawDataWithComplexOpcUaTypes()
        {
            var nodeId = new NodeId(1234, 2);
            var expandedNodeId = new ExpandedNodeId(5678, 3, "http://test.org/UA", 0);
            var qualifiedName = new QualifiedName("TestName", 2);
            var localizedText = new LocalizedText("en", "Test Text");
            StatusCode statusCode = StatusCodes.BadTimeout;

            var fields = new List<Field>
            {
                MakeField("NodeIdField", BuiltInType.NodeId, nodeId),
                MakeField("ExpandedNodeIdField", BuiltInType.ExpandedNodeId, expandedNodeId),
                MakeField("QualifiedNameField", BuiltInType.QualifiedName, qualifiedName),
                MakeField("LocalizedTextField", BuiltInType.LocalizedText, localizedText),
                MakeField("StatusCodeField", BuiltInType.StatusCode, statusCode)
            };

            FieldMetaData[] fieldMetaData = Array.ConvertAll(fields.ToArray(), f => f.FieldMetaData);

            var dsMsg = new PubSubEncoding.JsonDataSetMessage(new DataSet("ComplexRaw")
            {
                Fields = [.. fields],
                DataSetMetaData = new DataSetMetaDataType { Name = "ComplexRaw", Fields = [.. fieldMetaData] }
            });
            dsMsg.SetFieldContentMask(DataSetFieldContentMask.RawData);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("NodeIdField"));
            Assert.That(json, Does.Contain("Test Text"));
            Assert.That(json, Does.Contain("StatusCodeField"));
        }

        [Test]
        public void EncodeDataValueFieldEncodingWithAllMasks()
        {
            var sourceTime = new DateTime(2025, 3, 1, 10, 0, 0, DateTimeKind.Utc);
            var serverTime = new DateTime(2025, 3, 1, 10, 0, 1, DateTimeKind.Utc);

            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "TempField",
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

            var dsMsg = new PubSubEncoding.JsonDataSetMessage(new DataSet("DVTest")
            {
                Fields = [field],
                DataSetMetaData = new DataSetMetaDataType { Name = "DVTest", Fields = [field.FieldMetaData] }
            });

            dsMsg.SetFieldContentMask(
                DataSetFieldContentMask.StatusCode |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.ServerPicoSeconds);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("TempField"));
            Assert.That(json, Does.Contain("SourceTimestamp"));
            Assert.That(json, Does.Contain("ServerTimestamp"));
            Assert.That(json, Does.Contain("SourcePicoseconds"));
            Assert.That(json, Does.Contain("ServerPicoseconds"));
        }

        [Test]
        public void EncodeDataValueFieldEncodingWithStatusCodeOnly()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "StatusField",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(42))
                {
                    StatusCode = StatusCodes.BadTimeout
                }
            };

            var dsMsg = new PubSubEncoding.JsonDataSetMessage(new DataSet("StatusDV")
            {
                Fields = [field],
                DataSetMetaData = new DataSetMetaDataType { Name = "StatusDV", Fields = [field.FieldMetaData] }
            });

            dsMsg.SetFieldContentMask(DataSetFieldContentMask.StatusCode);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("StatusField"));
            Assert.That(json, Does.Contain("StatusCode"));
        }

        [Test]
        public void EncodeVariantFieldWithGoodStatusCodeEncodesAsNull()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "GoodStatus",
                    BuiltInType = (byte)BuiltInType.StatusCode,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(StatusCodes.Good))
            };

            PubSubEncoding.JsonDataSetMessage dsMsg = CreateDataSetMessageFromFields(
                [field],
                DataSetFieldContentMask.RawData);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void EncodeFieldWithBadStatusCodeReplacesValueInNonDataValueMode()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "BadField",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(42))
                {
                    StatusCode = StatusCodes.BadTimeout
                }
            };

            PubSubEncoding.JsonDataSetMessage dsMsg = CreateDataSetMessageFromFields(
                [field],
                DataSetFieldContentMask.None);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("BadField"));
        }

        [Test]
        public void EncodeVariantFieldWithArrayValues()
        {
            int[] intArray = [1, 2, 3, 4, 5];
            Field field = MakeField("IntArray", BuiltInType.Int32, intArray, ValueRanks.OneDimension);

            PubSubEncoding.JsonDataSetMessage dsMsg = CreateDataSetMessageFromFields(
                [field],
                DataSetFieldContentMask.None);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("IntArray"));
            Assert.That(json, Does.Contain("1"));
            Assert.That(json, Does.Contain("5"));
        }

        [Test]
        public void EncodeRawDataFieldWithArrayValues()
        {
            double[] doubleArray = [1.1, 2.2, 3.3];
            Field field = MakeField("DoubleArray", BuiltInType.Double, doubleArray, ValueRanks.OneDimension);

            PubSubEncoding.JsonDataSetMessage dsMsg = CreateDataSetMessageFromFields(
                [field],
                DataSetFieldContentMask.RawData);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("DoubleArray"));
        }

        [Test]
        public void EncodeVariantFieldsWithByteStringAndExtensionObject()
        {
            byte[] byteString = [0x01, 0x02, 0x03, 0xFF];
            var extObj = new ExtensionObject(new Argument("TestArg", DataTypeIds.Int32, ValueRanks.Scalar, "desc"));

            var fields = new List<Field>
            {
                MakeField("ByteStringField", BuiltInType.ByteString, byteString),
                MakeField("ExtObjField", BuiltInType.ExtensionObject, extObj)
            };

            PubSubEncoding.JsonDataSetMessage dsMsg = CreateDataSetMessageFromFields(
                [.. fields],
                DataSetFieldContentMask.None);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("ByteStringField"));
            Assert.That(json, Does.Contain("ExtObjField"));
        }

        [Test]
        public void EncodeVariantFieldWithDataValueType()
        {
            var dataValue = new DataValue(new Variant(42), StatusCodes.Good, DateTime.UtcNow);

            Field field = MakeField("DataValueField", BuiltInType.DataValue, dataValue);

            PubSubEncoding.JsonDataSetMessage dsMsg = CreateDataSetMessageFromFields(
                [field],
                DataSetFieldContentMask.None);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("DataValueField"));
        }

        [Test]
        public void EncodeDataSetMessageWithNullField()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "NullableField",
                    BuiltInType = (byte)BuiltInType.String,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(Variant.Null)
            };

            PubSubEncoding.JsonDataSetMessage dsMsg = CreateDataSetMessageFromFields(
                [field],
                DataSetFieldContentMask.None);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Is.Not.Null);
        }

        [Test]
        public void EncodeStreamOverloadProducesOutput()
        {
            PubSubEncoding.JsonDataSetMessage dsMsg = CreateSimpleDataSetMessage(
                FieldTypeEncodingMask.Variant,
                "StreamField", BuiltInType.Int32, 77);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);
            Assert.That(json, Does.Contain("StreamField"));
        }

        [Test]
        public void EncodeEmptyDataSetMessagesProducesValidJson()
        {
            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, []);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("ua-data"));
        }

        [Test]
        public void EncodeRawDataFieldWithStringArrayValues()
        {
            string[] stringArray = ["alpha", "beta", "gamma"];
            Field field = MakeField("StringArray", BuiltInType.String, stringArray, ValueRanks.OneDimension);

            PubSubEncoding.JsonDataSetMessage dsMsg = CreateDataSetMessageFromFields(
                [field],
                DataSetFieldContentMask.RawData);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("alpha"));
            Assert.That(json, Does.Contain("gamma"));
        }

        [Test]
        public void EncodeWithCompactEncoding()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Compact);
            Assert.That(encoder.EncodingToUse, Is.EqualTo(PubSubJsonEncoding.Compact));

            encoder.PushStructure("Root");
            encoder.WriteInt32("Val", 42);
            encoder.PopStructure();

            string json = encoder.CloseAndReturnText();
            Assert.That(json, Does.Contain("42"));
        }

        [Test]
        public void EncodeWithVerboseEncoding()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Verbose);
            Assert.That(encoder.EncodingToUse, Is.EqualTo(PubSubJsonEncoding.Verbose));

            encoder.PushStructure("Root");
            encoder.WriteString("Name", "test");
            encoder.PopStructure();

            string json = encoder.CloseAndReturnText();
            Assert.That(json, Does.Contain("test"));
        }

        [Test]
        public void WriteSwitchFieldCompactEncoding()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Compact);
            encoder.PushStructure(null);
            encoder.WriteSwitchField(1, out string fieldName);
            Assert.That(fieldName, Is.Null);
            encoder.PopStructure();
            encoder.Close();
        }

        [Test]
        public void WriteSwitchFieldReversibleEncoding()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            encoder.PushStructure(null);
            encoder.WriteSwitchField(2, out string fieldName);
            Assert.That(fieldName, Is.EqualTo("Value"));
            encoder.PopStructure();
            encoder.Close();
        }

        [Test]
        public void WriteSwitchFieldNonReversibleEncodingDoesNotWrite()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            encoder.PushStructure(null);
            encoder.WriteSwitchField(3, out string fieldName);
            Assert.That(fieldName, Is.Null);
            encoder.PopStructure();
            encoder.Close();
        }

        [Test]
        public void WriteSwitchFieldVerboseEncodingDoesNotWrite()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Verbose);
            encoder.PushStructure(null);
            encoder.WriteSwitchField(3, out string fieldName);
            Assert.That(fieldName, Is.Null);
            encoder.PopStructure();
            encoder.Close();
        }

        [Test]
        public void WriteEncodingMaskCompactEncoding()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Compact);
            encoder.PushStructure(null);
            encoder.WriteEncodingMask(0x03);
            encoder.PopStructure();

            string json = encoder.CloseAndReturnText();
            Assert.That(json, Does.Contain("EncodingMask"));
        }

        [Test]
        public void WriteEncodingMaskReversibleEncoding()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            encoder.PushStructure(null);
            encoder.WriteEncodingMask(0xFF);
            encoder.PopStructure();

            string json = encoder.CloseAndReturnText();
            Assert.That(json, Does.Contain("EncodingMask"));
        }

        [Test]
        public void WriteEncodingMaskNonReversibleDoesNotWrite()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            encoder.PushStructure(null);
            encoder.WriteEncodingMask(0xFF);
            encoder.PopStructure();

            string json = encoder.CloseAndReturnText();
            Assert.That(json, Does.Not.Contain("EncodingMask"));
        }

        [Test]
        public void UsingAlternateEncodingRestoresOriginalEncoding()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            Assert.That(encoder.EncodingToUse, Is.EqualTo(PubSubJsonEncoding.Reversible));

            encoder.PushStructure(null);
            encoder.UsingAlternateEncoding(
                encoder.WriteInt32, "Field", 123, PubSubJsonEncoding.Compact);
            Assert.That(encoder.EncodingToUse, Is.EqualTo(PubSubJsonEncoding.Reversible));
            encoder.PopStructure();
            encoder.Close();
        }

        [Test]
        public void EncodeVariantFieldWithVariantArrayValue()
        {
            var variants = new Variant[] { new(1), new("text"), new(3.14) };
            Field field = MakeField("VarArray", BuiltInType.Variant, variants, ValueRanks.OneDimension);

            PubSubEncoding.JsonDataSetMessage dsMsg = CreateDataSetMessageFromFields(
                [field],
                DataSetFieldContentMask.None);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("VarArray"));
        }

        [Test]
        public void EncodeDataValueFieldWithSourceTimestampOnly()
        {
            var sourceTime = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "SrcTsField",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(100))
                {
                    SourceTimestamp = sourceTime,
                    SourcePicoseconds = 50
                }
            };

            PubSubEncoding.JsonDataSetMessage dsMsg = CreateDataSetMessageFromFields(
                [field],
                DataSetFieldContentMask.SourceTimestamp | DataSetFieldContentMask.SourcePicoSeconds);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("SourceTimestamp"));
            Assert.That(json, Does.Contain("SourcePicoseconds"));
            Assert.That(json, Does.Not.Contain("ServerTimestamp"));
        }

        [Test]
        public void EncodeDataValueFieldWithServerTimestampOnly()
        {
            var serverTime = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "SrvTsField",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(3.14))
                {
                    ServerTimestamp = serverTime,
                    ServerPicoseconds = 75
                }
            };

            PubSubEncoding.JsonDataSetMessage dsMsg = CreateDataSetMessageFromFields(
                [field],
                DataSetFieldContentMask.ServerTimestamp | DataSetFieldContentMask.ServerPicoSeconds);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("ServerTimestamp"));
            Assert.That(json, Does.Contain("ServerPicoseconds"));
            Assert.That(json, Does.Not.Contain("SourceTimestamp"));
        }

        [Test]
        public void EncodeRawDataWithByteStringField()
        {
            byte[] byteStr = [0xDE, 0xAD, 0xBE, 0xEF];
            Field field = MakeField("RawBytes", BuiltInType.ByteString, byteStr);

            PubSubEncoding.JsonDataSetMessage dsMsg = CreateDataSetMessageFromFields(
                [field],
                DataSetFieldContentMask.RawData);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("RawBytes"));
        }

        [Test]
        public void EncodeRawDataWithEnumerationField()
        {
            Field field = MakeField("EnumField", BuiltInType.Enumeration, 2);

            PubSubEncoding.JsonDataSetMessage dsMsg = CreateDataSetMessageFromFields(
                [field],
                DataSetFieldContentMask.RawData);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("EnumField"));
        }

        [Test]
        public void EncodeWithTopLevelArrayAndMultipleMessages()
        {
            PubSubEncoding.JsonDataSetMessage ds1 = CreateSimpleDataSetMessage(
                FieldTypeEncodingMask.Variant,
                "A", BuiltInType.Int32, 1);
            ds1.HasDataSetMessageHeader = true;
            ds1.DataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId;
            ds1.DataSetWriterId = 1;

            PubSubEncoding.JsonDataSetMessage ds2 = CreateSimpleDataSetMessage(
                FieldTypeEncodingMask.Variant,
                "B", BuiltInType.Int32, 2);
            ds2.HasDataSetMessageHeader = true;
            ds2.DataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId;
            ds2.DataSetWriterId = 2;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [ds1, ds2]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.DataSetMessageHeader);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void EncodeRawDataWithDiagnosticInfoField()
        {
            // DiagnosticInfo cannot be put into a Variant directly,
            // so test with a string variant field typed as DiagnosticInfo
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "DiagField",
                    BuiltInType = (byte)BuiltInType.String,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant("diagnostic info value"))
            };

            PubSubEncoding.JsonDataSetMessage dsMsg = CreateDataSetMessageFromFields(
                [field],
                DataSetFieldContentMask.RawData);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("diagnostic info value"));
        }

        [Test]
        public void EncodeRawDataWithNodeIdArrayField()
        {
            var nodeIds = new NodeId[] { new(1, 0), new(2, 1), new("s=test", 2) };
            Field field = MakeField("NodeIdArray", BuiltInType.NodeId, nodeIds, ValueRanks.OneDimension);

            PubSubEncoding.JsonDataSetMessage dsMsg = CreateDataSetMessageFromFields(
                [field],
                DataSetFieldContentMask.RawData);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("NodeIdArray"));
        }

        [Test]
        public void EncodeDataValueFieldWithBadStatusAndValue()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "BadValField",
                    BuiltInType = (byte)BuiltInType.String,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant("original"))
                {
                    StatusCode = StatusCodes.BadCommunicationError
                }
            };

            PubSubEncoding.JsonDataSetMessage dsMsg = CreateDataSetMessageFromFields(
                [field],
                DataSetFieldContentMask.StatusCode | DataSetFieldContentMask.SourceTimestamp);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("BadValField"));
        }

        [Test]
        public void EncodeVariantFieldWithLocalizedTextArrayValue()
        {
            var ltArray = new LocalizedText[]
            {
                new("en", "Hello"),
                new("de", "Hallo")
            };
            Field field = MakeField("LtArray", BuiltInType.LocalizedText, ltArray, ValueRanks.OneDimension);

            PubSubEncoding.JsonDataSetMessage dsMsg = CreateDataSetMessageFromFields(
                [field],
                DataSetFieldContentMask.None);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("LtArray"));
        }

        [Test]
        public void EncodeVariantFieldWithQualifiedNameArrayValue()
        {
            var qnArray = new QualifiedName[]
            {
                new("Name1", 0),
                new("Name2", 1)
            };
            Field field = MakeField("QnArray", BuiltInType.QualifiedName, qnArray, ValueRanks.OneDimension);

            PubSubEncoding.JsonDataSetMessage dsMsg = CreateDataSetMessageFromFields(
                [field],
                DataSetFieldContentMask.None);
            dsMsg.HasDataSetMessageHeader = false;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                null, [dsMsg]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("QnArray"));
        }

        [Test]
        public void EncodeCompactEncoderWriteSwitchFieldSuppressArtifacts()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Compact);
            encoder.SuppressArtifacts = true;
            encoder.PushStructure(null);
            encoder.WriteSwitchField(1, out string fieldName);
            encoder.WriteEncodingMask(0x03);
            encoder.PopStructure();

            string json = encoder.CloseAndReturnText();
            Assert.That(json, Does.Not.Contain("SwitchField"));
            Assert.That(json, Does.Not.Contain("EncodingMask"));
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

        private static PubSubEncoding.JsonDataSetMessage CreateSimpleDataSetMessage(
            FieldTypeEncodingMask fieldEncoding,
            string fieldName,
            BuiltInType builtInType,
            object value)
        {
            Field field = MakeField(fieldName, builtInType, value);

            var dataSet = new DataSet(fieldName + "DS")
            {
                Fields = [field],
                DataSetMetaData = new DataSetMetaDataType { Name = fieldName + "DS", Fields = [field.FieldMetaData] }
            };

            var dsMsg = new PubSubEncoding.JsonDataSetMessage(dataSet);

            switch (fieldEncoding)
            {
                case FieldTypeEncodingMask.Variant:
                    dsMsg.SetFieldContentMask(DataSetFieldContentMask.None);
                    break;
                case FieldTypeEncodingMask.RawData:
                    dsMsg.SetFieldContentMask(DataSetFieldContentMask.RawData);
                    break;
                case FieldTypeEncodingMask.DataValue:
                    dsMsg.SetFieldContentMask(DataSetFieldContentMask.StatusCode);
                    break;
            }

            return dsMsg;
        }

        private static PubSubEncoding.JsonDataSetMessage CreateDataSetMessageFromFields(
            Field[] fields,
            DataSetFieldContentMask fieldContentMask)
        {
            FieldMetaData[] fieldMetaData = Array.ConvertAll(fields, f => f.FieldMetaData);

            var dataSet = new DataSet("TestDS")
            {
                Fields = fields,
                DataSetMetaData = new DataSetMetaDataType { Name = "TestDS", Fields = [.. fieldMetaData] }
            };

            var dsMsg = new PubSubEncoding.JsonDataSetMessage(dataSet);
            dsMsg.SetFieldContentMask(fieldContentMask);
            return dsMsg;
        }
    }
}
