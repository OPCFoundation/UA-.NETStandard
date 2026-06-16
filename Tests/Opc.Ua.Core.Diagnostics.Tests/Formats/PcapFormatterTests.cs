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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Formats;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.Models;

using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Tests.Formats
{
    [TestFixture]
    public sealed class PcapFormatterTests : TempDirectoryFixture
    {
        [Test]
        public void MetadataDescribesBinaryPcapFormat()
        {
            var formatter = new PcapFormatter();

            Assert.That(formatter.Kind, Is.EqualTo(FormatKind.Pcap));
            Assert.That(formatter.MimeType, Is.EqualTo("application/vnd.tcpdump.pcap"));
            Assert.That(formatter.IsBinary, Is.True);
        }

        [Test]
        public async Task FormatAsyncReturnsRawPcapBytesFromSourceFile()
        {
            string pcapPath = CreateTempPath("source.pcap");
            byte[] payload = [0xDE, 0xAD, 0xBE, 0xEF];
            var writer = new PcapFileWriter(pcapPath, PcapFileWriter.LinkTypeNull);
            try
            {
                await writer.WriteAsync(DateTimeOffset.UtcNow, payload, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            await using var source = new InMemoryCaptureSource(
                pcapFilePath: pcapPath,
                supportedFormats: new[] { FormatKind.Pcap });
            var formatter = new PcapFormatter();

            FormatResult result = await formatter.FormatAsync(source, maxFrames: null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.Kind, Is.EqualTo(FormatKind.Pcap));
            Assert.That(result.MimeType, Is.EqualTo("application/vnd.tcpdump.pcap"));
            Assert.That(result.Bytes, Is.EqualTo(await File.ReadAllBytesAsync(pcapPath, CancellationToken.None)
                .ConfigureAwait(false)).AsCollection);
            // libpcap magic in little-endian write order is 0xD4 0xC3 0xB2 0xA1.
            Assert.That(result.Bytes[0], Is.EqualTo(0xD4));
            Assert.That(result.Bytes[1], Is.EqualTo(0xC3));
            Assert.That(result.Bytes[2], Is.EqualTo(0xB2));
            Assert.That(result.Bytes[3], Is.EqualTo(0xA1));
        }

        [Test]
        public void FormatAsyncThrowsWhenSourceProducesNoPcapFile()
        {
            var formatter = new PcapFormatter();
            using var source = new InMemoryCaptureSource(pcapFilePath: null);

            Assert.That(
                async () => await formatter.FormatAsync(source, maxFrames: null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("Source does not produce a libpcap file"));
        }

        [Test]
        public void FormatAsyncRejectsNullSource()
        {
            var formatter = new PcapFormatter();

            Assert.That(
                async () => await formatter.FormatAsync(source: null!, maxFrames: null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
        }
    }
}
