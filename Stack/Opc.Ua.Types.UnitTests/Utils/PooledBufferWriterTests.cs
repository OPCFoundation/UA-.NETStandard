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
using System.Buffers;
using System.Linq;
using NUnit.Framework;

namespace Opc.Ua
{
    [TestFixture]
    public class PooledBufferWriterTests
    {
        [Test]
        public void ConstructorShouldInitializeWithMinSpace()
        {
            // Arrange & Act
            using var writer = new PooledBufferWriter();

            // Assert
            Assert.That(writer.Capacity, Is.GreaterThanOrEqualTo(256)); // kMinSpace = 256
            Assert.That(writer.WrittenCount, Is.EqualTo(0));
        }

        [Test]
        public void GetMemoryShouldReturnMemoryWithMinimumSize()
        {
            // Arrange
            using var writer = new PooledBufferWriter();

            // Act
            Memory<byte> memory = writer.GetMemory(100);

            // Assert
            Assert.That(memory.Length, Is.GreaterThanOrEqualTo(256)); // kMinSpace = 256
        }

        [Test]
        public void GetMemoryWithLargeSizeHintShouldReturnMemoryWithRequestedSize()
        {
            // Arrange
            using var writer = new PooledBufferWriter();
            const int largeSize = 1024;

            // Act
            Memory<byte> memory = writer.GetMemory(largeSize);

            // Assert
            Assert.That(memory.Length, Is.GreaterThanOrEqualTo(largeSize));
        }

        [Test]
        public void GetSpanShouldReturnSpanWithMinimumSize()
        {
            // Arrange
            using var writer = new PooledBufferWriter();

            // Act
            Span<byte> span = writer.GetSpan(100);

            // Assert
            Assert.That(span.Length, Is.GreaterThanOrEqualTo(256)); // kMinSpace = 256
        }

        [Test]
        public void GetSpanWithLargeSizeHintShouldReturnSpanWithRequestedSize()
        {
            // Arrange
            using var writer = new PooledBufferWriter();
            const int largeSize = 1024;

            // Act
            Span<byte> span = writer.GetSpan(largeSize);

            // Assert
            Assert.That(span.Length, Is.GreaterThanOrEqualTo(largeSize));
        }

        [Test]
        public void AdvanceShouldIncreaseWrittenCount()
        {
            // Arrange
            using var writer = new PooledBufferWriter();
            const int bytesToAdvance = 10;

            // Act
            writer.Advance(bytesToAdvance);

            // Assert
            Assert.That(writer.WrittenCount, Is.EqualTo(bytesToAdvance));
        }

        [Test]
        public void WrittenMemoryShouldReflectAdvancedBytes()
        {
            // Arrange
            using var writer = new PooledBufferWriter();
            Span<byte> span = writer.GetSpan();
            const byte testValue = 42;
            span[0] = testValue;

            // Act
            writer.Advance(1);

            // Assert
            Assert.That(writer.WrittenMemory.Length, Is.EqualTo(1));
            Assert.That(writer.WrittenMemory.Span[0], Is.EqualTo(testValue));
        }

        [Test]
        public void ResetShouldClearWrittenData()
        {
            // Arrange
            using var writer = new PooledBufferWriter();
            Span<byte> span = writer.GetSpan();
            span[0] = 42;
            writer.Advance(10);

            // Act
            writer.Reset();

            // Assert
            Assert.That(writer.WrittenCount, Is.EqualTo(0));
            Assert.That(writer.WrittenMemory.Length, Is.EqualTo(0));
        }

        [Test]
        public void ClearShouldReleaseBufferButKeepInstanceUsable()
        {
            // Arrange
            using var writer = new PooledBufferWriter();
            Span<byte> span = writer.GetSpan();
            span[0] = 42;
            writer.Advance(10);
            int initialCapacity = writer.Capacity;

            // Act
            writer.Clear();

            // Assert
            Assert.That(writer.WrittenCount, Is.EqualTo(0));
            Assert.That(writer.WrittenMemory.Length, Is.EqualTo(0));

            // Verify instance is still usable
            Span<byte> newSpan = writer.GetSpan();
            newSpan[0] = 99;
            writer.Advance(1);
            Assert.That(writer.WrittenCount, Is.EqualTo(1));
        }

        [Test]
        public void GetMemoryAfterMultipleAdvancesShouldReturnCorrectMemory()
        {
            // Arrange
            using var writer = new PooledBufferWriter();

            // Write data in multiple chunks
            Memory<byte> memory1 = writer.GetMemory(10);
            memory1.Span[0] = 1;
            writer.Advance(1);

            Memory<byte> memory2 = writer.GetMemory(10);
            memory2.Span[0] = 2;
            writer.Advance(1);

            // Act
            Memory<byte> finalMemory = writer.GetMemory(10);
            finalMemory.Span[0] = 3;
            writer.Advance(1);

            // Assert
            Assert.That(writer.WrittenCount, Is.EqualTo(3));
            Assert.That(writer.WrittenMemory.Length, Is.EqualTo(3));
            Assert.That(writer.WrittenMemory.Span[0], Is.EqualTo(1));
            Assert.That(writer.WrittenMemory.Span[1], Is.EqualTo(2));
            Assert.That(writer.WrittenMemory.Span[2], Is.EqualTo(3));
        }

        [Test]
        public void GetSpanAfterMultipleAdvancesShouldReturnCorrectSpan()
        {
            // Arrange
            using var writer = new PooledBufferWriter();

            // Write data in multiple chunks
            Span<byte> span1 = writer.GetSpan(10);
            span1[0] = 1;
            writer.Advance(1);

            Span<byte> span2 = writer.GetSpan(10);
            span2[0] = 2;
            writer.Advance(1);

            // Act
            Span<byte> finalSpan = writer.GetSpan(10);
            finalSpan[0] = 3;
            writer.Advance(1);

            // Assert
            Assert.That(writer.WrittenCount, Is.EqualTo(3));
            Assert.That(writer.WrittenMemory.Length, Is.EqualTo(3));
            Assert.That(writer.WrittenMemory.Span[0], Is.EqualTo(1));
            Assert.That(writer.WrittenMemory.Span[1], Is.EqualTo(2));
            Assert.That(writer.WrittenMemory.Span[2], Is.EqualTo(3));
        }

        [Test]
        public void GetMemoryWithZeroSizeHintShouldReturnMemoryWithMinSpace()
        {
            // Arrange
            using var writer = new PooledBufferWriter();

            // Act
            Memory<byte> memory = writer.GetMemory(0);

            // Assert
            Assert.That(memory.Length, Is.GreaterThanOrEqualTo(256)); // kMinSpace = 256
        }

        [Test]
        public void GetSpanWithZeroSizeHintShouldReturnSpanWithMinSpace()
        {
            // Arrange
            using var writer = new PooledBufferWriter();

            // Act
            Span<byte> span = writer.GetSpan(0);

            // Assert
            Assert.That(span.Length, Is.GreaterThanOrEqualTo(256)); // kMinSpace = 256
        }

        [Test]
        public void WritingLargeAmountShouldGrowBuffer()
        {
            // Arrange
            using var writer = new PooledBufferWriter();
            const int largeSize = 1024;

            // Act
            Span<byte> span = writer.GetSpan(largeSize);
            span.Fill(42); // Fill the span with a test value
            writer.Advance(largeSize);

            // Assert
            Assert.That(writer.WrittenCount, Is.EqualTo(largeSize));
            Assert.That(writer.WrittenMemory.Length, Is.EqualTo(largeSize));
            Assert.That(writer.WrittenMemory.Span.ToArray().All(b => b == 42), Is.True);
        }

        [Test]
        public void WritingMultipleChunksShouldCumulativelyStoreData()
        {
            // Arrange
            using var writer = new PooledBufferWriter();
            const int chunkSize = 100;

            // Write first chunk
            Span<byte> span1 = writer.GetSpan(chunkSize);
            span1.Fill(1);
            writer.Advance(chunkSize);

            // Write second chunk
            Span<byte> span2 = writer.GetSpan(chunkSize);
            span2.Fill(2);
            writer.Advance(chunkSize);

            // Write third chunk
            Span<byte> span3 = writer.GetSpan(chunkSize);
            span3.Fill(3);
            writer.Advance(chunkSize);

            // Act & Assert
            Assert.That(writer.WrittenCount, Is.EqualTo(chunkSize * 3));

            // Check first chunk
            for (int i = 0; i < chunkSize; i++)
            {
                Assert.That(writer.WrittenMemory.Span[i], Is.EqualTo(1));
            }

            // Check second chunk
            for (int i = chunkSize; i < chunkSize * 2; i++)
            {
                Assert.That(writer.WrittenMemory.Span[i], Is.EqualTo(2));
            }

            // Check third chunk
            for (int i = chunkSize * 2; i < chunkSize * 3; i++)
            {
                Assert.That(writer.WrittenMemory.Span[i], Is.EqualTo(3));
            }
        }

        [Test]
        public void DisposeShouldMakeInstanceUnusable()
        {
            // Arrange
            var writer = new PooledBufferWriter();
            writer.GetSpan(10);
            writer.Advance(5);

            // Act
            writer.Dispose();

            // Assert
            Action act = () => writer.GetSpan();
            Assert.That(act, Throws.TypeOf<NullReferenceException>());
        }

        [Test]
        public void WritingThenResetThenWritingAgainShouldWork()
        {
            // Arrange
            using var writer = new PooledBufferWriter();

            // Write initial data
            Span<byte> span1 = writer.GetSpan(10);
            span1.Fill(1);
            writer.Advance(10);
            Assert.That(writer.WrittenCount, Is.EqualTo(10));

            // Reset
            writer.Reset();
            Assert.That(writer.WrittenCount, Is.EqualTo(0));
            // Write new data
            Span<byte> span2 = writer.GetSpan(5);
            span2.Fill(2);
            writer.Advance(5);

            // Assert
            Assert.That(writer.WrittenCount, Is.EqualTo(5));
            Assert.That(writer.WrittenMemory.Length, Is.EqualTo(5));
            Assert.That(writer.WrittenMemory.Span.ToArray().All(b => b == 2), Is.True);
        }

        [Test]
        public void UsingAsIBufferWriterShouldWorkCorrectly()
        {
            // Arrange
#pragma warning disable CA1859 // Use concrete types when possible for improved performance
            IBufferWriter<byte> writer = new PooledBufferWriter();
#pragma warning restore CA1859 // Use concrete types when possible for improved performance
            try
            {
                // Act
                Memory<byte> memory = writer.GetMemory(10);
                memory.Span[0] = 42;
                writer.Advance(1);

                // Assert
                var pooledWriter = (PooledBufferWriter)writer;
                Assert.That(pooledWriter.WrittenCount, Is.EqualTo(1));
                Assert.That(pooledWriter.WrittenMemory.Span[0], Is.EqualTo(42));
            }
            finally
            {
                ((IDisposable)writer).Dispose();
            }
        }
    }
}
