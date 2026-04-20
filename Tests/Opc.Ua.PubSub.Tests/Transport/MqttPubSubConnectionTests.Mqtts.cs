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

using System.Collections.Generic;
using System.Threading;
#if !NET8_0_OR_GREATER
using MQTTnet.Client;
#endif
using NUnit.Framework;
using Opc.Ua.PubSub.Tests.Encoding;
using Opc.Ua.PubSub.Transport;
using Opc.Ua.Tests;
using PubSubEncoding = Opc.Ua.PubSub.Encoding;
using System.Linq;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using MQTTnet;
using Opc.Ua.Security.Certificates;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Opc.Ua.PubSub.Tests.Transport
{
    [TestFixture(Description = "Tests for Mqtt connections")]
    public partial class MqttPubSubConnectionTests
    {
        internal const string MqttsUrlFormat = $"{Utils.UriSchemeMqtts}://{{0}}:8883";

        [Test]
        public void ClientCertificateHasPrivateKey()
        {
            using Certificate cert = CertificateBuilder.Create("CN=Subject").CreateForRSA();
            using TestCertificateDirectory certificateDirectory = new();
            certificateDirectory.CreateAssets();

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var mqttTlsCertificates = new MqttTlsCertificates(caCertificatePath: null, certificateDirectory.ClientCertificatePfxPath);
            var mqttTlsOptions = new MqttTlsOptions(certificates: mqttTlsCertificates);

            var mqttConfiguration = new MqttClientProtocolConfiguration(
                version: EnumMqttProtocolVersion.V500, mqttTlsOptions: mqttTlsOptions);

            using var uaPubSubApplication = UaPubSubApplication.Create(telemetry);
            var pubSubConnectionDataType = new PubSubConnectionDataType
            {
                Enabled = true,
                Address = new ExtensionObject(new NetworkAddressUrlDataType { Url = "mqtts://localhost:8883" }),
                ConnectionProperties = mqttConfiguration.ConnectionProperties
            };

            using var pubSubConnection = new MqttPubSubConnection(uaPubSubApplication, pubSubConnectionDataType, MessageMapping.Json, telemetry);
            MqttClientOptions mqttClientOptions = pubSubConnection.PublisherMqttClientOptions;
            MqttClientTlsOptions channelTlsOptions = mqttClientOptions.ChannelOptions.TlsOptions;

            Assert.That(channelTlsOptions.UseTls, Is.True);
            X509CertificateCollection clientCertificates = channelTlsOptions.ClientCertificatesProvider.GetCertificates();
            Assert.That(clientCertificates, Has.Count.EqualTo(1));
            Assert.That((clientCertificates[0] as X509Certificate2)!.HasPrivateKey, Is.True, "Client certificate needs private key");
        }

#if NET7_0_OR_GREATER
        [Test(Description = "Validate mqtts local pub/sub connection with json data.")]
#if !CUSTOM_TESTS
        [Ignore("A mosquitto tool should be installed local in order to run correctly.")]
#endif
        public void ValidateMqttsLocalPubSubConnectionWithJson(
            [ValueSource(nameof(s_validPublisherIds))] Variant publisherId,
            [Values(0, 10000)] double metaDataUpdateTime)
        {
            using TestCertificateDirectory certificateDirectory = new();
            certificateDirectory.CreateAssets();

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Process process = RestartMosquitto($"-v -c \"{certificateDirectory.MosquittoConfigFilePath}\"");

            //Arrange
            const ushort writerGroupId = 1;

            string mqttLocalBrokerUrl = Utils.Format(
                MqttsUrlFormat,
                "localhost");

            var mqttTlsCertificates = new MqttTlsCertificates(
                clientCertificatePath: certificateDirectory.ClientCertificatePfxPath);
            var mqttTlsOptions = new MqttTlsOptions(certificates: mqttTlsCertificates);

            var mqttConfiguration = new MqttClientProtocolConfiguration(
                version: EnumMqttProtocolVersion.V500,
                mqttTlsOptions: mqttTlsOptions);

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
            publisherApplication.OnValidateBrokerCertificate = certificateDirectory.ValidateBrokerCertificate;
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

            // Create subscriber application for multiple datasets
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration, telemetry);
            subscriberApplication.OnValidateBrokerCertificate = certificateDirectory.ValidateBrokerCertificate;
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication should not be null");
            Assert.That(
                subscriberApplication.PubSubConnections[0],
                Is.Not.Null,
                "subscriberConfiguration first connection should not be null");

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
#endif

        /// <summary>
        /// Creates a temp directory with client and server certificate files and a mqtts mosquitto config.
        /// Deletes the directory on dispose.
        /// </summary>
        private sealed class TestCertificateDirectory : IDisposable
        {
            private readonly string m_path;
            private readonly Certificate m_clientCert;
            private readonly Certificate m_serverCert;

            public TestCertificateDirectory()
            {
                m_path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

                m_clientCert = CertificateBuilder.Create("CN=Client").CreateForRSA();
                m_serverCert = CertificateBuilder.Create("CN=Server").CreateForRSA();
            }

            public void CreateAssets()
            {
                Directory.CreateDirectory(m_path);

                ClientCertificatePfxPath = CombinePath("client.pfx");
                string clientCertificateDerPath = CombinePath("client.der");
                string clientCertificateCrtPath = CombinePath("client.crt");
                string serverCertificateDerPath = CombinePath("server.der");
                string serverCertificateKeyPath = CombinePath("server.key");

                File.WriteAllBytes(ClientCertificatePfxPath, m_clientCert.X509.Export(X509ContentType.Pfx));
                File.WriteAllBytes(clientCertificateDerPath, m_clientCert.X509.Export(X509ContentType.Cert));
#if NET7_0_OR_GREATER
                string clientCertificatePem = m_clientCert.X509.ExportCertificatePem();
                File.WriteAllText(clientCertificateCrtPath, clientCertificatePem);

                ServerCertificateCertPath = CombinePath("server.crt");

                string serverCertificatePem = m_serverCert.X509.ExportCertificatePem();

                AsymmetricAlgorithm key = m_serverCert.GetRSAPrivateKey();
                string privKeyPem = key.ExportPkcs8PrivateKeyPem();

                File.WriteAllText(serverCertificateKeyPath, privKeyPem);
                File.WriteAllText(ServerCertificateCertPath, serverCertificatePem);
#endif
                File.WriteAllBytes(serverCertificateDerPath, m_serverCert.X509.Export(X509ContentType.Cert));

                string mosquittoTlsConfig = CreateMosquittoTlsConfig(clientCertificateCrtPath,
                    serverCertificateKeyPath, ServerCertificateCertPath);
                MosquittoConfigFilePath = CombinePath("mosquitto.conf");

                File.WriteAllText(MosquittoConfigFilePath, mosquittoTlsConfig);
            }

            public string MosquittoConfigFilePath { get; private set; }
            public string ClientCertificatePfxPath { get; private set; }
            public string ServerCertificateCertPath { get; set; }

            private string CombinePath(string fileName)
            {
                return Path.Combine(m_path, fileName);
            }

            private static string CreateMosquittoTlsConfig(string caFile, string keyFile, string certFile)
            {
                return new StringBuilder()
                    .AppendLine("listener 8883")
                    .Append("cafile ").AppendLine(caFile)
                    .Append("keyfile ").AppendLine(keyFile)
                    .Append("certfile ").AppendLine(certFile)
                    .AppendLine("require_certificate true")
                    .AppendLine("allow_anonymous true")
                    .AppendLine("log_type all")
                    .AppendLine("log_dest stderr")
                    .AppendLine("connection_messages true")
                    .ToString();
            }

            public void Dispose()
            {
#pragma warning disable RCS1075 // Avoid empty catch clause that catches System.Exception
                try
                {
                    Directory.Delete(m_path, true);
                    m_clientCert?.Dispose();
                    m_serverCert?.Dispose();
                }
                catch (Exception)
                {
                }
#pragma warning restore RCS1075 // Avoid empty catch clause that catches System.Exception
            }

            internal bool ValidateBrokerCertificate(Certificate brokerCertificate)
            {
                return string.Equals(brokerCertificate.Thumbprint, m_serverCert.Thumbprint, StringComparison.OrdinalIgnoreCase);
            }

            public static implicit operator string(TestCertificateDirectory dir)
            {
                return dir?.m_path ?? string.Empty;
            }
        }
    }
}