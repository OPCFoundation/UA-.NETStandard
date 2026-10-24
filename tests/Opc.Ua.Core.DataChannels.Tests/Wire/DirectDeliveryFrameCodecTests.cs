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
using System.Threading;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Wire
{
    /// <summary>
    /// Checks exact direct ordered-stream framing and bounds independently of transport I/O.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class DirectDeliveryFrameCodecTests
    {
        /// <summary>
        /// Verifies the association token uses UA Binary Guid byte order and exact fixed size.
        /// </summary>
        [Test]
        public void AssociationTokenUsesUaBinaryGuidBytes()
        {
            Guid token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            Span<byte> bytes = stackalloc byte[DirectDeliveryFrameCodec.AssociationTokenSize];
            Assert.That(DirectDeliveryFrameCodec.WriteAssociationToken(token, bytes), Is.EqualTo(16));
            Assert.That(new ByteString(bytes.ToArray()).ToHexString().ToLowerInvariant(),
                Is.EqualTo("33221100554477668899aabbccddeeff"));
            Assert.That(DirectDeliveryFrameCodec.ReadAssociationToken(bytes), Is.EqualTo(token));
            byte[] prefixed = new byte[bytes.Length + 1];
            bytes.CopyTo(prefixed);
            prefixed[^1] = 0x7f;
            Assert.That(DirectDeliveryFrameCodec.ReadAssociationToken(prefixed), Is.EqualTo(token));
        }

        /// <summary>
        /// Verifies the frame length counts the kind and unchanged UADP body bytes.
        /// </summary>
        [Test]
        public void DataFrameHasLiteralLengthKindAndBody()
        {
            ByteString encoded = DirectDeliveryFrameCodec.EncodeData(
                ByteString.FromHexString("91 82 0c 01 00 00 00"),
                new(maxDataBytes: 7));
            Assert.That(encoded.ToHexString().ToLowerInvariant(), Is.EqualTo("080000000091820c01000000"));
            DirectDeliveryFrame decoded = DirectDeliveryFrameCodec.DecodeExact(encoded, new(maxDataBytes: 7));
            Assert.Multiple(() =>
            {
                Assert.That(decoded.Kind, Is.EqualTo(DirectDeliveryFrameKind.Data));
                Assert.That(decoded.Revision, Is.Zero);
                Assert.That(decoded.OperationId, Is.Zero);
                Assert.That(
                    decoded.Body.ToArray(),
                    Is.EqualTo(ByteString.FromHexString("91 82 0c 01 00 00 00").ToArray()));
            });
        }

        /// <summary>
        /// Matches the pinned COMMIT and END fixtures, including their distinct binary control bodies.
        /// </summary>
        [TestCase(true, "1200000001020000000a000000060100000000000000")]
        [TestCase(false, "0e00000001010000000b0000000a01000000")]
        public void BarrierMatchesPinnedControlWireLayout(bool commit, string expected)
        {
            FlowControl control = commit ? new FlowCommit(1, 0) : new FlowSignal(FlowOperation.End, 1);
            ByteString encoded = DirectDeliveryFrameCodec.EncodeBarrier(
                control, commit ? 2u : 1u, commit ? 10u : 11u, new());
            Assert.That(encoded.ToHexString().ToLowerInvariant(), Is.EqualTo(expected));
            DirectDeliveryFrame decoded = DirectDeliveryFrameCodec.DecodeExact(encoded, new());
            Assert.Multiple(() =>
            {
                Assert.That(decoded.Kind, Is.EqualTo(DirectDeliveryFrameKind.Barrier));
                Assert.That(decoded.Revision, Is.EqualTo(commit ? 2u : 1u));
                Assert.That(decoded.OperationId, Is.EqualTo(commit ? 10u : 11u));
                Assert.That(decoded.Control!.Operation, Is.EqualTo(control.Operation));
                Assert.That(decoded.Control.Flow, Is.EqualTo(1u));
                Assert.That(decoded.Body.ToArray(), Is.EqualTo(encoded[13..].ToArray()));
                if (commit)
                {
                    Assert.That(decoded.Control, Is.TypeOf<FlowCommit>());
                    Assert.That(((FlowCommit)decoded.Control).LastOldMessageId, Is.Zero);
                }
            });
        }

        /// <summary>
        /// Rejects every truncation before exposing an incomplete body.
        /// </summary>
        [Test]
        public void ExactDecodeRejectsEveryTruncation()
        {
            ByteString encoded = DirectDeliveryFrameCodec.EncodeData(
                ByteString.FromHexString("01020304"),
                new());
            for (int length = 0; length < encoded.Length; length++)
            {
                ByteString truncated = encoded[..length];
                AssertFailure(() => DirectDeliveryFrameCodec.DecodeExact(truncated, new()), FlowCodecError.Truncated);
            }
        }

        /// <summary>
        /// Rejects tails and malformed length, kind, barrier and token partitions.
        /// </summary>
        [Test]
        public void InvalidFramingAndTokensAreRejected()
        {
            FlowCodecLimits limits = new();
            AssertFailure(() => DirectDeliveryFrameCodec.DecodeExact(
                ByteString.FromHexString("0000000000"), limits), FlowCodecError.InvalidLength);
            AssertFailure(() => DirectDeliveryFrameCodec.DecodeExact(
                ByteString.FromHexString("01000000ff"), limits), FlowCodecError.UnknownKind);
            AssertFailure(() => DirectDeliveryFrameCodec.DecodeExact(
                ByteString.FromHexString("0200000001"), limits), FlowCodecError.InvalidLength);
            AssertFailure(() => DirectDeliveryFrameCodec.DecodeExact(
                ByteString.FromHexString("0e00000001"), limits), FlowCodecError.Truncated);
            AssertFailure(() => DirectDeliveryFrameCodec.DecodeExact(
                ByteString.FromHexString("09000000010100000001000000"), limits), FlowCodecError.InvalidLength);
            AssertFailure(() => DirectDeliveryFrameCodec.DecodeExact(
                ByteString.FromHexString("0e0000000100000000010000000a01000000"), limits),
                FlowCodecError.InvalidRevision);
            AssertFailure(() => DirectDeliveryFrameCodec.DecodeExact(
                ByteString.FromHexString("0e0000000101000000000000000a01000000"), limits),
                FlowCodecError.InvalidMessageId);
            AssertFailure(() => DirectDeliveryFrameCodec.DecodeExact(
                ByteString.FromHexString("020000000001ff"), limits), FlowCodecError.TrailingBytes);
            AssertFailure(() => DirectDeliveryFrameCodec.ReadAssociationToken(new byte[15]), FlowCodecError.Truncated);
            AssertFailure(
                () => DirectDeliveryFrameCodec.ReadAssociationToken(new byte[16]),
                FlowCodecError.InvalidFlow);
            Assert.Throws<ArgumentException>(() =>
                DirectDeliveryFrameCodec.WriteAssociationToken(Guid.Empty, new byte[16]));
            Assert.Throws<ArgumentException>(() =>
                DirectDeliveryFrameCodec.WriteAssociationToken(Guid.NewGuid(), new byte[15]));
        }

        /// <summary>
        /// Rejects an oversized declared length using only the fixed prefix.
        /// </summary>
        [Test]
        public void OversizedFrameLengthIsRejectedBeforeKindOrBody()
        {
            AssertFailure(
                () => DirectDeliveryFrameCodec.ReadHeader(ByteString.FromHexString("ffffffff").Span, new()),
                FlowCodecError.TooLarge);
        }

        /// <summary>
        /// Enforces exact inclusive message, record and control bounds before output allocation.
        /// </summary>
        [Test]
        public void DataAndBarrierBoundsAreInclusive()
        {
            var limits = new FlowCodecLimits(maxDataBytes: 8, maxControlBytes: 8, maxRecordBytes: 13);
            Assert.That(
                DirectDeliveryFrameCodec.EncodeData(new ByteString(new byte[8]), limits).Length,
                Is.EqualTo(13));
            AssertFailure(() => DirectDeliveryFrameCodec.EncodeData(new ByteString(new byte[9]), limits),
                FlowCodecError.TooLarge);
            AssertFailure(() => DirectDeliveryFrameCodec.EncodeData(
                new ByteString(new byte[9]),
                new(maxDataBytes: 9, maxControlBytes: 8, maxRecordBytes: 13)),
                FlowCodecError.TooLarge);
            var end = new FlowSignal(FlowOperation.End, 1);
            var commit = new FlowCommit(1, uint.MaxValue);
            Assert.That(DirectDeliveryFrameCodec.EncodeBarrier(
                end, 1, 1, new(maxControlBytes: 5, maxRecordBytes: 18)).Length, Is.EqualTo(18));
            Assert.That(DirectDeliveryFrameCodec.EncodeBarrier(
                commit, 2, 1, new(maxControlBytes: 9, maxRecordBytes: 22)).Length, Is.EqualTo(22));
            AssertFailure(() => DirectDeliveryFrameCodec.EncodeBarrier(
                end, 1, 1, new(maxControlBytes: 4, maxRecordBytes: 18)),
                FlowCodecError.TooLarge);
            AssertFailure(() => DirectDeliveryFrameCodec.EncodeBarrier(
                end, 1, 1, new(maxControlBytes: 5, maxRecordBytes: 17)),
                FlowCodecError.TooLarge);
            AssertFailure(() => DirectDeliveryFrameCodec.EncodeBarrier(
                commit, 2, 1, new(maxControlBytes: 8, maxRecordBytes: 22)),
                FlowCodecError.TooLarge);
            AssertFailure(() => DirectDeliveryFrameCodec.EncodeBarrier(
                commit, 2, 1, new(maxControlBytes: 9, maxRecordBytes: 21)),
                FlowCodecError.TooLarge);
            AssertFailure(() => DirectDeliveryFrameCodec.EncodeData(ByteString.Empty, new()),
                FlowCodecError.InvalidLength);
        }

        /// <summary>
        /// Rejects management-only controls and semantically invalid or truncated barrier bodies.
        /// </summary>
        [Test]
        public void BarrierRejectsManagementControlsAndInvalidControlShapes()
        {
            AssertFailure(() => DirectDeliveryFrameCodec.EncodeBarrier(
                new FlowSignal(FlowOperation.Ready, 1), 1, 2, new()), FlowCodecError.UnknownOperation);
            AssertFailure(() => DirectDeliveryFrameCodec.DecodeExact(
                ByteString.FromHexString("0e0000000101000000020000000301000000"), new()),
                FlowCodecError.UnknownOperation);
            AssertFailure(() => DirectDeliveryFrameCodec.DecodeExact(
                ByteString.FromHexString("0e0000000101000000020000000a00000000"), new()),
                FlowCodecError.InvalidFlow);
            AssertFailure(() => DirectDeliveryFrameCodec.DecodeExact(
                ByteString.FromHexString("0e0000000102000000020000000601000000"), new()),
                FlowCodecError.Truncated);
            AssertFailure(() => DirectDeliveryFrameCodec.DecodeExact(
                ByteString.FromHexString("120000000102000000020000000a0100000000000000"), new()),
                FlowCodecError.TrailingBytes);
        }

        /// <summary>
        /// Preserves every control field across all header and binary-body segmentation boundaries.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void BarrierIncrementalDecoderAcceptsEverySplit(bool commit)
        {
            FlowControl control = commit
                ? new FlowCommit(7, uint.MaxValue) : new FlowSignal(FlowOperation.End, 7);
            ByteString encoded = DirectDeliveryFrameCodec.EncodeBarrier(control, 2, 10, new());
            for (int split = 1; split < encoded.Length; split++)
            {
                using var decoder = new DirectDeliveryFrameDecoder(new());
                Assert.That(decoder.Read(encoded.Span[..split], out int first, out _), Is.False);
                Assert.That(first, Is.EqualTo(split));
                Assert.That(decoder.Read(encoded.Span[split..], out int second, out DirectDeliveryFrame frame),
                    Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(second, Is.EqualTo(encoded.Length - split));
                    Assert.That(frame.Revision, Is.EqualTo(2u));
                    Assert.That(frame.OperationId, Is.EqualTo(10u));
                    Assert.That(frame.Control!.Flow, Is.EqualTo(7u));
                    Assert.That(frame.Control.Operation, Is.EqualTo(control.Operation));
                    if (commit)
                    {
                        Assert.That(((FlowCommit)frame.Control).LastOldMessageId, Is.EqualTo(uint.MaxValue));
                    }
                });
            }
        }

        /// <summary>
        /// Decodes a data frame at every two-segment split, including split length and kind fields.
        /// </summary>
        [Test]
        public void IncrementalDecoderAcceptsEverySplit()
        {
            ByteString encoded = DirectDeliveryFrameCodec.EncodeData(
                ByteString.FromHexString("01020304050607"),
                new());
            for (int split = 1; split < encoded.Length; split++)
            {
                using var decoder = new DirectDeliveryFrameDecoder(new());
                bool complete = decoder.Read(encoded.Span[..split], out int firstConsumed, out _);
                Assert.That(firstConsumed, Is.EqualTo(split));
                if (!complete)
                {
                    Assert.That(
                        decoder.Read(encoded.Span[split..], out int secondConsumed, out DirectDeliveryFrame frame),
                        Is.True,
                        $"split={split}");
                    Assert.That(secondConsumed, Is.EqualTo(encoded.Length - split));
                    Assert.That(
                        frame.Body.ToArray(),
                        Is.EqualTo(ByteString.FromHexString("01020304050607").ToArray()));
                }
            }
        }

        /// <summary>
        /// Decodes one frame per call and preserves bytes across concatenated frames.
        /// </summary>
        [Test]
        public void IncrementalDecoderConsumesConcatenatedFramesOneAtATime()
        {
            ByteString first = DirectDeliveryFrameCodec.EncodeData(new ByteString(new byte[] { 1, 2 }), new());
            ByteString second = DirectDeliveryFrameCodec.EncodeBarrier(
                new FlowSignal(FlowOperation.End, 1), 3, 4, new());
            byte[] combined = [.. first.ToArray(), .. second.ToArray()];
            using var decoder = new DirectDeliveryFrameDecoder(new());
            Assert.That(decoder.Read(combined, out int consumed, out DirectDeliveryFrame data), Is.True);
            Assert.That(consumed, Is.EqualTo(first.Length));
            Assert.That(data.Body.ToArray(), Is.EqualTo(new byte[] { 1, 2 }));
            Assert.That(
                decoder.Read(combined.AsSpan(consumed), out int barrierConsumed, out DirectDeliveryFrame barrier),
                Is.True);
            Assert.That(barrierConsumed, Is.EqualTo(second.Length));
            Assert.That(barrier.Kind, Is.EqualTo(DirectDeliveryFrameKind.Barrier));
            Assert.That(barrier.Revision, Is.EqualTo(3u));
            Assert.That(barrier.OperationId, Is.EqualTo(4u));
            decoder.Complete();
            Assert.Throws<InvalidOperationException>(() => decoder.Read([], out _, out _));
        }

        /// <summary>
        /// Calls admission after fixed-header validation but before body storage allocation.
        /// </summary>
        [Test]
        public void IncrementalDecoderValidatesHeaderBeforeAllocatingBody()
        {
            var probe = new FrameHeaderProbe();
            using var decoder = new DirectDeliveryFrameDecoder(new(), probe.ValidateHeader);
            probe.Decoder = decoder;
            ByteString encoded = DirectDeliveryFrameCodec.EncodeData(new ByteString(new byte[7]), new());
            Assert.That(decoder.Read(encoded.Span[..DirectDeliveryFrameCodec.HeaderSize],
                out int consumed, out _), Is.False);
            Assert.That(consumed, Is.EqualTo(DirectDeliveryFrameCodec.HeaderSize));
            Assert.That(probe.ValidationCount, Is.EqualTo(1));
            Assert.That(probe.AllocatedBytesDuringValidation, Is.Zero);
            Assert.That(decoder.AllocatedBodyBytes, Is.EqualTo(7));
        }

        /// <summary>
        /// Deferred admission retains only its fixed header and resumes without dropping or copying the frame.
        /// </summary>
        [Test]
        public void IncrementalDecoderDefersBodyAllocationUntilMessageCapacityReturns()
        {
            var budget = new DataChannelApplicationBudget(new DataChannelApplicationLimits { MaxMessages = 1 });
            Assert.That(budget.TryReserveMessageLease(0, out var blocker), Is.True);
            using (blocker)
            using (var decoder = new DirectDeliveryFrameDecoder(new(), budget, Reserve))
            {
                ByteString encoded = DirectDeliveryFrameCodec.EncodeData(new ByteString(new byte[7]), new());
                Assert.That(decoder.Read(encoded.Span, out int consumed, out _, deferAdmission: true), Is.False);
                Assert.Multiple(() =>
                {
                    Assert.That(consumed, Is.EqualTo(DirectDeliveryFrameCodec.HeaderSize));
                    Assert.That(decoder.IsWaitingForCapacity, Is.True);
                    Assert.That(decoder.AllocatedBodyBytes, Is.Zero);
                    Assert.That(budget.Usage.Messages, Is.EqualTo(1));
                });
                blocker!.Dispose();
                Assert.That(decoder.Read(encoded.Span[consumed..], out int remainder, out DirectDeliveryFrame frame,
                    deferAdmission: true), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(remainder, Is.EqualTo(7));
                    Assert.That(decoder.IsWaitingForCapacity, Is.False);
                    Assert.That(frame.Body.Length, Is.EqualTo(7));
                    Assert.That(budget.Usage.Messages, Is.EqualTo(1));
                });
                frame.Dispose();
                frame.Dispose();
                Assert.That(budget.Usage.Messages, Is.Zero);
            }
            DataChannelResourceAssertions.AreReleased(budget.Usage);

            IDisposable? Reserve(in DirectDeliveryFrameHeader header)
            {
                return budget.TryReserveMessageLease(header.TotalLength, out var lease) ? lease : null;
            }
        }

        /// <summary>
        /// Rejects all incomplete prefixes/bodies at EOF and faults after invalid admission.
        /// </summary>
        [Test]
        public void IncrementalDecoderRejectsTruncationAndHeaderAdmissionFailure()
        {
            ByteString encoded = DirectDeliveryFrameCodec.EncodeData(new ByteString(new byte[] { 1, 2, 3 }), new());
            for (int length = 1; length < encoded.Length; length++)
            {
                using var decoder = new DirectDeliveryFrameDecoder(new());
                Assert.That(decoder.Read(encoded.Span[..length], out _, out _), Is.False);
                Assert.Throws<FlowCodecException>(() => decoder.Complete());
                Assert.That(decoder.BufferedBytes, Is.Zero);
                Assert.Throws<InvalidOperationException>(() => decoder.Read([], out _, out _));
            }

            using var rejected = new DirectDeliveryFrameDecoder(new(), RejectHeader);
            Assert.Throws<FlowCodecException>(() =>
                rejected.Read(encoded.Span[..DirectDeliveryFrameCodec.HeaderSize], out _, out _));
            Assert.That(rejected.AllocatedBodyBytes, Is.Zero);
            Assert.Throws<InvalidOperationException>(() => rejected.Read([], out _, out _));

            static IDisposable? RejectHeader(in DirectDeliveryFrameHeader header)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
        }

        /// <summary>
        /// Keeps a yielded data body stable while the decoder accepts a following frame.
        /// </summary>
        [Test]
        public void IncrementalDecoderOwnsYieldedBodyAcrossFrames()
        {
            ByteString first = DirectDeliveryFrameCodec.EncodeData(
                ByteString.FromHexString("aa bb cc"),
                new());
            ByteString second = DirectDeliveryFrameCodec.EncodeData(
                ByteString.FromHexString("11 22"),
                new());
            byte[] combined = [.. first.ToArray(), .. second.ToArray()];
            using var decoder = new DirectDeliveryFrameDecoder(new());
            Assert.That(decoder.Read(combined, out int firstConsumed, out DirectDeliveryFrame firstFrame), Is.True);
            Assert.That(decoder.Read(combined.AsSpan(firstConsumed), out _, out _), Is.True);
            Assert.That(firstFrame.Body.ToHexString().ToLowerInvariant(), Is.EqualTo("aabbcc"));
        }

        /// <summary>
        /// Releases incomplete reservations and transfers complete-frame reservations exactly once.
        /// </summary>
        [Test]
        public void IncrementalDecoderReleasesReservationsOnFailureAndTransfersOwnership()
        {
            ByteString encoded = DirectDeliveryFrameCodec.EncodeData(
                new ByteString(new byte[] { 1, 2, 3 }),
                new());
            int eofReleases = 0;
            using (var decoder = new DirectDeliveryFrameDecoder(
                new(),
                (in DirectDeliveryFrameHeader _) => new CountingReservation(() => eofReleases++)))
            {
                Assert.That(decoder.Read(
                    encoded.Span[..(DirectDeliveryFrameCodec.HeaderSize + 1)],
                    out _,
                    out _), Is.False);
                Assert.That(eofReleases, Is.Zero);
                Assert.Throws<FlowCodecException>(() => decoder.Complete());
                Assert.That(eofReleases, Is.EqualTo(1));
            }

            int disposeReleases = 0;
            var disposed = new DirectDeliveryFrameDecoder(
                new(),
                (in DirectDeliveryFrameHeader _) => new CountingReservation(() => disposeReleases++));
            Assert.That(disposed.Read(
                encoded.Span[..(DirectDeliveryFrameCodec.HeaderSize + 1)],
                out _,
                out _), Is.False);
            disposed.Dispose();
            disposed.Dispose();
            Assert.That(disposeReleases, Is.EqualTo(1));

            int transferredReleases = 0;
            using var completed = new DirectDeliveryFrameDecoder(
                new(),
                (in DirectDeliveryFrameHeader _) => new CountingReservation(() => transferredReleases++));
            Assert.That(completed.Read(encoded.Span, out _, out DirectDeliveryFrame frame), Is.True);
            Assert.That(transferredReleases, Is.Zero);
            completed.Dispose();
            Assert.That(transferredReleases, Is.Zero);
            frame.Dispose();
            frame.Dispose();
            Assert.That(transferredReleases, Is.EqualTo(1));

            int barrierReleases = 0;
            using var barrierDecoder = new DirectDeliveryFrameDecoder(
                new(),
                (in DirectDeliveryFrameHeader _) => new CountingReservation(() => barrierReleases++));
            ByteString barrierBytes = DirectDeliveryFrameCodec.EncodeBarrier(
                new FlowCommit(1, 0), 2, 5, new());
            Assert.That(
                barrierDecoder.Read(barrierBytes.Span, out _, out DirectDeliveryFrame barrier),
                Is.True);
            Assert.That(barrier.Kind, Is.EqualTo(DirectDeliveryFrameKind.Barrier));
            barrierDecoder.Dispose();
            Assert.That(barrierReleases, Is.Zero);
            barrier.Dispose();
            Assert.That(barrierReleases, Is.EqualTo(1));
        }

        /// <summary>
        /// Charges direct-frame body storage to the shared application budget before allocation.
        /// </summary>
        [Test]
        public void IncrementalDecoderUsesApplicationMessageLeaseUntilFrameDisposal()
        {
            var budget = new DataChannelApplicationBudget(new DataChannelApplicationLimits
            {
                MaxBufferedBytes = DirectDeliveryFrameDecoder.ParserStorageBytes + 3,
                MaxMessages = 1,
                MaxStreams = 1
            });
            var capacityChanged = budget.CapacityChanged;
            ByteString encoded = DirectDeliveryFrameCodec.EncodeData(new ByteString(new byte[3]), new());
            using var decoder = new DirectDeliveryFrameDecoder(new(), budget, ReserveBody);

            Assert.That(
                budget.Usage.BufferedBytes,
                Is.EqualTo(DirectDeliveryFrameDecoder.ParserStorageBytes));
            Assert.That(budget.Usage.Streams, Is.EqualTo(1));
            DataChannelException streamRefusal =
                Assert.Throws<DataChannelException>(
                    () => new DirectDeliveryFrameDecoder(new(), budget, ReserveBody))!;
            Assert.That(streamRefusal.Result, Is.EqualTo(DataChannelResult.WouldBlock));

            Assert.That(
                decoder.Read(encoded.Span[..DirectDeliveryFrameCodec.HeaderSize], out _, out _),
                Is.False);
            Assert.That(decoder.AllocatedBodyBytes, Is.EqualTo(3));
            Assert.That(
                budget.Usage.BufferedBytes,
                Is.EqualTo(DirectDeliveryFrameDecoder.ParserStorageBytes + 3));
            Assert.That(budget.Usage.Messages, Is.EqualTo(1));
            DataChannelApplicationBudget.DataChannelMessageReservation? refused;
            Assert.That(
                budget.TryReserveMessageLease(1, out refused),
                Is.False);
            Assert.That(refused, Is.Null);

            Assert.That(
                decoder.Read(
                    encoded.Span[DirectDeliveryFrameCodec.HeaderSize..],
                    out _,
                    out DirectDeliveryFrame frame),
                Is.True);
            Assert.That(
                budget.Usage.BufferedBytes,
                Is.EqualTo(DirectDeliveryFrameDecoder.ParserStorageBytes + 3));
            Assert.That(budget.Usage.Messages, Is.EqualTo(1));
            Assert.That(capacityChanged.IsCompleted, Is.False);
            decoder.Dispose();
            Assert.That(budget.Usage.BufferedBytes, Is.EqualTo(3));
            Assert.That(budget.Usage.Streams, Is.Zero);
            Assert.That(capacityChanged.IsCompleted, Is.True);
            var applicationCapacityChanged = budget.CapacityChanged;
            frame.Dispose();
            Assert.That(budget.Usage.BufferedBytes, Is.Zero);
            Assert.That(budget.Usage.Messages, Is.Zero);
            Assert.That(applicationCapacityChanged.IsCompleted, Is.True);

            using var incomplete = new DirectDeliveryFrameDecoder(new(), budget, ReserveBody);
            Assert.That(
                incomplete.Read(encoded.Span[..DirectDeliveryFrameCodec.HeaderSize], out _, out _),
                Is.False);
            Assert.That(
                budget.Usage.BufferedBytes,
                Is.EqualTo(DirectDeliveryFrameDecoder.ParserStorageBytes + 3));
            Assert.Throws<FlowCodecException>(() => incomplete.Complete());
            Assert.That(budget.Usage.BufferedBytes, Is.Zero);
            Assert.That(budget.Usage.Messages, Is.Zero);
            Assert.That(budget.Usage.Streams, Is.Zero);

            IDisposable? ReserveBody(in DirectDeliveryFrameHeader header)
            {
                Assert.That(header.Kind, Is.EqualTo(DirectDeliveryFrameKind.Data));
                Assert.That(budget.TryReserveMessageLease(header.BodyLength, out var reservation), Is.True);
                return reservation;
            }
        }

        /// <summary>
        /// Refuses budget-aware frame admission when the callback provides no body reservation.
        /// </summary>
        [Test]
        public void IncrementalDecoderRefusesBudgetAdmissionWithoutMessageLease()
        {
            var budget = new DataChannelApplicationBudget();
            ByteString encoded = DirectDeliveryFrameCodec.EncodeData(new ByteString(new byte[3]), new());
            using var decoder = new DirectDeliveryFrameDecoder(
                new(),
                budget,
                static (in DirectDeliveryFrameHeader _) => null);

            DataChannelException refusal = Assert.Throws<DataChannelException>(
                () => decoder.Read(
                    encoded.Span[..DirectDeliveryFrameCodec.HeaderSize],
                    out _,
                    out _))!;

            Assert.That(refusal.Result, Is.EqualTo(DataChannelResult.WouldBlock));
            Assert.That(decoder.AllocatedBodyBytes, Is.Zero);
            Assert.That(decoder.BufferedBytes, Is.Zero);
            Assert.That(budget.Usage.BufferedBytes, Is.Zero);
            Assert.That(budget.Usage.Messages, Is.Zero);
            Assert.That(budget.Usage.Streams, Is.Zero);
        }

        /// <summary>
        /// Asserts the exact codec failure for malformed or out-of-bounds input.
        /// </summary>
        private static void AssertFailure(Action action, FlowCodecError error)
        {
            FlowCodecException exception = Assert.Throws<FlowCodecException>(action)!;
            Assert.That(exception.Error, Is.EqualTo(error));
        }

        private sealed class FrameHeaderProbe
        {
            /// <summary>
            /// Gets or sets the decoder being observed.
            /// </summary>
            internal DirectDeliveryFrameDecoder Decoder { private get; set; } = null!;

            /// <summary>
            /// Gets the number of times the admission callback ran.
            /// </summary>
            internal int ValidationCount { get; private set; }

            /// <summary>
            /// Gets the body allocation observed before the admission callback returned.
            /// </summary>
            internal int AllocatedBytesDuringValidation { get; private set; } = -1;

            /// <summary>
            /// Records the header and allocation state at the admission boundary.
            /// </summary>
            internal IDisposable? ValidateHeader(in DirectDeliveryFrameHeader header)
            {
                ValidationCount++;
                AllocatedBytesDuringValidation = Decoder.AllocatedBodyBytes;
                Assert.That(header.Kind, Is.EqualTo(DirectDeliveryFrameKind.Data));
                Assert.That(header.BodyLength, Is.EqualTo(7));
                return null;
            }
        }

        private sealed class CountingReservation(Action release) : IDisposable
        {
            public void Dispose()
            {
                Interlocked.Exchange(ref m_release, null)?.Invoke();
            }

            private Action? m_release = release;
        }
    }
}
