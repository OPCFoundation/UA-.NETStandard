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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
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
        private static int m_sequenceNumber = 0;
        private static int m_dataSetSequenceNumber = 0;

        /// <summary>
        /// Event that is triggered when the <see cref="UaPubSubApplication"/> receives and decodes subscribed DataSets
        /// </summary>
        internal event EventHandler<UadpDataEventArgs> UadpMessageReceived;
        #endregion

        #region Constructor
        /// <summary>
        ///  Create new instance of <see cref="UdpPubSubConnection"/> from <see cref="PubSubConnectionDataType"/> configuration data
        /// </summary>
        public UdpPubSubConnection(UaPubSubApplication uaPubSubApplication, PubSubConnectionDataType pubSubConnectionDataType)
            : base(uaPubSubApplication, pubSubConnectionDataType)
        {

            m_transportProtocol = TransportProtocol.UADP;

            Utils.Trace("UdpPubSubConnection with name '{0}' was created.", pubSubConnectionDataType.Name);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Get the <see cref="IPAddress"/> from configured <see cref="PubSubConnectionDataType"/>.Address.
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
        protected override Task InternalStart()
        {
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
                    return Task.FromResult<object>(null);
                }
                NetworkInterfaceName = networkAddressUrlState.NetworkInterface;
                NetworkAddressEndPoint = UdpClientCreator.GetEndPoint(networkAddressUrlState.Url);

                if (NetworkAddressEndPoint == null)
                {
                    Utils.Trace(Utils.TraceMasks.Error, "The configuration for connection {0} with Url:'{1}' resulted in an invalid endpoint.",
                              this.PubSubConnectionConfiguration.Name, networkAddressUrlState.Url);
                    return Task.FromResult<object>(null);
                }

                //publisher initialization    
                if (Publishers.Count > 0)
                {
                    m_publisherUdpClients = UdpClientCreator.GetUdpClients(UsedInContext.Publisher, networkAddressUrlState, NetworkAddressEndPoint);
                }

                //subscriber initialization   
                if (GetAllDataSetReaders().Count > 0)
                {
                    m_subscriberUdpClients = UdpClientCreator.GetUdpClients(UsedInContext.Subscriber, networkAddressUrlState, NetworkAddressEndPoint);

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
            }
            return Task.FromResult<object>(null);
        }

        /// <summary>
        /// Perform specific Stop tasks
        /// </summary>
        protected override Task InternalStop()
        {
            lock (m_lock)
            {
                if (m_publisherUdpClients != null && m_publisherUdpClients.Count > 0)
                {
                    foreach (var udpClient in m_publisherUdpClients)
                    {
                        udpClient.Close();
                        udpClient.Dispose();
                    }
                    m_publisherUdpClients.Clear();
                }

                if (m_subscriberUdpClients != null && m_subscriberUdpClients.Count > 0)
                {
                    foreach (var udpClient in m_subscriberUdpClients)
                    {
                        udpClient.Close();
                        udpClient.Dispose();
                    }
                    m_subscriberUdpClients.Clear();
                }
            }
            return Task.FromResult<object>(null);
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

            //Create list of dataSet messages to be sent
            List<UadpDataSetMessage> dataSetMessages = new List<UadpDataSetMessage>();
            foreach (DataSetWriterDataType dataSetWriter in writerGroupConfiguration.DataSetWriters)
            {
                //check if dataSetWriter enabled
                if (dataSetWriter.Enabled)
                {
                    DataSet dataSet = Application.DataCollector.CollectData(dataSetWriter.DataSetName, false);

                    if (dataSet != null)
                    {
                        UadpDataSetWriterMessageDataType dataSetMessageSettings =
                            ExtensionObject.ToEncodeable(dataSetWriter.MessageSettings) as
                                UadpDataSetWriterMessageDataType;
                        // check MessageSettings to see how to encode DataSet
                        if (dataSetMessageSettings != null)
                        {
                            UadpDataSetMessage uadpDataSetMessage = new UadpDataSetMessage(dataSet);
                            uadpDataSetMessage.DataSetWriterId = dataSetWriter.DataSetWriterId;
                            uadpDataSetMessage.SetMessageContentMask((UadpDataSetMessageContentMask)dataSetMessageSettings.DataSetMessageContentMask);
                            uadpDataSetMessage.SetFieldContentMask((DataSetFieldContentMask)dataSetWriter.DataSetFieldContentMask);
                            uadpDataSetMessage.SequenceNumber = (ushort)(Utils.IncrementIdentifier(ref m_dataSetSequenceNumber) % UInt16.MaxValue);
                            uadpDataSetMessage.ConfiguredSize = dataSetMessageSettings.ConfiguredSize;
                            uadpDataSetMessage.DataSetOffset = dataSetMessageSettings.DataSetOffset;
                            uadpDataSetMessage.Timestamp = DateTime.UtcNow;
                            uadpDataSetMessage.Status = StatusCodes.Good;
                            dataSetMessages.Add(uadpDataSetMessage);
                        }
                    }
                }
            }

            //cancel send if no dataset message
            if (dataSetMessages.Count == 0)
            {
                return null;
            }

            UadpNetworkMessage uadpNetworkMessage = new UadpNetworkMessage(writerGroupConfiguration, dataSetMessages);
            uadpNetworkMessage.SetNetworkMessageContentMask((UadpNetworkMessageContentMask)messageSettings.NetworkMessageContentMask);
            uadpNetworkMessage.WriterGroupId = writerGroupConfiguration.WriterGroupId;
            // Network message header
            uadpNetworkMessage.PublisherId = PubSubConnectionConfiguration.PublisherId.Value;
            uadpNetworkMessage.SequenceNumber = (ushort)(Utils.IncrementIdentifier(ref m_sequenceNumber) % UInt16.MaxValue);

            // Writer group header
            uadpNetworkMessage.GroupVersion = messageSettings.GroupVersion;
            uadpNetworkMessage.NetworkMessageNumber = 1; //only one network message per publish

            return new List<UaNetworkMessage>() { uadpNetworkMessage };
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
                        byte[] bytes = networkMessage.Encode();

                        foreach (var udpClient in m_publisherUdpClients)
                        {
                            try
                            {
                                udpClient.Send(bytes, bytes.Length, NetworkAddressEndPoint);
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
        #endregion

        #region Private methods
        /// <summary>
        /// Process the bytes received from UADP channel
        /// </summary>
        /// <param name="message"></param>
        /// <param name="source"></param>
        private void ProcessReceivedMessage(byte[] message, IPEndPoint source)
        {
            Utils.Trace(Utils.TraceMasks.Information, "UdpPubSubConnection.ProcessReceivedMessage from source={0}", source);

            UadpNetworkMessage networkMessage = new UadpNetworkMessage();
            networkMessage.Decode(m_context, message, GetOperationalDataSetReaders());

            // Raise rhe DataReceived event 
            RaiseNetworkMessageDataReceivedEvent(networkMessage, source.ToString());
        }

        /// <summary>
        /// Handle Receive event for an UADP channel
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
                    RaiseUadpDataReceivedEvent(
                            new UadpDataEventArgs() {
                                Message = message,
                                SourceEndPoint = source
                            });

                    Utils.Trace(Utils.TraceMasks.Information, "OnUadpReceive received message with length {0} from {1}", message.Length, source.Address);

                    if (message.Length > 1)
                    {
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
                Utils.Trace(Utils.TraceMasks.Information, "OnUadpReceive BeginReceive throwed Exception {0}", ex.Message);

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
        /// Raise DataReceived event
        /// </summary>
        /// <param name="e"></param>
        internal void RaiseUadpDataReceivedEvent(UadpDataEventArgs e)
        {
            try
            {
                if (UadpMessageReceived != null)
                {
                    UadpMessageReceived(this, e);
                }
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "UaPubSubConnection.RaiseSubscriptionReceivedEvent");
            }
        }

        /// <summary>
        /// Resets SequenceNumber 
        /// </summary>
        internal void ResetSequenceNumber()
        {
            m_sequenceNumber = 0;
            m_dataSetSequenceNumber = 0;
        }
        #endregion
    }
}
