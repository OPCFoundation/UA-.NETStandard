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
using System.Threading;

namespace Opc.Ua.Buffers
{

    /// <summary>
    /// Helper to build a ReadOnlySequence from a set of <see cref="ArrayPool{T}"/> allocated buffers.
    /// </summary>
    public sealed class ArrayPoolBufferSegment<T> : ReadOnlySequenceSegment<T>
    {
        private T[] _array;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArrayPoolBufferSegment{T}"/> class.
        /// </summary>
        public ArrayPoolBufferSegment(T[] array, int offset, int length)
        {
            Memory = new ReadOnlyMemory<T>(array, offset, length);
            _array = array;
        }

        /// <summary>
        /// Returns a rented buffer to the shared pool and invalidates memory.
        /// </summary>
        public void Return(bool clearArray = false)
        {
            var array = Interlocked.Exchange(ref _array, null);
            if (array != null)
            {
                ArrayPool<T>.Shared.Return(array, clearArray);
                Memory = ReadOnlyMemory<T>.Empty;
            }
        }

        /// <summary>
        /// Appends a buffer to the sequence.
        /// </summary>
        public ArrayPoolBufferSegment<T> Append(T[] array, int offset, int length)
        {
            var segment = new ArrayPoolBufferSegment<T>(array, offset, length) {
                RunningIndex = RunningIndex + Memory.Length,
            };
            Next = segment;
            return segment;
        }
    }
}
