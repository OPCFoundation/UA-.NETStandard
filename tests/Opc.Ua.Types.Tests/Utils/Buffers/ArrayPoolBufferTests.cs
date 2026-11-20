// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Buffers;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Opc.Ua.Buffers;

namespace Opc.Ua.Types.Buffers.Tests
{
    /// <summary>
    /// Tests for <see cref="ArrayPoolBufferWriter{T}"/> where T is <see cref="byte"/>.
    /// </summary>
    [TestFixture]
    [Category("Buffers")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable(ParallelScope.All)]
    public class ArrayPoolBufferTests
    {
        /// <summary>
        /// Test the default behavior of <see cref="ArrayPoolBufferWriter{T}"/>.
        /// </summary>
        [Test]
        public void ArrayPoolBufferWriterWhenConstructedWithDefaultOptionsShouldNotThrow()
        {
            // Arrange
            var writer = new ArrayPoolBufferWriter<byte>();

            // Act
            Action act = writer.Dispose;
            byte[] buffer = [0x23];

            Memory<byte> memory = writer.GetMemory(1);
            memory.Span[0] = 0x12;
            writer.Advance(1);
            writer.Write(buffer);
            ReadOnlySequence<byte> sequence = writer.GetReadOnlySequence();
            Assert.ByVal(sequence.Length, Is.EqualTo(2));
            Assert.That(sequence.ToArray(), Is.EqualTo(new byte[] { 0x12, 0x23 }));

            // Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.GetMemory(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.GetSpan(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.Advance(-1));
            Assert.Throws<InvalidOperationException>(() => writer.Advance(2));

            act();

            Assert.Throws<ObjectDisposedException>(() => writer.GetReadOnlySequence());
            Assert.Throws<ObjectDisposedException>(() => writer.GetMemory(1));
            Assert.Throws<ObjectDisposedException>(() => writer.GetSpan(1));
            Assert.Throws<ObjectDisposedException>(() => writer.Advance(1));
        }

        /// <summary>
        /// Test the default behavior of <see cref="ArrayPoolBufferWriter{T}"/>.
        /// </summary>
        [Theory]
        public void ArrayPoolBufferWriterChunking(
            [Values(0, 1, 16, 128, 333, 1024, 7777)] int chunkSize,
            [Values(16, 333, 1024, 4096)] int defaultChunkSize,
            [Values(0, 1024, 4096, 65536)] int maxChunkSize)
        {
            int length;
            ReadOnlySequence<byte> sequence;
            byte[] buffer;

            // Arrange
            using var writer = new ArrayPoolBufferWriter<byte>(
                false,
                defaultChunkSize,
                maxChunkSize);
            // Act
            for (int i = 0; i <= byte.MaxValue; i++)
            {
                Span<byte> span;
                int randomGetChunkSize = maxChunkSize > 0
                    ? chunkSize + UnsecureRandom.Shared.Next(maxChunkSize)
                    : chunkSize;

                int repeats = UnsecureRandom.Shared.Next(3);
                do
                {
                    // get a new chunk
                    if (UnsecureRandom.Shared.Next(2) == 0)
                    {
                        Memory<byte> memory = writer.GetMemory(randomGetChunkSize);
                        Assert.That(memory.Length, Is.GreaterThanOrEqualTo(chunkSize));
                        span = memory.Span;
                    }
                    else
                    {
                        span = writer.GetSpan(randomGetChunkSize);
                    }

                    Assert.That(span.Length, Is.GreaterThanOrEqualTo(chunkSize));
                } while (repeats-- > 0);

                // fill chunk with a byte
                for (int v = 0; v < chunkSize; v++)
                {
                    span[v] = (byte)i;
                }

                writer.Advance(chunkSize);

                // Assert interim projections
                if (UnsecureRandom.Shared.Next(10) == 0)
                {
                    length = chunkSize * (i + 1);
                    sequence = writer.GetReadOnlySequence();
                    buffer = sequence.ToArray();

                    // Assert
                    Assert.That(buffer, Has.Length.EqualTo(length));
                    Assert.That(sequence.Length, Is.EqualTo(length));
                }
            }

            length = (byte.MaxValue + 1) * chunkSize;
            sequence = writer.GetReadOnlySequence();
            buffer = sequence.ToArray();

            // Assert
            Assert.That(sequence.Length, Is.EqualTo(length));
            Assert.That(buffer, Has.Length.EqualTo(length));

            for (int i = 0; i < buffer.Length; i++)
            {
                Assert.That(buffer[i], Is.EqualTo((byte)(i / chunkSize)));
            }
        }

        /// <summary>
        /// Security regression: when constructed with <c>clearArray: true</c>
        /// the writer must zero the rented backing buffer when it is returned
        /// to the pool on dispose, so sensitive data (e.g. an encoded secret)
        /// is not exposed to the next consumer of the shared pool. Guards the
        /// BinaryEncoder owned-buffer fix. Non-parallel so no other test can
        /// rent the just-returned buffer before the assertion.
        /// </summary>
        [Test]
        [NonParallelizable]
        public void ArrayPoolBufferWriterClearArrayZeroesBufferOnReturn()
        {
            const int count = 128;
            const byte sentinel = 0xAB;

            var writer = new ArrayPoolBufferWriter<byte>(clearArray: true);
            byte[] backing;
            int offset;
            try
            {
                Span<byte> span = writer.GetSpan(count);
                span[..count].Fill(sentinel);
                writer.Advance(count);

                ReadOnlySequence<byte> sequence = writer.GetReadOnlySequence();
                Assert.That(
                    MemoryMarshal.TryGetArray(sequence.First, out ArraySegment<byte> segment),
                    Is.True);
                backing = segment.Array!;
                offset = segment.Offset;

                // sanity: the secret pattern is present before return
                Assert.That(backing[offset], Is.EqualTo(sentinel));
            }
            finally
            {
                // Returns the rented buffer to ArrayPool<byte>.Shared with
                // clearArray:true, which zeroes it synchronously on return.
                writer.Dispose();
            }

            for (int i = 0; i < count; i++)
            {
                Assert.That(backing[offset + i], Is.Zero,
                    "clearArray:true must zero the pooled buffer on return.");
            }
        }

        /// <summary>
        /// A size hint of zero asks for any non-empty buffer: the writer hands
        /// out the rest of the current chunk instead of closing it and renting
        /// a new one per call, which stranded a pooled chunk per zero length
        /// write until the writer was disposed.
        /// </summary>
        [Test]
        public void ArrayPoolBufferWriterGetSpanZeroReturnsTheRestOfTheCurrentChunk()
        {
            using var writer = new ArrayPoolBufferWriter<byte>();
            const int writes = 1000;
            for (int i = 0; i < writes; i++)
            {
                Span<byte> span = writer.GetSpan(4);
                span[..4].Fill((byte)i);
                writer.Advance(4);

                Assert.That(writer.GetSpan(0).Length, Is.GreaterThan(0));
                writer.Advance(0);
                Assert.That(writer.GetMemory(0).Length, Is.GreaterThan(0));
                writer.Advance(0);
            }

            ReadOnlySequence<byte> sequence = writer.GetReadOnlySequence();
            int segments = 0;
            foreach (ReadOnlyMemory<byte> _ in sequence)
            {
                segments++;
            }

            Assert.That(sequence.Length, Is.EqualTo(writes * 4));
            Assert.That(segments, Is.LessThanOrEqualTo(8),
                "A zero size hint must not close the current chunk.");
            byte[] bytes = sequence.ToArray();
            for (int i = 0; i < writes; i++)
            {
                Assert.That(bytes[i * 4], Is.EqualTo((byte)i));
            }
        }

        /// <summary>
        /// A size hint of zero on a full chunk rents a new one, so the span
        /// is never empty.
        /// </summary>
        [Test]
        public void ArrayPoolBufferWriterGetSpanZeroOnAFullChunkReturnsANewChunk()
        {
            using var writer = new ArrayPoolBufferWriter<byte>(16, 16);
            Span<byte> first = writer.GetSpan(0);
            Assert.That(first.Length, Is.GreaterThan(0));
            writer.Advance(first.Length);

            Span<byte> second = writer.GetSpan(0);
            Assert.That(second.Length, Is.GreaterThan(0));
            second[0] = 0x42;
            writer.Advance(1);

            ReadOnlySequence<byte> sequence = writer.GetReadOnlySequence();
            Assert.That(sequence.Length, Is.EqualTo(first.Length + 1));
            byte[] bytes = sequence.ToArray();
            Assert.That(bytes[bytes.Length - 1], Is.EqualTo(0x42));
        }
    }
}
