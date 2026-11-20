/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Di.Server.SoftwareUpdate
{
    /// <summary>
    /// In-memory <see cref="ISoftwarePackageStore"/> implementation
    /// — packages are kept as <see cref="byte"/> arrays under a
    /// <see cref="ConcurrentDictionary{TKey, TValue}"/>. Suitable for
    /// unit tests and small fixtures; do not use for large production
    /// payloads (memory footprint scales linearly with stored data).
    /// </summary>
    public sealed class MemoryPackageStore : ISoftwarePackageStore
    {
        private readonly ConcurrentDictionary<string, Entry> m_entries = new();
        private readonly TimeProvider m_timeProvider;

        /// <summary>
        /// Creates a new in-memory store.
        /// </summary>
        public MemoryPackageStore(TimeProvider? timeProvider = null)
        {
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<SoftwarePackage> ListAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            foreach (KeyValuePair<string, Entry> kv in m_entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return kv.Value.Metadata;
            }
        }

        /// <inheritdoc/>
        public ValueTask<SoftwarePackage?> GetAsync(
            string packageId, CancellationToken cancellationToken = default)
        {
            ValidateId(packageId);
            return new ValueTask<SoftwarePackage?>(
                m_entries.TryGetValue(packageId, out Entry? entry) ? entry.Metadata : null);
        }

        /// <inheritdoc/>
        public ValueTask<bool> ExistsAsync(
            string packageId, CancellationToken cancellationToken = default)
        {
            ValidateId(packageId);
            return new ValueTask<bool>(m_entries.ContainsKey(packageId));
        }

        /// <inheritdoc/>
        public ValueTask<Stream> OpenReadAsync(
            string packageId, CancellationToken cancellationToken = default)
        {
            ValidateId(packageId);
            if (!m_entries.TryGetValue(packageId, out Entry? entry))
            {
                throw new FileNotFoundException(
                    $"Software package '{packageId}' not found.", packageId);
            }
            // Return a read-only view over the stored bytes; callers
            // own the stream and dispose when finished.
            return new ValueTask<Stream>(
                new MemoryStream(entry.Payload, writable: false));
        }

        /// <inheritdoc/>
        public async ValueTask<SoftwarePackage> AddAsync(
            SoftwarePackage metadata,
            Stream payload,
            CancellationToken cancellationToken = default)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }
            ValidateId(metadata.Id);
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }
            using var buffer = new MemoryStream();
#if NETFRAMEWORK
            await payload.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
#else
            await payload.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
#endif
            byte[] bytes = buffer.ToArray();

            SoftwarePackage final = metadata with
            {
                SizeBytes = bytes.LongLength,
                CreatedAt = m_timeProvider.GetUtcNow()
            };

            m_entries[metadata.Id] = new Entry(final, bytes);
            return final;
        }

        /// <inheritdoc/>
        public ValueTask<bool> DeleteAsync(
            string packageId, CancellationToken cancellationToken = default)
        {
            ValidateId(packageId);
            return new ValueTask<bool>(m_entries.TryRemove(packageId, out _));
        }

        private static void ValidateId(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                throw new ArgumentException(
                    "Package id must be a non-empty string.", nameof(packageId));
            }
        }

        private sealed class Entry
        {
            public Entry(SoftwarePackage metadata, byte[] payload)
            {
                Metadata = metadata;
                Payload = payload;
            }

            public SoftwarePackage Metadata { get; }
            public byte[] Payload { get; }
        }
    }
}
