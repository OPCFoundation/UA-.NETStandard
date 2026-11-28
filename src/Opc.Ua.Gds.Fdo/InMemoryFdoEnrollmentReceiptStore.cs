// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Fdo
{
    /// <summary>
    /// Process-local receipt storage for tests and demonstrations only. Receipts are lost on restart.
    /// Share one instance per approved product and application pair.
    /// </summary>
    public sealed class InMemoryFdoEnrollmentReceiptStore : IFdoEnrollmentReceiptStore, IDisposable
    {
        /// <inheritdoc/>
        public async ValueTask<ByteString> ReadAsync(CancellationToken ct = default)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return m_receipt.IsNull ? default : ByteString.From(m_receipt.Span);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask CreateAsync(Func<CancellationToken, ValueTask<ByteString>> create,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(create);
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!m_receipt.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "An enrollment receipt already exists.");
                }
                ByteString receipt = await create(ct).ConfigureAwait(false);
                if (receipt.IsEmpty)
                {
                    throw new ArgumentException("The factory must return a receipt.", nameof(create));
                }
                m_receipt = ByteString.From(receipt.Span);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<bool> TryReplaceAsync(
            ByteString expected, ByteString replacement, CancellationToken ct = default)
        {
            if (expected.IsEmpty || replacement.IsEmpty)
            {
                throw new ArgumentException("Replacement requires a nonempty expected receipt and replacement.");
            }
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (m_receipt != expected)
                {
                    return false;
                }
                m_receipt = ByteString.From(replacement.Span);
                return true;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Releases the store after its callers have stopped.
        /// </summary>
        public void Dispose()
        {
            m_gate.Dispose();
        }

        private readonly SemaphoreSlim m_gate = new(1, 1);
        private ByteString m_receipt;
    }
}
