// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Net;

namespace Opc.Ua.PubSub.Udp
{
    /// <summary>
    /// Parsed <c>opc.udp://</c> endpoint: the resolved IP address,
    /// port, classification, and the original URL kept for
    /// diagnostics. Produced by <see cref="UdpEndpointParser.Parse"/>
    /// and consumed by <see cref="UdpDatagramTransport"/> at open
    /// time so the socket plumbing does not re-parse strings on the
    /// hot path.
    /// </summary>
    /// <remarks>
    /// Implements the addressing model of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.2">
    /// Part 14 §7.3.2 UDP datagram transport</see>. Designed as a
    /// <see langword="readonly"/> <see langword="record"/>
    /// <see langword="struct"/> so callers can pass it by value
    /// without allocations.
    /// </remarks>
    /// <param name="Address">The resolved <see cref="IPAddress"/>.</param>
    /// <param name="Port">UDP port (1-65535).</param>
    /// <param name="AddressType">Classification of the address.</param>
    /// <param name="OriginalUrl">
    /// The original URL string the endpoint was parsed from, kept for
    /// log / diagnostic output. May be <see langword="null"/> if the
    /// endpoint was constructed directly.
    /// </param>
    /// <param name="IsDtls">
    /// Indicates the endpoint was parsed from <c>opc.dtls://</c> and must use DTLS.
    /// </param>
    /// <param name="DtlsProfileName">
    /// Selected DTLS profile name, or <see langword="null"/> for plain UDP.
    /// </param>
    public readonly record struct UdpEndpoint(
        IPAddress Address,
        int Port,
        UdpAddressType AddressType,
        string? OriginalUrl,
        bool IsDtls = false,
        string? DtlsProfileName = null)
    {
        /// <summary>
        /// Indicates whether the endpoint carries the minimum fields
        /// needed by the transport (non-null address and a port in
        /// the 1-65535 range).
        /// </summary>
        public bool IsValid => Address is not null && Port is > 0 and <= 65535;
    }
}
