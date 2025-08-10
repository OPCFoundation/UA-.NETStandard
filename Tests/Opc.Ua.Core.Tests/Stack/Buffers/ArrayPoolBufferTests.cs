/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Buffers;
using NUnit.Framework;

namespace Opc.Ua.Buffers.Tests
{
    /// <summary>
    /// Tests for <see cref="ArrayPoolBufferWriter{T}"/> where T is <see cref="byte"/>.
    /// </summary>
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
            [Values(0, 1024, 4096, 65536)] int maxChunkSize
        )
        {
            var random = new Random(42);
            int length;
            ReadOnlySequence<byte> sequence;
            byte[] buffer;

            // Arrange
            using var writer = new ArrayPoolBufferWriter<byte>(false, defaultChunkSize, maxChunkSize);
            // Act
            for (int i = 0; i <= byte.MaxValue; i++)
            {
                Span<byte> span;
                int randomGetChunkSize = maxChunkSize > 0 ? chunkSize + random.Next(maxChunkSize) : chunkSize;

                int repeats = random.Next(3);
                do
                {
                    // get a new chunk
                    if (random.Next(2) == 0)
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
                if (random.Next(10) == 0)
                {
                    length = chunkSize * (i + 1);
                    sequence = writer.GetReadOnlySequence();
                    buffer = sequence.ToArray();

                    // Assert
                    Assert.That(buffer.Length, Is.EqualTo(length));
                    Assert.That(sequence.Length, Is.EqualTo(length));
                }
            }

            length = (byte.MaxValue + 1) * chunkSize;
            sequence = writer.GetReadOnlySequence();
            buffer = sequence.ToArray();

            // Assert
            Assert.That(sequence.Length, Is.EqualTo(length));
            Assert.That(buffer.Length, Is.EqualTo(length));

            for (int i = 0; i < buffer.Length; i++)
            {
                Assert.That(buffer[i], Is.EqualTo((byte)(i / chunkSize)));
            }
        }
    }
}
