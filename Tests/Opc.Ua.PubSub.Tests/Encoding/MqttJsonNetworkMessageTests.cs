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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.PubSub.Transport;
using Opc.Ua.Tests;

using PubSubEncoding = Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    [TestFixture(Description = "Tests for Encoding/Decoding of JsonNetworkMessage objects")]
    [Parallelizable]
    public class MqttJsonNetworkMessageTests
    {
        private const ushort kNamespaceIndexAllTypes = 3;
        private const string kMqttAddressUrl = "mqtt://localhost:1883";
        private static List<DateTime> s_publishTimes = [];
        private ServiceMessageContext m_messageContext;
        internal const string MetaDataMessageId = "MessageId";
        internal const string MetaDataMessageType = "MessageType";
        internal const string MetaDataPublisherId = "PublisherId";
        internal const string MetaDataDataSetWriterId = "DataSetWriterId";

        private static readonly Variant[] s_validPublisherIds =
        [
            Variant.From(1),
            Variant.From("abc")
        ];

        [Flags]
        private enum MetaDataFailOptions
        {
            Ok,
            MessageId,
            MessageType,
            PublisherId,
            DataSetWriterId,
            DataSetMetaData,
            NonMetadata = MessageType | DataSetMetaData,

            MetaData_Name,
            MetaData_Fields,
            MetaData_DataSetClassId,
            MetaData_ConfigurationVersion
        }

        internal const string NetworkMessageMessageId = "MessageId";
        internal const string NetworkMessageMessageType = "MessageType";
        internal const string NetworkMessagePublisherId = "PublisherId";
        internal const string NetworkMessageDataSetClassId = "DataSetClassId";
        internal const string NetworkMessageMessages = "Messages";

        private enum NetworkMessageFailOptions
        {
            Ok,
            MessageId,
            MessageType,
            PublisherId,
            DataSetClassId,
            Messages
        }

        internal const string DataSetMessageDataSetWriterId = "DataSetWriterId";
        internal const string DataSetMessageSequenceNumber = "SequenceNumber";
        internal const string DataSetMessageMetaDataVersion = "MetaDataVersion";
        internal const string DataSetMessageTimestamp = "Timestamp";
        internal const string DataSetMessageStatus = "Status";
        internal const string DataSetMessagePayload = "Payload";

        public enum DataSetMessageFailOptions
        {
            Ok,
            DataSetWriterId,
            SequenceNumber,
            MetaDataVersion,
            Timestamp,
            Status,
            Payload
        }

        [OneTimeSetUp]
        public void MyTestInitialize()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_messageContext = ServiceMessageContext.Create(telemetry);
            // add some namespaceUris to be used at encode/decode
            m_messageContext.NamespaceUris
                .Append("http://opcfoundation.org/UA/DI/");
            m_messageContext.NamespaceUris
                .Append("http://opcfoundation.org/UA/ADI/");
            m_messageContext.NamespaceUris
                .Append("http://opcfoundation.org/UA/IA/");
        }

        [SetUp]
        public void TestSetup()
        {
            s_publishTimes.Clear();
        }

        [Test(Description = "Validate NetworkMessageHeader & PublisherId with PublisherId as parameter")]
        public void ValidateMessageHeaderAndPublisherIdWithParameters(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData,
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(
                JsonDataSetMessageContentMask.None,
                JsonDataSetMessageContentMask.DataSetWriterId,
                JsonDataSetMessageContentMask.MetaDataVersion,
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status
            )]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
            [Values(
                JsonNetworkMessageContentMask.None,
                JsonNetworkMessageContentMask.DataSetClassId,
                JsonNetworkMessageContentMask.ReplyTo,
                JsonNetworkMessageContentMask.DataSetClassId |
                JsonNetworkMessageContentMask.DataSetMessageHeader,
                JsonNetworkMessageContentMask.ReplyTo | JsonNetworkMessageContentMask.DataSetClassId
            )]
                JsonNetworkMessageContentMask jsonNetworkMessageContentMask,
            [ValueSource(nameof(s_validPublisherIds))]
                Variant publisherId)
        {
            // Arrange
            jsonNetworkMessageContentMask =
                jsonNetworkMessageContentMask |
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    kMqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: 1,
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(
                publisherConfiguration,
                m_messageContext.Telemetry);
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0].WriterGroups[0],
                Is.Not.Null,
                "publisherConfiguration first writer group of first connection should not be null");
            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                new WriterGroupPublishState());
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            List<PubSubEncoding.JsonNetworkMessage> uaDataNetworkMessages = MessagesHelper
                .GetJsonUaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaDataNetworkMessages,
                Is.Not.Null,
                "Json ua-data entries are missing from configuration!");

            // set PublisherId
            foreach (PubSubEncoding.JsonNetworkMessage uaNetworkMessage in uaDataNetworkMessages)
            {
                uaNetworkMessage.PublisherId = publisherId.ToString();
            }

            List<PubSubEncoding.JsonNetworkMessage> uaMetaDataNetworkMessages =
                MessagesHelper.GetJsonUaMetaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "Json ua-metadata entries are missing from configuration!");

            // set PublisherId
            foreach (PubSubEncoding.JsonNetworkMessage uaNetworkMessage in uaMetaDataNetworkMessages)
            {
                uaNetworkMessage.PublisherId = publisherId.ToString();
            }

            bool hasDataSetWriterId =
                (jsonNetworkMessageContentMask &
                    JsonNetworkMessageContentMask.DataSetMessageHeader) != 0 &&
                (jsonDataSetMessageContentMask &
                    JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    kMqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: 1,
                    setDataSetWriterId: hasDataSetWriterId,
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, m_messageContext.Telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");
            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");

            // Assert
            foreach (PubSubEncoding.JsonNetworkMessage uaDataNetworkMessage in uaDataNetworkMessages)
            {
                CompareEncodeDecode(uaDataNetworkMessage, dataSetReaders);
            }
            foreach (PubSubEncoding.JsonNetworkMessage uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                CompareEncodeDecode(uaMetaDataNetworkMessage, dataSetReaders);
            }
        }

        /// <summary>
        /// [Ignore("Temporary disabled due to changes in DataSetClassId handling on NetworkMessage")]
        /// </summary>
        [Test(Description = "Validate NetworkMessageHeader & DataSetClassId")]
        public void ValidateMessageHeaderAndDataSetClassIdWithParameters(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(
                JsonDataSetMessageContentMask.None,
                JsonDataSetMessageContentMask.DataSetWriterId,
                JsonDataSetMessageContentMask.MetaDataVersion,
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status
            )]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask)
        {
            /*The DataSetClassId associated with the DataSets in the NetworkMessage.
                  This value is optional. The presence of the value depends on the setting in the JsonNetworkMessageContentMask.
                  If specified, all DataSetMessages in the NetworkMessage shall have the same DataSetClassId.
                  The source is the DataSetClassId on the PublishedDataSet (see 6.2.2.2) associated with the DataSetWriters that produced the DataSetMessages.*/

            // Arrange
            const JsonNetworkMessageContentMask jsonNetworkMessageContentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetClassId |
                JsonNetworkMessageContentMask.SingleDataSetMessage; // add SingleDataSetMessage flag because of the special implementation od DataSetClassId that is written only in this case

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    kMqttAddressUrl,
                    publisherId: 1,
                    writerGroupId: 1,
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, m_messageContext.Telemetry);
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0].WriterGroups[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");
            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                new WriterGroupPublishState());

            List<PubSubEncoding.JsonNetworkMessage> uaNetworkMessages = MessagesHelper
                .GetJsonUaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaNetworkMessages,
                Is.Not.Null,
                "Json ua-data entries are missing from configuration!");

            // set DataSetClassId
            var dataSetClassId = Uuid.NewUuid();
            foreach (PubSubEncoding.JsonNetworkMessage uaNetworkMessage in uaNetworkMessages)
            {
                uaNetworkMessage.DataSetClassId = dataSetClassId.ToString();
                uaNetworkMessage.DataSetMessages[0].DataSet.DataSetMetaData.DataSetClassId
                    = (Guid)dataSetClassId;
            }

            bool hasDataSetWriterId =
                (jsonNetworkMessageContentMask &
                    JsonNetworkMessageContentMask.DataSetMessageHeader) != 0 &&
                (jsonDataSetMessageContentMask &
                    JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    kMqttAddressUrl,
                    publisherId: default,
                    writerGroupId: 1,
                    setDataSetWriterId: hasDataSetWriterId, // the writer header is saved
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, m_messageContext.Telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");
            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");

            // Assert
            // check first consistency of ua-data network messages
            List<PubSubEncoding.JsonNetworkMessage> uaDataNetworkMessages = MessagesHelper
                .GetJsonUaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaDataNetworkMessages,
                Is.Not.Null,
                "Json ua-data entries are missing from configuration!");

            int index = 0;
            Assert.That(uaDataNetworkMessages, Has.Count.EqualTo(dataSetReaders.Count));
            foreach (PubSubEncoding.JsonNetworkMessage uaDataNetworkMessage in uaDataNetworkMessages)
            {
                CompareEncodeDecode(uaDataNetworkMessage, [dataSetReaders[index++]]);
            }
        }

        [Test(Description = "Validate NetworkMessageHeader & DataSetMessageHeader without PublisherId parameter")]
        public void ValidateNetworkMessageHeaderAndDataSetMessageHeaderWithParameters(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(
                JsonDataSetMessageContentMask.None,
                JsonDataSetMessageContentMask.DataSetWriterId,
                JsonDataSetMessageContentMask.MetaDataVersion,
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status
            )]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask)
        {
            // Arrange
            const JsonNetworkMessageContentMask jsonNetworkMessageContentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    kMqttAddressUrl,
                    publisherId: 1,
                    writerGroupId: 1,
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, m_messageContext.Telemetry);
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0].WriterGroups[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");
            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                new WriterGroupPublishState());
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            List<PubSubEncoding.JsonNetworkMessage> uaNetworkMessages = MessagesHelper
                .GetJsonUaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaNetworkMessages,
                Is.Not.Null,
                "Json ua-data entries are missing from configuration!");

            List<PubSubEncoding.JsonNetworkMessage> uaMetaDataNetworkMessages =
                MessagesHelper.GetJsonUaMetaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "Json ua-metadata entries are missing from configuration!");

            bool hasDataSetWriterId =
                (jsonNetworkMessageContentMask &
                    JsonNetworkMessageContentMask.DataSetMessageHeader) != 0 &&
                (jsonDataSetMessageContentMask &
                    JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    kMqttAddressUrl,
                    publisherId: default,
                    writerGroupId: 1,
                    setDataSetWriterId: hasDataSetWriterId, // the writer header is saved
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, m_messageContext.Telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");
            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");

            // Assert
            foreach (PubSubEncoding.JsonNetworkMessage uaDataNetworkMessage in uaNetworkMessages)
            {
                CompareEncodeDecode(uaDataNetworkMessage, dataSetReaders);
            }
            foreach (PubSubEncoding.JsonNetworkMessage uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                CompareEncodeDecodeMetaData(uaMetaDataNetworkMessage);
            }
        }

        [Test(Description = "Validate NetworkMessageHeader & DataSetMessageHeader with PublisherId parameter")]
        public void ValidateNetworkAndDataSetMessageHeaderWithParameters(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(
                JsonDataSetMessageContentMask.None,
                JsonDataSetMessageContentMask.DataSetWriterId,
                JsonDataSetMessageContentMask.MetaDataVersion,
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status
            )]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
            [ValueSource(nameof(s_validPublisherIds))]
                Variant publisherId)
        {
            // Arrange
            const JsonNetworkMessageContentMask jsonNetworkMessageContentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.PublisherId;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    kMqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: 1,
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, m_messageContext.Telemetry);
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0].WriterGroups[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");
            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                new WriterGroupPublishState());
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            List<PubSubEncoding.JsonNetworkMessage> uaNetworkMessages = MessagesHelper
                .GetJsonUaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaNetworkMessages,
                Is.Not.Null,
                "Json ua-data entries are missing from configuration!");

            List<PubSubEncoding.JsonNetworkMessage> uaMetaDataNetworkMessages =
                MessagesHelper.GetJsonUaMetaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "Json ua-metadata entries are missing from configuration!");

            bool hasDataSetWriterId =
                (jsonNetworkMessageContentMask &
                    JsonNetworkMessageContentMask.DataSetMessageHeader) != 0 &&
                (jsonDataSetMessageContentMask &
                    JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    kMqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: 1,
                    setDataSetWriterId: hasDataSetWriterId, // no headers hence the values
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, m_messageContext.Telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");
            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");

            // Assert
            foreach (PubSubEncoding.JsonNetworkMessage uaDataNetworkMessage in uaNetworkMessages)
            {
                CompareEncodeDecode(uaDataNetworkMessage, dataSetReaders);
            }
            foreach (PubSubEncoding.JsonNetworkMessage uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                CompareEncodeDecodeMetaData(uaMetaDataNetworkMessage);
            }
        }

        [Test(Description = "Validate DataSetMessageHeader only with all JsonDataSetMessageContentMask combination")]
        public void ValidateDataSetMessageHeaderWithParameters(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(
                JsonDataSetMessageContentMask.None,
                JsonDataSetMessageContentMask.DataSetWriterId,
                JsonDataSetMessageContentMask.MetaDataVersion,
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status
            )]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask)
        {
            // Arrange
            const JsonNetworkMessageContentMask jsonNetworkMessageContentMask =
                JsonNetworkMessageContentMask.DataSetMessageHeader;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    kMqttAddressUrl,
                    publisherId: 1,
                    writerGroupId: 1,
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, m_messageContext.Telemetry);
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0].WriterGroups[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");
            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                new WriterGroupPublishState());
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            List<PubSubEncoding.JsonNetworkMessage> uaNetworkMessages = MessagesHelper
                .GetJsonUaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaNetworkMessages,
                Is.Not.Null,
                "Json ua-data entries are missing from configuration!");

            List<PubSubEncoding.JsonNetworkMessage> uaMetaDataNetworkMessages =
                MessagesHelper.GetJsonUaMetaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "Json ua-metadata entries are missing from configuration!");

            bool hasDataSetWriterId =
                (jsonNetworkMessageContentMask &
                    JsonNetworkMessageContentMask.DataSetMessageHeader) != 0 &&
                (jsonDataSetMessageContentMask &
                    JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    kMqttAddressUrl,
                    publisherId: default,
                    writerGroupId: 1,
                    setDataSetWriterId: hasDataSetWriterId, // the writer header is saved
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, m_messageContext.Telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");
            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");

            // Assert
            foreach (PubSubEncoding.JsonNetworkMessage uaDataNetworkMessage in uaNetworkMessages)
            {
                CompareEncodeDecode(uaDataNetworkMessage, dataSetReaders);
            }
            foreach (PubSubEncoding.JsonNetworkMessage uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                CompareEncodeDecodeMetaData(uaMetaDataNetworkMessage);
            }
        }

        [Test(
            Description = "Validate SingleDataSetMessage with parameters for DataSetFieldContentMask, JsonDataSetMessageContentMask and JsonNetworkMessageContentMask"
        )]
        public void ValidateSingleDataSetMessageWithParameters(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(
                JsonDataSetMessageContentMask.None,
                JsonDataSetMessageContentMask.DataSetWriterId,
                JsonDataSetMessageContentMask.MetaDataVersion,
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status
            )]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
            [Values(
                JsonNetworkMessageContentMask.None,
                JsonNetworkMessageContentMask.NetworkMessageHeader,
                JsonNetworkMessageContentMask.DataSetMessageHeader,
                JsonNetworkMessageContentMask.DataSetClassId,
                JsonNetworkMessageContentMask.PublisherId,
                JsonNetworkMessageContentMask.ReplyTo,
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader,
                JsonNetworkMessageContentMask.DataSetClassId |
                JsonNetworkMessageContentMask.DataSetMessageHeader,
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader,
                JsonNetworkMessageContentMask.ReplyTo |
                JsonNetworkMessageContentMask.DataSetMessageHeader,
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.DataSetClassId,
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.DataSetClassId,
                JsonNetworkMessageContentMask.ReplyTo |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.DataSetClassId,
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.DataSetClassId |
                JsonNetworkMessageContentMask.PublisherId,
                JsonNetworkMessageContentMask.ReplyTo |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.DataSetClassId |
                JsonNetworkMessageContentMask.PublisherId,
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.ReplyTo |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.DataSetClassId |
                JsonNetworkMessageContentMask.PublisherId
            )]
                JsonNetworkMessageContentMask jsonNetworkMessageContentMask)
        {
            // Arrange
            // mark SingleDataSetMessage message
            jsonNetworkMessageContentMask |= JsonNetworkMessageContentMask.SingleDataSetMessage;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    kMqttAddressUrl,
                    publisherId: 1,
                    writerGroupId: 1,
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, m_messageContext.Telemetry);
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0].WriterGroups[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");
            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                new WriterGroupPublishState());
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            bool hasDataSetWriterId =
                (jsonNetworkMessageContentMask &
                    JsonNetworkMessageContentMask.DataSetMessageHeader) != 0 &&
                (jsonDataSetMessageContentMask &
                    JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    kMqttAddressUrl,
                    publisherId: default,
                    writerGroupId: 1,
                    setDataSetWriterId: hasDataSetWriterId, // no headers hence the values
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, m_messageContext.Telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");
            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");

            // Assert
            // check first consistency of ua-data network messages
            List<PubSubEncoding.JsonNetworkMessage> uaDataNetworkMessages = MessagesHelper
                .GetJsonUaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaDataNetworkMessages,
                Is.Not.Null,
                "Json ua-data entries are missing from configuration!");
            int index = 0;
            foreach (PubSubEncoding.JsonNetworkMessage uaDataNetworkMessage in uaDataNetworkMessages)
            {
                CompareEncodeDecode(uaDataNetworkMessage, [dataSetReaders[index++]]);
            }

            List<PubSubEncoding.JsonNetworkMessage> uaMetaDataNetworkMessages =
                MessagesHelper.GetJsonUaMetaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "Json ua-metadata entries are missing from configuration!");
            foreach (PubSubEncoding.JsonNetworkMessage uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                CompareEncodeDecodeMetaData(uaMetaDataNetworkMessage);
                //(uaMetaDataNetworkMessage as PubSubEncoding.JsonNetworkMessage, new List<DataSetReaderDataType>() { dataSetReaders[index++] });
            }
        }

        [Test(Description = "Validate that metadata is encoded/decoded correctly")]
        public void ValidateMetaDataIsEncodedCorrectly()
        {
            // Arrange
            const JsonNetworkMessageContentMask jsonNetworkMessageContentMask
                = JsonNetworkMessageContentMask.None;
            const JsonDataSetMessageContentMask jsonDataSetMessageContentMask
                = JsonDataSetMessageContentMask.None;
            const DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("MetaData1"),
                MessagesHelper.CreateDataSetMetaData2("MetaData2"),
                MessagesHelper.CreateDataSetMetaData3("MetaData3"),
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaDataArrays("Arrays"),
                MessagesHelper.CreateDataSetMetaDataMatrices("Matrices")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    kMqttAddressUrl,
                    publisherId: 1,
                    writerGroupId: 1,
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes);

            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, m_messageContext.Telemetry);
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            var publishState = new WriterGroupPublishState();

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0].WriterGroups[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");
            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                publishState);
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            List<PubSubEncoding.JsonNetworkMessage> uaMetaDataNetworkMessages =
                MessagesHelper.GetJsonUaMetaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "Json ua-metadata entries are missing from configuration!");

            foreach (PubSubEncoding.JsonNetworkMessage uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                CompareEncodeDecodeMetaData(uaMetaDataNetworkMessage);
            }
        }

        [Test(Description = "Validate that metadata with update time 0 is sent at startup for a MQTT Json publisher")]
        public void ValidateMetaDataUpdateTimeZeroSentAtStartup()
        {
            // Arrange
            const JsonNetworkMessageContentMask jsonNetworkMessageContentMask
                = JsonNetworkMessageContentMask.None;
            const JsonDataSetMessageContentMask jsonDataSetMessageContentMask
                = JsonDataSetMessageContentMask.None;
            const DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("MetaData1"),
                MessagesHelper.CreateDataSetMetaData2("MetaData2"),
                MessagesHelper.CreateDataSetMetaData3("MetaData3"),
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaDataArrays("Arrays"),
                MessagesHelper.CreateDataSetMetaDataMatrices("Matrices")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    kMqttAddressUrl,
                    publisherId: 1,
                    writerGroupId: 1,
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes,
                    0);

            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, m_messageContext.Telemetry);
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            var publishState = new WriterGroupPublishState();

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0].WriterGroups[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");

            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                publishState);
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            List<PubSubEncoding.JsonNetworkMessage> uaMetaDataNetworkMessages =
                MessagesHelper.GetJsonUaMetaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "Json ua-metadata entries are missing from configuration!");

            // check if there are as many metadata messages as metadata were created in ARRAY
            Assert.That(
                uaMetaDataNetworkMessages,
                Has.Count.EqualTo(dataSetMetaDataArray.Length),
                "The ua-metadata messages count is different from the number of metadata in publisher!");
            int index = 0;
            foreach (PubSubEncoding.JsonNetworkMessage uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                // compare the initial metadata with the one from the messages
                Assert.That(
                    Utils.IsEqual(
                        dataSetMetaDataArray[index],
                        uaMetaDataNetworkMessage.DataSetMetaData),
                    Is.True,
                    "Metadata from network message is different from the original one for name " +
                    dataSetMetaDataArray[index].Name);

                index++;
            }

            // get the messages again and see if there are any metadata messages
            networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                publishState);
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            uaMetaDataNetworkMessages = MessagesHelper.GetJsonUaMetaDataNetworkMessages(
                [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "Json ua-metadata entries are missing from configuration!");

            // check if there are any metadata messages. second time around there shall be no metadata messages
            Assert.That(
                uaMetaDataNetworkMessages.Count,
                Is.Zero,
                "The ua-metadata messages count shall be zero for the second time when create messages is called!");
        }

        [Test(
            Description = "Validate that metadata with update time 0 is sent when the metadata changes for a MQTT Json publisher"
        )]
        public void ValidateMetaDataUpdateTimeZeroSentAtMetaDataChange()
        {
            // Arrange
            const JsonNetworkMessageContentMask jsonNetworkMessageContentMask
                = JsonNetworkMessageContentMask.None;
            const JsonDataSetMessageContentMask jsonDataSetMessageContentMask
                = JsonDataSetMessageContentMask.None;
            const DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("MetaData1"),
                MessagesHelper.CreateDataSetMetaData2("MetaData2"),
                MessagesHelper.CreateDataSetMetaData3("MetaData3"),
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaDataArrays("Arrays"),
                MessagesHelper.CreateDataSetMetaDataMatrices("Matrices")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    kMqttAddressUrl,
                    publisherId: 1,
                    writerGroupId: 1,
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes,
                    0);

            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, m_messageContext.Telemetry);
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            var publishState = new WriterGroupPublishState();

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0].WriterGroups[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");
            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                publishState);
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            List<PubSubEncoding.JsonNetworkMessage> uaMetaDataNetworkMessages =
                MessagesHelper.GetJsonUaMetaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "Json ua-metadata entries are missing from configuration!");

            // check if there are as many metadata messages as metadata were created in ARRAY
            Assert.That(
                uaMetaDataNetworkMessages,
                Has.Count.EqualTo(dataSetMetaDataArray.Length),
                "The ua-metadata messages count is different from the number of metadata in publisher!");
            int index = 0;
            foreach (PubSubEncoding.JsonNetworkMessage uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                // compare the initial metadata with the one from the messages
                Assert.That(
                    Utils.IsEqual(
                        dataSetMetaDataArray[index],
                        uaMetaDataNetworkMessage.DataSetMetaData),
                    Is.True,
                    "Metadata from network message is different from the original one for name " +
                    dataSetMetaDataArray[index].Name);

                index++;
            }

            // get the messages again and see if there are any metadata messages
            networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                publishState);
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            uaMetaDataNetworkMessages = MessagesHelper.GetJsonUaMetaDataNetworkMessages(
                [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "Json ua-metadata entries are missing from configuration!");

            // check if there are any metadata messages. second time around there shall be no metadata messages
            Assert.That(
                uaMetaDataNetworkMessages.Count,
                Is.Zero,
                "The ua-metadata messages count shall be zero for the second time when create messages is called!");

            // change the metadata version
            DateTime currentDateTime = DateTime.UtcNow;
            foreach (DataSetMetaDataType dataSetMetaData in dataSetMetaDataArray)
            {
                dataSetMetaData.ConfigurationVersion.MajorVersion = ConfigurationVersionUtils
                    .CalculateVersionTime(
                        currentDateTime);
                dataSetMetaData.ConfigurationVersion.MinorVersion = dataSetMetaData
                    .ConfigurationVersion
                    .MajorVersion;
            }

            // get the messages again and see if there are any metadata messages
            networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                publishState);
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "After MetaDataVersion change - connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "After MetaDataVersion change - connection.CreateNetworkMessages shall have at least one network message");

            uaMetaDataNetworkMessages = MessagesHelper.GetJsonUaMetaDataNetworkMessages(
                [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "After MetaDataVersion change - Json ua-metadata entries are missing from configuration!");

            // check if there are any metadata messages. second time around there shall be no metadata messages
            Assert.That(
                uaMetaDataNetworkMessages,
                Has.Count.EqualTo(dataSetMetaDataArray.Length),
                "After MetaDataVersion change - The ua-metadata messages count shall be equal to number of dataSetMetaData!");

            index = 0;
            foreach (PubSubEncoding.JsonNetworkMessage uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                // compare the initial metadata with the one from the messages
                Assert.That(
                    Utils.IsEqual(
                        dataSetMetaDataArray[index],
                        uaMetaDataNetworkMessage.DataSetMetaData),
                    Is.True,
                    "After MetaDataVersion change - Metadata from network message is different from the original one for name " +
                    dataSetMetaDataArray[index].Name);

                index++;
            }
        }

        [Test(
            Description = "Validate that metadata with update time different than 0 is sent periodically for a MQTT Json publisher"
        )]
        [Ignore("Max deviation instable in this version.")]
        public void ValidateMetaDataUpdateTimeNonZeroIsSentPeriodically(
            [Values(100, 1000, 2000)] double metaDataUpdateTime,
            [Values(30, 40)] double maxDeviation,
            [Values(10)] int publishTimeInSeconds)
        {
            s_publishTimes.Clear();
            // arrange
            const JsonNetworkMessageContentMask jsonNetworkMessageContentMask
                = JsonNetworkMessageContentMask.None;
            const JsonDataSetMessageContentMask jsonDataSetMessageContentMask
                = JsonDataSetMessageContentMask.None;
            const DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            var dataSetMetaDataArray = new DataSetMetaDataType[] {
                MessagesHelper.CreateDataSetMetaData1("MetaData1") };
            // create the publisher configuration
            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    kMqttAddressUrl,
                    publisherId: 1,
                    writerGroupId: 1,
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes,
                    0);

            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // create the mock IMqttPubSubConnection that will be used to monitor how often the metadata will be sent
            var mockConnection = new Mock<IMqttPubSubConnection>();

            mockConnection
                .Setup(x => x.CanPublishMetaData(
                    It.IsAny<WriterGroupDataType>(),
                    It.IsAny<DataSetWriterDataType>()))
                .Returns(true);

            mockConnection
                .Setup(x =>
                    x.CreateDataSetMetaDataNetworkMessage(
                        It.IsAny<WriterGroupDataType>(),
                        It.IsAny<DataSetWriterDataType>()))
                .Callback(() => s_publishTimes.Add(DateTime.Now));

            WriterGroupDataType writerGroupDataType = publisherConfiguration.Connections[0]
                .WriterGroups[0];

            //Act
            var mqttMetaDataPublisher = new MqttMetadataPublisher(
                mockConnection.Object,
                writerGroupDataType,
                writerGroupDataType.DataSetWriters[0],
                metaDataUpdateTime,
                m_messageContext.Telemetry);
            mqttMetaDataPublisher.Start();

            //wait so many seconds
            Thread.Sleep(publishTimeInSeconds * 1000);
            mqttMetaDataPublisher.Stop();
            int faultIndex = -1;
            double faultDeviation = 0;

            s_publishTimes = [.. from t in s_publishTimes orderby t select t];

            //Assert
            for (int i = 1; i < s_publishTimes.Count; i++)
            {
                double interval = s_publishTimes[i].Subtract(s_publishTimes[i - 1])
                    .TotalMilliseconds;
                double deviation = Math.Abs(metaDataUpdateTime - interval);
                if (deviation >= maxDeviation && deviation > faultDeviation)
                {
                    faultIndex = i;
                    faultDeviation = deviation;
                }
            }

            Assert.That(
                faultIndex,
                Is.LessThan(0),
                $"publishingInterval={metaDataUpdateTime}, maxDeviation={maxDeviation}, publishTimeInSeconds={publishTimeInSeconds}, deviation[{faultIndex}] = {faultDeviation} has maximum deviation");
        }

        [Test(Description = "Validate missing or wrong DataSetMetaData fields definition")]
        public void ValidateMissingDataSetMetaDataDefinitions(
            [Values("1", null)] string messageId,
            [Values("1", null)] string publisherId,
            [Values(1, null)] object dataSetWriterId,
            [Values] bool hasMetaData,
            [Values("Simple", null)] string metaDataName,
            [Values("Description text", null)] string metaDataDescription,
            [Values] bool hasMetaDataDataSetClassId,
            [Values] bool hasMetaDataConfigurationVersion,
            [Values] bool hasMetaDataFields)
        {
            DataSetMetaDataType metaDataType = MessagesHelper.CreateDataSetMetaData1("DataSet1");
            WriterGroupDataType writerGroup = MessagesHelper.CreateWriterGroup(1);

            DataSetMetaDataType metadata = MessagesHelper.CreateDataSetMetaData(
                dataSetName: "Test missing metadata fields definition",
                kNamespaceIndexAllTypes,
                metaDataType.Fields);
            metadata.Description = LocalizedText.From("Description text");
            metadata.DataSetClassId = Uuid.Empty;

            _ = hasMetaData ? metadata : null;

            ILogger logger = m_messageContext.Telemetry.CreateLogger<MqttJsonNetworkMessageTests>();
            var jsonNetworkMessage = new PubSubEncoding.JsonNetworkMessage(writerGroup, metadata, logger)
            {
                MessageId = messageId,
                PublisherId = publisherId,
                DataSetWriterId = MessagesHelper.ConvertToNullable<ushort>(dataSetWriterId, logger)
            };

            jsonNetworkMessage.DataSetMetaData.Name = metaDataName;
            jsonNetworkMessage.DataSetMetaData.Description = LocalizedText.From(metaDataDescription);
            jsonNetworkMessage.DataSetMetaData.DataSetClassId = hasMetaDataDataSetClassId
                ? Uuid.NewUuid()
                : Uuid.Empty;
            jsonNetworkMessage.DataSetMetaData.ConfigurationVersion
                = hasMetaDataConfigurationVersion
                ? new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 1 }
                : new ConfigurationVersionDataType();
            if (!hasMetaDataFields)
            {
                jsonNetworkMessage.DataSetMetaData.Fields = default;
            }

            MetaDataFailOptions failOptions = VerifyDataSetMetaDataEncoding(jsonNetworkMessage);
            if (failOptions != MetaDataFailOptions.Ok)
            {
                switch (failOptions)
                {
                    case MetaDataFailOptions.MessageId:
                        Assert.That(
                            failOptions,
                            Is.EqualTo(MetaDataFailOptions.MessageId),
                            "ValidateMissingDataSetMetaDataDefinitions should fail due to missing MessageId reason.");
                        break;
                    case MetaDataFailOptions.PublisherId:
                        Assert.That(
                            failOptions,
                            Is.EqualTo(MetaDataFailOptions.PublisherId),
                            "ValidateMissingDataSetMetaDataDefinitions should fail due to missing PublisherId reason.");
                        break;
                    case MetaDataFailOptions.DataSetWriterId:
                        Assert.That(
                            failOptions,
                            Is.EqualTo(MetaDataFailOptions.DataSetWriterId),
                            "ValidateMissingDataSetMetaDataDefinitions should fail due to missing DataSetWriterId reason.");
                        break;
                    case MetaDataFailOptions.NonMetadata:
                        Assert.That(
                            failOptions,
                            Is.EqualTo(MetaDataFailOptions.DataSetMetaData | MetaDataFailOptions.MessageType),
                            "ValidateMissingDataSetMetaDataDefinitions should fail due to missing DataSetMetaData reason.");
                        break;
                    case MetaDataFailOptions.MetaData_Name:
                        Assert.That(
                            failOptions,
                            Is.EqualTo(MetaDataFailOptions.MetaData_Name),
                            "ValidateMissingDataSetMetaDataDefinitions should fail due to missing MetaData.Name reason.");
                        break;
                    case MetaDataFailOptions.MetaData_DataSetClassId:
                        Assert.That(
                            failOptions,
                            Is.EqualTo(MetaDataFailOptions.MetaData_DataSetClassId),
                            "ValidateMissingDataSetMetaDataDefinitions should fail due to missing MetaData.DataSetClassId reason.");
                        break;
                    case MetaDataFailOptions.MetaData_ConfigurationVersion:
                        Assert.That(
                            failOptions,
                            Is.EqualTo(MetaDataFailOptions.MetaData_ConfigurationVersion),
                            "ValidateMissingDataSetMetaDataDefinitions should fail due to missing MetaData.ConfigurationVersion reason.");
                        break;
                    case MetaDataFailOptions.MetaData_Fields:
                        Assert.That(
                            failOptions,
                            Is.EqualTo(MetaDataFailOptions.MetaData_Fields),
                            "ValidateMissingDataSetMetaDataDefinitions should fail due to missing MetaData.Fields reason.");
                        break;
                }
            }
        }

        [Test(Description = "Validate missing or wrong NetworkMessage fields definition")]
        public void ValidateMissingNetworkMessageDefinitions(
            [Values("1", null)] string messageId,
            [Values("1", null)] string publisherId,
            [Values("1", null)] string dataSetClassId)
        {
            const JsonNetworkMessageContentMask jsonNetworkMessageContentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader;
            const JsonDataSetMessageContentMask jsonDataSetMessageContentMask =
                JsonDataSetMessageContentMask.DataSetWriterId;
            const DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType pubSubConfiguration = MessagesHelper
                .ConfigureDataSetMessages(
                    Profiles.PubSubMqttJsonTransport,
                    kMqttAddressUrl,
                    writerGroupId: 1,
                    jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask,
                    dataSetFieldContentMask,
                    dataSetMetaDataArray,
                    kNamespaceIndexAllTypes);
            Assert.That(pubSubConfiguration, Is.Not.Null, "pubSubConfiguration should not be null");

            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(pubSubConfiguration, m_messageContext.Telemetry);
            Assert.That(publisherApplication, Is.Not.Null, "publisherApplication should not be null");
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                pubSubConfiguration.Connections[0].WriterGroups[0],
                new WriterGroupPublishState());
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");

            // Assert
            // check first consistency of ua-data network messages
            List<PubSubEncoding.JsonNetworkMessage> uaDataNetworkMessages = MessagesHelper
                .GetJsonUaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaDataNetworkMessages,
                Is.Not.Null,
                "Json ua-data entries are missing from configuration!");

            foreach (PubSubEncoding.JsonNetworkMessage jsonNetworkMessage in uaDataNetworkMessages)
            {
                jsonNetworkMessage.MessageId = messageId;
                jsonNetworkMessage.PublisherId = publisherId;
                jsonNetworkMessage.DataSetClassId = dataSetClassId;

                var failOptions = (NetworkMessageFailOptions)VerifyDataEncoding(jsonNetworkMessage);
                if (failOptions != NetworkMessageFailOptions.Ok)
                {
                    switch (failOptions)
                    {
                        case NetworkMessageFailOptions.MessageId:
                            Assert.That(
                                failOptions,
                                Is.EqualTo(NetworkMessageFailOptions.MessageId),
                                "ValidateMissingNetworkMessageFields should fail due to missing MessageId reason.");
                            break;
                        case NetworkMessageFailOptions.MessageType:
                            Assert.That(
                                failOptions,
                                Is.EqualTo(NetworkMessageFailOptions.MessageType),
                                "ValidateMissingNetworkMessageFields should fail due to missing MessageType reason.");
                            break;
                        case NetworkMessageFailOptions.PublisherId:
                            Assert.That(
                                failOptions,
                                Is.EqualTo(NetworkMessageFailOptions.PublisherId),
                                "ValidateMissingNetworkMessageFields should fail due to missing PublisherId reason.");
                            break;
                        case NetworkMessageFailOptions.DataSetClassId:
                            Assert.That(
                                failOptions,
                                Is.EqualTo(NetworkMessageFailOptions.DataSetClassId),
                                "ValidateMissingNetworkMessageFields should fail due to missing DataSetClassId reason.");
                            break;
                    }
                }
            }
        }

        [Test(Description = "Validate missing or wrong DataSetMessage fields definition")]
        public void ValidateMissingDataSetMessagesDefinitions(
            [Values(
                JsonNetworkMessageContentMask.DataSetMessageHeader,
                JsonNetworkMessageContentMask.SingleDataSetMessage
            )]
                JsonNetworkMessageContentMask jsonNetworkMessageContentMask,
            [Values(
                JsonDataSetMessageContentMask.DataSetWriterId,
                JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.MetaDataVersion,
                JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.Status
            )]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData,
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType pubSubConfiguration = MessagesHelper
                .ConfigureDataSetMessages(
                    Profiles.PubSubMqttJsonTransport,
                    kMqttAddressUrl,
                    writerGroupId: 1,
                    jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask,
                    dataSetFieldContentMask,
                    dataSetMetaDataArray,
                    kNamespaceIndexAllTypes);
            Assert.That(pubSubConfiguration, Is.Not.Null, "pubSubConfiguration should not be null");

            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(pubSubConfiguration, m_messageContext.Telemetry);
            Assert.That(publisherApplication, Is.Not.Null, "publisherApplication should not be null");
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                pubSubConfiguration.Connections[0].WriterGroups[0],
                new WriterGroupPublishState());
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");

            // Assert
            // check first consistency of ua-data network messages
            List<PubSubEncoding.JsonNetworkMessage> uaDataNetworkMessages = MessagesHelper
                .GetJsonUaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaDataNetworkMessages,
                Is.Not.Null,
                "Json ua-data entries are missing from configuration!");

            foreach (PubSubEncoding.JsonNetworkMessage jsonNetworkMessage in uaDataNetworkMessages)
            {
                jsonNetworkMessage.MessageId = "1";
                jsonNetworkMessage.PublisherId = "1";
                jsonNetworkMessage.DataSetClassId = "1";

                foreach (
                    PubSubEncoding.JsonDataSetMessage jsonDataSetMessage in jsonNetworkMessage
                        .DataSetMessages
                        .OfType<PubSubEncoding.JsonDataSetMessage>())
                {
                    switch (jsonDataSetMessageContentMask)
                    {
                        case JsonDataSetMessageContentMask.DataSetWriterId:
                            jsonDataSetMessage.DataSetWriterId = 0xFF;
                            break;
                        case JsonDataSetMessageContentMask.SequenceNumber:
                            jsonDataSetMessage.SequenceNumber = 0xFFFF;
                            break;
                        case JsonDataSetMessageContentMask.MetaDataVersion:
                            jsonDataSetMessage.MetaDataVersion = new ConfigurationVersionDataType
                            {
                                MajorVersion = 0,
                                MinorVersion = 0
                            };
                            break;
                        case JsonDataSetMessageContentMask.Timestamp:
                            jsonDataSetMessage.Timestamp = DateTime.MinValue;
                            break;
                        case JsonDataSetMessageContentMask.Status:
                            jsonDataSetMessage.Status = StatusCodes.Good;
                            break;
                    }
                }

                object failOptions = VerifyDataEncoding(jsonNetworkMessage);
                if (failOptions is DataSetMessageFailOptions dmfo &&
                    dmfo != DataSetMessageFailOptions.Ok)
                {
                    Assert.That(
                        failOptions,
                        Is.EqualTo(DataSetMessageFailOptions.DataSetWriterId),
                        "ValidateMissingDataSetMessagesFields should fail due to missing DataSetWriterId reason.");
                }
            }
        }

        /// <summary>
        /// Compare encoded/decoded network messages
        /// </summary>
        /// <param name="jsonNetworkMessage">the message to encode</param>
        private void CompareEncodeDecodeMetaData(
            PubSubEncoding.JsonNetworkMessage jsonNetworkMessage)
        {
            Assert.That(
                jsonNetworkMessage.IsMetaDataMessage,
                Is.True,
                "The received message is not a metadata message");

            byte[] bytes = jsonNetworkMessage.Encode(m_messageContext);

            PrettifyAndValidateJson(bytes);

            ILogger logger = m_messageContext.Telemetry.CreateLogger<MqttJsonNetworkMessageTests>();
            var uaNetworkMessageDecoded = new PubSubEncoding.JsonNetworkMessage(logger);
            uaNetworkMessageDecoded.Decode(m_messageContext, bytes, null);

            Assert.That(
                uaNetworkMessageDecoded.IsMetaDataMessage,
                Is.True,
                "The Decode message is not a metadata message");

            Assert.That(
                uaNetworkMessageDecoded.WriterGroupId,
                Is.EqualTo(jsonNetworkMessage.WriterGroupId),
                "The Decoded WriterId does not match encoded value");

            Assert.That(
                Utils.IsEqual(
                    jsonNetworkMessage.DataSetMetaData,
                    uaNetworkMessageDecoded.DataSetMetaData),
                Is.True,
                jsonNetworkMessage.DataSetMetaData.Name + " Decoded metadata is not equal ");

            // validate network message metadata
            ValidateMetaDataEncoding(jsonNetworkMessage);
        }

        /// <summary>
        /// Compare encoded/decoded network messages
        /// </summary>
        /// <param name="jsonNetworkMessage">the message to encode</param>
        /// <param name="dataSetReaders">The list of readers used to decode</param>
        private void CompareEncodeDecode(
            PubSubEncoding.JsonNetworkMessage jsonNetworkMessage,
            IList<DataSetReaderDataType> dataSetReaders)
        {
            byte[] bytes = jsonNetworkMessage.Encode(m_messageContext);

            PrettifyAndValidateJson(bytes);

            ILogger logger = m_messageContext.Telemetry.CreateLogger<MqttJsonNetworkMessageTests>();
            var uaNetworkMessageDecoded = new PubSubEncoding.JsonNetworkMessage(logger);
            uaNetworkMessageDecoded.Decode(
                m_messageContext,
                bytes,
                dataSetReaders);

            // compare uaNetworkMessage with uaNetworkMessageDecoded
            CompareData(jsonNetworkMessage, uaNetworkMessageDecoded);

            // validate network message data
            ValidateDataEncoding(jsonNetworkMessage);
        }

        /// <summary>
        /// Compare network messages options
        /// </summary>
        private void CompareData(
            PubSubEncoding.JsonNetworkMessage jsonNetworkMessageEncode,
            PubSubEncoding.JsonNetworkMessage jsonNetworkMessageDecoded)
        {
            JsonNetworkMessageContentMask networkMessageContentMask =
                jsonNetworkMessageEncode.NetworkMessageContentMask;

            // Verify flags
            if (!jsonNetworkMessageEncode.IsMetaDataMessage)
            {
                Assert.That(
                    jsonNetworkMessageDecoded.NetworkMessageContentMask,
                    Is.EqualTo(jsonNetworkMessageEncode.NetworkMessageContentMask &
                        jsonNetworkMessageDecoded.NetworkMessageContentMask),
                    "NetworkMessageContentMask were not decoded correctly");
            }

            if ((networkMessageContentMask &
                JsonNetworkMessageContentMask.NetworkMessageHeader) != 0)
            {
                if ((networkMessageContentMask & JsonNetworkMessageContentMask.PublisherId) != 0)
                {
                    Assert.That(
                        jsonNetworkMessageDecoded.PublisherId,
                        Is.EqualTo(jsonNetworkMessageEncode.PublisherId),
                        "PublisherId was not decoded correctly");
                }

                if ((networkMessageContentMask & JsonNetworkMessageContentMask.DataSetClassId) != 0)
                {
                    Assert.That(
                        jsonNetworkMessageDecoded.DataSetClassId,
                        Is.EqualTo(jsonNetworkMessageEncode.DataSetClassId),
                        "DataSetClassId was not decoded correctly");
                }
            }

            var receivedDataSetMessages = jsonNetworkMessageDecoded.DataSetMessages.ToList();

            Assert.That(receivedDataSetMessages, Is.Not.Null, "Received DataSetMessages is null");

            // check the number of JsonDataSetMessage counts
            if ((networkMessageContentMask &
                JsonNetworkMessageContentMask.SingleDataSetMessage) == 0)
            {
                Assert.That(
                    receivedDataSetMessages,
                    Has.Count.EqualTo(jsonNetworkMessageEncode.DataSetMessages.Count),
                    $"JsonDataSetMessages.Count was not decoded correctly (Count = {receivedDataSetMessages.Count})");
            }
            else
            {
                Assert.That(
                    receivedDataSetMessages,
                    Has.Count.EqualTo(1),
                    $"JsonDataSetMessages.Count was not decoded correctly. There is no SingleDataSetMessage (Coount = {receivedDataSetMessages.Count})");
            }

            // check if the encoded match the received decoded DataSets
            for (int i = 0; i < receivedDataSetMessages.Count; i++)
            {
                var jsonDataSetMessage =
                    jsonNetworkMessageEncode.DataSetMessages[
                        i] as PubSubEncoding.JsonDataSetMessage;
                Assert.That(
                    jsonDataSetMessage,
                    Is.Not.Null,
                    $"DataSet [{i}] is missing from publisher datasets!");
                // check payload data fields count
                // get related dataset from subscriber DataSets
                DataSet decodedDataSet = receivedDataSetMessages[i].DataSet;
                Assert.That(
                    decodedDataSet,
                    Is.Not.Null,
                    $"DataSet '{jsonDataSetMessage.DataSet.Name}' is missing from subscriber datasets!");

                Assert.That(
                    decodedDataSet.Fields,
                    Has.Length.EqualTo(jsonDataSetMessage.DataSet.Fields.Length),
                    $"DataSet.Fields.Length was not decoded correctly, DataSetWriterId = {jsonDataSetMessage.DataSetWriterId}");

                // check the fields data consistency
                // at this time the DataSetField has just value!?
                for (int index = 0; index < jsonDataSetMessage.DataSet.Fields.Length; index++)
                {
                    Field fieldEncoded = jsonDataSetMessage.DataSet.Fields[index];
                    Field fieldDecoded = decodedDataSet.Fields[index];
                    Assert.That(
                        fieldEncoded,
                        Is.Not.Null,
                        $"jsonDataSetMessage.DataSet.Fields[{index}] is null,  DataSetWriterId = {jsonDataSetMessage.DataSetWriterId}");
                    Assert.That(
                        fieldDecoded,
                        Is.Not.Null,
                        $"jsonDataSetMessageDecoded.DataSet.Fields[{index}] is null,  DataSetWriterId = {jsonDataSetMessage.DataSetWriterId}");

                    DataValue dataValueEncoded = fieldEncoded.Value;
                    DataValue dataValueDecoded = fieldDecoded.Value;
                    Assert.That(
                        fieldEncoded.Value,
                        Is.Not.Null,
                        $"jsonDataSetMessage.DataSet.Fields[{index}].Value is null,  DataSetWriterId = {jsonDataSetMessage.DataSetWriterId}");
                    Assert.That(
                        fieldDecoded.Value,
                        Is.Not.Null,
                        $"jsonDataSetMessageDecoded.DataSet.Fields[{index}].Value is null,  DataSetWriterId = {jsonDataSetMessage.DataSetWriterId}");

                    // check dataValues values
                    string fieldName = fieldEncoded.FieldMetaData.Name;

#pragma warning disable CS0618 // Type or member is obsolete
                    ExpandedNodeId encodedExpandedNodeId =
                        dataValueEncoded.Value is ExpandedNodeId ee ? ee : default;
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
                    ExpandedNodeId decodedExpandedNodeId =
                        dataValueDecoded.Value is ExpandedNodeId de ? de : default;
#pragma warning restore CS0618 // Type or member is obsolete
                    if (!encodedExpandedNodeId.IsNull &&
                        !encodedExpandedNodeId.IsAbsolute &&
                        !decodedExpandedNodeId.IsNull &&
                        decodedExpandedNodeId.IsAbsolute)
                    {
#pragma warning disable CS0618 // Type or member is obsolete
                        dataValueDecoded.Value = ExpandedNodeId.ToNodeId(
                            decodedExpandedNodeId,
                            m_messageContext.NamespaceUris);
#pragma warning restore CS0618 // Type or member is obsolete
                    }

#pragma warning disable CS0618 // Type or member is obsolete
                    Assert.That(
                        dataValueDecoded.Value,
                        Is.EqualTo(dataValueEncoded.Value),
                        $"Wrong: Fields[{fieldName}].DataValue.Value; DataSetWriterId = {jsonDataSetMessage.DataSetWriterId}");
#pragma warning restore CS0618 // Type or member is obsolete

                    // Checks just for DataValue type only
                    if ((jsonDataSetMessage.FieldContentMask &
                        DataSetFieldContentMask.StatusCode) ==
                        DataSetFieldContentMask.StatusCode)
                    {
                        // check dataValues StatusCode
                        Assert.That(
                            dataValueDecoded.StatusCode,
                            Is.EqualTo(dataValueEncoded.StatusCode),
                            $"Wrong: Fields[{fieldName}].DataValue.StatusCode; DataSetWriterId = {jsonDataSetMessage.DataSetWriterId}");
                    }

                    // check dataValues SourceTimestamp
                    if ((jsonDataSetMessage.FieldContentMask &
                        DataSetFieldContentMask.SourceTimestamp) ==
                        DataSetFieldContentMask.SourceTimestamp)
                    {
                        Assert.That(
                            dataValueDecoded.SourceTimestamp,
                            Is.EqualTo(dataValueEncoded.SourceTimestamp),
                            $"Wrong: Fields[{fieldName}].DataValue.SourceTimestamp; DataSetWriterId = {jsonDataSetMessage.DataSetWriterId}");
                    }

                    // check dataValues ServerTimestamp
                    if ((jsonDataSetMessage.FieldContentMask &
                        DataSetFieldContentMask.ServerTimestamp) ==
                        DataSetFieldContentMask.ServerTimestamp)
                    {
                        // check dataValues ServerTimestamp
                        Assert.That(
                            dataValueDecoded.ServerTimestamp,
                            Is.EqualTo(dataValueEncoded.ServerTimestamp),
                            $"Wrong: Fields[{fieldName}].DataValue.ServerTimestamp; DataSetWriterId = {jsonDataSetMessage.DataSetWriterId}");
                    }

                    // check dataValues SourcePicoseconds
                    if ((jsonDataSetMessage.FieldContentMask &
                        DataSetFieldContentMask.SourcePicoSeconds) ==
                        DataSetFieldContentMask.SourcePicoSeconds)
                    {
                        Assert.That(
                            dataValueDecoded.SourcePicoseconds,
                            Is.EqualTo(dataValueEncoded.SourcePicoseconds),
                            $"Wrong: Fields[{fieldName}].DataValue.SourcePicoseconds; DataSetWriterId = {jsonDataSetMessage.DataSetWriterId}");
                    }

                    // check dataValues ServerPicoSeconds
                    if ((jsonDataSetMessage.FieldContentMask &
                        DataSetFieldContentMask.ServerPicoSeconds) ==
                        DataSetFieldContentMask.ServerPicoSeconds)
                    {
                        // check dataValues ServerPicoseconds
                        Assert.That(
                            dataValueDecoded.ServerPicoseconds,
                            Is.EqualTo(dataValueEncoded.ServerPicoseconds),
                            $"Wrong: Fields[{fieldName}].DataValue.ServerPicoseconds; DataSetWriterId = {jsonDataSetMessage.DataSetWriterId}");
                    }
                }

                if ((networkMessageContentMask &
                    JsonNetworkMessageContentMask.SingleDataSetMessage) != 0)
                {
                    // stop evaluation if there only one dataset
                    break;
                }
            }
        }

        /// <summary>
        /// Validate MetaData(DataSetMetaData) encoding consistency
        /// </summary>
        private void ValidateMetaDataEncoding(
            PubSubEncoding.JsonNetworkMessage jsonNetworkMessage)
        {
            MetaDataFailOptions failOptions = VerifyDataSetMetaDataEncoding(jsonNetworkMessage);
            if (failOptions != MetaDataFailOptions.Ok)
            {
                Assert.Fail(
                    $"The mandatory 'jsonNetworkMessage.{failOptions}' field is wrong or missing from decoded message.");
            }
        }

        /// <summary>
        /// Verify DataSetMetaData encoding consistency
        /// </summary>
        private MetaDataFailOptions VerifyDataSetMetaDataEncoding(
            PubSubEncoding.JsonNetworkMessage jsonNetworkMessage)
        {
            if (jsonNetworkMessage.DataSetMetaData == null ||
                jsonNetworkMessage.MessageType != MessagesHelper.UaMetaDataMessageType)
            {
                return MetaDataFailOptions.DataSetMetaData | MetaDataFailOptions.MessageType;
            }

            // encode network message
            byte[] networkMessage = jsonNetworkMessage.Encode(m_messageContext);

            // verify DataSetMetaData encoded consistency
            ServiceMessageContext context = m_messageContext;

            string messageIdValue = null;
            string messageTypeValue = null;
            string publisherIdValue = null;
            ushort dataSetWriterIdValue = 0;

            string jsonMessage = System.Text.Encoding.ASCII.GetString(networkMessage);
            using var jsonDecoder = new PubSubJsonDecoder(jsonMessage, context);
            if (jsonDecoder.ReadField(MetaDataMessageId, out object token))
            {
                messageIdValue = jsonDecoder.ReadString(MetaDataMessageId);
            }
            else
            {
                return MetaDataFailOptions.MessageId;
            }
            Assert.That(
                messageIdValue,
                Is.EqualTo(jsonNetworkMessage.MessageId),
                $"MessageId was not decoded correctly. Encoded: {jsonNetworkMessage.MessageId} Decoded: {messageIdValue}");

            if (jsonDecoder.ReadField(MetaDataMessageType, out token))
            {
                messageTypeValue = jsonDecoder.ReadString(MetaDataMessageType);
            }
            else
            {
                return MetaDataFailOptions.MessageType;
            }
            Assert.That(
                messageTypeValue,
                Is.EqualTo(jsonNetworkMessage.MessageType),
                $"MessageType was not decoded correctly, Encoded: {jsonNetworkMessage.MessageType} Decoded: {messageTypeValue}");

            if (jsonDecoder.ReadField(MetaDataPublisherId, out token))
            {
                publisherIdValue = jsonDecoder.ReadString(MetaDataPublisherId);
            }
            else
            {
                return MetaDataFailOptions.PublisherId;
            }
            Assert.That(
                publisherIdValue,
                Is.EqualTo(jsonNetworkMessage.PublisherId),
                $"PublisherId was not decoded correctly, Encoded: {jsonNetworkMessage.PublisherId} Decoded: {publisherIdValue}");

            if (jsonDecoder.ReadField(MetaDataDataSetWriterId, out token))
            {
                dataSetWriterIdValue = jsonDecoder.ReadUInt16(MetaDataDataSetWriterId);
            }
            else
            {
                return MetaDataFailOptions.DataSetWriterId;
            }
            Assert.That(
                dataSetWriterIdValue,
                Is.EqualTo(jsonNetworkMessage.DataSetWriterId),
                $"DataSetWriterId was not decoded correctly, Encoded: {jsonNetworkMessage.DataSetWriterId} Decoded: {dataSetWriterIdValue}");

            DataSetMetaDataType jsonDataSetMetaData = jsonNetworkMessage.DataSetMetaData;

            var dataSetMetaData =
                jsonDecoder.ReadEncodeable(
                    "MetaData",
                    typeof(DataSetMetaDataType)) as DataSetMetaDataType;
            Assert.That(
                dataSetMetaData,
                Is.Not.Null,
                "DataSetMetaData read by json decoder should not be null.");

            if (jsonDataSetMetaData.Name == null)
            {
                return MetaDataFailOptions.MetaData_Name;
            }
            Assert.That(
                dataSetMetaData.Name,
                Is.EqualTo(jsonNetworkMessage.DataSetMetaData.Name),
                $"DataSetMetaData.Name was not decoded correctly, Encoded: {jsonNetworkMessage.DataSetMetaData.Name} Decoded: {dataSetMetaData.Name}");

            Assert.That(
                dataSetMetaData.Description,
                Is.EqualTo(jsonNetworkMessage.DataSetMetaData.Description),
                $"DataSetMetaData.Description was not decoded correctly, Encoded: {jsonNetworkMessage.DataSetMetaData.Description} Decoded: {dataSetMetaData.Description}");

            // jsonDataSetMetaData.Fields.Count should be > 0
            if (jsonDataSetMetaData.Fields.Count == 0)
            {
                return MetaDataFailOptions.MetaData_Fields;
            }
            Assert.That(
                dataSetMetaData.Fields.Count,
                Is.EqualTo(jsonNetworkMessage.DataSetMetaData.Fields.Count),
                $"DataSetMetaData.Fields.Count are not equal, Encoded: {jsonNetworkMessage.DataSetMetaData.Fields.Count} Decoded: {dataSetMetaData.Fields.Count}");

            foreach (FieldMetaData jsonFieldMetaData in jsonNetworkMessage.DataSetMetaData.Fields)
            {
                FieldMetaData fieldMetaData = dataSetMetaData.Fields.Find(field =>
                    field.Name == jsonFieldMetaData.Name);

                Assert.That(
                    fieldMetaData,
                    Is.Not.Null,
                    $"DataSetMetaData.Field - Name: '{jsonFieldMetaData.Name}' read by json decoder not found into decoded DataSetMetaData.Fields collection.");
                Assert.That(
                    Utils.IsEqual(jsonFieldMetaData, fieldMetaData),
                    Is.True,
                    $"FieldMetaData found in decoded collection is not identical with original one. Encoded: {Utils.Format(
                        "Name: {0}, Description: {1}, DataSetFieldId: {2}, BuiltInType: {3}, DataType: {4}, TypeId: {5}",
                        jsonFieldMetaData.Name,
                        jsonFieldMetaData.Description,
                        jsonFieldMetaData.DataSetFieldId,
                        jsonFieldMetaData.BuiltInType,
                        jsonFieldMetaData.DataType,
                        jsonFieldMetaData.TypeId
                    )} Decoded: {Utils.Format(
                        "Name: {0}, Description: {1}, DataSetFieldId: {2}, BuiltInType: {3}, DataType: {4}, TypeId: {5}",
                        fieldMetaData.Name,
                        fieldMetaData.Description,
                        fieldMetaData.DataSetFieldId,
                        fieldMetaData.BuiltInType,
                        fieldMetaData.DataType,
                        fieldMetaData.TypeId)}");
            }

            if (jsonDataSetMetaData.DataSetClassId == Uuid.Empty)
            {
                return MetaDataFailOptions.MetaData_DataSetClassId;
            }
            Assert.That(
                dataSetMetaData.DataSetClassId,
                Is.EqualTo(jsonNetworkMessage.DataSetMetaData.DataSetClassId),
                $"DataSetMetaData.DataSetClassId was not decoded correctly, Encoded: {jsonNetworkMessage.DataSetMetaData.DataSetClassId} Decoded: {dataSetMetaData.DataSetClassId}");

            if (jsonDataSetMetaData.ConfigurationVersion.MajorVersion == 0 &&
                jsonDataSetMetaData.ConfigurationVersion.MinorVersion == 0)
            {
                return MetaDataFailOptions.MetaData_ConfigurationVersion;
            }
            Assert.That(
                Utils.IsEqual(
                    jsonNetworkMessage.DataSetMetaData.ConfigurationVersion,
                    dataSetMetaData.ConfigurationVersion
                ),
                Is.True,
                $"DataSetMetaData.ConfigurationVersion was not decoded correctly, Encoded: {Utils.Format(
                    "MajorVersion: {0}, MinorVersion: {1}",
                    jsonNetworkMessage.DataSetMetaData.ConfigurationVersion.MajorVersion,
                    jsonNetworkMessage.DataSetMetaData.ConfigurationVersion.MinorVersion
                )} Decoded: {Utils.Format(
                    "MajorVersion: {0}, MinorVersion: {1}",
                    dataSetMetaData.ConfigurationVersion.MajorVersion,
                    dataSetMetaData.ConfigurationVersion.MinorVersion)}");

            return MetaDataFailOptions.Ok;
        }

        /// <summary>
        /// Verify NetworkMessage encoding consistency
        /// </summary>
        private void ValidateDataEncoding(
            PubSubEncoding.JsonNetworkMessage jsonNetworkMessage)
        {
            object failOptions = VerifyDataEncoding(jsonNetworkMessage);
            switch (failOptions)
            {
                case NetworkMessageFailOptions nmfo when nmfo != NetworkMessageFailOptions.Ok:
                    Assert.Fail(
                        $"The mandatory 'jsonNetworkMessage.{failOptions}' field is wrong or missing from decoded message.");
                    break;
                case DataSetMessageFailOptions dmfo when dmfo != DataSetMessageFailOptions.Ok:
                    Assert.Fail(
                        $"The mandatory 'jsonDataSetMessage.{failOptions}' field is wrong or missing from decoded message.");
                    break;
            }
        }

        /// <summary>
        /// Verify NetworkMessage data encoding consistency
        /// </summary>
        private object VerifyDataEncoding(
            PubSubEncoding.JsonNetworkMessage jsonNetworkMessage)
        {
            // encode network message
            byte[] networkMessage = jsonNetworkMessage.Encode(m_messageContext);

            // verify network message encoded consistency
            ServiceMessageContext context = m_messageContext;

            string jsonMessage = System.Text.Encoding.ASCII.GetString(networkMessage);
            using var jsonDecoder = new PubSubJsonDecoder(jsonMessage, context);
            if (jsonNetworkMessage.HasNetworkMessageHeader)
            {
                NetworkMessageFailOptions failOptions = VerifyNetworkMessageEncoding(
                    jsonNetworkMessage,
                    jsonDecoder);
                if (failOptions != NetworkMessageFailOptions.Ok)
                {
                    return failOptions;
                }
            }

            if (jsonNetworkMessage.HasDataSetMessageHeader ||
                jsonNetworkMessage.HasSingleDataSetMessage)
            {
                DataSetMessageFailOptions failOptions = VerifyDataSetMessagesEncoding(
                    jsonNetworkMessage,
                    jsonDecoder);
                if (failOptions != DataSetMessageFailOptions.Ok)
                {
                    return failOptions;
                }
            }

            return NetworkMessageFailOptions.Ok;
        }

        /// <summary>
        /// Verify NetworkMessage encoding
        /// </summary>
        private static NetworkMessageFailOptions VerifyNetworkMessageEncoding(
            PubSubEncoding.JsonNetworkMessage jsonNetworkMessage,
            PubSubJsonDecoder jsonDecoder)
        {
            string publisherIdValue = null;

            string messageIdValue;

            if (jsonDecoder.ReadField(NetworkMessageMessageId, out _))
            {
                messageIdValue = jsonDecoder.ReadString(NetworkMessageMessageId);
            }
            else
            {
                return NetworkMessageFailOptions.MessageId;
            }
            Assert.That(
                messageIdValue,
                Is.EqualTo(jsonNetworkMessage.MessageId),
                $"MessageId was not decoded correctly. Encoded: {jsonNetworkMessage.MessageId} Decoded: {messageIdValue}");

            string messageTypeValue;
            if (jsonDecoder.ReadField(NetworkMessageMessageType, out _))
            {
                messageTypeValue = jsonDecoder.ReadString(NetworkMessageMessageType);
            }
            else
            {
                return NetworkMessageFailOptions.MessageType;
            }
            Assert.That(
                messageTypeValue,
                Is.EqualTo(jsonNetworkMessage.MessageType),
                $"MessageType was not decoded correctly, Encoded: {jsonNetworkMessage.MessageType} Decoded: {messageTypeValue}");

            if (jsonDecoder.ReadField(NetworkMessagePublisherId, out _))
            {
                publisherIdValue = jsonDecoder.ReadString(NetworkMessagePublisherId);
                Assert.That(
                    publisherIdValue,
                    Is.EqualTo(jsonNetworkMessage.PublisherId),
                    $"PublisherId was not decoded correctly, Encoded: {jsonNetworkMessage.PublisherId} Decoded: {publisherIdValue}");
            }

            if (jsonDecoder.ReadField(NetworkMessageDataSetClassId, out _))
            {
                string dataSetClassIdValue = jsonDecoder.ReadString(NetworkMessageDataSetClassId);
                Assert.That(
                    dataSetClassIdValue,
                    Is.EqualTo(jsonNetworkMessage.DataSetClassId),
                    $"DataSetClassId was not decoded correctly, Encoded: {jsonNetworkMessage.PublisherId} Decoded: {publisherIdValue}");
            }

            return NetworkMessageFailOptions.Ok;
        }

        /// <summary>
        /// Verify DataSetMessage(s) encoding
        /// </summary>
        private static DataSetMessageFailOptions VerifyDataSetMessagesEncoding(
            PubSubEncoding.JsonNetworkMessage jsonNetworkMessage,
            PubSubJsonDecoder jsonDecoder)
        {
            ushort dataSetWriterIdValue = 0;
            uint sequenceNumberValue = 0;
            StatusCode statusValue = StatusCodes.Good;
            FieldTypeEncodingMask fieldTypeEncoding = FieldTypeEncodingMask.Reserved;
            Dictionary<string, object> dataSetPayload = null;

            object token = null;
            //object token1 = null;

            List<object> messagesList = null;
            string messagesListName = string.Empty;
            if (jsonDecoder.ReadField(NetworkMessageMessages, out object messagesToken))
            {
                messagesList = messagesToken as List<object>;
                if (messagesList == null)
                {
                    // this is a SingleDataSetMessage encoded as the content of Messages
                    jsonDecoder.PushStructure(NetworkMessageMessages);
                }
                else
                {
                    messagesListName = NetworkMessageMessages;
                }
            }
            else if (jsonDecoder.ReadField(PubSubJsonDecoder.RootArrayName, out messagesToken))
            {
                messagesListName = PubSubJsonDecoder.RootArrayName;
            }
            // else this is a SingleDataSetMessage encoded as the content json
            if (!string.IsNullOrEmpty(messagesListName))
            {
                int index = 0;
                foreach (UaDataSetMessage uaDataSetMessage in jsonNetworkMessage.DataSetMessages)
                {
                    var jsonDataSetMessage = (PubSubEncoding.JsonDataSetMessage)uaDataSetMessage;
                    if (jsonDataSetMessage.FieldContentMask == DataSetFieldContentMask.None)
                    {
                        fieldTypeEncoding = FieldTypeEncodingMask.Variant;
                    }
                    else if ((jsonDataSetMessage.FieldContentMask &
                        DataSetFieldContentMask.RawData) != 0)
                    {
                        // If the RawData flag is set, all other bits are ignored.
                        // 01 RawData Field Encoding
                        fieldTypeEncoding = FieldTypeEncodingMask.RawData;
                    }
                    else if ((
                            jsonDataSetMessage.FieldContentMask &
                            (
                                DataSetFieldContentMask.StatusCode |
                                DataSetFieldContentMask.SourceTimestamp |
                                DataSetFieldContentMask.ServerTimestamp |
                                DataSetFieldContentMask.SourcePicoSeconds |
                                DataSetFieldContentMask.ServerPicoSeconds)
                        ) != 0)
                    {
                        // 10 DataValue Field Encoding
                        fieldTypeEncoding = FieldTypeEncodingMask.DataValue;
                    }

                    bool wasPushed = jsonDecoder.PushArray(PubSubJsonDecoder.RootArrayName, index++);
                    if (wasPushed)
                    {
                        if (jsonDecoder.ReadField(DataSetMessageDataSetWriterId, out token))
                        {
                            dataSetWriterIdValue = jsonDecoder.ReadUInt16(
                                DataSetMessageDataSetWriterId);
                            Assert.That(
                                dataSetWriterIdValue,
                                Is.EqualTo(jsonDataSetMessage.DataSetWriterId),
                                $"jsonDataSetMessage.DataSetWriterId was not decoded correctly, Encoded: {jsonDataSetMessage.DataSetWriterId} Decoded: {dataSetWriterIdValue}");
                            if (dataSetWriterIdValue == 0xFF)
                            {
                                return DataSetMessageFailOptions.DataSetWriterId;
                            }
                        }
                        else if ((
                                jsonDataSetMessage.DataSetMessageContentMask &
                                JsonDataSetMessageContentMask.DataSetWriterId
                            ) != 0)
                        {
                            return DataSetMessageFailOptions.DataSetWriterId;
                        }

                        if (jsonDecoder.ReadField(DataSetMessagePayload, out token))
                        {
                            dataSetPayload = token as Dictionary<string, object>;

                            bool wasPushed1 = jsonDecoder.PushStructure(DataSetMessagePayload);
                            if (wasPushed1)
                            {
                                object decodedFieldValue = null;
                                foreach (Field field in jsonDataSetMessage.DataSet.Fields)
                                {
                                    Assert.That(
                                        dataSetPayload?.Keys
                                            .Any(key => key == field.FieldMetaData.Name),
                                        Is.True,
                                        $"Decoded Field: {field.FieldMetaData.Name} not found");
                                    Assert.That(
                                        dataSetPayload[field.FieldMetaData.Name],
                                        Is.Not.Null,
                                        $"Decoded Field: {field.FieldMetaData.Name} is not null");

                                    if (jsonDecoder.ReadField(field.FieldMetaData.Name, out token))
                                    {
                                        switch (fieldTypeEncoding)
                                        {
                                            case FieldTypeEncodingMask.Variant:
                                                decodedFieldValue = jsonDecoder.ReadVariant(
                                                    field.FieldMetaData.Name);
                                                Assert.That(
                                                    ((Variant)decodedFieldValue).IsNull,
                                                    Is.False,
                                                    $"Decoded Field: {field.FieldMetaData.Name} value should not be null");
                                                Assert.That(
                                                    (Variant)decodedFieldValue,
                                                    Is.EqualTo(field.Value.WrappedValue),
                                                    $"Decoded Field name: {field.FieldMetaData.Name} values: encoded Variant {field.Value.WrappedValue} - decoded {dataSetPayload[field.FieldMetaData.Name]}");
#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
                                                Assert.That(
                                                    Utils.IsEqual(
                                                        field.Value.Value,
                                                        ((Variant)decodedFieldValue).Value
                                                    ),
                                                    Is.True,
                                                    $"Decoded Field name: {field.FieldMetaData.Name} values: encoded {field.Value.Value} - decoded {dataSetPayload[field.FieldMetaData.Name]}");
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning restore CS0618 // Type or member is obsolete
                                                break;
                                            case FieldTypeEncodingMask.RawData:
                                                decodedFieldValue = DecodeFieldData(
                                                    jsonDecoder,
                                                    field.FieldMetaData,
                                                    field.FieldMetaData.Name);
                                                Assert.That(
                                                    decodedFieldValue,
                                                    Is.Not.Null,
                                                    $"Decoded Field: {field.FieldMetaData.Name} value should not be null");
                                                // ExtendedNodeId namespaceIndex workaround issue
                                                if (decodedFieldValue is ExpandedNodeId expandedNodeId1 &&
                                                    !string.IsNullOrEmpty(
                                                        expandedNodeId1.NamespaceUri))
                                                {
                                                    // replace the namespaceUri with namespaceIndex to match the encoded value
                                                    ExpandedNodeId expandedNodeId = expandedNodeId1;
                                                    Assert.That(
                                                        expandedNodeId.IsNull,
                                                        Is.False,
                                                        $"Decoded 'ExpandedNodeId' Field: {field.FieldMetaData.Name} should not be null");
                                                    Assert.IsNotEmpty(
                                                        expandedNodeId.NamespaceUri,
                                                        "Decoded 'ExpandedNodeId.NamespaceUri' Field: {0} should not be empty",
                                                        field.FieldMetaData.Name);

                                                    ushort namespaceIndex = Convert.ToUInt16(
                                                        ServiceMessageContext.Create(jsonDecoder.Context.Telemetry)
                                                            .NamespaceUris
                                                            .GetIndex(
                                                                ((ExpandedNodeId)decodedFieldValue)
                                                                .NamespaceUri));

                                                    var stringBuilder = new StringBuilder();
                                                    ExpandedNodeId.Format(
                                                        CultureInfo.InvariantCulture,
                                                        stringBuilder,
                                                        expandedNodeId.IdentifierAsString,
                                                        expandedNodeId.IdType,
                                                        namespaceIndex,
                                                        string.Empty,
                                                        expandedNodeId.ServerIndex);
                                                    decodedFieldValue = ExpandedNodeId.Parse(
                                                        stringBuilder.ToString());
                                                }
#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
                                                Assert.That(
                                                    Utils.IsEqual(
                                                        field.Value.Value,
                                                        decodedFieldValue),
                                                    Is.True,
                                                    $"Decoded Field name: {field.FieldMetaData.Name} values: encoded {field.Value.Value} - decoded {dataSetPayload[field.FieldMetaData.Name]}");
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning restore CS0618 // Type or member is obsolete
                                                break;
                                            case FieldTypeEncodingMask.DataValue:
                                                bool wasPushed2 = jsonDecoder.PushStructure(
                                                    field.FieldMetaData.Name);
                                                var dataValue = new DataValue(Variant.Null);
                                                try
                                                {
                                                    if (wasPushed2 &&
                                                        jsonDecoder.ReadField("Value", out token))
                                                    {
                                                        // the Value was encoded using the non reversible json encoding
                                                        token = DecodeFieldData(
                                                            jsonDecoder,
                                                            field.FieldMetaData,
                                                            "Value");
#pragma warning disable CS0618 // Type or member is obsolete
                                                        dataValue = new DataValue(
                                                            new Variant(token));
#pragma warning restore CS0618 // Type or member is obsolete
                                                    }
                                                    else
                                                    {
                                                        // handle Good StatusCode that was not encoded
                                                        if (field.FieldMetaData.BuiltInType ==
                                                            (byte)BuiltInType.StatusCode)
                                                        {
                                                            dataValue = new DataValue(
                                                                new Variant(StatusCodes.Good));
                                                        }
                                                    }

                                                    if ((
                                                            jsonDataSetMessage.FieldContentMask &
                                                            DataSetFieldContentMask.StatusCode
                                                        ) != 0 &&
                                                        jsonDecoder.ReadField(
                                                            "StatusCode",
                                                            out token))
                                                    {
                                                        bool wasPush3 = jsonDecoder.PushStructure(
                                                            "StatusCode");
                                                        if (wasPush3)
                                                        {
                                                            dataValue.StatusCode = jsonDecoder
                                                                .ReadStatusCode("Code");
                                                            jsonDecoder.Pop();
                                                        }
                                                    }

                                                    if ((
                                                            jsonDataSetMessage.FieldContentMask &
                                                            DataSetFieldContentMask.SourceTimestamp
                                                        ) != 0)
                                                    {
                                                        dataValue.SourceTimestamp = jsonDecoder
                                                            .ReadDateTime(
                                                                "SourceTimestamp");
                                                    }

                                                    if ((
                                                            jsonDataSetMessage.FieldContentMask &
                                                            DataSetFieldContentMask.SourcePicoSeconds
                                                        ) != 0)
                                                    {
                                                        dataValue.SourcePicoseconds = jsonDecoder
                                                            .ReadUInt16(
                                                                "SourcePicoseconds");
                                                    }

                                                    if ((
                                                            jsonDataSetMessage.FieldContentMask &
                                                            DataSetFieldContentMask.ServerTimestamp
                                                        ) != 0)
                                                    {
                                                        dataValue.ServerTimestamp = jsonDecoder
                                                            .ReadDateTime(
                                                                "ServerTimestamp");
                                                    }

                                                    if ((
                                                            jsonDataSetMessage.FieldContentMask &
                                                            DataSetFieldContentMask.ServerPicoSeconds
                                                        ) != 0)
                                                    {
                                                        dataValue.ServerPicoseconds = jsonDecoder
                                                            .ReadUInt16(
                                                                "ServerPicoseconds");
                                                    }
#pragma warning disable CS0618 // Type or member is obsolete
                                                    Assert.That(
                                                        dataValue.Value,
                                                        Is.Not.Null,
                                                        $"Decoded Field: {field.FieldMetaData.Name} value should not be null");
#pragma warning restore CS0618 // Type or member is obsolete
                                                    // ExtendedNodeId namespaceIndex workaround issue
#pragma warning disable CS0618 // Type or member is obsolete
                                                    if (dataValue
                                                        .Value is ExpandedNodeId expandedNodeId2 &&
                                                        !string.IsNullOrEmpty(
                                                            expandedNodeId2.NamespaceUri))
                                                    {
                                                        // replace the namespaceUri with namespaceIndex to match the encoded value
                                                        ExpandedNodeId expandedNodeId = expandedNodeId2;
                                                        Assert.That(
                                                            expandedNodeId.IsNull,
                                                            Is.False,
                                                            $"Decoded 'ExpandedNodeId' Field: {field.FieldMetaData.Name} should not be null");
                                                        Assert.IsNotEmpty(
                                                            expandedNodeId.NamespaceUri,
                                                            "Decoded 'ExpandedNodeId.NamespaceUri' Field: {0} should not be empty",
                                                            field.FieldMetaData.Name);

#pragma warning disable CS0618 // Type or member is obsolete
                                                        ushort namespaceIndex = Convert.ToUInt16(
                                                            ServiceMessageContext.Create(jsonDecoder.Context.Telemetry)
                                                                .NamespaceUris
                                                                .GetIndex(
                                                                    ((ExpandedNodeId)dataValue
                                                                        .Value)
                                                                    .NamespaceUri));
#pragma warning restore CS0618 // Type or member is obsolete

                                                        var stringBuilder = new StringBuilder();
                                                        ExpandedNodeId.Format(
                                                            CultureInfo.InvariantCulture,
                                                            stringBuilder,
                                                            expandedNodeId.IdentifierAsString,
                                                            expandedNodeId.IdType,
                                                            namespaceIndex,
                                                            string.Empty,
                                                            expandedNodeId.ServerIndex);
#pragma warning disable CS0618 // Type or member is obsolete
                                                        dataValue.Value = ExpandedNodeId.Parse(
                                                            stringBuilder.ToString());
#pragma warning restore CS0618 // Type or member is obsolete
                                                    }
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
                                                    Assert.That(
                                                        Utils.IsEqual(
                                                            field.Value.Value,
                                                            dataValue.Value),
                                                        Is.True,
                                                        $"Decoded Field name: {field.FieldMetaData.Name} values: encoded {field.Value.Value} - decoded {dataSetPayload[field.FieldMetaData.Name]}");
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning restore CS0618 // Type or member is obsolete
                                                }
                                                finally
                                                {
                                                    if (wasPushed2)
                                                    {
                                                        jsonDecoder.Pop();
                                                    }
                                                }
                                                break;
                                        }
                                    }
                                }
                            }
                        }

                        if (jsonDecoder.ReadField(DataSetMessageSequenceNumber, out token))
                        {
                            sequenceNumberValue = jsonDecoder.ReadUInt32(
                                DataSetMessageSequenceNumber);
                            Assert.That(
                                sequenceNumberValue,
                                Is.EqualTo(jsonDataSetMessage.SequenceNumber),
                                $"jsonDataSetMessage.SequenceNumberValue was not decoded correctly, Encoded: {jsonDataSetMessage.SequenceNumber} Decoded: {sequenceNumberValue}");
                        }

                        if (jsonDecoder.ReadField(DataSetMessageMetaDataVersion, out token))
                        {
                            var configurationVersion =
                                jsonDecoder.ReadEncodeable(
                                    DataSetMessageMetaDataVersion,
                                    typeof(ConfigurationVersionDataType)
                                ) as ConfigurationVersionDataType;
                            Assert.That(
                                Utils.IsEqual(
                                    jsonDataSetMessage.MetaDataVersion,
                                    configurationVersion),
                                Is.True,
                                $"jsonDataSetMessage.MetaDataVersion was not decoded correctly, Encoded: {Utils.Format(
                                    "MajorVersion: {0}, MinorVersion: {1}",
                                    jsonDataSetMessage.MetaDataVersion.MajorVersion,
                                    jsonDataSetMessage.MetaDataVersion.MinorVersion
                                )} Decoded: {Utils.Format(
                                    "MajorVersion: {0}, MinorVersion: {1}",
                                    configurationVersion?.MajorVersion,
                                    configurationVersion?.MinorVersion)}");
                        }

                        if (jsonDecoder.ReadField(DataSetMessageTimestamp, out token))
                        {
                            DateTimeUtc timeStampValue = jsonDecoder.ReadDateTime(
                                DataSetMessageTimestamp);
                            Assert.That(
                                timeStampValue,
                                Is.EqualTo(jsonDataSetMessage.Timestamp),
                                $"jsonDataSetMessage.Timestamp was not decoded correctly, Encoded: {jsonDataSetMessage.Timestamp} Decoded: {timeStampValue}");
                        }

                        if (jsonDecoder.ReadField(DataSetMessageStatus, out token))
                        {
                            statusValue = jsonDecoder.ReadStatusCode(DataSetMessageStatus);
                            Assert.That(
                                statusValue,
                                Is.EqualTo(jsonDataSetMessage.Status),
                                $"jsonDataSetMessage.Timestamp was not decoded correctly, Encoded: {jsonDataSetMessage.Status} Decoded: {statusValue}");
                        }

                        jsonDecoder.Pop();
                    }
                }
            }

            return DataSetMessageFailOptions.Ok;
        }

        /// <summary>
        /// Decode field data
        /// </summary>
        private static object DecodeFieldData(
            PubSubJsonDecoder jsonDecoder,
            FieldMetaData fieldMetaData,
            string fieldName)
        {
            if (fieldMetaData.BuiltInType != 0)
            {
                try
                {
                    if (fieldMetaData.ValueRank == ValueRanks.Scalar)
                    {
                        return DecodeFieldByType(jsonDecoder, fieldMetaData.BuiltInType, fieldName);
                    }
                    if (fieldMetaData.ValueRank >= ValueRanks.OneDimension)
                    {
                        return jsonDecoder.ReadArray(
                            fieldName,
                            fieldMetaData.ValueRank,
                            (BuiltInType)fieldMetaData.BuiltInType);
                    }

                    Assert.Warn(
                        $"JsonDataSetMessage - Decoding ValueRank = {fieldMetaData.ValueRank} not supported yet !!!");
                }
                catch (Exception ex)
                {
                    Assert.Warn(
                        $"JsonDataSetMessage - Error reading element for RawData. {ex.Message}");
                    return StatusCodes.BadDecodingError;
                }
            }
            return null;
        }

        /// <summary>
        /// Decode field by type
        /// </summary>
        private static object DecodeFieldByType(
            PubSubJsonDecoder jsonDecoder,
            byte builtInType,
            string fieldName)
        {
            try
            {
                switch ((BuiltInType)builtInType)
                {
                    case BuiltInType.Boolean:
                        return jsonDecoder.ReadBoolean(fieldName);
                    case BuiltInType.SByte:
                        return jsonDecoder.ReadSByte(fieldName);
                    case BuiltInType.Byte:
                        return jsonDecoder.ReadByte(fieldName);
                    case BuiltInType.Int16:
                        return jsonDecoder.ReadInt16(fieldName);
                    case BuiltInType.UInt16:
                        return jsonDecoder.ReadUInt16(fieldName);
                    case BuiltInType.Int32:
                        return jsonDecoder.ReadInt32(fieldName);
                    case BuiltInType.UInt32:
                        return jsonDecoder.ReadUInt32(fieldName);
                    case BuiltInType.Int64:
                        return jsonDecoder.ReadInt64(fieldName);
                    case BuiltInType.UInt64:
                        return jsonDecoder.ReadUInt64(fieldName);
                    case BuiltInType.Float:
                        return jsonDecoder.ReadFloat(fieldName);
                    case BuiltInType.Double:
                        return jsonDecoder.ReadDouble(fieldName);
                    case BuiltInType.String:
                        return jsonDecoder.ReadString(fieldName);
                    case BuiltInType.DateTime:
                        return jsonDecoder.ReadDateTime(fieldName);
                    case BuiltInType.Guid:
                        return jsonDecoder.ReadGuid(fieldName);
                    case BuiltInType.ByteString:
                        return jsonDecoder.ReadByteString(fieldName);
                    case BuiltInType.XmlElement:
                        return jsonDecoder.ReadXmlElement(fieldName);
                    case BuiltInType.NodeId:
                        return jsonDecoder.ReadNodeId(fieldName);
                    case BuiltInType.ExpandedNodeId:
                        return jsonDecoder.ReadExpandedNodeId(fieldName);
                    case BuiltInType.QualifiedName:
                        return jsonDecoder.ReadQualifiedName(fieldName);
                    case BuiltInType.LocalizedText:
                        return jsonDecoder.ReadLocalizedText(fieldName);
                    case BuiltInType.DataValue:
                        return jsonDecoder.ReadDataValue(fieldName);
                    case BuiltInType.Enumeration:
                        return jsonDecoder.ReadInt32(fieldName);
                    case BuiltInType.Variant:
                        return jsonDecoder.ReadVariant(fieldName);
                    case BuiltInType.ExtensionObject:
                        return jsonDecoder.ReadExtensionObject(fieldName);
                    case BuiltInType.DiagnosticInfo:
                        return jsonDecoder.ReadDiagnosticInfo(fieldName);
                    case BuiltInType.StatusCode:
                        return jsonDecoder.ReadStatusCode(fieldName);
                }
            }
            catch (Exception)
            {
                Assert
                    .Warn($"JsonDataSetMessage - Error decoding field {fieldName}");
            }

            return null;
        }

        /// <summary>
        /// Format and validate a JSON string.
        /// </summary>
        private static string PrettifyAndValidateJson(byte[] json)
        {
            return PrettifyAndValidateJson(System.Text.Encoding.UTF8.GetString(json));
        }

        /// <summary>
        /// Format and validate a JSON string.
        /// </summary>
        private static string PrettifyAndValidateJson(string json)
        {
            try
            {
                using var stringWriter = new StringWriter();
                using var stringReader = new StringReader(json);
                var jsonReader = new JsonTextReader(stringReader);
                var jsonWriter = new JsonTextWriter(stringWriter)
                {
                    FloatFormatHandling = FloatFormatHandling.String,
                    Formatting = Formatting.Indented,
                    Culture = CultureInfo.InvariantCulture
                };
                jsonWriter.WriteToken(jsonReader);
                string formattedJson = stringWriter.ToString();
                TestContext.Out.WriteLine(formattedJson);
                return formattedJson;
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine(json);
                Assert.Fail("Invalid json data: " + ex.Message);
            }
            return json;
        }
    }
}
