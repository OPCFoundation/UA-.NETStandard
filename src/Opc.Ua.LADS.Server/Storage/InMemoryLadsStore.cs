// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// An explicitly selected, process-local store with the same snapshot isolation and format limits
    /// as <see cref="FileSystemLadsStore"/>. It is not a fallback for persistence failures.
    /// </summary>
    public sealed class InMemoryLadsStore : ILadsStore
    {
        /// <summary>
        /// Creates an empty store with a byte limit for each complete serialized snapshot.
        /// </summary>
        public InMemoryLadsStore(int maxBytes = 64 * 1024 * 1024)
        {
            LadsSnapshotCodec.ValidateLimit(maxBytes);
            m_maxBytes = maxBytes;
        }

        /// <inheritdoc/>
        public ValueTask<LadsStoredDeviceData?> LoadAsync(
            string deviceIdentity,
            CancellationToken cancellationToken = default)
        {
            LadsSnapshotCodec.ValidateIdentity(deviceIdentity);
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? content;
            lock (m_lock)
            {
                m_snapshots.TryGetValue(deviceIdentity, out content);
            }
            LadsStoredDeviceData? data = content == null
                ? null
                : LadsSnapshotCodec.Decode(content, deviceIdentity, m_maxBytes);
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<LadsStoredDeviceData?>(data);
        }

        /// <inheritdoc/>
        public ValueTask SaveAsync(
            string deviceIdentity,
            LadsStoredDeviceData data,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] content = LadsSnapshotCodec.Encode(data, deviceIdentity, m_maxBytes);
            lock (m_lock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_snapshots[deviceIdentity] = content;
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask DeleteAsync(string deviceIdentity, CancellationToken cancellationToken = default)
        {
            LadsSnapshotCodec.ValidateIdentity(deviceIdentity);
            lock (m_lock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_snapshots.Remove(deviceIdentity);
            }
            return default;
        }

        /// <summary>The complete snapshot byte limit.</summary>
        private readonly int m_maxBytes;

        /// <summary>Guards the snapshot map, never exposed to consumers.</summary>
        private readonly Lock m_lock = new();

        /// <summary>Private encoded snapshots prevent callers from mutating saved or loaded state.</summary>
        private readonly Dictionary<string, byte[]> m_snapshots = new(System.StringComparer.Ordinal);
    }
}
