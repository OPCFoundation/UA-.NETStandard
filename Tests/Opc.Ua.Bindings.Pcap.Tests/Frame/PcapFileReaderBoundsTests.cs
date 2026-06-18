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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings.Pcap.Capture;
using Opc.Ua.Bindings.Pcap.Frame;

namespace Opc.Ua.Bindings.Pcap.Tests.Frame
{
    /// <summary>
    /// Verifies pcap reader bounds checks for captured packet lengths.
    /// </summary>
    [TestFixture]
    public sealed class PcapFileReaderBoundsTests : TempDirectoryFixture
    {
        /// <summary>
        /// Rejects a pcap record whose captured length is larger than
        /// the supported packet size limit.
        /// </summary>
        [Test]
        public void ReadRejectsRecordWithCapturedLengthExceedingMaxPacketBytes()
        {
            string path = CreateTempPath("too-large.pcap");
            WritePcapFile(path, PcapFileReader.MaxPacketBytes + 1U, payloadLength: 0);

            PcapDiagnosticsException? exception = Assert.ThrowsAsync<PcapDiagnosticsException>(async () =>
                await PcapTestHelpers.ToListAsync(PcapFileReader.ReadAllAsync(path, CancellationToken.None))
                    .ConfigureAwait(false));

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Message, Does.Contain("exceeds"));
            Assert.That(exception.Message, Does.Contain("MaxPacketBytes"));
        }

        /// <summary>
        /// Rejects a pcap record whose captured length is the maximum
        /// unsigned integer value.
        /// </summary>
        [Test]
        public void ReadRejectsRecordWithCapturedLengthUintMaxValue()
        {
            string path = CreateTempPath("uint-max.pcap");
            WritePcapFile(path, uint.MaxValue, payloadLength: 0);

            PcapDiagnosticsException? exception = Assert.ThrowsAsync<PcapDiagnosticsException>(async () =>
                await PcapTestHelpers.ToListAsync(PcapFileReader.ReadAllAsync(path, CancellationToken.None))
                    .ConfigureAwait(false));

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Message, Does.Contain("exceeds"));
            Assert.That(exception.Message, Does.Contain("MaxPacketBytes"));
        }

        /// <summary>
        /// Accepts a normal pcap record to verify valid captures still
        /// read successfully after the bounds check.
        /// </summary>
        [Test]
        public async Task ReadAcceptsNormalRecord()
        {
            string path = CreateTempPath("normal.pcap");
            WritePcapFile(path, 1024, payloadLength: 1024);

            List<PcapRecord> records = await PcapTestHelpers.ToListAsync(
                PcapFileReader.ReadAllAsync(path, CancellationToken.None),
                maxCount: 1).ConfigureAwait(false);

            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].Data.Length, Is.EqualTo(1024));
            Assert.That(records[0].OriginalLength, Is.EqualTo(1024));
        }

        /// <summary>
        /// Exposes the packet size limit as a public constant for
        /// diagnostics and tests.
        /// </summary>
        [Test]
        public void MaxPacketBytesConstantIsExposed()
        {
            Assert.That(PcapFileReader.MaxPacketBytes, Is.EqualTo(64 * 1024 * 1024));
        }

        private static void WritePcapFile(string path, uint capturedLength, int payloadLength)
        {
            byte[] bytes = new byte[40 + payloadLength];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 0xA1B2C3D4U);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, 2), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6, 2), 4);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 65535);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20, 4), 101);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24, 4), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28, 4), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32, 4), capturedLength);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36, 4), capturedLength);

            File.WriteAllBytes(path, bytes);
        }
    }
}
