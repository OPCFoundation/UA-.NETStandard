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
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Formats;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Tests.Formats
{
    [TestFixture]
    public sealed class PcapNgFormatterTests
    {
        [Test]
        public void MetadataDescribesBinaryPcapNgFormat()
        {
            var formatter = new PcapNgFormatter();

            Assert.That(formatter.Kind, Is.EqualTo(FormatKind.PcapNg));
            Assert.That(formatter.MimeType, Is.EqualTo("application/x-pcapng"));
            Assert.That(formatter.IsBinary, Is.True);
        }

        [Test]
        public async Task FormatAsyncWritesShbAndIdbAndEpbBlocksForSuppliedFrames()
        {
            CaptureFrame[] frames =
            [
                new CaptureFrame(
                    new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                    CaptureFrameDirection.ClientToServer,
                    "127.0.0.1:12345",
                    "127.0.0.1:4840",
                    new byte[] { 1, 2, 3, 4 }),
                new CaptureFrame(
                    new DateTimeOffset(2026, 1, 2, 3, 4, 6, TimeSpan.Zero),
                    CaptureFrameDirection.ServerToClient,
                    "127.0.0.1:12345",
                    "127.0.0.1:4840",
                    new byte[] { 5, 6, 7, 8, 9 })
            ];
            await using var source = new InMemoryCaptureSource(
                frames,
                supportedFormats: [FormatKind.PcapNg]);
            var formatter = new PcapNgFormatter();

            FormatResult result = await formatter.FormatAsync(source, maxFrames: null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.Kind, Is.EqualTo(FormatKind.PcapNg));
            Assert.That(result.FramesFormatted, Is.EqualTo(2));
            // SHB magic at offset 0 in little-endian is 0x0A 0x0D 0x0D 0x0A
            uint shbType = BinaryPrimitives.ReadUInt32LittleEndian(result.Bytes.AsSpan(0, 4));
            Assert.That(shbType, Is.EqualTo(0x0A0D0D0AU));
            // IDB block type is 1, located immediately after SHB (which is 28 bytes long)
            uint idbType = BinaryPrimitives.ReadUInt32LittleEndian(result.Bytes.AsSpan(28, 4));
            Assert.That(idbType, Is.EqualTo(0x00000001U));
            // The first EPB starts at offset 48 (SHB 28 + IDB 20).
            uint epbType = BinaryPrimitives.ReadUInt32LittleEndian(result.Bytes.AsSpan(48, 4));
            Assert.That(epbType, Is.EqualTo(0x00000006U));
        }

        [Test]
        public async Task FormatAsyncHonoursMaxFramesLimit()
        {
            CaptureFrame[] frames =
            [
                new CaptureFrame(DateTimeOffset.UtcNow, CaptureFrameDirection.Unknown,
                    string.Empty, string.Empty, new byte[] { 0xAA }),
                new CaptureFrame(DateTimeOffset.UtcNow, CaptureFrameDirection.Unknown,
                    string.Empty, string.Empty, new byte[] { 0xBB }),
                new CaptureFrame(DateTimeOffset.UtcNow, CaptureFrameDirection.Unknown,
                    string.Empty, string.Empty, new byte[] { 0xCC })
            ];
            await using var source = new InMemoryCaptureSource(
                frames,
                supportedFormats: [FormatKind.PcapNg]);
            var formatter = new PcapNgFormatter();

            FormatResult result = await formatter.FormatAsync(source, maxFrames: 2, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.FramesFormatted, Is.EqualTo(2));
        }

        [Test]
        public async Task FormatAsyncHonoursMaxFramesLimitForFileBackedPcapNg()
        {
            string path = Path.Combine(Path.GetTempPath(), $"opcua-{Guid.NewGuid():N}.pcapng");
            try
            {
                using var capture = new MemoryStream();
                var writer = new PcapNgFileWriter(capture, PcapFileWriter.LinkTypeNull);
                try
                {
                    await writer.WriteAsync(
                        new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                        new byte[] { 0xAA },
                        CancellationToken.None).ConfigureAwait(false);
                    await writer.WriteAsync(
                        new DateTimeOffset(2026, 1, 2, 3, 4, 6, TimeSpan.Zero),
                        new byte[] { 0xBB },
                        CancellationToken.None).ConfigureAwait(false);
                    await writer.WriteAsync(
                        new DateTimeOffset(2026, 1, 2, 3, 4, 7, TimeSpan.Zero),
                        new byte[] { 0xCC },
                        CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    await writer.DisposeAsync().ConfigureAwait(false);
                }
                File.WriteAllBytes(path, capture.ToArray());

                await using var source = new InMemoryCaptureSource(
                    supportedFormats: [FormatKind.PcapNg],
                    pcapFilePath: path);
                var formatter = new PcapNgFormatter();

                FormatResult result = await formatter.FormatAsync(
                    source, maxFrames: 2, CancellationToken.None).ConfigureAwait(false);

                Assert.That(result.FramesFormatted, Is.EqualTo(2));
                Assert.That(CountEnhancedPacketBlocks(result.Bytes), Is.EqualTo(2));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void FormatAsyncThrowsWhenSourceDoesNotSupportPcapNgAndHasNoPcapFile()
        {
            var formatter = new PcapNgFormatter();
            // No pcap file path and PcapNg not in SupportedFormats — must throw.
            using var source = new InMemoryCaptureSource(
                supportedFormats: [FormatKind.Json],
                pcapFilePath: null);

            Assert.That(
                async () => await formatter.FormatAsync(source, maxFrames: null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("pcapng is not applicable"));
        }

        [Test]
        public void FormatAsyncRejectsNullSource()
        {
            var formatter = new PcapNgFormatter();

            Assert.That(
                async () => await formatter.FormatAsync(source: null!, maxFrames: null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
        }

        private static int CountEnhancedPacketBlocks(byte[] bytes)
        {
            int count = 0;
            int offset = 0;
            while (offset < bytes.Length)
            {
                uint blockType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
                int blockLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4)));
                if (blockType == 0x00000006)
                {
                    count++;
                }
                offset += blockLength;
            }
            return count;
        }
    }
}
