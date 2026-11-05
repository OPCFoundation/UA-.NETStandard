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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Gds.Server.Diagnostics;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server
{
    public partial class ApplicationsNodeManager
    {
        /// <summary>
        /// Gets the actual host's namespace table for durable push request identifiers.
        /// </summary>
        internal NamespaceTable PushNamespaceUris => Server.NamespaceUris;

        /// <summary>
        /// Validates the existing local authority and application before a device creates a signing key.
        /// </summary>
        internal void ValidatePushCertificateSelection(
            NodeId applicationId,
            ExpandedNodeId certificateGroupId,
            NodeId certificateTypeId)
        {
            ResolvePushCertificateGroup(certificateGroupId, certificateTypeId);
            if (m_database.GetApplication(applicationId) == null)
            {
                throw new ServiceResultException(StatusCodes.BadNotFound);
            }
        }

        /// <summary>
        /// Binds renewed external approval to the original shared DCA record rather than a different application.
        /// </summary>
        internal void ValidateExternalDcaApplication(NodeId applicationId, ApplicationRecordDataType approved)
        {
            ApplicationRecordDataType application = m_database.GetApplication(applicationId) ??
                throw new ServiceResultException(StatusCodes.BadNotFound);
            if (application.ApplicationUri != approved.ApplicationUri ||
                application.ApplicationType != approved.ApplicationType)
            {
                throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            }
        }

        /// <summary>
        /// Validates operator-recovered original CSR evidence and, when supplied, its unaccepted queue entry.
        /// An accepted entry has erased its CSR and cannot establish this recovery binding.
        /// </summary>
        internal async ValueTask ValidatePushSigningRecoveryAsync(
            NodeId applicationId,
            ExpandedNodeId certificateGroupId,
            NodeId certificateTypeId,
            ByteString certificateRequest,
            NodeId requestId,
            CancellationToken ct)
        {
            ICertificateGroup group = ResolvePushCertificateGroup(certificateGroupId, certificateTypeId);
            ApplicationRecordDataType application = m_database.GetApplication(applicationId) ??
                throw new ServiceResultException(StatusCodes.BadNotFound);
            await group.VerifySigningRequestAsync(application, certificateTypeId, certificateRequest, ct)
                .ConfigureAwait(false);
            if (!requestId.IsNull)
            {
                CertificateRequestState state = m_request.ReadRequest(
                    applicationId, requestId, out string? requestedGroup, out string? requestedType,
                    out ByteString requestedCsr, out _, out _, out _, out _);
                if (state is not (CertificateRequestState.New or CertificateRequestState.Approved) ||
                    requestedGroup != group.Configuration.Id || requestedType != m_certTypeMap[certificateTypeId] ||
                    requestedCsr != certificateRequest)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed, "The recovered request does not retain the original CSR.");
                }
            }
        }

        /// <summary>
        /// Validates and starts a locally authorized push request on the same queue and authority as pull requests.
        /// This is a local registrar action, not an incoming call authenticated as the device.
        /// </summary>
        internal async ValueTask<NodeId> StartPushSigningRequestAsync(
            NodeId applicationId,
            ExpandedNodeId certificateGroupId,
            NodeId certificateTypeId,
            ByteString certificateRequest,
            CancellationToken ct)
        {
            ICertificateGroup group = ResolvePushCertificateGroup(certificateGroupId, certificateTypeId);
            ApplicationRecordDataType application = m_database.GetApplication(applicationId) ??
                throw new ServiceResultException(StatusCodes.BadNotFound);
            Exception? failure = null;
            try
            {
                await group.VerifySigningRequestAsync(application, certificateTypeId, certificateRequest, ct)
                    .ConfigureAwait(false);
                NodeId requestId = m_request.StartSigningRequest(
                    applicationId, group.Configuration.Id!, m_certTypeMap[certificateTypeId],
                    certificateRequest, "OnboardingRegistrarPush");
                return requestId;
            }
            catch (Exception error)
            {
                failure = error;
                throw;
            }
            finally
            {
                var method = new StartSigningRequestMethodState(null)
                {
                    NodeId = ExpandedNodeId.ToNodeId(MethodIds.Directory_StartSigningRequest, Server.NamespaceUris)
                };
                Server.ReportCertificateRequestedAuditEvent(
                    SystemContext, m_directoryId, method,
                    [applicationId, group.Id, certificateTypeId, certificateRequest],
                    group.Id, certificateTypeId, m_logger, failure);
            }
        }

        /// <summary>
        /// Completes an approved local push request using the existing issuance generator and immutable receipts.
        /// Accepted requests recover only through that receipt, never through an unrelated current assignment.
        /// </summary>
        internal async ValueTask<DeviceRegistrarPushEnrollment> CompletePushSigningRequestAsync(
            OnboardingIdentitySelection identity,
            NodeId applicationId,
            NodeId requestId,
            ExpandedNodeId certificateGroupId,
            NodeId certificateTypeId,
            ByteString certificateRequest,
            IDeviceRegistrarIdentityLifecycle candidates,
            IDeviceRegistrarCertificateLifecycle devices,
            CancellationToken ct)
        {
            ICertificateRequestIssuanceStore receipts = CertificateRequestIssuanceStore ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            await m_finishRequestGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await RequirePushApprovalAsync(identity, applicationId, candidates, devices, ct).ConfigureAwait(false);
                ICertificateGroup group = ResolvePushCertificateGroup(certificateGroupId, certificateTypeId);
                string groupName = group.Configuration.Id!;
                string typeName = m_certTypeMap[certificateTypeId];
                ApplicationRecordDataType application = m_database.GetApplication(applicationId) ??
                    throw new ServiceResultException(StatusCodes.BadNotFound);
                await group.VerifySigningRequestAsync(application, certificateTypeId, certificateRequest, ct)
                    .ConfigureAwait(false);
                CertificateRequestState state = m_request.ReadRequest(
                    applicationId, requestId, out string? requestedGroup, out string? requestedType,
                    out ByteString requestedCsr, out _, out _, out _, out _);
                if (state == CertificateRequestState.New && m_autoApprove)
                {
                    m_request.ApproveRequest(requestId, false);
                    state = m_request.ReadRequest(
                        applicationId, requestId, out requestedGroup, out requestedType,
                        out requestedCsr, out _, out _, out _, out _);
                }
                if (state != CertificateRequestState.Accepted)
                {
                    ServiceResult? approval = VerifyApprovedState(state);
                    if (approval != null && ServiceResult.IsBad(approval))
                    {
                        throw new ServiceResultException(approval);
                    }
                    if (requestedGroup != groupName || requestedType != typeName || requestedCsr != certificateRequest)
                    {
                        throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                    }
                }
                CertificateRequestIssuance receipt = await receipts.GetOrCreateAsync(applicationId, requestId,
                    async token =>
                    {
                        if (state == CertificateRequestState.Accepted)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadInvalidState, "Accepted push issuance has no retained receipt.");
                        }
                        FinishRequestMethodStateResult result = await GenerateCertificateIssuanceAsync(
                            application, applicationId, requestId, group, certificateTypeId,
                            default, default, token).ConfigureAwait(false);
                        if (ServiceResult.IsBad(result.ServiceResult))
                        {
                            throw new ServiceResultException(result.ServiceResult);
                        }
                        return new CertificateRequestIssuance(
                            groupName, typeName, result.Certificate, result.PrivateKey, result.IssuerCertificates);
                    }, ct).ConfigureAwait(false);
                if (receipt.CertificateGroupId != groupName || receipt.CertificateTypeId != typeName ||
                    !receipt.PrivateKey.IsEmpty)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }
                await RequirePushApprovalAsync(identity, applicationId, candidates, devices, ct).ConfigureAwait(false);
                await PublishCertificateIssuanceAsync(
                    applicationId, requestId, group, certificateTypeId, receipt.Certificate, ct).ConfigureAwait(false);
                TrustListDataType trustList = await ReadPushTrustListAsync(group, ct).ConfigureAwait(false);
                var method = new FinishRequestMethodState(null)
                {
                    NodeId = ExpandedNodeId.ToNodeId(MethodIds.Directory_FinishRequest, Server.NamespaceUris)
                };
                Server.ReportCertificateDeliveredAuditEvent(
                    SystemContext, m_directoryId, method, [applicationId, requestId], m_logger);
                return new DeviceRegistrarPushEnrollment(
                    identity, applicationId, requestId, groupName, typeName, trustList, certificateRequest);
            }
            finally
            {
                m_finishRequestGate.Release();
            }
        }

        /// <summary>
        /// Rechecks persisted software approval and DCA ownership before local issuance publication.
        /// A managed application is accepted only when the same device has a confirmed DCA credential,
        /// is not singleton-restricted and records that application as one of its managed relations.
        /// </summary>
        internal static async ValueTask RequirePushApprovalAsync(
            OnboardingIdentitySelection identity,
            NodeId applicationId,
            IDeviceRegistrarIdentityLifecycle candidates,
            IDeviceRegistrarCertificateLifecycle devices,
            CancellationToken ct)
        {
            DeviceRegistrarIdentityCandidate? candidate = await candidates.FindCandidateByCertificateAsync(
                identity.SelectedIdentity, ct).ConfigureAwait(false);
            DeviceRegistrarDevice? device = await devices.FindDeviceAsync(identity.ProductInstanceUri, ct)
                .ConfigureAwait(false);
            if (candidate == null || candidate.ProductInstanceUri != identity.ProductInstanceUri ||
                applicationId.IsNull || !candidate.CertificateIssuanceAllowed ||
                device == null || device.DcaApplicationId != candidate.ApplicationId ||
                (device.IsSingletonRestricted && !device.Applications.IsEmpty))
            {
                throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            }
            if (applicationId != device.DcaApplicationId &&
                (device.IsSingletonRestricted || device.DcaCertificate.IsEmpty ||
                    !device.PendingDcaCertificate.IsEmpty || !IsManagedApplication(device, applicationId)))
            {
                throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            }
        }

        /// <summary>
        /// Checks the persisted managed-application relations of one device.
        /// </summary>
        private static bool IsManagedApplication(DeviceRegistrarDevice device, NodeId applicationId)
        {
            foreach (DeviceRegistrarApplication application in device.Applications)
            {
                if (application.ApplicationId == applicationId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Publishes an exact issued credential to the shared application store, database and request queue.
        /// </summary>
        private async ValueTask PublishCertificateIssuanceAsync(
            NodeId applicationId,
            NodeId requestId,
            ICertificateGroup certificateGroup,
            NodeId certificateTypeId,
            ByteString encodedCertificate,
            CancellationToken ct)
        {
            using Certificate certificate = Certificate.FromRawData(encodedCertificate);
            var identifier = new CertificateStoreIdentifier(
                m_globalDiscoveryServerConfiguration.ApplicationCertificatesStorePath!);
            using ICertificateStore store = identifier.OpenStore(Server.Telemetry);
            using CertificateCollection existing = await store.FindByThumbprintAsync(certificate.Thumbprint, ct)
                .ConfigureAwait(false);
            if (existing.Count == 0)
            {
                await store.AddAsync(certificate, null, ct).ConfigureAwait(false);
            }
            m_database.SetApplicationCertificate(applicationId, m_certTypeMap[certificateTypeId], encodedCertificate);
            m_database.SetApplicationTrustLists(
                applicationId, m_certTypeMap[certificateTypeId], certificateGroup.Configuration.TrustedListPath);
            m_request.AcceptRequest(requestId, encodedCertificate);
        }

        /// <summary>
        /// Resolves the selected local authority without creating or replacing certificate groups.
        /// </summary>
        private ICertificateGroup ResolvePushCertificateGroup(ExpandedNodeId groupId, NodeId certificateTypeId)
        {
            NodeId localId = ExpandedNodeId.ToNodeId(groupId, Server.NamespaceUris);
            if (localId.IsNull || !m_certificateGroups.TryGetValue(localId, out ICertificateGroup? group) ||
                !group.CertificateTypes.Contains(certificateTypeId) || !m_certTypeMap.ContainsKey(certificateTypeId))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            return group;
        }

        /// <summary>
        /// Snapshots the selected authority's existing trust stores for delivery to the device.
        /// </summary>
        private async ValueTask<TrustListDataType> ReadPushTrustListAsync(
            ICertificateGroup group,
            CancellationToken ct)
        {
            using ICertificateStore trusted =
                new CertificateStoreIdentifier(group.Configuration.TrustedListPath!).OpenStore(Server.Telemetry);
            using ICertificateStore issuers =
                new CertificateStoreIdentifier(group.Configuration.IssuerListPath!).OpenStore(Server.Telemetry);
            using CertificateCollection trustedCertificates = await trusted.EnumerateAsync(ct).ConfigureAwait(false);
            using CertificateCollection issuerCertificates = await issuers.EnumerateAsync(ct).ConfigureAwait(false);
            X509CRLCollection trustedCrls = await trusted.EnumerateCRLsAsync(ct).ConfigureAwait(false);
            X509CRLCollection issuerCrls = await issuers.EnumerateCRLsAsync(ct).ConfigureAwait(false);
            return new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.All,
                TrustedCertificates = trustedCertificates.Select(value => ByteString.From(value.RawData.AsSpan()))
                    .ToArrayOf(),
                IssuerCertificates = issuerCertificates.Select(value => ByteString.From(value.RawData.AsSpan()))
                    .ToArrayOf(),
                TrustedCrls = trustedCrls.Select(value => ByteString.From(value.RawData.AsSpan())).ToArrayOf(),
                IssuerCrls = issuerCrls.Select(value => ByteString.From(value.RawData.AsSpan())).ToArrayOf()
            };
        }
    }
}
