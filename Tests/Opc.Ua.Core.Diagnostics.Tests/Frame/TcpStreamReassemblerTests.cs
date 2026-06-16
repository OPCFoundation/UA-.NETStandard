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
using Opc.Ua.Core.Diagnostics.Frame;

using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Diagnostics.Tests.Frame
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
