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
using Opc.Ua.PubSub.Diagnostics;
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

        [Test]
        public async Task PayloadSizeExceedingRemainingFrame_ReturnsNull()
        {
            byte[] frame =
            [
                0x41,
                0x02,
                0x01, 0x00,
                0x02, 0x00,
                0x0A, 0x00,
                0x02, 0x00,
                0x81, 0x03,
                0x81, 0x03
            ];

            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(frame, UadpTestUtilities.NewContext()).ConfigureAwait(false);

            Assert.That(decoded, Is.Null);
        }

        [Test]
        public async Task PayloadSizeSmallerThanDataSetMessage_ReturnsNull()
        {
            byte[] frame =
            [
                0x41,
                0x02,
                0x01, 0x00,
                0x02, 0x00,
                0x01, 0x00,
                0x02, 0x00,
                0x81, 0x03,
                0x81, 0x03
            ];

            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(frame, UadpTestUtilities.NewContext()).ConfigureAwait(false);

            Assert.That(decoded, Is.Null);
        }

        [Test]
        public async Task PayloadSizeDelimitedKeepAliveMessages_Decode()
        {
            byte[] frame =
            [
                0x41,
                0x02,
                0x01, 0x00,
                0x02, 0x00,
                0x02, 0x00,
                0x02, 0x00,
                0x81, 0x03,
                0x81, 0x03
            ];

            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(frame, UadpTestUtilities.NewContext()).ConfigureAwait(false);

            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded!.DataSetMessages, Has.Count.EqualTo(2));
            Assert.That(decoded.DataSetMessages[0].MessageType, Is.EqualTo(PubSubDataSetMessageType.KeepAlive));
            Assert.That(decoded.DataSetMessages[1].MessageType, Is.EqualTo(PubSubDataSetMessageType.KeepAlive));
        }

        [Test]
        public async Task ZeroPayloadSizeFallsBackToUnsizedMessageAndPreservesFollowingMessage()
        {
            byte[] frame =
            [
                0x41,
                0x02,
                0x01, 0x00,
                0x02, 0x00,
                0x00, 0x00,
                0x02, 0x00,
                0x81, 0x03,
                0x81, 0x03
            ];

            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(frame, UadpTestUtilities.NewContext()).ConfigureAwait(false);

            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded!.DataSetMessages, Has.Count.EqualTo(2));
            Assert.That(decoded.DataSetMessages[0].DataSetWriterId, Is.EqualTo((ushort)1));
            Assert.That(decoded.DataSetMessages[0].MessageType, Is.EqualTo(PubSubDataSetMessageType.KeepAlive));
            Assert.That(decoded.DataSetMessages[1].DataSetWriterId, Is.EqualTo((ushort)2));
            Assert.That(decoded.DataSetMessages[1].MessageType, Is.EqualTo(PubSubDataSetMessageType.KeepAlive));
        }

        [TestCase((byte)0x0C)]
        [TestCase((byte)0x10)]
        [TestCase((byte)0x14)]
        [TestCase((byte)0x1C)]
        [TestCase((byte)0x40)]
        [TestCase((byte)0x80)]
        public void ReservedExtendedFlags2ValuesAreRejected(byte ext2)
        {
            // Probe-shaped body after the header so that a lenient decoder
            // would accept the frame.
            byte[] frame = [0x91, 0x80, ext2, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x81, 0x03];
            Assert.That(UadpDecoder.Decode(frame, UadpTestUtilities.NewContext()), Is.Null);
            Assert.That(UadpDecoder.TryReadOuterPrefix(frame, out _, out _, out _, out _), Is.False);
        }

        [Test]
        public void PayloadHeaderWithZeroCountReturnsNull()
        {
            // UADPFlags 0x41 (PayloadHeader), Count 0, then a keep-alive
            // DataSetMessage that must not be decoded.
            byte[] frame = [0x41, 0x00, 0x81, 0x03];
            Assert.That(UadpDecoder.Decode(frame, UadpTestUtilities.NewContext()), Is.Null);
            Assert.That(UadpDecoder.TryReadOuterPrefix(frame, out _, out _, out _, out _), Is.False);
        }

        [Test]
        public void AbsentPublisherIdDecodesAsNullInBothPaths()
        {
            byte[] frame = [0x01, 0x81, 0x03];
            PubSubNetworkMessage? decoded = UadpDecoder.Decode(frame, UadpTestUtilities.NewContext());
            Assert.That(decoded, Is.Not.Null);
            Assert.That(
                UadpDecoder.TryReadOuterPrefix(frame, out _, out _, out PublisherId prefixPublisherId, out _),
                Is.True);
            Assert.That(decoded!.PublisherId.IsNull, Is.True);
            Assert.That(decoded.PublisherId, Is.EqualTo(prefixPublisherId));
            Assert.That(
                ((UadpNetworkMessage)decoded).ContentMask.HasFlag(UadpNetworkMessageContentMask.PublisherId),
                Is.False);
        }

        [Test]
        public void InvalidDataSetMessageIsSkippedWhenSizeIsKnown()
        {
            byte[] frame =
            [
                0x41,
                0x02,
                0x01, 0x00,
                0x02, 0x00,
                0x02, 0x00,
                0x02, 0x00,
                0x80, 0x03, // DataSetFlags1 bit 0 (valid) clear
                0x81, 0x03
            ];

            PubSubNetworkMessage? decoded = UadpDecoder.Decode(frame, UadpTestUtilities.NewContext());

            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded!.DataSetMessages, Has.Count.EqualTo(1));
            Assert.That(decoded.DataSetMessages[0].DataSetWriterId, Is.EqualTo((ushort)2));
        }

        [Test]
        public void InvalidSingleDataSetMessageIsNotDecoded()
        {
            // No PayloadHeader, one DataSetMessage whose valid bit is clear,
            // followed by bytes that are not a valid field payload.
            byte[] frame = [0x01, 0x80, 0x00, 0xFF, 0xFF];

            PubSubNetworkMessage? decoded = UadpDecoder.Decode(frame, UadpTestUtilities.NewContext());

            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded!.DataSetMessages, Is.Empty);
        }

        [Test]
        public void DiscoveryRequestRejectsWriterIdCountBeyondRemainingBytes()
        {
            // UADPFlags 0x91, ExtFlags1 0x80, ExtFlags2 0x04 (probe),
            // PublisherId 0x00, DiscoveryType 1, count 0x7FFFFFC0 with no ids.
            byte[] frame = [0x91, 0x80, 0x04, 0x00, 0x01, 0xC0, 0xFF, 0xFF, 0x7F];
            PubSubNetworkMessage? decoded = null;
            Assert.DoesNotThrow(() => decoded = UadpDecoder.Decode(frame, UadpTestUtilities.NewContext()));
            Assert.That(decoded, Is.Null);
        }

        [Test]
        public void DiscoveryResponseRejectsEndpointCountBeyondRemainingBytes()
        {
            // ExtFlags2 0x08 (announcement), type PublisherEndpoints,
            // sequence 0, endpoint count 0x7FFFFFFF with no endpoints.
            byte[] frame = [0x91, 0x80, 0x08, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x7F];
            PubSubNetworkMessage? decoded = null;
            Assert.DoesNotThrow(() => decoded = UadpDecoder.Decode(frame, UadpTestUtilities.NewContext()));
            Assert.That(decoded, Is.Null);
        }

        [Test]
        public void DiscoveryResponseRejectsWriterIdCountBeyondRemainingBytes()
        {
            // Type DataSetWriterConfiguration, count 3 but only one id present.
            byte[] frame = [0x91, 0x80, 0x08, 0x00, 0x03, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00];
            PubSubNetworkMessage? decoded = null;
            Assert.DoesNotThrow(() => decoded = UadpDecoder.Decode(frame, UadpTestUtilities.NewContext()));
            Assert.That(decoded, Is.Null);
        }

        [Test]
        public void DiscoveryRequestRejectsWriterIdCountAboveMaxArrayLength()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            ((ServiceMessageContext)context.MessageContext).MaxArrayLength = 1;
            // Two writer ids present, but MaxArrayLength is 1.
            byte[] frame = [0x91, 0x80, 0x04, 0x00, 0x01, 0x02, 0x00, 0x00, 0x00, 0x01, 0x00, 0x02, 0x00];
            PubSubNetworkMessage? decoded = UadpDecoder.Decode(frame, context);
            Assert.That(decoded, Is.Null);
        }

        [Test]
        public void DiscoveryProbeWithTruncatedTransportProfileArrayReturnsNull()
        {
            // Probe type 6, 0 writer ids, three null strings, filter bytes,
            // TransportProfileUris count 5 with no entries.
            byte[] frame =
            [
                0x91, 0x80, 0x04, 0x00, 0x06,
                0x00, 0x00, 0x00, 0x00,
                0xFF, 0xFF, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFF, 0xFF,
                0x00, 0x00, 0x00,
                0x05, 0x00, 0x00, 0x00
            ];
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            PubSubNetworkMessage? decoded = null;
            Assert.DoesNotThrow(() => decoded = UadpDecoder.Decode(frame, context));
            Assert.That(decoded, Is.Null);
            Assert.That(
                context.Diagnostics.Read(PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages),
                Is.EqualTo(1));
        }
    }
}
