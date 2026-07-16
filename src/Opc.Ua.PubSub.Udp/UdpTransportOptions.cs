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

using System;

namespace Opc.Ua.PubSub.Udp
{
    /// <summary>
    /// Tunables for the UDP datagram transport.
    /// <c>IConfiguration</c>-bindable so the DI surface can
    /// load defaults from <c>OpcUa:PubSub:Udp</c>.
    /// </summary>
    /// <remarks>
    /// Implements the datagram transport parameters defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.4.1">
    /// Part 14 §6.4.1 Datagram transport data types</see>. Defaults
    /// favour safety over reach: <see cref="Ttl"/>=1 keeps multicast
    /// traffic on the local subnet, <see cref="MulticastLoopback"/> is
    /// off, and per-frame budgets match the IPv4 datagram payload
    /// maximum of 65 507 bytes.
    /// </remarks>
    public sealed class UdpTransportOptions
    {
        /// <summary>
        /// SO_SNDBUF size in bytes. Defaults to 64 KiB.
        /// </summary>
        public int SendBufferSize { get; set; } = 64 * 1024;

        /// <summary>
        /// SO_RCVBUF size in bytes. Defaults to 256 KiB to absorb
        /// bursty multicast traffic.
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 256 * 1024;

        /// <summary>
        /// Bounded capacity of the internal channel that buffers
        /// frames between the socket loop and the
        /// <c>ReceiveAsync</c> consumer. Defaults to 1024 frames.
        /// </summary>
        public int ReceiveQueueCapacity { get; set; } = 1024;

        /// <summary>
        /// IP TTL / hop-limit applied to outbound datagrams. Defaults
        /// to 1 so multicast traffic does not escape the local LAN
        /// without explicit operator opt-in.
        /// </summary>
        public int Ttl { get; set; } = 1;

        /// <summary>
        /// Whether the publisher receives a loopback copy of its own
        /// multicast traffic. Disabled by default; set to true only
        /// for local diagnostic / loopback tests.
        /// </summary>
        public bool MulticastLoopback { get; set; }

        /// <summary>
        /// Maximum accepted frame size in bytes. Defaults to 65 507
        /// (the UDP datagram payload maximum). Frames larger than the
        /// configured maximum are dropped and counted as
        /// <c>ReceivedInvalidNetworkMessages</c>.
        /// </summary>
        public int MaxFrameSize { get; set; } = 65507;

        /// <summary>
        /// <c>MessageRepeatCount</c> per Part 14 §6.4.1: the number of
        /// times to re-transmit each NetworkMessage in addition to the
        /// initial send. Defaults to 0 (single shot).
        /// </summary>
        public int MessageRepeatCount { get; set; }

        /// <summary>
        /// <c>MessageRepeatDelay</c> per Part 14 §6.4.1: the delay
        /// between successive re-transmissions when
        /// <see cref="MessageRepeatCount"/> is greater than zero.
        /// Defaults to 5 ms.
        /// </summary>
        public TimeSpan MessageRepeatDelay { get; set; } = TimeSpan.FromMilliseconds(5);

        /// <summary>
        /// Preferred network interface — either a NIC name (matched
        /// against <c>NetworkInterface.Name</c> /
        /// <c>NetworkInterface.Description</c>) or a literal IP
        /// address bound to a local NIC. When <see langword="null"/>
        /// or empty the transport picks the first up-and-running
        /// interface that supports the target address family.
        /// </summary>
        public string? PreferredNetworkInterface { get; set; }
    }
}
