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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Formats;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.Models;
using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Tests.Formats
{
    [TestFixture]
    public sealed class CsvFormatterTests
    {
        private const string ExpectedHeader =
            "timestamp,direction,client,server,length,messageType,channelId,tokenId";

        [Test]
        public void MetadataDescribesTextualCsvFormat()
        {
            var formatter = new CsvFormatter();

            Assert.That(formatter.Kind, Is.EqualTo(FormatKind.Csv));
            Assert.That(formatter.MimeType, Is.EqualTo("text/csv"));
            Assert.That(formatter.IsBinary, Is.False);
        }

        [Test]
        public async Task FormatAsyncEmitsHeaderRowEvenWithNoFrames()
        {
            await using var source = new InMemoryCaptureSource();
            var formatter = new CsvFormatter();

            FormatResult result = await formatter.FormatAsync(source, maxFrames: null, CancellationToken.None)
                .ConfigureAwait(false);

            string body = Encoding.UTF8.GetString(result.Bytes);
            Assert.That(result.FramesFormatted, Is.Zero);
            Assert.That(body.TrimEnd('\r', '\n'), Is.EqualTo(ExpectedHeader));
        }

        [Test]
        public async Task FormatAsyncEmitsOneRowPerFrameWithExpectedColumnValues()
        {
            byte[] chunk = BuildMessageChunk(
                fourCcAscii: "MSGF",
                channelId: 0x10203040,
                tokenId: 0x50607080);

            var timestamp = new DateTimeOffset(2026, 3, 4, 5, 6, 7, 89, TimeSpan.Zero);
            CaptureFrame[] frames =
            [
                new CaptureFrame(
                    timestamp,
                    CaptureFrameDirection.ServerToClient,
                    "127.0.0.1:12345",
                    "127.0.0.1:4840",
                    chunk)
            ];
            await using var source = new InMemoryCaptureSource(frames);
            var formatter = new CsvFormatter();

            FormatResult result = await formatter.FormatAsync(source, maxFrames: null, CancellationToken.None)
                .ConfigureAwait(false);

            string body = Encoding.UTF8.GetString(result.Bytes);
            string[] lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.That(result.FramesFormatted, Is.EqualTo(1));
            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines[0].TrimEnd('\r'), Is.EqualTo(ExpectedHeader));

            string row = lines[1].TrimEnd('\r');
            string[] cells = row.Split(',');
            Assert.That(cells, Has.Length.EqualTo(8));
            Assert.That(cells[0], Is.EqualTo("2026-03-04T05:06:07.089Z"));
            Assert.That(cells[1], Is.EqualTo("ServerToClient"));
            Assert.That(cells[2], Is.EqualTo("127.0.0.1:12345"));
            Assert.That(cells[3], Is.EqualTo("127.0.0.1:4840"));
            Assert.That(cells[4], Is.EqualTo(chunk.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(cells[5], Is.EqualTo("MSGF"));
            Assert.That(cells[6], Is.EqualTo(0x10203040U.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(cells[7], Is.EqualTo(0x50607080U.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        [Test]
        public void FormatAsyncRejectsNullSource()
        {
            var formatter = new CsvFormatter();

            Assert.That(
                async () => await formatter.FormatAsync(source: null!, maxFrames: null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
        }

        private static byte[] BuildMessageChunk(string fourCcAscii, uint channelId, uint tokenId)
        {
            byte[] data = new byte[16];
            byte[] ascii = Encoding.ASCII.GetBytes(fourCcAscii);
            ascii.AsSpan(0, 4).CopyTo(data.AsSpan(0, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), (uint)data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8, 4), channelId);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12, 4), tokenId);
            return data;
        }
    }
}
