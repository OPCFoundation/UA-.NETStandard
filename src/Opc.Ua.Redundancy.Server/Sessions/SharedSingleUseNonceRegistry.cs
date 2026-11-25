// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// <see cref="ISingleUseNonceRegistry"/> backed by an
    /// <see cref="ISharedKeyValueStore"/>. Consumption is a single
    /// compare-and-swap that creates a marker key only when it is absent, so the
    /// store's atomicity guarantees exactly one replica wins the race to consume
    /// a given nonce. The nonce itself is never stored: the key is the SHA-256
    /// digest of the nonce, so the secret-bearing keyspace stays one-way.
    /// </summary>
    public sealed class SharedSingleUseNonceRegistry : ISingleUseNonceRegistry
    {
        /// <summary>
        /// Creates a registry over a shared key/value backend.
        /// </summary>
        /// <param name="store">The shared key/value backend.</param>
        /// <param name="keyPrefix">
        /// The key prefix under which consumed-nonce markers are recorded.
        /// </param>
        /// <param name="timeProvider">
        /// The clock that stamps consumed-nonce markers and ages them in
        /// <see cref="PurgeAsync"/>; defaults to <see cref="TimeProvider.System"/>.
        /// </param>
        public SharedSingleUseNonceRegistry(
            ISharedKeyValueStore store,
            string keyPrefix = DefaultKeyPrefix,
            TimeProvider? timeProvider = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_keyPrefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public ValueTask<bool> TryConsumeAsync(ByteString nonce, CancellationToken ct = default)
        {
            if (nonce.IsNull || nonce.IsEmpty)
            {
                throw new ArgumentException("Nonce must not be null or empty.", nameof(nonce));
            }

            string key = m_keyPrefix + Digest(nonce);
            byte[] marker = new byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(marker, m_timeProvider.GetUtcNow().UtcDateTime.Ticks);

            // expected = default(ByteString) => require the key to be absent, so
            // the swap succeeds only for the first consumer of this nonce.
            return m_store.CompareAndSwapAsync(key, default, new ByteString(marker), ct);
        }

        /// <inheritdoc/>
        public async ValueTask<int> PurgeAsync(
            IEnumerable<ByteString> retainedNonces,
            TimeSpan minimumAge,
            CancellationToken ct = default)
        {
            if (retainedNonces == null)
            {
                throw new ArgumentNullException(nameof(retainedNonces));
            }

            var retained = new HashSet<string>(StringComparer.Ordinal);
            foreach (ByteString nonce in retainedNonces)
            {
                if (!nonce.IsNull && !nonce.IsEmpty)
                {
                    retained.Add(m_keyPrefix + Digest(nonce));
                }
            }

            long cutoff = (m_timeProvider.GetUtcNow().UtcDateTime - minimumAge).Ticks;
            var expired = new List<KeyValuePair<string, ByteString>>();
            await foreach (KeyValuePair<string, ByteString> marker in m_store
                .ScanAsync(m_keyPrefix, ct)
                .ConfigureAwait(false))
            {
                // A value that is not a timestamp marker is not ours and is left alone.
                if (!retained.Contains(marker.Key) &&
                    marker.Value.Length == 8 &&
                    BinaryPrimitives.ReadInt64LittleEndian(marker.Value.Span) <= cutoff)
                {
                    expired.Add(marker);
                }
            }

            int purged = 0;
            foreach (KeyValuePair<string, ByteString> marker in expired)
            {
                // Conditional, so a marker written again meanwhile is kept.
                if (await m_store
                    .CompareAndSwapAsync(marker.Key, marker.Value, default, ct)
                    .ConfigureAwait(false))
                {
                    purged++;
                }
            }
            return purged;
        }

        /// <summary>
        /// Requires a nonce keyspace that all replicas can use for linearizable single-use checks.
        /// </summary>
        internal static void ValidateSharedStore(ISharedKeyValueStore? store)
        {
            if (store is not ISharedKeyValueStoreConsistency consistency ||
                !consistency.IsLinearizable(DefaultKeyPrefix) ||
                consistency.IsProcessLocal(DefaultKeyPrefix))
            {
                throw new InvalidOperationException(
                    $"Fast reconnect requires an {nameof(ISharedKeyValueStore)} that declares '{DefaultKeyPrefix}' " +
                    $"linearizable and shared across processes via {nameof(ISharedKeyValueStoreConsistency)}. " +
                    $"Register a shared nonce backend or disable {nameof(DistributedSessionOptions)}." +
                    $"{nameof(DistributedSessionOptions.EnableFastReconnect)}.");
            }
        }

        private static string Digest(ByteString nonce)
        {
            byte[] data = nonce.ToArray();
#if NET8_0_OR_GREATER
            byte[] hash = SHA256.HashData(data);
#else
            byte[] hash;
            using (var sha = SHA256.Create())
            {
                hash = sha.ComputeHash(data);
            }
#endif
            return Convert.ToBase64String(hash);
        }

        private const string DefaultKeyPrefix = "nonce/";
        private readonly ISharedKeyValueStore m_store;
        private readonly string m_keyPrefix;
        private readonly TimeProvider m_timeProvider;
    }
}
