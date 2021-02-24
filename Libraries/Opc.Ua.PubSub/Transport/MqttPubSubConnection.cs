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

        private IMqttClient m_publisherMqttClient;
        private MessageMapping m_messageMapping;
        #region Constructor

        /// <summary>
        ///  Create new instance of <see cref="MqttPubSubConnection"/> from <see cref="PubSubConnectionDataType"/> configuration data
        /// </summary>
        /// <param name="uaPubSubApplication"></param>
        /// <param name="pubSubConnectionDataType"></param>
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
                    DataSet dataSet = Application.DataCollector.CollectData(dataSetWriter.DataSetName);
                    if (dataSet != null)
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

                                jsonDataSetMessage.TimeStamp = DateTime.UtcNow;
                                jsonDataSetMessage.Status = (ushort)StatusCodes.Good;
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

                // Network message header
                jsonNetworkMessage.PublisherId = PubSubConnectionConfiguration.PublisherId.Value.ToString();

                // Writer group header
                jsonNetworkMessage.NetworkMessageNumber = 1; //only one network message per publish

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
                        ServiceMessageContext messageContext = new ServiceMessageContext();
                        byte[] bytes = null;
                        if (m_messageMapping == MessageMapping.Uadp)
                        {
                            BinaryEncoder encoder = new BinaryEncoder(messageContext);
                            networkMessage.Encode(encoder);
                            bytes = ReadBytes(encoder.BaseStream);
                            encoder.Dispose();
                        }
                        else if (m_messageMapping == MessageMapping.Json)
                        {
                            JsonEncoder encoderJson = new JsonEncoder(messageContext, true);
                            networkMessage.Encode(encoderJson);
                            bytes = System.Text.Encoding.ASCII.GetBytes(encoderJson.CloseAndReturnText());
                            encoderJson.Dispose();
                        }


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
        private byte[] ReadBytes(Stream stream)
        {
            stream.Position = 0;
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        protected override bool InternalInitialize()
        {
            throw new NotImplementedException();
        }

        protected override void InternalStart()
        {
            lock (m_lock)
            {
                //cleanup all existing UdpClient previously open
                //InternalStop();

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
                try
                {
                    var options = new MqttClientOptions {
                        ChannelOptions = new MqttClientTcpOptions {
                            Server = m_brokerHostName,
                            Port = m_brokerPort,
                        },
                        CleanSession = true
                    };

                    m_publisherMqttClient = new MqttFactory().CreateMqttClient();
                    m_publisherMqttClient.ConnectAsync(options).GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception);
                }





                //try
                //    {
                //        var options = new MqttServerOptions {
                //            ConnectionValidator = new MqttServerConnectionValidatorDelegate(p => {
                //                if (p.ClientId == "SpecialClient")
                //                {
                //                    if (p.Username != "" || p.Password != "")
                //                    {
                //                        p.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                //                    }
                //                }
                //            }),

                //            //Storage = new RetainedMessageHandler(),

                //            ApplicationMessageInterceptor = new MqttServerApplicationMessageInterceptorDelegate(context => {
                //                if (MqttTopicFilterComparer.IsMatch(context.ApplicationMessage.Topic, "/myTopic/WithTimestamp/#"))
                //                {
                //                    // Replace the payload with the timestamp. But also extending a JSON 
                //                    // based payload with the timestamp is a suitable use case.
                //                    context.ApplicationMessage.Payload = Encoding.UTF8.GetBytes(DateTime.Now.ToString("O"));
                //                }

                //                if (context.ApplicationMessage.Topic == "not_allowed_topic")
                //                {
                //                    context.AcceptPublish = false;
                //                    context.CloseConnection = true;
                //                }
                //            }),

                //            SubscriptionInterceptor = new MqttServerSubscriptionInterceptorDelegate(context => {
                //                if (context.TopicFilter.Topic.StartsWith("admin/foo/bar") && context.ClientId != "theAdmin")
                //                {
                //                    context.AcceptSubscription = false;
                //                }

                //                if (context.TopicFilter.Topic.StartsWith("the/secret/stuff") && context.ClientId != "Imperator")
                //                {
                //                    context.AcceptSubscription = false;
                //                    context.CloseConnection = true;
                //                }
                //            })
                //        };

                //        // Extend the timestamp for all messages from clients.
                //        // Protect several topics from being subscribed from every client.

                //        //var certificate = new X509Certificate(@"C:\certs\test\test.cer", "");
                //        //options.TlsEndpointOptions.Certificate = certificate.Export(X509ContentType.Cert);
                //        //options.ConnectionBacklog = 5;
                //        //options.DefaultEndpointOptions.IsEnabled = true;
                //        //options.TlsEndpointOptions.IsEnabled = false;

                //        var mqttServer = new MqttFactory().CreateMqttServer();

                //        mqttServer.ApplicationMessageReceivedHandler = new MqttApplicationMessageReceivedHandlerDelegate(e => {
                //            Console.WriteLine(
                //                $"'{e.ClientId}' reported '{e.ApplicationMessage.Topic}' > '{Encoding.UTF8.GetString(e.ApplicationMessage.Payload ?? new byte[0])}'",
                //                ConsoleColor.Magenta);
                //        });

                //        //options.ApplicationMessageInterceptor = c =>
                //        //{
                //        //    if (c.ApplicationMessage.Payload == null || c.ApplicationMessage.Payload.Length == 0)
                //        //    {
                //        //        return;
                //        //    }

                //        //    try
                //        //    {
                //        //        var content = JObject.Parse(Encoding.UTF8.GetString(c.ApplicationMessage.Payload));
                //        //        var timestampProperty = content.Property("timestamp");
                //        //        if (timestampProperty != null && timestampProperty.Value.Type == JTokenType.Null)
                //        //        {
                //        //            timestampProperty.Value = DateTime.Now.ToString("O");
                //        //            c.ApplicationMessage.Payload = Encoding.UTF8.GetBytes(content.ToString());
                //        //        }
                //        //    }
                //        //    catch (Exception)
                //        //    {
                //        //    }
                //        //};

                //        mqttServer.ClientConnectedHandler = new MqttServerClientConnectedHandlerDelegate(e => {
                //            Console.Write("Client disconnected event fired.");
                //        });

                //        mqttServer.StartAsync(options).GetAwaiter().GetResult();

                //        Console.WriteLine("Press any key to exit.");
                //        Console.ReadLine();

                //        // await mqttServer.StopAsync();
                //    }
                //    catch (Exception e)
                //    {
                //        Console.WriteLine(e);
                //    }



                //    NetworkAddressEndPoint = UdpClientCreator.GetEndPoint(networkAddressUrlState.Url);

                //if (NetworkAddressEndPoint == null)
                //{
                //    Utils.Trace(Utils.TraceMasks.Error, "The configuration for connection {0} with Url:'{1}' resulted in an invalid endpoint.",
                //              this.PubSubConnectionConfiguration.Name, networkAddressUrlState.Url);
                //    return;
                //}

                ////publisher initialization    
                //if (Publishers.Count > 0)
                //{
                //    m_publisherUdpClients = UdpClientCreator.GetUdpClients(UsedInContext.Publisher, networkAddressUrlState, NetworkAddressEndPoint);
                //}

                ////subscriber initialization   
                //if (DataSetReaders.Count > 0)
                //{
                //    m_subscriberUdpClients = UdpClientCreator.GetUdpClients(UsedInContext.Subscriber, networkAddressUrlState, NetworkAddressEndPoint);

                //    foreach (UdpClient subscriberUdpClient in m_subscriberUdpClients)
                //    {
                //        try
                //        {
                //            subscriberUdpClient.BeginReceive(new AsyncCallback(OnUadpReceive), subscriberUdpClient);
                //        }
                //        catch (Exception ex)
                //        {
                //            Utils.Trace(Utils.TraceMasks.Information, "UdpClient '{0}' Cannot receive data. Exception: {1}",
                //              subscriberUdpClient.Client.LocalEndPoint, ex.Message);
                //        }
                //    }
                //}
            }
        }

        protected override void InternalStop()
        {
            throw new NotImplementedException();
        }
    }
}
