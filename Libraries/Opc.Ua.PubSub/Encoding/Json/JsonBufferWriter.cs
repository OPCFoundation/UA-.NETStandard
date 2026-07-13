/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
 *
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

namespace Opc.Ua.PubSub.Encoding.Json
{
    /// <summary>
    /// Pooled <see cref="IBufferWriter{T}"/> implementation that backs
    /// <see cref="System.Text.Json.Utf8JsonWriter"/> across all target
    /// frameworks.
    /// </summary>
    /// <remarks>
    /// .NET 6+ ships a public <c>ArrayBufferWriter&lt;byte&gt;</c>, but
    /// the same type is <c>internal</c> in the <c>System.Memory</c>
    /// back-compat package shipped for netstandard2.0/net472/net48. This
    /// shim therefore provides a uniform pooled implementation so the
    /// JSON PubSub encoder compiles across all PubSub TFMs.
    /// </remarks>
    internal sealed class JsonBufferWriter : IBufferWriter<byte>, IDisposable
    {
        /// <summary>
        /// Creates a new pooled buffer writer with the supplied initial
        /// capacity.
        /// </summary>
        /// <param name="initialCapacity">
        /// Initial buffer capacity in bytes; rounded up to the nearest
        /// power-of-two by <see cref="ArrayPool{T}.Shared"/>.
        /// </param>
        public JsonBufferWriter(int initialCapacity = 256)
        {
            if (initialCapacity <= 0)
            {
                initialCapacity = 256;
            }
            m_buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
            m_written = 0;
        }

        /// <summary>
        /// Bytes written to this buffer so far.
        /// </summary>
        public int WrittenCount => m_written;

        /// <summary>
        /// View over the written portion of the underlying buffer.
        /// </summary>
        public ReadOnlySpan<byte> WrittenSpan => new(m_buffer, 0, m_written);

        /// <summary>
        /// View over the written portion of the underlying buffer.
        /// </summary>
        public ReadOnlyMemory<byte> WrittenMemory => new(m_buffer, 0, m_written);

        /// <summary>
        /// Copies the written bytes into a freshly-allocated array.
        /// </summary>
        /// <returns>The serialised payload.</returns>
        public byte[] GetWritten()
        {
            byte[] result = new byte[m_written];
            Buffer.BlockCopy(m_buffer, 0, result, 0, m_written);
            return result;
        }

        /// <inheritdoc/>
        public void Advance(int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            if (m_written + count > m_buffer.Length)
            {
                throw new InvalidOperationException(
                    "Cannot advance past the end of the rented buffer.");
            }
            m_written += count;
        }

        /// <inheritdoc/>
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return new Memory<byte>(m_buffer, m_written, m_buffer.Length - m_written);
        }

        /// <inheritdoc/>
        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return new Span<byte>(m_buffer, m_written, m_buffer.Length - m_written);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            byte[]? buffer = m_buffer;
            if (buffer.Length > 0)
            {
                m_buffer = [];
                ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
            }
        }

        /// <summary>
        /// Grows the underlying buffer to accommodate at least
        /// <paramref name="sizeHint"/> more bytes.
        /// </summary>
        /// <param name="sizeHint">Required free capacity.</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private void EnsureCapacity(int sizeHint)
        {
            if (sizeHint < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeHint));
            }
            if (sizeHint == 0)
            {
                sizeHint = 1;
            }
            int available = m_buffer.Length - m_written;
            if (available >= sizeHint)
            {
                return;
            }
            int needed = m_written + sizeHint;
            int newSize = Math.Max(m_buffer.Length * 2, needed);
            byte[] rented = ArrayPool<byte>.Shared.Rent(newSize);
            Buffer.BlockCopy(m_buffer, 0, rented, 0, m_written);
            ArrayPool<byte>.Shared.Return(m_buffer, clearArray: false);
            m_buffer = rented;
        }

        private byte[] m_buffer;
        private int m_written;
    }
}
