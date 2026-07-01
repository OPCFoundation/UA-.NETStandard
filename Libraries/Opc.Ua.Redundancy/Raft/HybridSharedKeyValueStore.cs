/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: the <see cref="RedundancyConsistencyMode.Eventual"/> store. It routes each
    /// key to exactly one backend by key prefix: keys under a configured "strong" prefix (the single-use nonce,
    /// lease, and leader-election keyspaces) live entirely in the linearizable <see cref="RaftSharedKeyValueStore"/>;
    /// all other keys live in the eventually-consistent <see cref="CrdtSharedKeyValueStore"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The invariant is "a key lives in exactly one store", so a compare-and-swap or change-feed on a strong key gets
    /// the linearizable Raft semantics, while bulk keys keep the leaderless CRDT semantics (and, as with the raw CRDT
    /// store, do not support compare-and-swap / watch — by design those operations belong to the strong keyspace).
    /// </para>
    /// <para>
    /// A scan whose prefix spans both keyspaces (for example the empty prefix used for full hydration) enumerates the
    /// bulk store and then the strong store.
    /// </para>
    /// </remarks>
    public sealed class HybridSharedKeyValueStore : ISharedKeyValueStore, IAsyncDisposable
    {
        /// <summary>
        /// Creates a hybrid store.
        /// </summary>
        /// <param name="bulkStore">The eventually-consistent backend for bulk keys.</param>
        /// <param name="strongStore">The linearizable backend for strong keys.</param>
        /// <param name="strongKeyPrefixes">
        /// The key prefixes routed to <paramref name="strongStore"/>. When empty, the defaults
        /// (<c>nonce/</c>, <c>lease/</c>, <c>election/</c>) are used.
        /// </param>
        /// <param name="ownsStores">
        /// When <c>true</c>, both backends are disposed together with this store.
        /// </param>
        public HybridSharedKeyValueStore(
            ISharedKeyValueStore bulkStore,
            ISharedKeyValueStore strongStore,
            ArrayOf<string> strongKeyPrefixes = default,
            bool ownsStores = false)
        {
            m_bulk = bulkStore ?? throw new ArgumentNullException(nameof(bulkStore));
            m_strong = strongStore ?? throw new ArgumentNullException(nameof(strongStore));
            m_ownsStores = ownsStores;
            m_strongPrefixes = strongKeyPrefixes.IsEmpty ? s_defaultStrongPrefixes : ToArray(strongKeyPrefixes);
        }

        /// <inheritdoc/>
        public ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, CancellationToken ct = default)
        {
            return Route(key).TryGetAsync(key, ct);
        }

        /// <inheritdoc/>
        public ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
        {
            return Route(key).SetAsync(key, value, ct);
        }

        /// <inheritdoc/>
        public ValueTask<bool> CompareAndSwapAsync(
            string key,
            ByteString expected,
            ByteString value,
            CancellationToken ct = default)
        {
            return Route(key).CompareAndSwapAsync(key, expected, value, ct);
        }

        /// <inheritdoc/>
        public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            return Route(key).DeleteAsync(key, ct);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
            string keyPrefix,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            keyPrefix ??= string.Empty;

            if (IsStrong(keyPrefix))
            {
                await foreach (KeyValuePair<string, ByteString> entry in m_strong
                    .ScanAsync(keyPrefix, ct)
                    .ConfigureAwait(false))
                {
                    yield return entry;
                }
                yield break;
            }

            await foreach (KeyValuePair<string, ByteString> entry in m_bulk
                .ScanAsync(keyPrefix, ct)
                .ConfigureAwait(false))
            {
                yield return entry;
            }

            if (SpansStrong(keyPrefix))
            {
                await foreach (KeyValuePair<string, ByteString> entry in m_strong
                    .ScanAsync(keyPrefix, ct)
                    .ConfigureAwait(false))
                {
                    yield return entry;
                }
            }
        }

        /// <inheritdoc/>
        public IAsyncEnumerable<KeyValueChange> WatchAsync(string keyPrefix, CancellationToken ct = default)
        {
            keyPrefix ??= string.Empty;
            return IsStrong(keyPrefix)
                ? m_strong.WatchAsync(keyPrefix, ct)
                : m_bulk.WatchAsync(keyPrefix, ct);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            if (m_ownsStores)
            {
                await DisposeStoreAsync(m_strong).ConfigureAwait(false);
                await DisposeStoreAsync(m_bulk).ConfigureAwait(false);
            }
        }

        private ISharedKeyValueStore Route(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            return IsStrong(key) ? m_strong : m_bulk;
        }

        /// <summary>
        /// Returns whether <paramref name="key"/> is routed to the linearizable strong (Raft) store.
        /// </summary>
        /// <param name="key">The key (or key prefix) to test.</param>
        public bool IsStrongKey(string key)
        {
            return IsStrong(key ?? string.Empty);
        }

        private bool IsStrong(string keyOrPrefix)
        {
            for (int ii = 0; ii < m_strongPrefixes.Length; ii++)
            {
                if (keyOrPrefix.StartsWith(m_strongPrefixes[ii], StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private bool SpansStrong(string scanPrefix)
        {
            for (int ii = 0; ii < m_strongPrefixes.Length; ii++)
            {
                if (m_strongPrefixes[ii].StartsWith(scanPrefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static async ValueTask DisposeStoreAsync(ISharedKeyValueStore store)
        {
            switch (store)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }

        private static string[] ToArray(ArrayOf<string> prefixes)
        {
            var result = new string[prefixes.Count];
            for (int ii = 0; ii < prefixes.Count; ii++)
            {
                result[ii] = prefixes[ii];
            }
            return result;
        }

        private static readonly string[] s_defaultStrongPrefixes = ["nonce/", "lease/", "election/"];

        /// <summary>
        /// The default strong-keyspace prefixes (<c>nonce/</c>, <c>lease/</c>, <c>election/</c>) used when no explicit
        /// set is configured.
        /// </summary>
        public static ArrayOf<string> DefaultStrongKeyPrefixes { get; } = ["nonce/", "lease/", "election/"];

        private readonly ISharedKeyValueStore m_bulk;
        private readonly ISharedKeyValueStore m_strong;
        private readonly string[] m_strongPrefixes;
        private readonly bool m_ownsStores;
        private int m_disposed;
    }
}
