/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
