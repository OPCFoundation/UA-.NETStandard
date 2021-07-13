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
    /// Class responsible to manage the UDP Discovery Request/Response messages for a <see cref="UdpPubSubConnection"/> entity.
    /// </summary>
    internal abstract class UdpDiscovery
    {
        #region Fields
        private const string kDefalulDiscoveryUrl = "opc.udp://224.0.2.14:4840";

        protected object m_lock = new object();
        protected UdpPubSubConnection m_udpConnection;
        protected List<UdpClient> m_discoveryUdpClients;
        #endregion

        #region Constructors
        /// <summary>
        /// Create new instance of <see cref="UdpDiscovery"/>
        /// </summary>
        /// <param name="udpConnection"></param>
        public UdpDiscovery(UdpPubSubConnection udpConnection)
        {
            m_udpConnection = udpConnection;

            Initialize();
        }
        #endregion

        #region Properties
        /// <summary>
        /// Get the Discovery <see cref="IPEndPoint"/> from <see cref="PubSubConnectionDataType"/>.TransportSettings.
        /// </summary>
        public IPEndPoint DiscoveryNetworkAddressEndPoint { get; private set; }

        /// <summary>
        /// Get the discovery NetworkInterface name from <see cref="PubSubConnectionDataType"/>.TransportSettings.
        /// </summary>
        public string DiscoveryNetworkInterfaceName { get; set; }
        #endregion

        #region Public Methods

        /// <summary>
        /// Start the UdpDiscovery process
        /// </summary>
        /// <returns></returns>
        public async virtual Task StartAsync()
        {
            await Task.Run(() => {

                lock (m_lock)
                {
                    // initialize Discovery channels
                    m_discoveryUdpClients = UdpClientCreator.GetUdpClients(UsedInContext.Discovery, DiscoveryNetworkInterfaceName, DiscoveryNetworkAddressEndPoint);                    
                }
            });
        }

        /// <summary>
        /// Start the UdpDiscovery process
        /// </summary>
        /// <returns></returns>
        public async virtual Task StopAsync()
        {
            lock (m_lock)
            {
                if (m_discoveryUdpClients != null && m_discoveryUdpClients.Count > 0)
                {
                    foreach (var udpClient in m_discoveryUdpClients)
                    {
                        udpClient.Close();
                        udpClient.Dispose();
                    }
                    m_discoveryUdpClients.Clear();
                }
            }

            await Task.CompletedTask;
        }
        #endregion
               

        #region Private Methods
        /// <summary>
        /// Initialize Conection properties from connection configuration object
        /// </summary>
        private void Initialize()
        {
            PubSubConnectionDataType pubSubConnectionConfiguration = m_udpConnection.PubSubConnectionConfiguration;
            DatagramConnectionTransportDataType transportSettings = ExtensionObject.ToEncodeable(pubSubConnectionConfiguration.TransportSettings)
                       as DatagramConnectionTransportDataType;

            if (transportSettings != null && transportSettings.DiscoveryAddress != null)
            {
                NetworkAddressUrlDataType discoveryNetworkAddressUrlState = ExtensionObject.ToEncodeable(transportSettings.DiscoveryAddress)
                       as NetworkAddressUrlDataType;
                if (discoveryNetworkAddressUrlState != null)
                {
                    Utils.Trace(Utils.TraceMasks.Information, "The configuration for connection {0} has custom DiscoveryAddress configuration.",
                              pubSubConnectionConfiguration.Name);

                    DiscoveryNetworkInterfaceName = discoveryNetworkAddressUrlState.NetworkInterface;
                    DiscoveryNetworkAddressEndPoint = UdpClientCreator.GetEndPoint(discoveryNetworkAddressUrlState.Url);
                }                
            }

            if (DiscoveryNetworkAddressEndPoint == null)
            {
                Utils.Trace(Utils.TraceMasks.Information, "The configuration for connection {0} will use the default DiscoveryAddress: {1}.",
                              pubSubConnectionConfiguration.Name, kDefalulDiscoveryUrl);

                DiscoveryNetworkAddressEndPoint = UdpClientCreator.GetEndPoint(kDefalulDiscoveryUrl);
            }
        }

        
        
        #endregion

    }
}
