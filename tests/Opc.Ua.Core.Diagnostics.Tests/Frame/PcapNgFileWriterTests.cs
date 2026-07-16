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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Frame;

namespace Opc.Ua.Pcap.Tests.Frame
{
    /// <summary>
    /// Byte-level tests for <see cref="PcapNgFileWriter"/>. The writer is
    /// minimal (one interface, EPB blocks) so the tests assert on the
    /// pcap-ng magic numbers and block-length fields directly rather than
    /// reading the file back with a parser.
    /// </summary>
    [TestFixture]
    public sealed class PcapNgFileWriterTests
    {
        /// <summary>
        /// pcap-ng block-type magic numbers (little-endian on disk).
        /// </summary>
        private const uint BlockTypeShb = 0x0A0D0D0A;
        private const uint BlockTypeIdb = 0x00000001;
        private const uint BlockTypeEpb = 0x00000006;
        private const uint ByteOrderMagic = 0x1A2B3C4D;

        [Test]
        public void ConstructorRejectsNullStream()
        {
            Assert.That(
                () => new PcapNgFileWriter(stream: null!, linkType: 1),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("stream"));
        }

        [Test]
        public async Task WritesSectionHeaderAndInterfaceDescriptionUpFront()
        {
            using var memory = new MemoryStream();
            var writer = new PcapNgFileWriter(memory, linkType: 1);
            await writer.DisposeAsync().ConfigureAwait(false);

            byte[] data = memory.ToArray();

            // SHB is 28 bytes followed by IDB which is 20 bytes.
            Assert.That(data, Has.Length.EqualTo(48),
                "Empty writer must emit exactly one SHB (28 bytes) + one IDB (20 bytes).");
            Assert.That(ReadUInt32(data, 0), Is.EqualTo(BlockTypeShb),
                "First 4 bytes must be the Section Header Block magic 0x0A0D0D0A.");
            Assert.That(ReadUInt32(data, 4), Is.EqualTo(28U),
                "SHB total length field must equal the block size (28).");
            Assert.That(ReadUInt32(data, 8), Is.EqualTo(ByteOrderMagic),
                "SHB byte-order magic must be 0x1A2B3C4D.");
            Assert.That(ReadUInt32(data, 24), Is.EqualTo(28U),
                "SHB trailer total-length field must match the header.");
            Assert.That(ReadUInt32(data, 28), Is.EqualTo(BlockTypeIdb),
                "After the SHB comes an Interface Description Block (type 0x00000001).");
            Assert.That(ReadUInt32(data, 32), Is.EqualTo(20U),
                "IDB total-length field must equal the block size (20).");
            Assert.That(ReadUInt32(data, 44), Is.EqualTo(20U),
                "IDB trailer total-length field must match the header.");
        }

        [Test]
        public async Task IdbCarriesSuppliedLinkType()
        {
            using var memory = new MemoryStream();
            var writer = new PcapNgFileWriter(memory, linkType: 228); // LinkTypeIPv4
            await writer.DisposeAsync().ConfigureAwait(false);

            byte[] data = memory.ToArray();

            // IDB starts at offset 28. Link type is a uint16 at offset 8 inside the block.
            ushort linkType = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(28 + 8, 2));
            Assert.That(linkType, Is.EqualTo((ushort)228));
        }

        [Test]
        public async Task WriteAsyncEmitsEnhancedPacketBlocksOfExpectedSize()
        {
            using var memory = new MemoryStream();
            var writer = new PcapNgFileWriter(memory, linkType: 1);
            try
            {
                // 6-byte payload triggers 2 bytes of padding so the total
                // block length is 8 (block type + length)
                //                + 20 (interface id + ts hi/lo + cap len + orig len)
                //                + 6  (payload)
                //                + 2  (padding)
                //                + 4  (trailer length)
                //                = 40 bytes.
                byte[] payload = [1, 2, 3, 4, 5, 6];
                await writer.WriteAsync(
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    payload,
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            byte[] data = memory.ToArray();
            // Total = 28 (SHB) + 20 (IDB) + 40 (EPB) = 88 bytes.
            Assert.That(data, Has.Length.EqualTo(88));

            const int epbStart = 28 + 20;
            Assert.That(ReadUInt32(data, epbStart), Is.EqualTo(BlockTypeEpb),
                "Per-packet block must be Enhanced Packet Block type 0x00000006.");
            Assert.That(ReadUInt32(data, epbStart + 4), Is.EqualTo(40U),
                "EPB total-length field must equal the on-disk block size.");
            Assert.That(ReadUInt32(data, epbStart + 20), Is.EqualTo(6U),
                "EPB CapturedLength must equal the original payload length.");
            Assert.That(ReadUInt32(data, epbStart + 24), Is.EqualTo(6U),
                "EPB OriginalLength must equal the original payload length.");
            byte[] embedded = new byte[6];
            Array.Copy(data, epbStart + 28, embedded, 0, 6);
            Assert.That(embedded, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }).AsCollection,
                "Payload bytes must be embedded verbatim after the 28-byte EPB header.");
            Assert.That(ReadUInt32(data, epbStart + 36), Is.EqualTo(40U),
                "EPB trailer total-length field must match the header.");
        }

        [Test]
        public async Task WriteAsyncAlignedPayloadHasNoPaddingBytes()
        {
            using var memory = new MemoryStream();
            var writer = new PcapNgFileWriter(memory, linkType: 1);
            try
            {
                // 8-byte payload is already a multiple of 4 → 0 padding bytes
                // → EPB length = 8 + 20 + 8 + 0 + 4 = 40 bytes.
                byte[] payload = [0xA, 0xB, 0xC, 0xD, 0xE, 0xF, 0x10, 0x11];
                await writer.WriteAsync(
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    payload,
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            byte[] data = memory.ToArray();
            Assert.That(data, Has.Length.EqualTo(88));
            Assert.That(ReadUInt32(data, 48 + 4), Is.EqualTo(40U),
                "EPB total length for 8-byte aligned payload must be 40 (no padding).");
        }

        [Test]
        public async Task WriteAsyncMultipleRecordsAppendInOrder()
        {
            using var memory = new MemoryStream();
            var writer = new PcapNgFileWriter(memory, linkType: 1);
            try
            {
                await writer.WriteAsync(
                    DateTimeOffset.UnixEpoch.AddSeconds(1),
                    new byte[] { 1, 2, 3, 4 },
                    CancellationToken.None).ConfigureAwait(false);
                await writer.WriteAsync(
                    DateTimeOffset.UnixEpoch.AddSeconds(2),
                    new byte[] { 5, 6, 7, 8 },
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            byte[] data = memory.ToArray();
            // 28 (SHB) + 20 (IDB) + 2 × 36 (EPB without padding for 4-byte payload) = 120 bytes.
            // EPB length = 8 + 20 + 4 + 0 + 4 = 36.
            Assert.That(data, Has.Length.EqualTo(120));

            const int firstEpbStart = 48;
            const int secondEpbStart = firstEpbStart + 36;
            Assert.That(ReadUInt32(data, firstEpbStart), Is.EqualTo(BlockTypeEpb));
            Assert.That(ReadUInt32(data, firstEpbStart + 4), Is.EqualTo(36U));
            Assert.That(ReadUInt32(data, secondEpbStart), Is.EqualTo(BlockTypeEpb));
            Assert.That(ReadUInt32(data, secondEpbStart + 4), Is.EqualTo(36U));

            // Embedded payloads must remain in write order.
            Assert.That(data[firstEpbStart + 28], Is.EqualTo((byte)1));
            Assert.That(data[secondEpbStart + 28], Is.EqualTo((byte)5));
        }

        [Test]
        public async Task DisposeAsyncIsIdempotent()
        {
            using var memory = new MemoryStream();
            var writer = new PcapNgFileWriter(memory, linkType: 1);

            await writer.DisposeAsync().ConfigureAwait(false);
            Assert.That(
                async () => await writer.DisposeAsync().ConfigureAwait(false),
                Throws.Nothing);
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
        }
    }
}
