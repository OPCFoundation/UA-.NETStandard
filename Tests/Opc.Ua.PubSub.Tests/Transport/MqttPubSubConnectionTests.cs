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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.PubSub.Tests.Encoding;
using Opc.Ua.PubSub.Transport;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using PubSubEncoding = Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.Transport
{
    [TestFixture(Description = "Tests for Mqtt connections")]
    public class MqttPubSubConnectionTests
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
        internal const string MqttUrlFormat = "{0}://{1}:1883";

        [OneTimeSetUp]
        public void MyTestInitialize()
        {
        }

        [Test(Description = "Validate mqtt local pub/sub connection with uadp data.")]
#if !CUSTOM_TESTS
        [Ignore("A mosquitto tool should be installed local in order to run correctly.")]
#endif
        public void ValidateMqttLocalPubSubConnectionWithUadp(
            [Values((byte)1, (ushort)1, (uint)1, (ulong)1, "abc")] object publisherId
        )
        {
            RestartMosquitto();

            //Arrange
            const ushort writerGroupId = 1;

            string mqttLocalBrokerUrl = Utils.Format(
                MqttUrlFormat,
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
                    nameSpaceIndexForData: kNamespaceIndexAllTypes
                    );
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(
                publisherConfiguration,
                publisherId
            );
            Assert.IsNotNull(mqttPublisherConnection, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.ConnectionProperties;
            Assert.IsNotNull(
                mqttPublisherConnection.ConnectionProperties,
                "The MQTT publisher connection properties are not valid."
            );

            // Create publisher application for multiple datasets
            var publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections[0];
            Assert.IsNotNull(publisherConnection, "Publisher first connection should not be null");

            Assert.IsNotNull(
                publisherConfiguration.Connections[0],
                "publisherConfiguration first connection should not be null"
            );
            Assert.IsNotNull(
                publisherConfiguration.Connections[0].WriterGroups[0],
                "publisherConfiguration  first writer group of first connection should not be null"
            );

            IList<UaNetworkMessage> networkMessages = publisherConnection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                new WriterGroupPublishState()
            );
            Assert.IsNotNull(
                networkMessages,
                "connection.CreateNetworkMessages shall not return null");
            Assert.GreaterOrEqual(
                networkMessages.Count,
                1,
                "connection.CreateNetworkMessages shall have at least one network message"
            );

            var uaNetworkMessage = networkMessages[0] as PubSubEncoding.UadpNetworkMessage;
            Assert.IsNotNull(uaNetworkMessage, "networkMessageEncode should not be null");

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
                    nameSpaceIndexForData: kNamespaceIndexAllTypes
                    );
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            var subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(
                subscriberApplication.PubSubConnections[0],
                "subscriberConfiguration first connection should not be null"
            );

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubscriberConnection = MessagesHelper.GetConnection(
                subscriberConfiguration,
                publisherId
            );
            Assert.IsNotNull(
                mqttSubscriberConnection,
                "The MQTT subscriber connection is invalid.");
            mqttSubscriberConnection.ConnectionProperties = mqttConfiguration.ConnectionProperties;
            Assert.IsNotNull(
                mqttSubscriberConnection.ConnectionProperties,
                "The MQTT subscriber connection properties are not valid."
            );

            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections[0];
            Assert.IsNotNull(
                subscriberConnection,
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
                NUnit.Framework.Assert.Fail("The UADP message was not received");
            }

            subscriberConnection.Stop();
            publisherConnection.Stop();
        }

        [Test(Description = "Validate mqtt local pub/sub connection with uadp data.")]
#if !CUSTOM_TESTS
        [Ignore("A mosquitto tool should be installed local in order to run correctly.")]
#endif
        public void ValidateMqttLocalPubSubConnectionWithDeltaUadp(
            [Values((byte)1, (ushort)1, (uint)1, (ulong)1, "abc")] object publisherId,
            [Values(1, 2, 3, 4)] int keyFrameCount
        )
        {
            RestartMosquitto();

            //Arrange
            const ushort writerGroupId = 1;

            string mqttLocalBrokerUrl = Utils.Format(
                MqttUrlFormat,
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
                    keyFrameCount: Convert.ToUInt32(keyFrameCount)
                    );
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(
                publisherConfiguration,
                publisherId
            );
            Assert.IsNotNull(mqttPublisherConnection, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.ConnectionProperties;
            Assert.IsNotNull(
                mqttPublisherConnection.ConnectionProperties,
                "The MQTT publisher connection properties are not valid."
            );

            // Create publisher application for multiple datasets
            var publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections[0];
            Assert.IsNotNull(publisherConnection, "Publisher first connection should not be null");

            Assert.IsNotNull(
                publisherConfiguration.Connections[0],
                "publisherConfiguration first connection should not be null"
            );
            Assert.IsNotNull(
                publisherConfiguration.Connections[0].WriterGroups[0],
                "publisherConfiguration  first writer group of first connection should not be null"
            );

            IList<UaNetworkMessage> networkMessages = publisherConnection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                new WriterGroupPublishState()
            );
            Assert.IsNotNull(
                networkMessages,
                "connection.CreateNetworkMessages shall not return null");
            Assert.GreaterOrEqual(
                networkMessages.Count,
                1,
                "connection.CreateNetworkMessages shall have at least one network message"
            );

            var uaNetworkMessage = networkMessages[0] as PubSubEncoding.UadpNetworkMessage;
            Assert.IsNotNull(uaNetworkMessage, "networkMessageEncode should not be null");

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
                    keyFrameCount: Convert.ToUInt32(keyFrameCount)
                    );
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            var subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(
                subscriberApplication.PubSubConnections[0],
                "subscriberConfiguration first connection should not be null"
            );

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubscriberConnection = MessagesHelper.GetConnection(
                subscriberConfiguration,
                publisherId
            );
            Assert.IsNotNull(
                mqttSubscriberConnection,
                "The MQTT subscriber connection is invalid.");
            mqttSubscriberConnection.ConnectionProperties = mqttConfiguration.ConnectionProperties;
            Assert.IsNotNull(
                mqttSubscriberConnection.ConnectionProperties,
                "The MQTT subscriber connection properties are not valid."
            );

            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections[0];
            Assert.IsNotNull(
                subscriberConnection,
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
                NUnit.Framework.Assert.Fail("The UADP message was not received");
            }
            if (keyFrameCount > 1)
            {
                MessagesHelper.UpdateSnapshotData(publisherApplication, kNamespaceIndexAllTypes);
                if (!m_uaDeltaDataShutdownEvent.WaitOne(kEstimatedPublishingTime))
                {
                    NUnit.Framework.Assert.Fail("The UADP delta message was not received");
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
                        NUnit.Framework.Assert.Fail("The UADP delta message was not received");
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
            [Values((byte)1, (ushort)1, (uint)1, (ulong)1, "abc")] object publisherId,
            [Values(0, 10000)] double metaDataUpdateTime
        )
        {
            RestartMosquitto();

            //Arrange
            const ushort writerGroupId = 1;

            string mqttLocalBrokerUrl = Utils.Format(
                MqttUrlFormat,
                Utils.UriSchemeMqtt,
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
                    metaDataUpdateTime: metaDataUpdateTime
                    );
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(
                publisherConfiguration,
                publisherId
            );
            Assert.IsNotNull(mqttPublisherConnection, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.ConnectionProperties;
            Assert.IsNotNull(
                mqttPublisherConnection.ConnectionProperties,
                "The MQTT publisher connection properties are not valid."
            );

            // Create publisher application for multiple datasets
            var publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections[0];
            Assert.IsNotNull(publisherConnection, "Publisher first connection should not be null");

            Assert.IsNotNull(
                publisherConfiguration.Connections[0],
                "publisherConfiguration first connection should not be null"
            );
            Assert.IsNotNull(
                publisherConfiguration.Connections[0].WriterGroups[0],
                "publisherConfiguration  first writer group of first connection should not be null"
            );

            IList<UaNetworkMessage> networkMessages = publisherConnection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                new WriterGroupPublishState()
            );
            Assert.IsNotNull(
                networkMessages,
                "connection.CreateNetworkMessages shall not return null");
            Assert.GreaterOrEqual(
                networkMessages.Count,
                1,
                "connection.CreateNetworkMessages shall have at least one network message"
            );

            List<PubSubEncoding.JsonNetworkMessage> uaNetworkMessages = MessagesHelper
                .GetJsonUaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]
                    );
            Assert.IsNotNull(
                uaNetworkMessages,
                "Json ua-data entries are missing from configuration!");

            List<PubSubEncoding.JsonNetworkMessage> uaMetaDataNetworkMessages =
                MessagesHelper.GetJsonUaMetaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]
                );
            Assert.IsNotNull(
                uaMetaDataNetworkMessages,
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
                    nameSpaceIndexForData: kNamespaceIndexAllTypes
                    );
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            var subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(
                subscriberApplication.PubSubConnections[0],
                "subscriberConfiguration first connection should not be null"
            );

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubscriberConnection = MessagesHelper.GetConnection(
                subscriberConfiguration,
                publisherId
            );
            Assert.IsNotNull(
                mqttSubscriberConnection,
                "The MQTT subscriber connection is invalid.");
            mqttSubscriberConnection.ConnectionProperties = mqttConfiguration.ConnectionProperties;
            Assert.IsNotNull(
                mqttSubscriberConnection.ConnectionProperties,
                "The MQTT subscriber connection properties are not valid."
            );

            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections[0];
            Assert.IsNotNull(
                subscriberConnection,
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
                NUnit.Framework.Assert.Fail("The JSON message was not received");
            }
            if (!m_uaMetaDataShutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                NUnit.Framework.Assert.Fail("The JSON metadata message was not received");
            }

            subscriberConnection.Stop();
            publisherConnection.Stop();
        }

        [Test(Description = "Validate mqtt local pub/sub connection with json data.")]
#if !CUSTOM_TESTS
        [Ignore("A mosquitto tool should be installed local in order to run correctly.")]
#endif
        public void ValidateMqttLocalPubSubConnectionWithDeltaJson(
            [Values((byte)1, (ushort)1, (uint)1, (ulong)1, "abc")] object publisherId,
            [Values(2, 3, 4)] int keyFrameCount
        )
        {
            RestartMosquitto();

            //Arrange
            const ushort writerGroupId = 1;

            string mqttLocalBrokerUrl = Utils.Format(
                MqttUrlFormat,
                Utils.UriSchemeMqtt,
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
                    keyFrameCount: Convert.ToUInt32(keyFrameCount)
                    );
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(
                publisherConfiguration,
                publisherId
            );
            Assert.IsNotNull(mqttPublisherConnection, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.ConnectionProperties;
            Assert.IsNotNull(
                mqttPublisherConnection.ConnectionProperties,
                "The MQTT publisher connection properties are not valid."
            );

            // Create publisher application for multiple datasets
            var publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections[0];
            Assert.IsNotNull(publisherConnection, "Publisher first connection should not be null");

            Assert.IsNotNull(
                publisherConfiguration.Connections[0],
                "publisherConfiguration first connection should not be null"
            );
            Assert.IsNotNull(
                publisherConfiguration.Connections[0].WriterGroups[0],
                "publisherConfiguration  first writer group of first connection should not be null"
            );

            IList<UaNetworkMessage> networkMessages = publisherConnection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                new WriterGroupPublishState()
            );
            Assert.IsNotNull(
                networkMessages,
                "connection.CreateNetworkMessages shall not return null");
            Assert.GreaterOrEqual(
                networkMessages.Count,
                1,
                "connection.CreateNetworkMessages shall have at least one network message"
            );

            List<PubSubEncoding.JsonNetworkMessage> uaNetworkMessages = MessagesHelper
                .GetJsonUaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]
                    );
            Assert.IsNotNull(
                uaNetworkMessages,
                "Json ua-data entries are missing from configuration!");

            List<PubSubEncoding.JsonNetworkMessage> uaMetaDataNetworkMessages =
                MessagesHelper.GetJsonUaMetaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]
                );
            Assert.IsNotNull(
                uaMetaDataNetworkMessages,
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
                    keyFrameCount: Convert.ToUInt32(keyFrameCount)
                    );
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            var subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(
                subscriberApplication.PubSubConnections[0],
                "subscriberConfiguration first connection should not be null"
            );

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubscriberConnection = MessagesHelper.GetConnection(
                subscriberConfiguration,
                publisherId
            );
            Assert.IsNotNull(
                mqttSubscriberConnection,
                "The MQTT subscriber connection is invalid.");
            mqttSubscriberConnection.ConnectionProperties = mqttConfiguration.ConnectionProperties;
            Assert.IsNotNull(
                mqttSubscriberConnection.ConnectionProperties,
                "The MQTT subscriber connection properties are not valid."
            );

            List<DataSetReaderDataType> dataSetReaders = subscriberApplication
                .PubSubConnections[0]
                .GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections[0];
            Assert.IsNotNull(
                subscriberConnection,
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
            Assert.IsNotNull(m_snapshotData, "snapshot data should not be null");
            if (!m_uaDataShutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                NUnit.Framework.Assert.Fail("The JSON message was not received");
            }
            if (keyFrameCount > 1)
            {
                MessagesHelper.UpdateSnapshotData(publisherApplication, kNamespaceIndexAllTypes);
                if (!m_uaDeltaDataShutdownEvent.WaitOne(kEstimatedPublishingTime))
                {
                    NUnit.Framework.Assert.Fail("The JSON delta message was not received");
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
                        NUnit.Framework.Assert.Fail("The JSON delta message was not received");
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
                        if (
                            m_snapshotData.TryGetValue(
                                field.TargetNodeId,
                                out DataValue snapshotValue) &&
                            !field.Value.Equals(snapshotValue)
                        )
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
        private static void RestartMosquitto(string arguments = "")
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(DefaultBrokerProcessName);
                if (processes.Length > 0)
                {
                    Process mosquittoProcess = processes[0];
                    mosquittoProcess.Kill();
                }

                using var process = new Process();
                string programFilesPath = Environment.Is64BitOperatingSystem
                    ? Environment.GetEnvironmentVariable("ProgramW6432")
                    : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

                process.StartInfo = new ProcessStartInfo(
                    Path.Combine(
                        programFilesPath,
                        Path.Combine(DefaultBrokerProcessName, $"{DefaultBrokerProcessName}.exe")
                    )
                )
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    //startInfo.CreateNoWindow = true;
                    //startInfo.RedirectStandardOutput = true;
                    //startInfo.UseShellExecute = true;
                    //startInfo.Verb = "runas";
                    Arguments = arguments
                };
                process.Start();
            }
            catch (Exception)
            {
                NUnit.Framework.Assert.Fail("The mosquitto could not be restarted!");
            }
        }
    }
}
