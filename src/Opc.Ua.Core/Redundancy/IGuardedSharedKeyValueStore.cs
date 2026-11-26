// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Supports conditional updates that must observe the current grant and target together.
    /// </summary>
    public interface IGuardedSharedKeyValueStore : ISharedKeyValueStore
    {
        /// <summary>
        /// Swaps a value only if the expected target and a separate guard still match in the same commit.
        /// </summary>
        /// <param name="key">The target key.</param>
        /// <param name="expected">The expected target value, or null if absent.</param>
        /// <param name="value">The replacement, or null to delete the target.</param>
        /// <param name="guardKey">The existing key guarding this operation.</param>
        /// <param name="guardExpected">The non-null value expected at the guard key.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Whether both comparisons matched and the operation committed.</returns>
        ValueTask<bool> CompareAndSwapWhenAsync(
            string key,
            ByteString expected,
            ByteString value,
            string guardKey,
            ByteString guardExpected,
            CancellationToken ct = default);
    }
}
