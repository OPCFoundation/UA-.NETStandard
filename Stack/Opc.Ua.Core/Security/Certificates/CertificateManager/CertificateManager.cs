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
    public sealed class CertificateManager : ICertificateManager, IDisposable
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
                () => m_applicationCertificates,
                threshold,
                TimeSpan.FromHours(1),
                m_telemetry,
                m_timeProvider);

            m_certificateProvider = new CertificateProvider(m_telemetry);
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

            ITrustListTransaction transaction = new TrustListTransaction(this, trustList, this);
            return Task.FromResult(transaction);
        }

        /// <summary>
        /// Maps the stores defined in a <see cref="SecurityConfiguration"/>
        /// to named trust lists (Peers, Users, Https, Rejected).
        /// </summary>
        /// <param name="config">The security configuration to map from.</param>
        public void MapFromSecurityConfiguration(SecurityConfiguration config)
        {
            MapFromSecurityConfiguration(config, replaceExisting: false);
        }

        /// <summary>
        /// Maps the stores defined in a <see cref="SecurityConfiguration"/>
        /// to named trust lists with optional replacement of existing
        /// entries.
        /// </summary>
        /// <param name="config">The security configuration to map from.</param>
        /// <param name="replaceExisting">
        /// When <see langword="true"/>, existing trust list entries are
        /// replaced with the paths from <paramref name="config"/> (used by
        /// <see cref="UpdateAsync"/> to honour runtime trust-list path
        /// changes).
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <c>null</c>.</exception>
        private void MapFromSecurityConfiguration(SecurityConfiguration config, bool replaceExisting)
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
            ApplyValidationFlags(m_peerCore);
            ApplyValidationFlags(m_userCore);
            ApplyValidationFlags(m_httpsCore);
            lock (m_certificatesLock)
            {
                foreach (CertificateValidationCore core in m_customCores.Values)
                {
                    ApplyValidationFlags(core);
                }
            }

            RegisterOrReplaceTrustList(
                TrustListIdentifier.Peers,
                config.TrustedPeerCertificates?.StorePath,
                config.TrustedIssuerCertificates?.StorePath,
                replaceExisting);

            RegisterOrReplaceTrustList(
                TrustListIdentifier.Users,
                config.TrustedUserCertificates?.StorePath,
                config.UserIssuerCertificates?.StorePath,
                replaceExisting);

            RegisterOrReplaceTrustList(
                TrustListIdentifier.Https,
                config.TrustedHttpsCertificates?.StorePath,
                config.HttpsIssuerCertificates?.StorePath,
                replaceExisting);

            RegisterOrReplaceTrustList(
                TrustListIdentifier.Rejected,
                config.RejectedCertificateStore?.StorePath,
                issuerStorePath: null,
                replaceExisting);
        }

        private void RegisterOrReplaceTrustList(
            TrustListIdentifier trustList,
            string? trustedStorePath,
            string? issuerStorePath,
            bool replaceExisting)
        {
            if (string.IsNullOrEmpty(trustedStorePath))
            {
                return;
            }

            if (replaceExisting)
            {
                m_trustLists[trustList] = new TrustListEntry(
                    trustedStorePath!, issuerStorePath, StoreType: null);
            }
            else
            {
                RegisterTrustList(trustList, trustedStorePath!, issuerStorePath);
            }
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
                m_autoAcceptUntrustedCertificates = value;
                lock (m_certificatesLock)
                {
                    ApplyValidationFlags(m_peerCore);
                    ApplyValidationFlags(m_userCore);
                    ApplyValidationFlags(m_httpsCore);
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
                m_rejectSHA1SignedCertificates = value;
                lock (m_certificatesLock)
                {
                    ApplyValidationFlags(m_peerCore);
                    ApplyValidationFlags(m_userCore);
                    ApplyValidationFlags(m_httpsCore);
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
                m_rejectUnknownRevocationStatus = value;
                lock (m_certificatesLock)
                {
                    ApplyValidationFlags(m_peerCore);
                    ApplyValidationFlags(m_userCore);
                    ApplyValidationFlags(m_httpsCore);
                }
            }
        }

        /// <summary>
        /// Gets or sets the maximum number of rejected certificates kept in
        /// the rejected-certificate store. Setting a negative value clears
        /// the rejected store.
        /// </summary>
        public int MaxRejectedCertificates
        {
            get => m_maxRejectedCertificates;
            set
            {
                if (value < 0)
                {
                    // Negative limit disables the rejected store entirely
                    // and asks the processor to clear what's there.
                    m_maxRejectedCertificates = 0;
                }
                else
                {
                    m_maxRejectedCertificates = value;
                }
                if (m_rejectedProcessor != null)
                {
                    m_rejectedProcessor.SetMaxRejectedCertificates(m_maxRejectedCertificates);
                    // Actively re-apply the cap so existing entries are
                    // trimmed when the cap is lowered. The trim runs on
                    // the processor's background task and can be awaited
                    // via FlushRejectedAsync.
                    _ = m_rejectedProcessor.EnqueueTrimAsync().AsTask();
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
        public Task<bool> GetIssuersAsync(
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

            // CA2000: GetOrCreateCore returns a shared validation core owned by this
            // manager (cached per well-known trust list, disposed in Dispose); borrowed here.
#pragma warning disable CA2000
            CertificateValidationCore core = GetOrCreateCore(TrustListIdentifier.Peers);
#pragma warning restore CA2000
            return core.GetIssuersAsync(certificate, issuers, ct);
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
            // Build the new entries OUTSIDE the lock (resolution is async and
            // may be slow on file I/O), then atomically swap inside the lock.
            ArrayOf<CertificateIdentifier> appCerts = securityConfiguration.ApplicationCertificates;
            ICertificatePasswordProvider? passwordProvider = securityConfiguration
                .CertificatePasswordProvider;
            var newEntries = new List<CertificateEntry>(appCerts.Count);
            try
            {
                for (int i = 0; i < appCerts.Count; i++)
                {
                    CertificateIdentifier certId = appCerts[i];
                    // The resolver opens the identifier's store and applies
                    // post-rotation fallbacks (subject-null, then
                    // thumbprint-null, by applicationUri) so a freshly-pushed
                    // certificate is picked up even when the configured
                    // identifier's thumbprint still references the old cert.
                    using Certificate? certificate = await CertificateIdentifierResolver
                        .LoadPrivateKeyAsync(
                            certId,
                            passwordProvider,
                            applicationUri,
                            m_telemetry,
                            ct)
                        .ConfigureAwait(false);
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

                List<CertificateEntry> oldEntries;
                lock (m_certificatesLock)
                {
                    oldEntries = [.. m_applicationCertificates];
                    m_applicationCertificates.Clear();
                    m_applicationCertificates.AddRange(newEntries);
                }

                // Dispose the manager's own old entries OUTSIDE the lock.
                // Accessors hand out independent AddRef'd handles (never the
                // manager's own entries), so disposing these old entries here
                // cannot affect any handle a consumer is still holding.
                foreach (CertificateEntry oldEntry in oldEntries)
                {
                    oldEntry.Dispose();
                }

                // ownership transferred into m_applicationCertificates; do not
                // dispose newEntries on success.
                newEntries.Clear();
            }
            finally
            {
                // If we threw before the swap, dispose any partially-built
                // entries to avoid leaking ref-counted certs.
                foreach (CertificateEntry pending in newEntries)
                {
                    pending.Dispose();
                }
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
                m_logger.LogWarning(
                    ex,
                    "Failed to resolve issuer chain for application certificate " +
                    "{Certificate}; sending leaf certificate only.",
                    certificate);
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
            // CA2000: GetOrCreateCore returns a shared validation core owned by this
            // manager (cached per well-known trust list, disposed in Dispose); borrowed here.
#pragma warning disable CA2000
            CertificateValidationCore core = GetOrCreateCore(trustList);
#pragma warning restore CA2000

            // Per-call AcceptError takes precedence over the global hook.
            Func<Certificate, ServiceResult, bool>? acceptError =
                options?.AcceptError ?? m_acceptError;

            CertificateValidationResult result = await core
                .ValidateAsync(chain, acceptError, options, ct)
                .ConfigureAwait(false);

            if (!result.IsValid && chain != null && chain.Count > 0)
            {
                // The core does not own a rejected-store writer; the manager
                // is responsible for enqueuing failed chains on the shared
                // RejectedCertificateProcessor. CertificateCollection.Add
                // AddRef's each cert; the processor disposes the chain after
                // processing, balancing the AddRef.
                m_rejectedProcessor ??= new RejectedCertificateProcessor(
                    this, m_maxRejectedCertificates, m_telemetry);

                using var rejectedChain = new CertificateCollection();
                foreach (Certificate c in chain)
                {
                    rejectedChain.Add(c);
                }
                await m_rejectedProcessor.EnqueueAsync(rejectedChain, ct)
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

            // CA2000: GetOrCreateCore returns a shared validation core owned by this
            // manager (cached per well-known trust list, disposed in Dispose); borrowed here.
#pragma warning disable CA2000
            CertificateValidationCore core = GetOrCreateCore(TrustListIdentifier.Peers);
#pragma warning restore CA2000
            try
            {
                core.ValidateApplicationUri(serverCertificate, endpoint, m_acceptError);
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

            // CA2000: GetOrCreateCore returns a shared validation core owned by this
            // manager (cached per well-known trust list, disposed in Dispose); borrowed here.
#pragma warning disable CA2000
            CertificateValidationCore core = GetOrCreateCore(TrustListIdentifier.Peers);
#pragma warning restore CA2000
            try
            {
                core.ValidateDomains(
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
            m_rejectedProcessor ??= new RejectedCertificateProcessor(
                this, m_maxRejectedCertificates, m_telemetry);
            using var rejected = new CertificateCollection { certificate };
            // Fire-and-forget: the processor handles failures internally.
            _ = m_rejectedProcessor.EnqueueAsync(rejected).AsTask();
        }

        /// <inheritdoc/>
        public async Task UpdateApplicationCertificateAsync(
            NodeId certificateType,
            Certificate newCertificate,
            CertificateCollection? issuerChain = null,
            CancellationToken ct = default)
        {
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
            CertificateValidationCore? oldPeer;
            CertificateValidationCore? oldUser;
            CertificateValidationCore? oldHttps;

            lock (m_certificatesLock)
            {
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
        public async Task ReloadApplicationCertificatesAsync(
            SecurityConfiguration securityConfiguration,
            string? applicationUri = null,
            CancellationToken ct = default)
        {
            // Snapshot the previous primary entry (if any) so we can fire a
            // CertificateChange notification once the reload completes.
            CertificateValidationCore? oldPeer;
            CertificateValidationCore? oldUser;
            CertificateValidationCore? oldHttps;
            CertificateEntry? oldPrimary;
            lock (m_certificatesLock)
            {
                oldPrimary = m_applicationCertificates.FirstOrDefault();
            }
            using Certificate? oldCertSnapshot = oldPrimary?.Certificate.AddRef();

            await LoadApplicationCertificatesAsync(securityConfiguration, applicationUri, ct)
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

            CertificateEntry? newPrimary;
            lock (m_certificatesLock)
            {
                newPrimary = m_applicationCertificates.FirstOrDefault();
            }
            if (newPrimary != null)
            {
                m_changeSubject.Notify(new CertificateChangeEvent(
                    CertificateChangeKind.ApplicationCertificateUpdated,
                    TrustListIdentifier.Peers,
                    newPrimary.CertificateType,
                    oldCertSnapshot,
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

            // Re-map trust-list paths and validation flags. Existing entries
            // are replaced so trust-list path changes (rare but possible via
            // GDS push) propagate.
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
            return m_rejectedProcessor?.WaitForDrainAsync()
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

            if (((int)masks &
                ((int)TrustListMasks.TrustedCertificates | (int)TrustListMasks.TrustedCrls)) != 0)
            {
                using ICertificateStore store = OpenTrustedStore(trustList);

                if (((int)masks & (int)TrustListMasks.TrustedCertificates) != 0)
                {
                    await ClearCertificatesAsync(store, ct)
                        .ConfigureAwait(false);

                    foreach (Certificate cert in data.TrustedCertificates)
                    {
                        await store.AddAsync(cert, ct: ct)
                            .ConfigureAwait(false);
                    }

                    trustChanged = true;
                }

                if (((int)masks & (int)TrustListMasks.TrustedCrls) != 0 &&
                    store.SupportsCRLs)
                {
                    await ClearCrlsAsync(store, ct).ConfigureAwait(false);

                    foreach (X509CRL crl in data.TrustedCrls)
                    {
                        await store.AddCRLAsync(crl, ct)
                            .ConfigureAwait(false);
                    }

                    crlChanged = true;
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
                            await ClearCertificatesAsync(issuerStore, ct)
                                .ConfigureAwait(false);

                            foreach (Certificate cert in data.IssuerCertificates)
                            {
                                await issuerStore.AddAsync(cert, ct: ct)
                                    .ConfigureAwait(false);
                            }

                            trustChanged = true;
                        }

                        if (((int)masks & (int)TrustListMasks.IssuerCrls) != 0 &&
                            issuerStore.SupportsCRLs)
                        {
                            await ClearCrlsAsync(issuerStore, ct)
                                .ConfigureAwait(false);

                            foreach (X509CRL crl in data.IssuerCrls)
                            {
                                await issuerStore.AddCRLAsync(crl, ct)
                                    .ConfigureAwait(false);
                            }

                            crlChanged = true;
                        }
                    }
                }
            }

            NotifyTrustListChanged(trustList, trustChanged, crlChanged);
        }

        /// <summary>
        /// Invalidates cached validation cores for the affected trust
        /// list and dispatches <see cref="CertificateChangeKind.TrustListUpdated"/>
        /// and/or <see cref="CertificateChangeKind.CrlUpdated"/> events.
        /// Called by <see cref="WriteTrustListAsync"/> and by
        /// <see cref="TrustListTransaction.CommitAsync"/> after a
        /// successful apply. No-op when no change kind is set.
        /// </summary>
        internal void NotifyTrustListChanged(
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
            // all three roles defensively — trust list changes can
            // affect peer, user and HTTPS validators alike.
            CertificateValidationCore? oldPeer;
            CertificateValidationCore? oldUser;
            CertificateValidationCore? oldHttps;
            lock (m_certificatesLock)
            {
                oldPeer = m_peerCore;
                m_peerCore = null;
                oldUser = m_userCore;
                m_userCore = null;
                oldHttps = m_httpsCore;
                m_httpsCore = null;
            }

            oldPeer?.Dispose();
            oldUser?.Dispose();
            oldHttps?.Dispose();

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
            if (!m_disposed)
            {
                m_lifecycleMonitor?.Dispose();
                m_changeSubject.Complete();

                m_rejectedProcessor?.DisposeAsync()
                        .AsTask().GetAwaiter().GetResult();

                m_peerCore?.Dispose();
                m_peerCore = null;
                m_userCore?.Dispose();
                m_userCore = null;
                m_httpsCore?.Dispose();
                m_httpsCore = null;

                lock (m_certificatesLock)
                {
                    foreach (CertificateValidationCore core in m_customCores.Values)
                    {
                        core.Dispose();
                    }
                    m_customCores.Clear();
                }

                m_certificateProvider.Dispose();

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
        /// Gets or creates a <see cref="CertificateValidationCore"/> configured
        /// for the specified trust list.
        /// </summary>
        private CertificateValidationCore GetOrCreateCore(TrustListIdentifier trustList)
        {
            // Fast path: return a cached core without taking the lock.
            CertificateValidationCore? cached = GetCachedCore(trustList);
            if (cached != null)
            {
                return cached;
            }

            // Slow path: build a candidate core outside the lock,
            // then atomically install it. If a peer thread won the race,
            // dispose the loser.
            var candidate = new CertificateValidationCore(m_telemetry);

            if (m_trustLists.TryGetValue(trustList, out TrustListEntry? entry))
            {
                var trustedStore = new CertificateTrustList
                {
                    StorePath = entry.TrustedStorePath
                };

                CertificateTrustList? issuerStore = entry.IssuerStorePath != null
                    ? new CertificateTrustList { StorePath = entry.IssuerStorePath }
                    : null;

                candidate.Update(issuerStore, trustedStore, rejectedCertificateStore: null);
            }

            ApplyValidationFlags(candidate);

            CertificateValidationCore winner;
            lock (m_certificatesLock)
            {
                if (trustList == TrustListIdentifier.Peers)
                {
                    winner = m_peerCore ??= candidate;
                }
                else if (trustList == TrustListIdentifier.Users)
                {
                    winner = m_userCore ??= candidate;
                }
                else if (trustList == TrustListIdentifier.Https)
                {
                    winner = m_httpsCore ??= candidate;
                }
                else if (m_customCores.TryGetValue(trustList, out CertificateValidationCore? existingCustom))
                {
                    winner = existingCustom;
                }
                else
                {
                    // A custom (non-well-known) trust list registered via
                    // CertificateManagerOptions.AddTrustList: cache it the
                    // same way as Peers/Users/Https so this method never
                    // creates an unreachable, un-disposable core on every
                    // call, and so Dispose() below can release its
                    // certificate-store handles.
                    m_customCores.Add(trustList, candidate);
                    winner = candidate;
                }
            }

            if (!ReferenceEquals(winner, candidate))
            {
                // Lost the race; dispose our orphaned candidate.
                candidate.Dispose();
            }
            return winner;
        }

        /// <summary>
        /// Applies the global validation flags captured from the
        /// SecurityConfiguration to a (possibly already-created) core.
        /// Safe to call with a null core.
        /// </summary>
        private void ApplyValidationFlags(CertificateValidationCore? core)
        {
            if (core == null)
            {
                return;
            }

            core.AutoAcceptUntrustedCertificates = m_autoAcceptUntrustedCertificates;
            core.RejectSHA1SignedCertificates = m_rejectSHA1SignedCertificates;
            core.RejectUnknownRevocationStatus = m_rejectUnknownRevocationStatus;
            if (m_minimumCertificateKeySize > 0)
            {
                core.MinimumCertificateKeySize = m_minimumCertificateKeySize;
            }
            core.UseValidatedCertificates = m_useValidatedCertificates;
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

        /// <summary>
        /// Internal record for a registered trust list.
        /// </summary>
        private sealed record TrustListEntry(
            string TrustedStorePath,
            string? IssuerStorePath,
            string? StoreType);

        private readonly Dictionary<TrustListIdentifier, TrustListEntry> m_trustLists = [];
        private readonly Dictionary<TrustListIdentifier, CertificateValidationCore> m_customCores = [];
        private readonly List<CertificateEntry> m_applicationCertificates = [];
        private readonly List<ICertificateStoreProvider> m_storeProviders;
        private readonly CertificateChangeSubject m_changeSubject = new();
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
    }
}
