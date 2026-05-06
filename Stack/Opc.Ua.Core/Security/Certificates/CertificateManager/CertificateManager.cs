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
        private int m_maxRejectedCertificates;
        // Guards mutations of m_applicationCertificates and the cached
        // per-trust-list validators. Reads of single fields (e.g.
        // GetInstanceCertificate enumeration) take this lock too to
        // prevent the C5 / C1 races from the code review.
        private readonly object m_certificatesLock = new();
        private bool m_sendCertificateChain;
        private bool m_autoAcceptUntrustedCertificates;
        private bool m_rejectSHA1SignedCertificates = true;
        private bool m_rejectUnknownRevocationStatus;
        private ushort m_minimumCertificateKeySize = CertificateFactory.DefaultKeySize;
        private bool m_useValidatedCertificates;
        private RejectedCertificateProcessor? m_rejectedProcessor;
        private CertificateLifecycleMonitor? m_lifecycleMonitor;
#pragma warning disable CS0618 // Type or member is obsolete
        private CertificateValidator? m_peerValidator;
        private CertificateValidator? m_userValidator;
        private CertificateValidator? m_httpsValidator;
#pragma warning restore CS0618
        private Func<Certificate, ServiceResult, bool>? m_acceptError;
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
        private void MapFromSecurityConfiguration(SecurityConfiguration config, bool replaceExisting)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            m_sendCertificateChain = config.SendCertificateChain;

            // Snapshot global validation flags from the SecurityConfiguration so
            // that per-trust-list CertificateValidator instances created lazily
            // by GetOrCreateValidator inherit them. Without this, the
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

            // Propagate to any already-created cached validators so behavior
            // changes when MapFromSecurityConfiguration is called more than
            // once on the same manager.
            ApplyValidationFlags(m_peerValidator);
            ApplyValidationFlags(m_userValidator);
            ApplyValidationFlags(m_httpsValidator);

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
        /// the trusted-peer store. Modern replacement for the legacy
        /// <c>CertificateValidator.AutoAcceptUntrustedCertificates</c>.
        /// </summary>
        public bool AutoAcceptUntrustedCertificates
        {
            get => m_autoAcceptUntrustedCertificates;
            set
            {
                m_autoAcceptUntrustedCertificates = value;
                lock (m_certificatesLock)
                {
                    ApplyValidationFlags(m_peerValidator);
                    ApplyValidationFlags(m_userValidator);
                    ApplyValidationFlags(m_httpsValidator);
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether to reject certificates
        /// signed with a SHA-1 hash. Modern replacement for the legacy
        /// <c>CertificateValidator.RejectSHA1SignedCertificates</c>.
        /// </summary>
        public bool RejectSHA1SignedCertificates
        {
            get => m_rejectSHA1SignedCertificates;
            set
            {
                m_rejectSHA1SignedCertificates = value;
                lock (m_certificatesLock)
                {
                    ApplyValidationFlags(m_peerValidator);
                    ApplyValidationFlags(m_userValidator);
                    ApplyValidationFlags(m_httpsValidator);
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether to reject certificates
        /// whose revocation status cannot be determined. Modern replacement
        /// for the legacy <c>CertificateValidator.RejectUnknownRevocationStatus</c>.
        /// </summary>
        public bool RejectUnknownRevocationStatus
        {
            get => m_rejectUnknownRevocationStatus;
            set
            {
                m_rejectUnknownRevocationStatus = value;
                lock (m_certificatesLock)
                {
                    ApplyValidationFlags(m_peerValidator);
                    ApplyValidationFlags(m_userValidator);
                    ApplyValidationFlags(m_httpsValidator);
                }
            }
        }

        /// <summary>
        /// Gets or sets the maximum number of rejected certificates kept in
        /// the rejected-certificate store. Modern replacement for the legacy
        /// <c>CertificateValidator.MaxRejectedCertificates</c>. Setting a
        /// negative value clears the rejected store.
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
                lock (m_certificatesLock)
                {
                    // Propagate to per-trust-list legacy validators so the
                    // inner RejectedCertificateWriter caps its writes too.
                    ApplyValidationFlags(m_peerValidator);
                    ApplyValidationFlags(m_userValidator);
                    ApplyValidationFlags(m_httpsValidator);
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
        public IReadOnlyList<CertificateEntry> ApplicationCertificates
        {
            get
            {
                lock (m_certificatesLock)
                {
                    // Return a snapshot so callers can iterate without
                    // racing concurrent updates. The CertificateEntry
                    // references remain owned by the manager — callers
                    // must not Dispose them.
                    return [.. m_applicationCertificates];
                }
            }
        }

        /// <inheritdoc/>
        public CertificateEntry? GetApplicationCertificate(NodeId certificateType)
        {
            lock (m_certificatesLock)
            {
                return m_applicationCertificates.FirstOrDefault(
                    e => e.CertificateType == certificateType);
            }
        }

        /// <inheritdoc/>
        public CertificateEntry? GetInstanceCertificate(string securityPolicyUri)
        {
            lock (m_certificatesLock)
            {
                foreach (NodeId certType in CertificateIdentifier.MapSecurityPolicyToCertificateTypes(securityPolicyUri))
                {
                    CertificateEntry? entry = m_applicationCertificates.FirstOrDefault(
                        e => e.CertificateType == certType);
                    if (entry != null)
                    {
                        return entry;
                    }
                }

                return m_applicationCertificates.Count > 0 ? m_applicationCertificates[0] : null;
            }
        }

        /// <inheritdoc/>
        public byte[] GetEncodedChainBlob(string securityPolicyUri)
        {
            CertificateEntry? entry = GetInstanceCertificate(securityPolicyUri);
            return entry?.GetEncodedChainBlob() ?? [];
        }

        /// <inheritdoc/>
        public byte[]? LoadCertificateChainRaw(Certificate certificate)
        {
            if (certificate == null)
            {
                return null;
            }

            string thumbprint = certificate.Thumbprint;
            lock (m_certificatesLock)
            {
                for (int i = 0; i < m_applicationCertificates.Count; i++)
                {
                    CertificateEntry entry = m_applicationCertificates[i];
                    if (string.Equals(entry.Certificate.Thumbprint, thumbprint, StringComparison.Ordinal))
                    {
                        return entry.GetEncodedChainBlob();
                    }
                }
            }

            // Not a registered application certificate: return the raw cert bytes.
            return certificate.RawData;
        }

        /// <inheritdoc/>
        public async Task<bool> GetIssuersAsync(
            Certificate certificate,
            IList<CertificateIdentifier> issuers,
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

#pragma warning disable CS0618 // Type or member is obsolete
            CertificateValidator validator = GetOrCreateValidator(TrustListIdentifier.Peers);
#pragma warning restore CS0618

            // The legacy validator's GetIssuersAsync exposes a List<CertificateIdentifier>;
            // marshal between the two collection types so the registry can keep an IList
            // signature without forcing callers to allocate a concrete List.
            var temp = issuers as List<CertificateIdentifier> ?? [.. issuers];
            int existingCount = temp.Count;
            bool isTrusted = await validator.GetIssuersAsync(certificate, temp, ct)
                .ConfigureAwait(false);

            if (!ReferenceEquals(temp, issuers))
            {
                for (int i = existingCount; i < temp.Count; i++)
                {
                    issuers.Add(temp[i]);
                }
            }

            return isTrusted;
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
            // Build the new entries OUTSIDE the lock (FindAsync is async and
            // may be slow on file I/O), then atomically swap inside the lock.
            ArrayOf<CertificateIdentifier> appCerts = securityConfiguration.ApplicationCertificates;
            var newEntries = new List<CertificateEntry>(appCerts.Count);
            try
            {
                for (int i = 0; i < appCerts.Count; i++)
                {
                    CertificateIdentifier certId = appCerts[i];
                    using Certificate? certificate = await certId.FindAsync(true, applicationUri, m_telemetry, ct)
                        .ConfigureAwait(false);
                    if (certificate != null)
                    {
                        newEntries.Add(new CertificateEntry(
                            certificate,
                            new CertificateCollection(),
                            certId.CertificateType));
                    }
                }

                List<CertificateEntry> oldEntries;
                lock (m_certificatesLock)
                {
                    oldEntries = [.. m_applicationCertificates];
                    m_applicationCertificates.Clear();
                    foreach (CertificateEntry e in newEntries)
                    {
                        m_applicationCertificates.Add(e);
                    }
                }

                // Dispose old entries OUTSIDE the lock so that any concurrent
                // reader who captured a borrowed reference before the swap
                // still has time to AddRef before disposal completes.
                // (Borrowed-reference consumers are expected to AddRef before
                // any long-lived use; this gives them at least the lock-free
                // window between snapshot and dispose.)
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

        /// <inheritdoc/>
        public async Task<CertificateValidationResult> ValidateAsync(
            CertificateCollection chain,
            TrustListIdentifier? trustList = null,
            Opc.Ua.Security.Certificates.CertificateValidationOptions? options = null,
            CancellationToken ct = default)
        {
            trustList ??= TrustListIdentifier.Peers;
#pragma warning disable CS0618 // Type or member is obsolete
            CertificateValidator validator = GetOrCreateValidator(trustList);
#pragma warning restore CS0618

            // Per-call AcceptError takes precedence over the global hook.
            Func<Certificate, ServiceResult, bool>? acceptError =
                options?.AcceptError ?? m_acceptError;

            CertificateValidationEventHandler? handler = null;
            if (acceptError != null)
            {
                Func<Certificate, ServiceResult, bool> callback = acceptError;
                handler = (sender, e) =>
                {
                    try
                    {
                        if (callback(e.Certificate, e.Error))
                        {
                            e.Accept = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(
                            ex,
                            "CertificateValidationOptions.AcceptError callback threw; treating as reject.");
                    }
                };
                validator.CertificateValidation += handler;
            }

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
            finally
            {
                if (handler != null)
                {
                    validator.CertificateValidation -= handler;
                }
            }
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

        /// <inheritdoc/>
        public Task UpdateApplicationCertificateAsync(
            NodeId certificateType,
            Certificate newCertificate,
            CertificateCollection? issuerChain = null,
            CancellationToken ct = default)
        {
            CertificateEntry? oldEntry = null;
#pragma warning disable CS0618
            CertificateValidator? oldPeer, oldUser, oldHttps;
#pragma warning restore CS0618

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
                oldPeer = m_peerValidator; m_peerValidator = null;
                oldUser = m_userValidator; m_userValidator = null;
                oldHttps = m_httpsValidator; m_httpsValidator = null;
            }

            // Dispose orphaned validators OUTSIDE the lock.
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
        public async Task ReloadApplicationCertificatesAsync(
            SecurityConfiguration securityConfiguration,
            string? applicationUri = null,
            CancellationToken ct = default)
        {
            // Snapshot the previous primary entry (if any) so we can fire a
            // CertificateChange notification once the reload completes.
#pragma warning disable CS0618
            CertificateValidator? oldPeer, oldUser, oldHttps;
#pragma warning restore CS0618
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
                // Invalidate cached validators so subsequent validations pick up
                // any trust-list/cert changes implicit in the reload.
                oldPeer = m_peerValidator; m_peerValidator = null;
                oldUser = m_userValidator; m_userValidator = null;
                oldHttps = m_httpsValidator; m_httpsValidator = null;
            }

            // Dispose orphaned validators OUTSIDE the lock.
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

            await ReloadApplicationCertificatesAsync(securityConfiguration, applicationUri, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task FlushRejectedAsync(CancellationToken ct = default)
        {
            // Wait for the manager's own rejected processor (used when callers
            // invoke RejectCertificateAsync directly).
            Task processorDrain = m_rejectedProcessor?.WaitForDrainAsync()
                ?? Task.CompletedTask;

            // Snapshot the per-trust-list legacy validators so we can also
            // wait on their internal RejectedCertificateWriter queues. The
            // current ValidateAsync implementation delegates to a cached
            // legacy validator per trust list and that validator owns its
            // own writer — without flushing it the rejected store can lag
            // behind the test assertions.
#pragma warning disable CS0618 // Type or member is obsolete
            CertificateValidator? peer, user, https;
#pragma warning restore CS0618
            lock (m_certificatesLock)
            {
                peer = m_peerValidator;
                user = m_userValidator;
                https = m_httpsValidator;
            }

            await processorDrain.ConfigureAwait(false);
            if (peer != null)
            {
                await peer.WaitForRejectedCertificatesDrainAsync().ConfigureAwait(false);
            }
            if (user != null)
            {
                await user.WaitForRejectedCertificatesDrainAsync().ConfigureAwait(false);
            }
            if (https != null)
            {
                await https.WaitForRejectedCertificatesDrainAsync().ConfigureAwait(false);
            }
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

                m_peerValidator?.Dispose();
                m_peerValidator = null;
                m_userValidator?.Dispose();
                m_userValidator = null;
                m_httpsValidator?.Dispose();
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
#pragma warning disable CS0618 // Type or member is obsolete
        private CertificateValidator GetOrCreateValidator(TrustListIdentifier trustList)
        {
            // Fast path: return a cached validator without taking the lock.
            CertificateValidator? cached = GetCachedValidator(trustList);
            if (cached != null)
            {
                return cached;
            }

            // Slow path: build a candidate validator outside the lock,
            // then atomically install it. If a peer thread won the race,
            // dispose the loser.
            var candidate = new CertificateValidator(m_telemetry);

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

                candidate.Update(issuerStore, trustedStore, rejectedStore);
            }

            ApplyValidationFlags(candidate);

            CertificateValidator winner;
            lock (m_certificatesLock)
            {
                if (trustList == TrustListIdentifier.Peers)
                {
                    winner = m_peerValidator ??= candidate;
                }
                else if (trustList == TrustListIdentifier.Users)
                {
                    winner = m_userValidator ??= candidate;
                }
                else if (trustList == TrustListIdentifier.Https)
                {
                    winner = m_httpsValidator ??= candidate;
                }
                else
                {
                    // Non-cached trust list — return the candidate directly.
                    return candidate;
                }
            }

            if (!ReferenceEquals(winner, candidate))
            {
                // Lost the race; dispose our orphaned candidate.
                candidate.Dispose();
            }
            return winner;
        }
#pragma warning restore CS0618

        /// <summary>
        /// Applies the global validation flags captured from the
        /// SecurityConfiguration to a (possibly already-created) validator.
        /// Safe to call with a null validator.
        /// </summary>
#pragma warning disable CS0618 // Type or member is obsolete
        private void ApplyValidationFlags(CertificateValidator? validator)
        {
            if (validator == null)
            {
                return;
            }

            validator.AutoAcceptUntrustedCertificates = m_autoAcceptUntrustedCertificates;
            validator.RejectSHA1SignedCertificates = m_rejectSHA1SignedCertificates;
            validator.RejectUnknownRevocationStatus = m_rejectUnknownRevocationStatus;
            validator.MaxRejectedCertificates = m_maxRejectedCertificates;
            if (m_minimumCertificateKeySize > 0)
            {
                validator.MinimumCertificateKeySize = m_minimumCertificateKeySize;
            }
            validator.UseValidatedCertificates = m_useValidatedCertificates;
        }
#pragma warning restore CS0618

        /// <summary>
        /// Returns the cached validator for a well-known trust list,
        /// or <see langword="null"/> if none is cached yet.
        /// </summary>
#pragma warning disable CS0618 // Type or member is obsolete
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
#pragma warning restore CS0618

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
