// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// An exact committed grant with the exclusive deadline encoded in that grant.
    /// </summary>
    /// <param name="Value">The committed grant bytes, or null when the acquisition is no longer current.</param>
    /// <param name="Deadline">The grant's exclusive expiry.</param>
    public readonly record struct SharedStoreWriteGrant(ByteString Value, DateTimeOffset Deadline);

    /// <summary>
    /// Captures an election's committed grant for a writer on the same guarded store.
    /// </summary>
    public interface ISharedStoreWriteFenceSource
    {
        /// <summary>
        /// Captures a reader of the exact committed grant of one acquisition.
        /// </summary>
        /// <param name="store">The authoritative store used for publication.</param>
        /// <param name="guardKey">The existing key identifying the grant.</param>
        /// <param name="readBoundGrant">
        /// Returns the grant or a renewal of that same acquisition, or an empty grant after revocation.
        /// </param>
        /// <returns>Whether a current grant could be captured for this store.</returns>
        bool TryCaptureWriteFence(
            ISharedKeyValueStore store,
            out string guardKey,
            [NotNullWhen(true)] out Func<SharedStoreWriteGrant>? readBoundGrant);
    }
}
