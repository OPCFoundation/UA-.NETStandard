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

// Opc.Ua.Mcp targets net10.0 only, so the MCP integration fixtures only
// build and run on net10.0.
#if NET10_0
using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Mcp;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Small deterministic helpers shared by the PubSub MCP tool tests:
    /// ephemeral loopback port reservation, a fresh isolated
    /// <see cref="PubSubRuntimeManager"/>, minimal valid UADP frame
    /// encoding, and a synthetic Ethernet/IPv4/UDP frame builder for pcap
    /// decode tests. Nothing here touches a real network beyond loopback
    /// port reservation probes, and no multicast is used.
    /// </summary>
    internal static class PubSubMcpTestHelpers
    {
        /// <summary>
        /// Reserves an ephemeral loopback UDP port by binding a throwaway
        /// probe socket to port 0 and reading back the assigned port. The
        /// probe socket is closed immediately so the port can be reused by
        /// the caller's own transport.
        /// </summary>
        public static int ReserveEphemeralLoopbackPort()
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)probe.LocalEndPoint!).Port;
        }

        /// <summary>
        /// Creates a new, fully isolated <see cref="PubSubRuntimeManager"/>
        /// backed by the shared <see cref="McpTestEnvironment.Services"/>
        /// provider but with its own state, so tests never observe another
        /// test's runtime mode or registered Action responders.
        /// </summary>
        public static PubSubRuntimeManager NewManager()
        {
            return new PubSubRuntimeManager(
                McpTestEnvironment.Services,
                NullLogger<PubSubRuntimeManager>.Instance);
        }

        /// <summary>
        /// Encodes a minimal, valid single-field UADP NetworkMessage,
        /// mirroring the content mask used by
        /// <c>PubSubRuntimeManager.CreateWriterGroupMessageSettings</c> /
        /// <c>CreateWriterMessageSettings</c>, so the offline dissector can
        /// decode it as a real PubSub frame without any network I/O.
        /// </summary>
        public static async Task<byte[]> EncodeMinimalUadpAsync(
            ushort publisherId,
            ushort writerGroupId,
            ushort dataSetWriterId,
            Variant fieldValue,
            string fieldName = "Value")
        {
            var message = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = PublisherId.FromUInt16(publisherId),
                WriterGroupId = writerGroupId,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = dataSetWriterId,
                        SequenceNumber = 1,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [new DataSetField { Name = fieldName, Value = fieldValue }]
                    }
                ]
            };

            var context = new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                new DataSetMetaDataRegistry(),
                new Opc.Ua.PubSub.Diagnostics.PubSubDiagnostics(Opc.Ua.PubSub.Diagnostics.PubSubDiagnosticsLevel.Low),
                TimeProvider.System);

            ReadOnlyMemory<byte> encoded = await new UadpEncoder().EncodeAsync(message, context)
                .ConfigureAwait(false);
            return encoded.ToArray();
        }

        /// <summary>
        /// Builds a minimal Ethernet(II)/IPv4/UDP frame wrapping
        /// <paramref name="udpPayload"/>, suitable for a synthetic .pcap
        /// record with link type Ethernet. Checksums are left at zero;
        /// <c>PubSubDecodeTools.TryGetUdpPayload</c> does not validate
        /// them, only header shape, ethertype, protocol and length fields.
        /// </summary>
        public static byte[] BuildEthernetIPv4UdpFrame(
            byte[] udpPayload,
            IPAddress sourceAddress,
            IPAddress destinationAddress,
            ushort sourcePort,
            ushort destinationPort)
        {
            const int ethernetHeaderLength = 14;
            const int ipHeaderLength = 20;
            const int udpHeaderLength = 8;

            int udpLength = udpHeaderLength + udpPayload.Length;
            int ipTotalLength = ipHeaderLength + udpLength;
            byte[] frame = new byte[ethernetHeaderLength + ipTotalLength];

            // Ethernet header: destination MAC, source MAC (both zeroed;
            // not inspected by the decoder), then ethertype 0x0800 (IPv4).
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(12, 2), 0x0800);

            Span<byte> ip = frame.AsSpan(ethernetHeaderLength, ipHeaderLength);
            ip[0] = 0x45; // version 4, IHL 5 (20 bytes, no options)
            ip[1] = 0x00; // DSCP/ECN
            BinaryPrimitives.WriteUInt16BigEndian(ip[2..4], (ushort)ipTotalLength);
            BinaryPrimitives.WriteUInt16BigEndian(ip[4..6], 1); // identification
            ip[6] = 0x00; // flags/fragment offset
            ip[7] = 0x00;
            ip[8] = 64; // TTL
            ip[9] = 17; // protocol = UDP
            BinaryPrimitives.WriteUInt16BigEndian(ip[10..12], 0); // header checksum (unchecked)
            sourceAddress.GetAddressBytes().CopyTo(ip[12..16]);
            destinationAddress.GetAddressBytes().CopyTo(ip[16..20]);

            Span<byte> udp = frame.AsSpan(ethernetHeaderLength + ipHeaderLength, udpLength);
            BinaryPrimitives.WriteUInt16BigEndian(udp[0..2], sourcePort);
            BinaryPrimitives.WriteUInt16BigEndian(udp[2..4], destinationPort);
            BinaryPrimitives.WriteUInt16BigEndian(udp[4..6], (ushort)udpLength);
            BinaryPrimitives.WriteUInt16BigEndian(udp[6..8], 0); // checksum (unchecked)
            udpPayload.CopyTo(udp[8..]);

            return frame;
        }
    }
}
#endif
