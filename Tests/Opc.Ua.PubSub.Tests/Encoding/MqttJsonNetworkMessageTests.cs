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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    [TestFixture(Description = "Tests for Encoding/Decoding of JsonNetworkMessage objects")]
    public class MqttJsonNetworkMessageTests
    {
        private const UInt16 NamespaceIndexAllTypes = 3;
        
        private const string MqttAddressUrl = "mqtt://localhost:1883";


        [OneTimeSetUp()]
        public void MyTestInitialize()
        {
            // add some namespaceUris to be used at encode/decode
            ServiceMessageContext.GlobalContext.NamespaceUris.Append("http://opcfoundation.org/UA/DI/");
            ServiceMessageContext.GlobalContext.NamespaceUris.Append("http://opcfoundation.org/UA/ADI/");
            ServiceMessageContext.GlobalContext.NamespaceUris.Append("http://opcfoundation.org/UA/IA/");
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
            }
        }



        #region Private methods

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

        #endregion

    }
}
