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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.PubSub.Tests.Encoding;
using Opc.Ua.PubSub.Transport;
using Opc.Ua.Tests;

using PubSubEncoding = Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.Transport
{
    [TestFixture(Description = "Tests for Mqtt connections")]
    public partial class MqttPubSubConnectionTests
    {
        private const ushort kNamespaceIndexAllTypes = 3;

        private ManualResetEvent m_uaDataShutdownEvent;
        private ManualResetEvent m_uaDeltaDataShutdownEvent;
        private ManualResetEvent m_uaMetaDataShutdownEvent;
        private ManualResetEvent m_uaConfigurationUpdateEvent;
        private bool m_isDeltaFrame;
        private Dictionary<NodeId, DataValue> m_snapshotData;
        private const int kEstimatedPublishingTime = 60000;

        internal const string DefaultBrokerProcessName = "mosquitto";
        internal const string MqttUrlFormat = $"{Utils.UriSchemeMqtt}://{{0}}:1883";

        private static readonly Variant[] s_validPublisherIds =
        [
            Variant.From((byte)1),
            Variant.From((ushort)1),
            Variant.From((uint)1),
            Variant.From((ulong)1),
            Variant.From("abc")
        ];

        [TearDown]
        public void MyTestTearDown()
        {
            m_uaConfigurationUpdateEvent?.Dispose();
            m_uaMetaDataShutdownEvent?.Dispose();
            m_uaDeltaDataShutdownEvent?.Dispose();
            m_uaDataShutdownEvent?.Dispose();
        }

        [OneTimeSetUp]
        public void MyTestInitialize()
        {
        }

        [Test(Description = "Validate mqtt local pub/sub connection with uadp data.")]
#if !CUSTOM_TESTS
        [Ignore("A mosquitto tool should be installed local in order to run correctly.")]
#endif
        public void ValidateMqttLocalPubSubConnectionWithUadp(
            [ValueSource(nameof(s_validPublisherIds))] Variant publisherId)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Process _ = RestartMosquitto();

            //Arrange
            const ushort writerGroupId = 1;

            string mqttLocalBrokerUrl = Utils.Format(
                MqttUrlFormat,
                "localhost");

            var mqttConfiguration = new MqttClientProtocolConfiguration(
                version: EnumMqttProtocolVersion.V500);

            const UadpNetworkMessageContentMask uadpNetworkMessageContentMask =
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.WriterGroupId |
                UadpNetworkMessageContentMask.PayloadHeader;
            const UadpDataSetMessageContentMask uadpDataSetMessageContentMask
                = UadpDataSetMessageContentMask.None;

            const DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    mqttLocalBrokerUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(
                publisherConfiguration,
                publisherId);
            Assert.That(mqttPublisherConnection, Is.Not.Null, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.ConnectionProperties;
            Assert.That(
                mqttPublisherConnection.ConnectionProperties.IsNull,
                Is.False,
                "The MQTT publisher connection properties are not valid.");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections[0];
            Assert.That(publisherConnection, Is.Not.Null, "Publisher first connection should not be null");

            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0].WriterGroups[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");

            IList<UaNetworkMessage> networkMessages = publisherConnection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                new WriterGroupPublishState());
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            var uaNetworkMessage = networkMessages[0] as PubSubEncoding.UadpNetworkMessage;
            Assert.That(uaNetworkMessage, Is.Not.Null, "networkMessageEncode should not be null");

            const bool hasDataSetWriterId =
                (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    mqttLocalBrokerUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    setDataSetWriterId: hasDataSetWriterId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubscriberConnection = MessagesHelper.GetConnection(
                subscriberConfiguration,
                publisherId);
            Assert.That(
                mqttSubscriberConnection,
                Is.Not.Null,
                "The MQTT subscriber connection is invalid.");
            mqttSubscriberConnection.ConnectionProperties = mqttConfiguration.ConnectionProperties;
            Assert.That(
                mqttSubscriberConnection.ConnectionProperties.IsNull,
                Is.False,
                "The MQTT subscriber connection properties are not valid.");

            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections[0];
            Assert.That(
                subscriberConnection,
                Is.Not.Null,
                "Subscriber first connection should not be null");

            //Act
            // it will signal if the uadp message was received from local ip
            m_uaDataShutdownEvent = new ManualResetEvent(false);

            m_isDeltaFrame = false;
            subscriberApplication.DataReceived += UaPubSubApplication_DataReceived;
            subscriberConnection.Start();

            publisherConnection.Start();

            //Assert
            if (!m_uaDataShutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert.Fail("The UADP message was not received");
            }

            subscriberConnection.Stop();
            publisherConnection.Stop();
        }

        [Test(Description = "Validate mqtt local pub/sub connection with uadp data.")]
#if !CUSTOM_TESTS
        [Ignore("A mosquitto tool should be installed local in order to run correctly.")]
#endif
        public void ValidateMqttLocalPubSubConnectionWithDeltaUadp(
            [ValueSource(nameof(s_validPublisherIds))] Variant publisherId,
            [Values(1, 2, 3, 4)] int keyFrameCount)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Process _ = RestartMosquitto();

            //Arrange
            const ushort writerGroupId = 1;

            string mqttLocalBrokerUrl = Utils.Format(
                Utils.UriSchemeMqtt,
                "localhost");

            var mqttConfiguration = new MqttClientProtocolConfiguration(
                version: EnumMqttProtocolVersion.V500);

            const UadpNetworkMessageContentMask uadpNetworkMessageContentMask =
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.WriterGroupId |
                UadpNetworkMessageContentMask.PayloadHeader;
            const UadpDataSetMessageContentMask uadpDataSetMessageContentMask
                = UadpDataSetMessageContentMask.None;

            const DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    mqttLocalBrokerUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes,
                    keyFrameCount: Convert.ToUInt32(keyFrameCount));
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(
                publisherConfiguration,
                publisherId);
            Assert.That(mqttPublisherConnection, Is.Not.Null, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.ConnectionProperties;
            Assert.That(
                mqttPublisherConnection.ConnectionProperties.IsNull,
                Is.False,
                "The MQTT publisher connection properties are not valid.");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections[0];
            Assert.That(publisherConnection, Is.Not.Null, "Publisher first connection should not be null");

            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0].WriterGroups[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");

            IList<UaNetworkMessage> networkMessages = publisherConnection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                new WriterGroupPublishState());
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            var uaNetworkMessage = networkMessages[0] as PubSubEncoding.UadpNetworkMessage;
            Assert.That(uaNetworkMessage, Is.Not.Null, "networkMessageEncode should not be null");

            const bool hasDataSetWriterId =
                (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    mqttLocalBrokerUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    setDataSetWriterId: hasDataSetWriterId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes,
                    keyFrameCount: Convert.ToUInt32(keyFrameCount));
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubscriberConnection = MessagesHelper.GetConnection(
                subscriberConfiguration,
                publisherId);
            Assert.That(
                mqttSubscriberConnection,
                Is.Not.Null,
                "The MQTT subscriber connection is invalid.");
            mqttSubscriberConnection.ConnectionProperties = mqttConfiguration.ConnectionProperties;
            Assert.That(
                mqttSubscriberConnection.ConnectionProperties.IsNull,
                Is.False,
                "The MQTT subscriber connection properties are not valid.");

            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections[0];
            Assert.That(
                subscriberConnection,
                Is.Not.Null,
                "Subscriber first connection should not be null");

            //Act
            // it will signal if the uadp message was received from local ip
            m_uaDataShutdownEvent = new ManualResetEvent(false);
            // it will signal if the mqtt with delta frame message was received from local ip
            m_uaDeltaDataShutdownEvent = new ManualResetEvent(false);

            m_isDeltaFrame = keyFrameCount > 1;
            subscriberApplication.DataReceived += UaPubSubApplication_DataReceived;
            subscriberConnection.Start();

            publisherConnection.Start();

            //Assert
            m_snapshotData = MessagesHelper.GetSnapshotData(
                publisherApplication,
                kNamespaceIndexAllTypes);
            if (!m_uaDataShutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert.Fail("The UADP message was not received");
            }
            if (keyFrameCount > 1)
            {
                MessagesHelper.UpdateSnapshotData(publisherApplication, kNamespaceIndexAllTypes);
                if (!m_uaDeltaDataShutdownEvent.WaitOne(kEstimatedPublishingTime))
                {
                    Assert.Fail("The UADP delta message was not received");
                }
            }
            if (keyFrameCount > 2)
            {
                for (int keyCount = 0; keyCount < keyFrameCount - 1; keyCount++)
                {
                    m_uaDeltaDataShutdownEvent.Reset();
                    m_snapshotData = MessagesHelper.GetSnapshotData(
                        publisherApplication,
                        kNamespaceIndexAllTypes);
                    MessagesHelper.UpdateSnapshotData(
                        publisherApplication,
                        kNamespaceIndexAllTypes);
                    if (!m_uaDeltaDataShutdownEvent.WaitOne(kEstimatedPublishingTime))
                    {
                        Assert.Fail("The UADP delta message was not received");
                    }
                }
            }

            subscriberConnection.Stop();
            publisherConnection.Stop();
        }

        [Test(Description = "Validate mqtt local pub/sub connection with json data.")]
#if !CUSTOM_TESTS
        [Ignore("A mosquitto tool should be installed local in order to run correctly.")]
#endif
        public void ValidateMqttLocalPubSubConnectionWithJson(
            [ValueSource(nameof(s_validPublisherIds))] Variant publisherId,
            [Values(0, 10000)] double metaDataUpdateTime)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Process _ = RestartMosquitto();

            //Arrange
            const ushort writerGroupId = 1;

            string mqttLocalBrokerUrl = Utils.Format(
                MqttUrlFormat,
                "localhost");

            var mqttConfiguration = new MqttClientProtocolConfiguration(
                version: EnumMqttProtocolVersion.V500);

            const JsonNetworkMessageContentMask jsonNetworkMessageContentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader;
            const JsonDataSetMessageContentMask jsonDataSetMessageContentMask
                = JsonDataSetMessageContentMask.None;

            const DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3"),
                MessagesHelper.CreateDataSetMetaDataAllTypes("DataSet4")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    mqttLocalBrokerUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes,
                    metaDataUpdateTime: metaDataUpdateTime);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(
                publisherConfiguration,
                publisherId);
            Assert.That(mqttPublisherConnection, Is.Not.Null, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.ConnectionProperties;
            Assert.That(
                mqttPublisherConnection.ConnectionProperties.IsNull,
                Is.False,
                "The MQTT publisher connection properties are not valid.");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections[0];
            Assert.That(publisherConnection, Is.Not.Null, "Publisher first connection should not be null");

            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0].WriterGroups[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");

            IList<UaNetworkMessage> networkMessages = publisherConnection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                new WriterGroupPublishState());
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            List<PubSubEncoding.JsonNetworkMessage> uaNetworkMessages = MessagesHelper
                .GetJsonUaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaNetworkMessages,
                Is.Not.Null,
                "Json ua-data entries are missing from configuration!");

            List<PubSubEncoding.JsonNetworkMessage> uaMetaDataNetworkMessages =
                MessagesHelper.GetJsonUaMetaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "Json ua-metadata entries are missing from configuration!");

            const bool hasDataSetWriterId =
                (jsonNetworkMessageContentMask &
                    JsonNetworkMessageContentMask.DataSetMessageHeader) != 0 &&
                (jsonDataSetMessageContentMask &
                    JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    mqttLocalBrokerUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    setDataSetWriterId: hasDataSetWriterId,
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubscriberConnection = MessagesHelper.GetConnection(
                subscriberConfiguration,
                publisherId);
            Assert.That(
                mqttSubscriberConnection,
                Is.Not.Null,
                "The MQTT subscriber connection is invalid.");
            mqttSubscriberConnection.ConnectionProperties = mqttConfiguration.ConnectionProperties;
            Assert.That(
                mqttSubscriberConnection.ConnectionProperties.IsNull,
                Is.False,
                "The MQTT subscriber connection properties are not valid.");

            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections[0];
            Assert.That(
                subscriberConnection,
                Is.Not.Null,
                "Subscriber first connection should not be null");

            //Act
            // it will signal if the mqtt message was received from local ip
            m_uaDataShutdownEvent = new ManualResetEvent(false);
            // it will signal if the mqtt metadata message was received from local ip
            m_uaMetaDataShutdownEvent = new ManualResetEvent(false);
            // it will signal if the changed configuration message was received on local ip
            m_uaConfigurationUpdateEvent = new ManualResetEvent(false);

            m_isDeltaFrame = false;
            subscriberApplication.DataReceived += UaPubSubApplication_DataReceived;
            subscriberApplication.MetaDataReceived += UaPubSubApplication_MetaDataReceived;
            subscriberApplication.ConfigurationUpdating
                += UaPubSubApplication_ConfigurationUpdating;
            subscriberConnection.Start();

            publisherConnection.Start();

            //Assert
            if (!m_uaDataShutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert.Fail("The JSON message was not received");
            }
            if (!m_uaMetaDataShutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert.Fail("The JSON metadata message was not received");
            }

            subscriberConnection.Stop();
            publisherConnection.Stop();
        }

        [Test(Description = "Validate mqtt local pub/sub connection with json data.")]
#if !CUSTOM_TESTS
        [Ignore("A mosquitto tool should be installed local in order to run correctly.")]
#endif
        public void ValidateMqttLocalPubSubConnectionWithDeltaJson(
            [ValueSource(nameof(s_validPublisherIds))] Variant publisherId,
            [Values(2, 3, 4)] int keyFrameCount)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Process _ = RestartMosquitto();

            //Arrange
            const ushort writerGroupId = 1;

            string mqttLocalBrokerUrl = Utils.Format(
                MqttUrlFormat,
                "localhost");

            var mqttConfiguration = new MqttClientProtocolConfiguration(
                version: EnumMqttProtocolVersion.V500);

            const JsonNetworkMessageContentMask jsonNetworkMessageContentMask =
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader;
            const JsonDataSetMessageContentMask jsonDataSetMessageContentMask
                = JsonDataSetMessageContentMask.None;

            const DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper
                .CreatePublisherConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    mqttLocalBrokerUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes,
                    metaDataUpdateTime: 1000,
                    keyFrameCount: Convert.ToUInt32(keyFrameCount));
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(
                publisherConfiguration,
                publisherId);
            Assert.That(mqttPublisherConnection, Is.Not.Null, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.ConnectionProperties;
            Assert.That(
                mqttPublisherConnection.ConnectionProperties.IsNull,
                Is.False,
                "The MQTT publisher connection properties are not valid.");

            // Create publisher application for multiple datasets
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections[0];
            Assert.That(publisherConnection, Is.Not.Null, "Publisher first connection should not be null");

            Assert.That(
                publisherConfiguration.Connections[0],
                Is.Not.Null,
                "publisherConfiguration first connection should not be null");
            Assert.That(
                publisherConfiguration.Connections[0].WriterGroups[0],
                Is.Not.Null,
                "publisherConfiguration  first writer group of first connection should not be null");

            IList<UaNetworkMessage> networkMessages = publisherConnection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                new WriterGroupPublishState());
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Is.Not.Empty,
                "connection.CreateNetworkMessages shall have at least one network message");

            List<PubSubEncoding.JsonNetworkMessage> uaNetworkMessages = MessagesHelper
                .GetJsonUaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaNetworkMessages,
                Is.Not.Null,
                "Json ua-data entries are missing from configuration!");

            List<PubSubEncoding.JsonNetworkMessage> uaMetaDataNetworkMessages =
                MessagesHelper.GetJsonUaMetaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]);
            Assert.That(
                uaMetaDataNetworkMessages,
                Is.Not.Null,
                "Json ua-metadata entries are missing from configuration!");

            const bool hasDataSetWriterId =
                (jsonNetworkMessageContentMask &
                    JsonNetworkMessageContentMask.DataSetMessageHeader) != 0 &&
                (jsonDataSetMessageContentMask &
                    JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper
                .CreateSubscriberConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    mqttLocalBrokerUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    setDataSetWriterId: hasDataSetWriterId,
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes,
                    keyFrameCount: Convert.ToUInt32(keyFrameCount));
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubscriberConnection = MessagesHelper.GetConnection(
                subscriberConfiguration,
                publisherId);
            Assert.That(
                mqttSubscriberConnection,
                Is.Not.Null,
                "The MQTT subscriber connection is invalid.");
            mqttSubscriberConnection.ConnectionProperties = mqttConfiguration.ConnectionProperties;
            Assert.That(
                mqttSubscriberConnection.ConnectionProperties.IsNull,
                Is.False,
                "The MQTT subscriber connection properties are not valid.");

            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.That(dataSetReaders, Is.Not.Null, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections[0];
            Assert.That(
                subscriberConnection,
                Is.Not.Null,
                "Subscriber first connection should not be null");

            //Act
            // it will signal if the mqtt message was received from local ip
            m_uaDataShutdownEvent = new ManualResetEvent(false);
            // it will signal if the mqtt with delta frame message was received from local ip
            m_uaDeltaDataShutdownEvent = new ManualResetEvent(false);

            m_isDeltaFrame = keyFrameCount > 1;
            subscriberApplication.DataReceived += UaPubSubApplication_DataReceived;
            subscriberConnection.Start();

            publisherConnection.Start();

            //Assert
            m_snapshotData = MessagesHelper.GetSnapshotData(
                publisherApplication,
                kNamespaceIndexAllTypes);
            Assert.That(m_snapshotData, Is.Not.Null, "snapshot data should not be null");
            if (!m_uaDataShutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert.Fail("The JSON message was not received");
            }
            if (keyFrameCount > 1)
            {
                MessagesHelper.UpdateSnapshotData(publisherApplication, kNamespaceIndexAllTypes);
                if (!m_uaDeltaDataShutdownEvent.WaitOne(kEstimatedPublishingTime))
                {
                    Assert.Fail("The JSON delta message was not received");
                }
            }
            if (keyFrameCount > 2)
            {
                for (int keyCount = 0; keyCount < keyFrameCount - 1; keyCount++)
                {
                    m_uaDeltaDataShutdownEvent.Reset();
                    m_snapshotData = MessagesHelper.GetSnapshotData(
                        publisherApplication,
                        kNamespaceIndexAllTypes);
                    MessagesHelper.UpdateSnapshotData(
                        publisherApplication,
                        kNamespaceIndexAllTypes);
                    if (!m_uaDeltaDataShutdownEvent.WaitOne(kEstimatedPublishingTime))
                    {
                        Assert.Fail("The JSON delta message was not received");
                    }
                }
            }

            subscriberConnection.Stop();
            publisherConnection.Stop();
        }

        /// <summary>
        /// Data received handler
        /// </summary>
        private void UaPubSubApplication_DataReceived(object sender, SubscribedDataEventArgs e)
        {
            if (m_isDeltaFrame)
            {
                bool hasChanged = false;
                foreach (UaDataSetMessage dataSetMessage in e.NetworkMessage.DataSetMessages)
                {
                    foreach (Field field in dataSetMessage.DataSet.Fields)
                    {
                        if (m_snapshotData.TryGetValue(
                                field.TargetNodeId,
                                out DataValue snapshotValue) &&
                            !field.Value.Equals(snapshotValue))
                        {
                            hasChanged = true;
                        }
                    }
                }
                if (!hasChanged)
                {
                    m_uaDataShutdownEvent.Set();
                }
                else
                {
                    m_uaDeltaDataShutdownEvent.Set();
                }
            }
            else
            {
                m_uaDataShutdownEvent.Set();
            }
        }

        /// <summary>
        /// MetaData received handler
        /// </summary>
        private void UaPubSubApplication_MetaDataReceived(object sender, SubscribedDataEventArgs e)
        {
            m_uaMetaDataShutdownEvent.Set();
        }

        /// <summary>
        /// ConfigurationUpdating received handler
        /// </summary>
        private void UaPubSubApplication_ConfigurationUpdating(
            object sender,
            ConfigurationUpdatingEventArgs e)
        {
            m_uaConfigurationUpdateEvent.Set();
        }

        /// <summary>
        /// Start/stop local mosquitto
        /// </summary>
        private static Process RestartMosquitto(string arguments = "")
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(DefaultBrokerProcessName);
                if (processes.Length > 0)
                {
                    Process mosquittoProcess = processes[0];
                    try
                    {
                        mosquittoProcess.Kill();
#if NET472 || NET48
                        mosquittoProcess.WaitForExit(10);
#else
                        mosquittoProcess.WaitForExit(TimeSpan.FromSeconds(10));
#endif
                    }
                    finally
                    {
                        mosquittoProcess?.Dispose();
                    }
                }

                var process = new Process();
                string programFilesPath = Environment.Is64BitOperatingSystem
                    ? Environment.GetEnvironmentVariable("ProgramW6432")
                    : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

                process.StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(
                        programFilesPath,
                        Path.Combine(DefaultBrokerProcessName, $"{DefaultBrokerProcessName}.exe")),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    Arguments = arguments
                };

                process.Start();
                process.ErrorDataReceived += ErrorHandler;
                process.BeginErrorReadLine();
                return process;
            }
            catch (Exception)
            {
                Assert.Fail("The mosquitto could not be restarted!");
            }
            return null;
        }

        private static void ErrorHandler(object sender, DataReceivedEventArgs e)
        {
            Debug.WriteLine($"MOSQUITTO {e.Data}");
        }
    }
}
