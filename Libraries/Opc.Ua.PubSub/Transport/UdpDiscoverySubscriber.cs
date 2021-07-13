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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Transport
{
    /// <summary>
    /// Class responsible to manage the UDP Discovery Request/Response messages for a <see cref="UdpPubSubConnection"/> entity as a subscriber.
    ///
    /// TODO timer based triggering mechanism shall be implemented
    /// </summary>
    internal class UdpDiscoverySubscriber : UdpDiscovery
    {
        private Timer m_sendRequestsTimer;
        private int m_requestInterval = 5000;

        #region Constructor
        /// <summary>
        /// Create new instance of <see cref="UdpDiscoverySubscriber"/>
        /// </summary>
        /// <param name="udpConnection"></param>
        public UdpDiscoverySubscriber(UdpPubSubConnection udpConnection) : base(udpConnection)
        {

        }
        #endregion

        /// <summary>
        /// Start the UdpDiscovery process for subscriber
        /// </summary>
        /// <returns></returns>
        public async override Task StartAsync()
        {
            await base.StartAsync();

            lock (m_lock)
            {
                // start timer.
                m_sendRequestsTimer = new Timer(OnSendRequestsTimer, null, 0, m_requestInterval);
            }
        }

        /// <summary>
        /// Stop the UdpDiscovery process for Subscriber
        /// </summary>
        /// <returns></returns>
        public async override Task StopAsync()
        {
            await base.StopAsync();

            lock (m_lock)
            {
                m_sendRequestsTimer.Dispose();
            }
        }

        /// <summary>
        /// sendRequest tiemr Tick handler  
        /// </summary>
        /// <param name="state"></param>
        private void OnSendRequestsTimer(object state)
        {
            // check if there are dataSetReaders that need metadata
            List<UInt16> dataSetWriterIds = new List<ushort>();
            var operationalDataSetReaders = m_udpConnection.GetOperationalDataSetReaders();

            foreach (DataSetReaderDataType dataSetReader in operationalDataSetReaders)
            {
                if (dataSetReader.DataSetWriterId != 0
                    && (dataSetReader.DataSetMetaData == null || dataSetReader.DataSetMetaData.Fields.Count == 0) )
                {
                    dataSetWriterIds.Add(dataSetReader.DataSetWriterId);
                }
            }

            if (dataSetWriterIds.Count > 0)
            {
                SendDiscoveryRequestDataSetMetaData(dataSetWriterIds.ToArray());
            }
        }

        /// <summary>
        /// Create and Send the DiscoveryRequest messages for DataSetMetaData
        /// </summary>
        /// <param name="dataSetWriterIds">The dataSetWriterId list for the DataSetWriteres that have unknown metadata or the ConfigVersion does not match anymore.</param>
        public void SendDiscoveryRequestDataSetMetaData(UInt16[] dataSetWriterIds)
        {
            if (dataSetWriterIds == null)
            {
                return;
            }

            UadpNetworkMessage discoveryRequestMetaDataMessage = new UadpNetworkMessage(UADPNetworkMessageDiscoveryType.DataSetMetaData) {
                DataSetWriterIds = dataSetWriterIds,
                PublisherId = m_udpConnection.PubSubConnectionConfiguration.PublisherId.Value,
            };

            byte[] bytes = discoveryRequestMetaDataMessage.Encode();

            foreach (var udpClient in m_discoveryUdpClients)
            {
                try
                {
                    udpClient.Send(bytes, bytes.Length, DiscoveryNetworkAddressEndPoint);
                }
                catch (Exception ex)
                {
                    Utils.Trace(ex, "UdpDiscoverySubscriber.SendDiscoveryRequestDataSetMetaData");
                }
            }
        }

    }
}
