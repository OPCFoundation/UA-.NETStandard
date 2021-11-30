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

        // The list that will store the WriterIds that shall be included in a DataSetMetaData Request message
        private readonly List<UInt16> m_metadataWriterIdsToSend;

        // the component that triggers the publish request messages
        private readonly IntervalRunner m_intervalRunner;

        #region Constructor
        /// <summary>
        /// Create new instance of <see cref="UdpDiscoverySubscriber"/>
        /// </summary>
        /// <param name="udpConnection"></param>
        public UdpDiscoverySubscriber(UdpPubSubConnection udpConnection) : base(udpConnection)
        {
            m_metadataWriterIdsToSend = new List<ushort>();

            m_intervalRunner = new IntervalRunner(udpConnection.PubSubConnectionConfiguration.Name,
                kInitialRequestInterval, CanPublish, SendDiscoveryRequestDataSetMetaData);

        }
        #endregion

        #region Start/Stop Method Overides
        
        /// <summary>
        /// Implementation of StartAsync for the subscriber Discovery
        /// </summary>
        /// <param name="messageContext">The <see cref="IServiceMessageContext"/> object that should be used in encode/decode messages</param>
        /// <returns></returns>
        public override async Task StartAsync(IServiceMessageContext messageContext)
        {
            await base.StartAsync(messageContext).ConfigureAwait(false);

            m_intervalRunner.Start();
        }

        /// <summary>
        /// Stop the UdpDiscovery process for Subscriber
        /// </summary>
        /// <returns></returns>
        public override async Task StopAsync()
        {
            await base.StopAsync().ConfigureAwait(false);

            m_intervalRunner.Stop();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Enqueue the specified DataSetWriterId for DataSetInformation to be requested 
        /// </summary>
        /// <param name="writerId"></param>
        public void AddWriterIdForDataSetMetadata(UInt16 writerId)
        {
            lock (m_lock)
            {
                if (!m_metadataWriterIdsToSend.Contains(writerId))
                {
                    m_metadataWriterIdsToSend.Add(writerId);                    
                }
            }
        }

        /// <summary>
        /// Removes the specfoed DataSetWriterId for DataSetInformation to be requested 
        /// </summary>
        /// <param name="writerId"></param>
        public void RemoveWriterIdForDataSetMetadata(UInt16 writerId)
        {
            lock (m_lock)
            {
                if (m_metadataWriterIdsToSend.Contains(writerId))
                {
                    m_metadataWriterIdsToSend.Remove(writerId);
                }
            }
        }
        #endregion

        #region Private Methods
        
        /// <summary>
        /// Decide if there is anything to publish
        /// </summary>
        /// <returns></returns>
        private bool CanPublish()
        {
            lock (m_lock)
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
        /// Create and Send the DiscoveryRequest messages for DataSetMetaData
        /// </summary>
        private void SendDiscoveryRequestDataSetMetaData()
        {
            UInt16[] dataSetWriterIds = null;
            lock (m_lock)
            {
                dataSetWriterIds = m_metadataWriterIdsToSend.ToArray();
                m_metadataWriterIdsToSend.Clear();
            }

            if (dataSetWriterIds == null || dataSetWriterIds.Length == 0)
            {
                return;
            }

            // create the DataSetMetaData DiscoveryRequest message
            UadpNetworkMessage discoveryRequestMetaDataMessage = new UadpNetworkMessage(UADPNetworkMessageDiscoveryType.DataSetMetaData) {
                DataSetWriterIds = dataSetWriterIds,
                PublisherId = m_udpConnection.PubSubConnectionConfiguration.PublisherId.Value,
            };

            byte[] bytes = discoveryRequestMetaDataMessage.Encode(MessageContext);

            // send the DataSetMetaData DiscoveryRequest message to all open UDPClient 
            foreach (var udpClient in m_discoveryUdpClients)
            {
                try
                {
                    Utils.Trace("UdpDiscoverySubscriber.SendDiscoveryRequestDataSetMetaData Before sending message for DataSetWriterIds:{0}",
                        String.Join(", ", dataSetWriterIds));

                    udpClient.Send(bytes, bytes.Length, DiscoveryNetworkAddressEndPoint);                   
                }
                catch (Exception ex)
                {
                    Utils.Trace(ex, "UdpDiscoverySubscriber.SendDiscoveryRequestDataSetMetaData");
                }
            }

            // double the time between requests
            m_intervalRunner.Interval = m_intervalRunner.Interval * 2;
        }
        #endregion
    }
}
