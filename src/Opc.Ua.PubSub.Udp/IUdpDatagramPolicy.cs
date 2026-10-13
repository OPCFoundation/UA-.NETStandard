/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
