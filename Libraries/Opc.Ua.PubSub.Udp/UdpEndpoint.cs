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
using Opc.Ua.PubSub.Udp.Dtls;

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
