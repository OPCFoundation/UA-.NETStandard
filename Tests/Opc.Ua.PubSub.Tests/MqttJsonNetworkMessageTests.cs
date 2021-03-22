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
using System.Xml;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;

namespace Opc.Ua.PubSub.Tests
{
    [TestFixture(Description = "Tests for Encoding/Decoding of JsonNetworkMessage objects")]
    public class MqttJsonNetworkMessageTests
    {
        private const UInt16 NamespaceIndexAllTypes = 3;
        
        private const string MqttAddressUrl = "mqtt://localhost:1883";
       
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
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            JsonNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                JsonNetworkMessage;
            // set PublisherId
            uaNetworkMessage.PublisherId = publisherId.ToString();

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
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders);
        }

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
            // Arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.DataSetClassId;

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
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            JsonNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                JsonNetworkMessage;
            // set DataSetClassId
            uaNetworkMessage.DataSetClassId = Guid.NewGuid().ToString();

            bool hasDataSetWriterId = (jsonNetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0
                && (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: null, writerGroupId: 1, setDataSetWriterId: hasDataSetWriterId, // the writerheader is saved
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
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders);
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
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            JsonNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                JsonNetworkMessage;

            bool hasDataSetWriterId = (jsonNetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0
                && (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: null, writerGroupId: 1, setDataSetWriterId: hasDataSetWriterId, // the writerheader is saved
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
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders);
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
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            JsonNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                JsonNetworkMessage;

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
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders);
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
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            JsonNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                JsonNetworkMessage;

            bool hasDataSetWriterId = (jsonNetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0
                && (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: null, writerGroupId: 1, setDataSetWriterId: hasDataSetWriterId, // the writerheader is saved
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
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders);
        }

        [Test(Description = "Validate SingleDataSetMessage with parameters for DataSetFieldContentMask, JsonDataSetMessageContentMask and JsonNetworkMessageContentMask")]
        public void ValidateSingleDataSetMessageWithParameters(
            [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
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
            // mark singledataset message
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
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            JsonNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                JsonNetworkMessage;

            bool hasDataSetWriterId = (jsonNetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0
                && (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: null, writerGroupId: 1, setDataSetWriterId: hasDataSetWriterId, // no headers hence the values
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: new DataSetMetaDataType[] { dataSetMetaDataArray[0] }, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");
            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");

            // Assert
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders);
        }

        #region Private methods

        /// <summary>
        /// Load publishing data
        /// </summary>
        /// <param name="pubSubApplication"></param>
        private void LoadData(UaPubSubApplication pubSubApplication)
        {
            Assert.IsNotNull(pubSubApplication, "pubSubApplication should not be null");

            #region DataSet AllTypes
            // DataSet 'AllTypes' fill with data
            DataValue boolToggle = new DataValue(new Variant(false));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("BoolToggle", NamespaceIndexAllTypes), Attributes.Value, boolToggle);
            DataValue byteValue = new DataValue(new Variant((byte)10));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Byte", NamespaceIndexAllTypes), Attributes.Value, byteValue);
            DataValue int16Value = new DataValue(new Variant((short)100));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Int16", NamespaceIndexAllTypes), Attributes.Value, int16Value);
            DataValue int32Value = new DataValue(new Variant((int)1000));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Int32", NamespaceIndexAllTypes), Attributes.Value, int32Value);
            DataValue int64Value = new DataValue(new Variant((Int64)10000));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Int64", NamespaceIndexAllTypes), Attributes.Value, int64Value);
            DataValue sByteValue = new DataValue(new Variant((sbyte)11));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("SByte", NamespaceIndexAllTypes), Attributes.Value, sByteValue);
            DataValue uInt16Value = new DataValue(new Variant((ushort)110));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("UInt16", NamespaceIndexAllTypes), Attributes.Value, uInt16Value);
            DataValue uInt32Value = new DataValue(new Variant((uint)1100));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("UInt32", NamespaceIndexAllTypes), Attributes.Value, uInt32Value);
            DataValue uInt64Value = new DataValue(new Variant((UInt64)11100));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("UInt64", NamespaceIndexAllTypes), Attributes.Value, uInt64Value);
            DataValue floatValue = new DataValue(new Variant((float)1100.5));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Float", NamespaceIndexAllTypes), Attributes.Value, floatValue);
            DataValue doubleValue = new DataValue(new Variant((double)1100));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Double", NamespaceIndexAllTypes), Attributes.Value, doubleValue);
            DataValue stringValue = new DataValue(new Variant("String info"));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("String", NamespaceIndexAllTypes), Attributes.Value, stringValue);
            DataValue dateTimeVal = new DataValue(new Variant(DateTime.UtcNow));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DateTime", NamespaceIndexAllTypes), Attributes.Value, dateTimeVal);
            DataValue guidValue = new DataValue(new Variant(new Guid()));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Guid", NamespaceIndexAllTypes), Attributes.Value, guidValue);
            DataValue byteStringValue = new DataValue(new Variant(new byte[] { 1, 2, 3 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ByteString", NamespaceIndexAllTypes), Attributes.Value, byteStringValue);
            XmlDocument document = new XmlDocument();
            XmlElement xmlElement = document.CreateElement("test");
            xmlElement.InnerText = "Text";
            DataValue xmlElementValue = new DataValue(new Variant(xmlElement));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("XmlElement", NamespaceIndexAllTypes), Attributes.Value, xmlElementValue);
            DataValue nodeIdValue = new DataValue(new Variant(new NodeId(30, 1)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("NodeId", NamespaceIndexAllTypes), Attributes.Value, nodeIdValue);
            DataValue expandedNodeId = new DataValue(new Variant(new ExpandedNodeId(30, 1)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ExpandedNodeId", NamespaceIndexAllTypes), Attributes.Value, expandedNodeId);
            DataValue statusCode = new DataValue(new Variant(new StatusCode(StatusCodes.Good)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("StatusCode", NamespaceIndexAllTypes), Attributes.Value, statusCode);
            DataValue qualifiedValue = new DataValue(new Variant(new QualifiedName("wererwerw")));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("QualifiedName", NamespaceIndexAllTypes), Attributes.Value, qualifiedValue);
            DataValue localizedTextValue = new DataValue(new Variant(new LocalizedText("Localized_abcd")));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("LocalizedText", NamespaceIndexAllTypes), Attributes.Value, localizedTextValue);
            DataValue dataValue = new DataValue(new Variant(new DataValue(new Variant("DataValue_info"), StatusCodes.BadBoundNotFound)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DataValue", NamespaceIndexAllTypes), Attributes.Value, dataValue);
            DataValue diagnosticInfoValue = new DataValue(new Variant(new DiagnosticInfo(1, 1, 1, 1, "Diagnostic_info")));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DiagnosticInfo", NamespaceIndexAllTypes), Attributes.Value, diagnosticInfoValue);

            // creation of Structure by using complex type in JsonEncoderTests.cs sample

            // DataSet 'AllTypes' fill with data array
            DataValue boolToggleArray = new DataValue(new Variant(new BooleanCollection() { true, false, true }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("BoolToggleArray", NamespaceIndexAllTypes), Attributes.Value, boolToggleArray);
            DataValue byteValueArray = new DataValue(new Variant(new byte[] { 127, 101, 1 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ByteArray", NamespaceIndexAllTypes), Attributes.Value, byteValueArray);
            DataValue int16ValueArray = new DataValue(new Variant(new Int16Collection() { -100, -200, 300 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Int16Array", NamespaceIndexAllTypes), Attributes.Value, int16ValueArray);
            DataValue int32ValueArray = new DataValue(new Variant(new Int32Collection() { -1000, -2000, 3000 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Int32Array", NamespaceIndexAllTypes), Attributes.Value, int32ValueArray);
            DataValue int64ValueArray = new DataValue(new Variant(new Int64Collection() { -10000, -20000, 30000 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Int64Array", NamespaceIndexAllTypes), Attributes.Value, int64ValueArray);
            DataValue sByteValueArray = new DataValue(new Variant(new SByteCollection() { 1, -2, -3 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("SByteArray", NamespaceIndexAllTypes), Attributes.Value, sByteValueArray);
            DataValue uInt16ValueArray = new DataValue(new Variant(new UInt16Collection() { 110, 120, 130 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("UInt16Array", NamespaceIndexAllTypes), Attributes.Value, uInt16ValueArray);
            DataValue uInt32ValueArray = new DataValue(new Variant(new UInt32Collection() { 1100, 1200, 1300 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("UInt32Array", NamespaceIndexAllTypes), Attributes.Value, uInt32ValueArray);
            DataValue uInt64ValueArray = new DataValue(new Variant(new UInt64Collection() { 11100, 11200, 11300 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("UInt64Array", NamespaceIndexAllTypes), Attributes.Value, uInt64ValueArray);
            DataValue floatValueArray = new DataValue(new Variant(new FloatCollection() { 1100,5, 1200,5, 1300,5 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("FloatArray", NamespaceIndexAllTypes), Attributes.Value, floatValueArray);
            DataValue doubleValueArray = new DataValue(new Variant(new DoubleCollection() { 11000.5, 12000.6, 13000.7 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DoubleArray", NamespaceIndexAllTypes), Attributes.Value, doubleValueArray);
            DataValue stringValueArray = new DataValue(new Variant(new StringCollection() { "1a", "2b", "3c" }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("StringArray", NamespaceIndexAllTypes), Attributes.Value, stringValueArray);
            DataValue dateTimeValArray = new DataValue(new Variant(new DateTimeCollection() { new DateTime(2020, 3, 11).ToUniversalTime(), new DateTime(2021, 2, 17).ToUniversalTime() }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DateTimeArray", NamespaceIndexAllTypes), Attributes.Value, dateTimeValArray);
            DataValue guidValueArray = new DataValue(new Variant(new UuidCollection() { new Uuid(new Guid()), new Uuid(new Guid()) }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("GuidArray", NamespaceIndexAllTypes), Attributes.Value, guidValueArray);
            DataValue byteStringValueArray = new DataValue(new Variant(new ByteStringCollection() { new byte[] { 1, 2, 3 }, new byte[] { 5, 6, 7 } }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ByteStringArray", NamespaceIndexAllTypes), Attributes.Value, byteStringValueArray);
            XmlDocument document1 = new XmlDocument();
            XmlElement xmlElement1 = document.CreateElement("test1");
            xmlElement1.InnerText = "Text_2";
            XmlDocument document2 = new XmlDocument();
            XmlElement xmlElement2 = document.CreateElement("test2");
            xmlElement2.InnerText = "Text_2";
            DataValue xmlElementValueArray = new DataValue(new Variant(new XmlElementCollection() { xmlElement1, xmlElement2 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("XmlElementArray", NamespaceIndexAllTypes), Attributes.Value, xmlElementValueArray);
            DataValue nodeIdValueArray = new DataValue(new Variant(new NodeIdCollection() { new NodeId(30, 1), new NodeId(20, 3) }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("NodeIdArray", NamespaceIndexAllTypes), Attributes.Value, nodeIdValueArray);
            DataValue expandedNodeIdArray = new DataValue(new Variant(new ExpandedNodeIdCollection() { new ExpandedNodeId(50, 1), new ExpandedNodeId(70, 9) }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ExpandedNodeIdArray", NamespaceIndexAllTypes), Attributes.Value, expandedNodeIdArray);
            DataValue statusCodeArray = new DataValue(new Variant(new StatusCodeCollection() { StatusCodes.Good, StatusCodes.Bad, StatusCodes.Uncertain }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("StatusCodeArray", NamespaceIndexAllTypes), Attributes.Value, statusCodeArray);
            DataValue qualifiedValueArray = new DataValue(new Variant(new QualifiedNameCollection() { new QualifiedName("123"), new QualifiedName("abc") }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("QualifiedNameArray", NamespaceIndexAllTypes), Attributes.Value, qualifiedValueArray);
            DataValue localizedTextValueArray = new DataValue(new Variant(new LocalizedTextCollection() { new LocalizedText("1234"), new LocalizedText("abcd") }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("LocalizedTextArray", NamespaceIndexAllTypes), Attributes.Value, localizedTextValueArray);
            DataValue dataValueArray = new DataValue(new Variant(new DataValueCollection() { new DataValue(new Variant("DataValue_info1"), StatusCodes.BadBoundNotFound), new DataValue(new Variant("DataValue_info2"), StatusCodes.BadNoData) }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DataValueArray", NamespaceIndexAllTypes), Attributes.Value, dataValueArray);
            DataValue diagnosticInfoValueArray = new DataValue(new Variant(new DiagnosticInfoCollection() { new DiagnosticInfo(1, 1, 1, 1, "Diagnostic_info1"), new DiagnosticInfo(2, 2, 2, 2, "Diagnostic_info2") }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DiagnosticInfoArray", NamespaceIndexAllTypes), Attributes.Value, diagnosticInfoValueArray);


            // DataSet 'AllTypes' fill with data as matrix
            DataValue boolToggleMatrix = new DataValue(new Variant(new BooleanCollection() { true, false, true }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("BoolToggleMatrix", NamespaceIndexAllTypes), Attributes.Value, boolToggleMatrix);
            DataValue byteValueMatrix = new DataValue(new Variant(new byte[,] { { 127, 128 }, { 101, 102 } }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ByteMatrix", NamespaceIndexAllTypes), Attributes.Value, byteValueMatrix);
            DataValue int16ValueMatrix = new DataValue(new Variant(new Int16[,] { { -100, -101 }, { -200, -201 } }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Int16Matrix", NamespaceIndexAllTypes), Attributes.Value, int16ValueMatrix);
            DataValue int32ValueMatrix = new DataValue(new Variant(new Int32[,] { { -1000, -1001 }, { -2000, -2001 } }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Int32Matrix", NamespaceIndexAllTypes), Attributes.Value, int32ValueMatrix);
            DataValue int64ValueMatrix = new DataValue(new Variant(new Int64[,] { { -10000, -10001 }, { -20000, -20001 } }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Int64Matrix", NamespaceIndexAllTypes), Attributes.Value, int64ValueMatrix);
            DataValue sByteValueMatrix = new DataValue(new Variant(new SByte[,] { { 1, 2 }, { -2, -3 } }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("SByteMatrix", NamespaceIndexAllTypes), Attributes.Value, sByteValueMatrix);
            DataValue uInt16ValueMatrix = new DataValue(new Variant(new UInt16[,] { { 110, 120 }, { 130, 140 } }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("UInt16Matrix", NamespaceIndexAllTypes), Attributes.Value, uInt16ValueMatrix);
            DataValue uInt32ValueMatrix = new DataValue(new Variant(new UInt32[,] { { 1100, 1200 }, { 1300, 1400 } }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("UInt32Matrix", NamespaceIndexAllTypes), Attributes.Value, uInt32ValueMatrix);
            DataValue uInt64ValueMatrix = new DataValue(new Variant(new UInt64[,] { { 11100, 11200 },{ 11300, 11400 } }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("UInt64Matrix", NamespaceIndexAllTypes), Attributes.Value, uInt64ValueMatrix);
            DataValue floatValueMatrix = new DataValue(new Variant(new float[,] { { 1100, 5 }, { 1200, 7 } }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("FloatMatrix", NamespaceIndexAllTypes), Attributes.Value, floatValueMatrix);
            DataValue doubleValueMatrix = new DataValue(new Variant(new Double[,] { { 11000.5, 12000.6 }, { 13000.7, 14000.8 } }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DoubleMatrix", NamespaceIndexAllTypes), Attributes.Value, doubleValueMatrix);
            DataValue stringValueMatrix = new DataValue(new Variant(new String[,] { { "1a", "2b" }, { "3c", "4d" } }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("StringMatrix", NamespaceIndexAllTypes), Attributes.Value, stringValueMatrix);
            DataValue dateTimeValMatrix = new DataValue(new Variant(new DateTime[,]
            {
                { new DateTime(2020, 3, 11).ToUniversalTime(), new DateTime(2021, 2, 17).ToUniversalTime() },
                { new DateTime(2021, 5, 21).ToUniversalTime(), new DateTime(2020, 7, 23).ToUniversalTime() }
            }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DateTimeMatrix", NamespaceIndexAllTypes), Attributes.Value, dateTimeValMatrix);
            DataValue guidValueMatrix = new DataValue(new Variant(new Uuid[,]
            {
               { new Uuid(new Guid()), new Uuid(new Guid()) },
               { new Uuid(new Guid()), new Uuid(new Guid()) }
            }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("GuidMatrix", NamespaceIndexAllTypes), Attributes.Value, guidValueMatrix);
            DataValue byteStringValueMatrix = new DataValue(new Variant(new byte[,] { { 1, 2, 3 } , { 4, 5, 6 }  }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ByteStringMatrix", NamespaceIndexAllTypes), Attributes.Value, byteStringValueMatrix);
            XmlDocument document1m = new XmlDocument();
            XmlElement xmlElement1m = document.CreateElement("test1m");
            xmlElement1m.InnerText = "Text_1m";
            XmlDocument document2m = new XmlDocument();
            XmlElement xmlElement2m = document.CreateElement("test2m");
            xmlElement2m.InnerText = "Text_2m";
            XmlDocument document3m = new XmlDocument();
            XmlElement xmlElement3m = document.CreateElement("test3m");
            xmlElement3m.InnerText = "Text_3m";
            XmlDocument document4m = new XmlDocument();
            XmlElement xmlElement4m = document.CreateElement("test4m");
            xmlElement4m.InnerText = "Text_4m";
            DataValue xmlElementValueMatrix = new DataValue(new Variant(new XmlElement[,] { { xmlElement1m, xmlElement2m }, { xmlElement3m, xmlElement4m } }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("XmlElementMatrix", NamespaceIndexAllTypes), Attributes.Value, xmlElementValueMatrix);
            DataValue nodeIdValueMatrix = new DataValue(new Variant(new NodeId[,] { { new NodeId(30, 1), new NodeId(20, 3) }, { new NodeId(10, 3), new NodeId(50, 7) } }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("NodeIdMatrix", NamespaceIndexAllTypes), Attributes.Value, nodeIdValueMatrix);
            DataValue expandedNodeIdMatrix = new DataValue(new Variant(new ExpandedNodeId[,]
            {
                { new ExpandedNodeId(50, 1), new ExpandedNodeId(70, 9) },
                { new ExpandedNodeId(30, 2), new ExpandedNodeId(80, 3) },
            }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ExpandedNodeIdMatrix", NamespaceIndexAllTypes), Attributes.Value, expandedNodeIdMatrix);
            DataValue statusCodeMatrix = new DataValue(new Variant(new StatusCode[,]
            {
                { StatusCodes.Good, StatusCodes.Uncertain },
                { StatusCodes.BadCertificateInvalid, StatusCodes.Uncertain }
            }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("StatusCodeMatrix", NamespaceIndexAllTypes), Attributes.Value, statusCodeMatrix);
            DataValue qualifiedValueMatrix = new DataValue(new Variant(new QualifiedName[,]
            {
                { new QualifiedName("123"), new QualifiedName("abc") },
                { new QualifiedName("456"), new QualifiedName("xyz") }
            }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("QualifiedNameMatrix", NamespaceIndexAllTypes), Attributes.Value, qualifiedValueMatrix);
            DataValue localizedTextValueMatrix = new DataValue(new Variant(new LocalizedText[,]
            {
                {new LocalizedText("1234"), new LocalizedText("abcd") },
                {new LocalizedText("5678"), new LocalizedText("efgh") }
            }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("LocalizedTextMatrix", NamespaceIndexAllTypes), Attributes.Value, localizedTextValueMatrix);
            DataValue dataValueMatrix = new DataValue(new Variant(new DataValue[,]
            {
                { new DataValue(new Variant("DataValue_info1"), StatusCodes.BadBoundNotFound), new DataValue(new Variant("DataValue_info2"), StatusCodes.BadNoData) },
                { new DataValue(new Variant("DataValue_info3"), StatusCodes.BadCertificateInvalid), new DataValue(new Variant("DataValue_info4"), StatusCodes.GoodCallAgain) },
            }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DataValueMatrix", NamespaceIndexAllTypes), Attributes.Value, dataValueMatrix);
            DataValue diagnosticInfoValueMatrix = new DataValue(new Variant(new DiagnosticInfo[,]
            {
                { new DiagnosticInfo(1, 1, 1, 1, "Diagnostic_info1"), new DiagnosticInfo(2, 2, 2, 2, "Diagnostic_info2") },
                { new DiagnosticInfo(3, 3, 3, 3, "Diagnostic_info3"), new DiagnosticInfo(4, 4, 4, 4, "Diagnostic_info4") },
            }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DiagnosticInfoMatrix", NamespaceIndexAllTypes), Attributes.Value, diagnosticInfoValueMatrix);
            #endregion
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
            uaNetworkMessageDecoded.Decode(bytes, dataSetReaders);

            // compare uaNetworkMessage with uaNetworkMessageDecoded
            Compare(jsonNetworkMessage, uaNetworkMessageDecoded);
        }

        /// <summary>
        /// Compare network messages options 
        /// </summary>
        /// <param name="jsonNetworkMessageEncode"></param>
        /// <param name="jsonNetworkMessageDecoded"></param>
        /// <returns></returns>
        private void Compare(JsonNetworkMessage jsonNetworkMessageEncode, JsonNetworkMessage jsonNetworkMessageDecoded)
        {
            JsonNetworkMessageContentMask networkMessageContentMask = jsonNetworkMessageEncode.NetworkMessageContentMask;

            // Verify flags
            Assert.AreEqual(jsonNetworkMessageEncode.NetworkMessageContentMask & jsonNetworkMessageDecoded.NetworkMessageContentMask,
                jsonNetworkMessageDecoded.NetworkMessageContentMask, "NetworkMessageContentMask were not decoded correctly");

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
            List<DataSet> receivedDataSets = jsonNetworkMessageDecoded.ReceivedDataSets;

            Assert.IsNotNull(receivedDataSets, "ReceivedDataSets is null");

            // check the number of JsonDataSetMessage counts
            if ((networkMessageContentMask & JsonNetworkMessageContentMask.SingleDataSetMessage) == 0)
            {
                Assert.AreEqual(jsonNetworkMessageEncode.DataSetMessages.Count,
                    receivedDataSets.Count, "JsonDataSetMessages.Count was not decoded correctly (Count = {0})", receivedDataSets.Count);
            }
            else
            {
                Assert.AreEqual(1, receivedDataSets.Count,
                   "JsonDataSetMessages.Count was not decoded correctly. There is no SingleDataSetMessage (Coount = {0})", receivedDataSets.Count);
            }

            // check if the encoded match the decoded DataSetWriterId's
            for(int i =0; i < receivedDataSets.Count; i++)
            {
                JsonDataSetMessage jsonDataSetMessage = jsonNetworkMessageEncode.DataSetMessages[i] as JsonDataSetMessage;
                Assert.IsNotNull(jsonDataSetMessage, "DataSet [{0}] is missing from publisher datasets!", i);
                // check payload data fields count 
                // get related dataset from subscriber DataSets
                DataSet decodedDataSet = receivedDataSets[i];
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
