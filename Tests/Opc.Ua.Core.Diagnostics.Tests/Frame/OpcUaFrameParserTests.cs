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
using System.Linq;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Tests.Frame
{
    [TestFixture]
    public sealed class OpcUaFrameParserTests
    {
        [Test]
        public void SingleChunkYieldsOneChunk()
        {
            var parser = new OpcUaFrameParser();
            byte[] chunk = PcapTestHelpers.BuildOpcUaChunk(TcpMessageType.MessageFinal, 100);

            OpcUaChunk[] chunks = [.. parser.Process(PcapTestHelpers.CreateSegment("flow", chunk))];

            Assert.That(chunks, Has.Length.EqualTo(1));
            Assert.That(chunks[0].Data.Length, Is.EqualTo(108));
            Assert.That(chunks[0].MessageType, Is.EqualTo(TcpMessageType.MessageFinal));
        }

        [Test]
        public void TwoChunksInOneSegmentYieldBoth()
        {
            var parser = new OpcUaFrameParser();
            byte[] first = PcapTestHelpers.BuildOpcUaChunk(TcpMessageType.MessageFinal, 10, 1);
            byte[] second = PcapTestHelpers.BuildOpcUaChunk(TcpMessageType.MessageFinal, 11, 2);

            OpcUaChunk[] chunks = [.. parser.Process(PcapTestHelpers.CreateSegment("flow", [.. first, .. second]))];

            Assert.That(chunks, Has.Length.EqualTo(2));
            Assert.That(chunks[0].Data.Length, Is.EqualTo(first.Length));
            Assert.That(chunks[1].Data.Length, Is.EqualTo(second.Length));
        }

        [Test]
        public void ChunkSplitAcrossTwoSegmentsYieldsWhenComplete()
        {
            var parser = new OpcUaFrameParser();
            byte[] chunk = PcapTestHelpers.BuildOpcUaChunk(TcpMessageType.MessageFinal, 20);

            OpcUaChunk[] first = [.. parser.Process(PcapTestHelpers.CreateSegment("flow", chunk[..12]))];
            OpcUaChunk[] second = [.. parser.Process(PcapTestHelpers.CreateSegment("flow", chunk[12..]))];

            Assert.That(first, Is.Empty);
            Assert.That(second, Has.Length.EqualTo(1));
            Assert.That(second[0].Data.ToArray(), Is.EqualTo(chunk).AsCollection);
        }

        [Test]
        public void HeaderOnlyFirstSegmentIsHeldUntilBodyArrives()
        {
            var parser = new OpcUaFrameParser();
            byte[] chunk = PcapTestHelpers.BuildOpcUaChunk(TcpMessageType.MessageFinal, 20);

            OpcUaChunk[] first = [.. parser.Process(PcapTestHelpers.CreateSegment("flow", chunk[..8]))];
            OpcUaChunk[] second = [.. parser.Process(PcapTestHelpers.CreateSegment("flow", chunk[8..]))];

            Assert.That(first, Is.Empty);
            Assert.That(second, Has.Length.EqualTo(1));
            Assert.That(second[0].Data.ToArray(), Is.EqualTo(chunk).AsCollection);
        }

        [Test]
        public void MultipleFlowsAreBufferedIndependently()
        {
            var parser = new OpcUaFrameParser();
            byte[] first = PcapTestHelpers.BuildOpcUaChunk(TcpMessageType.MessageFinal, 10, 1);
            byte[] second = PcapTestHelpers.BuildOpcUaChunk(TcpMessageType.MessageFinal, 10, 2);

            OpcUaChunk[] firstHalf = [.. parser.Process(PcapTestHelpers.CreateSegment("flow-a", first[..8]))];
            OpcUaChunk[] otherFlow = [.. parser.Process(PcapTestHelpers.CreateSegment("flow-b", second))];
            OpcUaChunk[] completeFirst = [.. parser.Process(PcapTestHelpers.CreateSegment("flow-a", first[8..]))];

            Assert.That(firstHalf, Is.Empty);
            Assert.That(otherFlow, Has.Length.EqualTo(1));
            Assert.That(completeFirst, Has.Length.EqualTo(1));
            Assert.That(otherFlow[0].Data.ToArray(), Is.EqualTo(second).AsCollection);
            Assert.That(completeFirst[0].Data.ToArray(), Is.EqualTo(first).AsCollection);
        }

        [Test]
        public void InvalidMessageTypeLogsWarningAndResynchronizes()
        {
            var logger = new Mock<ILogger<OpcUaFrameParser>>();
            var parser = new OpcUaFrameParser(logger.Object);
            byte[] valid = PcapTestHelpers.BuildOpcUaChunk(TcpMessageType.MessageFinal, 4);
            byte[] invalid = new byte[12 + valid.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(invalid, 0x58585858);
            BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(4), 12);
            valid.CopyTo(invalid.AsSpan(12));

            OpcUaChunk[] chunks = [.. parser.Process(PcapTestHelpers.CreateSegment("flow", invalid))];

            Assert.That(chunks, Has.Length.EqualTo(1));
            Assert.That(chunks[0].Data.ToArray(), Is.EqualTo(valid).AsCollection);
            logger.Verify(
                static x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((value, _) => value.ToString()!.Contains("Skipped", StringComparison.Ordinal)),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }
    }
}
