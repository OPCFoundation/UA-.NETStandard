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
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// The retained chunk core and its production reassembler operate on synthetic cleartext bytes only.
    /// </summary>
    [TestFixture]
    [Category("Fuzzing")]
    public sealed class PubSubChunkTests
    {
        [TestCase(1)]
        [TestCase(245)]
        [TestCase(246)]
        [TestCase(247)]
        [TestCase(492)]
        [TestCase(493)]
        [TestCase(4096)]
        [TestCase(65535)]
        public void SharedChunkCorePreservesEveryByteInBothOrdersAtFrameBoundaries(int length)
        {
            byte[] payload = CreatePayload(length);
            IReadOnlyList<byte[]> frames = new UadpChunker().Split(payload, 42, 256);
            int expectedCount = (length + 245) / 246;
            Assert.That(frames, Has.Count.EqualTo(expectedCount));
            for (int i = 0; i < frames.Count; i++)
            {
                int offset = i * 246;
                int payloadLength = Math.Min(246, length - offset);
                Assert.That(frames[i], Has.Length.EqualTo(payloadLength + 10));
                Assert.That(
                    UadpChunker.TryParseChunk(
                        frames[i], out ushort sequence, out uint chunkOffset, out uint totalSize,
                        out ReadOnlyMemory<byte> fragment),
                    Is.True);
                Assert.That(sequence, Is.EqualTo(42));
                Assert.That(chunkOffset, Is.EqualTo(offset));
                Assert.That(totalSize, Is.EqualTo(length));
                Assert.That(fragment.ToArray(), Is.EqualTo(payload.AsSpan(offset, payloadLength).ToArray()));
            }

            foreach (bool reverse in new[] { false, true })
            {
                var clock = new ObservedTimeProvider(new FakeTimeProvider(PubSubSeedAssertions.SeedTime));

                ReadOnlyMemory<byte> reassembled = FuzzableCode.ReassembleUadpPayload(payload, clock, reverse);

                Assert.That(reassembled.ToArray(), Is.EqualTo(payload), $"reverse={reverse}");
                // Pins reassembler work, including the first-fragment duplicate, rather than an identity return.
                Assert.That(clock.UtcNowReads, Is.EqualTo(expectedCount + (expectedCount > 1 ? 1 : 0)));
            }
        }

        [Test]
        public void SharedChunkCoreHonorsCustomFrameSize()
        {
            byte[] payload = [0x31, 0x62, 0x93, 0xC4];
            foreach (bool reverse in new[] { false, true })
            {
                var clock = new ObservedTimeProvider(new FakeTimeProvider(PubSubSeedAssertions.SeedTime));

                ReadOnlyMemory<byte> result = FuzzableCode.ReassembleUadpPayload(
                    payload, clock, reverse, maxFrameSize: 11);

                Assert.That(result.ToArray(), Is.EqualTo(payload));
                Assert.That(clock.UtcNowReads, Is.EqualTo(5), "Four one-byte fragments plus the first duplicate.");
            }
        }

        [Test]
        public void SharedChunkCoreReportsExpiryAsAnOracleFailureInsteadOfReturningPartialData()
        {
            byte[] payload = CreatePayload(247);
            foreach (bool reverse in new[] { false, true })
            {
                var clock = new FakeTimeProvider(PubSubSeedAssertions.SeedTime)
                {
                    AutoAdvanceAmount = TimeSpan.FromSeconds(6)
                };

                Assert.That(
                    () => FuzzableCode.ReassembleUadpPayload(payload, clock, reverse),
                    Throws.TypeOf<InvalidOperationException>()
                        .With.Message.EqualTo("Reassembly completed at the wrong chunk boundary."));

                clock.AutoAdvanceAmount = TimeSpan.Zero;
                Assert.That(
                    FuzzableCode.ReassembleUadpPayload(payload, clock, reverse).ToArray(),
                    Is.EqualTo(payload), "A failed oracle must not retain a poisoned reassembler.");
            }
        }

        [Test]
        public void EmptyProbeDoesNotDecodeButInvalidRoundTripArgumentsStillThrow()
        {
            PubSubNetworkMessageContext context = FuzzableCode.NewContext();
            FuzzableCode.ExerciseUadpChunks(ReadOnlyMemory<byte>.Empty, context);
            PubSubSeedAssertions.AssertDiagnostics(context);

            Assert.That(
                () => FuzzableCode.ReassembleUadpPayload(
                    ReadOnlyMemory<byte>.Empty, context.TimeProvider, reverse: false),
                Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("encodedMessage"));
            byte[] payload = [0x42];
            Assert.That(
                () => FuzzableCode.ReassembleUadpPayload(
                    payload, context.TimeProvider, reverse: true, maxFrameSize: 10),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentException.ParamName)).EqualTo("maxFrameSize"));
            PubSubSeedAssertions.AssertDiagnostics(context);
        }

        [Test]
        public void StoredCompleteChunkReachesTheRealDownstreamUadpDecoder()
        {
            byte[] frame = PubSubSeedAssertions.LoadSeed("Chunks", "metadata-keyframe-complete.bin");
            byte[] rawSeed = PubSubSeedAssertions.LoadSeed("Uadp", "metadata-keyframe-raw.uadp");
            var clock = new ObservedTimeProvider(new FakeTimeProvider(PubSubSeedAssertions.SeedTime));
            PubSubNetworkMessageContext context = FuzzableCode.NewContext(clock);

            FuzzableCode.ExerciseUadpChunks(frame, context);

            // The raw probe decodes the embedded seed. The independent round-trip also probes the chunk frame,
            // whose first byte is the sequence number 42 (not a UADP v1 header), hence one expected rejection.
            PubSubSeedAssertions.AssertDiagnostics(context, received: 1, dataSets: 1, invalid: 1);
            Assert.That(
                clock.UtcNowReads, Is.EqualTo(3), "Raw probe plus independently ordered and reversed round-trips.");
            Assert.That(
                UadpChunker.TryParseChunk(
                    frame, out ushort sequence, out uint offset, out uint totalSize, out ReadOnlyMemory<byte> payload),
                Is.True);
            Assert.That(sequence, Is.EqualTo(42));
            Assert.That(offset, Is.Zero);
            Assert.That(totalSize, Is.EqualTo(79));
            Assert.That(payload.ToArray(), Is.EqualTo(rawSeed));
            using var reassembler = new UadpReassembler(new FakeTimeProvider(PubSubSeedAssertions.SeedTime));
            Assert.That(reassembler.TryAddChunk(PublisherId.FromUInt16(300), 1, frame, out var complete), Is.True);
            Assert.That(complete, Is.Not.Null);
            Assert.That(complete.Value.ToArray(), Is.EqualTo(rawSeed));
            Assert.That(reassembler.PendingCount, Is.Zero);

            PubSubNetworkMessageContext decodedContext = FuzzableCode.NewContext();
            PubSubSeedAssertions.AssertUadpSeed(
                FuzzableCode.DecodeUadp(complete.Value, decodedContext), PubSubFieldEncoding.RawData);
            PubSubSeedAssertions.AssertDiagnostics(decodedContext, received: 1, dataSets: 1);
            ReplayChunkAdapters(frame);
        }

        [Test]
        public void StoredFragmentsDoNotPoisonTheCoreAndReassembleInBothOrders()
        {
            byte[][] frames =
            [
                PubSubSeedAssertions.LoadSeed("Chunks", "metadata-keyframe-chunk-000.bin"),
                PubSubSeedAssertions.LoadSeed("Chunks", "metadata-keyframe-chunk-001.bin"),
                PubSubSeedAssertions.LoadSeed("Chunks", "metadata-keyframe-chunk-002.bin")
            ];
            byte[] rawSeed = PubSubSeedAssertions.LoadSeed("Uadp", "metadata-keyframe-raw.uadp");
            for (int i = 0; i < frames.Length; i++)
            {
                Assert.That(
                    UadpChunker.TryParseChunk(
                        frames[i], out ushort sequence, out uint offset, out uint totalSize,
                        out ReadOnlyMemory<byte> fragment),
                    Is.True);
                Assert.That(sequence, Is.EqualTo(42));
                Assert.That(offset, Is.EqualTo(i * 32));
                Assert.That(totalSize, Is.EqualTo(79));
                Assert.That(fragment.Length, Is.EqualTo(i < 2 ? 32 : 15));
                Assert.That(fragment.ToArray(), Is.EqualTo(rawSeed.AsSpan(i * 32, fragment.Length).ToArray()));

                var clock = new ObservedTimeProvider(new FakeTimeProvider(PubSubSeedAssertions.SeedTime));
                PubSubNetworkMessageContext probeContext = FuzzableCode.NewContext(clock);
                // Raw and generated chunks deliberately share sequence 42 but advertise different total sizes.
                FuzzableCode.ExerciseUadpChunks(frames[i], probeContext);
                PubSubSeedAssertions.AssertDiagnostics(probeContext, invalid: 1);
                Assert.That(clock.UtcNowReads, Is.EqualTo(3));
                ReplayChunkAdapters(frames[i]);
            }

            foreach (bool reverse in new[] { false, true })
            {
                using var reassembler = new UadpReassembler(new FakeTimeProvider(PubSubSeedAssertions.SeedTime));
                ReadOnlyMemory<byte>? result = null;
                for (int i = 0; i < frames.Length; i++)
                {
                    byte[] frame = frames[reverse ? frames.Length - 1 - i : i];
                    bool complete = reassembler.TryAddChunk(PublisherId.FromUInt16(300), 1, frame, out result);
                    Assert.That(complete, Is.EqualTo(i == frames.Length - 1));
                    Assert.That(reassembler.PendingCount, Is.EqualTo(complete ? 0 : 1));
                    if (!complete)
                    {
                        Assert.That(result, Is.Null);
                        Assert.That(
                            reassembler.TryAddChunk(PublisherId.FromUInt16(300), 1, frame, out var duplicate),
                            Is.False);
                        Assert.That(duplicate, Is.Null);
                        Assert.That(reassembler.PendingCount, Is.EqualTo(1));
                    }
                }
                Assert.That(result, Is.Not.Null);
                Assert.That(result.Value.ToArray(), Is.EqualTo(rawSeed));
                PubSubNetworkMessageContext context = FuzzableCode.NewContext();
                PubSubSeedAssertions.AssertUadpSeed(
                    FuzzableCode.DecodeUadp(result.Value, context), PubSubFieldEncoding.RawData);
                PubSubSeedAssertions.AssertDiagnostics(context, received: 1, dataSets: 1);
            }
        }

        [Test]
        public void DuplicateWithDifferentBytesCannotOverwriteOrCompletePendingMessage()
        {
            using var reassembler = new UadpReassembler(new FakeTimeProvider(PubSubSeedAssertions.SeedTime));
            byte[] first = CreateChunk(42, 0, 8, 1, 2, 3, 4);
            AssertPending(reassembler, first);
            AssertPending(reassembler, first);
            AssertPending(reassembler, CreateChunk(42, 0, 8, 101, 102, 103, 104));

            AssertComplete(reassembler, CreateChunk(42, 4, 8, 5, 6, 7, 8), [1, 2, 3, 4, 5, 6, 7, 8]);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PartialAndContainedOverlapCannotReplacePreviouslyReceivedBytes(bool tailFirst)
        {
            using var reassembler = new UadpReassembler(new FakeTimeProvider(PubSubSeedAssertions.SeedTime));
            byte[] first = tailFirst ? CreateChunk(42, 4, 8, 5, 6, 7, 8) : CreateChunk(42, 0, 8, 1, 2, 3, 4);
            byte[] last = tailFirst ? CreateChunk(42, 0, 8, 1, 2, 3, 4) : CreateChunk(42, 4, 8, 5, 6, 7, 8);
            AssertPending(reassembler, first);
            AssertPending(reassembler, CreateChunk(42, 2, 8, 91, 92, 93, 94));
            AssertPending(reassembler, CreateChunk(42, tailFirst ? 5u : 1u, 8, 95, 96));

            // Adjacent, non-overlapping bytes must still complete; equality covers the attempted overwrites too.
            AssertComplete(reassembler, last, [1, 2, 3, 4, 5, 6, 7, 8]);
        }

        [TestCase(7u)]
        [TestCase(9u)]
        public void TotalSizeConflictDropsStateAndReleasesItsReservation(uint conflictingSize)
        {
            var clock = new FakeTimeProvider(PubSubSeedAssertions.SeedTime);
            using var reassembler = new UadpReassembler(SmallBudget(), clock);
            AssertPending(reassembler, CreateChunk(42, 0, 8, 1, 2, 3, 4));

            Assert.That(
                reassembler.TryAddChunk(
                    PublisherId.FromUInt16(300), 1, CreateChunk(42, 4, conflictingSize, 5), out var rejected),
                Is.False);
            Assert.That(rejected, Is.Null);
            Assert.That(reassembler.PendingCount, Is.Zero);

            // The eight-byte budget would prevent a restart if only the dictionary entry were cleared.
            AssertPending(reassembler, CreateChunk(42, 0, 8, 11, 12, 13, 14));
            AssertComplete(reassembler, CreateChunk(42, 4, 8, 15, 16, 17, 18), [11, 12, 13, 14, 15, 16, 17, 18]);
            AssertPending(reassembler, CreateChunk(43, 0, 8, 21, 22, 23, 24));
            AssertComplete(reassembler, CreateChunk(43, 4, 8, 25, 26, 27, 28), [21, 22, 23, 24, 25, 26, 27, 28]);
        }

        [Test]
        public void ReassemblyStillCompletesAtTheExactTimeoutBoundary()
        {
            var clock = new FakeTimeProvider(PubSubSeedAssertions.SeedTime);
            using var reassembler = new UadpReassembler(clock);
            AssertPending(reassembler, CreateChunk(42, 0, 8, 1, 2, 3, 4));
            clock.Advance(TimeSpan.FromSeconds(5));

            Assert.That(reassembler.Sweep(), Is.Zero);
            Assert.That(reassembler.PendingCount, Is.EqualTo(1));
            AssertPending(reassembler, CreateChunk(42, 0, 8, 1, 2, 3, 4));
            AssertComplete(reassembler, CreateChunk(42, 4, 8, 5, 6, 7, 8), [1, 2, 3, 4, 5, 6, 7, 8]);
            Assert.That(reassembler.Sweep(), Is.Zero);
        }

        [Test]
        public void ExpiryIsStrictlyAfterCreationTimeoutAndDuplicateDoesNotRefreshIt()
        {
            var clock = new FakeTimeProvider(PubSubSeedAssertions.SeedTime);
            using var reassembler = new UadpReassembler(SmallBudget(), clock);
            byte[] first = CreateChunk(42, 0, 8, 1, 2);
            AssertPending(reassembler, first);
            clock.Advance(TimeSpan.FromSeconds(4));
            AssertPending(reassembler, CreateChunk(42, 2, 8, 3, 4));
            AssertPending(reassembler, first);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(reassembler.Sweep(), Is.Zero);
            Assert.That(reassembler.PendingCount, Is.EqualTo(1));

            clock.Advance(TimeSpan.FromTicks(1));
            Assert.That(reassembler.Sweep(), Is.EqualTo(1));
            Assert.That(reassembler.PendingCount, Is.Zero);
            Assert.That(reassembler.Sweep(), Is.Zero);
            AssertPending(reassembler, CreateChunk(43, 0, 8, 11, 12, 13, 14));
            AssertComplete(reassembler, CreateChunk(43, 4, 8, 15, 16, 17, 18), [11, 12, 13, 14, 15, 16, 17, 18]);
        }

        [Test]
        public void ArrivalSweepsExpiredStateBeforeCheckingTheResourceBudget()
        {
            var clock = new FakeTimeProvider(PubSubSeedAssertions.SeedTime);
            using var reassembler = new UadpReassembler(SmallBudget(), clock);
            AssertPending(reassembler, CreateChunk(42, 0, 8, 1, 2, 3, 4));
            clock.Advance(TimeSpan.FromSeconds(5) + TimeSpan.FromTicks(1));

            AssertPending(reassembler, CreateChunk(43, 0, 8, 11, 12, 13, 14));
            AssertComplete(reassembler, CreateChunk(43, 4, 8, 15, 16, 17, 18), [11, 12, 13, 14, 15, 16, 17, 18]);
            Assert.That(reassembler.Sweep(), Is.Zero);
        }

        [Test]
        public void OversizedAdvertisedTotalsAreRejectedByTheDefaultEightMiBPolicy()
        {
            using var reassembler = new UadpReassembler(new FakeTimeProvider(PubSubSeedAssertions.SeedTime));
            Assert.That(UadpReassemblerOptions.DefaultMaxReassembledMessageSize, Is.EqualTo(8 * 1024 * 1024));
            foreach (uint advertisedTotal in new[] { 8388609u, uint.MaxValue })
            {
                // Eleven input bytes exercise the allocation guard without storing a huge fixture.
                byte[] frame = CreateChunk(42, 0, advertisedTotal, 1);
                Assert.That(frame, Has.Length.EqualTo(11));
                Assert.That(
                    reassembler.TryAddChunk(PublisherId.FromUInt16(300), 1, frame, out var rejected),
                    Is.False);
                Assert.That(rejected, Is.Null);
                Assert.That(reassembler.PendingCount, Is.Zero);
            }
            AssertPending(reassembler, CreateChunk(42, 0, 8, 1, 2, 3, 4));
            AssertComplete(reassembler, CreateChunk(42, 4, 8, 5, 6, 7, 8), [1, 2, 3, 4, 5, 6, 7, 8]);
        }

        [Test]
        public void MalformedChunkHeadersLeaveNoPendingStateAndCannotPoisonTheSharedCore()
        {
            byte[][] malformed =
            [
                [42],
                CreateChunk(42, 0, 8),
                CreateChunk(42, 0, 0, 1),
                CreateChunk(42, 0, 1, 1, 2),
                CreateChunk(42, 9, 8, 1),
                CreateChunk(42, 7, 8, 1, 2),
                CreateChunk(42, uint.MaxValue, 8, 1)
            ];
            foreach (byte[] frame in malformed)
            {
                using var reassembler = new UadpReassembler(new FakeTimeProvider(PubSubSeedAssertions.SeedTime));
                Assert.That(
                    reassembler.TryAddChunk(PublisherId.FromUInt16(300), 1, frame, out var rejected),
                    Is.False);
                Assert.That(rejected, Is.Null);
                Assert.That(reassembler.PendingCount, Is.Zero);

                PubSubNetworkMessageContext context = FuzzableCode.NewContext();
                FuzzableCode.ExerciseUadpChunks(frame, context);
                PubSubSeedAssertions.AssertDiagnostics(context, invalid: 1);
                ReplayChunkAdapters(frame);
            }
        }

        private static UadpReassemblerOptions SmallBudget()
        {
            return new UadpReassemblerOptions
            {
                MaxReassembledMessageSize = 16,
                MaxConcurrentReassemblies = 1,
                MaxAggregatePendingBytes = 8,
                ChunkTimeout = TimeSpan.FromSeconds(5)
            };
        }

        private static byte[] CreatePayload(int length)
        {
            byte[] payload = new byte[length];
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(((i * 31) + (i / 251) + 17) % 256);
            }
            return payload;
        }

        private static byte[] CreateChunk(ushort sequence, uint offset, uint totalSize, params byte[] payload)
        {
            byte[] frame = new byte[10 + payload.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(frame, sequence);
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(2), offset);
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(6), totalSize);
            payload.CopyTo(frame.AsSpan(10));
            return frame;
        }

        private static void AssertPending(UadpReassembler reassembler, byte[] frame)
        {
            Assert.That(
                reassembler.TryAddChunk(PublisherId.FromUInt16(300), 1, frame, out var result),
                Is.False);
            Assert.That(result, Is.Null);
            Assert.That(reassembler.PendingCount, Is.EqualTo(1));
        }

        private static void AssertComplete(UadpReassembler reassembler, byte[] frame, byte[] expected)
        {
            Assert.That(
                reassembler.TryAddChunk(PublisherId.FromUInt16(300), 1, frame, out var result),
                Is.True);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Value.ToArray(), Is.EqualTo(expected));
            Assert.That(reassembler.PendingCount, Is.Zero);
        }

        private static void ReplayChunkAdapters(byte[] frame)
        {
            using var stream = new MemoryStream(frame, writable: false);
            FuzzableCode.AflfuzzUadpChunkReassembly(stream);
            Assert.That(stream.Position, Is.EqualTo(frame.Length));
            Assert.That(stream.CanRead, Is.True);
            FuzzableCode.LibfuzzUadpChunkReassembly(frame);
        }

        private sealed class ObservedTimeProvider(FakeTimeProvider clock) : TimeProvider
        {
            public int UtcNowReads { get; private set; }

            public override DateTimeOffset GetUtcNow()
            {
                UtcNowReads++;
                return clock.GetUtcNow();
            }
        }
    }
}
