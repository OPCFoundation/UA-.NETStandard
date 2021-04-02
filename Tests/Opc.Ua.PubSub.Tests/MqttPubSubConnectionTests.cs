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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Mqtt;
using Opc.Ua.PubSub.PublishedData;

namespace Opc.Ua.PubSub.Tests
{
    [TestFixture(Description = "Tests for Mqtt connections")]
    public class MqttPubSubConnectionTests
    {
        private const UInt16 NamespaceIndexAllTypes = 3;

        private ManualResetEvent m_shutdownEvent;
        private const int EstimatedPublishingTime = 60000;

        [OneTimeSetUp()]
        public void MyTestInitialize()
        {

        }

        [Test(Description = "Validate mqtt local pub/sub connection. The local mosquitto broker should be started")]
        public void ValidateMqttLocalPubSubConnection(
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            RestartMosquitto("mosquitto");

            //Arrange
            UInt16 writerGroupId = 1;

            string MqttAddressUrl = "mqtt://localhost:1883";

            ITransportProtocolConfiguration mqttConfiguration = new MqttClientProtocolConfiguration(version: EnumMqttProtocolVersion.V500);

            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId
                | UadpNetworkMessageContentMask.WriterGroupId
                | UadpNetworkMessageContentMask.PayloadHeader;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.None;

            DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

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

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(publisherConfiguration, publisherId);
            Assert.IsNotNull(mqttPublisherConnection, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttPublisherConnection.ConnectionProperties, "The MQTT publisher connection properties are not valid.");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(publisherConnection, "Publisher first connection should not be null");

            WriterGroupDataType writerGroup = MessagesHelper.GetWriterGroup(mqttPublisherConnection, writerGroupId);
            UadpWriterGroupMessageDataType messageSettings = ExtensionObject.ToEncodeable(writerGroup.MessageSettings)
                as UadpWriterGroupMessageDataType;

            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            UadpNetworkMessage uaNetworkMessage = publisherConnection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            Assert.IsNotNull(uaNetworkMessage, "CreateNetworkMessage did not return an UadpNetworkMessage.");

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

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubcriberConnection = MessagesHelper.GetConnection(subscriberConfiguration, publisherId);
            Assert.IsNotNull(mqttSubcriberConnection, "The MQTT subscriber connection is invalid.");
            mqttSubcriberConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttSubcriberConnection.ConnectionProperties, "The MQTT subscriber connection properties are not valid.");

            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections.First();
            Assert.IsNotNull(subscriberConnection, "Subscriber first connection should not be null");

            //Act
            // it will signal if the uadp message was received from local ip
            m_shutdownEvent = new ManualResetEvent(false);

            subscriberApplication.DataReceived += UaPubSubApplication_DataReceived;
            subscriberConnection.Start();

            publisherConnection.Start();
            publisherConnection.PublishNetworkMessage(uaNetworkMessage);

            //Assert
            if (!m_shutdownEvent.WaitOne(EstimatedPublishingTime))
            {
                Assert.Fail("The UADP message was not received");
            }

            subscriberConnection.Stop();
            publisherConnection.Stop();
            
        }

        [Test(Description = "Validate mqtt local pub/sub connection. The local mosquitto broker should be started with parameters")]
        public void ValidateMqttLocalPubSubConnectionWithCredentials(
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            RestartMosquitto("mosquitto", @"-c .\resources\mosquitto_pwd.conf");

            //Arrange
            UInt16 writerGroupId = 1;

            string MqttAddresswWithPasswordUrl = "mqtt://" + GetFirstActiveNic() + ":1883";

            string mqttUser = "user";
            SecureString secureUser = new SecureString();
            Array.ForEach(mqttUser.ToArray(), secureUser.AppendChar);
            secureUser.MakeReadOnly();

            string mqttPassword = "pwd";
            SecureString securePassword = new SecureString();
            Array.ForEach(mqttPassword.ToArray(), securePassword.AppendChar);
            securePassword.MakeReadOnly();

            ITransportProtocolConfiguration mqttConfiguration =
                new MqttClientProtocolConfiguration(userName: secureUser, password: securePassword, version: EnumMqttProtocolVersion.V500);

            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId
                | UadpNetworkMessageContentMask.WriterGroupId
                | UadpNetworkMessageContentMask.PayloadHeader;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.None;

            DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddresswWithPasswordUrl, publisherId: publisherId, writerGroupId: writerGroupId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(publisherConfiguration, publisherId);
            Assert.IsNotNull(mqttPublisherConnection, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttPublisherConnection.ConnectionProperties, "The MQTT publisher connection properties are not valid.");
            
            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(publisherConnection, "Publisher first connection should not be null");

            WriterGroupDataType writerGroup = MessagesHelper.GetWriterGroup(mqttPublisherConnection, writerGroupId);
            UadpWriterGroupMessageDataType messageSettings = ExtensionObject.ToEncodeable(writerGroup.MessageSettings)
                as UadpWriterGroupMessageDataType;

            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            UadpNetworkMessage uaNetworkMessage = publisherConnection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            Assert.IsNotNull(uaNetworkMessage, "CreateNetworkMessage did not return an UadpNetworkMessage.");

            bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddresswWithPasswordUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: hasDataSetWriterId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubcriberConnection = MessagesHelper.GetConnection(subscriberConfiguration, publisherId);
            Assert.IsNotNull(mqttSubcriberConnection, "The MQTT subscriber connection is invalid.");
            mqttSubcriberConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttSubcriberConnection.ConnectionProperties, "The MQTT subscriber connection properties are not valid.");

            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections.First();
            Assert.IsNotNull(subscriberConnection, "Subscriber first connection should not be null");

            //Act
            // it will signal if the uadp message was received from local ip
            m_shutdownEvent = new ManualResetEvent(false);

            subscriberApplication.DataReceived += UaPubSubApplication_DataReceived;
            subscriberConnection.Start();

            publisherConnection.Start();
            publisherConnection.PublishNetworkMessage(uaNetworkMessage);

            //Assert
            if (!m_shutdownEvent.WaitOne(EstimatedPublishingTime))
            {
                Assert.Fail("The UADP message was not received");
            }

            subscriberConnection.Stop();
            publisherConnection.Stop();
        }

        [Test(Description = "Validate mqtt local pub/sub connection. The local mosquitto broker should be started")]
        public void ValidateLocalMqttPubSubConnectionWithCertificates(
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            RestartMosquitto("mosquitto", @"-c .\resources\mosquitto_certif.conf");

            //Arrange
            UInt16 writerGroupId = 1;

            string mqttlocalBrokerUrl = "mqtt://" + GetFirstActiveNic() + ":1883";

            string certificatePath = Path.Combine(Directory.GetCurrentDirectory(), @"resources\mosquitto.local.crt");

            //CertificateStoreIdentifier certificateStore = new CertificateStoreIdentifier();
            //certificateStore.StorePath = certificatePath;

            MqttTlsCertificates mqttTlsCertificates =
                new MqttTlsCertificates(caCertificatePath: certificatePath, clientCertificatePath: certificatePath);

            ITransportProtocolConfiguration mqttConfiguration =
                new MqttClientProtocolConfiguration(
                    mqttTlsOptions: new MqttTlsOptions(certificates: mqttTlsCertificates,
                    allowUntrustedCertificates: true,
                    ignoreCertificateChainErrors: true,
                    ignoreRevocationListErrors: true),
                    version: EnumMqttProtocolVersion.V500);

            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId
                | UadpNetworkMessageContentMask.WriterGroupId
                | UadpNetworkMessageContentMask.PayloadHeader;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.None;

            DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttUadpTransport,
                mqttlocalBrokerUrl, publisherId: publisherId, writerGroupId: writerGroupId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(publisherConfiguration, publisherId);
            Assert.IsNotNull(mqttPublisherConnection, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttPublisherConnection.ConnectionProperties, "The MQTT publisher connection properties are not valid.");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(publisherConnection, "Publisher first connection should not be null");

            WriterGroupDataType writerGroup = MessagesHelper.GetWriterGroup(mqttPublisherConnection, writerGroupId);
            UadpWriterGroupMessageDataType messageSettings = ExtensionObject.ToEncodeable(writerGroup.MessageSettings)
                as UadpWriterGroupMessageDataType;

            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            UadpNetworkMessage uaNetworkMessage = publisherConnection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            Assert.IsNotNull(uaNetworkMessage, "CreateNetworkMessage did not return an UadpNetworkMessage.");

            bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                mqttlocalBrokerUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: hasDataSetWriterId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubcriberConnection = MessagesHelper.GetConnection(subscriberConfiguration, publisherId);
            Assert.IsNotNull(mqttSubcriberConnection, "The MQTT subscriber connection is invalid.");
            mqttSubcriberConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttSubcriberConnection.ConnectionProperties, "The MQTT subscriber connection properties are not valid.");

            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections.First();
            Assert.IsNotNull(subscriberConnection, "Subscriber first connection should not be null");

            //Act
            // it will signal if the uadp message was received from local ip
            m_shutdownEvent = new ManualResetEvent(false);

            subscriberApplication.DataReceived += UaPubSubApplication_DataReceived;
            subscriberConnection.Start();

            publisherConnection.Start();
            publisherConnection.PublishNetworkMessage(uaNetworkMessage);

            //Assert
            if (!m_shutdownEvent.WaitOne(EstimatedPublishingTime))
            {
                Assert.Fail("The UADP message was not received");
            }

            subscriberConnection.Stop();
            publisherConnection.Stop();

        }

        [Test(Description = "Validate mqtt pub/sub unencrypted connections to public free web brokers.")]
        public void ValidateMqttUnencryptedPubSubConnectionToPublicWebBrokers(
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId,
            [Values("mqtt://test.mosquitto.org:1883", "mqtt://broker.hivemq.com:1883")]
                string mqttBrokerUrl)
        {
            //Arrange
            UInt16 writerGroupId = 1;

            ITransportProtocolConfiguration mqttConfiguration = new MqttClientProtocolConfiguration(version: EnumMqttProtocolVersion.V500);

            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.PublisherId
                | JsonNetworkMessageContentMask.DataSetMessageHeader;
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.None;

            DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3"),
                // MessagesHelper.CreateDataSetMetaDataAllTypes("DataSet4")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                mqttBrokerUrl, publisherId: publisherId, writerGroupId: writerGroupId,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(publisherConfiguration, publisherId);
            Assert.IsNotNull(mqttPublisherConnection, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttPublisherConnection.ConnectionProperties, "The MQTT publisher connection properties are not valid.");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(publisherConnection, "Publisher first connection should not be null");

            WriterGroupDataType writerGroup = MessagesHelper.GetWriterGroup(mqttPublisherConnection, writerGroupId);
            JsonWriterGroupMessageDataType messageSettings = ExtensionObject.ToEncodeable(writerGroup.MessageSettings)
                as JsonWriterGroupMessageDataType;

            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            JsonNetworkMessage uaNetworkMessage = publisherConnection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                JsonNetworkMessage;
            Assert.IsNotNull(uaNetworkMessage, "CreateNetworkMessage did not return an UadpNetworkMessage.");

            bool hasDataSetWriterId = (jsonNetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0
                && (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                mqttBrokerUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: hasDataSetWriterId,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubcriberConnection = MessagesHelper.GetConnection(subscriberConfiguration, publisherId);
            Assert.IsNotNull(mqttSubcriberConnection, "The MQTT subscriber connection is invalid.");
            mqttSubcriberConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttSubcriberConnection.ConnectionProperties, "The MQTT subscriber connection properties are not valid.");

            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections.First();
            Assert.IsNotNull(subscriberConnection, "Subscriber first connection should not be null");

            //Act
            // it will signal if the uadp message was received from local ip
            m_shutdownEvent = new ManualResetEvent(false);

            subscriberApplication.DataReceived += UaPubSubApplication_DataReceived;
            subscriberConnection.Start();

            publisherConnection.Start();
            publisherConnection.PublishNetworkMessage(uaNetworkMessage);

            //Assert
            if (!m_shutdownEvent.WaitOne(EstimatedPublishingTime))
            {
                Assert.Fail("The JSON message was not received");
            }

            subscriberConnection.Stop();
            publisherConnection.Stop();

        }

        [Test(Description = "Validate mqtt pub/sub encrypted connections to public free web brokers.")]
        public void ValidateMqttEncryptedPubSubConnectionToPublicWebBrokers(
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId,
            [Values("mqtts://test.mosquitto.org:8883")] // , "mqtts://broker.hivemq.com:8883"
                string mqttsBrokerUrl)
        {
            //Arrange
            UInt16 writerGroupId = 1;

            string storeName = "OPC Foundation";
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            CopyResourceToStore(localAppData, storeName, @"resources\mosquitto.org.crt");
            CopyResourceToStore(localAppData, storeName, @"resources\mosquitto.org.der");
            
            CertificateStoreIdentifier trustedIssuerCertificates = new CertificateTrustList {
                StoreType = "Directory",
                StorePath = $"%LocalApplicationData%/{storeName}/pki/issuer"
            };
            CertificateStoreIdentifier trustedPeerCertificates = new CertificateTrustList {
                StoreType = "Directory",
                StorePath = $"%LocalApplicationData%/{storeName}/pki/trusted"
            };
            CertificateStoreIdentifier rejectedCertificateStore = new CertificateTrustList {
                StoreType = "Directory",
                StorePath = $"%LocalApplicationData%/{storeName}/pki/rejected"
            };

            ITransportProtocolConfiguration mqttConfiguration = new MqttClientProtocolConfiguration(version: EnumMqttProtocolVersion.V500,
               mqttTlsOptions: new MqttTlsOptions(ignoreRevocationListErrors: true,
               certificates: new MqttTlsCertificates(
                   caCertificatePath: Path.Combine(localAppData,Path.Combine(storeName,@"pki\trusted\certs\mosquitto.org.crt"))),
                trustedIssuerCertificates: trustedIssuerCertificates,
                trustedPeerCertificates: trustedPeerCertificates,
                rejectedCertificateStore: rejectedCertificateStore));

            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.PublisherId
                | JsonNetworkMessageContentMask.DataSetMessageHeader;
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.None;

            DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                mqttsBrokerUrl, publisherId: publisherId, writerGroupId: writerGroupId,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(publisherConfiguration, publisherId);
            Assert.IsNotNull(mqttPublisherConnection, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttPublisherConnection.ConnectionProperties, "The MQTT publisher connection properties are not valid.");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(publisherConnection, "Publisher first connection should not be null");

            WriterGroupDataType writerGroup = MessagesHelper.GetWriterGroup(mqttPublisherConnection, writerGroupId);
            JsonWriterGroupMessageDataType messageSettings = ExtensionObject.ToEncodeable(writerGroup.MessageSettings)
                as JsonWriterGroupMessageDataType;

            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            JsonNetworkMessage uaNetworkMessage = publisherConnection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                JsonNetworkMessage;
            Assert.IsNotNull(uaNetworkMessage, "CreateNetworkMessage did not return an JsonNetworkMessage.");

            bool hasDataSetWriterId = (jsonNetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0
                && (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                mqttsBrokerUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: hasDataSetWriterId,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubcriberConnection = MessagesHelper.GetConnection(subscriberConfiguration, publisherId);
            Assert.IsNotNull(mqttSubcriberConnection, "The MQTT subscriber connection is invalid.");
            mqttSubcriberConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttSubcriberConnection.ConnectionProperties, "The MQTT subscriber connection properties are not valid.");

            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections.First();
            Assert.IsNotNull(subscriberConnection, "Subscriber first connection should not be null");

            //Act
            // it will signal if the uadp message was received from local ip
            m_shutdownEvent = new ManualResetEvent(false);

            subscriberApplication.DataReceived += UaPubSubApplication_DataReceived;
            subscriberConnection.Start();

            publisherConnection.Start();
            publisherConnection.PublishNetworkMessage(uaNetworkMessage);

            //Assert
            if (!m_shutdownEvent.WaitOne(EstimatedPublishingTime))
            {
                Assert.Fail("The JSON message was not received");
            }

            subscriberConnection.Stop();
            publisherConnection.Stop();

        }

        [Test(Description = "Validate mqtt pub/sub encrypted connections to public free web brokers.")]
        public void ValidateMqttEncryptedWithCertificatesPubSubConnectionToPublicWebBrokers(
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId,
            [Values("mqtts://test.mosquitto.org:8883")] // , "mqtts://broker.hivemq.com:8883"
                string mqttsBrokerUrl)
        {
            //Arrange
            UInt16 writerGroupId = 1;

            string storeName = "OPC Foundation";
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            CopyResourceToStore(localAppData, storeName, @"resources\mosquitto.org.crt");
            CopyResourceToStore(localAppData, storeName, @"resources\mosquitto.org.der");

            CertificateStoreIdentifier trustedIssuerCertificates = new CertificateTrustList {
                StoreType = "Directory",
                StorePath = @"%LocalApplicationData%/OPC Foundation/pki/issuer"
            };
            CertificateStoreIdentifier trustedPeerCertificates = new CertificateTrustList {
                StoreType = "Directory",
                StorePath = @"%LocalApplicationData%/OPC Foundation/pki/trusted"
            };
            CertificateStoreIdentifier rejectedCertificateStore = new CertificateTrustList {
                StoreType = "Directory",
                StorePath = @"%LocalApplicationData%/OPC Foundation/pki/rejected"
            };

            ITransportProtocolConfiguration mqttConfiguration = new MqttClientProtocolConfiguration(version: EnumMqttProtocolVersion.V500,
               mqttTlsOptions: new MqttTlsOptions(ignoreRevocationListErrors: true,
               certificates: new MqttTlsCertificates(
                   caCertificatePath: Path.Combine(localAppData, Path.Combine(storeName, @"pki\trusted\certs\mosquitto.org.crt")),
                   clientCertificatePath: @"resources\client.pfx",
                   clientCertificatePassword: ""),
                trustedIssuerCertificates: trustedIssuerCertificates,
                trustedPeerCertificates: trustedPeerCertificates,
                rejectedCertificateStore: rejectedCertificateStore));

            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.PublisherId
                | JsonNetworkMessageContentMask.DataSetMessageHeader;
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.None;

            DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                mqttsBrokerUrl, publisherId: publisherId, writerGroupId: writerGroupId,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(publisherConfiguration, publisherId);
            Assert.IsNotNull(mqttPublisherConnection, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttPublisherConnection.ConnectionProperties, "The MQTT publisher connection properties are not valid.");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(publisherConnection, "Publisher first connection should not be null");

            WriterGroupDataType writerGroup = MessagesHelper.GetWriterGroup(mqttPublisherConnection, writerGroupId);
            JsonWriterGroupMessageDataType messageSettings = ExtensionObject.ToEncodeable(writerGroup.MessageSettings)
                as JsonWriterGroupMessageDataType;

            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            JsonNetworkMessage uaNetworkMessage = publisherConnection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                JsonNetworkMessage;
            Assert.IsNotNull(uaNetworkMessage, "CreateNetworkMessage did not return an UadpNetworkMessage.");

            bool hasDataSetWriterId = (jsonNetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0
                && (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                mqttsBrokerUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: hasDataSetWriterId,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubcriberConnection = MessagesHelper.GetConnection(subscriberConfiguration, publisherId);
            Assert.IsNotNull(mqttSubcriberConnection, "The MQTT subscriber connection is invalid.");
            mqttSubcriberConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttSubcriberConnection.ConnectionProperties, "The MQTT subscriber connection properties are not valid.");

            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections.First();
            Assert.IsNotNull(subscriberConnection, "Subscriber first connection should not be null");

            //Act
            // it will signal if the uadp message was received from local ip
            m_shutdownEvent = new ManualResetEvent(false);

            subscriberApplication.DataReceived += UaPubSubApplication_DataReceived;
            subscriberConnection.Start();

            publisherConnection.Start();
            publisherConnection.PublishNetworkMessage(uaNetworkMessage);

            //Assert
            if (!m_shutdownEvent.WaitOne(EstimatedPublishingTime))
            {
                Assert.Fail("The JSON message was not received");
            }

            subscriberConnection.Stop();
            publisherConnection.Stop();

        }

        [Test(Description = "Validate mqtt free web broker pub/sub connections with credentials.")]
        public void ValidateMqttPubSubConnectionToPublicWebBrokersWithCredentials(
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId,
            [Values("mqtt://test.mosquitto.org:1883", "mqtt://broker.hivemq.com:1883")]
                string mqttBrokerUrl)
        {
            //Arrange
            UInt16 writerGroupId = 1;

            string mqttUser = "user";
            SecureString secureUser = new SecureString();
            Array.ForEach(mqttUser.ToArray(), secureUser.AppendChar);
            secureUser.MakeReadOnly();

            string mqttPassword = "pwd";
            SecureString securePassword = new SecureString();
            Array.ForEach(mqttPassword.ToArray(), securePassword.AppendChar);
            securePassword.MakeReadOnly();

            ITransportProtocolConfiguration mqttConfiguration =
                new MqttClientProtocolConfiguration(userName: secureUser, password: securePassword, version: EnumMqttProtocolVersion.V500);

            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.PublisherId
                | JsonNetworkMessageContentMask.DataSetMessageHeader;
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.None;

            DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                mqttBrokerUrl, publisherId: publisherId, writerGroupId: writerGroupId,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(publisherConfiguration, publisherId);
            Assert.IsNotNull(mqttPublisherConnection, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttPublisherConnection.ConnectionProperties, "The MQTT publisher connection properties are not valid.");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(publisherConnection, "Publisher first connection should not be null");

            WriterGroupDataType writerGroup = MessagesHelper.GetWriterGroup(mqttPublisherConnection, writerGroupId);
            JsonWriterGroupMessageDataType messageSettings = ExtensionObject.ToEncodeable(writerGroup.MessageSettings)
                as JsonWriterGroupMessageDataType;

            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            JsonNetworkMessage uaNetworkMessage = publisherConnection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                JsonNetworkMessage;
            Assert.IsNotNull(uaNetworkMessage, "CreateNetworkMessage did not return an UadpNetworkMessage.");

            bool hasDataSetWriterId = (jsonNetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0
                && (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                mqttBrokerUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: hasDataSetWriterId,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubcriberConnection = MessagesHelper.GetConnection(subscriberConfiguration, publisherId);
            Assert.IsNotNull(mqttSubcriberConnection, "The MQTT subscriber connection is invalid.");
            mqttSubcriberConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttSubcriberConnection.ConnectionProperties, "The MQTT subscriber connection properties are not valid.");

            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections.First();
            Assert.IsNotNull(subscriberConnection, "Subscriber first connection should not be null");

            //Act
            // it will signal if the uadp message was received from local ip
            m_shutdownEvent = new ManualResetEvent(false);

            subscriberApplication.DataReceived += UaPubSubApplication_DataReceived;
            subscriberConnection.Start();

            publisherConnection.Start();
            publisherConnection.PublishNetworkMessage(uaNetworkMessage);

            //Assert
            if (!m_shutdownEvent.WaitOne(EstimatedPublishingTime))
            {
                Assert.Fail("The JSON message was not received");
            }

            subscriberConnection.Stop();
            publisherConnection.Stop();

        }

        [Test(Description = "Validate mqtt to public web broker pub/sub connections with certificates.")]
        public void ValidateMqttPubSubConnectionToWebBrokerWithCertificates(
           [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId,
           [Values("mqtt://test.mosquitto.org:1883", "mqtt://broker.hivemq.com:1883")]
                string mqttBrokerUrl)
        {
            //Arrange
            UInt16 writerGroupId = 1;

            // Certificates = new List<X509Certificate> { caCert, clientCert }

            ITransportProtocolConfiguration mqttConfiguration =
                new MqttClientProtocolConfiguration(
                    mqttTlsOptions: new MqttTlsOptions(allowUntrustedCertificates: true),
                    version: EnumMqttProtocolVersion.V500);

            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.PublisherId
                | JsonNetworkMessageContentMask.DataSetMessageHeader;
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.None;

            DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                mqttBrokerUrl, publisherId: publisherId, writerGroupId: writerGroupId,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(publisherConfiguration, publisherId);
            Assert.IsNotNull(mqttPublisherConnection, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttPublisherConnection.ConnectionProperties, "The MQTT publisher connection properties are not valid.");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(publisherConnection, "Publisher first connection should not be null");

            WriterGroupDataType writerGroup = MessagesHelper.GetWriterGroup(mqttPublisherConnection, writerGroupId);
            JsonWriterGroupMessageDataType messageSettings = ExtensionObject.ToEncodeable(writerGroup.MessageSettings)
                as JsonWriterGroupMessageDataType;

            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            JsonNetworkMessage uaNetworkMessage = publisherConnection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                JsonNetworkMessage;
            Assert.IsNotNull(uaNetworkMessage, "CreateNetworkMessage did not return an UadpNetworkMessage.");

            bool hasDataSetWriterId = (jsonNetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0
                && (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                mqttBrokerUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: hasDataSetWriterId,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubcriberConnection = MessagesHelper.GetConnection(subscriberConfiguration, publisherId);
            Assert.IsNotNull(mqttSubcriberConnection, "The MQTT subscriber connection is invalid.");
            mqttSubcriberConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttSubcriberConnection.ConnectionProperties, "The MQTT subscriber connection properties are not valid.");

            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections.First();
            Assert.IsNotNull(subscriberConnection, "Subscriber first connection should not be null");

            //Act
            // it will signal if the uadp message was received from local ip
            m_shutdownEvent = new ManualResetEvent(false);

            subscriberApplication.DataReceived += UaPubSubApplication_DataReceived;
            subscriberConnection.Start();

            publisherConnection.Start();
            publisherConnection.PublishNetworkMessage(uaNetworkMessage);

            //Assert
            if (!m_shutdownEvent.WaitOne(EstimatedPublishingTime))
            {
                Assert.Fail("The JSON message was not received");
            }

            subscriberConnection.Stop();
            publisherConnection.Stop();

        }

        [Test(Description = "Validate mqtt Azure hub pub/sub connections.")]
        public void ValidateMqttPubSubConnectionToAzureHub(
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            //Arrange
            UInt16 writerGroupId = 1;

            string azureHostname = "<hostname>.azure-devices.net";
            string azureBrokerUrl = $"mqtts://{azureHostname}:8883";
            string azureClientId = "<deviceId>";
            string publisherTopic = "devices/<deviceId>/messages/events";
                        
            string mqttUser = "<azure_user>";
            SecureString secureUser = new SecureString();
            Array.ForEach(mqttUser.ToArray(), secureUser.AppendChar);
            secureUser.MakeReadOnly();

            string mqttPassword = "<azure_password>";
            SecureString securePassword = new SecureString();
            Array.ForEach(mqttPassword.ToArray(), securePassword.AppendChar);
            securePassword.MakeReadOnly();

            string storeName = "OPC Foundation";
            //string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            //CopyResourceToStore(localAppData, storeName, @"resources\BaltimoreCyberTrustRoot.crt.pem");
            
            CertificateStoreIdentifier trustedIssuerCertificates = new CertificateTrustList {
                StoreType = "Directory",
                StorePath = $"%LocalApplicationData%/{storeName}/pki/issuer"
            };
            CertificateStoreIdentifier trustedPeerCertificates = new CertificateTrustList {
                StoreType = "Directory",
                StorePath = $"%LocalApplicationData%/{storeName}/pki/trusted"
            };
            CertificateStoreIdentifier rejectedCertificateStore = new CertificateTrustList {
                StoreType = "Directory",
                StorePath = $"%LocalApplicationData%/{storeName}/pki/rejected"
            };

            ITransportProtocolConfiguration mqttConfiguration = new MqttClientProtocolConfiguration(version: EnumMqttProtocolVersion.V311,
               userName: secureUser, password: securePassword, azureClientId: azureClientId,
               mqttTlsOptions: new MqttTlsOptions(ignoreRevocationListErrors: true,
                    ignoreCertificateChainErrors: true,
                    //allowUntrustedCertificates: true,
                    trustedIssuerCertificates: trustedIssuerCertificates,
                    trustedPeerCertificates: trustedPeerCertificates,
                    rejectedCertificateStore: rejectedCertificateStore)
                );

            //ITransportProtocolConfiguration mqttConfiguration = new MqttClientProtocolConfiguration(version: EnumMqttProtocolVersion.V311,
            //    userName: secureUser, password: securePassword, azureClientId: azureClientId,
            //    mqttTlsOptions: new MqttTlsOptions(ignoreRevocationListErrors: true,
            //         ignoreCertificateChainErrors: true,
            //         allowUntrustedCertificates: true,
            //         certificates: new MqttTlsCertificates(
            //             caCertificatePath: Path.Combine(localAppData, Path.Combine(storeName, @"pki\trusted\certs\BaltimoreCyberTrustRoot.crt.pem"))),
            //         trustedIssuerCertificates: trustedIssuerCertificates,
            //         trustedPeerCertificates: trustedPeerCertificates,
            //         rejectedCertificateStore: rejectedCertificateStore)
            //     );

            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.PublisherId
                | JsonNetworkMessageContentMask.DataSetMessageHeader;
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.None;

            DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                //MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                //MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreateAzurePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                azureBrokerUrl, publisherId: publisherId, writerGroupId: writerGroupId,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes, topic: publisherTopic);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(publisherConfiguration, publisherId);
            Assert.IsNotNull(mqttPublisherConnection, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttPublisherConnection.ConnectionProperties, "The MQTT publisher connection properties are not valid.");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(publisherConnection, "Publisher first connection should not be null");

            WriterGroupDataType writerGroup = MessagesHelper.GetWriterGroup(mqttPublisherConnection, writerGroupId);
            JsonWriterGroupMessageDataType messageSettings = ExtensionObject.ToEncodeable(writerGroup.MessageSettings)
                as JsonWriterGroupMessageDataType;

            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            JsonNetworkMessage uaNetworkMessage = publisherConnection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                JsonNetworkMessage;
            Assert.IsNotNull(uaNetworkMessage, "CreateNetworkMessage did not return an JsonNetworkMessage.");

            bool hasDataSetWriterId = (jsonNetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0
                && (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateAzureSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                azureBrokerUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: hasDataSetWriterId,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes, topic: publisherTopic);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubcriberConnection = MessagesHelper.GetConnection(subscriberConfiguration, publisherId);
            Assert.IsNotNull(mqttSubcriberConnection, "The MQTT subscriber connection is invalid.");
            mqttSubcriberConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttSubcriberConnection.ConnectionProperties, "The MQTT subscriber connection properties are not valid.");

            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections.First();
            Assert.IsNotNull(subscriberConnection, "Subscriber first connection should not be null");

            //Act
            // it will signal if the uadp message was received from local ip
            m_shutdownEvent = new ManualResetEvent(false);

            subscriberApplication.DataReceived += UaPubSubApplication_DataReceived;
            subscriberConnection.Start();

            publisherConnection.Start();
            publisherConnection.PublishNetworkMessage(uaNetworkMessage);

            //Assert
            if (!m_shutdownEvent.WaitOne(EstimatedPublishingTime))
            {
                Assert.Fail("The JSON message was not received");
            }

            subscriberConnection.Stop();
            publisherConnection.Stop();

            /*
            // publish message 
            var payload = Encoding.ASCII.GetBytes(< some json string >);
            var message = new MqttApplicationMessage {
                Topic = topic,
                Payload = payload,
                QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce,
                ContentType = "application/json"
            };

            mqttClient.PublishAsync(message, token).Wait(token);
            */
        }

        /// <summary>
        /// Data received handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UaPubSubApplication_DataReceived(object sender, SubscribedDataEventArgs e)
        {
            m_shutdownEvent.Set();
        }
        
        /// <summary>
        /// Get first active nic on local computer
        /// </summary>
        /// <returns></returns>
        private static IPAddress GetFirstActiveNic()
        {
            IPAddress firstActiveIPAddr = null;
            string localComputerName = Dns.GetHostName();
            try
            { // get host IP addresses
                IPAddress[] hostIPs = Dns.GetHostAddresses(localComputerName);
                // get local IP addresses
                IPAddress[] localIPs = Dns.GetHostAddresses(Dns.GetHostName());

                // test if any host IP equals to any local IP or to localhost
                foreach (IPAddress hostIP in hostIPs)
                {
                    // is loopback type?
                    if (IPAddress.IsLoopback(hostIP))
                    {
                        continue;
                    }
                    // ip address available
                    foreach (IPAddress localIP in localIPs)
                    {
                        if (hostIP.Equals(localIP))
                        {
                            firstActiveIPAddr = localIP;
                        }
                    }
                }
            }
            catch
            {
            }

            return firstActiveIPAddr;
        }

        /// <summary>
        /// Start/stop local mosquitto
        /// </summary>
        /// <param name="processName"></param>
        /// <param name="arguments"></param>
        private void RestartMosquitto(string processName, string arguments = "")
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(processName);
                if (processes.Length > 0)
                {
                    Process mosquittoProcess = processes[0];
                    mosquittoProcess.Kill();
                }

                Process process = new Process();
                ProcessStartInfo startInfo =
                   new ProcessStartInfo(
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        Path.Combine(processName, $"{processName}.exe")));
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                //startInfo.CreateNoWindow = true;
                //startInfo.RedirectStandardOutput = true;
                //startInfo.UseShellExecute = true;
                //startInfo.Verb = "runas";
                startInfo.Arguments = arguments;
                process.StartInfo = startInfo;
                process.Start();
                //proc.WaitForExit();
                processes = Process.GetProcessesByName(processName);
            }
            catch(Exception)
            {
                Assert.Fail("The mosquitto could not be restarted!");
            }

        }

        /// <summary>
        /// Copy to the test store where the certificates that will be loaded
        /// </summary>
        private void CopyResourceToStore(string storeLocation, string folderStoreName, string certificateFile)
        {
            string certificateSourcePath = Path.Combine(Directory.GetCurrentDirectory(), certificateFile);
            if (Directory.Exists(storeLocation))
            {
                try
                {
                    string opcFoundationStorePath = Path.Combine(storeLocation, folderStoreName);
                    if (!Directory.Exists(opcFoundationStorePath))
                    {
                        Directory.CreateDirectory(opcFoundationStorePath);
                    }
                    string pkiStorePath = Path.Combine(opcFoundationStorePath, "pki");
                    if (!Directory.Exists(pkiStorePath))
                    {
                        Directory.CreateDirectory(pkiStorePath);
                    }
                    string pkiTrustedStorePath = Path.Combine(pkiStorePath, "trusted");
                    if (!Directory.Exists(pkiTrustedStorePath))
                    {
                        Directory.CreateDirectory(pkiTrustedStorePath);
                    }
                    string pkiTrustedCertsStorePath = Path.Combine(pkiTrustedStorePath, "certs");
                    if (!Directory.Exists(pkiTrustedCertsStorePath))
                    {
                        Directory.CreateDirectory(pkiTrustedCertsStorePath);
                    }
                    string pkiRejectedStorePath = Path.Combine(pkiStorePath, "rejected");
                    if (!Directory.Exists(pkiRejectedStorePath))
                    {
                        Directory.CreateDirectory(pkiRejectedStorePath);
                    }
                    string pkiRejectedCertsStorePath = Path.Combine(pkiRejectedStorePath, "certs");
                    if (!Directory.Exists(pkiRejectedCertsStorePath))
                    {
                        Directory.CreateDirectory(pkiRejectedCertsStorePath);
                    }

                    string certificateStoreFile = Path.Combine(pkiTrustedCertsStorePath, Path.GetFileName(certificateSourcePath));
                    File.Copy(certificateSourcePath, certificateStoreFile, true);
                }
                catch (Exception ex)
                {
                    Assert.IsNotNull(storeLocation, "Certificate could not be loaded to store: {0}!", storeLocation);
                }
            }
            else
            {
                Assert.IsNotNull(storeLocation, "Directory store location does not exists!");
            }
        }
    }
}
