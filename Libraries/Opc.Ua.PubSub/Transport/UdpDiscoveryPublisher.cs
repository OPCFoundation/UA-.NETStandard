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
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Transport
{
    /// <summary>
    /// Class responsible to manage the UDP Discovery Request/Response messages for a <see cref="UdpPubSubConnection"/> entity as a publisher.
    /// </summary>
    internal class UdpDiscoveryPublisher : UdpDiscovery
    {
        /// <summary>
        /// Minimum response interval
        /// </summary>
        private const int kMinimumResponseInterval = 500;

        /// <summary>
        /// The list that will store the WriterIds that shall be set as DataSetMetaData Response message
        /// </summary>
        private readonly List<ushort> m_metadataWriterIdsToSend;

        /// <summary>
        /// Create new instance of <see cref="UdpDiscoveryPublisher"/>
        /// </summary>
        public UdpDiscoveryPublisher(UdpPubSubConnection udpConnection, ITelemetryContext telemetry)
            : base(udpConnection, telemetry, telemetry.CreateLogger<UdpDiscoveryPublisher>())
        {
            m_metadataWriterIdsToSend = [];
        }

        /// <summary>
        /// Implementation of StartAsync for the Publisher Discovery
        /// </summary>
        /// <param name="messageContext">The <see cref="IServiceMessageContext"/> object that should be used in encode/decode messages</param>
        public override async Task StartAsync(IServiceMessageContext messageContext)
        {
            await base.StartAsync(messageContext).ConfigureAwait(false);

            if (m_discoveryUdpClients != null)
            {
                foreach (UdpClient discoveryUdpClient in m_discoveryUdpClients)
                {
                    try
                    {
                        // attach callback for receiving messages
                        discoveryUdpClient.BeginReceive(OnUadpDiscoveryReceive, discoveryUdpClient);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogInformation(
                            "UdpDiscoveryPublisher: UdpClient '{Endpoint}' Cannot receive data. Exception: {Message}",
                            discoveryUdpClient.Client.LocalEndPoint,
                            ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Handle Receive event for an UADP channel on Discovery channel
        /// </summary>
        private void OnUadpDiscoveryReceive(IAsyncResult result)
        {
            // this is what had been passed into BeginReceive as the second parameter:
            if (result.AsyncState is not UdpClient socket)
            {
                return;
            }

            // points towards whoever had sent the message:
            var source = new IPEndPoint(0, 0);
            // get the actual message and fill out the source:
            try
            {
                byte[] message = socket.EndReceive(result, ref source);

                if (message != null)
                {
                    Logger.LogInformation(
                        "OnUadpDiscoveryReceive received message with length {Length} from {Address}",
                        message.Length,
                        source.Address);

                    if (message.Length > 1)
                    {
                        // call on a new thread
                        Task.Run(() => ProcessReceivedMessageDiscovery(message, source));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "OnUadpDiscoveryReceive from {Address}", source.Address);
            }

            try
            {
                // schedule the next receive operation once reading is done:
                socket.BeginReceive(OnUadpDiscoveryReceive, socket);
            }
            catch (Exception ex)
            {
                Logger.LogInformation(
                    "OnUadpDiscoveryReceive BeginReceive threw Exception {Message}",
                    ex.Message);

                lock (Lock)
                {
                    Renew(socket);
                }
            }
        }

        /// <summary>
        /// Process the bytes received from UADP discovery channel
        /// </summary>
        private void ProcessReceivedMessageDiscovery(byte[] messageBytes, IPEndPoint source)
        {
            Logger.LogInformation(
                "UdpDiscoveryPublisher.ProcessReceivedMessageDiscovery from source={Source}",
                source);

            var networkMessage = new UadpNetworkMessage(Logger);
            // decode the received message
            networkMessage.Decode(MessageContext, messageBytes, null);

            if (networkMessage.UADPNetworkMessageType == UADPNetworkMessageType.DiscoveryRequest &&
                networkMessage
                    .UADPDiscoveryType == UADPNetworkMessageDiscoveryType.DataSetMetaData &&
                networkMessage.DataSetWriterIds != null)
            {
                Logger.LogInformation(
                    "UdpDiscoveryPublisher.ProcessReceivedMessageDiscovery Request MetaData Received on endpoint {Address} for {DataSetWriterIds}",
                    source.Address,
                    string.Join(", ", networkMessage.DataSetWriterIds));

                foreach (ushort dataSetWriterId in networkMessage.DataSetWriterIds)
                {
                    lock (Lock)
                    {
                        if (!m_metadataWriterIdsToSend.Contains(dataSetWriterId))
                        {
                            // collect requested ids
                            m_metadataWriterIdsToSend.Add(dataSetWriterId);
                        }
                    }
                }

                Task.Run(SendResponseDataSetMetaDataAsync).ConfigureAwait(false);
            }
            else if (networkMessage
                .UADPNetworkMessageType == UADPNetworkMessageType.DiscoveryRequest &&
                networkMessage
                    .UADPDiscoveryType == UADPNetworkMessageDiscoveryType.PublisherEndpoint)
            {
                Task.Run(SendResponsePublisherEndpointsAsync).ConfigureAwait(false);
            }
            else if (networkMessage
                .UADPNetworkMessageType == UADPNetworkMessageType.DiscoveryRequest &&
                networkMessage.UADPDiscoveryType ==
                UADPNetworkMessageDiscoveryType.DataSetWriterConfiguration &&
                networkMessage.DataSetWriterIds != null)
            {
                Task.Run(SendResponseDataSetWriterConfigurationAsync).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Sends a DataSetMetaData discovery response message
        /// </summary>
        private async Task SendResponseDataSetMetaDataAsync()
        {
            await Task.Delay(kMinimumResponseInterval).ConfigureAwait(false);
            lock (Lock)
            {
                if (m_metadataWriterIdsToSend.Count > 0)
                {
                    foreach (
                        UaNetworkMessage message in m_udpConnection
                            .CreateDataSetMetaDataNetworkMessages(
                                [.. m_metadataWriterIdsToSend]))
                    {
                        Logger.LogInformation(
                            "UdpDiscoveryPublisher.SendResponseDataSetMetaData before sending message for DataSetWriterId:{DataSetWriterId}",
                            message.DataSetWriterId);

                        m_udpConnection.PublishNetworkMessage(message);
                    }
                    m_metadataWriterIdsToSend.Clear();
                }
            }
        }

        /// <summary>
        /// Sends a DataSetWriterConfiguration discovery response message
        /// </summary>
        private async Task SendResponseDataSetWriterConfigurationAsync()
        {
            await Task.Delay(kMinimumResponseInterval).ConfigureAwait(false);
            lock (Lock)
            {
                IList<ushort> dataSetWriterIdsToSend = [];
                if (GetDataSetWriterIds != null)
                {
                    dataSetWriterIdsToSend = GetDataSetWriterIds.Invoke(
                        m_udpConnection.Application);
                }

                if (dataSetWriterIdsToSend.Count > 0)
                {
                    IList<UaNetworkMessage> responsesMessages = m_udpConnection
                        .CreateDataSetWriterCofigurationMessage(
                            [.. dataSetWriterIdsToSend]);

                    foreach (UaNetworkMessage responsesMessage in responsesMessages)
                    {
                        Logger.LogInformation(
                            "UdpDiscoveryPublisher.SendResponseDataSetWriterConfiguration Before sending message for DataSetWriterId:{DataSetWriterId}",
                            responsesMessage.DataSetWriterId);

                        m_udpConnection.PublishNetworkMessage(responsesMessage);
                    }
                }
            }
        }

        /// <summary>
        ///  Send response PublisherEndpoints
        /// </summary>
        private async Task SendResponsePublisherEndpointsAsync()
        {
            await Task.Delay(kMinimumResponseInterval).ConfigureAwait(false);

            lock (Lock)
            {
                IList<EndpointDescription> publisherEndpointsToSend = [];
                if (GetPublisherEndpoints != null)
                {
                    publisherEndpointsToSend = GetPublisherEndpoints.Invoke();
                }

                UaNetworkMessage message = m_udpConnection.CreatePublisherEndpointsNetworkMessage(
                    [.. publisherEndpointsToSend],
                    publisherEndpointsToSend.Count > 0 ? StatusCodes.Good : StatusCodes.BadNotFound,
                    m_udpConnection.PubSubConnectionConfiguration.PublisherId.Value);

                Logger.LogInformation(
                    "UdpDiscoveryPublisher.SendResponsePublisherEndpoints before sending message for PublisherEndpoints.");

                m_udpConnection.PublishNetworkMessage(message);
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
                newsocket = new UdpClientMulticast(
                    mcastSocket.Address,
                    mcastSocket.MulticastAddress,
                    mcastSocket.Port,
                    Telemetry);
            }
            else if (socket is UdpClientBroadcast bcastSocket)
            {
                newsocket = new UdpClientBroadcast(
                    bcastSocket.Address,
                    bcastSocket.Port,
                    bcastSocket.PubSubContext,
                    Telemetry);
            }
            else if (socket is UdpClientUnicast ucastSocket)
            {
                newsocket = new UdpClientUnicast(
                    ucastSocket.Address,
                    ucastSocket.Port,
                    Telemetry);
            }
            m_discoveryUdpClients.Remove(socket);
            m_discoveryUdpClients.Add(newsocket);
            socket.Close();
            socket.Dispose();

            newsocket?.BeginReceive(OnUadpDiscoveryReceive, newsocket);
        }

        /// <summary>
        /// The GetPublisherEndpoints event callback reference to store the EndpointDescription[] to be set as PublisherEndpoints Response message
        /// </summary>
        public GetPublisherEndpointsEventHandler GetPublisherEndpoints { get; set; }

        /// <summary>
        ///  The GetDataSetWriterIds event callback reference to store the DataSetWriter ids to be set as PublisherEndpoints Response message
        /// </summary>
        public GetDataSetWriterIdsEventHandler GetDataSetWriterIds { get; set; }
    }
}
