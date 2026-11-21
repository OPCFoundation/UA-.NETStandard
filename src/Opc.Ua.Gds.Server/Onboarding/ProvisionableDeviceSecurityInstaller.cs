/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Verifies the device's software-update prerequisite using its existing software-management provider.
    /// </summary>
    public interface IProvisionableDeviceSoftwareUpdatePolicy
    {
        /// <summary>
        /// Returns only when required software updates for the specified DCA have completed.
        /// Reads the provider's durable state; missing approval or incomplete updates must throw.
        /// Security reset does not clear or modify this state.
        /// </summary>
        ValueTask RequireReadyAsync(string applicationUri, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Retrieves a private key retained independently for one pull signing attempt.
    /// </summary>
    public interface IProvisionableDevicePullKeyProvider
    {
        /// <summary>
        /// Returns an independently owned key handle for the received certificate and active attempt, or null
        /// when no matching key exists. Retrieval must not consume the retained key.
        /// </summary>
        ValueTask<Certificate?> GetSigningKeyAsync(
            Guid attemptId,
            ByteString issuedCertificate,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Selects the application-certificate slot and an independent private-key store for device reset.
    /// </summary>
    public sealed class ProvisionableDeviceSecurityInstallerOptions
    {
        /// <summary>
        /// The configured application-certificate store entry, including its OPC UA certificate type.
        /// </summary>
        public CertificateIdentifier? ApplicationCertificate { get; set; }

        /// <summary>
        /// The store path for the immutable device identity certificate and private key.
        /// </summary>
        public string? DeviceIdentityStorePath { get; set; }

        /// <summary>
        /// The store type for the immutable device identity certificate and private key.
        /// </summary>
        public string? DeviceIdentityStoreType { get; set; }

        /// <summary>
        /// The certificate group whose regenerated key is held by the pending-key store.
        /// </summary>
        public NodeId CertificateGroupId { get; set; }

        /// <summary>
        /// Supplies passwords for the configured application-certificate store when required.
        /// </summary>
        public ICertificatePasswordProvider? CertificatePasswordProvider { get; set; }
    }

    /// <summary>
    /// Installs device application credentials, retains the original device identity independently, and restores
    /// the captured trust and identity defaults without modifying installed software.
    /// </summary>
    public sealed class ProvisionableDeviceSecurityInstaller :
        IProvisionableDeviceSecurityInstaller,
        IProvisionableDevicePullSecurityInstaller,
        IProvisionableDeviceSecurityDefaults,
        IProvisionableDeviceAppliedSecurity,
        IDisposable
    {
        /// <summary>
        /// Uses the selected application slot, a durable secret store, certificate stores, pending signing keys,
        /// and the standard server's explicit endpoint-activation path.
        /// </summary>
        public ProvisionableDeviceSecurityInstaller(
            ISecretStore secrets,
            string scope,
            string applicationUri,
            ICertificateManager certificates,
            ICertificateStoreResolver stores,
            IPendingCertificateKeyStore pendingKeys,
            IServerApplicationCertificateActivator activator,
            ProvisionableDeviceSecurityInstallerOptions options,
            ITelemetryContext telemetry,
            IProvisionableDeviceSoftwareUpdatePolicy? softwareUpdates = null)
            : this(
                secrets, scope, applicationUri, certificates, stores, pendingKeys, activator,
                options, telemetry, softwareUpdates, null)
        {
        }

        /// <summary>
        /// Also uses an optional non-consuming provider for attempt-bound pull signing keys.
        /// </summary>
        public ProvisionableDeviceSecurityInstaller(
            ISecretStore secrets,
            string scope,
            string applicationUri,
            ICertificateManager certificates,
            ICertificateStoreResolver stores,
            IPendingCertificateKeyStore pendingKeys,
            IServerApplicationCertificateActivator activator,
            ProvisionableDeviceSecurityInstallerOptions options,
            ITelemetryContext telemetry,
            IProvisionableDeviceSoftwareUpdatePolicy? softwareUpdates,
            IProvisionableDevicePullKeyProvider? pullKeys)
        {
            m_secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            m_certificates = certificates ?? throw new ArgumentNullException(nameof(certificates));
            m_stores = stores ?? throw new ArgumentNullException(nameof(stores));
            m_pendingKeys = pendingKeys ?? throw new ArgumentNullException(nameof(pendingKeys));
            m_activator = activator ?? throw new ArgumentNullException(nameof(activator));
            m_softwareUpdates = softwareUpdates;
            m_pullKeys = pullKeys;
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (string.IsNullOrWhiteSpace(scope))
            {
                throw new ArgumentException("An exclusive device security scope is required.", nameof(scope));
            }
            if (!Uri.TryCreate(applicationUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("An absolute application URI is required.", nameof(applicationUri));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            CertificateIdentifier applicationCertificate = options.ApplicationCertificate
                ?? throw new ArgumentException("An application certificate slot is required.", nameof(options));
            if (string.IsNullOrWhiteSpace(applicationCertificate.StorePath) ||
                string.IsNullOrWhiteSpace(applicationCertificate.StoreType) ||
                applicationCertificate.CertificateType.IsNull)
            {
                throw new ArgumentException(
                    "The application certificate slot must identify a store and certificate type.",
                    nameof(options));
            }
            if (string.IsNullOrWhiteSpace(options.DeviceIdentityStorePath) ||
                string.IsNullOrWhiteSpace(options.DeviceIdentityStoreType) ||
                options.CertificateGroupId.IsNull)
            {
                throw new ArgumentException(
                    "A separate device identity store and certificate group are required.",
                    nameof(options));
            }
            if (string.Equals(applicationCertificate.StorePath, options.DeviceIdentityStorePath,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(applicationCertificate.StoreType, options.DeviceIdentityStoreType,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "The device identity private key must use a store separate from the rotating application store.",
                    nameof(options));
            }

            m_applicationStorePath = applicationCertificate.StorePath!;
            m_applicationStoreType = applicationCertificate.StoreType!;
            m_applicationCertificateType = applicationCertificate.CertificateType;
            m_identityStorePath = options.DeviceIdentityStorePath;
            m_identityStoreType = options.DeviceIdentityStoreType;
            m_certificateGroupId = options.CertificateGroupId;
            m_passwordProvider = options.CertificatePasswordProvider;
            m_applicationUri = applicationUri;
            m_context = ServiceMessageContext.Create(telemetry);
            m_defaultsIdentifier = new SecretIdentifier(
                "ProvisionableDeviceDefaults-" +
                    Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(scope))),
                secrets.StoreType);
            m_pendingKeyContext = new PendingCertificateKeyContext(
                new CertificateStoreIdentifier(
                    m_applicationStorePath,
                    m_applicationStoreType,
                    noPrivateKeys: false),
                m_certificateGroupId,
                m_applicationCertificateType,
                m_passwordProvider,
                telemetry);
        }

        /// <inheritdoc/>
        public async ValueTask PreserveDefaultsAsync(CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using CertificateEntry? current = m_certificates
                    .AcquireApplicationCertificateByType(ApplicationCertificateType);
                using Certificate identity = await EnsureDeviceIdentityAsync(current, cancellationToken)
                    .ConfigureAwait(false);
                await CaptureDefaultTrustListAsync(current, identity, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask VerifyDefaultsAsync(CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using Certificate identity = await EnsureDeviceIdentityAsync(null, cancellationToken)
                    .ConfigureAwait(false);
                using TrustListData defaults = await ReadDefaultTrustListAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ProvisionableDeviceCredentials?> CaptureAsync(
            CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using Certificate identity = await EnsureDeviceIdentityAsync(null, cancellationToken)
                    .ConfigureAwait(false);
                using TrustListData defaults = await ReadDefaultTrustListAsync(cancellationToken)
                    .ConfigureAwait(false);
                using CertificateEntry? current = m_certificates
                    .AcquireApplicationCertificateByType(ApplicationCertificateType);
                if (current == null || !current.Certificate.HasPrivateKey)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed, "The selected DCA slot has no current private key.");
                }
                if (current.Certificate.RawData.AsSpan().SequenceEqual(identity.RawData))
                {
                    return null;
                }
                ValidateCertificate(current.Certificate);
                if (m_softwareUpdates != null)
                {
                    await m_softwareUpdates.RequireReadyAsync(m_applicationUri, cancellationToken)
                        .ConfigureAwait(false);
                }
                using TrustListData trust = await m_certificates.ReadTrustListAsync(
                    TrustListIdentifier.Peers, TrustListMasks.All, cancellationToken).ConfigureAwait(false);
                return new(
                    ByteString.From(current.Certificate.RawData.AsSpan()),
                    current.IssuerChain.Select(static issuer => ByteString.From(issuer.RawData.AsSpan())).ToArrayOf(),
                    ToDataType(trust));
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask InstallAsync(
            ProvisionableDeviceCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            await InstallAsync(credentials, null, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask InstallForPullAsync(
            Guid attemptId,
            ProvisionableDeviceCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            if (attemptId == Guid.Empty)
            {
                throw new ArgumentException("A pull attempt is required.", nameof(attemptId));
            }
            await InstallAsync(credentials, attemptId, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask RequirePullKeyAsync(
            Guid attemptId,
            ByteString certificate,
            CancellationToken cancellationToken = default)
        {
            if (attemptId == Guid.Empty)
            {
                throw new ArgumentException("A pull attempt is required.", nameof(attemptId));
            }
            certificate = CopyByteString(certificate);
            using Certificate publicCertificate = Certificate.FromRawData(certificate);
            ValidateCertificate(publicCertificate);
            using Certificate paired = await CreatePullCertificateWithPrivateKeyAsync(
                attemptId, publicCertificate, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Snapshots public credentials and serializes installation with reset.
        /// </summary>
        private async ValueTask InstallAsync(
            ProvisionableDeviceCredentials credentials,
            Guid? attemptId,
            CancellationToken cancellationToken)
        {
            if (credentials == null)
            {
                throw new ArgumentNullException(nameof(credentials));
            }
            if (credentials.TrustList == null)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
            TrustListDataType trust = credentials.TrustList;
            var snapshot = new ProvisionableDeviceCredentials(
                CopyByteString(credentials.Certificate),
                CopyByteStrings(credentials.IssuerCertificates),
                new TrustListDataType
                {
                    SpecifiedLists = trust.SpecifiedLists,
                    TrustedCertificates = CopyByteStrings(trust.TrustedCertificates),
                    TrustedCrls = CopyByteStrings(trust.TrustedCrls),
                    IssuerCertificates = CopyByteStrings(trust.IssuerCertificates),
                    IssuerCrls = CopyByteStrings(trust.IssuerCrls)
                });

            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await InstallCoreAsync(snapshot, attemptId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask VerifyInstalledPullKeyAsync(
            ByteString certificate,
            CancellationToken cancellationToken = default)
        {
            if (certificate.IsEmpty)
            {
                throw new ArgumentException("The installed certificate is required.", nameof(certificate));
            }
            certificate = ByteString.From(certificate.Span);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using Certificate expected = Certificate.FromRawData(certificate);
                using CertificateEntry? current = m_certificates
                    .AcquireApplicationCertificateByType(ApplicationCertificateType);
                if (current == null || !current.Certificate.HasPrivateKey ||
                    !current.Certificate.RawData.AsSpan().SequenceEqual(certificate.Span))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed,
                        "The active DCA certificate differs from the pull receipt.");
                }
                using ICertificateStore store = OpenStore(
                    ApplicationStorePath, ApplicationStoreType, noPrivateKeys: false);
                char[]? password = PasswordFor(ApplicationCertificateIdentifier);
                try
                {
                    await RequireStoredApplicationKeyAsync(store, expected, password, cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    if (password != null)
                    {
                        Array.Clear(password, 0, password.Length);
                    }
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Copies a byte string without changing its OPC UA null value.
        /// </summary>
        private static ByteString CopyByteString(ByteString value)
        {
            return value.IsNull ? default : ByteString.From(value.Span);
        }

        /// <summary>
        /// Copies an array and its elements without changing its OPC UA null value.
        /// </summary>
        private static ArrayOf<ByteString> CopyByteStrings(ArrayOf<ByteString> values)
        {
            if (values.IsNull)
            {
                return default;
            }
            var copy = new ByteString[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                copy[i] = CopyByteString(values[i]);
            }
            return copy;
        }

        /// <inheritdoc/>
        public async ValueTask ResetToServerDefaultsAsync(CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using CertificateEntry? current = m_certificates
                    .AcquireApplicationCertificateByType(ApplicationCertificateType);
                using Certificate identity = await EnsureDeviceIdentityAsync(null, cancellationToken)
                    .ConfigureAwait(false);
                using TrustListData defaults = await ReadDefaultTrustListAsync(cancellationToken)
                    .ConfigureAwait(false);

                await m_certificates.WriteTrustListAsync(
                    TrustListIdentifier.Peers,
                    defaults,
                    TrustListMasks.All,
                    cancellationToken).ConfigureAwait(false);

                using ICertificateStore applicationStore = OpenStore(
                    ApplicationStorePath,
                    ApplicationStoreType,
                    noPrivateKeys: false);
                using CertificateCollection issuerChain = defaults.IssuerCertificates.AddRef();
                await EnsureCertificateStoredAsync(
                    applicationStore,
                    identity,
                    PasswordFor(ApplicationCertificateIdentifier),
                    cancellationToken).ConfigureAwait(false);
                await m_certificates.UpdateApplicationCertificateAsync(
                    ApplicationCertificateType,
                    identity,
                    issuerChain,
                    cancellationToken).ConfigureAwait(false);
                await m_activator.ActivateAsync(
                    ApplicationCertificateType,
                    identity.Thumbprint,
                    cancellationToken).ConfigureAwait(false);

                if (current != null &&
                    !string.Equals(
                        current.Certificate.Thumbprint,
                        identity.Thumbprint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    await applicationStore.DeleteAsync(current.Certificate.Thumbprint, cancellationToken)
                        .ConfigureAwait(false);
                }

                await m_pendingKeys.RemoveAsync(m_pendingKeyContext, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Releases the local operation gate after the host has stopped installation and reset operations.
        /// </summary>
        public void Dispose()
        {
            m_gate.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Captures the immutable key and original trust state before changing either active store.
        /// </summary>
        private async ValueTask InstallCoreAsync(
            ProvisionableDeviceCredentials credentials,
            Guid? attemptId,
            CancellationToken ct)
        {
            using Certificate publicCertificate = Certificate.FromRawData(credentials.Certificate);
            ValidateCertificate(publicCertificate);
            ValidateTrustList(credentials.TrustList);
            if (m_softwareUpdates != null)
            {
                await m_softwareUpdates.RequireReadyAsync(m_applicationUri, ct).ConfigureAwait(false);
            }
            using Certificate? pullCertificate = attemptId.HasValue
                ? await CreatePullCertificateWithPrivateKeyAsync(
                    attemptId.Value, publicCertificate, ct).ConfigureAwait(false)
                : null;

            using CertificateEntry? current = m_certificates
                .AcquireApplicationCertificateByType(ApplicationCertificateType);
            using Certificate identity = await EnsureDeviceIdentityAsync(current, ct).ConfigureAwait(false);
            await CaptureDefaultTrustListAsync(current, identity, ct).ConfigureAwait(false);

            using CertificateCollection issuers = DecodeCertificates(credentials.IssuerCertificates);
            (Certificate pairedCertificateHandle, Certificate? claimedPendingKey) = attemptId.HasValue
                ? (pullCertificate!.AddRef(), null)
                : await CreateCertificateWithPrivateKeyAsync(
                    publicCertificate, current, identity, ct).ConfigureAwait(false);
            using Certificate pairedCertificate = pairedCertificateHandle;
            using (claimedPendingKey)
            {
                try
                {
                    using TrustListData trustList = CreateTrustList(credentials.TrustList);
                    using ICertificateStore applicationStore = OpenStore(
                        ApplicationStorePath,
                        ApplicationStoreType,
                        noPrivateKeys: false);

                    await m_certificates.WriteTrustListAsync(
                        TrustListIdentifier.Peers,
                        trustList,
                        TrustListMasks.All,
                        ct).ConfigureAwait(false);
                    char[]? password = PasswordFor(ApplicationCertificateIdentifier);
                    try
                    {
                        await EnsureCertificateStoredAsync(applicationStore, pairedCertificate, password, ct)
                            .ConfigureAwait(false);
                        if (attemptId.HasValue)
                        {
                            await RequireStoredApplicationKeyAsync(applicationStore, pairedCertificate, password, ct)
                                .ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        if (password != null)
                        {
                            Array.Clear(password, 0, password.Length);
                        }
                    }

                    await m_certificates.UpdateApplicationCertificateAsync(
                        ApplicationCertificateType,
                        pairedCertificate,
                        issuers,
                        ct).ConfigureAwait(false);
                    await m_activator.ActivateAsync(
                        ApplicationCertificateType,
                        pairedCertificate.Thumbprint,
                        ct).ConfigureAwait(false);

                    if (current != null &&
                        !string.Equals(
                            current.Certificate.Thumbprint,
                            pairedCertificate.Thumbprint,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        await applicationStore.DeleteAsync(current.Certificate.Thumbprint, ct)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception installationError) when (claimedPendingKey != null)
                {
                    await RestorePendingKeyAsync(claimedPendingKey!, installationError).ConfigureAwait(false);
                    throw;
                }
            }
        }

        /// <summary>
        /// Borrows the exact attempt's independently retained key without consuming it.
        /// </summary>
        private async ValueTask<Certificate> CreatePullCertificateWithPrivateKeyAsync(
            Guid attemptId,
            Certificate certificate,
            CancellationToken ct)
        {
            IProvisionableDevicePullKeyProvider pullKeys = m_pullKeys ??
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The retained pull signing key cannot be recovered.");
            using Certificate? retained = await pullKeys.GetSigningKeyAsync(
                attemptId, ByteString.From(certificate.RawData.AsSpan()), ct).ConfigureAwait(false);
            if (retained == null || !retained.HasPrivateKey || !X509Utils.VerifyKeyPair(certificate, retained))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The retained pull signing key does not match.");
            }
            return DefaultCertificateFactory.Instance.CreateWithPrivateKey(certificate, retained);
        }

        /// <summary>
        /// Retains the first valid application certificate in its own store and never replaces that identity.
        /// </summary>
        private async ValueTask<Certificate> EnsureDeviceIdentityAsync(
            CertificateEntry? current,
            CancellationToken ct)
        {
            using ICertificateStore identityStore = OpenStore(
                m_identityStorePath,
                m_identityStoreType,
                noPrivateKeys: false);
            using CertificateCollection identities = await identityStore.EnumerateAsync(ct).ConfigureAwait(false);
            Certificate[] matching = identities
                .Where(MatchesApplication)
                .ToArray();
            if (identities.Count > 1 || matching.Length > 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "The immutable device identity store must contain only one matching certificate.");
            }
            if (matching.Length == 1)
            {
                return await LoadDeviceIdentityPrivateKeyAsync(identityStore, matching[0], ct)
                    .ConfigureAwait(false);
            }
            if (identities.Count != 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The immutable device identity store contains a certificate that does not match this device.");
            }

            if (current == null || !current.Certificate.HasPrivateKey || !IsDeviceIdentity(current.Certificate))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "The original device identity and private key are not available to retain.");
            }

            await EnsureCertificateStoredAsync(
                identityStore,
                current.Certificate,
                PasswordFor(IdentityCertificateIdentifier),
                ct).ConfigureAwait(false);
            return current.Certificate.AddRef();
        }

        /// <summary>
        /// Saves the pre-onboarding trust list once so retries and resets use the same device defaults.
        /// </summary>
        private async ValueTask CaptureDefaultTrustListAsync(
            CertificateEntry? current,
            Certificate identity,
            CancellationToken ct)
        {
            using ISecret? existing = await m_secrets.GetAsync(m_defaultsIdentifier, ct).ConfigureAwait(false);
            if (existing != null)
            {
                using TrustListData saved = DecodeTrustList(existing.Bytes);
                return;
            }

            if (current == null || !current.Certificate.RawData.AsSpan().SequenceEqual(identity.RawData))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "Original trust defaults cannot be captured after the device identity has been replaced.");
            }
            using TrustListData trustList = await m_certificates
                .ReadTrustListAsync(TrustListIdentifier.Peers, TrustListMasks.All, ct)
                .ConfigureAwait(false);
            byte[] encoded = EncodeTrustList(trustList);
            await m_secrets.SetAsync(m_defaultsIdentifier, encoded, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Loads the original trust list from the selected durable secret store.
        /// </summary>
        private async ValueTask<TrustListData> ReadDefaultTrustListAsync(CancellationToken ct)
        {
            using ISecret? secret = await m_secrets.GetAsync(m_defaultsIdentifier, ct).ConfigureAwait(false);
            if (secret == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "The original device trust list has not been captured.");
            }

            return DecodeTrustList(secret.Bytes);
        }

        /// <summary>
        /// Acquires the matching regenerated key or reuses the active or retained identity key.
        /// </summary>
        private async ValueTask<(Certificate Certificate, Certificate? ClaimedPendingKey)>
            CreateCertificateWithPrivateKeyAsync(
            Certificate certificate,
            CertificateEntry? current,
            Certificate identity,
            CancellationToken ct)
        {
            if (m_pendingKeys is not IPeekablePendingCertificateKeyStore peekable)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "Device certificate installation requires non-consuming pending-key matching.");
            }

            using Certificate? peekedKey = await peekable
                .TryPeekMatchingAsync(m_pendingKeyContext, certificate, ct)
                .ConfigureAwait(false);
            if (peekedKey != null)
            {
                if (m_pendingKeys is not IMatchingPendingCertificateKeyStore matchingStore)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "The pending-key store cannot atomically claim a matching signing key.");
                }
                Certificate? claimedPendingKey = await matchingStore.TryTakeMatchingAsync(
                    m_pendingKeyContext, certificate, ct).ConfigureAwait(false);
                if (claimedPendingKey == null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "The pending signing key changed before it could be claimed.");
                }
                using (claimedPendingKey)
                {
                    try
                    {
                        if (!X509Utils.VerifyKeyPair(certificate, claimedPendingKey))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadSecurityChecksFailed,
                                "The claimed pending signing key does not match the application certificate.");
                        }
                        return (
                            DefaultCertificateFactory.Instance.CreateWithPrivateKey(certificate, claimedPendingKey),
                            claimedPendingKey.AddRef());
                    }
                    catch (Exception pairingError)
                    {
                        await RestorePendingKeyAsync(claimedPendingKey, pairingError).ConfigureAwait(false);
                        throw;
                    }
                }
            }

            Certificate? existingKey = current?.Certificate.HasPrivateKey == true &&
                X509Utils.VerifyKeyPair(certificate, current.Certificate)
                ? current.Certificate
                : identity.HasPrivateKey && X509Utils.VerifyKeyPair(certificate, identity)
                    ? identity
                    : null;
            if (existingKey == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The device certificate matches neither the retained identity nor a pending signing key.");
            }

            return (DefaultCertificateFactory.Instance.CreateWithPrivateKey(certificate, existingKey), null);
        }

        /// <summary>
        /// Restores a claimed signing key after installation failure without replacing a newer request.
        /// </summary>
        private async ValueTask RestorePendingKeyAsync(Certificate pendingKey, Exception installationError)
        {
            try
            {
                if (m_pendingKeys is not IMatchingPendingCertificateKeyStore matchingStore)
                {
                    throw new InvalidOperationException(
                        "The pending-key store does not support restoration of a claimed signing key.");
                }
                bool restored = await matchingStore.TryRestoreAsync(
                    m_pendingKeyContext,
                    pendingKey,
                    CancellationToken.None).ConfigureAwait(false);
                if (!restored)
                {
                    throw new InvalidOperationException(
                        "The pending signing key could not be restored because its scope is no longer empty.");
                }
            }
            catch (Exception recoveryError)
            {
                throw new AggregateException(
                    "Device certificate installation failed and its pending signing key could not be restored.",
                    installationError,
                    recoveryError);
            }
        }

        /// <summary>
        /// Checks the certificate's application URI and OPC UA type before any store mutation.
        /// </summary>
        private void ValidateCertificate(Certificate certificate)
        {
            if (!CertificateIdentifier.ValidateCertificateType(certificate, ApplicationCertificateType))
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateUseNotAllowed,
                    "The application certificate does not match the configured OPC UA certificate type.");
            }
            if (!X509Utils.GetApplicationUrisFromCertificate(certificate).Contains(m_applicationUri))
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateUriInvalid,
                    "The application certificate does not match the configured application URI.");
            }
        }

        /// <summary>
        /// Determines whether the certificate belongs to this application and configured type.
        /// </summary>
        private bool IsDeviceIdentity(Certificate certificate)
        {
            return certificate.HasPrivateKey && MatchesApplication(certificate);
        }

        /// <summary>
        /// Loads and verifies the retained device private key when store enumeration returned only public data.
        /// </summary>
        private async ValueTask<Certificate> LoadDeviceIdentityPrivateKeyAsync(
            ICertificateStore identityStore,
            Certificate identity,
            CancellationToken ct)
        {
            if (identity.HasPrivateKey)
            {
                return identity.AddRef();
            }

            using Certificate? privateKey = await identityStore.LoadPrivateKeyAsync(
                identity.Thumbprint,
                subjectName: null,
                applicationUri: m_applicationUri,
                certificateType: ApplicationCertificateType,
                password: PasswordFor(IdentityCertificateIdentifier),
                ct: ct).ConfigureAwait(false);
            if (privateKey == null ||
                !privateKey.HasPrivateKey ||
                !X509Utils.VerifyKeyPair(identity, privateKey) ||
                !IsDeviceIdentity(privateKey))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The immutable device identity private key could not be loaded or verified.");
            }
            return privateKey.AddRef();
        }

        /// <summary>
        /// Checks the certificate's application URI and OPC UA type.
        /// </summary>
        private bool MatchesApplication(Certificate certificate)
        {
            return CertificateIdentifier.ValidateCertificateType(certificate, ApplicationCertificateType) &&
                X509Utils.GetApplicationUrisFromCertificate(certificate).Contains(m_applicationUri);
        }

        /// <summary>
        /// Rejects partial trust-list payloads before writing certificate stores.
        /// </summary>
        private static void ValidateTrustList(TrustListDataType trustList)
        {
            if (trustList == null || trustList.SpecifiedLists != (uint)TrustListMasks.All)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "Device installation requires a complete trust list.");
            }
        }

        /// <summary>
        /// Converts the wire trust list to owned certificate and revocation-list handles.
        /// </summary>
        private static TrustListData CreateTrustList(TrustListDataType trustList)
        {
            var decoded = new TrustListData();
            try
            {
                decoded.TrustedCertificates = DecodeCertificates(trustList.TrustedCertificates);
                decoded.IssuerCertificates = DecodeCertificates(trustList.IssuerCertificates);
                decoded.TrustedCrls = DecodeCrls(trustList.TrustedCrls);
                decoded.IssuerCrls = DecodeCrls(trustList.IssuerCrls);
                return decoded;
            }
            catch
            {
                decoded.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Decodes the supplied certificate byte strings into owned certificate handles.
        /// </summary>
        private static CertificateCollection DecodeCertificates(ArrayOf<ByteString> encoded)
        {
            var certificates = new CertificateCollection();
            try
            {
                foreach (ByteString value in encoded)
                {
                    using Certificate certificate = Certificate.FromRawData(value);
                    certificates.Add(certificate);
                }
                return certificates;
            }
            catch
            {
                certificates.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Decodes the supplied CRL byte strings into owned CRL handles.
        /// </summary>
        private static X509CRLCollection DecodeCrls(ArrayOf<ByteString> encoded)
        {
            var crls = new X509CRLCollection();
            foreach (ByteString value in encoded)
            {
                crls.Add(new X509CRL(value.ToArray()));
            }
            return crls;
        }

        /// <summary>
        /// Encodes the captured trust list as its OPC UA data type for durable reset recovery.
        /// </summary>
        private byte[] EncodeTrustList(TrustListData trustList)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, m_context, leaveOpen: true))
            {
                encoder.WriteUInt32(null, 2);
                encoder.WriteString(null, m_applicationUri);
                encoder.WriteString(null, m_applicationStoreType);
                encoder.WriteString(null, m_applicationStorePath);
                encoder.WriteString(null, m_identityStoreType);
                encoder.WriteString(null, m_identityStorePath);
                encoder.WriteNodeId(null, m_certificateGroupId);
                encoder.WriteNodeId(null, m_applicationCertificateType);
                encoder.WriteEncodeable(null, ToDataType(trustList));
            }
            if (stream.Length > kMaximumTrustListSize)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            return stream.ToArray();
        }

        /// <summary>
        /// Decodes a previously captured trust list and rejects unknown versions or trailing data.
        /// </summary>
        private TrustListData DecodeTrustList(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length > kMaximumTrustListSize)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var decoder = new BinaryDecoder(stream, m_context);
            if (decoder.ReadUInt32(null) != 2)
            {
                throw new InvalidDataException("The saved device trust-list version is invalid.");
            }
            if (decoder.ReadString(null) != m_applicationUri ||
                decoder.ReadString(null) != m_applicationStoreType ||
                decoder.ReadString(null) != m_applicationStorePath ||
                decoder.ReadString(null) != m_identityStoreType ||
                decoder.ReadString(null) != m_identityStorePath ||
                decoder.ReadNodeId(null) != m_certificateGroupId ||
                decoder.ReadNodeId(null) != m_applicationCertificateType)
            {
                throw new InvalidDataException("The saved device trust list belongs to another device scope.");
            }
            TrustListDataType encoded = decoder.ReadEncodeable<TrustListDataType>(null);
            if (decoder.Position != stream.Length)
            {
                throw new InvalidDataException("The saved device trust list contains trailing data.");
            }
            ValidateTrustList(encoded);
            return CreateTrustList(encoded);
        }

        /// <summary>
        /// Copies store contents into an OPC UA TrustListDataType without transferring their ownership.
        /// </summary>
        private static TrustListDataType ToDataType(TrustListData trustList)
        {
            return new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.All,
                TrustedCertificates = trustList.TrustedCertificates
                    .Select(certificate => certificate.RawData.ToByteString()).ToArrayOf(),
                IssuerCertificates = trustList.IssuerCertificates
                    .Select(certificate => certificate.RawData.ToByteString()).ToArrayOf(),
                TrustedCrls = trustList.TrustedCrls
                    .Select(crl => crl.RawData.ToByteString()).ToArrayOf(),
                IssuerCrls = trustList.IssuerCrls
                    .Select(crl => crl.RawData.ToByteString()).ToArrayOf()
            };
        }

        /// <summary>
        /// Opens a certificate store through the application's registered store providers.
        /// </summary>
        private ICertificateStore OpenStore(string path, string type, bool noPrivateKeys)
        {
            return m_stores.OpenCertificateStore(path, type, noPrivateKeys);
        }

        /// <summary>
        /// Reopens the exact application certificate and verifies its private key.
        /// </summary>
        private async ValueTask RequireStoredApplicationKeyAsync(
            ICertificateStore store,
            Certificate expected,
            char[]? password,
            CancellationToken ct)
        {
            if (!store.SupportsLoadPrivateKey)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The application store cannot reload private keys.");
            }
            using Certificate? stored = await store.LoadPrivateKeyAsync(
                expected.Thumbprint, null, m_applicationUri,
                ApplicationCertificateType, password, ct).ConfigureAwait(false);
            if (stored == null || !stored.HasPrivateKey ||
                !stored.RawData.AsSpan().SequenceEqual(expected.RawData) ||
                !X509Utils.VerifyKeyPair(expected, stored))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The application store did not retain the key.");
            }
        }

        /// <summary>
        /// Writes one certificate only when the store lacks a key-bearing copy of that thumbprint.
        /// </summary>
        private static async Task EnsureCertificateStoredAsync(
            ICertificateStore store,
            Certificate certificate,
            char[]? password,
            CancellationToken ct)
        {
            using CertificateCollection existing = await store.FindByThumbprintAsync(certificate.Thumbprint, ct)
                .ConfigureAwait(false);
            if (existing.Any(item => item.HasPrivateKey))
            {
                return;
            }

            if (existing.Count > 0)
            {
                await store.DeleteAsync(certificate.Thumbprint, ct).ConfigureAwait(false);
            }
            await store.AddAsync(certificate, password, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the password for the configured application certificate slot or identity store.
        /// </summary>
        private char[]? PasswordFor(CertificateIdentifier identifier)
        {
            return m_passwordProvider?.GetPassword(identifier);
        }

        /// <summary>
        /// The selected OPC UA application certificate slot.
        /// </summary>
        private CertificateIdentifier ApplicationCertificateIdentifier => new()
        {
            StorePath = m_applicationStorePath,
            StoreType = m_applicationStoreType,
            CertificateType = m_applicationCertificateType
        };

        /// <summary>
        /// The selected immutable device identity store entry.
        /// </summary>
        private CertificateIdentifier IdentityCertificateIdentifier => new()
        {
            StorePath = m_identityStorePath,
            StoreType = m_identityStoreType,
            CertificateType = m_applicationCertificateType
        };

        /// <summary>
        /// The selected OPC UA application certificate type.
        /// </summary>
        private NodeId ApplicationCertificateType => m_applicationCertificateType;

        /// <summary>
        /// The selected application certificate store path.
        /// </summary>
        private string ApplicationStorePath => m_applicationStorePath;

        /// <summary>
        /// The selected application certificate store type.
        /// </summary>
        private string ApplicationStoreType => m_applicationStoreType;

        /// <summary>The maximum retained pre-onboarding trust-list checkpoint size.</summary>
        private const int kMaximumTrustListSize = 4 * 1024 * 1024;

        /// <summary>The explicitly selected durable secret provider.</summary>
        private readonly ISecretStore m_secrets;

        /// <summary>The certificate registry, trust-list manager and lifecycle service.</summary>
        private readonly ICertificateManager m_certificates;

        /// <summary>The store resolver used for application and immutable identity stores.</summary>
        private readonly ICertificateStoreResolver m_stores;

        /// <summary>The pending generated-key store for this certificate-group/type pair.</summary>
        private readonly IPendingCertificateKeyStore m_pendingKeys;

        /// <summary>The server's explicit runtime endpoint-activation path.</summary>
        private readonly IServerApplicationCertificateActivator m_activator;

        /// <summary>The host's existing software-management readiness policy, when updates are required.</summary>
        private readonly IProvisionableDeviceSoftwareUpdatePolicy? m_softwareUpdates;

        /// <summary>The non-consuming source of attempt-bound pull signing keys, when configured.</summary>
        private readonly IProvisionableDevicePullKeyProvider? m_pullKeys;

        /// <summary>The configured application-certificate store.</summary>
        private readonly string m_applicationStorePath;

        /// <summary>The configured application-certificate store type.</summary>
        private readonly string m_applicationStoreType;

        /// <summary>The configured OPC UA application-certificate type.</summary>
        private readonly NodeId m_applicationCertificateType;

        /// <summary>The independent immutable device identity store.</summary>
        private readonly string m_identityStorePath;

        /// <summary>The independent immutable device identity store type.</summary>
        private readonly string m_identityStoreType;

        /// <summary>The certificate group that owns regenerated keys.</summary>
        private readonly NodeId m_certificateGroupId;

        /// <summary>The host-owned password provider used for both configured stores.</summary>
        private readonly ICertificatePasswordProvider? m_passwordProvider;

        /// <summary>The certificate encoding context.</summary>
        private readonly IServiceMessageContext m_context;

        /// <summary>The configured application URI.</summary>
        private readonly string m_applicationUri;

        /// <summary>The private persisted snapshot of the device's pre-onboarding trust list.</summary>
        private readonly SecretIdentifier m_defaultsIdentifier;

        /// <summary>The pending key's certificate store scope.</summary>
        private readonly PendingCertificateKeyContext m_pendingKeyContext;

        /// <summary>Serializes application and reset effects for this device instance.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);
    }
}
