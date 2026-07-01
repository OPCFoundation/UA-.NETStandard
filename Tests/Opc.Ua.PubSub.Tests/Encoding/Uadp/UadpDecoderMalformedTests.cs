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
 *
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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;

namespace Opc.Ua.PubSub.Tests.Encoding.Uadp
{
    /// <summary>
    /// Malformed-input coverage for <see cref="UadpDecoder"/>. Every
    /// rejection path must produce <c>null</c> rather than throwing.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.5")]
    public class UadpDecoderMalformedTests
    {
        [Test]
        public async Task EmptyFrame_ReturnsNull()
        {
            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(ReadOnlyMemory<byte>.Empty, UadpTestUtilities.NewContext())
                .ConfigureAwait(false);
            Assert.That(decoded, Is.Null);
        }

        [Test]
        public async Task InvalidVersion_ReturnsNull()
        {
            // First byte's low nibble is the version. Use version=2 (unsupported).
            byte[] frame = [0x02, 0x00, 0x00];
            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(frame, UadpTestUtilities.NewContext()).ConfigureAwait(false);
            Assert.That(decoded, Is.Null);
        }

        [Test]
        public async Task TruncatedExt1_ReturnsNull()
        {
            // version=1, ExtendedFlags1Enabled set, but no ext1 byte present
            byte[] frame = [0x81];
            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(frame, UadpTestUtilities.NewContext()).ConfigureAwait(false);
            Assert.That(decoded, Is.Null);
        }

        [Test]
        public async Task TruncatedExt2_ReturnsNull()
        {
            // version=1, ExtendedFlags1Enabled set, ext1=0x80 (ExtendedFlags2Enabled), no ext2 byte
            byte[] frame = [0x81, 0x80];
            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(frame, UadpTestUtilities.NewContext()).ConfigureAwait(false);
            Assert.That(decoded, Is.Null);
        }

        [Test]
        public async Task UnsupportedPublisherIdType_ReturnsNull()
        {
            // version=1, PublisherIdEnabled set + ExtendedFlags1Enabled,
            // ext1 has low 3 bits = 7 (no such type)
            byte[] frame = [0x91, 0x07, 0x00];
            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(frame, UadpTestUtilities.NewContext()).ConfigureAwait(false);
            Assert.That(decoded, Is.Null);
        }

        [Test]
        public async Task TruncatedPublisherId_ReturnsNull()
        {
            // version=1, PublisherIdEnabled but no payload byte
            byte[] frame = [0x11];
            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(frame, UadpTestUtilities.NewContext()).ConfigureAwait(false);
            Assert.That(decoded, Is.Null);
        }

        [Test]
        public async Task TruncatedDataSetClassId_ReturnsNull()
        {
            // version=1, ext1=DataSetClassIdEnabled but no 16-byte guid
            byte[] frame = [0x81, 0x08, 0xAA, 0xBB];
            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(frame, UadpTestUtilities.NewContext()).ConfigureAwait(false);
            Assert.That(decoded, Is.Null);
        }

        [Test]
        public async Task TruncatedGroupFlags_ReturnsNull()
        {
            // version=1, GroupHeaderEnabled but no group flags byte
            byte[] frame = [0x21];
            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(frame, UadpTestUtilities.NewContext()).ConfigureAwait(false);
            Assert.That(decoded, Is.Null);
        }

        [Test]
        public async Task TruncatedPayloadHeader_ReturnsNull()
        {
            // version=1, PayloadHeaderEnabled but no count
            byte[] frame = [0x41];
            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(frame, UadpTestUtilities.NewContext()).ConfigureAwait(false);
            Assert.That(decoded, Is.Null);
        }

        [Test]
        public async Task TruncatedPayloadWriterIds_ReturnsNull()
        {
            // version=1, PayloadHeaderEnabled, count=3 but only 2 bytes for IDs
            byte[] frame = [0x41, 0x03, 0x01, 0x00];
            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(frame, UadpTestUtilities.NewContext()).ConfigureAwait(false);
            Assert.That(decoded, Is.Null);
        }

        [Test]
        public async Task TruncatedDataSetMessageFlags_ReturnsNull()
        {
            // version=1, PublisherIdEnabled, byte publisherId — but then nothing for DataSet message
            byte[] frame = [0x11, 0x05];
            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(frame, UadpTestUtilities.NewContext()).ConfigureAwait(false);
            Assert.That(decoded, Is.Null);
        }

        [Test]
        public async Task ChunkedMessage_ReturnsNull()
        {
            // version=1, ext1+ext2 with ChunkMessage bit set
            byte[] frame = [0x81, 0x80, 0x01];
            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(frame, UadpTestUtilities.NewContext()).ConfigureAwait(false);
            Assert.That(decoded, Is.Null);
        }

        [Test]
        public void NullContext_Throws()
        {
            Assert.That(
                async () => await new UadpDecoder()
                    .TryDecodeAsync(new byte[] { 0x01 }, null!).ConfigureAwait(false),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void CancelledToken_Throws()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.That(
                async () => await new UadpDecoder().TryDecodeAsync(
                    new byte[] { 0x01 }, UadpTestUtilities.NewContext(), cts.Token)
                    .ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void Decode_NullContext_Throws()
        {
            Assert.That(
                () => UadpDecoder.Decode(new byte[] { 0x01 }, null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void Decoder_HasProfileUri()
        {
            Assert.That(new UadpDecoder().TransportProfileUri,
                Is.EqualTo(Profiles.PubSubUdpUadpTransport));
        }

        [Test]
        public async Task PromotedFieldsBlockOversized_ReturnsNull()
        {
            // version=1, ext1+ext2 with PromotedFields bit, advertise giant block.
            // ext2 bit 0x02 = PromotedFields. Then 16-bit size 0xFFFF.
            byte[] frame = [0x81, 0x80, 0x02, 0xFF, 0xFF];
            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(frame, UadpTestUtilities.NewContext()).ConfigureAwait(false);
            Assert.That(decoded, Is.Null);
        }
    }
}
