/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Net;
using System.Net.NetworkInformation;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Udp
{
    /// <summary>
    /// Optional host authority for every UDP datagram, including discovery and DTLS control traffic.
    /// Implementations capture authority before activation and must support background callbacks.
    /// </summary>
    public interface IUdpDatagramPolicy
    {
        /// <summary>
        /// Gets whether implicit membership of the standard discovery group is permitted.
        /// </summary>
        bool AllowStandardDiscovery { get; }

        /// <summary>
        /// Refuses an endpoint, direction or selected interface before creating a socket.
        /// </summary>
        void RequireOpen(UdpEndpoint endpoint, PubSubTransportDirection direction, NetworkInterface? networkInterface);

        /// <summary>
        /// Refuses an actual destination or frame before a send or retransmission.
        /// </summary>
        void RequireSend(IPEndPoint destination, int payloadLength);

        /// <summary>
        /// Filters source addresses, ports, receiving interfaces and sizes before allocating a buffered frame.
        /// </summary>
        bool AcceptReceive(IPEndPoint source, int interfaceIndex, int payloadLength);
    }
}
