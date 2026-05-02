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

namespace Opc.Ua
{
    /// <summary>
    /// A buffer writer over a pooled buffer
    /// </summary>
    public sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
    {
        /// <inheritdoc/>
        public ReadOnlyMemory<byte> WrittenMemory => m_buffer.Memory;

        /// <inheritdoc/>
        public int Capacity => m_buffer.Capacity;

        /// <inheritdoc/>
        public int WrittenCount => m_buffer.Length;

        /// <inheritdoc/>
        public void Advance(int count)
        {
            m_buffer.Commit(count);
        }

        /// <inheritdoc/>
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            m_buffer.EnsureFree(Math.Max(sizeHint, kMinSpace));
            return m_buffer.FreeMemory;
        }

        /// <inheritdoc/>
        public Span<byte> GetSpan(int sizeHint = 0)
        {
            m_buffer.EnsureFree(Math.Max(sizeHint, kMinSpace));
            return m_buffer.FreeSpan;
        }

        /// <inheritdoc/>
        public void Reset()
        {
            m_buffer.Discard(m_buffer.Length);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_buffer.Dispose();
        }

        internal void Clear()
        {
            m_buffer.ClearAndReturnBuffer();
        }

        private const int kMinSpace = 256;
        private PooledBuffer m_buffer = new(initialSize: kMinSpace);
    }
}
