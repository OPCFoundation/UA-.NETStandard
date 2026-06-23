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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Pcap.Frame;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// Writes captured UDP PubSub datagrams as synthetic Ethernet/IPv4/UDP
    /// packets in libpcap or pcapng files.
    /// </summary>
    public sealed class PubSubPcapWriter
    {
        /// <summary>
        /// Writes UDP PubSub frames to a libpcap file. MQTT payloads are skipped.
        /// </summary>
        /// <param name="frames">Captured frames.</param>
        /// <param name="filePath">Destination .pcap path.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Number of UDP frames written.</returns>
        public async ValueTask<long> WritePcapAsync(
            IAsyncEnumerable<PubSubCaptureFrame> frames,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(frames);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            PcapFileWriter writer = new(filePath, PcapFileWriter.LinkTypeEthernet);
            try
            {
                return await WriteAsync(frames, writer.WriteAsync, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Writes UDP PubSub frames to a pcapng file. MQTT payloads are skipped.
        /// </summary>
        /// <param name="frames">Captured frames.</param>
        /// <param name="filePath">Destination .pcapng path.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Number of UDP frames written.</returns>
        public async ValueTask<long> WritePcapNgAsync(
            IAsyncEnumerable<PubSubCaptureFrame> frames,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(frames);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            FileStream stream = new(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            PcapNgFileWriter writer = new(stream, PcapFileWriter.LinkTypeEthernet);
            try
            {
                return await WriteAsync(frames, writer.WriteAsync, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static async ValueTask<long> WriteAsync(
            IAsyncEnumerable<PubSubCaptureFrame> frames,
            PacketWriter writePacketAsync,
            CancellationToken cancellationToken)
        {
            long count = 0;
            await foreach (PubSubCaptureFrame frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (!IsUdpFrame(frame))
                {
                    continue;
                }
                byte[] packet = BuildUdpPacket(in frame);
                await writePacketAsync(frame.Timestamp, packet, cancellationToken).ConfigureAwait(false);
                count++;
            }
            return count;
        }

        private static bool IsUdpFrame(in PubSubCaptureFrame frame)
        {
            string profile = frame.TransportProfileUri;
            return frame.Topic is null &&
                (profile.Contains("udp", StringComparison.OrdinalIgnoreCase) ||
                    profile.Contains("uadp", StringComparison.OrdinalIgnoreCase));
        }

        private static byte[] BuildUdpPacket(in PubSubCaptureFrame frame)
        {
            IPAddress remoteAddress = TryParseEndpoint(frame.Endpoint, out IPAddress parsedAddress, out ushort remotePort)
                ? parsedAddress
                : s_defaultRemoteAddress;
            ushort pubSubPort = remotePort == 0 ? PubSubUdpPort : remotePort;
            ReadOnlySpan<byte> localAddress = LocalAddress;
            Span<byte> remoteBytes = stackalloc byte[4];
            if (!remoteAddress.TryWriteBytes(remoteBytes, out int bytesWritten) || bytesWritten != 4)
            {
                DefaultRemoteAddressBytes.CopyTo(remoteBytes);
            }

            bool outbound = frame.Direction == PubSubCaptureDirection.Outbound;
            ReadOnlySpan<byte> sourceAddress = outbound ? localAddress : remoteBytes;
            ReadOnlySpan<byte> destinationAddress = outbound ? remoteBytes : localAddress;
            ushort sourcePort = outbound ? EphemeralPort : pubSubPort;
            ushort destinationPort = outbound ? pubSubPort : EphemeralPort;
            int udpLength = 8 + frame.Data.Length;
            int ipLength = 20 + udpLength;
            byte[] packet = new byte[14 + ipLength];

            Span<byte> ethernet = packet.AsSpan(0, 14);
            DestinationMac.CopyTo(ethernet);
            SourceMac.CopyTo(ethernet[6..]);
            BinaryPrimitives.WriteUInt16BigEndian(ethernet[12..], EtherTypeIpv4);

            Span<byte> ip = packet.AsSpan(14, 20);
            ip[0] = 0x45;
            BinaryPrimitives.WriteUInt16BigEndian(ip[2..], checked((ushort)ipLength));
            BinaryPrimitives.WriteUInt16BigEndian(ip[6..], 0x4000);
            ip[8] = 64;
            ip[9] = 17;
            sourceAddress.CopyTo(ip[12..16]);
            destinationAddress.CopyTo(ip[16..20]);
            BinaryPrimitives.WriteUInt16BigEndian(ip[10..], ComputeOnesComplement(ip));

            Span<byte> udp = packet.AsSpan(34, 8);
            BinaryPrimitives.WriteUInt16BigEndian(udp, sourcePort);
            BinaryPrimitives.WriteUInt16BigEndian(udp[2..], destinationPort);
            BinaryPrimitives.WriteUInt16BigEndian(udp[4..], checked((ushort)udpLength));
            frame.Data.Span.CopyTo(packet.AsSpan(42));
            BinaryPrimitives.WriteUInt16BigEndian(
                udp[6..],
                ComputeUdpChecksum(sourceAddress, destinationAddress, packet.AsSpan(34)));
            return packet;
        }

        private static bool TryParseEndpoint(string? endpoint, out IPAddress address, out ushort port)
        {
            address = IPAddress.None;
            port = 0;
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return false;
            }
            string host = endpoint;
            int colon = endpoint.LastIndexOf(':');
            if (colon > 0 && colon + 1 < endpoint.Length &&
                ushort.TryParse(endpoint[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port))
            {
                host = endpoint[..colon];
            }
            return IPAddress.TryParse(host, out address!) && address.AddressFamily == AddressFamily.InterNetwork;
        }

        private static ushort ComputeUdpChecksum(
            ReadOnlySpan<byte> sourceAddress,
            ReadOnlySpan<byte> destinationAddress,
            ReadOnlySpan<byte> udpDatagram)
        {
            uint sum = SumWords(sourceAddress) + SumWords(destinationAddress);
            sum += 17;
            sum += (uint)udpDatagram.Length;
            sum += SumWords(udpDatagram);
            ushort checksum = Fold(sum);
            return checksum == 0 ? (ushort)0xFFFF : checksum;
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

        private delegate ValueTask PacketWriter(
            DateTimeOffset timestamp,
            ReadOnlyMemory<byte> packetData,
            CancellationToken cancellationToken);

        private const ushort PubSubUdpPort = 4840;
        private const ushort EphemeralPort = 49152;
        private const ushort EtherTypeIpv4 = 0x0800;
        private static readonly IPAddress s_defaultRemoteAddress = IPAddress.Parse("239.0.0.1");
        private static ReadOnlySpan<byte> DestinationMac => [0x01, 0x00, 0x5e, 0x00, 0x00, 0x01];
        private static ReadOnlySpan<byte> SourceMac => [0x02, 0x00, 0x00, 0x00, 0x00, 0x01];
        private static ReadOnlySpan<byte> LocalAddress => [192, 0, 2, 10];
        private static ReadOnlySpan<byte> DefaultRemoteAddressBytes => [239, 0, 0, 1];
    }
}
