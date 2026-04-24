// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using Opc.Ua.Security.Certificates;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The basis of a client or server application providing services like
    /// managing the application's private key infrastructure and certificate
    /// stores.
    /// </summary>
    internal abstract class ApplicationBase : IPkiManagement, IDisposable
    {
        /// <summary>
        /// Create application
        /// </summary>
        /// <param name="telemetry"></param>
        /// <exception cref="ArgumentException"></exception>
        protected ApplicationBase(ITelemetryContext telemetry) => _logger = telemetry.LoggerFactory.CreateLogger<ApplicationBase>();

        /// <inheritdoc/>
        public async ValueTask<IReadOnlyList<X509Certificate>> ListCertificatesAsync(
            CertificateStoreName store, bool includePrivateKey, CancellationToken ct)
        {
            // show application certs
            using var certStore = await OpenAsync(store).ConfigureAwait(false);
            var certificates = new List<X509Certificate>();
            ApplicationConfiguration? configuration = null;
            foreach (var cert in await certStore.Enumerate().ConfigureAwait(false))
            {
                switch (store)
                {
                    case CertificateStoreName.Application:
                        if (!includePrivateKey || !certStore.SupportsLoadPrivateKey)
                        {
                            goto default;
                        }
                        configuration ??= await GetConfigurationAsync().ConfigureAwait(false);
                        var security = configuration.SecurityConfiguration;
                        var password = security.CertificatePasswordProvider.GetPassword(
                            new CertificateIdentifier
                            {
                                StoreType = certStore.StoreType,
                                StorePath = certStore.StorePath,
                                Certificate = cert,
                                Thumbprint = cert.Thumbprint,
                                SubjectName = cert.Subject
                            });
                        var withPrivateKey = await certStore.LoadPrivateKey(cert.Thumbprint,
                            cert.Subject, password).ConfigureAwait(false);
                        if (withPrivateKey == null)
                        {
                            goto default;
                        }
                        certificates.Add(withPrivateKey);
                        break;
                    default:
                        certificates.Add(cert);
                        break;
                }
            }
            return certificates;
        }

        /// <inheritdoc/>
        public async ValueTask<IReadOnlyList<byte[]>> ListCertificateRevocationListsAsync(
            CertificateStoreName store, CancellationToken ct)
        {
            using var certStore = await OpenAsync(store).ConfigureAwait(false);
            if (!certStore.SupportsCRLs)
            {
                return [];
            }
            var crls = await certStore.EnumerateCRLs().ConfigureAwait(false);
            return crls.Select(c => c.RawData).ToList();
        }

        /// <inheritdoc/>
        public async ValueTask AddCertificateAsync(CertificateStoreName store, byte[] pfxBlob,
            string? password, CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var cert = X509CertificateLoader.LoadPkcs12(pfxBlob, password,
                X509KeyStorageFlags.Exportable);
            using var certStore = await OpenAsync(store).ConfigureAwait(false);
            try
            {
                _logger.LogInformation("Add Certificate {Thumbprint} to {Store}...",
                    cert.Thumbprint, store);
                var certCollection = await certStore.FindByThumbprint(
                    cert.Thumbprint).ConfigureAwait(false);
                if (certCollection.Count != 0)
                {
                    await certStore.Delete(cert.Thumbprint).ConfigureAwait(false);
                }

                if (store != CertificateStoreName.Application)
                {
                    await certStore.Add(cert, password).ConfigureAwait(false);
                }
                else
                {
                    var configuration = await GetConfigurationAsync().ConfigureAwait(false);
                    var security = configuration.SecurityConfiguration;
                    password = security.CertificatePasswordProvider.GetPassword(
                        new CertificateIdentifier
                        {
                            StoreType = certStore.StoreType,
                            StorePath = certStore.StorePath,
                            Certificate = cert,
                            Thumbprint = cert.Thumbprint,
                            SubjectName = cert.Subject
                        });
                    await certStore.Add(cert, password).ConfigureAwait(false);

                    if (security.AddAppCertToTrustedStore)
                    {
                        using var trustedCert = new X509Certificate2(cert);
                        using var trustedStore =
                            await OpenAsync(CertificateStoreName.Trusted).ConfigureAwait(false);
                        await trustedStore.Add(trustedCert).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add Certificate {Thumbprint} to {Store}...",
                    cert.Thumbprint, store);
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask AddCertificateRevocationListAsync(CertificateStoreName store, byte[] crl,
            CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var certStore = await OpenAsync(store).ConfigureAwait(false);
            if (!certStore.SupportsCRLs)
            {
                throw new NotSupportedException("Store does not support revocation lists");
            }
            try
            {
                _logger.LogInformation("Add Certificate revocation list to {Store}...", store);
                await certStore.AddCRL(new X509CRL(crl)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add Certificate revocation to {Store}...", store);
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask ApproveRejectedCertificateAsync(string thumbprint, CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            thumbprint = SanitizeThumbprint(thumbprint);
            using var rejected = await OpenAsync(CertificateStoreName.Rejected).ConfigureAwait(false);
            var certCollection = await rejected.FindByThumbprint(thumbprint).ConfigureAwait(false);
            if (certCollection.Count == 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadNotFound, "Certificate not found");
            }
            var trustedCert = certCollection[0];
            thumbprint = trustedCert.Thumbprint;
            try
            {
                using var trusted = await OpenAsync(CertificateStoreName.Trusted).ConfigureAwait(false);
                certCollection = await trusted.FindByThumbprint(thumbprint).ConfigureAwait(false);
                if (certCollection.Count != 0)
                {
                    // This should not happen but maybe a previous approval aborted half-way.
                    _logger.LogError("Found rejected cert already in trusted store. Deleting...");
                    await trusted.Delete(thumbprint).ConfigureAwait(false);
                }

                // Add the trusted cert and remove from rejected
                await trusted.Add(trustedCert).ConfigureAwait(false);
                if (!await rejected.Delete(thumbprint).ConfigureAwait(false))
                {
                    // Try revert back...
                    await trusted.Delete(thumbprint).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to approve Certificate {Thumbprint}...",
                    thumbprint);
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask AddCertificateChainAsync(byte[] certificateChain,
            bool isSslCertificate, CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var chain = Utils.ParseCertificateChainBlob(certificateChain)?
                .Cast<X509Certificate2>()
                .Reverse()
                .ToList();
            if (chain == null || chain.Count == 0)
            {
                throw new ArgumentNullException(nameof(certificateChain));
            }
            var configuration = await GetConfigurationAsync().ConfigureAwait(false);
            var x509Certificate = chain[0];
            try
            {
                _logger.LogInformation("Adding Certificate {Thumbprint}, " +
                    "{Subject} to trusted store...", x509Certificate.Thumbprint,
                    x509Certificate.Subject);

                if (isSslCertificate)
                {
                    Add(configuration.SecurityConfiguration.TrustedHttpsCertificates,
                        false, x509Certificate);
                    chain.RemoveAt(0);
                    if (chain.Count > 0)
                    {
                        Add(configuration.SecurityConfiguration.HttpsIssuerCertificates,
                            false, [.. chain]);
                    }
                }
                else
                {
                    Add(configuration.SecurityConfiguration.TrustedPeerCertificates,
                        false, x509Certificate);
                    chain.RemoveAt(0);
                    if (chain.Count > 0)
                    {
                        Add(configuration.SecurityConfiguration.TrustedIssuerCertificates,
                            false, [.. chain]);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add Certificate chain {Thumbprint}, " +
                    "{Subject} to trusted store.", x509Certificate.Thumbprint,
                    x509Certificate.Subject);
                throw;
            }
            finally
            {
                chain?.ForEach(c => c?.Dispose());
            }
        }

        /// <inheritdoc/>
        public async ValueTask RemoveCertificateAsync(CertificateStoreName store, string thumbprint,
            CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            thumbprint = SanitizeThumbprint(thumbprint);
            using var certStore = await OpenAsync(store).ConfigureAwait(false);
            try
            {
                _logger.LogInformation("Removing Certificate {Thumbprint} from {Store}...",
                    thumbprint, store);
                var certCollection = await certStore.FindByThumbprint(thumbprint).ConfigureAwait(false);
                if (certCollection.Count == 0)
                {
                    throw ServiceResultException.Create(StatusCodes.BadNotFound, "Certificate not found");
                }

                // delete all CRLs signed by cert
                var crlsToDelete = new X509CRLCollection();
                foreach (var crl in await certStore.EnumerateCRLs().ConfigureAwait(false))
                {
                    foreach (var cert in certCollection)
                    {
                        if (X509Utils.CompareDistinguishedName(cert.SubjectName, crl.IssuerName) &&
                            crl.VerifySignature(cert, false))
                        {
                            crlsToDelete.Add(crl);
                            break;
                        }
                    }
                }
                if (!await certStore.Delete(thumbprint).ConfigureAwait(false))
                {
                    throw ServiceResultException.Create(StatusCodes.BadNotFound, "Certificate not found");
                }
                foreach (var crl in crlsToDelete)
                {
                    if (!await certStore.DeleteCRL(crl).ConfigureAwait(false))
                    {
                        // intentionally ignore errors, try best effort
                        _logger.LogError("Failed to delete {Crl}.", crl.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove Certificate {Thumbprint} from {Store}...",
                    thumbprint, store);
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask CleanAsync(CertificateStoreName store, CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var certStore = await OpenAsync(store).ConfigureAwait(false);
            try
            {
                _logger.LogInformation("Removing all Certificate from {Store}...", store);
                foreach (var certs in await certStore.Enumerate().ConfigureAwait(false))
                {
                    if (!await certStore.Delete(certs.Thumbprint).ConfigureAwait(false))
                    {
                        // intentionally ignore errors, try best effort
                        _logger.LogError("Failed to delete {Certificate}.", certs.Thumbprint);
                    }
                }
                foreach (var crl in await certStore.EnumerateCRLs().ConfigureAwait(false))
                {
                    if (!await certStore.DeleteCRL(crl).ConfigureAwait(false))
                    {
                        // intentionally ignore errors, try best effort
                        _logger.LogError("Failed to delete {Crl}.", crl.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear {Store} store.", store);
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask RemoveCertificateRevocationListAsync(CertificateStoreName store, byte[] crl,
            CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var certStore = await OpenAsync(store).ConfigureAwait(false);
            if (!certStore.SupportsCRLs)
            {
                throw new NotSupportedException("Store does not support revocation lists");
            }
            try
            {
                _logger.LogInformation("Add Certificate revocation list to {Store}...", store);
                await certStore.DeleteCRL(new X509CRL(crl)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete Certificate revocation in {Store}...", store);
                throw;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            _disposed = true;
        }

        /// <summary>
        /// Get application configuration
        /// </summary>
        /// <returns></returns>
        protected abstract Task<ApplicationConfiguration> GetConfigurationAsync();

        /// <summary>
        /// Show all certificates in the certificate stores.
        /// </summary>
        /// <param name="appConfig"></param>
        protected async ValueTask ShowCertificateStoreInformationAsync(ApplicationConfiguration appConfig)
        {
            // show application certs
            try
            {
                using var certStore =
                    appConfig.SecurityConfiguration.ApplicationCertificate.OpenStore();
                var certs = await certStore.Enumerate().ConfigureAwait(false);
                var certNum = 1;
                _logger.LogInformation(
                    "Application own certificate store contains {Count} certs.", certs.Count);
                foreach (var cert in certs)
                {
                    _logger.LogInformation("{CertNum:D2}: Subject '{Subject}' (Thumbprint: {Thumbprint})",
                        certNum++, cert.Subject, cert.Thumbprint);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while trying to read information from application store.");
            }

            // show trusted issuer certs
            try
            {
                using var certStore = appConfig.SecurityConfiguration
                    .TrustedIssuerCertificates.OpenStore();
                var certs = await certStore.Enumerate().ConfigureAwait(false);
                var certNum = 1;
                _logger.LogInformation("Trusted issuer store contains {Count} certs.",
                    certs.Count);
                foreach (var cert in certs)
                {
                    _logger.LogInformation(
                        "{CertNum:D2}: Subject '{Subject}' (Thumbprint: {Thumbprint})",
                        certNum++, cert.Subject, cert.Thumbprint);
                }
                if (certStore.SupportsCRLs)
                {
                    var crls = await certStore.EnumerateCRLs().ConfigureAwait(false);
                    var crlNum = 1;
                    _logger.LogInformation("Trusted issuer store has {Count} CRLs.", crls.Count);
                    foreach (var crl in crls)
                    {
                        _logger.LogInformation(
                            "{CrlNum:D2}: Issuer '{Issuer}', Next update time '{NextUpdate}'",
                            crlNum++, crl.Issuer, crl.NextUpdate);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while trying to read information from trusted issuer store.");
            }

            // show trusted peer certs
            try
            {
                using var certStore = appConfig.SecurityConfiguration
                    .TrustedPeerCertificates.OpenStore();
                var certs = await certStore.Enumerate().ConfigureAwait(false);
                var certNum = 1;
                _logger.LogInformation("Trusted peer store contains {Count} certs.",
                    certs.Count);
                foreach (var cert in certs)
                {
                    _logger.LogInformation(
                        "{CertNum:D2}: Subject '{Subject}' (Thumbprint: {Thumbprint})",
                        certNum++, cert.Subject, cert.Thumbprint);
                }
                if (certStore.SupportsCRLs)
                {
                    var crls = await certStore.EnumerateCRLs().ConfigureAwait(false);
                    var crlNum = 1;
                    _logger.LogInformation("Trusted peer store has {Count} CRLs.", crls.Count);
                    foreach (var crl in crls)
                    {
                        _logger.LogInformation(
                            "{CrlNum:D2}: Issuer '{Issuer}', Next update time '{NextUpdate}'",
                            crlNum++, crl.Issuer, crl.NextUpdate);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e,
                    "Error while trying to read information from trusted peer store.");
            }

            // show rejected peer certs
            try
            {
                using var certStore = appConfig.SecurityConfiguration
                    .RejectedCertificateStore.OpenStore();
                var certs = await certStore.Enumerate().ConfigureAwait(false);
                var certNum = 1;
                _logger.LogInformation("Rejected certificate store contains {Count} certs.",
                    certs.Count);
                foreach (var cert in certs)
                {
                    _logger.LogInformation(
                        "{CertNum:D2}: Subject '{Subject}' (Thumbprint: {Thumbprint})",
                        certNum++, cert.Subject, cert.Thumbprint);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e,
                    "Error while trying to read information from rejected certificate store.");
            }
        }

        /// <summary>
        /// Open store
        /// </summary>
        /// <param name="store"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private async ValueTask<ICertificateStore> OpenAsync(CertificateStoreName store)
        {
            var configuration = await GetConfigurationAsync().ConfigureAwait(false);
            var security = configuration.SecurityConfiguration;
            switch (store)
            {
                case CertificateStoreName.Application:
                    Debug.Assert(security.ApplicationCertificate != null);
                    return security.ApplicationCertificate.OpenStore();
                case CertificateStoreName.Trusted:
                    Debug.Assert(security.TrustedPeerCertificates != null);
                    return security.TrustedPeerCertificates.OpenStore();
                case CertificateStoreName.Rejected:
                    Debug.Assert(security.RejectedCertificateStore != null);
                    return security.RejectedCertificateStore.OpenStore();
                case CertificateStoreName.Issuer:
                    Debug.Assert(security.TrustedIssuerCertificates != null);
                    return security.TrustedIssuerCertificates.OpenStore();
                case CertificateStoreName.User:
                    Debug.Assert(security.TrustedUserCertificates != null);
                    return security.TrustedUserCertificates.OpenStore();
                case CertificateStoreName.UserIssuer:
                    Debug.Assert(security.UserIssuerCertificates != null);
                    return security.UserIssuerCertificates.OpenStore();
                case CertificateStoreName.Https:
                    Debug.Assert(security.TrustedHttpsCertificates != null);
                    return security.TrustedHttpsCertificates.OpenStore();
                case CertificateStoreName.HttpsIssuer:
                    Debug.Assert(security.HttpsIssuerCertificates != null);
                    return security.HttpsIssuerCertificates.OpenStore();
                default:
                    throw new ArgumentException(
                        $"Bad unknown certificate store {store} specified.");
            }
        }

        private static string SanitizeThumbprint(string thumbprint)
        {
            if (thumbprint.Length > kMaxThumbprintLength)
            {
                throw new ArgumentException("Bad thumbprint", nameof(thumbprint));
            }
            return thumbprint.ReplaceLineEndings(string.Empty);
        }

        /// <summary>
        /// Add to trust list
        /// </summary>
        /// <param name="trustList"></param>
        /// <param name="noCopy"></param>
        /// <param name="certificates"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="certificates"/> is <c>null</c>.</exception>
        private static void Add(CertificateTrustList trustList, bool noCopy,
            params X509Certificate2[] certificates)
        {
            ArgumentNullException.ThrowIfNull(certificates);
            using var trustedStore = trustList.OpenStore();
            Add(trustedStore, noCopy, certificates);
            foreach (var cert in certificates)
            {
#pragma warning disable CA2000 // Dispose objects before losing scope
                trustList.TrustedCertificates.Add(new CertificateIdentifier(
                    noCopy ? cert : new X509Certificate2(cert)));
#pragma warning restore CA2000 // Dispose objects before losing scope
            }
        }

        /// <summary>
        /// Add to certificate store
        /// </summary>
        /// <param name="store"></param>
        /// <param name="noCopy"></param>
        /// <param name="certificates"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"><paramref name="certificates"/>
        /// is <c>null</c>.</exception>
        private static void Add(ICertificateStore store, bool noCopy,
            params X509Certificate2[] certificates)
        {
            ArgumentNullException.ThrowIfNull(certificates);
            foreach (var cert in certificates)
            {
                try { store.Delete(cert.Thumbprint); } catch { }
#pragma warning disable CA2000 // Dispose objects before losing scope
                store.Add(noCopy ? cert : new X509Certificate2(cert));
#pragma warning restore CA2000 // Dispose objects before losing scope
            }
        }

        private const int kMaxThumbprintLength = 64;
        private readonly ILogger<ApplicationBase> _logger;
        private bool _disposed;
    }
}
