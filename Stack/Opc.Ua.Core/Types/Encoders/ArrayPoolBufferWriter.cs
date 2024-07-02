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

namespace Opc.Ua
{
    /// <summary>
    /// Helper to build a <see cref="ReadOnlySequence{T}"/> from a set of buffers.
    /// Implements <see cref="IBufferWriter{T}"/> interface.
    /// </summary>
    public sealed class ArrayPoolBufferWriter<T> : IBufferWriter<T>, IDisposable
    {
#if DEBUG
        private const bool _clearArray = true;
#else
        private const bool _clearArray = false;
#endif

        private const int DefaultChunkSize = 1024;
        private const int MaxChunkSize = 65536;
        private int _chunkSize;
        private T[] _currentBuffer;
        private ArrayPoolBufferSegment<T> _firstSegment;
        private ArrayPoolBufferSegment<T> _nextSegment;
        private int _offset;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArrayPoolBufferWriter{T}"/> class.
        /// </summary>
        public ArrayPoolBufferWriter(int chunksize = DefaultChunkSize, int maxChunkSize = MaxChunkSize)
        {
            _firstSegment = _nextSegment = null;
            _offset = 0;
            _chunkSize = chunksize;
            _currentBuffer = Array.Empty<T>();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_firstSegment != null)
            {
                ArrayPoolBufferSegment<T> segment = _firstSegment;
                while (segment != null)
                {
                    ArrayPool<T>.Shared.Return(segment.Array(), _clearArray);
                    segment = (ArrayPoolBufferSegment<T>)segment.Next;
                }

                _firstSegment = _nextSegment = null;
                GC.SuppressFinalize(this);
            }
        }

        /// <inheritdoc/>
        public void Advance(int count)
        {
            _offset += count;
            Debug.Assert(_offset <= _currentBuffer.Length, "The offset was advanced beyond the length of the current buffer.");
        }

        /// <inheritdoc/>
        public Memory<T> GetMemory(int sizeHint = 0)
        {
            int remainingSpace = CheckAndAllocateBuffer(sizeHint);
            return _currentBuffer.AsMemory(_offset, remainingSpace);
        }

        /// <inheritdoc/>
        public Span<T> GetSpan(int sizeHint = 0)
        {
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
            AddSegment();

            if (_firstSegment == null || _nextSegment == null)
            {
                return ReadOnlySequence<T>.Empty;
            }

            return new ReadOnlySequence<T>(_firstSegment, 0, _nextSegment, _offset);
        }

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
            else
            {
                if (_currentBuffer.Length > 0)
                {
                    ArrayPool<T>.Shared.Return(_currentBuffer, _clearArray);
                }

                _currentBuffer = Array.Empty<T>();
            }
        }

        private int CheckAndAllocateBuffer(int sizeHint)
        {
            int remainingSpace = _currentBuffer.Length - _offset;
            if (remainingSpace < sizeHint || sizeHint == 0)
            {
                AddSegment();

                remainingSpace = Math.Max(sizeHint, _chunkSize);
                _currentBuffer = ArrayPool<T>.Shared.Rent(remainingSpace);
                _offset = 0;

                if (_chunkSize < MaxChunkSize)
                {
                    _chunkSize *= 2;
                }
            }

            return remainingSpace;
        }
    }
}