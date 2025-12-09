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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;
using Opc.Ua.X509StoreExtensions;

namespace Opc.Ua
{
    /// <summary>
    /// Provides access to a simple X509Store based certificate store.
    /// </summary>
    public class X509CertificateStore : ICertificateStore
    {
        /// <summary>
        /// Create an instance of the certificate store.
        /// </summary>
        public X509CertificateStore(ITelemetryContext telemetry)
        {
            // defaults
            m_logger = telemetry.CreateLogger<X509CertificateStore>();
            m_storeName = "My";
            m_storeLocation = StoreLocation.CurrentUser;
        }

        /// <summary>
        /// Raised when the certificate store contents change.
        /// </summary>
        public event CertificateStoreChangedEventHandler CertificateStoreChanged
        {
            add
            {
                m_CertificateStoreChanged += value;
                // Start monitoring when first subscriber is added
                if (m_CertificateStoreChanged != null && m_monitoringTimer == null)
                {
                    StartMonitoring();
                }
            }
            remove
            {
                m_CertificateStoreChanged -= value;
                // Stop monitoring when no more subscribers
                if (m_CertificateStoreChanged == null && m_monitoringTimer != null)
                {
                    StopMonitoring();
                }
            }
        }

        /// <inheritdoc/>
        public void StartMonitoring()
        {
            // Use polling to detect X509Store changes (FileSystemWatcher doesn't work for Windows cert stores)
            // Poll every 60 seconds
            if (m_monitoringTimer == null && !string.IsNullOrEmpty(StorePath))
            {
                m_lastCertificateCount = GetCertificateCount();
                m_monitoringTimer = new Timer(OnMonitoringTimerElapsed, null, 60000, 60000);
                m_logger.LogInformation("Started monitoring X509 certificate store: {StorePath}", StorePath);
            }
        }

        /// <inheritdoc/>
        public void StopMonitoring()
        {
            if (m_monitoringTimer != null)
            {
                m_monitoringTimer.Dispose();
                m_monitoringTimer = null;
                m_logger.LogInformation("Stopped monitoring X509 certificate store: {StorePath}", StorePath);
            }
        }

        /// <summary>
        /// Gets the current certificate count in the store.
        /// </summary>
        private int GetCertificateCount()
        {
            try
            {
                using var store = new X509Store(m_storeName, m_storeLocation);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                return store.Certificates.Count;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Called when the monitoring timer elapses to check for changes.
        /// </summary>
        private void OnMonitoringTimerElapsed(object state)
        {
            try
            {
                int currentCount = GetCertificateCount();
                if (currentCount != m_lastCertificateCount)
                {
                    m_lastCertificateCount = currentCount;
                    m_logger.LogInformation("X509 certificate store change detected: {StorePath}", StorePath);
                    m_CertificateStoreChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Error monitoring X509 certificate store: {StorePath}", StorePath);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose method for derived classes.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopMonitoring();
                Close();
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Syntax: StoreLocation\StoreName
        /// Example:
        ///   CurrentUser\My
        /// </remarks>
        public void Open(string location, bool noPrivateKeys = true)
        {
            StorePath = location ?? throw new ArgumentNullException(nameof(location));
            NoPrivateKeys = noPrivateKeys;
            location = location.Trim();

            if (string.IsNullOrEmpty(location))
            {
                throw ServiceResultException.Unexpected(
                    "Store Location cannot be empty.");
            }

            // extract store name.
            int index = location.IndexOf('\\', StringComparison.Ordinal);
            if (index == -1)
            {
                throw ServiceResultException.Unexpected(
                    "Path does not specify a store name. Path={0}",
                    location);
            }

            // extract store location.
            string storeLocation = location[..index];
            bool found = false;
            foreach (StoreLocation availableLocation in new[] {
                StoreLocation.LocalMachine,
                StoreLocation.CurrentUser })
            {
                if (availableLocation.ToString()
                    .Equals(storeLocation, StringComparison.OrdinalIgnoreCase))
                {
                    m_storeLocation = availableLocation;
                    found = true;
                }
            }
            if (!found)
            {
                throw ServiceResultException.Unexpected(
                    "Store location specified not available. Store location={0}",
                    storeLocation);
            }

            m_storeName = location[(index + 1)..];
        }

        /// <inheritdoc/>
        public void Close()
        {
            // nothing to do
        }

        /// <inheritdoc/>
        public string StoreType => CertificateStoreType.X509Store;

        /// <inheritdoc/>
        public string StorePath { get; private set; }

        /// <inheritdoc/>
        public bool NoPrivateKeys { get; private set; }

        /// <inheritdoc/>
        public Task<X509Certificate2Collection> EnumerateAsync(CancellationToken ct = default)
        {
            using var store = new X509Store(m_storeName, m_storeLocation);
            store.Open(OpenFlags.ReadOnly);
            return Task.FromResult(new X509Certificate2Collection(store.Certificates));
        }

        /// <inheritdoc/>
        public Task AddAsync(
            X509Certificate2 certificate,
            char[] password = null,
            CancellationToken ct = default)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            using (var store = new X509Store(m_storeName, m_storeLocation))
            {
                store.Open(OpenFlags.ReadWrite);
                if (!store.Certificates.Contains(certificate))
                {
                    if (certificate.HasPrivateKey && !NoPrivateKeys)
                    {
                        // X509Store needs a persisted private key
                        X509Certificate2 persistedCertificate = X509Utils.CreateCopyWithPrivateKey(
                            certificate,
                            true);
                        store.Add(persistedCertificate);
                    }
                    else if (certificate.HasPrivateKey && NoPrivateKeys)
                    {
                        // ensure no private key is added to store
                        using X509Certificate2 publicKey = CertificateFactory.Create(certificate.RawData);
                        store.Add(publicKey);
                    }
                    else
                    {
                        store.Add(certificate);
                    }

                    m_logger.LogInformation(
                        "Added certificate {Certificate} to X509Store {Name}.",
                        certificate.AsLogSafeString(),
                        store.Name);
                }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<bool> DeleteAsync(string thumbprint, CancellationToken ct = default)
        {
            using (var store = new X509Store(m_storeName, m_storeLocation))
            {
                store.Open(OpenFlags.ReadWrite);

                foreach (X509Certificate2 certificate in store.Certificates)
                {
                    if (certificate.Thumbprint == thumbprint)
                    {
                        store.Remove(certificate);
                    }
                }
            }

            return Task.FromResult(true);
        }

        /// <inheritdoc/>
        public Task<X509Certificate2Collection> FindByThumbprintAsync(
            string thumbprint,
            CancellationToken ct = default)
        {
            using var store = new X509Store(m_storeName, m_storeLocation);
            store.Open(OpenFlags.ReadOnly);

            var collection = new X509Certificate2Collection();

            foreach (X509Certificate2 certificate in store.Certificates)
            {
                if (certificate.Thumbprint == thumbprint)
                {
                    collection.Add(certificate);
                }
            }

            return Task.FromResult(collection);
        }

        /// <inheritdoc/>
        public bool SupportsLoadPrivateKey => false;

        /// <inheritdoc/>
        /// <remarks>The LoadPrivateKey special handling is not necessary in this store.</remarks>
        public Task<X509Certificate2> LoadPrivateKeyAsync(
            string thumbprint,
            string subjectName,
            string applicationUri,
            NodeId certificateType,
            char[] password,
            CancellationToken ct = default)
        {
            return Task.FromResult<X509Certificate2>(null);
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are only supported on Windows Platform.</remarks>
        public bool SupportsCRLs => PlatformHelper.IsWindowsWithCrlSupport();

        /// <inheritdoc/>
        /// <remarks>CRLs are only supported on Windows Platform.</remarks>
        public async Task<StatusCode> IsRevokedAsync(
            X509Certificate2 issuer,
            X509Certificate2 certificate,
            CancellationToken ct = default)
        {
            if (!SupportsCRLs)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }

            if (issuer == null)
            {
                throw new ArgumentNullException(nameof(issuer));
            }

            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            X509CRLCollection crls = await EnumerateCRLsAsync(ct).ConfigureAwait(false);
            // check for CRL.

            bool crlExpired = true;

            foreach (X509CRL crl in crls)
            {
                if (!X509Utils.CompareDistinguishedName(crl.IssuerName, issuer.SubjectName))
                {
                    continue;
                }

                if (!crl.VerifySignature(issuer, false))
                {
                    continue;
                }

                if (crl.IsRevoked(certificate))
                {
                    return (StatusCode)StatusCodes.BadCertificateRevoked;
                }

                if (crl.ThisUpdate <= DateTime.UtcNow &&
                    (crl.NextUpdate == DateTime.MinValue || crl.NextUpdate >= DateTime.UtcNow))
                {
                    crlExpired = false;
                }
            }

            // certificate is fine.
            if (!crlExpired)
            {
                return (StatusCode)StatusCodes.Good;
            }

            // can't find a valid CRL.
            return (StatusCode)StatusCodes.BadCertificateRevocationUnknown;
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are only supported on Windows Platform.</remarks>
        public Task<X509CRLCollection> EnumerateCRLsAsync(CancellationToken ct = default)
        {
            if (!SupportsCRLs)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            var crls = new X509CRLCollection();
            using (var store = new X509Store(m_storeName, m_storeLocation))
            {
                store.Open(OpenFlags.ReadOnly);

                foreach (byte[] rawCrl in store.EnumerateCrls(m_logger))
                {
                    try
                    {
                        var crl = new X509CRL(rawCrl);
                        crls.Add(crl);
                    }
                    catch (Exception e)
                    {
                        m_logger.LogError(e, "Failed to parse CRL in store {StoreName}.", store.Name);
                    }
                }
            }
            return Task.FromResult(crls);
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are only supported on Windows Platform.</remarks>
        public async Task<X509CRLCollection> EnumerateCRLsAsync(
            X509Certificate2 issuer,
            bool validateUpdateTime = true,
            CancellationToken ct = default)
        {
            if (!SupportsCRLs)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            if (issuer == null)
            {
                throw new ArgumentNullException(nameof(issuer));
            }
            var crls = new X509CRLCollection();
            foreach (X509CRL crl in await EnumerateCRLsAsync(ct).ConfigureAwait(false))
            {
                if (!X509Utils.CompareDistinguishedName(crl.IssuerName, issuer.SubjectName))
                {
                    continue;
                }

                if (!crl.VerifySignature(issuer, false))
                {
                    continue;
                }

                if (!validateUpdateTime ||
                    (
                        crl.ThisUpdate <= DateTime.UtcNow &&
                        (crl.NextUpdate == DateTime.MinValue || crl.NextUpdate >= DateTime.UtcNow)))
                {
                    crls.Add(crl);
                }
            }

            return crls;
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are only supported on Windows Platform.</remarks>
        public async Task AddCRLAsync(X509CRL crl, CancellationToken ct = default)
        {
            if (!SupportsCRLs)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            if (crl == null)
            {
                throw new ArgumentNullException(nameof(crl));
            }

            X509Certificate2 issuer = null;
            X509Certificate2Collection certificates = await EnumerateAsync(ct).ConfigureAwait(
                false);
            foreach (X509Certificate2 certificate in certificates)
            {
                if (X509Utils.CompareDistinguishedName(certificate.SubjectName, crl.IssuerName) &&
                    crl.VerifySignature(certificate, false))
                {
                    issuer = certificate;
                    break;
                }
            }

            if (issuer == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateInvalid,
                    "Could not find issuer of the CRL.");
            }
            using var store = new X509Store(m_storeName, m_storeLocation);
            store.Open(OpenFlags.ReadWrite);

            store.AddCrl(crl.RawData, m_logger);
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are only supported on Windows Platform.</remarks>
        public Task<bool> DeleteCRLAsync(X509CRL crl, CancellationToken ct = default)
        {
            if (!SupportsCRLs)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            if (crl == null)
            {
                throw new ArgumentNullException(nameof(crl));
            }
            using var store = new X509Store(m_storeName, m_storeLocation);
            store.Open(OpenFlags.ReadWrite);

            return Task.FromResult(store.DeleteCrl(crl.RawData, m_logger));
        }

        /// <inheritdoc/>
        public Task AddRejectedAsync(
            X509Certificate2Collection certificates,
            int maxCertificates,
            CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        private readonly ILogger m_logger;
        private string m_storeName;
        private StoreLocation m_storeLocation;
        private Timer m_monitoringTimer;
        private int m_lastCertificateCount;
        private event CertificateStoreChangedEventHandler m_CertificateStoreChanged;
    }
}
