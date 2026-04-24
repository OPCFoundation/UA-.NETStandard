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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Default implementation of <see cref="ITrustListTransaction"/> that
    /// buffers all changes in memory and applies them when
    /// <see cref="CommitAsync"/> is called. Disposing without committing
    /// discards all pending changes.
    /// </summary>
    internal sealed class TrustListTransaction : ITrustListTransaction
    {
        private readonly ICertificateTrustListManager m_manager;
        private readonly List<Certificate> m_addTrusted = new();
        private readonly List<string> m_removeTrusted = new();
        private readonly List<Certificate> m_addIssuer = new();
        private readonly List<string> m_removeIssuer = new();
        private readonly List<X509CRL> m_addCrls = new();
        private readonly List<X509CRL> m_removeCrls = new();
        private bool m_committed;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="TrustListTransaction"/> class.
        /// </summary>
        /// <param name="manager">
        /// The trust-list manager used to open stores on commit.
        /// </param>
        /// <param name="trustList">The trust list being modified.</param>
        internal TrustListTransaction(
            ICertificateTrustListManager manager,
            TrustListIdentifier trustList)
        {
            m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
            TrustList = trustList ?? throw new ArgumentNullException(nameof(trustList));
        }

        /// <inheritdoc/>
        public TrustListIdentifier TrustList { get; }

        /// <inheritdoc/>
        public Task AddTrustedCertificateAsync(
            Certificate certificate,
            CancellationToken ct = default)
        {
            ThrowIfDisposedOrCommitted();
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            m_addTrusted.Add(certificate);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task RemoveTrustedCertificateAsync(
            string thumbprint,
            CancellationToken ct = default)
        {
            ThrowIfDisposedOrCommitted();
            if (thumbprint == null)
            {
                throw new ArgumentNullException(nameof(thumbprint));
            }

            m_removeTrusted.Add(thumbprint);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task AddIssuerCertificateAsync(
            Certificate certificate,
            CancellationToken ct = default)
        {
            ThrowIfDisposedOrCommitted();
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            m_addIssuer.Add(certificate);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task RemoveIssuerCertificateAsync(
            string thumbprint,
            CancellationToken ct = default)
        {
            ThrowIfDisposedOrCommitted();
            if (thumbprint == null)
            {
                throw new ArgumentNullException(nameof(thumbprint));
            }

            m_removeIssuer.Add(thumbprint);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task AddCrlAsync(X509CRL crl, CancellationToken ct = default)
        {
            ThrowIfDisposedOrCommitted();
            if (crl == null)
            {
                throw new ArgumentNullException(nameof(crl));
            }

            m_addCrls.Add(crl);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task RemoveCrlAsync(X509CRL crl, CancellationToken ct = default)
        {
            ThrowIfDisposedOrCommitted();
            if (crl == null)
            {
                throw new ArgumentNullException(nameof(crl));
            }

            m_removeCrls.Add(crl);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task CommitAsync(CancellationToken ct = default)
        {
            ThrowIfDisposedOrCommitted();

            // Apply trusted-store operations.
            using (ICertificateStore trustedStore = m_manager.OpenTrustedStore(TrustList))
            {
                foreach (Certificate cert in m_addTrusted)
                {
                    await trustedStore.AddAsync(cert, ct: ct).ConfigureAwait(false);
                }

                foreach (string thumbprint in m_removeTrusted)
                {
                    await trustedStore.DeleteAsync(thumbprint, ct).ConfigureAwait(false);
                }

                // CRLs are stored alongside trusted certificates.
                foreach (X509CRL crl in m_addCrls)
                {
                    await trustedStore.AddCRLAsync(crl, ct).ConfigureAwait(false);
                }

                foreach (X509CRL crl in m_removeCrls)
                {
                    await trustedStore.DeleteCRLAsync(crl, ct).ConfigureAwait(false);
                }
            }

            // Apply issuer-store operations if an issuer store is configured.
            ICertificateStore? issuerStore = m_manager.OpenIssuerStore(TrustList);
            if (issuerStore != null)
            {
                using (issuerStore)
                {
                    foreach (Certificate cert in m_addIssuer)
                    {
                        await issuerStore.AddAsync(cert, ct: ct).ConfigureAwait(false);
                    }

                    foreach (string thumbprint in m_removeIssuer)
                    {
                        await issuerStore.DeleteAsync(thumbprint, ct).ConfigureAwait(false);
                    }
                }
            }

            m_committed = true;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            if (!m_disposed)
            {
                m_addTrusted.Clear();
                m_removeTrusted.Clear();
                m_addIssuer.Clear();
                m_removeIssuer.Clear();
                m_addCrls.Clear();
                m_removeCrls.Clear();
                m_disposed = true;
            }

            return default;
        }

        private void ThrowIfDisposedOrCommitted()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }

            if (m_committed)
            {
                throw new InvalidOperationException(
                    "This transaction has already been committed.");
            }
        }
    }
}
