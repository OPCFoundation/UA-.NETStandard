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
    [TestFixture(Description = "Tests for Encoding/Decoding of UadpNetworkMessage objects using mqtt")]
    public class MqttUadpNetworkMessageTests
    {
        private const UInt16 NamespaceIndexAllTypes = 3;

        private const string MqttAddressUrl = "mqtt://localhost:1883";

        [Test(Description = "Validate PublisherId with PublisherId as parameter")]
        public void ValidatePublisherIdWithWithPublisherIdParameter(
           [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData,
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            // Arrange
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask =  UadpNetworkMessageContentMask.PublisherId;

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
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            // set PublisherId
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.PublisherId);
            uaNetworkMessage.PublisherId = publisherId;

            //bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.DataSetMessageHeader) != 0
            //     && (uadpDataSetMessageContentMask & UadpDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1, setDataSetWriterId: true,
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

        [Test(Description = "Invalidate PublisherId with PublisherId as parameter")]
        public void InValidatePublisherIdWithWithPublisherIdParameter(
           [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData,
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values((float)1, (double)1)] object publisherId)
        {
            // Arrange
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId;

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
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            // set PublisherId
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.PublisherId);
            uaNetworkMessage.PublisherId = publisherId;

            //bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.DataSetMessageHeader) != 0
            //     && (uadpDataSetMessageContentMask & UadpDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1, setDataSetWriterId: true,
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
            InvalidCompareEncodeDecode(uaNetworkMessage, dataSetReaders);
        }

        [Test(Description = "Validate GroupHeader with PublisherId as parameter")]
        public void ValidateGroupHeaderWithPublisherIdParameter(
           [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData,
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            // Arrange
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId;
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
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            // set PublisherId
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.GroupHeader |
                                                          UadpNetworkMessageContentMask.PublisherId);
            uaNetworkMessage.PublisherId = publisherId;

            //bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.DataSetMessageHeader) != 0
            //     && (uadpDataSetMessageContentMask & UadpDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1, setDataSetWriterId: true,
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
           [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData,
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            // Arrange
            UInt16 writerGroupId = 1;
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId;
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
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            // set PublisherId
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.WriterGroupId |
                                                          UadpNetworkMessageContentMask.PublisherId |
                                                          UadpNetworkMessageContentMask.PayloadHeader);
            uaNetworkMessage.PublisherId = publisherId;
            uaNetworkMessage.WriterGroupId = writerGroupId;

            //bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.DataSetMessageHeader) != 0
            //     && (uadpDataSetMessageContentMask & UadpDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: true,
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
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId;
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
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            // set PublisherId
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.WriterGroupId |
                                                          UadpNetworkMessageContentMask.GroupVersion |
                                                          UadpNetworkMessageContentMask.PublisherId |
                                                          UadpNetworkMessageContentMask.PayloadHeader);
            uaNetworkMessage.PublisherId = publisherId;
            uaNetworkMessage.WriterGroupId = writerGroupId;
            uaNetworkMessage.GroupVersion = 1;

            //bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.DataSetMessageHeader) != 0
            //     && (uadpDataSetMessageContentMask & UadpDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: true,
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
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId;
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
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            // set PublisherId
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.WriterGroupId |
                                                          UadpNetworkMessageContentMask.NetworkMessageNumber |
                                                          UadpNetworkMessageContentMask.PublisherId |
                                                          UadpNetworkMessageContentMask.PayloadHeader);
            uaNetworkMessage.PublisherId = publisherId;
            uaNetworkMessage.WriterGroupId = writerGroupId;
            uaNetworkMessage.NetworkMessageNumber = 1;

            //bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.DataSetMessageHeader) != 0
            //     && (uadpDataSetMessageContentMask & UadpDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: true,
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
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId;
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
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            // set PublisherId
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.WriterGroupId |
                                                          UadpNetworkMessageContentMask.SequenceNumber |
                                                          UadpNetworkMessageContentMask.PublisherId |
                                                          UadpNetworkMessageContentMask.PayloadHeader);
            uaNetworkMessage.PublisherId = publisherId;
            uaNetworkMessage.WriterGroupId = writerGroupId;
            uaNetworkMessage.SequenceNumber = 1;

            //bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.DataSetMessageHeader) != 0
            //     && (uadpDataSetMessageContentMask & UadpDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: true,
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
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId;
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
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            // set PublisherId
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.WriterGroupId |
                                                          UadpNetworkMessageContentMask.PayloadHeader |
                                                          UadpNetworkMessageContentMask.PublisherId);
            uaNetworkMessage.PublisherId = publisherId;
            uaNetworkMessage.WriterGroupId = writerGroupId;

            //bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.DataSetMessageHeader) != 0
            //     && (uadpDataSetMessageContentMask & UadpDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: true,
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
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId;
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
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            // set PublisherId
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.WriterGroupId |
                                                          UadpNetworkMessageContentMask.Timestamp |
                                                          UadpNetworkMessageContentMask.PublisherId |
                                                          UadpNetworkMessageContentMask.PayloadHeader);
            uaNetworkMessage.PublisherId = publisherId;
            uaNetworkMessage.WriterGroupId = writerGroupId;
            uaNetworkMessage.Timestamp = DateTime.UtcNow;

            //bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.DataSetMessageHeader) != 0
            //     && (uadpDataSetMessageContentMask & UadpDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: true,
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
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId;
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
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            // set PublisherId
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.WriterGroupId |
                                                          UadpNetworkMessageContentMask.PicoSeconds |
                                                          UadpNetworkMessageContentMask.PublisherId |
                                                          UadpNetworkMessageContentMask.PayloadHeader);
            uaNetworkMessage.PublisherId = publisherId;
            uaNetworkMessage.WriterGroupId = writerGroupId;
            uaNetworkMessage.PicoSeconds = 10;

            //bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.DataSetMessageHeader) != 0
            //     && (uadpDataSetMessageContentMask & UadpDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: true,
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
            UInt16 writerGroupId = 1;
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId;
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
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            // set PublisherId
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.DataSetClassId);
            uaNetworkMessage.PublisherId = publisherId;
            uaNetworkMessage.DataSetClassId = Guid.NewGuid();

            //bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.DataSetMessageHeader) != 0
            //     && (uadpDataSetMessageContentMask & UadpDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: true,
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
            DataValue floatValueArray = new DataValue(new Variant(new FloatCollection() { 1100, 5, 1200, 5, 1300, 5 }));
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

            #endregion
        }

        /// <summary>
        /// Compare encoded/decoded network messages
        /// </summary>
        /// <param name="uadpNetworkMessageEncode"></param>
        /// <param name="uadpNetworkMessageDecoded"></param>
        private void CompareEncodeDecode(UadpNetworkMessage uadpNetworkMessage, IList<DataSetReaderDataType> dataSetReaders)
        {
            byte[] bytes = uadpNetworkMessage.Encode();

            UadpNetworkMessage uaNetworkMessageDecoded = new UadpNetworkMessage();
            uaNetworkMessageDecoded.Decode(bytes, dataSetReaders);

            // compare uaNetworkMessage with uaNetworkMessageDecoded
            // TODO Fix: this might be broken after refactor
            Compare(uadpNetworkMessage, uaNetworkMessageDecoded, uaNetworkMessageDecoded.ReceivedDataSets);
        }

        /// <summary>
        /// Compare network messages options 
        /// </summary>
        /// <param name="uadpNetworkMessageEncode"></param>
        /// <param name="uadpNetworkMessageDecoded"></param>
        /// <returns></returns>
        private void Compare(UadpNetworkMessage uadpNetworkMessageEncode, UadpNetworkMessage uadpNetworkMessageDecoded, List<DataSet> subscribedDataSets)
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

            #region Payload header + Payload data

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {
                // check the number of UadpDataSetMessage counts
                Assert.AreEqual(uadpNetworkMessageEncode.DataSetMessages.Count,
                    uadpNetworkMessageDecoded.DataSetMessages.Count, "UadpDataSetMessages.Count was not decoded correctly");

                Assert.IsNotNull(subscribedDataSets, "SubscribedDataSets is null");

                // check if the encoded match the decoded DataSetWriterId's
                foreach (UadpDataSetMessage uadpDataSetMessage in uadpNetworkMessageEncode.DataSetMessages)
                {
                    UadpDataSetMessage uadpDataSetMessageDecoded =
                        uadpNetworkMessageDecoded.DataSetMessages.FirstOrDefault(decoded =>
                            ((UadpDataSetMessage)decoded).DataSetWriterId == uadpDataSetMessage.DataSetWriterId) as UadpDataSetMessage;

                    Assert.IsNotNull(uadpDataSetMessageDecoded, "Decoded message did not found uadpDataSetMessage.DataSetWriterId = {0}", uadpDataSetMessage.DataSetWriterId);

                    // check payload data size in bytes
                    Assert.AreEqual(uadpDataSetMessage.PayloadSizeInStream, uadpDataSetMessageDecoded.PayloadSizeInStream,
                        "PayloadSizeInStream was not decoded correctly, DataSetWriterId = {0}", uadpDataSetMessage.DataSetWriterId);

                    // check payload data fields count 
                    // get related dataset from subscriber DataSets
                    DataSet decodedDataSet = subscribedDataSets.FirstOrDefault(dataSet => dataSet.Name == uadpDataSetMessage.DataSet.Name);
                    Assert.IsNotNull(decodedDataSet, "DataSet '{0}' is missing from subscriber datasets!", uadpDataSetMessage.DataSet.Name);

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

                        Assert.AreEqual(dataValueEncoded.Value, dataValueDecoded.Value, "Wrong: Fields[{0}].DataValue.Value; DataSetWriterId = {1}", index, uadpDataSetMessage.DataSetWriterId);

                        // Checks just for DataValue type only 
                        if ((uadpDataSetMessage.FieldContentMask & DataSetFieldContentMask.StatusCode) ==
                            DataSetFieldContentMask.StatusCode)
                        {
                            // check dataValues StatusCode
                            Assert.AreEqual(dataValueEncoded.StatusCode, dataValueDecoded.StatusCode,
                                "Wrong: Fields[{0}].DataValue.StatusCode; DataSetWriterId = {1}", index, uadpDataSetMessage.DataSetWriterId);
                        }

                        // check dataValues SourceTimestamp
                        if ((uadpDataSetMessage.FieldContentMask & DataSetFieldContentMask.SourceTimestamp) ==
                            DataSetFieldContentMask.SourceTimestamp)
                        {
                            Assert.AreEqual(dataValueEncoded.SourceTimestamp, dataValueDecoded.SourceTimestamp,
                                "Wrong: Fields[{0}].DataValue.SourceTimestamp; DataSetWriterId = {1}", index, uadpDataSetMessage.DataSetWriterId);
                        }

                        // check dataValues ServerTimestamp
                        if ((uadpDataSetMessage.FieldContentMask & DataSetFieldContentMask.ServerTimestamp) ==
                            DataSetFieldContentMask.ServerTimestamp)
                        {
                            // check dataValues ServerTimestamp
                            Assert.AreEqual(dataValueEncoded.ServerTimestamp, dataValueDecoded.ServerTimestamp,
                               "Wrong: Fields[{0}].DataValue.ServerTimestamp; DataSetWriterId = {1}", index, uadpDataSetMessage.DataSetWriterId);
                        }

                        // check dataValues SourcePicoseconds
                        if ((uadpDataSetMessage.FieldContentMask & DataSetFieldContentMask.SourcePicoSeconds) ==
                            DataSetFieldContentMask.SourcePicoSeconds)
                        {
                            Assert.AreEqual(dataValueEncoded.SourcePicoseconds, dataValueDecoded.SourcePicoseconds,
                               "Wrong: Fields[{0}].DataValue.SourcePicoseconds; DataSetWriterId = {1}", index, uadpDataSetMessage.DataSetWriterId);
                        }

                        // check dataValues ServerPicoSeconds
                        if ((uadpDataSetMessage.FieldContentMask & DataSetFieldContentMask.ServerPicoSeconds) ==
                            DataSetFieldContentMask.ServerPicoSeconds)
                        {
                            // check dataValues ServerPicoseconds
                            Assert.AreEqual(dataValueEncoded.ServerPicoseconds, dataValueDecoded.ServerPicoseconds,
                               "Wrong: Fields[{0}].DataValue.ServerPicoseconds; DataSetWriterId = {1}", index, uadpDataSetMessage.DataSetWriterId);
                        }
                    }
                }
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
            uaNetworkMessageDecoded.Decode(bytes, dataSetReaders);

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
