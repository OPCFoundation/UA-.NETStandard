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
using System.Runtime.CompilerServices;

namespace Opc.Ua.Buffers
{
    /// <summary>
    /// Helper to build a <see cref="ReadOnlySequence{T}"/> from a set of buffers.
    /// Implements <see cref="IBufferWriter{T}"/> interface.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class ArrayPoolBufferWriter<T> : IBufferWriter<T>, IDisposable
    {
        private const int kDefaultChunkSize = 256;
        private const int kMaxChunkSize = 65536;
        private readonly bool m_clearArray;
        private int m_chunkSize;
        private readonly int m_maxChunkSize;
        private T[] m_currentBuffer;
        private ArrayPoolBufferSegment<T> m_firstSegment;
        private ArrayPoolBufferSegment<T> m_nextSegment;
        private int m_offset;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArrayPoolBufferWriter{T}"/> class.
        /// </summary>
        public ArrayPoolBufferWriter()
            : this(false, kDefaultChunkSize, kMaxChunkSize)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArrayPoolBufferWriter{T}"/> class.
        /// </summary>
        public ArrayPoolBufferWriter(int defaultChunksize, int maxChunkSize)
            : this(false, defaultChunksize, maxChunkSize)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArrayPoolBufferWriter{T}"/> class.
        /// </summary>
        public ArrayPoolBufferWriter(bool clearArray, int defaultChunksize, int maxChunkSize)
        {
            m_firstSegment = m_nextSegment = null;
            m_offset = 0;
            m_clearArray = clearArray;
            m_chunkSize = defaultChunksize;
            m_maxChunkSize = maxChunkSize;
            m_currentBuffer = [];
            m_disposed = false;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_firstSegment != null)
            {
                ArrayPoolBufferSegment<T> segment = m_firstSegment;
                while (segment != null)
                {
                    segment.Return(m_clearArray);
                    segment = (ArrayPoolBufferSegment<T>)segment.Next;
                }

                m_firstSegment = m_nextSegment = null;
            }
            m_disposed = true;
        }

        /// <inheritdoc/>
        public void Advance(int count)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(ArrayPoolBufferWriter<>));
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    $"{nameof(count)} must be non-negative.");
            }

            if (m_offset + count > m_currentBuffer.Length)
            {
                throw new InvalidOperationException(
                    $"Cannot advance past the end of the buffer, which has a size of {m_currentBuffer.Length}.");
            }

            m_offset += count;
        }

        /// <inheritdoc/>
        public Memory<T> GetMemory(int sizeHint = 0)
        {
            if (sizeHint < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sizeHint),
                    $"{nameof(sizeHint)} must be non-negative.");
            }

            int remainingSpace = CheckAndAllocateBuffer(sizeHint);
            return m_currentBuffer.AsMemory(m_offset, remainingSpace);
        }

        /// <inheritdoc/>
        public Span<T> GetSpan(int sizeHint = 0)
        {
            if (sizeHint < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sizeHint),
                    $"{nameof(sizeHint)} must be non-negative.");
            }

            int remainingSpace = CheckAndAllocateBuffer(sizeHint);
            return m_currentBuffer.AsSpan(m_offset, remainingSpace);
        }

        /// <summary>
        /// Get a ReadOnlySequence that represents the written data.
        /// The sequence is only valid until the next write operation or
        /// until the writer is disposed.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        public ReadOnlySequence<T> GetReadOnlySequence()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(ArrayPoolBufferWriter<>));
            }

            AddSegment();

            if (m_firstSegment == null || m_nextSegment == null)
            {
                return ReadOnlySequence<T>.Empty;
            }

            return new ReadOnlySequence<T>(
                m_firstSegment,
                0,
                m_nextSegment,
                m_nextSegment.Memory.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddSegment()
        {
            if (m_offset > 0)
            {
                if (m_firstSegment == null)
                {
                    m_firstSegment = m_nextSegment = new ArrayPoolBufferSegment<T>(
                        m_currentBuffer,
                        0,
                        m_offset);
                }
                else
                {
                    m_nextSegment = m_nextSegment.Append(m_currentBuffer, 0, m_offset);
                }
            }
            else if (m_currentBuffer.Length > 0)
            {
                ArrayPool<T>.Shared.Return(m_currentBuffer, m_clearArray);
            }

            m_offset = 0;
            m_currentBuffer = [];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int CheckAndAllocateBuffer(int sizeHint)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(ArrayPoolBufferWriter<>));
            }

            int remainingSpace = m_currentBuffer.Length - m_offset;
            if (remainingSpace < sizeHint || sizeHint == 0)
            {
                AddSegment();

                remainingSpace = Math.Max(sizeHint, m_chunkSize);
                m_currentBuffer = ArrayPool<T>.Shared.Rent(remainingSpace);
                m_offset = 0;

                if (m_chunkSize < m_maxChunkSize)
                {
                    m_chunkSize *= 2;
                }
            }

            return remainingSpace;
        }
    }
}
