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

#nullable enable

// CA2000: ownership of disposables created in this file is transferred to long-lived
// caches, returned objects, or fields whose lifetime is managed by the containing type's
// Dispose. Per Phase 8 review the residual sites are accepted as ownership-transfer patterns
// rather than missed using statements.
#pragma warning disable CA2000
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Default in-process <see cref="ISecretStore"/>. Holds bytes in a
    /// concurrent dictionary keyed by
    /// <see cref="SecretIdentifier.Name"/>. Each
    /// <see cref="TryGet"/> / <see cref="GetAsync"/> hands out a fresh
    /// <see cref="ISecret"/> view; disposing the view is a no-op (secure
    /// clearing is deferred to a future revision).
    /// </summary>
    /// <remarks>
    /// Suitable for application-lifetime caller-supplied secrets such as
    /// the password held by <see cref="CertificatePasswordProvider"/>.
    /// Future stores (DPAPI, Key Vault, Kubernetes) can plug in alongside
    /// without touching consumers.
    /// </remarks>
    public sealed class InMemorySecretStore : ISecretStore
    {
        /// <summary>
        /// Default <see cref="ISecretStore.StoreType"/> for in-memory
        /// stores.
        /// </summary>
        public const string DefaultStoreType = "InMemory";

        private readonly ConcurrentDictionary<string, byte[]> m_entries = new();

        /// <summary>
        /// Creates a new in-memory store with the default store type.
        /// </summary>
        public InMemorySecretStore()
            : this(DefaultStoreType)
        {
        }

        /// <summary>
        /// Creates a new in-memory store with a custom store type
        /// discriminator. Useful when a process needs multiple
        /// in-memory stores routed by <see cref="ISecretRegistry"/>.
        /// </summary>
        public InMemorySecretStore(string storeType)
        {
            StoreType = storeType ?? throw new ArgumentNullException(nameof(storeType));
        }

        /// <inheritdoc/>
        public string StoreType { get; }

        /// <inheritdoc/>
        public ISecret? TryGet(SecretIdentifier id)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            return m_entries.TryGetValue(id.Name, out byte[]? bytes)
                ? new InMemorySecret(bytes)
                : null;
        }

        /// <inheritdoc/>
        public ValueTask<ISecret?> GetAsync(
            SecretIdentifier id,
            CancellationToken ct = default)
        {
            return new ValueTask<ISecret?>(TryGet(id));
        }

        /// <inheritdoc/>
        public ValueTask SetAsync(
            SecretIdentifier id,
            ReadOnlyMemory<byte> bytes,
            CancellationToken ct = default)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            m_entries[id.Name] = bytes.ToArray();
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<bool> RemoveAsync(
            SecretIdentifier id,
            CancellationToken ct = default)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            return new ValueTask<bool>(m_entries.TryRemove(id.Name, out _));
        }

        /// <summary>
        /// View onto a byte[] in <see cref="InMemorySecretStore"/>. The
        /// store owns the underlying buffer; disposing this view is a
        /// no-op. A future revision will wire secure-memory clearing
        /// (for example via <see cref="Array.Clear(Array,int,int)"/>
        /// on store removal) without changing the public surface.
        /// </summary>
        private sealed class InMemorySecret : ISecret
        {
            private readonly byte[] m_bytes;

            public InMemorySecret(byte[] bytes)
            {
                m_bytes = bytes;
            }

            public ReadOnlySpan<byte> Bytes => m_bytes;

            public void Dispose()
            {
                // Defer secure-memory zeroing to a follow-up phase; for
                // now the per-call view simply drops its reference.
            }
        }
    }
}
