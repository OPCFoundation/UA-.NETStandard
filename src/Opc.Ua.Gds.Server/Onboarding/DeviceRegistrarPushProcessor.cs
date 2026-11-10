/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Security.Certificates;
using Ua = Opc.Ua;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Pushes an approved DCA credential using generated clients and retained, request-correlated GDS issuance.
    /// After the DCA credential is confirmed on a non-singleton device, the processor pushes credentials and trust
    /// lists to authorized managed applications. Server applications require host-supplied endpoint metadata and
    /// proof of their installed certificate on a separate secure channel. This processor never installs software
    /// or grants a singleton authority over managed applications.
    /// </summary>
    public sealed class DeviceRegistrarPushProcessor : IDeviceRegistrarPushProcessor, IDisposable
    {
        /// <summary>
        /// Borrows host-selected connection, enrollment, managed application, policy, and registrar providers.
        /// Managed application providers are required when the device exposes Server or ClientAndServer entries.
        /// No provider is replaced with process-local storage or automatic trust.
        /// </summary>
        public DeviceRegistrarPushProcessor(
            IDeviceRegistrarPushConnectionProvider connections,
            IDeviceRegistrarPushEnrollmentProvider enrollments,
            OnboardingPushIdentityValidator identities,
            IDeviceRegistrarOnboardingPolicy policy,
            IApplicationsDatabase applications,
            IDeviceRegistrarIdentityLifecycle candidates,
            IDeviceRegistrarCertificateLifecycle certificates,
            ICertificateRequestIssuanceStore issuances,
            IDeviceRegistrarLifecycle lifecycle,
            IDeviceRegistrarAuthorization authorization,
            ITelemetryContext telemetry,
            IDeviceRegistrarManagedApplicationProvider? managedApplications = null,
            IDeviceRegistrarManagedApplicationChannelVerifier? managedApplicationChannels = null,
            IDeviceRegistrarExternalAuthenticationProvider? externalAuthentication = null)
            : this(connections, enrollments, identities, policy, applications, candidates, certificates,
                issuances, lifecycle, authorization, telemetry, TimeProvider.System, managedApplications,
                managedApplicationChannels, externalAuthentication)
        {
        }

        /// <summary>
        /// Creates the processor with an injectable clock for independent transaction cleanup deadlines.
        /// All connection and registrar providers retain their existing ownership.
        /// </summary>
        public DeviceRegistrarPushProcessor(
            IDeviceRegistrarPushConnectionProvider connections,
            IDeviceRegistrarPushEnrollmentProvider enrollments,
            OnboardingPushIdentityValidator identities,
            IDeviceRegistrarOnboardingPolicy policy,
            IApplicationsDatabase applications,
            IDeviceRegistrarIdentityLifecycle candidates,
            IDeviceRegistrarCertificateLifecycle certificates,
            ICertificateRequestIssuanceStore issuances,
            IDeviceRegistrarLifecycle lifecycle,
            IDeviceRegistrarAuthorization authorization,
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            IDeviceRegistrarManagedApplicationProvider? managedApplications = null,
            IDeviceRegistrarManagedApplicationChannelVerifier? managedApplicationChannels = null,
            IDeviceRegistrarExternalAuthenticationProvider? externalAuthentication = null)
        {
            m_connections = connections ?? throw new ArgumentNullException(nameof(connections));
            m_enrollments = enrollments ?? throw new ArgumentNullException(nameof(enrollments));
            m_identities = identities ?? throw new ArgumentNullException(nameof(identities));
            m_policy = policy ?? throw new ArgumentNullException(nameof(policy));
            m_applications = applications ?? throw new ArgumentNullException(nameof(applications));
            m_candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
            m_certificates = certificates ?? throw new ArgumentNullException(nameof(certificates));
            m_issuances = issuances ?? throw new ArgumentNullException(nameof(issuances));
            m_lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            m_authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            m_managedApplications = managedApplications;
            m_managedApplicationChannels = managedApplicationChannels;
            m_externalAuthentication = externalAuthentication;
        }

        /// <inheritdoc/>
        public async ValueTask ProcessAsync(
            ApplicationDescription application,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(DeviceRegistrarPushProcessor));
            }
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            cancellationToken.ThrowIfCancellationRequested();
            ApplicationDescription snapshot = CoreUtils.Clone(application)!;
            ValidateApplication(snapshot);
            await m_attemptGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ProcessCoreAsync(snapshot, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_attemptGate.Release();
            }
        }

        /// <summary>
        /// Releases attempt coordination after the host has stopped scheduling work and awaited active attempts.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                m_attemptGate.Dispose();
            }
        }

        /// <summary>
        /// Completes one enrollment and its independent installed-certificate proof without another attempt
        /// replacing the device's pending key or trust transaction through this processor.
        /// </summary>
        private async ValueTask ProcessCoreAsync(
            ApplicationDescription snapshot,
            CancellationToken cancellationToken)
        {
            ByteString installed = default;
            ByteString configuringPeer = default;
            ArrayOf<ByteString> issuerCertificates = default;
            bool updateRequired = false;
            NodeId applicationId = NodeId.Null;
            OnboardingIdentitySelection? acceptedIdentity = null;
            TrustListDataType? intendedTrust = null;
            await WithConnectionAsync(snapshot, default, async (connection, dispatch) =>
            {
                ValidateConnection(snapshot, connection, dispatch.Session);
                var device = new ProvisionableDeviceTypeClient(
                    dispatch.Session, connection.ProvisionableDeviceId, m_telemetry);
                bool singleton = await ReadSingletonAsync(
                    dispatch.Session, connection.IsSingletonId, cancellationToken).ConfigureAwait(false);

                ByteString peer = ReadPeer(dispatch.Session);
                DeviceRegistrarPushEnrollment? retained = await FindRetainedAsync(snapshot.ApplicationUri!, peer,
                    cancellationToken).ConfigureAwait(false);
                DeviceRegistrarExternalAuthenticationResult? external = null;
                if (m_externalAuthentication != null &&
                    (retained == null || retained.Identity.IsExternalAuthentication))
                {
                    external = await m_externalAuthentication.EvaluateAsync(
                        retained?.Identity.SelectedIdentity ?? peer, cancellationToken).ConfigureAwait(false);
                }
                if (retained?.Identity.IsExternalAuthentication == true && external == null)
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateUntrusted);
                }
                OnboardingIdentitySelection identity;
                if (retained != null)
                {
                    identity = retained.Identity;
                    if (external != null && external.ProductInstanceUri != identity.ProductInstanceUri)
                    {
                        throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                    }
                }
                else if (external != null)
                {
                    identity = OnboardingIdentitySelection.FromExternalAuthentication(
                        peer, external.ProductInstanceUri);
                }
                else
                {
                    ArrayOf<string> tickets =
                        await device.RequestTicketsAsync(cancellationToken).ConfigureAwait(false);
                    identity = await m_identities.ValidatePeerAsync(
                        dispatch.Session.TransportChannel, tickets, cancellationToken).ConfigureAwait(false);
                }
                if (retained == null && identity.SelectedIdentity != peer)
                {
                    throw new ServiceResultException(StatusCodes.BadSecureChannelIdInvalid);
                }
                acceptedIdentity = identity;
                DeviceRegistrarOnboardingDecision decision = external?.Decision ??
                    await m_policy.EvaluateAsync(identity, cancellationToken).ConfigureAwait(false);
                if (decision.Application == null ||
                    !string.Equals(decision.Application.ApplicationUri, snapshot.ApplicationUri,
                        StringComparison.Ordinal) ||
                    decision.RequiresSoftwareUpdate != (decision.SoftwareUpdateManager != null))
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError);
                }
                RequirePeer(dispatch, peer);
                applicationId = await RegisterAsync(identity, decision, cancellationToken).ConfigureAwait(false);
                string productInstanceUri = identity.ProductInstanceUri;
                await RetainScopeAsync(productInstanceUri, singleton, cancellationToken).ConfigureAwait(false);
                await RequireApprovalAsync(identity, applicationId, singleton, cancellationToken).ConfigureAwait(false);

                ByteString createdSigningRequest = ByteString.Empty;
                if (retained == null)
                {
                    var configuration = new ServerConfigurationTypeClient(
                        dispatch.Session, connection.ServerConfigurationId, m_telemetry);
                    int signingRequestCreated = 0;
                    retained = await m_enrollments.GetOrCreateAsync(identity, applicationId,
                        async ct =>
                        {
                            if (Interlocked.Exchange(ref signingRequestCreated, 1) != 0)
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadInvalidState, "CSR creation was already attempted.");
                            }
                            RequirePeer(dispatch, peer);
                            await RequireApprovalAsync(identity, applicationId, singleton, ct).ConfigureAwait(false);
                            ByteString csr = await configuration.CreateSigningRequestAsync(
                                connection.CertificateGroupId, connection.CertificateTypeId, string.Empty,
                                true, Nonce.CreateRandomNonceData(32).ToByteString(), ct).ConfigureAwait(false);
                            if (csr.IsEmpty)
                            {
                                throw new ServiceResultException(StatusCodes.BadInvalidState);
                            }
                            createdSigningRequest = ByteString.From(csr.Span);
                            return ByteString.From(createdSigningRequest.Span);
                        }, cancellationToken).ConfigureAwait(false);
                }
                ValidateEnrollment(retained, identity, applicationId);
                if (!createdSigningRequest.IsEmpty && retained.CertificateRequest != createdSigningRequest)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed, "The retained CSR differs from the device's response.");
                }
                retained = retained with
                {
                    TrustList = CoreUtils.Clone(retained.TrustList)!,
                    CertificateRequest = ByteString.From(retained.CertificateRequest.Span)
                };
                CertificateRequestIssuance issuance = await ReadIssuanceAsync(
                    retained, cancellationToken).ConfigureAwait(false);
                RequireSigningResult(
                    retained, issuance, snapshot.ApplicationUri!, connection.CertificateTypeId);
                installed = issuance.Certificate;
                await RequireApprovalAsync(identity, applicationId, singleton, cancellationToken).ConfigureAwait(false);
                RequirePeer(dispatch, peer);

                DeviceRegistrarDevice current = await RequireDeviceAsync(
                    productInstanceUri, applicationId, cancellationToken).ConfigureAwait(false);
                TrustListDataType trustList = retained.TrustList;
                intendedTrust = trustList;
                var server = new ServerConfigurationTypeClient(
                    dispatch.Session, connection.ServerConfigurationId, m_telemetry);
                if (peer == installed &&
                    (current.PendingDcaCertificate == peer || current.DcaCertificate == peer))
                {
                    // The channel proves only the installed certificate. Application setup also includes the
                    // TrustList (OPC 10000-21, 4.3.3), which may have been replaced since it was applied.
                    // Confirmation always happens on the proof lease, which reads the TrustList back first.
                    if (await HoldsTrustListAsync(
                        dispatch.Session, connection.TrustListId, trustList, cancellationToken).ConfigureAwait(false))
                    {
                        return;
                    }
                    // Applying the TrustList may close this channel, so confirmation waits for the new lease.
                    await RequireApprovalAsync(identity, applicationId, singleton, cancellationToken)
                        .ConfigureAwait(false);
                    RequirePeer(dispatch, peer);
                    if (await StageChangesAsync(server,
                        () => WriteTrustListAsync(
                            dispatch.Session, connection.TrustListId, trustList, cancellationToken),
                        null).ConfigureAwait(false))
                    {
                        await CommitStagedChangesAsync(server, async () =>
                        {
                            await RequireApprovalAsync(identity, applicationId, singleton, cancellationToken)
                                .ConfigureAwait(false);
                            RequirePeer(dispatch, peer);
                        }, null, cancellationToken).ConfigureAwait(false);
                    }
                    return;
                }
                if (!current.PendingDcaCertificate.IsEmpty && current.PendingDcaCertificate != installed)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                await m_certificates.RecordIssuedDcaCertificateAsync(
                    productInstanceUri, applicationId, installed, cancellationToken).ConfigureAwait(false);
                RequirePeer(dispatch, peer);

                bool applyTrust = await StageChangesAsync(server,
                    () => WriteTrustListAsync(dispatch.Session, connection.TrustListId, trustList, cancellationToken),
                    null).ConfigureAwait(false);
                if (applyTrust)
                {
                    await CommitStagedChangesAsync(server, async () =>
                    {
                        await RequireApprovalAsync(identity, applicationId, singleton, cancellationToken)
                            .ConfigureAwait(false);
                        RequirePeer(dispatch, peer);
                    }, null, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await RequireApprovalAsync(identity, applicationId, singleton, cancellationToken)
                        .ConfigureAwait(false);
                    RequirePeer(dispatch, peer);
                }
                configuringPeer = peer;
                issuerCertificates = issuance.IssuerCertificates.ToList()
                    .Select(static issuer => ByteString.From(issuer.Span)).ToArrayOf();
                updateRequired = true;
            }, cancellationToken).ConfigureAwait(false);

            if (updateRequired)
            {
                // Application-group UpdateCertificate validates against the committed TrustList.
                // Applying trust may close the first channel, so certificate transfer uses a new lease.
                await WithConnectionAsync(snapshot, configuringPeer, async (connection, dispatch) =>
                {
                    ValidateConnection(snapshot, connection, dispatch.Session);
                    RequirePeer(dispatch, configuringPeer);
                    bool singleton = await ReadSingletonAsync(
                        dispatch.Session, connection.IsSingletonId, cancellationToken).ConfigureAwait(false);
                    OnboardingIdentitySelection identity = acceptedIdentity ??
                        throw new ServiceResultException(StatusCodes.BadInvalidState);
                    await RequireApprovalAsync(
                        identity, applicationId, singleton, cancellationToken).ConfigureAwait(false);
                    RequirePeer(dispatch, configuringPeer);
                    var server = new ServerConfigurationTypeClient(
                        dispatch.Session, connection.ServerConfigurationId, m_telemetry);
                    bool applyCertificate = await StageChangesAsync(server,
                        () => server.UpdateCertificateAsync(
                            connection.CertificateGroupId, connection.CertificateTypeId, installed,
                            issuerCertificates, string.Empty, default, cancellationToken),
                        null).ConfigureAwait(false);
                    if (applyCertificate)
                    {
                        await CommitStagedChangesAsync(server, async () =>
                        {
                            await RequireApprovalAsync(identity, applicationId, singleton, cancellationToken)
                                .ConfigureAwait(false);
                            RequirePeer(dispatch, configuringPeer);
                        }, null, cancellationToken).ConfigureAwait(false);
                    }
                }, cancellationToken).ConfigureAwait(false);
            }

            // A new connection is required even if ApplyChanges did not request a restart.
            await WithConnectionAsync(snapshot, installed, async (connection, proof) =>
            {
                ValidateConnection(snapshot, connection, proof.Session);
                if (ReadPeer(proof.Session) != installed)
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
                }
                var device = new ProvisionableDeviceTypeClient(
                    proof.Session, connection.ProvisionableDeviceId, m_telemetry);
                if (acceptedIdentity?.IsExternalAuthentication != true)
                {
                    await device.RequestTicketsAsync(cancellationToken).ConfigureAwait(false);
                }
                bool singleton = await ReadSingletonAsync(
                    proof.Session, connection.IsSingletonId, cancellationToken).ConfigureAwait(false);
                OnboardingIdentitySelection identity = acceptedIdentity ??
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                TrustListDataType trustList = intendedTrust ??
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                if (!await HoldsTrustListAsync(proof.Session, connection.TrustListId, trustList, cancellationToken)
                    .ConfigureAwait(false))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The device did not report the pushed TrustList.");
                }
                await ConfirmAsync(proof, identity, applicationId, singleton, installed, cancellationToken)
                    .ConfigureAwait(false);
                if (!singleton)
                {
                    await PushManagedApplicationsAsync(connection, proof, identity, applicationId,
                        snapshot.ApplicationUri!, installed, cancellationToken).ConfigureAwait(false);
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Pushes credentials to every managed application the confirmed DCA exposes on the proven channel.
        /// A failure for one application does not prevent the others; all failures are reported together.
        /// </summary>
        private async ValueTask PushManagedApplicationsAsync(
            IDeviceRegistrarPushConnection connection,
            SessionRequestContext dispatch,
            OnboardingIdentitySelection identity,
            NodeId dcaApplicationId,
            string dcaApplicationUri,
            ByteString peer,
            CancellationToken ct)
        {
            List<NodeId> nodes = await FindManagedApplicationsAsync(
                dispatch.Session, connection.ProvisionableDeviceId, ct).ConfigureAwait(false);
            List<Exception>? failures = null;
            var retireSession = new StrongBox<bool>();
            foreach (NodeId node in nodes)
            {
                try
                {
                    await PushManagedApplicationAsync(connection, dispatch, identity, dcaApplicationId,
                        dcaApplicationUri, peer, node, retireSession, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error)
                {
                    (failures ??= []).Add(error);
                    // ApplyChanges commits every change queued in the session, so a later application
                    // must not run when changes remain or an earlier request can still stage them.
                    if (retireSession.Value || dispatch.Invalidated.IsCompleted)
                    {
                        break;
                    }
                }
            }
            if (failures != null)
            {
                throw new AggregateException("One or more managed applications were not provisioned.", failures);
            }
        }

        /// <summary>
        /// Lists ApplicationConfiguration instances below the device and in the standard ManagedApplications folder.
        /// </summary>
        private static async ValueTask<List<NodeId>> FindManagedApplicationsAsync(
            ISession session,
            NodeId deviceId,
            CancellationToken ct)
        {
            var found = new List<NodeId>();
            var seen = new HashSet<NodeId>();
            await AddManagedApplicationsAsync(session, deviceId, found, seen, ct).ConfigureAwait(false);
            try
            {
                await AddManagedApplicationsAsync(session, s_managedApplicationsFolderId, found, seen, ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException error) when (error.StatusCode == StatusCodes.BadNodeIdUnknown)
            {
                // The ManagedApplications folder is optional on a device.
            }
            return found;
        }

        /// <summary>
        /// Adds the ApplicationConfigurationType instances, including vendor subtypes, referenced by one parent.
        /// </summary>
        private static async ValueTask AddManagedApplicationsAsync(
            ISession session,
            NodeId parent,
            List<NodeId> found,
            HashSet<NodeId> seen,
            CancellationToken ct)
        {
            ArrayOf<ReferenceDescription> references = await ClientModelAccess.BrowseAsync(
                session, parent, (uint)NodeClass.Object, ct).ConfigureAwait(false);
            for (int index = 0; index < references.Count; index++)
            {
                ReferenceDescription reference = references[index];
                NodeId node = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                NodeId type = ExpandedNodeId.ToNodeId(reference.TypeDefinition, session.NamespaceUris);
                if (!node.IsNull && !type.IsNull && !seen.Contains(node) &&
                    await session.NodeCache.IsTypeOfAsync(
                        type, Opc.Ua.ObjectTypeIds.ApplicationConfigurationType, ct).ConfigureAwait(false))
                {
                    seen.Add(node);
                    found.Add(node);
                }
            }
        }

        /// <summary>
        /// Registers, authorizes and approves one managed application, then installs its retained GDS issuance
        /// through the application's own ApplicationConfiguration methods and TrustList.
        /// The certificate and the TrustList are each replaced only when the application does not already
        /// report the retained value, and each replacement is read back before the push succeeds.
        /// </summary>
        private async ValueTask PushManagedApplicationAsync(
            IDeviceRegistrarPushConnection connection,
            SessionRequestContext dispatch,
            OnboardingIdentitySelection identity,
            NodeId dcaApplicationId,
            string dcaApplicationUri,
            ByteString peer,
            NodeId node,
            StrongBox<bool> retireSession,
            CancellationToken ct)
        {
            ISession session = dispatch.Session;
            ApplicationRecordDataType record = await ReadManagedApplicationAsync(session, node, ct)
                .ConfigureAwait(false);
            if (string.Equals(record.ApplicationUri, dcaApplicationUri, StringComparison.Ordinal))
            {
                return;
            }
            bool isManagedServer =
                record.ApplicationType is ApplicationType.Server or ApplicationType.ClientAndServer;
            DeviceRegistrarManagedApplicationTarget managedTarget = null!;
            ApplicationDescription? managedDescription = null;
            ByteString managedInstalledCertificate = ByteString.Empty;
            NodeId managedGroup = NodeId.Null;
            NodeId managedTrustListId = NodeId.Null;
            NodeId certificateTypeId = connection.CertificateTypeId;
            ServerConfigurationTypeClient? managedConfiguration = null;
            Func<CancellationToken, ValueTask>? prepareAuthorizationRecord = null;
            Func<CancellationToken, ValueTask>? beforeRegistration = null;
            if (isManagedServer)
            {
                IDeviceRegistrarManagedApplicationProvider managedApplications = m_managedApplications ??
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "Managed Server provisioning requires an endpoint provider and secure-channel verifier.");
                IDeviceRegistrarManagedApplicationChannelVerifier managedApplicationChannels =
                    m_managedApplicationChannels ??
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "Managed Server provisioning requires an endpoint provider and secure-channel verifier.");
                string applicationUri = record.ApplicationUri ??
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                prepareAuthorizationRecord = async metadataToken =>
                {
                    DeviceRegistrarManagedApplicationTarget target =
                        await managedApplications.GetTargetAsync(applicationUri, metadataToken)
                            .ConfigureAwait(false) ?? throw new ServiceResultException(
                                StatusCodes.BadConfigurationError, "The managed application target is missing.");
                    managedTarget = SnapshotManagedApplicationTarget(target);
                    certificateTypeId = ResolveManagedCertificateType(
                        managedTarget.CertificateTypeId, connection.CertificateTypeId, session.NamespaceUris);
                    record.DiscoveryUrls = managedTarget.DiscoveryUrls;
                    record.ServerCapabilities = managedTarget.ServerCapabilities;
                };
                beforeRegistration = async verificationToken =>
                {
                    ApplicationDescription description =
                        CreateManagedApplicationDescription(record, managedTarget);
                    managedDescription = description;
                    NodeId groups = await RequireChildAsync(
                        session, node, BrowseNames.CertificateGroups, verificationToken).ConfigureAwait(false);
                    managedGroup = await RequireChildAsync(
                        session, groups, Ua.BrowseNames.DefaultApplicationGroup, verificationToken)
                        .ConfigureAwait(false);
                    managedTrustListId = await RequireChildAsync(
                        session, managedGroup, Ua.BrowseNames.TrustList, verificationToken).ConfigureAwait(false);
                    await RequireSupportedCertificateTypeAsync(
                        session, managedGroup, certificateTypeId, verificationToken).ConfigureAwait(false);
                    managedConfiguration = new ServerConfigurationTypeClient(session, node, m_telemetry);
                    managedInstalledCertificate = await ReadInstalledCertificateAsync(
                        managedConfiguration, managedGroup, certificateTypeId, verificationToken)
                        .ConfigureAwait(false);
                    ByteString expectedCertificate = await ResolveManagedApplicationProofCertificateAsync(
                        identity,
                        record,
                        managedTarget,
                        managedInstalledCertificate,
                        certificateTypeId,
                        verificationToken).ConfigureAwait(false);
                    await managedApplicationChannels.VerifyAsync(
                        description,
                        managedTarget,
                        expectedCertificate,
                        verificationToken).ConfigureAwait(false);
                    RequirePeer(dispatch, peer);
                };
            }
            else if (record.ApplicationType != ApplicationType.Client)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            else
            {
                if (m_managedApplications != null)
                {
                    prepareAuthorizationRecord = async metadataToken =>
                    {
                        DeviceRegistrarManagedApplicationTarget target =
                            await m_managedApplications.GetTargetAsync(record.ApplicationUri!, metadataToken)
                                .ConfigureAwait(false) ?? throw new ServiceResultException(
                                    StatusCodes.BadConfigurationError, "The managed application target is missing.");
                        certificateTypeId = ResolveManagedCertificateType(
                            target.CertificateTypeId, connection.CertificateTypeId, session.NamespaceUris);
                    };
                }
                beforeRegistration = async verificationToken =>
                {
                    NodeId groups = await RequireChildAsync(
                        session, node, BrowseNames.CertificateGroups, verificationToken).ConfigureAwait(false);
                    managedGroup = await RequireChildAsync(
                        session, groups, Ua.BrowseNames.DefaultApplicationGroup, verificationToken)
                        .ConfigureAwait(false);
                    managedTrustListId = await RequireChildAsync(
                        session, managedGroup, Ua.BrowseNames.TrustList, verificationToken).ConfigureAwait(false);
                    await RequireSupportedCertificateTypeAsync(
                        session, managedGroup, certificateTypeId, verificationToken).ConfigureAwait(false);
                };
            }
            RequirePeer(dispatch, peer);
            NodeId applicationId = await DeviceRegistrarRuntime.RegisterDeviceApplicationAsync(
                m_applications,
                m_lifecycle,
                m_authorization,
                identity.ProductInstanceUri,
                record,
                string.Empty,
                ct,
                prepareAuthorizationRecord: prepareAuthorizationRecord,
                beforeRegistration: beforeRegistration).ConfigureAwait(false);
            await RequireManagedApprovalAsync(identity, dcaApplicationId, applicationId, ct).ConfigureAwait(false);
            RequirePeer(dispatch, peer);

            NodeId group;
            NodeId trustListId;
            ServerConfigurationTypeClient configuration;
            if (isManagedServer)
            {
                group = managedGroup;
                trustListId = managedTrustListId;
                configuration = managedConfiguration!;
            }
            else
            {
                group = managedGroup;
                trustListId = managedTrustListId;
                configuration = new ServerConfigurationTypeClient(session, node, m_telemetry);
            }
            if (isManagedServer &&
                !await HoldsCertificateAsync(
                    configuration, group, certificateTypeId, managedInstalledCertificate, ct)
                    .ConfigureAwait(false))
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateInvalid, "The managed application's certificate changed after proof.");
            }
            ByteString createdSigningRequest = ByteString.Empty;
            int signingRequestCreated = 0;
            async ValueTask<ByteString> CreateSigningRequestAsync(CancellationToken factoryCt)
            {
                if (Interlocked.Exchange(ref signingRequestCreated, 1) != 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "CSR creation was already attempted.");
                }
                RequirePeer(dispatch, peer);
                await RequireManagedApprovalAsync(identity, dcaApplicationId, applicationId, factoryCt)
                    .ConfigureAwait(false);
                ByteString csr = await configuration.CreateSigningRequestAsync(
                    NodeId.Null, certificateTypeId, string.Empty,
                    true, Nonce.CreateRandomNonceData(32).ToByteString(), factoryCt).ConfigureAwait(false);
                if (csr.IsEmpty)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                createdSigningRequest = ByteString.From(csr.Span);
                return ByteString.From(createdSigningRequest.Span);
            }
            DeviceRegistrarPushEnrollment retained = m_enrollments is
                IDeviceRegistrarPushCertificateEnrollmentProvider routedEnrollments
                ? await routedEnrollments.GetOrCreateAsync(
                    identity, applicationId, certificateTypeId, CreateSigningRequestAsync, ct).ConfigureAwait(false)
                : await m_enrollments.GetOrCreateAsync(
                    identity, applicationId, CreateSigningRequestAsync, ct).ConfigureAwait(false);
            ValidateEnrollment(retained, identity, applicationId);
            if (!createdSigningRequest.IsEmpty && retained.CertificateRequest != createdSigningRequest)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The retained CSR differs from the application's response.");
            }
            CertificateRequestIssuance issuance = await ReadIssuanceAsync(retained, ct).ConfigureAwait(false);
            RequireSigningResult(retained, issuance, record.ApplicationUri!, certificateTypeId);
            // Application setup is the certificate and the TrustList; each is replaced only when it differs.
            bool holdsCertificate = await HoldsCertificateAsync(
                configuration, group, certificateTypeId, issuance.Certificate, ct).ConfigureAwait(false);
            bool holdsTrustList = await HoldsTrustListAsync(session, trustListId, retained.TrustList, ct)
                .ConfigureAwait(false);
            if (holdsCertificate && holdsTrustList)
            {
                return;
            }

            if (!holdsTrustList)
            {
                await RequireManagedApprovalAsync(identity, dcaApplicationId, applicationId, ct)
                    .ConfigureAwait(false);
                RequirePeer(dispatch, peer);
                if (await StageChangesAsync(configuration,
                    () => WriteTrustListAsync(session, trustListId, retained.TrustList, ct),
                    retireSession).ConfigureAwait(false))
                {
                    await CommitStagedChangesAsync(configuration, async () =>
                    {
                        await RequireManagedApprovalAsync(identity, dcaApplicationId, applicationId, ct)
                            .ConfigureAwait(false);
                        RequirePeer(dispatch, peer);
                    }, retireSession, ct).ConfigureAwait(false);
                }
            }
            if (!holdsCertificate)
            {
                ArrayOf<ByteString> issuers = issuance.IssuerCertificates.ToList()
                    .Select(static issuer => ByteString.From(issuer.Span)).ToArrayOf();
                await RequireManagedApprovalAsync(identity, dcaApplicationId, applicationId, ct)
                    .ConfigureAwait(false);
                RequirePeer(dispatch, peer);
                bool applyCertificate = await StageChangesAsync(configuration,
                    () => configuration.UpdateCertificateAsync(
                        NodeId.Null, certificateTypeId, issuance.Certificate, issuers, string.Empty,
                        default, ct),
                    retireSession).ConfigureAwait(false);
                if (applyCertificate)
                {
                    // Approval or association can be revoked while the device stages the credential.
                    await CommitStagedChangesAsync(configuration, async () =>
                    {
                        await RequireManagedApprovalAsync(identity, dcaApplicationId, applicationId, ct)
                            .ConfigureAwait(false);
                        RequirePeer(dispatch, peer);
                    }, retireSession, ct).ConfigureAwait(false);
                }
                if (!await HoldsCertificateAsync(
                    configuration, group, certificateTypeId, issuance.Certificate, ct).ConfigureAwait(false))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "The managed application did not report the pushed certificate.");
                }
            }
            if (!holdsTrustList &&
                !await HoldsTrustListAsync(session, trustListId, retained.TrustList, ct).ConfigureAwait(false))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The managed application did not report the pushed TrustList.");
            }
            if (isManagedServer)
            {
                ApplicationDescription application = managedDescription ??
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "The managed application was not resolved before certificate issuance.");
                await m_managedApplicationChannels!.VerifyAsync(
                    application, managedTarget, issuance.Certificate, ct).ConfigureAwait(false);
                RequirePeer(dispatch, peer);
            }
        }

        /// <summary>
        /// Uses the explicit application profile when present, otherwise the configured registrar default.
        /// </summary>
        private NodeId ResolveManagedCertificateType(
            ExpandedNodeId selected,
            NodeId defaultType,
            NamespaceTable namespaces)
        {
            if (selected.IsNull)
            {
                return defaultType;
            }
            NodeId type = ExpandedNodeId.ToNodeId(selected, namespaces);
            if (selected.ServerIndex != 0 || type.IsNull ||
                (selected.NamespaceIndex != 0 && string.IsNullOrEmpty(selected.NamespaceUri)))
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "The managed application's certificate type is not a local node.");
            }
            if (type != defaultType && m_enrollments is not IDeviceRegistrarPushCertificateEnrollmentProvider)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The enrollment provider cannot route the managed application's certificate type.");
            }
            return type;
        }

        /// <summary>
        /// Combines the application's standard metadata with host-authoritative endpoint URLs and validates the
        /// selected endpoint before it can be registered or used for certificate proof.
        /// </summary>
        private static ApplicationDescription CreateManagedApplicationDescription(
            ApplicationRecordDataType record,
            DeviceRegistrarManagedApplicationTarget target)
        {
            if (!Uri.TryCreate(record.ApplicationUri, UriKind.Absolute, out _) ||
                target.InstalledCertificate.IsEmpty ||
                target.ServerCapabilities.IsEmpty ||
                target.DiscoveryUrls.IsEmpty ||
                target.Endpoint == null ||
                target.UserIdentity == null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            for (int index = 0; index < target.ServerCapabilities.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(target.ServerCapabilities[index]))
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError);
                }
            }
            if (!Uri.TryCreate(target.Endpoint.Description.EndpointUrl, UriKind.Absolute, out _))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            for (int index = 0; index < target.DiscoveryUrls.Count; index++)
            {
                string url = target.DiscoveryUrls[index];
                if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
            }
            return new ApplicationDescription
            {
                ApplicationUri = record.ApplicationUri,
                ApplicationName = record.ApplicationNames.Count > 0
                    ? record.ApplicationNames[0]
                    : new LocalizedText(record.ApplicationUri!),
                ApplicationType = record.ApplicationType,
                ProductUri = record.ProductUri,
                DiscoveryUrls = target.DiscoveryUrls
            };
        }

        /// <summary>
        /// Copies provider-owned endpoint metadata before it crosses into the shared application database or
        /// asynchronous channel verification.
        /// </summary>
        private static DeviceRegistrarManagedApplicationTarget SnapshotManagedApplicationTarget(
            DeviceRegistrarManagedApplicationTarget target)
        {
            if (target.DiscoveryUrls.IsNull || target.DiscoveryUrls.IsEmpty ||
                target.InstalledCertificate.IsEmpty ||
                target.Endpoint == null ||
                target.UserIdentity == null ||
                (!target.CertificateTypeId.IsNull && target.CertificateTypeId.ServerIndex != 0))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            return target with
            {
                DiscoveryUrls = target.DiscoveryUrls.ToArray().ToArrayOf(),
                ServerCapabilities = target.ServerCapabilities.Span.ToArray().ToArrayOf(),
                InstalledCertificate = ByteString.From(target.InstalledCertificate.Span),
                HttpsServerCertificate = ByteString.From(target.HttpsServerCertificate.Span),
                Endpoint = SessionDeviceRegistrarManagedApplicationChannelVerifier.SnapshotEndpoint(target.Endpoint),
                UserIdentity = target.UserIdentity
            };
        }

        /// <summary>
        /// Reads the application's complete TrustList and checks that every list selected by the retained
        /// TrustList holds exactly the retained entries, independent of order.
        /// </summary>
        private async ValueTask<bool> HoldsTrustListAsync(
            ISession session,
            NodeId trustListId,
            TrustListDataType intended,
            CancellationToken ct)
        {
            var file = new FileTypeClient(session, trustListId, m_telemetry);
            using var stream = new MemoryStream();
            await file.DownloadToStreamAsync(stream, MaxTrustListBytes, 4096, ct).ConfigureAwait(false);
            stream.Position = 0;
            TrustListDataType actual;
            using (var decoder = new BinaryDecoder(stream, session.MessageContext, true))
            {
                actual = decoder.ReadEncodeable<TrustListDataType>(null);
            }
            return HoldsList(actual, intended, TrustListMasks.TrustedCertificates,
                    actual.TrustedCertificates, intended.TrustedCertificates) &&
                HoldsList(actual, intended, TrustListMasks.TrustedCrls, actual.TrustedCrls, intended.TrustedCrls) &&
                HoldsList(actual, intended, TrustListMasks.IssuerCertificates,
                    actual.IssuerCertificates, intended.IssuerCertificates) &&
                HoldsList(actual, intended, TrustListMasks.IssuerCrls, actual.IssuerCrls, intended.IssuerCrls);
        }

        /// <summary>
        /// Compares one TrustList member as a multiset when the retained TrustList specifies it.
        /// </summary>
        private static bool HoldsList(
            TrustListDataType actual,
            TrustListDataType intended,
            TrustListMasks mask,
            ArrayOf<ByteString> actualEntries,
            ArrayOf<ByteString> intendedEntries)
        {
            if ((intended.SpecifiedLists & (uint)mask) == 0)
            {
                return true;
            }
            if ((actual.SpecifiedLists & (uint)mask) == 0 || actualEntries.Count != intendedEntries.Count)
            {
                return false;
            }
            var remaining = new List<ByteString>(actualEntries.Count);
            for (int index = 0; index < actualEntries.Count; index++)
            {
                remaining.Add(actualEntries[index]);
            }
            for (int index = 0; index < intendedEntries.Count; index++)
            {
                ByteString wanted = intendedEntries[index];
                int match = -1;
                for (int candidate = 0; candidate < remaining.Count; candidate++)
                {
                    if (remaining[candidate] == wanted)
                    {
                        match = candidate;
                        break;
                    }
                }
                if (match < 0)
                {
                    return false;
                }
                remaining.RemoveAt(match);
            }
            return true;
        }

        /// <summary>
        /// Requires the confirmed DCA's software approval and the shared database's managed-application approval.
        /// </summary>
        private async ValueTask RequireManagedApprovalAsync(
            OnboardingIdentitySelection identity,
            NodeId dcaApplicationId,
            NodeId applicationId,
            CancellationToken ct)
        {
            await RequireApprovalAsync(identity, dcaApplicationId, false, ct).ConfigureAwait(false);
            await ApplicationsNodeManager.RequirePushApprovalAsync(
                identity, applicationId, m_candidates, m_certificates, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the standard identity properties of a managed ApplicationConfiguration instance.
        /// ApplicationNames is an Optional property inherited from ServerConfigurationType; when the
        /// application omits it or reports no name, the ApplicationUri becomes the registered name.
        /// </summary>
        private static async ValueTask<ApplicationRecordDataType> ReadManagedApplicationAsync(
            ISession session,
            NodeId node,
            CancellationToken ct)
        {
            Variant uri = await ReadChildAsync(session, node, Ua.BrowseNames.ApplicationUri, ct)
                .ConfigureAwait(false);
            Variant product = await ReadChildAsync(session, node, Ua.BrowseNames.ProductUri, ct)
                .ConfigureAwait(false);
            Variant type = await ReadChildAsync(session, node, Ua.BrowseNames.ApplicationType, ct)
                .ConfigureAwait(false);
            if (!uri.TryGetValue(out string applicationUri) || string.IsNullOrEmpty(applicationUri) ||
                !product.TryGetValue(out string productUri) || !type.TryGetValue(out int applicationType))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch);
            }
            ArrayOf<LocalizedText> applicationNames = default;
            NodeId namesId = await ClientModelAccess.ResolveAsync(
                session, node, Namespaces.OpcUa, Ua.BrowseNames.ApplicationNames, ct).ConfigureAwait(false);
            if (!namesId.IsNull)
            {
                DataValue names = await session.ReadValueAsync(namesId, ct).ConfigureAwait(false);
                if (StatusCode.IsBad(names.StatusCode))
                {
                    throw new ServiceResultException(names.StatusCode);
                }
                if (!names.WrappedValue.IsNull && !names.WrappedValue.TryGetValue(out applicationNames))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch);
                }
            }
            if (applicationNames.Count == 0 || applicationNames[0].IsNullOrEmpty)
            {
                applicationNames = [new LocalizedText(applicationUri)];
            }
            return new ApplicationRecordDataType
            {
                ApplicationUri = applicationUri,
                ProductUri = productUri,
                ApplicationType = (ApplicationType)applicationType,
                ApplicationNames = applicationNames
            };
        }

        /// <summary>
        /// Reads one standard child value without converting a bad status to a default value.
        /// </summary>
        private static async ValueTask<Variant> ReadChildAsync(
            ISession session,
            NodeId parent,
            string browseName,
            CancellationToken ct)
        {
            NodeId child = await RequireChildAsync(session, parent, browseName, ct).ConfigureAwait(false);
            DataValue value = await session.ReadValueAsync(child, ct).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                throw new ServiceResultException(value.StatusCode);
            }
            return value.WrappedValue;
        }

        /// <summary>
        /// Resolves a mandatory standard child by its OPC UA browse name.
        /// </summary>
        private static async ValueTask<NodeId> RequireChildAsync(
            ISession session,
            NodeId parent,
            string browseName,
            CancellationToken ct)
        {
            NodeId child = await ClientModelAccess.ResolveAsync(session, parent, Namespaces.OpcUa, browseName, ct)
                .ConfigureAwait(false);
            if (child.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
            return child;
        }

        /// <summary>
        /// Reads the current managed certificate and permits a rotated certificate only when the exact
        /// certificate is backed by this device's retained GDS issuance.
        /// </summary>
        private async ValueTask<ByteString> ResolveManagedApplicationProofCertificateAsync(
            OnboardingIdentitySelection identity,
            ApplicationRecordDataType record,
            DeviceRegistrarManagedApplicationTarget target,
            ByteString currentCertificate,
            NodeId certificateType,
            CancellationToken ct)
        {
            if (currentCertificate.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            if (currentCertificate == target.InstalledCertificate)
            {
                return currentCertificate;
            }

            ApplicationRecordDataType[]? registered = m_applications.FindApplications(record.ApplicationUri!);
            if (registered is not { Length: 1 } ||
                !string.Equals(
                    registered[0].ApplicationUri, record.ApplicationUri, StringComparison.Ordinal) ||
                (!record.ApplicationId.IsNull && record.ApplicationId != registered[0].ApplicationId))
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            NodeId applicationId = registered[0].ApplicationId;
            DeviceRegistrarApplicationBinding? binding =
                await m_certificates.FindApplicationAsync(applicationId, ct).ConfigureAwait(false);
            if (binding == null || binding.IsDca ||
                !string.Equals(binding.ProductInstanceUri, identity.ProductInstanceUri, StringComparison.Ordinal))
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }

            DeviceRegistrarPushEnrollment? retained = await m_enrollments.FindAsync(
                identity.ProductInstanceUri, applicationId, ct).ConfigureAwait(false);
            if (retained == null)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            ValidateEnrollment(retained, identity, applicationId);
            CertificateRequestIssuance issuance = await ReadIssuanceAsync(retained, ct).ConfigureAwait(false);
            RequireSigningResult(retained, issuance, record.ApplicationUri!, certificateType);
            if (currentCertificate != issuance.Certificate)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }

            return issuance.Certificate;
        }

        /// <summary>
        /// Reads the single configured leaf certificate for a certificate type from the managed application.
        /// </summary>
        private static async ValueTask<ByteString> ReadInstalledCertificateAsync(
            ServerConfigurationTypeClient configuration,
            NodeId group,
            NodeId certificateType,
            CancellationToken ct)
        {
            var (types, certificates) = await configuration.GetCertificatesAsync(group, ct)
                .ConfigureAwait(false);
            if (types.Count != certificates.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnknownResponse,
                    "GetCertificates returned CertificateTypeIds and Certificates of different lengths.");
            }
            ByteString certificate = ByteString.Empty;
            for (int index = 0; index < certificates.Count; index++)
            {
                if (types[index] != certificateType)
                {
                    continue;
                }
                if (!certificate.IsEmpty && certificate != certificates[index])
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUnknownResponse,
                        "GetCertificates returned multiple certificates for the selected type.");
                }
                certificate = ByteString.From(certificates[index].Span);
            }
            if (certificate.IsEmpty)
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateInvalid,
                    "The managed application did not report its installed certificate.");
            }
            return certificate;
        }

        /// <summary>
        /// Checks whether the resolved certificate group reports the exact certificate for the pushed type.
        /// GetCertificates is optional in Part 12; a device without it fails the push with the returned
        /// status, because the registrar cannot otherwise prove which credential the application holds.
        /// The CertificateTypeIds and Certificates outputs are parallel arrays (OPC 10000-12, 7.10.8), so a
        /// response with different lengths is rejected rather than read as an untyped certificate.
        /// </summary>
        private static async ValueTask<bool> HoldsCertificateAsync(
            ServerConfigurationTypeClient configuration,
            NodeId group,
            NodeId certificateType,
            ByteString certificate,
            CancellationToken ct)
        {
            var (types, certificates) = await configuration.GetCertificatesAsync(group, ct)
                .ConfigureAwait(false);
            if (types.Count != certificates.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnknownResponse,
                    "GetCertificates returned CertificateTypeIds and Certificates of different lengths.");
            }
            for (int index = 0; index < certificates.Count; index++)
            {
                if (certificates[index] == certificate && types[index] == certificateType)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Runs one call that can stage changes in the session transaction: Open(write) through CloseAndUpdate,
        /// or UpdateCertificate. If the call fails or is cancelled, the server may already have staged the
        /// change before the reply was lost, or may still stage it after cancellation completes.
        /// Cancellation is attempted, but the session must be retired even if it succeeds.
        /// A failed cancellation is reported together with the original error.
        /// </summary>
        private async ValueTask<bool> StageChangesAsync(
            ServerConfigurationTypeClient configuration,
            Func<ValueTask<bool>> stage,
            StrongBox<bool>? retireSession)
        {
            try
            {
                return await stage().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                if (retireSession != null)
                {
                    retireSession.Value = true;
                }
                await CancelStagedChangesAsync(configuration, error, retireSession).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Rechecks authorization before ApplyChanges commits the changes staged in the session.
        /// A rejected check cancels the staged changes and rethrows the original error. When
        /// <paramref name="retireSession"/> is supplied, because later applications share the session, a failed
        /// ApplyChanges is cancelled the same way and the session is retired because the request may still run.
        /// If CancelChanges also fails, both errors are thrown together and the session is retired.
        /// </summary>
        private async ValueTask CommitStagedChangesAsync(
            ServerConfigurationTypeClient configuration,
            Func<ValueTask> authorize,
            StrongBox<bool>? retireSession,
            CancellationToken ct)
        {
            try
            {
                await authorize().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                await CancelStagedChangesAsync(configuration, error, retireSession).ConfigureAwait(false);
                throw;
            }
            try
            {
                await configuration.ApplyChangesAsync(ct).ConfigureAwait(false);
            }
            catch (Exception error) when (retireSession != null)
            {
                retireSession.Value = true;
                await CancelStagedChangesAsync(configuration, error, retireSession).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Discards the changes staged in the session after a failure, preserving the failure and any
        /// cleanup error together. Bad_NothingToDo means no transaction is active when cancellation executes
        /// (OPC 10000-12, 7.10.11), not that an earlier timed-out request cannot stage changes later.
        /// </summary>
        private async ValueTask CancelStagedChangesAsync(
            ServerConfigurationTypeClient configuration,
            Exception error,
            StrongBox<bool>? retireSession)
        {
            try
            {
                // Bound cleanup independently; the transport must honor cancellation to release this await.
                using CancellationTokenSource cleanup =
                    m_timeProvider.CreateCancellationTokenSource(TimeSpan.FromSeconds(5));
                await configuration.CancelChangesAsync(cleanup.Token).ConfigureAwait(false);
            }
            catch (ServiceResultException cleanup) when (cleanup.StatusCode == StatusCodes.BadNothingToDo)
            {
                // This response does not establish completion of an earlier timed-out request.
            }
            catch (Exception cleanup)
            {
                if (retireSession != null)
                {
                    retireSession.Value = true;
                }
                throw new AggregateException(
                    "Configuration processing failed and transaction cancellation also failed.",
                    error, cleanup);
            }
        }

        /// <summary>
        /// Requires the application's certificate group to list the registrar's configured certificate type,
        /// because CreateSigningRequest and UpdateCertificate reject a type outside the group's CertificateTypes.
        /// </summary>
        private static async ValueTask RequireSupportedCertificateTypeAsync(
            ISession session,
            NodeId group,
            NodeId certificateType,
            CancellationToken ct)
        {
            Variant value = await ReadChildAsync(session, group, Ua.BrowseNames.CertificateTypes, ct)
                .ConfigureAwait(false);
            if (value.TryGetValue(out ArrayOf<NodeId> types))
            {
                for (int index = 0; index < types.Count; index++)
                {
                    if (types[index] == certificateType)
                    {
                        return;
                    }
                }
            }
            throw new ServiceResultException(
                StatusCodes.BadNotSupported,
                "The managed application's certificate group does not support the registrar's certificate type.");
        }

        /// <summary>
        /// Resolves prior request evidence only after the actual peer proves a retained DCA credential.
        /// </summary>
        private async ValueTask<DeviceRegistrarPushEnrollment?> FindRetainedAsync(
            string applicationUri,
            ByteString peer,
            CancellationToken ct)
        {
            ApplicationRecordDataType[]? applications = m_applications.FindApplications(applicationUri);
            if (applications == null || applications.Length == 0)
            {
                return null;
            }
            if (applications.Length != 1)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            DeviceRegistrarApplicationBinding? binding = await m_certificates.FindApplicationAsync(
                applications[0].ApplicationId, ct).ConfigureAwait(false);
            if (binding == null)
            {
                return null;
            }
            if (!binding.IsDca)
            {
                throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            }
            DeviceRegistrarDevice current = await RequireDeviceAsync(
                binding.ProductInstanceUri, binding.ApplicationId, ct).ConfigureAwait(false);
            if (current.PendingDcaCertificate != peer && current.DcaCertificate != peer)
            {
                return null;
            }
            DeviceRegistrarPushEnrollment retained = await m_enrollments.FindAsync(
                binding.ProductInstanceUri, binding.ApplicationId, ct).ConfigureAwait(false) ??
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "Installed DCA recovery requires retained push request evidence.");
            if (retained.Identity == null ||
                retained.Identity.ProductInstanceUri != binding.ProductInstanceUri ||
                retained.ApplicationId != binding.ApplicationId)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
            return retained;
        }

        /// <summary>
        /// Registers only the policy-selected DCA in the shared application database and preserves ownership.
        /// </summary>
        private async ValueTask<NodeId> RegisterAsync(
            OnboardingIdentitySelection identity,
            DeviceRegistrarOnboardingDecision decision,
            CancellationToken ct)
        {
            ApplicationRecordDataType record = CoreUtils.Clone(decision.Application)!;
            ApplicationsDatabaseBase.ValidateApplication(record);
            ApplicationRecordDataType[]? existing = m_applications.FindApplications(record.ApplicationUri!);
            NodeId applicationId;
            if (existing is { Length: > 0 })
            {
                if (existing.Length != 1)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                applicationId = existing[0].ApplicationId;
                DeviceRegistrarApplicationBinding? binding = await m_certificates.FindApplicationAsync(
                    applicationId, ct).ConfigureAwait(false);
                if (binding == null || !binding.IsDca ||
                    binding.ProductInstanceUri != identity.ProductInstanceUri ||
                    (!record.ApplicationId.IsNull && record.ApplicationId != applicationId))
                {
                    throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
                }
            }
            else
            {
                if (!record.ApplicationId.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
                applicationId = m_applications.RegisterApplication(record);
                if (applicationId.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
            }
            await m_candidates.RecordCandidateAsync(identity.ProductInstanceUri, applicationId,
                identity.SelectedIdentity, decision.RequiresSoftwareUpdate, ct).ConfigureAwait(false);
            return applicationId;
        }

        /// <summary>
        /// Rechecks persisted software approval and forbids managed relations on a singleton before mutations.
        /// </summary>
        private async ValueTask RequireApprovalAsync(
            OnboardingIdentitySelection identity,
            NodeId applicationId,
            bool singleton,
            CancellationToken ct)
        {
            if (identity.IsExternalAuthentication)
            {
                DeviceRegistrarExternalAuthenticationResult external =
                    await (m_externalAuthentication ??
                        throw new ServiceResultException(StatusCodes.BadConfigurationError))
                        .EvaluateAsync(identity.SelectedIdentity, ct).ConfigureAwait(false) ??
                    throw new ServiceResultException(StatusCodes.BadCertificateUntrusted);
                ApplicationRecordDataType[]? approved =
                    m_applications.FindApplications(external.Decision.Application.ApplicationUri!);
                if (external.ProductInstanceUri != identity.ProductInstanceUri ||
                    approved is not { Length: 1 } || approved[0].ApplicationId != applicationId)
                {
                    throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
                }
                DeviceRegistrarDevice current = await RequireDeviceAsync(
                    identity.ProductInstanceUri, applicationId, ct).ConfigureAwait(false);
                if (external.Decision.RequiresSoftwareUpdate && !current.SoftwareStatus)
                {
                    throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
                }
            }
            DeviceRegistrarIdentityCandidate? candidate = await m_candidates.FindCandidateByCertificateAsync(
                identity.SelectedIdentity, ct).ConfigureAwait(false);
            if (candidate == null || candidate.ProductInstanceUri != identity.ProductInstanceUri ||
                candidate.ApplicationId != applicationId || !candidate.CertificateIssuanceAllowed)
            {
                throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            }
            DeviceRegistrarDevice device = await RequireDeviceAsync(
                identity.ProductInstanceUri, applicationId, ct).ConfigureAwait(false);
            if (singleton && !device.Applications.IsEmpty)
            {
                throw new ServiceResultException(
                    StatusCodes.BadRequestNotAllowed, "A singleton DCA cannot manage other applications.");
            }
        }

        /// <summary>
        /// Reads an existing immutable GDS receipt; absent issuance is never synthesized by push orchestration.
        /// </summary>
        private async ValueTask<CertificateRequestIssuance> ReadIssuanceAsync(
            DeviceRegistrarPushEnrollment enrollment,
            CancellationToken ct)
        {
            CertificateRequestIssuance issuance = await m_issuances.GetOrCreateAsync(
                enrollment.ApplicationId, enrollment.RequestId,
                _ => throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "Complete the correlated GDS request before pushing credentials."),
                ct).ConfigureAwait(false);
            if (!issuance.PrivateKey.IsEmpty)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "Push enrollment requires a device-generated CSR.");
            }
            if (issuance.CertificateGroupId != enrollment.CertificateGroupId ||
                issuance.CertificateTypeId != enrollment.CertificateTypeId)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
            return issuance;
        }

        /// <summary>
        /// Proves the exact installed leaf on an encrypted generation before committing promotion.
        /// </summary>
        private async ValueTask ConfirmAsync(
            SessionRequestContext proof,
            OnboardingIdentitySelection identity,
            NodeId applicationId,
            bool singleton,
            ByteString expected,
            CancellationToken ct)
        {
            if (expected.IsEmpty || ReadPeer(proof.Session) != expected)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            await RetainScopeAsync(identity.ProductInstanceUri, singleton, ct).ConfigureAwait(false);
            await RequireApprovalAsync(identity, applicationId, singleton, ct).ConfigureAwait(false);
            DeviceRegistrarDevice current = await RequireDeviceAsync(
                identity.ProductInstanceUri, applicationId, ct).ConfigureAwait(false);
            if (current.PendingDcaCertificate != expected && current.DcaCertificate != expected)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            RequirePeer(proof, expected);
            await m_certificates.ConfirmDcaCertificateAsync(expected, ct).ConfigureAwait(false);
            RequirePeer(proof, expected);
            current = await RequireDeviceAsync(identity.ProductInstanceUri, applicationId, ct).ConfigureAwait(false);
            if (current.DcaCertificate != expected || !current.PendingDcaCertificate.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
        }

        /// <summary>
        /// Records singleton scope before issuance or promotion; unsupported lifecycle providers fail explicitly.
        /// </summary>
        private async ValueTask RetainScopeAsync(string productInstanceUri, bool singleton, CancellationToken ct)
        {
            if (singleton)
            {
                if (m_certificates is not IDeviceRegistrarApplicationScopeLifecycle scope)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported, "Singleton onboarding requires persisted application scope.");
                }
                await scope.RestrictToDcaApplicationAsync(productInstanceUri, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Requires the same persisted DCA owner for every credential transition.
        /// </summary>
        private async ValueTask<DeviceRegistrarDevice> RequireDeviceAsync(
            string productInstanceUri,
            NodeId applicationId,
            CancellationToken ct)
        {
            DeviceRegistrarDevice? device = await m_certificates.FindDeviceAsync(
                productInstanceUri, ct).ConfigureAwait(false);
            if (device == null || device.DcaApplicationId != applicationId)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            return device;
        }

        /// <summary>
        /// Uploads a bounded complete trust list through generated file proxies and preserves cleanup failures.
        /// </summary>
        private async ValueTask<bool> WriteTrustListAsync(
            ISession session,
            NodeId trustListId,
            TrustListDataType trustList,
            CancellationToken ct)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, session.MessageContext, true))
            {
                encoder.WriteEncodeable(null, trustList);
            }
            if (stream.Length > MaxTrustListBytes)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            var client = new TrustListTypeClient(session, trustListId, m_telemetry);
            uint handle = await client.OpenAsync(
                (byte)(OpenFileMode.Write | OpenFileMode.EraseExisting), ct).ConfigureAwait(false);
            try
            {
                byte[] bytes = stream.ToArray();
                for (int offset = 0; offset < bytes.Length; offset += 4096)
                {
                    await client.WriteAsync(handle,
                        ByteString.From(bytes.AsSpan(offset, Math.Min(4096, bytes.Length - offset))), ct)
                        .ConfigureAwait(false);
                }
                return await client.CloseAndUpdateAsync(handle, ct).ConfigureAwait(false);
            }
            catch (Exception transferError)
            {
                try
                {
                    await CloseTrustListAfterFailureAsync(client, handle).ConfigureAwait(false);
                }
                catch (Exception closeError)
                {
                    throw new AggregateException(transferError, closeError);
                }
                throw;
            }
        }

        /// <summary>
        /// Releases the remote writer with an independent timeout after transfer cancellation or failure.
        /// </summary>
        private static async ValueTask CloseTrustListAfterFailureAsync(TrustListTypeClient client, uint handle)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.CloseAsync(handle, cleanup.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Releases admission before the provider lease and preserves processing and all cleanup failures.
        /// </summary>
        private async ValueTask WithConnectionAsync(
            ApplicationDescription application,
            ByteString expected,
            Func<IDeviceRegistrarPushConnection, SessionRequestContext, ValueTask> process,
            CancellationToken ct)
        {
            IDeviceRegistrarPushConnection connection = await m_connections.ConnectAsync(
                application, expected, ct).ConfigureAwait(false) ??
                throw new ServiceResultException(StatusCodes.BadNotConnected);
            SessionRequestContext? dispatch = null;
            Exception? failure = null;
            try
            {
                dispatch = await SessionRequestContext.CaptureConfidentialAsync(connection.Session, ct)
                    .ConfigureAwait(false);
                await process(connection, dispatch).ConfigureAwait(false);
            }
            catch (Exception processingError)
            {
                failure = processingError;
            }
            if (dispatch != null)
            {
                try
                {
                    await dispatch.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupError)
                {
                    failure = failure == null ? cleanupError : new AggregateException(failure, cleanupError);
                }
            }
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupError)
            {
                failure = failure == null ? cleanupError : new AggregateException(failure, cleanupError);
            }
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        /// <summary>
        /// Reads topology through the admitted generation without converting service failures to false.
        /// </summary>
        private static async ValueTask<bool> ReadSingletonAsync(
            ISession session,
            NodeId propertyId,
            CancellationToken ct)
        {
            DataValue value = await session.ReadValueAsync(propertyId, ct).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                throw new ServiceResultException(value.StatusCode);
            }
            if (!value.WrappedValue.TryGetValue(out bool singleton))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch);
            }
            return singleton;
        }

        /// <summary>
        /// Checks metadata consistency without using either offered certificate as proof of device identity.
        /// </summary>
        private static void ValidateConnection(
            ApplicationDescription application,
            IDeviceRegistrarPushConnection connection,
            ISession session)
        {
            EndpointDescription offered = connection.OfferedEndpoint;
            EndpointDescription actual = session.TransportChannel.EndpointDescription;
            if (offered == null || offered.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                string.IsNullOrEmpty(offered.SecurityPolicyUri) || offered.SecurityPolicyUri == SecurityPolicies.None ||
                !Uri.TryCreate(offered.EndpointUrl, UriKind.Absolute, out _) ||
                offered.Server?.ApplicationUri != application.ApplicationUri ||
                actual.EndpointUrl != offered.EndpointUrl || actual.SecurityMode != offered.SecurityMode ||
                actual.SecurityPolicyUri != offered.SecurityPolicyUri ||
                actual.Server?.ApplicationUri != application.ApplicationUri ||
                connection.ProvisionableDeviceId.IsNull || connection.IsSingletonId.IsNull ||
                connection.ServerConfigurationId.IsNull || connection.CertificateGroupId.IsNull ||
                connection.CertificateTypeId.IsNull || connection.TrustListId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
        }

        /// <summary>
        /// Rejects invalid registration metadata before opening any connection.
        /// </summary>
        private static void ValidateApplication(ApplicationDescription application)
        {
            if (!Uri.TryCreate(application.ApplicationUri, UriKind.Absolute, out _) ||
                application.DiscoveryUrls.IsEmpty ||
                application.ApplicationType is not (ApplicationType.Server or ApplicationType.ClientAndServer))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            foreach (string url in application.DiscoveryUrls)
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
            }
        }

        /// <summary>
        /// Rejects provider substitution or incomplete recovery evidence.
        /// </summary>
        private static void ValidateEnrollment(
            DeviceRegistrarPushEnrollment enrollment,
            OnboardingIdentitySelection identity,
            NodeId applicationId)
        {
            if (enrollment == null || enrollment.Identity == null ||
                enrollment.Identity.ProductInstanceUri != identity.ProductInstanceUri ||
                enrollment.Identity.SelectedIdentity != identity.SelectedIdentity ||
                enrollment.Identity.IsExternalAuthentication != identity.IsExternalAuthentication ||
                enrollment.Identity.EncodedTicket != identity.EncodedTicket ||
                enrollment.Identity.EncodedComposite != identity.EncodedComposite ||
                enrollment.ApplicationId != applicationId || enrollment.RequestId.IsNull ||
                string.IsNullOrEmpty(enrollment.CertificateGroupId) ||
                string.IsNullOrEmpty(enrollment.CertificateTypeId) ||
                enrollment.TrustList == null || enrollment.TrustList.SpecifiedLists != (uint)TrustListMasks.All ||
                enrollment.CertificateRequest.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
        }

        /// <summary>
        /// Binds retained issuance to the original device-generated key, application and requested key profile.
        /// Matching group/type labels alone cannot distinguish a stale certificate from the requested issuance.
        /// </summary>
        private static void RequireSigningResult(
            DeviceRegistrarPushEnrollment enrollment,
            CertificateRequestIssuance issuance,
            string applicationUri,
            NodeId certificateType)
        {
            if (!ApplicationsNodeManager.TryGetCertificateTypeName(
                    certificateType, out string? expectedCertificateTypeId) ||
                !string.Equals(
                    enrollment.CertificateTypeId, expectedCertificateTypeId, StringComparison.Ordinal) ||
                !string.Equals(
                    issuance.CertificateTypeId, expectedCertificateTypeId, StringComparison.Ordinal))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The retained issuance does not match the currently selected certificate type.");
            }

            try
            {
                var framing = new AsnReader(enrollment.CertificateRequest.Memory, AsnEncodingRules.DER);
                framing.ReadSequence();
                framing.ThrowIfNotEmpty();
                var request = new Pkcs10CertificationRequest(enrollment.CertificateRequest.ToArray());
                if (!request.Verify() ||
                    Pkcs10Utils.GetSubjectAltNameExtension(request.Attributes)?.Uris.Contains(applicationUri) != true)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }
                CertificateGroup.VerifySigningRequestKey(certificateType, enrollment.CertificateRequest);
                using Certificate certificate = Certificate.FromRawData(issuance.Certificate);
                if (!certificate.RawData.AsSpan().SequenceEqual(issuance.Certificate.Span) ||
                    !CertificateIdentifier.ValidateCertificateType(certificate, certificateType) ||
                    !X509Utils.GetApplicationUrisFromCertificate(certificate).Contains(applicationUri))
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }
                var encoded = new AsnReader(request.SubjectPublicKeyInfo, AsnEncodingRules.DER);
                AsnReader publicKey = encoded.ReadSequence();
                AsnReader algorithm = publicKey.ReadSequence();
                string oid = algorithm.ReadObjectIdentifier();
                ReadOnlyMemory<byte> parameters = algorithm.HasData ? algorithm.ReadEncodedValue() : default;
                byte[] key = publicKey.ReadBitString(out int padding);
                algorithm.ThrowIfNotEmpty();
                publicKey.ThrowIfNotEmpty();
                encoded.ThrowIfNotEmpty();
                using System.Security.Cryptography.X509Certificates.X509Certificate2 leaf =
                    certificate.AsX509Certificate2();
                ReadOnlySpan<byte> certificateParameters = leaf.PublicKey.EncodedParameters?.RawData;
                if (padding != 0 || !string.Equals(oid, leaf.PublicKey.Oid?.Value, StringComparison.Ordinal) ||
                    !parameters.Span.SequenceEqual(certificateParameters) ||
                    !key.AsSpan().SequenceEqual(leaf.PublicKey.EncodedKeyValue.RawData))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed, "The retained certificate does not match the device CSR.");
                }
            }
            catch (Exception error) when (error is AsnContentException or
                System.Security.Cryptography.CryptographicException or ArgumentException or NotSupportedException)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The retained signing evidence is invalid.", error);
            }
        }

        /// <summary>
        /// Reads only the authenticated transport's leaf certificate, including when a chain blob is supplied.
        /// </summary>
        private static ByteString ReadPeer(ISession session)
        {
            byte[] peer = session.TransportChannel.ServerChannelCertificate;
            if (peer == null || peer.Length == 0)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            using CertificateCollection chain = DefaultCertificateFactory.Instance.ParseChainBlob(peer.AsMemory());
            if (chain.Count == 0)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            return ByteString.From(chain[0].RawData.AsSpan());
        }

        /// <summary>
        /// Never follows an invalidated admission into a replacement session or physical channel.
        /// </summary>
        private static void RequireGeneration(SessionRequestContext dispatch)
        {
            if (dispatch.Invalidated.IsCompleted)
            {
                throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
            }
        }

        /// <summary>
        /// Retains actual peer identity as well as dispatch generation across asynchronous host-provider calls.
        /// </summary>
        private static void RequirePeer(SessionRequestContext dispatch, ByteString expected)
        {
            RequireGeneration(dispatch);
            if (ReadPeer(dispatch.Session) != expected)
            {
                throw new ServiceResultException(StatusCodes.BadSecureChannelIdInvalid);
            }
        }

        /// <summary>The host's connection and bootstrap trust boundary.</summary>
        private readonly IDeviceRegistrarPushConnectionProvider m_connections;

        /// <summary>The existing certificate service's durable request and approval adapter.</summary>
        private readonly IDeviceRegistrarPushEnrollmentProvider m_enrollments;

        /// <summary>The ticket-scoped peer identity policy shared with pull admission.</summary>
        private readonly OnboardingPushIdentityValidator m_identities;

        /// <summary>The host's device and software onboarding policy.</summary>
        private readonly IDeviceRegistrarOnboardingPolicy m_policy;

        /// <summary>The shared certificate-manager application registry.</summary>
        private readonly IApplicationsDatabase m_applications;

        /// <summary>The persisted selected identity and current software issuance gate.</summary>
        private readonly IDeviceRegistrarIdentityLifecycle m_candidates;

        /// <summary>The persisted application ownership and pending/confirmed credentials.</summary>
        private readonly IDeviceRegistrarCertificateLifecycle m_certificates;

        /// <summary>The existing GDS request-correlated issuance receipts.</summary>
        private readonly ICertificateRequestIssuanceStore m_issuances;

        /// <summary>The persisted device and managed-application relations.</summary>
        private readonly IDeviceRegistrarLifecycle m_lifecycle;

        /// <summary>The host's independent per-application authorization policy.</summary>
        private readonly IDeviceRegistrarAuthorization m_authorization;

        /// <summary>The host's authoritative managed-server endpoint metadata and connection identity.</summary>
        private readonly IDeviceRegistrarManagedApplicationProvider? m_managedApplications;

        /// <summary>The fresh-session verifier for an application's installed server certificate.</summary>
        private readonly IDeviceRegistrarManagedApplicationChannelVerifier? m_managedApplicationChannels;

        /// <summary>The optional external certificate trust and explicit device approval boundary.</summary>
        private readonly IDeviceRegistrarExternalAuthenticationProvider? m_externalAuthentication;

        /// <summary>The host's generated-client telemetry.</summary>
        private readonly ITelemetryContext m_telemetry;

        /// <summary>Clock used for transaction cleanup deadlines independent of caller cancellation.</summary>
        private readonly TimeProvider m_timeProvider;

        /// <summary>The largest encoded TrustList the registrar uploads to or downloads from a device.</summary>
        private const int MaxTrustListBytes = 4 * 1024 * 1024;

        /// <summary>The standard ManagedApplications folder (OPC 10000-21, i=16706).</summary>
        private static readonly NodeId s_managedApplicationsFolderId = new(16706u);

        /// <summary>Serializes direct invocations as well as work admitted by the default push scheduler.</summary>
        private readonly SemaphoreSlim m_attemptGate = new(1, 1);

        /// <summary>Whether the owning host has released this processor.</summary>
        private int m_disposed;
    }
}
