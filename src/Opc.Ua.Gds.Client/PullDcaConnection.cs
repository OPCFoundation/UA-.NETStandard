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
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Typed transport boundary for the pull workflow; never owns its underlying session.
    /// </summary>
    internal interface IPullDcaConnection
    {
        /// <summary>
        /// Calls the registrar's generated identity proxy.
        /// </summary>
        ValueTask<PullDcaIdentitySelection> ProvideIdentitiesAsync(PullDcaIdentityOffer offer, CancellationToken ct);

        /// <summary>
        /// Reloads the host-installed certificate and reconnects the managed session.
        /// </summary>
        ValueTask ReloadAndReconnectAsync(CancellationToken ct);

        /// <summary>
        /// Calls the generated manager proxy.
        /// </summary>
        ValueTask<ArrayOf<ManagerDescription>> GetManagersAsync(CancellationToken ct);

        /// <summary>
        /// Calls the manager proxy on the channel verified against an installed pull credential.
        /// </summary>
        ValueTask<ArrayOf<ManagerDescription>> GetManagersOnInstalledChannelAsync(
            PullDcaSigningBinding? binding,
            ByteString installedCertificate,
            CancellationToken ct);

        /// <summary>
        /// Calls the generated application registration proxy.
        /// </summary>
        ValueTask<NodeId> RegisterAsync(PullDcaManagedApplication application, CancellationToken ct);

        /// <summary>
        /// Calls the certificate directory's generated signing proxy.
        /// </summary>
        ValueTask<NodeId> StartAsync(
            NodeId applicationId, PullDcaCertificateProfile profile, ByteString csr, CancellationToken ct);

        /// <summary>
        /// Starts a certificate-manager-generated key-pair request using the required PFX format.
        /// </summary>
        ValueTask<NodeId> StartNewKeyPairAsync(
            NodeId applicationId, PullDcaCertificateProfile profile, CancellationToken ct);

        /// <summary>
        /// Calls the certificate directory's generated completion proxy.
        /// </summary>
        ValueTask<(ByteString Certificate, ArrayOf<ByteString> Issuers)> FinishAsync(
            NodeId applicationId, NodeId requestId, CancellationToken ct);

        /// <summary>
        /// Finishes a new-key-pair request and returns private-key material only to the workflow caller.
        /// </summary>
        ValueTask<(ByteString Certificate, ByteString PrivateKeyPfx, ArrayOf<ByteString> Issuers)>
            FinishNewKeyPairAsync(NodeId applicationId, NodeId requestId, CancellationToken ct);

        /// <summary>
        /// Reads currently assigned certificates for one application and certificate group.
        /// </summary>
        ValueTask<(ArrayOf<NodeId> Types, ArrayOf<ByteString> Certificates)> GetCertificatesAsync(
            NodeId applicationId, NodeId certificateGroupId, CancellationToken ct);

        /// <summary>
        /// Resolves and downloads a bounded trust list using generated file proxies.
        /// </summary>
        ValueTask<TrustListDataType> ReadTrustListAsync(
            NodeId applicationId, NodeId certificateGroupId, CancellationToken ct);
    }

    /// <summary>
    /// Resolves and validates the channel identity for an existing signing request.
    /// </summary>
    internal interface IPullDcaBoundSigningConnection
    {
        /// <summary>
        /// Starts a request with the public certificate identifying its retained signing key.
        /// </summary>
        ValueTask<NodeId> StartWithEvidenceAsync(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            ByteString csr,
            ByteString signingKeyCertificate,
            CancellationToken ct);

        /// <summary>
        /// Returns the request binding only when the current confidential channel matches it.
        /// </summary>
        ValueTask<PullDcaSigningBinding> GetSigningBindingAsync(
            NodeId applicationId, NodeId requestId, CancellationToken ct);
    }

    /// <summary>
    /// Generated method proxy adapter over a factory-owned managed session.
    /// </summary>
    internal sealed class PullDcaConnection : IPullDcaConnection, IPullDcaBoundSigningConnection
    {
        /// <summary>
        /// Creates proxies for one explicitly selected endpoint.
        /// </summary>
        public PullDcaConnection(
            ManagedSession session,
            PullDcaRegistrar registrar,
            ITelemetryContext telemetry,
            long maximumTrustListSize,
            IPullDcaSigningJournal? journal = null,
            IPullDcaSigningAssignmentsObserver? assignmentsObserver = null)
            : this(
                session, registrar, telemetry, maximumTrustListSize,
                ct => ReloadAndReconnectAsync(session, ct), journal, assignmentsObserver)
        {
            if (!registrar.EndpointUrl.Equals(session.ConfiguredEndpoint.EndpointUrl))
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "The borrowed session is not connected to the selected registrar.");
            }
        }

        /// <summary>
        /// Creates a typed proxy adapter with an explicit reconnect operation for transport-level tests.
        /// </summary>
        internal PullDcaConnection(
            ISession session,
            PullDcaRegistrar registrar,
            ITelemetryContext telemetry,
            long maximumTrustListSize,
            Func<CancellationToken, ValueTask> reconnect,
            IPullDcaSigningJournal? journal = null,
            IPullDcaSigningAssignmentsObserver? assignmentsObserver = null,
            Func<CancellationToken, ValueTask<SessionRequestContext>>? confidentialContextFactory = null)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            if (registrar == null)
            {
                throw new ArgumentNullException(nameof(registrar));
            }
            m_selectedRegistrar = SnapshotRegistrar(registrar);
            m_registrar = new DeviceRegistrarTypeClient(session, m_selectedRegistrar.RegistrarId, telemetry);
            m_directory = new CertificateDirectoryTypeClient(
                session, m_selectedRegistrar.CertificateDirectoryId, telemetry);
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_logger = m_telemetry.CreateLogger<PullDcaConnection>();
            m_maximumTrustListSize = maximumTrustListSize;
            m_reconnect = reconnect ?? throw new ArgumentNullException(nameof(reconnect));
            m_journal = journal;
            m_assignmentsObserver = assignmentsObserver;
            m_originalNamespaces = session.NamespaceUris.ToArrayOf();
            m_confidentialContextFactory = confidentialContextFactory;
        }

        /// <summary>
        /// Creates a typed proxy adapter with an explicit confidential-context factory for transport tests.
        /// </summary>
        internal PullDcaConnection(
            ISession session,
            PullDcaRegistrar registrar,
            ITelemetryContext telemetry,
            long maximumTrustListSize,
            Func<CancellationToken, ValueTask> reconnect,
            Func<CancellationToken, ValueTask<SessionRequestContext>>? confidentialContextFactory)
            : this(
                session, registrar, telemetry, maximumTrustListSize, reconnect,
                null, null, confidentialContextFactory)
        {
        }

        /// <inheritdoc/>
        public async ValueTask<PullDcaIdentitySelection> ProvideIdentitiesAsync(
            PullDcaIdentityOffer offer,
            CancellationToken ct)
        {
            if (offer == null)
            {
                throw new ArgumentNullException(nameof(offer));
            }
            ArrayOf<ByteString> identities = CopyByteStrings(offer.Identities);
            ArrayOf<ByteString> issuers = CopyByteStrings(offer.Issuers);
            ArrayOf<string> tickets = offer.Tickets.IsNull
                ? default(ArrayOf<string>)
                : offer.Tickets.Span.ToArray();
            var (identity, ticket, applicationId, manager) = await m_registrar.ProvideIdentitiesAsync(
                identities, issuers, tickets, ct).ConfigureAwait(false);
            if (identity.IsEmpty || !identities.Contains(identity) || applicationId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
            m_selectedIdentity = CopyByteString(identity);
            m_identityApplicationId = SnapshotNodeId(applicationId);
            return new PullDcaIdentitySelection(
                CopyByteString(identity), ticket, SnapshotNodeId(applicationId), manager);
        }

        /// <inheritdoc/>
        public ValueTask ReloadAndReconnectAsync(CancellationToken ct)
        {
            return m_reconnect(ct);
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<ManagerDescription>> GetManagersAsync(CancellationToken ct)
        {
            return m_registrar.GetManagersAsync(ct);
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<ManagerDescription>> GetManagersOnInstalledChannelAsync(
            PullDcaSigningBinding? binding,
            ByteString installedCertificate,
            CancellationToken ct)
        {
            if (binding == null && m_binding == null && m_installedKeyPairBinding is { } keyPair)
            {
                binding = new PullDcaSigningBinding(
                    m_selectedRegistrar,
                    keyPair.ApplicationId,
                    keyPair.RequestId,
                    keyPair.Profile,
                    default,
                    default,
                    keyPair.ClientChannelCertificate,
                    keyPair.RegistrarChannelCertificate,
                    keyPair.RegistrarApplicationUri,
                    keyPair.NamespaceUris,
                    Guid.NewGuid());
            }
            binding = SnapshotBinding(binding ?? m_binding ??
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The original signing channel identity is not retained."));
            installedCertificate = CopyByteString(installedCertificate);
            if (installedCertificate.IsEmpty || binding.AttemptId == Guid.Empty ||
                binding.Registrar != m_selectedRegistrar)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The installed pull credential does not match the request.");
            }
            SessionRequestContext context = await CaptureConfidentialAsync(ct)
                .ConfigureAwait(false);
            await using var lifetime = context.ConfigureAwait(false);
            RequireNamespaces(
                context.Session.NamespaceUris, binding.NamespaceUris, binding.Registrar.RegistrarId,
                binding.Registrar.CertificateDirectoryId, binding.ApplicationId, binding.RequestId,
                binding.Profile.CertificateGroupId, binding.Profile.CertificateTypeId);
            RequireInstalledChannel(context, binding, installedCertificate);
            var registrar = new DeviceRegistrarTypeClient(
                context.Session, binding.Registrar.RegistrarId, m_telemetry);
            return await registrar.GetManagersAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> RegisterAsync(PullDcaManagedApplication application, CancellationToken ct)
        {
            return m_registrar.RegisterManagedApplicationAsync(application.Application, application.ProtocolUri, ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> StartAsync(
            NodeId applicationId, PullDcaCertificateProfile profile, ByteString csr, CancellationToken ct)
        {
            return StartWithEvidenceAsync(applicationId, profile, csr, default, ct);
        }

        /// <inheritdoc/>
        public async ValueTask<NodeId> StartNewKeyPairAsync(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            CancellationToken ct)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            applicationId = SnapshotNodeId(applicationId);
            profile = SnapshotProfile(profile);
            SessionRequestContext context = await CaptureConfidentialAsync(ct).ConfigureAwait(false);
            await using var lifetime = context.ConfigureAwait(false);
            RequireNamespaces(
                context.Session.NamespaceUris, m_originalNamespaces, m_selectedRegistrar.RegistrarId,
                m_selectedRegistrar.CertificateDirectoryId, applicationId,
                profile.CertificateGroupId, profile.CertificateTypeId);
            ITransportChannel channel = context.Session.TransportChannel;
            RequireSelectedIdentityChannel(applicationId, channel);
            ByteString clientCertificate = ByteString.From(channel.ClientChannelCertificate.AsSpan());
            var binding = new KeyPairRequestBinding(
                applicationId,
                NodeId.Null,
                profile,
                clientCertificate,
                ByteString.From(channel.ServerChannelCertificate.AsSpan()),
                channel.EndpointDescription.Server?.ApplicationUri ?? string.Empty,
                context.Session.NamespaceUris.ToArrayOf());
            RequireKeyPairChannel(context, binding);
            var directory = new CertificateDirectoryTypeClient(
                context.Session, m_selectedRegistrar.CertificateDirectoryId, m_telemetry);
            m_binding = null;
            NodeId requestId = await directory.StartNewKeyPairRequestAsync(
                applicationId,
                profile.CertificateGroupId,
                profile.CertificateTypeId,
                string.Empty,
                [],
                "PFX",
                string.Empty,
                ct).ConfigureAwait(false);
            if (requestId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError);
            }
            binding = binding with { RequestId = SnapshotNodeId(requestId) };
            var key = new KeyPairRequestKey(applicationId, SnapshotNodeId(requestId));
            if (!m_keyPairRequests.TryAdd(key, binding))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The registrar reused an active key-pair request identifier.");
            }
            return SnapshotNodeId(requestId);
        }

        /// <inheritdoc/>
        public async ValueTask<NodeId> StartWithEvidenceAsync(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            ByteString csr,
            ByteString signingKeyCertificate,
            CancellationToken ct)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            applicationId = SnapshotNodeId(applicationId);
            profile = SnapshotProfile(profile);
            csr = CopyByteString(csr);
            signingKeyCertificate = CopyByteString(signingKeyCertificate);
            if (m_journal != null && signingKeyCertificate.IsEmpty)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "The retained signing key certificate is required.");
            }
            SessionRequestContext context = await CaptureConfidentialAsync(ct)
                .ConfigureAwait(false);
            await using var lifetime = context.ConfigureAwait(false);
            RequireNamespaces(
                context.Session.NamespaceUris, m_originalNamespaces, m_selectedRegistrar.RegistrarId,
                m_selectedRegistrar.CertificateDirectoryId, applicationId,
                profile.CertificateGroupId, profile.CertificateTypeId);
            ITransportChannel channel = context.Session.TransportChannel;
            RequireSelectedIdentityChannel(applicationId, channel);
            var binding = new PullDcaSigningBinding(
                m_selectedRegistrar,
                applicationId,
                NodeId.Null,
                profile,
                ByteString.From(csr.Span),
                ByteString.From(signingKeyCertificate.Span),
                ByteString.From(channel.ClientChannelCertificate.AsSpan()),
                ByteString.From(channel.ServerChannelCertificate.AsSpan()),
                channel.EndpointDescription.Server?.ApplicationUri ?? string.Empty,
                context.Session.NamespaceUris.ToArrayOf(),
                Guid.NewGuid());
            RequireChannel(context, binding);
            var directory = new CertificateDirectoryTypeClient(
                context.Session, m_selectedRegistrar.CertificateDirectoryId, m_telemetry);
            if (m_journal != null && m_assignmentsObserver != null)
            {
                await RecordAssignmentsAsync(context, directory, binding, ct).ConfigureAwait(false);
            }

            bool dispatchAttempted = false;
            NodeId requestId;
            try
            {
                if (m_journal != null)
                {
                    dispatchAttempted = true;
                    await m_journal.RecordDispatchAsync(SnapshotBinding(binding), ct).ConfigureAwait(false);
                }
                RequireChannel(context, binding);
                requestId = await directory.StartSigningRequestAsync(
                    applicationId, profile.CertificateGroupId, profile.CertificateTypeId, csr, ct)
                    .ConfigureAwait(false);
                if (requestId.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadUnexpectedError);
                }
                binding = binding with { RequestId = SnapshotNodeId(requestId) };
                if (m_journal != null)
                {
                    await m_journal.RecordStartedAsync(SnapshotBinding(binding), ct).ConfigureAwait(false);
                }
                m_binding = binding;
            }
            catch (Exception error)
            {
                if (dispatchAttempted && m_journal is IPullDcaStartDispatchObserver observer)
                {
                    try
                    {
                        await observer.OnStartCompletedAsync(binding.AttemptId).ConfigureAwait(false);
                    }
                    catch (Exception completionError)
                    {
                        throw new AggregateException(
                            "The signing dispatch and completion notification both failed.", error, completionError);
                    }
                }
                throw;
            }

            if (dispatchAttempted && m_journal is IPullDcaStartDispatchObserver completionObserver)
            {
                await completionObserver.OnStartCompletedAsync(binding.AttemptId).ConfigureAwait(false);
            }
            return SnapshotNodeId(requestId);
        }

        /// <inheritdoc/>
        public async ValueTask<(ByteString Certificate, ByteString PrivateKeyPfx, ArrayOf<ByteString> Issuers)>
            FinishNewKeyPairAsync(
                NodeId applicationId,
                NodeId requestId,
                CancellationToken ct)
        {
            applicationId = SnapshotNodeId(applicationId);
            requestId = SnapshotNodeId(requestId);
            var key = new KeyPairRequestKey(applicationId, requestId);
            if (!m_keyPairRequests.TryGetValue(key, out KeyPairRequestBinding? binding))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The original key-pair request channel identity is not retained.");
            }
            SessionRequestContext context = await CaptureConfidentialAsync(ct).ConfigureAwait(false);
            await using var lifetime = context.ConfigureAwait(false);
            RequireNamespaces(
                context.Session.NamespaceUris, binding.NamespaceUris, m_selectedRegistrar.RegistrarId,
                m_selectedRegistrar.CertificateDirectoryId, applicationId,
                binding.Profile.CertificateGroupId, binding.Profile.CertificateTypeId);
            RequireKeyPairChannel(context, binding);
            var directory = new CertificateDirectoryTypeClient(
                context.Session, m_selectedRegistrar.CertificateDirectoryId, m_telemetry);
            try
            {
                var (certificate, privateKey, issuers) = await directory.FinishRequestAsync(
                    applicationId, requestId, ct).ConfigureAwait(false);
                if (!certificate.IsEmpty || !privateKey.IsEmpty)
                {
                    m_keyPairRequests.TryRemove(key, out _);
                    m_installedKeyPairBinding = binding with
                    {
                        ApplicationId = applicationId,
                        RequestId = requestId
                    };
                }
                return (certificate, privateKey, issuers);
            }
            catch (ServiceResultException error) when (error.StatusCode == StatusCodes.BadInvalidArgument)
            {
                m_keyPairRequests.TryRemove(key, out _);
                throw;
            }
        }

        /// <summary>
        /// Records the registrar's assignments on the captured channel before the signing dispatch.
        /// </summary>
        private async ValueTask RecordAssignmentsAsync(
            SessionRequestContext context,
            CertificateDirectoryTypeClient directory,
            PullDcaSigningBinding binding,
            CancellationToken ct)
        {
            (ArrayOf<NodeId> types, ArrayOf<ByteString> certificates) assignments;
            try
            {
                try
                {
                    assignments = await directory.GetCertificatesAsync(
                        binding.ApplicationId, binding.Profile.CertificateGroupId, ct).ConfigureAwait(false);
                }
                catch (ServiceResultException error) when (error.StatusCode == StatusCodes.BadInvalidArgument)
                {
                    assignments = await directory.GetCertificatesAsync(binding.ApplicationId, NodeId.Null, ct)
                        .ConfigureAwait(false);
                }
            }
            catch (ServiceResultException error) when (IsAssignmentBaselineUnavailable(error.StatusCode))
            {
                RequireChannel(context, binding);
                m_logger.AssignmentBaselineUnavailable(error.StatusCode.Code);
                return;
            }
            RequireChannel(context, binding);
            if (assignments.types.IsNull || assignments.certificates.IsNull ||
                assignments.types.Count != assignments.certificates.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError, "GetCertificates returned incomplete assignment arrays.");
            }
            for (int i = 0; i < assignments.types.Count; i++)
            {
                if (assignments.types[i].IsNull || assignments.certificates[i].IsEmpty)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError, "GetCertificates returned an incomplete assignment.");
                }
            }
            await m_assignmentsObserver!.RecordAssignmentsAsync(
                SnapshotBinding(binding), assignments.types, assignments.certificates, ct).ConfigureAwait(false);
            RequireChannel(context, binding);
        }

        /// <summary>
        /// Identifies responses that leave the optional pre-request assignment baseline unavailable.
        /// </summary>
        private static bool IsAssignmentBaselineUnavailable(StatusCode status)
        {
            return status == StatusCodes.BadMethodInvalid ||
                status == StatusCodes.BadNotImplemented ||
                status == StatusCodes.BadNotSupported;
        }

        /// <inheritdoc/>
        public async ValueTask<PullDcaSigningBinding> GetSigningBindingAsync(
            NodeId applicationId, NodeId requestId, CancellationToken ct)
        {
            applicationId = SnapshotNodeId(applicationId);
            requestId = SnapshotNodeId(requestId);
            SessionRequestContext context = await CaptureConfidentialAsync(ct)
                .ConfigureAwait(false);
            await using var lifetime = context.ConfigureAwait(false);
            PullDcaSigningBinding binding = await RequireSigningBindingAsync(
                context, applicationId, requestId, ct).ConfigureAwait(false);
            return SnapshotBinding(binding);
        }

        /// <inheritdoc/>
        public async ValueTask<(ByteString Certificate, ArrayOf<ByteString> Issuers)> FinishAsync(
            NodeId applicationId, NodeId requestId, CancellationToken ct)
        {
            applicationId = SnapshotNodeId(applicationId);
            requestId = SnapshotNodeId(requestId);
            SessionRequestContext context = await CaptureConfidentialAsync(ct)
                .ConfigureAwait(false);
            await using var lifetime = context.ConfigureAwait(false);
            PullDcaSigningBinding binding = await RequireSigningBindingAsync(context, applicationId, requestId, ct)
                .ConfigureAwait(false);
            var directory = new CertificateDirectoryTypeClient(
                context.Session, binding.Registrar.CertificateDirectoryId, m_telemetry);
            var (certificate, privateKey, issuers) = await directory.FinishRequestAsync(
                applicationId, requestId, ct).ConfigureAwait(false);
            if (!privateKey.IsEmpty)
            {
                ClearPrivateKeyMaterial(privateKey);
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "A signing request unexpectedly returned a private key.");
            }
            return (certificate, issuers);
        }

        /// <inheritdoc/>
        public async ValueTask<(ArrayOf<NodeId> Types, ArrayOf<ByteString> Certificates)> GetCertificatesAsync(
            NodeId applicationId, NodeId certificateGroupId, CancellationToken ct)
        {
            applicationId = SnapshotNodeId(applicationId);
            certificateGroupId = SnapshotNodeId(certificateGroupId);
            if (m_binding == null)
            {
                return await m_directory.GetCertificatesAsync(applicationId, certificateGroupId, ct)
                    .ConfigureAwait(false);
            }
            SessionRequestContext context = await SessionRequestContext.CaptureConfidentialAsync(m_session, ct)
                .ConfigureAwait(false);
            await using var lifetime = context.ConfigureAwait(false);
            PullDcaSigningBinding binding = await RequireSigningBindingAsync(
                context, applicationId, m_binding.RequestId, ct).ConfigureAwait(false);
            if (binding.Profile.CertificateGroupId != certificateGroupId)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
            var directory = new CertificateDirectoryTypeClient(
                context.Session, binding.Registrar.CertificateDirectoryId, m_telemetry);
            return await directory.GetCertificatesAsync(applicationId, certificateGroupId, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<TrustListDataType> ReadTrustListAsync(
            NodeId applicationId, NodeId certificateGroupId, CancellationToken ct)
        {
            applicationId = SnapshotNodeId(applicationId);
            certificateGroupId = SnapshotNodeId(certificateGroupId);
            if (m_binding == null)
            {
                return await ReadTrustListAsync(m_session, m_directory, applicationId, certificateGroupId, ct)
                    .ConfigureAwait(false);
            }
            SessionRequestContext context = await SessionRequestContext.CaptureConfidentialAsync(m_session, ct)
                .ConfigureAwait(false);
            await using var lifetime = context.ConfigureAwait(false);
            PullDcaSigningBinding binding = await RequireSigningBindingAsync(
                context, applicationId, m_binding.RequestId, ct).ConfigureAwait(false);
            if (binding.Profile.CertificateGroupId != certificateGroupId)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
            var directory = new CertificateDirectoryTypeClient(
                context.Session, binding.Registrar.CertificateDirectoryId, m_telemetry);
            return await ReadTrustListAsync(context.Session, directory, applicationId, certificateGroupId, ct)
                .ConfigureAwait(false);
        }

        private async ValueTask<TrustListDataType> ReadTrustListAsync(
            ISession session,
            CertificateDirectoryTypeClient directory,
            NodeId applicationId,
            NodeId certificateGroupId,
            CancellationToken ct)
        {
            NodeId trustListId = await directory.GetTrustListAsync(
                applicationId, certificateGroupId, ct).ConfigureAwait(false);
            if (trustListId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
            var file = new FileTypeClient(session, trustListId, m_telemetry);
            uint handle = await file.OpenAsync((byte)OpenFileMode.Read, ct).ConfigureAwait(false);
            return await TrustListFileTransferHelper.ReadAndCloseAsync(
                file, handle, session.MessageContext, m_maximumTrustListSize, 4096, ct).ConfigureAwait(false);
        }

        private async ValueTask<PullDcaSigningBinding> RequireSigningBindingAsync(
            SessionRequestContext context,
            NodeId applicationId,
            NodeId requestId,
            CancellationToken ct)
        {
            PullDcaSigningBinding? binding = m_binding;
            if (binding == null)
            {
                if (m_journal == null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The original signing channel identity is not retained.");
                }
                PullDcaSigningBinding resolved = await m_journal.ResolveAsync(
                    SnapshotRegistrar(m_selectedRegistrar),
                    SnapshotNodeId(applicationId),
                    SnapshotNodeId(requestId),
                    context.Session.NamespaceUris.ToArrayOf(),
                    ct)
                    .ConfigureAwait(false);
                binding = SnapshotBinding(resolved);
            }
            if (binding == null || binding.Registrar != m_selectedRegistrar ||
                binding.ApplicationId != applicationId || binding.RequestId != requestId ||
                binding.Profile == null || binding.SigningRequest.IsEmpty)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The retained signing request does not match this operation.");
            }
            RequireNamespaces(
                context.Session.NamespaceUris, binding.NamespaceUris, binding.Registrar.RegistrarId,
                binding.Registrar.CertificateDirectoryId, binding.ApplicationId, binding.RequestId,
                binding.Profile.CertificateGroupId, binding.Profile.CertificateTypeId);
            RequireChannel(context, binding);
            m_binding = binding;
            return binding;
        }

        /// <summary>
        /// Detaches channel and request evidence from its caller-owned buffers.
        /// </summary>
        internal static PullDcaSigningBinding SnapshotBinding(PullDcaSigningBinding binding)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }
            if (binding.Registrar == null)
            {
                throw new ArgumentException("The signing binding requires a registrar.", nameof(binding));
            }
            if (binding.Profile == null)
            {
                throw new ArgumentException("The signing binding requires a certificate profile.", nameof(binding));
            }
            return binding with
            {
                Registrar = SnapshotRegistrar(binding.Registrar),
                ApplicationId = SnapshotNodeId(binding.ApplicationId),
                RequestId = SnapshotNodeId(binding.RequestId),
                Profile = SnapshotProfile(binding.Profile),
                SigningRequest = CopyByteString(binding.SigningRequest),
                SigningKeyCertificate = CopyByteString(binding.SigningKeyCertificate),
                ClientChannelCertificate = CopyByteString(binding.ClientChannelCertificate),
                RegistrarChannelCertificate = CopyByteString(binding.RegistrarChannelCertificate),
                NamespaceUris = binding.NamespaceUris.IsNull
                    ? default(ArrayOf<string>)
                    : binding.NamespaceUris.Span.ToArray()
            };
        }

        /// <summary>
        /// Detaches opaque registrar and directory identifiers.
        /// </summary>
        internal static PullDcaRegistrar SnapshotRegistrar(PullDcaRegistrar registrar)
        {
            return registrar with
            {
                RegistrarId = SnapshotNodeId(registrar.RegistrarId),
                CertificateDirectoryId = SnapshotNodeId(registrar.CertificateDirectoryId)
            };
        }

        /// <summary>
        /// Detaches opaque certificate group and type identifiers.
        /// </summary>
        internal static PullDcaCertificateProfile SnapshotProfile(PullDcaCertificateProfile profile)
        {
            return profile with
            {
                CertificateGroupId = SnapshotNodeId(profile.CertificateGroupId),
                CertificateTypeId = SnapshotNodeId(profile.CertificateTypeId)
            };
        }

        /// <summary>
        /// Copies an opaque node identifier without changing its namespace or null semantics.
        /// </summary>
        internal static NodeId SnapshotNodeId(NodeId id)
        {
            return id.TryGetValue(out ByteString bytes)
                ? new NodeId(CopyByteString(bytes), id.NamespaceIndex)
                : id;
        }

        /// <summary>
        /// Clears array-backed private-key material returned by a certificate request.
        /// </summary>
        internal static void ClearPrivateKeyMaterial(ByteString privateKey)
        {
            if (MemoryMarshal.TryGetArray(privateKey.Memory, out ArraySegment<byte> bytes) && bytes.Array != null)
            {
                Array.Clear(bytes.Array, bytes.Offset, bytes.Count);
            }
        }

        /// <summary>
        /// Copies a byte string while retaining the distinction between null and empty.
        /// </summary>
        private static ByteString CopyByteString(ByteString value)
        {
            return value.IsNull ? default : ByteString.From(value.Span);
        }

        /// <summary>
        /// Detaches an array and its byte strings before passing it to a generated proxy.
        /// </summary>
        private static ArrayOf<ByteString> CopyByteStrings(ArrayOf<ByteString> values)
        {
            if (values.IsNull)
            {
                return default;
            }
            var result = new ByteString[values.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = CopyByteString(values[i]);
            }
            return result;
        }

        /// <summary>
        /// Requires the registrar-selected identity for the DCA's certificate request.
        /// </summary>
        private void RequireSelectedIdentityChannel(NodeId applicationId, ITransportChannel channel)
        {
            if (!m_selectedIdentity.IsEmpty && applicationId == m_identityApplicationId &&
                !m_selectedIdentity.Span.SequenceEqual(channel.ClientChannelCertificate))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The certificate request channel does not use the identity selected by the registrar.");
            }
        }

        private static void RequireChannel(SessionRequestContext context, PullDcaSigningBinding binding)
        {
            ITransportChannel channel = context.Session.TransportChannel;
            if (binding.ClientChannelCertificate.IsEmpty || binding.RegistrarChannelCertificate.IsEmpty ||
                !binding.ClientChannelCertificate.Span.SequenceEqual(channel.ClientChannelCertificate) ||
                !binding.RegistrarChannelCertificate.Span.SequenceEqual(channel.ServerChannelCertificate) ||
                !Uri.TryCreate(binding.RegistrarApplicationUri, UriKind.Absolute, out _) ||
                !string.Equals(
                    binding.RegistrarApplicationUri, channel.EndpointDescription.Server?.ApplicationUri,
                    StringComparison.Ordinal) ||
                !Uri.TryCreate(channel.EndpointDescription.EndpointUrl, UriKind.Absolute, out Uri? endpoint) ||
                !endpoint.Equals(binding.Registrar.EndpointUrl) || context.Invalidated.IsCompleted)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The signing request belongs to a different channel.");
            }
        }

        /// <summary>
        /// Requires the original secure registrar channel for a Part 12 key-pair request.
        /// </summary>
        private void RequireKeyPairChannel(SessionRequestContext context, KeyPairRequestBinding binding)
        {
            ITransportChannel channel = context.Session.TransportChannel;
            if (binding.ClientChannelCertificate.IsEmpty ||
                !binding.ClientChannelCertificate.Span.SequenceEqual(channel.ClientChannelCertificate) ||
                binding.RegistrarChannelCertificate.IsEmpty ||
                !binding.RegistrarChannelCertificate.Span.SequenceEqual(channel.ServerChannelCertificate) ||
                !Uri.TryCreate(binding.RegistrarApplicationUri, UriKind.Absolute, out _) ||
                !string.Equals(
                    binding.RegistrarApplicationUri,
                    channel.EndpointDescription.Server?.ApplicationUri,
                    StringComparison.Ordinal) ||
                !Uri.TryCreate(channel.EndpointDescription.EndpointUrl, UriKind.Absolute, out Uri? endpoint) ||
                !endpoint.Equals(m_selectedRegistrar.EndpointUrl) ||
                context.Invalidated.IsCompleted)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The key-pair request belongs to a different channel.");
            }
        }

        /// <summary>
        /// Captures the current confidential channel through the production or injected transport boundary.
        /// </summary>
        private ValueTask<SessionRequestContext> CaptureConfidentialAsync(CancellationToken cancellationToken)
        {
            return m_confidentialContextFactory == null
                ? SessionRequestContext.CaptureConfidentialAsync(m_session, cancellationToken)
                : m_confidentialContextFactory(cancellationToken);
        }

        /// <summary>
        /// Requires the installed client certificate and original registrar on the captured channel.
        /// </summary>
        private static void RequireInstalledChannel(
            SessionRequestContext context,
            PullDcaSigningBinding binding,
            ByteString installedCertificate)
        {
            ITransportChannel channel = context.Session.TransportChannel;
            using CertificateCollection originalRegistrar = DefaultCertificateFactory.Instance.ParseChainBlob(
                binding.RegistrarChannelCertificate.Memory);
            using CertificateCollection actualRegistrar = DefaultCertificateFactory.Instance.ParseChainBlob(
                channel.ServerChannelCertificate.AsMemory());
            using CertificateCollection actualClient = DefaultCertificateFactory.Instance.ParseChainBlob(
                channel.ClientChannelCertificate.AsMemory());
            if (originalRegistrar.Count == 0 || actualRegistrar.Count == 0 || actualClient.Count == 0 ||
                !originalRegistrar[0].RawData.AsSpan().SequenceEqual(actualRegistrar[0].RawData) ||
                !installedCertificate.Span.SequenceEqual(actualClient[0].RawData) ||
                !Uri.TryCreate(binding.RegistrarApplicationUri, UriKind.Absolute, out _) ||
                !string.Equals(
                    binding.RegistrarApplicationUri, channel.EndpointDescription.Server?.ApplicationUri,
                    StringComparison.Ordinal) ||
                !X509Utils.GetApplicationUrisFromCertificate(actualRegistrar[0])
                    .Contains(binding.RegistrarApplicationUri) ||
                !Uri.TryCreate(channel.EndpointDescription.EndpointUrl, UriKind.Absolute, out Uri? endpoint) ||
                !endpoint.Equals(binding.Registrar.EndpointUrl) || context.Invalidated.IsCompleted)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The installed pull channel differs from the request.");
            }
        }

        private static void RequireNamespaces(
            NamespaceTable current,
            ArrayOf<string> recorded,
            params NodeId[] identifiers)
        {
            foreach (NodeId id in identifiers)
            {
                int index = id.NamespaceIndex;
                if (id.IsNull || index >= recorded.Count || string.IsNullOrEmpty(recorded[index]) ||
                    !string.Equals(recorded[index], current.GetString((uint)index), StringComparison.Ordinal))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdUnknown, "A signing identifier does not match the session namespace.");
                }
            }
        }

        private static async ValueTask ReloadAndReconnectAsync(ManagedSession session, CancellationToken ct)
        {
            await session.ReloadInstanceCertificateAsync(ct).ConfigureAwait(false);
            await session.ReconnectAsync(null, null, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Borrowed session; its factory alone owns disposal.
        /// </summary>
        private readonly ISession m_session;

        /// <summary>The selected registrar and directory.</summary>
        private readonly PullDcaRegistrar m_selectedRegistrar;

        /// <summary>
        /// Generated registrar proxy.
        /// </summary>
        private readonly DeviceRegistrarTypeClient m_registrar;

        /// <summary>
        /// Generated certificate directory proxy.
        /// </summary>
        private readonly CertificateDirectoryTypeClient m_directory;

        /// <summary>
        /// Telemetry used by file proxies.
        /// </summary>
        private readonly ITelemetryContext m_telemetry;

        /// <summary>The logger for optional assignment-baseline failures.</summary>
        private readonly ILogger m_logger;

        /// <summary>
        /// Bound on every downloaded trust list.
        /// </summary>
        private readonly long m_maximumTrustListSize;

        /// <summary>
        /// Reloads and reconnects the host-owned session.
        /// </summary>
        private readonly Func<CancellationToken, ValueTask> m_reconnect;

        /// <summary>The optional host-owned durable request journal.</summary>
        private readonly IPullDcaSigningJournal? m_journal;

        /// <summary>The optional host-owned pre-dispatch assignment observer.</summary>
        private readonly IPullDcaSigningAssignmentsObserver? m_assignmentsObserver;

        /// <summary>The optional confidential session-context factory used by transport tests.</summary>
        private readonly Func<CancellationToken, ValueTask<SessionRequestContext>>? m_confidentialContextFactory;

        /// <summary>Client certificate identities retained for outstanding key-pair requests.</summary>
        private readonly ConcurrentDictionary<KeyPairRequestKey, KeyPairRequestBinding> m_keyPairRequests = new();

        /// <summary>Channel identity for the latest key-pair request completed on this connection.</summary>
        private KeyPairRequestBinding? m_installedKeyPairBinding;

        /// <summary>The namespace mapping in which the initial request identifiers were obtained.</summary>
        private readonly ArrayOf<string> m_originalNamespaces;

        /// <summary>The current request's retained channel identity.</summary>
        private PullDcaSigningBinding? m_binding;

        /// <summary>The certificate selected for the initial DCA identity offer.</summary>
        private ByteString m_selectedIdentity;

        /// <summary>The DCA application identifier associated with the selected identity.</summary>
        private NodeId m_identityApplicationId;

        private readonly record struct KeyPairRequestKey(NodeId ApplicationId, NodeId RequestId);

        private sealed record KeyPairRequestBinding(
            NodeId ApplicationId,
            NodeId RequestId,
            PullDcaCertificateProfile Profile,
            ByteString ClientChannelCertificate,
            ByteString RegistrarChannelCertificate,
            string RegistrarApplicationUri,
            ArrayOf<string> NamespaceUris);
    }

    /// <summary>
    /// Source-generated messages for the pull connection.
    /// </summary>
    internal static partial class PullDcaConnectionLog
    {
        /// <summary>
        /// Reports that lost-Finish reconciliation cannot use a pre-request assignment baseline.
        /// </summary>
        [LoggerMessage(
            EventId = GdsClientCommonEventIds.PullDcaConnection + 0,
            Level = LogLevel.Warning,
            Message = "The registrar did not return a pre-request certificate baseline (status {StatusCode}). " +
                "A lost FinishRequest response cannot be reconciled.")]
        public static partial void AssignmentBaselineUnavailable(this ILogger logger, uint statusCode);
    }
}
