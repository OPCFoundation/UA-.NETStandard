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
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;

namespace Opc.Ua.PubSub.Transport
{
    /// <summary>
    /// MQTT implementation of <see cref="UaPubSubConnection"/> class.
    /// </summary>
    internal class MqttPubSubConnection : UaPubSubConnection, IMqttPubSubConnection
    {
        #region Private Fields
        private string m_brokerHostName = "localhost";
        private string m_urlScheme;
        private int m_brokerPort = Utils.MqttDefaultPort;
        private int m_reconnectIntervalSeconds = 5;

        private IMqttClient m_publisherMqttClient;
        private IMqttClient m_subscriberMqttClient;
        private readonly MessageMapping m_messageMapping;
        private readonly MessageCreator m_messageCreator;

        private CertificateValidator m_certificateValidator;
        private MqttClientTlsOptions m_mqttClientTlsOptions;

        private readonly List<MqttMetadataPublisher> m_metaDataPublishers = new List<MqttMetadataPublisher>();
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the host name or IP address of the broker.
        /// </summary>
        public string BrokerHostName { get => m_brokerHostName; }

        /// <summary>
        /// Gets the port of the mqttConnection.
        /// </summary>
        public int BrokerPort { get { return m_brokerPort; } }

        /// <summary>
        /// Gets the scheme of the Url.
        /// </summary>
        public string UrlScheme { get => m_urlScheme; }
        #endregion Public Properties

        #region Constants
        /// <summary>
        /// Value in seconds with which to surpass the max keep alive value found.
        /// </summary>
        private readonly int MaxKeepAliveIncrement = 5;
        #endregion

        #region Constructor
        /// <summary>
        ///  Create new instance of <see cref="MqttPubSubConnection"/> from <see cref="PubSubConnectionDataType"/> configuration data
        /// </summary>
        /// <param name="uaPubSubApplication"></param>
        /// <param name="pubSubConnectionDataType"></param>
        /// <param name="messageMapping"></param>
        public MqttPubSubConnection(UaPubSubApplication uaPubSubApplication, PubSubConnectionDataType pubSubConnectionDataType, MessageMapping messageMapping)
            : base(uaPubSubApplication, pubSubConnectionDataType)
        {
            m_transportProtocol = TransportProtocol.MQTT;
            m_messageMapping = messageMapping;

            // initialize the message creators for current message 
            if (m_messageMapping == MessageMapping.Json)
            {
                m_messageCreator = new JsonMessageCreator(this);
            }
            else if (m_messageMapping == MessageMapping.Uadp)
            {
                m_messageCreator = new UadpMessageCreator(this);
            }
            else
            {
                Utils.Trace(Utils.TraceMasks.Error, "The current MessageMapping {0} does not have a valid message creator", m_messageMapping);
            }
            Utils.Trace("MqttPubSubConnection with name '{0}' was created.", pubSubConnectionDataType.Name);
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// Determine if the connection can publish metadata for specified writer group and data set writer
        /// </summary>
        public bool CanPublishMetaData(WriterGroupDataType writerGroupConfiguration,
            DataSetWriterDataType dataSetWriter)
        {
            if (!CanPublish(writerGroupConfiguration)) return false;

            if (Application.UaPubSubConfigurator.FindStateForObject(dataSetWriter) != PubSubState.Operational)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Create the list of network messages built from the provided writerGroupConfiguration
        /// </summary>
        public override IList<UaNetworkMessage> CreateNetworkMessages(WriterGroupDataType writerGroupConfiguration, WriterGroupPublishState state)
        {
            BrokerWriterGroupTransportDataType transportSettings =
                ExtensionObject.ToEncodeable(writerGroupConfiguration.TransportSettings)
                    as BrokerWriterGroupTransportDataType;

            if (transportSettings == null)
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
        /// <returns></returns>
        public UaNetworkMessage CreateDataSetMetaDataNetworkMessage(WriterGroupDataType writerGroup, DataSetWriterDataType dataSetWriter)
        {
            PublishedDataSetDataType publishedDataSet = Application.DataCollector.GetPublishedDataSet(dataSetWriter.DataSetName);
            if (publishedDataSet != null && publishedDataSet.DataSetMetaData != null)
            {
                if (m_messageCreator != null)
                {
                    return m_messageCreator.CreateDataSetMetaDataNetworkMessage(writerGroup,
                        dataSetWriter.DataSetWriterId, publishedDataSet.DataSetMetaData);
                }
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
                lock (m_lock)
                {
                    if (m_publisherMqttClient != null && m_publisherMqttClient.IsConnected)
                    {
                        // get the encoded bytes
                        byte[] bytes = networkMessage.Encode(MessageContext);

                        try
                        {
                            string queueName = null;
                            BrokerTransportQualityOfService qos = BrokerTransportQualityOfService.AtLeastOnce;

                            // the network messages that have DataSetWriterId are either metaData messages or SingleDataSet messages and 
                            if (networkMessage.DataSetWriterId != null)
                            {
                                var dataSetWriter = networkMessage.WriterGroupConfiguration.DataSetWriters
                                    .Find(x => x.DataSetWriterId == networkMessage.DataSetWriterId);

                                if (dataSetWriter != null)
                                { 
                                    var transportSettings = ExtensionObject
                                        .ToEncodeable(dataSetWriter.TransportSettings)
                                            as BrokerDataSetWriterTransportDataType;

                                    if (transportSettings != null)
                                    {
                                        qos = transportSettings.RequestedDeliveryGuarantee;

                                        queueName = networkMessage.IsMetaDataMessage ? transportSettings.MetaDataQueueName : transportSettings.QueueName;
                                    }
                                }
                            }

                            if (queueName == null || qos == BrokerTransportQualityOfService.NotSpecified)
                            {
                                var transportSettings = ExtensionObject.ToEncodeable(
                                    networkMessage.WriterGroupConfiguration.TransportSettings)
                                        as BrokerWriterGroupTransportDataType;

                                if (transportSettings != null)
                                {
                                    if (queueName == null)
                                    {
                                        queueName = transportSettings.QueueName;
                                    }
                                    // if the value is not specified and the value of the parent object shall be used
                                    if (qos == BrokerTransportQualityOfService.NotSpecified)
                                    {
                                        qos = transportSettings.RequestedDeliveryGuarantee;
                                    }
                                }
                            }

                            if (!String.IsNullOrEmpty(queueName))
                            {
                                var message = new MqttApplicationMessage {
                                    Topic = queueName,
                                    Payload = bytes,
                                    QualityOfServiceLevel = GetMqttQualityOfServiceLevel(qos),
                                    Retain = networkMessage.IsMetaDataMessage
                                };

                                m_publisherMqttClient.PublishAsync(message).GetAwaiter().GetResult();
                            }
                        }
                        catch (Exception ex)
                        {
                            Utils.Trace(ex, "MqttPubSubConnection.PublishNetworkMessage");
                            return false;
                        }

                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "MqttPubSubConnection.PublishNetworkMessage");
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
            return (m_publisherMqttClient == null || m_publisherMqttClient.IsConnected)
                && (m_subscriberMqttClient == null || m_subscriberMqttClient.IsConnected);
        }
        #endregion Public Methods

        #region Protected Methods
        /// <summary>
        /// Perform specific Start tasks
        /// </summary>
        protected override async Task InternalStart()
        {
            //cleanup all existing MQTT connections previously open
            await InternalStop().ConfigureAwait(false); 

            lock (m_lock)
            {
                NetworkAddressUrlDataType networkAddressUrlState = ExtensionObject.ToEncodeable(
                    PubSubConnectionConfiguration.Address) as NetworkAddressUrlDataType;

                if (networkAddressUrlState == null)
                {
                    Utils.Trace(
                        Utils.TraceMasks.Error,
                        "The configuration for mqttConnection {0} has invalid Address configuration.",
                        PubSubConnectionConfiguration.Name);

                    return;
                }

                Uri connectionUri;
                m_urlScheme = null;

                if (networkAddressUrlState.Url != null && Uri.TryCreate(networkAddressUrlState.Url, UriKind.Absolute, out connectionUri))
                {
                    if ((connectionUri.Scheme == Utils.UriSchemeMqtt) || (connectionUri.Scheme == Utils.UriSchemeMqtts))
                    {
                        if (!String.IsNullOrEmpty(connectionUri.Host))
                        {
                            m_brokerHostName = connectionUri.Host;
                            m_brokerPort = (connectionUri.Port > 0) ? connectionUri.Port : ((connectionUri.Scheme == Utils.UriSchemeMqtt) ? 1883 : 8883);
                            m_urlScheme = connectionUri.Scheme;
                        }
                    }
                }

                if (m_urlScheme == null)
                {
                    Utils.Trace(
                        Utils.TraceMasks.Error,
                        "The configuration for mqttConnection {0} has invalid MQTT URL '{1}'.",
                        PubSubConnectionConfiguration.Name,
                        networkAddressUrlState.Url);

                    return;
                }

                // create the DataSetMetaData publishers
                foreach (WriterGroupDataType writerGroup in PubSubConnectionConfiguration.WriterGroups)
                {
                    foreach (DataSetWriterDataType dataSetWriter in writerGroup.DataSetWriters)
                    {
                        if (dataSetWriter.DataSetWriterId == 0) continue;

                        BrokerDataSetWriterTransportDataType transport = ExtensionObject.ToEncodeable(dataSetWriter.TransportSettings)
                            as BrokerDataSetWriterTransportDataType;

                        if (transport == null || transport.MetaDataUpdateTime == 0) continue;

                        m_metaDataPublishers.Add(new MqttMetadataPublisher(this, writerGroup, dataSetWriter, transport.MetaDataUpdateTime));
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
            IMqttClientOptions mqttOptions = GetMqttClientOptions();

            int nrOfPublishers = Publishers.Count;
            int nrOfSubscribers = GetAllDataSetReaders().Count;

            //publisher initialization
            if (nrOfPublishers > 0)
            {
                publisherClient = (MqttClient)await MqttClientCreator.GetMqttClientAsync(
                    m_reconnectIntervalSeconds,
                    mqttOptions,
                    null).ConfigureAwait(false);
            }

            //subscriber initialization
            if (nrOfSubscribers > 0)
            {
                // collect all topics from all ReaderGroups
                StringCollection topics = new StringCollection();
                foreach (var readerGroup in PubSubConnectionConfiguration.ReaderGroups)
                {
                    if (!readerGroup.Enabled)
                    {
                        continue;
                    }

                    foreach (var dataSetReader in readerGroup.DataSetReaders)
                    {
                        if (!dataSetReader.Enabled)
                        {
                            continue;
                        }

                        BrokerDataSetReaderTransportDataType brokerTransportSettings =
                            ExtensionObject.ToEncodeable(dataSetReader.TransportSettings)
                                as BrokerDataSetReaderTransportDataType;

                        if (brokerTransportSettings != null && !topics.Contains(brokerTransportSettings.QueueName))
                        {
                            topics.Add(brokerTransportSettings.QueueName);

                            if (brokerTransportSettings.MetaDataQueueName != null)
                            {
                                topics.Add(brokerTransportSettings.MetaDataQueueName);
                            }
                        }
                    }
                }

                subscriberClient = (MqttClient)await MqttClientCreator.GetMqttClientAsync(
                    m_reconnectIntervalSeconds,
                    mqttOptions,
                    ProcessMqttMessage,
                    topics).ConfigureAwait(false);
            }

            lock (m_lock)
            {
                m_publisherMqttClient = publisherClient;
                m_subscriberMqttClient = subscriberClient;
            }

            Utils.Trace("Connection '{0}' started {1} publishers and {2} subscribers.",
                PubSubConnectionConfiguration.Name, nrOfPublishers, nrOfSubscribers);
        }

        /// <summary>
        /// Perform specific Stop tasks
        /// </summary>
        protected override async Task InternalStop()
        {
            var publisherMqttClient = m_publisherMqttClient;
            var subscriberMqttClient = m_subscriberMqttClient;

            if (publisherMqttClient != null)
            {
                if (publisherMqttClient.IsConnected)
                {
                    await publisherMqttClient.DisconnectAsync().ContinueWith((e) => publisherMqttClient.Dispose()).ConfigureAwait(false);
                }
                else
                {
                    publisherMqttClient.Dispose();
                }
            }

            if (subscriberMqttClient != null)
            {
                if (subscriberMqttClient.IsConnected)
                {
                    await subscriberMqttClient.DisconnectAsync().ContinueWith((e) => subscriberMqttClient.Dispose()).ConfigureAwait(false);
                }
                else
                {
                    subscriberMqttClient.Dispose();
                }
            }

            if (m_metaDataPublishers != null)
            {
                foreach (MqttMetadataPublisher metaDataPublisher in m_metaDataPublishers)
                {
                    metaDataPublisher.Stop();
                }
                m_metaDataPublishers.Clear();
            }

            lock (m_lock)
            {
                m_publisherMqttClient = null;
                m_subscriberMqttClient = null;
                m_mqttClientTlsOptions = null;
            }
        }
        #endregion Protected Methods

        #region Private Methods

        private bool MatchTopic(string pattern, string topic)
        {
            if (String.IsNullOrEmpty(pattern) || pattern == "#")
            {
                return true;
            }

            var fields1 = pattern.Split('/');
            var fields2 = topic.Split('/');

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
        /// <param name="eventArgs"></param>
        private void ProcessMqttMessage(MqttApplicationMessageReceivedEventArgs eventArgs)
        {
            string topic = eventArgs.ApplicationMessage.Topic;

            Utils.Trace("MQTTConnection - ProcessMqttMessage() received from topic={0}", topic);

            // get the datasetreaders for received message topic
            List<DataSetReaderDataType> dataSetReaders = new List<DataSetReaderDataType>();
            foreach (DataSetReaderDataType dsReader in GetOperationalDataSetReaders())
            {
                if (dsReader == null) continue;

                BrokerDataSetReaderTransportDataType brokerDataSetReaderTransportDataType =
                    ExtensionObject.ToEncodeable(dsReader.TransportSettings)
                       as BrokerDataSetReaderTransportDataType;

                string queueName = brokerDataSetReaderTransportDataType.QueueName;
                string metadataQueueName = brokerDataSetReaderTransportDataType.MetaDataQueueName;

                if (!MatchTopic(queueName, topic))
                {
                    if (String.IsNullOrEmpty(metadataQueueName))
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
                RawDataReceivedEventArgs rawDataReceivedEventArgs = new RawDataReceivedEventArgs() {
                    Message = eventArgs.ApplicationMessage.Payload,
                    Source = topic,
                    TransportProtocol = this.TransportProtocol,
                    MessageMapping = m_messageMapping,
                    PubSubConnectionConfiguration = PubSubConnectionConfiguration
                };

                // trigger notification for received raw data
                Application.RaiseRawDataReceivedEvent(rawDataReceivedEventArgs);

                // check if the RawData message is marked as handled
                if (rawDataReceivedEventArgs.Handled)
                {
                    Utils.Trace("MqttConnection message from topic={0} is marked as handled and will not be decoded.", topic);
                    return;
                }

                // initialize the expected NetworkMessage
                UaNetworkMessage networkMessage = m_messageCreator.CreateNewNetworkMessage();
                
                // trigger message decoding
                if (networkMessage != null)
                {
                    networkMessage.Decode(MessageContext, eventArgs.ApplicationMessage.Payload, dataSetReaders);

                    // Handle the decoded message and raise the necessary event on UaPubSubApplication 
                    ProcessDecodedNetworkMessage(networkMessage, topic);
                }
            }
            else
            {
                Utils.Trace("MqttConnection - ProcessMqttMessage() No DataSetReader is registered for topic={0}.", topic);
            }
        }

        /// <summary>
        /// Transform pub sub setting into MqttNet enum
        /// </summary>
        /// <param name="brokerTransportQualityOfService"></param>
        /// <returns></returns>
        private MqttQualityOfServiceLevel GetMqttQualityOfServiceLevel(BrokerTransportQualityOfService brokerTransportQualityOfService)
        {
            switch (brokerTransportQualityOfService)
            {
                case BrokerTransportQualityOfService.AtLeastOnce:
                    return MqttQualityOfServiceLevel.AtLeastOnce;
                case BrokerTransportQualityOfService.AtMostOnce:
                    return MqttQualityOfServiceLevel.AtMostOnce;
                case BrokerTransportQualityOfService.ExactlyOnce:
                    return MqttQualityOfServiceLevel.ExactlyOnce;
                default:
                    return MqttQualityOfServiceLevel.AtLeastOnce;
            }
        }

        /// <summary>
        /// Get appropriate IMqttClientOptions with which to connect to the MQTTBroker
        /// </summary>
        /// <returns></returns>
        private IMqttClientOptions GetMqttClientOptions()
        {
            IMqttClientOptions mqttOptions = null;
            TimeSpan mqttKeepAlive = TimeSpan.FromSeconds(GetWriterGroupsMaxKeepAlive() + MaxKeepAliveIncrement);

            NetworkAddressUrlDataType networkAddressUrlState =
                ExtensionObject.ToEncodeable(PubSubConnectionConfiguration.Address)
                    as NetworkAddressUrlDataType;
            if (networkAddressUrlState == null)
            {
                Utils.Trace(Utils.TraceMasks.Error,
                    "The configuration for mqttConnection {0} has invalid Address configuration.",
                    PubSubConnectionConfiguration.Name);
                return null;
            }

            Uri connectionUri = null;

            if (networkAddressUrlState.Url != null &&
                Uri.TryCreate(networkAddressUrlState.Url, UriKind.Absolute, out connectionUri))
            {
                if ((connectionUri.Scheme != Utils.UriSchemeMqtt) && (connectionUri.Scheme != Utils.UriSchemeMqtts))
                {
                    Utils.Trace(Utils.TraceMasks.Error,
                        "The configuration for mqttConnection '{0}' has an invalid Url value {1}. The Uri scheme should be either {2}:// or {3}://",
                        PubSubConnectionConfiguration.Name,
                        networkAddressUrlState.Url,
                        Utils.UriSchemeMqtt,
                        Utils.UriSchemeMqtts);
                    return null;
                }
            }

            if (connectionUri == null)
            {
                Utils.Trace(Utils.TraceMasks.Error,
                    "The configuration for mqttConnection '{0}' has an invalid Url value {1}.",
                    PubSubConnectionConfiguration.Name,
                    networkAddressUrlState.Url);
                return null;
            }

            ITransportProtocolConfiguration transportProtocolConfiguration =
                new MqttClientProtocolConfiguration(PubSubConnectionConfiguration.ConnectionProperties);


            MqttClientProtocolConfiguration mqttProtocolConfiguration =
                transportProtocolConfiguration as MqttClientProtocolConfiguration;
            if (mqttProtocolConfiguration != null)
            {
                MqttProtocolVersion mqttProtocolVersion =
                    (MqttProtocolVersion)((MqttClientProtocolConfiguration)transportProtocolConfiguration)
                    .ProtocolVersion;
                // create uniques client id
                string clientId = "ClientId_" + new Random().Next().ToString("D10");
                // MQTTS mqttConnection.
                if (connectionUri.Scheme == Utils.UriSchemeMqtts)
                {
                    MqttTlsOptions mqttTlsOptions =
                        ((MqttClientProtocolConfiguration)transportProtocolConfiguration).MqttTlsOptions;

                    MqttClientOptionsBuilder mqttClientOptionsBuilder = new MqttClientOptionsBuilder()
                        .WithTcpServer(m_brokerHostName, m_brokerPort)
                        .WithKeepAlivePeriod(mqttKeepAlive)
                        .WithProtocolVersion(mqttProtocolVersion)
                        .WithClientId(clientId)
                        .WithTls(new MqttClientOptionsBuilderTlsParameters {
                            UseTls = true,
                            Certificates = mqttTlsOptions?.Certificates?.X509Certificates,
                            SslProtocol =
                                mqttTlsOptions?.SslProtocolVersion ??
                                System.Security.Authentication.SslProtocols.Tls12,
                            AllowUntrustedCertificates = mqttTlsOptions?.AllowUntrustedCertificates ?? false,
                            IgnoreCertificateChainErrors = mqttTlsOptions?.IgnoreCertificateChainErrors ?? false,
                            IgnoreCertificateRevocationErrors = mqttTlsOptions?.IgnoreRevocationListErrors ?? false,
                            CertificateValidationHandler = ValidateBrokerCertificate
                        });

                    // Set user credentials.
                    if (mqttProtocolConfiguration.UseCredentials)
                    {
                        mqttClientOptionsBuilder.WithCredentials(
                            new System.Net.NetworkCredential(string.Empty, mqttProtocolConfiguration.UserName)
                                .Password,
                            new System.Net.NetworkCredential(string.Empty, mqttProtocolConfiguration.Password)
                                .Password);

                        // Set ClientId for Azure.
                        if (mqttProtocolConfiguration.UseAzureClientId)
                        {
                            mqttClientOptionsBuilder.WithClientId(mqttProtocolConfiguration.AzureClientId);
                        }
                    }

                    mqttOptions = mqttClientOptionsBuilder.Build();

                    // Create the certificate validator for broker certificates.
                    m_certificateValidator = CreateCertificateValidator(mqttTlsOptions);
                    m_certificateValidator.CertificateValidation += CertificateValidator_CertificateValidation;
                    m_mqttClientTlsOptions = mqttOptions?.ChannelOptions?.TlsOptions;
                }
                // MQTT mqttConnection
                else if (connectionUri.Scheme == Utils.UriSchemeMqtt)
                {
                    MqttClientOptionsBuilder mqttClientOptionsBuilder = new MqttClientOptionsBuilder()
                        .WithTcpServer(m_brokerHostName, m_brokerPort)
                        .WithKeepAlivePeriod(mqttKeepAlive)
                        .WithClientId(clientId)
                        .WithProtocolVersion(mqttProtocolVersion);

                    // Set user credentials.
                    if (mqttProtocolConfiguration.UseCredentials)
                    {
                        mqttClientOptionsBuilder.WithCredentials(
                            new System.Net.NetworkCredential(string.Empty, mqttProtocolConfiguration.UserName)
                                .Password,
                            new System.Net.NetworkCredential(string.Empty, mqttProtocolConfiguration.Password)
                                .Password);
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
        /// <returns>A new instance of stack validator <see cref="CertificateValidator"/></returns>
        private CertificateValidator CreateCertificateValidator(MqttTlsOptions mqttTlsOptions)
        {
            CertificateValidator certificateValidator = new CertificateValidator();

            SecurityConfiguration securityConfiguration = new SecurityConfiguration();
            securityConfiguration.TrustedIssuerCertificates = (CertificateTrustList)mqttTlsOptions.TrustedIssuerCertificates;
            securityConfiguration.TrustedPeerCertificates = (CertificateTrustList)mqttTlsOptions.TrustedPeerCertificates;
            securityConfiguration.RejectedCertificateStore = mqttTlsOptions.RejectedCertificateStore;

            securityConfiguration.RejectSHA1SignedCertificates = true;
            securityConfiguration.AutoAcceptUntrustedCertificates = mqttTlsOptions.AllowUntrustedCertificates;
            securityConfiguration.RejectUnknownRevocationStatus = !mqttTlsOptions.IgnoreRevocationListErrors;

            certificateValidator.Update(securityConfiguration).Wait();

            return certificateValidator;
        }

        /// <summary>
        /// Validates the broker certificate.
        /// </summary>
        /// <param name="context">The context of the validation</param>
        private bool ValidateBrokerCertificate(MqttClientCertificateValidationCallbackContext context)
        {
            X509Certificate2 brokerCertificate = new X509Certificate2(context.Certificate.GetRawCertData());

            try
            {
                // check if the broker certificate validation has been overridden.
                if (Application?.OnValidateBrokerCertificate != null)
                {
                    return Application.OnValidateBrokerCertificate(brokerCertificate);
                }
                else
                {
                    m_certificateValidator?.Validate(brokerCertificate);
                }
            }
            catch (Exception ex)
            {
                Utils.Trace(ex,"Connection '{0}' - Broker certificate '{1}' rejected.",
                    PubSubConnectionConfiguration.Name, brokerCertificate.Subject);
                return false;
            }

            Utils.Trace(Utils.TraceMasks.Security, "Connection '{0}' - Broker certificate '{1}'  accepted.",
                PubSubConnectionConfiguration.Name, brokerCertificate.Subject);
            return true;
        }

        /// <summary>
        /// Handler for validation errors of MQTT broker certificate.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CertificateValidator_CertificateValidation(CertificateValidator sender, CertificateValidationEventArgs e)
        {
            try
            {
                if (((e.Error.StatusCode == StatusCodes.BadCertificateRevocationUnknown) ||
                     (e.Error.StatusCode == StatusCodes.BadCertificateIssuerRevocationUnknown) ||
                     (e.Error.StatusCode == StatusCodes.BadCertificateRevoked) ||
                     (e.Error.StatusCode == StatusCodes.BadCertificateIssuerRevoked)) &&
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
                Utils.Trace(ex, "MqttPubSubConnection.CertificateValidation error.");
            }
        }
        
        #endregion Private methods

        #region MessageCreator innner classes
        /// <summary>
        /// Base abstract class for MessageCreator
        /// </summary>
        private abstract class MessageCreator
        {
            protected readonly MqttPubSubConnection m_mqttConnection;

            /// <summary>
            /// Create new instance of <see cref="MessageCreator"/>
            /// </summary>
            protected MessageCreator(MqttPubSubConnection mqttConnection)
            {
                m_mqttConnection = mqttConnection;
            }

            /// <summary>
            /// Create and return a new instance of the right <see cref="UaNetworkMessage"/> implementation.
            /// </summary>
            /// <returns></returns>
            public abstract UaNetworkMessage CreateNewNetworkMessage();

            /// <summary>
            /// Create the list of network messages to be published by the publisher
            /// </summary>
            public abstract IList<UaNetworkMessage> CreateNetworkMessages(WriterGroupDataType writerGroupConfiguration,
                WriterGroupPublishState state);

            /// <summary> 
            /// Create and return the Json DataSetMetaData message for a DataSetWriter
            /// </summary>
            public abstract UaNetworkMessage CreateDataSetMetaDataNetworkMessage(WriterGroupDataType writerGroup, UInt16 dataSetWriterId,
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
            public JsonMessageCreator(MqttPubSubConnection mqttConnection) : base(mqttConnection)
            {
            }

            /// <summary>
            /// Create and return a new instance of the right <see cref="JsonNetworkMessage"/>.
            /// </summary>
            public override UaNetworkMessage CreateNewNetworkMessage()
            {
                return new JsonNetworkMessage();
            }

            /// <summary>
            /// The Json implementation of CreateNetworkMessages for MQTT mqttConnection
            /// </summary>
            public override IList<UaNetworkMessage> CreateNetworkMessages(WriterGroupDataType writerGroupConfiguration,
                WriterGroupPublishState state)
            {
                JsonWriterGroupMessageDataType jsonMessageSettings = ExtensionObject.ToEncodeable(
                        writerGroupConfiguration.MessageSettings)
                    as JsonWriterGroupMessageDataType;
                if (jsonMessageSettings == null)
                {
                    //Wrong configuration of writer group MessageSettings
                    return null;
                }

                //Create list of dataSet messages to be sent
                List<JsonDataSetMessage> jsonDataSetMessages = new List<JsonDataSetMessage>();
                List<UaNetworkMessage> networkMessages = new List<UaNetworkMessage>();

                foreach (DataSetWriterDataType dataSetWriter in writerGroupConfiguration.DataSetWriters)
                {
                    //check if dataSetWriter enabled
                    if (dataSetWriter.Enabled)
                    {
                        DataSet dataSet = m_mqttConnection.CreateDataSet(dataSetWriter, state);

                        if (dataSet != null)
                        {
                            // check if the MetaData version is changed and issue a MetaData message
                            bool hasMetaDataChanged = state.HasMetaDataChanged(dataSetWriter, dataSet.DataSetMetaData);

                            if (hasMetaDataChanged)
                            {
                                networkMessages.Add(CreateDataSetMetaDataNetworkMessage(writerGroupConfiguration, dataSetWriter.DataSetWriterId, dataSet.DataSetMetaData));
                            }
                           
                            JsonDataSetWriterMessageDataType jsonDataSetMessageSettings =
                                ExtensionObject.ToEncodeable(dataSetWriter.MessageSettings) as JsonDataSetWriterMessageDataType;
                            if (jsonDataSetMessageSettings != null)
                            {
                                JsonDataSetMessage jsonDataSetMessage = new JsonDataSetMessage(dataSet);
                                jsonDataSetMessage.DataSetMessageContentMask = (JsonDataSetMessageContentMask)jsonDataSetMessageSettings.DataSetMessageContentMask;

                                // set common properties of dataset message
                                jsonDataSetMessage.SetFieldContentMask((DataSetFieldContentMask)dataSetWriter.DataSetFieldContentMask);
                                jsonDataSetMessage.DataSetWriterId = dataSetWriter.DataSetWriterId;
                                jsonDataSetMessage.SequenceNumber = dataSet.SequenceNumber;

                                jsonDataSetMessage.MetaDataVersion = dataSet.DataSetMetaData.ConfigurationVersion;
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
                List<List<JsonDataSetMessage>> dataSetMessagesList = new List<List<JsonDataSetMessage>>();
                if ((((JsonNetworkMessageContentMask)jsonMessageSettings.NetworkMessageContentMask) & JsonNetworkMessageContentMask.SingleDataSetMessage) != 0)
                {
                    // create a new network message for each dataset
                    foreach (JsonDataSetMessage dataSetMessage in jsonDataSetMessages)
                    {
                        dataSetMessagesList.Add(new List<JsonDataSetMessage>() { dataSetMessage });
                    }
                }
                else
                {
                    dataSetMessagesList.Add(jsonDataSetMessages);
                }

                foreach (List<JsonDataSetMessage> dataSetMessagesToUse in dataSetMessagesList)
                {
                    JsonNetworkMessage jsonNetworkMessage = new JsonNetworkMessage(writerGroupConfiguration, dataSetMessagesToUse);
                    jsonNetworkMessage.SetNetworkMessageContentMask((JsonNetworkMessageContentMask)jsonMessageSettings?.NetworkMessageContentMask);

                    // Network message header
                    jsonNetworkMessage.PublisherId = m_mqttConnection.PubSubConnectionConfiguration.PublisherId.Value.ToString();
                    jsonNetworkMessage.WriterGroupId = writerGroupConfiguration.WriterGroupId;

                    if ((jsonNetworkMessage.NetworkMessageContentMask & JsonNetworkMessageContentMask.SingleDataSetMessage) != 0)
                    {
                        jsonNetworkMessage.DataSetClassId = dataSetMessagesToUse[0].DataSet?.DataSetMetaData?.DataSetClassId.ToString();
                    }

                    networkMessages.Add(jsonNetworkMessage);
                }

                return networkMessages;
            }

            /// <summary> 
            /// Create and return the Json DataSetMetaData message for a DataSetWriter
            /// </summary>
            public override UaNetworkMessage CreateDataSetMetaDataNetworkMessage(WriterGroupDataType writerGroup, UInt16 dataSetWriterId, DataSetMetaDataType dataSetMetaData)
            {
                // return UADP metadata network message
                return new JsonNetworkMessage(writerGroup, dataSetMetaData) {
                    PublisherId = m_mqttConnection.PubSubConnectionConfiguration.PublisherId.Value.ToString(),
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
            public UadpMessageCreator(MqttPubSubConnection mqttConnection) : base(mqttConnection)
            {

            }

            /// <summary>
            /// Create and return a new instance of the right <see cref="UadpNetworkMessage"/>.
            /// </summary>
            public override UaNetworkMessage CreateNewNetworkMessage()
            {
                return new UadpNetworkMessage();
            }

            /// <summary>
            /// The Uadp implementation of CreateNetworkMessages for MQTT mqttConnection
            /// </summary>
            public override IList<UaNetworkMessage> CreateNetworkMessages(WriterGroupDataType writerGroupConfiguration,
                WriterGroupPublishState state)
            {
                UadpWriterGroupMessageDataType uadpMessageSettings = ExtensionObject.ToEncodeable(
                        writerGroupConfiguration.MessageSettings)
                    as UadpWriterGroupMessageDataType;

                if (uadpMessageSettings == null)
                {
                    //Wrong configuration of writer group MessageSettings
                    return null;
                }

                //Create list of dataSet messages to be sent
                List<UadpDataSetMessage> uadpDataSetMessages = new List<UadpDataSetMessage>();
                List<UaNetworkMessage> networkMessages = new List<UaNetworkMessage>();

                foreach (DataSetWriterDataType dataSetWriter in writerGroupConfiguration.DataSetWriters)
                {
                    //check if dataSetWriter enabled
                    if (dataSetWriter.Enabled)
                    {
                        DataSet dataSet = m_mqttConnection.CreateDataSet(dataSetWriter, state);

                        if (dataSet != null)
                        {
                            // check if the MetaData version is changed and issue a MetaData message
                            bool hasMetaDataChanged = state.HasMetaDataChanged(dataSetWriter, dataSet.DataSetMetaData);

                            if (hasMetaDataChanged)
                            {
                                networkMessages.Add(CreateDataSetMetaDataNetworkMessage(writerGroupConfiguration, dataSetWriter.DataSetWriterId, dataSet.DataSetMetaData));
                            }

                            // try to create Uadp message
                            UadpDataSetWriterMessageDataType uadpDataSetMessageSettings =
                                ExtensionObject.ToEncodeable(dataSetWriter.MessageSettings) as
                                    UadpDataSetWriterMessageDataType;
                            // check MessageSettings to see how to encode DataSet
                            if (uadpDataSetMessageSettings != null)
                            {
                                UadpDataSetMessage uadpDataSetMessage = new UadpDataSetMessage(dataSet);
                                uadpDataSetMessage.SetMessageContentMask((UadpDataSetMessageContentMask)uadpDataSetMessageSettings.DataSetMessageContentMask);
                                uadpDataSetMessage.ConfiguredSize = uadpDataSetMessageSettings.ConfiguredSize;
                                uadpDataSetMessage.DataSetOffset = uadpDataSetMessageSettings.DataSetOffset;

                                // set common properties of dataset message
                                uadpDataSetMessage.SetFieldContentMask((DataSetFieldContentMask)dataSetWriter.DataSetFieldContentMask);
                                uadpDataSetMessage.DataSetWriterId = dataSetWriter.DataSetWriterId;
                                uadpDataSetMessage.SequenceNumber = dataSet.SequenceNumber;

                                uadpDataSetMessage.MetaDataVersion = dataSet.DataSetMetaData.ConfigurationVersion;

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

                UadpNetworkMessage uadpNetworkMessage =
                    new UadpNetworkMessage(writerGroupConfiguration, uadpDataSetMessages);
                uadpNetworkMessage.SetNetworkMessageContentMask(
                    (UadpNetworkMessageContentMask)uadpMessageSettings?.NetworkMessageContentMask);

                // Network message header
                uadpNetworkMessage.PublisherId = m_mqttConnection.PubSubConnectionConfiguration.PublisherId.Value;
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
            public override UaNetworkMessage CreateDataSetMetaDataNetworkMessage(WriterGroupDataType writerGroup, UInt16 dataSetWriterId, DataSetMetaDataType dataSetMetaData)
            {
                // return UADP metadata network message
                return new UadpNetworkMessage(writerGroup, dataSetMetaData) {
                    PublisherId = m_mqttConnection.PubSubConnectionConfiguration.PublisherId.Value,
                    DataSetWriterId = dataSetWriterId
                };
            }
        }
        #endregion
    }
}
