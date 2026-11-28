// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using OnboardingDevice;
using Opc.Ua;
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace OnboardingClientSample
{
    /// <summary>
    /// Lifecycle state of the one request recorded in an opt-in sample signing journal.
    /// </summary>
    internal enum OnboardingSigningRequestState
    {
        /// <summary>
        /// No request is journaled.
        /// </summary>
        None = 0,

        /// <summary>
        /// A fresh key and CSR exist, but the StartSigningRequest outcome is unknown.
        /// </summary>
        Prepared = 1,

        /// <summary>
        /// The registrar returned a request identifier that is bound to the retained key.
        /// </summary>
        Started = 2,

        /// <summary>
        /// The certificate was installed; only removal of the retained key and journal remains.
        /// </summary>
        Installed = 3
    }

    /// <summary>
    /// The identifiers of a journaled request whose retained private key was proven to exist after a restart.
    /// </summary>
    internal sealed record OnboardingJournaledSigningRequest(
        PullDcaRegistrar Registrar,
        NodeId ApplicationId,
        NodeId RequestId,
        PullDcaCertificateProfile Profile);

    /// <summary>
    /// Installs the single sample DCA's RSA credential. By default the CSR uses the existing certificate-store key.
    /// With an opt-in signing journal, each request uses a fresh key held only in a dedicated certificate store,
    /// so a certificate recovered through GetCertificates can be proven to belong to exactly that request.
    /// The caller supplies an explicitly trusted registrar; this provider does not approve discovered peers.
    /// </summary>
    internal sealed class OnboardingDeviceCredentials :
        IPullDcaIdentityProvider,
        IPullDcaCredentialProvider,
        IPullDcaNewKeyPairCredentialProvider,
        IPullDcaBoundCredentialProvider,
        IPullDcaSigningKeyEvidenceProvider,
        IPullDcaSigningRecoveryProvider,
        IPullDcaSigningRequestObserver
    {
        /// <summary>
        /// Borrows the configuration and certificate manager owned by the client host.
        /// Certificate-store creation consults the supplied host providers before the built-in store types.
        /// </summary>
        internal OnboardingDeviceCredentials(
            ApplicationConfiguration configuration,
            ITelemetryContext telemetry,
            ArrayOf<ICertificateStoreProvider> storeProviders = default)
        {
            m_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_manager = configuration.CertificateManager ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            m_storeProviders = storeProviders;
        }

        /// <inheritdoc/>
        public async ValueTask ActivateAsync(
            ByteString selectedIdentity,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using Certificate selected = Certificate.FromRawData(selectedIdentity);
            CertificateIdentifier current = GetIdentifier();
            var identifier = new CertificateIdentifier
            {
                StoreType = current.StoreType,
                StorePath = current.StorePath,
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType,
                Thumbprint = selected.Thumbprint
            };
            using Certificate? local = await m_manager.CertificateProvider.GetPrivateKeyCertificateAsync(
                identifier, m_configuration.SecurityConfiguration.CertificatePasswordProvider,
                ct: cancellationToken).ConfigureAwait(false);
            if (local == null || !local.HasPrivateKey || !local.RawData.AsSpan().SequenceEqual(selectedIdentity.Span))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The selected identity's private key is not in this DCA.");
            }
            await m_manager.UpdateApplicationCertificateAsync(
                ObjectTypeIds.RsaSha256ApplicationCertificateType, local, ct: cancellationToken).ConfigureAwait(false);
            current.RawData = null;
            current.Thumbprint = local.Thumbprint;
            current.SubjectName = local.Subject;
        }

        /// <inheritdoc/>
        public ValueTask<ByteString> CreateSigningRequestAsync(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            CancellationToken cancellationToken = default)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (applicationId.IsNull || profile.CertificateGroupId.IsNull ||
                profile.CertificateTypeId != ObjectTypeIds.RsaSha256ApplicationCertificateType ||
                (!m_applicationId.IsNull && (m_applicationId != applicationId || m_profile != profile)))
            {
                throw new ServiceResultException(
                    StatusCodes.BadRequestNotAllowed, "This sample credential provider serves only one RSA DCA.");
            }
            if (m_journal != null)
            {
                return CreateJournaledSigningRequestAsync(applicationId, profile, cancellationToken);
            }
            return new ValueTask<ByteString>(CreateSigningRequest(applicationId, profile));
        }

        /// <summary>
        /// Creates a request using the locally active key while retaining its private-key handle through encoding.
        /// </summary>
        private ByteString CreateSigningRequest(NodeId applicationId, PullDcaCertificateProfile profile)
        {
            using CertificateEntry entry = m_manager.AcquireApplicationCertificateByType(
                profile.CertificateTypeId) ?? throw new ServiceResultException(StatusCodes.BadConfigurationError);
            if (!entry.Certificate.HasPrivateKey)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            ByteString csr = ByteString.From(
                DefaultCertificateFactory.Instance.CreateSigningRequest(entry.Certificate));
            m_applicationId = applicationId;
            m_profile = profile;
            m_signingCertificate = ByteString.From(entry.Certificate.RawData.AsSpan());
            m_signingRequest = ByteString.From(csr.Span);
            return csr;
        }

        /// <inheritdoc/>
        public async ValueTask<ByteString> GetSigningKeyCertificateAsync(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            ByteString signingRequest,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (m_journal == null)
            {
                if (applicationId != m_applicationId || profile != m_profile ||
                    signingRequest != m_signingRequest || m_signingCertificate.IsEmpty)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }
                return ByteString.From(m_signingCertificate.Span);
            }
            SigningJournal journal = EnterJournal();
            try
            {
                JournalEntry entry = await ReadEntryAsync(journal, cancellationToken).ConfigureAwait(false) ??
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                if (entry.ApplicationId != applicationId || entry.Profile != profile ||
                    entry.SigningRequest != signingRequest)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }
                return ByteString.From(entry.SigningCertificate.Span);
            }
            finally
            {
                ExitJournal();
            }
        }

        /// <summary>
        /// Restores the host-retained request binding only after proving that its original key remains available.
        /// The checkpoint must originate from this device, not from a remote registrar or discovery response.
        /// This does not establish that a certificate returned by GetCertificates is new for that request;
        /// use <see cref="UseSigningJournal"/> for request-bound recovery.
        /// </summary>
        internal ValueTask RestoreSigningRequestAsync(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            ByteString signingKeyCertificate,
            CancellationToken cancellationToken = default)
        {
            RestoreSigningRequest(applicationId, profile, signingKeyCertificate, cancellationToken);
            return default;
        }

        /// <summary>
        /// Checks the retained local key synchronously before restoring the application's request binding.
        /// </summary>
        private void RestoreSigningRequest(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            ByteString signingKeyCertificate,
            CancellationToken cancellationToken)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (m_journal != null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "Journaled requests are restored from the signing journal.");
            }
            if (applicationId.IsNull || profile.CertificateGroupId.IsNull ||
                profile.CertificateTypeId != ObjectTypeIds.RsaSha256ApplicationCertificateType ||
                (!m_applicationId.IsNull && (m_applicationId != applicationId || m_profile != profile)))
            {
                throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            }
            using Certificate original = Certificate.FromRawData(signingKeyCertificate);
            if (!X509Utils.GetApplicationUrisFromCertificate(original).Contains(m_configuration.ApplicationUri))
            {
                throw new ServiceResultException(StatusCodes.BadCertificateUriInvalid);
            }
            using CertificateEntry key = m_manager.AcquireApplicationCertificateByType(
                profile.CertificateTypeId) ?? throw new ServiceResultException(StatusCodes.BadConfigurationError);
            if (!key.Certificate.HasPrivateKey)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            using Certificate proof = DefaultCertificateFactory.Instance.CreateWithPrivateKey(
                original, key.Certificate);
            if (!proof.HasPrivateKey)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            m_applicationId = applicationId;
            m_profile = profile;
        }

        /// <inheritdoc/>
        public async ValueTask InstallAsync(
            NodeId applicationId,
            PullDcaCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            if (credentials == null)
            {
                throw new ArgumentNullException(nameof(credentials));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (m_journal != null)
            {
                await InstallJournaledAsync(applicationId, credentials, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (applicationId.IsNull || applicationId != m_applicationId)
            {
                throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            }
            using Certificate issued = Certificate.FromRawData(credentials.Certificate);
            if (!X509Utils.GetApplicationUrisFromCertificate(issued).Contains(m_configuration.ApplicationUri))
            {
                throw new ServiceResultException(StatusCodes.BadCertificateUriInvalid);
            }
            using CertificateEntry key = m_manager.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType) ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            using Certificate combined = DefaultCertificateFactory.Instance.CreateWithPrivateKey(
                issued, key.Certificate);
            await InstallCombinedAsync(issued, combined, credentials, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask InstallNewKeyPairAsync(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            PullDcaCredentials credentials,
            ByteString privateKeyPfx,
            CancellationToken cancellationToken = default)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            if (credentials == null)
            {
                throw new ArgumentNullException(nameof(credentials));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (applicationId.IsNull || profile.CertificateGroupId.IsNull ||
                profile.CertificateTypeId != ObjectTypeIds.RsaSha256ApplicationCertificateType ||
                (!m_applicationId.IsNull && (m_applicationId != applicationId || m_profile != profile)))
            {
                throw new ServiceResultException(
                    StatusCodes.BadRequestNotAllowed, "This sample credential provider serves one RSA DCA.");
            }
            if (credentials.Certificate.IsEmpty || privateKeyPfx.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }

            using Certificate issued = Certificate.FromRawData(credentials.Certificate);
            if (!X509Utils.GetApplicationUrisFromCertificate(issued).Contains(m_configuration.ApplicationUri) ||
                !CertificateIdentifier.ValidateCertificateType(
                    issued, ObjectTypeIds.RsaSha256ApplicationCertificateType))
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            byte[] pfxBytes = privateKeyPfx.ToArray();
            try
            {
                using Certificate returnedKeyPair = X509Utils.CreateCertificateFromPKCS12(
                    pfxBytes, ReadOnlySpan<char>.Empty);
                if (!returnedKeyPair.HasPrivateKey ||
                    !returnedKeyPair.RawData.AsSpan().SequenceEqual(issued.RawData))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed,
                        "The returned PFX does not contain the issued certificate and its private key.");
                }
                using Certificate combined = DefaultCertificateFactory.Instance.CreateWithPrivateKey(
                    issued, returnedKeyPair);
                await InstallCombinedAsync(issued, combined, credentials, cancellationToken, verifyPersistedKey: true)
                    .ConfigureAwait(false);
                if (!await HasAppliedPullCredentialsAsync(credentials, cancellationToken).ConfigureAwait(false))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed, "The new DCA credentials could not be verified.");
                }
            }
            finally
            {
                Array.Clear(pfxBytes, 0, pfxBytes.Length);
            }
            m_applicationId = applicationId;
            m_profile = profile;
        }

        /// <summary>
        /// Binds standalone installation to the received attempt and records completion after verifying applied stores.
        /// </summary>
        public async ValueTask InstallAsync(
            PullDcaSigningBinding binding,
            PullDcaCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }
            if (credentials == null)
            {
                throw new ArgumentNullException(nameof(credentials));
            }
            cancellationToken.ThrowIfCancellationRequested();
            credentials = SnapshotCredentials(credentials);
            DevicePullJournal? journal = m_pullJournal;
            if (journal == null)
            {
                await InstallAsync(binding.ApplicationId, credentials, cancellationToken).ConfigureAwait(false);
                return;
            }
            EnterPullInstallation();
            try
            {
                using CertificateCollection registrar = DefaultCertificateFactory.Instance.ParseChainBlob(
                    binding.RegistrarChannelCertificate.Memory);
                if (registrar.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
                }
                ByteString leaf = ByteString.From(registrar[0].RawData.AsSpan());
                await journal.RequireReceivedAsync(binding.AttemptId, leaf, credentials, cancellationToken)
                    .ConfigureAwait(false);
                await InstallAsync(binding.ApplicationId, credentials, cancellationToken).ConfigureAwait(false);
                if (!await HasAppliedPullCredentialsAsync(credentials, cancellationToken).ConfigureAwait(false))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed, "The applied credentials do not match the pull receipt.");
                }
                await journal.RecordInstalledAsync(binding.AttemptId, leaf, credentials, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref m_pullInstallationActive, 0);
            }
        }

        /// <summary>
        /// Selects the durable public channel and receipt journal used by this standalone host.
        /// </summary>
        internal void UsePullJournal(DevicePullJournal journal)
        {
            m_pullJournal = journal ?? throw new ArgumentNullException(nameof(journal));
        }

        /// <summary>
        /// Promotes a received receipt only when the application certificate and every trust-store set already match.
        /// </summary>
        internal async ValueTask<bool> RecoverAppliedPullReceiptAsync(
            DevicePullJournalCheckpoint checkpoint,
            CancellationToken cancellationToken)
        {
            if (checkpoint == null)
            {
                throw new ArgumentNullException(nameof(checkpoint));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (checkpoint.Stage is not (DevicePullJournalStage.Received or DevicePullJournalStage.Installed))
            {
                return false;
            }
            PullDcaCredentials credentials = SnapshotCredentials(checkpoint.Credentials ??
                throw new InvalidDataException("The retained receipt has no complete credentials."));
            using Certificate receivedCertificate = Certificate.FromRawData(credentials.Certificate);
            using Certificate retainedSigningKey = Certificate.FromRawData(checkpoint.Binding.SigningKeyCertificate);
            if (!X509Utils.GetApplicationUrisFromCertificate(receivedCertificate)
                .Contains(m_configuration.ApplicationUri))
            {
                throw new ServiceResultException(StatusCodes.BadCertificateUriInvalid);
            }
            if (!CertificateIdentifier.ValidateCertificateType(
                receivedCertificate, ObjectTypeIds.RsaSha256ApplicationCertificateType))
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            if (!HasSamePublicKey(receivedCertificate, retainedSigningKey))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The retained receipt differs from the signing attempt.");
            }
            EnterPullInstallation();
            try
            {
                bool applied = await HasAppliedPullCredentialsAsync(credentials, cancellationToken)
                    .ConfigureAwait(false);
                if (!applied)
                {
                    applied = await CompleteStoredPullActivationAsync(
                        checkpoint.Binding, credentials, cancellationToken).ConfigureAwait(false);
                }
                if (!applied)
                {
                    if (checkpoint.Stage == DevicePullJournalStage.Installed)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "The installed pull receipt does not match the stores.");
                    }
                    return false;
                }
                using CertificateCollection registrar = DefaultCertificateFactory.Instance.ParseChainBlob(
                    checkpoint.Binding.RegistrarChannelCertificate.Memory);
                if (registrar.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
                }
                DevicePullJournal journal = m_pullJournal ??
                    throw new InvalidOperationException("The durable pull journal is not configured.");
                await journal.RecordInstalledAsync(
                    checkpoint.Binding.AttemptId,
                    ByteString.From(registrar[0].RawData.AsSpan()),
                    credentials,
                    cancellationToken).ConfigureAwait(false);
                return true;
            }
            finally
            {
                Volatile.Write(ref m_pullInstallationActive, 0);
            }
        }

        /// <summary>
        /// Compares installed public material and the retained private key with the exact received credentials.
        /// </summary>
        private async ValueTask<bool> HasAppliedPullCredentialsAsync(
            PullDcaCredentials credentials,
            CancellationToken cancellationToken)
        {
            using CertificateEntry? identity = m_manager.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType);
            if (identity == null || !identity.Certificate.HasPrivateKey ||
                !identity.Certificate.RawData.AsSpan().SequenceEqual(credentials.Certificate.Span))
            {
                return false;
            }
            using Certificate? persisted = await FindPersistedPullIdentityAsync(identity.Certificate, cancellationToken)
                .ConfigureAwait(false);
            if (persisted == null || !persisted.HasPrivateKey ||
                !persisted.RawData.AsSpan().SequenceEqual(credentials.Certificate.Span))
            {
                return false;
            }
            if (!X509Utils.VerifyKeyPair(identity.Certificate, persisted))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The persisted DCA private key does not match its certificate.");
            }
            using CertificateCollection expectedChain = await ValidateChainAsync(
                identity.Certificate, credentials, cancellationToken).ConfigureAwait(false);
            using TrustListData applied = await m_manager.ReadTrustListAsync(
                TrustListIdentifier.Peers, TrustListMasks.All, cancellationToken).ConfigureAwait(false);
            using TrustListData expected = DecodeTrustList(credentials.TrustList);
            return SameMaterial(identity.IssuerChain.Select(certificate => certificate.RawData),
                    expectedChain.Skip(1).Select(certificate => certificate.RawData)) &&
                SameTrustMaterial(applied, expected);
        }

        /// <summary>
        /// Completes interrupted activation only from an exact persisted key pair and matching complete trust stores.
        /// </summary>
        private async ValueTask<bool> CompleteStoredPullActivationAsync(
            PullDcaSigningBinding binding,
            PullDcaCredentials credentials,
            CancellationToken cancellationToken)
        {
            using Certificate issued = Certificate.FromRawData(credentials.Certificate);
            CertificateIdentifier current = GetIdentifier();
            using Certificate? persisted = await FindPersistedPullIdentityAsync(issued, cancellationToken)
                .ConfigureAwait(false);
            if (persisted == null || !persisted.HasPrivateKey ||
                !persisted.RawData.AsSpan().SequenceEqual(credentials.Certificate.Span))
            {
                return false;
            }
            if (!X509Utils.VerifyKeyPair(issued, persisted))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The persisted DCA private key does not match its certificate.");
            }
            using Certificate signingKey = Certificate.FromRawData(binding.SigningKeyCertificate);
            if (!HasSamePublicKey(persisted, signingKey))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The persisted key pair differs from the signing attempt.");
            }
            using CertificateCollection chain = await ValidateChainAsync(
                issued, credentials, cancellationToken).ConfigureAwait(false);
            using TrustListData applied = await m_manager.ReadTrustListAsync(
                TrustListIdentifier.Peers, TrustListMasks.All, cancellationToken).ConfigureAwait(false);
            using TrustListData expected = DecodeTrustList(credentials.TrustList);
            if (!SameTrustMaterial(applied, expected))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The persisted issued key pair has different trust stores; recovery did not replace them.");
            }
            using var issuers = new CertificateCollection();
            for (int i = 1; i < chain.Count; i++)
            {
                issuers.Add(chain[i]);
            }
            await m_manager.UpdateApplicationCertificateAsync(
                ObjectTypeIds.RsaSha256ApplicationCertificateType, persisted, issuers, cancellationToken)
                .ConfigureAwait(false);
            current.RawData = null;
            current.Thumbprint = persisted.Thumbprint;
            current.SubjectName = persisted.Subject;
            if (!await HasAppliedPullCredentialsAsync(credentials, cancellationToken).ConfigureAwait(false))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The completed activation does not match the pull receipt.");
            }
            return true;
        }

        /// <summary>
        /// Reopens the exact receipt identity from the configured application certificate store.
        /// </summary>
        private ValueTask<Certificate?> FindPersistedPullIdentityAsync(
            Certificate issued,
            CancellationToken cancellationToken)
        {
            CertificateIdentifier current = GetIdentifier();
            var identifier = new CertificateIdentifier
            {
                StoreType = current.StoreType,
                StorePath = current.StorePath,
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType,
                Thumbprint = issued.Thumbprint
            };
            return m_manager.CertificateProvider.GetPrivateKeyCertificateAsync(
                identifier, m_configuration.SecurityConfiguration.CertificatePasswordProvider,
                ct: cancellationToken);
        }

        /// <summary>
        /// Compares every certificate and CRL store set without relying on enumeration order.
        /// </summary>
        private static bool SameTrustMaterial(TrustListData applied, TrustListData expected)
        {
            return SameMaterial(applied.TrustedCertificates.Select(certificate => certificate.RawData),
                    expected.TrustedCertificates.Select(certificate => certificate.RawData)) &&
                SameMaterial(applied.IssuerCertificates.Select(certificate => certificate.RawData),
                    expected.IssuerCertificates.Select(certificate => certificate.RawData)) &&
                SameMaterial(applied.TrustedCrls.Select(crl => crl.RawData),
                    expected.TrustedCrls.Select(crl => crl.RawData)) &&
                SameMaterial(applied.IssuerCrls.Select(crl => crl.RawData),
                    expected.IssuerCrls.Select(crl => crl.RawData));
        }

        /// <summary>
        /// Compares complete certificate or CRL sets independently of store enumeration order.
        /// </summary>
        private static bool SameMaterial(IEnumerable<byte[]> actual, IEnumerable<byte[]> expected)
        {
            return actual.Select(Convert.ToBase64String).OrderBy(value => value, StringComparer.Ordinal)
                .SequenceEqual(expected.Select(Convert.ToBase64String)
                    .OrderBy(value => value, StringComparer.Ordinal));
        }

        /// <summary>
        /// Detaches every credential byte array before installation awaits journal or store operations.
        /// </summary>
        private static PullDcaCredentials SnapshotCredentials(PullDcaCredentials credentials)
        {
            TrustListDataType trust = credentials.TrustList ??
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "A complete trust list is required.");
            return new PullDcaCredentials(
                ByteString.From(credentials.Certificate.Span),
                CopyBytes(credentials.IssuerCertificates),
                new TrustListDataType
                {
                    SpecifiedLists = trust.SpecifiedLists,
                    TrustedCertificates = CopyBytes(trust.TrustedCertificates),
                    IssuerCertificates = CopyBytes(trust.IssuerCertificates),
                    TrustedCrls = CopyBytes(trust.TrustedCrls),
                    IssuerCrls = CopyBytes(trust.IssuerCrls)
                });
        }

        /// <summary>
        /// Copies each byte string while preserving OPC UA null-array semantics.
        /// </summary>
        private static ArrayOf<ByteString> CopyBytes(ArrayOf<ByteString> values)
        {
            if (values.IsNull)
            {
                return default;
            }
            var detached = new ByteString[values.Count];
            for (int i = 0; i < detached.Length; i++)
            {
                detached[i] = values[i].IsNull ? default : ByteString.From(values[i].Span);
            }
            return detached;
        }

        /// <summary>
        /// Rejects overlapping standalone installation or interrupted-install verification.
        /// </summary>
        private void EnterPullInstallation()
        {
            if (Interlocked.CompareExchange(ref m_pullInstallationActive, 1, 0) != 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "A pull installation is already active.");
            }
        }

        /// <summary>
        /// Validates the external leaf against locally approved enrollment trust and the retained CSR key.
        /// Call before confirming enrollment; this does not install a credential or change peer trust.
        /// </summary>
        internal async ValueTask ValidateExternalCredentialAsync(
            ByteString certificate,
            Certificate retainedKey,
            CertificateValidationScope enrollmentTrust,
            CancellationToken ct = default)
        {
            if (retainedKey == null)
            {
                throw new ArgumentNullException(nameof(retainedKey));
            }
            if (enrollmentTrust == null)
            {
                throw new ArgumentNullException(nameof(enrollmentTrust));
            }
            ct.ThrowIfCancellationRequested();
            using Certificate issued = LoadExternalLeaf(certificate);
            ValidateRetainedKeyBinding(issued, retainedKey);
            using var chain = new CertificateCollection { issued };
            await ValidateScopedChainAsync(chain, enrollmentTrust, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Installs the exact host-validated external leaf with its retained CSR key without changing peer trust.
        /// The external protocol must authenticate completion before calling this method.
        /// </summary>
        internal async ValueTask InstallCompletedExternalCredentialAsync(
            ByteString approvedCertificate,
            Certificate retainedKey,
            CertificateCollection issuerChain,
            CancellationToken ct = default)
        {
            if (retainedKey == null)
            {
                throw new ArgumentNullException(nameof(retainedKey));
            }
            if (issuerChain == null)
            {
                throw new ArgumentNullException(nameof(issuerChain));
            }
            ct.ThrowIfCancellationRequested();
            using Certificate issued = LoadExternalLeaf(approvedCertificate);
            using Certificate combined = BindToRetainedKey(issued, retainedKey);
            CertificateIdentifier identifier = GetIdentifier();
            await PersistApplicationCertificateAsync(
                    identifier, combined, verifyPersistence: true, verifyPersistedKey: true, ct)
                .ConfigureAwait(false);
            await ActivateInstalledCertificateAsync(identifier, combined, issuerChain, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Enables request-bound signing recovery for this single-writer device. Each new request then uses a fresh
        /// RSA key that is stored only in <paramref name="signingKeyStore"/>. The journal at <paramref name="path"/>
        /// holds public data only: the key's certificate, the CSR and the URI-qualified registrar, application,
        /// group, type and request identifiers. Exactly one process may own the journal path; the journal does not
        /// support concurrent or highly available writers.
        /// </summary>
        /// <param name="path">The journal file path.</param>
        /// <param name="signingKeyStore">A dedicated store, separate from the application certificate store.</param>
        /// <param name="namespaces">The registrar session's namespace table, used to qualify identifiers.</param>
        /// <param name="fileSystem">The file system that publishes the journal; the local one by default.</param>
        internal void UseSigningJournal(
            string path,
            CertificateStoreIdentifier signingKeyStore,
            NamespaceTable namespaces,
            IFileSystem? fileSystem = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException(nameof(path));
            }
            if (signingKeyStore == null)
            {
                throw new ArgumentNullException(nameof(signingKeyStore));
            }
            if (namespaces == null)
            {
                throw new ArgumentNullException(nameof(namespaces));
            }
            if (string.IsNullOrEmpty(signingKeyStore.StorePath) || string.IsNullOrEmpty(signingKeyStore.StoreType))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            CertificateIdentifier application = GetIdentifier();
            if (string.Equals(application.StorePath, signingKeyStore.StorePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "Request signing keys need a store separate from the application certificate store.");
            }
            if (m_journal != null || !m_applicationId.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "A signing request or journal is already configured.");
            }
            m_journal = new SigningJournal(
                path, fileSystem ?? LocalFileSystem.Instance, signingKeyStore, namespaces, m_telemetry);
        }

        /// <summary>
        /// Reads the state of the journaled request without changing it.
        /// </summary>
        internal async ValueTask<OnboardingSigningRequestState> GetSigningRequestStateAsync(
            CancellationToken cancellationToken = default)
        {
            SigningJournal journal = EnterJournal();
            try
            {
                JournalEntry? entry = await ReadEntryAsync(journal, cancellationToken).ConfigureAwait(false);
                return entry?.State ?? OnboardingSigningRequestState.None;
            }
            finally
            {
                ExitJournal();
            }
        }

        /// <summary>
        /// Restores a started request after a restart. It proves that the retained key is still in the signing-key
        /// store and that the request was started at <paramref name="registrar"/>. A prepared request whose
        /// StartSigningRequest outcome is unknown is rejected: discard it, never start it again with the same key.
        /// </summary>
        internal async ValueTask<OnboardingJournaledSigningRequest> RestoreJournaledSigningRequestAsync(
            PullDcaRegistrar registrar,
            CancellationToken cancellationToken = default)
        {
            if (registrar == null)
            {
                throw new ArgumentNullException(nameof(registrar));
            }
            SigningJournal journal = EnterJournal();
            try
            {
                JournalEntry entry = await ReadEntryAsync(journal, cancellationToken).ConfigureAwait(false) ??
                    throw new ServiceResultException(StatusCodes.BadNotFound, "No signing request is journaled.");
                if (entry.State == OnboardingSigningRequestState.Prepared)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "The StartSigningRequest outcome is unknown. Discard the request; never restart it.");
                }
                if (entry.State != OnboardingSigningRequestState.Started)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The journaled request is installed; discard it to clean up.");
                }
                if (entry.Registrar != registrar)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed, "The request was started at another registrar.");
                }
                using Certificate retained = await LoadRetainedKeyAsync(journal, entry, cancellationToken)
                    .ConfigureAwait(false);
                m_applicationId = entry.ApplicationId;
                m_profile = entry.Profile;
                return new OnboardingJournaledSigningRequest(
                    registrar, entry.ApplicationId, entry.RequestId, entry.Profile);
            }
            finally
            {
                ExitJournal();
            }
        }

        /// <summary>
        /// Explicitly abandons the journaled request: deletes its retained key, then its journal entry.
        /// Use it when the StartSigningRequest outcome is unknown or a started request must not be completed.
        /// A request that the registrar already accepted stays there; only a new request with a new key follows.
        /// </summary>
        internal async ValueTask DiscardSigningRequestAsync(CancellationToken cancellationToken = default)
        {
            SigningJournal journal = EnterJournal();
            try
            {
                JournalEntry? entry = await ReadEntryAsync(journal, cancellationToken).ConfigureAwait(false);
                if (entry != null)
                {
                    await RemoveRequestAsync(journal, entry, cancellationToken).ConfigureAwait(false);
                }
                m_applicationId = NodeId.Null;
                m_profile = null;
            }
            finally
            {
                ExitJournal();
            }
        }

        /// <inheritdoc/>
        public async ValueTask OnSigningRequestStartedAsync(
            PullDcaRegistrar registrar,
            NodeId applicationId,
            NodeId requestId,
            PullDcaCertificateProfile profile,
            ByteString signingRequest,
            CancellationToken cancellationToken = default)
        {
            if (registrar == null)
            {
                throw new ArgumentNullException(nameof(registrar));
            }
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            if (requestId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            SigningJournal journal = EnterJournal();
            try
            {
                JournalEntry entry = await ReadEntryAsync(journal, cancellationToken).ConfigureAwait(false) ??
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "No signing request is prepared.");
                if (entry.State != OnboardingSigningRequestState.Prepared)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The journaled key is already bound to a request.");
                }
                if (entry.ApplicationId != applicationId || entry.Profile != profile ||
                    !entry.SigningRequest.Span.SequenceEqual(signingRequest.Span))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed, "The request does not match the journaled CSR.");
                }
                JournalEntry started = entry with
                {
                    State = OnboardingSigningRequestState.Started,
                    Registrar = registrar,
                    RequestId = requestId
                };
                await journal.WriteAsync(started, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ExitJournal();
            }
        }

        /// <inheritdoc/>
        public async ValueTask ValidateAsync(
            PullDcaRegistrar registrar,
            NodeId applicationId,
            NodeId requestId,
            PullDcaCertificateProfile profile,
            PullDcaCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            if (registrar == null)
            {
                throw new ArgumentNullException(nameof(registrar));
            }
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            if (credentials == null)
            {
                throw new ArgumentNullException(nameof(credentials));
            }
            SigningJournal journal = EnterJournal();
            try
            {
                JournalEntry entry = await ReadEntryAsync(journal, cancellationToken).ConfigureAwait(false) ??
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "No signing request is journaled.");
                if (entry.State != OnboardingSigningRequestState.Started || entry.Registrar != registrar ||
                    entry.ApplicationId != applicationId || entry.RequestId != requestId || entry.Profile != profile)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed,
                        "The recovered certificate is not bound to the journaled signing request.");
                }
                using Certificate retained = await LoadRetainedKeyAsync(journal, entry, cancellationToken)
                    .ConfigureAwait(false);
                using Certificate issued = Certificate.FromRawData(credentials.Certificate);
                using Certificate combined = BindToRetainedKey(issued, retained);
                using CertificateCollection chain = await ValidateChainAsync(issued, credentials, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                ExitJournal();
            }
        }

        /// <summary>
        /// Creates a fresh request key, records it as prepared, then stores its private key. A store failure removes
        /// the entry again, so no CSR is returned for a key that is not retained.
        /// </summary>
        private async ValueTask<ByteString> CreateJournaledSigningRequestAsync(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            CancellationToken cancellationToken)
        {
            SigningJournal journal = EnterJournal();
            try
            {
                if (await ReadEntryAsync(journal, cancellationToken).ConfigureAwait(false) != null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "A journaled signing request exists; resume or discard it.");
                }
                using CertificateEntry identity = m_manager.AcquireApplicationCertificateByType(
                    profile.CertificateTypeId) ?? throw new ServiceResultException(StatusCodes.BadConfigurationError);
                using Certificate key = CertificateBuilder.Create(identity.Certificate.SubjectName)
                    .AddExtension(new X509SubjectAltNameExtension(
                        m_configuration.ApplicationUri ?? throw new ServiceResultException(
                            StatusCodes.BadConfigurationError, "The DCA application URI is missing."),
                        X509Utils.GetDomainsFromCertificate(identity.Certificate).ToArray() ?? []))
                    .SetLifeTime(kSigningKeyLifeTimeMonths)
                    .SetRSAKeySize(kSigningKeySize)
                    .CreateForRSA();
                ByteString csr = ByteString.From(DefaultCertificateFactory.Instance.CreateSigningRequest(key));
                var entry = new JournalEntry(
                    OnboardingSigningRequestState.Prepared,
                    m_configuration.ApplicationUri,
                    ByteString.From(key.RawData.AsSpan()),
                    csr,
                    applicationId,
                    profile,
                    null,
                    NodeId.Null);
                await journal.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
                try
                {
                    using ICertificateStore store = journal.OpenKeyStore(m_storeProviders);
                    char[] password = GetSigningKeyPassword(journal, key.Thumbprint);
                    try
                    {
                        await store.AddAsync(key, password, cancellationToken).ConfigureAwait(false);
                        using Certificate? persisted = await store.LoadPrivateKeyAsync(
                            key.Thumbprint, null, null, profile.CertificateTypeId, password, cancellationToken)
                            .ConfigureAwait(false);
                        if (persisted == null || !persisted.HasPrivateKey ||
                            !persisted.RawData.AsSpan().SequenceEqual(key.RawData))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadConfigurationError, "The signing store did not retain the private key.");
                        }
                    }
                    finally
                    {
                        Array.Clear(password, 0, password.Length);
                    }
                }
                catch (Exception error)
                {
                    try
                    {
                        await RemoveRequestAsync(journal, entry, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception cleanupError)
                    {
                        throw new AggregateException(
                            "Retaining the request key and removing its journal entry both failed.",
                            error, cleanupError);
                    }
                    throw;
                }
                m_applicationId = applicationId;
                m_profile = profile;
                return csr;
            }
            finally
            {
                ExitJournal();
            }
        }

        /// <summary>
        /// Installs a certificate only for the started request with the retained key, then marks the request
        /// installed and removes the retained key and journal entry.
        /// </summary>
        private async ValueTask InstallJournaledAsync(
            NodeId applicationId,
            PullDcaCredentials credentials,
            CancellationToken cancellationToken)
        {
            SigningJournal journal = EnterJournal();
            try
            {
                JournalEntry entry = await ReadEntryAsync(journal, cancellationToken).ConfigureAwait(false) ??
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "No signing request is journaled.");
                if (entry.State != OnboardingSigningRequestState.Started || entry.ApplicationId != applicationId)
                {
                    throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
                }
                using Certificate retained = await LoadRetainedKeyAsync(journal, entry, cancellationToken)
                    .ConfigureAwait(false);
                using Certificate issued = Certificate.FromRawData(credentials.Certificate);
                using Certificate combined = BindToRetainedKey(issued, retained);
                await InstallCombinedAsync(issued, combined, credentials, cancellationToken).ConfigureAwait(false);
                JournalEntry installed = entry with { State = OnboardingSigningRequestState.Installed };
                await journal.WriteAsync(installed, cancellationToken).ConfigureAwait(false);
                await RemoveRequestAsync(journal, installed, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ExitJournal();
            }
        }

        /// <summary>
        /// Validates the chain against the complete received trust list, then stores the key pair, replaces the
        /// trust list and activates the certificate.
        /// </summary>
        private async ValueTask InstallCombinedAsync(
            Certificate issued,
            Certificate combined,
            PullDcaCredentials credentials,
            CancellationToken cancellationToken,
            bool verifyPersistedKey = false)
        {
            using CertificateCollection chain = await ValidateChainAsync(issued, credentials, cancellationToken)
                .ConfigureAwait(false);
            using TrustListData trust = DecodeTrustList(credentials.TrustList);
            CertificateIdentifier identifier = GetIdentifier();
            await PersistApplicationCertificateAsync(
                    identifier, combined, m_journal != null || verifyPersistedKey, verifyPersistedKey,
                    cancellationToken)
                .ConfigureAwait(false);
            await m_manager.WriteTrustListAsync(
                TrustListIdentifier.Peers, trust, TrustListMasks.All, cancellationToken).ConfigureAwait(false);
            using var issuers = new CertificateCollection();
            for (int i = 1; i < chain.Count; i++)
            {
                issuers.Add(chain[i]);
            }
            await ActivateInstalledCertificateAsync(identifier, combined, issuers, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Writes the key pair to the configured store and optionally checks the persisted certificate and key.
        /// </summary>
        private async ValueTask PersistApplicationCertificateAsync(
            CertificateIdentifier identifier,
            Certificate combined,
            bool verifyPersistence,
            bool verifyPersistedKey,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(identifier.StorePath) || string.IsNullOrEmpty(identifier.StoreType))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            using ICertificateStore store = CertificateStoreIdentifier.CreateStore(
                identifier.StoreType, m_telemetry, m_storeProviders.ToArray());
            store.Open(identifier.StorePath, noPrivateKeys: false);
            char[] password = m_configuration.SecurityConfiguration.CertificatePasswordProvider?
                .GetPassword(identifier) ?? [];
            try
            {
                if (!verifyPersistence)
                {
                    await store.AddAsync(combined, password, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    using CertificateCollection existing = await store.FindByThumbprintAsync(
                        combined.Thumbprint, cancellationToken).ConfigureAwait(false);
                    if (existing.Count == 0)
                    {
                        await store.AddAsync(combined, password, cancellationToken).ConfigureAwait(false);
                    }
                    using Certificate? persisted = await store.LoadPrivateKeyAsync(
                        combined.Thumbprint, null, null, ObjectTypeIds.RsaSha256ApplicationCertificateType, password,
                        cancellationToken).ConfigureAwait(false);
                    if (persisted == null || !persisted.HasPrivateKey ||
                        !persisted.RawData.AsSpan().SequenceEqual(combined.RawData) ||
                        (verifyPersistedKey
                            ? !X509Utils.VerifyKeyPair(combined, persisted)
                            : !HasSamePublicKey(persisted, combined)))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "The application store did not retain the issued key pair.");
                    }
                }
            }
            finally
            {
                Array.Clear(password, 0, password.Length);
            }
        }

        /// <summary>
        /// Updates the live certificate manager before selecting the stored leaf in the application configuration.
        /// </summary>
        private async ValueTask ActivateInstalledCertificateAsync(
            CertificateIdentifier identifier,
            Certificate combined,
            CertificateCollection issuers,
            CancellationToken cancellationToken)
        {
            await m_manager.UpdateApplicationCertificateAsync(
                ObjectTypeIds.RsaSha256ApplicationCertificateType, combined, issuers, cancellationToken)
                .ConfigureAwait(false);
            identifier.RawData = null;
            identifier.Thumbprint = combined.Thumbprint;
            identifier.SubjectName = combined.Subject;
        }

        /// <summary>
        /// Validates the issued certificate and its issuers against the complete received trust list, including
        /// revocation, without accepting untrusted or unknown-revocation chains. The caller disposes the chain.
        /// </summary>
        private async ValueTask<CertificateCollection> ValidateChainAsync(
            Certificate issued,
            PullDcaCredentials credentials,
            CancellationToken cancellationToken)
        {
            TrustListDataType received = credentials.TrustList;
            if (received == null || received.SpecifiedLists != (uint)TrustListMasks.All)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "Onboarding requires a complete DCA trust list.");
            }
            var chain = new CertificateCollection { issued };
            try
            {
                for (int i = 0; i < credentials.IssuerCertificates.Count; i++)
                {
                    using Certificate issuer = Certificate.FromRawData(credentials.IssuerCertificates[i]);
                    chain.Add(issuer);
                }
                var scope = new CertificateValidationScope(
                    received.TrustedCertificates, received.IssuerCertificates,
                    (received.TrustedCrls.ToArray() ?? []).Concat(received.IssuerCrls.ToArray() ?? []).ToArrayOf());
                await ValidateScopedChainAsync(chain, scope, cancellationToken).ConfigureAwait(false);
                return chain;
            }
            catch
            {
                chain.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Applies strict application certificate checks using only the supplied trust material.
        /// </summary>
        private async ValueTask ValidateScopedChainAsync(
            CertificateCollection chain,
            CertificateValidationScope scope,
            CancellationToken ct)
        {
            Opc.Ua.CertificateValidationResult validation = await m_manager.ValidateAsync(
                chain, TrustListIdentifier.Peers, new ValidationOptions
                {
                    TrustScope = scope,
                    RejectUnknownRevocationStatus = true,
                    RejectSHA1SignedCertificates = true,
                    MinimumCertificateKeySize = 2048,
                    AutoAcceptUntrustedCertificates = false,
                    AcceptError = static (_, _) => false,
                    RecordRejectedCertificates = false
                }, ct).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                throw new ServiceResultException(validation.StatusCode);
            }
        }

        /// <summary>
        /// Reads exactly one DER-encoded application leaf, not a CA or a certificate bundle.
        /// </summary>
        private static Certificate LoadExternalLeaf(ByteString certificate)
        {
            byte[] encoded = certificate.ToArray();
            using Certificate issued = Certificate.FromRawData(encoded);
            if (!issued.RawData.AsSpan().SequenceEqual(encoded) || X509Utils.IsCertificateAuthority(issued))
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateInvalid, "Expected exactly one DER application leaf certificate.");
            }
            return issued.AddRef();
        }

        /// <summary>
        /// Binds the leaf to its retained key for installation.
        /// </summary>
        private Certificate BindToRetainedKey(Certificate issued, Certificate retained)
        {
            ValidateRetainedKeyBinding(issued, retained);
            try
            {
                return DefaultCertificateFactory.Instance.CreateWithPrivateKey(issued, retained);
            }
            catch (NotSupportedException error)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The retained request key is not usable.", error);
            }
        }

        /// <summary>
        /// Checks the application identity, certificate type and retained private key without copying that key.
        /// </summary>
        private void ValidateRetainedKeyBinding(Certificate issued, Certificate retained)
        {
            if (!X509Utils.GetApplicationUrisFromCertificate(issued).Contains(m_configuration.ApplicationUri))
            {
                throw new ServiceResultException(StatusCodes.BadCertificateUriInvalid);
            }
            if (!CertificateIdentifier.ValidateCertificateType(
                issued, ObjectTypeIds.RsaSha256ApplicationCertificateType))
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            if (!HasSamePublicKey(issued, retained))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The certificate does not certify the retained request key.");
            }
            if (!retained.HasPrivateKey)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The retained request key has no private key.");
            }
        }

        /// <summary>
        /// Compares RSA moduli and exponents; this works on every supported target framework.
        /// </summary>
        private static bool HasSamePublicKey(Certificate first, Certificate second)
        {
            using RSA? a = first.GetRSAPublicKey();
            using RSA? b = second.GetRSAPublicKey();
            if (a == null || b == null)
            {
                return false;
            }
            RSAParameters pa = a.ExportParameters(false);
            RSAParameters pb = b.ExportParameters(false);
            return pa.Modulus != null && pb.Modulus != null && pa.Exponent != null && pb.Exponent != null &&
                pa.Modulus.AsSpan().SequenceEqual(pb.Modulus) && pa.Exponent.AsSpan().SequenceEqual(pb.Exponent);
        }

        /// <summary>
        /// Loads the retained key by the journaled thumbprint and proves it is the journaled key certificate.
        /// </summary>
        private async ValueTask<Certificate> LoadRetainedKeyAsync(
            SigningJournal journal,
            JournalEntry entry,
            CancellationToken cancellationToken)
        {
            using Certificate expected = Certificate.FromRawData(entry.SigningCertificate);
            using ICertificateStore store = journal.OpenKeyStore(m_storeProviders);
            char[] password = GetSigningKeyPassword(journal, expected.Thumbprint);
            Certificate? retained;
            try
            {
                retained = await store.LoadPrivateKeyAsync(
                    expected.Thumbprint, null, null, ObjectTypeIds.RsaSha256ApplicationCertificateType, password,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Array.Clear(password, 0, password.Length);
            }
            if (retained == null || !retained.HasPrivateKey ||
                !retained.RawData.AsSpan().SequenceEqual(entry.SigningCertificate.Span))
            {
                retained?.Dispose();
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The retained request key is missing; discard the request.");
            }
            return retained;
        }

        /// <summary>
        /// Deletes the retained key, if present, and then the journal entry, so the entry never outlives a
        /// deletion attempt that failed.
        /// </summary>
        private async ValueTask RemoveRequestAsync(
            SigningJournal journal,
            JournalEntry entry,
            CancellationToken cancellationToken)
        {
            using (Certificate key = Certificate.FromRawData(entry.SigningCertificate))
            using (ICertificateStore store = journal.OpenKeyStore(m_storeProviders))
            {
                using CertificateCollection found = await store.FindByThumbprintAsync(
                    key.Thumbprint, cancellationToken).ConfigureAwait(false);
                if (found.Count > 0 && !await store.DeleteAsync(key.Thumbprint, cancellationToken)
                    .ConfigureAwait(false))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The retained request key could not be deleted.");
                }
            }
            journal.Delete();
        }

        /// <summary>
        /// Reads the journal and rejects an entry created for another application URI.
        /// </summary>
        private async ValueTask<JournalEntry?> ReadEntryAsync(
            SigningJournal journal,
            CancellationToken cancellationToken)
        {
            JournalEntry? entry = await journal.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (entry != null &&
                !string.Equals(entry.ApplicationUri, m_configuration.ApplicationUri, StringComparison.Ordinal))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The signing journal belongs to another application.");
            }
            return entry;
        }

        /// <summary>
        /// Returns the host's password for a key in the signing-key store, or an empty password.
        /// </summary>
        private char[] GetSigningKeyPassword(SigningJournal journal, string thumbprint)
        {
            var identifier = new CertificateIdentifier
            {
                StoreType = journal.KeyStore.StoreType,
                StorePath = journal.KeyStore.StorePath,
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType,
                Thumbprint = thumbprint
            };
            return m_configuration.SecurityConfiguration.CertificatePasswordProvider?.GetPassword(identifier) ?? [];
        }

        /// <summary>
        /// Requires a configured journal and rejects overlapping journal operations instead of interleaving them.
        /// </summary>
        private SigningJournal EnterJournal()
        {
            SigningJournal journal = m_journal ?? throw new ServiceResultException(
                StatusCodes.BadConfigurationError, "No signing journal is configured for request recovery.");
            if (Interlocked.CompareExchange(ref m_journalBusy, 1, 0) != 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "Another signing journal operation is in progress.");
            }
            return journal;
        }

        /// <summary>
        /// Ends the current journal operation.
        /// </summary>
        private void ExitJournal()
        {
            Volatile.Write(ref m_journalBusy, 0);
        }

        /// <summary>
        /// Parses the complete received trust list before changing any trust-store content.
        /// </summary>
        private static TrustListData DecodeTrustList(TrustListDataType received)
        {
            var result = new TrustListData();
            try
            {
                foreach (ByteString encoded in received.TrustedCertificates)
                {
                    using Certificate certificate = Certificate.FromRawData(encoded);
                    result.TrustedCertificates.Add(certificate);
                }
                foreach (ByteString encoded in received.IssuerCertificates)
                {
                    using Certificate certificate = Certificate.FromRawData(encoded);
                    result.IssuerCertificates.Add(certificate);
                }
                foreach (ByteString encoded in received.TrustedCrls)
                {
                    result.TrustedCrls.Add(new X509CRL(encoded.ToArray()));
                }
                foreach (ByteString encoded in received.IssuerCrls)
                {
                    result.IssuerCrls.Add(new X509CRL(encoded.ToArray()));
                }
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Returns the explicit local RSA certificate-store configuration.
        /// </summary>
        private CertificateIdentifier GetIdentifier()
        {
            foreach (CertificateIdentifier identifier in m_configuration.SecurityConfiguration.ApplicationCertificates)
            {
                if (identifier.CertificateType == ObjectTypeIds.RsaSha256ApplicationCertificateType)
                {
                    return identifier;
                }
            }
            throw new ServiceResultException(StatusCodes.BadConfigurationError);
        }

        /// <summary>The host-owned application configuration.</summary>
        private readonly ApplicationConfiguration m_configuration;

        /// <summary>The host-owned certificate manager.</summary>
        private readonly ICertificateManager m_manager;

        /// <summary>Telemetry used to open the configured certificate store.</summary>
        private readonly ITelemetryContext m_telemetry;

        /// <summary>Host-registered store providers, consulted before built-in store types.</summary>
        private readonly ArrayOf<ICertificateStoreProvider> m_storeProviders;

        /// <summary>The one DCA application whose key was used for the CSR.</summary>
        private NodeId m_applicationId;

        /// <summary>The original certificate group/type selected for the retained DCA request.</summary>
        private PullDcaCertificateProfile? m_profile;

        /// <summary>The public certificate of the key used for the last non-journaled CSR.</summary>
        private ByteString m_signingCertificate;

        /// <summary>The last non-journaled CSR, used to identify its key evidence.</summary>
        private ByteString m_signingRequest;

        /// <summary>The opt-in request journal, or null to keep the identity-key behavior.</summary>
        private SigningJournal? m_journal;

        /// <summary>The retained public channel and credential receipt for this standalone DCA.</summary>
        private DevicePullJournal? m_pullJournal;

        /// <summary>Excludes another installation while exact applied state is checked and recorded.</summary>
        private int m_pullInstallationActive;

        /// <summary>One while a journal operation runs.</summary>
        private int m_journalBusy;

        /// <summary>RSA key size of fresh request keys.</summary>
        private const ushort kSigningKeySize = 2048;

        /// <summary>Lifetime of the self-signed container that holds a fresh request key.</summary>
        private const ushort kSigningKeyLifeTimeMonths = 12;

        /// <summary>
        /// One immutable public journal record.
        /// </summary>
        private sealed record JournalEntry(
            OnboardingSigningRequestState State,
            string ApplicationUri,
            ByteString SigningCertificate,
            ByteString SigningRequest,
            NodeId ApplicationId,
            PullDcaCertificateProfile Profile,
            PullDcaRegistrar? Registrar,
            NodeId RequestId);

        /// <summary>
        /// Persists one public journal record with versioned binary encoding and staged, indivisible replacement.
        /// Identifiers are stored with namespace URIs, so a changed server namespace order cannot rebind them.
        /// </summary>
        private sealed class SigningJournal
        {
            /// <summary>
            /// Borrows the file system and namespace table owned by the host.
            /// </summary>
            public SigningJournal(
                string path,
                IFileSystem fileSystem,
                CertificateStoreIdentifier keyStore,
                NamespaceTable namespaces,
                ITelemetryContext telemetry)
            {
                m_path = path;
                m_fileSystem = fileSystem;
                KeyStore = keyStore;
                m_namespaces = namespaces;
                m_telemetry = telemetry;
                m_context = ServiceMessageContext.Create(telemetry);
            }

            /// <summary>
            /// Gets the dedicated store holding fresh request keys.
            /// </summary>
            public CertificateStoreIdentifier KeyStore { get; }

            /// <summary>
            /// Opens the signing-key store with private-key access.
            /// </summary>
            public ICertificateStore OpenKeyStore(ArrayOf<ICertificateStoreProvider> providers)
            {
                ICertificateStore store = CertificateStoreIdentifier.CreateStore(
                    KeyStore.StoreType!, m_telemetry, providers.ToArray());
                try
                {
                    store.Open(KeyStore.StorePath!, noPrivateKeys: false);
                    return store;
                }
                catch
                {
                    store.Dispose();
                    throw;
                }
            }

            /// <summary>
            /// Reads the record, rejecting unknown versions, inconsistent states and trailing content.
            /// </summary>
            public async ValueTask<JournalEntry?> ReadAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!m_fileSystem.Exists(m_path))
                {
                    return null;
                }
                using var buffer = new MemoryStream();
                using (Stream input = m_fileSystem.OpenRead(m_path))
                {
                    byte[] block = new byte[8192];
                    int read;
#if NETSTANDARD2_0 || NETFRAMEWORK
                    while ((read = await input.ReadAsync(block, 0, block.Length, cancellationToken)
                        .ConfigureAwait(false)) > 0)
#else
                    while ((read = await input.ReadAsync(block.AsMemory(), cancellationToken)
                        .ConfigureAwait(false)) > 0)
#endif
                    {
                        if (buffer.Length + read > kMaximumJournalBytes)
                        {
                            throw new InvalidDataException("The signing journal exceeds its size limit.");
                        }
#if NETSTANDARD2_0 || NETFRAMEWORK
                        await buffer.WriteAsync(block, 0, read, cancellationToken).ConfigureAwait(false);
#else
                        await buffer.WriteAsync(block.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
#endif
                    }
                }
                buffer.Position = 0;
                try
                {
                    return Decode(buffer);
                }
                catch (Exception error) when (error is ServiceResultException or EndOfStreamException or
                    ArgumentException or FormatException)
                {
                    throw new InvalidDataException("The signing journal is invalid.", error);
                }
            }

            /// <summary>
            /// Replaces the record; failure leaves the previous record in place.
            /// </summary>
            public async ValueTask WriteAsync(JournalEntry entry, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var buffer = new MemoryStream();
                using (var encoder = new BinaryEncoder(buffer, m_context, leaveOpen: true))
                {
                    encoder.WriteUInt32(null, kVersion);
                    encoder.WriteByte(null, (byte)entry.State);
                    encoder.WriteString(null, entry.ApplicationUri);
                    encoder.WriteByteString(null, entry.SigningCertificate);
                    encoder.WriteByteString(null, entry.SigningRequest);
                    WriteId(encoder, entry.ApplicationId);
                    WriteId(encoder, entry.Profile.CertificateGroupId);
                    WriteId(encoder, entry.Profile.CertificateTypeId);
                    encoder.WriteBoolean(null, entry.Registrar != null);
                    if (entry.Registrar != null)
                    {
                        encoder.WriteString(null, entry.Registrar.EndpointUrl.AbsoluteUri);
                        WriteId(encoder, entry.Registrar.RegistrarId);
                        WriteId(encoder, entry.Registrar.CertificateDirectoryId);
                    }
                    WriteId(encoder, entry.RequestId);
                }
                if (buffer.Length > kMaximumJournalBytes)
                {
                    throw new InvalidDataException("The signing journal exceeds its size limit.");
                }
                string staging = m_path + "." + Guid.NewGuid().ToString("N") + ".pending";
                ExceptionDispatchInfo? publicationError = null;
                try
                {
                    using (Stream output = m_fileSystem.OpenWrite(staging))
                    {
                        buffer.Position = 0;
                        await buffer.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
                        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                    for (int attempt = 1; ; attempt++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            m_fileSystem.Replace(staging, m_path);
                            break;
                        }
                        catch (IOException error) when (
                            error.HResult == kUnableToRemoveReplacedHResult && attempt < kMaximumReplaceAttempts)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception error)
                {
                    publicationError = ExceptionDispatchInfo.Capture(error);
                }
                try
                {
                    if (m_fileSystem.Exists(staging))
                    {
                        m_fileSystem.Delete(staging);
                    }
                }
                catch (Exception cleanupError) when (publicationError != null)
                {
                    throw new AggregateException(
                        "Publishing the signing journal and removing its staging file both failed.",
                        publicationError.SourceException, cleanupError);
                }
                publicationError?.Throw();
            }

            /// <summary>
            /// Removes the record.
            /// </summary>
            public void Delete()
            {
                if (m_fileSystem.Exists(m_path))
                {
                    m_fileSystem.Delete(m_path);
                }
            }

            /// <summary>
            /// Decodes one complete record and checks that its state and bindings are consistent.
            /// </summary>
            private JournalEntry Decode(MemoryStream buffer)
            {
                using var decoder = new BinaryDecoder(buffer, m_context, leaveOpen: true);
                if (decoder.ReadUInt32(null) != kVersion)
                {
                    throw new InvalidDataException("The signing journal version is not supported.");
                }
                var state = (OnboardingSigningRequestState)decoder.ReadByte(null);
                string applicationUri = decoder.ReadString(null) ?? string.Empty;
                ByteString certificate = decoder.ReadByteString(null);
                ByteString csr = decoder.ReadByteString(null);
                NodeId applicationId = ReadId(decoder);
                NodeId group = ReadId(decoder);
                NodeId type = ReadId(decoder);
                PullDcaRegistrar? registrar = null;
                if (decoder.ReadBoolean(null))
                {
                    var endpoint = new Uri(decoder.ReadString(null) ?? string.Empty, UriKind.Absolute);
                    NodeId registrarId = ReadId(decoder);
                    NodeId directoryId = ReadId(decoder);
                    registrar = new PullDcaRegistrar(endpoint, registrarId, directoryId);
                }
                NodeId requestId = ReadId(decoder);
                if (decoder.Position != buffer.Length)
                {
                    throw new InvalidDataException("The signing journal contains trailing content.");
                }
                bool bound = registrar != null && !registrar.RegistrarId.IsNull &&
                    !registrar.CertificateDirectoryId.IsNull && !requestId.IsNull;
                bool consistent = state switch
                {
                    OnboardingSigningRequestState.Prepared => registrar == null && requestId.IsNull,
                    OnboardingSigningRequestState.Started or OnboardingSigningRequestState.Installed => bound,
                    _ => false
                };
                if (!consistent || applicationUri.Length == 0 || certificate.IsEmpty || csr.IsEmpty ||
                    applicationId.IsNull || group.IsNull || type.IsNull)
                {
                    throw new InvalidDataException("The signing journal entry is inconsistent.");
                }
                return new JournalEntry(
                    state,
                    applicationUri,
                    certificate,
                    csr,
                    applicationId,
                    new PullDcaCertificateProfile(group, type),
                    registrar,
                    requestId);
            }

            /// <summary>
            /// Writes a namespace URI and a namespace-zero identifier; unknown namespace indices are rejected.
            /// </summary>
            private void WriteId(BinaryEncoder encoder, NodeId id)
            {
                string? uri = null;
                if (!id.IsNull && id.NamespaceIndex > 0)
                {
                    uri = m_namespaces.GetString(id.NamespaceIndex) ?? throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "An identifier's namespace is not in the session table.");
                }
                encoder.WriteString(null, uri);
                encoder.WriteNodeId(null, uri == null ? id : id.WithNamespaceIndex(0));
            }

            /// <summary>
            /// Resolves a stored namespace URI against the current session namespace table.
            /// </summary>
            private NodeId ReadId(BinaryDecoder decoder)
            {
                string? uri = decoder.ReadString(null);
                NodeId id = decoder.ReadNodeId(null);
                if (id.NamespaceIndex != 0 || (id.IsNull && !string.IsNullOrEmpty(uri)))
                {
                    throw new InvalidDataException("A journaled identifier is not URI-qualified.");
                }
                if (string.IsNullOrEmpty(uri))
                {
                    return id;
                }
                int index = m_namespaces.GetIndex(uri!);
                if (index <= 0 || index > ushort.MaxValue)
                {
                    throw new InvalidDataException("A journaled namespace is not in the session table.");
                }
                return id.WithNamespaceIndex((ushort)index);
            }

            /// <summary>The journal format version.</summary>
            private const uint kVersion = 1;

            /// <summary>The largest accepted journal.</summary>
            private const int kMaximumJournalBytes = 1024 * 1024;

            // ReplaceFile documents that ERROR_UNABLE_TO_REMOVE_REPLACED leaves both files under their original names.
            private const int kUnableToRemoveReplacedHResult = unchecked((int)0x80070497);
            private const int kMaximumReplaceAttempts = 3;

            /// <summary>The journal file path, owned by exactly one process.</summary>
            private readonly string m_path;

            /// <summary>The borrowed file system.</summary>
            private readonly IFileSystem m_fileSystem;

            /// <summary>The borrowed session namespace table.</summary>
            private readonly NamespaceTable m_namespaces;

            /// <summary>Telemetry used to open the signing-key store.</summary>
            private readonly ITelemetryContext m_telemetry;

            /// <summary>The encoding context.</summary>
            private readonly ServiceMessageContext m_context;
        }
    }
}
