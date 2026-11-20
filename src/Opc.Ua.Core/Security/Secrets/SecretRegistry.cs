/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Default <see cref="ISecretRegistry"/> implementation: maintains a
    /// dictionary of stores keyed by <see cref="ISecretStore.StoreType"/>
    /// and dispatches each lookup by the
    /// <see cref="SecretIdentifier.StoreType"/> of the identifier.
    /// </summary>
    public sealed class SecretRegistry : ISecretRegistry
    {
        private readonly ConcurrentDictionary<string, ISecretStore> m_stores = new();

        /// <summary>
        /// Creates an empty registry. Stores must be added with
        /// <see cref="RegisterStore"/> before lookups will succeed.
        /// </summary>
        public SecretRegistry()
        {
        }

        /// <summary>
        /// Creates a registry pre-populated with the supplied stores.
        /// Convenience for the common one-store-per-process case.
        /// </summary>
        public SecretRegistry(params ISecretStore[] stores)
        {
            if (stores == null)
            {
                throw new ArgumentNullException(nameof(stores));
            }

            foreach (ISecretStore store in stores)
            {
                RegisterStore(store);
            }
        }

        /// <inheritdoc/>
        public void RegisterStore(ISecretStore store)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            m_stores[store.StoreType] = store;
        }

        /// <inheritdoc/>
        public ISecret? TryGet(SecretIdentifier id)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            return m_stores.TryGetValue(id.StoreType, out ISecretStore? store)
                ? store.TryGet(id)
                : null;
        }

        /// <inheritdoc/>
        public ValueTask<ISecret?> GetAsync(
            SecretIdentifier id,
            CancellationToken ct = default)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            return m_stores.TryGetValue(id.StoreType, out ISecretStore? store)
                ? store.GetAsync(id, ct)
                : new ValueTask<ISecret?>((ISecret?)null);
        }
    }
}
