/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// Checks the frame codec against the thirteen annotated wire
    /// vectors published with the OPC UA Data Channels errata, and
    /// against the receiver obligations of Part 6 errata 5.1 to 5.6 and
    /// 5.12.
    /// </summary>
    [TestFixture]
    [Category("DataChannels")]
    [Parallelizable(ParallelScope.All)]
    public class DataChannelFrameCodecTests
    {
        private const long FixedTicks = 133_000_000_000_000_000L;
        private const uint BadDataChannelClosedProvisional = 0x81B10000;

        [Test]
        public void SpecVectorInlineDataFirstDecodes()
        {
            byte[] chunk = SpecVectors.Load("inline_data_first");

            Assert.Multiple(() =>
            {
                Assert.That(SpecVectors.MessageType(chunk), Is.EqualTo("STR"));
                Assert.That(SpecVectors.IsFinal(chunk), Is.EqualTo('F'));
                Assert.That(SpecVectors.MessageSize(chunk), Is.EqualTo((uint)chunk.Length));
            });

            Assert.That(
                DataChannelFrameCodec.TryDecode(
                    SpecVectors.Body(chunk, SpecVectors.InlinePrefix),
                    0,
                    out DataChannelFrame frame,
                    out DataChannelFrameError error),
                Is.True,
                error.ToString());

            Assert.Multiple(() =>
            {
                Assert.That(frame.ChannelId, Is.EqualTo(1u));
                Assert.That(frame.FrameType, Is.EqualTo(DataChannelFrameType.Data));
                Assert.That(
                    frame.Flags,
                    Is.EqualTo(DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.Marker));
                Assert.That(frame.FrameSequenceNumber, Is.EqualTo(1u));
                Assert.That(frame.Payload.Length, Is.EqualTo(16));
                Assert.That(frame.Payload.Span[0], Is.Zero);
                Assert.That(frame.Payload.Span[15], Is.EqualTo(0x0F));
            });
        }

        [Test]
        public void SpecVectorInlineDataDroppableCarriesDeadline()
        {
            byte[] chunk = SpecVectors.Load("inline_data_droppable");

            Assert.That(
                DataChannelFrameCodec.TryDecode(
                    SpecVectors.Body(chunk, SpecVectors.InlinePrefix),
                    0,
                    out DataChannelFrame frame,
                    out _),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(frame.ChannelId, Is.EqualTo(2u));
                Assert.That(frame.FrameSequenceNumber, Is.EqualTo(97u));
                Assert.That(frame.HasDeadline, Is.True);
                Assert.That(frame.IsDroppable, Is.True);
                Assert.That(frame.Deadline, Is.EqualTo(FixedTicks));
                Assert.That(frame.Payload.Length, Is.EqualTo(8));
            });
        }

        [Test]
        public void SpecVectorInlineCreditChannelDecodes()
        {
            byte[] chunk = SpecVectors.Load("inline_credit_channel");

            Assert.That(
                DataChannelFrameCodec.TryDecode(
                    SpecVectors.Body(chunk, SpecVectors.InlinePrefix),
                    0,
                    out DataChannelFrame frame,
                    out _),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(frame.FrameType, Is.EqualTo(DataChannelFrameType.Credit));
                Assert.That(frame.ChannelId, Is.EqualTo(2u));
                Assert.That(frame.FrameSequenceNumber, Is.EqualTo(98u));
            });
        }

        [Test]
        public void SpecVectorInlineCreditConnectionGrantsTheConnectionWindow()
        {
            byte[] chunk = SpecVectors.Load("inline_credit_connection");

            Assert.That(
                DataChannelFrameCodec.TryDecode(
                    SpecVectors.Body(chunk, SpecVectors.InlinePrefix),
                    0,
                    out DataChannelFrame frame,
                    out _),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(
                    frame.ChannelId,
                    Is.EqualTo(DataChannelConstants.ConnectionControlChannelId));
                Assert.That(frame.ChannelCredit, Is.Zero);
                Assert.That(frame.ConnectionCredit, Is.EqualTo(262144u));
                Assert.That(frame.FrameSequenceNumber, Is.EqualTo(11u));
            });
        }

        [Test]
        public void SpecVectorInlineGapNamesAnInclusiveRun()
        {
            byte[] chunk = SpecVectors.Load("inline_gap");

            Assert.That(
                DataChannelFrameCodec.TryDecode(
                    SpecVectors.Body(chunk, SpecVectors.InlinePrefix),
                    0,
                    out DataChannelFrame frame,
                    out _),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(frame.FrameType, Is.EqualTo(DataChannelFrameType.Gap));
                Assert.That(frame.FirstDiscarded, Is.EqualTo(99u));
                Assert.That(frame.LastDiscarded, Is.EqualTo(102u));
                Assert.That(frame.FrameSequenceNumber, Is.EqualTo(103u));
            });
        }

        [Test]
        public void SpecVectorInlineResetCarriesItsStatusCode()
        {
            byte[] chunk = SpecVectors.Load("inline_reset");

            Assert.That(
                DataChannelFrameCodec.TryDecode(
                    SpecVectors.Body(chunk, SpecVectors.InlinePrefix),
                    0,
                    out DataChannelFrame frame,
                    out _),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(frame.FrameType, Is.EqualTo(DataChannelFrameType.Reset));
                Assert.That(frame.Status.Code, Is.EqualTo(BadDataChannelClosedProvisional));
                Assert.That(frame.FrameSequenceNumber, Is.EqualTo(104u));
            });
        }

        [Test]
        public void SpecVectorInlineEndCarriesNoFields()
        {
            byte[] chunk = SpecVectors.Load("inline_end");

            Assert.That(
                DataChannelFrameCodec.TryDecode(
                    SpecVectors.Body(chunk, SpecVectors.InlinePrefix),
                    0,
                    out DataChannelFrame frame,
                    out _),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(frame.FrameType, Is.EqualTo(DataChannelFrameType.End));
                Assert.That(frame.ChannelId, Is.EqualTo(1u));
                Assert.That(frame.FrameSequenceNumber, Is.EqualTo(3u));
                Assert.That(frame.Payload.Length, Is.Zero);
            });
        }

        [TestCase("inline_ping", DataChannelFrameType.Ping, 12u)]
        [TestCase("inline_pong", DataChannelFrameType.Pong, 13u)]
        public void SpecVectorProbeCarriesTheFixedTimestamp(
            string name,
            DataChannelFrameType expectedType,
            uint expectedSequenceNumber)
        {
            byte[] chunk = SpecVectors.Load(name);

            Assert.That(
                DataChannelFrameCodec.TryDecode(
                    SpecVectors.Body(chunk, SpecVectors.InlinePrefix),
                    0,
                    out DataChannelFrame frame,
                    out _),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(frame.FrameType, Is.EqualTo(expectedType));
                Assert.That(frame.Timestamp, Is.EqualTo(FixedTicks));
                Assert.That(frame.FrameSequenceNumber, Is.EqualTo(expectedSequenceNumber));
                Assert.That(
                    frame.ChannelId,
                    Is.EqualTo(DataChannelConstants.ConnectionControlChannelId));
            });
        }

        [Test]
        public void SpecVectorInlineDataSignedDecodesWithoutItsFooter()
        {
            byte[] chunk = SpecVectors.Load("inline_data_signed");
            const int footerSize = 1 + 32;

            Assert.That(
                DataChannelFrameCodec.TryDecode(
                    SpecVectors.Body(chunk, SpecVectors.InlinePrefix, footerSize),
                    0,
                    out DataChannelFrame frame,
                    out _),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(frame.ChannelId, Is.EqualTo(1u));
                Assert.That(frame.FrameSequenceNumber, Is.EqualTo(4u));
                Assert.That(frame.Payload.Length, Is.EqualTo(4));
                Assert.That(
                    frame.Flags,
                    Is.EqualTo(
                        DataChannelFrameFlags.MessageStart |
                        DataChannelFrameFlags.MessageEnd));
            });
        }

        [Test]
        public void SpecVectorQuicDataStreamOmitsTheSecurityAndSequenceHeaders()
        {
            byte[] chunk = SpecVectors.Load("quic_data_stream");

            Assert.That(
                DataChannelFrameCodec.TryDecode(
                    SpecVectors.Body(chunk, SpecVectors.QuicPrefix),
                    0,
                    out DataChannelFrame frame,
                    out _),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(frame.ChannelId, Is.EqualTo(1u));
                Assert.That(frame.Payload.Length, Is.EqualTo(16));
                Assert.That(
                    chunk,
                    Has.Length.EqualTo(SpecVectors.Load("inline_data_first").Length - 12),
                    "QUIC framing is twelve bytes shorter than inline framing.");
            });
        }

        [Test]
        public void SpecVectorQuicDatagramCarriesADroppableFrame()
        {
            byte[] chunk = SpecVectors.Load("quic_datagram_unreliable");

            Assert.That(
                DataChannelFrameCodec.TryDecode(
                    SpecVectors.Body(chunk, SpecVectors.QuicPrefix),
                    0,
                    out DataChannelFrame frame,
                    out _),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(frame.ChannelId, Is.EqualTo(3u));
                Assert.That(frame.FrameSequenceNumber, Is.EqualTo(4096u));
                Assert.That(frame.IsDroppable, Is.True);
                Assert.That(frame.HasDeadline, Is.False);
                Assert.That(frame.Payload.Length, Is.EqualTo(6));
            });
        }

        /// <summary>
        /// Encoding into a buffer that cannot hold the frame is refused.
        /// </summary>
        /// <remarks>
        /// The alternative is a silently truncated frame on the wire, which
        /// the peer would read as a framing violation and answer by
        /// resetting a channel that did nothing wrong.
        /// </remarks>
        [Test]
        public void EncodingIntoATooSmallBufferIsRefused()
        {
            DataChannelFrame frame = DataChannelFrame.Data(
                7,
                1,
                DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.MessageEnd,
                new byte[32]);

            byte[] tooSmall = new byte[frame.EncodedSize - 1];

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => DataChannelFrameCodec.Encode(tooSmall, frame))!;

            Assert.That(exception.ParamName, Is.EqualTo("destination"));
        }

        [Test]
        public void EncodeRoundTripsEverySpecVectorByteForByte(
            [Values(
                "inline_data_first",
                "inline_data_final",
                "inline_data_droppable",
                "inline_credit_channel",
                "inline_credit_connection",
                "inline_gap",
                "inline_reset",
                "inline_end",
                "inline_ping",
                "inline_pong",
                "quic_data_stream",
                "quic_datagram_unreliable")]
            string name)
        {
            int prefix = name.StartsWith("quic", StringComparison.Ordinal)
                ? SpecVectors.QuicPrefix
                : SpecVectors.InlinePrefix;

            byte[] chunk = SpecVectors.Load(name);
            ReadOnlyMemory<byte> body = SpecVectors.Body(chunk, prefix);

            Assert.That(
                DataChannelFrameCodec.TryDecode(body, 0, out DataChannelFrame frame, out _),
                Is.True);

            byte[] encoded = new byte[frame.EncodedSize];
            int written = DataChannelFrameCodec.Encode(encoded, frame);

            Assert.Multiple(() =>
            {
                Assert.That(written, Is.EqualTo(body.Length));
                Assert.That(encoded, Is.EqualTo(body.ToArray()));
            });
        }

        // DCF-003: a FrameSequenceNumber of zero is rejected.
        [Test]
        public void DcF003ZeroFrameSequenceNumberIsRejected()
        {
            byte[] body = BuildHeader(1, DataChannelFrameType.Data, 0, 0);

            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelFrameCodec.TryDecode(body, 0, out _, out DataChannelFrameError error),
                    Is.False);
                Assert.That(error, Is.EqualTo(DataChannelFrameError.ZeroFrameSequenceNumber));
                Assert.That(error.IsFatal(), Is.True);
            });
        }

        // DCF-004: a non zero padding field in the stream header is rejected.
        [Test]
        public void DcF004NonZeroHeaderPaddingIsRejected()
        {
            byte[] body = BuildHeader(1, DataChannelFrameType.Data, 0, 1);
            body[6] = 0x01;

            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelFrameCodec.TryDecode(body, 0, out _, out DataChannelFrameError error),
                    Is.False);
                Assert.That(error, Is.EqualTo(DataChannelFrameError.NonZeroHeaderPadding));
                Assert.That(error.IsFatal(), Is.True);
            });
        }

        // DCF-005: a flag bit held back for a future revision is rejected.
        [Test]
        public void DcF005UnknownFlagBitIsRejected()
        {
            byte[] body = BuildHeader(1, DataChannelFrameType.Data, 0x20, 1);

            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelFrameCodec.TryDecode(body, 0, out _, out DataChannelFrameError error),
                    Is.False);
                Assert.That(error, Is.EqualTo(DataChannelFrameError.UnknownFlagBits));
                Assert.That(error.IsFatal(), Is.True);
            });
        }

        // DCF-006: a FrameType held back for a future revision resets the
        // channel rather than closing the SecureChannel.
        [Test]
        public void DcF006UnknownFrameTypeResetsTheChannel()
        {
            byte[] body = BuildHeader(1, (DataChannelFrameType)7, 0, 1);

            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelFrameCodec.TryDecode(body, 0, out _, out DataChannelFrameError error),
                    Is.False);
                Assert.That(error, Is.EqualTo(DataChannelFrameError.UnknownFrameType));
                Assert.That(error.IsFatal(), Is.False);
            });
        }

        // DCF-007: payload on a frame type that carries none is rejected.
        [Test]
        public void DcF007PayloadOnNonDataFrameResetsTheChannel()
        {
            byte[] header = BuildHeader(1, DataChannelFrameType.End, 0, 1);
            byte[] body = new byte[header.Length + 1];
            header.CopyTo(body, 0);

            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelFrameCodec.TryDecode(body, 0, out _, out DataChannelFrameError error),
                    Is.False);
                Assert.That(error, Is.EqualTo(DataChannelFrameError.PayloadOnNonDataFrame));
                Assert.That(error.IsFatal(), Is.False);
            });
        }

        // DCF-008: only CREDIT, PING and PONG are accepted on ChannelId 0.
        [Test]
        public void DcF008DataOnTheControlChannelIsRejected()
        {
            byte[] body = BuildHeader(0, DataChannelFrameType.Data, 0, 1);

            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelFrameCodec.TryDecode(body, 0, out _, out DataChannelFrameError error),
                    Is.False);
                Assert.That(error, Is.EqualTo(DataChannelFrameError.InvalidControlChannelFrame));
            });
        }

        // DCF-009: a frame too short for the header its FrameType and
        // flags imply is rejected.
        [Test]
        public void DcF009TruncatedCreditFrameIsRejected()
        {
            byte[] header = BuildHeader(1, DataChannelFrameType.Credit, 0, 1);
            byte[] body = new byte[header.Length + 4];
            header.CopyTo(body, 0);

            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelFrameCodec.TryDecode(body, 0, out _, out DataChannelFrameError error),
                    Is.False);
                Assert.That(error, Is.EqualTo(DataChannelFrameError.FrameTooShort));
                Assert.That(error.IsFatal(), Is.True);
            });
        }

        [Test]
        public void DcF009TruncatedDeadlineIsRejected()
        {
            byte[] body = BuildHeader(
                1,
                DataChannelFrameType.Data,
                (byte)DataChannelFrameFlags.DeadlinePresent,
                1);

            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelFrameCodec.TryDecode(body, 0, out _, out DataChannelFrameError error),
                    Is.False);
                Assert.That(error, Is.EqualTo(DataChannelFrameError.FrameTooShort));
            });
        }

        // DCF-002: a non zero RequestId is rejected.
        [Test]
        public void DcF002NonZeroRequestIdIsRejected()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelFrameCodec.TryValidateChunkHeaders(
                        TcpMessageType.Stream | TcpMessageType.Final,
                        1,
                        out DataChannelFrameError error),
                    Is.False);
                Assert.That(error, Is.EqualTo(DataChannelFrameError.NonZeroRequestId));
                Assert.That(error.IsFatal(), Is.True);
            });
        }

        // DCF-001 and DCF-030: IsFinal other than 'F' is rejected, and the
        // body is never parsed as an abort Error and Reason.
        [TestCase(TcpMessageType.Intermediate)]
        [TestCase(TcpMessageType.Abort)]
        public void DcF001AndDcF030NonFinalChunkIsRejected(uint chunkType)
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelFrameCodec.TryValidateChunkHeaders(
                        TcpMessageType.Stream | chunkType,
                        0,
                        out DataChannelFrameError error),
                    Is.False);
                Assert.That(error, Is.EqualTo(DataChannelFrameError.InvalidIsFinal));
                Assert.That(error.IsFatal(), Is.True);
            });
        }

        [Test]
        public void FinalStreamChunkIsAccepted()
        {
            Assert.That(
                DataChannelFrameCodec.TryValidateChunkHeaders(
                    TcpMessageType.Stream | TcpMessageType.Final,
                    0,
                    out DataChannelFrameError error),
                Is.True);

            Assert.That(error, Is.EqualTo(DataChannelFrameError.None));
        }

        [Test]
        public void OnlyTheFinalStreamChunkTypeIsValid()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    TcpMessageType.IsValid(TcpMessageType.Stream | TcpMessageType.Final),
                    Is.True);
                Assert.That(
                    TcpMessageType.IsValid(TcpMessageType.Stream | TcpMessageType.Abort),
                    Is.False);
                Assert.That(
                    TcpMessageType.IsValid(TcpMessageType.Stream | TcpMessageType.Intermediate),
                    Is.False);
            });
        }

        [Test]
        public void FrameLargerThanTheNegotiatedBufferIsRejected()
        {
            byte[] header = BuildHeader(1, DataChannelFrameType.Data, 0, 1);
            byte[] body = new byte[header.Length + 64];
            header.CopyTo(body, 0);

            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelFrameCodec.TryDecode(body, 32, out _, out DataChannelFrameError error),
                    Is.False);
                Assert.That(error, Is.EqualTo(DataChannelFrameError.FrameTooLarge));
                Assert.That(error.IsFatal(), Is.True);
            });
        }

        [Test]
        public void BodyShorterThanTheStreamHeaderIsRejected()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelFrameCodec.TryDecode(
                        new byte[11],
                        0,
                        out _,
                        out DataChannelFrameError error),
                    Is.False);
                Assert.That(error, Is.EqualTo(DataChannelFrameError.MalformedHeader));
            });
        }

        [Test]
        public void MaxPayloadMatchesTheSpecifiedOverheads()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelFrameCodec.MaxPayload(
                        DataChannelFramingMode.Inline,
                        8192,
                        0,
                        withDeadline: false),
                    Is.EqualTo(8192 - 36));
                Assert.That(
                    DataChannelFrameCodec.MaxPayload(
                        DataChannelFramingMode.Quic,
                        8192,
                        0,
                        withDeadline: false),
                    Is.EqualTo(8192 - 24));
                Assert.That(
                    DataChannelFrameCodec.MaxPayload(
                        DataChannelFramingMode.Inline,
                        8192,
                        0,
                        withDeadline: true),
                    Is.EqualTo(8192 - 44));
            });
        }

        private static byte[] BuildHeader(
            uint channelId,
            DataChannelFrameType frameType,
            byte flags,
            uint frameSequenceNumber)
        {
            byte[] body = new byte[DataChannelConstants.StreamHeaderSize];
            BitConverter.GetBytes(channelId).CopyTo(body, 0);
            body[4] = (byte)frameType;
            body[5] = flags;
            BitConverter.GetBytes(frameSequenceNumber).CopyTo(body, 8);
            return body;
        }
    }
}
