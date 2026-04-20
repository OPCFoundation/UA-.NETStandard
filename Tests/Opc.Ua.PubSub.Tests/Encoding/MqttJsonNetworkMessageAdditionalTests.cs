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
    public class MqttJsonNetworkMessageAdditionalTests
    {
        private ServiceMessageContext m_messageContext;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_messageContext = ServiceMessageContext.Create(telemetry);
        }

        private PubSubEncoding.JsonNetworkMessage CreateDataSetMessage(
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
                Enabled = true,
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
                writerGroup,
                [dsMessage],
                null);
            networkMessage.SetNetworkMessageContentMask(contentMask);
            networkMessage.PublisherId = "Publisher1";
            return networkMessage;
        }

        [Test]
        public void DefaultConstructorCreatesValidMessage()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage();
            Assert.That(msg, Is.Not.Null);
            Assert.That(msg.MessageId, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ConstructorWithWriterGroupAndMessagesCreatesDataSetMessage()
        {
            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "WG1" };
            var messages = new List<PubSubEncoding.JsonDataSetMessage>();
            var msg = new PubSubEncoding.JsonNetworkMessage(writerGroup, messages, null);
            Assert.That(msg.MessageType, Is.EqualTo("ua-data"));
        }

        [Test]
        public void ConstructorWithMetaDataCreatesMetaDataMessage()
        {
            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "WG1" };
            var metadata = new DataSetMetaDataType { Name = "TestMeta" };
            var msg = new PubSubEncoding.JsonNetworkMessage(writerGroup, metadata, null);
            Assert.That(msg.MessageType, Is.EqualTo("ua-metadata"));
        }

        [Test]
        public void SetNetworkMessageContentMaskUpdatesProperty()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage();
            const JsonNetworkMessageContentMask mask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId;
            msg.SetNetworkMessageContentMask(mask);
            Assert.That(msg.NetworkMessageContentMask, Is.EqualTo(mask));
        }

        [Test]
        public void HasNetworkMessageHeaderReturnsTrueWhenSet()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage();
            msg.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader);
            Assert.That(msg.HasNetworkMessageHeader, Is.True);
        }

        [Test]
        public void HasSingleDataSetMessageReturnsTrueWhenSet()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage();
            msg.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.SingleDataSetMessage);
            Assert.That(msg.HasSingleDataSetMessage, Is.True);
        }

        [Test]
        public void HasDataSetMessageHeaderReturnsTrueWhenSet()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage();
            msg.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.DataSetMessageHeader);
            Assert.That(msg.HasDataSetMessageHeader, Is.True);
        }

        [Test]
        public void EncodeWithNetworkMessageHeaderProducesBytes()
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
        public void EncodeDecodeRoundTripWithHeaderPreservesPublisherId()
        {
            const JsonNetworkMessageContentMask contentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(contentMask, ("IntField", Variant.From(42)));
            byte[] encoded = msg.Encode(m_messageContext);

            var reader = new DataSetReaderDataType
            {
                Enabled = true,
                Name = "Reader1",
                PublisherId = Variant.Null,
                DataSetFieldContentMask = (uint)DataSetFieldContentMask.None,
                MessageSettings = new ExtensionObject(
                    new JsonDataSetReaderMessageDataType
                    {
                        NetworkMessageContentMask = (uint)contentMask,
                        DataSetMessageContentMask =
                            (uint)JsonDataSetMessageContentMask.None
                    })
            };

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(
                m_messageContext,
                encoded,
                [reader]);
            Assert.That(decoded.PublisherId, Is.EqualTo("Publisher1"));
        }

        [Test]
        public void EncodeSingleDataSetMessageWithoutHeaderProducesBytes()
        {
            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                JsonNetworkMessageContentMask.SingleDataSetMessage,
                ("Field1", Variant.From("hello")));

            byte[] encoded = msg.Encode(m_messageContext);
            Assert.That(encoded, Is.Not.Null);
            Assert.That(encoded, Is.Not.Empty);
        }

        [Test]
        public void EncodeSingleDataSetMessageWithDataSetHeaderProducesBytes()
        {
            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                JsonNetworkMessageContentMask.SingleDataSetMessage |
                JsonNetworkMessageContentMask.DataSetMessageHeader,
                ("Field1", Variant.From(1)));

            byte[] encoded = msg.Encode(m_messageContext);
            Assert.That(encoded, Is.Not.Null);
            Assert.That(encoded, Is.Not.Empty);
        }

        [Test]
        public void EncodeMultipleDataSetMessagesWithoutHeaderProducesBytes()
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

            var dsMsg1 = new PubSubEncoding.JsonDataSetMessage(dataSet1, null);
            dsMsg1.SetFieldContentMask(DataSetFieldContentMask.None);

            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "WG1" };
            var msg = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                [dsMsg1],
                null);
            msg.SetNetworkMessageContentMask(JsonNetworkMessageContentMask.None);

            byte[] encoded = msg.Encode(m_messageContext);
            Assert.That(encoded, Is.Not.Null);
            Assert.That(encoded, Is.Not.Empty);
        }

        [Test]
        public void EncodeMetaDataMessageProducesBytes()
        {
            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "WG1" };
            var metadata = new DataSetMetaDataType
            {
                Name = "MetaTest",
                Fields = [new FieldMetaData { Name = "F1", DataType = DataTypeIds.Int32 }]
            };
            var msg = new PubSubEncoding.JsonNetworkMessage(writerGroup, metadata, null)
            {
                PublisherId = "Pub1",
                DataSetWriterId = 100
            };

            byte[] encoded = msg.Encode(m_messageContext);
            Assert.That(encoded, Is.Not.Null);
            string json = System.Text.Encoding.UTF8.GetString(encoded);
            Assert.That(json, Does.Contain("ua-metadata"));
        }

        [Test]
        public void DecodeWithEmptyReadersDoesNotThrow()
        {
            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                JsonNetworkMessageContentMask.NetworkMessageHeader,
                ("F1", Variant.From(1)));
            byte[] encoded = msg.Encode(m_messageContext);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            Assert.DoesNotThrow(
                () => decoded.Decode(
                    m_messageContext,
                    encoded,
                    []));
        }

        [Test]
        public void DecodeWithNullReadersDoesNotThrow()
        {
            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                JsonNetworkMessageContentMask.NetworkMessageHeader,
                ("F1", Variant.From(1)));
            byte[] encoded = msg.Encode(m_messageContext);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            Assert.DoesNotThrow(
                () => decoded.Decode(m_messageContext, encoded, null));
        }

        [Test]
        public void PublisherIdPropertyRoundTrips()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage
            {
                PublisherId = "TestPub"
            };
            Assert.That(msg.PublisherId, Is.EqualTo("TestPub"));
        }

        [Test]
        public void DataSetClassIdPropertyRoundTrips()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage
            {
                DataSetClassId = "ClassA"
            };
            Assert.That(msg.DataSetClassId, Is.EqualTo("ClassA"));
        }

        [Test]
        public void ReplyToPropertyRoundTrips()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage
            {
                ReplyTo = "reply/topic"
            };
            Assert.That(msg.ReplyTo, Is.EqualTo("reply/topic"));
        }

        [Test]
        public void EncodeWithReplyToMaskIncludesReplyTo()
        {
            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.ReplyTo |
                JsonNetworkMessageContentMask.DataSetMessageHeader,
                ("F1", Variant.From(1)));
            msg.ReplyTo = "reply/topic";

            byte[] encoded = msg.Encode(m_messageContext);
            string json = System.Text.Encoding.UTF8.GetString(encoded);
            Assert.That(json, Does.Contain("reply/topic"));
        }

        [Test]
        public void DecodeFiltersByPublisherId()
        {
            const JsonNetworkMessageContentMask contentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            PubSubEncoding.JsonNetworkMessage msg = CreateDataSetMessage(contentMask, ("F1", Variant.From(1)));
            byte[] encoded = msg.Encode(m_messageContext);

            var reader = new DataSetReaderDataType
            {
                Enabled = true,
                Name = "Reader1",
                PublisherId = Variant.From("WrongPublisher"),
                DataSetFieldContentMask = (uint)DataSetFieldContentMask.None,
                MessageSettings = new ExtensionObject(
                    new JsonDataSetReaderMessageDataType
                    {
                        NetworkMessageContentMask = (uint)contentMask,
                        DataSetMessageContentMask =
                            (uint)JsonDataSetMessageContentMask.None
                    })
            };

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(
                m_messageContext,
                encoded,
                [reader]);
            Assert.That(decoded.DataSetMessages.Count, Is.Zero);
        }
    }
}