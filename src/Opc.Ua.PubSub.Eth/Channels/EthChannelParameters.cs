// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Net.NetworkInformation;

namespace Opc.Ua.PubSub.Eth.Channels
{
    /// <summary>
    /// Creation parameters for an <see cref="IEthernetFrameChannel"/>.
    /// Carries the resolved network interface, EtherType filter, and the
    /// receive tunables a backend needs to bind a socket / capture
    /// handle.
    /// </summary>
    public sealed class EthChannelParameters
    {
        /// <summary>
        /// Name of the network interface to bind, or
        /// <see langword="null"/> to let the backend choose. Also used as
        /// the loopback bus key by the in-memory backend.
        /// </summary>
        public string? InterfaceName { get; init; }

        /// <summary>
        /// The resolved network interface, when known. Backends use it to
        /// determine the interface index and source MAC.
        /// </summary>
        public NetworkInterface? NetworkInterface { get; init; }

        /// <summary>
        /// Explicit source MAC override. When <see langword="null"/> the
        /// backend derives it from <see cref="NetworkInterface"/>.
        /// </summary>
        public PhysicalAddress? InterfaceAddress { get; init; }

        /// <summary>
        /// EtherType the channel filters inbound frames on and binds for
        /// receive. Defaults to <see cref="EthernetFrameCodec.OpcUaEtherType"/>.
        /// </summary>
        public ushort EtherType { get; init; } = EthernetFrameCodec.OpcUaEtherType;

        /// <summary>
        /// Optional multicast group MAC to join for receive, or
        /// <see langword="null"/> for unicast / promiscuous receive.
        /// </summary>
        public PhysicalAddress? MulticastGroup { get; init; }

        /// <summary>
        /// Whether to place the interface in promiscuous mode (receive
        /// all frames). Defaults to <see langword="false"/>.
        /// </summary>
        public bool Promiscuous { get; init; }

        /// <summary>
        /// Bounded capacity of the channel's receive queue in frames.
        /// Defaults to 1024.
        /// </summary>
        public int ReceiveQueueCapacity { get; init; } = 1024;

        /// <summary>
        /// Maximum accepted frame size in octets. Frames larger than this
        /// are dropped. Defaults to 1522 (standard Ethernet + 802.1Q).
        /// </summary>
        public int MaxFrameSize { get; init; } = 1522;
    }
}
