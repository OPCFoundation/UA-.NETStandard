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
        public void ValidateMatrixEncodigWithParameters(
          [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds//,
            //DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourcePicoSeconds,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.StatusCode,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourceTimestamp,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.StatusCode,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.StatusCode,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
           [Values((byte)1
           // , (UInt16)1, (UInt32)1, (UInt64)1, "abc"
            )] object publisherId)
        {
            // Arrange
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId | UadpNetworkMessageContentMask.WriterGroupId
                | UadpNetworkMessageContentMask.PayloadHeader;

            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.SequenceNumber;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaMatrixes("Matrixes")
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
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;

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
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds//,
            //DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourcePicoSeconds,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.StatusCode,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourceTimestamp,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.StatusCode,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.StatusCode,
            //DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode
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
                //MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                //MessagesHelper.CreateDataSetMetaData3("DataSet3")
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
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;

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
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId
                | UadpNetworkMessageContentMask.WriterGroupId;
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
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            
            bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

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
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.GroupHeader
                | UadpNetworkMessageContentMask.PublisherId;
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
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;

            bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 0, setDataSetWriterId: hasDataSetWriterId,
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
            DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.ServerTimestamp | DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            // Arrange
            UInt16 writerGroupId = 1;
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.WriterGroupId
                | UadpNetworkMessageContentMask.PublisherId
                | UadpNetworkMessageContentMask.PayloadHeader;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.None;

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
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            
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
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.WriterGroupId
                | UadpNetworkMessageContentMask.GroupVersion
                | UadpNetworkMessageContentMask.PublisherId
                | UadpNetworkMessageContentMask.PayloadHeader;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.None;

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
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
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
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.WriterGroupId
                | UadpNetworkMessageContentMask.NetworkMessageNumber
                | UadpNetworkMessageContentMask.PublisherId
                | UadpNetworkMessageContentMask.PayloadHeader;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.None;

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
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
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
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.WriterGroupId
                | UadpNetworkMessageContentMask.SequenceNumber
                | UadpNetworkMessageContentMask.PublisherId
                | UadpNetworkMessageContentMask.PayloadHeader;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.None;

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
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
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
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.WriterGroupId
                | UadpNetworkMessageContentMask.PayloadHeader
                | UadpNetworkMessageContentMask.PublisherId;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.None;

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
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            
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
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.WriterGroupId
                | UadpNetworkMessageContentMask.Timestamp
                | UadpNetworkMessageContentMask.PublisherId
                | UadpNetworkMessageContentMask.PayloadHeader;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.None;

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
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
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
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.WriterGroupId
                | UadpNetworkMessageContentMask.PicoSeconds
                | UadpNetworkMessageContentMask.PublisherId
                | UadpNetworkMessageContentMask.PayloadHeader;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.None;

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
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
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
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId
                | UadpNetworkMessageContentMask.DataSetClassId;
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
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            uaNetworkMessage.DataSetClassId = Guid.NewGuid();

            bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

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

            #region Payload header

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {
                // check the number of UadpDataSetMessage counts
                Assert.AreEqual(uadpNetworkMessageEncode.DataSetMessages.Count,
                    uadpNetworkMessageDecoded.DataSetMessages.Count, "UadpDataSetMessages.Count was not decoded correctly");

                Assert.IsNotNull(subscribedDataSets, "SubscribedDataSets is null");                
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
            List<DataSet> receivedDataSets = uadpNetworkMessageDecoded.ReceivedDataSets;
            Assert.IsNotNull(receivedDataSets, "ReceivedDataSets is null");

            // check the number of UadpDataSetMessages counts
            Assert.AreEqual(uadpNetworkMessageEncode.DataSetMessages.Count,
                     receivedDataSets.Count, "UadpDataSetMessages.Count was not decoded correctly (Count = {0})", receivedDataSets.Count);


            // check if the encoded match the received decoded DataSets
            for (int i = 0; i < receivedDataSets.Count; i++)
            {
                UadpDataSetMessage uadpDataSetMessage = uadpNetworkMessageEncode.DataSetMessages[i] as UadpDataSetMessage;
                Assert.IsNotNull(uadpDataSetMessage, "DataSet [{0}] is missing from publisher datasets!", i);

                //UadpDataSetMessage uadpDataSetMessageDecoded = uadpNetworkMessageDecoded.DataSetMessages[i] as UadpDataSetMessage;

                //Assert.IsNotNull(uadpDataSetMessageDecoded, "Decoded message did not found uadpDataSetMessage.DataSetWriterId = {0}", uadpDataSetMessage.DataSetWriterId);

                //// check payload data size in bytes
                //Assert.AreEqual(uadpDataSetMessage.PayloadSizeInStream, uadpDataSetMessageDecoded.PayloadSizeInStream,
                //    "PayloadSizeInStream was not decoded correctly, DataSetWriterId = {0}", uadpDataSetMessage.DataSetWriterId);

                // check payload data fields count 
                // get related dataset from subscriber DataSets
                DataSet decodedDataSet = receivedDataSets[i];
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
                Assert.AreNotSame(uadpNetworkMessageEncode.PublisherId, uadpNetworkMessageDecoded.PublisherId, "PublisherId was not decoded correctly");
            }
        }
    }
}
