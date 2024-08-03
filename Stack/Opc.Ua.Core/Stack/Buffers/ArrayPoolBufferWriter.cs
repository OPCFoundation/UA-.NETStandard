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
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Opc.Ua.Buffers
{
    /// <summary>
    /// Helper to build a <see cref="ReadOnlySequence{T}"/> from a set of buffers.
    /// Implements <see cref="IBufferWriter{T}"/> interface.
    /// </summary>
    public sealed class ArrayPoolBufferWriter<T> : IBufferWriter<T>, IDisposable
    {
        private const int DefaultChunkSize = 256;
        private const int MaxChunkSize = 65536;
        private readonly bool _clearArray;
        private int _chunkSize;
        private readonly int _maxChunkSize;
        private T[] _currentBuffer;
        private ArrayPoolBufferSegment<T> _firstSegment;
        private ArrayPoolBufferSegment<T> _nextSegment;
        private int _offset;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArrayPoolBufferWriter{T}"/> class.
        /// </summary>
        public ArrayPoolBufferWriter()
            : this(false, DefaultChunkSize, MaxChunkSize)
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
            _firstSegment = _nextSegment = null;
            _offset = 0;
            _clearArray = clearArray;
            _chunkSize = defaultChunksize;
            _maxChunkSize = maxChunkSize;
            _currentBuffer = Array.Empty<T>();
            _disposed = false;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_firstSegment != null)
            {
                ArrayPoolBufferSegment<T> segment = _firstSegment;
                while (segment != null)
                {
                    segment.Return(_clearArray);
                    segment = (ArrayPoolBufferSegment<T>)segment.Next;
                }

                _firstSegment = _nextSegment = null;
            }
            _disposed = true;
        }

        /// <inheritdoc/>
        public void Advance(int count)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ArrayPoolBufferWriter<T>));
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), $"{nameof(count)} must be non-negative.");
            }

            if (_offset + count > _currentBuffer.Length)
            {
                throw new InvalidOperationException($"Cannot advance past the end of the buffer, which has a size of {_currentBuffer.Length}.");
            }

            _offset += count;
        }

        /// <inheritdoc/>
        public Memory<T> GetMemory(int sizeHint = 0)
        {
            if (sizeHint < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeHint), $"{nameof(sizeHint)} must be non-negative.");
            }

            int remainingSpace = CheckAndAllocateBuffer(sizeHint);
            return _currentBuffer.AsMemory(_offset, remainingSpace);
        }

        /// <inheritdoc/>
        public Span<T> GetSpan(int sizeHint = 0)
        {
            if (sizeHint < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeHint), $"{nameof(sizeHint)} must be non-negative.");
            }

            int remainingSpace = CheckAndAllocateBuffer(sizeHint);
            return _currentBuffer.AsSpan(_offset, remainingSpace);
        }

        /// <summary>
        /// Get a ReadOnlySequence that represents the written data.
        /// The sequence is only valid until the next write operation or
        /// until the writer is disposed.
        /// </summary>
        public ReadOnlySequence<T> GetReadOnlySequence()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ArrayPoolBufferWriter<T>));
            }

            AddSegment();

            if (_firstSegment == null || _nextSegment == null)
            {
                return ReadOnlySequence<T>.Empty;
            }

            return new ReadOnlySequence<T>(_firstSegment, 0, _nextSegment, _nextSegment.Memory.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddSegment()
        {
            if (_offset > 0)
            {
                if (_firstSegment == null)
                {
                    _firstSegment = _nextSegment = new ArrayPoolBufferSegment<T>(_currentBuffer, 0, _offset);
                }
                else
                {
                    _nextSegment = _nextSegment.Append(_currentBuffer, 0, _offset);
                }
            }
            else if (_currentBuffer.Length > 0)
            {
                ArrayPool<T>.Shared.Return(_currentBuffer, _clearArray);
            }

            _offset = 0;
            _currentBuffer = Array.Empty<T>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int CheckAndAllocateBuffer(int sizeHint)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ArrayPoolBufferWriter<T>));
            }

            int remainingSpace = _currentBuffer.Length - _offset;
            if (remainingSpace < sizeHint || sizeHint == 0)
            {
                AddSegment();

                remainingSpace = Math.Max(sizeHint, _chunkSize);
                _currentBuffer = ArrayPool<T>.Shared.Rent(remainingSpace);
                _offset = 0;

                if (_chunkSize < _maxChunkSize)
                {
                    _chunkSize *= 2;
                }
            }

            return remainingSpace;
        }
    }
}
