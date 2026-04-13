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
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.Tests;

using DataSet = Opc.Ua.PubSub.PublishedData.DataSet;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    [TestFixture(Description = "Tests for Encoding/Decoding of UadpNetworkMessage objects")]
    public class UadpNetworkMessageTests
    {
        private readonly string m_publisherConfigurationFileName = Path.Combine(
            "Configuration",
            "PublisherConfiguration.xml");

        private readonly string m_subscriberConfigurationFileName = Path.Combine(
            "Configuration",
            "SubscriberConfiguration.xml");

        private PubSubConfigurationDataType m_publisherConfiguration;
        private ITelemetryContext m_telemetry;
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

        [OneTimeTearDown]
        public void MyTestTearDown()
        {
            m_publisherApplication?.Dispose();
            m_subscriberApplication?.Dispose();
        }

        [OneTimeSetUp]
        public void MyTestInitialize()
        {
            // Create a publisher application
            string publisherConfigurationFile = Utils.GetAbsoluteFilePath(
                m_publisherConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);

            m_telemetry = NUnitTelemetryContext.Create();
            m_publisherApplication = UaPubSubApplication.Create(publisherConfigurationFile, m_telemetry);
            Assert.That(m_publisherApplication, Is.Not.Null, "m_publisherApplication shall not be null");

            // Get the publisher configuration
            m_publisherConfiguration = m_publisherApplication.UaPubSubConfigurator
                .PubSubConfiguration;
            Assert.That(
                m_publisherConfiguration,
                Is.Not.Null,
                "m_publisherConfiguration should not be null");

            // Get first connection
            Assert.That(
                m_publisherConfiguration.Connections.IsEmpty,
                Is.False,
                "m_publisherConfiguration.Connections should not be empty");
            m_firstPublisherConnection = m_publisherApplication.PubSubConnections[0];
            Assert.That(
                m_firstPublisherConnection,
                Is.Not.Null,
                "m_firstPublisherConnection should not be null");

            // Read the first writer group
            Assert.That(
                m_publisherConfiguration.Connections[0].WriterGroups.IsEmpty,
                Is.False,
                "pubSubConfigConnection.WriterGroups should not be empty");
            m_firstWriterGroup = m_publisherConfiguration.Connections[0].WriterGroups[0];
            Assert.That(m_firstWriterGroup, Is.Not.Null, "m_firstWriterGroup should not be null");

            // Create a subscriber application
            string subscriberConfigurationFile = Utils.GetAbsoluteFilePath(
                m_subscriberConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            m_subscriberApplication = UaPubSubApplication.Create(subscriberConfigurationFile, m_telemetry);
            Assert.That(m_subscriberApplication, Is.Not.Null, "m_subscriberApplication should not be null");

            // Get the subscriber configuration
            m_subscriberConfiguration = m_subscriberApplication.UaPubSubConfigurator
                .PubSubConfiguration;
            Assert.That(
                m_subscriberConfiguration,
                Is.Not.Null,
                "m_subscriberConfiguration should not be null");

            // Get first reader group
            m_firstReaderGroup = m_subscriberConfiguration.Connections[0].ReaderGroups[0];
            Assert.That(m_firstWriterGroup, Is.Not.Null, "m_firstReaderGroup should not be null");

            m_firstDataSetReadersType = GetFirstDataSetReaders();
        }

        private static readonly Variant[] s_validPublisherIds =
        [
            Variant.From((byte)10),
            Variant.From((ushort)10),
            Variant.From((uint)10),
            Variant.From((ulong)10),
            Variant.From((sbyte)10),
            Variant.From((short)10),
            Variant.From((int)10),
            Variant.From((long)10),
            Variant.From("abc"),
            Variant.From("Test$!#$%^&*87"),
            Variant.From("Begrüßung")
        ];

        [Test(Description = "Validate PublisherId with supported data types")]
        public void ValidatePublisherId(
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
            [ValueSource(nameof(s_validPublisherIds))]
                Variant publisherId)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act
            // Check PublisherId as byte type
            uaNetworkMessage.SetNetworkMessageContentMask(
                UadpNetworkMessageContentMask.PublisherId);
            uaNetworkMessage.PublisherId = publisherId;

            // Assert
            ILogger logger = m_telemetry.CreateLogger<UadpNetworkMessageTests>();
            CompareEncodeDecode(uaNetworkMessage, logger);
        }

        private static readonly Variant[] s_invalidPublisherIds =
        [
            Variant.From((float)10),
            Variant.From((double)10),
            Variant.From(ByteString.From(10, 20))
        ];

        [Test(Description = "Invalidate PublisherId with wrong data type")]
        public void InvalidatePublisherId(
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
            [ValueSource(nameof(s_invalidPublisherIds))]
                Variant publisherId)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act
            // Check PublisherId as byte type
            uaNetworkMessage.SetNetworkMessageContentMask(
                UadpNetworkMessageContentMask.PublisherId);
            uaNetworkMessage.PublisherId = publisherId;

            // Assert
            ILogger logger = m_telemetry.CreateLogger<UadpNetworkMessageTests>();
            InvalidCompareEncodeDecode(uaNetworkMessage, logger);
        }

        [Test(Description = "Validate GroupHeader")]
        public void ValidateGroupHeader(
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
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act
            // GroupFlags are changed internally by the group header options (WriterGroupId, GroupVersion, NetworkMessageNumber, SequenceNumber)
            uaNetworkMessage.SetNetworkMessageContentMask(
                UadpNetworkMessageContentMask.GroupHeader |
                UadpNetworkMessageContentMask.PublisherId);
            uaNetworkMessage.PublisherId = (ushort)10;

            // Assert
            ILogger logger = m_telemetry.CreateLogger<UadpNetworkMessageTests>();
            CompareEncodeDecode(uaNetworkMessage, logger);
        }

        [Test(Description = "Validate WriterGroupId")]
        public void ValidateWriterGroupIdWithVariantType(
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
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act
            uaNetworkMessage.SetNetworkMessageContentMask(
                UadpNetworkMessageContentMask.WriterGroupId |
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.PayloadHeader);
            uaNetworkMessage.PublisherId = (ushort)10;
            uaNetworkMessage.WriterGroupId = 1;

            // Assert
            ILogger logger = m_telemetry.CreateLogger<UadpNetworkMessageTests>();
            CompareEncodeDecode(uaNetworkMessage, logger);
        }

        [Test(Description = "Validate GroupVersion")]
        public void ValidateGroupVersionWithVariantType(
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
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act
            uaNetworkMessage.SetNetworkMessageContentMask(
                UadpNetworkMessageContentMask.GroupVersion |
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.PayloadHeader);
            uaNetworkMessage.PublisherId = (ushort)10;
            uaNetworkMessage.GroupVersion = 1;

            // Assert
            ILogger logger = m_telemetry.CreateLogger<UadpNetworkMessageTests>();
            CompareEncodeDecode(uaNetworkMessage, logger);
        }

        [Test(Description = "Validate NetworkMessageNumber")]
        public void ValidateNetworkMessageNumber(
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
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act
            uaNetworkMessage.SetNetworkMessageContentMask(
                UadpNetworkMessageContentMask.NetworkMessageNumber |
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.PayloadHeader);
            uaNetworkMessage.PublisherId = (ushort)10;
            uaNetworkMessage.NetworkMessageNumber = 1;

            // Assert
            ILogger logger = m_telemetry.CreateLogger<UadpNetworkMessageTests>();
            CompareEncodeDecode(uaNetworkMessage, logger);
        }

        [Test(Description = "Validate SequenceNumber")]
        public void ValidateSequenceNumber(
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
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act
            uaNetworkMessage.SetNetworkMessageContentMask(
                UadpNetworkMessageContentMask.SequenceNumber |
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.PayloadHeader);
            uaNetworkMessage.PublisherId = (ushort)10;
            uaNetworkMessage.SequenceNumber = 1;

            // Assert
            ILogger logger = m_telemetry.CreateLogger<UadpNetworkMessageTests>();
            CompareEncodeDecode(uaNetworkMessage, logger);
        }

        [Test(Description = "Validate PayloadHeader")]
        public void ValidatePayloadHeader(
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
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act
            uaNetworkMessage.SetNetworkMessageContentMask(
                UadpNetworkMessageContentMask.PayloadHeader |
                UadpNetworkMessageContentMask.PublisherId);
            uaNetworkMessage.PublisherId = (ushort)10;

            // Assert
            ILogger logger = m_telemetry.CreateLogger<UadpNetworkMessageTests>();
            CompareEncodeDecode(uaNetworkMessage, logger);
        }

        [Test(Description = "Validate Timestamp")]
        public void ValidateTimestamp(
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
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act
            uaNetworkMessage.SetNetworkMessageContentMask(
                UadpNetworkMessageContentMask.Timestamp |
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.PayloadHeader);
            uaNetworkMessage.PublisherId = (ushort)10;
            uaNetworkMessage.Timestamp = DateTime.UtcNow;

            // Assert
            ILogger logger = m_telemetry.CreateLogger<UadpNetworkMessageTests>();
            CompareEncodeDecode(uaNetworkMessage, logger);
        }

        [Test(Description = "Validate PicoSeconds")]
        public void ValidatePicoSeconds(
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
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act
            uaNetworkMessage.SetNetworkMessageContentMask(
                UadpNetworkMessageContentMask.PicoSeconds |
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.PayloadHeader);
            uaNetworkMessage.PublisherId = (ushort)10;
            uaNetworkMessage.PicoSeconds = 10;

            // Assert
            ILogger logger = m_telemetry.CreateLogger<UadpNetworkMessageTests>();
            CompareEncodeDecode(uaNetworkMessage, logger);
        }

        [Test(Description = "Validate DataSetClassId")]
        public void ValidateDataSetClassIdWithVariantType(
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
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpNetworkMessage uaNetworkMessage = CreateNetworkMessage(dataSetFieldContentMask);

            // Act
            uaNetworkMessage.SetNetworkMessageContentMask(
                UadpNetworkMessageContentMask.DataSetClassId);
            uaNetworkMessage.DataSetClassId = Uuid.NewUuid();

            // Assert
            ILogger logger = m_telemetry.CreateLogger<UadpNetworkMessageTests>();
            CompareEncodeDecode(uaNetworkMessage, logger);
        }

        /// <summary>
        /// Load RawData data type into datasets
        /// </summary>
        private void LoadData()
        {
            Assert.That(m_publisherApplication, Is.Not.Null, "m_publisherApplication should not be null");

            // DataSet 'Simple' fill with data
            var booleanValue = new DataValue(new Variant(true));
            m_publisherApplication.DataStore.WritePublishedDataItem(
                new NodeId("BoolToggle", NamespaceIndexSimple),
                Attributes.Value,
                booleanValue);
            var scalarInt32XValue = new DataValue(new Variant(100));
            m_publisherApplication.DataStore.WritePublishedDataItem(
                new NodeId("Int32", NamespaceIndexSimple),
                Attributes.Value,
                scalarInt32XValue);
            var scalarInt32YValue = new DataValue(new Variant(50));
            m_publisherApplication.DataStore.WritePublishedDataItem(
                new NodeId("Int32Fast", NamespaceIndexSimple),
                Attributes.Value,
                scalarInt32YValue);
            var dateTimeValue = new DataValue(new Variant(DateTime.UtcNow));
            m_publisherApplication.DataStore.WritePublishedDataItem(
                new NodeId("DateTime", NamespaceIndexSimple),
                Attributes.Value,
                dateTimeValue);

            // DataSet 'AllTypes' fill with data
            var allTypesBooleanValue = new DataValue(new Variant(false));
            m_publisherApplication.DataStore.WritePublishedDataItem(
                new NodeId("BoolToggle", NamespaceIndexAllTypes),
                Attributes.Value,
                allTypesBooleanValue);
            var byteValue = new DataValue(new Variant((byte)10));
            m_publisherApplication.DataStore.WritePublishedDataItem(
                new NodeId("Byte", NamespaceIndexAllTypes),
                Attributes.Value,
                byteValue);
            var int16Value = new DataValue(new Variant((short)100));
            m_publisherApplication.DataStore.WritePublishedDataItem(
                new NodeId("Int16", NamespaceIndexAllTypes),
                Attributes.Value,
                int16Value);
            var int32Value = new DataValue(new Variant(1000));
            m_publisherApplication.DataStore.WritePublishedDataItem(
                new NodeId("Int32", NamespaceIndexAllTypes),
                Attributes.Value,
                int32Value);
            var sByteValue = new DataValue(new Variant((sbyte)11));
            m_publisherApplication.DataStore.WritePublishedDataItem(
                new NodeId("SByte", NamespaceIndexAllTypes),
                Attributes.Value,
                sByteValue);
            var uInt16Value = new DataValue(new Variant((ushort)110));
            m_publisherApplication.DataStore.WritePublishedDataItem(
                new NodeId("UInt16", NamespaceIndexAllTypes),
                Attributes.Value,
                uInt16Value);
            var uInt32Value = new DataValue(new Variant((uint)1100));
            m_publisherApplication.DataStore.WritePublishedDataItem(
                new NodeId("UInt32", NamespaceIndexAllTypes),
                Attributes.Value,
                uInt32Value);
            var floatValue = new DataValue(new Variant((float)1100.5));
            m_publisherApplication.DataStore.WritePublishedDataItem(
                new NodeId("Float", NamespaceIndexAllTypes),
                Attributes.Value,
                floatValue);
            var doubleValue = new DataValue(new Variant((double)1100));
            m_publisherApplication.DataStore.WritePublishedDataItem(
                new NodeId("Double", NamespaceIndexAllTypes),
                Attributes.Value,
                doubleValue);

            // DataSet 'MassTest' fill with data
            for (uint index = 0; index < 100; index++)
            {
                var value = new DataValue(new Variant(index));
                m_publisherApplication.DataStore.WritePublishedDataItem(
                    new NodeId(Utils.Format("Mass_{0}", index), NamespaceIndexMassTest),
                    Attributes.Value,
                    value);
            }
        }

        /// <summary>
        /// Get first DataSetReaders from configuration
        /// </summary>
        private List<DataSetReaderDataType> GetFirstDataSetReaders()
        {
            // Read the first configured ReaderGroup
            Assert.That(m_firstReaderGroup, Is.Not.Null, "m_firstReaderGroup should not be null");
            Assert.That(
                m_firstReaderGroup.DataSetReaders.IsEmpty,
                Is.False,
                "m_firstReaderGroup.DataSetReaders should not be empty");

            return m_firstReaderGroup.DataSetReaders.ToList();
        }

        /// <summary>
        /// Creates a network message (based on a configuration)
        /// </summary>
        private UadpNetworkMessage CreateNetworkMessage(
            DataSetFieldContentMask dataSetFieldContentMask)
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

            IList<UaNetworkMessage> networkMessages = m_firstPublisherConnection
                .CreateNetworkMessages(
                    m_firstWriterGroup,
                    new WriterGroupPublishState());
            // filter out the metadata message
            networkMessages = [.. from m in networkMessages where !m.IsMetaDataMessage select m];
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Has.Count.EqualTo(1),
                "connection.CreateNetworkMessages shall return only one network message");

            var uaNetworkMessage = networkMessages[0] as UadpNetworkMessage;

            Assert.That(uaNetworkMessage, Is.Not.Null, "networkMessageEncode should not be null");

            return uaNetworkMessage;
        }

        /// <summary>
        /// Compare encoded/decoded network messages
        /// </summary>
        private void CompareEncodeDecode(UadpNetworkMessage uadpNetworkMessage, ILogger logger)
        {
            byte[] bytes = uadpNetworkMessage.Encode(ServiceMessageContext.Create(m_telemetry));

            var uaNetworkMessageDecoded = new UadpNetworkMessage(logger);
            uaNetworkMessageDecoded.Decode(
                ServiceMessageContext.Create(m_telemetry),
                bytes,
                m_firstDataSetReadersType);

            // compare uaNetworkMessage with uaNetworkMessageDecoded
            Compare(uadpNetworkMessage, uaNetworkMessageDecoded);
        }

        /// <summary>
        /// Invalid compare encoded/decoded network messages
        /// </summary>
        private void InvalidCompareEncodeDecode(UadpNetworkMessage uadpNetworkMessage, ILogger logger)
        {
            byte[] bytes = uadpNetworkMessage.Encode(ServiceMessageContext.Create(m_telemetry));

            var uaNetworkMessageDecoded = new UadpNetworkMessage(logger);
            uaNetworkMessageDecoded.Decode(
                ServiceMessageContext.Create(m_telemetry),
                bytes,
                m_firstDataSetReadersType);

            // compare uaNetworkMessage with uaNetworkMessageDecoded
            // TODO Fix: this might be broken after refactor
            InvalidCompare(uadpNetworkMessage, uaNetworkMessageDecoded);
        }

        /// <summary>
        /// Invalid compare network messages options (special case for PublisherId
        /// </summary>
        private static void InvalidCompare(
            UadpNetworkMessage uadpNetworkMessageEncode,
            UadpNetworkMessage uadpNetworkMessageDecoded)
        {
            UadpNetworkMessageContentMask networkMessageContentMask =
                uadpNetworkMessageEncode.NetworkMessageContentMask;

            if ((networkMessageContentMask |
                UadpNetworkMessageContentMask.None) == UadpNetworkMessageContentMask.None)
            {
                //nothing to check
                return;
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.PublisherId) ==
                UadpNetworkMessageContentMask.PublisherId)
            {
                // special case for valid PublisherId type only
                Assert.That(
                    uadpNetworkMessageDecoded.PublisherId,
                    Is.Not.EqualTo(uadpNetworkMessageEncode.PublisherId),
                    "PublisherId was not decoded correctly");
            }
        }

        /// <summary>
        /// Compare network messages options
        /// </summary>
        private static void Compare(
            UadpNetworkMessage uadpNetworkMessageEncode,
            UadpNetworkMessage uadpNetworkMessageDecoded)
        {
            UadpNetworkMessageContentMask networkMessageContentMask =
                uadpNetworkMessageEncode.NetworkMessageContentMask;

            if ((networkMessageContentMask |
                UadpNetworkMessageContentMask.None) == UadpNetworkMessageContentMask.None)
            {
                //nothing to check
                return;
            }

            // Verify flags
            Assert.That(
                uadpNetworkMessageDecoded.UADPFlags,
                Is.EqualTo(uadpNetworkMessageEncode.UADPFlags),
                "UADPFlags were not decoded correctly");

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.PublisherId) != 0)
            {
                Assert.That(
                    uadpNetworkMessageDecoded.PublisherId,
                    Is.EqualTo(uadpNetworkMessageEncode.PublisherId),
                    "PublisherId was not decoded correctly");
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.DataSetClassId) != 0)
            {
                Assert.That(
                    uadpNetworkMessageDecoded.DataSetClassId,
                    Is.EqualTo(uadpNetworkMessageEncode.DataSetClassId),
                    "DataSetClassId was not decoded correctly");
            }

            if ((
                    networkMessageContentMask &
                    (
                        UadpNetworkMessageContentMask.GroupHeader |
                        UadpNetworkMessageContentMask.WriterGroupId |
                        UadpNetworkMessageContentMask.GroupVersion |
                        UadpNetworkMessageContentMask.NetworkMessageNumber |
                        UadpNetworkMessageContentMask.SequenceNumber)
                ) != 0)
            {
                Assert.That(
                    uadpNetworkMessageDecoded.GroupFlags,
                    Is.EqualTo(uadpNetworkMessageEncode.GroupFlags),
                    "GroupFlags was not decoded correctly");
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.WriterGroupId) != 0)
            {
                Assert.That(
                    uadpNetworkMessageDecoded.WriterGroupId,
                    Is.EqualTo(uadpNetworkMessageEncode.WriterGroupId),
                    "WriterGroupId was not decoded correctly");
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.GroupVersion) != 0)
            {
                Assert.That(
                    uadpNetworkMessageDecoded.GroupVersion,
                    Is.EqualTo(uadpNetworkMessageEncode.GroupVersion),
                    "GroupVersion was not decoded correctly");
            }

            if ((networkMessageContentMask &
                UadpNetworkMessageContentMask.NetworkMessageNumber) != 0)
            {
                Assert.That(
                    uadpNetworkMessageDecoded.NetworkMessageNumber,
                    Is.EqualTo(uadpNetworkMessageEncode.NetworkMessageNumber),
                    "NetworkMessageNumber was not decoded correctly");
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.SequenceNumber) != 0)
            {
                Assert.That(
                    uadpNetworkMessageDecoded.SequenceNumber,
                    Is.EqualTo(uadpNetworkMessageEncode.SequenceNumber),
                    "SequenceNumber was not decoded correctly");
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {
                // check the number of UadpDataSetMessage counts
                Assert.That(
                    uadpNetworkMessageDecoded.DataSetMessages,
                    Has.Count.EqualTo(uadpNetworkMessageEncode.DataSetMessages.Count),
                    "UadpDataSetMessages.Count was not decoded correctly");

                // check if the encoded match the decoded DataSetWriterId's

                foreach (
                    UadpDataSetMessage uadpDataSetMessage in uadpNetworkMessageEncode
                        .DataSetMessages
                        .OfType<UadpDataSetMessage>())
                {
                    var uadpDataSetMessageDecoded =
                        uadpNetworkMessageDecoded.DataSetMessages.FirstOrDefault(decoded =>
                            decoded.DataSetWriterId == uadpDataSetMessage.DataSetWriterId
                        ) as UadpDataSetMessage;

                    Assert.That(
                        uadpDataSetMessageDecoded,
                        Is.Not.Null,
                        $"Decoded message did not found uadpDataSetMessage.DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");

                    // check payload data size in bytes
                    Assert.That(
                        uadpDataSetMessageDecoded.PayloadSizeInStream,
                        Is.EqualTo(uadpDataSetMessage.PayloadSizeInStream),
                        $"PayloadSizeInStream was not decoded correctly, DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");

                    // check payload data fields count
                    // get related dataset from subscriber DataSets
                    DataSet decodedDataSet = uadpDataSetMessageDecoded.DataSet;
                    Assert.That(
                        decodedDataSet,
                        Is.Not.Null,
                        $"DataSet '{uadpDataSetMessage.DataSet.Name}' is missing from subscriber datasets!");

                    Assert.That(
                        decodedDataSet.Fields,
                        Has.Length.EqualTo(uadpDataSetMessage.DataSet.Fields.Length),
                        $"DataSet.Fields.Length was not decoded correctly, DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");

                    // check the fields data consistency
                    // at this time the DataSetField has just value!?
                    for (int index = 0; index < uadpDataSetMessage.DataSet.Fields.Length; index++)
                    {
                        Field fieldEncoded = uadpDataSetMessage.DataSet.Fields[index];
                        Field fieldDecoded = decodedDataSet.Fields[index];
                        Assert.That(
                            fieldEncoded,
                            Is.Not.Null,
                            $"uadpDataSetMessage.DataSet.Fields[{index}] is null,  DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");
                        Assert.That(
                            fieldDecoded,
                            Is.Not.Null,
                            $"uadpDataSetMessageDecoded.DataSet.Fields[{index}] is null,  DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");

                        DataValue dataValueEncoded = fieldEncoded.Value;
                        DataValue dataValueDecoded = fieldDecoded.Value;
                        Assert.That(
                            fieldEncoded.Value,
                            Is.Not.Null,
                            $"uadpDataSetMessage.DataSet.Fields[{index}].Value is null,  DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");
                        Assert.That(
                            fieldDecoded.Value,
                            Is.Not.Null,
                            $"uadpDataSetMessageDecoded.DataSet.Fields[{index}].Value is null,  DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");

                        // check dataValues values
#pragma warning disable CS0618 // Type or member is obsolete
                        Assert.That(
                            fieldEncoded.Value.Value,
                            Is.Not.Null,
                            $"uadpDataSetMessage.DataSet.Fields[{index}].Value is null,  DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
                        Assert.That(
                            fieldDecoded.Value.Value,
                            Is.Not.Null,
                            $"uadpDataSetMessageDecoded.DataSet.Fields[{index}].Value is null,  DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");
#pragma warning restore CS0618 // Type or member is obsolete

#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
                        Assert.That(
                            dataValueDecoded.Value,
                            Is.EqualTo(dataValueEncoded.Value),
                            $"Wrong: Fields[{index}].DataValue.Value; DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning restore CS0618 // Type or member is obsolete

                        // Checks just for DataValue type only
                        if ((uadpDataSetMessage.FieldContentMask &
                            DataSetFieldContentMask.StatusCode) ==
                            DataSetFieldContentMask.StatusCode)
                        {
                            // check dataValues StatusCode
                            Assert.That(
                                dataValueDecoded.StatusCode,
                                Is.EqualTo(dataValueEncoded.StatusCode),
                                $"Wrong: Fields[{index}].DataValue.StatusCode; DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");
                        }

                        // check dataValues SourceTimestamp
                        if ((uadpDataSetMessage.FieldContentMask &
                            DataSetFieldContentMask.SourceTimestamp) ==
                            DataSetFieldContentMask.SourceTimestamp)
                        {
                            Assert.That(
                                dataValueDecoded.SourceTimestamp,
                                Is.EqualTo(dataValueEncoded.SourceTimestamp),
                                $"Wrong: Fields[{index}].DataValue.SourceTimestamp; DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");
                        }

                        // check dataValues ServerTimestamp
                        if ((uadpDataSetMessage.FieldContentMask &
                            DataSetFieldContentMask.ServerTimestamp) ==
                            DataSetFieldContentMask.ServerTimestamp)
                        {
                            // check dataValues ServerTimestamp
                            Assert.That(
                                dataValueDecoded.ServerTimestamp,
                                Is.EqualTo(dataValueEncoded.ServerTimestamp),
                                $"Wrong: Fields[{index}].DataValue.ServerTimestamp; DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");
                        }

                        // check dataValues SourcePicoseconds
                        if ((uadpDataSetMessage.FieldContentMask &
                            DataSetFieldContentMask.SourcePicoSeconds) ==
                            DataSetFieldContentMask.SourcePicoSeconds)
                        {
                            Assert.That(
                                dataValueDecoded.SourcePicoseconds,
                                Is.EqualTo(dataValueEncoded.SourcePicoseconds),
                                $"Wrong: Fields[{index}].DataValue.SourcePicoseconds; DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");
                        }

                        // check dataValues ServerPicoSeconds
                        if ((uadpDataSetMessage.FieldContentMask &
                            DataSetFieldContentMask.ServerPicoSeconds) ==
                            DataSetFieldContentMask.ServerPicoSeconds)
                        {
                            // check dataValues ServerPicoseconds
                            Assert.That(
                                dataValueDecoded.ServerPicoseconds,
                                Is.EqualTo(dataValueEncoded.ServerPicoseconds),
                                $"Wrong: Fields[{index}].DataValue.ServerPicoseconds; DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");
                        }
                    }
                }
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.Timestamp) != 0)
            {
                Assert.That(
                    uadpNetworkMessageDecoded.Timestamp,
                    Is.EqualTo(uadpNetworkMessageEncode.Timestamp),
                    "Timestamp was not decoded correctly");
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.PicoSeconds) != 0)
            {
                Assert.That(
                    uadpNetworkMessageDecoded.PicoSeconds,
                    Is.EqualTo(uadpNetworkMessageEncode.PicoSeconds),
                    "PicoSeconds was not decoded correctly");
            }
        }
    }

    [TestFixture]
    public class UadpNetworkMessageAdditionalTests
    {
        private const UadpNetworkMessageContentMask AllContentMask =
            UadpNetworkMessageContentMask.PublisherId |
            UadpNetworkMessageContentMask.GroupHeader |
            UadpNetworkMessageContentMask.WriterGroupId |
            UadpNetworkMessageContentMask.GroupVersion |
            UadpNetworkMessageContentMask.NetworkMessageNumber |
            UadpNetworkMessageContentMask.SequenceNumber |
            UadpNetworkMessageContentMask.PayloadHeader |
            UadpNetworkMessageContentMask.Timestamp |
            UadpNetworkMessageContentMask.PicoSeconds |
            UadpNetworkMessageContentMask.DataSetClassId |
            UadpNetworkMessageContentMask.PromotedFields;

        private static readonly ushort[] SampleWriterIds = [1, 2];

        private static readonly StatusCode[] SampleStatusCodes =
            [StatusCodes.Good, StatusCodes.Good];

        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void ConstructorDataSetMessageSetsDefaults()
        {
            var writerGroup = CreateWriterGroup(UadpNetworkMessageContentMask.PublisherId);
            var messages = new List<UadpDataSetMessage>();

            var message = new UadpNetworkMessage(writerGroup, messages);

            Assert.That(message.UADPNetworkMessageType,
                Is.EqualTo(UADPNetworkMessageType.DataSetMessage));
            Assert.That(message.UADPVersion, Is.EqualTo(1));
        }

        [Test]
        public void ConstructorDiscoveryRequestSetsType()
        {
            var message = new UadpNetworkMessage(
                UADPNetworkMessageDiscoveryType.DataSetMetaData);

            Assert.That(message.UADPNetworkMessageType,
                Is.EqualTo(UADPNetworkMessageType.DiscoveryRequest));
            Assert.That(message.UADPDiscoveryType,
                Is.EqualTo(UADPNetworkMessageDiscoveryType.DataSetMetaData));
        }

        [Test]
        public void ConstructorDiscoveryResponseMetaDataSetsType()
        {
            var writerGroup = CreateWriterGroup(UadpNetworkMessageContentMask.PublisherId);
            var metadata = new DataSetMetaDataType { Name = "TestMeta" };

            var message = new UadpNetworkMessage(writerGroup, metadata);

            Assert.That(message.UADPNetworkMessageType,
                Is.EqualTo(UADPNetworkMessageType.DiscoveryResponse));
            Assert.That(message.UADPDiscoveryType,
                Is.EqualTo(UADPNetworkMessageDiscoveryType.DataSetMetaData));
        }

        [Test]
        public void ConstructorDiscoveryResponsePublisherEndpointsSetsType()
        {
            var endpoints = new[] { new EndpointDescription() };

            var message = new UadpNetworkMessage(endpoints, StatusCodes.Good);

            Assert.That(message.UADPNetworkMessageType,
                Is.EqualTo(UADPNetworkMessageType.DiscoveryResponse));
            Assert.That(message.UADPDiscoveryType,
                Is.EqualTo(UADPNetworkMessageDiscoveryType.PublisherEndpoint));
        }

        [Test]
        public void ConstructorDiscoveryResponseWriterConfigSetsType()
        {
            var writerGroup = CreateWriterGroup(UadpNetworkMessageContentMask.PublisherId);

            var message = new UadpNetworkMessage(
                SampleWriterIds, writerGroup, SampleStatusCodes);

            Assert.That(message.UADPNetworkMessageType,
                Is.EqualTo(UADPNetworkMessageType.DiscoveryResponse));
            Assert.That(message.UADPDiscoveryType,
                Is.EqualTo(UADPNetworkMessageDiscoveryType.DataSetWriterConfiguration));
        }

        [Test]
        public void PublisherIdByte()
        {
            var message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);
            message.PublisherId = Variant.From((byte)1);

            Assert.That(message.PublisherId.GetByte(), Is.EqualTo(1));
        }

        [Test]
        public void PublisherIdUInt16()
        {
            var message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);
            message.PublisherId = Variant.From((ushort)100);

            Assert.That(message.PublisherId.GetUInt16(), Is.EqualTo(100));
        }

        [Test]
        public void PublisherIdUInt32()
        {
            var message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);
            message.PublisherId = Variant.From((uint)1000);

            Assert.That(message.PublisherId.GetUInt32(), Is.EqualTo(1000));
        }

        [Test]
        public void PublisherIdUInt64()
        {
            var message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);
            message.PublisherId = Variant.From((ulong)10000);

            Assert.That(message.PublisherId.GetUInt64(), Is.EqualTo(10000));
        }

        [Test]
        public void PublisherIdString()
        {
            var message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);
            message.PublisherId = Variant.From("publisher1");

            Assert.That(message.PublisherId.GetString(), Is.EqualTo("publisher1"));
        }

        [Test]
        public void PublisherIdSignedByteCast()
        {
            var message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);
            message.PublisherId = Variant.From((sbyte)5);

            Assert.That(message.PublisherId.TryGet(out byte result), Is.True);
            Assert.That(result, Is.EqualTo(5));
        }

        [Test]
        public void PublisherIdSignedInt16Cast()
        {
            var message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);
            message.PublisherId = Variant.From((short)100);

            Assert.That(message.PublisherId.TryGet(out ushort result), Is.True);
            Assert.That(result, Is.EqualTo(100));
        }

        [Test]
        public void PublisherIdSignedInt32Cast()
        {
            var message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);
            message.PublisherId = Variant.From((int)1000);

            Assert.That(message.PublisherId.TryGet(out uint result), Is.True);
            Assert.That(result, Is.EqualTo(1000));
        }

        [Test]
        public void PublisherIdSignedInt64Cast()
        {
            var message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);
            message.PublisherId = Variant.From((long)10000);

            Assert.That(message.PublisherId.TryGet(out ulong result), Is.True);
            Assert.That(result, Is.EqualTo(10000));
        }

        [Test]
        public void SetNetworkMessageContentMaskPublisherId()
        {
            var message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);

            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.PublisherId), Is.True);
            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.ExtendedFlags1), Is.True);
        }

        [Test]
        public void SetNetworkMessageContentMaskGroupHeader()
        {
            var message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.GroupHeader);

            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.GroupHeader), Is.True);
        }

        [Test]
        public void SetNetworkMessageContentMaskWriterGroupId()
        {
            var message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.WriterGroupId);

            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.GroupHeader), Is.True);
            Assert.That(message.GroupFlags.HasFlag(
                GroupFlagsEncodingMask.WriterGroupId), Is.True);
        }

        [Test]
        public void SetNetworkMessageContentMaskTimestamp()
        {
            var message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.Timestamp);

            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.ExtendedFlags1), Is.True);
            Assert.That(message.ExtendedFlags1.HasFlag(
                ExtendedFlags1EncodingMask.Timestamp), Is.True);
        }

        [Test]
        public void SetNetworkMessageContentMaskPicoSeconds()
        {
            var message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PicoSeconds);

            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.ExtendedFlags1), Is.True);
            Assert.That(message.ExtendedFlags1.HasFlag(
                ExtendedFlags1EncodingMask.PicoSeconds), Is.True);
        }

        [Test]
        public void SetNetworkMessageContentMaskPromotedFields()
        {
            var message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PromotedFields);

            Assert.That(message.ExtendedFlags1.HasFlag(
                ExtendedFlags1EncodingMask.ExtendedFlags2), Is.True);
            Assert.That(message.ExtendedFlags2.HasFlag(
                ExtendedFlags2EncodingMask.PromotedFields), Is.True);
        }

        [Test]
        public void SetNetworkMessageContentMaskPayloadHeader()
        {
            var message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PayloadHeader);

            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.PayloadHeader), Is.True);
        }

        [Test]
        public void SetNetworkMessageContentMaskAll()
        {
            var message = CreateDataSetNetworkMessage(AllContentMask);

            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.PublisherId), Is.True);
            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.GroupHeader), Is.True);
            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.PayloadHeader), Is.True);
            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.ExtendedFlags1), Is.True);
            Assert.That(message.GroupFlags.HasFlag(
                GroupFlagsEncodingMask.WriterGroupId), Is.True);
            Assert.That(message.GroupFlags.HasFlag(
                GroupFlagsEncodingMask.GroupVersion), Is.True);
            Assert.That(message.GroupFlags.HasFlag(
                GroupFlagsEncodingMask.NetworkMessageNumber), Is.True);
            Assert.That(message.GroupFlags.HasFlag(
                GroupFlagsEncodingMask.SequenceNumber), Is.True);
            Assert.That(message.ExtendedFlags1.HasFlag(
                ExtendedFlags1EncodingMask.Timestamp), Is.True);
            Assert.That(message.ExtendedFlags1.HasFlag(
                ExtendedFlags1EncodingMask.PicoSeconds), Is.True);
            Assert.That(message.ExtendedFlags2.HasFlag(
                ExtendedFlags2EncodingMask.PromotedFields), Is.True);
        }

        [Test]
        public void EncodeDecodeDataSetMessageRoundTrip()
        {
            UadpNetworkMessageContentMask contentMask =
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.GroupHeader |
                UadpNetworkMessageContentMask.WriterGroupId |
                UadpNetworkMessageContentMask.GroupVersion |
                UadpNetworkMessageContentMask.NetworkMessageNumber |
                UadpNetworkMessageContentMask.SequenceNumber |
                UadpNetworkMessageContentMask.PayloadHeader |
                UadpNetworkMessageContentMask.Timestamp |
                UadpNetworkMessageContentMask.PicoSeconds;

            var writerGroup = CreateWriterGroup(contentMask);
            var dataSetMessage = CreateSimpleDataSetMessage();
            var messages = new List<UadpDataSetMessage> { dataSetMessage };

            var networkMessage = new UadpNetworkMessage(writerGroup, messages);
            networkMessage.SetNetworkMessageContentMask(contentMask);
            networkMessage.PublisherId = Variant.From((ushort)100);
            networkMessage.WriterGroupId = 1;
            networkMessage.GroupVersion = 1;
            networkMessage.NetworkMessageNumber = 1;
            networkMessage.SequenceNumber = 1;

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            byte[] encoded = networkMessage.Encode(context);
            Assert.That(encoded, Is.Not.Null);
            Assert.That(encoded.Length, Is.GreaterThan(0));

            var decodedMessage = new UadpNetworkMessage(writerGroup, []);
            decodedMessage.SetNetworkMessageContentMask(contentMask);

            var readers = CreateMatchingReaders(dataSetMessage);
            decodedMessage.Decode(context, encoded, readers);

            Assert.That(decodedMessage.UADPNetworkMessageType,
                Is.EqualTo(UADPNetworkMessageType.DataSetMessage));
            Assert.That(decodedMessage.PublisherId.GetUInt16(), Is.EqualTo(100));
        }

        [Test]
        public void EncodeDecodeDiscoveryRequestRoundTrip()
        {
            var message = new UadpNetworkMessage(
                UADPNetworkMessageDiscoveryType.DataSetMetaData);
            message.PublisherId = Variant.From((ushort)50);

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            byte[] encoded = message.Encode(context);
            Assert.That(encoded, Is.Not.Null);
            Assert.That(encoded.Length, Is.GreaterThan(0));

            var decoded = new UadpNetworkMessage(
                UADPNetworkMessageDiscoveryType.DataSetMetaData);
            decoded.Decode(context, encoded, null);

            Assert.That(decoded.UADPNetworkMessageType,
                Is.EqualTo(UADPNetworkMessageType.DiscoveryRequest));
        }

        [Test]
        public void EncodeDecodeDiscoveryResponseMetaDataRoundTrip()
        {
            var writerGroup = CreateWriterGroup(UadpNetworkMessageContentMask.PublisherId);
            var metadata = new DataSetMetaDataType
            {
                Name = "TestMeta",
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                }
            };

            var message = new UadpNetworkMessage(writerGroup, metadata);
            message.PublisherId = Variant.From((ushort)10);
            message.DataSetWriterId = 1;

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            byte[] encoded = message.Encode(context);
            Assert.That(encoded, Is.Not.Null);
            Assert.That(encoded.Length, Is.GreaterThan(0));
        }

        [Test]
        public void EncodeDecodeDiscoveryResponsePublisherEndpointsRoundTrip()
        {
            var endpoints = new[] { new EndpointDescription { EndpointUrl = "opc.tcp://localhost:4840" } };

            var message = new UadpNetworkMessage(endpoints, StatusCodes.Good);
            message.PublisherId = Variant.From((ushort)20);

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            byte[] encoded = message.Encode(context);
            Assert.That(encoded, Is.Not.Null);
            Assert.That(encoded.Length, Is.GreaterThan(0));
        }

        [Test]
        public void EncodeDecodeDiscoveryResponseWriterConfigRoundTrip()
        {
            var writerGroup = CreateWriterGroup(UadpNetworkMessageContentMask.PublisherId);

            var message = new UadpNetworkMessage(
                SampleWriterIds, writerGroup, SampleStatusCodes);
            message.PublisherId = Variant.From((ushort)30);

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            byte[] encoded = message.Encode(context);
            Assert.That(encoded, Is.Not.Null);
            Assert.That(encoded.Length, Is.GreaterThan(0));
        }

        [Test]
        public void EncodeToByteArrayMatchesStreamEncode()
        {
            UadpNetworkMessageContentMask contentMask =
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.PayloadHeader;

            var writerGroup = CreateWriterGroup(contentMask);
            var dataSetMessage = CreateSimpleDataSetMessage();
            var messages = new List<UadpDataSetMessage> { dataSetMessage };

            var networkMessage = new UadpNetworkMessage(writerGroup, messages);
            networkMessage.SetNetworkMessageContentMask(contentMask);
            networkMessage.PublisherId = Variant.From((byte)1);
            networkMessage.WriterGroupId = 1;

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            byte[] fromByteArray = networkMessage.Encode(context);

            using var stream = new MemoryStream();
            networkMessage.Encode(context, stream);
            byte[] fromStream = stream.ToArray();

            Assert.That(fromByteArray, Is.EqualTo(fromStream));
        }

        [Test]
        public void DecodeWithNullReadersReturnsEarly()
        {
            UadpNetworkMessageContentMask contentMask =
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.PayloadHeader;

            var writerGroup = CreateWriterGroup(contentMask);
            var dataSetMessage = CreateSimpleDataSetMessage();
            var messages = new List<UadpDataSetMessage> { dataSetMessage };

            var networkMessage = new UadpNetworkMessage(writerGroup, messages);
            networkMessage.SetNetworkMessageContentMask(contentMask);
            networkMessage.PublisherId = Variant.From((byte)1);

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            byte[] encoded = networkMessage.Encode(context);

            var decoded = new UadpNetworkMessage(writerGroup, []);
            decoded.SetNetworkMessageContentMask(contentMask);
            decoded.Decode(context, encoded, null);

            Assert.That(decoded.DataSetMessages, Has.Count.EqualTo(0));
        }

        private static WriterGroupDataType CreateWriterGroup(
            UadpNetworkMessageContentMask contentMask)
        {
            return new WriterGroupDataType
            {
                WriterGroupId = 1,
                MessageSettings = new ExtensionObject(
                    new UadpWriterGroupMessageDataType
                    {
                        NetworkMessageContentMask = (uint)contentMask
                    })
            };
        }

        private static UadpNetworkMessage CreateDataSetNetworkMessage(
            UadpNetworkMessageContentMask contentMask)
        {
            var writerGroup = CreateWriterGroup(contentMask);
            var message = new UadpNetworkMessage(writerGroup, []);
            message.SetNetworkMessageContentMask(contentMask);
            return message;
        }

        private static UadpDataSetMessage CreateSimpleDataSetMessage()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "Int32Field",
                    BuiltInType = (byte)BuiltInType.Int32
                },
                Value = new DataValue(Variant.From(42))
            };

            var dataSet = new DataSet("TestDataSet")
            {
                Fields = [field]
            };

            var dataSetMessage = new UadpDataSetMessage(dataSet);
            dataSetMessage.SetFieldContentMask(DataSetFieldContentMask.None);
            dataSetMessage.SetMessageContentMask(
                UadpDataSetMessageContentMask.SequenceNumber |
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion);
            dataSetMessage.MetaDataVersion = new ConfigurationVersionDataType
            {
                MajorVersion = 1,
                MinorVersion = 1
            };
            dataSetMessage.DataSetWriterId = 1;

            return dataSetMessage;
        }

        private static List<DataSetReaderDataType> CreateMatchingReaders(
            UadpDataSetMessage dataSetMessage)
        {
            var metaData = new DataSetMetaDataType
            {
                ConfigurationVersion = dataSetMessage.MetaDataVersion,
                Fields = [dataSetMessage.DataSet.Fields[0].FieldMetaData]
            };

            var reader = new DataSetReaderDataType
            {
                DataSetWriterId = dataSetMessage.DataSetWriterId,
                WriterGroupId = 1,
                DataSetMetaData = metaData,
                MessageSettings = new ExtensionObject(
                    new UadpDataSetReaderMessageDataType
                    {
                        DataSetMessageContentMask = (uint)(
                            UadpDataSetMessageContentMask.SequenceNumber |
                            UadpDataSetMessageContentMask.MajorVersion |
                            UadpDataSetMessageContentMask.MinorVersion),
                        NetworkMessageContentMask = (uint)(
                            UadpNetworkMessageContentMask.PublisherId |
                            UadpNetworkMessageContentMask.GroupHeader |
                            UadpNetworkMessageContentMask.WriterGroupId |
                            UadpNetworkMessageContentMask.GroupVersion |
                            UadpNetworkMessageContentMask.NetworkMessageNumber |
                            UadpNetworkMessageContentMask.SequenceNumber |
                            UadpNetworkMessageContentMask.PayloadHeader |
                            UadpNetworkMessageContentMask.Timestamp |
                            UadpNetworkMessageContentMask.PicoSeconds)
                    })
            };

            return [reader];
        }
    }
}