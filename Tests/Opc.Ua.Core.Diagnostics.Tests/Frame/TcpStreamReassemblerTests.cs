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
using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Pcap.Frame;

using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Tests.Frame
{
    [TestFixture]
    public sealed class TcpStreamReassemblerTests
    {
        [Test]
        public void SingleSegmentYieldsPayloadAndFlowKey()
        {
            var reassembler = new TcpStreamReassembler();
            byte[] payload = Encoding.ASCII.GetBytes("hello");
            PcapRecord record = CreateEthernetRecord(payload);

            TcpFlowSegment[] segments = [.. reassembler.Process(record)];

            Assert.That(segments, Has.Length.EqualTo(1));
            Assert.That(segments[0].Data.ToArray(), Is.EqualTo(payload).AsCollection);
            Assert.That(segments[0].FlowKey, Does.Contain("10.0.0.1:50000->10.0.0.2:4840"));
        }

        [Test]
        public void TwoOrderedSegmentsSameFlowYieldInOrder()
        {
            var reassembler = new TcpStreamReassembler();
            byte[] first = Encoding.ASCII.GetBytes("hel");
            byte[] second = Encoding.ASCII.GetBytes("lo");

            TcpFlowSegment[] segments = [.. reassembler.Process(CreateEthernetRecord(first, 100))
, .. reassembler.Process(CreateEthernetRecord(second, 103))];

            Assert.That(segments, Has.Length.EqualTo(2));
            Assert.That(
                segments.SelectMany(static segment => segment.Data.ToArray()).ToArray(),
                Is.EqualTo(Encoding.ASCII.GetBytes("hello")).AsCollection);
        }

        [Test]
        public void TwoInterleavedFlowsYieldDifferentFlowKeys()
        {
            var reassembler = new TcpStreamReassembler();

            TcpFlowSegment[] segments =
            [
                .. reassembler.Process(CreateEthernetRecord([1], sourcePort: 50000))
,
                .. reassembler.Process(CreateEthernetRecord([2], sourcePort: 50001)),
            ];

            Assert.That(segments, Has.Length.EqualTo(2));
            Assert.That(segments[0].FlowKey, Is.Not.EqualTo(segments[1].FlowKey));
        }

        [Test]
        public void FinFlagIsPreservedWhenPayloadIsPresent()
        {
            var reassembler = new TcpStreamReassembler();

            TcpFlowSegment[] segments = [.. reassembler.Process(CreateEthernetRecord([1], fin: true))];

            Assert.That(segments, Has.Length.EqualTo(1));
            Assert.That(segments[0].IsFin, Is.True);
        }

        [Test]
        public void BsdLoopbackPacketExtractsTcpPayload()
        {
            var reassembler = new TcpStreamReassembler();
            byte[] payload = Encoding.ASCII.GetBytes("loop");
            byte[] raw = PcapTestHelpers.BuildRawIpv4TcpPacket(payload);
            byte[] packet = new byte[4 + raw.Length];
            packet[0] = 2;
            raw.CopyTo(packet.AsSpan(4));
            var record = new PcapRecord(DateTimeOffset.UtcNow, PcapFileWriter.LinkTypeNull, packet.Length, packet);

            TcpFlowSegment[] segments = [.. reassembler.Process(record)];

            Assert.That(segments, Has.Length.EqualTo(1));
            Assert.That(segments[0].Data.ToArray(), Is.EqualTo(payload).AsCollection);
        }

        [Test]
        public void RawIpv4PacketExtractsTcpPayload()
        {
            var reassembler = new TcpStreamReassembler();
            byte[] payload = Encoding.ASCII.GetBytes("raw");
            byte[] packet = PcapTestHelpers.BuildRawIpv4TcpPacket(payload);
            var record = new PcapRecord(DateTimeOffset.UtcNow, PcapFileWriter.LinkTypeRaw, packet.Length, packet);

            TcpFlowSegment[] segments = [.. reassembler.Process(record)];

            Assert.That(segments, Has.Length.EqualTo(1));
            Assert.That(segments[0].Data.ToArray(), Is.EqualTo(payload).AsCollection);
        }

        [Test]
        public void OutOfOrderSegmentsAreBufferedAndDrainedWhenGapArrives()
        {
            var reassembler = new TcpStreamReassembler();
            byte[] first = Encoding.ASCII.GetBytes("ab");
            byte[] second = Encoding.ASCII.GetBytes("cd");
            byte[] third = Encoding.ASCII.GetBytes("ef");

            TcpFlowSegment[] early = [.. reassembler.Process(CreateEthernetRecord(first, 100))];
            TcpFlowSegment[] buffered = [.. reassembler.Process(CreateEthernetRecord(third, 104))];
            TcpFlowSegment[] drained = [.. reassembler.Process(CreateEthernetRecord(second, 102))];

            Assert.That(early, Has.Length.EqualTo(1));
            Assert.That(buffered, Is.Empty);
            Assert.That(drained, Has.Length.EqualTo(2));
            Assert.That(
                drained.SelectMany(static segment => segment.Data.ToArray()).ToArray(),
                Is.EqualTo(Encoding.ASCII.GetBytes("cdef")).AsCollection);
        }

        [Test]
        public void DuplicateOrOlderSegmentYieldsImmediately()
        {
            var reassembler = new TcpStreamReassembler();
            _ = reassembler.Process(CreateEthernetRecord([1, 2], sequenceNumber: 100)).ToArray();

            TcpFlowSegment[] segments = [.. reassembler.Process(CreateEthernetRecord([9], sequenceNumber: 99))];

            Assert.That(segments, Has.Length.EqualTo(1));
            Assert.That(segments[0].SequenceNumber, Is.EqualTo(99));
            Assert.That(segments[0].Data.ToArray(), Is.EqualTo(new byte[] { 9 }).AsCollection);
        }

        [Test]
        public void InvalidPacketsProduceNoSegments()
        {
            var reassembler = new TcpStreamReassembler();
            byte[] nonIpv4Ethernet = PcapTestHelpers.BuildEthernetTcpPacket([1]);
            nonIpv4Ethernet[12] = 0x86;
            nonIpv4Ethernet[13] = 0xDD;
            byte[] udpPacket = PcapTestHelpers.BuildRawIpv4TcpPacket([2]);
            udpPacket[9] = 17;
            byte[] shortPacket = new byte[8];

            TcpFlowSegment[] segments =
            [
                .. reassembler.Process(new PcapRecord(DateTimeOffset.UtcNow, 999, shortPacket.Length, shortPacket)),
                .. reassembler.Process(
                    new PcapRecord(
                        DateTimeOffset.UtcNow,
                        PcapFileWriter.LinkTypeEthernet,
                        nonIpv4Ethernet.Length,
                        nonIpv4Ethernet)),
                .. reassembler.Process(
                    new PcapRecord(DateTimeOffset.UtcNow, PcapFileWriter.LinkTypeRaw, udpPacket.Length, udpPacket))
            ];

            Assert.That(segments, Is.Empty);
        }

        [Test]
        public void TcpFlowSegmentEqualityIncludesMetadataAndPayload()
        {
            var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var left = new TcpFlowSegment("flow", "a:1", "b:2", 7, timestamp, new byte[] { 1, 2 }, true, false);
            var same = new TcpFlowSegment("flow", "a:1", "b:2", 7, timestamp, new byte[] { 1, 2 }, true, false);
            var different = new TcpFlowSegment("flow", "a:1", "b:2", 8, timestamp, new byte[] { 1, 2 }, true, false);
            bool operatorEqual = left == same;
            bool operatorDifferent = left != different;
            bool objectEqual = left.Equals((object)same);
            bool objectDifferent = left.Equals("not a segment");

            Assert.That(left, Is.EqualTo(same));
            Assert.That(operatorEqual, Is.True);
            Assert.That(operatorDifferent, Is.True);
            Assert.That(objectEqual, Is.True);
            Assert.That(objectDifferent, Is.False);
            Assert.That(left.GetHashCode(), Is.EqualTo(same.GetHashCode()));
        }

        private static PcapRecord CreateEthernetRecord(
            byte[] payload,
            uint sequenceNumber = 1,
            ushort sourcePort = 50000,
            bool fin = false)
        {
            byte[] packet = PcapTestHelpers.BuildEthernetTcpPacket(payload, sequenceNumber, sourcePort, fin: fin);
            return new PcapRecord(DateTimeOffset.UtcNow, PcapFileWriter.LinkTypeEthernet, packet.Length, packet);
        }
    }
}
