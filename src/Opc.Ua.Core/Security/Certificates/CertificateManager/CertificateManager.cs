// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Central certificate management implementation.
    /// Currently implements trust-list management; other interfaces
    /// will be added in subsequent phases.
    /// </summary>
    public sealed class CertificateManager : ICertificateManager, ICertificateStoreResolver, IDisposable, IAsyncDisposable
    {
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
        /// rejected store. Zero is unlimited; a negative value disables new
        /// additions and applies the backend's existing pruning behavior.
        /// Defaults to 5.
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
            : this(telemetry, storeProviders, maxRejectedCertificates, expiryWarningThreshold, null)
        {
        }

        /// <summary>
        /// Creates a certificate manager with the supplied
        /// <see cref="TimeProvider"/> used for expiry monitoring.
        /// </summary>
        public CertificateManager(
            ITelemetryContext telemetry,
            IEnumerable<ICertificateStoreProvider>? storeProviders,
            int maxRejectedCertificates,
            TimeSpan? expiryWarningThreshold,
            TimeProvider? timeProvider)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_logger = telemetry.CreateLogger<CertificateManager>();
            m_changeSubject = new CertificateChangeSubject(m_logger);
            m_maxRejectedCertificates = maxRejectedCertificates;
            m_storeProviders = storeProviders?.ToList() ??
                [
                    new DirectoryStoreProvider(),
                    new X509StoreProvider()
                ];
            m_timeProvider = timeProvider ?? TimeProvider.System;

            TimeSpan threshold = expiryWarningThreshold ?? TimeSpan.FromDays(14);
            m_lifecycleMonitor = new CertificateLifecycleMonitor(
                m_changeSubject,
                SnapshotApplicationCertificates,
                threshold,
                TimeSpan.FromHours(1),
                m_telemetry,
                m_timeProvider);

            m_certificateProvider = new CertificateProvider(m_telemetry, this);
        }

        /// <summary>
        /// Returns the centralised <see cref="ICertificateProvider"/>
        /// used to resolve private-key certificates by
        /// <see cref="CertificateIdentifier"/>. Backed by the manager's
        /// internal cache + the standard store pipeline; consumers that
        /// hold a <see cref="CertificateIdentifier"/> rather than a live
        /// <see cref="Certificate"/> reference should use this provider
        /// to materialise the certificate on demand.
        /// </summary>
        public ICertificateProvider CertificateProvider => m_certificateProvider;

        /// <inheritdoc/>
        public IReadOnlyCollection<TrustListIdentifier> TrustLists
        {
            get
            {
                lock (m_certificatesLock)
                {
                    return [.. m_trustLists.Keys];
                }
            }
        }

        /// <inheritdoc/>
        public IObservable<CertificateChangeEvent> CertificateChanges => m_changeSubject;

        /// <inheritdoc/>
        public ICertificateStore OpenCertificateStore(
            string storePath,
            string? storeType = null,
            bool noPrivateKeys = true)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(storePath))
            {
                throw new ArgumentException("Store path must not be null or empty.", nameof(storePath));
            }
            storeType ??= ResolveStoreType(storePath);

            ICertificateStore store = CertificateStoreIdentifier.CreateStore(storeType, m_telemetry, m_storeProviders);
            try
            {
                store.Open(storePath, noPrivateKeys);
                return store;
            }
            catch
            {
                store.Dispose();
                throw;
            }
        }

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

            RegisterOrReplaceTrustList(
                trustList,
                new CertificateTrustList
                {
                    StorePath = trustedStorePath,
                    StoreType = ResolveStoreType(trustedStorePath)
                },
                issuerStorePath != null
                    ? new CertificateTrustList
                    {
                        StorePath = issuerStorePath,
                        StoreType = ResolveStoreType(issuerStorePath)
                    }
                    : null,
                replaceExisting: false);
        }

        /// <inheritdoc/>
        public ICertificateStore OpenTrustedStore(TrustListIdentifier trustList)
        {
            TrustListEntry entry = GetTrustListEntry(trustList);

            CertificateTrustList? store = entry.TrustedStore;
            if (store == null || string.IsNullOrEmpty(store.StorePath))
            {
                throw ServiceResultException.ConfigurationError(
                    $"Trust list '{trustList}' has no configured trusted certificate store.");
            }

            return OpenStore(store.StorePath!, store.StoreType);
        }

        /// <inheritdoc/>
        public ICertificateStore? OpenIssuerStore(TrustListIdentifier trustList)
        {
            TrustListEntry entry = GetTrustListEntry(trustList);

            CertificateTrustList? store = entry.IssuerStore;
            if (store == null || string.IsNullOrEmpty(store.StorePath))
            {
                return null;
            }

            return OpenStore(store.StorePath!, store.StoreType);
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

            _ = GetTrustListEntry(trustList);

            ITrustListTransaction transaction = new TrustListTransaction(this, trustList, this);
            return Task.FromResult(transaction);
        }

        /// <summary>
        /// Maps the stores and explicit certificates defined in a <see cref="SecurityConfiguration"/>
        /// to named trust lists (Peers, Users, Https, Rejected).
        /// </summary>
        /// <param name="config">The security configuration to map from.</param>
        public void MapFromSecurityConfiguration(SecurityConfiguration config)
        {
            MapFromSecurityConfiguration(config, replaceExisting: false);
        }

        /// <summary>
        /// Maps the stores and explicit certificates defined in a <see cref="SecurityConfiguration"/>
        /// to named trust lists with optional replacement of existing
        /// entries.
        /// </summary>
        /// <param name="config">The security configuration to map from.</param>
        /// <param name="replaceExisting">
        /// When <see langword="true"/>, existing trust list entries are
        /// replaced with snapshots from <paramref name="config"/> (used by
        /// <see cref="UpdateAsync"/> to honour runtime trust-list path
        /// changes).
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException"></exception>
        private void MapFromSecurityConfiguration(SecurityConfiguration config, bool replaceExisting)
        {
            bool changed;
            lock (m_certificatesLock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(CertificateManager));
                }
                changed = MapSecurityConfigurationCore(config, replaceExisting);
            }
            if (changed)
            {
                InvalidateCores();
            }
        }

        /// <summary>
        /// Applies global validation flags and snapshots configured trust sources, reporting whether registration
        /// changed.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <c>null</c>.</exception>
        private bool MapSecurityConfigurationCore(SecurityConfiguration config, bool replaceExisting)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            m_sendCertificateChain = config.SendCertificateChain;

            // Snapshot global validation flags from the SecurityConfiguration so
            // that per-trust-list CertificateValidationCore instances created lazily
            // by GetOrCreateCore inherit them. Without this, the
            // ApplicationConfiguration-level flags (AutoAcceptUntrustedCertificates
            // etc.) are silently dropped and validation regresses to defaults.
            m_autoAcceptUntrustedCertificates = config.AutoAcceptUntrustedCertificates;
            m_rejectSHA1SignedCertificates = config.RejectSHA1SignedCertificates;
            m_rejectUnknownRevocationStatus = config.RejectUnknownRevocationStatus;
            if (config.MinimumCertificateKeySize > 0)
            {
                m_minimumCertificateKeySize = config.MinimumCertificateKeySize;
            }
            m_useValidatedCertificates = config.UseValidatedCertificates;

            // Propagate to any already-created cached cores so behavior
            // changes when MapFromSecurityConfiguration is called more than
            // once on the same manager.
            ApplyValidationFlagsToCachedCores();
            lock (m_certificatesLock)
            {
                foreach (CertificateValidationCore core in m_customCores.Values)
                {
                    ApplyValidationFlags(core);
                }
            }

            bool changed = RegisterTrustListCore(
                TrustListIdentifier.Peers,
                config.TrustedPeerCertificates,
                config.TrustedIssuerCertificates,
                replaceExisting);

            changed |= RegisterTrustListCore(
                TrustListIdentifier.Users,
                config.TrustedUserCertificates,
                config.UserIssuerCertificates,
                replaceExisting);

            changed |= RegisterTrustListCore(
                TrustListIdentifier.Https,
                config.TrustedHttpsCertificates,
                config.HttpsIssuerCertificates,
                replaceExisting);

            changed |= RegisterTrustListCore(
                TrustListIdentifier.Rejected,
                config.RejectedCertificateStore,
                issuerStore: null,
                replaceExisting);
            return changed;
        }

        /// <summary>
        /// Registers trust-list snapshots and invalidates cached validation cores when the registration changes.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        private void RegisterOrReplaceTrustList(
            TrustListIdentifier trustList,
            CertificateStoreIdentifier? trustedStore,
            CertificateTrustList? issuerStore,
            bool replaceExisting)
        {
            bool changed;
            lock (m_certificatesLock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(CertificateManager));
                }
                changed = RegisterTrustListCore(trustList, trustedStore, issuerStore, replaceExisting);
            }
            if (changed)
            {
                InvalidateCores();
            }
        }

        /// <summary>
        /// Adds, replaces, or removes a named trust list according to its configured stores and explicit certificates.
        /// </summary>
        private bool RegisterTrustListCore(
            TrustListIdentifier trustList,
            CertificateStoreIdentifier? trustedStore,
            CertificateTrustList? issuerStore,
            bool replaceExisting)
        {
            if (!HasTrustSource(trustedStore) && !HasTrustSource(issuerStore))
            {
                return replaceExisting && m_trustLists.Remove(trustList);
            }

            var entry = new TrustListEntry(
                CreateTrustListSnapshot(trustedStore),
                CreateTrustListSnapshot(issuerStore));

            if (replaceExisting)
            {
                m_trustLists[trustList] = entry;
                return true;
            }
            if (m_trustLists.TryAdd(trustList, entry))
            {
                return true;
            }
            if (m_logger.IsEnabled(LogLevel.Debug))
            {
                m_logger.CertificateManagerLogMessage0(trustList.ToString());
            }
            return false;
        }

        /// <summary>
        /// Copies trust-list metadata and resolves inferred store types using the manager's registered providers.
        /// </summary>
        private CertificateTrustList? CreateTrustListSnapshot(CertificateStoreIdentifier? store)
        {
            var snapshot = CertificateTrustList.CreateSnapshot(store);
            if (snapshot != null &&
                !string.IsNullOrEmpty(snapshot.StorePath) &&
                (string.IsNullOrEmpty(snapshot.StoreType) || snapshot.StoreType == CertificateStoreType.Directory))
            {
                // StorePath infers Directory without seeing injected providers.
                // Preserve named custom stores while resolving that fallback here.
                snapshot.StoreType = ResolveStoreType(snapshot.StorePath!);
            }
            return snapshot;
        }

        /// <summary>
        /// Resolves a path-specific store provider before allowing the directory-store fallback.
        /// </summary>
        private string ResolveStoreType(string storePath)
        {
            // The built-in directory provider is a catch-all, not a path-specific provider.
            return CertificateStoreIdentifier.DetermineStoreType(
                storePath,
                m_storeProviders.Where(static provider => provider is not DirectoryStoreProvider));
        }

        /// <summary>
        /// Determines whether a trust source supplies a store path or explicitly listed certificates.
        /// </summary>
        private static bool HasTrustSource(CertificateStoreIdentifier? store)
        {
            return store != null &&
                (!string.IsNullOrEmpty(store.StorePath) ||
                    (store is CertificateTrustList trustList && !trustList.TrustedCertificates.IsEmpty));
        }

        /// <inheritdoc/>
        public bool SendCertificateChain => m_sendCertificateChain;

        /// <summary>
        /// Gets or sets a value indicating whether to auto-accept untrusted
        /// peer certificates. When <see langword="true"/>, a fresh peer cert
        /// (with no chain errors) is accepted even if it is not present in
        /// the trusted-peer store.
        /// </summary>
        public bool AutoAcceptUntrustedCertificates
        {
            get => m_autoAcceptUntrustedCertificates;
            set
            {
                lock (m_certificatesLock)
                {
                    m_autoAcceptUntrustedCertificates = value;
                    ApplyValidationFlagsToCachedCores();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether to reject certificates
        /// signed with a SHA-1 hash.
        /// </summary>
        public bool RejectSHA1SignedCertificates
        {
            get => m_rejectSHA1SignedCertificates;
            set
            {
                lock (m_certificatesLock)
                {
                    m_rejectSHA1SignedCertificates = value;
                    ApplyValidationFlagsToCachedCores();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether to reject certificates
        /// whose revocation status cannot be determined.
        /// </summary>
        public bool RejectUnknownRevocationStatus
        {
            get => m_rejectUnknownRevocationStatus;
            set
            {
                lock (m_certificatesLock)
                {
                    m_rejectUnknownRevocationStatus = value;
                    ApplyValidationFlagsToCachedCores();
                }
            }
        }

        /// <summary>
        /// Gets or sets the maximum number of rejected certificates kept in
        /// the rejected-certificate store. Zero is unlimited; a negative value
        /// disables new additions and applies the backend's existing pruning behavior.
        /// </summary>
        public int MaxRejectedCertificates
        {
            get => Volatile.Read(ref m_maxRejectedCertificates);
            set
            {
                RejectedCertificateProcessor? processor;
                lock (m_certificatesLock)
                {
                    ThrowIfDisposed();
                    m_maxRejectedCertificates = value;
                    processor = m_rejectedProcessor;
                    processor?.SetMaxRejectedCertificates(value);
                }
                if (processor != null)
                {
                    // Actively re-apply the cap so existing entries are
                    // trimmed when the cap is lowered. The trim runs on
                    // the processor's background task and can be awaited
                    // via FlushRejectedAsync.
                    _ = processor.EnqueueTrimAsync().AsTask();
                }
            }
        }

        /// <inheritdoc/>
        public Func<Certificate, ServiceResult, bool>? AcceptError
        {
            get => Volatile.Read(ref m_acceptError);
            set => Volatile.Write(ref m_acceptError, value);
        }

        /// <inheritdoc/>
        public CertificateEntryCollection SnapshotApplicationCertificates()
        {
            lock (m_certificatesLock)
            {
                // The collection takes an independent owning handle on each
                // entry, so the caller can dispose the returned snapshot
                // without affecting the manager's own entries, and a concurrent
                // hot-update does not invalidate it.
                return new CertificateEntryCollection(m_applicationCertificates);
            }
        }

        /// <inheritdoc/>
        public CertificateEntry? AcquireApplicationCertificateByType(NodeId certificateType)
        {
            lock (m_certificatesLock)
            {
                return m_applicationCertificates
                    .FirstOrDefault(e => e.CertificateType == certificateType)?
                    .AddRef();
            }
        }

        /// <inheritdoc/>
        public CertificateEntry? AcquireApplicationCertificateBySecurityPolicy(string securityPolicyUri)
        {
            lock (m_certificatesLock)
            {
                foreach (NodeId certType in CertificateIdentifier.MapSecurityPolicyToCertificateTypes(securityPolicyUri))
                {
                    CertificateEntry? entry = m_applicationCertificates.FirstOrDefault(
                        e => e.CertificateType == certType);
                    if (entry != null)
                    {
                        return entry.AddRef();
                    }
                }

                return m_applicationCertificates.Count > 0
                    ? m_applicationCertificates[0].AddRef()
                    : null;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> GetIssuersAsync(
            Certificate certificate,
            IList<CertificateIssuerReference> issuers,
            CancellationToken ct = default)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            if (issuers == null)
            {
                throw new ArgumentNullException(nameof(issuers));
            }

            using CertificateValidationCore.Borrow borrow = GetOrCreateCore(TrustListIdentifier.Peers);
            return await borrow.Core.GetIssuersAsync(certificate, issuers, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Loads application certificates from the security configuration.
        /// </summary>
        /// <remarks>
        /// A private key loaded from a directory store is reused instead of
        /// decrypted again when the same identifier is loaded again (for
        /// example by configuration validation and then by server start) and
        /// no file in that store changed since: same names, sizes and last
        /// write times, checked on every call, for at most 30 seconds after
        /// the key was read. Keys from other store types are
        /// always reloaded, and so are all keys in
        /// <see cref="ReloadApplicationCertificatesAsync"/> and
        /// <see cref="UpdateAsync"/>. Issuer chains are always resolved again.
        /// </remarks>
        /// <param name="securityConfiguration">
        /// The security configuration containing the application certificates.
        /// </param>
        /// <param name="applicationUri">
        /// The application URI used to match certificates.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        public Task LoadApplicationCertificatesAsync(
            SecurityConfiguration securityConfiguration,
            string? applicationUri = null,
            CancellationToken ct = default)
        {
            return LoadApplicationCertificatesCoreAsync(
                securityConfiguration,
                applicationUri,
                reuseLoadedKeys: true,
                ct);
        }

        /// <summary>
        /// Loads the application certificates, optionally reusing private keys
        /// loaded earlier from an unchanged directory store.
        /// </summary>
        private async Task LoadApplicationCertificatesCoreAsync(
            SecurityConfiguration securityConfiguration,
            string? applicationUri,
            bool reuseLoadedKeys,
            CancellationToken ct)
        {
            ThrowIfDisposed();
            // Build the new entries OUTSIDE the lock (resolution is async and
            // may be slow on file I/O), then atomically swap inside the lock.
            ArrayOf<CertificateIdentifier> appCerts = securityConfiguration.ApplicationCertificates;
            ICertificatePasswordProvider? passwordProvider = securityConfiguration
                .CertificatePasswordProvider;
            var newEntries = new List<CertificateEntry>(appCerts.Count);
            var newLoadedKeys = new List<LoadedApplicationKey>(appCerts.Count);
            // Own-store snapshots taken before the first key of a store is
            // loaded; a key is only remembered for reuse when its store is
            // still unchanged after all keys were loaded.
            var storeSnapshots = new Dictionary<string, (CertificateIdentifier Id, CertificateStoreFileSnapshot? Snapshot)>(
                StringComparer.Ordinal);
            // Stores from which at least one key was reused.
            var reusedStores = new HashSet<string>(StringComparer.Ordinal);
            // Keys loaded before a certificate update completes must not be
            // remembered after it (see UpdateApplicationCertificateAsync).
            int keyGeneration;
            lock (m_certificatesLock)
            {
                keyGeneration = m_loadedKeysGeneration;
            }
            try
            {
                for (int i = 0; i < appCerts.Count; i++)
                {
                    CertificateIdentifier certId = appCerts[i];
                    string? storeKey = GetLoadedKeyStoreKey(certId);
                    CertificateStoreFileSnapshot? snapshot = null;
                    if (storeKey != null)
                    {
                        if (storeSnapshots.TryGetValue(storeKey, out (CertificateIdentifier Id, CertificateStoreFileSnapshot? Snapshot) known))
                        {
                            snapshot = known.Snapshot;
                        }
                        else
                        {
                            snapshot = CaptureOwnStoreSnapshot(certId);
                            storeSnapshots[storeKey] = (certId, snapshot);
                        }
                    }

                    string loadedKeyId = GetLoadedKeyId(certId, applicationUri);
                    LoadedApplicationKey? reused = reuseLoadedKeys && snapshot != null
                        ? TryAcquireLoadedKey(loadedKeyId, certId.Thumbprint, passwordProvider, snapshot)
                        : null;

                    Certificate? loaded;
                    if (reused != null)
                    {
                        reusedStores.Add(storeKey!);
                        newLoadedKeys.Add(reused);
                        loaded = reused.Certificate.AddRef();
                    }
                    else
                    {
                        // The resolver opens the identifier's store and applies
                        // post-rotation fallbacks (subject-null, then
                        // thumbprint-null, by applicationUri) so a freshly-pushed
                        // certificate is picked up even when the configured
                        // identifier's thumbprint still references the old cert.
                        loaded = await CertificateIdentifierResolver
                            .LoadPrivateKeyCoreAsync(
                                certId,
                                passwordProvider,
                                applicationUri,
                                m_telemetry,
                                this,
                                ct)
                            .ConfigureAwait(false);
                        if (loaded != null && storeKey != null && snapshot != null)
                        {
                            newLoadedKeys.Add(new LoadedApplicationKey(
                                loadedKeyId,
                                certId.Thumbprint,
                                storeKey,
                                passwordProvider,
                                snapshot,
                                m_timeProvider.GetUtcNow(),
                                loaded.AddRef()));
                        }
                    }

                    using Certificate? certificate = loaded;
                    if (certificate != null)
                    {
                        // Resolve the issuer chain so that servers configured
                        // with SendCertificateChain = true transmit the full
                        // chain. The leaf alone is registered when no issuers
                        // are found. (Regression #3896.)
                        using CertificateCollection issuerChain =
                            await ResolveIssuerChainAsync(certificate, ct)
                                .ConfigureAwait(false);
                        newEntries.Add(new CertificateEntry(
                            certificate,
                            issuerChain,
                            certId.CertificateType));
                    }
                }

                // Forget keys whose store changed while they were loaded.
                foreach (KeyValuePair<string, (CertificateIdentifier Id, CertificateStoreFileSnapshot? Snapshot)> store
                    in storeSnapshots)
                {
                    if (store.Value.Snapshot != null &&
                        !store.Value.Snapshot.Equals(CaptureOwnStoreSnapshot(store.Value.Id)))
                    {
                        if (reusedStores.Contains(store.Key))
                        {
                            // A key of this store was reused against the
                            // snapshot taken before the change, so the loaded
                            // set can mix old and new certificates. Load all
                            // keys from the stores again; the pending entries
                            // are disposed below.
                            await LoadApplicationCertificatesCoreAsync(
                                securityConfiguration,
                                applicationUri,
                                reuseLoadedKeys: false,
                                ct).ConfigureAwait(false);
                            return;
                        }
                        for (int i = newLoadedKeys.Count - 1; i >= 0; i--)
                        {
                            if (newLoadedKeys[i].StoreKey == store.Key)
                            {
                                newLoadedKeys[i].Dispose();
                                newLoadedKeys.RemoveAt(i);
                            }
                        }
                    }
                }

                List<CertificateEntry> oldEntries;
                List<LoadedApplicationKey> oldLoadedKeys;
                lock (m_certificatesLock)
                {
                    ThrowIfDisposed();
                    oldEntries = [.. m_applicationCertificates];
                    m_applicationCertificates.Clear();
                    m_applicationCertificates.AddRange(newEntries);
                    newEntries.Clear();
                    oldLoadedKeys = [.. m_loadedApplicationKeys];
                    m_loadedApplicationKeys.Clear();
                    if (keyGeneration == m_loadedKeysGeneration)
                    {
                        m_loadedApplicationKeys.AddRange(newLoadedKeys);
                        newLoadedKeys.Clear();
                    }
                }

                // Dispose the manager's own old entries OUTSIDE the lock.
                // Accessors hand out independent AddRef'd handles (never the
                // manager's own entries), so disposing these old entries here
                // cannot affect any handle a consumer is still holding.
                foreach (CertificateEntry oldEntry in oldEntries)
                {
                    oldEntry.Dispose();
                }
                foreach (LoadedApplicationKey oldLoadedKey in oldLoadedKeys)
                {
                    oldLoadedKey.Dispose();
                }
            }
            finally
            {
                // If we threw before the swap, dispose any partially-built
                // entries to avoid leaking ref-counted certs.
                foreach (CertificateEntry pending in newEntries)
                {
                    pending.Dispose();
                }
                foreach (LoadedApplicationKey pending in newLoadedKeys)
                {
                    pending.Dispose();
                }
            }
        }

        /// <summary>
        /// Returns the private-key certificate this manager loaded for
        /// <paramref name="identifier"/> when its directory store did not
        /// change since, or <see langword="null"/> when the key must be
        /// loaded from the store.
        /// </summary>
        /// <remarks>
        /// Lets the application certificate check reuse the keys that
        /// configuration validation loaded instead of decrypting them again.
        /// The result equals what
        /// <see cref="CertificateIdentifierResolver.LoadPrivateKeyWithStoreResolverAsync"/>
        /// would load with the same arguments and this manager as resolver.
        /// </remarks>
        /// <param name="identifier">The application certificate identifier.</param>
        /// <param name="passwordProvider">The private-key password provider.</param>
        /// <param name="applicationUri">The application URI used for rotation fallbacks.</param>
        /// <returns>An owned certificate reference, or <see langword="null"/>.</returns>
        internal Certificate? TryAcquireLoadedApplicationKey(
            CertificateIdentifier identifier,
            ICertificatePasswordProvider? passwordProvider,
            string? applicationUri)
        {
            if (identifier == null || GetLoadedKeyStoreKey(identifier) == null)
            {
                return null;
            }

            lock (m_certificatesLock)
            {
                if (m_disposed || m_loadedApplicationKeys.Count == 0)
                {
                    return null;
                }
            }

            CertificateStoreFileSnapshot? snapshot = CaptureOwnStoreSnapshot(identifier);
            if (snapshot == null)
            {
                return null;
            }

            using LoadedApplicationKey? loaded = TryAcquireLoadedKey(
                GetLoadedKeyId(identifier, applicationUri),
                identifier.Thumbprint,
                passwordProvider,
                snapshot);
            return loaded?.Certificate.AddRef();
        }

        /// <summary>
        /// Finds a remembered key for the lookup, password provider and
        /// unchanged store snapshot, and returns an owned copy of the record.
        /// </summary>
        /// <remarks>
        /// A lookup without a thumbprint only matches a key that was looked up
        /// without one. A lookup with a thumbprint matches a key whose
        /// certificate has that thumbprint: in an unchanged store every
        /// resolver pass that filters by the thumbprint can only return that
        /// certificate, and the same subject, application URI and type found
        /// it before. This covers the identifiers whose thumbprint the
        /// application certificate check fills in after the first load.
        /// </remarks>
        private LoadedApplicationKey? TryAcquireLoadedKey(
            string loadedKeyId,
            string? lookupThumbprint,
            ICertificatePasswordProvider? passwordProvider,
            CertificateStoreFileSnapshot currentSnapshot)
        {
            DateTimeOffset now = m_timeProvider.GetUtcNow();
            lock (m_certificatesLock)
            {
                if (m_disposed)
                {
                    return null;
                }
                foreach (LoadedApplicationKey loaded in m_loadedApplicationKeys)
                {
                    bool thumbprintMatches = string.IsNullOrEmpty(lookupThumbprint)
                        ? string.IsNullOrEmpty(loaded.LookupThumbprint)
                        : string.Equals(
                            loaded.Certificate.Thumbprint,
                            lookupThumbprint,
                            StringComparison.OrdinalIgnoreCase);
                    if (thumbprintMatches &&
                        string.Equals(loaded.Id, loadedKeyId, StringComparison.Ordinal) &&
                        ReferenceEquals(loaded.PasswordProvider, passwordProvider) &&
                        now - loaded.LoadedAt <= kLoadedKeyMaxAge &&
                        now >= loaded.LoadedAt &&
                        loaded.Snapshot.Equals(currentSnapshot))
                    {
                        return loaded.AddRef();
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Drops every remembered private key, so the next load reads the
        /// stores again.
        /// </summary>
        private void ForgetLoadedApplicationKeys()
        {
            List<LoadedApplicationKey> old;
            lock (m_certificatesLock)
            {
                old = DetachLoadedApplicationKeys();
            }
            foreach (LoadedApplicationKey loaded in old)
            {
                loaded.Dispose();
            }
        }

        /// <summary>
        /// Removes every remembered private key and makes loads that are
        /// still running not remember theirs. The caller holds
        /// <see cref="m_certificatesLock"/> and disposes the returned keys
        /// outside it.
        /// </summary>
        private List<LoadedApplicationKey> DetachLoadedApplicationKeys()
        {
            List<LoadedApplicationKey> old = [.. m_loadedApplicationKeys];
            m_loadedApplicationKeys.Clear();
            m_loadedKeysGeneration++;
            return old;
        }

        /// <summary>
        /// Identifies the store of an application certificate for key reuse,
        /// or returns <see langword="null"/> when keys from it are never
        /// reused (X509Store, no store path).
        /// </summary>
        private static string? GetLoadedKeyStoreKey(CertificateIdentifier identifier)
        {
            if (string.IsNullOrEmpty(identifier.StorePath) ||
                identifier.StoreType == CertificateStoreType.X509Store)
            {
                return null;
            }
            return ComposeKey(identifier.StoreType, identifier.StorePath);
        }

        /// <summary>
        /// Identifies a private-key lookup by everything the resolver uses
        /// except the thumbprint, which <see cref="TryAcquireLoadedKey"/>
        /// matches separately.
        /// </summary>
        private static string GetLoadedKeyId(CertificateIdentifier identifier, string? applicationUri)
        {
            return ComposeKey(
                identifier.StoreType,
                identifier.StorePath,
                identifier.SubjectName,
                identifier.CertificateType.ToString(),
                applicationUri);
        }

        /// <summary>
        /// Joins the parts so that different parts always give different
        /// keys: each part is prefixed with its length, so a separator inside
        /// a part (a subject name or path may contain any character) cannot
        /// make two lookups collide. A null part differs from an empty one.
        /// </summary>
        private static string ComposeKey(params string?[] parts)
        {
            var builder = new StringBuilder();
            foreach (string? part in parts)
            {
                if (part == null)
                {
                    builder.Append("-;");
                }
                else
                {
                    builder
                        .Append(part.Length.ToString(CultureInfo.InvariantCulture))
                        .Append(':')
                        .Append(part)
                        .Append(';');
                }
            }
            return builder.ToString();
        }

        /// <summary>
        /// Captures the file snapshot of the identifier's own store when it
        /// is a directory store served by the built-in
        /// <see cref="DirectoryStoreProvider"/>, without opening the store.
        /// Any other store (X509Store, PKCS#11, custom providers) returns
        /// <see langword="null"/>, so its keys are always loaded from the store.
        /// </summary>
        private CertificateStoreFileSnapshot? CaptureOwnStoreSnapshot(CertificateIdentifier identifier)
        {
            if (GetLoadedKeyStoreKey(identifier) == null)
            {
                return null;
            }
            try
            {
                string storeType = string.IsNullOrEmpty(identifier.StoreType)
                    ? ResolveStoreType(identifier.StorePath!)
                    : identifier.StoreType!;
                if (storeType != CertificateStoreType.Directory)
                {
                    return null;
                }

                // CertificateStoreIdentifier.CreateStore uses the first
                // provider registered for the type name.
                ICertificateStoreProvider? provider = m_storeProviders.FirstOrDefault(
                    static p => p.StoreTypeName == CertificateStoreType.Directory);
                if (provider != null && provider is not DirectoryStoreProvider)
                {
                    return null;
                }

                string? path = Utils.ReplaceSpecialFolderNames(identifier.StorePath);
                if (string.IsNullOrEmpty(path))
                {
                    return null;
                }
                return CertificateStoreFileSnapshot.TryCapture(new System.IO.DirectoryInfo(path));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// Resolves the issuer chain for an application certificate from the
        /// configured trusted and issuer stores.
        /// </summary>
        /// <remarks>
        /// Returns an owned <see cref="CertificateCollection"/> (never
        /// <c>null</c>, possibly empty) that the caller must dispose. Every
        /// resolved issuer is included irrespective of trust state: the
        /// boolean returned by <see cref="GetIssuersAsync(Certificate, IList{CertificateIssuerReference}, CancellationToken)"/>
        /// reports whether the issuer is <em>trusted</em> (only true when the
        /// issuer is in the trusted store), not whether it was resolved, so it
        /// is deliberately ignored — a server must send its chain even when the
        /// issuing CA lives in the issuer store. Resolution failures are logged
        /// and swallowed so they never block certificate registration / server
        /// startup (a leaf-only chain is returned instead).
        /// </remarks>
        /// <param name="certificate">The application (leaf) certificate.</param>
        /// <param name="ct">Cancellation token.</param>
        private async Task<CertificateCollection> ResolveIssuerChainAsync(
            Certificate certificate,
            CancellationToken ct)
        {
            var issuerChain = new CertificateCollection();
            var issuerReferences = new List<CertificateIssuerReference>();
            try
            {
                await GetIssuersAsync(certificate, issuerReferences, ct)
                    .ConfigureAwait(false);

                foreach (CertificateIssuerReference issuerReference in issuerReferences)
                {
                    issuerChain.Add(issuerReference.Certificate);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Propagate caller-requested cancellation so shutdown / abort
                // stays responsive; only genuine resolution failures are
                // swallowed below. Dispose the (empty) chain to avoid a leak.
                issuerChain.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                m_logger.CertificateManagerLogMessage1(ex, certificate);
            }
            finally
            {
                // GetIssuersAsync hands back caller-owned references; the chain
                // collection took its own AddRef in Add, so release ours here.
                foreach (CertificateIssuerReference issuerReference in issuerReferences)
                {
                    issuerReference.Certificate.Dispose();
                }
            }

            return issuerChain;
        }

        /// <inheritdoc/>
        public async Task<CertificateValidationResult> ValidateAsync(
            CertificateCollection chain,
            TrustListIdentifier? trustList = null,
            Security.Certificates.CertificateValidationOptions? options = null,
            CancellationToken ct = default)
        {
            trustList ??= TrustListIdentifier.Peers;
            bool useCache = options == null ||
                (options.AcceptError == null &&
                options.AutoAcceptUntrustedCertificates == null &&
                options.RejectSHA1SignedCertificates == null &&
                options.RejectUnknownRevocationStatus == null &&
                options.MinimumCertificateKeySize == null &&
                options.TrustScope == null &&
                !options.ValidateForSigning);
            using CertificateValidationCore.Borrow borrow = GetOrCreateCore(
                trustList, useCache, options);

            // Per-call AcceptError takes precedence over the global hook.
            Func<Certificate, ServiceResult, bool>? acceptError =
                options?.TrustScope != null
                    ? options.AcceptError ?? (static (_, _) => false)
                    : options?.AcceptError ?? m_acceptError;

            CertificateValidationResult result = await borrow.Core
                .ValidateAsync(chain, acceptError, options, ct)
                .ConfigureAwait(false);

            if (!result.IsValid &&
                options?.RecordRejectedCertificates != false &&
                chain != null &&
                chain.Count > 0 &&
                TryGetRejectedProcessor() is { } processor)
            {
                // The core does not own a rejected-store writer; the manager
                // is responsible for enqueuing failed chains on the shared
                // RejectedCertificateProcessor. CertificateCollection.Add
                // AddRef's each cert; the processor disposes the chain after
                // processing, balancing the AddRef.
                using var rejectedChain = new CertificateCollection();
                foreach (Certificate c in chain)
                {
                    rejectedChain.Add(c);
                }
                await processor.EnqueueAsync(rejectedChain, ct)
                    .ConfigureAwait(false);
            }

            return result;
        }

        /// <inheritdoc/>
        public async Task<CertificateValidationResult> ValidateAsync(
            Certificate certificate,
            TrustListIdentifier? trustList = null,
            CancellationToken ct = default)
        {
            using var chain = new CertificateCollection { certificate };
            return await ValidateAsync(
                chain,
                trustList,
                ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Validates that the application URI in <paramref name="serverCertificate"/>
        /// matches the application URI in the endpoint description. Failed
        /// certificates are enqueued on the rejected-certificate processor.
        /// </summary>
        /// <param name="serverCertificate">The server certificate.</param>
        /// <param name="endpoint">The endpoint used to connect.</param>
        /// <exception cref="ArgumentNullException">
        /// When <paramref name="serverCertificate"/> or
        /// <paramref name="endpoint"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadCertificateUriInvalid"/>
        /// when the application URI cannot be found in the certificate.
        /// </exception>
        public void ValidateApplicationUri(
            Certificate serverCertificate,
            ConfiguredEndpoint endpoint)
        {
            if (serverCertificate == null)
            {
                throw new ArgumentNullException(nameof(serverCertificate));
            }
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            using CertificateValidationCore.Borrow borrow = GetOrCreateCore(TrustListIdentifier.Peers);
            try
            {
                borrow.Core.ValidateApplicationUri(serverCertificate, endpoint, m_acceptError);
            }
            catch (ServiceResultException)
            {
                EnqueueRejectedCertificate(serverCertificate);
                throw;
            }
        }

        /// <summary>
        /// Validates that the endpoint URL host appears in
        /// <paramref name="serverCertificate"/>'s domain list. Failed
        /// certificates are enqueued on the rejected-certificate processor
        /// (client-side checks only; server-side validations are not
        /// recorded as rejected).
        /// </summary>
        /// <param name="serverCertificate">The server certificate.</param>
        /// <param name="endpoint">The endpoint used to connect.</param>
        /// <param name="serverValidation">
        /// Whether this is a server-side validation (changes how the failure
        /// is logged and skips rejected-store enqueue).
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// When <paramref name="serverCertificate"/> or
        /// <paramref name="endpoint"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadCertificateHostNameInvalid"/>
        /// when the endpoint URL host is not listed in the certificate.
        /// </exception>
        public void ValidateDomains(
            Certificate serverCertificate,
            ConfiguredEndpoint endpoint,
            bool serverValidation = false)
        {
            if (serverCertificate == null)
            {
                throw new ArgumentNullException(nameof(serverCertificate));
            }
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            using CertificateValidationCore.Borrow borrow = GetOrCreateCore(TrustListIdentifier.Peers);
            try
            {
                borrow.Core.ValidateDomains(
                    serverCertificate,
                    endpoint,
                    serverValidation,
                    m_acceptError);
            }
            catch (ServiceResultException)
            {
                if (!serverValidation)
                {
                    EnqueueRejectedCertificate(serverCertificate);
                }
                throw;
            }
        }

        /// <summary>
        /// Enqueues a single certificate on the rejected-certificate
        /// processor without taking ownership of the certificate's
        /// reference count. The processor disposes the chain it consumes,
        /// balancing the per-cert AddRef performed by
        /// <see cref="CertificateCollection.Add"/>.
        /// </summary>
        private void EnqueueRejectedCertificate(Certificate certificate)
        {
            RejectedCertificateProcessor? processor = TryGetRejectedProcessor();
            if (processor == null)
            {
                return;
            }
            using var rejected = new CertificateCollection { certificate };
            // Fire-and-forget: the processor handles failures internally.
            _ = processor.EnqueueAsync(rejected).AsTask();
        }

        /// <summary>
        /// Acquires the single rejected-store writer without allowing creation after disposal begins.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The certificate manager is shutting down.</exception>
        private RejectedCertificateProcessor GetRejectedProcessor()
        {
            return TryGetRejectedProcessor() ??
                throw new ObjectDisposedException(nameof(CertificateManager));
        }

        /// <summary>
        /// Skips best-effort rejected recording when an admitted validation completes during shutdown.
        /// </summary>
        private RejectedCertificateProcessor? TryGetRejectedProcessor()
        {
            lock (m_certificatesLock)
            {
                if (m_disposed)
                {
                    return null;
                }
                return m_rejectedProcessor ??= new RejectedCertificateProcessor(
                    this, m_maxRejectedCertificates, m_telemetry);
            }
        }

        /// <inheritdoc/>
        public async Task UpdateApplicationCertificateAsync(
            NodeId certificateType,
            Certificate newCertificate,
            CertificateCollection? issuerChain = null,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            // When the caller does not supply a chain (e.g. the GDS push /
            // rotation flow), resolve it from the configured stores so the
            // replaced certificate also emits its full chain immediately
            // rather than waiting for the next reload (regression #3896).
            // Resolution must happen outside the lock.
            using CertificateCollection? resolvedChain = issuerChain == null
                ? await ResolveIssuerChainAsync(newCertificate, ct).ConfigureAwait(false)
                : null;
            CertificateCollection effectiveChain = issuerChain ?? resolvedChain!;

            CertificateEntry? oldEntry = null;
            List<LoadedApplicationKey> forgottenKeys;
            CertificateValidationCore? oldPeer;
            CertificateValidationCore? oldUser;
            CertificateValidationCore? oldHttps;

            lock (m_certificatesLock)
            {
                ThrowIfDisposed();
                // A replaced certificate invalidates every remembered key, in
                // the same critical section, so no load can make a key
                // available again after the replacement: the next load reads
                // the stores again.
                forgottenKeys = DetachLoadedApplicationKeys();

                // Find and replace the existing entry.
                for (int i = 0; i < m_applicationCertificates.Count; i++)
                {
                    if (m_applicationCertificates[i].CertificateType == certificateType)
                    {
                        oldEntry = m_applicationCertificates[i];
                        m_applicationCertificates[i] = new CertificateEntry(
                            newCertificate,
                            effectiveChain,
                            certificateType);
                        break;
                    }
                }

                // If not found, add a new entry.
                if (oldEntry == null)
                {
                    m_applicationCertificates.Add(new CertificateEntry(
                        newCertificate,
                        effectiveChain,
                        certificateType));
                }

                // Conservative invalidation: drop all cached validation cores so
                // subsequent validations pick up trust-list / issuer-chain changes
                // that GDS push flows typically apply alongside the app-cert
                // replacement (e.g. adding the new issuer to the trusted store).
                // See InvalidateCoreForCertificateType for the per-type variant
                // intended for callers that can guarantee no concurrent trust
                // list changes.
                oldPeer = m_peerCore;
                m_peerCore = null;
                oldUser = m_userCore;
                m_userCore = null;
                oldHttps = m_httpsCore;
                m_httpsCore = null;
            }

            // Dispose orphaned cores OUTSIDE the lock.
            oldPeer?.Dispose();
            oldUser?.Dispose();
            oldHttps?.Dispose();
            // Dispose the forgotten keys OUTSIDE the lock.
            foreach (LoadedApplicationKey forgotten in forgottenKeys)
            {
                forgotten.Dispose();
            }

            m_lifecycleMonitor?.Reset();

            m_changeSubject.Notify(new CertificateChangeEvent(
                CertificateChangeKind.ApplicationCertificateUpdated,
                TrustListIdentifier.Peers,
                certificateType,
                oldEntry?.Certificate,
                newCertificate,
                effectiveChain));

            // Dispose the old entry after notification so observers
            // can still read the old certificate during the callback.
            oldEntry?.Dispose();
        }

        /// <inheritdoc/>
        public Task RejectCertificateAsync(
            CertificateCollection chain,
            CancellationToken ct = default)
        {
            return GetRejectedProcessor().EnqueueAsync(chain, ct).AsTask();
        }

        /// <inheritdoc/>
        public async Task ReloadApplicationCertificatesAsync(
            SecurityConfiguration securityConfiguration,
            string? applicationUri = null,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            // Snapshot the previous primary entry (if any) so we can fire a
            // CertificateChange notification once the reload completes.
            CertificateValidationCore? oldPeer;
            CertificateValidationCore? oldUser;
            CertificateValidationCore? oldHttps;
            using CertificateEntry? oldPrimary = AcquirePrimaryCertificate();

            // An explicit reload always reads every key from its store.
            await LoadApplicationCertificatesCoreAsync(
                    securityConfiguration,
                    applicationUri,
                    reuseLoadedKeys: false,
                    ct)
                .ConfigureAwait(false);

            lock (m_certificatesLock)
            {
                // Conservative invalidation: drop all cached cores so subsequent
                // validations pick up any trust-list / cert / issuer-chain changes
                // implicit in the reload. See InvalidateCoreForCertificateType for
                // the per-type variant intended for callers that can guarantee no
                // concurrent trust list changes.
                oldPeer = m_peerCore;
                m_peerCore = null;
                oldUser = m_userCore;
                m_userCore = null;
                oldHttps = m_httpsCore;
                m_httpsCore = null;
            }

            // Dispose orphaned cores OUTSIDE the lock.
            oldPeer?.Dispose();
            oldUser?.Dispose();
            oldHttps?.Dispose();

            m_lifecycleMonitor?.Reset();

            using CertificateEntry? newPrimary = AcquirePrimaryCertificate();
            if (newPrimary != null)
            {
                m_changeSubject.Notify(new CertificateChangeEvent(
                    CertificateChangeKind.ApplicationCertificateUpdated,
                    TrustListIdentifier.Peers,
                    newPrimary.CertificateType,
                    oldPrimary?.Certificate,
                    newPrimary.Certificate,
                    newPrimary.IssuerChain));
            }
        }

        /// <inheritdoc/>
        public async Task UpdateAsync(
            SecurityConfiguration securityConfiguration,
            string? applicationUri = null,
            CancellationToken ct = default)
        {
            if (securityConfiguration == null)
            {
                throw new ArgumentNullException(nameof(securityConfiguration));
            }

            // Replace trust-list snapshots and validation flags, including
            // explicit entries and the removal of previously configured sources.
            MapFromSecurityConfiguration(securityConfiguration, replaceExisting: true);

            // Reload the registry from the underlying stores. The reload
            // routes through CertificateIdentifierResolver.LoadPrivateKeyAsync,
            // which carries the post-rotation fallbacks (subject-null then
            // thumbprint-null by applicationUri). That makes a fresh GDS
            // push picked up even when the configured identifier's
            // thumbprint still references the old cert — no separate cache-
            // invalidation step on the identifier is needed.
            await ReloadApplicationCertificatesAsync(securityConfiguration, applicationUri, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public Task FlushRejectedAsync(CancellationToken ct = default)
        {
            // Only the manager-owned RejectedCertificateProcessor is used now;
            // the per-trust-list validation cores no longer own writer queues.
            return Volatile.Read(ref m_rejectedProcessor)?.WaitForDrainAsync()
                ?? Task.CompletedTask;
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

            if (((int)masks & (int)TrustListMasks.TrustedCertificates) != 0)
            {
                using ICertificateStore store = OpenTrustedStore(trustList);
                data.TrustedCertificates = await store.EnumerateAsync(ct)
                    .ConfigureAwait(false);
            }

            if (((int)masks & (int)TrustListMasks.TrustedCrls) != 0)
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
                    if (((int)masks & (int)TrustListMasks.IssuerCertificates) != 0)
                    {
                        data.IssuerCertificates = await issuerStore
                            .EnumerateAsync(ct).ConfigureAwait(false);
                    }

                    if (((int)masks & (int)TrustListMasks.IssuerCrls) != 0 &&
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

            bool trustChanged = false;
            bool crlChanged = false;

            try
            {
                if (((int)masks &
                    ((int)TrustListMasks.TrustedCertificates | (int)TrustListMasks.TrustedCrls)) != 0)
                {
                    using ICertificateStore store = OpenTrustedStore(trustList);

                    if (((int)masks & (int)TrustListMasks.TrustedCertificates) != 0)
                    {
                        trustChanged = true;
                        await ClearCertificatesAsync(store, ct)
                            .ConfigureAwait(false);

                        foreach (Certificate cert in data.TrustedCertificates)
                        {
                            await store.AddAsync(cert, ct: ct)
                                .ConfigureAwait(false);
                        }
                    }

                    if (((int)masks & (int)TrustListMasks.TrustedCrls) != 0 &&
                        store.SupportsCRLs)
                    {
                        crlChanged = true;
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
                            if (((int)masks & (int)TrustListMasks.IssuerCertificates) != 0)
                            {
                                trustChanged = true;
                                await ClearCertificatesAsync(issuerStore, ct)
                                    .ConfigureAwait(false);

                                foreach (Certificate cert in data.IssuerCertificates)
                                {
                                    await issuerStore.AddAsync(cert, ct: ct)
                                        .ConfigureAwait(false);
                                }
                            }

                            if (((int)masks & (int)TrustListMasks.IssuerCrls) != 0 &&
                                issuerStore.SupportsCRLs)
                            {
                                crlChanged = true;
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
            finally
            {
                NotifyTrustListChanged(trustList, trustChanged, crlChanged);
            }
        }

        /// <summary>
        /// Invalidates cached validation cores for the affected trust
        /// list and dispatches <see cref="CertificateChangeKind.TrustListUpdated"/>
        /// and/or <see cref="CertificateChangeKind.CrlUpdated"/> events.
        /// Called by <see cref="WriteTrustListAsync"/>,
        /// <see cref="TrustListTransaction.CommitAsync"/>, and writers that
        /// modify backing stores directly. A failed write can also trigger a
        /// notification if it may have changed a store. No-op when no change
        /// kind is set.
        /// </summary>
        public void NotifyTrustListChanged(
            TrustListIdentifier trustList,
            bool trustChanged,
            bool crlChanged)
        {
            if (!trustChanged && !crlChanged)
            {
                return;
            }

            // Drop cached validation cores so the next ValidateAsync
            // pick up the fresh trust list / CRL state. We invalidate
            // all three well-known roles AND every custom trust-list core
            // defensively — trust list changes can affect peer, user and
            // HTTPS validators alike, and a change announced for a custom
            // trust list must not leave its cached core validating against
            // stale trust material.
            InvalidateCores();

            if (trustChanged)
            {
                m_changeSubject.Notify(new CertificateChangeEvent(
                    CertificateChangeKind.TrustListUpdated,
                    trustList,
                    CertificateType: null,
                    OldCertificate: null,
                    NewCertificate: null,
                    IssuerChain: null));
            }

            if (crlChanged)
            {
                m_changeSubject.Notify(new CertificateChangeEvent(
                    CertificateChangeKind.CrlUpdated,
                    trustList,
                    CertificateType: null,
                    OldCertificate: null,
                    NewCertificate: null,
                    IssuerChain: null));
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
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            CertificateValidationCore[] cores;
            CertificateValidationCore? peer;
            CertificateValidationCore? user;
            CertificateValidationCore? https;
            RejectedCertificateProcessor? rejectedProcessor;
            lock (m_certificatesLock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                rejectedProcessor = m_rejectedProcessor;
                cores = [.. m_customCores.Values];
                peer = m_peerCore;
                user = m_userCore;
                https = m_httpsCore;
                m_peerCore = null;
                m_userCore = null;
                m_httpsCore = null;
                m_customCores.Clear();
            }
            m_lifecycleMonitor?.Dispose();
            m_changeSubject.Complete();
            peer?.Dispose();
            user?.Dispose();
            https?.Dispose();
            foreach (CertificateValidationCore core in cores)
            {
                core.Dispose();
            }
            await Task.WhenAll(cores.Select(core => core.Disposal)).ConfigureAwait(false);
            if (peer != null)
            {
                await peer.Disposal.ConfigureAwait(false);
            }
            if (user != null)
            {
                await user.Disposal.ConfigureAwait(false);
            }
            if (https != null)
            {
                await https.Disposal.ConfigureAwait(false);
            }
            if (rejectedProcessor != null)
            {
                await rejectedProcessor.DisposeAsync().ConfigureAwait(false);
            }

            m_certificateProvider.Dispose();
            CertificateEntry[] certificates;
            LoadedApplicationKey[] loadedKeys;
            lock (m_certificatesLock)
            {
                certificates = [.. m_applicationCertificates];
                m_applicationCertificates.Clear();
                loadedKeys = [.. m_loadedApplicationKeys];
                m_loadedApplicationKeys.Clear();
                m_trustLists.Clear();
            }
            foreach (CertificateEntry entry in certificates)
            {
                entry.Dispose();
            }
            foreach (LoadedApplicationKey loadedKey in loadedKeys)
            {
                loadedKey.Dispose();
            }
        }

        /// <summary>
        /// Drops every cached validation core so the next validation rebuilds it
        /// from the current trust-list configuration.
        /// </summary>
        /// <remarks>
        /// A core snapshots its trust list when it is built, so anything that
        /// changes that material - a trust list re-registered, a store updated,
        /// an application certificate replaced - has to evict the cores as well.
        /// The fields are cleared under the lock and the cores released outside
        /// it; a validation already running keeps its own reference and finishes
        /// against the trust material it started with.
        /// </remarks>
        private void InvalidateCores()
        {
            CertificateValidationCore? oldPeer;
            CertificateValidationCore? oldUser;
            CertificateValidationCore? oldHttps;
            CertificateValidationCore[] oldCustom;

            lock (m_certificatesLock)
            {
                oldPeer = m_peerCore;
                m_peerCore = null;
                oldUser = m_userCore;
                m_userCore = null;
                oldHttps = m_httpsCore;
                m_httpsCore = null;
                oldCustom = [.. m_customCores.Values];
                m_customCores.Clear();
            }

            oldPeer?.Dispose();
            oldUser?.Dispose();
            oldHttps?.Dispose();
            foreach (CertificateValidationCore core in oldCustom)
            {
                core.Dispose();
            }
        }

        /// <summary>
        /// Gets or creates a <see cref="CertificateValidationCore"/> configured
        /// for the specified trust list.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        private CertificateValidationCore.Borrow GetOrCreateCore(
            TrustListIdentifier trustList,
            bool useCache = true,
            Security.Certificates.CertificateValidationOptions? options = null)
        {
            lock (m_certificatesLock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(CertificateManager));
                }
                // Per-call acceptance must neither consume nor populate the
                // cache used by normal connection validation.
                CertificateValidationCore? core = useCache ? GetCachedCore(trustList) : null;
                if (core == null)
                {
                    core = new CertificateValidationCore(
                        m_telemetry,
                        store => string.IsNullOrEmpty(store.StorePath)
                            ? null
                            : OpenStore(store.StorePath!, store.StoreType),
                        static certificate => certificate.AsX509Certificate2(),
                        m_timeProvider);
                    try
                    {
                        if (options?.TrustScope is { } scope)
                        {
                            core.Update(scope);
                        }
                        else if (m_trustLists.TryGetValue(trustList, out TrustListEntry? entry))
                        {
                            core.Update(entry.IssuerStore, entry.TrustedStore, rejectedCertificateStore: null);
                        }
                        ApplyValidationFlags(core, options);
                        if (useCache)
                        {
                            ReplaceCachedCore(trustList, core);
                        }
                    }
                    catch
                    {
                        core.Dispose();
                        throw;
                    }
                }
                CertificateValidationCore.Borrow borrow = core.AcquireBorrow();
                if (!useCache)
                {
                    core.Dispose();
                }
                return borrow;
            }
        }

        /// <summary>
        /// Retrieves a registered trust-list snapshot or reports that the requested list is unknown.
        /// </summary>
        /// <exception cref="KeyNotFoundException"></exception>
        private TrustListEntry GetTrustListEntry(TrustListIdentifier trustList)
        {
            lock (m_certificatesLock)
            {
                return m_trustLists.TryGetValue(trustList, out TrustListEntry? entry)
                    ? entry
                    : throw new KeyNotFoundException($"Trust list '{trustList}' is not registered.");
            }
        }

        /// <summary>
        /// Installs a freshly built core for a trust list, replacing whatever was
        /// cached. The caller must hold <see cref="m_certificatesLock"/>.
        /// </summary>
        private void ReplaceCachedCore(
            TrustListIdentifier trustList,
            CertificateValidationCore core)
        {
            if (trustList == TrustListIdentifier.Peers)
            {
                m_peerCore = core;
            }
            else if (trustList == TrustListIdentifier.Users)
            {
                m_userCore = core;
            }
            else if (trustList == TrustListIdentifier.Https)
            {
                m_httpsCore = core;
            }
            else
            {
                m_customCores[trustList] = core;
            }
        }

        /// <summary>
        /// Applies the global validation flags captured from the
        /// SecurityConfiguration to a (possibly already-created) core.
        /// Safe to call with a null core.
        /// </summary>
        private void ApplyValidationFlags(
            CertificateValidationCore? core,
            Security.Certificates.CertificateValidationOptions? options = null)
        {
            if (core == null)
            {
                return;
            }

            core.AutoAcceptUntrustedCertificates =
                options?.AutoAcceptUntrustedCertificates ??
                (options?.TrustScope == null && m_autoAcceptUntrustedCertificates);
            core.RejectSHA1SignedCertificates =
                options?.RejectSHA1SignedCertificates ?? m_rejectSHA1SignedCertificates;
            core.RejectUnknownRevocationStatus =
                options?.RejectUnknownRevocationStatus ?? m_rejectUnknownRevocationStatus;
            ushort minimumKeySize = options?.MinimumCertificateKeySize ?? m_minimumCertificateKeySize;
            if (minimumKeySize > 0)
            {
                core.MinimumCertificateKeySize = minimumKeySize;
            }
            core.UseValidatedCertificates = m_useValidatedCertificates;
        }

        private void ApplyValidationFlagsToCachedCores()
        {
            ApplyValidationFlags(m_peerCore);
            ApplyValidationFlags(m_userCore);
            ApplyValidationFlags(m_httpsCore);
            foreach (CertificateValidationCore core in m_customCores.Values)
            {
                ApplyValidationFlags(core);
            }
        }

        /// <summary>
        /// Returns the cached core for a well-known trust list, a
        /// previously cached custom trust list, or <see langword="null"/>
        /// if none is cached yet.
        /// </summary>
        private CertificateValidationCore? GetCachedCore(TrustListIdentifier trustList)
        {
            if (trustList == TrustListIdentifier.Peers)
            {
                return m_peerCore;
            }

            if (trustList == TrustListIdentifier.Users)
            {
                return m_userCore;
            }

            if (trustList == TrustListIdentifier.Https)
            {
                return m_httpsCore;
            }

            lock (m_certificatesLock)
            {
                return m_customCores.TryGetValue(trustList, out CertificateValidationCore? core)
                    ? core
                    : null;
            }
        }

        /// <summary>
        /// Opens a certificate store at the given path, resolving the
        /// store type from the registered providers or falling back to
        /// <see cref="CertificateStoreIdentifier.CreateStore(string, ITelemetryContext)"/>.
        /// </summary>
        private ICertificateStore OpenStore(string storePath, string? storeType)
        {
            return OpenCertificateStore(storePath, storeType);
        }

        /// <summary>
        /// Internal record for a registered trust list.
        /// </summary>
        /// <param name="TrustedStore">
        /// Snapshotted trusted-store metadata and explicitly trusted certificates.
        /// </param>
        /// <param name="IssuerStore">
        /// Snapshotted issuer-store metadata and explicit chain-completion certificates.
        /// </param>
        private sealed record TrustListEntry(
            CertificateTrustList? TrustedStore,
            CertificateTrustList? IssuerStore);

        /// <summary>
        /// Acquires a caller-owned reference to the first application certificate while the manager remains active.
        /// </summary>
        private CertificateEntry? AcquirePrimaryCertificate()
        {
            lock (m_certificatesLock)
            {
                ThrowIfDisposed();
                return m_applicationCertificates.FirstOrDefault()?.AddRef();
            }
        }

        /// <summary>
        /// Rejects operations after the manager has released its owned resources.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed))
            {
                throw new ObjectDisposedException(nameof(CertificateManager));
            }
        }

        /// <summary>
        /// Holds the trusted and issuer source snapshots registered under each trust-list identifier.
        /// </summary>
        private readonly Dictionary<TrustListIdentifier, TrustListEntry> m_trustLists = [];
        private readonly Dictionary<TrustListIdentifier, CertificateValidationCore> m_customCores = [];
        private readonly List<CertificateEntry> m_applicationCertificates = [];

        /// <summary>
        /// Private keys loaded from directory stores, with the store snapshot
        /// they were loaded from; guarded by m_certificatesLock.
        /// </summary>
        private readonly List<LoadedApplicationKey> m_loadedApplicationKeys = [];
        private int m_loadedKeysGeneration;

        /// <summary>
        /// How long a loaded private key may be reused from an unchanged store.
        /// Bounds the window in which a same-name, same-size, same-timestamp
        /// replacement of a key file could go unnoticed.
        /// </summary>
        private static readonly TimeSpan kLoadedKeyMaxAge = TimeSpan.FromSeconds(30);
        private readonly List<ICertificateStoreProvider> m_storeProviders;
        private readonly CertificateChangeSubject m_changeSubject;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly TimeProvider m_timeProvider;
        private int m_maxRejectedCertificates;

        /// <summary>
        /// Guards mutations of m_applicationCertificates and the cached
        /// per-trust-list validators. Reads of single fields (e.g.
        /// GetInstanceCertificate enumeration) take this lock too to
        /// prevent the C5 / C1 races from the code review.
        /// </summary>
        private readonly Lock m_certificatesLock = new();
        private bool m_sendCertificateChain;
        private bool m_autoAcceptUntrustedCertificates;
        private bool m_rejectSHA1SignedCertificates = true;
        private bool m_rejectUnknownRevocationStatus;
        private ushort m_minimumCertificateKeySize = CertificateFactory.DefaultKeySize;
        private bool m_useValidatedCertificates;
        private RejectedCertificateProcessor? m_rejectedProcessor;
        private readonly CertificateLifecycleMonitor? m_lifecycleMonitor;
        private CertificateValidationCore? m_peerCore;
        private CertificateValidationCore? m_userCore;
        private CertificateValidationCore? m_httpsCore;
        private Func<Certificate, ServiceResult, bool>? m_acceptError;
        private readonly CertificateProvider m_certificateProvider;
        private bool m_disposed;

        /// <summary>
        /// A private-key certificate loaded from a directory store, with the
        /// lookup it answered and the store snapshot taken before the load.
        /// Owns one reference to the certificate.
        /// </summary>
        private sealed class LoadedApplicationKey : IDisposable
        {
            public LoadedApplicationKey(
                string id,
                string? lookupThumbprint,
                string storeKey,
                ICertificatePasswordProvider? passwordProvider,
                CertificateStoreFileSnapshot snapshot,
                DateTimeOffset loadedAt,
                Certificate certificate)
            {
                Id = id;
                LookupThumbprint = lookupThumbprint;
                StoreKey = storeKey;
                PasswordProvider = passwordProvider;
                Snapshot = snapshot;
                LoadedAt = loadedAt;
                Certificate = certificate;
            }

            public string Id { get; }
            public string? LookupThumbprint { get; }
            public string StoreKey { get; }
            public ICertificatePasswordProvider? PasswordProvider { get; }
            public CertificateStoreFileSnapshot Snapshot { get; }
            public DateTimeOffset LoadedAt { get; }
            public Certificate Certificate { get; }

            /// <summary>
            /// Returns a copy that owns its own certificate reference.
            /// </summary>
            public LoadedApplicationKey AddRef()
            {
                return new LoadedApplicationKey(
                    Id,
                    LookupThumbprint,
                    StoreKey,
                    PasswordProvider,
                    Snapshot,
                    LoadedAt,
                    Certificate.AddRef());
            }

            public void Dispose()
            {
                Certificate.Dispose();
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for CertificateManager.
    /// </summary>
    internal static partial class CertificateManagerLog
    {
        [LoggerMessage(EventId = CoreEventIds.CertificateManager + 0, Level = LogLevel.Debug,
            Message = "Trust list '{TrustList}' is already registered, skipping.")]
        public static partial void CertificateManagerLogMessage0(this ILogger logger, [FilePath] string? trustList);

        [LoggerMessage(EventId = CoreEventIds.CertificateManager + 1, Level = LogLevel.Warning,
            Message = "Failed to resolve issuer chain for application certificate {Certificate}; " +
                "sending leaf certificate only.")]
        public static partial void CertificateManagerLogMessage1(
            this ILogger logger,
            Exception? exception,
            [ApplicationIdentity] Certificate? certificate);

        [LoggerMessage(EventId = CoreEventIds.CertificateManager + 2, Level = LogLevel.Error,
            Message = "A CertificateChanges observer threw while handling {Kind}; " +
                "delivery continues with the remaining observers.")]
        public static partial void CertificateChangeObserverFailed(
            this ILogger logger,
            Exception exception,
            CertificateChangeKind? kind);
    }
}
