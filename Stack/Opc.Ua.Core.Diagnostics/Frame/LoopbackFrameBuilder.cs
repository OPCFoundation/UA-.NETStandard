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

namespace Opc.Ua.Pcap.Frame
{
    /// <summary>
    /// Builds synthetic BSD-loopback IPv4/TCP packets around OPC UA chunks.
    /// </summary>
    public static class LoopbackFrameBuilder
    {
        /// <summary>
        /// Maximum TCP payload that fits the IPv4 and pcap snap-length limits.
        /// </summary>
        internal const int MaxTcpPayloadSize =
            ushort.MaxValue -
            kLoopbackHeaderSize -
            kIpHeaderSize -
            kTcpHeaderSize;

        /// <summary>
        /// Builds a BSD-loopback packet containing a fake IPv4/TCP segment.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The chunk is too large for one IPv4/TCP packet.
        /// </exception>
        public static byte[] Build(bool fromClient, uint channelId, ReadOnlySpan<byte> chunkBytes)
        {
            return BuildPacket(fromClient, channelId, sequenceNumber: 0, chunkBytes);
        }

        /// <summary>
        /// Builds one or more sequential synthetic TCP packets for a chunk.
        /// </summary>
        internal static byte[][] BuildPackets(
            bool fromClient,
            uint channelId,
            uint sequenceNumber,
            ReadOnlySpan<byte> chunkBytes)
        {
            int packetCount = GetPacketCount(chunkBytes.Length);
            byte[][] packets = new byte[packetCount][];
            int offset = 0;
            for (int ii = 0; ii < packetCount; ii++)
            {
                int count = Math.Min(MaxTcpPayloadSize, chunkBytes.Length - offset);
                packets[ii] = BuildPacket(
                    fromClient,
                    channelId,
                    unchecked(sequenceNumber + (uint)offset),
                    chunkBytes.Slice(offset, count));
                offset += count;
            }
            return packets;
        }

        /// <summary>
        /// Returns the synthetic TCP packet count required for a payload.
        /// </summary>
        internal static int GetPacketCount(int payloadLength)
        {
            if (payloadLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadLength));
            }
            int packetCount = payloadLength / MaxTcpPayloadSize;
            if (payloadLength % MaxTcpPayloadSize != 0)
            {
                packetCount++;
            }
            return Math.Max(1, packetCount);
        }

        private static byte[] BuildPacket(
            bool fromClient,
            uint channelId,
            uint sequenceNumber,
            ReadOnlySpan<byte> chunkBytes)
        {
            if (chunkBytes.Length > MaxTcpPayloadSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkBytes),
                    chunkBytes.Length,
                    $"TCP payload cannot exceed {MaxTcpPayloadSize} bytes in a synthetic IPv4 frame.");
            }

            uint clientHost = channelId >> 14;
            Span<byte> clientAddress =
            [
                127,
                (byte)(clientHost >> 16),
                (byte)(clientHost >> 8),
                (byte)clientHost
            ];
            Span<byte> serverAddress = [127, 255, 255, 255];
            ushort clientPort = checked((ushort)(49152U + (channelId & 0x3FFFU)));
            const ushort serverPort = 4840;
            ReadOnlySpan<byte> sourceAddress = fromClient ? clientAddress : serverAddress;
            ReadOnlySpan<byte> destinationAddress = fromClient ? serverAddress : clientAddress;
            ushort sourcePort = fromClient ? clientPort : serverPort;
            ushort destinationPort = fromClient ? serverPort : clientPort;
            int tcpLength = kTcpHeaderSize + chunkBytes.Length;
            int ipLength = kIpHeaderSize + tcpLength;
            byte[] packet = new byte[kLoopbackHeaderSize + ipLength];

            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, kLoopbackHeaderSize), 2);
            Span<byte> ip = packet.AsSpan(kLoopbackHeaderSize, kIpHeaderSize);
            ip[0] = 0x45;
            BinaryPrimitives.WriteUInt16BigEndian(ip[2..], (ushort)ipLength);
            BinaryPrimitives.WriteUInt16BigEndian(ip[6..], 0x4000);
            ip[8] = 64;
            ip[9] = 6;
            sourceAddress.CopyTo(ip[12..16]);
            destinationAddress.CopyTo(ip[16..20]);
            BinaryPrimitives.WriteUInt16BigEndian(ip[10..], ComputeOnesComplement(ip));

            Span<byte> tcp = packet.AsSpan(
                kLoopbackHeaderSize + kIpHeaderSize,
                kTcpHeaderSize);
            BinaryPrimitives.WriteUInt16BigEndian(tcp, sourcePort);
            BinaryPrimitives.WriteUInt16BigEndian(tcp[2..], destinationPort);
            BinaryPrimitives.WriteUInt32BigEndian(tcp[4..], sequenceNumber);
            tcp[12] = 0x50;
            tcp[13] = 0x18;
            BinaryPrimitives.WriteUInt16BigEndian(tcp[14..], 0xFFFF);
            chunkBytes.CopyTo(packet.AsSpan(kLoopbackHeaderSize + kIpHeaderSize + kTcpHeaderSize));
            BinaryPrimitives.WriteUInt16BigEndian(
                tcp[16..],
                ComputeTcpChecksum(
                    sourceAddress,
                    destinationAddress,
                    packet.AsSpan(kLoopbackHeaderSize + kIpHeaderSize)));
            return packet;
        }

        private static ushort ComputeTcpChecksum(
            ReadOnlySpan<byte> sourceAddress,
            ReadOnlySpan<byte> destinationAddress,
            ReadOnlySpan<byte> tcpSegment)
        {
            uint sum = SumWords(sourceAddress) + SumWords(destinationAddress);
            sum += 6;
            sum += (uint)tcpSegment.Length;
            sum += SumWords(tcpSegment);
            return Fold(sum);
        }

        private static ushort ComputeOnesComplement(ReadOnlySpan<byte> data)
        {
            return Fold(SumWords(data));
        }

        private static uint SumWords(ReadOnlySpan<byte> data)
        {
            uint sum = 0;
            int index = 0;
            while (index + 1 < data.Length)
            {
                sum += BinaryPrimitives.ReadUInt16BigEndian(data[index..]);
                index += 2;
            }
            if (index < data.Length)
            {
                sum += (uint)(data[index] << 8);
            }
            return sum;
        }

        private static ushort Fold(uint sum)
        {
            while ((sum >> 16) != 0)
            {
                sum = (sum & 0xFFFFU) + (sum >> 16);
            }
            return (ushort)~sum;
        }

        private const int kLoopbackHeaderSize = 4;
        private const int kIpHeaderSize = 20;
        private const int kTcpHeaderSize = 20;
    }
}
