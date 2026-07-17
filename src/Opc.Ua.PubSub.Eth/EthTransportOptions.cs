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

namespace Opc.Ua.PubSub.Eth
{
    /// <summary>
    /// Tunables for the Ethernet (Layer 2) datagram transport.
    /// <c>IConfiguration</c>-bindable so the DI surface can load defaults
    /// from <c>OpcUa:PubSub:Eth</c>.
    /// </summary>
    /// <remarks>
    /// Implements the transport parameters of the OPC UA Part 14 Ethernet
    /// mapping. The defaults favour standard (non-jumbo) frames and
    /// untagged traffic; VLAN identifiers, priority, and discovery are
    /// opt-in.
    /// </remarks>
    public sealed class EthTransportOptions
    {
        /// <summary>
        /// Bounded capacity of the internal channel that buffers frames
        /// between the receive backend and the <c>ReceiveAsync</c>
        /// consumer. Defaults to 1024 frames.
        /// </summary>
        public int ReceiveQueueCapacity { get; set; } = 1024;

        /// <summary>
        /// Maximum accepted frame size in octets. Frames larger than the
        /// configured maximum are dropped. Defaults to 1522 (standard
        /// Ethernet payload plus the 802.1Q tag).
        /// </summary>
        public int MaxFrameSize { get; set; } = 1522;

        /// <summary>
        /// Preferred network interface — a NIC name matched against
        /// <c>NetworkInterface.Name</c> / <c>NetworkInterface.Description</c>.
        /// When <see langword="null"/> or empty the transport falls back
        /// to the standard <c>NetworkAddressUrlDataType.NetworkInterface</c>
        /// field of the connection address.
        /// </summary>
        public string? PreferredNetworkInterface { get; set; }

        /// <summary>
        /// Default IEEE 802.1Q VLAN identifier (0-4095) applied to
        /// outbound frames when the address URL does not specify one, or
        /// <see langword="null"/> to send untagged.
        /// </summary>
        public ushort? DefaultVlanId { get; set; }

        /// <summary>
        /// Default IEEE 802.1Q priority code point (0-7) applied to
        /// outbound frames when the address URL does not specify one, or
        /// <see langword="null"/>.
        /// </summary>
        public byte? DefaultPriority { get; set; }

        /// <summary>
        /// Whether to place the interface in promiscuous mode so it
        /// receives frames not addressed to its own MAC. Disabled by
        /// default; multicast destinations are received through group
        /// membership without it.
        /// </summary>
        public bool Promiscuous { get; set; }

        /// <summary>
        /// Periodic discovery announcement rate in milliseconds. A value
        /// of zero (the default) disables cyclic announcements.
        /// </summary>
        public uint DiscoveryAnnounceRate { get; set; }

        /// <summary>
        /// Destination MAC address (e.g. <c>01-1B-19-00-00-00</c>) for
        /// discovery announcements. When <see langword="null"/> the
        /// transport sends announcements to the configured data
        /// destination MAC.
        /// </summary>
        public string? DiscoveryMulticastAddress { get; set; }
    }
}
