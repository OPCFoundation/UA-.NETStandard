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

using NUnit.Framework;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Frame;

using Opc.Ua.Bindings;

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
