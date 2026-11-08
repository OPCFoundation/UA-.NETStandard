/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
                options,
                CreateConnectionFactory(options, sessionAccessor, telemetry),
                identityProvider,
                credentialProvider,
                softwareUpdateProvider,
                discovery,
                receipts,
                signingRecovery)
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
            IPullDcaSigningRecoveryProvider? signingRecovery = null)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (options.MaximumFinishAttempts < 1 || options.MaximumFinishAttempts > 100)
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
            m_registrars = options.Registrars.ToArray() ?? [];
            m_maximumFinishAttempts = options.MaximumFinishAttempts;
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
        public async ValueTask<PullDcaResult> RunAsync(
            PullDcaRegistrar registrar,
            PullDcaRequest request,
            IProgress<PullDcaProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateRegistrar(registrar);
            request = SnapshotRequest(request);
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
                if (identity == null || identity.SelectedIdentity.IsEmpty ||
                    !request.IdentityOffer.Identities.Contains(identity.SelectedIdentity) ||
                    identity.ApplicationId.IsNull || identity.MatchingTicket == null)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }

                Report(progress, PullDcaStage.ActivatingIdentity, identity.ApplicationId);
                await m_identityProvider.ActivateAsync(identity.SelectedIdentity, ct).ConfigureAwait(false);
                await connection.ReloadAndReconnectAsync(ct).ConfigureAwait(false);

                if (identity.SoftwareUpdateManager is { } manager)
                {
                    Report(progress, PullDcaStage.UpdatingSoftware, identity.ApplicationId);
                    if (m_softwareUpdateProvider == null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadConfigurationError, "A real software-update provider is required.");
                    }
                    await m_softwareUpdateProvider.CompleteAsync(
                        registrar, identity, manager, ct).ConfigureAwait(false);
                }

                PullDcaCredentials dcaCredentials = await ProvisionAsync(
                    registrar, connection, identity.ApplicationId, request.DcaCertificateProfile, progress, ct)
                    .ConfigureAwait(false);
                Report(progress, PullDcaStage.ActivatingDca, identity.ApplicationId);
                await connection.ReloadAndReconnectAsync(ct).ConfigureAwait(false);
                Report(progress, PullDcaStage.GettingManagers, identity.ApplicationId);
                ArrayOf<ManagerDescription> managers = await connection.GetManagersAsync(ct).ConfigureAwait(false);

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
            managedApplications = SnapshotManagedApplications(managedApplications);
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
        /// Completes a previously persisted signing request without creating another private key or request.
        /// The host restores the original registrar, channel identity, retained private key and certificate profile.
        /// This installs credentials only; the host reconnects the DCA and resumes its remaining lifecycle steps.
        /// </summary>
        public async ValueTask<PullDcaCredentials> CompleteCertificateRequestAsync(
            PullDcaRegistrar registrar,
            NodeId applicationId,
            NodeId requestId,
            PullDcaCertificateProfile profile,
            IProgress<PullDcaProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateRegistrar(registrar);
            ValidateProfile(profile);
            if (applicationId.IsNull || requestId.IsNull)
            {
                throw new ArgumentException("Persisted application and request identifiers are required.");
            }
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
                return await FinishAndInstallAsync(
                    registrar, connection, applicationId, requestId, profile, progress, timeout.Token)
                    .ConfigureAwait(false);
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
                if (applicationId.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadUnexpectedError);
                }
                PullDcaCredentials credentials = await ProvisionAsync(
                    registrar, connection, applicationId, application.CertificateProfile, progress, ct)
                    .ConfigureAwait(false);
                applications.Add(new PullDcaProvisionedApplication(applicationId, credentials));
            }
            return applications.ToArrayOf();
        }

        private async ValueTask<PullDcaCredentials> ProvisionAsync(
            PullDcaRegistrar registrar,
            IPullDcaConnection connection,
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            IProgress<PullDcaProgress>? progress,
            CancellationToken ct)
        {
            Report(progress, PullDcaStage.RequestingCertificate, applicationId);
            ByteString csr = await m_credentialProvider.CreateSigningRequestAsync(applicationId, profile, ct)
                .ConfigureAwait(false);
            if (csr.IsEmpty)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "The CSR provider returned no CSR.");
            }
            NodeId requestId = await connection.StartAsync(applicationId, profile, csr, ct).ConfigureAwait(false);
            if (requestId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError);
            }
            if (m_signingRecovery is IPullDcaSigningRequestObserver observer)
            {
                await observer.OnSigningRequestStartedAsync(registrar, applicationId, requestId, profile, csr, ct)
                    .ConfigureAwait(false);
            }
            return await FinishAndInstallAsync(registrar, connection, applicationId, requestId, profile, progress, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Uses one existing certificate request for bounded polling and host-owned installation.
        /// </summary>
        private async ValueTask<PullDcaCredentials> FinishAndInstallAsync(
            PullDcaRegistrar registrar,
            IPullDcaConnection connection,
            NodeId applicationId,
            NodeId requestId,
            PullDcaCertificateProfile profile,
            IProgress<PullDcaProgress>? progress,
            CancellationToken ct)
        {
            PullDcaCertificateReceipt receipt = m_receipts == null
                ? await ReceiveCertificateAsync(registrar, connection, applicationId, requestId, profile, progress, ct)
                    .ConfigureAwait(false)
                : await m_receipts.GetOrCreateAsync(
                    registrar, applicationId, requestId, profile,
                    token => ReceiveCertificateAsync(
                        registrar, connection, applicationId, requestId, profile, progress, token), ct)
                    .ConfigureAwait(false);
            if (receipt == null || receipt.Certificate.IsEmpty)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "The signing receipt contains no issued certificate.");
            }
            ct.ThrowIfCancellationRequested();
            Report(progress, PullDcaStage.ReadingTrustList, applicationId, requestId);
            TrustListDataType trustList = await connection.ReadTrustListAsync(
                applicationId, profile.CertificateGroupId, ct).ConfigureAwait(false);
            if (trustList == null)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError);
            }
            var credentials = new PullDcaCredentials(receipt.Certificate, receipt.IssuerCertificates, trustList);
            Report(progress, PullDcaStage.InstallingCredentials, applicationId, requestId);
            await m_credentialProvider.InstallAsync(applicationId, credentials, ct).ConfigureAwait(false);
            return credentials;
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
            if (types.Count != certificates.Count)
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
            var credentials = new PullDcaCredentials(selected, trust.IssuerCertificates, trust);
            await m_signingRecovery!.ValidateAsync(
                registrar, applicationId, requestId, profile, credentials, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return new PullDcaCertificateReceipt(selected, credentials.IssuerCertificates);
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
            ITelemetryContext telemetry)
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
                await sessionAccessor(registrar, ct).ConfigureAwait(false), registrar, telemetry, maximumTrustListSize);
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
                ManagedApplications = SnapshotManagedApplications(request.ManagedApplications)
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
                        ApplicationId = record.ApplicationId,
                        ApplicationUri = record.ApplicationUri,
                        ApplicationType = record.ApplicationType,
                        ApplicationNames = record.ApplicationNames.ToArray().ToArrayOf(),
                        ProductUri = record.ProductUri,
                        DiscoveryUrls = record.DiscoveryUrls.ToArray().ToArrayOf(),
                        ServerCapabilities = record.ServerCapabilities.ToArray().ToArrayOf()
                    }
                };
            }
            return applications;
        }

        /// <summary>
        /// Copies each byte buffer as well as the array so mutable caller storage cannot alter an active request.
        /// </summary>
        private static ArrayOf<ByteString> SnapshotBytes(ArrayOf<ByteString> values)
        {
            var detached = new ByteString[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                detached[i] = ByteString.From(values[i].Span);
            }
            return detached;
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
            progress?.Report(new PullDcaProgress(stage, applicationId, requestId));
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
        /// Configured registrar candidates, not a durable lifecycle store.
        /// </summary>
        private readonly ArrayOf<PullDcaRegistrar> m_registrars;

        /// <summary>
        /// Finite certificate completion attempt limit.
        /// </summary>
        private readonly int m_maximumFinishAttempts;

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
