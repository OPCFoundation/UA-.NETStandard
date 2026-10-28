// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers;
using NUnit.Framework;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    /// <summary>
    /// Verifies QUIC variable-length integer and bounded HTTP/3 frame-header codecs.
    /// </summary>
    [TestFixture]
    public sealed class Http3VarIntTests
    {
        /// <summary>
        /// Encodes and decodes each representation boundary and the largest value.
        /// </summary>
        [TestCase(0UL, 1)]
        [TestCase(63UL, 1)]
        [TestCase(64UL, 2)]
        [TestCase(16383UL, 2)]
        [TestCase(16384UL, 4)]
        [TestCase(1073741823UL, 4)]
        [TestCase(1073741824UL, 8)]
        [TestCase(Http3VarInt.MaximumValue, 8)]
        public void VariableLengthInteger_RepresentationBoundaries_RoundTrip(ulong value, int expectedLength)
        {
            Span<byte> encoded = stackalloc byte[8];

            Assert.That(Http3VarInt.TryWrite(encoded, value, out int bytesWritten), Is.True);
            Assert.That(bytesWritten, Is.EqualTo(expectedLength));

            SequenceReader<byte> reader = new(new ReadOnlySequence<byte>(encoded[..bytesWritten].ToArray()));
            Assert.That(Http3VarInt.TryRead(ref reader, out ulong decoded), Is.True);
            Assert.That(decoded, Is.EqualTo(value));
            Assert.That(reader.Consumed, Is.EqualTo(expectedLength));
        }

        /// <summary>
        /// Emits the QUIC length prefix and network-order value for each representation width.
        /// </summary>
        [TestCase(0UL, "00")]
        [TestCase(63UL, "3F")]
        [TestCase(64UL, "4040")]
        [TestCase(16383UL, "7FFF")]
        [TestCase(16384UL, "80004000")]
        [TestCase(1073741823UL, "BFFFFFFF")]
        [TestCase(1073741824UL, "C000000040000000")]
        [TestCase(Http3VarInt.MaximumValue, "FFFFFFFFFFFFFFFF")]
        public void VariableLengthInteger_RepresentationBoundaries_MatchesWireEncoding(
            ulong value,
            string expectedHex)
        {
            Span<byte> encoded = stackalloc byte[8];

            Assert.That(Http3VarInt.TryWrite(encoded, value, out int bytesWritten), Is.True);
            Assert.That(encoded[..bytesWritten].ToArray(), Is.EqualTo(Convert.FromHexString(expectedHex)));
        }

        /// <summary>
        /// Accepts valid non-minimal encodings because QUIC integers are length-prefixed on the wire.
        /// </summary>
        [TestCase(new byte[] { 0x40, 0x01 }, 1UL)]
        [TestCase(new byte[] { 0x80, 0x00, 0x00, 0x01 }, 1UL)]
        public void VariableLengthInteger_NonMinimalEncoding_Decodes(byte[] encoded, ulong expected)
        {
            SequenceReader<byte> reader = new(new ReadOnlySequence<byte>(encoded));

            Assert.That(Http3VarInt.TryRead(ref reader, out ulong value), Is.True);
            Assert.That(value, Is.EqualTo(expected));
            Assert.That(reader.Consumed, Is.EqualTo(encoded.Length));
        }

        /// <summary>
        /// Preserves the reader position when the integer is truncated at any encoded length.
        /// </summary>
        [TestCase(new byte[] { })]
        [TestCase(new byte[] { 0x40 })]
        [TestCase(new byte[] { 0x80, 0x01, 0x02 })]
        [TestCase(new byte[] { 0xc0, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 })]
        public void VariableLengthInteger_TruncatedInput_DoesNotConsume(byte[] encoded)
        {
            SequenceReader<byte> reader = new(new ReadOnlySequence<byte>(encoded));

            Assert.That(Http3VarInt.TryRead(ref reader, out _), Is.False);
            Assert.That(reader.Consumed, Is.Zero);
        }

        /// <summary>
        /// Refuses unrepresentable values and undersized output buffers without writing bytes.
        /// </summary>
        [Test]
        public void VariableLengthInteger_InvalidValueOrCapacity_DoesNotWrite()
        {
            Span<byte> output = stackalloc byte[8];
            output.Fill(0xaa);

            Assert.That(Http3VarInt.TryWrite(output, Http3VarInt.MaximumValue + 1, out int invalidBytes), Is.False);
            Assert.That(invalidBytes, Is.Zero);
            Assert.That(output.ToArray(), Is.All.EqualTo(0xaa));
            Assert.That(Http3VarInt.TryWrite(output[..1], 64, out int shortBytes), Is.False);
            Assert.That(shortBytes, Is.Zero);
            Assert.That(output.ToArray(), Is.All.EqualTo(0xaa));
        }

        /// <summary>
        /// Parses split frame headers and consumes only the complete type and length pair.
        /// </summary>
        [Test]
        public void FrameHeader_SplitAtEveryOffset_ParsesOnlyWhenComplete()
        {
            byte[] encoded = [0x01, 0x40, 0x40];

            for (int split = 0; split < encoded.Length; split++)
            {
                ReadOnlySequence<byte> input = MakeSplitSequence(encoded, split);
                SequenceReader<byte> reader = new(input);

                Http3FrameHeaderReadStatus status = Http3FrameHeader.TryRead(ref reader, 64, out var header);

                Assert.That(status, Is.EqualTo(Http3FrameHeaderReadStatus.Success), $"split={split}");
                Assert.That(header, Is.EqualTo(new Http3FrameHeader(1, 64)));
                Assert.That(reader.Consumed, Is.EqualTo(encoded.Length));
            }
        }

        /// <summary>
        /// Leaves the reader untouched when a frame header is incomplete or exceeds its payload limit.
        /// </summary>
        [Test]
        public void FrameHeader_IncompleteOrOversized_DoesNotConsume()
        {
            byte[][] inputs = [[0x01], [0x01, 0x40], [0x01, 0x40, 0x41]];
            Http3FrameHeaderReadStatus[] expected =
            [
                Http3FrameHeaderReadStatus.NeedMoreData,
                Http3FrameHeaderReadStatus.NeedMoreData,
                Http3FrameHeaderReadStatus.PayloadTooLarge
            ];

            for (int index = 0; index < inputs.Length; index++)
            {
                SequenceReader<byte> reader = new(new ReadOnlySequence<byte>(inputs[index]));

                Assert.That(
                    Http3FrameHeader.TryRead(ref reader, 64, out _),
                    Is.EqualTo(expected[index]),
                    $"case={index}");
                Assert.That(reader.Consumed, Is.Zero, $"case={index}");
            }
        }

        /// <summary>
        /// Reads a complete frame payload and leaves subsequent frames available to the caller.
        /// </summary>
        [Test]
        public void Frame_ConcatenatedFrames_ConsumesOneFrameAtATime()
        {
            byte[] encoded = [0x00, 0x03, 0x61, 0x62, 0x63, 0x01, 0x00];
            SequenceReader<byte> reader = new(new ReadOnlySequence<byte>(encoded));

            Assert.That(
                Http3Frame.TryRead(ref reader, 3, out Http3Frame first),
                Is.EqualTo(Http3FrameReadStatus.Success));
            Assert.That(first.Type, Is.Zero);
            Assert.That(first.Payload.ToArray(), Is.EqualTo(new byte[] { 0x61, 0x62, 0x63 }));
            Assert.That(reader.Consumed, Is.EqualTo(5));

            Assert.That(
                Http3Frame.TryRead(ref reader, 3, out Http3Frame second),
                Is.EqualTo(Http3FrameReadStatus.Success));
            Assert.That(second.Type, Is.EqualTo(1));
            Assert.That(second.Payload.IsEmpty, Is.True);
            Assert.That(reader.Consumed, Is.EqualTo(encoded.Length));
        }

        /// <summary>
        /// Does not consume any bytes when a frame payload is incomplete.
        /// </summary>
        [Test]
        public void Frame_TruncatedPayload_DoesNotConsume()
        {
            byte[] encoded = [0x00, 0x03, 0x61, 0x62];
            SequenceReader<byte> reader = new(new ReadOnlySequence<byte>(encoded));

            Assert.That(Http3Frame.TryRead(ref reader, 3, out _), Is.EqualTo(Http3FrameReadStatus.NeedMoreData));
            Assert.That(reader.Consumed, Is.Zero);
        }

        /// <summary>
        /// Recognizes the reserved frame type sequence without interpreting its payload.
        /// </summary>
        [TestCase(0x21UL)]
        [TestCase(0x40UL)]
        [TestCase(0x5fUL)]
        [TestCase(0x7eUL)]
        public void Frame_ReservedExtensionType_IsMarkedForDiscard(ulong frameType)
        {
            Span<byte> encoded = stackalloc byte[16];
            Assert.That(Http3VarInt.TryWrite(encoded, frameType, out int typeLength), Is.True);
            Assert.That(Http3VarInt.TryWrite(encoded[typeLength..], 0, out int lengthLength), Is.True);
            ReadOnlySequence<byte> sequence =
                new(encoded[..(typeLength + lengthLength)].ToArray());
            SequenceReader<byte> reader = new(sequence);

            Assert.That(
                Http3Frame.TryRead(ref reader, 0, out Http3Frame frame),
                Is.EqualTo(Http3FrameReadStatus.Success));
            Assert.That(frame.IsReservedType, Is.True);
            Assert.That(frame.Payload.IsEmpty, Is.True);
        }

        /// <summary>
        /// Keeps ordinary extension frame types distinct from the reserved sequence.
        /// </summary>
        [TestCase(0x20UL)]
        [TestCase(0x22UL)]
        [TestCase(0x3fUL)]
        [TestCase(0x41UL)]
        public void Frame_UnknownNonReservedType_IsNotMarkedReserved(ulong frameType)
        {
            Span<byte> encoded = stackalloc byte[16];
            Assert.That(Http3VarInt.TryWrite(encoded, frameType, out int typeLength), Is.True);
            Assert.That(Http3VarInt.TryWrite(encoded[typeLength..], 0, out int lengthLength), Is.True);
            ReadOnlySequence<byte> sequence =
                new(encoded[..(typeLength + lengthLength)].ToArray());
            SequenceReader<byte> reader = new(sequence);

            Assert.That(
                Http3Frame.TryRead(ref reader, 0, out Http3Frame frame),
                Is.EqualTo(Http3FrameReadStatus.Success));
            Assert.That(frame.IsReservedType, Is.False);
        }

        /// <summary>
        /// Rejects a configured payload bound outside the QUIC integer range.
        /// </summary>
        [Test]
        public void FrameHeader_InvalidMaximum_Throws()
        {
            Assert.That(
                ReadInvalidMaximum,
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Reads capsule type, length, and payload correctly when every field is split across segments.
        /// </summary>
        [Test]
        public void Capsule_SplitAtEveryOffset_PreservesPayload()
        {
            byte[] encoded = [0x40, 0x40, 0x03, 0x61, 0x62, 0x63];

            for (int split = 1; split < encoded.Length; split++)
            {
                SequenceReader<byte> reader = new(MakeSplitSequence(encoded, split));

                WebTransportCapsuleReadStatus status = WebTransportCapsule.TryRead(ref reader, 3, out var capsule);

                Assert.That(status, Is.EqualTo(WebTransportCapsuleReadStatus.Success), $"split={split}");
                Assert.That(capsule.Type, Is.EqualTo(64));
                Assert.That(capsule.Payload.ToArray(), Is.EqualTo(new byte[] { 0x61, 0x62, 0x63 }));
                Assert.That(reader.Consumed, Is.EqualTo(encoded.Length));
            }
        }

        /// <summary>
        /// Leaves incomplete and oversized capsules unconsumed before payload processing.
        /// </summary>
        [Test]
        public void Capsule_IncompleteOrOversized_DoesNotConsume()
        {
            byte[][] inputs = [[0x01], [0x01, 0x03, 0x61], [0x01, 0x04]];
            WebTransportCapsuleReadStatus[] expected =
            [
                WebTransportCapsuleReadStatus.NeedMoreData,
                WebTransportCapsuleReadStatus.NeedMoreData,
                WebTransportCapsuleReadStatus.PayloadTooLarge
            ];

            for (int index = 0; index < inputs.Length; index++)
            {
                SequenceReader<byte> reader = new(new ReadOnlySequence<byte>(inputs[index]));

                Assert.That(
                    WebTransportCapsule.TryRead(ref reader, 3, out _),
                    Is.EqualTo(expected[index]),
                    $"case={index}");
                Assert.That(reader.Consumed, Is.Zero, $"case={index}");
            }
        }

        /// <summary>
        /// Preserves unknown capsule types and recognizes valid empty payloads for caller dispatch.
        /// </summary>
        [Test]
        public void Capsule_UnknownTypeAndEmptyPayload_AreReturnedWithoutInterpretation()
        {
            byte[] encoded = [0x3f, 0x00];
            SequenceReader<byte> reader = new(new ReadOnlySequence<byte>(encoded));

            Assert.That(
                WebTransportCapsule.TryRead(ref reader, 3, out WebTransportCapsule capsule),
                Is.EqualTo(WebTransportCapsuleReadStatus.Success));
            Assert.That(capsule.Type, Is.EqualTo(63));
            Assert.That(capsule.Payload.IsEmpty, Is.True);
            Assert.That(reader.Consumed, Is.EqualTo(encoded.Length));
        }

        private static Http3FrameHeaderReadStatus ReadInvalidMaximum()
        {
            SequenceReader<byte> reader = new(new ReadOnlySequence<byte>(Array.Empty<byte>()));
            return Http3FrameHeader.TryRead(ref reader, Http3VarInt.MaximumValue + 1, out _);
        }

        private static ReadOnlySequence<byte> MakeSplitSequence(byte[] bytes, int split)
        {
            if (split == 0 || split == bytes.Length)
            {
                return new ReadOnlySequence<byte>(bytes);
            }

            BufferSegment first = new(bytes.AsMemory(0, split));
            BufferSegment last = first.Append(bytes.AsMemory(split));
            return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        }

        private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
        {
            internal BufferSegment(ReadOnlyMemory<byte> memory)
            {
                Memory = memory;
            }

            internal BufferSegment Append(ReadOnlyMemory<byte> memory)
            {
                BufferSegment next = new(memory)
                {
                    RunningIndex = RunningIndex + Memory.Length
                };
                Next = next;
                return next;
            }
        }
    }
}
