/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
