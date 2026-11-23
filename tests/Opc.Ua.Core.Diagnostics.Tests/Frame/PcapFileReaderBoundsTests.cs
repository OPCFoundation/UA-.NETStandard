// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Frame;

namespace Opc.Ua.Pcap.Tests.Frame
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
        public async Task ReadRejectsRecordWithCapturedLengthExceedingMaxPacketBytes()
        {
            string path = CreateTempPath("too-large.pcap");
            WritePcapFile(path, PcapFileReader.MaxPacketBytes + 1U, payloadLength: 0);

            PcapDiagnosticsException? exception = await Assert.ThrowsAsync<PcapDiagnosticsException>(async () =>
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
        public async Task ReadRejectsRecordWithCapturedLengthUintMaxValue()
        {
            string path = CreateTempPath("uint-max.pcap");
            WritePcapFile(path, uint.MaxValue, payloadLength: 0);

            PcapDiagnosticsException? exception = await Assert.ThrowsAsync<PcapDiagnosticsException>(async () =>
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
