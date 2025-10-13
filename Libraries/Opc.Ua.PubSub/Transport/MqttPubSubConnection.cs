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
using System.Data;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using Opc.Ua.PubSub.Encoding;
using DataSet = Opc.Ua.PubSub.PublishedData.DataSet;
using Microsoft.Extensions.Logging;

#if !NET8_0_OR_GREATER
using MQTTnet.Client;
#else
using System.Buffers;
#endif

namespace Opc.Ua.PubSub.Transport
{
    /// <summary>
    /// MQTT implementation of <see cref="UaPubSubConnection"/> class.
    /// </summary>
    internal sealed class MqttPubSubConnection : UaPubSubConnection, IMqttPubSubConnection
    {
        private readonly int m_reconnectIntervalSeconds = 5;

        private MqttClient m_publisherMqttClient;
        private MqttClient m_subscriberMqttClient;
        private readonly MessageMapping m_messageMapping;
        private readonly MessageCreator m_messageCreator;

        private CertificateValidator m_certificateValidator;
        private MqttClientTlsOptions m_mqttClientTlsOptions;
        private MqttClientOptions m_publisherMqttClientOptions;
        private MqttClientOptions m_subscriberMqttClientOptions;
        private readonly List<MqttMetadataPublisher> m_metaDataPublishers = [];

        /// <summary>
        /// Gets the host name or IP address of the broker.
        /// </summary>
        public string BrokerHostName { get; private set; } = "localhost";

        /// <summary>
        /// Gets the port of the mqttConnection.
        /// </summary>
        public int BrokerPort { get; private set; } = Utils.MqttDefaultPort;

        /// <summary>
        /// Gets the scheme of the Url.
        /// </summary>
        public string UrlScheme { get; private set; }

        /// <summary>
        /// Gets and sets the MqttClientOptions for the publisher connection
        /// </summary>
        /// <exception cref="InvalidConstraintException"></exception>
        public MqttClientOptions PublisherMqttClientOptions
        {
            get
            {
                if (!IsRunning)
                {
                    return m_publisherMqttClientOptions;
                }

                throw new InvalidConstraintException(
                    "Can't access PublisherMqttClientOptions if connection is started");
            }
            set
            {
                if (!IsRunning)
                {
                    m_publisherMqttClientOptions = value;
                }
                else
                {
                    throw new InvalidConstraintException(
                        "Can't change PublisherMqttClientOptions if connection is started");
                }
            }
        }

        /// <summary>
        /// Gets and sets the MqttClientOptions for the subscriber connection
        /// </summary>
        /// <exception cref="InvalidConstraintException"></exception>
        public MqttClientOptions SubscriberMqttClientOptions
        {
            get
            {
                if (!IsRunning)
                {
                    return m_subscriberMqttClientOptions;
                }

                throw new InvalidConstraintException(
                    "Can't access SubscriberMqttClientOptions if connection is started");
            }
            set
            {
                if (!IsRunning)
                {
                    m_subscriberMqttClientOptions = value;
                }
                else
                {
                    throw new InvalidConstraintException(
                        "Can't change SubscriberMqttClientOptions if connection is started");
                }
            }
        }

        /// <summary>
        /// Value in seconds with which to surpass the max keep alive value found.
        /// </summary>
        private readonly int m_maxKeepAliveIncrement = 5;

        /// <summary>
        ///  Create new instance of <see cref="MqttPubSubConnection"/> from
        ///  <see cref="PubSubConnectionDataType"/> configuration data
        /// </summary>
        public MqttPubSubConnection(
            UaPubSubApplication uaPubSubApplication,
            PubSubConnectionDataType pubSubConnectionDataType,
            MessageMapping messageMapping,
            ITelemetryContext telemetry)
            : base(
                uaPubSubApplication,
                pubSubConnectionDataType,
                telemetry,
                telemetry.CreateLogger<MqttPubSubConnection>())
        {
            m_transportProtocol = TransportProtocol.MQTT;
            m_messageMapping = messageMapping;

            // initialize the message creators for current message
            if (m_messageMapping == MessageMapping.Json)
            {
                m_messageCreator = new JsonMessageCreator(this, telemetry);
            }
            else if (m_messageMapping == MessageMapping.Uadp)
            {
                m_messageCreator = new UadpMessageCreator(this, telemetry);
            }
            else
            {
                m_logger.LogError(
                    Utils.TraceMasks.Error,
                    "The current MessageMapping {MessageMapping} does not have a valid message creator",
                    m_messageMapping);
            }

            m_publisherMqttClientOptions = GetMqttClientOptions();
            m_subscriberMqttClientOptions = GetMqttClientOptions();

            m_logger.LogInformation(
                "MqttPubSubConnection with name '{Name}' was created.",
                pubSubConnectionDataType.Name);
        }

        /// <summary>
        /// Determine if the connection can publish metadata for specified writer group and data set writer
        /// </summary>
        public bool CanPublishMetaData(
            WriterGroupDataType writerGroupConfiguration,
            DataSetWriterDataType dataSetWriter)
        {
            return CanPublish(writerGroupConfiguration) &&
                Application.UaPubSubConfigurator
                    .FindStateForObject(dataSetWriter) == PubSubState.Operational;
        }

        /// <summary>
        /// Create the list of network messages built from the provided writerGroupConfiguration
        /// </summary>
        public override IList<UaNetworkMessage> CreateNetworkMessages(
            WriterGroupDataType writerGroupConfiguration,
            WriterGroupPublishState state)
        {
            if (ExtensionObject.ToEncodeable(writerGroupConfiguration.TransportSettings)
                is not BrokerWriterGroupTransportDataType)
            {
                //Wrong configuration of writer group MessageSettings
                return null;
            }

            if (m_messageCreator != null)
            {
                return m_messageCreator.CreateNetworkMessages(writerGroupConfiguration, state);
            }

            // no other encoding is implemented
            return null;
        }

        /// <summary>
        /// Create and return the DataSetMetaData message for a DataSetWriter
        /// </summary>
        public UaNetworkMessage CreateDataSetMetaDataNetworkMessage(
            WriterGroupDataType writerGroup,
            DataSetWriterDataType dataSetWriter)
        {
            PublishedDataSetDataType publishedDataSet = Application.DataCollector
                .GetPublishedDataSet(
                    dataSetWriter.DataSetName);
            if (publishedDataSet != null &&
                publishedDataSet.DataSetMetaData != null &&
                m_messageCreator != null)
            {
                return m_messageCreator.CreateDataSetMetaDataNetworkMessage(
                    writerGroup,
                    dataSetWriter.DataSetWriterId,
                    publishedDataSet.DataSetMetaData);
            }
            return null;
        }

        /// <summary>
        /// Publish the network message
        /// </summary>
        public override bool PublishNetworkMessage(UaNetworkMessage networkMessage)
        {
            if (networkMessage == null || m_publisherMqttClient == null)
            {
                return false;
            }

            try
            {
                lock (Lock)
                {
                    if (m_publisherMqttClient != null && m_publisherMqttClient.IsConnected)
                    {
                        // get the encoded bytes
                        byte[] bytes = networkMessage.Encode(MessageContext);

                        try
                        {
                            string queueName = null;
                            BrokerTransportQualityOfService qos
                                = BrokerTransportQualityOfService.AtLeastOnce;

                            // the network messages that have DataSetWriterId are either metaData messages or SingleDataSet messages and
                            if (networkMessage.DataSetWriterId != null)
                            {
                                DataSetWriterDataType dataSetWriter =
                                    networkMessage.WriterGroupConfiguration.DataSetWriters.Find(x =>
                                        x.DataSetWriterId == networkMessage.DataSetWriterId);

                                if (dataSetWriter != null &&
                                    ExtensionObject.ToEncodeable(dataSetWriter.TransportSettings)
                                        is BrokerDataSetWriterTransportDataType transportSettings)
                                {
                                    qos = transportSettings.RequestedDeliveryGuarantee;

                                    queueName = networkMessage.IsMetaDataMessage
                                        ? transportSettings.MetaDataQueueName
                                        : transportSettings.QueueName;
                                }
                            }

                            if (queueName == null ||
                                qos == BrokerTransportQualityOfService.NotSpecified)
                            {
                                if (ExtensionObject.ToEncodeable(
                                        networkMessage.WriterGroupConfiguration.TransportSettings)
                                    is BrokerWriterGroupTransportDataType transportSettings)
                                {
                                    queueName ??= transportSettings.QueueName;
                                    // if the value is not specified and the value of the parent object shall be used
                                    if (qos == BrokerTransportQualityOfService.NotSpecified)
                                    {
                                        qos = transportSettings.RequestedDeliveryGuarantee;
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(queueName))
                            {
                                var message = new MqttApplicationMessage
                                {
                                    Topic = queueName,
                                    PayloadSegment = new ArraySegment<byte>(bytes),
                                    QualityOfServiceLevel = GetMqttQualityOfServiceLevel(qos),
                                    Retain = networkMessage.IsMetaDataMessage
                                };

                                m_publisherMqttClient.PublishAsync(message).GetAwaiter()
                                    .GetResult();
                            }
                        }
                        catch (Exception ex)
                        {
                            m_logger.LogError(ex, "MqttPubSubConnection.PublishNetworkMessage");
                            return false;
                        }

                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "MqttPubSubConnection.PublishNetworkMessage");
                return false;
            }

            return false;
        }

        /// <summary>
        /// Get flag that indicates if all the network connections are active and connected
        /// </summary>
        public override bool AreClientsConnected()
        {
            // Check if existing clients are connected
            return (m_publisherMqttClient == null || m_publisherMqttClient.IsConnected) &&
                (m_subscriberMqttClient == null || m_subscriberMqttClient.IsConnected);
        }

        /// <summary>
        /// Perform specific Start tasks
        /// </summary>
        protected override async Task InternalStart()
        {
            //cleanup all existing MQTT connections previously open
            await InternalStop().ConfigureAwait(false);

            lock (Lock)
            {
                if (ExtensionObject.ToEncodeable(PubSubConnectionConfiguration.Address)
                    is not NetworkAddressUrlDataType networkAddressUrlState)
                {
                    m_logger.LogError(
                        "The configuration for mqttConnection {Name} has invalid Address configuration.",
                        PubSubConnectionConfiguration.Name);

                    return;
                }

                UrlScheme = null;

                if (networkAddressUrlState.Url != null &&
                    Uri.TryCreate(
                        networkAddressUrlState.Url,
                        UriKind.Absolute,
                        out Uri connectionUri) &&
                    connectionUri.Scheme is Utils.UriSchemeMqtt or Utils.UriSchemeMqtts &&
                    !string.IsNullOrEmpty(connectionUri.Host))
                {
                    BrokerHostName = connectionUri.Host;
                    BrokerPort =
                        connectionUri.Port > 0
                            ? connectionUri.Port
                            : (connectionUri.Scheme == Utils.UriSchemeMqtt ? 1883 : 8883);
                    UrlScheme = connectionUri.Scheme;
                }

                if (UrlScheme == null)
                {
                    m_logger.LogError(
                        "The configuration for mqttConnection {Name} has invalid MQTT URL '{Url}'.",
                        PubSubConnectionConfiguration.Name,
                        networkAddressUrlState.Url);

                    return;
                }

                // create the DataSetMetaData publishers
                foreach (WriterGroupDataType writerGroup in PubSubConnectionConfiguration
                    .WriterGroups)
                {
                    foreach (DataSetWriterDataType dataSetWriter in writerGroup.DataSetWriters)
                    {
                        if (dataSetWriter.DataSetWriterId == 0)
                        {
                            continue;
                        }

                        if (ExtensionObject.ToEncodeable(dataSetWriter.TransportSettings)
                                is not BrokerDataSetWriterTransportDataType transport ||
                            transport.MetaDataUpdateTime == 0)
                        {
                            continue;
                        }

                        m_metaDataPublishers.Add(
                            new MqttMetadataPublisher(
                                this,
                                writerGroup,
                                dataSetWriter,
                                transport.MetaDataUpdateTime,
                                Telemetry));
                    }
                }

                // start the mqtt metadata publishers
                foreach (MqttMetadataPublisher metaDataPublisher in m_metaDataPublishers)
                {
                    metaDataPublisher.Start();
                }
            }

            MqttClient publisherClient = null;
            MqttClient subscriberClient = null;

            m_publisherMqttClientOptions ??= GetMqttClientOptions();

            int nrOfPublishers = Publishers.Count;
            int nrOfSubscribers = GetAllDataSetReaders().Count;

            //publisher initialization
            if (nrOfPublishers > 0)
            {
                publisherClient = (MqttClient)
                    await MqttClientCreator
                        .GetMqttClientAsync(
                            m_reconnectIntervalSeconds,
                            m_publisherMqttClientOptions,
                            null,
                            m_logger)
                        .ConfigureAwait(false);
            }

            //subscriber initialization
            if (nrOfSubscribers > 0)
            {
                // collect all topics from all ReaderGroups
                var topics = new StringCollection();
                foreach (ReaderGroupDataType readerGroup in PubSubConnectionConfiguration
                    .ReaderGroups)
                {
                    if (!readerGroup.Enabled)
                    {
                        continue;
                    }

                    foreach (DataSetReaderDataType dataSetReader in readerGroup.DataSetReaders)
                    {
                        if (!dataSetReader.Enabled)
                        {
                            continue;
                        }

                        if (ExtensionObject.ToEncodeable(dataSetReader.TransportSettings)
                                is BrokerDataSetReaderTransportDataType brokerTransportSettings &&
                            !topics.Contains(brokerTransportSettings.QueueName))
                        {
                            topics.Add(brokerTransportSettings.QueueName);

                            if (brokerTransportSettings.MetaDataQueueName != null)
                            {
                                topics.Add(brokerTransportSettings.MetaDataQueueName);
                            }
                        }
                    }
                }

                m_subscriberMqttClientOptions ??= GetMqttClientOptions();

                subscriberClient = (MqttClient)
                    await MqttClientCreator
                        .GetMqttClientAsync(
                            m_reconnectIntervalSeconds,
                            m_subscriberMqttClientOptions,
                            ProcessMqttMessage,
                            m_logger,
                            topics)
                        .ConfigureAwait(false);
            }

            lock (Lock)
            {
                m_publisherMqttClient = publisherClient;
                m_subscriberMqttClient = subscriberClient;
            }

            m_logger.LogInformation(
                "Connection '{Name}' started {Publishers} publishers and {Subscribers} subscribers.",
                PubSubConnectionConfiguration.Name,
                nrOfPublishers,
                nrOfSubscribers);
        }

        /// <summary>
        /// Perform specific Stop tasks
        /// </summary>
        protected override async Task InternalStop()
        {
            IMqttClient publisherMqttClient = m_publisherMqttClient;
            IMqttClient subscriberMqttClient = m_subscriberMqttClient;

            void DisposeCerts(X509CertificateCollection certificates)
            {
                if (certificates != null)
                {
                    // dispose certificates
                    foreach (X509Certificate cert in certificates)
                    {
                        Utils.SilentDispose(cert);
                    }
                }
            }
            async Task InternalStop(IMqttClient client)
            {
                if (client != null)
                {
                    X509CertificateCollection certificates =
                        client.Options?.ChannelOptions?.TlsOptions?.ClientCertificatesProvider?
                            .GetCertificates();
                    if (client.IsConnected)
                    {
                        await client
                            .DisconnectAsync()
                            .ContinueWith(_ =>
                            {
                                DisposeCerts(certificates);
                                Utils.SilentDispose(client);
                            })
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        DisposeCerts(certificates);
                        Utils.SilentDispose(client);
                    }
                }
            }
            await InternalStop(publisherMqttClient).ConfigureAwait(false);
            await InternalStop(subscriberMqttClient).ConfigureAwait(false);

            if (m_metaDataPublishers != null)
            {
                foreach (MqttMetadataPublisher metaDataPublisher in m_metaDataPublishers)
                {
                    metaDataPublisher.Stop();
                }
                m_metaDataPublishers.Clear();
            }

            lock (Lock)
            {
                m_publisherMqttClient = null;
                m_subscriberMqttClient = null;
                m_mqttClientTlsOptions = null;
            }
        }

        private static bool MatchTopic(string pattern, string topic)
        {
            if (string.IsNullOrEmpty(pattern) || pattern == "#")
            {
                return true;
            }

            string[] fields1 = pattern.Split('/');
            string[] fields2 = topic.Split('/');

            for (int ii = 0; ii < fields1.Length && ii < fields2.Length; ii++)
            {
                if (fields1[ii] == "#")
                {
                    return true;
                }

                if (fields1[ii] != "+" && fields1[ii] != fields2[ii])
                {
                    return false;
                }
            }

            return fields1.Length == fields2.Length;
        }

        /// <summary>
        /// Processes a message from the MQTT broker.
        /// </summary>
        private Task ProcessMqttMessage(MqttApplicationMessageReceivedEventArgs eventArgs)
        {
            string topic = eventArgs.ApplicationMessage.Topic;

            m_logger.LogInformation("MQTTConnection - ProcessMqttMessage() received from topic={Topic}", topic);

            // get the datasetreaders for received message topic
            var dataSetReaders = new List<DataSetReaderDataType>();
            foreach (DataSetReaderDataType dsReader in GetOperationalDataSetReaders())
            {
                if (dsReader == null)
                {
                    continue;
                }

                var brokerDataSetReaderTransportDataType =
                    ExtensionObject.ToEncodeable(
                        dsReader.TransportSettings) as BrokerDataSetReaderTransportDataType;

                string queueName = brokerDataSetReaderTransportDataType.QueueName;
                string metadataQueueName = brokerDataSetReaderTransportDataType.MetaDataQueueName;

                if (!MatchTopic(queueName, topic))
                {
                    if (string.IsNullOrEmpty(metadataQueueName))
                    {
                        continue;
                    }

                    if (!MatchTopic(metadataQueueName, topic))
                    {
                        continue;
                    }
                }

                // At this point the message is accepted
                // if ((topic.Length == queueName.Length) && (topic == queueName)) || (queueName == #)
                dataSetReaders.Add(dsReader);
            }

            if (dataSetReaders.Count > 0)
            {
                // raise RawData received event
                var rawDataReceivedEventArgs = new RawDataReceivedEventArgs
                {
#if !NET8_0_OR_GREATER
                    Message = eventArgs.ApplicationMessage.PayloadSegment.Array,
#else
                    Message = eventArgs.ApplicationMessage.Payload.ToArray(),
#endif
                    Source = topic,
                    TransportProtocol = TransportProtocol,
                    MessageMapping = m_messageMapping,
                    PubSubConnectionConfiguration = PubSubConnectionConfiguration
                };

                // trigger notification for received raw data
                Application.RaiseRawDataReceivedEvent(rawDataReceivedEventArgs);

                // check if the RawData message is marked as handled
                if (rawDataReceivedEventArgs.Handled)
                {
                    m_logger.LogInformation(
                        "MqttConnection message from topic={Topic} is marked as handled and will not be decoded.",
                        topic);
                    return Task.CompletedTask;
                }

                // initialize the expected NetworkMessage
                UaNetworkMessage networkMessage = m_messageCreator.CreateNewNetworkMessage();

                // trigger message decoding
                if (networkMessage != null)
                {
#if !NET8_0_OR_GREATER
                    networkMessage.Decode(
                        MessageContext,
                        eventArgs.ApplicationMessage.PayloadSegment.Array,
                        dataSetReaders);
#else
                    networkMessage.Decode(
                        MessageContext,
                        eventArgs.ApplicationMessage.Payload.ToArray(),
                        dataSetReaders);
#endif

                    // Handle the decoded message and raise the necessary event on UaPubSubApplication
                    ProcessDecodedNetworkMessage(networkMessage, topic);
                }
            }
            else
            {
                m_logger.LogInformation(
                    "MqttConnection - ProcessMqttMessage() No DataSetReader is registered for topic={Topic}.",
                    topic);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Transform pub sub setting into MqttNet enum
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static MqttQualityOfServiceLevel GetMqttQualityOfServiceLevel(
            BrokerTransportQualityOfService brokerTransportQualityOfService)
        {
            switch (brokerTransportQualityOfService)
            {
                case BrokerTransportQualityOfService.AtLeastOnce:
                    return MqttQualityOfServiceLevel.AtLeastOnce;
                case BrokerTransportQualityOfService.AtMostOnce:
                    return MqttQualityOfServiceLevel.AtMostOnce;
                case BrokerTransportQualityOfService.ExactlyOnce:
                    return MqttQualityOfServiceLevel.ExactlyOnce;
                case BrokerTransportQualityOfService.NotSpecified:
                case BrokerTransportQualityOfService.BestEffort:
                    return MqttQualityOfServiceLevel.AtLeastOnce;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(brokerTransportQualityOfService),
                        brokerTransportQualityOfService,
                        "Unexpected service level");
            }
        }

        /// <summary>
        /// Get appropriate IMqttClientOptions with which to connect to the MQTTBroker
        /// </summary>
        private MqttClientOptions GetMqttClientOptions()
        {
            MqttClientOptions mqttOptions = null;
            var mqttKeepAlive = TimeSpan.FromSeconds(
                GetWriterGroupsMaxKeepAlive() + m_maxKeepAliveIncrement);

            if (ExtensionObject.ToEncodeable(PubSubConnectionConfiguration.Address)
                is not NetworkAddressUrlDataType networkAddressUrlState)
            {
                m_logger.LogError(
                    "The configuration for mqttConnection {Name} has invalid Address configuration.",
                    PubSubConnectionConfiguration.Name);
                return null;
            }

            Uri connectionUri = null;

            if (networkAddressUrlState.Url != null &&
                Uri.TryCreate(networkAddressUrlState.Url, UriKind.Absolute, out connectionUri) &&
                (connectionUri.Scheme != Utils.UriSchemeMqtt) &&
                (connectionUri.Scheme != Utils.UriSchemeMqtts))
            {
                m_logger.LogError(
                    "The configuration for mqttConnection '{Name}' has an invalid Url value {Url}. The Uri scheme should be either {Mqtt}:// or {Mqtts}://",
                    PubSubConnectionConfiguration.Name,
                    networkAddressUrlState.Url,
                    Utils.UriSchemeMqtt,
                    Utils.UriSchemeMqtts);
                return null;
            }

            if (connectionUri == null)
            {
                m_logger.LogError(
                    "The configuration for mqttConnection '{Name}' has an invalid Url value {Url}.",
                    PubSubConnectionConfiguration.Name,
                    networkAddressUrlState.Url);
                return null;
            }

            // Setup data needed also in mqttClientOptionsBuilder
            if (connectionUri.Scheme is Utils.UriSchemeMqtt or Utils.UriSchemeMqtts &&
                !string.IsNullOrEmpty(connectionUri.Host))
            {
                BrokerHostName = connectionUri.Host;
                BrokerPort =
                    connectionUri.Port > 0
                        ? connectionUri.Port
                        : (connectionUri.Scheme == Utils.UriSchemeMqtt ? 1883 : 8883);
                UrlScheme = connectionUri.Scheme;
            }

            ITransportProtocolConfiguration transportProtocolConfiguration
                = new MqttClientProtocolConfiguration(
                PubSubConnectionConfiguration.ConnectionProperties, m_logger);

            if (transportProtocolConfiguration is MqttClientProtocolConfiguration mqttProtocolConfiguration)
            {
                var mqttProtocolVersion = (MqttProtocolVersion)
                    ((MqttClientProtocolConfiguration)transportProtocolConfiguration)
                        .ProtocolVersion;
                // create uniques client id
                string clientId = $"ClientId_{new Random().Next():D10}";

                // MQTTS mqttConnection.
                if (connectionUri.Scheme == Utils.UriSchemeMqtts)
                {
                    MqttTlsOptions mqttTlsOptions = (
                        (MqttClientProtocolConfiguration)transportProtocolConfiguration
                    ).MqttTlsOptions;

                    var x509Certificate2s = new List<X509Certificate2>();
                    if (mqttTlsOptions?.Certificates != null)
                    {
                        foreach (X509Certificate x509cert in mqttTlsOptions?.Certificates
                            .X509Certificates)
                        {
                            if (x509cert is X509Certificate2 x509Certificate2)
                            {
                                x509Certificate2s.Add(
                                    X509CertificateLoader.LoadCertificate(
                                        x509Certificate2.RawData));
                            }
                        }
                    }

                    MqttClientOptionsBuilder mqttClientOptionsBuilder
                        = new MqttClientOptionsBuilder()
                        .WithTcpServer(BrokerHostName, BrokerPort)
                        .WithKeepAlivePeriod(mqttKeepAlive)
                        .WithProtocolVersion(mqttProtocolVersion)
                        .WithClientId(clientId)
                        .WithTlsOptions(o => o.UseTls(true)
                            .WithClientCertificates(x509Certificate2s)
                            .WithSslProtocols(
                                mqttTlsOptions?.SslProtocolVersion ??
                                System.Security.Authentication.SslProtocols.None)
                            .WithAllowUntrustedCertificates(
                                mqttTlsOptions?.AllowUntrustedCertificates ?? false)
                            .WithIgnoreCertificateChainErrors(
                                mqttTlsOptions?.IgnoreCertificateChainErrors ?? false)
                            .WithIgnoreCertificateRevocationErrors(
                                mqttTlsOptions?.IgnoreRevocationListErrors ?? false)
                            .WithCertificateValidationHandler(ValidateBrokerCertificate));

                    // Set user credentials.
                    if (mqttProtocolConfiguration.UseCredentials)
                    {
                        mqttClientOptionsBuilder.WithCredentials(
                            new System.Net.NetworkCredential(
                                string.Empty,
                                mqttProtocolConfiguration.UserName).Password,
                            new System.Net.NetworkCredential(
                                string.Empty,
                                mqttProtocolConfiguration.Password).Password);

                        // Set ClientId for Azure.
                        if (mqttProtocolConfiguration.UseAzureClientId)
                        {
                            mqttClientOptionsBuilder.WithClientId(
                                mqttProtocolConfiguration.AzureClientId);
                        }
                    }

                    mqttOptions = mqttClientOptionsBuilder.Build();

                    // Create the certificate validator for broker certificates.
                    m_certificateValidator = CreateCertificateValidator(mqttTlsOptions, Telemetry);
                    m_certificateValidator.CertificateValidation
                        += CertificateValidator_CertificateValidation;
                    m_mqttClientTlsOptions = mqttOptions?.ChannelOptions?.TlsOptions;
                }
                // MQTT mqttConnection
                else if (connectionUri.Scheme == Utils.UriSchemeMqtt)
                {
                    MqttClientOptionsBuilder mqttClientOptionsBuilder
                        = new MqttClientOptionsBuilder()
                        .WithTcpServer(BrokerHostName, BrokerPort)
                        .WithKeepAlivePeriod(mqttKeepAlive)
                        .WithClientId(clientId)
                        .WithProtocolVersion(mqttProtocolVersion);

                    // Set user credentials.
                    if (mqttProtocolConfiguration.UseCredentials)
                    {
                        // Following Password usage in both cases is correct since it is the Password position
                        // to be taken into account for the UserName to be read properly
                        mqttClientOptionsBuilder.WithCredentials(
                            new System.Net.NetworkCredential(
                                string.Empty,
                                mqttProtocolConfiguration.UserName).Password,
                            new System.Net.NetworkCredential(
                                string.Empty,
                                mqttProtocolConfiguration.Password).Password);
                    }

                    mqttOptions = mqttClientOptionsBuilder.Build();
                }
            }

            return mqttOptions;
        }

        /// <summary>
        /// Set up a new instance of a certificate validator based on passed in tls options
        /// </summary>
        /// <param name="mqttTlsOptions"><see cref="MqttTlsOptions"/></param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <returns>A new instance of stack validator <see cref="CertificateValidator"/></returns>
        private static CertificateValidator CreateCertificateValidator(
            MqttTlsOptions mqttTlsOptions,
            ITelemetryContext telemetry)
        {
            var certificateValidator = new CertificateValidator(telemetry);

            var securityConfiguration = new SecurityConfiguration
            {
                TrustedIssuerCertificates = (CertificateTrustList)mqttTlsOptions
                    .TrustedIssuerCertificates,
                TrustedPeerCertificates = (CertificateTrustList)mqttTlsOptions
                    .TrustedPeerCertificates,
                RejectedCertificateStore = mqttTlsOptions.RejectedCertificateStore,

                RejectSHA1SignedCertificates = true,
                AutoAcceptUntrustedCertificates = mqttTlsOptions.AllowUntrustedCertificates,
                RejectUnknownRevocationStatus = !mqttTlsOptions.IgnoreRevocationListErrors
            };

            certificateValidator.UpdateAsync(securityConfiguration).Wait();

            return certificateValidator;
        }

        /// <summary>
        /// Validates the broker certificate.
        /// </summary>
        /// <param name="context">The context of the validation</param>
        private bool ValidateBrokerCertificate(MqttClientCertificateValidationEventArgs context)
        {
            X509Certificate2 brokerCertificate = X509CertificateLoader.LoadCertificate(
                context.Certificate.GetRawCertData());

            try
            {
                // check if the broker certificate validation has been overridden.
                if (Application?.OnValidateBrokerCertificate != null)
                {
                    return Application.OnValidateBrokerCertificate(brokerCertificate);
                }

                m_certificateValidator?.Validate(brokerCertificate);
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "Connection '{Name}' - Broker certificate '{Subject}' rejected.",
                    PubSubConnectionConfiguration.Name,
                    brokerCertificate.Subject);
                return false;
            }

            m_logger.LogInformation(
                Utils.TraceMasks.Security,
                "Connection '{Name}' - Broker certificate '{Subject}'  accepted.",
                PubSubConnectionConfiguration.Name,
                brokerCertificate.Subject);
            return true;
        }

        /// <summary>
        /// Handler for validation errors of MQTT broker certificate.
        /// </summary>
        private void CertificateValidator_CertificateValidation(
            CertificateValidator sender,
            CertificateValidationEventArgs e)
        {
            try
            {
                if ((
                        (e.Error.StatusCode == StatusCodes.BadCertificateRevocationUnknown) ||
                        (e.Error.StatusCode == StatusCodes.BadCertificateIssuerRevocationUnknown) ||
                        (e.Error.StatusCode == StatusCodes.BadCertificateRevoked) ||
                        (e.Error.StatusCode == StatusCodes.BadCertificateIssuerRevoked)
                    ) &&
                    (m_mqttClientTlsOptions?.IgnoreCertificateRevocationErrors ?? false))
                {
                    // Accept broker certificate with revocation errors.
                    e.Accept = true;
                }
                else if ((e.Error.StatusCode == StatusCodes.BadCertificateChainIncomplete) &&
                    (m_mqttClientTlsOptions?.IgnoreCertificateChainErrors ?? false))
                {
                    // Accept broker certificate with chain errors.
                    e.Accept = true;
                }
                else if ((e.Error.StatusCode == StatusCodes.BadCertificateUntrusted) &&
                    (m_mqttClientTlsOptions?.AllowUntrustedCertificates ?? false))
                {
                    // Accept untrusted broker certificate.
                    e.Accept = true;
                }
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "MqttPubSubConnection.CertificateValidation error.");
            }
        }

        /// <summary>
        /// Base abstract class for MessageCreator
        /// </summary>
        private abstract class MessageCreator
        {
            protected MqttPubSubConnection MqttConnection { get; }
            protected ILogger Logger { get; }

            /// <summary>
            /// Create new instance of <see cref="MessageCreator"/>
            /// </summary>
            protected MessageCreator(MqttPubSubConnection mqttConnection, ILogger logger)
            {
                MqttConnection = mqttConnection;
                Logger = logger;
            }

            /// <summary>
            /// Create and return a new instance of the right <see cref="UaNetworkMessage"/> implementation.
            /// </summary>
            public abstract UaNetworkMessage CreateNewNetworkMessage();

            /// <summary>
            /// Create the list of network messages to be published by the publisher
            /// </summary>
            public abstract IList<UaNetworkMessage> CreateNetworkMessages(
                WriterGroupDataType writerGroupConfiguration,
                WriterGroupPublishState state);

            /// <summary>
            /// Create and return the Json DataSetMetaData message for a DataSetWriter
            /// </summary>
            public abstract UaNetworkMessage CreateDataSetMetaDataNetworkMessage(
                WriterGroupDataType writerGroup,
                ushort dataSetWriterId,
                DataSetMetaDataType dataSetMetaData);
        }

        /// <summary>
        /// The Json implementation for the Message creator
        /// </summary>
        private class JsonMessageCreator : MessageCreator
        {
            /// <summary>
            /// Create new instance of <see cref="JsonMessageCreator"/>
            /// </summary>
            public JsonMessageCreator(MqttPubSubConnection mqttConnection, ITelemetryContext telemetry)
                : base(mqttConnection, telemetry.CreateLogger<JsonMessageCreator>())
            {
            }

            /// <summary>
            /// Create and return a new instance of the right <see cref="JsonNetworkMessage"/>.
            /// </summary>
            public override UaNetworkMessage CreateNewNetworkMessage()
            {
                return new Encoding.JsonNetworkMessage(Logger);
            }

            /// <summary>
            /// The Json implementation of CreateNetworkMessages for MQTT mqttConnection
            /// </summary>
            public override IList<UaNetworkMessage> CreateNetworkMessages(
                WriterGroupDataType writerGroupConfiguration,
                WriterGroupPublishState state)
            {
                if (ExtensionObject.ToEncodeable(writerGroupConfiguration.MessageSettings)
                    is not JsonWriterGroupMessageDataType jsonMessageSettings)
                {
                    //Wrong configuration of writer group MessageSettings
                    return null;
                }

                //Create list of dataSet messages to be sent
                var jsonDataSetMessages = new List<Encoding.JsonDataSetMessage>();
                var networkMessages = new List<UaNetworkMessage>();

                foreach (DataSetWriterDataType dataSetWriter in writerGroupConfiguration
                    .DataSetWriters)
                {
                    //check if dataSetWriter enabled
                    if (dataSetWriter.Enabled)
                    {
                        DataSet dataSet = MqttConnection.CreateDataSet(dataSetWriter, state);

                        if (dataSet != null)
                        {
                            // check if the MetaData version is changed and issue a MetaData message
                            bool hasMetaDataChanged = state.HasMetaDataChanged(
                                dataSetWriter,
                                dataSet.DataSetMetaData);

                            if (hasMetaDataChanged)
                            {
                                networkMessages.Add(
                                    CreateDataSetMetaDataNetworkMessage(
                                        writerGroupConfiguration,
                                        dataSetWriter.DataSetWriterId,
                                        dataSet.DataSetMetaData));
                            }

                            if (ExtensionObject.ToEncodeable(dataSetWriter.MessageSettings)
                                is JsonDataSetWriterMessageDataType jsonDataSetMessageSettings)
                            {
                                var jsonDataSetMessage = new Encoding.JsonDataSetMessage(dataSet, Logger)
                                {
                                    DataSetMessageContentMask = (JsonDataSetMessageContentMask)
                                        jsonDataSetMessageSettings.DataSetMessageContentMask
                                };

                                // set common properties of dataset message
                                jsonDataSetMessage.SetFieldContentMask(
                                    (DataSetFieldContentMask)dataSetWriter.DataSetFieldContentMask);
                                jsonDataSetMessage.DataSetWriterId = dataSetWriter.DataSetWriterId;
                                jsonDataSetMessage.SequenceNumber = dataSet.SequenceNumber;

                                jsonDataSetMessage.MetaDataVersion = dataSet.DataSetMetaData
                                    .ConfigurationVersion;
                                jsonDataSetMessage.Timestamp = DateTime.UtcNow;
                                jsonDataSetMessage.Status = StatusCodes.Good;

                                jsonDataSetMessages.Add(jsonDataSetMessage);

                                state.OnMessagePublished(dataSetWriter, dataSet);
                            }
                        }
                    }
                }

                //send existing network messages if no dataset message was created
                if (jsonDataSetMessages.Count == 0)
                {
                    return networkMessages;
                }

                // each entry of this list will generate a network message
                var dataSetMessagesList = new List<List<Encoding.JsonDataSetMessage>>();
                if (((int)jsonMessageSettings.NetworkMessageContentMask &
                    (int)JsonNetworkMessageContentMask.SingleDataSetMessage) != 0)
                {
                    // create a new network message for each dataset
                    foreach (Encoding.JsonDataSetMessage dataSetMessage in jsonDataSetMessages)
                    {
                        dataSetMessagesList.Add([dataSetMessage]);
                    }
                }
                else
                {
                    dataSetMessagesList.Add(jsonDataSetMessages);
                }

                foreach (List<Encoding.JsonDataSetMessage> dataSetMessagesToUse in dataSetMessagesList)
                {
                    var jsonNetworkMessage = new Encoding.JsonNetworkMessage(
                        writerGroupConfiguration,
                        dataSetMessagesToUse,
                        Logger);
                    jsonNetworkMessage.SetNetworkMessageContentMask(
                        (JsonNetworkMessageContentMask)jsonMessageSettings?
                            .NetworkMessageContentMask);

                    // Network message header
                    jsonNetworkMessage.PublisherId =
                        MqttConnection.PubSubConnectionConfiguration.PublisherId.Value.ToString();
                    jsonNetworkMessage.WriterGroupId = writerGroupConfiguration.WriterGroupId;

                    if (((int)jsonNetworkMessage.NetworkMessageContentMask &
                        (int)JsonNetworkMessageContentMask.SingleDataSetMessage) != 0)
                    {
                        jsonNetworkMessage.DataSetClassId = dataSetMessagesToUse[0]
                            .DataSet?.DataSetMetaData?.DataSetClassId.ToString();
                    }

                    networkMessages.Add(jsonNetworkMessage);
                }

                return networkMessages;
            }

            /// <summary>
            /// Create and return the Json DataSetMetaData message for a DataSetWriter
            /// </summary>
            public override UaNetworkMessage CreateDataSetMetaDataNetworkMessage(
                WriterGroupDataType writerGroup,
                ushort dataSetWriterId,
                DataSetMetaDataType dataSetMetaData)
            {
                // return UADP metadata network message
                return new Encoding.JsonNetworkMessage(writerGroup, dataSetMetaData, Logger)
                {
                    PublisherId = MqttConnection.PubSubConnectionConfiguration.PublisherId.Value
                        .ToString(),
                    DataSetWriterId = dataSetWriterId
                };
            }
        }

        /// <summary>
        /// The Uadp implementation for the Message creator
        /// </summary>
        private class UadpMessageCreator : MessageCreator
        {
            /// <summary>
            /// Create new instance of <see cref="UadpMessageCreator"/>
            /// </summary>
            public UadpMessageCreator(MqttPubSubConnection mqttConnection, ITelemetryContext telemetry)
                : base(mqttConnection, telemetry.CreateLogger<UadpMessageCreator>())
            {
            }

            /// <summary>
            /// Create and return a new instance of the right <see cref="UadpNetworkMessage"/>.
            /// </summary>
            public override UaNetworkMessage CreateNewNetworkMessage()
            {
                return new UadpNetworkMessage(Logger);
            }

            /// <summary>
            /// The Uadp implementation of CreateNetworkMessages for MQTT mqttConnection
            /// </summary>
            public override IList<UaNetworkMessage> CreateNetworkMessages(
                WriterGroupDataType writerGroupConfiguration,
                WriterGroupPublishState state)
            {
                if (ExtensionObject.ToEncodeable(writerGroupConfiguration.MessageSettings)
                    is not UadpWriterGroupMessageDataType uadpMessageSettings)
                {
                    //Wrong configuration of writer group MessageSettings
                    return null;
                }

                //Create list of dataSet messages to be sent
                var uadpDataSetMessages = new List<UadpDataSetMessage>();
                var networkMessages = new List<UaNetworkMessage>();

                foreach (DataSetWriterDataType dataSetWriter in writerGroupConfiguration
                    .DataSetWriters)
                {
                    //check if dataSetWriter enabled
                    if (dataSetWriter.Enabled)
                    {
                        DataSet dataSet = MqttConnection.CreateDataSet(dataSetWriter, state);

                        if (dataSet != null)
                        {
                            // check if the MetaData version is changed and issue a MetaData message
                            bool hasMetaDataChanged = state.HasMetaDataChanged(
                                dataSetWriter,
                                dataSet.DataSetMetaData);

                            if (hasMetaDataChanged)
                            {
                                networkMessages.Add(
                                    CreateDataSetMetaDataNetworkMessage(
                                        writerGroupConfiguration,
                                        dataSetWriter.DataSetWriterId,
                                        dataSet.DataSetMetaData));
                            }

                            // try to create Uadp message
                            // check MessageSettings to see how to encode DataSet
                            if (ExtensionObject.ToEncodeable(dataSetWriter.MessageSettings)
                                is UadpDataSetWriterMessageDataType uadpDataSetMessageSettings)
                            {
                                var uadpDataSetMessage = new UadpDataSetMessage(dataSet, Logger);
                                uadpDataSetMessage.SetMessageContentMask(
                                    (UadpDataSetMessageContentMask)uadpDataSetMessageSettings
                                        .DataSetMessageContentMask);
                                uadpDataSetMessage.ConfiguredSize = uadpDataSetMessageSettings
                                    .ConfiguredSize;
                                uadpDataSetMessage.DataSetOffset = uadpDataSetMessageSettings
                                    .DataSetOffset;

                                // set common properties of dataset message
                                uadpDataSetMessage.SetFieldContentMask(
                                    (DataSetFieldContentMask)dataSetWriter.DataSetFieldContentMask);
                                uadpDataSetMessage.DataSetWriterId = dataSetWriter.DataSetWriterId;
                                uadpDataSetMessage.SequenceNumber = dataSet.SequenceNumber;

                                uadpDataSetMessage.MetaDataVersion = dataSet.DataSetMetaData
                                    .ConfigurationVersion;

                                uadpDataSetMessage.Timestamp = DateTime.UtcNow;
                                uadpDataSetMessage.Status = StatusCodes.Good;

                                uadpDataSetMessages.Add(uadpDataSetMessage);

                                state.OnMessagePublished(dataSetWriter, dataSet);
                            }
                        }
                    }
                }

                //send existing network messages if no dataset message was created
                if (uadpDataSetMessages.Count == 0)
                {
                    return networkMessages;
                }

                var uadpNetworkMessage = new UadpNetworkMessage(
                    writerGroupConfiguration,
                    uadpDataSetMessages,
                    Logger);
                uadpNetworkMessage.SetNetworkMessageContentMask(
                    (UadpNetworkMessageContentMask)uadpMessageSettings?.NetworkMessageContentMask);

                // Network message header
                uadpNetworkMessage.PublisherId = MqttConnection.PubSubConnectionConfiguration
                    .PublisherId
                    .Value;
                uadpNetworkMessage.WriterGroupId = writerGroupConfiguration.WriterGroupId;

                // Writer group header
                uadpNetworkMessage.GroupVersion = uadpMessageSettings.GroupVersion;
                uadpNetworkMessage.NetworkMessageNumber = 1; //only one network message per publish

                networkMessages.Add(uadpNetworkMessage);

                return networkMessages;
            }

            /// <summary>
            /// Create and return the Uadp DataSetMetaData message for a DataSetWriter
            /// </summary>
            public override UaNetworkMessage CreateDataSetMetaDataNetworkMessage(
                WriterGroupDataType writerGroup,
                ushort dataSetWriterId,
                DataSetMetaDataType dataSetMetaData)
            {
                // return UADP metadata network message
                return new UadpNetworkMessage(writerGroup, dataSetMetaData, Logger)
                {
                    PublisherId = MqttConnection.PubSubConnectionConfiguration.PublisherId.Value,
                    DataSetWriterId = dataSetWriterId
                };
            }
        }
    }
}
