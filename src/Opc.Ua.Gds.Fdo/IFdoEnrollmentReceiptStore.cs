// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Fdo
{
    /// <summary>
    /// Durable storage for one approved FDO device and DCA identity pair.
    /// Every writer for the same pair must share the same atomic storage scope.
    /// </summary>
    public interface IFdoEnrollmentReceiptStore
    {
        /// <summary>
        /// Reads the latest complete opaque receipt, or returns a null byte string when none exists.
        /// </summary>
        ValueTask<ByteString> ReadAsync(CancellationToken ct = default);

        /// <summary>
        /// Exclusively creates a receipt when none exists and durably publishes it before returning.
        /// Rejects an existing receipt without invoking the factory. A failed or uncertain publication
        /// must be reconciled before another enrollment run.
        /// </summary>
        ValueTask CreateAsync(
            Func<CancellationToken, ValueTask<ByteString>> create,
            CancellationToken ct = default);

        /// <summary>
        /// Atomically replaces the exact expected receipt, returning false if it changed.
        /// A successful replacement must be durable before this call returns.
        /// </summary>
        ValueTask<bool> TryReplaceAsync(ByteString expected, ByteString replacement, CancellationToken ct = default);
    }
}
