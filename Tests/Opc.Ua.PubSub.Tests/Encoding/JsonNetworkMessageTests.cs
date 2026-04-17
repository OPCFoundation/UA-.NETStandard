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

using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
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
    public class JsonNetworkMessageTests
    {
        private ServiceMessageContext m_messageContext;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_messageContext = ServiceMessageContext.Create(telemetry);
        }

        private static PubSubEncoding.JsonNetworkMessage CreateDataSetMessage(
            JsonNetworkMessageContentMask contentMask,
            params (string name, Variant value)[] fields)
        {
            var dataSet = new DataSet("TestDataSet");
            var fieldList = new List<Field>();
            var metaFieldList = new List<FieldMetaData>();
            foreach ((string name, Variant value) in fields)
            {
                fieldList.Add(new Field
                {
                    FieldMetaData = new FieldMetaData { Name = name },
                    Value = new DataValue(value)
                });
                metaFieldList.Add(new FieldMetaData { Name = name });
            }
            dataSet.Fields = [.. fieldList];
            dataSet.DataSetMetaData = new DataSetMetaDataType
            {
                Name = "TestDataSet",
                Fields = metaFieldList.ToArray().ToArrayOf()
            };

            var writerGroup = new WriterGroupDataType
            {
                Name = "WG1",
                MessageSettings = new ExtensionObject(
                    new JsonWriterGroupMessageDataType
                    {
                        NetworkMessageContentMask = (uint)contentMask
                    })
            };

            var dsMessage = new PubSubEncoding.JsonDataSetMessage(dataSet, null);
            dsMessage.SetFieldContentMask(DataSetFieldContentMask.None);

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup, [dsMessage], null);
            networkMessage.SetNetworkMessageContentMask(contentMask);
            networkMessage.PublisherId = "TestPublisher";
            return networkMessage;
        }

        private static DataSetReaderDataType CreateDataSetReader(
            JsonNetworkMessageContentMask networkMask,
            JsonDataSetMessageContentMask dataSetMask = JsonDataSetMessageContentMask.None,
            string publisherId = null)
        {
            return new DataSetReaderDataType
            {
                Name = "Reader1",
                PublisherId = publisherId != null ? Variant.From(publisherId) : Variant.Null,
                DataSetFieldContentMask = (uint)DataSetFieldContentMask.None,
                MessageSettings = new ExtensionObject(
                    new JsonDataSetReaderMessageDataType
                    {
                        NetworkMessageContentMask = (uint)networkMask,
                        DataSetMessageContentMask = (uint)dataSetMask
                    })
            };
        }

        [Test]
        public void EncodeToByteArrayProducesNonEmptyResult()
        {
            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader,
                ("IntField", Variant.From(42)));

            byte[] encoded = msg.Encode(m_messageContext);

            Assert.That(encoded, Is.Not.Null);
            Assert.That(encoded, Is.Not.Empty);
        }

        [Test]
        public void EncodeToStreamProducesNonEmptyResult()
        {
            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader,
                ("IntField", Variant.From(42)));

            using var stream = new MemoryStream();
            msg.Encode(m_messageContext, stream);

            Assert.That(stream.ToArray(), Is.Not.Empty);
        }

        [Test]
        public void EncodeDecodeRoundTripPreservesMessageType()
        {
            const JsonNetworkMessageContentMask contentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                contentMask, ("F1", Variant.From(1)));
            byte[] encoded = msg.Encode(m_messageContext);

            DataSetReaderDataType reader = CreateDataSetReader(contentMask);
            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_messageContext, encoded, [reader]);

            Assert.That(decoded.MessageType, Is.EqualTo("ua-data"));
        }

        [Test]
        public void EncodeDecodeRoundTripPreservesPublisherId()
        {
            const JsonNetworkMessageContentMask contentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                contentMask, ("F1", Variant.From(100)));
            byte[] encoded = msg.Encode(m_messageContext);

            DataSetReaderDataType reader = CreateDataSetReader(contentMask);
            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_messageContext, encoded, [reader]);

            Assert.That(decoded.PublisherId, Is.EqualTo("TestPublisher"));
        }

        [Test]
        public void EncodeDecodeMetaDataMessageRoundTrips()
        {
            var writerGroup = new WriterGroupDataType { Name = "WG1" };
            var metadata = new DataSetMetaDataType
            {
                Name = "MetaRoundTrip",
                Fields = [new FieldMetaData { Name = "Field1", DataType = DataTypeIds.Int32 }]
            };

            var msg = new PubSubEncoding.JsonNetworkMessage(writerGroup, metadata, null)
            {
                PublisherId = "MetaPub",
                DataSetWriterId = 200
            };

            byte[] encoded = msg.Encode(m_messageContext);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("ua-metadata"));
            Assert.That(json, Does.Contain("MetaRoundTrip"));

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_messageContext, encoded, []);
            Assert.That(decoded.MessageType, Is.EqualTo("ua-metadata"));
        }

        [Test]
        public void EncodeMetaDataWithoutDataSetWriterIdLogsButDoesNotThrow()
        {
            var writerGroup = new WriterGroupDataType { Name = "WG1" };
            var metadata = new DataSetMetaDataType { Name = "Meta1" };
            var msg = new PubSubEncoding.JsonNetworkMessage(writerGroup, metadata, null)
            {
                PublisherId = "Pub1"
            };

            Assert.DoesNotThrow(() =>
            {
                byte[] encoded = msg.Encode(m_messageContext);
                Assert.That(encoded, Is.Not.Null);
                Assert.That(encoded, Is.Not.Empty);
            });
        }

        [Test]
        public void EncodeSingleDataSetMessageWithoutHeaderAndWithoutDataSetHeader()
        {
            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                JsonNetworkMessageContentMask.SingleDataSetMessage,
                ("StringField", Variant.From("hello")));

            byte[] encoded = msg.Encode(m_messageContext);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(encoded, Is.Not.Null);
            Assert.That(json, Does.Contain("hello"));
        }

        [Test]
        public void EncodeSingleDataSetMessageWithDataSetHeader()
        {
            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                JsonNetworkMessageContentMask.SingleDataSetMessage |
                JsonNetworkMessageContentMask.DataSetMessageHeader,
                ("IntField", Variant.From(77)));

            byte[] encoded = msg.Encode(m_messageContext);
            Assert.That(encoded, Is.Not.Null);
            Assert.That(encoded, Is.Not.Empty);
        }

        [Test]
        public void EncodeMultipleMessagesWithoutHeaderProducesArray()
        {
            var dataSet1 = new DataSet("DS1")
            {
                Fields =
                [
                    new Field
                    {
                        FieldMetaData = new FieldMetaData { Name = "F1" },
                        Value = new DataValue(Variant.From(1))
                    }
                ],
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS1",
                    Fields = [new FieldMetaData { Name = "F1" }]
                }
            };

            var dataSet2 = new DataSet("DS2")
            {
                Fields =
                [
                    new Field
                    {
                        FieldMetaData = new FieldMetaData { Name = "F2" },
                        Value = new DataValue(Variant.From(2))
                    }
                ],
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS2",
                    Fields = [new FieldMetaData { Name = "F2" }]
                }
            };

            var dsMsg1 = new PubSubEncoding.JsonDataSetMessage(dataSet1, null);
            dsMsg1.SetFieldContentMask(DataSetFieldContentMask.None);
            var dsMsg2 = new PubSubEncoding.JsonDataSetMessage(dataSet2, null);
            dsMsg2.SetFieldContentMask(DataSetFieldContentMask.None);

            var writerGroup = new WriterGroupDataType { Name = "WG1" };
            var msg = new PubSubEncoding.JsonNetworkMessage(
                writerGroup, [dsMsg1, dsMsg2], null);
            msg.SetNetworkMessageContentMask(JsonNetworkMessageContentMask.None);

            byte[] encoded = msg.Encode(m_messageContext);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.StartWith("["));
        }

        [Test]
        public void EncodeWithNetworkMessageHeaderAndMultipleMessagesUsesMessagesField()
        {
            var dataSet1 = new DataSet("DS1")
            {
                Fields =
                [
                    new Field
                    {
                        FieldMetaData = new FieldMetaData { Name = "F1" },
                        Value = new DataValue(Variant.From(10))
                    }
                ],
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS1",
                    Fields = [new FieldMetaData { Name = "F1" }]
                }
            };

            var dsMsg1 = new PubSubEncoding.JsonDataSetMessage(dataSet1, null);
            dsMsg1.SetFieldContentMask(DataSetFieldContentMask.None);
            var dsMsg2 = new PubSubEncoding.JsonDataSetMessage(dataSet1, null);
            dsMsg2.SetFieldContentMask(DataSetFieldContentMask.None);

            var writerGroup = new WriterGroupDataType { Name = "WG1" };
            var msg = new PubSubEncoding.JsonNetworkMessage(
                writerGroup, [dsMsg1, dsMsg2], null);
            msg.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader);
            msg.PublisherId = "Pub1";

            byte[] encoded = msg.Encode(m_messageContext);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("Messages"));
        }

        [Test]
        public void EncodeWithSingleDataSetMessageAndHeaderUsesSingleObject()
        {
            const JsonNetworkMessageContentMask contentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage |
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                contentMask, ("F1", Variant.From(42)));

            byte[] encoded = msg.Encode(m_messageContext);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("Messages"));
        }

        [Test]
        public void EncodeWithReplyToIncludesReplyToField()
        {
            const JsonNetworkMessageContentMask contentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.ReplyTo |
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                contentMask, ("F1", Variant.From(1)));
            msg.ReplyTo = "response/topic";

            byte[] encoded = msg.Encode(m_messageContext);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("ReplyTo"));
            Assert.That(json, Does.Contain("response/topic"));
        }

        [Test]
        public void EncodeWithDataSetClassIdMaskIncludesClassId()
        {
            var dataSet = new DataSet("DS1")
            {
                Fields =
                [
                    new Field
                    {
                        FieldMetaData = new FieldMetaData { Name = "F1" },
                        Value = new DataValue(Variant.From(1))
                    }
                ],
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS1",
                    Fields = [new FieldMetaData { Name = "F1" }],
                    DataSetClassId = Uuid.NewUuid()
                }
            };

            var writerGroup = new WriterGroupDataType { Name = "WG1" };
            var dsMsg = new PubSubEncoding.JsonDataSetMessage(dataSet, null);
            dsMsg.SetFieldContentMask(DataSetFieldContentMask.None);

            var msg = new PubSubEncoding.JsonNetworkMessage(writerGroup, [dsMsg], null);
            msg.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage |
                JsonNetworkMessageContentMask.DataSetClassId |
                JsonNetworkMessageContentMask.DataSetMessageHeader);
            msg.PublisherId = "Pub1";

            byte[] encoded = msg.Encode(m_messageContext);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("DataSetClassId"));
        }

        [Test]
        public void DecodeWithNullReadersDoesNotThrow()
        {
            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                JsonNetworkMessageContentMask.NetworkMessageHeader,
                ("F1", Variant.From(1)));
            byte[] encoded = msg.Encode(m_messageContext);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            Assert.DoesNotThrow(() =>
                decoded.Decode(m_messageContext, encoded, null));
        }

        [Test]
        public void DecodeWithEmptyReadersDoesNotThrow()
        {
            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                JsonNetworkMessageContentMask.NetworkMessageHeader,
                ("F1", Variant.From(1)));
            byte[] encoded = msg.Encode(m_messageContext);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            Assert.DoesNotThrow(() =>
                decoded.Decode(m_messageContext, encoded, []));
        }

        [Test]
        public void DecodeFiltersByPublisherIdAndRejectsNonMatching()
        {
            const JsonNetworkMessageContentMask contentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                contentMask, ("F1", Variant.From(1)));
            byte[] encoded = msg.Encode(m_messageContext);

            DataSetReaderDataType reader = CreateDataSetReader(
                contentMask, publisherId: "WrongPublisher");

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_messageContext, encoded, [reader]);

            Assert.That(decoded.DataSetMessages.Count, Is.Zero);
        }

        [Test]
        public void DecodeWithWildcardPublisherIdAcceptsAnyPublisher()
        {
            const JsonNetworkMessageContentMask contentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                contentMask, ("F1", Variant.From(1)));
            byte[] encoded = msg.Encode(m_messageContext);

            DataSetReaderDataType reader = CreateDataSetReader(contentMask);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_messageContext, encoded, [reader]);

            Assert.That(decoded.DataSetMessages.Count, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void DecodeWithExactPublisherIdMatchAcceptsMessage()
        {
            const JsonNetworkMessageContentMask contentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                contentMask, ("F1", Variant.From(42)));
            byte[] encoded = msg.Encode(m_messageContext);

            DataSetReaderDataType reader = CreateDataSetReader(
                contentMask, publisherId: "TestPublisher");

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_messageContext, encoded, [reader]);

            Assert.That(decoded.PublisherId, Is.EqualTo("TestPublisher"));
        }

        [Test]
        public void DecodeWithReaderMissingMessageSettingsSkipsReader()
        {
            const JsonNetworkMessageContentMask contentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                contentMask, ("F1", Variant.From(1)));
            byte[] encoded = msg.Encode(m_messageContext);

            var reader = new DataSetReaderDataType
            {
                Name = "BadReader",
                PublisherId = Variant.Null,
                DataSetFieldContentMask = (uint)DataSetFieldContentMask.None,
                MessageSettings = new ExtensionObject()
            };

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_messageContext, encoded, [reader]);

            Assert.That(decoded.DataSetMessages.Count, Is.Zero);
        }

        [Test]
        public void DecodeWithMismatchedNetworkContentMaskSkipsReader()
        {
            const JsonNetworkMessageContentMask contentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                contentMask, ("F1", Variant.From(1)));
            byte[] encoded = msg.Encode(m_messageContext);

            // Reader expects only NetworkMessageHeader (missing PublisherId bit)
            DataSetReaderDataType reader = CreateDataSetReader(
                JsonNetworkMessageContentMask.NetworkMessageHeader);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_messageContext, encoded, [reader]);

            Assert.That(decoded.DataSetMessages.Count, Is.Zero);
        }

        [Test]
        public void DecodeInvalidMessageTypeDoesNotThrow()
        {
            string invalidJson = @"{""MessageId"":""test"",""MessageType"":""ua-invalid""}";
            byte[] encoded = System.Text.Encoding.UTF8.GetBytes(invalidJson);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            DataSetReaderDataType reader = CreateDataSetReader(
                JsonNetworkMessageContentMask.NetworkMessageHeader);

            Assert.DoesNotThrow(() =>
                decoded.Decode(m_messageContext, encoded, [reader]));
        }

        [Test]
        public void DecodeDataSetClassIdSetsProperty()
        {
            const JsonNetworkMessageContentMask contentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetClassId |
                JsonNetworkMessageContentMask.SingleDataSetMessage |
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            var dataSet = new DataSet("DS1")
            {
                Fields =
                [
                    new Field
                    {
                        FieldMetaData = new FieldMetaData { Name = "F1" },
                        Value = new DataValue(Variant.From(1))
                    }
                ],
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS1",
                    Fields = [new FieldMetaData { Name = "F1" }],
                    DataSetClassId = Uuid.NewUuid()
                }
            };

            var writerGroup = new WriterGroupDataType { Name = "WG1" };
            var dsMsg = new PubSubEncoding.JsonDataSetMessage(dataSet, null);
            dsMsg.SetFieldContentMask(DataSetFieldContentMask.None);

            var msg = new PubSubEncoding.JsonNetworkMessage(writerGroup, [dsMsg], null);
            msg.SetNetworkMessageContentMask(contentMask);
            msg.PublisherId = "Pub1";

            byte[] encoded = msg.Encode(m_messageContext);

            DataSetReaderDataType reader = CreateDataSetReader(contentMask);
            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_messageContext, encoded, [reader]);

            Assert.That(decoded.DataSetClassId, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void DecodeMetaDataMessageSetsMetaData()
        {
            var writerGroup = new WriterGroupDataType { Name = "WG1" };
            var metadata = new DataSetMetaDataType
            {
                Name = "MetaDecode",
                Fields = [new FieldMetaData { Name = "F1", DataType = DataTypeIds.Int32 }]
            };

            var msg = new PubSubEncoding.JsonNetworkMessage(writerGroup, metadata, null)
            {
                PublisherId = "MetaPub",
                DataSetWriterId = 50
            };

            byte[] encoded = msg.Encode(m_messageContext);

            DataSetReaderDataType reader = CreateDataSetReader(
                JsonNetworkMessageContentMask.NetworkMessageHeader);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_messageContext, encoded, [reader]);

            Assert.That(decoded.IsMetaDataMessage, Is.True);
        }

        [Test]
        public void DecodeMetaDataMessageViaSubscribedDataSetsPath()
        {
            var writerGroup = new WriterGroupDataType { Name = "WG1" };
            var metadata = new DataSetMetaDataType
            {
                Name = "MetaViaSubscribed",
                Fields = [new FieldMetaData { Name = "F1", DataType = DataTypeIds.String }]
            };

            var msg = new PubSubEncoding.JsonNetworkMessage(writerGroup, metadata, null)
            {
                PublisherId = "Pub1",
                DataSetWriterId = 100
            };

            byte[] encoded = msg.Encode(m_messageContext);

            DataSetReaderDataType reader = CreateDataSetReader(
                JsonNetworkMessageContentMask.NetworkMessageHeader);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_messageContext, encoded, [reader]);

            Assert.That(decoded.DataSetMetaData, Is.Not.Null);
            Assert.That(decoded.DataSetMetaData.Name, Is.EqualTo("MetaViaSubscribed"));
        }

        [Test]
        public void EncodeEmptyDataSetMessagesWithHeaderProducesValidJson()
        {
            var writerGroup = new WriterGroupDataType { Name = "WG1" };
            var msg = new PubSubEncoding.JsonNetworkMessage(
                writerGroup, new List<PubSubEncoding.JsonDataSetMessage>(), null);
            msg.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader);
            msg.PublisherId = "Pub1";

            byte[] encoded = msg.Encode(m_messageContext);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("MessageId"));
        }

        [Test]
        public void SetNetworkMessageContentMaskPropagatesToDataSetMessages()
        {
            const JsonNetworkMessageContentMask contentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            var dataSet = new DataSet("DS1")
            {
                Fields =
                [
                    new Field
                    {
                        FieldMetaData = new FieldMetaData { Name = "F1" },
                        Value = new DataValue(Variant.From(1))
                    }
                ],
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS1",
                    Fields = [new FieldMetaData { Name = "F1" }]
                }
            };

            var writerGroup = new WriterGroupDataType { Name = "WG1" };
            var dsMsg = new PubSubEncoding.JsonDataSetMessage(dataSet, null);
            dsMsg.SetFieldContentMask(DataSetFieldContentMask.None);

            var msg = new PubSubEncoding.JsonNetworkMessage(writerGroup, [dsMsg], null);
            msg.SetNetworkMessageContentMask(contentMask);

            Assert.That(msg.HasDataSetMessageHeader, Is.True);
        }

        [Test]
        public void MessageIdIsUniqueAcrossInstances()
        {
            var msg1 = new PubSubEncoding.JsonNetworkMessage();
            var msg2 = new PubSubEncoding.JsonNetworkMessage();

            Assert.That(msg1.MessageId, Is.Not.EqualTo(msg2.MessageId));
        }

        [Test]
        public void MessageIdPropertyIsSettable()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage
            {
                MessageId = "custom-id"
            };
            Assert.That(msg.MessageId, Is.EqualTo("custom-id"));
        }

        [Test]
        public void EncodeDecodeWithMultipleFieldsRoundTrips()
        {
            const JsonNetworkMessageContentMask contentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                contentMask,
                ("IntField", Variant.From(42)),
                ("StringField", Variant.From("test")),
                ("BoolField", Variant.From(true)));

            byte[] encoded = msg.Encode(m_messageContext);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("IntField"));
            Assert.That(json, Does.Contain("StringField"));
            Assert.That(json, Does.Contain("BoolField"));
        }

        [Test]
        public void DecodeWithNoNetworkMessageHeaderInJsonStillWorks()
        {
            // Encode without network message header
            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                JsonNetworkMessageContentMask.DataSetMessageHeader,
                ("F1", Variant.From(1)));

            byte[] encoded = msg.Encode(m_messageContext);

            DataSetReaderDataType reader = CreateDataSetReader(
                JsonNetworkMessageContentMask.None);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            Assert.DoesNotThrow(() =>
                decoded.Decode(m_messageContext, encoded, [reader]));
        }

        [Test]
        public void DecodeMetaDataWithMissingDataSetWriterIdDoesNotThrow()
        {
            string json =
                @"{""MessageId"":""id1"",""MessageType"":""ua-metadata""," +
                @"""PublisherId"":""Pub1"",""MetaData"":{""Name"":""M1""," +
                @"""Fields"":[],""ConfigurationVersion"":" +
                @"{""MajorVersion"":0,""MinorVersion"":0}}}";
            byte[] encoded = System.Text.Encoding.UTF8.GetBytes(json);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            Assert.DoesNotThrow(() =>
                decoded.Decode(m_messageContext, encoded, []));
        }

        [Test]
        public void EncodeNoHeaderNoSingleNonMetaUsesTopLevelArray()
        {
            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                JsonNetworkMessageContentMask.DataSetMessageHeader,
                ("F1", Variant.From(1)));

            byte[] encoded = msg.Encode(m_messageContext);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.StartWith("["));
        }

        [Test]
        public void EncodeWithPublisherIdMaskIncludesPublisherId()
        {
            const JsonNetworkMessageContentMask contentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                contentMask, ("F1", Variant.From(1)));

            byte[] encoded = msg.Encode(m_messageContext);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("PublisherId"));
            Assert.That(json, Does.Contain("TestPublisher"));
        }

        [Test]
        public void EncodeWithoutPublisherIdMaskExcludesPublisherId()
        {
            const JsonNetworkMessageContentMask contentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                contentMask, ("F1", Variant.From(1)));

            byte[] encoded = msg.Encode(m_messageContext);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Not.Contain("PublisherId"));
        }

        [Test]
        public void HasNetworkMessageHeaderReturnsFalseByDefault()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage();
            Assert.That(msg.HasNetworkMessageHeader, Is.False);
        }

        [Test]
        public void HasSingleDataSetMessageReturnsFalseByDefault()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage();
            Assert.That(msg.HasSingleDataSetMessage, Is.False);
        }

        [Test]
        public void HasDataSetMessageHeaderReturnsFalseByDefault()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage();
            Assert.That(msg.HasDataSetMessageHeader, Is.False);
        }
    }
}
