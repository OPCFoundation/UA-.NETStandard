// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Binds every address-space mutation to one committed writer grant.
    /// </summary>
    internal sealed class FencedAddressSpaceWriteView : ISharedKeyValueStore, ISharedKeyValueStoreConsistency
    {
        /// <summary>
        /// Creates a view that can never adopt a successor's writer grant.
        /// </summary>
        public FencedAddressSpaceWriteView(
            IDeadlineGuardedSharedKeyValueStore store,
            ISharedKeyValueStoreConsistency consistency,
            string guardKey,
            Func<SharedStoreWriteGrant> readBoundGrant)
        {
            if (string.IsNullOrEmpty(guardKey))
            {
                throw new ArgumentException("The writer grant key must be present.", nameof(guardKey));
            }
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            if (!store.SupportsGuardedDeadlines)
            {
                throw new NotSupportedException("The state store cannot enforce the writer deadline at commit.");
            }
            m_consistency = consistency ?? throw new ArgumentNullException(nameof(consistency));
            m_guardKey = guardKey;
            m_readBoundGrant = readBoundGrant ?? throw new ArgumentNullException(nameof(readBoundGrant));
        }

        /// <inheritdoc/>
        public ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, CancellationToken ct = default) =>
            m_store.TryGetAsync(key, ct);

        /// <inheritdoc/>
        public ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default) =>
            ChangeAsync(key, value, ct);

        /// <inheritdoc/>
        public async ValueTask<bool> CompareAndSwapAsync(
            string key,
            ByteString expected,
            ByteString value,
            CancellationToken ct = default)
        {
            if (!IsLinearizable(key))
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "Fenced address-space mutations require strong keyspaces.");
            }
            SharedStoreWriteGrant grant = m_readBoundGrant();
            while (!grant.Value.IsNull)
            {
                if (await m_store.CompareAndSwapBeforeAsync(
                        key, expected, value, m_guardKey, grant.Value, grant.Deadline, ct)
                    .ConfigureAwait(false))
                {
                    return true;
                }
                SharedStoreWriteGrant renewed = m_readBoundGrant();
                if (renewed != grant)
                {
                    grant = renewed;
                    continue;
                }
                // This read is only for error classification. Only the guarded CAS is allowed to write.
                await VerifyGrantAsync(grant.Value, ct).ConfigureAwait(false);
                return false;
            }
            throw LostWriterGrant();
        }

        /// <inheritdoc/>
        public async ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                (bool found, ByteString expected) = await m_store.TryGetAsync(key, ct).ConfigureAwait(false);
                if (!found)
                {
                    return false;
                }
                if (await CompareAndSwapAsync(key, expected, default, ct).ConfigureAwait(false))
                {
                    return true;
                }
            }
        }

        /// <inheritdoc/>
        public IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
            string keyPrefix,
            CancellationToken ct = default) =>
            m_store.ScanAsync(keyPrefix, ct);

        /// <inheritdoc/>
        public IAsyncEnumerable<KeyValueChange> WatchAsync(string keyPrefix, CancellationToken ct = default) =>
            m_store.WatchAsync(keyPrefix, ct);

        /// <inheritdoc/>
        public bool IsLinearizable(string key) => m_consistency.IsLinearizable(key);

        /// <inheritdoc/>
        public bool IsProcessLocal(string key) => m_consistency.IsProcessLocal(key);

        /// <summary>
        /// Fails if this grant is no longer visible. This does not authorize a subsequent mutation.
        /// </summary>
        public ValueTask VerifyGrantAsync(CancellationToken ct) => VerifyGrantAsync(m_readBoundGrant().Value, ct);

        private async ValueTask VerifyGrantAsync(ByteString grant, CancellationToken ct)
        {
            if (grant.IsNull)
            {
                throw LostWriterGrant();
            }
            (bool found, ByteString current) = await m_store.TryGetAsync(m_guardKey, ct).ConfigureAwait(false);
            if (!found || current != grant)
            {
                throw LostWriterGrant();
            }
        }

        private static ServiceResultException LostWriterGrant() => new(
            StatusCodes.BadNotWritable, "The committed address-space writer grant has changed.");

        private async ValueTask ChangeAsync(string key, ByteString value, CancellationToken ct)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                (bool found, ByteString expected) = await m_store.TryGetAsync(key, ct).ConfigureAwait(false);
                if (await CompareAndSwapAsync(key, found ? expected : default, value, ct).ConfigureAwait(false))
                {
                    return;
                }
            }
        }

        private readonly IDeadlineGuardedSharedKeyValueStore m_store;
        private readonly ISharedKeyValueStoreConsistency m_consistency;
        private readonly string m_guardKey;
        private readonly Func<SharedStoreWriteGrant> m_readBoundGrant;
    }
}
