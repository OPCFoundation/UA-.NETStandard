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
using System.Net.Sockets;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Transport
{
    /// <summary>
    /// Class responsible to manage the UDP Discovery Request/Response messages for a <see cref="UdpPubSubConnection"/> entity as a subscriber.
    /// </summary>
    internal class UdpDiscoverySubscriber : UdpDiscovery
    {
        private const int kInitialRequestInterval = 5000;

        /// <summary>
        /// The list that will store the WriterIds that shall be included in a DataSetMetaData Request message
        /// </summary>
        private readonly List<ushort> m_metadataWriterIdsToSend;

        /// <summary>
        /// the component that triggers the publish request messages
        /// </summary>
        private readonly IntervalRunner m_intervalRunner;

        /// <summary>
        /// Create new instance of <see cref="UdpDiscoverySubscriber"/>
        /// </summary>
        public UdpDiscoverySubscriber(UdpPubSubConnection udpConnection)
            : base(udpConnection)
        {
            m_metadataWriterIdsToSend = [];

            m_intervalRunner = new IntervalRunner(
                udpConnection.PubSubConnectionConfiguration.Name,
                kInitialRequestInterval,
                CanPublish,
                RequestDiscoveryMessages);
        }

        /// <summary>
        /// Implementation of StartAsync for the subscriber Discovery
        /// </summary>
        /// <param name="messageContext">The <see cref="IServiceMessageContext"/> object that should be used in encode/decode messages</param>
        public override async Task StartAsync(IServiceMessageContext messageContext)
        {
            await base.StartAsync(messageContext).ConfigureAwait(false);

            m_intervalRunner.Start();
        }

        /// <summary>
        /// Stop the UdpDiscovery process for Subscriber
        /// </summary>
        public override async Task StopAsync()
        {
            await base.StopAsync().ConfigureAwait(false);

            m_intervalRunner.Stop();
        }

        /// <summary>
        /// Enqueue the specified DataSetWriterId for DataSetInformation to be requested
        /// </summary>
        public void AddWriterIdForDataSetMetadata(ushort writerId)
        {
            lock (Lock)
            {
                if (!m_metadataWriterIdsToSend.Contains(writerId))
                {
                    m_metadataWriterIdsToSend.Add(writerId);
                }
            }
        }

        /// <summary>
        /// Removes the specified DataSetWriterId for DataSetInformation to be requested
        /// </summary>
        public void RemoveWriterIdForDataSetMetadata(ushort writerId)
        {
            lock (Lock)
            {
                m_metadataWriterIdsToSend.Remove(writerId);
            }
        }

        /// <summary>
        /// Send a discovery Request for DataSetWriterConfiguration
        /// </summary>
        public void SendDiscoveryRequestDataSetWriterConfiguration()
        {
            ushort[] dataSetWriterIds = m_udpConnection
                .PubSubConnectionConfiguration.ReaderGroups?
                .SelectMany(group => group.DataSetReaders)?
                .Select(group => group.DataSetWriterId)?
                .ToArray();

            var discoveryRequestDataSetWriterConfiguration = new UadpNetworkMessage(
                UADPNetworkMessageDiscoveryType.DataSetWriterConfiguration)
            {
                DataSetWriterIds = dataSetWriterIds,
                PublisherId = m_udpConnection.PubSubConnectionConfiguration.PublisherId.Value
            };

            byte[] bytes = discoveryRequestDataSetWriterConfiguration.Encode(MessageContext);

            // send the Discovery request message to all open UADPClient
            foreach (UdpClient udpClient in m_discoveryUdpClients)
            {
                try
                {
                    Utils.Trace("UdpDiscoverySubscriber.SendDiscoveryRequestDataSetWriterConfiguration message");
                    udpClient.Send(bytes, bytes.Length, DiscoveryNetworkAddressEndPoint);
                }
                catch (Exception ex)
                {
                    Utils.Trace(
                        ex,
                        "UdpDiscoverySubscriber.SendDiscoveryRequestDataSetWriterConfiguration");
                }
            }

            // double the time between requests
            m_intervalRunner.Interval *= 2;
        }

        /// <summary>
        /// Updates the dataset writer configuration
        /// </summary>
        /// <param name="writerConfig">the configuration</param>
        public void UpdateDataSetWriterConfiguration(WriterGroupDataType writerConfig)
        {
            WriterGroupDataType writerGroup = m_udpConnection.PubSubConnectionConfiguration
                .WriterGroups?
                .Find(x =>
                    x.WriterGroupId == writerConfig.WriterGroupId);
            if (writerGroup != null)
            {
                int index = m_udpConnection.PubSubConnectionConfiguration.WriterGroups
                    .IndexOf(writerGroup);
                m_udpConnection.PubSubConnectionConfiguration.WriterGroups[index] = writerConfig;
            }
        }

        /// <summary>
        /// Send a discovery Request for PublisherEndpoints
        /// </summary>
        public void SendDiscoveryRequestPublisherEndpoints()
        {
            var discoveryRequestPublisherEndpoints = new UadpNetworkMessage(
                UADPNetworkMessageDiscoveryType.PublisherEndpoint)
            {
                PublisherId = m_udpConnection.PubSubConnectionConfiguration.PublisherId.Value
            };

            byte[] bytes = discoveryRequestPublisherEndpoints.Encode(MessageContext);

            // send the PublisherEndpoints DiscoveryRequest message to all open UdpClients
            foreach (UdpClient udpClient in m_discoveryUdpClients)
            {
                try
                {
                    Utils.Trace(
                        "UdpDiscoverySubscriber.SendDiscoveryRequestPublisherEndpoints message for PublisherId: {0}",
                        discoveryRequestPublisherEndpoints.PublisherId);

                    udpClient.Send(bytes, bytes.Length, DiscoveryNetworkAddressEndPoint);
                }
                catch (Exception ex)
                {
                    Utils.Trace(
                        ex,
                        "UdpDiscoverySubscriber.SendDiscoveryRequestPublisherEndpoints");
                }
            }

            // double the time between requests
            m_intervalRunner.Interval *= 2;
        }

        /// <summary>
        /// Create and Send the DiscoveryRequest messages for DataSetMetaData
        /// </summary>
        public void SendDiscoveryRequestDataSetMetaData()
        {
            ushort[] dataSetWriterIds = null;
            lock (Lock)
            {
                dataSetWriterIds = [.. m_metadataWriterIdsToSend];
                m_metadataWriterIdsToSend.Clear();
            }

            if (dataSetWriterIds == null || dataSetWriterIds.Length == 0)
            {
                return;
            }

            // create the DataSetMetaData DiscoveryRequest message
            var discoveryRequestMetaDataMessage = new UadpNetworkMessage(
                UADPNetworkMessageDiscoveryType.DataSetMetaData)
            {
                DataSetWriterIds = dataSetWriterIds,
                PublisherId = m_udpConnection.PubSubConnectionConfiguration.PublisherId.Value
            };

            byte[] bytes = discoveryRequestMetaDataMessage.Encode(MessageContext);

            // send the DataSetMetaData DiscoveryRequest message to all open UDPClient
            foreach (UdpClient udpClient in m_discoveryUdpClients)
            {
                try
                {
                    Utils.Trace(
                        "UdpDiscoverySubscriber.SendDiscoveryRequestDataSetMetaData Before sending message for DataSetWriterIds:{0}",
                        string.Join(", ", dataSetWriterIds));

                    udpClient.Send(bytes, bytes.Length, DiscoveryNetworkAddressEndPoint);
                }
                catch (Exception ex)
                {
                    Utils.Trace(ex, "UdpDiscoverySubscriber.SendDiscoveryRequestDataSetMetaData");
                }
            }

            // double the time between requests
            m_intervalRunner.Interval *= 2;
        }

        /// <summary>
        /// Decide if there is anything to publish
        /// </summary>
        private bool CanPublish()
        {
            lock (Lock)
            {
                if (m_metadataWriterIdsToSend.Count == 0)
                {
                    // reset the interval for publisher if there is nothing to send
                    m_intervalRunner.Interval = kInitialRequestInterval;
                }

                return m_metadataWriterIdsToSend.Count > 0;
            }
        }

        /// <summary>
        /// Joint task to request discovery messages
        /// </summary>
        private void RequestDiscoveryMessages()
        {
            SendDiscoveryRequestDataSetMetaData();
            SendDiscoveryRequestDataSetWriterConfiguration();
        }
    }
}
