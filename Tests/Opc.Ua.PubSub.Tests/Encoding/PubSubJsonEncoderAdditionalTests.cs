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
    public class PubSubJsonEncoderAdditionalTests
    {
        private ServiceMessageContext m_context;

        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_context = ServiceMessageContext.Create(telemetry);
        }

        [Test]
        public void EncodeNetworkMessageWithHeaderProducesValidJson()
        {
            var writerGroup = new WriterGroupDataType
            {
                Name = "WriterGroup1",
                WriterGroupId = 1,
                Enabled = true,
                PublishingInterval = 1000,
                KeepAliveTime = 5000,
                MaxNetworkMessageSize = 1500,
                MessageSettings = new ExtensionObject(new JsonWriterGroupMessageDataType
                {
                    NetworkMessageContentMask = (uint)(
                        JsonNetworkMessageContentMask.NetworkMessageHeader |
                        JsonNetworkMessageContentMask.DataSetMessageHeader |
                        JsonNetworkMessageContentMask.PublisherId)
                })
            };

#pragma warning disable CS0618 // Type or member is obsolete
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "Temperature",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(22.5))
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var dataSetMessage = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            dataSetMessage.SetFieldContentMask(DataSetFieldContentMask.RawData);
            dataSetMessage.HasDataSetMessageHeader = true;
            dataSetMessage.DataSetMessageContentMask =
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.SequenceNumber;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                [dataSetMessage]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.PublisherId);
            networkMessage.PublisherId = "Publisher1";

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("MessageId"));
            Assert.That(json, Does.Contain("ua-data"));
            Assert.That(json, Does.Contain("Publisher1"));
        }

        [Test]
        public void EncodeNetworkMessageWithSingleDataSetMessageProducesValidJson()
        {
            var writerGroup = new WriterGroupDataType
            {
                Name = "WriterGroup1",
                WriterGroupId = 1,
                Enabled = true,
                MessageSettings = new ExtensionObject(new JsonWriterGroupMessageDataType
                {
                    NetworkMessageContentMask = (uint)(
                        JsonNetworkMessageContentMask.NetworkMessageHeader |
                        JsonNetworkMessageContentMask.SingleDataSetMessage)
                })
            };

#pragma warning disable CS0618 // Type or member is obsolete
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "Value1",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(100))
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var dataSetMessage = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            dataSetMessage.SetFieldContentMask(DataSetFieldContentMask.RawData);

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                [dataSetMessage]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("Value1"));
            Assert.That(json, Does.Contain("MessageId"));
        }

        [Test]
        public void EncodeNetworkMessageWithMultipleDataSetMessagesProducesJsonArray()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var field1 = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "Temp",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(20.0))
            };
            var field2 = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "Pressure",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(101.3))
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var msg1 = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field1] });
            msg1.SetFieldContentMask(DataSetFieldContentMask.RawData);

            var msg2 = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field2] });
            msg2.SetFieldContentMask(DataSetFieldContentMask.RawData);

            var writerGroup = new WriterGroupDataType
            {
                Name = "WG1",
                WriterGroupId = 1,
                Enabled = true
            };

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                [msg1, msg2]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("Messages"));
            Assert.That(json, Does.Contain("Temp"));
            Assert.That(json, Does.Contain("Pressure"));
        }

        [Test]
        public void EncodeMetaDataNetworkMessageProducesValidJson()
        {
            var writerGroup = new WriterGroupDataType
            {
                Name = "MetaDataWG",
                WriterGroupId = 1,
                Enabled = true
            };

            var metadata = new DataSetMetaDataType
            {
                Name = "TestDataSet",
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "Field1",
                        BuiltInType = (byte)BuiltInType.Int32,
                        ValueRank = ValueRanks.Scalar
                    }
                ],
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                }
            };

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(writerGroup, metadata, null);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader);
            networkMessage.PublisherId = "MetaPublisher";
            networkMessage.DataSetWriterId = 10;

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("ua-metadata"));
            Assert.That(json, Does.Contain("MetaData"));
        }

        [Test]
        public void EncodeNoHeaderSingleDataSetMessageProducesPayloadOnly()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "RawField",
                    BuiltInType = (byte)BuiltInType.String,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant("hello"))
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var dataSetMessage = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            dataSetMessage.SetFieldContentMask(DataSetFieldContentMask.RawData);

            var writerGroup = new WriterGroupDataType
            {
                Name = "WG",
                WriterGroupId = 1,
                Enabled = true
            };

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                [dataSetMessage]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("RawField"));
            Assert.That(json, Does.Not.Contain("MessageId"));
        }

        [Test]
        public void EncodeNoHeaderMultipleDataSetMessagesAsArray()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var field1 = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "A",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(1))
            };
            var field2 = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "B",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(2))
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var msg1 = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field1] });
            msg1.SetFieldContentMask(DataSetFieldContentMask.RawData);

            var msg2 = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field2] });
            msg2.SetFieldContentMask(DataSetFieldContentMask.RawData);

            var writerGroup = new WriterGroupDataType
            {
                Name = "WG",
                WriterGroupId = 1,
                Enabled = true
            };

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                [msg1, msg2]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.DataSetMessageHeader);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("A"));
            Assert.That(json, Does.Contain("B"));
        }

#pragma warning disable CS0618 // Type or member is obsolete
        [Test]
        public void EncodeDataSetFieldWithByteStringType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "ByteData",
                    BuiltInType = (byte)BuiltInType.ByteString,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(new byte[] { 1, 2, 3, 4 }))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("ByteData"));
        }

        [Test]
        public void EncodeDataSetFieldWithGuidType()
        {
            var testGuid = Guid.NewGuid();
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "GuidField",
                    BuiltInType = (byte)BuiltInType.Guid,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(new Uuid(testGuid)))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("GuidField"));
        }

        [Test]
        public void EncodeDataSetFieldWithDateTimeType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "Timestamp",
                    BuiltInType = (byte)BuiltInType.DateTime,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(DateTime.UtcNow))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("Timestamp"));
        }

        [Test]
        public void EncodeDataSetFieldWithNodeIdType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "NodeIdField",
                    BuiltInType = (byte)BuiltInType.NodeId,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(new NodeId(1234, 2)))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("NodeIdField"));
        }

        [Test]
        public void EncodeDataSetFieldWithStatusCodeType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "StatusField",
                    BuiltInType = (byte)BuiltInType.StatusCode,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(StatusCodes.BadUnexpectedError))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.StatusCode);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("StatusField"));
        }

        [Test]
        public void EncodeDataSetFieldWithUInt64Type()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "BigNumber",
                    BuiltInType = (byte)BuiltInType.UInt64,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant((ulong)9999999999))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("BigNumber"));
        }

        [Test]
        public void EncodeDataSetFieldWithInt64Type()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "SignedBig",
                    BuiltInType = (byte)BuiltInType.Int64,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant((long)-9999999999))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("SignedBig"));
        }

        [Test]
        public void EncodeDataSetFieldWithByteType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "ByteVal",
                    BuiltInType = (byte)BuiltInType.Byte,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant((byte)200))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("ByteVal"));
        }

        [Test]
        public void EncodeDataSetFieldWithSByteType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "SByteVal",
                    BuiltInType = (byte)BuiltInType.SByte,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant((sbyte)-50))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("SByteVal"));
        }

        [Test]
        public void EncodeDataSetFieldWithUInt16Type()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "UShortVal",
                    BuiltInType = (byte)BuiltInType.UInt16,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant((ushort)60000))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("UShortVal"));
        }

        [Test]
        public void EncodeDataSetFieldWithInt16Type()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "ShortVal",
                    BuiltInType = (byte)BuiltInType.Int16,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant((short)-30000))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("ShortVal"));
        }

        [Test]
        public void EncodeDataSetFieldWithUInt32Type()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "UIntVal",
                    BuiltInType = (byte)BuiltInType.UInt32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant((uint)4000000000))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("UIntVal"));
        }

        [Test]
        public void EncodeDataSetWithDataValueFieldEncoding()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "DVField",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(42.0))
                {
                    SourceTimestamp = DateTime.UtcNow,
                    ServerTimestamp = DateTime.UtcNow,
                    StatusCode = StatusCodes.Good
                }
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(
                DataSetFieldContentMask.StatusCode |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.ServerTimestamp);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("DVField"));
        }

        [Test]
        public void EncodeDataSetWithSourcePicoSecondsFieldEncoding()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "PicoField",
                    BuiltInType = (byte)BuiltInType.Float,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(1.0f))
                {
                    SourceTimestamp = DateTime.UtcNow,
                    SourcePicoseconds = 1234,
                    StatusCode = StatusCodes.Good
                }
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("PicoField"));
        }

        [Test]
        public void EncodeDataSetWithServerPicoSecondsFieldEncoding()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "ServerPico",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(77))
                {
                    ServerTimestamp = DateTime.UtcNow,
                    ServerPicoseconds = 5678,
                    StatusCode = StatusCodes.Good
                }
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.ServerPicoSeconds);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("ServerPico"));
        }

        [Test]
        public void EncodeDataSetWithNonReversibleRawDataFieldEncoding()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "NonRevRaw",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(99.9))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(
                m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("NonRevRaw"));
        }

        [Test]
        public void EncodeDataSetWithVariantFieldEncodingReversible()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "VarField",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(55))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.None);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("VarField"));
        }

        [Test]
        public void EncodeDataSetWithVariantFieldEncodingNonReversible()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "VarFieldNR",
                    BuiltInType = (byte)BuiltInType.String,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant("NonRevVariant"))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.None);

            using var encoder = new PubSubJsonEncoder(
                m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("VarFieldNR"));
        }

        [Test]
        public void EncodeDataSetWithNullDataValueField()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "NullField",
                    BuiltInType = (byte)BuiltInType.String,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(Variant.Null)
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Is.Not.Null);
        }

        [Test]
        public void EncodeDataSetWithBadStatusCodeField()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "BadField",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(StatusCodes.BadNodeIdUnknown)
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(
                DataSetFieldContentMask.StatusCode |
                DataSetFieldContentMask.SourceTimestamp);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("BadField"));
        }

        [Test]
        public void EncodeDataSetMessageWithDataSetMessageHeader()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "HeaderField",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(10))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);
            message.HasDataSetMessageHeader = true;
            message.DataSetMessageContentMask =
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status;
            message.DataSetWriterId = 5;
            message.SequenceNumber = 42;

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("Payload"));
        }
#pragma warning restore CS0618 // Type or member is obsolete

        [Test]
        public void EncodeNetworkMessageWithReplyTo()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "Reply",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(1))
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var dataSetMessage = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            dataSetMessage.SetFieldContentMask(DataSetFieldContentMask.RawData);

            var writerGroup = new WriterGroupDataType
            {
                Name = "WG",
                WriterGroupId = 1,
                Enabled = true
            };

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                [dataSetMessage]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage |
                JsonNetworkMessageContentMask.ReplyTo);
            networkMessage.ReplyTo = "opc.mqtt://reply/topic";

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("ReplyTo"));
            Assert.That(json, Does.Contain("opc.mqtt://reply/topic"));
        }

        [Test]
        public void EncodeNetworkMessageWithDataSetClassId()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var classId = Uuid.NewUuid();
            var metaData = new DataSetMetaDataType
            {
                Name = "ClassDS",
                DataSetClassId = (Guid)classId,
                Fields = []
            };

            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "ClassField",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(7))
            };

            var dataSet = new DataSet
            {
                Fields = [field],
                DataSetMetaData = metaData
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var dataSetMessage = new PubSubEncoding.JsonDataSetMessage(dataSet);
            dataSetMessage.SetFieldContentMask(DataSetFieldContentMask.RawData);

            var writerGroup = new WriterGroupDataType
            {
                Name = "WG",
                WriterGroupId = 1,
                Enabled = true
            };

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                [dataSetMessage]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage |
                JsonNetworkMessageContentMask.DataSetClassId |
                JsonNetworkMessageContentMask.PublisherId);
            networkMessage.PublisherId = "Pub1";

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("DataSetClassId"));
        }

        [Test]
        public void WriteVariantWithComplexTypes()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteVariant("Var1", new Variant(42));
            encoder.WriteVariant("Var2", new Variant("hello"));
            encoder.WriteVariant("Var3", new Variant(3.14));
            encoder.WriteVariant("Var4", new Variant(true));
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("Var1"));
            Assert.That(result, Does.Contain("Var2"));
            Assert.That(result, Does.Contain("Var3"));
            Assert.That(result, Does.Contain("Var4"));
        }

        [Test]
        public void WriteDataValueProducesJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteDataValue(
                "DV",
                new DataValue(new Variant(99), StatusCodes.Good, DateTime.UtcNow));
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("DV"));
        }

        [Test]
        public void WriteExtensionObjectProducesJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            var extObj = new ExtensionObject(new WriterGroupDataType { Enabled = true, Name = "TestWG" });
            encoder.WriteExtensionObject("ExtObj", extObj);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("ExtObj"));
        }

        [Test]
        public void WriteNodeIdProducesJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteNodeId("NId", new NodeId(100, 2));
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("NId"));
        }

        [Test]
        public void WriteExpandedNodeIdProducesJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteExpandedNodeId(
                "ENId", new ExpandedNodeId(200, 2));
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("ENId"));
        }

        [Test]
        public void WriteQualifiedNameProducesJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteQualifiedName("QN", new QualifiedName("TestName", 1));
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("QN"));
        }

        [Test]
        public void WriteLocalizedTextProducesJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteLocalizedText("LT", new LocalizedText("en", "Hello"));
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("LT"));
        }

        [Test]
        public void WriteStatusCodeProducesJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteStatusCode("SC", StatusCodes.BadNodeIdUnknown);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("SC"));
        }

        [Test]
        public void WriteByteStringProducesJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteByteString("BS", (ByteString)new byte[] { 0x01, 0x02, 0x03 });
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("BS"));
        }

        [Test]
        public void WriteDateTimeProducesJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteDateTime("DT", DateTime.UtcNow);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("DT"));
        }

        [Test]
        public void WriteGuidProducesJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteGuid("GU", Uuid.NewUuid());
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("GU"));
        }

        [Test]
        public void EncodeVerboseModeSetsEncoding()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Verbose);
            Assert.That(encoder.EncodingToUse, Is.EqualTo(PubSubJsonEncoding.Verbose));

            encoder.PushStructure(null);
            encoder.WriteInt32("Val", 1);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("Val"));
        }

        [Test]
        public void EncodeCompactModeSetsEncoding()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Compact);
            Assert.That(encoder.EncodingToUse, Is.EqualTo(PubSubJsonEncoding.Compact));

            encoder.PushStructure(null);
            encoder.WriteInt32("Val", 2);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("Val"));
        }

        [Test]
        public void ForceNamespaceUriPropertyIsSettable()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.ForceNamespaceUri = true;
            Assert.That(encoder.ForceNamespaceUri, Is.True);

            encoder.ForceNamespaceUri = false;
            Assert.That(encoder.ForceNamespaceUri, Is.False);
        }

        [Test]
        public void EncodeNodeIdAsStringPropertyIsSettable()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.EncodeNodeIdAsString = true;
            Assert.That(encoder.EncodeNodeIdAsString, Is.True);
        }

        [Test]
        public void ForceNamespaceUriForIndex1PropertyIsSettable()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.ForceNamespaceUriForIndex1 = true;
            Assert.That(encoder.ForceNamespaceUriForIndex1, Is.True);
        }

        [Test]
        public void IncludeDefaultValuesPropertyIsSettable()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.IncludeDefaultValues = true;
            Assert.That(encoder.IncludeDefaultValues, Is.True);

            encoder.IncludeDefaultValues = false;
            Assert.That(encoder.IncludeDefaultValues, Is.False);
        }

        [Test]
        public void IncludeDefaultNumberValuesPropertyIsSettable()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.IncludeDefaultNumberValues = true;
            Assert.That(encoder.IncludeDefaultNumberValues, Is.True);
        }

        [Test]
        public void EncodingTypeIsJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            Assert.That(encoder.EncodingType, Is.EqualTo(EncodingType.Json));
        }

        [Test]
        public void PushAndPopNamespaceDoesNotThrow()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            Assert.DoesNotThrow(() =>
            {
                encoder.PushNamespace("http://test.org");
                encoder.PopNamespace();
            });
        }

        [Test]
        public void WriteInt32ArrayProducesJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteInt32Array("Arr", [1, 2, 3]);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("Arr"));
        }

        [Test]
        public void WriteStringArrayProducesJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteStringArray("StrArr", ["a", "b", "c"]);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("StrArr"));
        }

        [Test]
        public void WriteDoubleArrayProducesJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteDoubleArray("DblArr", [1.1, 2.2, 3.3]);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("DblArr"));
        }

        [Test]
        public void WriteBooleanArrayProducesJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteBooleanArray("BoolArr", [true, false, true]);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("BoolArr"));
        }

        [Test]
        public void EncodeEmptyNetworkMessageProducesValidJson()
        {
            var writerGroup = new WriterGroupDataType
            {
                Name = "EmptyWG",
                WriterGroupId = 1,
                Enabled = true
            };

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                []);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("MessageId"));
        }

        [Test]
        public void EncodeNetworkMessageWithNoHeaderSingleDataSetWithHeader()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "SingleHeaderField",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(42))
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var dataSetMessage = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            dataSetMessage.SetFieldContentMask(DataSetFieldContentMask.RawData);
            dataSetMessage.HasDataSetMessageHeader = true;
            dataSetMessage.DataSetMessageContentMask =
                JsonDataSetMessageContentMask.DataSetWriterId;
            dataSetMessage.DataSetWriterId = 1;

            var writerGroup = new WriterGroupDataType
            {
                Name = "WG",
                WriterGroupId = 1,
                Enabled = true
            };

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                [dataSetMessage]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.SingleDataSetMessage |
                JsonNetworkMessageContentMask.DataSetMessageHeader);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("SingleHeaderField"));
        }

        [Test]
        public void WriteVariantArrayProducesJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteVariantArray(
                "VarArr",
                new Variant[] { new(1), new("two"), new(3.0) });
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("VarArr"));
        }

        [Test]
        public void WriteDataValueArrayProducesJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteDataValueArray(
                "DVArr",
                new DataValue[]
                {
                    new(new Variant(1)),
                    new(new Variant(2))
                });
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("DVArr"));
        }
    }
}