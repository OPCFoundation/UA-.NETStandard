/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PubSub.Eth
{
    /// <summary>
    /// Classifies the kind of Ethernet destination the
    /// <see cref="EthEndpointParser"/> extracted from an
    /// <c>opc.eth://</c> URL. The classification drives membership /
    /// filter selection at <see cref="EthernetDatagramTransport"/> open
    /// time (multicast group join vs. unicast filtering).
    /// </summary>
    /// <remarks>
    /// Implements the address-class branching for the OPC UA Part 14
    /// Ethernet mapping. The class is derived from the I/G (group) bit
    /// of the most significant octet of the destination MAC address and
    /// the all-ones broadcast address.
    /// </remarks>
    public enum EthAddressType
    {
        /// <summary>
        /// Individual (unicast) destination MAC address — the I/G bit of
        /// the first octet is clear.
        /// </summary>
        Unicast,

        /// <summary>
        /// Group (multicast) destination MAC address — the I/G bit of the
        /// first octet is set and the address is not the all-ones
        /// broadcast address.
        /// </summary>
        Multicast,

        /// <summary>
        /// The Ethernet broadcast address <c>FF-FF-FF-FF-FF-FF</c>.
        /// </summary>
        Broadcast
    }
}
