/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// In-memory metadata store that persists credential secrets through an <see cref="ISecretStore"/>.
    /// </summary>
    public sealed class InMemoryKeyCredentialStore : IKeyCredentialStore, IDisposable
    {
        private const string DefaultSecretStoreType = "KeyCredentialPush";
        private readonly Dictionary<string, StoredCredential> m_credentials = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim m_lock = new(1, 1);
        private readonly ISecretStore m_secretStore;
        private bool m_disposed;

        /// <summary>
        /// Creates a store backed by an in-process <see cref="InMemorySecretStore"/>.
        /// </summary>
        public InMemoryKeyCredentialStore()
            : this(new InMemorySecretStore(DefaultSecretStoreType))
        {
        }

        /// <summary>
        /// Creates a store backed by the supplied secret store.
        /// </summary>
        public InMemoryKeyCredentialStore(ISecretStore secretStore)
        {
            m_secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        }

        /// <inheritdoc/>
        public async Task<KeyCredential?> GetAsync(string credentialId, CancellationToken ct)
        {
            ValidateCredentialId(credentialId);
            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (!m_credentials.TryGetValue(credentialId, out StoredCredential? metadata))
                {
                    return null;
                }

                using ISecret? secret = await m_secretStore
                    .GetAsync(CreateSecretId(credentialId), ct)
                    .ConfigureAwait(false);
                if (secret == null)
                {
                    return null;
                }

                return new KeyCredential(
                    secret.Bytes.ToArray(),
                    metadata.Expiration,
                    metadata.Subject,
                    metadata.Scopes);
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <inheritdoc/>
        public async Task UpdateAsync(string credentialId, KeyCredential credential, CancellationToken ct)
        {
            ValidateCredentialId(credentialId);
            if (credential == null)
            {
                throw new ArgumentNullException(nameof(credential));
            }

            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await m_secretStore
                    .SetAsync(CreateSecretId(credentialId), credential.Secret, ct)
                    .ConfigureAwait(false);
                m_credentials[credentialId] = new StoredCredential(
                    credential.Expiration,
                    credential.Subject,
                    credential.Scopes);
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <inheritdoc/>
        public async Task DeleteAsync(string credentialId, CancellationToken ct)
        {
            ValidateCredentialId(credentialId);
            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                m_credentials.Remove(credentialId);
                await m_secretStore
                    .RemoveAsync(CreateSecretId(credentialId), ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<string>> ListAsync(CancellationToken ct)
        {
            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var result = new List<string>(m_credentials.Keys);
                result.Sort(StringComparer.Ordinal);
                return result.AsReadOnly();
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            m_lock.Dispose();
        }

        private static void ValidateCredentialId(string credentialId)
        {
            if (string.IsNullOrWhiteSpace(credentialId))
            {
                throw new ArgumentException("CredentialId must be supplied.", nameof(credentialId));
            }
        }

        private SecretIdentifier CreateSecretId(string credentialId)
        {
            return new SecretIdentifier("KeyCredential/" + credentialId, m_secretStore.StoreType);
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(InMemoryKeyCredentialStore));
            }
        }

        private sealed class StoredCredential
        {
            public StoredCredential(
                DateTime expiration,
                IReadOnlyDictionary<string, object?> subject,
                IReadOnlyList<string> scopes)
            {
                Expiration = expiration;
                var subjectCopy = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, object?> item in subject)
                {
                    subjectCopy[item.Key] = item.Value;
                }
                Subject = subjectCopy;
                Scopes = new List<string>(scopes).AsReadOnly();
            }

            public DateTime Expiration { get; }

            public IReadOnlyDictionary<string, object?> Subject { get; }

            public IReadOnlyList<string> Scopes { get; }
        }
    }
}
