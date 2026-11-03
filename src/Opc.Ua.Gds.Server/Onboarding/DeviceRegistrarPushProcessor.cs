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
using System.Formats.Asn1;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Pushes an approved DCA credential using generated clients and retained, request-correlated GDS issuance.
    /// This processor never installs software or grants a singleton authority over managed applications.
    /// </summary>
    public sealed class DeviceRegistrarPushProcessor : IDeviceRegistrarPushProcessor, IDisposable
    {
        /// <summary>
        /// Borrows host-selected connection, enrollment, policy, and shared registrar persistence providers.
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
            ITelemetryContext telemetry)
        {
            m_connections = connections ?? throw new ArgumentNullException(nameof(connections));
            m_enrollments = enrollments ?? throw new ArgumentNullException(nameof(enrollments));
            m_identities = identities ?? throw new ArgumentNullException(nameof(identities));
            m_policy = policy ?? throw new ArgumentNullException(nameof(policy));
            m_applications = applications ?? throw new ArgumentNullException(nameof(applications));
            m_candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
            m_certificates = certificates ?? throw new ArgumentNullException(nameof(certificates));
            m_issuances = issuances ?? throw new ArgumentNullException(nameof(issuances));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
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
            await WithConnectionAsync(snapshot, default, async (connection, dispatch) =>
            {
                ValidateConnection(snapshot, connection, dispatch.Session);
                var device = new ProvisionableDeviceTypeClient(
                    dispatch.Session, connection.ProvisionableDeviceId, m_telemetry);
                ArrayOf<string> tickets = await device.RequestTicketsAsync(cancellationToken).ConfigureAwait(false);
                bool singleton = await ReadSingletonAsync(
                    dispatch.Session, connection.IsSingletonId, cancellationToken).ConfigureAwait(false);

                ByteString peer = ReadPeer(dispatch.Session);
                DeviceRegistrarPushEnrollment? retained = await FindRetainedAsync(snapshot.ApplicationUri!, peer,
                    cancellationToken).ConfigureAwait(false);
                OnboardingIdentitySelection identity = retained?.Identity ??
                    await m_identities.ValidatePeerAsync(
                        dispatch.Session.TransportChannel, tickets, cancellationToken).ConfigureAwait(false);
                if (retained == null && identity.SelectedIdentity != peer)
                {
                    throw new ServiceResultException(StatusCodes.BadSecureChannelIdInvalid);
                }
                acceptedIdentity = identity;
                DeviceRegistrarOnboardingDecision decision = await m_policy.EvaluateAsync(
                    identity, cancellationToken).ConfigureAwait(false);
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
                if (peer == installed &&
                    (current.PendingDcaCertificate == peer || current.DcaCertificate == peer))
                {
                    await ConfirmAsync(dispatch, identity, applicationId, singleton, installed, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }
                if (!current.PendingDcaCertificate.IsEmpty && current.PendingDcaCertificate != installed)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                await m_certificates.RecordIssuedDcaCertificateAsync(
                    productInstanceUri, applicationId, installed, cancellationToken).ConfigureAwait(false);
                RequirePeer(dispatch, peer);

                var server = new ServerConfigurationTypeClient(
                    dispatch.Session, connection.ServerConfigurationId, m_telemetry);
                bool applyTrust = await WriteTrustListAsync(
                    dispatch.Session, connection.TrustListId, retained.TrustList, cancellationToken)
                    .ConfigureAwait(false);
                await RequireApprovalAsync(identity, applicationId, singleton, cancellationToken).ConfigureAwait(false);
                RequirePeer(dispatch, peer);
                if (applyTrust)
                {
                    await server.ApplyChangesAsync(cancellationToken).ConfigureAwait(false);
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
                    bool applyCertificate = await server.UpdateCertificateAsync(
                        connection.CertificateGroupId, connection.CertificateTypeId, installed,
                        issuerCertificates, string.Empty, default, cancellationToken).ConfigureAwait(false);
                    if (applyCertificate)
                    {
                        await RequireApprovalAsync(
                            identity, applicationId, singleton, cancellationToken).ConfigureAwait(false);
                        RequirePeer(dispatch, configuringPeer);
                        await server.ApplyChangesAsync(cancellationToken).ConfigureAwait(false);
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
                await device.RequestTicketsAsync(cancellationToken).ConfigureAwait(false);
                bool singleton = await ReadSingletonAsync(
                    proof.Session, connection.IsSingletonId, cancellationToken).ConfigureAwait(false);
                await ConfirmAsync(proof,
                    acceptedIdentity ?? throw new ServiceResultException(StatusCodes.BadInvalidState),
                    applicationId, singleton, installed, cancellationToken)
                    .ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
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
            if (stream.Length > 4 * 1024 * 1024)
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

        /// <summary>The host's generated-client telemetry.</summary>
        private readonly ITelemetryContext m_telemetry;

        /// <summary>Serializes direct invocations as well as work admitted by the default push scheduler.</summary>
        private readonly SemaphoreSlim m_attemptGate = new(1, 1);

        /// <summary>Whether the owning host has released this processor.</summary>
        private int m_disposed;
    }
}
