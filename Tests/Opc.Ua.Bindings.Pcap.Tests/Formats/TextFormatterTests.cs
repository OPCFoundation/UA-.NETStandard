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
using Opc.Ua.Bindings.Pcap.Formats;
using Opc.Ua.Bindings.Pcap.Frame;
using Opc.Ua.Bindings.Pcap.Models;

namespace Opc.Ua.Bindings.Pcap.Tests.Formats
{
    [TestFixture]
    public sealed class TextFormatterTests
    {
        [Test]
        public void MetadataDescribesPlainTextFormat()
        {
            var formatter = new TextFormatter();

            Assert.That(formatter.Kind, Is.EqualTo(FormatKind.Text));
            Assert.That(formatter.MimeType, Is.EqualTo("text/plain"));
            Assert.That(formatter.IsBinary, Is.False);
        }

        [Test]
        public async Task FormatAsyncEmitsOneLinePerFrameWithDirectionArrow()
        {
            byte[] chunk = BuildMessageChunk(
                fourCcAscii: "MSGF",
                channelId: 0x000000AAU,
                tokenId: 0x000000BBU);

            CaptureFrame[] frames =
            [
                new CaptureFrame(
                    new DateTimeOffset(2026, 1, 2, 3, 4, 5, 0, TimeSpan.Zero),
                    CaptureFrameDirection.ClientToServer,
                    string.Empty,
                    string.Empty,
                    chunk),
                new CaptureFrame(
                    new DateTimeOffset(2026, 1, 2, 3, 4, 6, 0, TimeSpan.Zero),
                    CaptureFrameDirection.ServerToClient,
                    string.Empty,
                    string.Empty,
                    chunk)
            ];
            await using var source = new InMemoryCaptureSource(frames);
            var formatter = new TextFormatter();

            FormatResult result = await formatter.FormatAsync(source, maxFrames: null, CancellationToken.None)
                .ConfigureAwait(false);

            string body = Encoding.UTF8.GetString(result.Bytes);
            string[] lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.That(result.FramesFormatted, Is.EqualTo(2));
            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines[0], Does.Contain("C->S"));
            Assert.That(lines[0], Does.Contain("MSGF"));
            Assert.That(lines[0], Does.Contain("channel=170"));
            Assert.That(lines[0], Does.Contain("token=187"));
            Assert.That(lines[1], Does.Contain("S->C"));
        }

        [Test]
        public async Task FormatAsyncEmitsDashesForUnknownDirectionAndTooShortFrame()
        {
            CaptureFrame[] frames =
            [
                new CaptureFrame(
                    new DateTimeOffset(2026, 1, 2, 3, 4, 7, 0, TimeSpan.Zero),
                    CaptureFrameDirection.Unknown,
                    string.Empty,
                    string.Empty,
                    new byte[] { 1, 2, 3 }) // 3 bytes is below the 4-byte minimum for messageType.
            ];
            await using var source = new InMemoryCaptureSource(frames);
            var formatter = new TextFormatter();

            FormatResult result = await formatter.FormatAsync(source, maxFrames: null, CancellationToken.None)
                .ConfigureAwait(false);

            string body = Encoding.UTF8.GetString(result.Bytes);
            Assert.That(body, Does.Contain(" ? "), "Unknown direction is rendered as '?'.");
            Assert.That(body, Does.Contain("- channel=- token=-"),
                "Frames shorter than the OPC UA header use '-' for messageType/channel/token.");
        }

        [Test]
        public void FormatAsyncRejectsNullSource()
        {
            var formatter = new TextFormatter();

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
