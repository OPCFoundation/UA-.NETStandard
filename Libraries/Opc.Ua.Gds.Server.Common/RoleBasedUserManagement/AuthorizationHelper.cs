using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Gds.Server.Database;

namespace Opc.Ua.Gds.Server

{
    internal static class AuthorizationHelper
    {
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
        /// <param name="trustedStorePath">path of the trustList, needed to check for Application Self Admin priviledge</param>
        /// <param name="certTypeMap">all supported cert types, needed to check for Application Self Admin priviledge </param>
        /// <param name="applicationsDatabase">all registered applications  <see cref="IApplicationsDatabase"/> , needed to check for Application Self Admin priviledge </param>
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

        private static bool HasRole(IUserIdentity userIdentity, IEnumerable<Role> roles)
        {
            RoleBasedIdentity identity = userIdentity as RoleBasedIdentity;

            if (identity != null)
            {
                foreach (Role role in roles)
                {
                    if ((identity.Roles.Contains(role)))
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
