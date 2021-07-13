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
using System.Text;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Transport
{
    /// <summary>
    /// Class responsible to manage the UDP Discovery Request/Response messages for a <see cref="UdpPubSubConnection"/> entity as a publisher.
    ///
    /// TODO timer based triggering mechanism shall be implemented
    /// </summary>
    internal class UdpDiscoveryPublisher : UdpDiscovery
    {

        #region Constructor
        /// <summary>
        /// Create new instance of <see cref="UdpDiscoveryPublisher"/>
        /// </summary>
        /// <param name="udpConnection"></param>
        public UdpDiscoveryPublisher(UdpPubSubConnection udpConnection) : base(udpConnection)
        {

        }
        #endregion

        /// <summary>
        /// Implementation of StartAsync for the Publisher Discovery
        /// </summary>
        /// <returns></returns>
        public override async Task StartAsync()
        {
            await base.StartAsync();

            if (m_discoveryUdpClients != null)
            {
                foreach (UdpClient discoveryUdpClient in m_discoveryUdpClients)
                {
                    try
                    {
                        // attach callback for receiving messages
                        discoveryUdpClient.BeginReceive(new AsyncCallback(OnUadpDiscoveryReceive), discoveryUdpClient);
                    }
                    catch (Exception ex)
                    {
                        Utils.Trace(Utils.TraceMasks.Information, "UdpDiscoveryPublisher: UdpClient '{0}' Cannot receive data. Exception: {1}",
                          discoveryUdpClient.Client.LocalEndPoint, ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Handle Receive event for an UADP channel on Discovery channel
        /// </summary>
        /// <param name="result"></param>
        private void OnUadpDiscoveryReceive(IAsyncResult result)
        {
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
                    Utils.Trace(Utils.TraceMasks.Information, "OnUadpDiscoveryReceive received message with length {0} from {1}", message.Length, source.Address);

                    if (message.Length > 1)
                    {
                        // call on a new thread
                        Task.Run(() => {
                            ProcessReceivedMessageDiscovery(message, source);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "OnUadpDiscoveryReceive from {0}", source.Address);
            }

            try
            {
                // schedule the next receive operation once reading is done:
                socket.BeginReceive(new AsyncCallback(OnUadpDiscoveryReceive), socket);
            }
            catch (Exception ex)
            {
                Utils.Trace(Utils.TraceMasks.Information, "OnUadpDiscoveryReceive BeginReceive throwed Exception {0}", ex.Message);

                lock (m_lock)
                {
                    Renew(socket);
                }
            }
        }

        /// <summary>
        /// Process the bytes received from UADP discuvery channel 
        /// </summary>
        private void ProcessReceivedMessageDiscovery(byte[] messageBytes, IPEndPoint source)
        {
            Utils.Trace(Utils.TraceMasks.Information, "UdpDiscoveryPublisher.ProcessReceivedMessageDiscovery from source={0}", source);

            UadpNetworkMessage networkMessage = new UadpNetworkMessage();

            IServiceMessageContext context = new ServiceMessageContext {
                NamespaceUris = ServiceMessageContext.GlobalContext.NamespaceUris,
                ServerUris = ServiceMessageContext.GlobalContext.ServerUris
            };

            networkMessage.Decode(context, messageBytes, null);

            // process Decoded Message
            if (m_discoveryUdpClients != null)
            {
                if (networkMessage.UADPNetworkMessageType == UADPNetworkMessageType.DiscoveryRequest
                    && networkMessage.UADPDiscoveryType == UADPNetworkMessageDiscoveryType.DataSetMetaData)
                {
                    // create the respinse data SetMetaData messages
                    IList<UaNetworkMessage> responseMessages = m_udpConnection.CreateNetworkMessageDataSetMetaData(networkMessage.DataSetWriterIds);
                    // todo clarify where sgall be the respnse messages be sent. For now they are sent on the normal communication channel not on the discovery address                   

                    foreach (UaNetworkMessage message in responseMessages)
                    {
                        m_udpConnection.PublishNetworkMessage(message);
                    }
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
            m_discoveryUdpClients.Remove(socket);
            m_discoveryUdpClients.Add(newsocket);
            socket.Close();
            socket.Dispose();

            if (newsocket != null)
            {
                newsocket.BeginReceive(new AsyncCallback(OnUadpDiscoveryReceive), newsocket);
            }
        }
    }
}
