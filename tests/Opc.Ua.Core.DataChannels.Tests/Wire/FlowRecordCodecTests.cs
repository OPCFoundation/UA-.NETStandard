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
using System.Buffers.Binary;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Wire
{
    /// <summary>
    /// Checks framing, incremental ownership and allocation boundaries independently of JSON.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class FlowRecordCodecTests
    {
        /// <summary>
        /// Uses non-palindromic integers to catch byte-order and offset mutations.
        /// </summary>
        [Test]
        public void HeaderIsLittleEndianAndCountsFollowingBytes()
        {
            var record = new FlowRecord(FlowRecordKind.Data, 0x10203040, 0x50607080,
                new ByteString("k9!"u8.ToArray()));
            Assert.That(FlowRecordCodec.Encode(in record, new()).ToHexString().ToLowerInvariant(),
                Is.EqualTo("0c0000000040302010807060506b3921"));
            FlowRecord decoded = FlowRecordCodec.DecodeExact(
                ByteString.FromHexString("0c0000000040302010807060506b3921"), new());
            Assert.That(decoded.Revision, Is.EqualTo(0x10203040u));
            Assert.That(decoded.MessageId, Is.EqualTo(0x50607080u));
            Assert.That(decoded.Body.ToArray(), Is.EqualTo("k9!"u8.ToArray()));
        }

        /// <summary>
        /// Distinguishes an empty payload from an absent ByteString.
        /// </summary>
        [Test]
        public void EmptyOpaqueDataUsesOnlyTheFixedHeader()
        {
            var expected = ByteString.FromHexString("09000000000100000001000000");
            FlowRecord record = FlowRecordCodec.DecodeExact(expected, new(maxDataBytes: 0, maxRecordBytes: 13));
            Assert.That(record.Body.IsNull, Is.False);
            Assert.That(record.Body.IsEmpty, Is.True);
            Assert.That(FlowRecordCodec.Encode(in record, new(maxDataBytes: 0)).ToArray(),
                Is.EqualTo(expected.ToArray()));
            var missing = new FlowRecord(FlowRecordKind.Data, 1, 1, default);
            FrozenWire.Fails(() => FlowRecordCodec.Encode(in missing, new()), FlowCodecError.WrongType);
        }

        /// <summary>
        /// Rejects every shortened header or body of an independent record.
        /// </summary>
        [Test]
        public void ExactContainerRejectsEveryTruncation()
        {
            ByteString bytes = new FrozenWire("quic_opaque").Bytes;
            for (int size = 0; size < bytes.Length; size++)
            {
                ByteString shortened = bytes[..size];
                FrozenWire.Fails(() => FlowRecordCodec.DecodeExact(shortened, new()), FlowCodecError.Truncated);
            }
        }

        /// <summary>
        /// Rejects both arbitrary and complete-record tails in a single-record container.
        /// </summary>
        [Test]
        public void ExactContainerRejectsTail()
        {
            ByteString first = new FrozenWire("quic_opaque").Bytes;
            foreach (ByteString tail in new[] { new ByteString(new byte[] { 0 }), first })
            {
                ByteString input = new(first.ToArray().Concat(tail.ToArray()).ToArray());
                FrozenWire.Fails(() => FlowRecordCodec.DecodeExact(input, new()), FlowCodecError.TrailingBytes);
            }
        }

        /// <summary>
        /// Checks the 9-byte following-length minimum and overflow-safe maximum.
        /// </summary>
        [TestCase(0u, "InvalidLength")]
        [TestCase(8u, "InvalidLength")]
        [TestCase(uint.MaxValue, "TooLarge")]
        [TestCase(2147483644u, "TooLarge")]
        public void RecordLengthBoundariesAreChecked(uint length, string error)
        {
            FlowCodecException exception = Assert.Throws<FlowCodecException>(() =>
                FlowRecordCodec.ReadHeader(FrozenWire.Header(length).Span, new()))!;
            Assert.That(exception.Error.ToString(), Is.EqualTo(error));
        }

        /// <summary>
        /// Checks declared size at the first possible byte before any body allocation.
        /// </summary>
        [TestCase(0, 16, 4)]
        [TestCase(0, 13, 5)]
        [TestCase(1, 13, 5)]
        public void DeclaredLengthIsRejectedBeforeBodyBufferAllocation(int kind, int length, int rejectAt)
        {
            FlowCodecLimits limits = kind == 0 ? new FlowCodecLimits(maxDataBytes: 3, maxControlBytes: 5) :
                new FlowCodecLimits(maxDataBytes: 5, maxControlBytes: 3);
            ByteString header = FrozenWire.Header((uint)length, (byte)kind);
            using var decoder = new FlowRecordDecoder(limits);
            Assert.That(decoder.Read(header.Span[..(rejectAt - 1)], out int consumed, out _), Is.False);
            Assert.That(consumed, Is.EqualTo(rejectAt - 1));
            Assert.That(decoder.AllocatedBodyBytes, Is.Zero);
            FrozenWire.Fails(() => decoder.Read(header.Span.Slice(rejectAt - 1, 1), out _, out _),
                FlowCodecError.TooLarge);
            Assert.That(decoder.AllocatedBodyBytes, Is.Zero);
            Assert.That(decoder.BufferedBytes, Is.Zero);
        }

        /// <summary>
        /// Proves just-below, exact and just-above body and encoded-record capacities.
        /// </summary>
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void BodyAndEncodedLimitsAreInclusive(int bytes)
        {
            var record = new FlowRecord(FlowRecordKind.Data, 1, 1, new ByteString(new byte[bytes]));
            var limits = new FlowCodecLimits(maxDataBytes: 3, maxRecordBytes: 16);
            if (bytes <= 3)
            {
                ByteString encoded = FlowRecordCodec.Encode(in record, limits);
                Assert.That(encoded.Length, Is.EqualTo(13 + bytes));
                Assert.That(FlowRecordCodec.DecodeExact(encoded, limits).Body.Length, Is.EqualTo(bytes));
            }
            else
            {
                FrozenWire.Fails(() => FlowRecordCodec.Encode(in record, limits), FlowCodecError.TooLarge);
            }
            var smallerRecordLimit = new FlowCodecLimits(maxDataBytes: 4, maxRecordBytes: 13 + bytes - 1);
            FrozenWire.Fails(() => FlowRecordCodec.Encode(in record, smallerRecordLimit), FlowCodecError.TooLarge);
        }

        /// <summary>
        /// Checks each intrinsic header discriminator and zero identifier partition.
        /// </summary>
        [TestCase(2, 1u, 1u, "UnknownKind")]
        [TestCase(255, 1u, 1u, "UnknownKind")]
        [TestCase(0, 0u, 1u, "InvalidRevision")]
        [TestCase(0, 1u, 0u, "InvalidMessageId")]
        [TestCase(1, 0u, 0u, "InvalidMessageId")]
        public void HeaderFieldsRejectInvalidPartitions(int kind, uint revision, uint id, string error)
        {
            FlowCodecException exception = Assert.Throws<FlowCodecException>(() =>
                FlowRecordCodec.ReadHeader(FrozenWire.Header(9, (byte)kind, revision, id).Span, new()))!;
            Assert.That(exception.Error.ToString(), Is.EqualTo(error));
            var record = new FlowRecord((FlowRecordKind)kind, revision, id, ByteString.Empty);
            exception = Assert.Throws<FlowCodecException>(() => FlowRecordCodec.Encode(in record, new()))!;
            Assert.That(exception.Error.ToString(), Is.EqualTo(error));
        }

        /// <summary>
        /// Keeps all representable header values without applying lifecycle wrap or replay state.
        /// </summary>
        [Test]
        public void HeaderPreservesUnsignedMaximaWithoutInventingLifecycleState()
        {
            FlowRecord decoded = FlowRecordCodec.DecodeExact(
                FrozenWire.Header(9, 0, uint.MaxValue, uint.MaxValue), new());
            Assert.That(decoded.Revision, Is.EqualTo(uint.MaxValue));
            Assert.That(decoded.MessageId, Is.EqualTo(uint.MaxValue));
            var control = new FlowRecord(FlowRecordKind.Control, 0, uint.MaxValue, ByteString.Empty);
            Assert.That(FlowRecordCodec.GetEncodedLength(in control, new()), Is.EqualTo(13));
        }

        /// <summary>
        /// Checks representable total-length arithmetic using headers only, without allocating enormous bodies.
        /// </summary>
        [Test]
        public void HeaderSizeArithmeticAcceptsTheLastRepresentableLengthWithoutBodyAllocation()
        {
            var limits = new FlowCodecLimits(int.MaxValue - 13, int.MaxValue - 13, int.MaxValue);
            FlowRecordHeader header = FlowRecordCodec.ReadHeader(FrozenWire.Header(int.MaxValue - 4u).Span, limits);
            Assert.That(header.TotalLength, Is.EqualTo(int.MaxValue));
            Assert.That(header.BodyLength, Is.EqualTo(int.MaxValue - 13));
            FrozenWire.Fails(() => FlowRecordCodec.ReadHeader(
                FrozenWire.Header(int.MaxValue - 3u).Span, limits), FlowCodecError.TooLarge);
            FrozenWire.Fails(() => FlowRecordCodec.ReadHeader(FrozenWire.Header(uint.MaxValue).Span, limits),
                FlowCodecError.TooLarge);
        }

        /// <summary>
        /// Reconstructs an independent record at every transport split, including empty segments.
        /// </summary>
        [TestCase("quic_opaque")]
        [TestCase("quic_hello")]
        [TestCase("webtransport_json")]
        public void StreamDecoderAcceptsEverySplit(string name)
        {
            ByteString bytes = new FrozenWire(name).Bytes;
            for (int split = 0; split <= bytes.Length; split++)
            {
                using var decoder = new FlowRecordDecoder(new());
                bool complete = decoder.Read(bytes.Span[..split], out int consumed, out FlowRecord record);
                Assert.That(consumed, Is.EqualTo(split));
                Assert.That(complete, Is.EqualTo(split == bytes.Length));
                if (!complete)
                {
                    Assert.That(decoder.Read([], out int emptyConsumed, out _), Is.False);
                    Assert.That(emptyConsumed, Is.Zero);
                    Assert.That(decoder.Read(bytes.Span[split..], out consumed, out record), Is.True);
                    Assert.That(consumed, Is.EqualTo(bytes.Length - split));
                }
                Assert.That(record.Body.ToArray(), Is.EqualTo(bytes[13..].ToArray()));
                Assert.That(FlowRecordCodec.Encode(in record, new()).ToArray(), Is.EqualTo(bytes.ToArray()));
                Assert.That(decoder.BufferedBytes, Is.Zero);
                Assert.That(decoder.AllocatedBodyBytes, Is.Zero);
                decoder.Complete();
            }
        }

        /// <summary>
        /// Handles concatenation without treating the next complete record as a trailing-byte error.
        /// </summary>
        [Test]
        public void StreamDecoderAcceptsConcatenatedRecords()
        {
            ByteString first = new FrozenWire("quic_opaque").Bytes;
            ByteString second = new FrozenWire("quic_hello").Bytes;
            ByteString third = FrozenWire.Header(9, 0, 2, 3);
            byte[] input = first.ToArray().Concat(second.ToArray()).Concat(third.ToArray()).ToArray();
            using var decoder = new FlowRecordDecoder(new());
            int offset = 0;
            foreach (ByteString expected in new[] { first, second, third })
            {
                Assert.That(decoder.Read(input.AsSpan(offset), out int consumed, out FlowRecord record), Is.True);
                Assert.That(consumed, Is.EqualTo(expected.Length));
                Assert.That(FlowRecordCodec.Encode(in record, new()).ToArray(), Is.EqualTo(expected.ToArray()));
                offset += consumed;
            }
            Assert.That(offset, Is.EqualTo(input.Length));
            decoder.Complete();
            decoder.Complete();
            Assert.Throws<InvalidOperationException>(() => decoder.Read(first.Span, out _, out _));
        }

        /// <summary>
        /// Combines transport segmentation with concatenation, using consumed counts to retain every record byte.
        /// </summary>
        [TestCase(1)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(13)]
        [TestCase(16)]
        [TestCase(17)]
        [TestCase(64)]
        public void SegmentedConcatenationPreservesRecordsAndConsumption(int segmentSize)
        {
            ByteString[] records =
            [
                new FrozenWire("quic_opaque").Bytes,
                new FrozenWire("quic_hello").Bytes,
                new FrozenWire("quic_uadp").Bytes,
                ByteString.FromHexString("09000000000200000003000000")
            ];
            byte[] input = [.. records.SelectMany(record => record.ToArray())];
            using var decoder = new FlowRecordDecoder(new());
            int completed = 0;
            for (int offset = 0; offset < input.Length; offset += segmentSize)
            {
                ReadOnlySpan<byte> segment = input.AsSpan(offset, Math.Min(segmentSize, input.Length - offset));
                while (!segment.IsEmpty)
                {
                    bool ready = decoder.Read(segment, out int consumed, out FlowRecord record);
                    Assert.That(consumed, Is.GreaterThan(0).And.LessThanOrEqualTo(segment.Length));
                    segment = segment[consumed..];
                    if (ready)
                    {
                        Assert.That(completed, Is.LessThan(records.Length));
                        ByteString expected = records[completed++];
                        Assert.That(record.Body.ToArray(), Is.EqualTo(expected[13..].ToArray()));
                        Assert.That((byte)record.Kind, Is.EqualTo(expected[4]));
                        Assert.That(record.Revision,
                            Is.EqualTo(BinaryPrimitives.ReadUInt32LittleEndian(expected.Span[5..])));
                        Assert.That(record.MessageId,
                            Is.EqualTo(BinaryPrimitives.ReadUInt32LittleEndian(expected.Span[9..])));
                        Assert.That(FlowRecordCodec.Encode(in record, new()).ToArray(), Is.EqualTo(expected.ToArray()));
                    }
                }
            }
            Assert.That(completed, Is.EqualTo(records.Length));
            Assert.That(decoder.BufferedBytes, Is.Zero);
            Assert.That(decoder.AllocatedBodyBytes, Is.Zero);
            decoder.Complete();
        }

        /// <summary>
        /// Rejects every nonempty incomplete prefix at EOF and releases its allocation.
        /// </summary>
        [Test]
        public void StreamEofRejectsEveryIncompletePrefix()
        {
            ByteString bytes = new FrozenWire("quic_opaque").Bytes;
            for (int length = 1; length < bytes.Length; length++)
            {
                using var decoder = new FlowRecordDecoder(new());
                Assert.That(decoder.Read(bytes.Span[..length], out _, out _), Is.False);
                FrozenWire.Fails(decoder.Complete, FlowCodecError.Truncated);
                Assert.That(decoder.BufferedBytes, Is.Zero);
                Assert.That(decoder.AllocatedBodyBytes, Is.Zero);
            }
            using var empty = new FlowRecordDecoder(new());
            Assert.That(empty.Read([], out int consumed, out _), Is.False);
            Assert.That(consumed, Is.Zero);
            empty.Complete();
        }

        /// <summary>
        /// Treats the native tail specimens as incomplete next records at end of stream.
        /// </summary>
        [TestCase("quic_tail")]
        [TestCase("webtransport_tail")]
        public void NativeTailIsNotConfusedWithTransportSegmentation(string name)
        {
            ByteString input = new FrozenWire(name).Bytes;
            using var decoder = new FlowRecordDecoder(new());
            Assert.That(decoder.Read(input.Span, out int consumed, out FlowRecord record), Is.True);
            Assert.That(record.Body.ToArray(), Is.EqualTo("k9!"u8.ToArray()));
            Assert.That(consumed, Is.EqualTo(16));
            Assert.That(decoder.Read(input.Span[consumed..], out _, out _), Is.False);
            FrozenWire.Fails(decoder.Complete, FlowCodecError.Truncated);
        }

        /// <summary>
        /// Retains only one bounded body, even when more input follows, and transfers its ownership.
        /// </summary>
        [Test]
        public void StreamDecoderRetainsAtMostOneRecord()
        {
            ByteString bytes = new FrozenWire("quic_opaque").Bytes;
            using var decoder = new FlowRecordDecoder(new(maxDataBytes: 3));
            Assert.That(decoder.Read(bytes.Span[..14], out _, out _), Is.False);
            Assert.That(decoder.AllocatedBodyBytes, Is.EqualTo(3));
            Assert.That(decoder.BufferedBytes, Is.EqualTo(14));
            byte[] remainder = bytes[14..].ToArray().Concat(bytes.ToArray()).ToArray();
            Assert.That(decoder.Read(remainder, out int consumed, out FlowRecord result), Is.True);
            Assert.That(consumed, Is.EqualTo(2));
            Assert.That(decoder.BufferedBytes, Is.Zero);
            Assert.That(decoder.AllocatedBodyBytes, Is.Zero);
            Assert.That(result.Body.ToArray(), Is.EqualTo("k9!"u8.ToArray()));
            Assert.That(decoder.Read(remainder.AsSpan(consumed, 14), out _, out _), Is.False);
            decoder.Dispose();
            Assert.That(decoder.AllocatedBodyBytes, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => decoder.Read(bytes.Span, out _, out _));
        }

        /// <summary>
        /// Prevents resynchronizing after invalid framing without a new stream decoder.
        /// </summary>
        [Test]
        public void StreamFaultIsTerminal()
        {
            using var decoder = new FlowRecordDecoder(new());
            FrozenWire.Fails(() => decoder.Read(FrozenWire.Header(8).Span, out _, out _),
                FlowCodecError.InvalidLength);
            Assert.Throws<InvalidOperationException>(() =>
                decoder.Read(FrozenWire.Header(9).Span, out _, out _));
            Assert.Throws<InvalidOperationException>(decoder.Complete);
        }

        /// <summary>
        /// Calls the owner's pre-allocation guard exactly once per header, including empty records.
        /// </summary>
        [Test]
        public void OwnerHeaderValidationRunsOnceBeforeBodyAllocation()
        {
            ByteString bytes = new FrozenWire("quic_opaque").Bytes;
            int calls = 0;
            using var decoder = new FlowRecordDecoder(new(), (in header) =>
            {
                calls++;
                Assert.That(header.MessageId, Is.EqualTo(1));
                Assert.That(header.Revision, Is.EqualTo(1));
                Assert.That(header.Kind, Is.EqualTo(FlowRecordKind.Data));
                Assert.That(header.BodyLength, Is.EqualTo(calls == 1 ? 3 : 0));
            });
            Assert.That(decoder.Read(bytes.Span[..12], out _, out _), Is.False);
            Assert.That(calls, Is.Zero);
            Assert.That(decoder.AllocatedBodyBytes, Is.Zero);
            Assert.That(decoder.Read(bytes.Span.Slice(12, 1), out _, out _), Is.False);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(decoder.Read(bytes.Span.Slice(13, 1), out _, out _), Is.False);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(decoder.Read(bytes.Span[14..], out _, out _), Is.True);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(decoder.Read(FrozenWire.Header(9).Span, out _, out _), Is.True);
            Assert.That(calls, Is.EqualTo(2));
        }

        /// <summary>
        /// Rejects an owner-refused header before allocating and does not resume after callback failure.
        /// </summary>
        [Test]
        public void OwnerHeaderRejectionPreventsAllocationAndFaultsDecoder()
        {
            FlowRecordDecoder? decoder = null;
            decoder = new FlowRecordDecoder(new(), (in _) =>
            {
                Assert.That(decoder!.AllocatedBodyBytes, Is.Zero);
                throw new InvalidOperationException("Rejected by owner.");
            });
            using (decoder)
            {
                Assert.Throws<InvalidOperationException>(() =>
                    decoder.Read(new FrozenWire("quic_opaque").Bytes.Span, out _, out _));
                Assert.That(decoder.AllocatedBodyBytes, Is.Zero);
                Assert.That(decoder.BufferedBytes, Is.Zero);
                Assert.Throws<InvalidOperationException>(() => decoder.Read(FrozenWire.Header(9).Span, out _, out _));
            }
        }

        /// <summary>
        /// Makes borrowed and copied storage behavior observable to the caller.
        /// </summary>
        [Test]
        public void ExactDecodeBorrowsAndStreamDecodeOwnsBody()
        {
            byte[] bytes = new FrozenWire("quic_opaque").Bytes.ToArray();
            FlowRecord borrowed = FlowRecordCodec.DecodeExact(new ByteString(bytes), new());
            using var decoder = new FlowRecordDecoder(new());
            Assert.That(decoder.Read(bytes, out _, out FlowRecord owned), Is.True);
            bytes[13] = 42;
            Assert.That(borrowed.Body[0], Is.EqualTo(42));
            Assert.That(owned.Body[0], Is.EqualTo(107));
        }

        /// <summary>
        /// Keeps completed bodies unchanged while the decoder receives another record and is disposed.
        /// </summary>
        [Test]
        public void StreamBodiesRemainOwnedAcrossFollowingRecordsAndDisposal()
        {
            byte[] first = new FrozenWire("quic_opaque").Bytes.ToArray();
            var second = ByteString.FromHexString("0c000000000200000002000000005aff");
            using var decoder = new FlowRecordDecoder(new());
            Assert.That(decoder.Read(first, out int consumed, out FlowRecord original), Is.True);
            Assert.That(consumed, Is.EqualTo(first.Length));
            first[13] = 0xFF;
            Assert.That(decoder.Read(second.Span[..14], out consumed, out _), Is.False);
            Assert.That(consumed, Is.EqualTo(14));
            Assert.That(original.Body.ToArray(), Is.EqualTo("k9!"u8.ToArray()));
            Assert.That(decoder.Read(second.Span[14..], out consumed, out FlowRecord following), Is.True);
            Assert.That(consumed, Is.EqualTo(2));
            decoder.Dispose();
            Assert.Multiple(() =>
            {
                Assert.That(original.Body.ToArray(), Is.EqualTo("k9!"u8.ToArray()));
                Assert.That(following.Body.ToArray(), Is.EqualTo(new byte[] { 0, 0x5A, 0xFF }));
                Assert.That(following.Revision, Is.EqualTo(2));
                Assert.That(following.MessageId, Is.EqualTo(2));
                Assert.That(decoder.BufferedBytes, Is.Zero);
                Assert.That(decoder.AllocatedBodyBytes, Is.Zero);
            });
        }

        /// <summary>
        /// Produces a present empty body without allocating body storage for an empty DATA record.
        /// </summary>
        [Test]
        public void StreamEmptyDataNeedsNoBodyAllocation()
        {
            var bytes = ByteString.FromHexString("09000000000100000001000000");
            using var decoder = new FlowRecordDecoder(new(maxDataBytes: 0, maxRecordBytes: 13));
            Assert.That(decoder.Read(bytes.Span, out int consumed, out FlowRecord record), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(consumed, Is.EqualTo(13));
                Assert.That(record.Kind, Is.EqualTo(FlowRecordKind.Data));
                Assert.That(record.Body.IsEmpty, Is.True);
                Assert.That(record.Body.IsNull, Is.False);
                Assert.That(decoder.AllocatedBodyBytes, Is.Zero);
                Assert.That(decoder.BufferedBytes, Is.Zero);
            });
            decoder.Complete();
        }

        /// <summary>
        /// Leaves short destinations unchanged and supports overlapping payload/header storage.
        /// </summary>
        [Test]
        public void CallerOwnedEncodingChecksCapacityBeforeWritingAndSupportsOverlap()
        {
            byte[] buffer = [.. Enumerable.Repeat((byte)0xA5, 16)];
            var record = new FlowRecord(FlowRecordKind.Data, 1, 1, new ByteString(new byte[] { 1, 2, 3 }));
            Assert.Throws<ArgumentException>(() => FlowRecordCodec.Write(in record, buffer.AsSpan(0, 15), new()));
            Assert.That(buffer, Is.All.EqualTo(0xA5));
            record = new FlowRecord(FlowRecordKind.Data, 1, 1, new ByteString(buffer.AsMemory(0, 3)));
            Assert.That(FlowRecordCodec.Write(in record, buffer, new()), Is.EqualTo(16));
            Assert.That(buffer.Skip(13), Is.EqualTo(new byte[] { 0xA5, 0xA5, 0xA5 }));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buffer), Is.EqualTo(12));
        }

        /// <summary>
        /// Preserves every byte value without consulting an inner PubSub or service encoding.
        /// </summary>
        [Test]
        public void EveryOpaqueByteValueIsPreservedWithoutInnerDecoding()
        {
            byte[] payload = [.. Enumerable.Range(0, 256).Select(value => (byte)value)];
            var record = new FlowRecord(FlowRecordKind.Data, 1, 1, new ByteString(payload));
            ByteString encoded = FlowRecordCodec.Encode(in record, new());
            Assert.That(encoded[13..].ToArray(), Is.EqualTo(payload));
            Assert.That(encoded.Length, Is.EqualTo(payload.Length + 13));
            FlowRecord decoded = FlowRecordCodec.DecodeExact(encoded, new());
            Assert.That(decoded.Body.ToArray(), Is.EqualTo(payload));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(encoded.Span), Is.EqualTo(payload.Length + 9));
        }

        /// <summary>
        /// Rejects impossible local allocation policies at construction.
        /// </summary>
        [Test]
        public void LocalLimitsRejectInvalidConstruction()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FlowCodecLimits(maxDataBytes: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FlowCodecLimits(maxDataBytes: int.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FlowCodecLimits(maxControlBytes: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FlowCodecLimits(maxControlBytes: int.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FlowCodecLimits(maxRecordBytes: 12));
            var limits = new FlowCodecLimits(int.MaxValue - 13, int.MaxValue - 13, int.MaxValue);
            Assert.That(limits.MaxDataBytes, Is.EqualTo(int.MaxValue - 13));
            Assert.That(limits.MaxControlBytes, Is.EqualTo(int.MaxValue - 13));
            Assert.That(limits.MaxRecordBytes, Is.EqualTo(int.MaxValue));
        }
    }
}
