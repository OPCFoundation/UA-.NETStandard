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
using MQTTnet.Server;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;

namespace Opc.Ua.PubSub.Transport
{
    /// <summary>
    /// MQTT implementation of <see cref="UaPubSubConnection"/> class.
    /// </summary>
    internal class MqttPubSubConnection : UaPubSubConnection
    {
        #region Private Fields
        private static int m_dataSetSequenceNumber = 0;

        private string m_brokerHostName = "localhost";
        private string m_urlScheme;
        private int m_brokerPort = Utils.MqttDefaultPort;
        private int m_reconnectIntervalSeconds = 5;

        private IMqttClient m_publisherMqttClient;
        private IMqttClient m_subscriberMqttClient;
        private MessageMapping m_messageMapping;

        private CertificateValidator m_certificateValidator;
        private MqttClientTlsOptions m_mqttClientTlsOptions;
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the host name or IP address of the broker.
        /// </summary>
        public string BrokerHostName { get => m_brokerHostName; }

        /// <summary>
        /// Gets the port of the connection.
        /// </summary>
        public int BrokerPort { get { return m_brokerPort; } }

        /// <summary>
        /// Gets the scheme of the Url.
        /// </summary>
        public string UrlScheme { get => m_urlScheme; }

        /// <summary>
        /// Gets or sets the certificate validator for MQTT broker certificates.
        /// </summary>
        public CertificateValidator CertificateValidator
        {
            get => m_certificateValidator;
            set => m_certificateValidator = value;
        }
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

            Utils.Trace("MqttPubSubConnection with name '{0}' was created.", pubSubConnectionDataType.Name);
        }

        #endregion

        #region Public Methods
        public override UaNetworkMessage CreateNetworkMessage(WriterGroupDataType writerGroupConfiguration)
        {
            UadpWriterGroupMessageDataType uadpMessageSettings = ExtensionObject.ToEncodeable(writerGroupConfiguration.MessageSettings)
                as UadpWriterGroupMessageDataType;

            JsonWriterGroupMessageDataType jsonMessageSettings = ExtensionObject.ToEncodeable(writerGroupConfiguration.MessageSettings)
               as JsonWriterGroupMessageDataType;
            if (m_messageMapping == MessageMapping.Uadp && uadpMessageSettings == null)
            {
                //Wrong configuration of writer group MessageSettings
                return null;
            }
            if (m_messageMapping == MessageMapping.Json && jsonMessageSettings == null)
            {
                //Wrong configuration of writer group MessageSettings
                return null;
            }
            BrokerWriterGroupTransportDataType transportSettings = ExtensionObject.ToEncodeable(writerGroupConfiguration.TransportSettings)
                as BrokerWriterGroupTransportDataType;
            if (transportSettings == null)
            {
                //Wrong configuration of writer group MessageSettings
                return null;
            }

            //Create list of dataSet messages to be sent
            List<UaDataSetMessage> dataSetMessages = new List<UaDataSetMessage>();
            foreach (DataSetWriterDataType dataSetWriter in writerGroupConfiguration.DataSetWriters)
            {
                //check if dataSetWriter enabled
                if (dataSetWriter.Enabled)
                {
                    PublishedDataSetDataType publishedDataSet = Application.DataCollector.GetPublishedDataSet(dataSetWriter.DataSetName);
                    DataSet dataSet = Application.DataCollector.CollectData(dataSetWriter.DataSetName);

                    if (publishedDataSet != null && dataSet != null)
                    {
                        UaDataSetMessage uaDataSetMessage = null;
                        if (m_messageMapping == MessageMapping.Uadp && uadpMessageSettings != null)
                        {
                            // try to create Uadp message
                            UadpDataSetWriterMessageDataType uadpDataSetMessageSettings =
                                ExtensionObject.ToEncodeable(dataSetWriter.MessageSettings) as UadpDataSetWriterMessageDataType;
                            // check MessageSettings to see how to encode DataSet
                            if (uadpDataSetMessageSettings != null)
                            {
                                UadpDataSetMessage uadpDataSetMessage = new UadpDataSetMessage(dataSet);
                                uadpDataSetMessage.SetMessageContentMask((UadpDataSetMessageContentMask)uadpDataSetMessageSettings.DataSetMessageContentMask);
                                uadpDataSetMessage.SetFieldContentMask((DataSetFieldContentMask)dataSetWriter.DataSetFieldContentMask);
                                uadpDataSetMessage.ConfiguredSize = uadpDataSetMessageSettings.ConfiguredSize;
                                uadpDataSetMessage.DataSetOffset = uadpDataSetMessageSettings.DataSetOffset;
                                uaDataSetMessage = uadpDataSetMessage;
                            }
                        }
                        else if (m_messageMapping == MessageMapping.Json && jsonMessageSettings != null)
                        {
                            JsonDataSetWriterMessageDataType jsonDataSetMessageSettings =
                                 ExtensionObject.ToEncodeable(dataSetWriter.MessageSettings) as JsonDataSetWriterMessageDataType;
                            if (jsonDataSetMessageSettings != null)
                            {
                                JsonDataSetMessage jsonDataSetMessage = new JsonDataSetMessage(dataSet);
                                jsonDataSetMessage.SetMessageContentMask((JsonDataSetMessageContentMask)jsonDataSetMessageSettings.DataSetMessageContentMask);
                                jsonDataSetMessage.SetFieldContentMask((DataSetFieldContentMask)dataSetWriter.DataSetFieldContentMask);
                                uaDataSetMessage = jsonDataSetMessage;
                            }
                        }

                        if (uaDataSetMessage != null)
                        {
                            // set common properties of dataset message
                            uaDataSetMessage.DataSetWriterId = dataSetWriter.DataSetWriterId;
                            uaDataSetMessage.SequenceNumber = (ushort)(Utils.IncrementIdentifier(ref m_dataSetSequenceNumber) % UInt16.MaxValue);
                            if (publishedDataSet.DataSetMetaData != null)
                            {
                                uaDataSetMessage.MetaDataVersion = publishedDataSet.DataSetMetaData.ConfigurationVersion;
                            }
                            uaDataSetMessage.Timestamp = DateTime.UtcNow;
                            uaDataSetMessage.Status = StatusCodes.Good;
                            dataSetMessages.Add(uaDataSetMessage);
                        }
                    }
                }
            }

            //cancel send if no dataset message
            if (dataSetMessages.Count == 0)
            {
                return null;
            }

            UaNetworkMessage networkMessage = null;

            if (m_messageMapping == MessageMapping.Uadp)
            {
                UadpNetworkMessage uadpNetworkMessage = new UadpNetworkMessage(writerGroupConfiguration, dataSetMessages);
                uadpNetworkMessage.SetNetworkMessageContentMask((UadpNetworkMessageContentMask)uadpMessageSettings?.NetworkMessageContentMask);
                // Network message header
                uadpNetworkMessage.PublisherId = PubSubConnectionConfiguration.PublisherId.Value;

                // Writer group header
                uadpNetworkMessage.GroupVersion = uadpMessageSettings.GroupVersion;
                uadpNetworkMessage.NetworkMessageNumber = 1; //only one network message per publish

                networkMessage = uadpNetworkMessage;
            }
            else if (m_messageMapping == MessageMapping.Json)
            {
                JsonNetworkMessage jsonNetworkMessage = new JsonNetworkMessage(writerGroupConfiguration, dataSetMessages);
                jsonNetworkMessage.SetNetworkMessageContentMask((JsonNetworkMessageContentMask)jsonMessageSettings?.NetworkMessageContentMask);
                jsonNetworkMessage.MessageId = Guid.NewGuid().ToString();
                // Network message header
                jsonNetworkMessage.PublisherId = PubSubConnectionConfiguration.PublisherId.Value.ToString();

                networkMessage = jsonNetworkMessage;
            }

            if (networkMessage != null)
            {
                networkMessage.WriterGroupId = writerGroupConfiguration.WriterGroupId;
            }
            return networkMessage;
        }

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
                        // get rthe encoded bytes
                        byte[] bytes = networkMessage.Encode();

                        try
                        {
                            BrokerWriterGroupTransportDataType transportSettings = ExtensionObject.ToEncodeable(networkMessage.WriterGroupConfiguration.TransportSettings)
                                as BrokerWriterGroupTransportDataType;
                            if (transportSettings == null)
                            {
                                return false;
                            }

                            var message = new MqttApplicationMessage {
                                Topic = transportSettings.QueueName,
                                Payload = bytes,
                                QualityOfServiceLevel = GetMqttQualityOfServiceLevel(transportSettings.RequestedDeliveryGuarantee)
                            };

                            m_publisherMqttClient.PublishAsync(message).GetAwaiter().GetResult();
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
        #endregion Public Methods

        #region Protected Methods
        /// <summary>
        /// Perform specific Start tasks
        /// </summary>
        protected override void InternalStart()
        {
            int nrOfPublishers = 0;
            int nrOfSubscribers = 0;

            lock (m_lock)
            {
                //cleanup all existing MQTT connections previously open
                InternalStop();

                NetworkAddressUrlDataType networkAddressUrlState = ExtensionObject.ToEncodeable(PubSubConnectionConfiguration.Address)
                       as NetworkAddressUrlDataType;
                if (networkAddressUrlState == null)
                {
                    Utils.Trace(Utils.TraceMasks.Error, "The configuration for connection {0} has invalid Address configuration.",
                              PubSubConnectionConfiguration.Name);
                    return;
                }

                Uri connectionUri;
                if (networkAddressUrlState.Url != null && Uri.TryCreate(networkAddressUrlState.Url, UriKind.Absolute, out connectionUri))
                {
                    if ((connectionUri.Scheme == Utils.UriSchemeMqtt) || (connectionUri.Scheme == Utils.UriSchemeMqtts))
                    {
                        m_brokerHostName = connectionUri.Host;
                        m_brokerPort = connectionUri.Port;
                        m_urlScheme = connectionUri.Scheme;
                    }
                }

                nrOfPublishers = Publishers.Count;
                nrOfSubscribers = GetAllDataSetReaders().Count;
            }

            MqttClient publisherClient = null;
            MqttClient subscriberClient = null;
            IMqttClientOptions mqttOptions = GetMqttClientOptions();

            //publisher initialization
            if (nrOfPublishers > 0)
            {
                publisherClient = Task.Run(async () =>
                          (MqttClient)await MqttClientCreator.GetMqttClientAsync(m_reconnectIntervalSeconds,
                                                                                 mqttOptions,
                                                                                 null).ConfigureAwait(false)).Result;
            }

            //subscriber initialization
            if (nrOfSubscribers > 0)
            {
                // collect all topics from all ReaderGroups
                StringCollection topics = new StringCollection();
                foreach (var readGroup in PubSubConnectionConfiguration.ReaderGroups)
                {
                    foreach (var dataSetReader in readGroup.DataSetReaders)
                    {
                        BrokerDataSetReaderTransportDataType brokerTransportSettings = ExtensionObject.ToEncodeable(dataSetReader.TransportSettings)
                            as BrokerDataSetReaderTransportDataType;
                        if (brokerTransportSettings != null && !topics.Contains(brokerTransportSettings.QueueName))
                        {
                            topics.Add(brokerTransportSettings.QueueName);
                        }
                    }
                }

                subscriberClient = Task.Run(async () =>
                     (MqttClient)await MqttClientCreator.GetMqttClientAsync(m_reconnectIntervalSeconds,
                                                                            mqttOptions,
                                                                            ProcessMqttMessage,
                                                                            topics).ConfigureAwait(false)).Result;
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
        protected override void InternalStop()
        {
            lock (m_lock)
            {
                if (m_publisherMqttClient != null)
                {
                    if (m_publisherMqttClient.IsConnected)
                    {
                        Task.Run(async () => await m_publisherMqttClient.DisconnectAsync().ConfigureAwait(false));
                    }
                    m_publisherMqttClient.Dispose();
                }

                if (m_subscriberMqttClient != null)
                {
                    if (m_subscriberMqttClient.IsConnected)
                    {
                        Task.Run(async () => await m_subscriberMqttClient.DisconnectAsync().ConfigureAwait(false));
                    }
                    m_subscriberMqttClient.Dispose();
                }

                if (m_certificateValidator != null)
                {
                    m_certificateValidator.CertificateValidation -= CertificateValidator_CertificateValidation;
                    m_certificateValidator = null;
                }

                m_mqttClientTlsOptions = null;
            }
        }
        #endregion Protected Methods

        #region Private Methods
        /// <summary>
        /// Processes a message from the MQTT broker.
        /// </summary>
        /// <param name="eventArgs"></param>
        private void ProcessMqttMessage(MqttApplicationMessageReceivedEventArgs eventArgs)
        {
            string topic = eventArgs.ApplicationMessage.Topic;
            Utils.Trace("Connection '{0}' - ProcessMqttMessage() received from topic={0}", topic);

            // get the datasetreaders for received message topic
            List<DataSetReaderDataType> dataSetReaders = new List<DataSetReaderDataType>();
            foreach (DataSetReaderDataType dsReader in GetOperationalDataSetReaders())
            {
                if (dsReader == null) continue;
                BrokerDataSetReaderTransportDataType brokerDataSetReaderTransportDataType =
                    ExtensionObject.ToEncodeable(dsReader.TransportSettings) as BrokerDataSetReaderTransportDataType;

                string queueName = brokerDataSetReaderTransportDataType.QueueName;
                if (queueName != "#")
                {
                    // The following block of code checks if the received topic is the expected one
                    // In case the message is received from Azure the topic is received with an appended GUID which needs to be filtered out
                    // The match is done using the following logic
                    if (!string.IsNullOrEmpty(queueName) && queueName.LastIndexOf('#') == queueName.Length - 1)
                    {
                        // Keep the queueName without #
                        queueName = queueName.Substring(0, queueName.Length - 1);
                    }
                    if (brokerDataSetReaderTransportDataType == null || !topic.StartsWith(queueName))
                    {
                        // Ignore message
                        continue;
                    }
                    if (topic.Length > queueName.Length)
                    {
                        // Keep the portion of the topic having the length of the queueName
                        string filterTopic = topic.Substring(0, queueName.Length);
                        if (filterTopic != queueName)
                        {
                            // Ignore message
                            continue;
                        }
                    }
                    else if ((topic.Length == queueName.Length) && (topic != queueName))
                    {
                        // Ignore message
                        continue;
                    }
                    else if (topic.Length < queueName.Length)
                    {
                        // Ignore message
                        continue;
                    }
                }
                // At this point the message is accepted 
                // if ((topic.Length == queueName.Length) && (topic == queueName)) || (queueName == #)
                dataSetReaders.Add(dsReader);
            }

            if (dataSetReaders.Count > 0)
            {
                // iniaialize the expected NetworkMessage
                UaNetworkMessage networkMessage = null;
                if (m_messageMapping == MessageMapping.Uadp)
                {
                    networkMessage = new UadpNetworkMessage();
                }
                else if (m_messageMapping == MessageMapping.Json)
                {
                    networkMessage = new JsonNetworkMessage();
                }
                // trigger message decoding
                if (networkMessage != null)
                {
                    networkMessage.Decode(eventArgs.ApplicationMessage.Payload, dataSetReaders);

                    // Raise the DataReceived event 
                    RaiseNetworkMessageDataReceivedEvent(networkMessage, topic);
                }
            }
            else
            {
                Utils.Trace("Connection '{0}' - ProcessMqttMessage() No DataSetReader is registered for topic={0}.", topic);
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
        /// Get apropriate IMqttClientOptions with which to connect to the MQTTBroker
        /// </summary>
        /// <returns></returns>
        private IMqttClientOptions GetMqttClientOptions()
        {
            IMqttClientOptions mqttOptions = null;
            TimeSpan mqttKeepAlive = TimeSpan.FromSeconds(GetWriterGroupsMaxKeepAlive() + MaxKeepAliveIncrement);

            NetworkAddressUrlDataType networkAddressUrlState = ExtensionObject.ToEncodeable(PubSubConnectionConfiguration.Address)
                       as NetworkAddressUrlDataType;
            if (networkAddressUrlState == null)
            {
                Utils.Trace(Utils.TraceMasks.Error, "The configuration for connection {0} has invalid Address configuration.",
                    PubSubConnectionConfiguration.Name);
                return null;
            }

            Uri connectionUri = null;

            if (networkAddressUrlState.Url != null && Uri.TryCreate(networkAddressUrlState.Url, UriKind.Absolute, out connectionUri))
            {
                if ((connectionUri.Scheme != Utils.UriSchemeMqtt) && (connectionUri.Scheme != Utils.UriSchemeMqtts))
                {
                    Utils.Trace(Utils.TraceMasks.Error,
                    "The configuration for connection '{0}' has an invalid Url value {1}. The Uri scheme should be either {2}:// or {3}://",
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
                    "The configuration for connection '{0}' has an invalid Url value {1}.",
                    PubSubConnectionConfiguration.Name,
                    networkAddressUrlState.Url);
                return null;
            }

            ITransportProtocolConfiguration transportProtocolConfiguration = new MqttClientProtocolConfiguration(PubSubConnectionConfiguration.ConnectionProperties);

            if (transportProtocolConfiguration == null)
            {
                Utils.Trace(Utils.TraceMasks.Error,
                    "The configuration for connection '{0}' has invalid TransportSettings configuration will use a default one.",
                     PubSubConnectionConfiguration.Name);
                mqttOptions = new MqttClientOptionsBuilder()
                                .WithTcpServer(m_brokerHostName, m_brokerPort)
                                .WithKeepAlivePeriod(mqttKeepAlive)
                                .Build();
                return mqttOptions;
            }
            else
            {
                MqttClientProtocolConfiguration mqttProtocolConfiguration = transportProtocolConfiguration as MqttClientProtocolConfiguration;
                if (mqttProtocolConfiguration != null)
                {
                    MqttProtocolVersion mqttProtocolVersion = (MqttProtocolVersion)((MqttClientProtocolConfiguration)transportProtocolConfiguration).ProtocolVersion;

                    // MQTTS connection.
                    if (connectionUri.Scheme == Utils.UriSchemeMqtts)
                    {
                        MqttTlsOptions mqttTlsOptions = ((MqttClientProtocolConfiguration)transportProtocolConfiguration).MqttTlsOptions;

                        MqttClientOptionsBuilder mqttClientOptionsBuilder = new MqttClientOptionsBuilder()
                            .WithTcpServer(m_brokerHostName, m_brokerPort)
                            .WithKeepAlivePeriod(mqttKeepAlive)
                            .WithProtocolVersion(mqttProtocolVersion)
                            .WithTls(new MqttClientOptionsBuilderTlsParameters {
                                UseTls = true,
                                Certificates = mqttTlsOptions?.Certificates?.X509Certificates,
                                SslProtocol = mqttTlsOptions?.SslProtocolVersion ?? System.Security.Authentication.SslProtocols.Tls12,
                                AllowUntrustedCertificates = mqttTlsOptions?.AllowUntrustedCertificates ?? false,
                                IgnoreCertificateChainErrors = mqttTlsOptions?.IgnoreCertificateChainErrors ?? false,
                                IgnoreCertificateRevocationErrors = mqttTlsOptions?.IgnoreRevocationListErrors ?? false,
                                CertificateValidationHandler = ValidateCertificate
                                    });

                        // Set user credentials.
                        if (mqttProtocolConfiguration.UseCredentials)
                        {
                            mqttClientOptionsBuilder.WithCredentials(new System.Net.NetworkCredential(string.Empty, mqttProtocolConfiguration.UserName).Password,
                                new System.Net.NetworkCredential(string.Empty, mqttProtocolConfiguration.Password).Password);

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
                    // MQTT connection
                    else if (connectionUri.Scheme == Utils.UriSchemeMqtt)
                    {
                        MqttClientOptionsBuilder mqttClientOptionsBuilder = new MqttClientOptionsBuilder()
                            .WithTcpServer(m_brokerHostName, m_brokerPort)
                            .WithKeepAlivePeriod(mqttKeepAlive)
                            .WithProtocolVersion(mqttProtocolVersion);

                        // Set user credentials.
                        if (mqttProtocolConfiguration.UseCredentials)
                        {
                            mqttClientOptionsBuilder.WithCredentials(new System.Net.NetworkCredential(string.Empty, mqttProtocolConfiguration.UserName).Password,
                                new System.Net.NetworkCredential(string.Empty, mqttProtocolConfiguration.Password).Password);
                        }

                        mqttOptions = mqttClientOptionsBuilder.Build();
                    }
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
            securityConfiguration.RejectUnknownRevocationStatus = !m_mqttClientTlsOptions.IgnoreCertificateRevocationErrors;

            certificateValidator.Update(securityConfiguration).Wait();

            return certificateValidator;
        }

        /// <summary>
        /// Validates the broker certificate.
        /// </summary>
        /// <param name="context">The context of the validation</param>
        private bool ValidateCertificate(MqttClientCertificateValidationCallbackContext context)
        {
            X509Certificate2 certificate = new X509Certificate2(context.Certificate.GetRawCertData());

            try
            {
                m_certificateValidator?.Validate(certificate);
            }
            catch (Exception ex)
            {
                Utils.Trace(ex,"Connection '{0}' - Broker certificate '{1}' rejected.",
                    PubSubConnectionConfiguration.Name, certificate.Subject);
                return false;
            }

            Utils.Trace(Utils.TraceMasks.Security, "Connection '{0}' - Broker certificate '{1}'  accepted.",
                PubSubConnectionConfiguration.Name, certificate.Subject);
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
    }
}
