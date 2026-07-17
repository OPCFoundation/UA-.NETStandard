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
