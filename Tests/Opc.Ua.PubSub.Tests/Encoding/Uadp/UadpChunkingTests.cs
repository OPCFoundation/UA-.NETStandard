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
using System.Collections.Generic;
using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;

namespace Opc.Ua.PubSub.Tests.Encoding.Uadp
{
    /// <summary>
    /// Coverage for the UADP chunker and reassembler. Validates that a
    /// large encoded message can be split + reassembled, and that the
    /// reassembler drops duplicates and expires partial state.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.4.4")]
    public class UadpChunkingTests
    {
        [Test]
        public void Split_TwiceMaxFrameSize_ProducesTwoChunks()
        {
            byte[] payload = new byte[1024];
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(i & 0xFF);
            }

            var chunker = new UadpChunker();
            IReadOnlyList<byte[]> chunks = chunker.Split(payload, 0x42, 522);
            Assert.That(chunks, Has.Count.EqualTo(2));
            Assert.That(chunks[0], Has.Length.EqualTo(522));
            Assert.That(chunks[1],
                Has.Length.EqualTo(1024 - 512 + UadpChunker.ChunkHeaderSize));
        }

        [Test]
        public void Split_SmallMessage_OneChunk()
        {
            byte[] payload = new byte[64];
            var chunker = new UadpChunker();
            IReadOnlyList<byte[]> chunks = chunker.Split(payload, 1, 1500);
            Assert.That(chunks, Has.Count.EqualTo(1));
            Assert.That(chunks[0], Has.Length.EqualTo(64 + UadpChunker.ChunkHeaderSize));
        }

        [Test]
        public void Split_EmptyMessage_Throws()
        {
            var chunker = new UadpChunker();
            Assert.That(
                () => chunker.Split(ReadOnlyMemory<byte>.Empty, 0, 100),
                Throws.ArgumentException);
        }

        [Test]
        public void Split_TooSmallFrame_Throws()
        {
            var chunker = new UadpChunker();
            Assert.That(
                () => chunker.Split(new byte[10], 0, UadpChunker.ChunkHeaderSize),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void TryParseChunk_RoundTripsHeader()
        {
            byte[] payload = new byte[100];
            using (var rng = RandomNumberGenerator.Create())
            { rng.GetBytes(payload); }
            var chunker = new UadpChunker();
            byte[] frame = chunker.Split(payload, 0xABCD, 200)[0];

            bool ok = UadpChunker.TryParseChunk(
                frame, out ushort seq, out uint offset, out uint total,
                out ReadOnlyMemory<byte> body);
            Assert.That(ok, Is.True);
            Assert.That(seq, Is.EqualTo((ushort)0xABCD));
            Assert.That(offset, Is.Zero);
            Assert.That(total, Is.EqualTo((uint)100));
            Assert.That(body, Has.Length.EqualTo(100));
        }

        [Test]
        public void TryParseChunk_TooShort_ReturnsFalse()
        {
            Assert.That(UadpChunker.TryParseChunk(
                new byte[3], out _, out _, out _, out _), Is.False);
        }

        [Test]
        public void Reassemble_OrderedChunks_ProducesOriginal()
        {
            byte[] payload = new byte[2048];
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(i & 0xFF);
            }

            var chunker = new UadpChunker();
            IReadOnlyList<byte[]> chunks = chunker.Split(payload, 1, 256);
            var reassembler = new UadpReassembler();
            var pid = PublisherId.FromByte(1);

            ReadOnlyMemory<byte>? result = null;
            for (int i = 0; i < chunks.Count; i++)
            {
                if (reassembler.TryAddChunk(pid, 5, chunks[i], out result))
                {
                    Assert.That(i, Is.EqualTo(chunks.Count - 1),
                        "Reassembly only completes after the final chunk");
                }
            }
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Value.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public void Reassemble_OutOfOrderChunks_ProducesOriginal()
        {
            byte[] payload = new byte[1500];
            using (var rng = RandomNumberGenerator.Create())
            { rng.GetBytes(payload); }
            var chunker = new UadpChunker();
            byte[][] chunks = [.. chunker.Split(payload, 9, 256)];
            // Reverse order
            Array.Reverse(chunks);

            var reassembler = new UadpReassembler();
            var pid = PublisherId.FromByte(2);

            ReadOnlyMemory<byte>? result = null;
            for (int i = 0; i < chunks.Length; i++)
            {
                _ = reassembler.TryAddChunk(pid, 0, chunks[i], out result);
            }
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Value.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public void Reassemble_DuplicateChunkRejected()
        {
            byte[] payload = new byte[512];
            using (var rng = RandomNumberGenerator.Create())
            { rng.GetBytes(payload); }
            var chunker = new UadpChunker();
            IReadOnlyList<byte[]> chunks = chunker.Split(payload, 4, 256);
            Assert.That(chunks, Has.Count.GreaterThanOrEqualTo(2));

            var reassembler = new UadpReassembler();
            var pid = PublisherId.FromByte(3);
            bool first = reassembler.TryAddChunk(pid, 0, chunks[0], out _);
            Assert.That(first, Is.False);
            bool dup = reassembler.TryAddChunk(pid, 0, chunks[0], out _);
            Assert.That(dup, Is.False);
            Assert.That(reassembler.PendingCount, Is.EqualTo(1));
        }

        [Test]
        public void Reassemble_TotalSizeConflictDropsEntry()
        {
            byte[] payload1 = new byte[512];
            byte[] payload2 = new byte[1024];
            var chunker = new UadpChunker();
            byte[] firstChunkOfA = chunker.Split(payload1, 4, 256)[0];
            byte[] firstChunkOfB = chunker.Split(payload2, 4, 256)[0];

            var reassembler = new UadpReassembler();
            var pid = PublisherId.FromByte(5);
            bool a = reassembler.TryAddChunk(pid, 0, firstChunkOfA, out _);
            Assert.That(a, Is.False);
            bool b = reassembler.TryAddChunk(pid, 0, firstChunkOfB, out _);
            Assert.That(b, Is.False);
            // The conflicting second chunk dropped the entry.
            Assert.That(reassembler.PendingCount, Is.Zero);
        }

        [Test]
        public void Reassemble_TimeoutExpiresPartialState()
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));
            var reassembler = new UadpReassembler(clock, TimeSpan.FromSeconds(1));

            byte[] payload = new byte[2048];
            using (var rng = RandomNumberGenerator.Create())
            { rng.GetBytes(payload); }
            IReadOnlyList<byte[]> chunks = new UadpChunker().Split(payload, 7, 256);

            var pid = PublisherId.FromByte(7);
            bool added = reassembler.TryAddChunk(pid, 0, chunks[0], out _);
            Assert.That(added, Is.False);
            Assert.That(reassembler.PendingCount, Is.EqualTo(1));

            // Advance past TTL and confirm Sweep clears it.
            clock.Advance(TimeSpan.FromSeconds(5));
            int removed = reassembler.Sweep();
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(reassembler.PendingCount, Is.Zero);
        }

        [Test]
        public void Reassemble_MalformedChunkRejected()
        {
            var reassembler = new UadpReassembler();
            bool ok = reassembler.TryAddChunk(
                PublisherId.FromByte(1), 0,
                new byte[3], out ReadOnlyMemory<byte>? result);
            Assert.That(ok, Is.False);
            Assert.That(result, Is.Null);
        }

        [Test]
        [TestSpec("7.2.4.4.4")]
        public void ReassembleTotalSizeExceedingMaximumIsRejected()
        {
            var reassembler = new UadpReassembler(new UadpReassemblerOptions
            {
                MaxReassembledMessageSize = 8
            });
            byte[] frame = BuildChunk(
                sequenceNumber: 1,
                chunkOffset: 0,
                totalSize: 1024,
                payloadLength: 1);

            bool ok = reassembler.TryAddChunk(
                PublisherId.FromByte(1), 0, frame, out ReadOnlyMemory<byte>? result);

            Assert.That(ok, Is.False);
            Assert.That(result, Is.Null);
            Assert.That(reassembler.PendingCount, Is.Zero);
        }

        [Test]
        [TestSpec("7.2.4.4.4")]
        public void ReassembleTotalSizeInNegativeCastRangeIsRejected()
        {
            var reassembler = new UadpReassembler();
            byte[] frame = BuildChunk(
                sequenceNumber: 1,
                chunkOffset: 0,
                totalSize: uint.MaxValue,
                payloadLength: 1);

            bool ok = reassembler.TryAddChunk(
                PublisherId.FromByte(1), 0, frame, out ReadOnlyMemory<byte>? result);

            Assert.That(ok, Is.False);
            Assert.That(result, Is.Null);
            Assert.That(reassembler.PendingCount, Is.Zero);
        }

        [Test]
        [TestSpec("7.2.4.4.4")]
        public void ReassembleConcurrentPendingContextsStayBounded()
        {
            var reassembler = new UadpReassembler(new UadpReassemblerOptions
            {
                MaxConcurrentReassemblies = 2,
                MaxAggregatePendingBytes = 1024
            });
            var publisherId = PublisherId.FromByte(1);

            Assert.That(reassembler.TryAddChunk(
                publisherId, 0, BuildChunk(1, 0, 100, 1), out _), Is.False);
            Assert.That(reassembler.TryAddChunk(
                publisherId, 0, BuildChunk(2, 0, 100, 1), out _), Is.False);
            Assert.That(reassembler.TryAddChunk(
                publisherId, 0, BuildChunk(3, 0, 100, 1), out _), Is.False);

            Assert.That(reassembler.PendingCount, Is.EqualTo(2));
        }

        [Test]
        [TestSpec("7.2.4.4.4")]
        public void ReassembleAggregatePendingBytesStayBounded()
        {
            var reassembler = new UadpReassembler(new UadpReassemblerOptions
            {
                MaxConcurrentReassemblies = 10,
                MaxAggregatePendingBytes = 150
            });
            var publisherId = PublisherId.FromByte(1);

            Assert.That(reassembler.TryAddChunk(
                publisherId, 0, BuildChunk(1, 0, 100, 1), out _), Is.False);
            Assert.That(reassembler.TryAddChunk(
                publisherId, 0, BuildChunk(2, 0, 100, 1), out _), Is.False);

            Assert.That(reassembler.PendingCount, Is.EqualTo(1));
        }

        [Test]
        public void Reassemble_OffsetBeyondTotalRejected()
        {
            // Build a synthetic chunk with offset > total.
            byte[] frame = new byte[UadpChunker.ChunkHeaderSize + 4];
            // seq=1, offset=100, total=10, payload=4 bytes
            frame[0] = 0x01;
            frame[1] = 0x00;
            frame[2] = 100;
            frame[3] = 0;
            frame[4] = 0;
            frame[5] = 0;
            frame[6] = 10;
            frame[7] = 0;
            frame[8] = 0;
            frame[9] = 0;

            var reassembler = new UadpReassembler();
            bool ok = reassembler.TryAddChunk(
                PublisherId.FromByte(1), 0, frame, out _);
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Reassembler_Dispose_Clears()
        {
            var reassembler = new UadpReassembler();
            byte[] payload = new byte[128];
            byte[] chunk = new UadpChunker().Split(payload, 1, 64)[0];
            _ = reassembler.TryAddChunk(PublisherId.FromByte(1), 0, chunk, out _);
            Assert.That(reassembler.PendingCount, Is.GreaterThan(0));
            reassembler.Dispose();
            Assert.That(reassembler.PendingCount, Is.Zero);
        }

        private static byte[] BuildChunk(
            ushort sequenceNumber,
            uint chunkOffset,
            uint totalSize,
            int payloadLength)
        {
            byte[] frame = new byte[UadpChunker.ChunkHeaderSize + payloadLength];
            frame[0] = (byte)(sequenceNumber & 0xFF);
            frame[1] = (byte)(sequenceNumber >> 8);
            frame[2] = (byte)(chunkOffset & 0xFF);
            frame[3] = (byte)((chunkOffset >> 8) & 0xFF);
            frame[4] = (byte)((chunkOffset >> 16) & 0xFF);
            frame[5] = (byte)((chunkOffset >> 24) & 0xFF);
            frame[6] = (byte)(totalSize & 0xFF);
            frame[7] = (byte)((totalSize >> 8) & 0xFF);
            frame[8] = (byte)((totalSize >> 16) & 0xFF);
            frame[9] = (byte)((totalSize >> 24) & 0xFF);

            for (int i = 0; i < payloadLength; i++)
            {
                frame[UadpChunker.ChunkHeaderSize + i] = (byte)(i + 1);
            }

            return frame;
        }
    }
}
