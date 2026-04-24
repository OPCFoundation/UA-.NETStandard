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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Central certificate management implementation.
    /// Currently implements trust-list management; other interfaces
    /// will be added in subsequent phases.
    /// </summary>
    public sealed partial class CertificateManager :
        ICertificateManager,
        IDisposable
    {
        private readonly Dictionary<TrustListIdentifier, TrustListEntry> m_trustLists = new();
        private readonly List<CertificateEntry> m_applicationCertificates = [];
        private readonly List<ICertificateStoreProvider> m_storeProviders;
        private readonly CertificateChangeSubject m_changeSubject = new();
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly int m_maxRejectedCertificates;
        private RejectedCertificateProcessor? m_rejectedProcessor;
        private CertificateLifecycleMonitor? m_lifecycleMonitor;
        private CertificateValidator? m_peerValidator;
        private CertificateValidator? m_userValidator;
        private CertificateValidator? m_httpsValidator;
        private bool m_disposed;

        /// <summary>
        /// Internal record for a registered trust list.
        /// </summary>
        private sealed record TrustListEntry(
            string TrustedStorePath,
            string? IssuerStorePath,
            string? StoreType);

        /// <summary>
        /// Initializes a new instance of the <see cref="CertificateManager"/> class.
        /// </summary>
        /// <param name="telemetry">
        /// The telemetry context used for logging and diagnostics.
        /// </param>
        /// <param name="storeProviders">
        /// An optional set of store providers. When <see langword="null"/>,
        /// the default directory and X.509 store providers are used.
        /// </param>
        /// <param name="maxRejectedCertificates">
        /// The maximum number of rejected certificates to keep in the
        /// rejected store. Defaults to 5.
        /// </param>
        /// <param name="expiryWarningThreshold">
        /// The time before expiry at which
        /// <see cref="CertificateChangeKind.CertificateExpiring"/> events
        /// are emitted. Defaults to 14 days.
        /// </param>
        public CertificateManager(
            ITelemetryContext telemetry,
            IEnumerable<ICertificateStoreProvider>? storeProviders = null,
            int maxRejectedCertificates = 5,
            TimeSpan? expiryWarningThreshold = null)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_logger = telemetry.CreateLogger<CertificateManager>();
            m_maxRejectedCertificates = maxRejectedCertificates;
            m_storeProviders = storeProviders?.ToList() ??
            [
                new DirectoryStoreProvider(),
                new X509StoreProvider()
            ];

            TimeSpan threshold = expiryWarningThreshold ?? TimeSpan.FromDays(14);
            m_lifecycleMonitor = new CertificateLifecycleMonitor(
                m_changeSubject,
                () => m_applicationCertificates,
                threshold,
                TimeSpan.FromHours(1),
                m_telemetry);
        }

        /// <inheritdoc/>
        public IReadOnlyCollection<TrustListIdentifier> TrustLists => m_trustLists.Keys;

        /// <inheritdoc/>
        public IObservable<CertificateChangeEvent> CertificateChanges => m_changeSubject;

        /// <inheritdoc/>
        public void RegisterTrustList(
            TrustListIdentifier trustList,
            string trustedStorePath,
            string? issuerStorePath = null)
        {
            if (trustList == null)
            {
                throw new ArgumentNullException(nameof(trustList));
            }

            if (string.IsNullOrEmpty(trustedStorePath))
            {
                throw new ArgumentException(
                    "Trusted store path must not be null or empty.",
                    nameof(trustedStorePath));
            }

            if (!m_trustLists.TryAdd(trustList, new TrustListEntry(
                    trustedStorePath, issuerStorePath, StoreType: null)))
            {
                m_logger.LogDebug(
                    "Trust list '{TrustList}' is already registered, skipping.",
                    trustList);
            }
        }

        /// <inheritdoc/>
        public ICertificateStore OpenTrustedStore(TrustListIdentifier trustList)
        {
            if (!m_trustLists.TryGetValue(trustList, out TrustListEntry? entry))
            {
                throw new KeyNotFoundException(
                    $"Trust list '{trustList}' is not registered.");
            }

            return OpenStore(entry.TrustedStorePath, entry.StoreType);
        }

        /// <inheritdoc/>
        public ICertificateStore? OpenIssuerStore(TrustListIdentifier trustList)
        {
            if (!m_trustLists.TryGetValue(trustList, out TrustListEntry? entry))
            {
                throw new KeyNotFoundException(
                    $"Trust list '{trustList}' is not registered.");
            }

            if (string.IsNullOrEmpty(entry.IssuerStorePath))
            {
                return null;
            }

            return OpenStore(entry.IssuerStorePath!, entry.StoreType);
        }

        /// <inheritdoc/>
        public Task<ITrustListTransaction> BeginUpdateAsync(
            TrustListIdentifier trustList,
            CancellationToken ct = default)
        {
            if (trustList == null)
            {
                throw new ArgumentNullException(nameof(trustList));
            }

            if (!m_trustLists.ContainsKey(trustList))
            {
                throw new KeyNotFoundException(
                    $"Trust list '{trustList}' is not registered.");
            }

            ITrustListTransaction transaction = new TrustListTransaction(this, trustList);
            return Task.FromResult(transaction);
        }

        /// <summary>
        /// Maps the stores defined in a <see cref="SecurityConfiguration"/>
        /// to named trust lists (Peers, Users, Https, Rejected).
        /// </summary>
        /// <param name="config">The security configuration to map from.</param>
        public void MapFromSecurityConfiguration(SecurityConfiguration config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (config.TrustedPeerCertificates != null)
            {
                RegisterTrustList(
                    TrustListIdentifier.Peers,
                    config.TrustedPeerCertificates.StorePath,
                    config.TrustedIssuerCertificates?.StorePath);
            }

            if (config.TrustedUserCertificates != null)
            {
                RegisterTrustList(
                    TrustListIdentifier.Users,
                    config.TrustedUserCertificates.StorePath,
                    config.UserIssuerCertificates?.StorePath);
            }

            if (config.TrustedHttpsCertificates != null)
            {
                RegisterTrustList(
                    TrustListIdentifier.Https,
                    config.TrustedHttpsCertificates.StorePath,
                    config.HttpsIssuerCertificates?.StorePath);
            }

            if (config.RejectedCertificateStore != null)
            {
                RegisterTrustList(
                    TrustListIdentifier.Rejected,
                    config.RejectedCertificateStore.StorePath);
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<CertificateEntry> ApplicationCertificates => m_applicationCertificates;

        /// <inheritdoc/>
        public CertificateEntry? GetApplicationCertificate(NodeId certificateType)
        {
            return m_applicationCertificates.FirstOrDefault(
                e => e.CertificateType == certificateType);
        }

        /// <inheritdoc/>
        public CertificateEntry? GetInstanceCertificate(string securityPolicyUri)
        {
            foreach (NodeId certType in CertificateIdentifier.MapSecurityPolicyToCertificateTypes(securityPolicyUri))
            {
                CertificateEntry? entry = GetApplicationCertificate(certType);
                if (entry != null)
                {
                    return entry;
                }
            }

            return m_applicationCertificates.Count > 0 ? m_applicationCertificates[0] : null;
        }

        /// <inheritdoc/>
        public byte[] GetEncodedChainBlob(string securityPolicyUri)
        {
            CertificateEntry? entry = GetInstanceCertificate(securityPolicyUri);
            return entry?.GetEncodedChainBlob() ?? [];
        }

        /// <summary>
        /// Loads application certificates from the security configuration.
        /// </summary>
        /// <param name="securityConfiguration">
        /// The security configuration containing the application certificates.
        /// </param>
        /// <param name="applicationUri">
        /// The application URI used to match certificates.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        public async Task LoadApplicationCertificatesAsync(
            SecurityConfiguration securityConfiguration,
            string? applicationUri = null,
            CancellationToken ct = default)
        {
            // Dispose existing entries before clearing the list.
            foreach (CertificateEntry oldEntry in m_applicationCertificates)
            {
                oldEntry.Dispose();
            }

            m_applicationCertificates.Clear();
            ArrayOf<CertificateIdentifier> appCerts = securityConfiguration.ApplicationCertificates;
            for (int i = 0; i < appCerts.Count; i++)
            {
                CertificateIdentifier certId = appCerts[i];
                using Certificate certificate = await certId.FindAsync(true, applicationUri, m_telemetry, ct)
                    .ConfigureAwait(false);
                if (certificate != null)
                {
                    m_applicationCertificates.Add(new CertificateEntry(
                        certificate,
                        new CertificateCollection(),
                        certId.CertificateType));
                }
            }
        }

        /// <inheritdoc/>
        public async Task<CertificateValidationResult> ValidateAsync(
            CertificateCollection chain,
            TrustListIdentifier? trustList = null,
            CertificateValidationOptions? options = null,
            CancellationToken ct = default)
        {
            trustList ??= TrustListIdentifier.Peers;
            CertificateValidator validator = GetOrCreateValidator(trustList);

            try
            {
                await validator.ValidateAsync(chain, ct).ConfigureAwait(false);
                return CertificateValidationResult.Success;
            }
            catch (ServiceResultException ex)
            {
                return new CertificateValidationResult(
                    false,
                    ex.StatusCode,
                    [ex.Result],
                    isSuppressible: false);
            }
        }

        /// <inheritdoc/>
        public Task<CertificateValidationResult> ValidateAsync(
            Certificate certificate,
            TrustListIdentifier? trustList = null,
            CancellationToken ct = default)
        {
            return ValidateAsync(
                new CertificateCollection { certificate.AddRef() },
                trustList,
                ct: ct);
        }

        /// <inheritdoc/>
        public Task UpdateApplicationCertificateAsync(
            NodeId certificateType,
            Certificate newCertificate,
            CertificateCollection? issuerChain = null,
            CancellationToken ct = default)
        {
            CertificateEntry? oldEntry = null;

            // Find and replace the existing entry.
            for (int i = 0; i < m_applicationCertificates.Count; i++)
            {
                if (m_applicationCertificates[i].CertificateType == certificateType)
                {
                    oldEntry = m_applicationCertificates[i];
                    m_applicationCertificates[i] = new CertificateEntry(
                        newCertificate,
                        issuerChain ?? new CertificateCollection(),
                        certificateType);
                    break;
                }
            }

            // If not found, add a new entry.
            if (oldEntry == null)
            {
                m_applicationCertificates.Add(new CertificateEntry(
                    newCertificate,
                    issuerChain ?? new CertificateCollection(),
                    certificateType));
            }

            // Invalidate cached validators.
            m_peerValidator = null;
            m_userValidator = null;
            m_httpsValidator = null;

            m_lifecycleMonitor?.Reset();

            m_changeSubject.Notify(new CertificateChangeEvent(
                CertificateChangeKind.ApplicationCertificateUpdated,
                TrustListIdentifier.Peers,
                certificateType,
                oldEntry?.Certificate,
                newCertificate,
                issuerChain));

            // Dispose the old entry after notification so observers
            // can still read the old certificate during the callback.
            oldEntry?.Dispose();

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task RejectCertificateAsync(
            CertificateCollection chain,
            CancellationToken ct = default)
        {
            m_rejectedProcessor ??= new RejectedCertificateProcessor(
                this, m_maxRejectedCertificates, m_telemetry);
            return m_rejectedProcessor.EnqueueAsync(chain, ct).AsTask();
        }

        /// <inheritdoc/>
        public async Task<TrustListData> ReadTrustListAsync(
            TrustListIdentifier trustList,
            TrustListMasks masks = TrustListMasks.All,
            CancellationToken ct = default)
        {
            if (trustList == null)
            {
                throw new ArgumentNullException(nameof(trustList));
            }

            var data = new TrustListData();

            if ((masks & TrustListMasks.TrustedCertificates) != 0)
            {
                using ICertificateStore store = OpenTrustedStore(trustList);
                data.TrustedCertificates = await store.EnumerateAsync(ct)
                    .ConfigureAwait(false);
            }

            if ((masks & TrustListMasks.TrustedCrls) != 0)
            {
                using ICertificateStore store = OpenTrustedStore(trustList);
                if (store.SupportsCRLs)
                {
                    data.TrustedCrls = await store.EnumerateCRLsAsync(ct)
                        .ConfigureAwait(false);
                }
            }

            ICertificateStore? issuerStore = OpenIssuerStore(trustList);
            if (issuerStore != null)
            {
                using (issuerStore)
                {
                    if ((masks & TrustListMasks.IssuerCertificates) != 0)
                    {
                        data.IssuerCertificates = await issuerStore
                            .EnumerateAsync(ct).ConfigureAwait(false);
                    }

                    if ((masks & TrustListMasks.IssuerCrls) != 0 &&
                        issuerStore.SupportsCRLs)
                    {
                        data.IssuerCrls = await issuerStore
                            .EnumerateCRLsAsync(ct).ConfigureAwait(false);
                    }
                }
            }

            return data;
        }

        /// <inheritdoc/>
        public async Task WriteTrustListAsync(
            TrustListIdentifier trustList,
            TrustListData data,
            TrustListMasks masks = TrustListMasks.All,
            CancellationToken ct = default)
        {
            if (trustList == null)
            {
                throw new ArgumentNullException(nameof(trustList));
            }

            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (((int)masks &
                ((int)TrustListMasks.TrustedCertificates | (int)TrustListMasks.TrustedCrls)) != 0)
            {
                using ICertificateStore store = OpenTrustedStore(trustList);

                if ((masks & TrustListMasks.TrustedCertificates) != 0)
                {
                    await ClearCertificatesAsync(store, ct)
                        .ConfigureAwait(false);

                    foreach (Certificate cert in data.TrustedCertificates)
                    {
                        await store.AddAsync(cert, ct: ct)
                            .ConfigureAwait(false);
                    }
                }

                if ((masks & TrustListMasks.TrustedCrls) != 0 &&
                    store.SupportsCRLs)
                {
                    await ClearCrlsAsync(store, ct).ConfigureAwait(false);

                    foreach (X509CRL crl in data.TrustedCrls)
                    {
                        await store.AddCRLAsync(crl, ct)
                            .ConfigureAwait(false);
                    }
                }
            }

            if (((int)masks &
                ((int)TrustListMasks.IssuerCertificates | (int)TrustListMasks.IssuerCrls)) != 0)
            {
                ICertificateStore? issuerStore = OpenIssuerStore(trustList);
                if (issuerStore != null)
                {
                    using (issuerStore)
                    {
                        if ((masks & TrustListMasks.IssuerCertificates) != 0)
                        {
                            await ClearCertificatesAsync(issuerStore, ct)
                                .ConfigureAwait(false);

                            foreach (Certificate cert in data.IssuerCertificates)
                            {
                                await issuerStore.AddAsync(cert, ct: ct)
                                    .ConfigureAwait(false);
                            }
                        }

                        if ((masks & TrustListMasks.IssuerCrls) != 0 &&
                            issuerStore.SupportsCRLs)
                        {
                            await ClearCrlsAsync(issuerStore, ct)
                                .ConfigureAwait(false);

                            foreach (X509CRL crl in data.IssuerCrls)
                            {
                                await issuerStore.AddCRLAsync(crl, ct)
                                    .ConfigureAwait(false);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Removes all certificates from the specified store.
        /// </summary>
        private static async Task ClearCertificatesAsync(
            ICertificateStore store,
            CancellationToken ct)
        {
            using CertificateCollection existing =
                await store.EnumerateAsync(ct).ConfigureAwait(false);

            foreach (Certificate cert in existing)
            {
                await store.DeleteAsync(cert.Thumbprint, ct)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Removes all CRLs from the specified store.
        /// </summary>
        private static async Task ClearCrlsAsync(
            ICertificateStore store,
            CancellationToken ct)
        {
            X509CRLCollection existing =
                await store.EnumerateCRLsAsync(ct).ConfigureAwait(false);

            foreach (X509CRL crl in existing)
            {
                await store.DeleteCRLAsync(crl, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!m_disposed)
            {
                m_lifecycleMonitor?.Dispose();
                m_changeSubject.Complete();

                if (m_rejectedProcessor != null)
                {
                    m_rejectedProcessor.DisposeAsync()
                        .AsTask().GetAwaiter().GetResult();
                }

                m_peerValidator = null;
                m_userValidator = null;
                m_httpsValidator = null;

                foreach (CertificateEntry entry in m_applicationCertificates)
                {
                    entry.Dispose();
                }

                m_applicationCertificates.Clear();

                m_trustLists.Clear();
                m_disposed = true;
            }
        }

        /// <summary>
        /// Gets or creates a <see cref="CertificateValidator"/> configured
        /// for the specified trust list.
        /// </summary>
        private CertificateValidator GetOrCreateValidator(TrustListIdentifier trustList)
        {
            // Return a cached validator for well-known trust lists.
            CertificateValidator? cached = GetCachedValidator(trustList);
            if (cached != null)
            {
                return cached;
            }

            var validator = new CertificateValidator(m_telemetry);

            if (m_trustLists.TryGetValue(trustList, out TrustListEntry? entry))
            {
                var trustedStore = new CertificateTrustList {
                    StorePath = entry.TrustedStorePath
                };

                CertificateTrustList? issuerStore = entry.IssuerStorePath != null
                    ? new CertificateTrustList { StorePath = entry.IssuerStorePath }
                    : null;

                CertificateStoreIdentifier? rejectedStore = null;
                if (m_trustLists.TryGetValue(
                        TrustListIdentifier.Rejected,
                        out TrustListEntry? rejectedEntry))
                {
                    rejectedStore = new CertificateStoreIdentifier(
                        rejectedEntry.TrustedStorePath);
                }

                validator.Update(issuerStore, trustedStore, rejectedStore);
            }

            // Cache well-known validators for reuse.
            if (trustList == TrustListIdentifier.Peers)
            {
                m_peerValidator = validator;
            }
            else if (trustList == TrustListIdentifier.Users)
            {
                m_userValidator = validator;
            }
            else if (trustList == TrustListIdentifier.Https)
            {
                m_httpsValidator = validator;
            }

            return validator;
        }

        /// <summary>
        /// Returns the cached validator for a well-known trust list,
        /// or <see langword="null"/> if none is cached yet.
        /// </summary>
        private CertificateValidator? GetCachedValidator(TrustListIdentifier trustList)
        {
            if (trustList == TrustListIdentifier.Peers)
            {
                return m_peerValidator;
            }

            if (trustList == TrustListIdentifier.Users)
            {
                return m_userValidator;
            }

            if (trustList == TrustListIdentifier.Https)
            {
                return m_httpsValidator;
            }

            return null;
        }

        /// <summary>
        /// Opens a certificate store at the given path, resolving the
        /// store type from the registered providers or falling back to
        /// <see cref="CertificateStoreIdentifier.CreateStore(string, ITelemetryContext)"/>.
        /// </summary>
        private ICertificateStore OpenStore(string storePath, string? storeType)
        {
            storeType ??= CertificateStoreIdentifier.DetermineStoreType(storePath);

            foreach (ICertificateStoreProvider provider in m_storeProviders)
            {
                if (string.Equals(
                        provider.StoreTypeName,
                        storeType,
                        StringComparison.Ordinal))
                {
                    ICertificateStore store = provider.CreateStore(m_telemetry);
                    store.Open(storePath);
                    return store;
                }
            }

            // Fallback to the existing factory method for custom store types.
            ICertificateStore fallbackStore =
                CertificateStoreIdentifier.CreateStore(storeType, m_telemetry);
            fallbackStore.Open(storePath);
            return fallbackStore;
        }
    }
}
