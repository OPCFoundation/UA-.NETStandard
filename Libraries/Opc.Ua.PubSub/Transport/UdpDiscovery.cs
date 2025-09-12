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

using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Transport
{
    /// <summary>
    /// Class responsible to manage the UDP Discovery Request/Response messages for a <see cref="UdpPubSubConnection"/> entity.
    /// </summary>
    internal abstract class UdpDiscovery
    {
        private const string kDefaultDiscoveryUrl = "opc.udp://224.0.2.14:4840";

        protected UdpPubSubConnection m_udpConnection;
        protected List<UdpClient> m_discoveryUdpClients;

        /// <summary>
        /// Create new instance of <see cref="UdpDiscovery"/>
        /// </summary>
        protected UdpDiscovery(UdpPubSubConnection udpConnection, ITelemetryContext telemetry, ILogger logger)
        {
            m_udpConnection = udpConnection;
            Telemetry = telemetry;
            Logger = logger;

            Initialize();
        }

        /// <summary>
        /// Get the Discovery <see cref="IPEndPoint"/> from <see cref="PubSubConnectionDataType"/>.TransportSettings.
        /// </summary>
        public IPEndPoint DiscoveryNetworkAddressEndPoint { get; private set; }

        /// <summary>
        /// Get the discovery NetworkInterface name from <see cref="PubSubConnectionDataType"/>.TransportSettings.
        /// </summary>
        public string DiscoveryNetworkInterfaceName { get; set; }

        /// <summary>
        /// Get the corresponding <see cref="IServiceMessageContext"/>
        /// </summary>
        public IServiceMessageContext MessageContext { get; private set; }

        protected ILogger Logger { get; }
        protected Lock Lock { get; } = new();
        protected ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Start the UdpDiscovery process
        /// </summary>
        /// <param name="messageContext">The <see cref="IServiceMessageContext"/> object that should be used in encode/decode messages</param>
        public virtual async Task StartAsync(IServiceMessageContext messageContext)
        {
            await Task.Run(() =>
                {
                    lock (Lock)
                    {
                        MessageContext = messageContext;

                        // initialize Discovery channels
                        m_discoveryUdpClients = UdpClientCreator.GetUdpClients(
                            UsedInContext.Discovery,
                            DiscoveryNetworkInterfaceName,
                            DiscoveryNetworkAddressEndPoint,
                            Telemetry,
                            Logger);
                    }
                })
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Start the UdpDiscovery process
        /// </summary>
        public virtual async Task StopAsync()
        {
            lock (Lock)
            {
                if (m_discoveryUdpClients != null && m_discoveryUdpClients.Count > 0)
                {
                    foreach (UdpClient udpClient in m_discoveryUdpClients)
                    {
                        udpClient.Close();
                        udpClient.Dispose();
                    }
                    m_discoveryUdpClients.Clear();
                }
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <summary>
        /// Initialize Connection properties from connection configuration object
        /// </summary>
        private void Initialize()
        {
            PubSubConnectionDataType pubSubConnectionConfiguration = m_udpConnection
                .PubSubConnectionConfiguration;

            if (ExtensionObject.ToEncodeable(pubSubConnectionConfiguration.TransportSettings)
                    is DatagramConnectionTransportDataType transportSettings &&
                transportSettings.DiscoveryAddress != null &&
                ExtensionObject.ToEncodeable(transportSettings.DiscoveryAddress)
                    is NetworkAddressUrlDataType discoveryNetworkAddressUrlState)
            {
                Logger.LogInformation(
                    "The configuration for connection {Name} has custom DiscoveryAddress configuration.",
                    pubSubConnectionConfiguration.Name);

                DiscoveryNetworkInterfaceName = discoveryNetworkAddressUrlState.NetworkInterface;
                DiscoveryNetworkAddressEndPoint = UdpClientCreator.GetEndPoint(
                    discoveryNetworkAddressUrlState.Url,
                    Logger);
            }

            if (DiscoveryNetworkAddressEndPoint == null)
            {
                Logger.LogInformation(
                    "The configuration for connection {Name} will use the default DiscoveryAddress: {DiscoveryUrl}.",
                    pubSubConnectionConfiguration.Name,
                    kDefaultDiscoveryUrl);

                DiscoveryNetworkAddressEndPoint = UdpClientCreator.GetEndPoint(
                    kDefaultDiscoveryUrl,
                    Logger);
            }
        }
    }
}
