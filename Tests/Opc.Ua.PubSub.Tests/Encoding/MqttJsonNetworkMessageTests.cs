/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.PubSub.Transport;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    [TestFixture(Description = "Tests for Encoding/Decoding of JsonNetworkMessage objects")]
    public class MqttJsonNetworkMessageTests
    {
        private const UInt16 NamespaceIndexAllTypes = 3;

        private const string MqttAddressUrl = "mqtt://localhost:1883";
        static IList<DateTime> s_publishTimes = new List<DateTime>();

        private const string MetaDataMessageId = "MessageId";
        private const string MetaDataMessageType = "MessageType";
        private const string MetaDataPublisherId = "PublisherId";
        private const string MetaDataDataSetWriterId = "DataSetWriterId";

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
            MetaData_Description,
            MetaData_Fields,
            MetaData_DataSetClassId,
            MetaData_ConfigurationVersion
        }

        private const string NetworkMessageMessageId = "MessageId";
        private const string NetworkMessageMessageType = "MessageType";
        private const string NetworkMessagePublisherId = "PublisherId";
        private const string NetworkMessageDataSetClassId = "DataSetClassId";
        private const string NetworkMessageMessages = "Messages";

        private enum NetworkMessageFailOptions
        {
            Ok,
            MessageId,
            MessageType,
            PublisherId,
            DataSetClassId,
            Messages
        }

        private const string DataSetMessageDataSetWriterId = "DataSetWriterId";
        private const string DataSetMessageSequenceNumber = "SequenceNumber";
        private const string DataSetMessageMetaDataVersion = "MetaDataVersion";
        private const string DataSetMessageTimestamp = "Timestamp";
        private const string DataSetMessageStatus = "Status";
        private const string DataSetMessagePayload = "Payload";

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

        [OneTimeSetUp()]
        public void MyTestInitialize()
        {
            // add some namespaceUris to be used at encode/decode
            ServiceMessageContext.GlobalContext.NamespaceUris.Append("http://opcfoundation.org/UA/DI/");
            ServiceMessageContext.GlobalContext.NamespaceUris.Append("http://opcfoundation.org/UA/ADI/");
            ServiceMessageContext.GlobalContext.NamespaceUris.Append("http://opcfoundation.org/UA/IA/");
        }

        [SetUp()]
        public void TestSetup()
        {
            s_publishTimes.Clear();
        }

        [Test(Description = "Validate NetworkMessageHeader & PublisherId with PublisherId as parameter")]
        public void ValidateMessageHeaderAndPublisherIdWithParameters(
           [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData,
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(JsonDataSetMessageContentMask.None,
            JsonDataSetMessageContentMask.DataSetWriterId,
            JsonDataSetMessageContentMask.MetaDataVersion,
            JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp|JsonDataSetMessageContentMask.Status)]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
            [Values (JsonNetworkMessageContentMask.None, JsonNetworkMessageContentMask.DataSetClassId, JsonNetworkMessageContentMask.ReplyTo,
            JsonNetworkMessageContentMask.DataSetClassId| JsonNetworkMessageContentMask.DataSetMessageHeader,
            JsonNetworkMessageContentMask.ReplyTo| JsonNetworkMessageContentMask.DataSetClassId)]
                JsonNetworkMessageContentMask jsonNetworkMessageContentMask,
            [Values(1, "abc")] object publisherId)
        {
            // Arrange
            jsonNetworkMessageContentMask = jsonNetworkMessageContentMask | JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.PublisherId | JsonNetworkMessageContentMask.DataSetMessageHeader;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First().WriterGroups.First(), "publisherConfiguration first writer group of first connection should not be null");
            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState());
            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.GreaterOrEqual(networkMessages.Count, 1, "connection.CreateNetworkMessages shall have at least one network message");

            List<JsonNetworkMessage> uaDataNetworkMessages = MessagesHelper.GetJsonUaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaDataNetworkMessages, "Json ua-data entries are missing from configuration!");

            // set PublisherId
            foreach (JsonNetworkMessage uaNetworkMessage in uaDataNetworkMessages)
            {
                uaNetworkMessage.PublisherId = publisherId.ToString();
            }

            List<JsonNetworkMessage> uaMetaDataNetworkMessages = MessagesHelper.GetJsonUaMetaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaMetaDataNetworkMessages, "Json ua-metadata entries are missing from configuration!");

            // set PublisherId
            foreach (JsonNetworkMessage uaNetworkMessage in uaMetaDataNetworkMessages)
            {
                uaNetworkMessage.PublisherId = publisherId.ToString();
            }

            bool hasDataSetWriterId = (jsonNetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0
                 && (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1, setDataSetWriterId: hasDataSetWriterId,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");
            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");

            // Assert
            foreach (JsonNetworkMessage uaDataNetworkMessage in uaDataNetworkMessages)
            {
                CompareEncodeDecode(uaDataNetworkMessage, dataSetReaders);
            }
            foreach (JsonNetworkMessage uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                CompareEncodeDecode(uaMetaDataNetworkMessage, dataSetReaders);
            }
        }

        // [Ignore("Temporary disabled due to changes in DataSetClassId handling on NetworkMessage")]
        [Test(Description = "Validate NetworkMessageHeader & DataSetClassId")]
        public void ValidateMessageHeaderAndDataSetClassIdWithParameters(
           [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
           [Values(JsonDataSetMessageContentMask.None,
            JsonDataSetMessageContentMask.DataSetWriterId,
            JsonDataSetMessageContentMask.MetaDataVersion,
            JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp|JsonDataSetMessageContentMask.Status)]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask)
        {


            /*The DataSetClassId associated with the DataSets in the NetworkMessage.
            This value is optional. The presence of the value depends on the setting in the JsonNetworkMessageContentMask.
            If specified, all DataSetMessages in the NetworkMessage shall have the same DataSetClassId.
            The source is the DataSetClassId on the PublishedDataSet (see 6.2.2.2) associated with the DataSetWriters that produced the DataSetMessages.*/

            // Arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.DataSetClassId | JsonNetworkMessageContentMask.SingleDataSetMessage;     // add SingleDataSetMessage flag because of the special implementation od DataSetClassId that is written only in this case

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
             {
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
             };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: 1, writerGroupId: 1,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First().WriterGroups.First(), "publisherConfiguration  first writer group of first connection should not be null");
            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState());

            List<JsonNetworkMessage> uaNetworkMessages = MessagesHelper.GetJsonUaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaNetworkMessages, "Json ua-data entries are missing from configuration!");

            // set DataSetClassId
            Guid dataSetClassId = Guid.NewGuid();
            foreach (JsonNetworkMessage uaNetworkMessage in uaNetworkMessages)
            {
                uaNetworkMessage.DataSetClassId = dataSetClassId.ToString();
                uaNetworkMessage.DataSetMessages[0].DataSet.DataSetMetaData.DataSetClassId = (Uuid)dataSetClassId;
            }

            bool hasDataSetWriterId = (jsonNetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0
                && (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: null, writerGroupId: 1, setDataSetWriterId: hasDataSetWriterId, // the writer header is saved
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");
            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");

            // Assert
            // check first consistency of ua-data network messages
            List<JsonNetworkMessage> uaDataNetworkMessages = MessagesHelper.GetJsonUaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaDataNetworkMessages, "Json ua-data entries are missing from configuration!");

            int index = 0;
            foreach (var uaDataNetworkMessage in uaDataNetworkMessages)
            {
                CompareEncodeDecode(uaDataNetworkMessage, new List<DataSetReaderDataType>() { dataSetReaders[index++] });
            }
        }

        [Test(Description = "Validate NetworkMessageHeader & DataSetMessageHeader without PublisherId parameter")]
        public void ValidateNetworkMessageHeaderAndDataSetMessageHeaderWithParameters(
           [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(JsonDataSetMessageContentMask.None,
            JsonDataSetMessageContentMask.DataSetWriterId,
            JsonDataSetMessageContentMask.MetaDataVersion,
            JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp|JsonDataSetMessageContentMask.Status)]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask)
        {
            // Arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.DataSetMessageHeader;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: 1, writerGroupId: 1,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First().WriterGroups.First(), "publisherConfiguration  first writer group of first connection should not be null");
            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState());
            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.GreaterOrEqual(networkMessages.Count, 1, "connection.CreateNetworkMessages shall have at least one network message");

            List<JsonNetworkMessage> uaNetworkMessages = MessagesHelper.GetJsonUaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaNetworkMessages, "Json ua-data entries are missing from configuration!");

            List<JsonNetworkMessage> uaMetaDataNetworkMessages = MessagesHelper.GetJsonUaMetaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaMetaDataNetworkMessages, "Json ua-metadata entries are missing from configuration!");

            bool hasDataSetWriterId = (jsonNetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0
                && (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: null, writerGroupId: 1, setDataSetWriterId: hasDataSetWriterId, // the writer header is saved
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");
            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");

            // Assert
            foreach (JsonNetworkMessage uaDataNetworkMessage in uaNetworkMessages)
            {
                CompareEncodeDecode(uaDataNetworkMessage, dataSetReaders);
            }
            foreach (JsonNetworkMessage uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                CompareEncodeDecodeMetaData(uaMetaDataNetworkMessage);
            }
        }


        [Test(Description = "Validate NetworkMessageHeader & DataSetMessageHeader with PublisherId parameter")]
        public void ValidateNetworkAndDataSetMessageHeaderWithParameters(
           [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(JsonDataSetMessageContentMask.None,
            JsonDataSetMessageContentMask.DataSetWriterId,
            JsonDataSetMessageContentMask.MetaDataVersion,
            JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp|JsonDataSetMessageContentMask.Status)]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
            [Values(1, "abc")] object publisherId)
        {
            // Arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.DataSetMessageHeader
                | JsonNetworkMessageContentMask.PublisherId;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First().WriterGroups.First(), "publisherConfiguration  first writer group of first connection should not be null");
            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState());
            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.GreaterOrEqual(networkMessages.Count, 1, "connection.CreateNetworkMessages shall have at least one network message");

            List<JsonNetworkMessage> uaNetworkMessages = MessagesHelper.GetJsonUaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaNetworkMessages, "Json ua-data entries are missing from configuration!");

            List<JsonNetworkMessage> uaMetaDataNetworkMessages = MessagesHelper.GetJsonUaMetaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaMetaDataNetworkMessages, "Json ua-metadata entries are missing from configuration!");

            bool hasDataSetWriterId = (jsonNetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0
               && (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1, setDataSetWriterId: hasDataSetWriterId, // no headers hence the values
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");
            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");

            // Assert
            foreach (JsonNetworkMessage uaDataNetworkMessage in uaNetworkMessages)
            {
                CompareEncodeDecode(uaDataNetworkMessage, dataSetReaders);
            }
            foreach (JsonNetworkMessage uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                CompareEncodeDecodeMetaData(uaMetaDataNetworkMessage);
            }
        }

        [Test(Description = "Validate DataSetMessageHeader only with all JsonDataSetMessageContentMask combination")]
        public void ValidateDataSetMessageHeaderWithParameters(
            [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(JsonDataSetMessageContentMask.None,
            JsonDataSetMessageContentMask.DataSetWriterId,
            JsonDataSetMessageContentMask.MetaDataVersion,
            JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp|JsonDataSetMessageContentMask.Status)]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask)
        {
            // Arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.DataSetMessageHeader;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: 1, writerGroupId: 1,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First().WriterGroups.First(), "publisherConfiguration  first writer group of first connection should not be null");
            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState());
            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.GreaterOrEqual(networkMessages.Count, 1, "connection.CreateNetworkMessages shall have at least one network message");

            List<JsonNetworkMessage> uaNetworkMessages = MessagesHelper.GetJsonUaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaNetworkMessages, "Json ua-data entries are missing from configuration!");

            List<JsonNetworkMessage> uaMetaDataNetworkMessages = MessagesHelper.GetJsonUaMetaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaMetaDataNetworkMessages, "Json ua-metadata entries are missing from configuration!");

            bool hasDataSetWriterId = (jsonNetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0
                && (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: null, writerGroupId: 1, setDataSetWriterId: hasDataSetWriterId, // the writer header is saved
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");
            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");

            // Assert 
            foreach (JsonNetworkMessage uaDataNetworkMessage in uaNetworkMessages)
            {
                CompareEncodeDecode(uaDataNetworkMessage, dataSetReaders);
            }
            foreach (JsonNetworkMessage uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                CompareEncodeDecodeMetaData(uaMetaDataNetworkMessage);
            }
        }

        [Test(Description = "Validate SingleDataSetMessage with parameters for DataSetFieldContentMask, JsonDataSetMessageContentMask and JsonNetworkMessageContentMask")]
        public void ValidateSingleDataSetMessageWithParameters(
            [Values(DataSetFieldContentMask.None,
            DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(JsonDataSetMessageContentMask.None,
            JsonDataSetMessageContentMask.DataSetWriterId,
            JsonDataSetMessageContentMask.MetaDataVersion,
            JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Status,
            JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion|JsonDataSetMessageContentMask.SequenceNumber|JsonDataSetMessageContentMask.Timestamp|JsonDataSetMessageContentMask.Status)]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
            [Values (JsonNetworkMessageContentMask.None,
            JsonNetworkMessageContentMask.NetworkMessageHeader,
            JsonNetworkMessageContentMask.DataSetMessageHeader,
            JsonNetworkMessageContentMask.DataSetClassId,
            JsonNetworkMessageContentMask.PublisherId,
            JsonNetworkMessageContentMask.ReplyTo,
            JsonNetworkMessageContentMask.NetworkMessageHeader | JsonNetworkMessageContentMask.DataSetMessageHeader,
            JsonNetworkMessageContentMask.DataSetClassId| JsonNetworkMessageContentMask.DataSetMessageHeader,
            JsonNetworkMessageContentMask.PublisherId| JsonNetworkMessageContentMask.DataSetMessageHeader,
            JsonNetworkMessageContentMask.ReplyTo| JsonNetworkMessageContentMask.DataSetMessageHeader,
            JsonNetworkMessageContentMask.NetworkMessageHeader | JsonNetworkMessageContentMask.DataSetMessageHeader|JsonNetworkMessageContentMask.DataSetClassId,
            JsonNetworkMessageContentMask.PublisherId| JsonNetworkMessageContentMask.DataSetMessageHeader|JsonNetworkMessageContentMask.DataSetClassId,
            JsonNetworkMessageContentMask.ReplyTo| JsonNetworkMessageContentMask.DataSetMessageHeader|JsonNetworkMessageContentMask.DataSetClassId,
            JsonNetworkMessageContentMask.NetworkMessageHeader | JsonNetworkMessageContentMask.DataSetMessageHeader|JsonNetworkMessageContentMask.DataSetClassId|JsonNetworkMessageContentMask.PublisherId,
            JsonNetworkMessageContentMask.ReplyTo| JsonNetworkMessageContentMask.DataSetMessageHeader|JsonNetworkMessageContentMask.DataSetClassId|JsonNetworkMessageContentMask.PublisherId,
            JsonNetworkMessageContentMask.NetworkMessageHeader |JsonNetworkMessageContentMask.ReplyTo| JsonNetworkMessageContentMask.DataSetMessageHeader|JsonNetworkMessageContentMask.DataSetClassId|JsonNetworkMessageContentMask.PublisherId)]
                JsonNetworkMessageContentMask jsonNetworkMessageContentMask
            )
        {
            // Arrange
            // mark SingleDataSetMessage message
            jsonNetworkMessageContentMask = jsonNetworkMessageContentMask | JsonNetworkMessageContentMask.SingleDataSetMessage;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
           {
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
           };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: 1, writerGroupId: 1,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First().WriterGroups.First(), "publisherConfiguration  first writer group of first connection should not be null");
            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState());
            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.GreaterOrEqual(networkMessages.Count, 1, "connection.CreateNetworkMessages shall have at least one network message");

            bool hasDataSetWriterId = (jsonNetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0
                && (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: null, writerGroupId: 1, setDataSetWriterId: hasDataSetWriterId, // no headers hence the values
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");
            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");

            // Assert
            // check first consistency of ua-data network messages
            List<JsonNetworkMessage> uaDataNetworkMessages = MessagesHelper.GetJsonUaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaDataNetworkMessages, "Json ua-data entries are missing from configuration!");
            int index = 0;
            foreach (var uaDataNetworkMessage in uaDataNetworkMessages)
            {
                CompareEncodeDecode(uaDataNetworkMessage, new List<DataSetReaderDataType>() { dataSetReaders[index++] });
            }

            List<JsonNetworkMessage> uaMetaDataNetworkMessages = MessagesHelper.GetJsonUaMetaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaMetaDataNetworkMessages, "Json ua-metadata entries are missing from configuration!");
            index = 0;
            foreach (var uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                CompareEncodeDecodeMetaData(uaMetaDataNetworkMessage); //(uaMetaDataNetworkMessage as JsonNetworkMessage, new List<DataSetReaderDataType>() { dataSetReaders[index++] });
            }
        }

        [Test(Description = "Validate that metadata is encoded/decoded correctly")]
        public void ValidateMetaDataIsEncodedCorrectly()
        {
            // Arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.None;
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.None;
            DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("MetaData1"),
                MessagesHelper.CreateDataSetMetaData2("MetaData2"),
                MessagesHelper.CreateDataSetMetaData3("MetaData3"),
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaDataArrays("Arrays"),
                MessagesHelper.CreateDataSetMetaDataMatrices("Matrices"),
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: 1, writerGroupId: 1,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);

            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            WriterGroupPublishState publishState = new WriterGroupPublishState();

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First().WriterGroups.First(), "publisherConfiguration  first writer group of first connection should not be null");
            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), publishState);
            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.GreaterOrEqual(networkMessages.Count, 1, "connection.CreateNetworkMessages shall have at least one network message");

            List<JsonNetworkMessage> uaMetaDataNetworkMessages = MessagesHelper.GetJsonUaMetaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaMetaDataNetworkMessages, "Json ua-metadata entries are missing from configuration!");

            foreach (var uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                CompareEncodeDecodeMetaData(uaMetaDataNetworkMessage);
            }
        }

        [Test(Description = "Validate that metadata with update time 0 is sent at startup for a MQTT Json publisher")]
        public void ValidateMetaDataUpdateTimeZeroSentAtStartup()
        {
            // Arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.None;
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.None;
            DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("MetaData1"),
                MessagesHelper.CreateDataSetMetaData2("MetaData2"),
                MessagesHelper.CreateDataSetMetaData3("MetaData3"),
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaDataArrays("Arrays"),
                MessagesHelper.CreateDataSetMetaDataMatrices("Matrices"),
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: 1, writerGroupId: 1,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes, 0);

            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            WriterGroupPublishState publishState = new WriterGroupPublishState();

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First().WriterGroups.First(), "publisherConfiguration  first writer group of first connection should not be null");

            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), publishState);
            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.GreaterOrEqual(networkMessages.Count, 1, "connection.CreateNetworkMessages shall have at least one network message");

            List<JsonNetworkMessage> uaMetaDataNetworkMessages = MessagesHelper.GetJsonUaMetaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaMetaDataNetworkMessages, "Json ua-metadata entries are missing from configuration!");

            // check if there are as many metadata messages as metadata were created in ARRAY
            Assert.AreEqual(dataSetMetaDataArray.Length, uaMetaDataNetworkMessages.Count, "The ua-metadata messages count is different from the number of metadata in publisher!");
            int index = 0;
            foreach (var uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                // compare the initial metadata with the one from the messages
                Assert.IsTrue(Utils.IsEqual(dataSetMetaDataArray[index], uaMetaDataNetworkMessage.DataSetMetaData),
                    "Metadata from network message is different from the original one for name " + dataSetMetaDataArray[index].Name);

                index++;
            }

            // get the messages again and see if there are any metadata messages
            networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), publishState);
            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.GreaterOrEqual(networkMessages.Count, 1, "connection.CreateNetworkMessages shall have at least one network message");

            uaMetaDataNetworkMessages = MessagesHelper.GetJsonUaMetaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaMetaDataNetworkMessages, "Json ua-metadata entries are missing from configuration!");

            // check if there are any metadata messages. second time around there shall be no metadata messages
            Assert.AreEqual(0, uaMetaDataNetworkMessages.Count, "The ua-metadata messages count shall be zero for the second time when create messages is called!");
        }

        [Test(Description = "Validate that metadata with update time 0 is sent when the metadata changes for a MQTT Json publisher")]
        public void ValidateMetaDataUpdateTimeZeroSentAtMetaDataChange()
        {
            // Arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.None;
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.None;
            DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("MetaData1"),
                MessagesHelper.CreateDataSetMetaData2("MetaData2"),
                MessagesHelper.CreateDataSetMetaData3("MetaData3"),
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaDataArrays("Arrays"),
                MessagesHelper.CreateDataSetMetaDataMatrices("Matrices"),
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: 1, writerGroupId: 1,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes, 0);

            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            WriterGroupPublishState publishState = new WriterGroupPublishState();

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First().WriterGroups.First(), "publisherConfiguration  first writer group of first connection should not be null");
            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), publishState);
            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.GreaterOrEqual(networkMessages.Count, 1, "connection.CreateNetworkMessages shall have at least one network message");

            List<JsonNetworkMessage> uaMetaDataNetworkMessages = MessagesHelper.GetJsonUaMetaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaMetaDataNetworkMessages, "Json ua-metadata entries are missing from configuration!");

            // check if there are as many metadata messages as metadata were created in ARRAY
            Assert.AreEqual(dataSetMetaDataArray.Length, uaMetaDataNetworkMessages.Count, "The ua-metadata messages count is different from the number of metadata in publisher!");
            int index = 0;
            foreach (var uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                // compare the initial metadata with the one from the messages
                Assert.IsTrue(Utils.IsEqual(dataSetMetaDataArray[index], uaMetaDataNetworkMessage.DataSetMetaData),
                    "Metadata from network message is different from the original one for name " + dataSetMetaDataArray[index].Name);

                index++;
            }

            // get the messages again and see if there are any metadata messages
            networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), publishState);
            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.GreaterOrEqual(networkMessages.Count, 1, "connection.CreateNetworkMessages shall have at least one network message");

            uaMetaDataNetworkMessages = MessagesHelper.GetJsonUaMetaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaMetaDataNetworkMessages, "Json ua-metadata entries are missing from configuration!");

            // check if there are any metadata messages. second time around there shall be no metadata messages
            Assert.AreEqual(0, uaMetaDataNetworkMessages.Count, "The ua-metadata messages count shall be zero for the second time when create messages is called!");

            // change the metadata version
            DateTime currentDateTime = DateTime.UtcNow;
            foreach (DataSetMetaDataType dataSetMetaData in dataSetMetaDataArray)
            {
                dataSetMetaData.ConfigurationVersion.MajorVersion =
                    ConfigurationVersionUtils.CalculateVersionTime(currentDateTime);
                dataSetMetaData.ConfigurationVersion.MinorVersion = dataSetMetaData.ConfigurationVersion.MajorVersion;
            }

            // get the messages again and see if there are any metadata messages
            networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), publishState);
            Assert.IsNotNull(networkMessages, "After MetaDataVersion change - connection.CreateNetworkMessages shall not return null");
            Assert.GreaterOrEqual(networkMessages.Count, 1, "After MetaDataVersion change - connection.CreateNetworkMessages shall have at least one network message");

            uaMetaDataNetworkMessages = MessagesHelper.GetJsonUaMetaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaMetaDataNetworkMessages, "After MetaDataVersion change - Json ua-metadata entries are missing from configuration!");

            // check if there are any metadata messages. second time around there shall be no metadata messages
            Assert.AreEqual(dataSetMetaDataArray.Length, uaMetaDataNetworkMessages.Count, "After MetaDataVersion change - The ua-metadata messages count shall be equal to number of dataSetMetaData!");

            index = 0;
            foreach (var uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                // compare the initial metadata with the one from the messages
                Assert.IsTrue(Utils.IsEqual(dataSetMetaDataArray[index], uaMetaDataNetworkMessage.DataSetMetaData),
                    "After MetaDataVersion change - Metadata from network message is different from the original one for name " + dataSetMetaDataArray[index].Name);

                index++;
            }
        }

        [Test(Description = "Validate that metadata with update time different than 0 is sent periodically for a MQTT Json publisher")]
        [Ignore("Max deviation instable in this version.")]
        public void ValidateMetaDataUpdateTimeNonZeroIsSentPeriodically([Values(100, 1000, 2000)] double metaDataUpdateTime,
            [Values(30, 40)] double maxDeviation,
            [Values(10)] int publishTimeInSeconds)
        {
            s_publishTimes.Clear();
            // arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.None;
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.None;
            DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("MetaData1"),
            };
            // create the publisher configuration
            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: 1, writerGroupId: 1,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes, 0);

            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // create the mock IMqttPubSubConnection that will be used to monitor how often the metadata will be sent
            var mockConnection = new Mock<IMqttPubSubConnection>();

            mockConnection.Setup(x
                => x.CanPublishMetaData(It.IsAny<WriterGroupDataType>(), It.IsAny<DataSetWriterDataType>())).Returns(true);

            mockConnection.Setup(x => x.CreateDataSetMetaDataNetworkMessage(It.IsAny<WriterGroupDataType>(), It.IsAny<DataSetWriterDataType>()))
                .Callback(() => s_publishTimes.Add(DateTime.Now));

            WriterGroupDataType writerGroupDataType = publisherConfiguration.Connections.First().WriterGroups.First();

            //Act 
            MqttMetadataPublisher mqttMetaDataPublisher = new MqttMetadataPublisher(mockConnection.Object, writerGroupDataType,
                writerGroupDataType.DataSetWriters[0], metaDataUpdateTime);
            mqttMetaDataPublisher.Start();

            //wait so many seconds
            Thread.Sleep(publishTimeInSeconds * 1000);
            mqttMetaDataPublisher.Stop();
            int faultIndex = -1;
            double faultDeviation = 0;

            s_publishTimes = (from t in s_publishTimes
                              orderby t
                              select t).ToList();

            //Assert
            for (int i = 1; i < s_publishTimes.Count; i++)
            {
                double interval = s_publishTimes[i].Subtract(s_publishTimes[i - 1]).TotalMilliseconds;
                double deviation = Math.Abs(metaDataUpdateTime - interval);
                if (deviation >= maxDeviation && deviation > faultDeviation)
                {
                    faultIndex = i;
                    faultDeviation = deviation;
                }
            }

            Assert.IsTrue(faultIndex < 0, "publishingInterval={0}, maxDeviation={1}, publishTimeInSeconds={2}, deviation[{3}] = {4} has maximum deviation", metaDataUpdateTime, maxDeviation, publishTimeInSeconds, faultIndex, faultDeviation);
        }
                
        [Test(Description = "Validate missing or wrong DataSetMetaData fields definition")]
        public void ValidateMissingDataSetMetaDataDefinitions(
            [Values("1", null)] string messageId,
            [Values("1", null)] string publisherId,
            [Values(1, null)] object dataSetWriterId,
            [Values(false, true)] bool hasMetaData,
            [Values("Simple", null)] string metaDataName,
            [Values("Description text", null)] string metaDataDescription,
            [Values(false, true)] bool hasMetaDataDataSetClassId,
            [Values(false, true)] bool hasMetaDataConfigurationVersion,
            [Values(false, true)] bool hasMetaDataFields)
        {
            DataSetMetaDataType metaDataType = MessagesHelper.CreateDataSetMetaData1("DataSet1");
            WriterGroupDataType writerGroup = MessagesHelper.CreateWriterGroup(1);

            DataSetMetaDataType metadata =
                MessagesHelper.CreateDataSetMetaData(dataSetName: "Test missing metadata fields definition", NamespaceIndexAllTypes, metaDataType.Fields);
            metadata.Description = new LocalizedText("Description text");
            metadata.DataSetClassId = new Uuid();

            DataSetMetaDataType dataSetMetaData = hasMetaData ? metadata : null;

            JsonNetworkMessage jsonNetworkMessage = new JsonNetworkMessage(writerGroup, metadata);
            jsonNetworkMessage.MessageId = messageId;
            jsonNetworkMessage.PublisherId = publisherId;
            jsonNetworkMessage.DataSetWriterId = MessagesHelper.ConvertToNullable<UInt16>(dataSetWriterId);

            jsonNetworkMessage.DataSetMetaData.Name = metaDataName;
            jsonNetworkMessage.DataSetMetaData.Description = metaDataDescription!=null ? new LocalizedText(metaDataDescription) : metaDataDescription;
            jsonNetworkMessage.DataSetMetaData.DataSetClassId = hasMetaDataDataSetClassId ? new Uuid(Guid.NewGuid()) : Uuid.Empty;
            jsonNetworkMessage.DataSetMetaData.ConfigurationVersion = hasMetaDataConfigurationVersion ?  new ConfigurationVersionDataType() { MajorVersion = 1, MinorVersion = 1 }: new ConfigurationVersionDataType();
            if (!hasMetaDataFields)
            {
                jsonNetworkMessage.DataSetMetaData.Fields = null;
            }
            
            MetaDataFailOptions failOptions = VerifyDataSetMetaDataEncoding(jsonNetworkMessage);
            if (failOptions != MetaDataFailOptions.Ok)
            {
                switch (failOptions)
                {
                    case MetaDataFailOptions.MessageId:
                        Assert.AreEqual(failOptions, MetaDataFailOptions.MessageId, "ValidateMissingDataSetMetaDataDefinitions should fail due to missing MessageId reason.");
                        break;
                    case MetaDataFailOptions.PublisherId:
                        Assert.AreEqual(failOptions, MetaDataFailOptions.PublisherId, "ValidateMissingDataSetMetaDataDefinitions should fail due to missing PublisherId reason.");
                        break;
                    case MetaDataFailOptions.DataSetWriterId:
                        Assert.AreEqual(failOptions, MetaDataFailOptions.DataSetWriterId, "ValidateMissingDataSetMetaDataDefinitions should fail due to missing DataSetWriterId reason.");
                        break;
                    case MetaDataFailOptions.NonMetadata:
                        Assert.AreEqual(failOptions, MetaDataFailOptions.DataSetMetaData | MetaDataFailOptions.MessageType, "ValidateMissingDataSetMetaDataDefinitions should fail due to missing DataSetMetaData reason.");
                        break;
                    case MetaDataFailOptions.MetaData_Name:
                        Assert.AreEqual(failOptions, MetaDataFailOptions.MetaData_Name, "ValidateMissingDataSetMetaDataDefinitions should fail due to missing MetaData.Name reason.");
                        break;
                    case MetaDataFailOptions.MetaData_Description:
                        Assert.AreEqual(failOptions, MetaDataFailOptions.MetaData_Description, "ValidateMissingDataSetMetaDataDefinitions should fail due to missing MetaData.Description reason.");
                        break;
                    case MetaDataFailOptions.MetaData_DataSetClassId:
                        Assert.AreEqual(failOptions, MetaDataFailOptions.MetaData_DataSetClassId, "ValidateMissingDataSetMetaDataDefinitions should fail due to missing MetaData.DataSetClassId reason.");
                        break;
                    case MetaDataFailOptions.MetaData_ConfigurationVersion:
                        Assert.AreEqual(failOptions, MetaDataFailOptions.MetaData_ConfigurationVersion, "ValidateMissingDataSetMetaDataDefinitions should fail due to missing MetaData.ConfigurationVersion reason.");
                        break;
                    case MetaDataFailOptions.MetaData_Fields:
                        Assert.AreEqual(failOptions, MetaDataFailOptions.MetaData_Fields, "ValidateMissingDataSetMetaDataDefinitions should fail due to missing MetaData.Fields reason.");
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
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader;
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId;
            DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3"),
            };

            PubSubConfigurationDataType pubSubConfiguration = MessagesHelper.ConfigureDataSetMessages(Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl,
                writerGroupId: 1,
                jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray,
                NamespaceIndexAllTypes);
            Assert.IsNotNull(pubSubConfiguration, "pubSubConfiguration should not be null");

            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(pubSubConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication should not be null");
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            var networkMessages = connection.CreateNetworkMessages(pubSubConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState());
            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");

            // Assert
            // check first consistency of ua-data network messages
            List<JsonNetworkMessage> uaDataNetworkMessages = MessagesHelper.GetJsonUaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaDataNetworkMessages, "Json ua-data entries are missing from configuration!");

            foreach (JsonNetworkMessage jsonNetworkMessage in uaDataNetworkMessages)
            {
                jsonNetworkMessage.MessageId = messageId;
                jsonNetworkMessage.PublisherId = publisherId;
                jsonNetworkMessage.DataSetClassId = dataSetClassId;

                NetworkMessageFailOptions failOptions = (NetworkMessageFailOptions)VerifyDataEncoding(jsonNetworkMessage);
                if(failOptions != NetworkMessageFailOptions.Ok)
                {
                    switch(failOptions)
                    {
                        case NetworkMessageFailOptions.MessageId:
                            Assert.AreEqual(failOptions, NetworkMessageFailOptions.MessageId, "ValidateMissingNetworkMessageFields should fail due to missing MessageId reason.");
                            break;
                        case NetworkMessageFailOptions.MessageType:
                            Assert.AreEqual(failOptions, NetworkMessageFailOptions.MessageType, "ValidateMissingNetworkMessageFields should fail due to missing MessageType reason.");
                            break;
                        case NetworkMessageFailOptions.PublisherId:
                            Assert.AreEqual(failOptions, NetworkMessageFailOptions.PublisherId, "ValidateMissingNetworkMessageFields should fail due to missing PublisherId reason.");
                            break;
                        case NetworkMessageFailOptions.DataSetClassId:
                            Assert.AreEqual(failOptions, NetworkMessageFailOptions.DataSetClassId, "ValidateMissingNetworkMessageFields should fail due to missing DataSetClassId reason.");
                            break;
                    }
                }
            }
        }

        [Test(Description = "Validate missing or wrong DataSetMessage fields definition")]
        public void ValidateMissingDataSetMessagesDefinitions(
            [Values(JsonNetworkMessageContentMask.DataSetMessageHeader, JsonNetworkMessageContentMask.SingleDataSetMessage)]
                JsonNetworkMessageContentMask jsonNetworkMessageContentMask,
            [Values(JsonDataSetMessageContentMask.DataSetWriterId, JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.MetaDataVersion, JsonDataSetMessageContentMask.Timestamp, JsonDataSetMessageContentMask.Status)]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
            [Values(DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData,
                DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType pubSubConfiguration = MessagesHelper.ConfigureDataSetMessages(Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl,
                writerGroupId: 1,
                jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray,
                NamespaceIndexAllTypes);
            Assert.IsNotNull(pubSubConfiguration, "pubSubConfiguration should not be null");

            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(pubSubConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication should not be null");
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            var networkMessages = connection.CreateNetworkMessages(pubSubConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState());
            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");

            // Assert
            // check first consistency of ua-data network messages
            List<JsonNetworkMessage> uaDataNetworkMessages = MessagesHelper.GetJsonUaDataNetworkMessages(networkMessages.Cast<JsonNetworkMessage>().ToList());
            Assert.IsNotNull(uaDataNetworkMessages, "Json ua-data entries are missing from configuration!");

            foreach (JsonNetworkMessage jsonNetworkMessage in uaDataNetworkMessages)
            {
                jsonNetworkMessage.MessageId = "1";
                jsonNetworkMessage.PublisherId = "1";
                jsonNetworkMessage.DataSetClassId = "1";

                foreach (JsonDataSetMessage jsonDataSetMessage in jsonNetworkMessage.DataSetMessages)
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
                            jsonDataSetMessage.MetaDataVersion = new ConfigurationVersionDataType() { MajorVersion = 0, MinorVersion = 0 };
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
                if (failOptions is DataSetMessageFailOptions &&
                   (DataSetMessageFailOptions)failOptions != DataSetMessageFailOptions.Ok)
                {
                    Assert.AreEqual(failOptions, DataSetMessageFailOptions.DataSetWriterId, "ValidateMissingDataSetMessagesFields should fail due to missing DataSetWriterId reason.");
                }
            }
        }
        
        #region Private methods

        /// <summary>
        /// Compare encoded/decoded network messages
        /// </summary>
        /// <param name="jsonNetworkMessage">the message to encode</param>
        private void CompareEncodeDecodeMetaData(JsonNetworkMessage jsonNetworkMessage)
        {
            Assert.IsTrue(jsonNetworkMessage.IsMetaDataMessage, "The received message is not a metadata message");

            byte[] bytes = jsonNetworkMessage.Encode(ServiceMessageContext.GlobalContext);

            JsonNetworkMessage uaNetworkMessageDecoded = new JsonNetworkMessage();
            uaNetworkMessageDecoded.Decode(ServiceMessageContext.GlobalContext, bytes, null);

            Assert.IsTrue(uaNetworkMessageDecoded.IsMetaDataMessage, "The Decode message is not a metadata message");

            Assert.AreEqual(jsonNetworkMessage.WriterGroupId, uaNetworkMessageDecoded.WriterGroupId, "The Decoded WriterId does not match encoded value");

            Assert.IsTrue(Utils.IsEqual(jsonNetworkMessage.DataSetMetaData, uaNetworkMessageDecoded.DataSetMetaData), jsonNetworkMessage.DataSetMetaData.Name + " Decoded metadata is not equal ");

            // validate network message metadata
            ValidateMetaDataEncoding(jsonNetworkMessage);
        }

        /// <summary>
        /// Compare encoded/decoded network messages
        /// </summary>
        /// <param name="jsonNetworkMessage">the message to encode</param>
        /// <param name="dataSetReaders">The list of readers used to decode</param>
        private void CompareEncodeDecode(JsonNetworkMessage jsonNetworkMessage, IList<DataSetReaderDataType> dataSetReaders)
        {
            byte[] bytes = jsonNetworkMessage.Encode(ServiceMessageContext.GlobalContext);

            JsonNetworkMessage uaNetworkMessageDecoded = new JsonNetworkMessage();
            uaNetworkMessageDecoded.Decode(ServiceMessageContext.GlobalContext, bytes, dataSetReaders);

            // compare uaNetworkMessage with uaNetworkMessageDecoded
            CompareData(jsonNetworkMessage, uaNetworkMessageDecoded);

            // validate network message data 
            ValidateDataEncoding(jsonNetworkMessage);
        }

        /// <summary>
        /// Compare network messages options 
        /// </summary>
        /// <param name="jsonNetworkMessageEncode"></param>
        /// <param name="jsonNetworkMessageDecoded"></param>
        /// <returns></returns>
        private void CompareData(JsonNetworkMessage jsonNetworkMessageEncode, JsonNetworkMessage jsonNetworkMessageDecoded)
        {
            JsonNetworkMessageContentMask networkMessageContentMask = jsonNetworkMessageEncode.NetworkMessageContentMask;

            // Verify flags
            if (!jsonNetworkMessageEncode.IsMetaDataMessage)
            {
                Assert.AreEqual(jsonNetworkMessageEncode.NetworkMessageContentMask & jsonNetworkMessageDecoded.NetworkMessageContentMask,
                    jsonNetworkMessageDecoded.NetworkMessageContentMask, "NetworkMessageContentMask were not decoded correctly");
            }

            #region Network Message Header
            if ((networkMessageContentMask & JsonNetworkMessageContentMask.NetworkMessageHeader) != 0)
            {
                if ((networkMessageContentMask & JsonNetworkMessageContentMask.PublisherId) != 0)
                {
                    Assert.AreEqual(jsonNetworkMessageEncode.PublisherId, jsonNetworkMessageDecoded.PublisherId, "PublisherId was not decoded correctly");
                }

                if ((networkMessageContentMask & JsonNetworkMessageContentMask.DataSetClassId) != 0)
                {
                    Assert.AreEqual(jsonNetworkMessageEncode.DataSetClassId, jsonNetworkMessageDecoded.DataSetClassId, "DataSetClassId was not decoded correctly");
                }
            }
            #endregion

            #region Payload header + Payload data
            List<UaDataSetMessage> receivedDataSetMessages = jsonNetworkMessageDecoded.DataSetMessages.ToList();

            Assert.IsNotNull(receivedDataSetMessages, "Received DataSetMessages is null");

            // check the number of JsonDataSetMessage counts
            if ((networkMessageContentMask & JsonNetworkMessageContentMask.SingleDataSetMessage) == 0)
            {
                Assert.AreEqual(jsonNetworkMessageEncode.DataSetMessages.Count,
                    receivedDataSetMessages.Count, "JsonDataSetMessages.Count was not decoded correctly (Count = {0})", receivedDataSetMessages.Count);
            }
            else
            {
                Assert.AreEqual(1, receivedDataSetMessages.Count,
                   "JsonDataSetMessages.Count was not decoded correctly. There is no SingleDataSetMessage (Coount = {0})", receivedDataSetMessages.Count);
            }

            // check if the encoded match the received decoded DataSets
            for (int i = 0; i < receivedDataSetMessages.Count; i++)
            {
                JsonDataSetMessage jsonDataSetMessage = jsonNetworkMessageEncode.DataSetMessages[i] as JsonDataSetMessage;
                Assert.IsNotNull(jsonDataSetMessage, "DataSet [{0}] is missing from publisher datasets!", i);
                // check payload data fields count 
                // get related dataset from subscriber DataSets
                DataSet decodedDataSet = receivedDataSetMessages[i].DataSet;
                Assert.IsNotNull(decodedDataSet, "DataSet '{0}' is missing from subscriber datasets!", jsonDataSetMessage.DataSet.Name);

                Assert.AreEqual(jsonDataSetMessage.DataSet.Fields.Length, decodedDataSet.Fields.Length,
                    "DataSet.Fields.Length was not decoded correctly, DataSetWriterId = {0}", jsonDataSetMessage.DataSetWriterId);

                // check the fields data consistency
                // at this time the DataSetField has just value!?
                for (int index = 0; index < jsonDataSetMessage.DataSet.Fields.Length; index++)
                {
                    Field fieldEncoded = jsonDataSetMessage.DataSet.Fields[index];
                    Field fieldDecoded = decodedDataSet.Fields[index];
                    Assert.IsNotNull(fieldEncoded, "jsonDataSetMessage.DataSet.Fields[{0}] is null,  DataSetWriterId = {1}",
                        index, jsonDataSetMessage.DataSetWriterId);
                    Assert.IsNotNull(fieldDecoded, "jsonDataSetMessageDecoded.DataSet.Fields[{0}] is null,  DataSetWriterId = {1}",
                        index, jsonDataSetMessage.DataSetWriterId);

                    DataValue dataValueEncoded = fieldEncoded.Value;
                    DataValue dataValueDecoded = fieldDecoded.Value;
                    Assert.IsNotNull(fieldEncoded.Value, "jsonDataSetMessage.DataSet.Fields[{0}].Value is null,  DataSetWriterId = {1}",
                       index, jsonDataSetMessage.DataSetWriterId);
                    Assert.IsNotNull(fieldDecoded.Value, "jsonDataSetMessageDecoded.DataSet.Fields[{0}].Value is null,  DataSetWriterId = {1}",
                      index, jsonDataSetMessage.DataSetWriterId);

                    // check dataValues values
                    string fieldName = fieldEncoded.FieldMetaData.Name;

                    ExpandedNodeId encodedExpandedNodeId = dataValueEncoded.Value as ExpandedNodeId;
                    ExpandedNodeId decodedExpandedNodeId = dataValueDecoded.Value as ExpandedNodeId;
                    if (encodedExpandedNodeId != null && !encodedExpandedNodeId.IsAbsolute
                        && decodedExpandedNodeId != null && decodedExpandedNodeId.IsAbsolute)
                    {
                        dataValueDecoded.Value = ExpandedNodeId.ToNodeId(decodedExpandedNodeId, ServiceMessageContext.GlobalContext.NamespaceUris);
                    }

                    Assert.AreEqual(dataValueEncoded.Value, dataValueDecoded.Value,
                        "Wrong: Fields[{0}].DataValue.Value; DataSetWriterId = {1}",
                        fieldName, jsonDataSetMessage.DataSetWriterId);

                    // Checks just for DataValue type only 
                    if ((jsonDataSetMessage.FieldContentMask & DataSetFieldContentMask.StatusCode) ==
                        DataSetFieldContentMask.StatusCode)
                    {
                        // check dataValues StatusCode
                        Assert.AreEqual(dataValueEncoded.StatusCode, dataValueDecoded.StatusCode,
                            "Wrong: Fields[{0}].DataValue.StatusCode; DataSetWriterId = {1}", fieldName, jsonDataSetMessage.DataSetWriterId);
                    }

                    // check dataValues SourceTimestamp
                    if ((jsonDataSetMessage.FieldContentMask & DataSetFieldContentMask.SourceTimestamp) ==
                        DataSetFieldContentMask.SourceTimestamp)
                    {
                        Assert.AreEqual(dataValueEncoded.SourceTimestamp, dataValueDecoded.SourceTimestamp,
                            "Wrong: Fields[{0}].DataValue.SourceTimestamp; DataSetWriterId = {1}", fieldName, jsonDataSetMessage.DataSetWriterId);
                    }

                    // check dataValues ServerTimestamp
                    if ((jsonDataSetMessage.FieldContentMask & DataSetFieldContentMask.ServerTimestamp) ==
                        DataSetFieldContentMask.ServerTimestamp)
                    {
                        // check dataValues ServerTimestamp
                        Assert.AreEqual(dataValueEncoded.ServerTimestamp, dataValueDecoded.ServerTimestamp,
                           "Wrong: Fields[{0}].DataValue.ServerTimestamp; DataSetWriterId = {1}", fieldName, jsonDataSetMessage.DataSetWriterId);
                    }

                    // check dataValues SourcePicoseconds
                    if ((jsonDataSetMessage.FieldContentMask & DataSetFieldContentMask.SourcePicoSeconds) ==
                        DataSetFieldContentMask.SourcePicoSeconds)
                    {
                        Assert.AreEqual(dataValueEncoded.SourcePicoseconds, dataValueDecoded.SourcePicoseconds,
                           "Wrong: Fields[{0}].DataValue.SourcePicoseconds; DataSetWriterId = {1}", fieldName, jsonDataSetMessage.DataSetWriterId);
                    }

                    // check dataValues ServerPicoSeconds
                    if ((jsonDataSetMessage.FieldContentMask & DataSetFieldContentMask.ServerPicoSeconds) ==
                        DataSetFieldContentMask.ServerPicoSeconds)
                    {
                        // check dataValues ServerPicoseconds
                        Assert.AreEqual(dataValueEncoded.ServerPicoseconds, dataValueDecoded.ServerPicoseconds,
                           "Wrong: Fields[{0}].DataValue.ServerPicoseconds; DataSetWriterId = {1}", fieldName, jsonDataSetMessage.DataSetWriterId);
                    }
                }

                if ((networkMessageContentMask & JsonNetworkMessageContentMask.SingleDataSetMessage) != 0)
                {
                    // stop evaluation if there only one dataset
                    break;
                }
            }
            #endregion
        }

        /// <summary>
        /// Validate MetaData(DataSetMetaData) encoding consistency
        /// </summary>
        /// <param name="jsonNetworkMessage"></param>
        private void ValidateMetaDataEncoding(JsonNetworkMessage jsonNetworkMessage)
        {
            MetaDataFailOptions failOptions = VerifyDataSetMetaDataEncoding(jsonNetworkMessage);
            if (failOptions != MetaDataFailOptions.Ok)
            {
                Assert.Fail("The mandatory 'jsonNetworkMessage.{0}' field is wrong or missing from decoded message.", failOptions);
            }
        }

        /// <summary>
        /// Verify DataSetMetaData encoding consistency
        /// </summary>
        /// <param name="jsonNetworkMessage"></param>
        private MetaDataFailOptions VerifyDataSetMetaDataEncoding(JsonNetworkMessage jsonNetworkMessage)
        {
            if (jsonNetworkMessage.DataSetMetaData == null ||
                jsonNetworkMessage.MessageType != MessagesHelper.UaMetaDataMessageType)
            {
                return MetaDataFailOptions.DataSetMetaData | MetaDataFailOptions.MessageType;
            }

            // encode network message
            byte[] networkMessage = jsonNetworkMessage.Encode(ServiceMessageContext.GlobalContext);

            // verify DataSetMetaData encoded consistency
            ServiceMessageContext context = ServiceMessageContext.GlobalContext;

            string messageIdValue = null;
            string messageTypeValue = null;
            string publisherIdValue = null;
            ushort dataSetWriterIdValue = 0;

            object token = null;
            string jsonMessage = System.Text.Encoding.ASCII.GetString(networkMessage);
            using (JsonDecoder jsonDecoder = new JsonDecoder(jsonMessage, context))
            {
                #region Verify DataSetMetaData mandatory fields

                if (jsonDecoder.ReadField(MetaDataMessageId, out token))
                {
                    messageIdValue = jsonDecoder.ReadString(MetaDataMessageId);
                }
                else
                {
                    return MetaDataFailOptions.MessageId;
                }
                Assert.AreEqual(jsonNetworkMessage.MessageId, messageIdValue, "MessageId was not decoded correctly. Encoded: {0} Decoded: {1}", jsonNetworkMessage.MessageId, messageIdValue);

                if (jsonDecoder.ReadField(MetaDataMessageType, out token))
                {
                    messageTypeValue = jsonDecoder.ReadString(MetaDataMessageType);
                }
                else
                {
                    return MetaDataFailOptions.MessageType;
                }
                Assert.AreEqual(jsonNetworkMessage.MessageType, messageTypeValue, "MessageType was not decoded correctly, Encoded: {0} Decoded: {1}", jsonNetworkMessage.MessageType, messageTypeValue);

                if (jsonDecoder.ReadField(MetaDataPublisherId, out token))
                {
                    publisherIdValue = jsonDecoder.ReadString(MetaDataPublisherId);
                }
                else
                {
                    return MetaDataFailOptions.PublisherId;
                }
                Assert.AreEqual(jsonNetworkMessage.PublisherId, publisherIdValue, "PublisherId was not decoded correctly, Encoded: {0} Decoded: {1}", jsonNetworkMessage.PublisherId, publisherIdValue);

                if (jsonDecoder.ReadField(MetaDataDataSetWriterId, out token))
                {
                    dataSetWriterIdValue = jsonDecoder.ReadUInt16(MetaDataDataSetWriterId);
                }
                else
                {
                    return MetaDataFailOptions.DataSetWriterId;
                }
                Assert.AreEqual(jsonNetworkMessage.DataSetWriterId, dataSetWriterIdValue, "DataSetWriterId was not decoded correctly, Encoded: {0} Decoded: {1}", jsonNetworkMessage.DataSetWriterId, dataSetWriterIdValue);

                #region Verify DataSetMetaData.Metadata fields, the Metadata value is also mandatory

                DataSetMetaDataType jsonDataSetMetaData = jsonNetworkMessage.DataSetMetaData;

                DataSetMetaDataType dataSetMetaData = jsonDecoder.ReadEncodeable("MetaData", typeof(DataSetMetaDataType)) as DataSetMetaDataType;
                Assert.IsNotNull(dataSetMetaData, "DataSetMetaData read by json decoder should not be null.");

                if (jsonDataSetMetaData.Name == null)
                {
                    return MetaDataFailOptions.MetaData_Name;
                }
                Assert.AreEqual(jsonNetworkMessage.DataSetMetaData.Name, dataSetMetaData.Name, "DataSetMetaData.Name was not decoded correctly, Encoded: {0} Decoded: {1}", jsonNetworkMessage.DataSetMetaData.Name, dataSetMetaData.Name);
                if (jsonDataSetMetaData.Description == null)
                {
                    return MetaDataFailOptions.MetaData_Description;
                }
                Assert.AreEqual(jsonNetworkMessage.DataSetMetaData.Description, dataSetMetaData.Description, "DataSetMetaData.Description was not decoded correctly, Encoded: {0} Decoded: {1}", jsonNetworkMessage.DataSetMetaData.Description, dataSetMetaData.Description);

                // jsonDataSetMetaData.Fields.Count should be > 0
                if (jsonDataSetMetaData.Fields.Count == 0)
                {
                    return MetaDataFailOptions.MetaData_Fields;
                }
                Assert.AreEqual(jsonNetworkMessage.DataSetMetaData.Fields.Count, dataSetMetaData.Fields.Count, "DataSetMetaData.Fields.Count are not equal, Encoded: {0} Decoded: {1}", jsonNetworkMessage.DataSetMetaData.Fields.Count, dataSetMetaData.Fields.Count);

                foreach (FieldMetaData jsonFieldMetaData in jsonNetworkMessage.DataSetMetaData.Fields)
                {
                    FieldMetaData fieldMetaData = dataSetMetaData.Fields.Find(field => field.Name == jsonFieldMetaData.Name);

                    Assert.IsNotNull(fieldMetaData, "DataSetMetaData.Field - Name: '{0}' read by json decoder not found into decoded DataSetMetaData.Fields collection.", jsonFieldMetaData.Name);
                    Assert.IsTrue(Utils.IsEqual(jsonFieldMetaData, fieldMetaData), "FieldMetaData found in decoded collection is not identical with original one. Encoded: {0} Decoded: {1}",
                        string.Format("Name: {0}, Description: {1}, DataSetFieldId: {2}, BuiltInType: {3}, DataType: {4}, TypeId: {5}",
                            jsonFieldMetaData.Name,
                            jsonFieldMetaData.Description,
                            jsonFieldMetaData.DataSetFieldId,
                            jsonFieldMetaData.BuiltInType,
                            jsonFieldMetaData.DataType,
                            jsonFieldMetaData.TypeId),
                         string.Format("Name: {0}, Description: {1}, DataSetFieldId: {2}, BuiltInType: {3}, DataType: {4}, TypeId: {5}",
                            fieldMetaData.Name,
                            fieldMetaData.Description,
                            fieldMetaData.DataSetFieldId,
                            fieldMetaData.BuiltInType,
                            fieldMetaData.DataType,
                            fieldMetaData.TypeId));
                }

                if (jsonDataSetMetaData.DataSetClassId == Uuid.Empty)
                {
                    return MetaDataFailOptions.MetaData_DataSetClassId;
                }
                Assert.AreEqual(jsonNetworkMessage.DataSetMetaData.DataSetClassId, dataSetMetaData.DataSetClassId, "DataSetMetaData.DataSetClassId was not decoded correctly, Encoded: {0} Decoded: {1}", jsonNetworkMessage.DataSetMetaData.DataSetClassId, dataSetMetaData.DataSetClassId);

                if (jsonDataSetMetaData.ConfigurationVersion.MajorVersion == 0 && jsonDataSetMetaData.ConfigurationVersion.MinorVersion == 0)
                {
                    return MetaDataFailOptions.MetaData_ConfigurationVersion;
                }
                Assert.IsTrue(Utils.IsEqual(jsonNetworkMessage.DataSetMetaData.ConfigurationVersion, dataSetMetaData.ConfigurationVersion), "DataSetMetaData.ConfigurationVersion was not decoded correctly, Encoded: {0} Decoded: {1}",
                    string.Format("MajorVersion: {0}, MinorVersion: {1}", jsonNetworkMessage.DataSetMetaData.ConfigurationVersion.MajorVersion, jsonNetworkMessage.DataSetMetaData.ConfigurationVersion.MinorVersion),
                    string.Format("MajorVersion: {0}, MinorVersion: {1}", dataSetMetaData.ConfigurationVersion.MajorVersion, dataSetMetaData.ConfigurationVersion.MinorVersion));

                #endregion

                #endregion
            }

            return MetaDataFailOptions.Ok;
        }

        /// <summary>
        /// Verify NetworkMessage encoding consistency
        /// </summary>
        /// <param name="jsonNetworkMessage"></param>
        private void ValidateDataEncoding(JsonNetworkMessage jsonNetworkMessage)
        {
            object failOptions = VerifyDataEncoding(jsonNetworkMessage);
            if (failOptions is NetworkMessageFailOptions)
            {
                if ((NetworkMessageFailOptions)failOptions != NetworkMessageFailOptions.Ok)
                {
                    Assert.Fail("The mandatory 'jsonNetworkMessage.{0}' field is wrong or missing from decoded message.", failOptions);
                }
            }
            if (failOptions is DataSetMessageFailOptions)
            {
                if ((DataSetMessageFailOptions)failOptions != DataSetMessageFailOptions.Ok)
                {
                    Assert.Fail("The mandatory 'jsonDataSetMessage.{0}' field is wrong or missing from decoded message.", failOptions);
                }
            }
        }

        /// <summary>
        /// Verify NetworkMessage data encoding consistency
        /// </summary>
        /// <param name="jsonNetworkMessage"></param>
        private object VerifyDataEncoding(JsonNetworkMessage jsonNetworkMessage)
        {
            // encode network message
            byte[] networkMessage = jsonNetworkMessage.Encode(ServiceMessageContext.GlobalContext);

            // verify network message encoded consistency
            ServiceMessageContext context = ServiceMessageContext.GlobalContext;

            string jsonMessage = System.Text.Encoding.ASCII.GetString(networkMessage);
            using (JsonDecoder jsonDecoder = new JsonDecoder(jsonMessage, context))
            {
                if (jsonNetworkMessage.HasNetworkMessageHeader)
                {
                    NetworkMessageFailOptions failOptions = VerifyNetworkMessageEncoding(jsonNetworkMessage, jsonDecoder);
                    if (failOptions != NetworkMessageFailOptions.Ok)
                    {
                        return failOptions;
                    }
                }

                if (jsonNetworkMessage.HasDataSetMessageHeader || jsonNetworkMessage.HasSingleDataSetMessage)
                {
                    DataSetMessageFailOptions failOptions = VerifyDataSetMessagesEncoding(jsonNetworkMessage, jsonDecoder);
                    if (failOptions != DataSetMessageFailOptions.Ok)
                    {
                        return failOptions;
                    }
                }
            }

            return NetworkMessageFailOptions.Ok;
        }

        /// <summary>
        /// Verify NetworkMessage encoding
        /// </summary>
        /// <param name="jsonNetworkMessage"></param>
        /// <param name="jsonDecoder"></param>
        /// <returns></returns>
        private NetworkMessageFailOptions VerifyNetworkMessageEncoding(JsonNetworkMessage jsonNetworkMessage, JsonDecoder jsonDecoder)
        {
            string messageIdValue = null;
            string messageTypeValue = null;
            string publisherIdValue = null;
            string dataSetClassIdValue = null;

            object token = null;

            #region Verify NetworkMessage mandatory fields

            if (jsonDecoder.ReadField(NetworkMessageMessageId, out token))
            {
                messageIdValue = jsonDecoder.ReadString(NetworkMessageMessageId);
            }
            else
            {
                return NetworkMessageFailOptions.MessageId;
            }
            Assert.AreEqual(jsonNetworkMessage.MessageId, messageIdValue, "MessageId was not decoded correctly. Encoded: {0} Decoded: {1}", jsonNetworkMessage.MessageId, messageIdValue);

            if (jsonDecoder.ReadField(NetworkMessageMessageType, out token))
            {
                messageTypeValue = jsonDecoder.ReadString(NetworkMessageMessageType);
            }
            else
            {
                return NetworkMessageFailOptions.MessageType;
            }
            Assert.AreEqual(jsonNetworkMessage.MessageType, messageTypeValue, "MessageType was not decoded correctly, Encoded: {0} Decoded: {1}", jsonNetworkMessage.MessageType, messageTypeValue);

            #endregion Verify NetworkMessage mandatory fields

            #region Verify NetworkMessage optional fields

            if (jsonDecoder.ReadField(NetworkMessagePublisherId, out token))
            {
                publisherIdValue = jsonDecoder.ReadString(NetworkMessagePublisherId);
                Assert.AreEqual(jsonNetworkMessage.PublisherId, publisherIdValue, "PublisherId was not decoded correctly, Encoded: {0} Decoded: {1}", jsonNetworkMessage.PublisherId, publisherIdValue);
            }

            if (jsonDecoder.ReadField(NetworkMessageDataSetClassId, out token))
            {
                dataSetClassIdValue = jsonDecoder.ReadString(NetworkMessageDataSetClassId);
                Assert.AreEqual(jsonNetworkMessage.DataSetClassId, dataSetClassIdValue, "DataSetClassId was not decoded correctly, Encoded: {0} Decoded: {1}", jsonNetworkMessage.PublisherId, publisherIdValue);
            }

            #endregion Verify NetworkMessage optional fields

            return NetworkMessageFailOptions.Ok;
        }

        /// <summary>
        /// Verify DataSetMessage(s) encoding
        /// </summary>
        /// <param name="jsonNetworkMessage"></param>
        /// <param name="jsonDecoder"></param>
        /// <returns></returns>
        private DataSetMessageFailOptions VerifyDataSetMessagesEncoding(JsonNetworkMessage jsonNetworkMessage, JsonDecoder jsonDecoder)
        {
            UInt16 dataSetWriterIdValue = 0;
            UInt32 sequenceNumberValue = 0;
            StatusCode statusValue = StatusCodes.Good;
            FieldTypeEncodingMask fieldTypeEncoding = FieldTypeEncodingMask.Reserved;
            Dictionary<string, object> dataSetPayload = null;
            
            object token = null;
            //object token1 = null;

            object messagesToken = null;
            List<object> messagesList = null;
            string messagesListName = string.Empty;
            if (jsonDecoder.ReadField(NetworkMessageMessages, out messagesToken))
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
            else if (jsonDecoder.ReadField(JsonDecoder.RootArrayName, out messagesToken))
            {
                messagesListName = JsonDecoder.RootArrayName;
            }
            // else this is a SingleDataSetMessage encoded as the content json 
            if (!string.IsNullOrEmpty(messagesListName))
            {
                int index = 0;
                foreach (var uaDataSetMessage in jsonNetworkMessage.DataSetMessages)
                {
                    var jsonDataSetMessage = (JsonDataSetMessage) uaDataSetMessage;
                    if (jsonDataSetMessage.FieldContentMask == DataSetFieldContentMask.None)
                    {
                        fieldTypeEncoding = FieldTypeEncodingMask.Variant;
                    }
                    else if ((jsonDataSetMessage.FieldContentMask & DataSetFieldContentMask.RawData) != 0)
                    {
                        // If the RawData flag is set, all other bits are ignored.
                        // 01 RawData Field Encoding
                        fieldTypeEncoding = FieldTypeEncodingMask.RawData;
                    }
                    else if ((jsonDataSetMessage.FieldContentMask & (DataSetFieldContentMask.StatusCode
                                                  | DataSetFieldContentMask.SourceTimestamp
                                                  | DataSetFieldContentMask.ServerTimestamp
                                                  | DataSetFieldContentMask.SourcePicoSeconds
                                                  | DataSetFieldContentMask.ServerPicoSeconds)) != 0)
                    {
                        // 10 DataValue Field Encoding
                        fieldTypeEncoding = FieldTypeEncodingMask.DataValue;
                    }

                    bool wasPushed = jsonDecoder.PushArray(JsonDecoder.RootArrayName, index++);
                    if (wasPushed)
                    {
                        #region Verify DataSetMessages mandatory fields

                        if (jsonDecoder.ReadField(DataSetMessageDataSetWriterId, out token))
                        {
                            dataSetWriterIdValue = jsonDecoder.ReadUInt16(DataSetMessageDataSetWriterId);
                            Assert.AreEqual(jsonDataSetMessage.DataSetWriterId, dataSetWriterIdValue, "jsonDataSetMessage.DataSetWriterId was not decoded correctly, Encoded: {0} Decoded: {1}", jsonDataSetMessage.DataSetWriterId, dataSetWriterIdValue);
                            if (dataSetWriterIdValue == 0xFF)
                            {
                                return DataSetMessageFailOptions.DataSetWriterId;
                            }
                        }
                        else
                        {
                            if ((jsonDataSetMessage.DataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0)
                            {
                                return DataSetMessageFailOptions.DataSetWriterId;
                            }
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
                                    Assert.IsTrue(dataSetPayload?.Keys.Any(key => key == field.FieldMetaData.Name), "Decoded Field: {0} not found", field.FieldMetaData.Name);
                                    Assert.IsNotNull(dataSetPayload[field.FieldMetaData.Name], "Decoded Field: {0} is not null", field.FieldMetaData.Name);

                                    if (jsonDecoder.ReadField(field.FieldMetaData.Name, out token))
                                    {
                                        switch(fieldTypeEncoding)
                                        {
                                            case FieldTypeEncodingMask.Variant:
                                                decodedFieldValue = jsonDecoder.ReadVariant(field.FieldMetaData.Name);
                                                Assert.IsNotNull(((Variant)decodedFieldValue).Value, "Decoded Field: {0} value should not be null", field.FieldMetaData.Name);
                                                Assert.IsTrue(Utils.IsEqual(field.Value.Value, ((Variant)decodedFieldValue).Value),
                                                     "Decoded Field name: {0} values: encoded {1} - decoded {2}", field.FieldMetaData.Name, field.Value.Value, dataSetPayload[field.FieldMetaData.Name]);
                                                break;
                                            case FieldTypeEncodingMask.RawData:
                                                decodedFieldValue = DecodeFieldData(jsonDecoder, field.FieldMetaData, field.FieldMetaData.Name);
                                                Assert.IsNotNull(decodedFieldValue, "Decoded Field: {0} value should not be null", field.FieldMetaData.Name);
                                                // ExtendedNodeId namespaceIndex workaround issue
                                                if (decodedFieldValue is ExpandedNodeId &&
                                                    !string.IsNullOrEmpty(((ExpandedNodeId)decodedFieldValue).NamespaceUri))
                                                {
                                                    // replace the namespaceUri with namespaceIndex to match the encoded value
                                                    ExpandedNodeId expandedNodeId = Utils.Clone(decodedFieldValue) as ExpandedNodeId;
                                                    Assert.IsNotNull(expandedNodeId, "Decoded 'ExpandedNodeId' Field: {0} should not be null", field.FieldMetaData.Name);
                                                    Assert.IsNotEmpty(expandedNodeId.NamespaceUri, "Decoded 'ExpandedNodeId.NamespaceUri' Field: {0} should not be empty", field.FieldMetaData.Name);

                                                    UInt16 namespaceIndex =
                                                        Convert.ToUInt16(ServiceMessageContext.GlobalContext.NamespaceUris.GetIndex(((ExpandedNodeId)decodedFieldValue).NamespaceUri));

                                                    StringBuilder stringBuilder = new StringBuilder();
                                                    ExpandedNodeId.Format(stringBuilder, expandedNodeId.Identifier, expandedNodeId.IdType, namespaceIndex, string.Empty, expandedNodeId.ServerIndex);
                                                    decodedFieldValue = new ExpandedNodeId(stringBuilder.ToString());
                                                }
                                                Assert.IsTrue(Utils.IsEqual(field.Value.Value, decodedFieldValue),
                                                         "Decoded Field name: {0} values: encoded {1} - decoded {2}", field.FieldMetaData.Name, field.Value.Value, dataSetPayload[field.FieldMetaData.Name]);
                                                break;
                                            case FieldTypeEncodingMask.DataValue:
                                                bool wasPushed2 = jsonDecoder.PushStructure(field.FieldMetaData.Name);
                                                DataValue dataValue = new DataValue(Variant.Null);
                                                try
                                                {
                                                    if (wasPushed2 && jsonDecoder.ReadField("Value", out token))
                                                    {
                                                        // the Value was encoded using the non reversible json encoding 
                                                        token = DecodeFieldData(jsonDecoder, field.FieldMetaData, "Value");
                                                        dataValue = new DataValue(new Variant(token));
                                                    }
                                                    else
                                                    {
                                                        // handle Good StatusCode that was not encoded
                                                        if (field.FieldMetaData.BuiltInType == (byte)BuiltInType.StatusCode)
                                                        {
                                                            dataValue = new DataValue(new Variant(new StatusCode(StatusCodes.Good)));
                                                        }
                                                    }

                                                    if ((jsonDataSetMessage.FieldContentMask & DataSetFieldContentMask.StatusCode) != 0)
                                                    {
                                                        if (jsonDecoder.ReadField("StatusCode", out token))
                                                        {
                                                            bool wasPush3 = jsonDecoder.PushStructure("StatusCode");
                                                            if (wasPush3)
                                                            {
                                                                dataValue.StatusCode = jsonDecoder.ReadStatusCode("Code");
                                                                jsonDecoder.Pop();
                                                            }
                                                        }
                                                    }

                                                    if ((jsonDataSetMessage.FieldContentMask & DataSetFieldContentMask.SourceTimestamp) != 0)
                                                    {
                                                        dataValue.SourceTimestamp = jsonDecoder.ReadDateTime("SourceTimestamp");
                                                    }

                                                    if ((jsonDataSetMessage.FieldContentMask & DataSetFieldContentMask.SourcePicoSeconds) != 0)
                                                    {
                                                        dataValue.SourcePicoseconds = jsonDecoder.ReadUInt16("SourcePicoseconds");
                                                    }

                                                    if ((jsonDataSetMessage.FieldContentMask & DataSetFieldContentMask.ServerTimestamp) != 0)
                                                    {
                                                        dataValue.ServerTimestamp = jsonDecoder.ReadDateTime("ServerTimestamp");
                                                    }

                                                    if ((jsonDataSetMessage.FieldContentMask & DataSetFieldContentMask.ServerPicoSeconds) != 0)
                                                    {
                                                        dataValue.ServerPicoseconds = jsonDecoder.ReadUInt16("ServerPicoseconds");
                                                    }
                                                    Assert.IsNotNull(dataValue.Value, "Decoded Field: {0} value should not be null", field.FieldMetaData.Name);
                                                    // ExtendedNodeId namespaceIndex workaround issue
                                                    if (dataValue.Value is ExpandedNodeId &&
                                                        !string.IsNullOrEmpty(((ExpandedNodeId)dataValue.Value).NamespaceUri))
                                                    {
                                                        // replace the namespaceUri with namespaceIndex to match the encoded value
                                                        ExpandedNodeId expandedNodeId = Utils.Clone(dataValue.Value) as ExpandedNodeId;
                                                        Assert.IsNotNull(expandedNodeId, "Decoded 'ExpandedNodeId' Field: {0} should not be null", field.FieldMetaData.Name);
                                                        Assert.IsNotEmpty(expandedNodeId.NamespaceUri, "Decoded 'ExpandedNodeId.NamespaceUri' Field: {0} should not be empty", field.FieldMetaData.Name);

                                                        UInt16 namespaceIndex =
                                                            Convert.ToUInt16(ServiceMessageContext.GlobalContext.NamespaceUris.GetIndex(((ExpandedNodeId)dataValue.Value).NamespaceUri));

                                                        StringBuilder stringBuilder = new StringBuilder();
                                                        ExpandedNodeId.Format(stringBuilder, expandedNodeId.Identifier, expandedNodeId.IdType, namespaceIndex, string.Empty, expandedNodeId.ServerIndex);
                                                        dataValue.Value = new ExpandedNodeId(stringBuilder.ToString());
                                                    }
                                                    Assert.IsTrue(Utils.IsEqual(field.Value.Value, dataValue.Value),
                                                         "Decoded Field name: {0} values: encoded {1} - decoded {2}", field.FieldMetaData.Name, field.Value.Value, dataSetPayload[field.FieldMetaData.Name]);
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

                        #endregion Verify DataSetMessages mandatory fields

                        #region Verify DataSetMessages optional fields

                        if (jsonDecoder.ReadField(DataSetMessageSequenceNumber, out token))
                        {
                            sequenceNumberValue = jsonDecoder.ReadUInt32(DataSetMessageSequenceNumber);
                            Assert.AreEqual(jsonDataSetMessage.SequenceNumber, sequenceNumberValue, "jsonDataSetMessage.SequenceNumberValue was not decoded correctly, Encoded: {0} Decoded: {1}", jsonDataSetMessage.SequenceNumber, sequenceNumberValue);
                        }

                        if (jsonDecoder.ReadField(DataSetMessageMetaDataVersion, out token))
                        {
                            ConfigurationVersionDataType configurationVersion = jsonDecoder.ReadEncodeable(DataSetMessageMetaDataVersion, typeof(ConfigurationVersionDataType)) as ConfigurationVersionDataType;
                            Assert.IsTrue(Utils.IsEqual(jsonDataSetMessage.MetaDataVersion, configurationVersion), "jsonDataSetMessage.MetaDataVersion was not decoded correctly, Encoded: {0} Decoded: {1}",
                            string.Format("MajorVersion: {0}, MinorVersion: {1}", jsonDataSetMessage.MetaDataVersion.MajorVersion, jsonDataSetMessage.MetaDataVersion.MinorVersion),
                            string.Format("MajorVersion: {0}, MinorVersion: {1}", configurationVersion?.MajorVersion, configurationVersion?.MinorVersion));
                        }

                        if (jsonDecoder.ReadField(DataSetMessageTimestamp, out token))
                        {
                            DateTime timeStampValue = jsonDecoder.ReadDateTime(DataSetMessageTimestamp);
                            Assert.AreEqual(jsonDataSetMessage.Timestamp, timeStampValue, "jsonDataSetMessage.Timestamp was not decoded correctly, Encoded: {0} Decoded: {1}", jsonDataSetMessage.Timestamp, timeStampValue);
                        }

                        if (jsonDecoder.ReadField(DataSetMessageStatus, out token))
                        {
                            statusValue = jsonDecoder.ReadStatusCode(DataSetMessageStatus);
                            Assert.AreEqual(jsonDataSetMessage.Status, statusValue, "jsonDataSetMessage.Timestamp was not decoded correctly, Encoded: {0} Decoded: {1}", jsonDataSetMessage.Status, statusValue);
                        }

                        #endregion Verify DataSetMessages optional fields

                        jsonDecoder.Pop();
                    }
                }
            }

            return DataSetMessageFailOptions.Ok;
        }
                
        /// <summary>
        /// Decode field data
        /// </summary>
        /// <param name="jsonDecoder"></param>
        /// <param name="fieldMetaData"></param>
        /// <param name="fieldName"></param>
        /// <returns></returns>
        private object DecodeFieldData(JsonDecoder jsonDecoder, FieldMetaData fieldMetaData, string fieldName)
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
                        return jsonDecoder.ReadArray(fieldName, fieldMetaData.ValueRank, (BuiltInType)fieldMetaData.BuiltInType);
                    }
                    else
                    {
                        Assert.Warn("JsonDataSetMessage - Decoding ValueRank = {0} not supported yet !!!", fieldMetaData.ValueRank);
                    }
                }
                catch (Exception ex)
                {
                    Assert.Warn("JsonDataSetMessage - Error reading element for RawData. {0}", ex.Message);
                    return (StatusCodes.BadDecodingError);
                }
            }
            return null;
        }

        /// <summary>
        /// Decode field by type
        /// </summary>
        /// <param name="jsonDecoder"></param>
        /// <param name="builtInType"></param>
        /// <param name="fieldName"></param>
        /// <returns></returns>
        private object DecodeFieldByType(JsonDecoder jsonDecoder, byte builtInType, string fieldName)
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
                Assert.Warn("JsonDataSetMessage - Error decoding field {0}", fieldName);
            }

            return null;
        }
        #endregion

    }
}
