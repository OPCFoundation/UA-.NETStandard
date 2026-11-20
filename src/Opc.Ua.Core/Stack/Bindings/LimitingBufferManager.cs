// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
