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
using System.IO;
using System.Threading;
using System.Threading.Tasks;


namespace Opc.Ua.Core.Tests
{
    /// <summary>
    /// Test helper that creates a temporary directory-backed PKI store
    /// and exposes an <see cref="ICertificateManager"/> built from it.
    /// </summary>
    /// <remarks>
    /// This is the modern replacement for <c>TemporaryCertValidator</c> which
    /// uses the legacy <see cref="CertificateValidator"/>. New tests should
    /// prefer this helper. The two helpers share the same store layout
    /// (issuer / trusted / rejected directories) so they can be used
    /// interchangeably during the migration.
    /// </remarks>
    public sealed class TemporaryCertificateManager : IDisposable
    {
        /// <summary>
        /// Create the PKI store under a fresh temporary path and return
        /// the helper.
        /// </summary>
        public static TemporaryCertificateManager Create(
            ITelemetryContext telemetry,
            bool rejectedStore = false)
        {
            return new TemporaryCertificateManager(telemetry, rejectedStore);
        }

        private TemporaryCertificateManager(ITelemetryContext telemetry, bool rejectedStore)
        {
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName() + Path.DirectorySeparatorChar;
            m_telemetry = telemetry;
            m_issuerStore = new DirectoryCertificateStore(telemetry);
            m_issuerStore.Open(m_pkiRoot + "issuer", true);
            m_trustedStore = new DirectoryCertificateStore(telemetry);
            m_trustedStore.Open(m_pkiRoot + "trusted", true);
            if (rejectedStore)
            {
                m_rejectedStore = new DirectoryCertificateStore(telemetry);
                m_rejectedStore.Open(m_pkiRoot + "rejected", true);
            }
        }

        ~TemporaryCertificateManager()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing && Interlocked.CompareExchange(ref m_disposed, 1, 0) == 0)
            {
                CleanupValidatorAndStoresAsync(true).GetAwaiter().GetResult();
                m_issuerStore = null;
                m_trustedStore = null;
                m_rejectedStore = null;
                string path = Utils.ReplaceSpecialFolderNames(m_pkiRoot);
                int retries = 5;
                while (retries-- > 0)
                {
                    try
                    {
                        if (Directory.Exists(path))
                        {
                            Directory.Delete(path, true);
                        }
                        retries = 0;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(1000);
                    }
                }
            }
        }

        /// <summary>
        /// The certificate manager built from the temporary stores. The
        /// instance is created on demand via <see cref="UpdateAsync"/>
        /// (mirroring the legacy <c>TemporaryCertValidator.Update</c>
        /// pattern) and refreshed each time it is called.
        /// </summary>
        public ICertificateManager Manager => m_manager;

        /// <summary>
        /// Convenience accessor that returns <see cref="Manager"/> typed
        /// as <see cref="ICertificateValidatorEx"/>.
        /// </summary>
        public ICertificateValidatorEx Validator => m_manager;

        /// <summary>
        /// The issuer store, contains certs used for chain validation.
        /// </summary>
        public ICertificateStore IssuerStore => m_issuerStore;

        /// <summary>
        /// The trusted store, used for trusted CA, Sub CA and leaf
        /// certificates.
        /// </summary>
        public ICertificateStore TrustedStore => m_trustedStore;

        /// <summary>
        /// The rejected store, used for rejected certificates.
        /// </summary>
        public ICertificateStore RejectedStore => m_rejectedStore;

        /// <summary>
        /// (Re)builds the certificate manager from the current state of
        /// the issuer / trusted / rejected directories. Returns the
        /// <see cref="ICertificateManager"/> for direct use in tests.
        /// </summary>
        public ICertificateManager Update()
        {
            CertificateManager previous = m_manager as CertificateManager;
            previous?.Dispose();

            var securityConfiguration = new SecurityConfiguration
            {
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = m_issuerStore.Directory.FullName
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = m_trustedStore.Directory.FullName
                }
            };
            if (m_rejectedStore != null)
            {
                securityConfiguration.RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = m_rejectedStore.Directory.FullName
                };
            }

            CertificateManager manager = CertificateManagerFactory.Create(
                securityConfiguration,
                m_telemetry);
            m_manager = manager;
            return manager;
        }

        /// <summary>
        /// Async wrapper around <see cref="Update"/> for parity with
        /// callers that expect an awaitable factory.
        /// </summary>
        public Task<ICertificateManager> UpdateAsync()
        {
            return Task.FromResult(Update());
        }

        /// <summary>
        /// Cleans up (deletes) the contents of the issuer, trusted and
        /// rejected stores. Disposes the underlying manager when
        /// <paramref name="dispose"/> is <see langword="true"/>.
        /// </summary>
        public async Task CleanupValidatorAndStoresAsync(bool dispose = false)
        {
            (m_manager as CertificateManager)?.Dispose();
            await TestUtils.CleanupTrustListAsync(m_issuerStore, dispose).ConfigureAwait(false);
            await TestUtils.CleanupTrustListAsync(m_trustedStore, dispose).ConfigureAwait(false);
            await TestUtils.CleanupTrustListAsync(m_rejectedStore, dispose).ConfigureAwait(false);
        }

        private int m_disposed;
        private ICertificateManager m_manager;
        private DirectoryCertificateStore m_issuerStore;
        private DirectoryCertificateStore m_trustedStore;
        private DirectoryCertificateStore m_rejectedStore;
        private readonly string m_pkiRoot;
        private readonly ITelemetryContext m_telemetry;
    }
}
