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
using NUnit.Framework;

namespace Opc.Ua.Types.Buffers.Tests
{
    [TestFixture]
    [Category("Buffers")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable(ParallelScope.All)]
    public class PooledBufferTests
    {
        [Test]
        public void ConstructorWithZeroSizeShouldInitializeWithEmptyArray()
        {
            // Arrange & Act
            using var buffer = new PooledBuffer(0);

            // Assert
            Assert.That(buffer.Capacity, Is.EqualTo(0));
            Assert.That(buffer.Length, Is.EqualTo(0));
            Assert.That(buffer.Free, Is.EqualTo(0));
            Assert.That(buffer.CommittedStart, Is.EqualTo(0));
            Assert.That(buffer.CommittedEnd, Is.EqualTo(0));
        }

        [TestCase(1)]
        [TestCase(16)]
        [TestCase(1024)]
        public void ConstructorWithPositiveSizeShouldInitializeWithCorrectCapacity(int initialSize)
        {
            // Arrange & Act
            using var buffer = new PooledBuffer(initialSize);

            // Assert
            Assert.That(buffer.Capacity, Is.GreaterThanOrEqualTo(initialSize));
            Assert.That(buffer.Length, Is.EqualTo(0));
            Assert.That(buffer.Free, Is.GreaterThanOrEqualTo(initialSize));
            Assert.That(buffer.CommittedStart, Is.EqualTo(0));
            Assert.That(buffer.CommittedEnd, Is.EqualTo(0));
        }

        [Test]
        public void ClearAndReturnBufferShouldResetPositionsAndReplaceBuffer()
        {
            // Arrange
            var buffer = new PooledBuffer(16);
            buffer.EnsureFree(8);
            buffer.FreeSpan[0] = 42;
            buffer.Commit(1);
            Assert.That(buffer.Length, Is.EqualTo(1));

            // Act
            buffer.ClearAndReturnBuffer();

            // Assert
            Assert.That(buffer.Length, Is.EqualTo(0));
            Assert.That(buffer.CommittedStart, Is.EqualTo(0));
            Assert.That(buffer.CommittedEnd, Is.EqualTo(0));
            Assert.That(buffer.Capacity, Is.EqualTo(0));
        }

        [Test]
        public void DiscardPartialDiscardShouldUpdateCommittedStart()
        {
            // Arrange
            var buffer = new PooledBuffer(16);
            buffer.EnsureFree(8);

            // Write 5 bytes
            for (int i = 0; i < 5; i++)
            {
                buffer.FreeSpan[i] = (byte)i;
            }
            buffer.Commit(5);

            // Act
            buffer.Discard(2); // Discard first 2 bytes

            // Assert
            Assert.That(buffer.Length, Is.EqualTo(3));
            Assert.That(buffer.CommittedStart, Is.EqualTo(2));
            Assert.That(buffer.CommittedEnd, Is.EqualTo(5));
            Assert.That(buffer.ReadOnlySpan[0], Is.EqualTo(2)); // The first value should now be '2'
        }

        [Test]
        public void DiscardCompleteDiscardShouldResetPositions()
        {
            // Arrange
            var buffer = new PooledBuffer(16);
            buffer.EnsureFree(8);
            buffer.FreeSpan[0] = 42;
            buffer.Commit(1);

            // Act
            buffer.Discard(1); // Discard the only byte we have

            // Assert
            Assert.That(buffer.Length, Is.EqualTo(0));
            Assert.That(buffer.CommittedStart, Is.EqualTo(0));
            Assert.That(buffer.CommittedEnd, Is.EqualTo(0));
        }

        [Test]
        public void ResetShouldDiscardAllData()
        {
            // Arrange
            var buffer = new PooledBuffer(16);
            buffer.EnsureFree(8);

            // Write some data
            for (int i = 0; i < 5; i++)
            {
                buffer.FreeSpan[i] = (byte)i;
            }
            buffer.Commit(5);

            // Act
            buffer.Reset();

            // Assert
            Assert.That(buffer.Length, Is.EqualTo(0));
            Assert.That(buffer.CommittedStart, Is.EqualTo(0));
            Assert.That(buffer.CommittedEnd, Is.EqualTo(0));
        }

        [Test]
        public void CommitShouldUpdateCommittedEnd()
        {
            // Arrange
            var buffer = new PooledBuffer(16);

            // Act
            buffer.Commit(5);

            // Assert
            Assert.That(buffer.Length, Is.EqualTo(5));
            Assert.That(buffer.CommittedEnd, Is.EqualTo(5));
        }

        [Test]
        public void GrowShouldIncreaseCapacity()
        {
            // Arrange
            var buffer = new PooledBuffer(1);
            buffer.Commit(1); // Fill the buffer
            int initialCapacity = buffer.Capacity;

            // Act
            buffer.Grow();

            // Assert
            Assert.That(buffer.Capacity, Is.GreaterThan(initialCapacity));
            Assert.That(buffer.Free, Is.GreaterThan(0));
        }

        [Test]
        public void EnsureFreeWhenEnoughSpaceShouldNotChangeCapacity()
        {
            // Arrange
            var buffer = new PooledBuffer(16);
            int initialCapacity = buffer.Capacity;

            // Act
            buffer.EnsureFree(8); // We already have 16 bytes

            // Assert
            Assert.That(buffer.Capacity, Is.EqualTo(initialCapacity));
        }

        [Test]
        public void EnsureFreeWhenNotEnoughSpaceShouldIncreaseCapacity()
        {
            // Arrange
            var buffer = new PooledBuffer(8);
            buffer.Commit(5); // Use 5 bytes
            int initialCapacity = buffer.Capacity;

            // Act
            buffer.EnsureFree(200); // Need 200 more bytes

            // Assert
            Assert.That(buffer.Capacity, Is.GreaterThan(initialCapacity));
            Assert.That(buffer.Free, Is.GreaterThanOrEqualTo(200));
        }

        [Test]
        public void EnsureAvailableSpaceCoreWithCompactionShouldShiftData()
        {
            // Arrange
            var buffer = new PooledBuffer(16);

            // Fill the buffer with data
            for (int i = 0; i < 8; i++)
            {
                buffer.FreeSpan[i] = (byte)(i + 1);
            }
            buffer.Commit(8);

            // Discard some from the beginning
            buffer.Discard(6);

            int initialCapacity = buffer.Capacity;
            int initialLength = buffer.Length;
            byte[] remainingData = buffer.ReadOnlySpan.ToArray(); // Should be [5, 6, 7, 8]

            Assert.That(buffer.Free, Is.EqualTo(8));
            Assert.That(buffer.CommittedStart, Is.EqualTo(6));

            // Act - this should trigger compaction not expansion
            // We must want > Free, but less than Capacity
            buffer.EnsureFree(10);

            // Assert
            Assert.That(buffer.Capacity, Is.EqualTo(initialCapacity)); // Capacity shouldn't change
            Assert.That(buffer.Length, Is.EqualTo(initialLength)); // Length should remain the same
            Assert.That(buffer.CommittedStart, Is.EqualTo(0));

            // Verify the data was shifted correctly
            byte[] newData = buffer.ReadOnlySpan.ToArray();
            Assert.That(newData, Is.EquivalentTo(remainingData));
        }

        [Test]
        public void EnsureAvailableSpaceCoreWhenNeedingExpansionShouldCopyData()
        {
            // Arrange
            var buffer = new PooledBuffer(8);

            // Fill the buffer with data
            for (int i = 0; i < 5; i++)
            {
                buffer.FreeSpan[i] = (byte)(i + 1);
            }
            buffer.Commit(5);

            byte[] initialData = buffer.ReadOnlySpan.ToArray();

            // Act
            buffer.EnsureFree(8); // Need more space than available

            // Assert
            Assert.That(buffer.Capacity, Is.GreaterThan(8));
            Assert.That(buffer.Length, Is.EqualTo(5));

            // Verify data was preserved
            byte[] newData = buffer.ReadOnlySpan.ToArray();
            Assert.That(newData, Is.EquivalentTo(initialData));
        }

        [Test]
        public void MemoryShouldReturnCorrectSlice()
        {
            // Arrange
            var buffer = new PooledBuffer(16);

            // Fill with data
            for (int i = 0; i < 8; i++)
            {
                buffer.FreeSpan[i] = (byte)(i + 1);
            }
            buffer.Commit(8);
            buffer.Discard(3); // Skip first 3 bytes

            // Act
            Memory<byte> memory = buffer.Memory;

            // Assert
            Assert.That(memory.Length, Is.EqualTo(5));
            Assert.That(memory.Span[0], Is.EqualTo(4)); // First value should be 4 (we discarded 1,2,3)
            Assert.That(memory.Span[4], Is.EqualTo(8)); // Last value should be 8
        }

        [Test]
        public void SpanShouldReturnCorrectSlice()
        {
            // Arrange
            var buffer = new PooledBuffer(16);

            // Fill with data
            for (int i = 0; i < 8; i++)
            {
                buffer.FreeSpan[i] = (byte)(i + 1);
            }
            buffer.Commit(8);
            buffer.Discard(3); // Skip first 3 bytes

            // Act
            Span<byte> span = buffer.Span;

            // Assert
            Assert.That(span.Length, Is.EqualTo(5));
            Assert.That(span[0], Is.EqualTo(4)); // First value should be 4 (we discarded 1,2,3)
            Assert.That(span[4], Is.EqualTo(8)); // Last value should be 8
        }

        [Test]
        public void FreeMemorySlicedShouldReturnCorrectSlice()
        {
            // Arrange
            var buffer = new PooledBuffer(16);
            buffer.Commit(4); // Use first 4 bytes

            // Act
            Memory<byte> freeMemory = buffer.FreeMemorySliced(4);

            // Assert
            Assert.That(freeMemory.Length, Is.EqualTo(4));
        }

        [Test]
        public void DangerousGetUnderlyingBufferReturnsSameOnInit()
        {
            // Arrange
            var buffer = new PooledBuffer(16);
            buffer.Commit(buffer.Capacity);

            // Act
            byte[] memory = buffer.DangerousGetUnderlyingBuffer();

            // Assert
            Assert.That(memory.SequenceEqual(buffer.ReadOnlySpan), Is.True);
        }

        [Test]
        public void GetTooLargeBufferThrowsInvalidOperationExceptionWithOutOfMemoryMessage()
        {
            var buffer = new PooledBuffer(16);

            void Act() => buffer.EnsureFree(PooledBuffer.ArrayMaxLength + 1);

            Assert.That((Action)Act, Throws.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo("Out of memory"));
        }

        [Test]
        public void CreateShouldReturnEmptyBufferForEmptySequence()
        {
            // Arrange
            ReadOnlySequence<byte> emptySequence = ReadOnlySequence<byte>.Empty;

            // Act
            var buffer = PooledBuffer.Create(emptySequence);

            // Assert
            Assert.That(buffer.Length, Is.EqualTo(0));
            Assert.That(buffer.Capacity, Is.EqualTo(0));
            Assert.That(buffer.Span.IsEmpty, Is.True);
            Assert.That(buffer.Free, Is.EqualTo(0));
        }

        [Test]
        public void CreateShouldReturnEmptyBufferForEmptySpan()
        {
            // Arrange
            ReadOnlySpan<byte> emptySpan = [];

            // Act
            var buffer = PooledBuffer.Create(emptySpan);

            // Assert
            Assert.That(buffer.Length, Is.EqualTo(0));
            Assert.That(buffer.Capacity, Is.EqualTo(0));
            Assert.That(buffer.Span.IsEmpty, Is.True);
            Assert.That(buffer.Free, Is.EqualTo(0));
        }

        [Test]
        public void CreateShouldCopySequenceToBuffer()
        {
            // Arrange
            byte[] data = [1, 2, 3, 4, 5];
            var sequence = new ReadOnlySequence<byte>(data);

            // Act
            var buffer = PooledBuffer.Create(sequence);

            // Assert
            Assert.That(buffer.Length, Is.EqualTo(data.Length));
            Assert.That(buffer.Capacity, Is.GreaterThanOrEqualTo(data.Length));
            Assert.That(buffer.Span.ToArray(), Is.EqualTo(data));
        }

        [Test]
        public void CreateShouldCopySpanToBuffer()
        {
            // Arrange
            byte[] data = [1, 2, 3, 4, 5];
            Span<byte> span = data.AsSpan();

            // Act
            var buffer = PooledBuffer.Create(span);

            // Assert
            Assert.That(buffer.Length, Is.EqualTo(data.Length));
            Assert.That(buffer.Capacity, Is.GreaterThanOrEqualTo(data.Length));
            Assert.That(buffer.Span.ToArray(), Is.EqualTo(data));
        }
#if TODO
        [Test]
        public void CreateShouldHandleMultiSegmentSequence()
        {
            // Arrange
            ReadOnlyMemory<byte> expected = new byte[] { 1, 2, 3, 4, 5, 6 };
            var sequence = expected.ToReadOnlySequence(3);

            // Act
            PooledBuffer buffer = PooledBuffer.Create(sequence);

            // Assert
            Assert.That(buffer.Length, Is.EqualTo(expected.Length));
            Assert.That(buffer.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }));
        }

        [Test]
        public void CreateShouldThrowForSequenceExceedingIntMaxValue()
        {
            // Arrange
            var largeSequence = SequenceHelper.FakeSize<byte>(int.MaxValue / 2, 2);

            // Act
            Action act = () => PooledBuffer.Create(largeSequence);

            // Assert
            Assert.That(act, Throws.TypeOf<OutOfMemoryException>());
        }
#endif

        [Test]
        public void CreateShouldReuseBufferFromPool()
        {
            // Arrange
            byte[] data = [1, 2, 3, 4, 5];
            var sequence = new ReadOnlySequence<byte>(data);

            // Act
            var buffer = PooledBuffer.Create(sequence);

            // Assert
            Assert.That(buffer.Capacity, Is.GreaterThanOrEqualTo(data.Length));
            Assert.That(buffer.Free, Is.EqualTo(buffer.Capacity - data.Length));
        }
    }
}
