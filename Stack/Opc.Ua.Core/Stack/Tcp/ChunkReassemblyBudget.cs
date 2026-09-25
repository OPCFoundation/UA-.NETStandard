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

using System;
using System.Threading;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Bounds backing arrays retained for incomplete requests across a server's channels.
    /// </summary>
    internal sealed class ChunkReassemblyBudget
    {
        internal ChunkReassemblyBudget(long maxBytes)
        {
            if (maxBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBytes));
            }
            MaxBytes = maxBytes;
        }

        internal long MaxBytes { get; }

        internal long ReservedBytes => Interlocked.Read(ref m_reservedBytes);

        internal static ChunkReassemblyBudget CreateDefault(int maxMessageSize)
        {
            const long min = 64L * 1024 * 1024;
            const long max = 1024L * 1024 * 1024;
            long capacity = maxMessageSize <= 0 ? max :
                Math.Max(Math.Min(Math.Max(16L * maxMessageSize, min), max), 4L * maxMessageSize);
            return new ChunkReassemblyBudget(capacity);
        }

        internal bool TryReserve(long bytes)
        {
            if (bytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bytes));
            }
            long reserved = ReservedBytes;
            while (true)
            {
                if (bytes > MaxBytes - reserved)
                {
                    return false;
                }
                long observed = Interlocked.CompareExchange(ref m_reservedBytes, reserved + bytes, reserved);
                if (observed == reserved)
                {
                    return true;
                }
                reserved = observed;
            }
        }

        internal void Release(long bytes)
        {
            if (bytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bytes));
            }
            long reserved = ReservedBytes;
            while (true)
            {
                if (bytes > reserved)
                {
                    throw new InvalidOperationException("More reassembly bytes released than reserved.");
                }
                long observed = Interlocked.CompareExchange(ref m_reservedBytes, reserved - bytes, reserved);
                if (observed == reserved)
                {
                    return;
                }
                reserved = observed;
            }
        }

        private long m_reservedBytes;
    }
}
