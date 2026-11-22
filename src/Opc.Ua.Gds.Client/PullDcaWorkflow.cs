// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Onboarding;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Executes one bounded OPC 10000-21 pull-DCA workflow using generated proxies and borrowed managed sessions.
    /// Host providers own key material, installation, software updates and any durable recovery state.
    /// </summary>
    public sealed class PullDcaWorkflow
    {
        /// <summary>
        /// Creates a workflow directly. The accessor must return a factory-owned managed session for the selected
        /// endpoint; this workflow never disposes it. Hosts must not concurrently change that session's identity.
        /// </summary>
        public PullDcaWorkflow(
            PullDcaOptions options,
            Func<PullDcaRegistrar, CancellationToken, ValueTask<ManagedSession>> sessionAccessor,
            IPullDcaIdentityProvider identityProvider,
            IPullDcaCredentialProvider credentialProvider,
            ITelemetryContext telemetry,
            IPullDcaSoftwareUpdateProvider? softwareUpdateProvider = null,
            IPullDcaRegistrarDiscovery? discovery = null,
            IPullDcaCertificateReceiptStore? receipts = null,
            IPullDcaSigningRecoveryProvider? signingRecovery = null)
            : this(
                options, sessionAccessor, identityProvider, credentialProvider, telemetry,
                softwareUpdateProvider, discovery, receipts, signingRecovery, null, null)
        {
        }

        /// <summary>
        /// Creates a workflow with a separately supplied new key-pair credential provider.
        /// </summary>
        public PullDcaWorkflow(
            PullDcaOptions options,
            IPullDcaNewKeyPairCredentialProvider newKeyPairCredentialProvider,
            Func<PullDcaRegistrar, CancellationToken, ValueTask<ManagedSession>> sessionAccessor,
            IPullDcaIdentityProvider identityProvider,
            IPullDcaCredentialProvider credentialProvider,
            ITelemetryContext telemetry,
            IPullDcaSoftwareUpdateProvider? softwareUpdateProvider = null,
            IPullDcaRegistrarDiscovery? discovery = null,
            IPullDcaCertificateReceiptStore? receipts = null,
            IPullDcaSigningRecoveryProvider? signingRecovery = null)
            : this(
                options, sessionAccessor, identityProvider, credentialProvider, telemetry,
                softwareUpdateProvider, discovery, receipts, signingRecovery, null,
                newKeyPairCredentialProvider ?? throw new ArgumentNullException(nameof(newKeyPairCredentialProvider)))
        {
        }

        /// <summary>
        /// Creates a workflow with an optional host-owned signing journal.
        /// The journal coordinates competing writers in its own storage scope.
        /// </summary>
        public PullDcaWorkflow(
            PullDcaOptions options,
            Func<PullDcaRegistrar, CancellationToken, ValueTask<ManagedSession>> sessionAccessor,
            IPullDcaIdentityProvider identityProvider,
            IPullDcaCredentialProvider credentialProvider,
            ITelemetryContext telemetry,
            IPullDcaSoftwareUpdateProvider? softwareUpdateProvider,
            IPullDcaRegistrarDiscovery? discovery,
            IPullDcaCertificateReceiptStore? receipts,
            IPullDcaSigningRecoveryProvider? signingRecovery,
            IPullDcaSigningJournal? signingJournal,
            IPullDcaNewKeyPairCredentialProvider? newKeyPairCredentialProvider = null)
            : this(
                options,
                CreateConnectionFactory(options, sessionAccessor, telemetry, signingJournal, signingRecovery),
                identityProvider,
                credentialProvider,
                softwareUpdateProvider,
                discovery,
                receipts,
                signingRecovery,
                signingJournal,
                newKeyPairCredentialProvider)
        {
        }

        /// <summary>
        /// Creates a workflow with a host-owned durable new key-pair recovery provider.
        /// </summary>
        public PullDcaWorkflow(
            PullDcaOptions options,
            Func<PullDcaRegistrar, CancellationToken, ValueTask<ManagedSession>> sessionAccessor,
            IPullDcaIdentityProvider identityProvider,
            IPullDcaCredentialProvider credentialProvider,
            ITelemetryContext telemetry,
            IPullDcaSoftwareUpdateProvider? softwareUpdateProvider,
            IPullDcaRegistrarDiscovery? discovery,
            IPullDcaCertificateReceiptStore? receipts,
            IPullDcaSigningRecoveryProvider? signingRecovery,
            IPullDcaSigningJournal? signingJournal,
            IPullDcaNewKeyPairCredentialProvider? newKeyPairCredentialProvider,
            IPullDcaNewKeyPairRecoveryProvider? newKeyPairRecoveryProvider)
            : this(
                options,
                CreateConnectionFactory(options, sessionAccessor, telemetry, signingJournal, signingRecovery),
                identityProvider,
                credentialProvider,
                softwareUpdateProvider,
                discovery,
                receipts,
                signingRecovery,
                signingJournal,
                newKeyPairCredentialProvider,
                newKeyPairRecoveryProvider)
        {
        }

        /// <summary>
        /// Creates a workflow with a typed transport boundary for deterministic tests.
        /// </summary>
        internal PullDcaWorkflow(
            PullDcaOptions options,
            Func<PullDcaRegistrar, CancellationToken, ValueTask<IPullDcaConnection>> connectionFactory,
            IPullDcaIdentityProvider identityProvider,
            IPullDcaCredentialProvider credentialProvider,
            IPullDcaSoftwareUpdateProvider? softwareUpdateProvider = null,
            IPullDcaRegistrarDiscovery? discovery = null,
            IPullDcaCertificateReceiptStore? receipts = null,
            IPullDcaSigningRecoveryProvider? signingRecovery = null,
            IPullDcaSigningJournal? signingJournal = null,
            IPullDcaNewKeyPairCredentialProvider? newKeyPairCredentialProvider = null,
            IPullDcaNewKeyPairRecoveryProvider? newKeyPairRecoveryProvider = null)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (options.MaximumFinishAttempts < 1 || options.MaximumFinishAttempts > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }
            if (options.MaximumNewKeyPairRestarts < 0 || options.MaximumNewKeyPairRestarts > 10)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }
            if (options.FinishRetryDelay < TimeSpan.Zero || options.FinishRetryDelay > TimeSpan.FromMinutes(1) ||
                options.OperationTimeout <= TimeSpan.Zero || options.OperationTimeout > TimeSpan.FromDays(1) ||
                options.MaximumTrustListSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }
            foreach (PullDcaRegistrar registrar in options.Registrars)
            {
                ValidateRegistrar(registrar);
            }
            m_connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            m_identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
            m_credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
            m_softwareUpdateProvider = softwareUpdateProvider;
            m_discovery = discovery;
            m_receipts = receipts;
            m_signingRecovery = signingRecovery;
            m_signingJournal = signingJournal;
            m_newKeyPairCredentialProvider = newKeyPairCredentialProvider ??
                credentialProvider as IPullDcaNewKeyPairCredentialProvider;
            m_newKeyPairRecoveryProvider = newKeyPairRecoveryProvider;
            m_registrars = options.Registrars.ToArray() ?? [];
            m_maximumFinishAttempts = options.MaximumFinishAttempts;
            m_maximumNewKeyPairRestarts = options.MaximumNewKeyPairRestarts;
            m_finishRetryDelay = options.FinishRetryDelay;
            m_operationTimeout = options.OperationTimeout;
        }

        /// <summary>
        /// Returns configured candidates or invokes the host's discovery provider. The caller selects one candidate;
        /// discovered endpoints are not automatically trusted or used as fallback targets.
        /// </summary>
        public async ValueTask<ArrayOf<PullDcaRegistrar>> DiscoverRegistrarsAsync(
            CancellationToken cancellationToken = default)
        {
            using var timeout = CreateDeadline(cancellationToken);
            timeout.Token.ThrowIfCancellationRequested();
            ArrayOf<PullDcaRegistrar> registrars = m_discovery == null
                ? m_registrars
                : await m_discovery.DiscoverAsync(timeout.Token).ConfigureAwait(false);
            foreach (PullDcaRegistrar registrar in registrars)
            {
                ValidateRegistrar(registrar);
            }
            return registrars;
        }

        /// <summary>
        /// Provides identities, proves the selection, completes the software gate, installs DCA credentials,
        /// then registers and provisions the requested managed applications.
        /// Service and host-provider failures propagate without being converted to success or automatically retried.
        /// </summary>
        public ValueTask<PullDcaResult> RunAsync(
            PullDcaRegistrar registrar,
            PullDcaRequest request,
            IProgress<PullDcaProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return RunCoreAsync(registrar, request, progress, null, cancellationToken);
        }

        /// <summary>
        /// Runs the workflow with a host-owned operation for the installed DCA's manager request.
        /// Capture the operation for the intended deployment before starting admission or reading its journal.
        /// </summary>
        /// <remarks>
        /// The DCA must use a signing request because this operation requires a retained signing binding.
        /// Managed applications may still use new key-pair requests.
        /// </remarks>
        public ValueTask<PullDcaResult> RunAsync(
            PullDcaRegistrar registrar,
            PullDcaRequest request,
            IProgress<PullDcaProgress>? progress,
            IPullDcaInstalledManagerOperation managerOperation,
            CancellationToken cancellationToken = default)
        {
            if (managerOperation == null)
            {
                throw new ArgumentNullException(nameof(managerOperation));
            }
            return RunCoreAsync(registrar, request, progress, managerOperation, cancellationToken);
        }

        /// <summary>
        /// Uses the DCA certificate already installed by an external authentication service.
        /// The host completes any required software update first, using the managers returned by GetManagers.
        /// The registrar validates the actual channel, dedicated issuer trust and configured device approval.
        /// This workflow never calls ProvideIdentities, creates a ticket or replaces the external DCA credential.
        /// </summary>
        public async ValueTask<PullDcaExternalAuthenticationResult> RunExternalAuthenticationAsync(
            PullDcaRegistrar registrar,
            ArrayOf<PullDcaManagedApplication> managedApplications,
            IProgress<PullDcaProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateRegistrar(registrar);
            registrar = PullDcaConnection.SnapshotRegistrar(registrar);
            managedApplications = SnapshotManagedApplications(managedApplications);
            ValidateKeyPairSupport(PullDcaCertificateRequestMode.SigningRequest, managedApplications);
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref m_running, 1, 0) != 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "A pull workflow is already running.");
            }
            try
            {
                using var timeout = CreateDeadline(cancellationToken);
                Report(progress, PullDcaStage.Connecting);
                IPullDcaConnection connection = await m_connectionFactory(registrar, timeout.Token)
                    .ConfigureAwait(false);
                Report(progress, PullDcaStage.GettingManagers);
                ArrayOf<ManagerDescription> managers = await connection.GetManagersAsync(timeout.Token)
                    .ConfigureAwait(false);
                ArrayOf<PullDcaProvisionedApplication> applications = await ProvisionManagedApplicationsAsync(
                    registrar, connection, managedApplications, progress, timeout.Token).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                Report(progress, PullDcaStage.Completed);
                return new PullDcaExternalAuthenticationResult(managers, applications);
            }
            finally
            {
                Volatile.Write(ref m_running, 0);
            }
        }

        /// <summary>
        /// Retrieves managers after a retained pull installation using its installed certificate and registrar.
        /// The caller verifies the retained receipt, private key, and applied trust before invoking this method.
        /// </summary>
        public async ValueTask<ArrayOf<ManagerDescription>> ResumeInstalledAsync(
            PullDcaSigningBinding binding,
            ByteString installedCertificate,
            IProgress<PullDcaProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }
            if (binding.AttemptId == Guid.Empty)
            {
                throw new ArgumentException("The retained attempt identifier is required.", nameof(binding));
            }
            if (installedCertificate.IsEmpty)
            {
                throw new ArgumentException("The installed certificate is required.", nameof(installedCertificate));
            }
            binding = PullDcaConnection.SnapshotBinding(binding);
            installedCertificate = ByteString.From(installedCertificate.Span);
            ValidateRegistrar(binding.Registrar);
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref m_running, 1, 0) != 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "A pull workflow is already running.");
            }
            try
            {
                using var timeout = CreateDeadline(cancellationToken);
                Report(progress, PullDcaStage.Connecting, binding.ApplicationId, binding.RequestId);
                IPullDcaConnection connection = await m_connectionFactory(binding.Registrar, timeout.Token)
                    .ConfigureAwait(false);
                Report(progress, PullDcaStage.GettingManagers, binding.ApplicationId, binding.RequestId);
                ArrayOf<ManagerDescription> managers = await connection.GetManagersOnInstalledChannelAsync(
                    binding, installedCertificate, timeout.Token).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                Report(progress, PullDcaStage.Completed, binding.ApplicationId, binding.RequestId);
                return managers;
            }
            finally
            {
                Volatile.Write(ref m_running, 0);
            }
        }

        /// <summary>
        /// Completes a previously persisted signing request without creating another private key or request.
        /// The host restores the original registrar, channel identity, retained private key and certificate profile.
        /// This installs credentials only; the host reconnects the DCA and resumes its remaining lifecycle steps.
        /// </summary>
        public ValueTask<PullDcaCredentials> CompleteCertificateRequestAsync(
            PullDcaRegistrar registrar,
            NodeId applicationId,
            NodeId requestId,
            PullDcaCertificateProfile profile,
            IProgress<PullDcaProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return CompleteCertificateRequestCoreAsync(
                registrar, applicationId, requestId, profile, null, progress, cancellationToken);
        }

        /// <summary>
        /// Completes the exact retained attempt after resolving its identifiers for the selected session.
        /// A reset or replacement request cannot be mistaken for this attempt.
        /// </summary>
        public ValueTask<PullDcaCredentials> CompleteCertificateRequestAsync(
            PullDcaSigningBinding binding,
            IProgress<PullDcaProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }
            if (binding.AttemptId == Guid.Empty)
            {
                throw new ArgumentException("The retained attempt identifier is required.", nameof(binding));
            }
            return CompleteCertificateRequestCoreAsync(
                binding.Registrar, binding.ApplicationId, binding.RequestId, binding.Profile,
                binding.AttemptId, progress, cancellationToken);
        }

        /// <summary>
        /// Executes the pull workflow using the optional deployment-bound manager operation.
        /// </summary>
        private async ValueTask<PullDcaResult> RunCoreAsync(
            PullDcaRegistrar registrar,
            PullDcaRequest request,
            IProgress<PullDcaProgress>? progress,
            IPullDcaInstalledManagerOperation? managerOperation,
            CancellationToken cancellationToken)
        {
            ValidateRegistrar(registrar);
            registrar = PullDcaConnection.SnapshotRegistrar(registrar);
            request = SnapshotRequest(request);
            ValidateKeyPairSupport(request.DcaCertificateRequestMode, request.ManagedApplications);
            if (managerOperation != null &&
                request.DcaCertificateRequestMode == PullDcaCertificateRequestMode.NewKeyPairRequest)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The installed manager operation requires a DCA signing request.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref m_running, 1, 0) != 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "A pull workflow is already running.");
            }
            try
            {
                using var timeout = CreateDeadline(cancellationToken);
                CancellationToken ct = timeout.Token;
                Report(progress, PullDcaStage.Connecting);
                IPullDcaConnection connection = await m_connectionFactory(registrar, ct).ConfigureAwait(false);
                Report(progress, PullDcaStage.ProvidingIdentities);
                PullDcaIdentitySelection identity = await connection.ProvideIdentitiesAsync(
                    request.IdentityOffer, ct).ConfigureAwait(false);
                if (identity != null)
                {
                    identity = identity with
                    {
                        SelectedIdentity = identity.SelectedIdentity.Copy(),
                        ApplicationId = PullDcaConnection.SnapshotNodeId(identity.ApplicationId),
                        MatchingTicket = identity.MatchingTicket is { } selectedTicket
                            ? SnapshotTicket(selectedTicket)
                            : null!,
                        SoftwareUpdateManager = identity.SoftwareUpdateManager is { } selectedManager
                            ? SnapshotManager(selectedManager)
                            : null
                    };
                }
                if (identity == null || identity.SelectedIdentity.IsEmpty ||
                    !request.IdentityOffer.Identities.Contains(identity.SelectedIdentity) ||
                    identity.ApplicationId.IsNull || identity.MatchingTicket == null)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }

                Report(progress, PullDcaStage.ActivatingIdentity, identity.ApplicationId);
                await m_identityProvider.ActivateAsync(identity.SelectedIdentity.Copy(), ct).ConfigureAwait(false);
                await connection.ReloadAndReconnectAsync(ct).ConfigureAwait(false);

                if (identity.SoftwareUpdateManager is { } manager)
                {
                    Report(progress, PullDcaStage.UpdatingSoftware, identity.ApplicationId);
                    if (m_softwareUpdateProvider == null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadConfigurationError, "A real software-update provider is required.");
                    }
                    ManagerDescription providerManager = SnapshotManager(manager);
                    await m_softwareUpdateProvider.CompleteAsync(
                        PullDcaConnection.SnapshotRegistrar(registrar),
                        identity with
                        {
                            SelectedIdentity = identity.SelectedIdentity.Copy(),
                            ApplicationId = PullDcaConnection.SnapshotNodeId(identity.ApplicationId),
                            MatchingTicket = SnapshotTicket(identity.MatchingTicket),
                            SoftwareUpdateManager = providerManager
                        },
                        providerManager, ct).ConfigureAwait(false);
                }

                (PullDcaCredentials dcaCredentials, PullDcaSigningBinding? binding) = await ProvisionAsync(
                    registrar, connection, identity.ApplicationId, request.DcaCertificateProfile,
                    request.DcaCertificateRequestMode, progress, ct)
                    .ConfigureAwait(false);
                Report(progress, PullDcaStage.ActivatingDca, identity.ApplicationId);
                await connection.ReloadAndReconnectAsync(ct).ConfigureAwait(false);
                Report(progress, PullDcaStage.GettingManagers, identity.ApplicationId);
                ArrayOf<ManagerDescription> managers;
                if (managerOperation == null)
                {
                    managers = await connection.GetManagersOnInstalledChannelAsync(
                        null, dcaCredentials.Certificate, ct).ConfigureAwait(false);
                }
                else
                {
                    if (binding == null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNotSupported, "The connection cannot identify the installed pull attempt.");
                    }
                    PullDcaSigningBinding dispatchBinding = PullDcaConnection.SnapshotBinding(binding);
                    ByteString dispatchCertificate = ByteString.From(dcaCredentials.Certificate.Span);
                    managers = await managerOperation.ExecuteAsync(
                        PullDcaConnection.SnapshotBinding(dispatchBinding),
                        ByteString.From(dispatchCertificate.Span),
                        token => connection.GetManagersOnInstalledChannelAsync(
                            dispatchBinding, dispatchCertificate, token),
                        ct).ConfigureAwait(false);
                }

                ArrayOf<PullDcaProvisionedApplication> applications = await ProvisionManagedApplicationsAsync(
                    registrar, connection, request.ManagedApplications, progress, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                Report(progress, PullDcaStage.Completed, identity.ApplicationId);
                return new PullDcaResult(identity, dcaCredentials, managers, applications);
            }
            finally
            {
                Volatile.Write(ref m_running, 0);
            }
        }

        private async ValueTask<PullDcaCredentials> CompleteCertificateRequestCoreAsync(
            PullDcaRegistrar registrar,
            NodeId applicationId,
            NodeId requestId,
            PullDcaCertificateProfile profile,
            Guid? expectedAttemptId,
            IProgress<PullDcaProgress>? progress,
            CancellationToken cancellationToken)
        {
            ValidateRegistrar(registrar);
            ValidateProfile(profile);
            if (applicationId.IsNull || requestId.IsNull)
            {
                throw new ArgumentException("Persisted application and request identifiers are required.");
            }
            registrar = PullDcaConnection.SnapshotRegistrar(registrar);
            applicationId = PullDcaConnection.SnapshotNodeId(applicationId);
            requestId = PullDcaConnection.SnapshotNodeId(requestId);
            profile = PullDcaConnection.SnapshotProfile(profile);
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref m_running, 1, 0) != 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "A pull workflow is already running.");
            }
            try
            {
                using var timeout = CreateDeadline(cancellationToken);
                Report(progress, PullDcaStage.Connecting, applicationId, requestId);
                IPullDcaConnection connection = await m_connectionFactory(registrar, timeout.Token)
                    .ConfigureAwait(false);
                (PullDcaCredentials credentials, _) = await FinishAndInstallAsync(
                    registrar, connection, applicationId, requestId, profile, expectedAttemptId, progress,
                    timeout.Token)
                    .ConfigureAwait(false);
                return credentials;
            }
            finally
            {
                Volatile.Write(ref m_running, 0);
            }
        }

        /// <summary>
        /// Reuses the same registration, request recovery and credential installation for either authentication path.
        /// </summary>
        private async ValueTask<ArrayOf<PullDcaProvisionedApplication>> ProvisionManagedApplicationsAsync(
            PullDcaRegistrar registrar,
            IPullDcaConnection connection,
            ArrayOf<PullDcaManagedApplication> managedApplications,
            IProgress<PullDcaProgress>? progress,
            CancellationToken ct)
        {
            var applications = new List<PullDcaProvisionedApplication>();
            for (int i = 0; i < managedApplications.Count; i++)
            {
                PullDcaManagedApplication application = managedApplications[i];
                ct.ThrowIfCancellationRequested();
                Report(progress, PullDcaStage.RegisteringApplication);
                NodeId applicationId = await connection.RegisterAsync(application, ct).ConfigureAwait(false);
                applicationId = PullDcaConnection.SnapshotNodeId(applicationId);
                if (applicationId.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadUnexpectedError);
                }
                (PullDcaCredentials credentials, _) = await ProvisionAsync(
                    registrar, connection, applicationId, application.CertificateProfile,
                    application.CertificateRequestMode, progress, ct)
                    .ConfigureAwait(false);
                applications.Add(new PullDcaProvisionedApplication(applicationId, credentials));
            }
            return applications.ToArrayOf();
        }

        private async ValueTask<(PullDcaCredentials Credentials, PullDcaSigningBinding? Binding)> ProvisionAsync(
            PullDcaRegistrar registrar,
            IPullDcaConnection connection,
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            PullDcaCertificateRequestMode requestMode,
            IProgress<PullDcaProgress>? progress,
            CancellationToken ct)
        {
            if (requestMode == PullDcaCertificateRequestMode.NewKeyPairRequest)
            {
                PullDcaCredentials keyPairCredentials = await ProvisionNewKeyPairAsync(
                    registrar, connection, applicationId, profile, progress, ct).ConfigureAwait(false);
                return (keyPairCredentials, null);
            }
            Report(progress, PullDcaStage.RequestingCertificate, applicationId);
            ByteString csr = await m_credentialProvider.CreateSigningRequestAsync(
                PullDcaConnection.SnapshotNodeId(applicationId),
                PullDcaConnection.SnapshotProfile(profile),
                ct).ConfigureAwait(false);
            csr = csr.Copy();
            if (csr.IsEmpty)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "The CSR provider returned no CSR.");
            }
            NodeId requestId;
            if (m_signingJournal != null)
            {
                if (m_credentialProvider is not IPullDcaSigningKeyEvidenceProvider keyEvidence ||
                    connection is not IPullDcaBoundSigningConnection boundConnection)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "Journaled signing requires a retained key and a channel-bound connection.");
                }
                ByteString keyCertificate = await keyEvidence.GetSigningKeyCertificateAsync(
                    PullDcaConnection.SnapshotNodeId(applicationId),
                    PullDcaConnection.SnapshotProfile(profile),
                    csr.Copy(),
                    ct).ConfigureAwait(false);
                keyCertificate = keyCertificate.Copy();
                if (keyCertificate.IsEmpty)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "The retained signing key certificate is missing.");
                }
                requestId = await boundConnection.StartWithEvidenceAsync(
                    applicationId, profile, csr, keyCertificate, ct).ConfigureAwait(false);
            }
            else
            {
                requestId = await connection.StartAsync(applicationId, profile, csr, ct).ConfigureAwait(false);
            }
            requestId = PullDcaConnection.SnapshotNodeId(requestId);
            if (requestId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError);
            }
            if (m_signingRecovery is IPullDcaSigningRequestObserver observer &&
                !ReferenceEquals(m_signingRecovery, m_signingJournal))
            {
                await observer.OnSigningRequestStartedAsync(
                    PullDcaConnection.SnapshotRegistrar(registrar),
                    PullDcaConnection.SnapshotNodeId(applicationId),
                    PullDcaConnection.SnapshotNodeId(requestId),
                    PullDcaConnection.SnapshotProfile(profile),
                    csr.Copy(),
                    ct).ConfigureAwait(false);
            }
            return await FinishAndInstallAsync(
                registrar, connection, applicationId, requestId, profile, null, progress, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Uses the Part 12 key-pair operation. A consumed FinishRequest cannot use signing recovery; it starts a
        /// fresh key-pair request, as required by Part 12 §7.9.5.
        /// </summary>
        private async ValueTask<PullDcaCredentials> ProvisionNewKeyPairAsync(
            PullDcaRegistrar registrar,
            IPullDcaConnection connection,
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            IProgress<PullDcaProgress>? progress,
            CancellationToken ct)
        {
            if (m_newKeyPairCredentialProvider == null || m_newKeyPairRecoveryProvider == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    $"Both {nameof(IPullDcaNewKeyPairCredentialProvider)} and " +
                    $"{nameof(IPullDcaNewKeyPairRecoveryProvider)} are required for a new key-pair request.");
            }
            if (connection is not IPullDcaRecoverableNewKeyPairConnection recoveryConnection)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The connection cannot validate durable new key-pair recovery bindings.");
            }

            applicationId = PullDcaConnection.SnapshotNodeId(applicationId);
            profile = PullDcaConnection.SnapshotProfile(profile);
            PullDcaNewKeyPairRecoveryScope scope = PullDcaConnection.SnapshotRecoveryScope(
                await recoveryConnection.CaptureNewKeyPairRecoveryScopeAsync(applicationId, profile, ct)
                    .ConfigureAwait(false));
            RequireRecoveryScope(scope, registrar, applicationId, profile);
            PullDcaNewKeyPairRecoveryAttempt? storedAttempt =
                await m_newKeyPairRecoveryProvider.GetPendingAsync(scope, ct).ConfigureAwait(false);
            PullDcaNewKeyPairRecoveryAttempt? recoveryAttempt = null;
            if (storedAttempt != null)
            {
                try
                {
                    recoveryAttempt = SnapshotRecoveryAttempt(storedAttempt);
                }
                finally
                {
                    PullDcaConnection.ClearPrivateKeyMaterial(storedAttempt.PrivateKeyPfx);
                }
                try
                {
                    RequireRecoveryScope(recoveryAttempt.Binding.Scope, registrar, applicationId, profile);
                    PullDcaConnection.RequireSameRecoveryScope(scope, recoveryAttempt.Binding.Scope);
                    if (recoveryAttempt.State == PullDcaNewKeyPairRecoveryState.Dispatching)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState,
                            "A key-pair dispatch has an unknown outcome and cannot be sent again automatically.");
                    }
                }
                catch
                {
                    PullDcaConnection.ClearPrivateKeyMaterial(recoveryAttempt.PrivateKeyPfx);
                    throw;
                }
            }
            for (int restart = 0; restart <= m_maximumNewKeyPairRestarts; restart++)
            {
                if (recoveryAttempt == null)
                {
                    Report(progress, PullDcaStage.RequestingCertificate, applicationId);
                    var candidateBinding = new PullDcaNewKeyPairRecoveryBinding(
                        PullDcaConnection.SnapshotRecoveryScope(scope), NodeId.Null, Guid.NewGuid());
                    PullDcaNewKeyPairRecoveryBinding startedBinding =
                        await recoveryConnection.StartRecoverableNewKeyPairAsync(
                            candidateBinding,
                            (binding, token) => m_newKeyPairRecoveryProvider.RecordDispatchAsync(binding, token),
                            ct).ConfigureAwait(false);
                    startedBinding = PullDcaConnection.SnapshotRecoveryBinding(startedBinding);
                    PullDcaConnection.RequireSameRecoveryScope(
                        candidateBinding.Scope, startedBinding.Scope);
                    if (startedBinding.AttemptId != candidateBinding.AttemptId || startedBinding.RequestId.IsNull)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "The connection returned a different key-pair recovery attempt.");
                    }
                    await m_newKeyPairRecoveryProvider.RecordStartedAsync(
                        startedBinding, CancellationToken.None).ConfigureAwait(false);
                    recoveryAttempt = new PullDcaNewKeyPairRecoveryAttempt(
                        startedBinding,
                        PullDcaNewKeyPairRecoveryState.Started,
                        default,
                        default,
                        default);
                }

                bool restartRequest = false;
                for (int attempt = 0; attempt < m_maximumFinishAttempts; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (recoveryAttempt.State == PullDcaNewKeyPairRecoveryState.Received)
                    {
                        return await InstallReceivedNewKeyPairAsync(
                            connection, recoveryConnection, recoveryAttempt, progress, ct).ConfigureAwait(false);
                    }

                    PullDcaNewKeyPairRecoveryBinding binding = recoveryAttempt.Binding;
                    await recoveryConnection.ValidateNewKeyPairRecoveryBindingAsync(binding, ct)
                        .ConfigureAwait(false);
                    Report(progress, PullDcaStage.FinishingCertificate, applicationId, binding.RequestId);
                    ByteString certificate;
                    ByteString privateKeyPfx;
                    ArrayOf<ByteString> issuers;
                    try
                    {
                        (certificate, privateKeyPfx, issuers) =
                            await recoveryConnection.FinishRecoverableNewKeyPairAsync(binding, ct)
                                .ConfigureAwait(false);
                    }
                    catch (ServiceResultException error) when (error.StatusCode == StatusCodes.BadNothingToDo)
                    {
                        certificate = default;
                        privateKeyPfx = default;
                        issuers = default;
                    }
                    catch (ServiceResultException error) when (
                        IsRetryableFinishFailure(error.StatusCode) && attempt + 1 < m_maximumFinishAttempts)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Delay(m_finishRetryDelay, ct).ConfigureAwait(false);
                        continue;
                    }
                    catch (ServiceResultException error) when (
                        error.StatusCode == StatusCodes.BadInvalidArgument &&
                        restart < m_maximumNewKeyPairRestarts)
                    {
                        await m_newKeyPairRecoveryProvider.RetireLostRequestAsync(
                            binding, CancellationToken.None).ConfigureAwait(false);
                        recoveryAttempt = null;
                        restartRequest = true;
                        break;
                    }

                    try
                    {
                        if (certificate.IsEmpty != privateKeyPfx.IsEmpty)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadSecurityChecksFailed,
                                "The key-pair request returned incomplete certificate or private-key output.");
                        }

                        if (!certificate.IsEmpty)
                        {
                            foreach (ByteString issuer in issuers)
                            {
                                if (issuer.IsEmpty)
                                {
                                    throw new ServiceResultException(
                                        StatusCodes.BadSecurityChecksFailed,
                                        "The key-pair request returned an empty issuer certificate.");
                                }
                            }
                            certificate = ByteString.From(certificate.Span);
                            issuers = SnapshotBytes(issuers);
                            ByteString retainedPrivateKeyPfx = ByteString.From(privateKeyPfx.Span);
                            try
                            {
                                await m_newKeyPairRecoveryProvider!.RecordReceivedAsync(
                                    binding,
                                    ByteString.From(certificate.Span),
                                    SnapshotBytes(issuers),
                                    retainedPrivateKeyPfx,
                                    CancellationToken.None).ConfigureAwait(false);
                                recoveryAttempt = new PullDcaNewKeyPairRecoveryAttempt(
                                    binding,
                                    PullDcaNewKeyPairRecoveryState.Received,
                                    ByteString.From(certificate.Span),
                                    SnapshotBytes(issuers),
                                    ByteString.From(retainedPrivateKeyPfx.Span));
                                return await InstallReceivedNewKeyPairAsync(
                                    connection, recoveryConnection, recoveryAttempt, progress, ct)
                                    .ConfigureAwait(false);
                            }
                            finally
                            {
                                PullDcaConnection.ClearPrivateKeyMaterial(retainedPrivateKeyPfx);
                            }
                        }

                        if (attempt + 1 < m_maximumFinishAttempts)
                        {
                            await Task.Delay(m_finishRetryDelay, ct).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        PullDcaConnection.ClearPrivateKeyMaterial(privateKeyPfx);
                    }
                }

                if (restartRequest)
                {
                    continue;
                }
                if (recoveryAttempt == null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                throw new PullDcaCertificatePendingException(applicationId, recoveryAttempt.Binding.RequestId);
            }
            throw new ServiceResultException(StatusCodes.BadInvalidState);
        }

        /// <summary>
        /// Completes trust retrieval and idempotent installation from durably received key-pair material.
        /// </summary>
        private async ValueTask<PullDcaCredentials> InstallReceivedNewKeyPairAsync(
            IPullDcaConnection connection,
            IPullDcaRecoverableNewKeyPairConnection recoveryConnection,
            PullDcaNewKeyPairRecoveryAttempt attempt,
            IProgress<PullDcaProgress>? progress,
            CancellationToken ct)
        {
            if (attempt.State != PullDcaNewKeyPairRecoveryState.Received ||
                attempt.Certificate.IsEmpty || attempt.PrivateKeyPfx.IsEmpty)
            {
                PullDcaConnection.ClearPrivateKeyMaterial(attempt.PrivateKeyPfx);
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The retained key-pair response is incomplete.");
            }

            PullDcaNewKeyPairRecoveryBinding binding =
                PullDcaConnection.SnapshotRecoveryBinding(attempt.Binding);
            NodeId applicationId = binding.Scope.ApplicationId;
            PullDcaCertificateProfile profile = binding.Scope.Profile;
            ByteString certificate = ByteString.From(attempt.Certificate.Span);
            ArrayOf<ByteString> issuers = SnapshotBytes(attempt.IssuerCertificates);
            ByteString privateKeyPfx = ByteString.From(attempt.PrivateKeyPfx.Span);
            try
            {
                foreach (ByteString issuer in issuers)
                {
                    if (issuer.IsEmpty)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "The retained key-pair response contains an empty issuer certificate.");
                    }
                }
                await recoveryConnection.ValidateNewKeyPairRecoveryBindingAsync(binding, ct)
                    .ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                Report(progress, PullDcaStage.ReadingTrustList, applicationId, binding.RequestId);
                TrustListDataType trustList = await connection.ReadTrustListAsync(
                    applicationId, profile.CertificateGroupId, ct).ConfigureAwait(false);
                if (trustList == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnexpectedError);
                }
                var credentials = new PullDcaCredentials(certificate, issuers, SnapshotTrustList(trustList));
                Report(progress, PullDcaStage.InstallingCredentials, applicationId, binding.RequestId);
                await m_newKeyPairCredentialProvider!.InstallNewKeyPairAsync(
                    applicationId,
                    PullDcaConnection.SnapshotProfile(profile),
                    SnapshotCredentials(credentials),
                    privateKeyPfx,
                    ct).ConfigureAwait(false);
                await m_newKeyPairRecoveryProvider!.CompleteAsync(
                    binding, ByteString.From(certificate.Span), CancellationToken.None).ConfigureAwait(false);
                recoveryConnection.SetInstalledNewKeyPairBinding(binding);
                return credentials;
            }
            finally
            {
                PullDcaConnection.ClearPrivateKeyMaterial(attempt.PrivateKeyPfx);
                PullDcaConnection.ClearPrivateKeyMaterial(privateKeyPfx);
            }
        }

        /// <summary>
        /// Verifies the scope returned by a recovery provider before using its retained state.
        /// </summary>
        private static void RequireRecoveryScope(
            PullDcaNewKeyPairRecoveryScope scope,
            PullDcaRegistrar registrar,
            NodeId applicationId,
            PullDcaCertificateProfile profile)
        {
            if (scope == null || scope.Registrar == null || scope.Registrar.EndpointUrl == null ||
                registrar == null || registrar.EndpointUrl == null || scope.Profile == null ||
                !scope.Registrar.EndpointUrl.Equals(registrar.EndpointUrl) ||
                scope.Registrar.RegistrarId != registrar.RegistrarId ||
                scope.Registrar.CertificateDirectoryId != registrar.CertificateDirectoryId ||
                scope.ApplicationId != applicationId ||
                scope.Profile.CertificateGroupId != profile.CertificateGroupId ||
                scope.Profile.CertificateTypeId != profile.CertificateTypeId ||
                scope.ClientChannelCertificate.IsEmpty ||
                scope.RegistrarChannelCertificate.IsEmpty ||
                string.IsNullOrWhiteSpace(scope.RegistrarApplicationUri) ||
                scope.NamespaceUris.IsNull || scope.NamespaceUris.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The key-pair recovery scope is incomplete or mismatched.");
            }
        }

        /// <summary>
        /// Detaches and validates a recovery provider's returned attempt.
        /// </summary>
        private static PullDcaNewKeyPairRecoveryAttempt SnapshotRecoveryAttempt(
            PullDcaNewKeyPairRecoveryAttempt attempt)
        {
            if (attempt == null || attempt.Binding == null || attempt.Binding.Scope == null ||
                attempt.Binding.AttemptId == Guid.Empty)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The key-pair recovery provider returned an invalid attempt.");
            }
            if (attempt.State is not PullDcaNewKeyPairRecoveryState.Dispatching and
                not PullDcaNewKeyPairRecoveryState.Started and
                not PullDcaNewKeyPairRecoveryState.Received)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The key-pair recovery provider returned an unknown state.");
            }
            if (attempt.State == PullDcaNewKeyPairRecoveryState.Dispatching && !attempt.Binding.RequestId.IsNull ||
                attempt.State != PullDcaNewKeyPairRecoveryState.Dispatching && attempt.Binding.RequestId.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The key-pair recovery request identifier is inconsistent.");
            }
            if (attempt.State == PullDcaNewKeyPairRecoveryState.Received &&
                (attempt.Certificate.IsEmpty || attempt.PrivateKeyPfx.IsEmpty) ||
                attempt.State != PullDcaNewKeyPairRecoveryState.Received &&
                (!attempt.Certificate.IsEmpty || !attempt.PrivateKeyPfx.IsEmpty ||
                    attempt.IssuerCertificates.Count > 0))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The key-pair recovery payload is inconsistent.");
            }
            return attempt with
            {
                Binding = PullDcaConnection.SnapshotRecoveryBinding(attempt.Binding),
                Certificate = attempt.Certificate.IsNull ? default : ByteString.From(attempt.Certificate.Span),
                IssuerCertificates = SnapshotBytes(attempt.IssuerCertificates),
                PrivateKeyPfx = attempt.PrivateKeyPfx.IsNull
                    ? default
                    : ByteString.From(attempt.PrivateKeyPfx.Span)
            };
        }

        /// <summary>
        /// Uses one existing certificate request for bounded polling and host-owned installation.
        /// </summary>
        private async ValueTask<(PullDcaCredentials Credentials, PullDcaSigningBinding? Binding)> FinishAndInstallAsync(
            PullDcaRegistrar registrar,
            IPullDcaConnection connection,
            NodeId applicationId,
            NodeId requestId,
            PullDcaCertificateProfile profile,
            Guid? expectedAttemptId,
            IProgress<PullDcaProgress>? progress,
            CancellationToken ct)
        {
            PullDcaSigningBinding? binding = null;
            if (connection is IPullDcaBoundSigningConnection boundConnection)
            {
                binding = await boundConnection.GetSigningBindingAsync(applicationId, requestId, ct)
                    .ConfigureAwait(false);
                binding = PullDcaConnection.SnapshotBinding(binding);
                if (binding.Profile != profile ||
                    (expectedAttemptId.HasValue && binding.AttemptId != expectedAttemptId.Value))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed, "The signing attempt differs from the retained request.");
                }
            }
            else if (m_signingJournal != null || expectedAttemptId.HasValue)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The connection cannot validate a retained signing channel.");
            }
            PullDcaCredentials? credentials = m_signingJournal == null
                ? null
                : await m_signingJournal.GetReceivedAsync(
                    PullDcaConnection.SnapshotBinding(binding!), ct).ConfigureAwait(false);
            if (credentials != null)
            {
                credentials = SnapshotCredentials(credentials);
                RequireCompleteSigningTrustList(credentials.TrustList);
            }
            if (credentials == null)
            {
                if (m_signingJournal != null && m_receipts == null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "A journaled request requires a signing receipt store.");
                }
                PullDcaCertificateReceipt receipt = m_receipts == null
                    ? await ReceiveCertificateAsync(
                        registrar, connection, applicationId, requestId, profile, progress, ct).ConfigureAwait(false)
                    : await m_receipts.GetOrCreateAsync(
                        PullDcaConnection.SnapshotRegistrar(registrar),
                        PullDcaConnection.SnapshotNodeId(applicationId),
                        PullDcaConnection.SnapshotNodeId(requestId),
                        PullDcaConnection.SnapshotProfile(profile),
                        token => ReceiveCertificateAsync(
                            registrar, connection, applicationId, requestId, profile, progress, token), ct)
                        .ConfigureAwait(false);
                if (receipt == null || receipt.Certificate.IsEmpty)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "The signing receipt contains no issued certificate.");
                }
                receipt = new PullDcaCertificateReceipt(
                    receipt.Certificate.Copy(), SnapshotBytes(receipt.IssuerCertificates));
                ct.ThrowIfCancellationRequested();
                Report(progress, PullDcaStage.ReadingTrustList, applicationId, requestId);
                TrustListDataType trustList = await connection.ReadTrustListAsync(
                    applicationId, profile.CertificateGroupId, ct).ConfigureAwait(false);
                if (trustList == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnexpectedError);
                }
                credentials = new PullDcaCredentials(
                    receipt.Certificate, receipt.IssuerCertificates, SnapshotTrustList(trustList));
                RequireCompleteSigningTrustList(credentials.TrustList);
                if (m_signingJournal != null)
                {
                    await m_signingJournal.RecordReceivedAsync(
                        PullDcaConnection.SnapshotBinding(binding!), SnapshotCredentials(credentials), ct)
                        .ConfigureAwait(false);
                }
            }
            Report(progress, PullDcaStage.InstallingCredentials, applicationId, requestId);
            if (binding != null && m_credentialProvider is IPullDcaBoundCredentialProvider boundCredentials)
            {
                await boundCredentials.InstallAsync(
                    PullDcaConnection.SnapshotBinding(binding), SnapshotCredentials(credentials), ct)
                    .ConfigureAwait(false);
            }
            else
            {
                await m_credentialProvider.InstallAsync(
                    PullDcaConnection.SnapshotNodeId(applicationId), SnapshotCredentials(credentials), ct)
                    .ConfigureAwait(false);
            }
            if (m_signingJournal != null)
            {
                await m_signingJournal.RecordInstalledAsync(PullDcaConnection.SnapshotBinding(binding!), ct)
                    .ConfigureAwait(false);
            }
            return (credentials, binding);
        }

        /// <summary>
        /// Rejects partial trust images before recording or installing signing credentials.
        /// </summary>
        private static void RequireCompleteSigningTrustList(TrustListDataType trustList)
        {
            if (trustList.SpecifiedLists != (uint)TrustListMasks.All)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "Signing credential installation requires a complete trust list.");
            }
        }

        /// <summary>
        /// Receives signing output for one retained request without installing credentials.
        /// Consumed-request recovery also reads trust before the host validates the recovered assignment.
        /// </summary>
        private async ValueTask<PullDcaCertificateReceipt> ReceiveCertificateAsync(
            PullDcaRegistrar registrar,
            IPullDcaConnection connection,
            NodeId applicationId,
            NodeId requestId,
            PullDcaCertificateProfile profile,
            IProgress<PullDcaProgress>? progress,
            CancellationToken ct)
        {
            for (int attempt = 0; attempt < m_maximumFinishAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                Report(progress, PullDcaStage.FinishingCertificate, applicationId, requestId);
                ByteString certificate;
                ArrayOf<ByteString> issuers;
                try
                {
                    (certificate, issuers) = await connection.FinishAsync(applicationId, requestId, ct)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException error) when (error.StatusCode == StatusCodes.BadNothingToDo)
                {
                    certificate = default;
                    issuers = default;
                }
                catch (ServiceResultException error) when (
                    IsRetryableFinishFailure(error.StatusCode) && attempt + 1 < m_maximumFinishAttempts)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(m_finishRetryDelay, ct).ConfigureAwait(false);
                    continue;
                }
                catch (ServiceResultException error) when (
                    error.StatusCode == StatusCodes.BadInvalidArgument && m_signingRecovery != null)
                {
                    return await RecoverSigningCertificateAsync(
                        registrar, connection, applicationId, requestId, profile, ct).ConfigureAwait(false);
                }
                if (!certificate.IsEmpty)
                {
                    return new PullDcaCertificateReceipt(certificate, issuers);
                }
                if (attempt + 1 < m_maximumFinishAttempts)
                {
                    await Task.Delay(m_finishRetryDelay, ct).ConfigureAwait(false);
                }
            }
            throw new PullDcaCertificatePendingException(applicationId, requestId);
        }

        /// <summary>
        /// Limits retries to transport failures whose outcome may be unknown, never authorization or trust failures.
        /// The same signing request is retried; neither its key nor the borrowed session identity is replaced.
        /// </summary>
        private static bool IsRetryableFinishFailure(StatusCode statusCode)
        {
            return statusCode == StatusCodes.BadTimeout ||
                statusCode == StatusCodes.BadCommunicationError ||
                statusCode == StatusCodes.BadConnectionClosed ||
                statusCode == StatusCodes.BadSecureChannelClosed ||
                statusCode == StatusCodes.BadNoCommunication;
        }

        /// <summary>
        /// Implements Part 12 section 7.9.5 recovery for signing requests only, with explicit request-bound validation.
        /// </summary>
        private async ValueTask<PullDcaCertificateReceipt> RecoverSigningCertificateAsync(
            PullDcaRegistrar registrar,
            IPullDcaConnection connection,
            NodeId applicationId,
            NodeId requestId,
            PullDcaCertificateProfile profile,
            CancellationToken ct)
        {
            var (types, certificates) = await connection.GetCertificatesAsync(
                applicationId, profile.CertificateGroupId, ct).ConfigureAwait(false);
            if (types.IsNull || certificates.IsNull || types.Count != certificates.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError, "GetCertificates returned inconsistent output arrays.");
            }
            ByteString selected = default;
            for (int i = 0; i < types.Count; i++)
            {
                if (types[i] != profile.CertificateTypeId)
                {
                    continue;
                }
                if (certificates[i].IsEmpty || !selected.IsEmpty)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed, "The recovered certificate assignment is ambiguous.");
                }
                selected = ByteString.From(certificates[i].Span);
            }
            if (selected.IsEmpty)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound, "No certificate is assigned for the original signing profile.");
            }
            TrustListDataType trust = await connection.ReadTrustListAsync(
                applicationId, profile.CertificateGroupId, ct).ConfigureAwait(false);
            if (trust == null || trust.SpecifiedLists != (uint)TrustListMasks.All)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "Signing recovery requires a complete trust list.");
            }
            ArrayOf<ByteString> issuers = SnapshotBytes(trust.IssuerCertificates);
            var credentials = new PullDcaCredentials(
                selected.Copy(), SnapshotBytes(issuers), SnapshotTrustList(trust));
            await m_signingRecovery!.ValidateAsync(
                PullDcaConnection.SnapshotRegistrar(registrar),
                PullDcaConnection.SnapshotNodeId(applicationId),
                PullDcaConnection.SnapshotNodeId(requestId),
                PullDcaConnection.SnapshotProfile(profile),
                credentials,
                ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return new PullDcaCertificateReceipt(selected, issuers);
        }

        private CancellationTokenSource CreateDeadline(CancellationToken ct)
        {
            var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(m_operationTimeout);
            return timeout;
        }

        private static Func<PullDcaRegistrar, CancellationToken, ValueTask<IPullDcaConnection>> CreateConnectionFactory(
            PullDcaOptions options,
            Func<PullDcaRegistrar, CancellationToken, ValueTask<ManagedSession>> sessionAccessor,
            ITelemetryContext telemetry,
            IPullDcaSigningJournal? signingJournal,
            IPullDcaSigningRecoveryProvider? signingRecovery)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (sessionAccessor is null)
            {
                throw new ArgumentNullException(nameof(sessionAccessor));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            long maximumTrustListSize = options.MaximumTrustListSize;
            return async (registrar, ct) => new PullDcaConnection(
                await sessionAccessor(registrar, ct).ConfigureAwait(false), registrar, telemetry,
                maximumTrustListSize, signingJournal, signingRecovery as IPullDcaSigningAssignmentsObserver);
        }

        private static void ValidateRegistrar(PullDcaRegistrar registrar)
        {
            if (registrar is null)
            {
                throw new ArgumentNullException(nameof(registrar));
            }
            if (registrar.EndpointUrl == null || !registrar.EndpointUrl.IsAbsoluteUri ||
                registrar.RegistrarId.IsNull || registrar.CertificateDirectoryId.IsNull)
            {
                throw new ArgumentException("A registrar requires an absolute endpoint and resolved node identifiers.",
                    nameof(registrar));
            }
        }

        /// <summary>
        /// Detaches validated request data before borrowing a connection or awaiting a host operation.
        /// </summary>
        private static PullDcaRequest SnapshotRequest(PullDcaRequest request)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (request.IdentityOffer == null || request.IdentityOffer.Identities.IsEmpty ||
                request.IdentityOffer.Identities.Contains(static identity => identity.IsEmpty))
            {
                throw new ArgumentException("At least one non-empty identity must be offered.", nameof(request));
            }
            ValidateProfile(request.DcaCertificateProfile);
            return request with
            {
                IdentityOffer = new PullDcaIdentityOffer(
                    SnapshotBytes(request.IdentityOffer.Identities),
                    SnapshotBytes(request.IdentityOffer.Issuers),
                    request.IdentityOffer.Tickets.ToArray().ToArrayOf()),
                DcaCertificateProfile = PullDcaConnection.SnapshotProfile(request.DcaCertificateProfile),
                ManagedApplications = SnapshotManagedApplications(request.ManagedApplications),
                DcaCertificateRequestMode = ValidateRequestMode(request.DcaCertificateRequestMode)
            };
        }

        /// <summary>
        /// Detaches and validates managed application requests for either authentication path.
        /// </summary>
        private static ArrayOf<PullDcaManagedApplication> SnapshotManagedApplications(
            ArrayOf<PullDcaManagedApplication> managedApplications)
        {
            var applications = new PullDcaManagedApplication[managedApplications.Count];
            for (int i = 0; i < applications.Length; i++)
            {
                PullDcaManagedApplication application = managedApplications[i];
                if (application == null || application.Application == null ||
                    !Uri.TryCreate(application.Application.ApplicationUri, UriKind.Absolute, out _) ||
                    (application.ProtocolUri?.Length != 0 &&
                        !Uri.TryCreate(application.ProtocolUri, UriKind.Absolute, out _)))
                {
                    throw new ArgumentException("Managed applications require valid application and protocol URIs.",
                        nameof(managedApplications));
                }
                ValidateProfile(application.CertificateProfile);
                ApplicationRecordDataType record = application.Application;
                applications[i] = application with
                {
                    Application = new ApplicationRecordDataType
                    {
                        ApplicationId = PullDcaConnection.SnapshotNodeId(record.ApplicationId),
                        ApplicationUri = record.ApplicationUri,
                        ApplicationType = record.ApplicationType,
                        ApplicationNames = SnapshotApplicationNames(record.ApplicationNames),
                        ProductUri = record.ProductUri,
                        DiscoveryUrls = record.DiscoveryUrls.ToArray().ToArrayOf(),
                        ServerCapabilities = record.ServerCapabilities.ToArray().ToArrayOf()
                    },
                    CertificateProfile = PullDcaConnection.SnapshotProfile(application.CertificateProfile),
                    CertificateRequestMode = ValidateRequestMode(application.CertificateRequestMode)
                };
            }
            return applications;
        }

        /// <summary>
        /// Rejects requests that require a host key-pair installer when none is registered.
        /// </summary>
        private void ValidateKeyPairSupport(
            PullDcaCertificateRequestMode dcaRequestMode,
            ArrayOf<PullDcaManagedApplication> managedApplications)
        {
            bool requiresNewKeyPair = dcaRequestMode == PullDcaCertificateRequestMode.NewKeyPairRequest;
            for (int i = 0; i < managedApplications.Count; i++)
            {
                requiresNewKeyPair |=
                    managedApplications[i].CertificateRequestMode == PullDcaCertificateRequestMode.NewKeyPairRequest;
            }
            if (!requiresNewKeyPair)
            {
                return;
            }
            if (m_newKeyPairCredentialProvider == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    $"A {nameof(IPullDcaNewKeyPairCredentialProvider)} is required for a new key-pair request.");
            }
            if (m_newKeyPairRecoveryProvider == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    $"A {nameof(IPullDcaNewKeyPairRecoveryProvider)} is required for a new key-pair request.");
            }
        }

        /// <summary>
        /// Validates a requested certificate provisioning mode.
        /// </summary>
        private static PullDcaCertificateRequestMode ValidateRequestMode(
            PullDcaCertificateRequestMode requestMode)
        {
            if (requestMode is not PullDcaCertificateRequestMode.SigningRequest and
                not PullDcaCertificateRequestMode.NewKeyPairRequest)
            {
                throw new ArgumentOutOfRangeException(nameof(requestMode));
            }
            return requestMode;
        }

        /// <summary>
        /// Copies localized names without changing null or empty arrays or retaining caller-owned translation state.
        /// </summary>
        private static ArrayOf<LocalizedText> SnapshotApplicationNames(ArrayOf<LocalizedText> applicationNames)
        {
            if (applicationNames.IsNull)
            {
                return applicationNames;
            }

            LocalizedText[] snapshot = applicationNames.Span.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i] = snapshot[i].Copy();
            }
            return snapshot.ToArrayOf();
        }

        /// <summary>
        /// Copies each byte buffer as well as the array so mutable caller storage cannot alter an active request.
        /// </summary>
        private static ArrayOf<ByteString> SnapshotBytes(ArrayOf<ByteString> values)
        {
            if (values.IsNull)
            {
                return default;
            }
            var detached = new ByteString[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                detached[i] = values[i].Copy();
            }
            return detached;
        }

        /// <summary>
        /// Detaches a standard ticket without relying on overridable clone implementations.
        /// </summary>
        private static BaseTicketType SnapshotTicket(BaseTicketType ticket)
        {
            BaseTicketType snapshot;
            if (ticket.GetType() == typeof(DeviceIdentityTicketType))
            {
                var device = (DeviceIdentityTicketType)ticket;
                snapshot = new DeviceIdentityTicketType { ProductInstanceUri = device.ProductInstanceUri };
            }
            else if (ticket.GetType() == typeof(CompositeIdentityTicketType))
            {
                var composite = (CompositeIdentityTicketType)ticket;
                snapshot = new CompositeIdentityTicketType
                {
                    CompositeInstanceUri = composite.CompositeInstanceUri,
                    Devices = composite.Devices.ToArray().ToArrayOf(),
                    Composites = composite.Composites.ToArray().ToArrayOf()
                };
            }
            else
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The selected ticket type cannot be copied safely.");
            }

            snapshot.ManufacturerName = ticket.ManufacturerName;
            snapshot.ModelName = ticket.ModelName;
            snapshot.ModelVersion = ticket.ModelVersion;
            snapshot.HardwareRevision = ticket.HardwareRevision;
            snapshot.SoftwareRevision = ticket.SoftwareRevision;
            snapshot.SerialNumber = ticket.SerialNumber;
            snapshot.ManufactureDate = ticket.ManufactureDate;
            if (!ticket.Authorities.IsNull)
            {
                var authorities = new CertificateAuthorityType[ticket.Authorities.Count];
                for (int i = 0; i < authorities.Length; i++)
                {
                    CertificateAuthorityType authority = ticket.Authorities[i];
                    if (authority == null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed, "The selected ticket has a null authority.");
                    }
                    if (authority.GetType() != typeof(CertificateAuthorityType))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNotSupported, "The selected ticket authority cannot be copied safely.");
                    }
                    authorities[i] = new CertificateAuthorityType
                    {
                        AuthorityCertificate = authority.AuthorityCertificate.Copy(),
                        IssuerCertificates = SnapshotBytes(authority.IssuerCertificates)
                    };
                }
                snapshot.Authorities = authorities;
            }
            else
            {
                snapshot.Authorities = default;
            }
            return snapshot;
        }

        /// <summary>
        /// Detaches the selected manager from the connection and software-update provider.
        /// </summary>
        private static ManagerDescription SnapshotManager(ManagerDescription manager)
        {
            if (manager.GetType() != typeof(ManagerDescription))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "A derived software-update manager cannot be copied safely.");
            }
            return new ManagerDescription
            {
                Name = manager.Name.Copy(),
                IsRequired = manager.IsRequired,
                PurposeUri = manager.PurposeUri,
                ProtocolUri = manager.ProtocolUri,
                EndpointUrls = manager.EndpointUrls.ToArray().ToArrayOf()
            };
        }

        /// <summary>
        /// Detaches public credential data before passing it across a host boundary.
        /// </summary>
        private static PullDcaCredentials SnapshotCredentials(PullDcaCredentials credentials)
        {
            return new PullDcaCredentials(
                credentials.Certificate.Copy(),
                SnapshotBytes(credentials.IssuerCertificates),
                SnapshotTrustList(credentials.TrustList));
        }

        /// <summary>
        /// Detaches the complete or partial trust-list payload without changing its list mask.
        /// </summary>
        private static TrustListDataType SnapshotTrustList(TrustListDataType trust)
        {
            return new TrustListDataType
            {
                SpecifiedLists = trust.SpecifiedLists,
                TrustedCertificates = SnapshotBytes(trust.TrustedCertificates),
                TrustedCrls = SnapshotBytes(trust.TrustedCrls),
                IssuerCertificates = SnapshotBytes(trust.IssuerCertificates),
                IssuerCrls = SnapshotBytes(trust.IssuerCrls)
            };
        }

        private static void ValidateProfile(PullDcaCertificateProfile profile)
        {
            if (profile is null || profile.CertificateTypeId.IsNull)
            {
                throw new ArgumentException("An explicit certificate type is required.", nameof(profile));
            }
        }

        private static void Report(
            IProgress<PullDcaProgress>? progress,
            PullDcaStage stage,
            NodeId applicationId = default,
            NodeId requestId = default)
        {
            progress?.Report(new PullDcaProgress(
                stage,
                PullDcaConnection.SnapshotNodeId(applicationId),
                PullDcaConnection.SnapshotNodeId(requestId)));
        }

        /// <summary>
        /// Borrowed connection factory.
        /// </summary>
        private readonly Func<PullDcaRegistrar, CancellationToken, ValueTask<IPullDcaConnection>> m_connectionFactory;

        /// <summary>
        /// Host-owned selected-identity installation.
        /// </summary>
        private readonly IPullDcaIdentityProvider m_identityProvider;

        /// <summary>
        /// Host-owned credential installation.
        /// </summary>
        private readonly IPullDcaCredentialProvider m_credentialProvider;

        /// <summary>
        /// Optional real software-update bridge.
        /// </summary>
        private readonly IPullDcaSoftwareUpdateProvider? m_softwareUpdateProvider;

        /// <summary>
        /// Optional real registrar discovery.
        /// </summary>
        private readonly IPullDcaRegistrarDiscovery? m_discovery;

        /// <summary>
        /// Optional host-owned received-certificate persistence.
        /// </summary>
        private readonly IPullDcaCertificateReceiptStore? m_receipts;

        /// <summary>
        /// Explicit request-bound validation for Part 12 signing recovery.
        /// </summary>
        private readonly IPullDcaSigningRecoveryProvider? m_signingRecovery;

        /// <summary>
        /// Optional host-owned signing and installation journal.
        /// </summary>
        private readonly IPullDcaSigningJournal? m_signingJournal;

        /// <summary>Optional host-owned installer for server-generated key pairs.</summary>
        private readonly IPullDcaNewKeyPairCredentialProvider? m_newKeyPairCredentialProvider;

        /// <summary>Optional host-owned durable recovery for server-generated key pairs.</summary>
        private readonly IPullDcaNewKeyPairRecoveryProvider? m_newKeyPairRecoveryProvider;

        /// <summary>
        /// Configured registrar candidates, not a durable lifecycle store.
        /// </summary>
        private readonly ArrayOf<PullDcaRegistrar> m_registrars;

        /// <summary>
        /// Finite certificate completion attempt limit.
        /// </summary>
        private readonly int m_maximumFinishAttempts;

        /// <summary>Maximum fresh key-pair requests after a consumed completion request.</summary>
        private readonly int m_maximumNewKeyPairRestarts;

        /// <summary>
        /// Bounded delay between pending responses.
        /// </summary>
        private readonly TimeSpan m_finishRetryDelay;

        /// <summary>
        /// Cooperative operation deadline.
        /// </summary>
        private readonly TimeSpan m_operationTimeout;

        /// <summary>
        /// Rejects concurrent runs that would race identity changes.
        /// </summary>
        private int m_running;
    }
}
