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
