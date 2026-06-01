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
        private int m_disposed;

        /// <summary>
        /// Creates a new provider with a private cache.
        /// </summary>
        public CertificateProvider(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
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
                .LoadPrivateKeyAsync(identifier, passwordProvider, applicationUri, m_telemetry, ct)
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
