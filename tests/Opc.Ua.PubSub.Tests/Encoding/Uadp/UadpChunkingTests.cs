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
using UadpDataSetMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

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
            IReadOnlyList<byte[]> chunks = chunker.Split(payload, 0x42, 512 + UadpChunker.ChunkHeaderSize);
            Assert.That(chunks, Has.Count.EqualTo(2));
            Assert.That(chunks[0], Has.Length.EqualTo(512 + UadpChunker.ChunkHeaderSize));
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
            {
                rng.GetBytes(payload);
            }
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
            {
                rng.GetBytes(payload);
            }
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
            {
                rng.GetBytes(payload);
            }
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
        public void ReassembleTotalSizeConflictKeepsPendingEntry()
        {
            byte[] payload1 = new byte[512];
            byte[] payload2 = new byte[1024];
            var chunker = new UadpChunker();
            IReadOnlyList<byte[]> chunksOfA = chunker.Split(payload1, 4, 256);
            byte[] firstChunkOfB = chunker.Split(payload2, 4, 256)[0];

            var reassembler = new UadpReassembler();
            var pid = PublisherId.FromByte(5);
            bool a = reassembler.TryAddChunk(pid, 0, chunksOfA[0], out _);
            Assert.That(a, Is.False);
            bool b = reassembler.TryAddChunk(pid, 0, firstChunkOfB, out _);
            Assert.That(b, Is.False);
            // An unauthenticated chunk with a conflicting TotalSize is
            // dropped without evicting the pending reassembly.
            Assert.That(reassembler.PendingCount, Is.EqualTo(1));

            ReadOnlyMemory<byte>? result = null;
            for (int i = 1; i < chunksOfA.Count; i++)
            {
                _ = reassembler.TryAddChunk(pid, 0, chunksOfA[i], out result);
            }
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Value.ToArray(), Is.EqualTo(payload1));
        }

        [Test]
        public void ReassembleRejectsChunkNotAlignedToTheChunkSize()
        {
            var reassembler = new UadpReassembler();
            var pid = PublisherId.FromByte(1);

            // First chunk fixes the chunk size to 4 bytes.
            Assert.That(reassembler.TryAddChunk(pid, 0, BuildChunk(1, 0, 12, 4), out _), Is.False);
            // Misaligned and wrongly sized non-last chunks are rejected.
            Assert.That(reassembler.TryAddChunk(pid, 0, BuildChunk(1, 2, 12, 4), out _), Is.False);
            Assert.That(reassembler.TryAddChunk(pid, 0, BuildChunk(1, 4, 12, 2), out _), Is.False);
            Assert.That(reassembler.TryAddChunk(pid, 0, BuildChunk(1, 4, 12, 4), out _), Is.False);
            Assert.That(
                reassembler.TryAddChunk(pid, 0, BuildChunk(1, 8, 12, 4), out ReadOnlyMemory<byte>? result),
                Is.True);
            Assert.That(result!.Value.Length, Is.EqualTo(12));
        }

        [Test]
        public void ReassembleRejectsMoreChunksThanAllowedPerMessage()
        {
            var reassembler = new UadpReassembler(new UadpReassemblerOptions
            {
                MaxChunksPerMessage = 8
            });

            // A one-byte chunk of a 100 byte payload implies 100 chunks.
            Assert.That(
                reassembler.TryAddChunk(PublisherId.FromByte(1), 0, BuildChunk(1, 0, 100, 1), out _),
                Is.False);
            Assert.That(reassembler.PendingCount, Is.Zero);
        }

        [Test]
        public void SpoofedFirstChunksDoNotStarveLegitimateReassembly()
        {
            // Default limits: 64 MiB aggregate, 8 MiB per message. Before the
            // fix eight 1-byte chunks advertising 8 MiB each reserved the
            // whole budget and every further reassembly was refused.
            var reassembler = new UadpReassembler();
            var spoofed = PublisherId.FromByte(66);
            for (ushort i = 0; i < 16; i++)
            {
                Assert.That(
                    reassembler.TryAddChunk(spoofed, 0, BuildChunk(i, 0, 8 * 1024 * 1024, 1), out _),
                    Is.False);
            }

            byte[] payload = new byte[600];
            payload.AsSpan().Fill(0x5A);
            IReadOnlyList<byte[]> chunks = new UadpChunker().Split(payload, 7, 256);
            var legitimate = PublisherId.FromByte(1);
            ReadOnlyMemory<byte>? result = null;
            foreach (byte[] chunk in chunks)
            {
                _ = reassembler.TryAddChunk(legitimate, 0, chunk, out result);
            }
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Value.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public void ReassembleEvictsTheOldestEntryWhenTheConcurrencyLimitIsReached()
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));
            var reassembler = new UadpReassembler(
                new UadpReassemblerOptions { MaxConcurrentReassemblies = 1 },
                clock);
            var pid = PublisherId.FromByte(1);

            Assert.That(reassembler.TryAddChunk(pid, 0, BuildChunk(1, 0, 8, 4), out _), Is.False);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(reassembler.TryAddChunk(pid, 0, BuildChunk(2, 0, 8, 4), out _), Is.False);
            Assert.That(reassembler.PendingCount, Is.EqualTo(1));

            // The newer reassembly survived, the older one was evicted.
            Assert.That(reassembler.TryAddChunk(pid, 0, BuildChunk(2, 4, 8, 4), out _), Is.True);
            Assert.That(reassembler.TryAddChunk(pid, 0, BuildChunk(1, 4, 8, 4), out _), Is.False);
        }

        [Test]
        public void ReassemblyIsKeyedByDataSetWriterId()
        {
            var reassembler = new UadpReassembler();
            var pid = PublisherId.FromByte(1);

            Assert.That(reassembler.TryAddChunk(pid, 0, 10, BuildChunk(1, 0, 8, 4), out _), Is.False);
            Assert.That(reassembler.TryAddChunk(pid, 0, 11, BuildChunk(1, 0, 8, 4), out _), Is.False);
            Assert.That(reassembler.PendingCount, Is.EqualTo(2));
            Assert.That(reassembler.TryAddChunk(pid, 0, 10, BuildChunk(1, 4, 8, 4), out _), Is.True);
            Assert.That(reassembler.TryAddChunk(pid, 0, 11, BuildChunk(1, 4, 8, 4), out _), Is.True);
        }

        [Test]
        public void ReassembleManySmallChunksIsLinear()
        {
            // 16384 one-byte chunks: the old overlap scan was quadratic.
            const int count = 16384;
            var reassembler = new UadpReassembler();
            var pid = PublisherId.FromByte(1);
            ReadOnlyMemory<byte>? result = null;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (uint i = 0; i < count; i++)
            {
                _ = reassembler.TryAddChunk(pid, 0, BuildChunk(1, i, count, 1), out result);
            }
            watch.Stop();
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Value.Length, Is.EqualTo(count));
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        }

        [Test]
        public void Reassemble_TimeoutExpiresPartialState()
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));
            var reassembler = new UadpReassembler(clock, TimeSpan.FromSeconds(1));

            byte[] payload = new byte[2048];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(payload);
            }
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
                // One 1-byte chunk plus its bookkeeping charge fits, two do not.
                MaxAggregatePendingBytes = 100
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
            byte[] frame = BuildChunk(1, 100, 10, 4);

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

        [Test]
        public void EncodeChunksWritesThePart14ChunkNetworkMessageLayout()
        {
            UadpNetworkMessage message = CreateMessage(fieldBytes: 40);
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();

            IReadOnlyList<UadpChunkFrame> chunks = UadpEncoder.EncodeChunks(
                message, context, maxNetworkMessageSize: 40, securityOverhead: 0,
                securityEnabled: false, fallbackSequenceNumber: 99);

            Assert.That(chunks, Has.Count.GreaterThan(1));
            byte[] first = chunks[0].Frame.ToArray();
            // UADPFlags: v1, PublisherId, GroupHeader, PayloadHeader, ExtendedFlags1.
            Assert.That(first[0], Is.EqualTo((byte)0xF1));
            // ExtendedFlags1: ExtendedFlags2 enabled. ExtendedFlags2: chunk.
            Assert.That(first[1], Is.EqualTo((byte)0x80));
            Assert.That(first[2], Is.EqualTo((byte)0x01));
            // PublisherId (Byte), GroupHeader (WriterGroupId 3).
            Assert.That(first[3], Is.EqualTo((byte)7));
            Assert.That(first[4], Is.EqualTo((byte)GroupFlagsEncodingMask.WriterGroupIdEnabled));
            Assert.That(first[5] | (first[6] << 8), Is.EqualTo(3));
            // Table 158: PayloadHeader is the DataSetWriterId only.
            Assert.That(first[7] | (first[8] << 8), Is.EqualTo(21));
            Assert.That(chunks[0].PayloadOffset, Is.EqualTo(9));
            // Table 159: MessageSequenceNumber (the DataSetMessage sequence
            // number), ChunkOffset, TotalSize and a ByteString ChunkData.
            Assert.That(
                UadpChunker.TryParseChunk(
                    chunks[0].Frame[chunks[0].PayloadOffset..],
                    out ushort sequence, out uint offset, out uint total, out ReadOnlyMemory<byte> data),
                Is.True);
            Assert.That(sequence, Is.EqualTo((ushort)1234));
            Assert.That(offset, Is.Zero);
            Assert.That(data.Length, Is.EqualTo(40 - 9 - UadpChunker.ChunkHeaderSize));
            Assert.That(total, Is.GreaterThan((uint)data.Length));
            foreach (UadpChunkFrame chunk in chunks)
            {
                Assert.That(chunk.Frame.Length, Is.LessThanOrEqualTo(40));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EncodedChunksReassembleIntoTheOriginalNetworkMessage(bool reverse)
        {
            UadpNetworkMessage message = CreateMessage(fieldBytes: 300);
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            List<UadpChunkFrame> chunks = [.. UadpEncoder.EncodeChunks(
                message, context, maxNetworkMessageSize: 64, securityOverhead: 0,
                securityEnabled: false, fallbackSequenceNumber: 1)];
            if (reverse)
            {
                chunks.Reverse();
            }

            var reassembler = new UadpReassembler();
            byte[]? rebuilt = null;
            foreach (UadpChunkFrame chunk in chunks)
            {
                Assert.That(UadpDecoder.TryReadPrefix(chunk.Frame, out UadpPrefixInfo prefix), Is.True);
                Assert.That(prefix.ChunkMessage, Is.True);
                Assert.That(prefix.ChunkDataSetWriterId, Is.EqualTo((ushort?)21));
                Assert.That(prefix.PrefixLength, Is.EqualTo(chunk.PayloadOffset));
                if (reassembler.TryAddChunk(
                    prefix.PublisherId, prefix.WriterGroupId, prefix.ChunkDataSetWriterId,
                    chunk.Frame[prefix.PrefixLength..], out ReadOnlyMemory<byte>? payload))
                {
                    rebuilt = UadpChunker.ComposeReassembledNetworkMessage(
                        chunk.Frame.Span, prefix, payload!.Value.Span);
                }
            }

            Assert.That(rebuilt, Is.Not.Null);
            PubSubNetworkMessage? decoded = UadpDecoder.Decode(rebuilt, context);
            Assert.That(decoded, Is.InstanceOf<UadpNetworkMessage>());
            var uadp = (UadpNetworkMessage)decoded!;
            Assert.That(uadp.PublisherId, Is.EqualTo(message.PublisherId));
            Assert.That(uadp.WriterGroupId, Is.EqualTo(message.WriterGroupId));
            Assert.That(uadp.DataSetMessages, Has.Count.EqualTo(1));
            Assert.That(uadp.DataSetMessages[0].DataSetWriterId, Is.EqualTo((ushort)21));
            Assert.That(
                uadp.DataSetMessages[0].Fields[0].Value.TryGetValue(out ByteString value),
                Is.True);
            Assert.That(value.Span.ToArray(), Is.EqualTo(CreateFieldBytes(300)));
        }

        [Test]
        public void EncodeChunksSplitsEveryDataSetMessageSeparately()
        {
            UadpNetworkMessage message = CreateMessage(fieldBytes: 100) with
            {
                DataSetMessages =
                [
                    CreateDataSetMessage(21, 100),
                    CreateDataSetMessage(22, 100)
                ]
            };
            IReadOnlyList<UadpChunkFrame> chunks = UadpEncoder.EncodeChunks(
                message, UadpTestUtilities.NewContext(), maxNetworkMessageSize: 64, securityOverhead: 0,
                securityEnabled: false, fallbackSequenceNumber: 1);

            var writers = new HashSet<ushort>();
            foreach (UadpChunkFrame chunk in chunks)
            {
                Assert.That(UadpDecoder.TryReadPrefix(chunk.Frame, out UadpPrefixInfo prefix), Is.True);
                writers.Add(prefix.ChunkDataSetWriterId!.Value);
            }
            Assert.That(writers, Is.EquivalentTo(new ushort[] { 21, 22 }));
        }

        [Test]
        public void ChunkedDiscoveryAnnouncementWithoutPayloadHeaderReassembles()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var response = new UadpDiscoveryResponseMessage
            {
                PublisherId = PublisherId.FromUInt16(0x33),
                SequenceNumber = 1234,
                DiscoveryType = UadpDiscoveryType.DataSetWriterConfiguration,
                DataSetWriterIds = new ushort[] { 10, 20 },
                WriterConfiguration = new WriterGroupDataType { Name = "discovery-chunks" },
                StatusCode = StatusCodes.Good
            };
            byte[] encoded = UadpDiscoveryCoder.Encode(response, context);
            Assert.That(UadpDecoder.TryReadPrefix(encoded, out UadpPrefixInfo original), Is.True);

            // Part 14 §7.2.4.4.4: announcement chunks carry no PayloadHeader.
            byte[] header = encoded.AsSpan(0, original.PrefixLength).ToArray();
            header[2] |= (byte)ExtendedFlags2EncodingMask.ChunkMessage;
            IReadOnlyList<byte[]> pieces = new UadpChunker().Split(
                encoded.AsMemory(original.PrefixLength), 5, UadpChunker.ChunkHeaderSize + 16);
            Assert.That(pieces, Has.Count.GreaterThan(1));

            var reassembler = new UadpReassembler();
            byte[]? rebuilt = null;
            foreach (byte[] piece in pieces)
            {
                byte[] frame = [.. header, .. piece];
                Assert.That(UadpDecoder.TryReadPrefix(frame, out UadpPrefixInfo prefix), Is.True);
                Assert.That(prefix.ChunkMessage, Is.True);
                Assert.That(prefix.ChunkDataSetWriterId, Is.Null);
                if (reassembler.TryAddChunk(
                    prefix.PublisherId, prefix.WriterGroupId, prefix.ChunkDataSetWriterId,
                    frame.AsMemory(prefix.PrefixLength), out ReadOnlyMemory<byte>? payload))
                {
                    rebuilt = UadpChunker.ComposeReassembledNetworkMessage(frame, prefix, payload!.Value.Span);
                }
            }

            Assert.That(rebuilt, Is.EqualTo(encoded));
        }

        private static UadpNetworkMessage CreateMessage(int fieldBytes)
        {
            return new UadpNetworkMessage
            {
                ContentMask =
                    UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = PublisherId.FromByte(7),
                WriterGroupId = 3,
                DataSetMessages = [CreateDataSetMessage(21, fieldBytes)]
            };
        }

        private static UadpDataSetMessage CreateDataSetMessage(ushort writerId, int fieldBytes)
        {
            return new UadpDataSetMessage
            {
                DataSetWriterId = writerId,
                ContentMask = UadpDataSetMessageContentMask.SequenceNumber,
                SequenceNumber = 1234,
                FieldEncoding = PubSubFieldEncoding.Variant,
                Fields = [new DataSetField { Value = new Variant(new ByteString(CreateFieldBytes(fieldBytes))) }]
            };
        }

        private static byte[] CreateFieldBytes(int length)
        {
            byte[] bytes = new byte[length];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)(i * 7);
            }
            return bytes;
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
            // ChunkData ByteString length (Table 159).
            frame[10] = (byte)(payloadLength & 0xFF);
            frame[11] = (byte)((payloadLength >> 8) & 0xFF);
            frame[12] = (byte)((payloadLength >> 16) & 0xFF);
            frame[13] = (byte)((payloadLength >> 24) & 0xFF);

            for (int i = 0; i < payloadLength; i++)
            {
                frame[UadpChunker.ChunkHeaderSize + i] = (byte)(i + 1);
            }

            return frame;
        }
    }
}
