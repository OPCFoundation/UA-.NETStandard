// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Frame;

namespace Opc.Ua.Pcap.Tests.Frame
{
    [TestFixture]
    public sealed class PcapFileReadWriteTests : TempDirectoryFixture
    {
        [TestCase(0)]
        [TestCase(8)]
        [TestCase(23)]
        public async Task CompleteFileValidationRejectsMissingOrPartialGlobalHeadersAsync(int length)
        {
            string path = CreateTempPath("partial-global.pcap");
            await File.WriteAllBytesAsync(path, new byte[length]).ConfigureAwait(false);
            await Assert.ThatAsync(() => PcapTestHelpers.ToListAsync(
                PcapFileReader.ReadAllAsync(path, requireCompleteFile: true, CancellationToken.None)),
                Throws.TypeOf<PcapDiagnosticsException>()).ConfigureAwait(false);
            List<PcapRecord> legacy = await PcapTestHelpers.ToListAsync(
                PcapFileReader.ReadAllAsync(path, CancellationToken.None)).ConfigureAwait(false);
            Assert.That(legacy, Is.Empty);
        }

        [Test]
        public async Task CompleteFileValidationRejectsAPartialTailAfterAValidRecordAsync()
        {
            string path = CreateTempPath("partial-tail.pcap");
            await WriteSinglePacketAsync(path, PcapFileWriter.LinkTypeEthernet).ConfigureAwait(false);
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous);
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(new byte[7]).ConfigureAwait(false);
            }
            await Assert.ThatAsync(() => PcapTestHelpers.ToListAsync(
                PcapFileReader.ReadAllAsync(path, requireCompleteFile: true, CancellationToken.None), maxCount: 2),
                Throws.TypeOf<PcapDiagnosticsException>()).ConfigureAwait(false);
            List<PcapRecord> legacy = await PcapTestHelpers.ToListAsync(
                PcapFileReader.ReadAllAsync(path, CancellationToken.None), maxCount: 2).ConfigureAwait(false);
            Assert.That(legacy, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task UnrepresentableOriginalPacketLengthProducesAProtocolErrorAsync()
        {
            string path = CreateTempPath("original-length.pcap");
            await WriteSinglePacketAsync(path, PcapFileWriter.LinkTypeEthernet).ConfigureAwait(false);
            byte[] content = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(36, 4), uint.MaxValue);
            await File.WriteAllBytesAsync(path, content).ConfigureAwait(false);
            await Assert.ThatAsync(() => PcapTestHelpers.ToListAsync(
                PcapFileReader.ReadAllAsync(path, requireCompleteFile: true, CancellationToken.None)),
                Throws.TypeOf<PcapDiagnosticsException>().With.Message.Contains("original packet length"))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task OneRecordRoundTrips()
        {
            string path = CreateTempPath("one.pcap");
            byte[] payload = [1, 2, 3, 4, 5];
            DateTimeOffset timestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(123450);

            var writer = new PcapFileWriter(path, PcapFileWriter.LinkTypeEthernet);
            try
            {
                await writer.WriteAsync(timestamp, payload, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            List<PcapRecord> records = await PcapTestHelpers.ToListAsync(
                PcapFileReader.ReadAllAsync(path, CancellationToken.None),
                maxCount: 1).ConfigureAwait(false);

            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That((records[0].Timestamp - timestamp).Duration(), Is.LessThanOrEqualTo(TimeSpan.FromTicks(10)));
            Assert.That(records[0].OriginalLength, Is.EqualTo(payload.Length));
            Assert.That(records[0].Data.ToArray(), Is.EqualTo(payload).AsCollection);
        }

        [Test]
        public async Task ManyRecordsRoundTripInOrder()
        {
            string path = CreateTempPath("many.pcap");
            var payloads = new List<byte[]>();

            var writer = new PcapFileWriter(path, PcapFileWriter.LinkTypeEthernet);
            try
            {
                for (int index = 0; index < 100; index++)
                {
                    byte[] payload = RandomNumberGenerator.GetBytes(10 + (index % 91));
                    payloads.Add(payload);
                    await writer.WriteAsync(DateTimeOffset.UnixEpoch.AddMilliseconds(index), payload, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            List<PcapRecord> records = await PcapTestHelpers.ToListAsync(
                PcapFileReader.ReadAllAsync(path, CancellationToken.None),
                maxCount: 100).ConfigureAwait(false);

            Assert.That(records, Has.Count.EqualTo(payloads.Count));
            for (int index = 0; index < payloads.Count; index++)
            {
                Assert.That(records[index].Data.ToArray(), Is.EqualTo(payloads[index]).AsCollection);
            }
        }

        [Test]
        public async Task LinkTypeIsPreserved()
        {
            string nullPath = CreateTempPath("null.pcap");
            string ethernetPath = CreateTempPath("ethernet.pcap");

            await WriteSinglePacketAsync(nullPath, PcapFileWriter.LinkTypeNull).ConfigureAwait(false);
            await WriteSinglePacketAsync(ethernetPath, PcapFileWriter.LinkTypeEthernet).ConfigureAwait(false);

            List<PcapRecord> nullRecords = await PcapTestHelpers.ToListAsync(
                PcapFileReader.ReadAllAsync(nullPath, CancellationToken.None),
                maxCount: 1).ConfigureAwait(false);
            List<PcapRecord> ethernetRecords = await PcapTestHelpers.ToListAsync(
                PcapFileReader.ReadAllAsync(ethernetPath, CancellationToken.None),
                maxCount: 1).ConfigureAwait(false);

            Assert.That(nullRecords[0].LinkType, Is.EqualTo(PcapFileWriter.LinkTypeNull));
            Assert.That(ethernetRecords[0].LinkType, Is.EqualTo(PcapFileWriter.LinkTypeEthernet));
        }

        [Test]
        public async Task TruncatedPartialHeaderStopsAfterCompleteRecords()
        {
            string path = CreateTempPath("truncated.pcap");
            byte[] payload = [9, 8, 7];
            var writer = new PcapFileWriter(path, PcapFileWriter.LinkTypeEthernet);
            try
            {
                await writer.WriteAsync(DateTimeOffset.UtcNow, payload, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            using (FileStream stream = File.Open(path, FileMode.Append, FileAccess.Write))
            {
                await stream.WriteAsync(new byte[] { 1, 2, 3, 4 }, CancellationToken.None).ConfigureAwait(false);
            }

            List<PcapRecord> records = await PcapTestHelpers.ToListAsync(
                PcapFileReader.ReadAllAsync(path, CancellationToken.None),
                maxCount: 1).ConfigureAwait(false);

            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].Data.ToArray(), Is.EqualTo(payload).AsCollection);
        }

        [Test]
        public void BadMagicThrowsDiagnosticsException()
        {
            string path = CreateTempPath("bad.pcap");
            File.WriteAllBytes(path, new byte[24]);

            Assert.That(
                () => PcapTestHelpers.ToListAsync(PcapFileReader.ReadAllAsync(path, CancellationToken.None)),
                Throws.TypeOf<PcapDiagnosticsException>());
        }

        private static async Task WriteSinglePacketAsync(string path, uint linkType)
        {
            var writer = new PcapFileWriter(path, linkType);
            try
            {
                await writer.WriteAsync(DateTimeOffset.UtcNow, new byte[] { 1 }, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
