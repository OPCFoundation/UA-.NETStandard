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
            foreach (var cert in await certStore.EnumerateAsync(ct).ConfigureAwait(false))
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
                        var withPrivateKey = await certStore.LoadPrivateKeyAsync(cert.Thumbprint,
                            cert.Subject, null, NodeId.Null, password, ct).ConfigureAwait(false);
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
            var crls = await certStore.EnumerateCRLsAsync(ct).ConfigureAwait(false);
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
                var certCollection = await certStore.FindByThumbprintAsync(
                    cert.Thumbprint, ct).ConfigureAwait(false);
                if (certCollection.Count != 0)
                {
                    await certStore.DeleteAsync(cert.Thumbprint, ct).ConfigureAwait(false);
                }

                if (store != CertificateStoreName.Application)
                {
                    await certStore.AddAsync(cert, password?.ToCharArray(), ct).ConfigureAwait(false);
                }
                else
                {
                    var configuration = await GetConfigurationAsync().ConfigureAwait(false);
                    var security = configuration.SecurityConfiguration;
                    var storePassword = security.CertificatePasswordProvider.GetPassword(
                        new CertificateIdentifier
                        {
                            StoreType = certStore.StoreType,
                            StorePath = certStore.StorePath,
                            Certificate = cert,
                            Thumbprint = cert.Thumbprint,
                            SubjectName = cert.Subject
                        });
                    await certStore.AddAsync(cert, storePassword, ct).ConfigureAwait(false);

                    if (security.AddAppCertToTrustedStore)
                    {
                        using var trustedCert = new X509Certificate2(cert);
                        using var trustedStore =
                            await OpenAsync(CertificateStoreName.Trusted).ConfigureAwait(false);
                        await trustedStore.AddAsync(trustedCert, ct: ct).ConfigureAwait(false);
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
                await certStore.AddCRLAsync(new X509CRL(crl), ct).ConfigureAwait(false);
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
            var certCollection = await rejected.FindByThumbprintAsync(thumbprint, ct).ConfigureAwait(false);
            if (certCollection.Count == 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadNotFound, "Certificate not found");
            }
            var trustedCert = certCollection[0];
            thumbprint = trustedCert.Thumbprint;
            try
            {
                using var trusted = await OpenAsync(CertificateStoreName.Trusted).ConfigureAwait(false);
                certCollection = await trusted.FindByThumbprintAsync(thumbprint, ct).ConfigureAwait(false);
                if (certCollection.Count != 0)
                {
                    // This should not happen but maybe a previous approval aborted half-way.
                    _logger.LogError("Found rejected cert already in trusted store. Deleting...");
                    await trusted.DeleteAsync(thumbprint, ct).ConfigureAwait(false);
                }

                // Add the trusted cert and remove from rejected
                await trusted.AddAsync(trustedCert, ct: ct).ConfigureAwait(false);
                if (!await rejected.DeleteAsync(thumbprint, ct).ConfigureAwait(false))
                {
                    // Try revert back...
                    await trusted.DeleteAsync(thumbprint, ct).ConfigureAwait(false);
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
            var chain = Utils.ParseCertificateChainBlob(certificateChain,
                    (Opc.Ua.ITelemetryContext?)null)?
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
                    await AddAsync(configuration.SecurityConfiguration.TrustedHttpsCertificates,
                        false, x509Certificate).ConfigureAwait(false);
                    chain.RemoveAt(0);
                    if (chain.Count > 0)
                    {
                        await AddAsync(configuration.SecurityConfiguration.HttpsIssuerCertificates,
                            false, [.. chain]).ConfigureAwait(false);
                    }
                }
                else
                {
                    await AddAsync(configuration.SecurityConfiguration.TrustedPeerCertificates,
                        false, x509Certificate).ConfigureAwait(false);
                    chain.RemoveAt(0);
                    if (chain.Count > 0)
                    {
                        await AddAsync(configuration.SecurityConfiguration.TrustedIssuerCertificates,
                            false, [.. chain]).ConfigureAwait(false);
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
                var certCollection = await certStore.FindByThumbprintAsync(thumbprint, ct).ConfigureAwait(false);
                if (certCollection.Count == 0)
                {
                    throw ServiceResultException.Create(StatusCodes.BadNotFound, "Certificate not found");
                }

                // delete all CRLs signed by cert
                var crlsToDelete = new X509CRLCollection();
                foreach (var crl in await certStore.EnumerateCRLsAsync(ct).ConfigureAwait(false))
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
                if (!await certStore.DeleteAsync(thumbprint, ct).ConfigureAwait(false))
                {
                    throw ServiceResultException.Create(StatusCodes.BadNotFound, "Certificate not found");
                }
                foreach (var crl in crlsToDelete)
                {
                    if (!await certStore.DeleteCRLAsync(crl, ct).ConfigureAwait(false))
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
                foreach (var certs in await certStore.EnumerateAsync(ct).ConfigureAwait(false))
                {
                    if (!await certStore.DeleteAsync(certs.Thumbprint, ct).ConfigureAwait(false))
                    {
                        // intentionally ignore errors, try best effort
                        _logger.LogError("Failed to delete {Certificate}.", certs.Thumbprint);
                    }
                }
                foreach (var crl in await certStore.EnumerateCRLsAsync(ct).ConfigureAwait(false))
                {
                    if (!await certStore.DeleteCRLAsync(crl, ct).ConfigureAwait(false))
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
                await certStore.DeleteCRLAsync(new X509CRL(crl), ct).ConfigureAwait(false);
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
                    appConfig.SecurityConfiguration.ApplicationCertificate.OpenStore(
                        (Opc.Ua.ITelemetryContext?)null);
                var certs = await certStore.EnumerateAsync().ConfigureAwait(false);
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
                    .TrustedIssuerCertificates.OpenStore((Opc.Ua.ITelemetryContext?)null);
                var certs = await certStore.EnumerateAsync().ConfigureAwait(false);
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
                    var crls = await certStore.EnumerateCRLsAsync().ConfigureAwait(false);
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
                    .TrustedPeerCertificates.OpenStore((Opc.Ua.ITelemetryContext?)null);
                var certs = await certStore.EnumerateAsync().ConfigureAwait(false);
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
                    var crls = await certStore.EnumerateCRLsAsync().ConfigureAwait(false);
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
                    .RejectedCertificateStore.OpenStore((Opc.Ua.ITelemetryContext?)null);
                var certs = await certStore.EnumerateAsync().ConfigureAwait(false);
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
                    return security.ApplicationCertificate.OpenStore(
                        (Opc.Ua.ITelemetryContext?)null);
                case CertificateStoreName.Trusted:
                    Debug.Assert(security.TrustedPeerCertificates != null);
                    return security.TrustedPeerCertificates.OpenStore(
                        (Opc.Ua.ITelemetryContext?)null);
                case CertificateStoreName.Rejected:
                    Debug.Assert(security.RejectedCertificateStore != null);
                    return security.RejectedCertificateStore.OpenStore(
                        (Opc.Ua.ITelemetryContext?)null);
                case CertificateStoreName.Issuer:
                    Debug.Assert(security.TrustedIssuerCertificates != null);
                    return security.TrustedIssuerCertificates.OpenStore(
                        (Opc.Ua.ITelemetryContext?)null);
                case CertificateStoreName.User:
                    Debug.Assert(security.TrustedUserCertificates != null);
                    return security.TrustedUserCertificates.OpenStore(
                        (Opc.Ua.ITelemetryContext?)null);
                case CertificateStoreName.UserIssuer:
                    Debug.Assert(security.UserIssuerCertificates != null);
                    return security.UserIssuerCertificates.OpenStore(
                        (Opc.Ua.ITelemetryContext?)null);
                case CertificateStoreName.Https:
                    Debug.Assert(security.TrustedHttpsCertificates != null);
                    return security.TrustedHttpsCertificates.OpenStore(
                        (Opc.Ua.ITelemetryContext?)null);
                case CertificateStoreName.HttpsIssuer:
                    Debug.Assert(security.HttpsIssuerCertificates != null);
                    return security.HttpsIssuerCertificates.OpenStore(
                        (Opc.Ua.ITelemetryContext?)null);
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
        private static async Task AddAsync(CertificateTrustList trustList, bool noCopy,
            params X509Certificate2[] certificates)
        {
            ArgumentNullException.ThrowIfNull(certificates);
            using var trustedStore = trustList.OpenStore((Opc.Ua.ITelemetryContext?)null);
            await AddAsync(trustedStore, noCopy, certificates).ConfigureAwait(false);
            foreach (var cert in certificates)
            {
#pragma warning disable CA2000 // Dispose objects before losing scope
                trustList.TrustedCertificates = trustList.TrustedCertificates.AddItem(
                    new CertificateIdentifier(noCopy ? cert : new X509Certificate2(cert)));
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
        private static async Task AddAsync(ICertificateStore store, bool noCopy,
            params X509Certificate2[] certificates)
        {
            ArgumentNullException.ThrowIfNull(certificates);
            foreach (var cert in certificates)
            {
                try { await store.DeleteAsync(cert.Thumbprint).ConfigureAwait(false); } catch { }
#pragma warning disable CA2000 // Dispose objects before losing scope
                await store.AddAsync(noCopy ? cert : new X509Certificate2(cert)).ConfigureAwait(false);
#pragma warning restore CA2000 // Dispose objects before losing scope
            }
        }

        private const int kMaxThumbprintLength = 64;
        private readonly ILogger<ApplicationBase> _logger;
        private bool _disposed;
    }
}
