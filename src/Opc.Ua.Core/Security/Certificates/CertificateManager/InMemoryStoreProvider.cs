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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// A certificate store provider that creates in-memory certificate
    /// stores, primarily intended for testing scenarios.
    /// </summary>
    /// <remarks>
    /// Every store opened on the same <c>InMemory:</c> path shares the
    /// certificates held for that path by this provider, so a certificate
    /// written through one store instance (e.g. a trust list update) is seen
    /// by the next one (e.g. the validator). The held certificates are
    /// released when the provider is disposed.
    /// </remarks>
    public sealed class InMemoryStoreProvider : ICertificateStoreProvider, IDisposable
    {
        /// <inheritdoc/>
        public void Dispose()
        {
            CertificateIdentifierCollectionStore[] stores;
            lock (m_lock)
            {
                stores = [.. m_stores.Values];
                m_stores.Clear();
            }
            foreach (CertificateIdentifierCollectionStore store in stores)
            {
                store.Dispose();
            }
        }

        /// <inheritdoc/>
        public string StoreTypeName => "InMemory";

        /// <inheritdoc/>
        public bool SupportsStorePath(string storePath)
        {
            return !string.IsNullOrEmpty(storePath) &&
                storePath.StartsWith(
                    "InMemory:",
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public ICertificateStore CreateStore(ITelemetryContext telemetry)
        {
            return new SharedStore(this, telemetry);
        }

        /// <summary>
        /// Returns the backing store shared by all stores opened on
        /// <paramref name="storePath"/>.
        /// </summary>
        private CertificateIdentifierCollectionStore GetBackingStore(
            string storePath,
            ITelemetryContext telemetry)
        {
            lock (m_lock)
            {
                if (!m_stores.TryGetValue(storePath, out CertificateIdentifierCollectionStore? store))
                {
                    store = new CertificateIdentifierCollectionStore(telemetry);
                    m_stores.Add(storePath, store);
                }
                return store;
            }
        }

        /// <summary>
        /// A non-owning view of the backing store of one path. Disposing the
        /// view leaves the shared certificates in place. Access to the backing
        /// store is serialized because store instances are used concurrently.
        /// </summary>
        private sealed class SharedStore(
            InMemoryStoreProvider provider,
            ITelemetryContext telemetry) : ICertificateStore
        {
            public void Dispose()
            {
                m_backing = null;
            }

            public void Open(string location, bool noPrivateKeys = true)
            {
                m_storePath = location ?? throw new ArgumentNullException(nameof(location));
                m_backing = provider.GetBackingStore(location, telemetry);
            }

            public void Close()
            {
                // nothing to do.
            }

            public string StoreType => "InMemory";

            public string StorePath => m_storePath;

            public bool NoPrivateKeys => true;

            public Task<CertificateCollection> EnumerateAsync(CancellationToken ct = default)
            {
                return Invoke(store => store.EnumerateAsync(ct));
            }

            public Task AddAsync(
                Certificate certificate,
                char[]? password = null,
                CancellationToken ct = default)
            {
                return Invoke(store => store.AddAsync(certificate, password, ct));
            }

            public Task AddRejectedAsync(
                CertificateCollection certificates,
                int maxCertificates,
                CancellationToken ct = default)
            {
                return Invoke(store => store.AddRejectedAsync(certificates, maxCertificates, ct));
            }

            public Task<bool> DeleteAsync(string thumbprint, CancellationToken ct = default)
            {
                return Invoke(store => store.DeleteAsync(thumbprint, ct));
            }

            public Task<CertificateCollection> FindByThumbprintAsync(
                string thumbprint,
                CancellationToken ct = default)
            {
                return Invoke(store => store.FindByThumbprintAsync(thumbprint, ct));
            }

            public bool SupportsLoadPrivateKey => false;

            public Task<Certificate?> LoadPrivateKeyAsync(
                string thumbprint,
                string? subjectName,
                string? applicationUri,
                NodeId certificateType,
                char[]? password,
                CancellationToken ct = default)
            {
                return Task.FromResult<Certificate?>(null);
            }

            public Task<StatusCode> IsRevokedAsync(
                Certificate issuer,
                Certificate certificate,
                CancellationToken ct = default)
            {
                return Task.FromResult(StatusCodes.BadNotSupported);
            }

            public bool SupportsCRLs => false;

            public Task<X509CRLCollection> EnumerateCRLsAsync(CancellationToken ct = default)
            {
                return Task.FromResult(new X509CRLCollection());
            }

            public Task<X509CRLCollection> EnumerateCRLsAsync(
                Certificate issuer,
                bool validateUpdateTime = true,
                CancellationToken ct = default)
            {
                return Task.FromResult(new X509CRLCollection());
            }

            public Task AddCRLAsync(X509CRL crl, CancellationToken ct = default)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }

            public Task<bool> DeleteCRLAsync(X509CRL crl, CancellationToken ct = default)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }

            /// <summary>
            /// Runs a (synchronously completing) operation of the backing
            /// store under the provider lock.
            /// </summary>
            /// <typeparam name="T">The result type of the operation.</typeparam>
            /// <exception cref="InvalidOperationException"></exception>
            private T Invoke<T>(Func<CertificateIdentifierCollectionStore, T> operation)
            {
                CertificateIdentifierCollectionStore backing = m_backing ??
                    throw new InvalidOperationException("The in-memory store is not open.");
                lock (provider.m_lock)
                {
                    return operation(backing);
                }
            }

            private string m_storePath = string.Empty;
            private CertificateIdentifierCollectionStore? m_backing;
        }

        private readonly Dictionary<string, CertificateIdentifierCollectionStore> m_stores =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Lock m_lock = new();
    }
}
