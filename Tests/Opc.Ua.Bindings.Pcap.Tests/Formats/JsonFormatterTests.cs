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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings.Pcap.Formats;
using Opc.Ua.Bindings.Pcap.Frame;
using Opc.Ua.Bindings.Pcap.Models;

namespace Opc.Ua.Bindings.Pcap.Tests.Formats
{
    [TestFixture]
    public sealed class JsonFormatterTests
    {
        [Test]
        public void MetadataDescribesTextualJsonFormat()
        {
            var formatter = new JsonFormatter();

            Assert.That(formatter.Kind, Is.EqualTo(FormatKind.Json));
            Assert.That(formatter.MimeType, Is.EqualTo("application/json"));
            Assert.That(formatter.IsBinary, Is.False);
        }

        [Test]
        public async Task FormatAsyncSerializesEveryFrameAndCountsThem()
        {
            byte[] chunk = BuildMessageChunk(
                messageType: 0x46534D, // "MSF" - placeholder; FrameFormatHelpers parses the literal 4-byte ascii prefix.
                channelId: 0x11112222,
                tokenId: 0x33334444);

            CaptureFrame[] frames = new[]
            {
                new CaptureFrame(
                    new DateTimeOffset(2026, 6, 7, 8, 9, 10, TimeSpan.Zero),
                    CaptureFrameDirection.ClientToServer,
                    "client:1",
                    "server:1",
                    chunk),
                new CaptureFrame(
                    new DateTimeOffset(2026, 6, 7, 8, 9, 11, TimeSpan.Zero),
                    CaptureFrameDirection.ServerToClient,
                    "client:1",
                    "server:1",
                    chunk)
            };
            await using var source = new InMemoryCaptureSource(frames);
            var formatter = new JsonFormatter();

            FormatResult result = await formatter.FormatAsync(source, maxFrames: null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.Kind, Is.EqualTo(FormatKind.Json));
            Assert.That(result.MimeType, Is.EqualTo("application/json"));
            Assert.That(result.FramesFormatted, Is.EqualTo(2));

            string json = Encoding.UTF8.GetString(result.Bytes);
            using JsonDocument doc = JsonDocument.Parse(json);
            Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(doc.RootElement.GetArrayLength(), Is.EqualTo(2));
            JsonElement first = doc.RootElement[0];
            Assert.That(first.GetProperty("Direction").GetString(), Is.EqualTo("ClientToServer"));
            Assert.That(first.GetProperty("Client").GetString(), Is.EqualTo("client:1"));
            Assert.That(first.GetProperty("Server").GetString(), Is.EqualTo("server:1"));
            Assert.That(first.GetProperty("Length").GetInt32(), Is.EqualTo(chunk.Length));
            Assert.That(first.GetProperty("ChannelId").GetUInt32(), Is.EqualTo(0x11112222u));
            Assert.That(first.GetProperty("TokenId").GetUInt32(), Is.EqualTo(0x33334444u));
        }

        [Test]
        public async Task FormatAsyncReturnsEmptyJsonArrayWhenNoFrames()
        {
            await using var source = new InMemoryCaptureSource();
            var formatter = new JsonFormatter();

            FormatResult result = await formatter.FormatAsync(source, maxFrames: null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.FramesFormatted, Is.Zero);
            Assert.That(Encoding.UTF8.GetString(result.Bytes), Is.EqualTo("[]"));
        }

        [Test]
        public void FormatAsyncRejectsNullSource()
        {
            var formatter = new JsonFormatter();

            Assert.That(
                async () => await formatter.FormatAsync(source: null!, maxFrames: null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
        }

        private static byte[] BuildMessageChunk(uint messageType, uint channelId, uint tokenId)
        {
            // 16-byte symmetric header so FrameFormatHelpers can extract
            // channel and token. We only need the byte layout, not a
            // valid OPC UA message.
            byte[] data = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), messageType);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), (uint)data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8, 4), channelId);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12, 4), tokenId);
            return data;
        }
    }
}
