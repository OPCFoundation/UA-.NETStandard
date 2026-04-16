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
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.PubSub.Transport;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    [TestFixture(
        Description = "Tests for Encoding/Decoding of UadpNetworkMessage objects using mqtt")]
    public class MqttUadpNetworkMessageTests
    {
        internal const ushort NamespaceIndexAllTypes = 3;

        internal const string MqttAddressUrl = "mqtt://localhost:1883";
        private static List<DateTime> s_publishTimes = [];

        private static readonly Variant[] s_validPublisherIds =
        [
            Variant.From((byte)1),
            Variant.From((ushort)1),
            Variant.From((uint)1),
            Variant.From((ulong)1),
            Variant.From("abc")
        ];

        [Test(Description = "Validate PublisherId with PublisherId as parameter")]
        public void ValidateMatrixEncodigWithParameters(
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
            [ValueSource(nameof(s_validPublisherIds))] Variant publisherId)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            const UadpNetworkMessageContentMask uadpNetworkMessageContentMask =
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.WriterGroupId |
                UadpNetworkMessageContentMask.PayloadHeader;

            const UadpDataSetMessageContentMask uadpDataSetMessageContentMask =
                UadpDataSetMessageContentMask.SequenceNumber;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaDataArrays("Arrays")
                //MessagesHelper.CreateDataSetMetaDataMatrices("Matrices")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: 1,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");

            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
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

            const bool hasDataSetWriterId =
                (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: 1,
                    setDataSetWriterId: hasDataSetWriterId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");
            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");

            // Assert
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders, telemetry);
        }

        [Test(Description = "Validate PublisherId with PublisherId as parameter")]
        public void ValidatePublisherIdWithWithPublisherIdParameter(
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
            Variant publisherId = (byte)1;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            const UadpNetworkMessageContentMask uadpNetworkMessageContentMask =
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.WriterGroupId;

            const UadpDataSetMessageContentMask uadpDataSetMessageContentMask =
                UadpDataSetMessageContentMask.SequenceNumber;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: 1,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");

            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
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

            const bool hasDataSetWriterId =
                (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: 1,
                    setDataSetWriterId: hasDataSetWriterId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");
            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");

            // Assert
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders, telemetry);
        }

        [Test(Description = "Validate GroupHeader with PublisherId as parameter")]
        public void ValidateGroupHeaderWithPublisherIdParameter(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData,
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            Variant publisherId = (byte)1;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            const UadpNetworkMessageContentMask uadpNetworkMessageContentMask =
                UadpNetworkMessageContentMask.GroupHeader |
                UadpNetworkMessageContentMask.PublisherId;
            const UadpDataSetMessageContentMask uadpDataSetMessageContentMask
                = UadpDataSetMessageContentMask.None;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaDataMatrices("Matrices"),
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: 1,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");

            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
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

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: 0,
                    setDataSetWriterId: false,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");
            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");

            // Assert
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders, telemetry);
        }

        [Test(Description = "Validate WriterGroupId with PublisherId as parameter")]
        public void ValidateWriterGroupIdWithPublisherIdParameter(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData,
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
            [ValueSource(nameof(s_validPublisherIds))] Variant publisherId)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            const ushort writerGroupId = 1;
            const UadpNetworkMessageContentMask uadpNetworkMessageContentMask =
                UadpNetworkMessageContentMask.WriterGroupId |
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.PayloadHeader;
            const UadpDataSetMessageContentMask uadpDataSetMessageContentMask =
                UadpDataSetMessageContentMask.SequenceNumber;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaDataArrays("DataSet1"),
                MessagesHelper.CreateDataSetMetaDataMatrices("DataSet2")
                //  MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");

            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
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

            const bool hasDataSetWriterId =
                (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    setDataSetWriterId: hasDataSetWriterId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");
            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");

            // Assert
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders, telemetry);
        }

        [Test(Description = "Validate GroupVersion with PublisherId as parameter")]
        public void ValidateGroupVersionWithPublisherIdParameter(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData,
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
            [ValueSource(nameof(s_validPublisherIds))] Variant publisherId)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            const ushort writerGroupId = 1;
            const UadpNetworkMessageContentMask uadpNetworkMessageContentMask =
                UadpNetworkMessageContentMask.WriterGroupId |
                UadpNetworkMessageContentMask.GroupVersion |
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.PayloadHeader;
            const UadpDataSetMessageContentMask uadpDataSetMessageContentMask =
                UadpDataSetMessageContentMask.SequenceNumber;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");

            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
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

            Assert.That(uaNetworkMessage, Is.Not.Null, "uaNetworkMessage should not be null");
            uaNetworkMessage.GroupVersion = 1;

            const bool hasDataSetWriterId =
                (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    setDataSetWriterId: hasDataSetWriterId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");
            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");

            // Assert
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders, telemetry);
        }

        [Test(Description = "Validate NetworkMessageNumber with PublisherId as parameter")]
        public void ValidateNetworkMessageNumberWithPublisherIdParameter(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData,
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
            [ValueSource(nameof(s_validPublisherIds))] Variant publisherId)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            const ushort writerGroupId = 1;
            const UadpNetworkMessageContentMask uadpNetworkMessageContentMask =
                UadpNetworkMessageContentMask.WriterGroupId |
                UadpNetworkMessageContentMask.NetworkMessageNumber |
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.PayloadHeader;
            const UadpDataSetMessageContentMask uadpDataSetMessageContentMask =
                UadpDataSetMessageContentMask.SequenceNumber;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");

            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
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

            Assert.That(uaNetworkMessage, Is.Not.Null, "uaNetworkMessage should not be null");
            uaNetworkMessage.NetworkMessageNumber = 1;

            const bool hasDataSetWriterId =
                (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    setDataSetWriterId: hasDataSetWriterId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");
            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");

            // Assert
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders, telemetry);
        }

        [Test(Description = "Validate SequenceNumber with PublisherId as parameter")]
        public void ValidateSequenceNumberWithPublisherIdParameter(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData,
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
            [ValueSource(nameof(s_validPublisherIds))] Variant publisherId)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            // Arrange
            const ushort writerGroupId = 1;
            const UadpNetworkMessageContentMask uadpNetworkMessageContentMask =
                UadpNetworkMessageContentMask.WriterGroupId |
                UadpNetworkMessageContentMask.SequenceNumber |
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.PayloadHeader;
            const UadpDataSetMessageContentMask uadpDataSetMessageContentMask =
                UadpDataSetMessageContentMask.SequenceNumber;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");
            var uaNetworkMessage =
                connection.CreateNetworkMessages(
                    publisherConfiguration.Connections[0].WriterGroups[0],
                    new WriterGroupPublishState()
                )[0] as UadpNetworkMessage;

            Assert.That(uaNetworkMessage, Is.Not.Null, "uaNetworkMessage should not be null");
            uaNetworkMessage.SequenceNumber = 1;

            const bool hasDataSetWriterId =
                (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    setDataSetWriterId: hasDataSetWriterId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");
            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");

            // Assert
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders, telemetry);
        }

        [Test(Description = "Validate PayloadHeader with PublisherId as parameter")]
        public void ValidatePayloadHeaderWithPublisherIdParameter(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData,
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
            [ValueSource(nameof(s_validPublisherIds))] Variant publisherId)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            const ushort writerGroupId = 1;
            const UadpNetworkMessageContentMask uadpNetworkMessageContentMask =
                UadpNetworkMessageContentMask.WriterGroupId |
                UadpNetworkMessageContentMask.PayloadHeader |
                UadpNetworkMessageContentMask.PublisherId;
            const UadpDataSetMessageContentMask uadpDataSetMessageContentMask =
                UadpDataSetMessageContentMask.SequenceNumber;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");

            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
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

            const bool hasDataSetWriterId =
                (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    setDataSetWriterId: hasDataSetWriterId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");
            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");

            // Assert
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders, telemetry);
        }

        [Test(Description = "Validate Timestamp with PublisherId as parameter")]
        public void ValidateTimestampWithPublisherIdParameter(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData,
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
            [ValueSource(nameof(s_validPublisherIds))] Variant publisherId)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            const ushort writerGroupId = 1;
            const UadpNetworkMessageContentMask uadpNetworkMessageContentMask =
                UadpNetworkMessageContentMask.WriterGroupId |
                UadpNetworkMessageContentMask.Timestamp |
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.PayloadHeader;
            const UadpDataSetMessageContentMask uadpDataSetMessageContentMask =
                UadpDataSetMessageContentMask.SequenceNumber;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");

            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
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

            Assert.That(uaNetworkMessage, Is.Not.Null, "uaNetworkMessage should not be null");
            uaNetworkMessage.Timestamp = DateTime.UtcNow;

            const bool hasDataSetWriterId =
                (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    setDataSetWriterId: hasDataSetWriterId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");
            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");

            // Assert
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders, telemetry);
        }

        [Test(Description = "Validate PicoSeconds with PublisherId as parameter")]
        public void ValidatePicoSecondsWithPublisherIdParameter(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData,
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
            [ValueSource(nameof(s_validPublisherIds))] Variant publisherId)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            const ushort writerGroupId = 1;
            const UadpNetworkMessageContentMask uadpNetworkMessageContentMask =
                UadpNetworkMessageContentMask.WriterGroupId |
                UadpNetworkMessageContentMask.PicoSeconds |
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.PayloadHeader;
            const UadpDataSetMessageContentMask uadpDataSetMessageContentMask =
                UadpDataSetMessageContentMask.SequenceNumber;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");

            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
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

            Assert.That(uaNetworkMessage, Is.Not.Null, "uaNetworkMessage should not be null");
            uaNetworkMessage.PicoSeconds = 10;

            const bool hasDataSetWriterId =
                (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    setDataSetWriterId: hasDataSetWriterId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");
            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");

            // Assert
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders, telemetry);
        }

        [Test(Description = "Validate DataSetClassId with PublisherId as parameter")]
        public void ValidateDataSetClassIdWithPublisherIdParameter(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData,
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
            [ValueSource(nameof(s_validPublisherIds))] Variant publisherId)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            const UadpNetworkMessageContentMask uadpNetworkMessageContentMask =
                UadpNetworkMessageContentMask.PublisherId;
            const UadpDataSetMessageContentMask uadpDataSetMessageContentMask
                = UadpDataSetMessageContentMask.None;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: 1,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");

            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
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

            // set DataSetClassId
            Assert.That(uaNetworkMessage, Is.Not.Null, "uaNetworkMessage should not be null");
            uaNetworkMessage.DataSetClassId = Uuid.NewUuid();

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: 0,
                    setDataSetWriterId: false,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");
            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");

            // Assert
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders, telemetry);
        }

        [Test(Description = "Validate that Uadp metadata is encoded/decoded correctly")]
        public void ValidateMetaDataIsEncodedCorrectly(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData,
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
            [ValueSource(nameof(s_validPublisherIds))] Variant publisherId)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            const UadpNetworkMessageContentMask uadpNetworkMessageContentMask =
                UadpNetworkMessageContentMask.PublisherId;
            const UadpDataSetMessageContentMask uadpDataSetMessageContentMask
                = UadpDataSetMessageContentMask.None;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("MetaData1"),
                MessagesHelper.CreateDataSetMetaData2("MetaData2"),
                MessagesHelper.CreateDataSetMetaData3("MetaData3"),
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaDataArrays("Arrays"),
                MessagesHelper.CreateDataSetMetaDataMatrices("Matrices")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: 1,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            var publishState = new WriterGroupPublishState();

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0].WriterGroups[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");
            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                publishState);
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            List<UadpNetworkMessage> uaMetaDataNetworkMessages = MessagesHelper
                .GetUadpUaMetaDataNetworkMessages(
                    [.. networkMessages.Cast<UadpNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "Uadp ua-metadata entries are missing from configuration!");

            foreach (UadpNetworkMessage uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                CompareEncodeDecodeMetaData(uaMetaDataNetworkMessage, telemetry);
            }
        }

        [Test(Description = "Validate that metadata with update time 0 is sent at startup for a MQTT Uadp publisher")]
        public void ValidateMetaDataUpdateTimeZeroSentAtStartup(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData,
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
            [ValueSource(nameof(s_validPublisherIds))] Variant publisherId)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            const UadpNetworkMessageContentMask uadpNetworkMessageContentMask =
                UadpNetworkMessageContentMask.PublisherId;
            const UadpDataSetMessageContentMask uadpDataSetMessageContentMask
                = UadpDataSetMessageContentMask.None;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("MetaData1"),
                MessagesHelper.CreateDataSetMetaData2("MetaData2"),
                MessagesHelper.CreateDataSetMetaData3("MetaData3"),
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaDataArrays("Arrays"),
                MessagesHelper.CreateDataSetMetaDataMatrices("Matrices")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: 1,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes,
                    0);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            var publishState = new WriterGroupPublishState();

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0].WriterGroups[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");

            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                publishState);
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            List<UadpNetworkMessage> uaMetaDataNetworkMessages = MessagesHelper
                .GetUadpUaMetaDataNetworkMessages(
                    [.. networkMessages.Cast<UadpNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "Uadp ua-metadata entries are missing from configuration!");

            // check if there are as many metadata messages as metadata were created in ARRAY
            Assert.That(
                uaMetaDataNetworkMessages,
                Has.Count.EqualTo(dataSetMetaDataArray.Length),
                "The ua-metadata messages count is different from the number of metadata in publisher!");
            int index = 0;
            foreach (UadpNetworkMessage uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                // compare the initial metadata with the one from the messages
                Assert.That(
                    Utils.IsEqual(
                        dataSetMetaDataArray[index],
                        uaMetaDataNetworkMessage.DataSetMetaData),
                    Is.True,
                    "Metadata from network message is different from the original one for name " +
                    dataSetMetaDataArray[index].Name);

                index++;
            }

            // get the messages again and see if there are any metadata messages
            networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                publishState);
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            uaMetaDataNetworkMessages = MessagesHelper.GetUadpUaMetaDataNetworkMessages(
                [.. networkMessages.Cast<UadpNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "Uadp ua-metadata entries are missing from configuration!");

            // check if there are any metadata messages. second time around there shall be no metadata messages
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Empty,
                "The ua-metadata messages count shall be zero for the second time when create messages is called!");
        }

        [Test(
            Description = "Validate that metadata with update time 0 is sent when the metadata changes for a MQTT Uadp publisher"
        )]
        public void ValidateMetaDataUpdateTimeZeroSentAtMetaDataChange(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData,
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask,
            [ValueSource(nameof(s_validPublisherIds))] Variant publisherId)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            const UadpNetworkMessageContentMask uadpNetworkMessageContentMask =
                UadpNetworkMessageContentMask.PublisherId;
            const UadpDataSetMessageContentMask uadpDataSetMessageContentMask
                = UadpDataSetMessageContentMask.None;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("MetaData1"),
                MessagesHelper.CreateDataSetMetaData2("MetaData2"),
                MessagesHelper.CreateDataSetMetaData3("MetaData3"),
                MessagesHelper.CreateDataSetMetaDataAllTypes("AllTypes"),
                MessagesHelper.CreateDataSetMetaDataArrays("Arrays"),
                MessagesHelper.CreateDataSetMetaDataMatrices("Matrices")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: 1,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes,
                    0);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections[0];
            Assert.That(connection, Is.Not.Null, "Pubsub first connection should not be null");

            var publishState = new WriterGroupPublishState();

            // Act
            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0].WriterGroups[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");
            IList<UaNetworkMessage> networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                publishState);
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            List<UadpNetworkMessage> uaMetaDataNetworkMessages = MessagesHelper
                .GetUadpUaMetaDataNetworkMessages(
                    [.. networkMessages.Cast<UadpNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "Uadp ua-metadata entries are missing from configuration!");

            // check if there are as many metadata messages as metadata were created in ARRAY
            Assert.That(
                uaMetaDataNetworkMessages,
                Has.Count.EqualTo(dataSetMetaDataArray.Length),
                "The ua-metadata messages count is different from the number of metadata in publisher!");
            int index = 0;
            foreach (UadpNetworkMessage uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                // compare the initial metadata with the one from the messages
                Assert.That(
                    Utils.IsEqual(
                        dataSetMetaDataArray[index],
                        uaMetaDataNetworkMessage.DataSetMetaData),
                    Is.True,
                    "Metadata from network message is different from the original one for name " +
                    dataSetMetaDataArray[index].Name);

                index++;
            }

            // get the messages again and see if there are any metadata messages
            networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                publishState);
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            uaMetaDataNetworkMessages = MessagesHelper.GetUadpUaMetaDataNetworkMessages(
                [.. networkMessages.Cast<UadpNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "Uadp ua-metadata entries are missing from configuration!");

            // check if there are any metadata messages. second time around there shall be no metadata messages
            Assert.That(
                uaMetaDataNetworkMessages.Count,
                Is.Zero,
                "The ua-metadata messages count shall be zero for the second time when create messages is called!");

            // change the metadata version
            DateTime currentDateTime = DateTime.UtcNow;
            foreach (DataSetMetaDataType dataSetMetaData in dataSetMetaDataArray)
            {
                dataSetMetaData.ConfigurationVersion.MajorVersion = ConfigurationVersionUtils
                    .CalculateVersionTime(
                        currentDateTime);
                dataSetMetaData.ConfigurationVersion.MinorVersion = dataSetMetaData
                    .ConfigurationVersion
                    .MajorVersion;
            }

            // get the messages again and see if there are any metadata messages
            networkMessages = connection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                publishState);
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "After MetaDataVersion change - connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "After MetaDataVersion change - connection.CreateNetworkMessages shall have at least one network message");

            uaMetaDataNetworkMessages = MessagesHelper.GetUadpUaMetaDataNetworkMessages(
                [.. networkMessages.Cast<UadpNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "After MetaDataVersion change - Uadp ua-metadata entries are missing from configuration!");

            // check if there are any metadata messages. second time around there shall be no metadata messages
            Assert.That(
                uaMetaDataNetworkMessages,
                Has.Count.EqualTo(dataSetMetaDataArray.Length),
                "After MetaDataVersion change - The ua-metadata messages count shall be equal to number of dataSetMetaData!");

            index = 0;
            foreach (UadpNetworkMessage uaMetaDataNetworkMessage in uaMetaDataNetworkMessages)
            {
                // compare the initial metadata with the one from the messages
                Assert.That(
                    Utils.IsEqual(
                        dataSetMetaDataArray[index],
                        uaMetaDataNetworkMessage.DataSetMetaData),
                    Is.True,
                    "After MetaDataVersion change - Metadata from network message is different from the original one for name " +
                    dataSetMetaDataArray[index].Name);

                index++;
            }
        }

        [Test(
            Description = "Validate that metadata with update time different than 0 is sent periodically for a MQTT Uadp publisher"
        )]
        [Ignore("Max deviation instable in this version.")]
        public void ValidateMetaDataUpdateTimeNonZeroIsSentPeriodically(
            [ValueSource(nameof(s_validPublisherIds))] Variant publisherId,
            [Values(100, 1000, 2000)] double metaDataUpdateTime,
            [Values(30, 40)] double maxDeviation,
            [Values(10)] int publishTimeInSeconds)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            s_publishTimes.Clear();

            // Arrange
            const UadpNetworkMessageContentMask uadpNetworkMessageContentMask =
                UadpNetworkMessageContentMask.PublisherId;
            const UadpDataSetMessageContentMask uadpDataSetMessageContentMask
                = UadpDataSetMessageContentMask.None;
            const DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            var dataSetMetaDataArray = new DataSetMetaDataType[] {
                MessagesHelper.CreateDataSetMetaData1("MetaData1") };

            // create the publisher configuration
            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    MqttAddressUrl,
                    publisherId: publisherId,
                    writerGroupId: 1,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: NamespaceIndexAllTypes,
                    0);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // create the mock IMqttPubSubConnection that will bje used to monitor hpw often the metadata will be sent
            var mockConnection = new Mock<IMqttPubSubConnection>();

            mockConnection
                .Setup(x => x.CanPublishMetaData(
                    It.IsAny<WriterGroupDataType>(),
                    It.IsAny<DataSetWriterDataType>()))
                .Returns(true);

            mockConnection
                .Setup(x =>
                    x.CreateDataSetMetaDataNetworkMessage(
                        It.IsAny<WriterGroupDataType>(),
                        It.IsAny<DataSetWriterDataType>()))
                .Callback(() => s_publishTimes.Add(DateTime.Now));

            WriterGroupDataType writerGroupDataType = publisherConfiguration.Connections[0]
                .WriterGroups[0];

            //Act
            var mqttMetaDataPublisher = new MqttMetadataPublisher(
                mockConnection.Object,
                writerGroupDataType,
                writerGroupDataType.DataSetWriters[0],
                metaDataUpdateTime,
                telemetry);
            mqttMetaDataPublisher.Start();

            //wait so many seconds
            Thread.Sleep(publishTimeInSeconds * 1000);
            mqttMetaDataPublisher.Stop();
            int faultIndex = -1;
            double faultDeviation = 0;

            s_publishTimes = [.. from t in s_publishTimes orderby t select t];

            //Assert
            for (int i = 1; i < s_publishTimes.Count; i++)
            {
                double interval = s_publishTimes[i].Subtract(s_publishTimes[i - 1])
                    .TotalMilliseconds;
                double deviation = Math.Abs(metaDataUpdateTime - interval);
                if (deviation >= maxDeviation && deviation > faultDeviation)
                {
                    faultIndex = i;
                    faultDeviation = deviation;
                }
            }

            Assert.That(
                faultIndex,
                Is.LessThan(0),
                $"publishingInterval={metaDataUpdateTime}, maxDeviation={maxDeviation}, publishTimeInSeconds={publishTimeInSeconds}, deviation[{faultIndex}] = {faultDeviation} has maximum deviation");
        }

        /// <summary>
        /// Compare encoded/decoded network messages
        /// </summary>
        /// <param name="uadpNetworkMessage">the message to encode</param>
        private static void CompareEncodeDecodeMetaData(UadpNetworkMessage uadpNetworkMessage, ITelemetryContext telemetry)
        {
            Assert.That(
                uadpNetworkMessage.IsMetaDataMessage,
                Is.True,
                "The received message is not a metadata message");

            byte[] bytes = uadpNetworkMessage.Encode(ServiceMessageContext.Create(telemetry));

            ILogger logger = telemetry.CreateLogger<MqttUadpNetworkMessageTests>();
            var uaNetworkMessageDecoded = new UadpNetworkMessage(logger);
            uaNetworkMessageDecoded.Decode(ServiceMessageContext.Create(telemetry), bytes, null);

            Assert.That(
                uaNetworkMessageDecoded.IsMetaDataMessage,
                Is.True,
                "The Decode message is not a metadata message");

            Assert.That(
                uaNetworkMessageDecoded.WriterGroupId,
                Is.EqualTo(uadpNetworkMessage.WriterGroupId),
                "The Decoded WriterId does not match encoded value");

            Assert.That(
                Utils.IsEqual(
                    uadpNetworkMessage.DataSetMetaData,
                    uaNetworkMessageDecoded.DataSetMetaData),
                Is.True,
                uadpNetworkMessage.DataSetMetaData.Name + " Decoded metadata is not equal ");
        }

        /// <summary>
        /// Compare encoded/decoded network messages
        /// </summary>
        private static void CompareEncodeDecode(
            UadpNetworkMessage uadpNetworkMessage,
            IList<DataSetReaderDataType> dataSetReaders,
            ITelemetryContext telemetry)
        {
            ILogger logger = telemetry.CreateLogger<MqttUadpNetworkMessageTests>();

            byte[] bytes = uadpNetworkMessage.Encode(ServiceMessageContext.Create(telemetry));

            var uaNetworkMessageDecoded = new UadpNetworkMessage(logger);
            uaNetworkMessageDecoded.Decode(
                ServiceMessageContext.Create(telemetry),
                bytes,
                dataSetReaders);

            // compare uaNetworkMessage with uaNetworkMessageDecoded
            Compare(uadpNetworkMessage, uaNetworkMessageDecoded);
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

            var receivedDataSetMessages = uadpNetworkMessageDecoded.DataSetMessages.ToList();

            Assert.That(receivedDataSetMessages, Is.Not.Null, "Received DataSetMessages is null");

            // check the number of UadpDataSetMessages counts
            Assert.That(
                receivedDataSetMessages,
                Has.Count.EqualTo(uadpNetworkMessageEncode.DataSetMessages.Count),
                $"UadpDataSetMessages.Count was not decoded correctly (Count = {receivedDataSetMessages.Count})");

            // check if the encoded match the received decoded DataSets
            for (int i = 0; i < receivedDataSetMessages.Count; i++)
            {
                var uadpDataSetMessage = uadpNetworkMessageEncode.DataSetMessages[
                    i] as UadpDataSetMessage;
                Assert.That(
                    uadpDataSetMessage,
                    Is.Not.Null,
                    $"DataSet [{i}] is missing from publisher datasets!");

                // check payload data fields count
                // get related dataset from subscriber DataSets
                DataSet decodedDataSet = receivedDataSetMessages[i].DataSet;
                Assert.That(
                    decodedDataSet,
                    Is.Not.Null,
                    $"DataSet '{uadpDataSetMessage?.DataSet.Name}' is missing from subscriber datasets!");

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

                    // check dataValues values
                    string fieldName = fieldEncoded.FieldMetaData.Name;

#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
                    Assert.That(
                        dataValueDecoded.Value,
                        Is.EqualTo(dataValueEncoded.Value),
                        $"Wrong: Fields[{fieldName}].DataValue.Value; DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");
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
                            $"Wrong: Fields[{fieldName}].DataValue.StatusCode; DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");
                    }

                    // check dataValues SourceTimestamp
                    if ((uadpDataSetMessage.FieldContentMask &
                        DataSetFieldContentMask.SourceTimestamp) ==
                        DataSetFieldContentMask.SourceTimestamp)
                    {
                        Assert.That(
                            dataValueDecoded.SourceTimestamp,
                            Is.EqualTo(dataValueEncoded.SourceTimestamp),
                            $"Wrong: Fields[{fieldName}].DataValue.SourceTimestamp; DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");
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
                            $"Wrong: Fields[{fieldName}].DataValue.ServerTimestamp; DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");
                    }

                    // check dataValues SourcePicoseconds
                    if ((uadpDataSetMessage.FieldContentMask &
                        DataSetFieldContentMask.SourcePicoSeconds) ==
                        DataSetFieldContentMask.SourcePicoSeconds)
                    {
                        Assert.That(
                            dataValueDecoded.SourcePicoseconds,
                            Is.EqualTo(dataValueEncoded.SourcePicoseconds),
                            $"Wrong: Fields[{fieldName}].DataValue.SourcePicoseconds; DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");
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
                            $"Wrong: Fields[{fieldName}].DataValue.ServerPicoseconds; DataSetWriterId = {uadpDataSetMessage.DataSetWriterId}");
                    }
                }
            }
        }
    }
}
