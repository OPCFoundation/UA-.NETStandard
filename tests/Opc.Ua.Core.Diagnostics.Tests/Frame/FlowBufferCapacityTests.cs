// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using NUnit.Framework;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Frame;

namespace Opc.Ua.Pcap.Tests.Frame
{
    [TestFixture]
    public sealed class FlowBufferCapacityTests
    {
        [Test]
        public void EnsureCapacityRejectsRequestExceedingMax()
        {
            var buffer = new OpcUaFrameParser.FlowBuffer(16);

            Assert.That(
                () => buffer.EnsureCapacity(OpcUaFrameParser.FlowBuffer.MaxBufferBytes + 1),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("would exceed").And
                    .With.Message.Contains(nameof(OpcUaFrameParser.FlowBuffer.MaxBufferBytes)));
        }

        [Test]
        public void EnsureCapacityNormalGrowthStillDoubles()
        {
            var buffer = new OpcUaFrameParser.FlowBuffer(16);
            int initialCapacity = buffer.Capacity;

            buffer.EnsureCapacity(initialCapacity * 2);

            Assert.That(buffer.Capacity, Is.GreaterThanOrEqualTo(initialCapacity * 2));
        }

        [Test]
        [Explicit("Allocates MaxBufferBytes by design; run manually when boundary allocation coverage is required.")]
        public void EnsureCapacityAcceptsRequestAtMaxBoundary()
        {
            var buffer = new OpcUaFrameParser.FlowBuffer(16);

            Assert.That(() => buffer.EnsureCapacity(OpcUaFrameParser.FlowBuffer.MaxBufferBytes), Throws.Nothing);
        }

        [Test]
        public void MaxBufferBytesConstantIsExposed()
        {
            Assert.That(OpcUaFrameParser.FlowBuffer.MaxBufferBytes, Is.EqualTo(256 * 1024 * 1024));
        }
    }
}
