/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Default <see cref="ICertificateProvider"/> implementation backed
    /// by an internal <see cref="CertificateCache"/> for the sync
    /// fast-path and
    /// <see cref="CertificateIdentifierResolver.LoadPrivateKeyAsync"/>
    /// for the async cold-path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cache writes happen on every successful cold-path resolution so
    /// repeated <see cref="GetPrivateKeyCertificateAsync"/> calls hit
    /// the in-memory tier. Private-key entries inherit the cache's
    /// time-to-live (default 30 seconds in
    /// <see cref="CertificateCache"/>) — long-running token handlers
    /// will see eviction and re-load transparently.
    /// </para>
    /// <para>
    /// The provider holds a single
    /// <see cref="CertificateCache"/>; consumers that want isolated
    /// caches construct their own instance. The cache itself is owned
    /// by this provider and disposed when the provider is disposed.
    /// </para>
    /// </remarks>
    internal sealed class CertificateProvider : ICertificateProvider, IDisposable
    {
        private readonly CertificateCache m_cache;
        private readonly ITelemetryContext m_telemetry;
        private readonly ICertificateStoreResolver? m_storeResolver;
        private int m_disposed;

        /// <summary>
        /// Creates a new provider with a private cache.
        /// </summary>
        public CertificateProvider(
            ITelemetryContext telemetry,
            ICertificateStoreResolver? storeResolver = null)
        {
            m_telemetry = telemetry;
            m_storeResolver = storeResolver;
            m_cache = new CertificateCache(telemetry);
        }

        /// <inheritdoc/>
        public Certificate? TryGetPrivateKeyCertificate(string thumbprint)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(thumbprint))
            {
                return null;
            }

            Certificate? cert = m_cache.TryGet(thumbprint);
            if (cert != null && !cert.HasPrivateKey)
            {
                // The cache may also hold the public-key copy; a public
                // entry is not what callers asked for here. Drop the ref
                // and treat as a miss.
                cert.Dispose();
                return null;
            }
            return cert;
        }

        /// <inheritdoc/>
        public async ValueTask<Certificate?> GetPrivateKeyCertificateAsync(
            CertificateIdentifier identifier,
            ICertificatePasswordProvider? passwordProvider = null,
            string? applicationUri = null,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (identifier == null)
            {
                throw new ArgumentNullException(nameof(identifier));
            }

            // Fast-path: sync cache hit for the supplied thumbprint.
            if (!string.IsNullOrEmpty(identifier.Thumbprint))
            {
                Certificate? cached = TryGetPrivateKeyCertificate(identifier.Thumbprint!);
                if (cached != null)
                {
                    return cached;
                }
            }

            // Cold-path: load from the underlying store and write back.
            Certificate? loaded = await CertificateIdentifierResolver
                .LoadPrivateKeyCoreAsync(identifier, passwordProvider, applicationUri, m_telemetry, m_storeResolver, ct)
                .ConfigureAwait(false);

            if (loaded != null && loaded.HasPrivateKey && !string.IsNullOrEmpty(loaded.Thumbprint))
            {
                m_cache.Set(loaded.Thumbprint!, loaded);
            }

            return loaded;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            m_cache.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(CertificateProvider));
            }
        }
    }
}
