/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Runtime.InteropServices;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server

{
    internal static class AuthorizationHelper
    {
        internal static List<Role> AuthenticatedUser { get; } = new List<Role> { Role.AuthenticatedUser };
        internal static List<Role> DiscoveryAdmin { get; } = new List<Role> { GdsRole.DiscoveryAdmin };
        internal static List<Role> DiscoveryAdminOrSelfAdmin { get; } = new List<Role> { GdsRole.DiscoveryAdmin, GdsRole.ApplicationSelfAdmin };
        internal static List<Role> AuthenticatedUserOrSelfAdmin { get; } = new List<Role> { Role.AuthenticatedUser, GdsRole.ApplicationSelfAdmin };
        internal static List<Role> CertificateAuthorityAdminOrSelfAdmin { get; } = new List<Role> { GdsRole.CertificateAuthorityAdmin, GdsRole.ApplicationSelfAdmin };
        internal static List<Role> CertificateAuthorityAdmin { get; } = new List<Role> { GdsRole.CertificateAuthorityAdmin };

        /// <summary>
        /// Checks if the current session (context) has one of the requested roles. If <see cref="GdsRole.ApplicationSelfAdmin"/> is allowed the applicationId needs to be specified
        /// </summary>
        /// <param name="context">the current <see cref="ISystemContext"/></param>
        /// <param name="roles">all allowed roles, if wanted include <see cref="GdsRole.ApplicationSelfAdmin"/></param>
        /// <param name="applicationId">If <see cref="GdsRole.ApplicationSelfAdmin"/> is allowed specifies the id of the Application-Entry to access</param>
        public static void HasAuthorization(ISystemContext context, IEnumerable<Role> roles, [Optional] NodeId applicationId)
        {
            if (context != null)
            {
                List<Role> allowedRoles = roles.ToList();
                bool selfAdmin = allowedRoles.Remove(GdsRole.ApplicationSelfAdmin);

                //if true access is allowed
                if (HasRole(context.UserIdentity, allowedRoles))
                    return;

                if (selfAdmin)
                {
                    //if true access to own application is allowed
                    if (CheckSelfAdminPrivilege(context.UserIdentity, applicationId))
                        return;
                }
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied, $"At least one of the Roles {string.Join(", ", roles)} is required to call the method");
            }
        }
        /// <summary>
        /// Checks if the current session (context) is allowed to access the trust List (has roles CertificateAuthorityAdmin, SecurityAdmin or <see cref="GdsRole.ApplicationSelfAdmin"/>)
        /// </summary>
        /// <param name="context">the current <see cref="ISystemContext"/></param>
        /// <param name="trustedStorePath">path of the trustList, needed to check for Application Self Admin privilege</param>
        /// <param name="certTypeMap">all supported cert types, needed to check for Application Self Admin privilege </param>
        /// <param name="applicationsDatabase">all registered applications  <see cref="IApplicationsDatabase"/> , needed to check for Application Self Admin privilege </param>
        /// <exception cref="ServiceResultException"></exception>
        public static void HasTrustListAccess(ISystemContext context, string trustedStorePath, Dictionary<NodeId, string> certTypeMap, IApplicationsDatabase applicationsDatabase)
        {
            var roles = new List<Role> { GdsRole.CertificateAuthorityAdmin, Role.SecurityAdmin };
            if (HasRole(context.UserIdentity, roles))
                return;

            if (!string.IsNullOrEmpty(trustedStorePath) && certTypeMap != null && applicationsDatabase != null &&
                CheckSelfAdminPrivilege(context.UserIdentity, trustedStorePath, certTypeMap, applicationsDatabase))
                return;

            throw new ServiceResultException(StatusCodes.BadUserAccessDenied, $"At least one of the Roles {string.Join(", ", roles)} or ApplicationSelfAdminPrivilege is required to use the TrustList");
        }
        /// <summary>
        /// Checks if current session (context) is connected using a secure channel
        /// </summary>
        /// <param name="context">the current <see cref="ISystemContext"/></param>
        /// <exception cref="ServiceResultException"></exception>
        public static void HasAuthenticatedSecureChannel(ISystemContext context)
        {
            OperationContext operationContext = (context as SystemContext)?.OperationContext as OperationContext;
            if (operationContext != null)
            {
                if (operationContext.ChannelContext?.EndpointDescription?.SecurityMode != MessageSecurityMode.SignAndEncrypt)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "Method has to be called from an authenticated secure channel.");
                }
            }
        }
        private static bool HasRole(IUserIdentity userIdentity, IEnumerable<Role> roles)
        {
            if (userIdentity != null && userIdentity.TokenType != UserTokenType.Anonymous)
            {
                foreach (Role role in roles)
                {
                    if (!NodeId.IsNull(role.RoleId) && userIdentity.GrantedRoleIds.Contains(role.RoleId))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool CheckSelfAdminPrivilege(IUserIdentity userIdentity, NodeId applicationId)
        {
            if (applicationId is null || applicationId.IsNullNodeId)
                return false;

            GdsRoleBasedIdentity identity = userIdentity as GdsRoleBasedIdentity;
            if (identity != null)
            {
                //self Admin only has access to own application
                if (identity.ApplicationId == applicationId)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool CheckSelfAdminPrivilege(IUserIdentity userIdentity, string trustedStorePath, Dictionary<NodeId, string> certTypeMap, IApplicationsDatabase applicationsDatabase)
        {
            GdsRoleBasedIdentity identity = userIdentity as GdsRoleBasedIdentity;
            if (identity != null)
            {
                foreach (var certType in certTypeMap.Values)
                {
                    applicationsDatabase.GetApplicationTrustLists(identity.ApplicationId, certType, out var trustListId);
                    if (trustedStorePath == trustListId)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
