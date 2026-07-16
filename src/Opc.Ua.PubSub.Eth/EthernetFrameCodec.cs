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
using System.Buffers.Binary;
using System.Net.NetworkInformation;

namespace Opc.Ua.PubSub.Eth
{
    /// <summary>
    /// Builds and parses Ethernet II frames carrying OPC UA PubSub
    /// NetworkMessages, with optional IEEE 802.1Q VLAN tagging.
    /// </summary>
    /// <remarks>
    /// Implements the OPC UA Part 14 Ethernet mapping frame layout:
    /// destination MAC (6) + source MAC (6) + optional 802.1Q tag
    /// (TPID 0x8100 + 2-octet TCI) + EtherType
    /// <see cref="OpcUaEtherType"/> (0xB62C) + payload, zero-padded to
    /// the 60-octet minimum frame length (the 4-octet FCS is appended by
    /// the network adapter and is not part of these buffers). All
    /// multi-octet header fields are big-endian (network order).
    /// </remarks>
    public static class EthernetFrameCodec
    {
        /// <summary>
        /// The EtherType assigned to OPC UA for the Part 14 Ethernet
        /// mapping.
        /// </summary>
        public const ushort OpcUaEtherType = 0xB62C;

        /// <summary>
        /// The Tag Protocol Identifier of an IEEE 802.1Q VLAN tag.
        /// </summary>
        public const ushort VlanTpid = 0x8100;

        /// <summary>
        /// Length of the untagged Ethernet II header: destination MAC
        /// (6) + source MAC (6) + EtherType (2).
        /// </summary>
        public const int HeaderLength = 14;

        /// <summary>
        /// Length of the 802.1Q VLAN-tagged Ethernet II header:
        /// <see cref="HeaderLength"/> + the 4-octet VLAN tag.
        /// </summary>
        public const int VlanTaggedHeaderLength = 18;

        /// <summary>
        /// Minimum Ethernet frame length excluding the FCS. Smaller
        /// frames are zero-padded to this length on send.
        /// </summary>
        public const int MinFrameLength = 60;

        /// <summary>
        /// Length of a MAC address in octets.
        /// </summary>
        public const int MacAddressLength = 6;

        /// <summary>
        /// Computes the buffer length required by
        /// <see cref="Build"/> for the supplied payload length.
        /// </summary>
        /// <param name="payloadLength">Payload length in octets.</param>
        /// <param name="vlanTagged">
        /// <see langword="true"/> when an 802.1Q tag is included.
        /// </param>
        /// <returns>
        /// The required buffer length, never less than
        /// <see cref="MinFrameLength"/>.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static int GetRequiredLength(int payloadLength, bool vlanTagged)
        {
            if (payloadLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadLength));
            }
            int header = vlanTagged ? VlanTaggedHeaderLength : HeaderLength;
            if (payloadLength > int.MaxValue - header)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadLength));
            }
            return Math.Max(header + payloadLength, MinFrameLength);
        }

        /// <summary>
        /// Builds an Ethernet II frame into <paramref name="destination"/>.
        /// </summary>
        /// <param name="destination">
        /// Target buffer; must be at least
        /// <see cref="GetRequiredLength"/> octets long.
        /// </param>
        /// <param name="destinationMac">Destination MAC (6 octets).</param>
        /// <param name="sourceMac">Source MAC (6 octets).</param>
        /// <param name="vlanId">
        /// Optional 802.1Q VLAN identifier (0-4095). A tag is emitted
        /// when either <paramref name="vlanId"/> or
        /// <paramref name="priority"/> is supplied.
        /// </param>
        /// <param name="priority">
        /// Optional 802.1Q Priority Code Point (0-7).
        /// </param>
        /// <param name="payload">The NetworkMessage payload.</param>
        /// <returns>The number of octets written.</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static int Build(
            Span<byte> destination,
            ReadOnlySpan<byte> destinationMac,
            ReadOnlySpan<byte> sourceMac,
            ushort? vlanId,
            byte? priority,
            ReadOnlySpan<byte> payload)
        {
            if (destinationMac.Length != MacAddressLength)
            {
                throw new ArgumentException(
                    "Destination MAC must be six octets.", nameof(destinationMac));
            }
            if (sourceMac.Length != MacAddressLength)
            {
                throw new ArgumentException(
                    "Source MAC must be six octets.", nameof(sourceMac));
            }
            if (vlanId is > 4095)
            {
                throw new ArgumentOutOfRangeException(nameof(vlanId));
            }
            if (priority is > 7)
            {
                throw new ArgumentOutOfRangeException(nameof(priority));
            }
            bool tagged = vlanId.HasValue || priority.HasValue;
            int total = GetRequiredLength(payload.Length, tagged);
            if (destination.Length < total)
            {
                throw new ArgumentException(
                    "Destination buffer is too small for the frame.", nameof(destination));
            }

            destinationMac.CopyTo(destination);
            sourceMac.CopyTo(destination[MacAddressLength..]);
            int offset = 2 * MacAddressLength;
            if (tagged)
            {
                BinaryPrimitives.WriteUInt16BigEndian(destination[offset..], VlanTpid);
                ushort tci = (ushort)(((priority ?? 0) << 13) | ((vlanId ?? 0) & 0x0FFF));
                BinaryPrimitives.WriteUInt16BigEndian(destination[(offset + 2)..], tci);
                offset += 4;
            }
            BinaryPrimitives.WriteUInt16BigEndian(destination[offset..], OpcUaEtherType);
            offset += 2;
            payload.CopyTo(destination[offset..]);
            offset += payload.Length;
            if (offset < total)
            {
                destination[offset..total].Clear();
            }
            return total;
        }

        /// <summary>
        /// Parses and EtherType-filters an Ethernet II frame without
        /// allocating. Returns the payload offset and the decoded 802.1Q
        /// context.
        /// </summary>
        /// <param name="frame">The complete frame (without FCS).</param>
        /// <param name="payloadOffset">
        /// On success, the offset of the NetworkMessage payload within
        /// <paramref name="frame"/>.
        /// </param>
        /// <param name="vlanId">
        /// On success, the 802.1Q VLAN identifier, or
        /// <see langword="null"/> when untagged.
        /// </param>
        /// <param name="priority">
        /// On success, the 802.1Q priority, or <see langword="null"/>
        /// when untagged.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the frame carries the OPC UA
        /// EtherType; otherwise <see langword="false"/>.
        /// </returns>
        public static bool TryParse(
            ReadOnlySpan<byte> frame,
            out int payloadOffset,
            out ushort? vlanId,
            out byte? priority)
        {
            payloadOffset = 0;
            vlanId = null;
            priority = null;
            if (frame.Length < HeaderLength)
            {
                return false;
            }
            int offset = 2 * MacAddressLength;
            ushort type = BinaryPrimitives.ReadUInt16BigEndian(frame[offset..]);
            offset += 2;
            if (type == VlanTpid)
            {
                if (frame.Length < VlanTaggedHeaderLength)
                {
                    return false;
                }
                ushort tci = BinaryPrimitives.ReadUInt16BigEndian(frame[offset..]);
                vlanId = (ushort)(tci & 0x0FFF);
                priority = (byte)((tci >> 13) & 0x07);
                offset += 2;
                type = BinaryPrimitives.ReadUInt16BigEndian(frame[offset..]);
                offset += 2;
            }
            if (type != OpcUaEtherType)
            {
                vlanId = null;
                priority = null;
                return false;
            }
            payloadOffset = offset;
            return true;
        }

        /// <summary>
        /// Parses and EtherType-filters an Ethernet II frame into an
        /// <see cref="EthernetFrame"/>.
        /// </summary>
        /// <param name="frame">The complete frame (without FCS).</param>
        /// <param name="parsed">On success, the decoded frame.</param>
        /// <returns>
        /// <see langword="true"/> when the frame carries the OPC UA
        /// EtherType; otherwise <see langword="false"/>.
        /// </returns>
        public static bool TryParse(ReadOnlyMemory<byte> frame, out EthernetFrame parsed)
        {
            parsed = default;
            if (!TryParse(frame.Span, out int payloadOffset, out ushort? vlanId, out byte? priority))
            {
                return false;
            }
            var destination = new PhysicalAddress(frame.Span[..MacAddressLength].ToArray());
            var source = new PhysicalAddress(
                frame.Span[MacAddressLength..(2 * MacAddressLength)].ToArray());
            parsed = new EthernetFrame(
                frame[payloadOffset..],
                source,
                destination,
                vlanId,
                priority);
            return true;
        }
    }
}
