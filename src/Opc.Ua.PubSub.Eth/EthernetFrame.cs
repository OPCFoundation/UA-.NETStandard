/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Net.NetworkInformation;

namespace Opc.Ua.PubSub.Eth
{
    /// <summary>
    /// A parsed inbound OPC UA Ethernet frame: the NetworkMessage
    /// payload plus the addressing and 802.1Q context decoded by
    /// <see cref="EthernetFrameCodec"/>.
    /// </summary>
    /// <remarks>
    /// Produced by <see cref="EthernetFrameCodec.TryParse(ReadOnlyMemory{byte}, out EthernetFrame)"/>
    /// for the OPC UA Part 14 Ethernet mapping (EtherType 0xB62C).
    /// </remarks>
    /// <param name="Payload">
    /// The NetworkMessage payload (everything after the Ethernet / VLAN
    /// header). May include trailing padding bytes when the on-the-wire
    /// frame was padded to the 60-octet minimum; the UADP decoder stops
    /// at the end of the message and ignores the padding.
    /// </param>
    /// <param name="SourceAddress">The source MAC address.</param>
    /// <param name="DestinationAddress">The destination MAC address.</param>
    /// <param name="VlanId">
    /// The 802.1Q VLAN identifier (0-4095) when the frame was tagged, or
    /// <see langword="null"/> for an untagged frame.
    /// </param>
    /// <param name="Priority">
    /// The 802.1Q Priority Code Point (0-7) when the frame was tagged, or
    /// <see langword="null"/> for an untagged frame.
    /// </param>
    public readonly record struct EthernetFrame(
        ReadOnlyMemory<byte> Payload,
        PhysicalAddress SourceAddress,
        PhysicalAddress DestinationAddress,
        ushort? VlanId,
        byte? Priority);
}
