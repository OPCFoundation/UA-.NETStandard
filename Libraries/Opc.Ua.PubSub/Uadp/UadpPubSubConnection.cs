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

using Opc.Ua.PubSub.PublishedData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Uadp
{
    /// <summary>
    /// UADP implementation of <see cref="UaPubSubConnection"/> class.
    /// </summary>
    internal class UadpPubSubConnection : UaPubSubConnection
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
        ///  Create new instance of <see cref="UadpPubSubConnection"/> from <see cref="PubSubConnectionDataType"/> configuration data
        /// </summary>
        /// <param name="uaPubSubApplication"></param>
        /// <param name="pubSubConnectionDataType"></param>
        public UadpPubSubConnection(UaPubSubApplication uaPubSubApplication,  PubSubConnectionDataType pubSubConnectionDataType)
            : base(uaPubSubApplication, pubSubConnectionDataType)
        {
            m_transportProtocol = TransportProtocol.UADP;
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
        /// Initialize UADP connection and return true if success.
        /// </summary>
        /// <returns></returns>
        protected override bool InternalInitialize()
        {
            return true;
        }
       
        /// <summary>
        /// Perform specific Start tasks
        /// </summary>
        protected override void InternalStart()
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
                    return;
                }
                NetworkInterfaceName = networkAddressUrlState.NetworkInterface;
                NetworkAddressEndPoint = UdpClientCreator.GetEndPoint(networkAddressUrlState.Url);

                if (NetworkAddressEndPoint == null)
                {
                    Utils.Trace(Utils.TraceMasks.Error, "The configuration for connection {0} with Url:'{1}' resulted in an invalid endpoint.",
                              this.PubSubConnectionConfiguration.Name, networkAddressUrlState.Url);
                    return;
                }

                //publisher initialization    
                if (Publishers.Count > 0)
                {
                    m_publisherUdpClients = UdpClientCreator.GetUdpClients(UsedInContext.Publisher, networkAddressUrlState, NetworkAddressEndPoint);
                }                

                //subscriber initialization   
                if (DataSetReaders.Count > 0)
                {                    
                    m_subscriberUdpClients = UdpClientCreator.GetUdpClients(UsedInContext.Subscriber, networkAddressUrlState, NetworkAddressEndPoint);
                  
                    foreach(UdpClient subscriberUdpClient in m_subscriberUdpClients)
                    {
                        try
                        {
                            subscriberUdpClient.BeginReceive(new AsyncCallback(OnUadpReceive), subscriberUdpClient);
                        }
                        catch(Exception ex)
                        {
                            Utils.Trace(Utils.TraceMasks.Information, "UdpClient '{0}' Cannot receive data. Exception: {1}",
                              subscriberUdpClient.Client.LocalEndPoint, ex.Message);
                        }
                    }                   
                }
            }
        }

        /// <summary>
        /// Perform specific Stop tasks
        /// </summary>
        protected override void InternalStop()
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
        }


        /// <summary>
        /// Create the network message built from the provided writerGroupConfiguration
        /// </summary>
        /// <param name="writerGroupConfiguration"></param>
        /// <returns></returns>
        public override UaNetworkMessage CreateNetworkMessage(WriterGroupDataType writerGroupConfiguration)
        {
            UadpWriterGroupMessageDataType messageSettings = ExtensionObject.ToEncodeable(writerGroupConfiguration.MessageSettings) 
                as UadpWriterGroupMessageDataType;
            if (messageSettings == null)
            {
                //Wrong configuration of writer group MessageSettings
                return null;
            }

            //Create list of dataSet messages to be sent
            List<UadpDataSetMessage> dataSetMessages = new List<UadpDataSetMessage>();
            foreach (DataSetWriterDataType dataSetWriter in writerGroupConfiguration.DataSetWriters)
            {
                //check if dataSetWriter enabled
                if (dataSetWriter.Enabled)
                {
                    DataSet dataSet = Application.DataCollector.CollectData(dataSetWriter.DataSetName);
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
                            uadpDataSetMessage.TimeStamp = DateTime.UtcNow;
                            uadpDataSetMessage.Status = (ushort)StatusCodes.Good;
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

            UadpNetworkMessage uadpNetworkMessage = new UadpNetworkMessage(dataSetMessages);
            uadpNetworkMessage.SetNetworkMessageContentMask((UadpNetworkMessageContentMask)messageSettings.NetworkMessageContentMask);
            uadpNetworkMessage.WriterGroupId = writerGroupConfiguration.WriterGroupId;
            // Network message header
            uadpNetworkMessage.PublisherId = PubSubConnectionConfiguration.PublisherId.Value;
            uadpNetworkMessage.SequenceNumber = (ushort)(Utils.IncrementIdentifier(ref m_sequenceNumber) % UInt16.MaxValue);

            // Writer group header
            uadpNetworkMessage.GroupVersion = messageSettings.GroupVersion;
            uadpNetworkMessage.NetworkMessageNumber = 1; //only one network message per publish
      
            return uadpNetworkMessage;
        }

        /// <summary>
        /// Publish the network message
        /// </summary>
        /// <param name="networkMessage"></param>
        /// <returns></returns>
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
                        ServiceMessageContext messageContext = new ServiceMessageContext();

                        BinaryEncoder encoder = new BinaryEncoder(messageContext);
                        networkMessage.Encode(encoder);
                        byte[] bytes = ReadBytes(encoder.BaseStream);
                        encoder.Dispose();

                        foreach(var udpClient in m_publisherUdpClients)
                        {
                            try
                            {
                                int sent = udpClient.Send(bytes, bytes.Length, NetworkAddressEndPoint);
                            }
                            catch(Exception ex)
                            {
                                Utils.Trace(ex, "UadpPubSubConnection.PublishNetworkMessage");
                                return false;
                            }
                        }                        
                        return true;
                    }
                }                
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "UadpPubSubConnection.PublishNetworkMessage");
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
            Utils.Trace(Utils.TraceMasks.Information, "UadpPubSubConnection.ProcessReceivedMessage from source={0}", source);
            ServiceMessageContext messageContext = new ServiceMessageContext();

            using (BinaryDecoder decoder = new BinaryDecoder(message, messageContext))
            {
                UadpNetworkMessage uadpNetworkMessage = new UadpNetworkMessage();
                //decode bytes using dataset reader information
                var subscribedDataSets = uadpNetworkMessage.DecodeSubscribedDataSets(decoder, DataSetReaders);
                if (subscribedDataSets != null && subscribedDataSets.Count > 0)
                {
                    //trigger notification for received subscribed data set
                    Application.RaiseDataReceivedEvent(
                        new SubscribedDataEventArgs()
                        {
                            NetworkMessageSequenceNumber = uadpNetworkMessage.SequenceNumber,
                            DataSets = subscribedDataSets,
                            SourceEndPoint = source
                        }
                        );
                    Utils.Trace(Utils.TraceMasks.Information, 
                        "UadpPubSubConnection.RaiseDataReceivedEvent from source={0}, with {1} DataSets", source, subscribedDataSets.Count);
                }
                else
                {
                    Utils.Trace(Utils.TraceMasks.Information,
                        "Message from source={0} cannot be decoded.", source);
                }
            }
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
            // points towards whoever had sent the message:
            IPEndPoint source = new IPEndPoint(0, 0);
            // get the actual message and fill out the source:
            try
            {
                byte[] message = socket.EndReceive(result, ref source);

                RaiseUadpDataReceivedEvent(
                            new UadpDataEventArgs()
                            {
                                Message = message,
                                SourceEndPoint = source
                            });

                Utils.Trace(Utils.TraceMasks.Information, "OnUadpReceive received message with length {0} from {1}", message.Length, source.Address);

                if (message != null && message.Length > 1)
                {
                    // call on a new thread
                    Task.Run(() =>
                    {
                        ProcessReceivedMessage(message, source);
                    });
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

            newsocket.BeginReceive(new AsyncCallback(OnUadpReceive), newsocket);
        }

        /// <summary>
        /// Read All bytes from a given stream
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        private byte[] ReadBytes(Stream stream)
        {
            stream.Position = 0;
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
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
