// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Tests
{
    /// <summary>
    /// Simulates an external linearizable store for tests that give their in-process replicas one shared backend.
    /// </summary>
    public sealed class SimulatedSharedKeyValueStore :
        ISharedKeyValueStore,
        ISharedKeyValueStoreConsistency,
        IDisposable
    {
        /// <inheritdoc/>
        public bool IsLinearizable(string key)
        {
            return m_store.IsLinearizable(key);
        }

        /// <inheritdoc/>
        public bool IsProcessLocal(string key)
        {
            return false;
        }

        /// <inheritdoc/>
        public ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, CancellationToken ct = default)
        {
            return m_store.TryGetAsync(key, ct);
        }

        /// <inheritdoc/>
        public ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
        {
            return m_store.SetAsync(key, value, ct);
        }

        /// <inheritdoc/>
        public ValueTask<bool> CompareAndSwapAsync(
            string key,
            ByteString expected,
            ByteString value,
            CancellationToken ct = default)
        {
            return m_store.CompareAndSwapAsync(key, expected, value, ct);
        }

        /// <inheritdoc/>
        public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            return m_store.DeleteAsync(key, ct);
        }

        /// <inheritdoc/>
        public IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
            string keyPrefix,
            CancellationToken ct = default)
        {
            return m_store.ScanAsync(keyPrefix, ct);
        }

        /// <inheritdoc/>
        public IAsyncEnumerable<KeyValueChange> WatchAsync(string keyPrefix, CancellationToken ct = default)
        {
            return m_store.WatchAsync(keyPrefix, ct);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_store.Dispose();
        }

        private readonly InMemorySharedKeyValueStore m_store = new();
    }
}
