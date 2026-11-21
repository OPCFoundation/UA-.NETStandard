// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Net.NetworkInformation;

namespace Opc.Ua.PubSub.Eth
{
    /// <summary>
    /// Parsed <c>opc.eth://</c> endpoint: the destination MAC address,
    /// the optional IEEE 802.1Q VLAN identifier and priority, the
    /// address classification, and the original URL kept for
    /// diagnostics. Produced by <see cref="EthEndpointParser.Parse"/>
    /// and consumed by <see cref="EthPubSubTransportFactory"/> /
    /// <see cref="EthernetDatagramTransport"/>.
    /// </summary>
    /// <remarks>
    /// Implements the addressing model of the OPC UA Part 14 Ethernet
    /// mapping. Designed as a <see langword="readonly"/>
    /// <see langword="record"/> <see langword="struct"/> so callers can
    /// pass it by value. Equality uses <see cref="PhysicalAddress"/>
    /// value semantics (the address byte sequence).
    /// </remarks>
    /// <param name="Address">The destination MAC address.</param>
    /// <param name="VlanId">
    /// The optional 802.1Q VLAN identifier (0-4095), or
    /// <see langword="null"/> when the frame is sent untagged.
    /// </param>
    /// <param name="Priority">
    /// The optional 802.1Q Priority Code Point (0-7), or
    /// <see langword="null"/> when the frame is sent untagged.
    /// </param>
    /// <param name="AddressType">Classification of the destination MAC.</param>
    /// <param name="OriginalUrl">
    /// The original URL string the endpoint was parsed from, kept for
    /// log / diagnostic output. May be <see langword="null"/> when the
    /// endpoint was constructed directly.
    /// </param>
    public readonly record struct EthEndpoint(
        PhysicalAddress Address,
        ushort? VlanId,
        byte? Priority,
        EthAddressType AddressType,
        string? OriginalUrl)
    {
        /// <summary>
        /// Indicates whether the endpoint carries a usable destination
        /// MAC (a non-null six-octet address) and, when present, an
        /// in-range VLAN identifier and priority.
        /// </summary>
        public bool IsValid =>
            Address is not null &&
            Address.GetAddressBytes().Length == 6 &&
            (VlanId is null || VlanId.Value <= 4095) &&
            (Priority is null || Priority.Value <= 7);
    }
}
