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

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Opc.Ua.PubSub.PublishedData;
using System.IO;
using DataSet = Opc.Ua.PubSub.PublishedData.DataSet;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    [TestFixture(Description = "Tests for Encoding/Decoding of UadpNetworkMessage objects")]
    public class UadpNetworkMessageTests
    {
        private string PublisherConfigurationFileName = Path.Combine("Configuration", "PublisherConfiguration.xml");
        private string SubscriberConfigurationFileName = Path.Combine("Configuration", "SubscriberConfiguration.xml");

        private PubSubConfigurationDataType m_publisherConfiguration;
        private UaPubSubApplication m_publisherApplication;
        private WriterGroupDataType m_firstWriterGroup;
        private IUaPubSubConnection m_firstPublisherConnection;

        private PubSubConfigurationDataType m_subscriberConfiguration;
        private UaPubSubApplication m_subscriberApplication;
        private ReaderGroupDataType m_firstReaderGroup;
        private List<DataSetReaderDataType> m_firstDataSetReadersType;

        public const ushort NamespaceIndexSimple = 2;
        public const ushort NamespaceIndexAllTypes = 3;
        public const ushort NamespaceIndexMassTest = 4;

        private const uint NetworkMessageContentMask = 0x3ff;

        [OneTimeSetUp()]
        public void MyTestInitialize()
        {
            // Create a publisher application
            string publisherConfigurationFile = Utils.GetAbsoluteFilePath(PublisherConfigurationFileName, true, true, false);
            m_publisherApplication = UaPubSubApplication.Create(publisherConfigurationFile);
            Assert.IsNotNull(m_publisherApplication, "m_publisherApplication shall not be null");

            // Get the publisher configuration
            m_publisherConfiguration = m_publisherApplication.UaPubSubConfigurator.PubSubConfiguration;
            Assert.IsNotNull(m_publisherConfiguration, "m_publisherConfiguration should not be null");

            //Get first connection
            Assert.IsNotNull(m_publisherConfiguration.Connections, "m_publisherConfiguration.Connections should not be null");
            Assert.IsNotEmpty(m_publisherConfiguration.Connections, "m_publisherConfiguration.Connections should not be empty");
            m_firstPublisherConnection = m_publisherApplication.PubSubConnections[0];
            Assert.IsNotNull(m_firstPublisherConnection, "m_firstPublisherConnection should not be null");

            // Read the first writer group
            Assert.IsNotEmpty(m_publisherConfiguration.Connections[0].WriterGroups, "pubSubConfigConnection.WriterGroups should not be empty");
            m_firstWriterGroup = m_publisherConfiguration.Connections[0].WriterGroups[0];
            Assert.IsNotNull(m_firstWriterGroup, "m_firstWriterGroup should not be null");

            // Create a subscriber application
            string subscriberConfigurationFile = Utils.GetAbsoluteFilePath(SubscriberConfigurationFileName, true, true, false);
            m_subscriberApplication = UaPubSubApplication.Create(subscriberConfigurationFile);
            Assert.IsNotNull(m_subscriberApplication, "m_subscriberApplication should not be null");

            // Get the subscriber configuration
            m_subscriberConfiguration = m_subscriberApplication.UaPubSubConfigurator.PubSubConfiguration;
            Assert.IsNotNull(m_subscriberConfiguration, "m_subscriberConfiguration should not be null");

            // Get first reader group
            m_firstReaderGroup = m_subscriberConfiguration.Connections[0].ReaderGroups[0];
            Assert.IsNotNull(m_firstWriterGroup, "m_firstReaderGroup should not be null");

            m_firstDataSetReadersType = GetFirstDataSetReaders();
        }

        [Test(Description = "Validate PublisherId with supported data types")]
        public void ValidatePublisherId(
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
           [Values((byte)10, (UInt16)10, (UInt32)10, (UInt64)10, "abc", "Test$!#$%^&*87", "Begrüßung")] object publisherId)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act  
            // Check PublisherId as byte type
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.PublisherId);
            uaNetworkMessage.PublisherId = publisherId;

            // Assert
            CompareEncodeDecode(uaNetworkMessage);
        }

       [Test(Description = "Invalidate PublisherId with wrong data type")]
        public void InvalidatePublisherId([
           Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
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
           [Values((float)10, (double)10)] object publisherId)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act  
            // Check PublisherId as byte type
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.PublisherId);
            uaNetworkMessage.PublisherId = publisherId;

            // Assert
            InvalidCompareEncodeDecode(uaNetworkMessage);
        }

        [Test(Description = "Validate GroupHeader")]
        public void ValidateGroupHeader(
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
            DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act  
            // GroupFlags are changed internally by the group header options (WriterGroupId, GroupVersion, NetworkMessageNumber, SequenceNumber)
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.GroupHeader |
                                                          UadpNetworkMessageContentMask.PublisherId);
            uaNetworkMessage.PublisherId = (UInt16)10;

            // Assert
            CompareEncodeDecode(uaNetworkMessage);
        }

        

        [Test(Description = "Validate WriterGroupId")]
        public void ValidateWriterGroupIdWithVariantType(
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
            DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act  
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.WriterGroupId |
                                                          UadpNetworkMessageContentMask.PublisherId |
                                                          UadpNetworkMessageContentMask.PayloadHeader);
            uaNetworkMessage.PublisherId = (UInt16)10;
            uaNetworkMessage.WriterGroupId = 1;

            // Assert
            CompareEncodeDecode(uaNetworkMessage);
        }

        [Test(Description = "Validate GroupVersion")]
        public void ValidateGroupVersionWithVariantType(
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
            DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act  
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.GroupVersion |
                                                          UadpNetworkMessageContentMask.PublisherId |
                                                          UadpNetworkMessageContentMask.PayloadHeader);
            uaNetworkMessage.PublisherId = (UInt16)10;
            uaNetworkMessage.GroupVersion = 1;

            // Assert
            CompareEncodeDecode(uaNetworkMessage);
        }

        [Test(Description = "Validate NetworkMessageNumber")]
        public void ValidateNetworkMessageNumber(
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
            DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act  
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.NetworkMessageNumber |
                                                          UadpNetworkMessageContentMask.PublisherId |
                                                          UadpNetworkMessageContentMask.PayloadHeader);
            uaNetworkMessage.PublisherId = (UInt16)10;
            uaNetworkMessage.NetworkMessageNumber = 1;

            // Assert
            CompareEncodeDecode(uaNetworkMessage);
        }

        [Test(Description = "Validate SequenceNumber")]
        public void ValidateSequenceNumber(
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
            DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act  
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.SequenceNumber |
                                                          UadpNetworkMessageContentMask.PublisherId |
                                                          UadpNetworkMessageContentMask.PayloadHeader);
            uaNetworkMessage.PublisherId = (UInt16)10;
            uaNetworkMessage.SequenceNumber = 1;

            // Assert
            CompareEncodeDecode(uaNetworkMessage);
        }

        [Test(Description = "Validate PayloadHeader")]
        public void ValidatePayloadHeader(
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
            DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act  
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.PayloadHeader |
                                                          UadpNetworkMessageContentMask.PublisherId);
            uaNetworkMessage.PublisherId = (UInt16)10;

            // Assert
            CompareEncodeDecode(uaNetworkMessage);
        }

        [Test(Description = "Validate Timestamp")]
        public void ValidateTimestamp(
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
            DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act  
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.Timestamp |
                                                          UadpNetworkMessageContentMask.PublisherId |
                                                          UadpNetworkMessageContentMask.PayloadHeader);
            uaNetworkMessage.PublisherId = (UInt16)10;
            uaNetworkMessage.Timestamp = DateTime.UtcNow;

            // Assert
            CompareEncodeDecode(uaNetworkMessage);
        }

        [Test(Description = "Validate PicoSeconds")]
        public void ValidatePicoSeconds(
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
            DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act  
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.PicoSeconds |
                                                          UadpNetworkMessageContentMask.PublisherId |
                                                          UadpNetworkMessageContentMask.PayloadHeader);
            uaNetworkMessage.PublisherId = (UInt16)10;
            uaNetworkMessage.PicoSeconds = 10;

            // Assert
            CompareEncodeDecode(uaNetworkMessage);
        }

        [Test(Description = "Validate DataSetClassId")]
        public void ValidateDataSetClassIdWithVariantType(
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
            DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act  
            uaNetworkMessage.SetNetworkMessageContentMask(UadpNetworkMessageContentMask.DataSetClassId);
            uaNetworkMessage.DataSetClassId = Guid.NewGuid();

            // Assert
            CompareEncodeDecode(uaNetworkMessage);
        }

        #region Private Methods       

        /// <summary>
        /// Load RawData data type into datasets
        /// </summary>
        private void LoadData()
        {
            Assert.IsNotNull(m_publisherApplication, "m_publisherApplication should not be null");

            #region DataSet Simple
            // DataSet 'Simple' fill with data
            DataValue booleanValue = new DataValue(new Variant(true));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("BoolToggle", NamespaceIndexSimple), Attributes.Value, booleanValue);
            DataValue scalarInt32XValue = new DataValue(new Variant(100));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("Int32", NamespaceIndexSimple), Attributes.Value, scalarInt32XValue);
            DataValue scalarInt32YValue = new DataValue(new Variant(50));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("Int32Fast", NamespaceIndexSimple), Attributes.Value, scalarInt32YValue);
            DataValue dateTimeValue = new DataValue(new Variant(DateTime.UtcNow));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("DateTime", NamespaceIndexSimple), Attributes.Value, dateTimeValue);
            #endregion

            #region DataSet AllTypes
            // DataSet 'AllTypes' fill with data
            DataValue allTypesBooleanValue = new DataValue(new Variant(false));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("BoolToggle", NamespaceIndexAllTypes), Attributes.Value, allTypesBooleanValue);
            DataValue byteValue = new DataValue(new Variant((byte)10));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("Byte", NamespaceIndexAllTypes), Attributes.Value, byteValue);
            DataValue int16Value = new DataValue(new Variant((short)100));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("Int16", NamespaceIndexAllTypes), Attributes.Value, int16Value);
            DataValue int32Value = new DataValue(new Variant((int)1000));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("Int32", NamespaceIndexAllTypes), Attributes.Value, int32Value);
            DataValue sByteValue = new DataValue(new Variant((sbyte)11));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("SByte", NamespaceIndexAllTypes), Attributes.Value, sByteValue);
            DataValue uInt16Value = new DataValue(new Variant((ushort)110));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("UInt16", NamespaceIndexAllTypes), Attributes.Value, uInt16Value);
            DataValue uInt32Value = new DataValue(new Variant((uint)1100));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("UInt32", NamespaceIndexAllTypes), Attributes.Value, uInt32Value);
            DataValue floatValue = new DataValue(new Variant((float)1100.5));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("Float", NamespaceIndexAllTypes), Attributes.Value, floatValue);
            DataValue doubleValue = new DataValue(new Variant((double)1100));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("Double", NamespaceIndexAllTypes), Attributes.Value, doubleValue);
            #endregion

            #region DataSet MassTest 

            // DataSet 'MassTest' fill with data
            for (uint index = 0; index < 100; index++)
            {
                DataValue value = new DataValue(new Variant(index));
                m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId(string.Format("Mass_{0}", index), NamespaceIndexMassTest),
                    Attributes.Value, value);
            }
            #endregion
        }

        /// <summary>
        /// Get first DataSetReaders from configuration
        /// </summary>
        /// <returns></returns>
        private List<DataSetReaderDataType> GetFirstDataSetReaders()
        {
            // Read the first configured ReaderGroup
            Assert.IsNotNull(m_firstReaderGroup, "m_firstReaderGroup should not be null");
            Assert.IsNotEmpty(m_firstReaderGroup.DataSetReaders, "m_firstReaderGroup.DataSetReaders should not be empty");

            return m_firstReaderGroup.DataSetReaders;
        }

        /// <summary>
        /// Creates a network message (based on a configuration)
        /// </summary>
        /// <param name="dataSetFieldContentMask"></param>
        /// <returns></returns>
        private UadpNetworkMessage CreateNetworkMessage(DataSetFieldContentMask dataSetFieldContentMask)
        {
            LoadData();

            // set the configurable field content mask to allow only Variant data type
            foreach (DataSetWriterDataType dataSetWriter in m_firstWriterGroup.DataSetWriters)
            {
                // 00 The DataSet fields are encoded as Variant data type
                // The Variant can contain a StatusCode instead of the expected DataType if the status of the field is Bad.
                // The Variant can contain a DataValue with the value and the statusCode if the status of the field is Uncertain.
                dataSetWriter.DataSetFieldContentMask = (uint)dataSetFieldContentMask;
            }            

            var networkMessages = m_firstPublisherConnection.CreateNetworkMessages(m_firstWriterGroup);
            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            Assert.AreEqual(1, networkMessages.Count, "connection.CreateNetworkMessages shall return only one network message");

            UadpNetworkMessage uaNetworkMessage = networkMessages[0] as UadpNetworkMessage;

            Assert.IsNotNull(uaNetworkMessage, "networkMessageEncode should not be null");

            return uaNetworkMessage;
        }

        /// <summary>
        /// Compare encoded/decoded network messages
        /// </summary>
        /// <param name="uadpNetworkMessageEncode"></param>
        /// <param name="uadpNetworkMessageDecoded"></param>
        private void CompareEncodeDecode(UadpNetworkMessage uadpNetworkMessage)
        {
            byte[] bytes = uadpNetworkMessage.Encode();

            UadpNetworkMessage uaNetworkMessageDecoded = new UadpNetworkMessage();
            uaNetworkMessageDecoded.Decode(new ServiceMessageContext(), bytes, m_firstDataSetReadersType);            

            // compare uaNetworkMessage with uaNetworkMessageDecoded
            Compare(uadpNetworkMessage, uaNetworkMessageDecoded);
        }

        /// <summary>
        /// Invalid compare encoded/decoded network messages
        /// </summary>
        /// <param name="uadpNetworkMessageEncode"></param>
        /// <param name="uadpNetworkMessageDecoded"></param>
        private void InvalidCompareEncodeDecode(UadpNetworkMessage uadpNetworkMessage)
        {
            byte[] bytes = uadpNetworkMessage.Encode();

            UadpNetworkMessage uaNetworkMessageDecoded = new UadpNetworkMessage();
            uaNetworkMessageDecoded.Decode(new ServiceMessageContext(), bytes, m_firstDataSetReadersType);

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

            #region Payload header + Payload data

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {
                // check the number of UadpDataSetMessage counts
                Assert.AreEqual(uadpNetworkMessageEncode.DataSetMessages.Count,
                    uadpNetworkMessageDecoded.DataSetMessages.Count, "UadpDataSetMessages.Count was not decoded correctly");

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
                    DataSet decodedDataSet = uadpDataSetMessageDecoded.DataSet;
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
        /// Read All bytes from a given stream
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        private byte[] ReadBytes(Stream stream)
        {
            stream.Position = 0;
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }
        #endregion
    }
}
