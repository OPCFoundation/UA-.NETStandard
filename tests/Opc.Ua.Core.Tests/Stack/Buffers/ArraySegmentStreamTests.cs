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
using System.IO;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Buffers.Tests
{
    /// <summary>
    /// Tests for <see cref="ArrayPoolBufferWriter{T}"/> where T is <see cref="byte"/>.
    /// </summary>
    [Parallelizable(ParallelScope.All)]
    public class ArraySegmentStreamTests
    {
        /// <summary>
        /// Test the default behavior of <see cref="ArrayPoolMemoryStream"/>.
        /// </summary>
        [Test]
        public void ArraySegmentStreamWhenConstructedWithDefaultOptionsShouldNotThrow()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var bufferManager = new BufferManager(
                nameof(ArraySegmentStreamWhenConstructedWithDefaultOptionsShouldNotThrow),
                0x10000 - 1,
                telemetry);
            var stream = new ArraySegmentStream(bufferManager);

            // Act
            Action act = stream.Dispose;
            byte[] buffer = [0x55];

            // Assert
            Assert.That(stream.CanRead, Is.True);
            Assert.That(stream.CanWrite, Is.True);
            Assert.That(stream.CanSeek, Is.True);
            Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
            Assert.Throws<IOException>(() => stream.Seek(-1, SeekOrigin.Begin));
            Assert.Throws<IOException>(() => stream.Seek(0, (SeekOrigin)66));

            Assert.That(stream.Seek(0, SeekOrigin.Begin), Is.Zero);
            Assert.That(stream.ReadByte(), Is.EqualTo(-1));
            Assert.That(stream.Read(buffer, 0, 1), Is.Zero);
#if NET5_0_OR_GREATER
            Assert.That(stream.Read(buffer.AsSpan(0, 1)), Is.Zero);
#endif
            stream.Position = 0;
            Assert.That(stream.Position, Is.Zero);

            Assert.That(stream.Length, Is.Zero);

            Assert.That(stream.Seek(0, SeekOrigin.Begin), Is.Zero);
            stream.WriteByte(0xaa);
            stream.Write(buffer, 0, 1);
#if NET5_0_OR_GREATER
            stream.Write(buffer.AsSpan(0, 1));
#else
            stream.Write(buffer, 0, 1);
#endif
            stream.Flush();
            Assert.That(stream.Length, Is.EqualTo(3));
            Assert.That(stream.Position, Is.EqualTo(3));
            Assert.That(stream.Length, Is.EqualTo(3));

            Assert.That(stream.Seek(-3, SeekOrigin.Current), Is.Zero);
            Assert.That(stream.ReadByte(), Is.EqualTo(0xaa));
            Assert.That(stream.Length, Is.EqualTo(3));
            Assert.That(stream.Position, Is.EqualTo(1));
            Assert.That(stream.Read(buffer, 0, 1), Is.EqualTo(1));
            Assert.That(stream.Position, Is.EqualTo(2));
            Assert.That(stream.Length, Is.EqualTo(3));
            Assert.That(buffer[0], Is.EqualTo(0x55));
#if NET5_0_OR_GREATER
            Assert.That(stream.Read(buffer.AsSpan(0, 1)), Is.EqualTo(1));
#else
            Assert.That(stream.Read(buffer, 0, 1), Is.EqualTo(1));
#endif
            Assert.That(stream.Position, Is.EqualTo(3));
            Assert.That(stream.Length, Is.EqualTo(3));
            Assert.That(buffer[0], Is.EqualTo(0x55));
            Assert.That(stream.ReadByte(), Is.EqualTo(-1));
            Assert.That(stream.Read(buffer, 0, 1), Is.Zero);
#if NET5_0_OR_GREATER
            Assert.That(stream.Read(buffer.AsSpan(0, 1)), Is.Zero);
#endif

            byte[] array = stream.ToArray();
            Assert.That(array, Has.Length.EqualTo(3));
            Assert.That(array[0], Is.EqualTo(0xaa));
            Assert.That(array[1], Is.EqualTo(0x55));
            Assert.That(array[2], Is.EqualTo(0x55));

            // now buffer sequence owns the buffers
            using (BufferSequence bufferSequence = stream.GetSequence("Test"))
            {
                ReadOnlySequence<byte> sequence = bufferSequence.Sequence;
                Assert.That(sequence.Length, Is.EqualTo(3));
                Assert.That(sequence.Slice(0, 1).First.Span[0], Is.EqualTo(0xaa));
                Assert.That(sequence.Slice(1, 1).First.Span[0], Is.EqualTo(0x55));
                Assert.That(sequence.Slice(2, 1).First.Span[0], Is.EqualTo(0x55));
            }

            act();

            Assert.Throws<ObjectDisposedException>(() => stream.ToArray());
        }

        /// <summary>
        /// Test the default behavior of <see cref="ArraySegmentStream"/>.
        /// </summary>
        [Theory]
        public void ArraySegmentStreamWrite(
            [Values(0, 1, 16, 17, 128, 333, 777, 1024, 4096)] int chunkSize,
            [Values(16, 128, 333, 1024, 4096, 65536)] int defaultBufferSize)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            int length;
            byte[] buffer = new byte[chunkSize];

            // Arrange
            var bufferManager = new BufferManager(
                nameof(ArraySegmentStreamWhenConstructedWithDefaultOptionsShouldNotThrow),
                defaultBufferSize,
                telemetry);
            using var writer = new ArraySegmentStream(bufferManager);
            // Act
            for (int i = 0; i <= byte.MaxValue; i++)
            {
                // fill chunk with a byte
                for (int v = 0; v < chunkSize; v++)
                {
                    buffer[v] = (byte)i;
                }

                // write next chunk
                switch (UnsecureRandom.Shared.Next(3))
                {
                    case 0:
                        for (int v = 0; v < chunkSize; v++)
                        {
                            writer.WriteByte((byte)i);
                        }

                        break;
                    case 1:
                        writer.Write(buffer, 0, chunkSize);
                        break;
#if NET5_0_OR_GREATER
                    default:
                        writer.Write(buffer.AsSpan(0, chunkSize));
                        break;
#else
                    default:
                        writer.Write(buffer, 0, chunkSize);
                        break;
#endif
                }
            }

            length = (byte.MaxValue + 1) * chunkSize;
            long result = writer.Seek(0, SeekOrigin.Begin);
            Assert.That(result, Is.Zero);

            result = writer.Seek(0, SeekOrigin.End);
            Assert.That(result, Is.EqualTo(length));

            // read back from writer MemoryStream
            result = writer.Seek(0, SeekOrigin.Begin);
            Assert.That(result, Is.Zero);

            Assert.That(writer.Length, Is.EqualTo(length));

            long position;
            for (int i = 0; i <= byte.MaxValue; i++)
            {
                if (UnsecureRandom.Shared.Next(2) == 0)
                {
                    position = writer.Seek(chunkSize * i, SeekOrigin.Begin);
                    Assert.That(position, Is.EqualTo(chunkSize * i));
                }

                int bytesRead;
                switch (UnsecureRandom.Shared.Next(3))
                {
                    case 0:
                        for (int v = 0; v < chunkSize; v++)
                        {
                            Assert.That(writer.ReadByte(), Is.EqualTo((byte)i));
                        }
                        break;
                    case 1:
                        bytesRead = writer.Read(buffer, 0, chunkSize);
                        Assert.That(chunkSize, Is.EqualTo(bytesRead));
                        for (int v = 0; v < chunkSize; v++)
                        {
                            Assert.That(buffer[v], Is.EqualTo((byte)i));
                        }
                        break;
                    default:
#if NET5_0_OR_GREATER
                        bytesRead = writer.Read(buffer.AsSpan(0, chunkSize));
                        Assert.That(chunkSize, Is.EqualTo(bytesRead));
                        for (int v = 0; v < chunkSize; v++)
                        {
                            Assert.That(buffer[v], Is.EqualTo((byte)i));
                        }
                        break;
#else
                        goto case 1;
#endif
                }
            }

            position = writer.Seek(0, SeekOrigin.Begin);
            Assert.That(position, Is.Zero);

            using BufferSequence bufferSequence = writer.GetSequence("Test");
            ReadOnlySequence<byte> sequence = bufferSequence.Sequence;
            buffer = sequence.ToArray();

            // Assert sequence properties
            Assert.That(buffer, Has.Length.EqualTo(length));
            Assert.That(sequence.Length, Is.EqualTo(length));

            for (int i = 0; i < buffer.Length; i++)
            {
                Assert.That(buffer[i], Is.EqualTo((byte)(i / chunkSize)));
            }

            for (int i = 0; i <= byte.MaxValue; i++)
            {
                ReadOnlySequence<byte> chunkSequence = sequence.Slice(i * chunkSize, chunkSize);
                Assert.That(chunkSequence.Length, Is.EqualTo((long)chunkSize));

                buffer = chunkSequence.ToArray();
                for (int v = 0; v < chunkSize; v++)
                {
                    Assert.That(buffer[v], Is.EqualTo((byte)i));
                }
            }
        }

        /// <summary>
        /// A writable stream hands its buffers over with the written counts, and writes after
        /// that go to new buffers the earlier collection does not see.
        /// </summary>
        [Test]
        public void GetBuffersHandsOverTheWrittenBuffersAndTheStreamContinues()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var bufferManager = new BufferManager(nameof(GetBuffersHandsOverTheWrittenBuffersAndTheStreamContinues), 64, telemetry);
            using var writer = new ArraySegmentStream(bufferManager, 64, 8, 32);
            byte[] first = new byte[40];
            for (int ii = 0; ii < first.Length; ii++)
            {
                first[ii] = (byte)ii;
            }
            writer.Write(first, 0, first.Length);

            BufferCollection buffers = writer.GetBuffers("test");

            Assert.That(buffers, Has.Count.EqualTo(2));
            Assert.That(buffers[0].Offset, Is.EqualTo(8));
            Assert.That(buffers[0], Has.Count.EqualTo(32));
            Assert.That(buffers[1], Has.Count.EqualTo(8));
            Assert.That(buffers[0].AsSpan().ToArray(), Is.EqualTo(first.AsSpan(0, 32).ToArray()));
            Assert.That(buffers[1].AsSpan().ToArray(), Is.EqualTo(first.AsSpan(32, 8).ToArray()));
            Assert.That(writer.Length, Is.Zero);

            writer.WriteByte(0xAB);
            BufferCollection next = writer.GetBuffers("test");

            Assert.That(next, Is.Not.SameAs(buffers));
            Assert.That(next, Has.Count.EqualTo(1));
            Assert.That(next[0], Has.Count.EqualTo(1));
            Assert.That(next[0].Array![next[0].Offset], Is.EqualTo(0xAB));
            Assert.That(buffers, Has.Count.EqualTo(2));
            Assert.That(next[0].Array, Is.Not.SameAs(buffers[0].Array));
            Assert.That(next[0].Array, Is.Not.SameAs(buffers[1].Array));

            buffers.Release(bufferManager, "test");
            next.Release(bufferManager, "test");
        }

        /// <summary>
        /// An empty writable stream reset with other buffer sizes writes like a new stream with
        /// them; a stream that still holds buffers returns them before it can be reset.
        /// </summary>
        [Test]
        public void ResetStreamWritesWithTheNewBufferSizes()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var small = new BufferManager(nameof(ResetStreamWritesWithTheNewBufferSizes) + "1", 64, telemetry);
            var large = new BufferManager(nameof(ResetStreamWritesWithTheNewBufferSizes) + "2", 256, telemetry);
            using var stream = new ArraySegmentStream(small, 64, 8, 32);
            stream.Write(new byte[40], 0, 40);

            Assert.That(() => stream.Reset(large, 256, 16, 128), Throws.InvalidOperationException);
            stream.ReleaseBuffers();
            Assert.That(stream.Length, Is.Zero);

            stream.Reset(large, 256, 16, 128);
            stream.Write(new byte[100], 0, 100);
            BufferCollection buffers = stream.GetBuffers("test");

            Assert.That(buffers, Has.Count.EqualTo(1));
            Assert.That(buffers[0].Offset, Is.EqualTo(16));
            Assert.That(buffers[0], Has.Count.EqualTo(100));
            Assert.That(buffers[0].Array, Has.Length.GreaterThanOrEqualTo(256));
            buffers.Release(large, "test");

            using var reader = new ArraySegmentStream(new BufferCollection());
            Assert.That(() => reader.Reset(large, 256, 16, 128), Throws.InvalidOperationException);
        }

        /// <summary>
        /// A stream over a caller's collection returns a copy of it and clears it, as before.
        /// </summary>
        [Test]
        public void GetBuffersOfAReadStreamReturnsACopy()
        {
            using (var empty = new ArraySegmentStream(new BufferCollection()))
            {
                // a read stream owns no buffers to return.
                Assert.That(empty.ReleaseBuffers, Throws.Nothing);
            }

            var source = new BufferCollection { new ArraySegment<byte>(new byte[] { 1, 2, 3 }, 0, 3) };
            using var reader = new ArraySegmentStream(source);

            BufferCollection buffers = reader.GetBuffers("test");

            Assert.That(buffers, Is.Not.SameAs(source));
            Assert.That(buffers, Has.Count.EqualTo(1));
            Assert.That(buffers[0], Has.Count.EqualTo(3));
            Assert.That(source, Is.Empty);
        }
    }
}
