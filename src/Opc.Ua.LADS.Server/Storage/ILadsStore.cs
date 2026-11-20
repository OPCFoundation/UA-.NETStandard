// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// Persists complete device snapshots without resuming physical execution.
    /// </summary>
    public interface ILadsStore
    {
        /// <summary>
        /// Loads a detached snapshot, or returns null when no snapshot exists.
        /// Corrupt, unsupported, oversized and inaccessible snapshots cause an exception.
        /// </summary>
        ValueTask<LadsStoredDeviceData?> LoadAsync(
            string deviceIdentity,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically replaces the complete snapshot for the given identity.
        /// The snapshot identity must match the key.
        /// </summary>
        ValueTask SaveAsync(
            string deviceIdentity,
            LadsStoredDeviceData data,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the snapshot, if present.
        /// </summary>
        ValueTask DeleteAsync(string deviceIdentity, CancellationToken cancellationToken = default);
    }
}
