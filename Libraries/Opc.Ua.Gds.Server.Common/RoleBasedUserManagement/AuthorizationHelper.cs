/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server
{
    internal static class AuthorizationHelper
    {
        internal static List<Role> AuthenticatedUser { get; } = [Role.AuthenticatedUser];
        internal static List<Role> DiscoveryAdmin { get; } = [GdsRole.DiscoveryAdmin];

        /// <summary>
        /// Roles/privileges accepted by <c>RegisterApplication</c>
        /// per OPC 10000-12 §6.5.6: <c>DiscoveryAdmin</c> Role or
        /// <c>ApplicationAdmin</c> Privilege.
        /// </summary>
        internal static List<Role> DiscoveryAdminOrAppAdmin { get; } =
        [GdsRole.DiscoveryAdmin, GdsRole.ApplicationAdmin];

        internal static List<Role> DiscoveryAdminOrSelfAdmin { get; } =
        [GdsRole.DiscoveryAdmin, GdsRole.ApplicationSelfAdmin];

        /// <summary>
        /// Roles/privileges accepted by <c>UpdateApplication</c> /
        /// <c>UnregisterApplication</c> per OPC 10000-12 §6.5.7 / §6.5.8.
        /// </summary>
        internal static List<Role> DiscoveryAdminOrSelfAdminOrAppAdmin { get; } =
        [GdsRole.DiscoveryAdmin, GdsRole.ApplicationSelfAdmin, GdsRole.ApplicationAdmin];

        internal static List<Role> AuthenticatedUserOrSelfAdmin { get; } =
        [Role.AuthenticatedUser, GdsRole.ApplicationSelfAdmin];

        internal static List<Role> CertificateAuthorityAdminOrSelfAdmin { get; } =
        [GdsRole.CertificateAuthorityAdmin, GdsRole.ApplicationSelfAdmin];

        /// <summary>
        /// Roles/privileges accepted by certificate-management methods that
        /// permit ApplicationSelfAdmin / ApplicationAdmin
        /// (OPC 10000-12 §7.6.4 - §7.6.10).
        /// </summary>
        internal static List<Role> CertificateAuthorityAdminOrSelfAdminOrAppAdmin { get; } =
        [
            GdsRole.CertificateAuthorityAdmin,
            GdsRole.ApplicationSelfAdmin,
            GdsRole.ApplicationAdmin
        ];

        internal static List<Role> CertificateAuthorityAdmin { get; }
            = [GdsRole.CertificateAuthorityAdmin];

        /// <summary>
        /// Checks if the current session (context) has one of the requested
        /// roles. If <see cref="GdsRole.ApplicationSelfAdmin"/> or
        /// <see cref="GdsRole.ApplicationAdmin"/> is included in
        /// <paramref name="roles"/>, the <paramref name="applicationId"/> is
        /// checked against the identity's bound application(s).
        /// </summary>
        /// <param name="context">The current <see cref="ISystemContext"/>.</param>
        /// <param name="roles">
        /// All allowed roles; may include
        /// <see cref="GdsRole.ApplicationSelfAdmin"/> and/or
        /// <see cref="GdsRole.ApplicationAdmin"/>.
        /// </param>
        /// <param name="applicationId">
        /// When <see cref="GdsRole.ApplicationSelfAdmin"/> or
        /// <see cref="GdsRole.ApplicationAdmin"/> is allowed this specifies
        /// the id of the Application entry to access.
        /// </param>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadUserAccessDenied"/> when the
        /// caller lacks the required roles/privileges.
        /// </exception>
        public static void HasAuthorization(
            ISystemContext context,
            IEnumerable<Role> roles,
            [Optional] NodeId applicationId)
        {
            if (context != null)
            {
                var allowedRoles = roles.ToHashSet();
                bool selfAdmin = allowedRoles.Remove(GdsRole.ApplicationSelfAdmin);
                bool appAdmin = allowedRoles.Remove(GdsRole.ApplicationAdmin);

                //if true access is allowed
                IUserIdentity? userIdentity = (context as ISessionSystemContext)?.UserIdentity;
                if (HasRole(userIdentity, allowedRoles, context.NamespaceUris))
                {
                    return;
                }

                if (selfAdmin)
                {
                    //if true access to own application is allowed
                    if (CheckSelfAdminPrivilege(userIdentity, applicationId))
                    {
                        return;
                    }
                }

                if (appAdmin && CheckApplicationAdminPrivilege(userIdentity, applicationId))
                {
                    return;
                }

                throw new ServiceResultException(
                    StatusCodes.BadUserAccessDenied,
                    $"At least one of the Roles {string.Join(", ", roles)} is required to call the method");
            }
        }

        /// <summary>
        /// Checks if the current session (context) is allowed to access the
        /// trust list (has the <c>CertificateAuthorityAdmin</c> /
        /// <c>SecurityAdmin</c> Role, the <c>ApplicationSelfAdmin</c>
        /// Privilege, or the <c>ApplicationAdmin</c> Privilege).
        /// </summary>
        /// <param name="context">the current <see cref="ISystemContext"/></param>
        /// <param name="trustedStore">Certificate store Identifier needed to check for Application Self Admin privilege</param>
        /// <param name="certTypeMap">all supported cert types, needed to check for Application Self Admin privilege </param>
        /// <param name="applicationsDatabase">all registered applications  <see cref="IApplicationsDatabase"/> , needed to check for Application Self Admin privilege </param>
        /// <exception cref="ServiceResultException"></exception>
        public static void HasTrustListAccess(
            ISystemContext context,
            CertificateStoreIdentifier trustedStore,
            Dictionary<NodeId, string> certTypeMap,
            IApplicationsDatabase applicationsDatabase)
        {
            var roles = new List<Role> { GdsRole.CertificateAuthorityAdmin, Role.SecurityAdmin };
            IUserIdentity? userIdentity = (context as ISessionSystemContext)?.UserIdentity;
            if (HasRole(userIdentity, roles, context.NamespaceUris))
            {
                return;
            }

            if (trustedStore != null &&
                certTypeMap != null &&
                applicationsDatabase != null &&
                CheckSelfAdminPrivilegeForTrustList(
                    userIdentity,
                    trustedStore,
                    certTypeMap,
                    applicationsDatabase))
            {
                return;
            }

            if (trustedStore != null &&
                certTypeMap != null &&
                applicationsDatabase != null &&
                CheckApplicationAdminPrivilegeForTrustList(
                    userIdentity,
                    trustedStore,
                    certTypeMap,
                    applicationsDatabase))
            {
                return;
            }

            throw new ServiceResultException(
                StatusCodes.BadUserAccessDenied,
                $"At least one of the Roles {string.Join(", ", roles)}, ApplicationSelfAdmin Privilege, or ApplicationAdmin Privilege is required to use the TrustList");
        }

        /// <summary>
        /// Checks if current session (context) is connected using an
        /// authenticated secure channel (<see cref="MessageSecurityMode.Sign"/>
        /// or <see cref="MessageSecurityMode.SignAndEncrypt"/>).
        /// </summary>
        /// <param name="context">the current <see cref="ISystemContext"/></param>
        /// <param name="requireEncryption">
        /// When <c>true</c> the channel must use
        /// <see cref="MessageSecurityMode.SignAndEncrypt"/>; otherwise
        /// <see cref="MessageSecurityMode.Sign"/> is also accepted.
        /// </param>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadSecurityModeInsufficient"/>
        /// per OPC 10000-12 method result tables.
        /// </exception>
        public static void HasAuthenticatedSecureChannel(
            ISystemContext context,
            bool requireEncryption = false)
        {
            if (context is not SystemContext { OperationContext: OperationContext operationContext })
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityModeInsufficient,
                    "Unable to verify secure channel requirements.");
            }

            MessageSecurityMode securityMode = operationContext
                .ChannelContext
                ?.EndpointDescription
                ?.SecurityMode
                ?? MessageSecurityMode.Invalid;

            bool ok = requireEncryption
                ? securityMode == MessageSecurityMode.SignAndEncrypt
                : securityMode == MessageSecurityMode.Sign ||
                  securityMode == MessageSecurityMode.SignAndEncrypt;

            if (!ok)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityModeInsufficient,
                    requireEncryption
                        ? "Method has to be called from an encrypted secure channel."
                        : "Method has to be called from an authenticated secure channel.");
            }
        }

        private static bool HasRole(IUserIdentity? userIdentity, IEnumerable<Role> roles, NamespaceTable namespaces)
        {
            if (userIdentity != null && userIdentity.TokenType != UserTokenType.Anonymous)
            {
                foreach (Role role in roles)
                {
                    if (!role.RoleId.IsNull &&
                        userIdentity.GrantedRoleIds.Contains(
                            ExpandedNodeId.ToNodeId(role.RoleId, namespaces)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool CheckSelfAdminPrivilege(
            IUserIdentity? userIdentity,
            NodeId applicationId)
        {
            if (applicationId.IsNull)
            {
                return false;
            }

            if (userIdentity is GdsRoleBasedIdentity identity)
            {
                //self Admin only has access to own application
                if (identity.ApplicationId == applicationId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks whether the identity carries the
        /// <see cref="GdsRole.ApplicationAdmin"/> privilege and (when
        /// <paramref name="applicationId"/> is supplied) administers it.
        /// </summary>
        /// <remarks>
        /// Per OPC 10000-12 §7.2 the ApplicationAdmin Privilege authorizes a
        /// configurable set of Applications. When no <paramref name="applicationId"/>
        /// is supplied (e.g. for <c>RegisterApplication</c>) the privilege
        /// alone is sufficient.
        /// </remarks>
        private static bool CheckApplicationAdminPrivilege(
            IUserIdentity? userIdentity,
            NodeId applicationId)
        {
            if (userIdentity is not GdsRoleBasedIdentity identity)
            {
                return false;
            }

            // The ApplicationAdmin privilege is asserted by the impersonation
            // logic via Role.ApplicationAdmin and a non-empty
            // AdministeredApplicationIds list.
            if (identity.AdministeredApplicationIds.Count == 0)
            {
                return false;
            }

            // No specific application: privilege alone is sufficient
            // (e.g. RegisterApplication before the appId exists).
            if (applicationId.IsNull)
            {
                return true;
            }

            for (int i = 0; i < identity.AdministeredApplicationIds.Count; i++)
            {
                if (identity.AdministeredApplicationIds[i] == applicationId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CheckSelfAdminPrivilegeForTrustList(
            IUserIdentity? userIdentity,
            CertificateStoreIdentifier trustedStore,
            Dictionary<NodeId, string> certTypeMap,
            IApplicationsDatabase applicationsDatabase)
        {
            if (userIdentity is GdsRoleBasedIdentity identity &&
                !identity.ApplicationId.IsNull)
            {
                foreach (string certType in certTypeMap.Values)
                {
                    applicationsDatabase.GetApplicationTrustLists(
                        identity.ApplicationId,
                        certType,
                        out string? trustListId);
                    if (trustedStore.StorePath == trustListId)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool CheckApplicationAdminPrivilegeForTrustList(
            IUserIdentity? userIdentity,
            CertificateStoreIdentifier trustedStore,
            Dictionary<NodeId, string> certTypeMap,
            IApplicationsDatabase applicationsDatabase)
        {
            if (userIdentity is not GdsRoleBasedIdentity identity ||
                identity.AdministeredApplicationIds.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < identity.AdministeredApplicationIds.Count; i++)
            {
                NodeId appId = identity.AdministeredApplicationIds[i];
                foreach (string certType in certTypeMap.Values)
                {
                    applicationsDatabase.GetApplicationTrustLists(
                        appId,
                        certType,
                        out string? trustListId);
                    if (trustedStore.StorePath == trustListId)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
