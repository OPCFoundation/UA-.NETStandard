/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Buffers.Binary;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Frame;

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
            logger.Setup(static x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
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
                    It.Is<EventId>(static e => e.Name == "SkippedBytesWhileResynchronizing"),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }
    }
}
