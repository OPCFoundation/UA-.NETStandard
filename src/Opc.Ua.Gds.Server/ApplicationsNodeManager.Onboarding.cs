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
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

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
            for (int i = 0; i < bindings.Count; i++)
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
                            return;
                        }
                    }
                }
            }
            throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
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
                    return true;
                }
            }
            AuthorizationHelper.HasAuthorization(
                context, AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin, applicationId);
            return false;
        }

        /// <summary>Serializes receipt reads, issuance, publication and acceptance within this node manager.</summary>
        private readonly SemaphoreSlim m_finishRequestGate = new(1, 1);
    }
}
