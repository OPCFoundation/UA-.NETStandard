// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.PubSub.Udp
{
    /// <summary>
    /// Classifies the kind of IP destination the
    /// <see cref="UdpEndpointParser"/> extracted from an
    /// <c>opc.udp://</c> URL. The classification drives socket-option
    /// selection at <see cref="UdpDatagramTransport"/> open time
    /// (multicast group join, broadcast flag, unicast connect).
    /// </summary>
    /// <remarks>
    /// Implements the address-class branching for
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.2.2">
    /// Part 14 §7.3.2.2 UDP multicast / broadcast</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.2.3">
    /// Part 14 §7.3.2.3 UDP unicast</see>.
    /// </remarks>
    public enum UdpAddressType
    {
        /// <summary>
        /// Unicast destination (host-local or routable single host).
        /// </summary>
        Unicast,

        /// <summary>
        /// IPv4 multicast (<c>224.0.0.0/4</c>) or IPv6 multicast
        /// (<c>ff00::/8</c>) group address.
        /// </summary>
        Multicast,

        /// <summary>
        /// IPv4 limited broadcast address (<c>255.255.255.255</c>).
        /// </summary>
        Broadcast,

        /// <summary>
        /// IPv4 directed (subnet) broadcast address — the last host
        /// address in a /24 or coarser subnet, recognised by the
        /// trailing <c>.255</c> octet. IPv6 has no broadcast concept;
        /// such addresses are always classified as
        /// <see cref="Multicast"/> or <see cref="Unicast"/>.
        /// </summary>
        SubnetBroadcast
    }
}
