/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.PubSub.Mqtt;
using Opc.Ua.PubSub.PublishedData;

namespace Opc.Ua.PubSub.Tests
{
    [TestFixture(Description = "Tests for Encoding/Decoding of JsonNetworkMessage objects")]
    public class JsonNetworkMessageTests
    {
        private const UInt16 NamespaceIndexAllTypes = 3;

        // constants for DataSetFieldContentMask
        private const DataSetFieldContentMask FieldContentMaskRawData = DataSetFieldContentMask.RawData;
        private const DataSetFieldContentMask FieldContentMaskVariant = DataSetFieldContentMask.None;
        private const DataSetFieldContentMask FieldContentMaskDatavalue1 = DataSetFieldContentMask.ServerPicoSeconds;
        private const DataSetFieldContentMask FieldContentMaskDatavalue2 = DataSetFieldContentMask.StatusCode
            | DataSetFieldContentMask.SourceTimestamp
            | DataSetFieldContentMask.ServerTimestamp
            | DataSetFieldContentMask.SourcePicoSeconds
            | DataSetFieldContentMask.ServerPicoSeconds;

        private const string MqttAddressUrl = "mqtt://localhost:1883";


        [OneTimeSetUp()]
        public void MyTestInitialize()
        {

        }

        /*
        [Test(Description = "Validate NetworkMessageHeader & PublisherId as parameter")]
        public void ValidateNetworkMessageMaskWithPublisherIdParameters(
            [Values(FieldContentMaskRawData, FieldContentMaskVariant, FieldContentMaskDatavalue1, FieldContentMaskDatavalue2)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(1, "abc")] object publisherId)
        {
            // Arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.DataSetMessageHeader
                | JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.DataSetClassId
                | JsonNetworkMessageContentMask.PublisherId
                | JsonNetworkMessageContentMask.ReplyTo
                | JsonNetworkMessageContentMask.SingleDataSetMessage;

            JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId
                | JsonDataSetMessageContentMask.MetaDataVersion
                | JsonDataSetMessageContentMask.SequenceNumber
                | JsonDataSetMessageContentMask.Status
                | JsonDataSetMessageContentMask.Timestamp;

            DataSetMetaDataType dataSetMetaData = MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes");

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1, dataSetWritersCount: 3,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub connection should not be null");

            // Act
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            JsonNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                JsonNetworkMessage;
           //  set PublisherId
            uaNetworkMessage.PublisherId = publisherId.ToString();

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1, dataSetReadersCount: 1, setDataSetWriterId: true, // the writerheader is saved
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberConfiguration should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");
            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");
             
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders);
        }
        */

        [Test(Description = "Validate NetworkMessageHeader & PublisherId with PublisherId as parameter")]
        public void ValidateMessageHeaderAndPublisherIdWithFieldContentMaskAndPublisherIdParameters(
            [Values(FieldContentMaskRawData, FieldContentMaskVariant, FieldContentMaskDatavalue1, FieldContentMaskDatavalue2)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(JsonDataSetMessageContentMask.None, JsonDataSetMessageContentMask.DataSetWriterId
                | JsonDataSetMessageContentMask.MetaDataVersion
                | JsonDataSetMessageContentMask.SequenceNumber
                | JsonDataSetMessageContentMask.Status
                | JsonDataSetMessageContentMask.Timestamp)]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
            [Values(1, "abc")] object publisherId)
        {
            // Arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.PublisherId;
            //JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.None;

            DataSetMetaDataType dataSetMetaData = MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes");
            Assert.IsNotNull(dataSetMetaData, "dataSetMetaData should not be null");

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1, dataSetWritersCount: 3,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
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

            bool hasDataSetWriterId = (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;
            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1, dataSetReadersCount: 3, setDataSetWriterId: hasDataSetWriterId,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
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
        public void ValidateMessageHeaderAndDataSetClassIdWithFieldContentMaskParameter(
            [Values(FieldContentMaskRawData, FieldContentMaskVariant, FieldContentMaskDatavalue1, FieldContentMaskDatavalue2)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(JsonDataSetMessageContentMask.None, JsonDataSetMessageContentMask.DataSetWriterId
                | JsonDataSetMessageContentMask.MetaDataVersion
                | JsonDataSetMessageContentMask.SequenceNumber
                | JsonDataSetMessageContentMask.Status
                | JsonDataSetMessageContentMask.Timestamp)]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask)
        {
            // Arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.DataSetClassId;
            //JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId
            //    | JsonDataSetMessageContentMask.MetaDataVersion
            //    | JsonDataSetMessageContentMask.SequenceNumber
            //    | JsonDataSetMessageContentMask.Status
            //    | JsonDataSetMessageContentMask.Timestamp;

            DataSetMetaDataType dataSetMetaData = MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes");
            Assert.IsNotNull(dataSetMetaData, "dataSetMetaData should not be null");

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: 1, writerGroupId: 1, dataSetWritersCount: 3,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
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

            bool hasDataSetWriterId = (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;
            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: null, writerGroupId: 1, dataSetReadersCount: 3, setDataSetWriterId: hasDataSetWriterId, // the writerheader is saved
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
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

        [Test(Description = "Validate NetworkMessageHeader & PublisherId & DataSetClassId with PublisherId as parameter")]
        public void ValidateMessageHeaderPublisherIdAndDataSetClassIdWithFieldContentMaskWithPublisherIdParameter(
            [Values(FieldContentMaskRawData, FieldContentMaskVariant, FieldContentMaskDatavalue1, FieldContentMaskDatavalue2)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(JsonDataSetMessageContentMask.None, JsonDataSetMessageContentMask.DataSetWriterId
                | JsonDataSetMessageContentMask.MetaDataVersion
                | JsonDataSetMessageContentMask.SequenceNumber
                | JsonDataSetMessageContentMask.Status
                | JsonDataSetMessageContentMask.Timestamp)]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
                [Values(1, "abc")] object publisherId)
        {
            // Arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.PublisherId
                | JsonNetworkMessageContentMask.DataSetClassId;
            //JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId
            //    | JsonDataSetMessageContentMask.MetaDataVersion
            //    | JsonDataSetMessageContentMask.SequenceNumber
            //    | JsonDataSetMessageContentMask.Status
            //    | JsonDataSetMessageContentMask.Timestamp;

            DataSetMetaDataType dataSetMetaData = MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes");
            Assert.IsNotNull(dataSetMetaData, "dataSetMetaData should not be null");

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1, dataSetWritersCount: 3,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
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
            uaNetworkMessage.PublisherId = publisherId.ToString();
            // set DataSetClassId
            uaNetworkMessage.DataSetClassId = Guid.NewGuid().ToString();

            bool hasDataSetWriterId = (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;
            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: null, writerGroupId: 1, dataSetReadersCount: 3, setDataSetWriterId: hasDataSetWriterId, // the writerheader is saved
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
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

        [Test(Description = "Validate NetworkMessageHeader & PublisherId & DataSetClassId & SingleDataSetMessage with PublisherId as parameter")]
        public void ValidateMessageHeaderPublisherIdDataSetClassIdAndSingleDataSetMessageWithFieldContentMaskWithPublisherIdParameter(
            [Values(FieldContentMaskRawData, FieldContentMaskVariant, FieldContentMaskDatavalue1, FieldContentMaskDatavalue2)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(JsonDataSetMessageContentMask.None, JsonDataSetMessageContentMask.DataSetWriterId
                | JsonDataSetMessageContentMask.MetaDataVersion
                | JsonDataSetMessageContentMask.SequenceNumber
                | JsonDataSetMessageContentMask.Status
                | JsonDataSetMessageContentMask.Timestamp)]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
                [Values(1, "abc")] object publisherId)
        {
            // Arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.PublisherId
                | JsonNetworkMessageContentMask.DataSetClassId
                | JsonNetworkMessageContentMask.SingleDataSetMessage;
            //JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId
            //    | JsonDataSetMessageContentMask.MetaDataVersion
            //    | JsonDataSetMessageContentMask.SequenceNumber
            //    | JsonDataSetMessageContentMask.Status
            //    | JsonDataSetMessageContentMask.Timestamp;

            DataSetMetaDataType dataSetMetaData = MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes");
            Assert.IsNotNull(dataSetMetaData, "dataSetMetaData should not be null");

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1, dataSetWritersCount: 3,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
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
            uaNetworkMessage.PublisherId = publisherId.ToString();
            // set DataSetClassId
            uaNetworkMessage.DataSetClassId = Guid.NewGuid().ToString();

            bool hasDataSetWriterId = (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;
            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: null, writerGroupId: 1, dataSetReadersCount: 3, setDataSetWriterId: hasDataSetWriterId, // the writerheader is saved
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
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
        public void ValidateNetworkMessageHeaderAndDataSetMessageHeaderWithFieldContentMaskParameter(
            [Values(FieldContentMaskRawData, FieldContentMaskVariant, FieldContentMaskDatavalue1, FieldContentMaskDatavalue2)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(JsonDataSetMessageContentMask.None, JsonDataSetMessageContentMask.DataSetWriterId,
                JsonDataSetMessageContentMask.MetaDataVersion, JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.Timestamp, JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion,
                JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.MetaDataVersion |JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.MetaDataVersion |JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.SequenceNumber |JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.SequenceNumber |JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.Timestamp |JsonDataSetMessageContentMask.Status)]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask)
        {
            // Arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.DataSetMessageHeader;
            //JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.None;

            DataSetMetaDataType dataSetMetaData = MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes");
            Assert.IsNotNull(dataSetMetaData, "dataSetMetaData should not be null");

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: 1, writerGroupId: 1, dataSetWritersCount: 3,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
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

            bool hasDataSetWriterId = (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;
            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: null, writerGroupId: 1, dataSetReadersCount: 3, setDataSetWriterId: hasDataSetWriterId, // the writerheader is saved
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
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
        public void ValidateNetworkAndDataSetMessageHeaderWithFieldContentMaskAndPublisherIdParameters(
            [Values(FieldContentMaskRawData, FieldContentMaskVariant, FieldContentMaskDatavalue1, FieldContentMaskDatavalue2)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(JsonDataSetMessageContentMask.None, JsonDataSetMessageContentMask.DataSetWriterId,
                JsonDataSetMessageContentMask.MetaDataVersion, JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.Timestamp, JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion,
                JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.MetaDataVersion |JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.MetaDataVersion |JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.SequenceNumber |JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.SequenceNumber |JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.Timestamp |JsonDataSetMessageContentMask.Status)]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
             [Values(1, "abc")] object publisherId)
        {
            // Arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.DataSetMessageHeader
                | JsonNetworkMessageContentMask.PublisherId;
            //JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId;

            DataSetMetaDataType dataSetMetaData = MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes");
            Assert.IsNotNull(dataSetMetaData, "dataSetMetaData should not be null");

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1, dataSetWritersCount: 3,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
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

            bool hasDataSetWriterId = (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;
            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1, dataSetReadersCount: 3, setDataSetWriterId: hasDataSetWriterId, // no headers hence the values
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
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

        [Test(Description = "Validate NetworkMessageHeader & DataSetMessageHeader & SingleDataSetMessage")]
        public void ValidateMessageHeaderDataSetMessageHeaderAndSingleDataSetMessageWithFieldContentMaskParameter(
            [Values(FieldContentMaskRawData, FieldContentMaskVariant, FieldContentMaskDatavalue1, FieldContentMaskDatavalue2)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(JsonDataSetMessageContentMask.None, JsonDataSetMessageContentMask.DataSetWriterId,
                JsonDataSetMessageContentMask.MetaDataVersion, JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.Timestamp, JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion,
                JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.MetaDataVersion |JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.MetaDataVersion |JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.SequenceNumber |JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.SequenceNumber |JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.Timestamp |JsonDataSetMessageContentMask.Status)]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask)
        {
            // Arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.DataSetMessageHeader
                | JsonNetworkMessageContentMask.SingleDataSetMessage;
            //JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId
            //    | JsonDataSetMessageContentMask.MetaDataVersion
            //    | JsonDataSetMessageContentMask.SequenceNumber
            //    | JsonDataSetMessageContentMask.Status
            //    | JsonDataSetMessageContentMask.Timestamp;

            DataSetMetaDataType dataSetMetaData = MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes");
            Assert.IsNotNull(dataSetMetaData, "dataSetMetaData should not be null");

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: 1, writerGroupId: 1, dataSetWritersCount: 3,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
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

            //bool hasDataSetWriterId = (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;
            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: null, writerGroupId: 1, dataSetReadersCount: 1, setDataSetWriterId: true, // the writerheader is saved
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
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
        public void ValidateDataSetMessageHeaderWithFieldContentMaskParameter(
            [Values(FieldContentMaskRawData, FieldContentMaskVariant, FieldContentMaskDatavalue1, FieldContentMaskDatavalue2)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(JsonDataSetMessageContentMask.None, JsonDataSetMessageContentMask.DataSetWriterId,
                JsonDataSetMessageContentMask.MetaDataVersion, JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.Timestamp, JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.MetaDataVersion,
                JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.DataSetWriterId |JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.MetaDataVersion |JsonDataSetMessageContentMask.SequenceNumber,
                JsonDataSetMessageContentMask.MetaDataVersion |JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.MetaDataVersion |JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.SequenceNumber |JsonDataSetMessageContentMask.Timestamp,
                JsonDataSetMessageContentMask.SequenceNumber |JsonDataSetMessageContentMask.Status,
                JsonDataSetMessageContentMask.Timestamp |JsonDataSetMessageContentMask.Status)]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask)
        {
            // Arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.DataSetMessageHeader;

            DataSetMetaDataType dataSetMetaData = MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes");
            Assert.IsNotNull(dataSetMetaData, "dataSetMetaData should not be null");

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: 1, writerGroupId: 1, dataSetWritersCount: 3,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            JsonNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                JsonNetworkMessage;

            bool hasDataSetWriterId = (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;
            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: null, writerGroupId: 1, dataSetReadersCount: 3, setDataSetWriterId: hasDataSetWriterId, // the writerheader is saved
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
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

        [Test(Description = "Validate SingleDataSetMessage")]
        public void ValidateSingleDataSetMessageWithFieldContentMaskParameter(
            [Values(FieldContentMaskRawData, FieldContentMaskVariant, FieldContentMaskDatavalue1, FieldContentMaskDatavalue2)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values(JsonDataSetMessageContentMask.None, JsonDataSetMessageContentMask.DataSetWriterId
                | JsonDataSetMessageContentMask.MetaDataVersion
                | JsonDataSetMessageContentMask.SequenceNumber
                | JsonDataSetMessageContentMask.Status
                | JsonDataSetMessageContentMask.Timestamp)]
                JsonDataSetMessageContentMask jsonDataSetMessageContentMask)
        {
            // Arrange
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.SingleDataSetMessage;
            //JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.DataSetWriterId;

            DataSetMetaDataType dataSetMetaData = MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes");
            Assert.IsNotNull(dataSetMetaData, "dataSetMetaData should not be null");

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: 1, writerGroupId: 1, dataSetWritersCount: 3,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
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

            bool hasDataSetWriterId = (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;
            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                MqttAddressUrl, publisherId: null, writerGroupId: 1, dataSetReadersCount: 1, setDataSetWriterId: hasDataSetWriterId, // no headers hence the values
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaData: dataSetMetaData, nameSpaceIndexForData: NamespaceIndexAllTypes);
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

        private void LoadData(UaPubSubApplication pubSubApplication)
        {
            Assert.IsNotNull(pubSubApplication, "pubSubApplication should not be null");

            #region DataSet AllTypes
            // DataSet 'AllTypes' fill with data
            DataValue allTypesBooleanValue = new DataValue(new Variant(false));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("BoolToggle", NamespaceIndexAllTypes), Attributes.Value, allTypesBooleanValue);
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
            DataValue qualifiedName = new DataValue(new Variant(new QualifiedName("wererwerw")));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("QualifiedName", NamespaceIndexAllTypes), Attributes.Value, qualifiedName);
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
            foreach (JsonDataSetMessage jsonDataSetMessage in jsonNetworkMessageEncode.DataSetMessages)
            {
                // check payload data fields count 
                // get related dataset from subscriber DataSets
                DataSet decodedDataSet = receivedDataSets.FirstOrDefault(dataSet => dataSet.Name == jsonDataSetMessage.DataSet.Name);
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
