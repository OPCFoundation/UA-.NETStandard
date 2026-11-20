// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
        private ArrayPoolBufferSegment<T>? m_firstSegment;
        private ArrayPoolBufferSegment<T>? m_nextSegment;
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
        /// Initializes a new instance of the <see cref="ArrayPoolBufferWriter{T}"/>
        /// class that optionally clears (zeroes) rented buffers when they are
        /// returned to the pool. Use this when the buffer may hold sensitive
        /// data (e.g. encoded secrets or credentials) so leftover bytes are not
        /// exposed to the next consumer of the shared pool.
        /// </summary>
        public ArrayPoolBufferWriter(bool clearArray)
            : this(clearArray, kDefaultChunkSize, kMaxChunkSize)
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
                ArrayPoolBufferSegment<T>? segment = m_firstSegment;
                while (segment != null)
                {
                    segment.Return(m_clearArray);
                    segment = (ArrayPoolBufferSegment<T>?)segment.Next;
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
                    m_nextSegment = m_nextSegment!.Append(m_currentBuffer, 0, m_offset);
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

            // Per the IBufferWriter contract a sizeHint of 0 asks for any
            // non-empty buffer: hand out the rest of the current chunk and
            // rent a new chunk only when it is full. Renting on every
            // GetSpan(0) would close a segment per call and strand a pooled
            // array (up to the max chunk size) per zero length write.
            int remainingSpace = m_currentBuffer.Length - m_offset;
            if (remainingSpace < sizeHint || remainingSpace == 0)
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
