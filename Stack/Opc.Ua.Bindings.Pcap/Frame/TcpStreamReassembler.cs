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
using System.Net;

namespace Opc.Ua.Bindings.Pcap.Frame
{
    /// <summary>
    /// Reassembles best-effort unidirectional TCP byte streams from pcap records.
    /// </summary>
    public sealed class TcpStreamReassembler
    {
        /// <summary>
        /// Processes a pcap record and returns TCP payload segments discovered in it.
        /// </summary>
        public IEnumerable<TcpFlowSegment> Process(PcapRecord record)
        {
            if (!TryParseTcp(record, out ParsedTcp parsed) || parsed.Payload.IsEmpty)
            {
                yield break;
            }

            string sourceEndpoint = FormatEndpoint(parsed.SourceAddress, parsed.SourcePort);
            string destinationEndpoint = FormatEndpoint(parsed.DestinationAddress, parsed.DestinationPort);
            string flowKey = string.Concat(sourceEndpoint, "->", destinationEndpoint);
            if (!m_flows.TryGetValue(flowKey, out FlowState? state))
            {
                state = new FlowState();
                m_flows.Add(flowKey, state);
            }
            foreach (TcpFlowSegment segment in state.Process(flowKey, sourceEndpoint, destinationEndpoint, record.Timestamp, parsed))
            {
                yield return segment;
            }
        }

        private static bool TryParseTcp(PcapRecord record, out ParsedTcp parsed)
        {
            parsed = default;
            ReadOnlySpan<byte> data = record.Data.Span;
            int offset = record.LinkType switch
            {
                PcapFileWriter.LinkTypeNull when data.Length >= 4 => 4,
                PcapFileWriter.LinkTypeEthernet when data.Length >= 14 && BinaryPrimitives.ReadUInt16BigEndian(data[12..]) == 0x0800 => 14,
                PcapFileWriter.LinkTypeRaw or PcapFileWriter.LinkTypeIPv4 => 0,
                _ => -1
            };
            if (offset < 0 || data.Length < offset + 40)
            {
                return false;
            }

            ReadOnlySpan<byte> ip = data[offset..];
            int headerLength = (ip[0] & 0x0F) * 4;
            if ((ip[0] >> 4) != 4 || headerLength < 20 || ip.Length < headerLength || ip[9] != 6)
            {
                return false;
            }

            int totalLength = BinaryPrimitives.ReadUInt16BigEndian(ip[2..]);
            if (totalLength < headerLength + 20 || ip.Length < totalLength)
            {
                return false;
            }

            ReadOnlySpan<byte> tcp = ip[headerLength..totalLength];
            int tcpHeaderLength = (tcp[12] >> 4) * 4;
            if (tcpHeaderLength < 20 || tcp.Length < tcpHeaderLength)
            {
                return false;
            }

            parsed = new ParsedTcp(
                new IPAddress(ip[12..16]),
                new IPAddress(ip[16..20]),
                BinaryPrimitives.ReadUInt16BigEndian(tcp),
                BinaryPrimitives.ReadUInt16BigEndian(tcp[2..]),
                BinaryPrimitives.ReadUInt32BigEndian(tcp[4..]),
                (tcp[13] & 0x01) != 0,
                (tcp[13] & 0x02) != 0,
                data.Slice(offset + headerLength + tcpHeaderLength, totalLength - headerLength - tcpHeaderLength).ToArray());
            return true;
        }

        private static string FormatEndpoint(IPAddress address, ushort port)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{address}:{port}");
        }

        private readonly Dictionary<string, FlowState> m_flows = [];

        private readonly struct ParsedTcp
        {
            public ParsedTcp(
                IPAddress sourceAddress,
                IPAddress destinationAddress,
                ushort sourcePort,
                ushort destinationPort,
                uint sequenceNumber,
                bool isFin,
                bool isSyn,
                ReadOnlyMemory<byte> payload)
            {
                SourceAddress = sourceAddress;
                DestinationAddress = destinationAddress;
                SourcePort = sourcePort;
                DestinationPort = destinationPort;
                SequenceNumber = sequenceNumber;
                IsFin = isFin;
                IsSyn = isSyn;
                Payload = payload;
            }

            public IPAddress SourceAddress { get; }
            public IPAddress DestinationAddress { get; }
            public ushort SourcePort { get; }
            public ushort DestinationPort { get; }
            public uint SequenceNumber { get; }
            public bool IsFin { get; }
            public bool IsSyn { get; }
            public ReadOnlyMemory<byte> Payload { get; }
        }

        private sealed class FlowState
        {
            public IEnumerable<TcpFlowSegment> Process(
                string flowKey,
                string sourceEndpoint,
                string destinationEndpoint,
                DateTimeOffset timestamp,
                ParsedTcp parsed)
            {
                if (!m_hasNextSequence || parsed.SequenceNumber == m_nextSequence)
                {
                    m_hasNextSequence = true;
                    m_nextSequence = parsed.SequenceNumber + (uint)parsed.Payload.Length;
                    yield return CreateSegment(flowKey, sourceEndpoint, destinationEndpoint, timestamp, parsed);
                    foreach (TcpFlowSegment buffered in DrainBuffered(flowKey, sourceEndpoint, destinationEndpoint, timestamp))
                    {
                        yield return buffered;
                    }
                    yield break;
                }

                if (IsAfter(parsed.SequenceNumber, m_nextSequence) && m_bufferedBytes + parsed.Payload.Length <= MaxBufferedBytes)
                {
                    m_buffered[parsed.SequenceNumber] = parsed.Payload.ToArray();
                    m_bufferedBytes += parsed.Payload.Length;
                    yield break;
                }

                yield return CreateSegment(flowKey, sourceEndpoint, destinationEndpoint, timestamp, parsed);
            }

            private IEnumerable<TcpFlowSegment> DrainBuffered(
                string flowKey,
                string sourceEndpoint,
                string destinationEndpoint,
                DateTimeOffset timestamp)
            {
                while (m_buffered.Remove(m_nextSequence, out byte[]? data))
                {
                    uint sequence = m_nextSequence;
                    m_nextSequence += (uint)data.Length;
                    m_bufferedBytes -= data.Length;
                    yield return new TcpFlowSegment(
                        flowKey,
                        sourceEndpoint,
                        destinationEndpoint,
                        sequence,
                        timestamp,
                        data,
                        isFin: false,
                        isSyn: false);
                }
            }

            private static TcpFlowSegment CreateSegment(
                string flowKey,
                string sourceEndpoint,
                string destinationEndpoint,
                DateTimeOffset timestamp,
                ParsedTcp parsed)
            {
                return new TcpFlowSegment(
                    flowKey,
                    sourceEndpoint,
                    destinationEndpoint,
                    parsed.SequenceNumber,
                    timestamp,
                    parsed.Payload,
                    parsed.IsFin,
                    parsed.IsSyn);
            }

            private static bool IsAfter(uint value, uint compare)
            {
                return unchecked((int)(value - compare)) > 0;
            }

            private const int MaxBufferedBytes = 1024 * 1024;
            private readonly SortedList<uint, byte[]> m_buffered = [];
            private uint m_nextSequence;
            private int m_bufferedBytes;
            private bool m_hasNextSequence;
        }
    }

    /// <summary>
    /// A TCP payload segment for one unidirectional flow.
    /// </summary>
    public readonly struct TcpFlowSegment : IEquatable<TcpFlowSegment>
    {
        /// <summary>
        /// Constructs a TCP flow segment.
        /// </summary>
        public TcpFlowSegment(
            string flowKey,
            string sourceEndpoint,
            string destinationEndpoint,
            uint sequenceNumber,
            DateTimeOffset timestamp,
            ReadOnlyMemory<byte> data,
            bool isFin,
            bool isSyn)
        {
            FlowKey = flowKey;
            SourceEndpoint = sourceEndpoint;
            DestinationEndpoint = destinationEndpoint;
            SequenceNumber = sequenceNumber;
            Timestamp = timestamp;
            Data = data;
            IsFin = isFin;
            IsSyn = isSyn;
        }

        /// <summary>Unidirectional flow key.</summary>
        public string FlowKey { get; }

        /// <summary>Source endpoint.</summary>
        public string SourceEndpoint { get; }

        /// <summary>Destination endpoint.</summary>
        public string DestinationEndpoint { get; }

        /// <summary>TCP sequence number.</summary>
        public uint SequenceNumber { get; }

        /// <summary>Segment timestamp.</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>TCP payload bytes.</summary>
        public ReadOnlyMemory<byte> Data { get; }

        /// <summary>Whether FIN was set.</summary>
        public bool IsFin { get; }

        /// <summary>Whether SYN was set.</summary>
        public bool IsSyn { get; }

        /// <inheritdoc/>
        public bool Equals(TcpFlowSegment other)
        {
            return string.Equals(FlowKey, other.FlowKey, StringComparison.Ordinal)
                && string.Equals(SourceEndpoint, other.SourceEndpoint, StringComparison.Ordinal)
                && string.Equals(DestinationEndpoint, other.DestinationEndpoint, StringComparison.Ordinal)
                && SequenceNumber == other.SequenceNumber
                && Timestamp.Equals(other.Timestamp)
                && Data.Span.SequenceEqual(other.Data.Span)
                && IsFin == other.IsFin
                && IsSyn == other.IsSyn;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is TcpFlowSegment other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(FlowKey, SourceEndpoint, DestinationEndpoint, SequenceNumber, Timestamp, Data.Length);
        }

        /// <summary>
        /// Equality comparison.
        /// </summary>
        public static bool operator ==(TcpFlowSegment left, TcpFlowSegment right) => left.Equals(right);

        /// <summary>
        /// Inequality comparison.
        /// </summary>
        public static bool operator !=(TcpFlowSegment left, TcpFlowSegment right) => !left.Equals(right);
    }
}
