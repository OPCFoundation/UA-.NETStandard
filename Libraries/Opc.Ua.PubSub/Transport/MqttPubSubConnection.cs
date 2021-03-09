/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using MQTTnet.Client.Receiving;
using MQTTnet.Protocol;
using MQTTnet.Server;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Mqtt;
using static Opc.Ua.Utils;
using MQTTnet.Formatter;

namespace Opc.Ua.PubSub.Transport
{
    /// <summary>
    /// MQTT implementation of <see cref="UaPubSubConnection"/> class.
    /// </summary>
    internal class MqttPubSubConnection : UaPubSubConnection
    {
        private static int m_sequenceNumber = 0;
        private static int m_dataSetSequenceNumber = 0;

        private string m_brokerHostName = "localhost";
        private int m_brokerPort = 1883;
        private int m_reconnectIntervalSeconds = 5;

        private IMqttClient m_publisherMqttClient;
        private IMqttClient m_subscriberMqttClient;
        private MessageMapping m_messageMapping;

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
        }

        #endregion


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
                        if (m_messageMapping == MessageMapping.Uadp && uadpMessageSettings != null)
                        {
                            // try to create Uadp message
                            UadpDataSetWriterMessageDataType uadpDataSetMessageSettings =
                                ExtensionObject.ToEncodeable(dataSetWriter.MessageSettings) as UadpDataSetWriterMessageDataType;
                            // check MessageSettings to see how to encode DataSet
                            if (uadpDataSetMessageSettings != null)
                            {
                                UadpDataSetMessage uadpDataSetMessage = new UadpDataSetMessage(dataSet);
                                uadpDataSetMessage.DataSetWriterId = dataSetWriter.DataSetWriterId;
                                uadpDataSetMessage.SetMessageContentMask((UadpDataSetMessageContentMask)uadpDataSetMessageSettings.DataSetMessageContentMask);
                                uadpDataSetMessage.SetFieldContentMask((DataSetFieldContentMask)dataSetWriter.DataSetFieldContentMask);
                                uadpDataSetMessage.SequenceNumber = (ushort)(Utils.IncrementIdentifier(ref m_dataSetSequenceNumber) % UInt16.MaxValue);
                                uadpDataSetMessage.ConfiguredSize = uadpDataSetMessageSettings.ConfiguredSize;
                                uadpDataSetMessage.DataSetOffset = uadpDataSetMessageSettings.DataSetOffset;
                                uadpDataSetMessage.TimeStamp = DateTime.UtcNow;
                                uadpDataSetMessage.Status = (ushort)StatusCodes.Good;
                                dataSetMessages.Add(uadpDataSetMessage);
                            }
                        }
                        else if (m_messageMapping == MessageMapping.Json && jsonMessageSettings != null)
                        {
                            JsonDataSetWriterMessageDataType jsonDataSetMessageSettings =
                                 ExtensionObject.ToEncodeable(dataSetWriter.MessageSettings) as JsonDataSetWriterMessageDataType;
                            if (jsonDataSetMessageSettings != null)
                            {
                                JsonDataSetMessage jsonDataSetMessage = new JsonDataSetMessage(dataSet);
                                jsonDataSetMessage.DataSetWriterId = dataSetWriter.DataSetWriterId;
                                jsonDataSetMessage.SetMessageContentMask((JsonDataSetMessageContentMask)jsonDataSetMessageSettings.DataSetMessageContentMask);
                                jsonDataSetMessage.SetFieldContentMask((DataSetFieldContentMask)dataSetWriter.DataSetFieldContentMask);
                                jsonDataSetMessage.SequenceNumber = (ushort)(Utils.IncrementIdentifier(ref m_dataSetSequenceNumber) % UInt16.MaxValue);

                                if (publishedDataSet.DataSetMetaData != null)
                                {
                                    jsonDataSetMessage.MetaDataVersion = publishedDataSet.DataSetMetaData.ConfigurationVersion;
                                }

                                jsonDataSetMessage.Timestamp = DateTime.UtcNow;
                                jsonDataSetMessage.Status = StatusCodes.BadUnexpectedError;
                                dataSetMessages.Add(jsonDataSetMessage);
                            }
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
                uadpNetworkMessage.SetNetworkMessageContentMask((UadpNetworkMessageContentMask)uadpMessageSettings.NetworkMessageContentMask);
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
                jsonNetworkMessage.SetNetworkMessageContentMask((JsonNetworkMessageContentMask)jsonMessageSettings.NetworkMessageContentMask);
                jsonNetworkMessage.MessageId = Guid.NewGuid().ToString();
                // Network message header
                jsonNetworkMessage.PublisherId = PubSubConnectionConfiguration.PublisherId.Value.ToString();

                // Writer group header
                //jsonNetworkMessage.NetworkMessageNumber = 1; //only one network message per publish

                networkMessage = jsonNetworkMessage;
            }

            if (networkMessage != null)
            {
                networkMessage.WriterGroupId = writerGroupConfiguration.WriterGroupId;
                networkMessage.SequenceNumber = (ushort)(Utils.IncrementIdentifier(ref m_sequenceNumber) % UInt16.MaxValue);
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
                                //TODO Wrong configuration of writer group MessageSettings, log error
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
            const string MqttUrlIdentifier = "mqtt://";
            const string MqttSUrlIdentifier = "mqtts://";


            IMqttClientOptions mqttOptions = null;
            TimeSpan mqttKeepAlive = TimeSpan.FromSeconds(GetWriterGroupsMaxKeepAlive() + MaxKeepAliveIncrement);

            NetworkAddressUrlDataType networkAddressUrlState = ExtensionObject.ToEncodeable(PubSubConnectionConfiguration.Address)
                       as NetworkAddressUrlDataType;
            if (networkAddressUrlState == null)
            {
                Utils.Trace(Utils.TraceMasks.Error, "The configuration for connection {0} has invalid Address configuration.",
                    this.PubSubConnectionConfiguration.Name);
                return null;
            }

            string networkAddressUrl = networkAddressUrlState.Url.ToLower();

            if (!networkAddressUrl.StartsWith(MqttUrlIdentifier) &&
                !networkAddressUrl.StartsWith(MqttSUrlIdentifier))
            {
                Utils.Trace(Utils.TraceMasks.Error, "The configuration for connection {0} has an invalid Url value {1}. The Url should start either with {2} or with {3}",
                    this.PubSubConnectionConfiguration.Name,
                    networkAddressUrlState.Url,
                    MqttUrlIdentifier,
                    MqttSUrlIdentifier);
                return null;
            }

            ITransportProtocolConfiguration transportProtocolConfiguration = ExtensionObject.ToEncodeable(PubSubConnectionConfiguration.TransportSettings)
                       as ITransportProtocolConfiguration;
            if (transportProtocolConfiguration == null)
            {
                Utils.Trace(Utils.TraceMasks.Error, "The configuration for connection {0} has invalid TransportSettings configuration will use a default one.",
                      this.PubSubConnectionConfiguration.Name);
                mqttOptions = new MqttClientOptionsBuilder()
                                .WithTcpServer(m_brokerHostName, m_brokerPort)
                                .WithKeepAlivePeriod(mqttKeepAlive)
                                .Build();
                return mqttOptions;
            }
            else if (transportProtocolConfiguration != null)
            {
                MqttClientProtocolConfiguration mqttProtocolConfiguration = transportProtocolConfiguration as MqttClientProtocolConfiguration;
                if (mqttProtocolConfiguration != null)
                {
                    
                    MqttProtocolVersion mqttProtocolVersion = (MqttProtocolVersion)((MqttClientProtocolConfiguration)transportProtocolConfiguration).ProtocolVersion;
                    if (networkAddressUrl.StartsWith(MqttSUrlIdentifier) &&
                        ((MqttClientProtocolConfiguration)transportProtocolConfiguration).UseCredentials)
                    {
                        mqttOptions = new MqttClientOptionsBuilder()
                            .WithTcpServer(m_brokerHostName, m_brokerPort)
                            .WithCredentials(new System.Net.NetworkCredential(string.Empty, mqttProtocolConfiguration.UserName).Password,
                                             new System.Net.NetworkCredential(string.Empty, mqttProtocolConfiguration.Password).Password)
                            .WithKeepAlivePeriod(mqttKeepAlive)
                            .WithProtocolVersion(mqttProtocolVersion)
                            .WithTls()
                            .Build();
                    }
                    else if (!networkAddressUrl.StartsWith(MqttSUrlIdentifier) &&
                             ((MqttClientProtocolConfiguration)transportProtocolConfiguration).UseCredentials)
                    {
                        mqttOptions = new MqttClientOptionsBuilder()
                            .WithTcpServer(m_brokerHostName, m_brokerPort)
                            .WithCredentials(new System.Net.NetworkCredential(string.Empty, mqttProtocolConfiguration.UserName).Password,
                                             new System.Net.NetworkCredential(string.Empty, mqttProtocolConfiguration.Password).Password)
                            .WithKeepAlivePeriod(mqttKeepAlive)
                            .WithProtocolVersion(mqttProtocolVersion)
                            .Build();
                    }
                    else if (networkAddressUrl.StartsWith(MqttSUrlIdentifier) &&
                             !((MqttClientProtocolConfiguration)transportProtocolConfiguration).UseCredentials)
                    {
                        mqttOptions = new MqttClientOptionsBuilder()
                            .WithTcpServer(m_brokerHostName, m_brokerPort)
                            .WithKeepAlivePeriod(mqttKeepAlive)
                            .WithProtocolVersion(mqttProtocolVersion)
                            .WithTls()
                            .Build();
                    }
                    //if (!networkAddressUrl.StartsWith(MqttSUriIdentifier) &&
                    //        !((MqttClientProtocolConfiguration)Application.TransportProtocolConfiguration).UseCredentials)
                    else
                    {
                        mqttOptions = new MqttClientOptionsBuilder()
                            .WithTcpServer(m_brokerHostName, m_brokerPort)
                            .WithKeepAlivePeriod(mqttKeepAlive)
                            .WithProtocolVersion(mqttProtocolVersion)
                            .Build();
                    }

                }
            }

            return mqttOptions;

        }

        protected override bool InternalInitialize()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Perform specific Start tasks
        /// </summary>
        protected override void InternalStart()
        {
            int nrOfPublishers = 0;
            int nrOfSubscribers = 0;

            lock (m_lock)
            {
                //cleanup all existing UdpClient previously open
                InternalStop();

                NetworkAddressUrlDataType networkAddressUrlState = ExtensionObject.ToEncodeable(PubSubConnectionConfiguration.Address)
                       as NetworkAddressUrlDataType;
                if (networkAddressUrlState == null)
                {
                    Utils.Trace(Utils.TraceMasks.Error, "The configuration for connection {0} has invalid Address configuration.",
                              this.PubSubConnectionConfiguration.Name);
                    return;
                }

                Uri connectionUri;
                if (networkAddressUrlState.Url != null && Uri.TryCreate(networkAddressUrlState.Url, UriKind.Absolute, out connectionUri))
                {
                    if (connectionUri.Scheme == Utils.UriSchemeMqtt)
                    {
                        m_brokerHostName = connectionUri.Host;
                        m_brokerPort = connectionUri.Port;
                    }
                }

                nrOfPublishers = Publishers.Count;
                nrOfSubscribers = GetAllDataSetReaders().Count;
            }

            MqttClient pubClient = null;
            MqttClient subClient = null;
            IMqttClientOptions mqttOptions = GetMqttClientOptions();

            //publisher initialization    
            if (nrOfPublishers > 0)
            {
                pubClient = Task.Run(async () =>
                          (MqttClient)await MqttClientCreator.GetMqttClientAsync(m_reconnectIntervalSeconds,
                                                                                 mqttOptions,
                                                                                 null).ConfigureAwait(false)).Result;
            }

            //subscriber initialization   
            if (nrOfSubscribers > 0)
            {
                subClient = Task.Run(async () =>
                     (MqttClient)await MqttClientCreator.GetMqttClientAsync(m_reconnectIntervalSeconds,
                                                                            mqttOptions,
                                                                            ProcessMqttMessage).ConfigureAwait(false)).Result;          
            }

            lock (m_lock)
            {
                m_publisherMqttClient = pubClient;
                m_subscriberMqttClient = subClient;
            }

        }

        #region Private methods


        void ProcessMqttMessage(MqttApplicationMessageReceivedEventArgs eventArgs)
        {
            string topic = eventArgs.ApplicationMessage.Topic;
            Utils.Trace(Utils.TraceMasks.Information, "MqttPubSubConnection.ProcessReceivedMessage from topic={0}", topic);

            // get the datasetreaders for received message topic
            List<DataSetReaderDataType> dataSetReaders = new List<DataSetReaderDataType>();
            foreach(DataSetReaderDataType dsReader in GetOperationalDataSetReaders())
            {
                if (dsReader == null) continue;
                BrokerDataSetReaderTransportDataType brokerDataSetReaderTransportDataType =
                    ExtensionObject.ToEncodeable(dsReader.TransportSettings) as BrokerDataSetReaderTransportDataType;
                if (brokerDataSetReaderTransportDataType == null || brokerDataSetReaderTransportDataType.QueueName != topic)
                {
                    continue;
                }
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

                    // Raise rthe DataReceived event 
                    RaiseNetworkMessageDataReceivedEvent(networkMessage, topic);
                }                
            }
            else
            {
                Utils.Trace(Utils.TraceMasks.Information, "No DataSetReader is registered for topic={0}.", topic);
            }
        }

        #endregion Private methods
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
                        Task.Run(async () => await m_publisherMqttClient.DisconnectAsync());
                    }
                    m_publisherMqttClient.Dispose();
                }

                if (m_subscriberMqttClient != null)
                {
                    if (m_subscriberMqttClient.IsConnected)
                    {
                        Task.Run(async () => await m_subscriberMqttClient.DisconnectAsync());
                    }
                    m_subscriberMqttClient.Dispose();
                }
            }
        }
    }
}
