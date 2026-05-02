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

/* Licensed to the .NET Foundation under one or more agreements.
 * The .NET Foundation licenses this file to you under the MIT license.
 * See the LICENSE file in the project root for more information. */

using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Opc.Ua
{
    /// <summary>
    /// A pooled buffer that can grow and that is returned to the pool
    /// when disposed
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
#pragma warning disable CA1815 // Override equals and operator equals on value types
    public struct PooledBuffer : IDisposable
#pragma warning restore CA1815 // Override equals and operator equals on value types
    {
        /// <summary>
        /// Capacity of the buffer
        /// </summary>
        public readonly int Capacity => m_memory.Length;

        /// <summary>
        /// Start of committed memory in the buffer
        /// </summary>
        public readonly int CommittedStart => m_committedStart;

        /// <summary>
        /// Start of committed memory in the buffer
        /// </summary>
        public readonly int CommittedEnd => m_committedEnd;

        /// <summary>
        /// Length of the buffer
        /// </summary>
        public readonly int Length
            => m_committedEnd - m_committedStart;

        /// <summary>
        /// A span over the buffer
        /// </summary>
        public readonly Span<byte> Span
            => new(m_memory, m_committedStart, m_committedEnd - m_committedStart);

        /// <summary>
        /// A readonly span over the buffer
        /// </summary>
        public readonly ReadOnlySpan<byte> ReadOnlySpan
            => new(m_memory, m_committedStart, m_committedEnd - m_committedStart);

        /// <summary>
        /// Memory over the buffer
        /// </summary>
        public readonly Memory<byte> Memory
            => new(m_memory, m_committedStart, m_committedEnd - m_committedStart);

        /// <summary>
        /// Free in the buffer
        /// </summary>
        public readonly int Free
            => m_memory.Length - m_committedEnd;

        /// <summary>
        /// The free span to write
        /// </summary>
        public readonly Span<byte> FreeSpan
            => m_memory.AsSpan(m_committedEnd);

        /// <summary>
        /// Free memory to write
        /// </summary>
        public readonly Memory<byte> FreeMemory
            => m_memory.AsMemory(m_committedEnd);

        /// <summary>
        /// Sliced to length free memory
        /// </summary>
        /// <param name="length"></param>
        /// <returns></returns>
        public readonly Memory<byte> FreeMemorySliced(int length)
        {
            return new(m_memory, m_committedEnd, length);
        }

        /// <summary>
        /// Create buffer
        /// </summary>
        public PooledBuffer()
        {
            m_memory = [];
            m_committedStart = 0;
            m_committedEnd = 0;
        }

        /// <summary>
        /// Create buffer
        /// </summary>
        /// <param name="initialSize"></param>
        public PooledBuffer(int initialSize)
        {
            m_memory = initialSize != 0 ?
                ArrayPool<byte>.Shared.Rent(initialSize) : [];
            m_committedStart = 0;
            m_committedEnd = 0;
        }

        /// <summary>
        /// Create pooled buffer from sequence
        /// </summary>
        /// <param name="sequence"></param>
        /// <returns></returns>
        public static PooledBuffer Create(ReadOnlySequence<byte> sequence)
        {
            int length = sequence.Length > int.MaxValue ?
                int.MaxValue : (int)sequence.Length; // int.max throws later
            if (length == 0)
            {
                return new PooledBuffer();
            }
            var buffer = new PooledBuffer(length);
            sequence.Slice(0, length).CopyTo(buffer.FreeSpan[..length]);
            buffer.Commit(length);
            return buffer;
        }

        /// <summary>
        /// Create pooled buffer from span
        /// </summary>
        /// <param name="span"></param>
        /// <returns></returns>
        public static PooledBuffer Create(ReadOnlySpan<byte> span)
        {
            if (span.IsEmpty)
            {
                return new PooledBuffer();
            }
            var buffer = new PooledBuffer(span.Length);
            span.CopyTo(buffer.FreeSpan[..span.Length]);
            buffer.Commit(span.Length);
            return buffer;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_committedStart = 0;
            m_committedEnd = 0;

            byte[] array = m_memory;
            m_memory = null!;

            if (array is not null)
            {
                ReturnBufferToPool(array);
            }
        }

        /// <summary>
        /// This is different from Dispose as the instance remains usable
        /// afterwards
        /// </summary>
        public void ClearAndReturnBuffer()
        {
            Debug.Assert(m_memory is not null);

            m_committedStart = 0;
            m_committedEnd = 0;

            byte[] bufferToReturn = m_memory!;
            m_memory = [];
            ReturnBufferToPool(bufferToReturn);
        }

        /// <summary>
        /// Access underlying buffer (no start/end)
        /// </summary>
        /// <returns></returns>
        public readonly byte[] DangerousGetUnderlyingBuffer()
        {
            return m_memory;
        }

        /// <summary>
        /// Drop data that was written
        /// </summary>
        /// <param name="byteCount"></param>
        public void Discard(int byteCount)
        {
            Debug.Assert(byteCount <= Length);
            m_committedStart += byteCount;

            if (m_committedStart == m_committedEnd)
            {
                m_committedStart = 0;
                m_committedEnd = 0;
            }
        }

        /// <summary>
        /// Drop all data and reset current buffer.
        /// </summary>
        public void Reset()
        {
            Discard(Length);
        }

        /// <summary>
        /// Commit number of bytes
        /// </summary>
        /// <param name="byteCount"></param>
        public void Commit(int byteCount)
        {
            Debug.Assert(byteCount <= Free);
            m_committedEnd += byteCount;
        }

        /// <summary>
        /// Ensure at least [byteCount] bytes to write to.
        /// </summary>
        /// <param name="byteCount"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureFree(int byteCount)
        {
            if (byteCount > Free)
            {
                EnsureAvailableSpaceCore(byteCount);
            }
        }

        private void EnsureAvailableSpaceCore(int byteCount)
        {
            Debug.Assert(Free < byteCount);

            if (m_memory.Length == 0)
            {
                Debug.Assert(m_committedStart == 0);
                Debug.Assert(m_committedEnd == 0);
                m_memory = ArrayPool<byte>.Shared.Rent(byteCount);
                return;
            }

            int totalFree = m_committedStart + Free;
            if (byteCount <= totalFree)
            {
                // We can free up enough space by just shifting the bytes down, so do so.
                Buffer.BlockCopy(m_memory, m_committedStart, m_memory, 0, Length);
                m_committedEnd = Length;
                m_committedStart = 0;
                Debug.Assert(byteCount <= Free);
                return;
            }

            int desiredSize = Length + byteCount;
            if ((uint)desiredSize > ArrayMaxLength)
            {
                throw new InvalidOperationException("Out of memory");
            }

            // Double the existing buffer size (capped at Array.MaxLength).
            int newSize = Math.Max(desiredSize,
                (int)Math.Min(ArrayMaxLength, 2 * (uint)m_memory.Length));

            byte[] newBytes = ArrayPool<byte>.Shared.Rent(newSize);
            byte[] oldBytes = m_memory;
            if (Length != 0)
            {
                Buffer.BlockCopy(oldBytes, m_committedStart, newBytes, 0, Length);
            }

            m_committedEnd = Length;
            m_committedStart = 0;

            m_memory = newBytes;
            ReturnBufferToPool(oldBytes);

            Debug.Assert(byteCount <= Free);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReturnBufferToPool(byte[] buffer)
        {
            // The buffer may be Array.Empty<byte>()
            if (buffer.Length > 0)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// https://github.com/dotnet/runtime/discussions/121495
        /// </summary>
#if NET8_0_OR_GREATER
        internal static int ArrayMaxLength => Array.MaxLength;
#else
        internal static int ArrayMaxLength => 0x7FFFFFC7;
#endif
        private byte[] m_memory;
        /// <summary> Start of written memory </summary>
        private int m_committedStart;
        /// <summary> End of written memory </summary>
        private int m_committedEnd;
    }
}
