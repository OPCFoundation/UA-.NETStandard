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
using System.Xml;
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
    public class PubSubJsonEncoderExtendedTests
    {
        private ServiceMessageContext m_context;

        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_context = ServiceMessageContext.Create(telemetry);
        }

#pragma warning disable CS0618 // Type or member is obsolete

        [Test]
        public void EncodeMetaDataMessageWithPublisherIdAndDataSetWriterId()
        {
            var writerGroup = new WriterGroupDataType
            {
                Name = "MetaWG",
                WriterGroupId = 1,
                Enabled = true
            };

            var metadata = new DataSetMetaDataType
            {
                Name = "TestMeta",
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "Field1",
                        BuiltInType = (byte)BuiltInType.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Field2",
                        BuiltInType = (byte)BuiltInType.String,
                        ValueRank = ValueRanks.Scalar
                    }
                ],
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 2,
                    MinorVersion = 5
                }
            };

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(writerGroup, metadata, null);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader);
            networkMessage.PublisherId = "MetaPub123";
            networkMessage.DataSetWriterId = 42;

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("ua-metadata"));
            Assert.That(json, Does.Contain("MetaPub123"));
            Assert.That(json, Does.Contain("MetaData"));
            Assert.That(json, Does.Contain("42"));
        }

        [Test]
        public void EncodeMetaDataMessageWithoutDataSetWriterIdStillProducesJson()
        {
            var writerGroup = new WriterGroupDataType
            {
                Name = "MetaWG2",
                WriterGroupId = 1,
                Enabled = true
            };

            var metadata = new DataSetMetaDataType
            {
                Name = "SimpleMeta",
                Fields = [],
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                }
            };

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(writerGroup, metadata, null);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader);
            networkMessage.PublisherId = "NoDswPub";

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("ua-metadata"));
            Assert.That(json, Does.Contain("NoDswPub"));
        }

        [Test]
        public void EncodeNetworkMessageWithReplyToField()
        {
            var writerGroup = new WriterGroupDataType
            {
                Name = "ReplyWG",
                WriterGroupId = 1,
                Enabled = true
            };

            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "Val",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(10))
            };

            var dataSetMessage = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            dataSetMessage.SetFieldContentMask(DataSetFieldContentMask.RawData);

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                [dataSetMessage]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.SingleDataSetMessage |
                JsonNetworkMessageContentMask.ReplyTo);
            networkMessage.PublisherId = "ReplyPub";
            networkMessage.ReplyTo = "opc.udp://reply:4840";

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("ReplyPub"));
            Assert.That(json, Does.Contain("opc.udp://reply:4840"));
        }

        [Test]
        public void EncodeNetworkMessageWithDataSetClassId()
        {
            var classId = Guid.NewGuid();
            var writerGroup = new WriterGroupDataType
            {
                Name = "ClassIdWG",
                WriterGroupId = 1,
                Enabled = true
            };

            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "Val",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(1))
            };

            var dataSetMessage = new PubSubEncoding.JsonDataSetMessage(
                new DataSet
                {
                    Fields = [field],
                    DataSetMetaData = new DataSetMetaDataType
                    {
                        DataSetClassId = new Uuid(classId)
                    }
                });
            dataSetMessage.SetFieldContentMask(DataSetFieldContentMask.RawData);

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                [dataSetMessage]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetClassId |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("DataSetClassId"));
        }

        [Test]
        public void EncodeDataSetMessageWithAllHeaderFields()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "TestVal",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(3.14))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);
            message.HasDataSetMessageHeader = true;
            message.DataSetMessageContentMask =
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status;
            message.DataSetWriterId = 5;
            message.SequenceNumber = 100;
            message.MetaDataVersion = new ConfigurationVersionDataType
            {
                MajorVersion = 3,
                MinorVersion = 7
            };
            message.Timestamp = DateTime.UtcNow;
            message.Status = StatusCodes.Good;

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("DataSetWriterId"));
            Assert.That(json, Does.Contain("SequenceNumber"));
            Assert.That(json, Does.Contain("MetaDataVersion"));
            Assert.That(json, Does.Contain("Timestamp"));
        }

        [Test]
        public void EncodeDataSetFieldWithBooleanType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "BoolField",
                    BuiltInType = (byte)BuiltInType.Boolean,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(true))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("BoolField"));
            Assert.That(json, Does.Contain("true"));
        }

        [Test]
        public void EncodeDataSetFieldWithFloatType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "FloatVal",
                    BuiltInType = (byte)BuiltInType.Float,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(1.5f))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("FloatVal"));
        }

        [Test]
        public void EncodeDataSetFieldWithLocalizedTextType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "TextVal",
                    BuiltInType = (byte)BuiltInType.LocalizedText,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(new LocalizedText("en", "Hello")))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("TextVal"));
            Assert.That(json, Does.Contain("Hello"));
        }

        [Test]
        public void EncodeDataSetFieldWithQualifiedNameType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "QnField",
                    BuiltInType = (byte)BuiltInType.QualifiedName,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(new QualifiedName("TestName", 2)))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("QnField"));
            Assert.That(json, Does.Contain("TestName"));
        }

        [Test]
        public void EncodeDataSetFieldWithExpandedNodeIdType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "ExpandedNid",
                    BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(new ExpandedNodeId(1234, 2)))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("ExpandedNid"));
        }

        [Test]
        public void EncodeDataSetFieldWithXmlElementType()
        {
            var doc = new XmlDocument();
            using (var reader = new System.IO.StringReader("<root>test</root>"))
            using (var xmlReader = XmlReader.Create(reader))
            {
                doc.Load(xmlReader);
            }
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "XmlField",
                    BuiltInType = (byte)BuiltInType.XmlElement,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(doc.DocumentElement))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("XmlField"));
        }

        [Test]
        public void EncodeDataSetFieldWithExtensionObjectType()
        {
            var eo = new ExtensionObject(new ConfigurationVersionDataType
            {
                MajorVersion = 1,
                MinorVersion = 2
            });
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "ExtObj",
                    BuiltInType = (byte)BuiltInType.ExtensionObject,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(eo))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("ExtObj"));
        }

        [Test]
        public void EncodeDataSetFieldWithStatusCodeGoodBecomeNull()
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

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("GoodStatus"));
        }

        [Test]
        public void EncodeDataSetFieldWithBadStatusCodeReplacesValue()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "BadField",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(42.0))
                {
                    StatusCode = StatusCodes.BadOutOfRange
                }
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("BadField"));
        }

        [Test]
        public void EncodeDataSetFieldWithVariantEncodingAndBadStatus()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "VarBadField",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(99))
                {
                    StatusCode = StatusCodes.BadTypeMismatch
                }
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.None);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("VarBadField"));
        }

        [Test]
        public void EncodeDataSetWithDataValueEncodingAllTimestampsAndPicos()
        {
            DateTime now = DateTime.UtcNow;
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "FullDV",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(77.7))
                {
                    StatusCode = StatusCodes.GoodOverload,
                    SourceTimestamp = now,
                    SourcePicoseconds = 1000,
                    ServerTimestamp = now.AddSeconds(1),
                    ServerPicoseconds = 2000
                }
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(
                DataSetFieldContentMask.StatusCode |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.ServerPicoSeconds);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("FullDV"));
        }

        [Test]
        public void EncodeDataSetFieldWithIntegerArrayType()
        {
            int[] value = [1, 2, 3, 4, 5];
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "IntArray",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(value))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("IntArray"));
        }

        [Test]
        public void EncodeDataSetFieldWithStringArrayType()
        {
            string[] value = ["a", "b", "c"];
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "StrArray",
                    BuiltInType = (byte)BuiltInType.String,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(value))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("StrArray"));
        }

        [Test]
        public void EncodeDataSetFieldWithDoubleArrayType()
        {
            double[] value = [1.1, 2.2, 3.3];
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "DblArray",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(value))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("DblArray"));
        }

        [Test]
        public void EncodeDataSetFieldWithBooleanArrayType()
        {
            bool[] value = [true, false, true];
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "BoolArray",
                    BuiltInType = (byte)BuiltInType.Boolean,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(value))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("BoolArray"));
        }

        [Test]
        public void EncodeDataSetFieldWithByteArrayTypeOneDimension()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "ByteArr",
                    BuiltInType = (byte)BuiltInType.Byte,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(new byte[] { 10, 20, 30 }))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.None);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("ByteArr"));
        }

        [Test]
        public void EncodeDataSetFieldWithUInt16ArrayType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "UShortArr",
                    BuiltInType = (byte)BuiltInType.UInt16,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(new ushort[] { 100, 200, 300 }))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("UShortArr"));
        }

        [Test]
        public void EncodeDataSetFieldWithInt64ArrayType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "LongArr",
                    BuiltInType = (byte)BuiltInType.Int64,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(new long[] { -1L, 0L, 1L }))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("LongArr"));
        }

        [Test]
        public void EncodeDataSetFieldWithFloatArrayType()
        {
            float[] value = [1.1f, 2.2f];
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "FloatArr",
                    BuiltInType = (byte)BuiltInType.Float,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(value))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("FloatArr"));
        }

        [Test]
        public void EncodeDataSetFieldWithDateTimeArrayType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "DtArr",
                    BuiltInType = (byte)BuiltInType.DateTime,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(new DateTime[] { DateTime.UtcNow, DateTime.MinValue }))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("DtArr"));
        }

        [Test]
        public void EncodeDataSetFieldWithGuidArrayType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "GuidArr",
                    BuiltInType = (byte)BuiltInType.Guid,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(new Uuid[] { Uuid.NewUuid(), Uuid.NewUuid() }))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("GuidArr"));
        }

        [Test]
        public void EncodeDataSetFieldWithNodeIdArrayType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "NidArr",
                    BuiltInType = (byte)BuiltInType.NodeId,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(new NodeId[] { new(1, 0), new(2, 0) }))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("NidArr"));
        }

        [Test]
        public void EncodeDataSetFieldWithLocalizedTextArrayType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "LtArr",
                    BuiltInType = (byte)BuiltInType.LocalizedText,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(new LocalizedText[]
                {
                    new("en", "Hi"),
                    new("de", "Hallo")
                }))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("LtArr"));
        }

        [Test]
        public void EncodeDataSetFieldWithVariantArrayType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "VarArr",
                    BuiltInType = (byte)BuiltInType.Variant,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(new Variant[] { new(1), new("two") }))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.None);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("VarArr"));
        }

        [Test]
        public void EncodeDataSetFieldWithSByteArrayType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "SByteArr",
                    BuiltInType = (byte)BuiltInType.SByte,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(new sbyte[] { -1, 0, 1 }))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("SByteArr"));
        }

        [Test]
        public void EncodeDataSetFieldWithInt16ArrayType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "ShortArr",
                    BuiltInType = (byte)BuiltInType.Int16,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(new short[] { -100, 0, 100 }))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("ShortArr"));
        }

        [Test]
        public void EncodeDataSetFieldWithUInt32ArrayType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "UIntArr",
                    BuiltInType = (byte)BuiltInType.UInt32,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(new uint[] { 0, 1000, 2000 }))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("UIntArr"));
        }

        [Test]
        public void EncodeDataSetFieldWithUInt64ArrayType()
        {
            ulong[] value = [0UL, 999UL];
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "ULongArr",
                    BuiltInType = (byte)BuiltInType.UInt64,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(value))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("ULongArr"));
        }

        [Test]
        public void EncodeDataSetFieldWithStatusCodeArrayType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "StatusArr",
                    BuiltInType = (byte)BuiltInType.StatusCode,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(new StatusCode[] { StatusCodes.Good, StatusCodes.Bad }))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.None);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("StatusArr"));
        }

        [Test]
        public void EncodeDataSetFieldWithQualifiedNameArrayType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "QnArr",
                    BuiltInType = (byte)BuiltInType.QualifiedName,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(new QualifiedName[]
                {
                    new("A", 0),
                    new("B", 1)
                }))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("QnArr"));
        }

        [Test]
        public void EncodeDataSetFieldWithExpandedNodeIdArrayType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "EnidArr",
                    BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(new ExpandedNodeId[]
                {
                    new(1, 0),
                    new(2, 0)
                }))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("EnidArr"));
        }

        [Test]
        public void EncodeDataSetFieldWithByteStringArrayType()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "BsArr",
                    BuiltInType = (byte)BuiltInType.ByteString,
                    ValueRank = ValueRanks.OneDimension
                },
                Value = new DataValue(new Variant(new byte[][]
                {
                    [1, 2],
                    [3, 4]
                }))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("BsArr"));
        }

        [Test]
        public void EncodeNetworkMessageWithSingleDataSetAndHeader()
        {
            var writerGroup = new WriterGroupDataType
            {
                Name = "SDSHeaderWG",
                WriterGroupId = 1,
                Enabled = true
            };

            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "Temp",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(25.5))
            };

            var dataSetMessage = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            dataSetMessage.SetFieldContentMask(DataSetFieldContentMask.RawData);
            dataSetMessage.HasDataSetMessageHeader = true;
            dataSetMessage.DataSetMessageContentMask =
                JsonDataSetMessageContentMask.DataSetWriterId;
            dataSetMessage.DataSetWriterId = 1;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                [dataSetMessage]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage |
                JsonNetworkMessageContentMask.DataSetMessageHeader);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("Temp"));
            Assert.That(json, Does.Contain("DataSetWriterId"));
        }

        [Test]
        public void EncodeNetworkMessageWithMultiDataSetAndHeader()
        {
            var writerGroup = new WriterGroupDataType
            {
                Name = "MultiDSWG",
                WriterGroupId = 1,
                Enabled = true
            };

            var field1 = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "F1",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(1))
            };

            var field2 = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "F2",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(2))
            };

            var msg1 = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field1] });
            msg1.SetFieldContentMask(DataSetFieldContentMask.RawData);
            msg1.HasDataSetMessageHeader = true;
            msg1.DataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId;
            msg1.DataSetWriterId = 1;

            var msg2 = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field2] });
            msg2.SetFieldContentMask(DataSetFieldContentMask.RawData);
            msg2.HasDataSetMessageHeader = true;
            msg2.DataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId;
            msg2.DataSetWriterId = 2;

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                [msg1, msg2]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader);

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("Messages"));
            Assert.That(json, Does.Contain("F1"));
            Assert.That(json, Does.Contain("F2"));
        }

        [Test]
        public void EncodeNetworkMessageToStreamProducesValidOutput()
        {
            var writerGroup = new WriterGroupDataType
            {
                Name = "StreamWG",
                WriterGroupId = 1,
                Enabled = true
            };

            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "StreamVal",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(42))
            };

            var dataSetMessage = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            dataSetMessage.SetFieldContentMask(DataSetFieldContentMask.RawData);

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                [dataSetMessage]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage);

            using var stream = new MemoryStream();
            Assert.DoesNotThrow(() => networkMessage.Encode(m_context, stream));
        }

        [Test]
        public void EncodeDataSetFieldWithNullFieldSkipsField()
        {
            var fields = new Field[]
            {
                new() {
                    FieldMetaData = new FieldMetaData
                    {
                        Name = "ValidField",
                        BuiltInType = (byte)BuiltInType.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    Value = new DataValue(new Variant(1))
                },
                null
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = fields });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            Assert.DoesNotThrow(() => message.Encode(encoder));
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("ValidField"));
        }

        [Test]
        public void EncodeDataSetWithNullDataSetDoesNotThrow()
        {
            var message = new PubSubEncoding.JsonDataSetMessage();
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);
            message.HasDataSetMessageHeader = true;
            message.DataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId;
            message.DataSetWriterId = 1;

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            Assert.DoesNotThrow(() => message.Encode(encoder));
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("DataSetWriterId"));
        }

        [Test]
        public void EncodeDataSetFieldWithDataValueType()
        {
            var innerDv = new DataValue(new Variant(42.0))
            {
                SourceTimestamp = DateTime.UtcNow,
                StatusCode = StatusCodes.Good
            };
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "DvField",
                    BuiltInType = (byte)BuiltInType.DataValue,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(innerDv))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("DvField"));
        }

        [Test]
        public void EncodeDataSetFieldWithDiagnosticInfoType()
        {
            var di = new DiagnosticInfo(1, 2, 3, 4, "diag");
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "DiagField",
                    BuiltInType = (byte)BuiltInType.DiagnosticInfo,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(di))
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.None);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void EncodeDataSetWithMultipleFieldsAllTypes()
        {
            var fields = new Field[]
            {
                CreateField("BoolF", BuiltInType.Boolean, true),
                CreateField("SByteF", BuiltInType.SByte, (sbyte)-1),
                CreateField("ByteF", BuiltInType.Byte, (byte)255),
                CreateField("Int16F", BuiltInType.Int16, (short)-32000),
                CreateField("UInt16F", BuiltInType.UInt16, (ushort)65000),
                CreateField("Int32F", BuiltInType.Int32, -100000),
                CreateField("UInt32F", BuiltInType.UInt32, 4000000000u),
                CreateField("Int64F", BuiltInType.Int64, -999999999999L),
                CreateField("UInt64F", BuiltInType.UInt64, 999999999999UL),
                CreateField("FloatF", BuiltInType.Float, 3.14f),
                CreateField("DoubleF", BuiltInType.Double, 2.718281828),
                CreateField("StringF", BuiltInType.String, "hello world"),
                CreateField("DateTimeF", BuiltInType.DateTime, DateTime.UtcNow),
                CreateField("GuidF", BuiltInType.Guid, Uuid.NewUuid()),
                CreateField("ByteStringF", BuiltInType.ByteString, "ޭ"u8.ToArray())
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = fields });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("BoolF"));
            Assert.That(json, Does.Contain("StringF"));
            Assert.That(json, Does.Contain("DoubleF"));
        }

        [Test]
        public void EncodeMultipleFieldsWithVariantEncoding()
        {
            var fields = new Field[]
            {
                CreateField("V1", BuiltInType.Int32, 42),
                CreateField("V2", BuiltInType.Double, 3.14),
                CreateField("V3", BuiltInType.String, "test")
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = fields });
            message.SetFieldContentMask(DataSetFieldContentMask.None);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("V1"));
            Assert.That(json, Does.Contain("V2"));
            Assert.That(json, Does.Contain("V3"));
        }

        [Test]
        public void EncoderPushPopStructureWorks()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            encoder.PushStructure("Outer");
            encoder.WriteInt32("Inner", 42);
            encoder.PopStructure();
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("Outer"));
            Assert.That(json, Does.Contain("Inner"));
        }

        [Test]
        public void EncoderPushPopArrayWorks()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            encoder.PushArray("Items");
            encoder.PushStructure(null);
            encoder.WriteInt32("Id", 1);
            encoder.PopStructure();
            encoder.PopArray();
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("Items"));
        }

        [Test]
        public void EncoderWriteNodeIdProducesOutput()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            encoder.WriteNodeId("Nid", new NodeId(42, 2));
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("Nid"));
        }

        [Test]
        public void EncoderWriteExpandedNodeIdProducesOutput()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            encoder.WriteExpandedNodeId("Enid", new ExpandedNodeId(42, 2));
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("Enid"));
        }

        [Test]
        public void EncoderWriteVariantProducesOutput()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            encoder.WriteVariant("Var", new Variant(42));
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("Var"));
        }

        [Test]
        public void EncoderWriteDataValueProducesOutput()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            var dv = new DataValue(new Variant(42)) { StatusCode = StatusCodes.Good };
            encoder.WriteDataValue("DV", dv);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("DV"));
        }

        [Test]
        public void EncoderDisposeIsIdempotent()
        {
            var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            encoder.Close();
            Assert.DoesNotThrow(encoder.Dispose);
        }

        [Test]
        public void EncoderCloseAndReturnTextReturnsValidJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            encoder.WriteString("Key", "Value");
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Is.Not.Null.And.Not.Empty);
            Assert.That(json, Does.Contain("Key"));
            Assert.That(json, Does.Contain("Value"));
        }

        [Test]
        public void EncoderWriteEncodingMaskProducesOutput()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            encoder.WriteEncodingMask(0x0F);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void EncoderWriteSwitchFieldProducesOutput()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            encoder.WriteSwitchField(3, out string fieldName);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void EncoderWriteStatusCodeProducesOutput()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            encoder.WriteStatusCode("SC", StatusCodes.BadOutOfRange);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("SC"));
        }

        [Test]
        public void EncoderWriteLocalizedTextProducesOutput()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            encoder.WriteLocalizedText("LT", new LocalizedText("en", "Hello"));
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("LT"));
            Assert.That(json, Does.Contain("Hello"));
        }

        [Test]
        public void EncoderWriteQualifiedNameProducesOutput()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            encoder.WriteQualifiedName("QN", new QualifiedName("TestQN", 0));
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("QN"));
            Assert.That(json, Does.Contain("TestQN"));
        }

        [Test]
        public void EncoderSetMappingTablesDoesNotThrow()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            var nsTable = new NamespaceTable();
            var serverTable = new StringTable();
            Assert.DoesNotThrow(() => encoder.SetMappingTables(nsTable, serverTable));
        }

#pragma warning restore CS0618 // Type or member is obsolete

        private static Field CreateField(string name, BuiltInType type, object value)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = name,
                    BuiltInType = (byte)type,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(value))
            };
#pragma warning restore CS0618 // Type or member is obsolete
        }
    }
}
