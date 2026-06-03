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
 *
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
