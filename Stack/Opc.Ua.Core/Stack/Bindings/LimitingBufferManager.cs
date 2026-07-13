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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Wraps another buffer manager and enforces a shared outstanding byte budget.
    /// </summary>
    /// <remarks>
    /// Callers must follow the existing <see cref="IBufferManager"/> ownership
    /// contract and return each rental exactly once before discarding its
    /// reference. Because the compatibility API exposes a raw <c>byte[]</c>
    /// rather than a generation-bearing lease, a stale alias cannot be
    /// distinguished after the same pooled array has been legitimately re-rented.
    /// </remarks>
    public sealed class LimitingBufferManager : IBufferManager
    {
        /// <summary>
        /// Initializes the wrapper.
        /// </summary>
        /// <param name="innerBufferManager">The inner manager to wrap.</param>
        /// <param name="memoryLimiter">The shared process-wide limiter.</param>
        public LimitingBufferManager(
            IBufferManager innerBufferManager,
            BufferManagerMemoryLimiter memoryLimiter)
        {
            m_innerBufferManager = innerBufferManager ??
                throw new ArgumentNullException(nameof(innerBufferManager));
            m_memoryLimiter = memoryLimiter ?? throw new ArgumentNullException(nameof(memoryLimiter));
        }

        /// <inheritdoc/>
        public string Name => m_innerBufferManager.Name;

        /// <inheritdoc/>
        public int MaxSuggestedBufferSize => m_innerBufferManager.MaxSuggestedBufferSize;

        /// <inheritdoc/>
        public int GetSuggestedBufferSize(int size)
        {
            return m_innerBufferManager.GetSuggestedBufferSize(size);
        }

        /// <inheritdoc/>
        public int GetExpectedBufferSize(int size)
        {
            return m_innerBufferManager.GetExpectedBufferSize(size);
        }

        /// <inheritdoc/>
        public byte[] TakeBuffer(int size, string owner)
        {
            return TakeBuffer(size, owner, default);
        }

        /// <inheritdoc/>
        public byte[] TakeBuffer(
            int size,
            string owner,
            System.Threading.CancellationToken ct)
        {
            int expectedBufferSize = m_innerBufferManager.GetExpectedBufferSize(size);
            long reservationId = m_memoryLimiter.Reserve(expectedBufferSize, ct);

            byte[] buffer;

            try
            {
                buffer = m_innerBufferManager.TakeBuffer(size, owner, ct);
            }
            catch
            {
                m_memoryLimiter.Cancel(reservationId);
                throw;
            }

            if (m_memoryLimiter.TryBind(reservationId, buffer))
            {
                return buffer;
            }

            m_innerBufferManager.ReturnBuffer(buffer, owner);
            throw new InvalidOperationException(
                "The inner buffer manager returned a buffer larger than the reserved budget.");
        }

        /// <inheritdoc/>
        public void TransferBuffer(byte[]? buffer, string owner)
        {
            m_innerBufferManager.TransferBuffer(buffer, owner);
        }

        /// <inheritdoc/>
        public void Lock(byte[] buffer)
        {
            m_innerBufferManager.Lock(buffer);
        }

        /// <inheritdoc/>
        public void Unlock(byte[] buffer)
        {
            m_innerBufferManager.Unlock(buffer);
        }

        /// <inheritdoc/>
        public void ReturnBuffer(byte[]? buffer, string owner)
        {
            if (buffer == null)
            {
                return;
            }

            long reservationId = m_memoryLimiter.BeginReturn(buffer);
            m_memoryLimiter.EnterReturn();

            try
            {
                m_innerBufferManager.ReturnBuffer(buffer, owner);
            }
            catch
            {
                m_memoryLimiter.CancelReturn(reservationId);
                throw;
            }
            finally
            {
                m_memoryLimiter.ExitReturn();
            }

            m_memoryLimiter.CompleteReturn(reservationId);
        }

        private readonly IBufferManager m_innerBufferManager;
        private readonly BufferManagerMemoryLimiter m_memoryLimiter;
    }
}
