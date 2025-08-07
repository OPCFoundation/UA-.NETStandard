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

using NUnit.Framework;
using NUnit.Framework.Internal;
using System;
using System.Buffers;
using System.IO;
using Opc.Ua.Bindings;

namespace Opc.Ua.Buffers.Tests
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
            // Arrange
            var bufferManager = new BufferManager(nameof(ArraySegmentStreamWhenConstructedWithDefaultOptionsShouldNotThrow), 0x10000 - 1);
            var stream = new ArraySegmentStream(bufferManager);

            // Act
            Action act = () => stream.Dispose();
            byte[] buffer = new byte[1] { 0x55 };

            // Assert
            Assert.That(stream.CanRead, Is.True);
            Assert.That(stream.CanWrite, Is.True);
            Assert.That(stream.CanSeek, Is.True);
            Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
            Assert.Throws<IOException>(() => stream.Seek(-1, SeekOrigin.Begin));
            Assert.Throws<IOException>(() => stream.Seek(0, (SeekOrigin)66));

            Assert.That(stream.Seek(0, SeekOrigin.Begin), Is.EqualTo(0));
            Assert.That(stream.ReadByte(), Is.EqualTo(-1));
            Assert.That(stream.Read(buffer, 0, 1), Is.EqualTo(0));
#if NET5_0_OR_GREATER
            Assert.That(stream.Read(buffer.AsSpan(0, 1)), Is.EqualTo(0));
#endif
            stream.Position = 0;
            Assert.That(stream.Position, Is.EqualTo(0));

            Assert.That(stream.Length, Is.EqualTo(0));

            Assert.That(stream.Seek(0, SeekOrigin.Begin), Is.EqualTo(0));
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

            Assert.That(stream.Seek(-3, SeekOrigin.Current), Is.EqualTo(0));
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
            Assert.That(stream.Read(buffer, 0, 1), Is.EqualTo(0));
#if NET5_0_OR_GREATER
            Assert.That(stream.Read(buffer.AsSpan(0, 1)), Is.EqualTo(0));
#endif

            byte[] array = stream.ToArray();
            Assert.That(array.Length, Is.EqualTo(3));
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
            var random = new Random(42);
            int length;
            byte[] buffer = new byte[chunkSize];

            // Arrange
            var bufferManager = new BufferManager(nameof(ArraySegmentStreamWhenConstructedWithDefaultOptionsShouldNotThrow), defaultBufferSize);
            using (var writer = new ArraySegmentStream(bufferManager))
            {
                // Act
                for (int i = 0; i <= byte.MaxValue; i++)
                {
                    // fill chunk with a byte
                    for (int v = 0; v < chunkSize; v++)
                    {
                        buffer[v] = (byte)i;
                    }

                    // write next chunk
                    switch (random.Next(3))
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
                Assert.That(result, Is.EqualTo(0));

                result = writer.Seek(0, SeekOrigin.End);
                Assert.That(result, Is.EqualTo(length));

                // read back from writer MemoryStream
                result = writer.Seek(0, SeekOrigin.Begin);
                Assert.That(result, Is.EqualTo(0));

                Assert.That(writer.Length, Is.EqualTo(length));

                long position;
                for (int i = 0; i <= byte.MaxValue; i++)
                {
                    if (random.Next(2) == 0)
                    {
                        position = writer.Seek(chunkSize * i, SeekOrigin.Begin);
                        Assert.That(position, Is.EqualTo(chunkSize * i));
                    }

                    int bytesRead;
                    switch (random.Next(3))
                    {
                        case 0:
                            for (int v = 0; v < chunkSize; v++)
                            {
                                Assert.That(writer.ReadByte(), Is.EqualTo((byte)i));
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
#endif
                        case 1:
                            bytesRead = writer.Read(buffer, 0, chunkSize);
                            Assert.That(chunkSize, Is.EqualTo(bytesRead));
                            for (int v = 0; v < chunkSize; v++)
                            {
                                Assert.That(buffer[v], Is.EqualTo((byte)i));
                            }
                            break;
                    }
                }

                position = writer.Seek(0, SeekOrigin.Begin);
                Assert.That(position, Is.EqualTo(0));

                using (BufferSequence bufferSequence = writer.GetSequence("Test"))
                {
                    ReadOnlySequence<byte> sequence = bufferSequence.Sequence;
                    buffer = sequence.ToArray();

                    // Assert sequence properties
                    Assert.That(buffer.Length, Is.EqualTo(length));
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
            }
        }
    }
}
