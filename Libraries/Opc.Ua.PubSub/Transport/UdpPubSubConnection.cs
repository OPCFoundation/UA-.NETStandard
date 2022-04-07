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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;

namespace Opc.Ua.PubSub.Transport
{
    /// <summary>
    /// UADP implementation of <see cref="UaPubSubConnection"/> class.
    /// </summary>
    internal class UdpPubSubConnection : UaPubSubConnection
    {
        #region Private Fields
        private List<UdpClient> m_publisherUdpClients = new List<UdpClient>();
        private List<UdpClient> m_subscriberUdpClients = new List<UdpClient>();
        private UdpDiscoverySubscriber m_udpDiscoverySubscriber;
        private UdpDiscoveryPublisher m_udpDiscoveryPublisher;

        private static int s_sequenceNumber = 0;
        private static int s_dataSetSequenceNumber = 0;
       
        #endregion

        #region Constructor
        /// <summary>
        ///  Create new instance of <see cref="UdpPubSubConnection"/> from <see cref="PubSubConnectionDataType"/> configuration data
        /// </summary>
        public UdpPubSubConnection(UaPubSubApplication uaPubSubApplication, PubSubConnectionDataType pubSubConnectionDataType)
            : base(uaPubSubApplication, pubSubConnectionDataType)
        {
            m_transportProtocol = TransportProtocol.UDP;

            Utils.Trace("UdpPubSubConnection with name '{0}' was created.", pubSubConnectionDataType.Name);

            Initialize();
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Get the NetworkInterface name from configured <see cref="PubSubConnectionDataType"/>.Address.
        /// </summary>
        public string NetworkInterfaceName { get; set; }

        /// <summary>
        /// Get the <see cref="IPEndPoint"/> from configured <see cref="PubSubConnectionDataType"/>.Address.
        /// </summary>
        public IPEndPoint NetworkAddressEndPoint { get; private set; }

        /// <summary>
        /// Get the port from configured <see cref="PubSubConnectionDataType"/>.Address
        /// </summary>
        public int Port { get; private set; }
        #endregion

        #region UaPubSubConnection - Overrides
        /// <summary>
        /// Perform specific Start tasks
        /// </summary>
        protected override async Task InternalStart()
        {
            //cleanup all existing UdpClient previously open
            await InternalStop().ConfigureAwait(false);

            if (NetworkAddressEndPoint == null)
            {
                return;
            }

            //publisher initialization    
            if (Publishers.Count > 0)
            {
                lock (m_lock)
                {
                    m_publisherUdpClients = UdpClientCreator.GetUdpClients(UsedInContext.Publisher, NetworkInterfaceName, NetworkAddressEndPoint);
                }

                m_udpDiscoveryPublisher = new UdpDiscoveryPublisher(this);
                await m_udpDiscoveryPublisher.StartAsync(MessageContext).ConfigureAwait(false);
            }

            //subscriber initialization   
            if (GetAllDataSetReaders().Count > 0)
            {
                lock (m_lock)
                {
                    m_subscriberUdpClients = UdpClientCreator.GetUdpClients(UsedInContext.Subscriber, NetworkInterfaceName, NetworkAddressEndPoint);

                    foreach (UdpClient subscriberUdpClient in m_subscriberUdpClients)
                    {
                        try
                        {
                            subscriberUdpClient.BeginReceive(new AsyncCallback(OnUadpReceive), subscriberUdpClient);
                        }
                        catch (Exception ex)
                        {
                            Utils.Trace(Utils.TraceMasks.Information, "UdpClient '{0}' Cannot receive data. Exception: {1}",
                              subscriberUdpClient.Client.LocalEndPoint, ex.Message);
                        }
                    }
                }

                // initialize the discovery channel
                m_udpDiscoverySubscriber = new UdpDiscoverySubscriber(this);
                await m_udpDiscoverySubscriber.StartAsync(MessageContext).ConfigureAwait(false);

                // add handler to metaDataReceived event
                this.Application.MetaDataReceived += Application_MetaDataReceived;
            }
        }       

        /// <summary>
        /// Perform specific Stop tasks
        /// </summary>
        protected override async Task InternalStop()
        {
            lock (m_lock)
            {
                foreach (var list in new List<List<UdpClient>>() { m_publisherUdpClients, m_subscriberUdpClients })
                {
                    if (list != null && list.Count > 0)
                    {
                        foreach (var udpClient in list)
                        {
                            udpClient.Close();
                            udpClient.Dispose();
                        }
                        list.Clear();
                    }
                }
            }

            if (m_udpDiscoveryPublisher != null)
            {
               await m_udpDiscoveryPublisher.StopAsync().ConfigureAwait(false);
            }

            if (m_udpDiscoverySubscriber != null)
            {
                await m_udpDiscoverySubscriber.StopAsync().ConfigureAwait(false);

                // remove handler to metaDataReceived event
                this.Application.MetaDataReceived -= Application_MetaDataReceived;               
            }
        }

        /// <summary>
        /// Create the list of network messages built from the provided writerGroupConfiguration
        /// </summary>
        public override IList<UaNetworkMessage> CreateNetworkMessages(WriterGroupDataType writerGroupConfiguration, WriterGroupPublishState state)
        {
            UadpWriterGroupMessageDataType messageSettings = ExtensionObject.ToEncodeable(writerGroupConfiguration.MessageSettings)
                as UadpWriterGroupMessageDataType;
            if (messageSettings == null)
            {
                //Wrong configuration of writer group MessageSettings
                return null;
            }
            DatagramWriterGroupTransportDataType transportSettings = ExtensionObject.ToEncodeable(writerGroupConfiguration.TransportSettings)
                as DatagramWriterGroupTransportDataType;

            if (transportSettings == null)
            {
                //Wrong configuration of writer group TransportSettings
                return null;
            }
            List<UaNetworkMessage> networkMessages = new List<UaNetworkMessage>();

            //Create list of dataSet messages to be sent
            List<UadpDataSetMessage> dataSetMessages = new List<UadpDataSetMessage>();
            foreach (DataSetWriterDataType dataSetWriter in writerGroupConfiguration.DataSetWriters)
            {
                //check if dataSetWriter enabled
                if (dataSetWriter.Enabled)
                {
                    DataSet dataSet = CreateDataSet(dataSetWriter, state);  

                    if (dataSet != null)
                    {
                        bool hasMetaDataChanged =  state.HasMetaDataChanged(dataSetWriter, dataSet.DataSetMetaData);

                        if (hasMetaDataChanged)
                        {
                            // add metadata network message
                            networkMessages.Add( new UadpNetworkMessage(writerGroupConfiguration, dataSet.DataSetMetaData) {
                                PublisherId = PubSubConnectionConfiguration.PublisherId.Value,
                                DataSetWriterId = dataSetWriter.DataSetWriterId
                            });
                        }

                        UadpDataSetWriterMessageDataType dataSetMessageSettings = ExtensionObject.ToEncodeable(dataSetWriter.MessageSettings) as
                                UadpDataSetWriterMessageDataType;
                        // check MessageSettings to see how to encode DataSet
                        if (dataSetMessageSettings != null)
                        {
                            UadpDataSetMessage uadpDataSetMessage = new UadpDataSetMessage(dataSet);
                            uadpDataSetMessage.DataSetWriterId = dataSetWriter.DataSetWriterId;
                            uadpDataSetMessage.SetMessageContentMask((UadpDataSetMessageContentMask)dataSetMessageSettings.DataSetMessageContentMask);
                            uadpDataSetMessage.SetFieldContentMask((DataSetFieldContentMask)dataSetWriter.DataSetFieldContentMask);
                            uadpDataSetMessage.SequenceNumber = (ushort)(Utils.IncrementIdentifier(ref s_dataSetSequenceNumber) % UInt16.MaxValue);
                            uadpDataSetMessage.ConfiguredSize = dataSetMessageSettings.ConfiguredSize;
                            uadpDataSetMessage.DataSetOffset = dataSetMessageSettings.DataSetOffset;
                            uadpDataSetMessage.Timestamp = DateTime.UtcNow;
                            uadpDataSetMessage.Status = StatusCodes.Good;
                            dataSetMessages.Add(uadpDataSetMessage);

                            state.OnMessagePublished(dataSetWriter, dataSet);
                        }
                    }
                }
            }

            //cancel send if no dataset message
            if (dataSetMessages.Count == 0)
            {
                return networkMessages;
            }

            UadpNetworkMessage uadpNetworkMessage = new UadpNetworkMessage(writerGroupConfiguration, dataSetMessages);
            uadpNetworkMessage.SetNetworkMessageContentMask((UadpNetworkMessageContentMask)messageSettings.NetworkMessageContentMask);
            uadpNetworkMessage.WriterGroupId = writerGroupConfiguration.WriterGroupId;
            // Network message header
            uadpNetworkMessage.PublisherId = PubSubConnectionConfiguration.PublisherId.Value;
            uadpNetworkMessage.SequenceNumber = (ushort)(Utils.IncrementIdentifier(ref s_sequenceNumber) % UInt16.MaxValue);

            // Writer group header
            uadpNetworkMessage.GroupVersion = messageSettings.GroupVersion;
            uadpNetworkMessage.NetworkMessageNumber = 1; //only one network message per publish

            networkMessages.Add(uadpNetworkMessage);

            
            return networkMessages;
        }

        /// <summary>
        /// Create and return the list of DataSetMetaData response messages 
        /// </summary>
        /// <param name="dataSetWriterIds"></param>
        /// <returns></returns>
        public IList<UaNetworkMessage> CreateDataSetMetaDataNetworkMessages(UInt16[] dataSetWriterIds)
        {
            List<UaNetworkMessage> networkMessages = new List<UaNetworkMessage>();
            var writers = GetAllDataSetWriters();

            foreach(UInt16 dataSetWriterId in dataSetWriterIds)
            {
                DataSetWriterDataType writer = writers.Where(w => w.DataSetWriterId == dataSetWriterId).FirstOrDefault();
                if (writer != null)
                {
                    WriterGroupDataType writerGroup = PubSubConnectionConfiguration.WriterGroups.Where(wg => wg.DataSetWriters.Contains(writer)).FirstOrDefault();
                    if (writerGroup != null)
                    {
                        DataSetMetaDataType metaData = Application.DataCollector.GetPublishedDataSet(writer.DataSetName)?.DataSetMetaData;
                        if (metaData != null)
                        {
                            UadpNetworkMessage networkMessage = new UadpNetworkMessage(writerGroup, metaData);
                            networkMessage.PublisherId = PubSubConnectionConfiguration.PublisherId.Value;
                            networkMessage.DataSetWriterId = dataSetWriterId;

                            networkMessages.Add(networkMessage);
                        }
                    }                    
                }
            }          

            return networkMessages;
        }

        /// <summary>
        /// Publish the network message
        /// </summary>
        public override bool PublishNetworkMessage(UaNetworkMessage networkMessage)
        {
            if (networkMessage == null || m_publisherUdpClients == null || m_publisherUdpClients.Count == 0)
            {
                return false;
            }

            try
            {
                lock (m_lock)
                {
                    if (m_publisherUdpClients != null && m_publisherUdpClients.Count > 0)
                    {
                        // Get encoded bytes
                        byte[] bytes = networkMessage.Encode(MessageContext);

                        foreach (var udpClient in m_publisherUdpClients)
                        {
                            try
                            {
                                udpClient.Send(bytes, bytes.Length, NetworkAddressEndPoint);

                                Utils.Trace("UdpPubSubConnection.PublishNetworkMessage bytes:{0}, endpoint:{1}", bytes.Length, NetworkAddressEndPoint);
                            }
                            catch (Exception ex)
                            {
                                Utils.Trace(ex, "UdpPubSubConnection.PublishNetworkMessage");
                                return false;
                            }
                        }
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "UdpPubSubConnection.PublishNetworkMessage");
                return false;
            }

            return false;
        }

        /// <summary>
        /// Always returns true since UDP is a connectionless protocol
        /// </summary>
        public override bool AreClientsConnected()
        {
            return true;
        }
        #endregion

        #region Private methods
        /// <summary>
        /// Initialize Conection properties from connection configuration object
        /// </summary>
        private void Initialize()
        {
            NetworkAddressUrlDataType networkAddressUrlState = ExtensionObject.ToEncodeable(PubSubConnectionConfiguration.Address)
                       as NetworkAddressUrlDataType;
            if (networkAddressUrlState == null)
            {
                Utils.Trace(Utils.TraceMasks.Error, "The configuration for connection {0} has invalid Address configuration.",
                          PubSubConnectionConfiguration.Name);
                return;
            }
            // set properties
            NetworkInterfaceName = networkAddressUrlState.NetworkInterface;
            NetworkAddressEndPoint = UdpClientCreator.GetEndPoint(networkAddressUrlState.Url);

            if (NetworkAddressEndPoint == null)
            {
                Utils.Trace(Utils.TraceMasks.Error, "The configuration for connection {0} with Url:'{1}' resulted in an invalid endpoint.",
                          PubSubConnectionConfiguration.Name, networkAddressUrlState.Url);
            }            
        }

        /// <summary>
        /// Process the bytes received from UADP channel as Subscriber
        /// </summary>
        /// <param name="message"></param>
        /// <param name="source"></param>
        private void ProcessReceivedMessage(byte[] message, IPEndPoint source)
        {
            Utils.Trace(Utils.TraceMasks.Information, "UdpPubSubConnection.ProcessReceivedMessage from source={0}", source);

            List<DataSetReaderDataType> dataSetReaders = GetOperationalDataSetReaders();
            List<DataSetReaderDataType> dataSetReadersToDecode = new List<DataSetReaderDataType>();
            
            foreach (DataSetReaderDataType dataSetReader in dataSetReaders)
            {
                // check if dataSetReaders have metadata information
                if (!ConfigurationVersionUtils.IsUsable(dataSetReader.DataSetMetaData)) 
                {
                    // check if it is possible to request the metadata information
                    if (dataSetReader.DataSetWriterId != 0)
                    {
                        m_udpDiscoverySubscriber.AddWriterIdForDataSetMetadata(dataSetReader.DataSetWriterId);                       
                    }
                }
                else
                {
                    dataSetReadersToDecode.Add(dataSetReader);
                }
            }          

            UadpNetworkMessage networkMessage = new UadpNetworkMessage();
            networkMessage.DataSetDecodeErrorOccurred += NetworkMessage_DataSetDecodeErrorOccurred;
            networkMessage.Decode(MessageContext, message, dataSetReadersToDecode);
            networkMessage.DataSetDecodeErrorOccurred -= NetworkMessage_DataSetDecodeErrorOccurred;

            // Process the decoded network message 
            ProcessDecodedNetworkMessage(networkMessage, source.ToString());
        }


        /// <summary>
        /// Handle Receive event for an UADP channel on Subscriber Side
        /// </summary>
        /// <param name="result"></param>
        private void OnUadpReceive(IAsyncResult result)
        {
            lock (m_lock)
            {
                if (m_subscriberUdpClients == null || m_subscriberUdpClients.Count == 0)
                {
                    return;
                }
            }

            // this is what had been passed into BeginReceive as the second parameter:
            UdpClient socket = result.AsyncState as UdpClient;

            if (socket == null)
            {
                return;
            }

            // points towards whoever had sent the message:
            IPEndPoint source = new IPEndPoint(0, 0);
            // get the actual message and fill out the source:
            try
            {
                byte[] message = socket.EndReceive(result, ref source);

                if (message != null)
                {
                    Utils.Trace("OnUadpReceive received message with length {0} from {1}", message.Length, source.Address);       

                    if (message.Length > 1)
                    {
                        // raise RawData received event
                        RawDataReceivedEventArgs rawDataReceivedEventArgs = new RawDataReceivedEventArgs() {
                            Message = message,
                            Source = source.Address.ToString(),
                            TransportProtocol = this.TransportProtocol,
                            MessageMapping = MessageMapping.Uadp,
                            PubSubConnectionConfiguration = PubSubConnectionConfiguration
                        };

                        // trigger notification for received raw data
                        Application.RaiseRawDataReceivedEvent(rawDataReceivedEventArgs);

                        // check if the RawData message is marked as handled
                        if (rawDataReceivedEventArgs.Handled)
                        {
                            Utils.Trace("UdpConnection message from source={0} is marked as handled and will not be decoded.", rawDataReceivedEventArgs.Source);
                            return;
                        }

                        // call on a new thread
                        Task.Run(() => {
                            ProcessReceivedMessage(message, source);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "OnUadpReceive from {0}", source.Address);
            }

            try
            {
                // schedule the next receive operation once reading is done:
                socket.BeginReceive(new AsyncCallback(OnUadpReceive), socket);
            }
            catch (Exception ex)
            {
                Utils.Trace(Utils.TraceMasks.Information, "OnUadpReceive BeginReceive threw Exception {0}", ex.Message);

                lock (m_lock)
                {
                    Renew(socket);
                }
            }
        }

        /// <summary>
        /// Re initializes the socket 
        /// </summary>
        /// <param name="socket">The socket which should be reinitialized</param>
        private void Renew(UdpClient socket)
        {
            UdpClient newsocket = null;

            if (socket is UdpClientMulticast mcastSocket)
            {
                newsocket = new UdpClientMulticast(mcastSocket.Address, mcastSocket.MulticastAddress, mcastSocket.Port);
            }
            else if (socket is UdpClientBroadcast bcastSocket)
            {
                newsocket = new UdpClientBroadcast(bcastSocket.Address, bcastSocket.Port, bcastSocket.PubSubContext);
            }
            else if (socket is UdpClientUnicast ucastSocket)
            {
                newsocket = new UdpClientUnicast(ucastSocket.Address, ucastSocket.Port);
            }
            m_subscriberUdpClients.Remove(socket);
            m_subscriberUdpClients.Add(newsocket);
            socket.Close();
            socket.Dispose();

            if (newsocket != null)
            {
                newsocket.BeginReceive(new AsyncCallback(OnUadpReceive), newsocket);
            }
        }       

        /// <summary>
        /// Resets SequenceNumber 
        /// </summary>
        internal void ResetSequenceNumber()
        {
            s_sequenceNumber = 0;
            s_dataSetSequenceNumber = 0;
        }

        /// <summary>
        /// Handle <see cref="UaPubSubApplication.MetaDataReceived"/> event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Application_MetaDataReceived(object sender, SubscribedDataEventArgs e)
        {           
            if (m_udpDiscoverySubscriber != null && e.NetworkMessage.DataSetWriterId != null)
            {
                m_udpDiscoverySubscriber.RemoveWriterIdForDataSetMetadata(e.NetworkMessage.DataSetWriterId.Value);
            }
        }

        /// <summary>
        /// Handle <see cref="UaNetworkMessage.DataSetDecodeErrorOccurred"/> event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void NetworkMessage_DataSetDecodeErrorOccurred(object sender, DataSetDecodeErrorEventArgs e)
        {
            if (e.DecodeErrorReason == DataSetDecodeErrorReason.MetadataMajorVersion)
            {
                // Resend metadata request
                // check if it is possible to request the metadata information
                if (e.DataSetReader.DataSetWriterId != 0)
                {
                    m_udpDiscoverySubscriber.AddWriterIdForDataSetMetadata(e.DataSetReader.DataSetWriterId);
                }
            }
        }
        #endregion
    }
}
