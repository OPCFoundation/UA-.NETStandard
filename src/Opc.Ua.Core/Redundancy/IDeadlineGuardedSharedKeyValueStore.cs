// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Commits guarded mutations before a deadline measured by the authority's clock.
    /// </summary>
    public interface IDeadlineGuardedSharedKeyValueStore : IGuardedSharedKeyValueStore
    {
        /// <summary>
        /// Indicates whether the underlying authority can enforce a deadline in the same commit.
        /// </summary>
        bool SupportsGuardedDeadlines { get; }

        /// <summary>
        /// Replaces the target only when both values match and the authority has not reached the deadline.
        /// </summary>
        /// <remarks>
        /// Check the deadline at the operation's linearization point, not at request submission or before
        /// entering the transaction. A proposal timestamp, client timeout, or cancellation does not qualify.
        /// </remarks>
        /// <param name="key">The target key.</param>
        /// <param name="expected">The expected target value, or null if absent.</param>
        /// <param name="value">The replacement, or null to delete the target.</param>
        /// <param name="guardKey">The existing key guarding this operation.</param>
        /// <param name="guardExpected">The non-null value expected at the guard key.</param>
        /// <param name="deadline">The exclusive expiry of the expected grant.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Whether both comparisons matched and the operation committed.</returns>
        /// <exception cref="ServiceResultException">
        /// The authority reached the deadline (<see cref="StatusCodes.BadNotWritable"/>).
        /// </exception>
        /// <exception cref="NotSupportedException">The authority does not support guarded deadlines.</exception>
        ValueTask<bool> CompareAndSwapBeforeAsync(
            string key,
            ByteString expected,
            ByteString value,
            string guardKey,
            ByteString guardExpected,
            DateTimeOffset deadline,
            CancellationToken ct = default);
    }
}
