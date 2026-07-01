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
