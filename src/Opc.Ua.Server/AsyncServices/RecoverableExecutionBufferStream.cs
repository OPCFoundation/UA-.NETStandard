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
using System.IO;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Bounds encoder growth while allowing small terminal records to use only small buffers.
    /// </summary>
    internal sealed class RecoverableExecutionBufferStream : MemoryStream
    {
        /// <summary>
        /// Creates a bounded buffer derived from an existing message or protected-record limit.
        /// </summary>
        public RecoverableExecutionBufferStream(int maximum)
            : base(Math.Min(4096, maximum))
        {
            if (maximum <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum));
            }
            m_maximum = maximum;
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureBoundedCapacity(count);
            base.Write(buffer, offset, count);
        }

        /// <inheritdoc/>
        public override void WriteByte(byte value)
        {
            EnsureBoundedCapacity(1);
            base.WriteByte(value);
        }

#if NET8_0_OR_GREATER
        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureBoundedCapacity(buffer.Length);
            base.Write(buffer);
        }
#endif

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            if (value > m_maximum)
            {
                throw new ServiceResultException(StatusCodes.BadResponseTooLarge);
            }
            if (value > Capacity)
            {
                Capacity = (int)Math.Min(m_maximum, Math.Max(value, 2L * Capacity));
            }
            base.SetLength(value);
        }

        /// <summary>
        /// Caps growth before MemoryStream can double its allocation beyond the admitted limit.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void EnsureBoundedCapacity(int count)
        {
            if (Position > m_maximum - (long)count)
            {
                throw new ServiceResultException(StatusCodes.BadResponseTooLarge);
            }
            long required = Position + count;
            if (required > Capacity)
            {
                Capacity = (int)Math.Min(m_maximum, Math.Max(required, 2L * Capacity));
            }
        }

        private readonly int m_maximum;
    }
}
