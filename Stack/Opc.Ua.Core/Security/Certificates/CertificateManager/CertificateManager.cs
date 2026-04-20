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
        private readonly Dictionary<TrustListIdentifier, TrustListEntry> _trustLists = new();
        private readonly List<CertificateEntry> _applicationCertificates = [];
        private readonly List<ICertificateStoreProvider> _storeProviders;
        private readonly CertificateChangeSubject _changeSubject = new();
        private readonly ITelemetryContext _telemetry;
        private readonly ILogger _logger;
        private readonly int _maxRejectedCertificates;
        private RejectedCertificateProcessor? _rejectedProcessor;
        private CertificateValidator? _peerValidator;
        private CertificateValidator? _userValidator;
        private CertificateValidator? _httpsValidator;
        private bool _disposed;

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
        public CertificateManager(
            ITelemetryContext telemetry,
            IEnumerable<ICertificateStoreProvider>? storeProviders = null,
            int maxRejectedCertificates = 5)
        {
            _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            _logger = telemetry.CreateLogger<CertificateManager>();
            _maxRejectedCertificates = maxRejectedCertificates;
            _storeProviders = storeProviders?.ToList() ?? [
                new DirectoryStoreProvider(),
                new X509StoreProvider()
            ];
        }

        /// <inheritdoc/>
        public IReadOnlyCollection<TrustListIdentifier> TrustLists => _trustLists.Keys;

        /// <inheritdoc/>
        public IObservable<CertificateChangeEvent> CertificateChanges => _changeSubject;

        /// <inheritdoc/>
        public void RegisterTrustList(
            TrustListIdentifier trustList,
            string trustedStorePath,
            string? issuerStorePath = null)
        {
            if (trustList == null) throw new ArgumentNullException(nameof(trustList));

            if (string.IsNullOrEmpty(trustedStorePath))
            {
                throw new ArgumentException(
                    "Trusted store path must not be null or empty.",
                    nameof(trustedStorePath));
            }

            if (!_trustLists.TryAdd(trustList, new TrustListEntry(
                    trustedStorePath, issuerStorePath, StoreType: null)))
            {
                _logger.LogDebug(
                    "Trust list '{TrustList}' is already registered, skipping.",
                    trustList);
            }
        }

        /// <inheritdoc/>
        public ICertificateStore OpenTrustedStore(TrustListIdentifier trustList)
        {
            if (!_trustLists.TryGetValue(trustList, out TrustListEntry? entry))
            {
                throw new KeyNotFoundException(
                    $"Trust list '{trustList}' is not registered.");
            }

            return OpenStore(entry.TrustedStorePath, entry.StoreType);
        }

        /// <inheritdoc/>
        public ICertificateStore? OpenIssuerStore(TrustListIdentifier trustList)
        {
            if (!_trustLists.TryGetValue(trustList, out TrustListEntry? entry))
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
            if (trustList == null) throw new ArgumentNullException(nameof(trustList));

            if (!_trustLists.ContainsKey(trustList))
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
            if (config == null) throw new ArgumentNullException(nameof(config));

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
        public IReadOnlyList<CertificateEntry> ApplicationCertificates => _applicationCertificates;

        /// <inheritdoc/>
        public CertificateEntry? GetApplicationCertificate(NodeId certificateType)
        {
            return _applicationCertificates.FirstOrDefault(
                e => e.CertificateType == certificateType);
        }

        /// <inheritdoc/>
        public CertificateEntry? GetInstanceCertificate(string securityPolicyUri)
        {
            foreach (NodeId certType in CertificateIdentifier.MapSecurityPolicyToCertificateTypes(securityPolicyUri))
            {
                var entry = GetApplicationCertificate(certType);
                if (entry != null)
                {
                    return entry;
                }
            }

            return _applicationCertificates.Count > 0 ? _applicationCertificates[0] : null;
        }

        /// <inheritdoc/>
        public byte[] GetEncodedChainBlob(string securityPolicyUri)
        {
            var entry = GetInstanceCertificate(securityPolicyUri);
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
            _applicationCertificates.Clear();
            var appCerts = securityConfiguration.ApplicationCertificates;
            for (int i = 0; i < appCerts.Count; i++)
            {
                var certId = appCerts[i];
                var certificate = await certId.FindAsync(true, applicationUri, _telemetry, ct)
                    .ConfigureAwait(false);
                if (certificate != null)
                {
                    _applicationCertificates.Add(new CertificateEntry(
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
                new CertificateCollection { certificate },
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
            Certificate? oldCert = null;

            // Find and replace the existing entry.
            for (int i = 0; i < _applicationCertificates.Count; i++)
            {
                if (_applicationCertificates[i].CertificateType == certificateType)
                {
                    oldCert = _applicationCertificates[i].Certificate;
                    _applicationCertificates[i] = new CertificateEntry(
                        newCertificate,
                        issuerChain ?? new CertificateCollection(),
                        certificateType);
                    break;
                }
            }

            // If not found, add a new entry.
            if (oldCert == null)
            {
                _applicationCertificates.Add(new CertificateEntry(
                    newCertificate,
                    issuerChain ?? new CertificateCollection(),
                    certificateType));
            }

            // Invalidate cached validators.
            _peerValidator = null;
            _userValidator = null;
            _httpsValidator = null;

            _changeSubject.Notify(new CertificateChangeEvent(
                CertificateChangeKind.ApplicationCertificateUpdated,
                TrustListIdentifier.Peers,
                certificateType,
                oldCert,
                newCertificate,
                issuerChain));

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task RejectCertificateAsync(
            CertificateCollection chain,
            CancellationToken ct = default)
        {
            _rejectedProcessor ??= new RejectedCertificateProcessor(
                this, _maxRejectedCertificates, _telemetry);
            return _rejectedProcessor.EnqueueAsync(chain, ct).AsTask();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!_disposed)
            {
                _changeSubject.Complete();

                if (_rejectedProcessor != null)
                {
                    _rejectedProcessor.DisposeAsync()
                        .AsTask().GetAwaiter().GetResult();
                }

                _peerValidator = null;
                _userValidator = null;
                _httpsValidator = null;

                _trustLists.Clear();
                _disposed = true;
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

            var validator = new CertificateValidator(_telemetry);

            if (_trustLists.TryGetValue(trustList, out TrustListEntry? entry))
            {
                var trustedStore = new CertificateTrustList {
                    StorePath = entry.TrustedStorePath
                };

                CertificateTrustList? issuerStore = entry.IssuerStorePath != null
                    ? new CertificateTrustList { StorePath = entry.IssuerStorePath }
                    : null;

                CertificateStoreIdentifier? rejectedStore = null;
                if (_trustLists.TryGetValue(
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
                _peerValidator = validator;
            }
            else if (trustList == TrustListIdentifier.Users)
            {
                _userValidator = validator;
            }
            else if (trustList == TrustListIdentifier.Https)
            {
                _httpsValidator = validator;
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
                return _peerValidator;
            }

            if (trustList == TrustListIdentifier.Users)
            {
                return _userValidator;
            }

            if (trustList == TrustListIdentifier.Https)
            {
                return _httpsValidator;
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

            foreach (ICertificateStoreProvider provider in _storeProviders)
            {
                if (string.Equals(
                        provider.StoreTypeName,
                        storeType,
                        StringComparison.Ordinal))
                {
                    ICertificateStore store = provider.CreateStore(_telemetry);
                    store.Open(storePath);
                    return store;
                }
            }

            // Fallback to the existing factory method for custom store types.
            ICertificateStore fallbackStore =
                CertificateStoreIdentifier.CreateStore(storeType, _telemetry);
            fallbackStore.Open(storePath);
            return fallbackStore;
        }
    }
}
