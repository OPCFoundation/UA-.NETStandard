// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Net;
using System.Net.NetworkInformation;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Udp;

namespace RedundantPubSub
{
    /// <summary>
    /// Keeps the unsigned demo on loopback, including the UDP discovery path.
    /// </summary>
    internal sealed class LoopbackUdpPolicy : IUdpDatagramPolicy
    {
        /// <inheritdoc/>
        public bool AllowStandardDiscovery => false;

        /// <inheritdoc/>
        public void RequireOpen(
            UdpEndpoint endpoint,
            PubSubTransportDirection direction,
            NetworkInterface? networkInterface)
        {
            RequireLoopback(endpoint.Address);
        }

        /// <inheritdoc/>
        public void RequireSend(IPEndPoint destination, int payloadLength)
        {
            RequireLoopback(destination.Address);
        }

        /// <inheritdoc/>
        public bool AcceptReceive(IPEndPoint source, int interfaceIndex, int payloadLength)
        {
            return IPAddress.IsLoopback(source.Address);
        }

        private static void RequireLoopback(IPAddress address)
        {
            if (!IPAddress.IsLoopback(address))
            {
                throw new InvalidOperationException("The unsigned PubSub demo uses loopback datagrams only.");
            }
        }
    }
}
