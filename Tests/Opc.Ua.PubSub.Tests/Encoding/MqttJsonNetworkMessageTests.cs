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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
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
            foreach (JsonNetworkMessage uaNetworkMessage in uaDataNetworkMessages)
            {
                CompareEncodeDecode(uaNetworkMessage, dataSetReaders);
            }
            foreach (JsonNetworkMessage uaNetworkMessage in uaMetaDataNetworkMessages)
            {
                CompareEncodeDecode(uaNetworkMessage, dataSetReaders);
                VerifyDataSetMetaDataEncoding(uaNetworkMessage);
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
                uaNetworkMessage.DataSetClassId =  dataSetClassId.ToString();
                uaNetworkMessage.DataSetMessages[0].DataSet.DataSetMetaData.DataSetClassId = (Uuid) dataSetClassId;
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
            foreach (JsonNetworkMessage uaNetworkMessage in uaNetworkMessages)
            {
                CompareEncodeDecode(uaNetworkMessage, dataSetReaders);
            }
            foreach (JsonNetworkMessage uaNetworkMessage in uaMetaDataNetworkMessages)
            {
                CompareEncodeDecode(uaNetworkMessage, dataSetReaders);
                VerifyDataSetMetaDataEncoding(uaNetworkMessage);
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
            foreach (JsonNetworkMessage uaNetworkMessage in uaNetworkMessages)
            {
                CompareEncodeDecode(uaNetworkMessage, dataSetReaders);
            }
            foreach (JsonNetworkMessage uaNetworkMessage in uaMetaDataNetworkMessages)
            {
                CompareEncodeDecode(uaNetworkMessage, dataSetReaders);
                VerifyDataSetMetaDataEncoding(uaNetworkMessage);
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
            foreach(JsonNetworkMessage uaNetworkMessage in uaNetworkMessages)
            {
                CompareEncodeDecode(uaNetworkMessage, dataSetReaders);
            }
            foreach (JsonNetworkMessage uaNetworkMessage in uaMetaDataNetworkMessages)
            {
                CompareEncodeDecode(uaNetworkMessage, dataSetReaders);
                VerifyDataSetMetaDataEncoding(uaNetworkMessage);
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
                CompareEncodeDecode(uaMetaDataNetworkMessage as JsonNetworkMessage, new List<DataSetReaderDataType>() { dataSetReaders[index++] });
                VerifyDataSetMetaDataEncoding(uaMetaDataNetworkMessage);
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
                VerifyDataSetMetaDataEncoding(uaMetaDataNetworkMessage);
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
            // arrange
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

            Assert.IsTrue(faultIndex < 0, "publishingInterval={0}, maxDeviation={1}, publishTimeInSecods={2}, deviation[{3}] = {4} has maximum deviation", metaDataUpdateTime, maxDeviation, publishTimeInSeconds, faultIndex, faultDeviation);
        }


        // todo: tests for encoding metadata of jsonNetworkMessage - test all mandatory parameters
        // Add tests which have incomplete metadata info
        [Test(Description = "Validate metadata with missing MessageId")]
        public void ValidateMissingMessageId()
        {
            DataSetMetaDataType metaDataType = MessagesHelper.CreateDataSetMetaData1("DataSet1");
            WriterGroupDataType writerGroup = MessagesHelper.CreateWriterGroup(1, new WriterGroupMessageDataType(), new WriterGroupTransportDataType());

            DataSetMetaDataType metadata = MessagesHelper.CreateDataSetMetaData(dataSetName: "Test missing MessageId", NamespaceIndexAllTypes, metaDataType.Fields);
            metadata.Description = new LocalizedText("Description text");
            metadata.DataSetClassId = new Uuid();
            metadata.ConfigurationVersion = new ConfigurationVersionDataType() { MajorVersion = 2, MinorVersion = 2 };

            JsonNetworkMessage jsonNetworkMessage = new JsonNetworkMessage(writerGroup, metadata);
            jsonNetworkMessage.MessageId = null; // error is here
            jsonNetworkMessage.PublisherId = "1";
            jsonNetworkMessage.DataSetWriterId = 1;

            VerifyDataSetMetaDataEncoding(jsonNetworkMessage);
        }

        [Test(Description = "Validate metadata with missing MessageType")]
        public void ValidateMissingMessageType()
        {
            DataSetMetaDataType metaDataType = MessagesHelper.CreateDataSetMetaData1("DataSet1");
            WriterGroupDataType writerGroup = MessagesHelper.CreateWriterGroup(1, new WriterGroupMessageDataType(), new WriterGroupTransportDataType());

            DataSetMetaDataType metadata = MessagesHelper.CreateDataSetMetaData(dataSetName: "Test missing MessageType", NamespaceIndexAllTypes, metaDataType.Fields);
            metadata.Description = new LocalizedText("Description text");
            metadata.DataSetClassId = new Uuid();
            metadata.ConfigurationVersion = new ConfigurationVersionDataType() { MajorVersion = 2, MinorVersion = 2 };

            JsonNetworkMessage jsonNetworkMessage = new JsonNetworkMessage(); // do not pass metadata - then is ua-data type
            jsonNetworkMessage.MessageId = "1"; 
            jsonNetworkMessage.PublisherId = "1";
            jsonNetworkMessage.DataSetWriterId = 1;

            VerifyDataSetMetaDataEncoding(jsonNetworkMessage);
        }

        #region Private methods

        /// <summary>
        /// Compare encoded/decoded network messages
        /// </summary>
        /// <param name="jsonNetworkMessage">the message to encode</param>
        private void CompareEncodeDecodeMetaData(JsonNetworkMessage jsonNetworkMessage)
        {
            Assert.IsTrue(jsonNetworkMessage.IsMetaDataMessage, "The received message is not a metadata message");

            byte[] bytes = jsonNetworkMessage.Encode();

            JsonNetworkMessage uaNetworkMessageDecoded = new JsonNetworkMessage();
            uaNetworkMessageDecoded.Decode(ServiceMessageContext.GlobalContext, bytes, null);

            Assert.IsTrue(uaNetworkMessageDecoded.IsMetaDataMessage, "The Decode message is not a metadata message");

            Assert.AreEqual(jsonNetworkMessage.WriterGroupId, uaNetworkMessageDecoded.WriterGroupId, "The Decoded WriterId does not match encoded value");

            Assert.IsTrue(Utils.IsEqual(jsonNetworkMessage.DataSetMetaData, uaNetworkMessageDecoded.DataSetMetaData), jsonNetworkMessage.DataSetMetaData.Name+ " Decoded metadata is not equal ");
            
        }

        /// <summary>
        /// Compare encoded/decoded network messages
        /// </summary>
        /// <param name="jsonNetworkMessage">the message to encode</param>
        /// <param name="dataSetReaders">The list of readers used to decode</param>
        private void CompareEncodeDecode(JsonNetworkMessage jsonNetworkMessage, IList<DataSetReaderDataType> dataSetReaders)
        {
            byte[] bytes = jsonNetworkMessage.Encode();

            JsonNetworkMessage uaNetworkMessageDecoded = new JsonNetworkMessage();
            uaNetworkMessageDecoded.Decode(ServiceMessageContext.GlobalContext, bytes, dataSetReaders);

            // compare uaNetworkMessage with uaNetworkMessageDecoded
            CompareData(jsonNetworkMessage, uaNetworkMessageDecoded);
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
            for(int i =0; i < receivedDataSetMessages.Count; i++)
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
        /// Verify DataSetMetaData encoding consistency
        /// </summary>
        /// <param name="jsonNetworkMessage"></param>
        /// <param name="dataSetReaders"></param>
        private void VerifyDataSetMetaDataEncoding(JsonNetworkMessage jsonNetworkMessage)
        {
            Assert.IsTrue(jsonNetworkMessage.IsMetaDataMessage, "JsonNetworkMessage is not MetaData message type");

            // encode network message
            byte[] networkMessage = jsonNetworkMessage.Encode();

            // verify DataSetMetaData encoded consistency
            ServiceMessageContext context = new ServiceMessageContext {
                NamespaceUris = ServiceMessageContext.GlobalContext.NamespaceUris,
                ServerUris = ServiceMessageContext.GlobalContext.ServerUris
            };

            string messageIdValue = "";
            string messageTypeValue = "";
            string publisherIdValue = "";
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
                    Assert.Fail("The mandatory 'MetaData.MessageId' field is missing from decoded message.");
                }
                Assert.AreEqual(jsonNetworkMessage.MessageId, messageIdValue, "MessageId was not decoded correctly. Encoded: {0} Decoded: {1}", jsonNetworkMessage.MessageId, messageIdValue);

                if (jsonDecoder.ReadField(MetaDataMessageType, out token))
                {
                    messageTypeValue = jsonDecoder.ReadString(MetaDataMessageType);
                }
                else
                {
                    Assert.Fail("The mandatory 'MetaData.MessageType' field is missing from decoded message.");
                }
                Assert.AreEqual(jsonNetworkMessage.MessageType, messageTypeValue, "MessageType was not decoded correctly, Encoded: {0} Decoded: {1}", jsonNetworkMessage.MessageType, messageTypeValue);

                if (jsonDecoder.ReadField(MetaDataPublisherId, out token))
                {
                    publisherIdValue = jsonDecoder.ReadString(MetaDataPublisherId);
                }
                else
                {
                    Assert.Fail("The mandatory 'MetaData.PublisherId' field is missing from decoded message.");
                }
                Assert.AreEqual(jsonNetworkMessage.PublisherId, publisherIdValue, "PublisherId was not decoded correctly, Encoded: {0} Decoded: {1}", jsonNetworkMessage.PublisherId, publisherIdValue);

                if (jsonDecoder.ReadField(MetaDataDataSetWriterId, out token))
                {
                    dataSetWriterIdValue = jsonDecoder.ReadUInt16(MetaDataDataSetWriterId);
                }
                else
                {
                    Assert.Fail("The mandatory 'MetaData.DataSetWriterId' field is missing from decoded message.");
                }
                Assert.AreEqual(jsonNetworkMessage.DataSetWriterId, dataSetWriterIdValue, "DataSetWriterId was not decoded correctly, Encoded: {0} Decoded: {1}", jsonNetworkMessage.DataSetWriterId, dataSetWriterIdValue);

                #region Verify DataSetMetaData.Metadata fields, the Metadata value is also mandatory

                Assert.IsNotNull(jsonNetworkMessage.DataSetMetaData, "jsonNetworkMessage.DataSetMetaData should not be null.");
                DataSetMetaDataType jsonDataSetMetaData = jsonNetworkMessage.DataSetMetaData;

                DataSetMetaDataType dataSetMetaData = jsonDecoder.ReadEncodeable("MetaData", typeof(DataSetMetaDataType)) as DataSetMetaDataType;
                Assert.IsNotNull(dataSetMetaData, "DataSetMetaData read by json decoder should not be null.");

                Assert.AreEqual(jsonNetworkMessage.DataSetMetaData.Name, dataSetMetaData.Name, "DataSetMetaData.Name was not decoded correctly, Encoded: {0} Decoded: {1}", jsonNetworkMessage.DataSetMetaData.Name, dataSetMetaData.Name);
                Assert.AreEqual(jsonNetworkMessage.DataSetMetaData.Description, dataSetMetaData.Description, "DataSetMetaData.Description was not decoded correctly, Encoded: {0} Decoded: {1}", jsonNetworkMessage.DataSetMetaData.Description, dataSetMetaData.Description);

                Assert.AreEqual(jsonNetworkMessage.DataSetMetaData.Fields.Count, dataSetMetaData.Fields.Count, "DataSetMetaData.Fields.Count are not equal, Encoded: {0} Decoded: {1}", jsonNetworkMessage.DataSetMetaData.Fields.Count, dataSetMetaData.Fields.Count);
                
                foreach(FieldMetaData jsonFieldMetaData in jsonNetworkMessage.DataSetMetaData.Fields)
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
                
                Assert.AreEqual(jsonNetworkMessage.DataSetMetaData.DataSetClassId, dataSetMetaData.DataSetClassId, "DataSetMetaData.DataSetClassId was not decoded correctly, Encoded: {0} Decoded: {1}", jsonNetworkMessage.DataSetMetaData.DataSetClassId, dataSetMetaData.DataSetClassId);
                Assert.IsTrue(Utils.IsEqual(jsonNetworkMessage.DataSetMetaData.ConfigurationVersion, dataSetMetaData.ConfigurationVersion), "DataSetMetaData.ConfigurationVersion was not decoded correctly, Encoded: {0} Decoded: {1}",
                    string.Format("MajorVersion: {0}, MinorVersion: {1}", jsonNetworkMessage.DataSetMetaData.ConfigurationVersion.MajorVersion, jsonNetworkMessage.DataSetMetaData.ConfigurationVersion.MinorVersion),
                    string.Format("MajorVersion: {0}, MinorVersion: {1}", dataSetMetaData.ConfigurationVersion.MajorVersion, dataSetMetaData.ConfigurationVersion.MinorVersion));

                #endregion

                #endregion
            }
        }

        #endregion

    }
}
