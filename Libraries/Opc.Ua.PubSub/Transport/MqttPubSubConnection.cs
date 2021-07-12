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
        private string m_applicationId;
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
            m_applicationId = uaPubSubApplication.ApplicationId;

            Utils.Trace("MqttPubSubConnection with name '{0}' was created.", pubSubConnectionDataType.Name);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Create the list of network messages built from the provided writerGroupConfiguration
        /// </summary>
        public override IList<UaNetworkMessage> CreateNetworkMessages(WriterGroupDataType writerGroupConfiguration, WriterGroupPublishState state)
        {
            UadpWriterGroupMessageDataType uadpMessageSettings = ExtensionObject.ToEncodeable(
                writerGroupConfiguration.MessageSettings)
                    as UadpWriterGroupMessageDataType;

            JsonWriterGroupMessageDataType jsonMessageSettings = ExtensionObject.ToEncodeable(
                writerGroupConfiguration.MessageSettings)
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

            BrokerWriterGroupTransportDataType transportSettings =
                ExtensionObject.ToEncodeable(writerGroupConfiguration.TransportSettings)
                    as BrokerWriterGroupTransportDataType;

            if (transportSettings == null)
            {
                //Wrong configuration of writer group MessageSettings
                return null;
            }

            //Create list of dataSet messages to be sent
            List<UadpDataSetMessage> uadpDataSetMessages = new List<UadpDataSetMessage>();
            List<JsonDataSetMessage> jsonDataSetMessages = new List<JsonDataSetMessage>();
            List<UaNetworkMessage> networkMessages = new List<UaNetworkMessage>();

            foreach (DataSetWriterDataType dataSetWriter in writerGroupConfiguration.DataSetWriters)
            {
                //check if dataSetWriter enabled
                if (dataSetWriter.Enabled)
                {
                    uint sequenceNumber = 0;
                    bool isDeltaFrame = state.IsDeltaFrame(dataSetWriter, out sequenceNumber);
                    PublishedDataSetDataType publishedDataSet = Application.DataCollector.GetPublishedDataSet(dataSetWriter.DataSetName);
                    DataSet dataSet = Application.DataCollector.CollectData(dataSetWriter.DataSetName, isDeltaFrame);
                    dataSet.SequenceNumber = sequenceNumber;

                    BrokerDataSetWriterTransportDataType transport = ExtensionObject.ToEncodeable(dataSetWriter.TransportSettings) as BrokerDataSetWriterTransportDataType;

                    if (publishedDataSet != null && dataSet != null)
                    {
                        if (isDeltaFrame)
                        {
                            dataSet = state.ExcludeUnchangedFields(dataSetWriter, dataSet);

                            if (dataSet == null)
                            {
                                continue;
                            }
                        }

                        bool hasMetaDataChanged = state.HasMetaDataChanged(dataSetWriter, dataSet.DataSetMetaData, transport?.MetaDataUpdateTime ?? 0);

                        if (hasMetaDataChanged)
                        {
                            if (m_messageMapping == MessageMapping.Uadp)
                            {
                                // add UADP metadata network message
                                networkMessages.Add(new UadpNetworkMessage(writerGroupConfiguration, dataSet.DataSetMetaData) {
                                    PublisherId = PubSubConnectionConfiguration.PublisherId.Value,
                                    DataSetWriterId = dataSetWriter.DataSetWriterId
                                });
                            }
                            else if (m_messageMapping == MessageMapping.Json)
                            {
                                // add JSON metadata network message
                                networkMessages.Add(new JsonNetworkMessage(writerGroupConfiguration, dataSet.DataSetMetaData) {
                                    PublisherId = PubSubConnectionConfiguration.PublisherId.ToString(),
                                    DataSetWriterId = dataSetWriter.DataSetWriterId
                                });
                            }
                        }

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

                                uadpDataSetMessages.Add(uadpDataSetMessage);
                            }
                        }
                        else if (m_messageMapping == MessageMapping.Json && jsonMessageSettings != null)
                        {
                            JsonDataSetWriterMessageDataType jsonDataSetMessageSettings =
                                 ExtensionObject.ToEncodeable(dataSetWriter.MessageSettings) as JsonDataSetWriterMessageDataType;
                            if (jsonDataSetMessageSettings != null)
                            {
                                JsonDataSetMessage jsonDataSetMessage = new JsonDataSetMessage(dataSet);
                                jsonDataSetMessage.DataSetMessageContentMask = (JsonDataSetMessageContentMask)jsonDataSetMessageSettings.DataSetMessageContentMask;
                                jsonDataSetMessage.SetFieldContentMask((DataSetFieldContentMask)dataSetWriter.DataSetFieldContentMask);
                                uaDataSetMessage = jsonDataSetMessage;

                                jsonDataSetMessages.Add(jsonDataSetMessage);
                            }
                        }

                        if (uaDataSetMessage != null)
                        {
                            // set common properties of dataset message
                            uaDataSetMessage.DataSetWriterId = dataSetWriter.DataSetWriterId;
                            uaDataSetMessage.SequenceNumber = dataSet.SequenceNumber;

                            state.MessagePublished(dataSetWriter, dataSet);

                            if (publishedDataSet.DataSetMetaData != null)
                            {
                                uaDataSetMessage.MetaDataVersion = publishedDataSet.DataSetMetaData.ConfigurationVersion;
                            }
                            uaDataSetMessage.Timestamp = DateTime.UtcNow;
                            uaDataSetMessage.Status = StatusCodes.Good;
                        }
                    }
                }
            }

            if (m_messageMapping == MessageMapping.Uadp)
            {
                // cancel send if no dataset message
                if (uadpDataSetMessages.Count == 0)
                {
                    return networkMessages;
                }

                UadpNetworkMessage uadpNetworkMessage = new UadpNetworkMessage(writerGroupConfiguration, uadpDataSetMessages);
                uadpNetworkMessage.SetNetworkMessageContentMask((UadpNetworkMessageContentMask)uadpMessageSettings?.NetworkMessageContentMask);

                // Network message header
                uadpNetworkMessage.PublisherId = PubSubConnectionConfiguration.PublisherId.Value;
                uadpNetworkMessage.WriterGroupId = writerGroupConfiguration.WriterGroupId;

                // Writer group header
                uadpNetworkMessage.GroupVersion = uadpMessageSettings.GroupVersion;
                uadpNetworkMessage.NetworkMessageNumber = 1; //only one network message per publish

                networkMessages.Add(uadpNetworkMessage);

                return networkMessages;
            }
            else if (m_messageMapping == MessageMapping.Json)
            {
                //cancel send if no dataset message
                if (jsonDataSetMessages.Count == 0)
                {
                    return networkMessages;
                }

                // each entry of this list will generate a network message
                List<List<JsonDataSetMessage>> dataSetMessagesList = new List<List<JsonDataSetMessage>>();
                if ((((JsonNetworkMessageContentMask)jsonMessageSettings?.NetworkMessageContentMask) & JsonNetworkMessageContentMask.SingleDataSetMessage) != 0)
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
                    jsonNetworkMessage.PublisherId = PubSubConnectionConfiguration.PublisherId.Value.ToString();
                    jsonNetworkMessage.WriterGroupId = writerGroupConfiguration.WriterGroupId;

                    if ((jsonNetworkMessage.NetworkMessageContentMask & JsonNetworkMessageContentMask.SingleDataSetMessage) != 0)
                    {
                        jsonNetworkMessage.DataSetClassId = dataSetMessagesToUse[0].DataSet?.DataSetMetaData?.DataSetClassId.ToString();

                        // set the DataSetWriterId for single dataset network messages - TODO investigate if this is reaLLY NECESSARY???
                        jsonNetworkMessage.DataSetWriterId = dataSetMessagesToUse[0].DataSetWriterId;
                    }

                    networkMessages.Add(jsonNetworkMessage);
                }

                return networkMessages;
            }

            // no other encoding is implemented
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
                        byte[] bytes = networkMessage.Encode();

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

                                        if (networkMessage.IsMetaDataMessage)
                                        {
                                            queueName = transportSettings.MetaDataQueueName;
                                        }
                                        else
                                        {
                                            queueName = transportSettings.QueueName;
                                        }
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
        #endregion Public Methods

        #region Protected Methods
        /// <summary>
        /// Perform specific Start tasks
        /// </summary>
        protected override async Task InternalStart()
        {
            int nrOfPublishers = 0;
            int nrOfSubscribers = 0;

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
                        "The configuration for connection {0} has invalid Address configuration.",
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
                        "The configuration for connection {0} has invalid MQTT URL '{1}'.",
                        PubSubConnectionConfiguration.Name,
                        networkAddressUrlState.Url);

                    return;
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
            Utils.Trace("Connection '{0}' - ProcessMqttMessage() received from topic={0}", topic);

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
                // initialize the expected NetworkMessage
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
                    networkMessage.Decode(m_context, eventArgs.ApplicationMessage.Payload, dataSetReaders);

                    // Hanndle the decoded message and raise the necessary event on UaPubSubApplication 
                    ProcessDecodedNetworkMessage(networkMessage, topic);
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
                            .WithClientId(m_applicationId)
                            .WithTls(new MqttClientOptionsBuilderTlsParameters {
                                UseTls = true,
                                Certificates = mqttTlsOptions?.Certificates?.X509Certificates,
                                SslProtocol = mqttTlsOptions?.SslProtocolVersion ?? System.Security.Authentication.SslProtocols.Tls12,
                                AllowUntrustedCertificates = mqttTlsOptions?.AllowUntrustedCertificates ?? false,
                                IgnoreCertificateChainErrors = mqttTlsOptions?.IgnoreCertificateChainErrors ?? false,
                                IgnoreCertificateRevocationErrors = mqttTlsOptions?.IgnoreRevocationListErrors ?? false,
                                CertificateValidationHandler = ValidateBrokerCertificate
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
                            .WithClientId(m_applicationId)
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
    }
}
