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
    [TestFixture(Description = "Tests for Encoding/Decoding of UadpNetworkMessage objects using mqtt")]
    public class MqttUadpNetworkMessageTests
    {
        private const UInt16 NamespaceIndexAllTypes = 3;

        private const string MqttAddressUrl = "mqtt://localhost:1883";

        [Test(Description = "Validate PublisherId with PublisherId as parameter")]
        public void ValidateMatrixEncodigWithParameters(
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
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
           [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            // Arrange
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId | UadpNetworkMessageContentMask.WriterGroupId
                | UadpNetworkMessageContentMask.PayloadHeader;

            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.SequenceNumber;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaDataArrays("Arrays"),
                //MessagesHelper.CreateDataSetMetaDataMatrices("Matrices")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");

            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState());
            // filter out the metadata message
            networkMessages = (from m in networkMessages
                               where !m.IsMetaDataMessage
                               select m).ToList();

            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.AreEqual(1, networkMessages.Count, "connection.CreateNetworkMessages shall return only one network message");

            UadpNetworkMessage uaNetworkMessage = networkMessages[0] as UadpNetworkMessage;

            bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1, setDataSetWriterId: hasDataSetWriterId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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



        [Test(Description = "Validate PublisherId with PublisherId as parameter")]
        public void ValidatePublisherIdWithWithPublisherIdParameter(
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
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values((byte)1
           // , (UInt16)1, (UInt32)1, (UInt64)1, "abc"
            )] object publisherId)
        {
            // Arrange
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask =  UadpNetworkMessageContentMask.PublisherId | UadpNetworkMessageContentMask.WriterGroupId;

            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.SequenceNumber;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");

            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState());
            // filter out the metadata message
            networkMessages = (from m in networkMessages
                               where !m.IsMetaDataMessage
                               select m).ToList();

            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.AreEqual(1, networkMessages.Count, "connection.CreateNetworkMessages shall return only one network message");

            UadpNetworkMessage uaNetworkMessage = networkMessages[0] as UadpNetworkMessage;

            bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1, setDataSetWriterId: hasDataSetWriterId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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

        [Test(Description = "Validate GroupHeader with PublisherId as parameter")]
        public void ValidateGroupHeaderWithPublisherIdParameter(
           [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData,
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values((byte)1/*, (UInt16)1, (UInt32)1, (UInt64)1, "abc"*/)] object publisherId)
        {
            // Arrange
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.GroupHeader |
                                                          UadpNetworkMessageContentMask.PublisherId;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.None;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaDataMatrices("Matrices"),
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");

            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState());

            // filter out the metadata message
            networkMessages = (from m in networkMessages
                               where !m.IsMetaDataMessage
                               select m).ToList();


            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.AreEqual(1, networkMessages.Count, "connection.CreateNetworkMessages shall return only one network message");

            UadpNetworkMessage uaNetworkMessage = networkMessages[0] as UadpNetworkMessage;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 0, setDataSetWriterId: false,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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

        [Test(Description = "Validate WriterGroupId with PublisherId as parameter")]
        public void ValidateWriterGroupIdWithPublisherIdParameter(
           [Values(DataSetFieldContentMask.None,
            DataSetFieldContentMask.RawData,
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            // Arrange
            UInt16 writerGroupId = 1;
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.WriterGroupId |
                                                          UadpNetworkMessageContentMask.PublisherId |
                                                          UadpNetworkMessageContentMask.PayloadHeader;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.SequenceNumber;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaDataArrays("DataSet1"),
                MessagesHelper.CreateDataSetMetaDataMatrices("DataSet2"),
              //  MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");

            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState());
            // filter out the metadata message
            networkMessages = (from m in networkMessages
                               where !m.IsMetaDataMessage
                               select m).ToList();

            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.AreEqual(1, networkMessages.Count, "connection.CreateNetworkMessages shall return only one network message");

            UadpNetworkMessage uaNetworkMessage = networkMessages[0] as UadpNetworkMessage;

            bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: hasDataSetWriterId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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

        [Test(Description = "Validate GroupVersion with PublisherId as parameter")]
        public void ValidateGroupVersionWithPublisherIdParameter(
           [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData,
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            // Arrange
            UInt16 writerGroupId = 1;
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.WriterGroupId |
                                                          UadpNetworkMessageContentMask.GroupVersion |
                                                          UadpNetworkMessageContentMask.PublisherId |
                                                          UadpNetworkMessageContentMask.PayloadHeader;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.SequenceNumber;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");

            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState());

            // filter out the metadata message
            networkMessages = (from m in networkMessages
                               where !m.IsMetaDataMessage
                               select m).ToList();
            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.AreEqual(1, networkMessages.Count, "connection.CreateNetworkMessages shall return only one network message");

            UadpNetworkMessage uaNetworkMessage = networkMessages[0] as UadpNetworkMessage;

            Assert.IsNotNull(uaNetworkMessage, "uaNetworkMessage should not be null");
            uaNetworkMessage.GroupVersion = 1;

            bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: hasDataSetWriterId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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

        [Test(Description = "Validate NetworkMessageNumber with PublisherId as parameter")]
        public void ValidateNetworkMessageNumberWithPublisherIdParameter(
           [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData,
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            // Arrange
            UInt16 writerGroupId = 1;
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.WriterGroupId |
                                                          UadpNetworkMessageContentMask.NetworkMessageNumber |
                                                          UadpNetworkMessageContentMask.PublisherId |
                                                          UadpNetworkMessageContentMask.PayloadHeader;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.SequenceNumber;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");

            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState());
            // filter out the metadata message
            networkMessages = (from m in networkMessages
                               where !m.IsMetaDataMessage
                               select m).ToList();
            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.AreEqual(1, networkMessages.Count, "connection.CreateNetworkMessages shall return only one network message");

            UadpNetworkMessage uaNetworkMessage = networkMessages[0] as UadpNetworkMessage;

            Assert.IsNotNull(uaNetworkMessage, "uaNetworkMessage should not be null");
            uaNetworkMessage.NetworkMessageNumber = 1;

            bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: hasDataSetWriterId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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

        [Test(Description = "Validate SequenceNumber with PublisherId as parameter")]
        public void ValidateSequenceNumberWithPublisherIdParameter(
           [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData,
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            // Arrange
            UInt16 writerGroupId = 1;
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.WriterGroupId |
                                                          UadpNetworkMessageContentMask.SequenceNumber |
                                                          UadpNetworkMessageContentMask.PublisherId |
                                                          UadpNetworkMessageContentMask.PayloadHeader;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.SequenceNumber;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState())[0] as
                UadpNetworkMessage;

            Assert.IsNotNull(uaNetworkMessage, "uaNetworkMessage should not be null");
            uaNetworkMessage.SequenceNumber = 1;

            bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: hasDataSetWriterId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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

        [Test(Description = "Validate PayloadHeader with PublisherId as parameter")]
        public void ValidatePayloadHeaderWithPublisherIdParameter(
           [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData,
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            // Arrange
            UInt16 writerGroupId = 1;
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.WriterGroupId |
                                                          UadpNetworkMessageContentMask.PayloadHeader |
                                                          UadpNetworkMessageContentMask.PublisherId;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.SequenceNumber;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");

            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState());
            // filter out the metadata message
            networkMessages = (from m in networkMessages
                               where !m.IsMetaDataMessage
                               select m).ToList();

            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.AreEqual(1, networkMessages.Count, "connection.CreateNetworkMessages shall return only one network message");

            UadpNetworkMessage uaNetworkMessage = networkMessages[0] as UadpNetworkMessage;

            bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: hasDataSetWriterId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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

        [Test(Description = "Validate Timestamp with PublisherId as parameter")]
        public void ValidateTimestampWithPublisherIdParameter(
           [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData,
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            // Arrange
            UInt16 writerGroupId = 1;
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.WriterGroupId |
                                                          UadpNetworkMessageContentMask.Timestamp |
                                                          UadpNetworkMessageContentMask.PublisherId |
                                                          UadpNetworkMessageContentMask.PayloadHeader;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.SequenceNumber;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");

            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState());
            // filter out the metadata message
            networkMessages = (from m in networkMessages
                               where !m.IsMetaDataMessage
                               select m).ToList();
            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.AreEqual(1, networkMessages.Count, "connection.CreateNetworkMessages shall return only one network message");

            UadpNetworkMessage uaNetworkMessage = networkMessages[0] as UadpNetworkMessage;

            Assert.IsNotNull(uaNetworkMessage, "uaNetworkMessage should not be null");
            uaNetworkMessage.Timestamp = DateTime.UtcNow;

            bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: hasDataSetWriterId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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

        [Test(Description = "Validate PicoSeconds with PublisherId as parameter")]
        public void ValidatePicoSecondsWithPublisherIdParameter(
           [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData,
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            // Arrange
            UInt16 writerGroupId = 1;
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.WriterGroupId |
                                                          UadpNetworkMessageContentMask.PicoSeconds |
                                                          UadpNetworkMessageContentMask.PublisherId |
                                                          UadpNetworkMessageContentMask.PayloadHeader;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.SequenceNumber;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");

            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState());
            // filter out the metadata message
            networkMessages = (from m in networkMessages
                               where !m.IsMetaDataMessage
                               select m).ToList();

            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.AreEqual(1, networkMessages.Count, "connection.CreateNetworkMessages shall return only one network message");

            UadpNetworkMessage uaNetworkMessage = networkMessages[0] as UadpNetworkMessage;

            Assert.IsNotNull(uaNetworkMessage, "uaNetworkMessage should not be null");
            uaNetworkMessage.PicoSeconds = 10;

            bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: hasDataSetWriterId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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

        [Test(Description = "Validate DataSetClassId with PublisherId as parameter")]
        public void ValidateDataSetClassIdWithPublisherIdParameter(
           [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData,
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            // Arrange
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.None;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");

            var networkMessages = connection.CreateNetworkMessages(publisherConfiguration.Connections.First().WriterGroups.First(), new WriterGroupPublishState());

            // filter out the metadata message
            networkMessages = (from m in networkMessages
                              where !m.IsMetaDataMessage
                              select m).ToList();

            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.AreEqual(1, networkMessages.Count, "connection.CreateNetworkMessages shall return only one network message");

            UadpNetworkMessage uaNetworkMessage = networkMessages[0] as UadpNetworkMessage;

            // set DataSetClassId
            Assert.IsNotNull(uaNetworkMessage, "uaNetworkMessage should not be null");
            uaNetworkMessage.DataSetClassId = Guid.NewGuid();

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 0, setDataSetWriterId: false,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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


        [Test(Description = "Validate that Uadp metadata is encoded/decoded correctly")]
        public void ValidateMetaDataIsEncodedCorrectly(
            [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData,
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            // Arrange
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.None;
           
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
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
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

            List<UadpNetworkMessage> uaMetaDataNetworkMessages = MessagesHelper.GetUadpUaMetaDataNetworkMessages(networkMessages.Cast<UadpNetworkMessage>().ToList());
            Assert.IsNotNull(uaMetaDataNetworkMessages, "Uadp ua-metadata entries are missing from configuration!");

            foreach (var uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                CompareEncodeDecodeMetaData(uaMetaDataNetworkMessage);
            }
        }

        [Test(Description = "Validate that metadata with update time 0 is sent at startup for a MQTT Uadp publisher")]
        public void ValidateMetaDataUpdateTimeZeroSentAtStartup(
            [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData,
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            // arrange
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.None;
            
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
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

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

            List<UadpNetworkMessage> uaMetaDataNetworkMessages = MessagesHelper.GetUadpUaMetaDataNetworkMessages(networkMessages.Cast<UadpNetworkMessage>().ToList());
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

            uaMetaDataNetworkMessages = MessagesHelper.GetUadpUaMetaDataNetworkMessages(networkMessages.Cast<UadpNetworkMessage>().ToList());
            Assert.IsNotNull(uaMetaDataNetworkMessages, "Uadp ua-metadata entries are missing from configuration!");

            // check if there are any metadata messages. second time around there shall be no metadata messages
            Assert.AreEqual(0, uaMetaDataNetworkMessages.Count, "The ua-metadata messages count shall be zero for the second time when create messages is called!");
        }

        /// <summary>
        /// Compare encoded/decoded network messages
        /// </summary>
        /// <param name="uadpNetworkMessage">the message to encode</param>
        private void CompareEncodeDecodeMetaData(UadpNetworkMessage uadpNetworkMessage)
        {
            Assert.IsTrue(uadpNetworkMessage.IsMetaDataMessage, "The received message is not a metadata message");

            byte[] bytes = uadpNetworkMessage.Encode();

            UadpNetworkMessage uaNetworkMessageDecoded = new UadpNetworkMessage();
            uaNetworkMessageDecoded.Decode(ServiceMessageContext.GlobalContext, bytes, null);

            Assert.IsTrue(uaNetworkMessageDecoded.IsMetaDataMessage, "The Decode message is not a metadata message");

            Assert.AreEqual(uadpNetworkMessage.WriterGroupId, uaNetworkMessageDecoded.WriterGroupId, "The Decoded WriterId does not match encoded value");

            Assert.IsTrue(Utils.IsEqual(uadpNetworkMessage.DataSetMetaData, uaNetworkMessageDecoded.DataSetMetaData), uadpNetworkMessage.DataSetMetaData.Name + " Decoded metadata is not equal ");

        }

        /// <summary>
        /// Compare encoded/decoded network messages
        /// </summary>
        /// <param name="uadpNetworkMessage"></param>
        /// <param name="dataSetReaders"></param>
        private void CompareEncodeDecode(UadpNetworkMessage uadpNetworkMessage, IList<DataSetReaderDataType> dataSetReaders)
        {
            byte[] bytes = uadpNetworkMessage.Encode();

            UadpNetworkMessage uaNetworkMessageDecoded = new UadpNetworkMessage();
            uaNetworkMessageDecoded.Decode(ServiceMessageContext.GlobalContext, bytes, dataSetReaders);

            // compare uaNetworkMessage with uaNetworkMessageDecoded
            Compare(uadpNetworkMessage, uaNetworkMessageDecoded);
        }

        /// <summary>
        /// Compare network messages options 
        /// </summary>
        /// <param name="uadpNetworkMessageEncode"></param>
        /// <param name="uadpNetworkMessageDecoded"></param>
        /// <returns></returns>
        private void Compare(UadpNetworkMessage uadpNetworkMessageEncode, UadpNetworkMessage uadpNetworkMessageDecoded)
        {
            UadpNetworkMessageContentMask networkMessageContentMask = uadpNetworkMessageEncode.NetworkMessageContentMask;

            if ((networkMessageContentMask | UadpNetworkMessageContentMask.None) == UadpNetworkMessageContentMask.None)
            {
                //nothing to check
                return;
            }

            // Verify flags
            Assert.AreEqual(uadpNetworkMessageEncode.UADPFlags, uadpNetworkMessageDecoded.UADPFlags, "UADPFlags were not decoded correctly");

            #region Network Message Header
            if ((networkMessageContentMask & UadpNetworkMessageContentMask.PublisherId) != 0)
            {
                Assert.AreEqual(uadpNetworkMessageEncode.PublisherId, uadpNetworkMessageDecoded.PublisherId, "PublisherId was not decoded correctly");
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.DataSetClassId) != 0)
            {
                Assert.AreEqual(uadpNetworkMessageEncode.DataSetClassId, uadpNetworkMessageDecoded.DataSetClassId, "DataSetClassId was not decoded correctly");
            }
            #endregion

            #region Group Message Header
            if ((networkMessageContentMask & (UadpNetworkMessageContentMask.GroupHeader |
                                              UadpNetworkMessageContentMask.WriterGroupId |
                                              UadpNetworkMessageContentMask.GroupVersion |
                                              UadpNetworkMessageContentMask.NetworkMessageNumber |
                                              UadpNetworkMessageContentMask.SequenceNumber)) != 0)
            {
                Assert.AreEqual(uadpNetworkMessageEncode.GroupFlags, uadpNetworkMessageDecoded.GroupFlags, "GroupFlags was not decoded correctly");
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.WriterGroupId) != 0)
            {
                Assert.AreEqual(uadpNetworkMessageEncode.WriterGroupId, uadpNetworkMessageDecoded.WriterGroupId, "WriterGroupId was not decoded correctly");
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.GroupVersion) != 0)
            {
                Assert.AreEqual(uadpNetworkMessageEncode.GroupVersion, uadpNetworkMessageDecoded.GroupVersion, "GroupVersion was not decoded correctly");
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.NetworkMessageNumber) != 0)
            {
                Assert.AreEqual(uadpNetworkMessageEncode.NetworkMessageNumber, uadpNetworkMessageDecoded.NetworkMessageNumber, "NetworkMessageNumber was not decoded correctly");
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.SequenceNumber) != 0)
            {
                Assert.AreEqual(uadpNetworkMessageEncode.SequenceNumber, uadpNetworkMessageDecoded.SequenceNumber, "SequenceNumber was not decoded correctly");
            }
            #endregion

            #region Payload header

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {
                // check the number of UadpDataSetMessage counts
                Assert.AreEqual(uadpNetworkMessageEncode.DataSetMessages.Count,
                    uadpNetworkMessageDecoded.DataSetMessages.Count, "UadpDataSetMessages.Count was not decoded correctly");          
            }
            #endregion

            #region Extended network message header
            if ((networkMessageContentMask & UadpNetworkMessageContentMask.Timestamp) != 0)
            {
                Assert.AreEqual(uadpNetworkMessageEncode.Timestamp, uadpNetworkMessageDecoded.Timestamp, "Timestamp was not decoded correctly");
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.PicoSeconds) != 0)
            {
                Assert.AreEqual(uadpNetworkMessageEncode.PicoSeconds, uadpNetworkMessageDecoded.PicoSeconds, "PicoSeconds was not decoded correctly");
            }

            #endregion

            #region Payload data - check received datasets to match the encoded datasets
            List<UaDataSetMessage> receivedDataSetMessages = uadpNetworkMessageDecoded.DataSetMessages.ToList();

            Assert.IsNotNull(receivedDataSetMessages, "Received DataSetMessages is null");

            // check the number of UadpDataSetMessages counts
            Assert.AreEqual(uadpNetworkMessageEncode.DataSetMessages.Count,
                     receivedDataSetMessages.Count, "UadpDataSetMessages.Count was not decoded correctly (Count = {0})", receivedDataSetMessages.Count);


            // check if the encoded match the received decoded DataSets
            for (int i = 0; i < receivedDataSetMessages.Count; i++)
            {
                UadpDataSetMessage uadpDataSetMessage = uadpNetworkMessageEncode.DataSetMessages[i] as UadpDataSetMessage;
                Assert.IsNotNull(uadpDataSetMessage, "DataSet [{0}] is missing from publisher datasets!", i);

                // check payload data fields count 
                // get related dataset from subscriber DataSets
                DataSet decodedDataSet = receivedDataSetMessages[i].DataSet;
                Assert.IsNotNull(decodedDataSet, "DataSet '{0}' is missing from subscriber datasets!", uadpDataSetMessage?.DataSet.Name);

                Assert.AreEqual(uadpDataSetMessage.DataSet.Fields.Length, decodedDataSet.Fields.Length,
                    "DataSet.Fields.Length was not decoded correctly, DataSetWriterId = {0}", uadpDataSetMessage.DataSetWriterId);

                // check the fields data consistency
                // at this time the DataSetField has just value!?
                for (int index = 0; index < uadpDataSetMessage.DataSet.Fields.Length; index++)
                {
                    Field fieldEncoded = uadpDataSetMessage.DataSet.Fields[index];
                    Field fieldDecoded = decodedDataSet.Fields[index];
                    Assert.IsNotNull(fieldEncoded, "uadpDataSetMessage.DataSet.Fields[{0}] is null,  DataSetWriterId = {1}",
                        index, uadpDataSetMessage.DataSetWriterId);
                    Assert.IsNotNull(fieldDecoded, "uadpDataSetMessageDecoded.DataSet.Fields[{0}] is null,  DataSetWriterId = {1}",
                        index, uadpDataSetMessage.DataSetWriterId);

                    DataValue dataValueEncoded = fieldEncoded.Value;
                    DataValue dataValueDecoded = fieldDecoded.Value;
                    Assert.IsNotNull(fieldEncoded.Value, "uadpDataSetMessage.DataSet.Fields[{0}].Value is null,  DataSetWriterId = {1}",
                       index, uadpDataSetMessage.DataSetWriterId);
                    Assert.IsNotNull(fieldDecoded.Value, "uadpDataSetMessageDecoded.DataSet.Fields[{0}].Value is null,  DataSetWriterId = {1}",
                      index, uadpDataSetMessage.DataSetWriterId);

                    // check dataValues values
                    Assert.IsNotNull(fieldEncoded.Value.Value, "uadpDataSetMessage.DataSet.Fields[{0}].Value is null,  DataSetWriterId = {1}",
                       index, uadpDataSetMessage.DataSetWriterId);
                    Assert.IsNotNull(fieldDecoded.Value.Value, "uadpDataSetMessageDecoded.DataSet.Fields[{0}].Value is null,  DataSetWriterId = {1}",
                      index, uadpDataSetMessage.DataSetWriterId);


                    // check dataValues values
                    string fieldName = fieldEncoded.FieldMetaData.Name;

                    Assert.AreEqual(dataValueEncoded.Value, dataValueDecoded.Value,
                        "Wrong: Fields[{0}].DataValue.Value; DataSetWriterId = {1}",
                        fieldName, uadpDataSetMessage.DataSetWriterId);

                    // Checks just for DataValue type only 
                    if ((uadpDataSetMessage.FieldContentMask & DataSetFieldContentMask.StatusCode) ==
                        DataSetFieldContentMask.StatusCode)
                    {
                        // check dataValues StatusCode
                        Assert.AreEqual(dataValueEncoded.StatusCode, dataValueDecoded.StatusCode,
                            "Wrong: Fields[{0}].DataValue.StatusCode; DataSetWriterId = {1}", fieldName, uadpDataSetMessage.DataSetWriterId);
                    }

                    // check dataValues SourceTimestamp
                    if ((uadpDataSetMessage.FieldContentMask & DataSetFieldContentMask.SourceTimestamp) ==
                        DataSetFieldContentMask.SourceTimestamp)
                    {
                        Assert.AreEqual(dataValueEncoded.SourceTimestamp, dataValueDecoded.SourceTimestamp,
                            "Wrong: Fields[{0}].DataValue.SourceTimestamp; DataSetWriterId = {1}", fieldName, uadpDataSetMessage.DataSetWriterId);
                    }

                    // check dataValues ServerTimestamp
                    if ((uadpDataSetMessage.FieldContentMask & DataSetFieldContentMask.ServerTimestamp) ==
                        DataSetFieldContentMask.ServerTimestamp)
                    {
                        // check dataValues ServerTimestamp
                        Assert.AreEqual(dataValueEncoded.ServerTimestamp, dataValueDecoded.ServerTimestamp,
                           "Wrong: Fields[{0}].DataValue.ServerTimestamp; DataSetWriterId = {1}", fieldName, uadpDataSetMessage.DataSetWriterId);
                    }

                    // check dataValues SourcePicoseconds
                    if ((uadpDataSetMessage.FieldContentMask & DataSetFieldContentMask.SourcePicoSeconds) ==
                        DataSetFieldContentMask.SourcePicoSeconds)
                    {
                        Assert.AreEqual(dataValueEncoded.SourcePicoseconds, dataValueDecoded.SourcePicoseconds,
                           "Wrong: Fields[{0}].DataValue.SourcePicoseconds; DataSetWriterId = {1}", fieldName, uadpDataSetMessage.DataSetWriterId);
                    }

                    // check dataValues ServerPicoSeconds
                    if ((uadpDataSetMessage.FieldContentMask & DataSetFieldContentMask.ServerPicoSeconds) ==
                        DataSetFieldContentMask.ServerPicoSeconds)
                    {
                        // check dataValues ServerPicoseconds
                        Assert.AreEqual(dataValueEncoded.ServerPicoseconds, dataValueDecoded.ServerPicoseconds,
                           "Wrong: Fields[{0}].DataValue.ServerPicoseconds; DataSetWriterId = {1}", fieldName, uadpDataSetMessage.DataSetWriterId);
                    }
                }               
            }
            #endregion
        }

        /// <summary>
        /// Invalid compare encoded/decoded network messages
        /// </summary>
        /// <param name="uadpNetworkMessageEncode"></param>
        /// <param name="uadpNetworkMessageDecoded"></param>
        private void InvalidCompareEncodeDecode(UadpNetworkMessage uadpNetworkMessage, IList<DataSetReaderDataType> dataSetReaders)
        {
            byte[] bytes = uadpNetworkMessage.Encode();

            UadpNetworkMessage uaNetworkMessageDecoded = new UadpNetworkMessage();
            uaNetworkMessageDecoded.Decode(new ServiceMessageContext(), bytes, dataSetReaders);

            // compare uaNetworkMessage with uaNetworkMessageDecoded
            // TODO Fix: this might be broken after refactor
            InvalidCompare(uadpNetworkMessage, uaNetworkMessageDecoded);
        }

        /// <summary>
        /// Invalid compare network messages options (special case for PublisherId
        /// </summary>
        /// <param name="uadpNetworkMessageEncode"></param>
        /// <param name="uadpNetworkMessageDecoded"></param>
        private void InvalidCompare(UadpNetworkMessage uadpNetworkMessageEncode, UadpNetworkMessage uadpNetworkMessageDecoded)
        {
            UadpNetworkMessageContentMask networkMessageContentMask = uadpNetworkMessageEncode.NetworkMessageContentMask;

            if ((networkMessageContentMask | UadpNetworkMessageContentMask.None) == UadpNetworkMessageContentMask.None)
            {
                //nothing to check
                return;
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.PublisherId) ==
                UadpNetworkMessageContentMask.PublisherId)
            {
                // special case for valid PublisherId type only
                Assert.AreNotEqual(uadpNetworkMessageEncode.PublisherId, uadpNetworkMessageDecoded.PublisherId, "PublisherId was not decoded correctly");
            }
        }
    }
}
