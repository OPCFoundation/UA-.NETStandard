// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Buffers;
using System.Threading;

namespace Opc.Ua.Buffers
{
    /// <summary>
    /// Helper to build a ReadOnlySequence from a set of <see cref="ArrayPool{T}"/> allocated buffers.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class ArrayPoolBufferSegment<T> : ReadOnlySequenceSegment<T>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArrayPoolBufferSegment{T}"/> class.
        /// </summary>
        public ArrayPoolBufferSegment(T[] array, int offset, int length)
        {
            Memory = new ReadOnlyMemory<T>(array, offset, length);
            m_array = array;
        }

        /// <summary>
        /// Returns a rented buffer to the shared pool and invalidates memory.
        /// </summary>
        public void Return(bool clearArray = false)
        {
            T[]? array = Interlocked.Exchange(ref m_array, null!);
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
            var segment = new ArrayPoolBufferSegment<T>(array, offset, length)
            {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = segment;
            return segment;
        }

        private T[] m_array;
    }
}
