// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Gds.Server
{
    public partial class ApplicationsNodeManager
    {
        /// <summary>
        /// Gets or sets the host-owned issuance receipt store used before publishing onboarding credentials.
        /// Configure before startup; durable storage is required for completion recovery across restarts.
        /// </summary>
        public ICertificateRequestIssuanceStore? CertificateRequestIssuanceStore { get; set; }

        /// <summary>
        /// Gets or sets the request store that durably binds certificate requests to their initiating client
        /// certificate. Direct construction uses the request store when it implements the bound-store contract.
        /// </summary>
        public IClientCertificateBoundCertificateRequestStore? ClientCertificateBoundRequestStore { get; set; }

        /// <summary>
        /// Gets the configured request store that enforces client-certificate binding.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadRequestNotAllowed"/> when the configured request store does not
        /// support client-certificate binding.
        /// </exception>
        private IClientCertificateBoundCertificateRequestStore RequireClientCertificateBoundRequestStore()
        {
            return ClientCertificateBoundRequestStore
                ?? throw new ServiceResultException(
                    StatusCodes.BadRequestNotAllowed,
                    "The certificate request store does not support client certificate binding.");
        }

        /// <summary>
        /// Gets the request store selected for all certificate-request lifecycle operations.
        /// </summary>
        private ICertificateRequest CertificateRequestStore =>
            ClientCertificateBoundRequestStore ?? m_request;

        /// <summary>
        /// Reads assigned certificates only after current onboarding ownership or the standard GDS grant is checked.
        /// This enables Part 12 signing recovery without allowing a denied device relation to use an admin fallback.
        /// </summary>
        internal async ValueTask<GetCertificatesMethodStateResult> OnGetCertificatesAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            NodeId certificateGroupId,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: false);
            await AuthorizeCertificateApplicationAsync(context, applicationId, cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<NodeId> types = default;
            ArrayOf<ByteString> certificates = default;
            ServiceResult result = GetCertificatesCore(applicationId, certificateGroupId, ref types, ref certificates);
            return new GetCertificatesMethodStateResult
            {
                CertificateTypeIds = types,
                Certificates = certificates,
                ServiceResult = result
            };
        }

        /// <summary>
        /// Resolves a TrustList only after applying the current application's onboarding or standard GDS permission.
        /// </summary>
        internal async ValueTask<GetTrustListMethodStateResult> OnGetTrustListAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            NodeId certificateGroupId,
            CancellationToken cancellationToken)
        {
            await AuthorizeCertificateApplicationAsync(context, applicationId, cancellationToken)
                .ConfigureAwait(false);
            NodeId trustListId = default;
            ServiceResult result = GetTrustListCore(applicationId, certificateGroupId, ref trustListId);
            return new GetTrustListMethodStateResult { TrustListId = trustListId, ServiceResult = result };
        }

        /// <summary>
        /// Admits only the read-related file methods to asynchronous application-specific checks.
        /// Open still applies its unchanged write callback when the requested mode writes.
        /// </summary>
        private void ConfigureOnboardingTrustListMethods(TrustListState node)
        {
            MethodState[] methods = [node.Open!, node.OpenWithMasks!, node.Read!, node.Close!];
            foreach (MethodState method in methods)
            {
                method.OnReadRolePermissions = OnReadOnboardingCertificateRolePermissions;
                method.OnReadUserRolePermissions = OnReadOnboardingCertificateUserRolePermissions;
            }
        }

        /// <summary>
        /// A channel may read only a TrustList assigned to one of its current authorized application relations.
        /// Unknown channels retain the original GDS policy. A known device never uses a global-role fallback.
        /// </summary>
        internal async ValueTask AuthorizeOnboardingTrustListReadAsync(
            ISystemContext context,
            CertificateStoreIdentifier trustedStore,
            CancellationToken cancellationToken)
        {
            if (DeviceRegistrarCertificateAuthorization is not { } onboarding)
            {
                HasTrustListAccess(context, trustedStore);
                return;
            }
            ArrayOf<DeviceRegistrarApplicationBinding> bindings = await onboarding.GetChannelApplicationsAsync(
                context, cancellationToken).ConfigureAwait(false);
            if (bindings.IsEmpty)
            {
                HasTrustListAccess(context, trustedStore);
                return;
            }
            NodeId grantedApplication = NodeId.Null;
            for (int i = 0; i < bindings.Count && grantedApplication.IsNull; i++)
            {
                DeviceRegistrarApplicationBinding binding = bindings[i];
                foreach (string certificateType in s_certificateTypeMap.Values)
                {
                    if (m_database.GetApplicationTrustLists(
                        binding.ApplicationId, certificateType, out string? storePath) &&
                        string.Equals(storePath, trustedStore.StorePath, StringComparison.Ordinal))
                    {
                        ApplicationRecordDataType? application = m_database.GetApplication(binding.ApplicationId);
                        if (application != null &&
                            await onboarding.AuthorizeApplicationAsync(
                                context, application, cancellationToken).ConfigureAwait(false))
                        {
                            grantedApplication = binding.ApplicationId;
                            break;
                        }
                    }
                }
            }

            // The device binding is the standard rule; the server policy makes the decision.
            ServiceResult standard = grantedApplication.IsNull
                ? new ServiceResult(StatusCodes.BadUserAccessDenied)
                : ServiceResult.Good;
            ServiceResult result = await ServerAuthorizationPolicy.Resolve(context).AuthorizeAsync(
                    context,
                    new ServerAuthorizationRequest(
                        ServerAuthorizationDecision.TrustListRead,
                        grantedApplication,
                        _ => standard),
                    cancellationToken)
                .ConfigureAwait(false);
            if (ServiceResult.IsBad(result))
            {
                throw new ServiceResultException(result);
            }
        }

        /// <summary>
        /// Allows entry to the asynchronous handlers without conferring a global GDS role.
        /// Each handler checks the authenticated channel, application owner, and current software policy.
        /// </summary>
        /// <typeparam name="TMethod">The generated certificate-directory method state.</typeparam>
        private TMethod OnboardingCertificateMethod<TMethod>(
            INodeBuilder<CertificateDirectoryState> directory,
            string browseName)
            where TMethod : MethodState
        {
            TMethod method = SelfAdministered<TMethod>(directory, browseName);
            if (DeviceRegistrarCertificateAuthorization != null)
            {
                method.OnReadRolePermissions = OnReadOnboardingCertificateRolePermissions;
                method.OnReadUserRolePermissions = OnReadOnboardingCertificateUserRolePermissions;
            }
            return method;
        }

        /// <summary>
        /// Preserves the existing GDS method permissions and admits calls to the argument-specific gate.
        /// An empty permission array retains inherited permissions rather than replacing them.
        /// </summary>
        private ServiceResult OnReadOnboardingCertificateRolePermissions(
            ISystemContext context,
            NodeState node,
            ref ArrayOf<RolePermissionType> value)
        {
            ServiceResult result = OnAddSelfAdminRolePermissions(context, node, ref value);
            if (!value.IsEmpty)
            {
                AddOnboardingCertificateCallPermissions(ref value);
            }
            return result;
        }

        /// <summary>
        /// Reports method admission only; it does not authorize any application or expose certificate data.
        /// Anonymous user tokens still require a matching authenticated application channel in the handler.
        /// </summary>
        private ServiceResult OnReadOnboardingCertificateUserRolePermissions(
            ISystemContext context,
            NodeState node,
            ref ArrayOf<RolePermissionType> value)
        {
            ServiceResult result = OnAddSelfAdminUserRolePermissions(context, node, ref value);
            if (context is ISessionSystemContext { UserIdentity: not null })
            {
                AddOnboardingCertificateCallPermissions(ref value);
            }
            return result;
        }

        /// <summary>
        /// Admits both user-token kinds to handlers that independently authorize application-certificate proof.
        /// No TrustList write, application administration, or unguarded method uses these permissions.
        /// </summary>
        private static void AddOnboardingCertificateCallPermissions(ref ArrayOf<RolePermissionType> value)
        {
            value = ArrayOf.Combine(value,
            [
                new RolePermissionType
                {
                    RoleId = Ua.ObjectIds.WellKnownRole_Anonymous,
                    Permissions = (uint)PermissionType.Call
                },
                new RolePermissionType
                {
                    RoleId = Ua.ObjectIds.WellKnownRole_AuthenticatedUser,
                    Permissions = (uint)PermissionType.Call
                }
            ]);
        }

        /// <summary>
        /// Rechecks persisted onboarding ownership without allowing a denied relation to use the GDS fallback.
        /// Applications outside onboarding retain the existing certificate-management authorization.
        /// </summary>
        private async ValueTask<bool> AuthorizeCertificateApplicationAsync(
            ISystemContext context,
            NodeId applicationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DeviceRegistrarCertificateAuthorization is { } onboarding)
            {
                ApplicationRecordDataType? application = m_database.GetApplication(applicationId);
                if (application != null &&
                    await onboarding.AuthorizeApplicationAsync(
                        context, application, cancellationToken).ConfigureAwait(false))
                {
                    // The device binding is the standard rule; the server policy makes the decision.
                    ServiceResult result = await ServerAuthorizationPolicy.Resolve(context).AuthorizeAsync(
                            context,
                            new ServerAuthorizationRequest(
                                ServerAuthorizationDecision.CertificateManagement,
                                applicationId,
                                static _ => ServiceResult.Good),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (ServiceResult.IsBad(result))
                    {
                        throw new ServiceResultException(result);
                    }
                    return true;
                }
            }
            AuthorizationHelper.HasAuthorization(
                context, ServerAuthorizationDecision.CertificateManagement, AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin, applicationId);
            return false;
        }

        /// <summary>Serializes receipt reads, issuance, publication and acceptance within this node manager.</summary>
        private readonly SemaphoreSlim m_finishRequestGate = new(1, 1);
    }
}
